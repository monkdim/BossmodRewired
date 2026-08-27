// Receives fight exports from the plugin and files them somewhere they can be read again.
//
// The plugin cannot post to the destination directly. Whatever the destination is, reaching it needs a
// credential, and a credential inside a plugin anybody can install is a credential anybody can read: .NET
// assemblies decompile in seconds and the string is right there. So the plugin posts here, where the
// credential lives on the server instead, and this decides what to do with it.
//
// The gain is not only secrecy. Because this sits in the middle it can be changed without shipping a new
// plugin: rotate a leaked token, throttle somebody flooding it, or move to different storage entirely, and
// every install that already exists follows along without noticing.
//
// Deploy with `wrangler deploy`, and set the secrets named below with `wrangler secret put`. Neither belongs
// in this file or in the repository around it.

const MAX_BYTES = 8 * 1024 * 1024;

export default {
  async fetch(request, env) {
    if (request.method !== "POST") {
      return text(405, "post an export here");
    }

    const url = new URL(request.url);
    if (url.pathname !== "/submit") {
      return text(404, "nothing here");
    }

    // A size check before reading, so a large body is refused rather than buffered. The header can lie, which
    // is why the real length is checked again after reading.
    const declared = Number(request.headers.get("content-length") || 0);
    if (declared > MAX_BYTES) {
      return text(413, "too large");
    }

    const body = await request.text();
    if (body.length > MAX_BYTES) {
      return text(413, "too large");
    }

    // Cheapest possible filter against anything that is not one of ours. It stops drive-by junk without
    // pretending to be authentication, which this endpoint cannot have: whatever the plugin knows in order to
    // post here is knowable by anybody holding the plugin.
    let payload;
    try {
      payload = JSON.parse(body);
    } catch {
      return text(400, "not json");
    }

    if (typeof payload?.schema !== "number" || !Array.isArray(payload?.samples)) {
      return text(400, "not an export");
    }

    const name = fileName(payload);

    // Named rather than lumped together, because the two ways to get this wrong (a secret never added, and a
    // secret added under a slightly different name) look identical from the outside and are fixed differently.
    // Saying which one is absent gives away nothing: these are the names, not the values.
    const missing = ["GITHUB_TOKEN", "GITHUB_REPO"].filter((key) => !env[key]);
    if (missing.length > 0) {
      return text(500, `relay has nowhere to file this, cannot see ${missing.join(" or ")}`);
    }

    try {
      await toGitHub(env, name, body);
    } catch (e) {
      // The plugin is told plainly, because an export silently going nowhere is worse than one that failed
      // loudly: the recording still exists on disk and can be sent again.
      return text(502, `could not file it: ${e.message}`);
    }

    return text(200, `filed as ${name}`);
  },
};

// Dated so a month's worth stays browsable, and suffixed so two people submitting the same fight in the same
// second do not overwrite each other.
function fileName(payload) {
  const boss = (payload.pulls?.[0]?.boss || "unknown").replace(/[^A-Za-z0-9_-]/g, "");
  const day = new Date().toISOString().slice(0, 10);
  const stamp = new Date().toISOString().slice(11, 19).replace(/:/g, "");
  const tag = Math.random().toString(36).slice(2, 8);
  return `${day}/${boss}-${stamp}-${tag}.json`;
}

// Committed through the contents API, which is one request and needs no tree building. The token should be
// fine-grained, scoped to this one repository, and allowed nothing beyond writing contents.
async function toGitHub(env, name, body) {
  const path = `data/${name}`;
  const res = await fetch(`https://api.github.com/repos/${env.GITHUB_REPO}/contents/${path}`, {
    method: "PUT",
    headers: {
      authorization: `Bearer ${env.GITHUB_TOKEN}`,
      accept: "application/vnd.github+json",
      "user-agent": "bossmod-rewired-relay",
      "content-type": "application/json",
    },
    body: JSON.stringify({
      message: `export: ${name}`,
      content: base64(body),
      branch: env.GITHUB_BRANCH || "main",
    }),
  });

  if (!res.ok) {
    throw new Error(`github ${res.status} ${(await res.text()).slice(0, 200)}`);
  }
}


// btoa only handles latin-1, and an export is UTF-8, so the bytes are widened first.
function base64(s) {
  const bytes = new TextEncoder().encode(s);
  let binary = "";
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  return btoa(binary);
}

function text(status, message) {
  return new Response(message, { status, headers: { "content-type": "text/plain" } });
}
