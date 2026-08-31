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

   To accept feedback as well, add the plugin's own repository to the token with **issues: read and write**,
   and set where the issues go:

   ```
   wrangler secret put GITHUB_ISSUE_REPO  # owner/name of the plugin repository
   ```

   Leave it unset and the feedback endpoint says so and files nothing. Exports are unaffected either way.

4. Wrangler prints the deployed URL. That is what goes into the plugin, and it is safe to hand out: it accepts
   writes and nothing else, and neither secret above is reachable through it.

## What it will accept

`POST /submit` takes an export: valid JSON, shaped like one (a numeric `schema` and an array of `samples`),
up to 8 MB.

`POST /feedback` takes what somebody typed into the plugin's feedback box, up to 8 KB, and opens an issue
with it. The message is fenced in the issue body so that nothing in it can pose as a heading or an
instruction once rendered, and the issue is labelled `from-plugin` and nothing else.

Anything else is refused before it reaches storage.

Those shape checks are filters, not authentication, and are not mistaken for it here. Whatever the plugin
knows in order to post is knowable by anybody holding the plugin, so both endpoints are writable by anybody
who cares to. The defence is that the relay can only ever append files to one repository and open issues on
another, the credential for both is not in it, and you can turn it off or replace it at any moment without
touching a single install.

That is also why the relay does not start anything. An issue it files sits there until a person with write
access reads it and applies a label. The work an agent does on this repository begins with that label, never
with an arriving report, because a report is text from a stranger and the alternative is letting a stranger
decide what gets written.
