# Repo conventions

## Authorship

Every commit in this repository is authored and committed as:

```
monkdim <cdimaggio2@gmail.com>
```

Do not add `Co-Authored-By` trailers, tool or assistant attribution, generated-with
footers, or any other name to commits, tags, pull request titles and bodies, code
comments, or release notes. This applies no matter what tooling produced the change.

Set the identity before committing:

```
git config user.name  "monkdim"
git config user.email "cdimaggio2@gmail.com"
```

The only exception is the committer line on merge commits created by GitHub's own
merge button, which GitHub writes as `GitHub <noreply@github.com>` and neither we
nor it can change. Merge locally if that matters.

## This is a fork

Forked from [BossMod Reborn](https://github.com/FFXIV-CombatReborn/BossmodReborn),
itself a fork of [awgil/ffxiv_bossmod](https://github.com/awgil/ffxiv_bossmod). Both
are BSD 3-Clause and their copyright notices stay where they are.

Pull upstream changes with:

```
git remote add upstream https://github.com/FFXIV-CombatReborn/BossmodReborn.git
git fetch upstream && git merge upstream/main
```

Keep new work in new files and new directories wherever possible. Shared files are
where merge conflicts come from, so when one has to change, keep the change small and
additive.

## Building

The project targets `net10.0-windows10.0.26100.0` and links the Dalamud assemblies, so
it does not build on Linux or macOS. CI is the compiler:

- `build.yml` builds every pull request on Windows and uploads the plugin folder as an
  artifact. Download it and point Dev Plugin Locations at it to test a branch in game.
- `publish.yml` builds a release. Trigger it from the Actions tab with a four-part
  version, or push a matching tag. It generates and validates `repo.json`, which is what
  the Dalamud custom repository URL serves.

Never claim a change works without a green build behind it.

## Plugin identity

The assembly and plugin are named `BossModRewired` so Dalamud treats this as separate
from upstream and both can be installed at once.

`RootNamespace` is deliberately pinned to `BossModReborn`. Embedded obstacle maps under
`Pathfinding/ObstacleMaps` declare no `LogicalName`, so their resource names follow
`RootNamespace`; letting it track `AssemblyName` renames them and breaks pathfinding at
runtime with a perfectly green build. Shaders and fonts set `LogicalName` explicitly and
are not affected.

Because the internal name differs from upstream, this plugin has its own Dalamud config
directory. Settings and replays from a stock BMR install do not carry over.
