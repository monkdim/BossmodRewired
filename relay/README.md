# Export relay

A small Cloudflare Worker that receives fight exports from the plugin and files them where they can be read
again. It exists for one reason: the plugin cannot hold the credential that reaching the destination needs.

A write token is the whole password. Put it in this repository and it is public; put it in the compiled plugin
instead and it is still public, because a .NET assembly decompiles in seconds and the string sits there in
plain text. Either way you cannot rotate it without shipping a release, and everyone who has not updated goes
quiet.

Moving it here fixes that, and buys more besides. Because the relay sits in the middle it can change without
the plugin changing: rotate a leaked token, throttle somebody flooding it, or move to entirely different
storage, and every install that already exists follows along without noticing.

## Where the exports go

A private GitHub repository, for a reason specific to how this data gets used. It is the only destination that
removes the handoff. Chat attachments cannot be read without an authenticated session, so every file becomes a
manual download and re-upload before anything can be done with it. A repository can be cloned whole, in one go,
and it arrives versioned and diffable for free.

## Setting it up

1. Create a private repository for the data. It only ever holds exports, so it can be small and boring.
2. Create a fine-grained personal access token scoped to that one repository, with **contents: read and
   write** and nothing else. It should be able to do exactly one thing.
3. Install [wrangler](https://developers.cloudflare.com/workers/wrangler/), then from this directory:

   ```
   wrangler deploy
   wrangler secret put GITHUB_TOKEN  # the token from step 2
   wrangler secret put GITHUB_REPO   # owner/name of the repository from step 1
   ```

   `GITHUB_BRANCH` may be set as a plain variable if the branch is not `main`.

4. Wrangler prints the deployed URL. That is what goes into the plugin, and it is safe to hand out: it accepts
   writes and nothing else, and neither secret above is reachable through it.

## What it will accept

Only a POST to `/submit`, only valid JSON, only if it looks like an export (a numeric `schema` and an array of
`samples`), and only up to 8 MB. Anything else is refused before it reaches storage.

That shape check is a filter, not authentication, and is not mistaken for it here. Whatever the plugin knows in
order to post is knowable by anybody holding the plugin, so this endpoint is writable by anybody who cares to.
The defence is that it can only ever append files to one repository, the credential for that is not in it, and
you can turn it off or replace it at any moment without touching a single install.
