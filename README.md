<img src="https://raw.githubusercontent.com/monkdim/BossmodRewired/main/Data/PluginIcon.png" width="128" alt="BossMod Rewired">

# BossMod Rewired

**Boss mechanics, with the positions spelled out.**

![Latest release downloads](https://img.shields.io/github/downloads/monkdim/BossmodRewired/latest/total.svg?style=for-the-badge&label=downloads)
![Licence](https://img.shields.io/github/license/monkdim/BossmodRewired.svg?label=licence&style=for-the-badge)

A Dalamud plugin for Final Fantasy XIV, forked from
[BossMod Reborn](https://github.com/FFXIV-CombatReborn/BossmodReborn), which is itself a fork of
[awgil's ffxiv_bossmod](https://github.com/awgil/ffxiv_bossmod). Everything either of them does is
included here.

> [!IMPORTANT]
> **Disable BossMod Reborn before enabling this.** Both plugins detour the same game functions, and
> two plugins on one address does not degrade gracefully: the game goes down with it. Rewired checks
> at startup and refuses to load with an explanation rather than letting that happen, so if it will
> not start, this is why. You do not need both installed.

## What is different about it

BossMod Reborn tells you a mechanic is coming. That is most of the problem solved, and it is why this
is a fork rather than something written from scratch.

The part it leaves you is the part this fork is about: **where you, in your role, should be standing
for it.** A radar showing an incoming cone is not the same as knowing that the melees go north-east at
about two thirds of the way out and the tanks do not move.

Nobody can hand-author that for every fight in the game, so this fork derives it from recordings
instead.

- **Every duty is recorded and exported automatically**, module or not, on by default. Content nobody
  has written a module for is exactly the content most worth capturing, so it falls back to dumping
  the whole recording rather than skipping it.
- **Positions are measured, not guessed.** For every ability that resolved: where each player stood
  when the cast began, as a distance and direction from the caster and from the arena centre, with a
  plain statement of how firmly that spot was held across casts. A position held tightly across nine
  casts and a position somebody happened to be in once read differently, and are reported differently.
- **Pulls pool per boss**, so a mechanic that fires once a pull still has enough observations across a
  progression session to say whether its position was chosen or incidental.
- **Learned positions appear on the mechanic timer bars**, which is the whole point: the answer arrives
  while the cast bar is still filling, rather than in a report you read afterwards.
- **Mechanic shapes are inferred** from who actually got hit: stack, spread, raidwide, light party,
  probable tank buster, hedged openly where the data cannot decide.
- **Arena taken from the module where one exists**, since every module carries the real centre and shape
  as a literal somebody typed in, and estimated from where the party stood where none does.
- **Recordings split into fights** wherever the log goes quiet or the party changes room, so a dungeon
  reads as its bosses rather than one long encounter on a single clock.

What it does not do is play for you. Reading the information and pressing the buttons is the player's
job. The fork inherits upstream's autorotation and AI modules, which do automate play; they come with
the codebase, are off by default, and are not being developed here.

## Installing

Add this to Dalamud's custom repository list:

```
https://github.com/monkdim/BossmodRewired/releases/latest/download/repo.json
```

1. Disable BossMod Reborn if you have it. `/xlplugins` → Installed Plugins → BossMod Reborn → Disable.
2. `/xlsettings` → Experimental → Custom Plugin Repositories → paste the link → tick the box beside it
   → Save and Close.
3. `/xlplugins` → search for **BossMod Rewired** → Install.

The link always resolves to the newest release, so it is the only one you will ever need. Updates
arrive through Dalamud on their own.

A first-run window walks through the radar, roles, and one question about sharing.

## What it records, and what leaves your machine

Recording is local and on by default. Exports are written to your own disk as readable text alongside a
data file, whether or not you share anything.

**Nothing is sent anywhere unless you say yes.** The question is asked outright during setup and has no
default. Turning it off later is one toggle in the settings, effective immediately, and you keep every
export either way.

If you do say yes, what goes is the position summary: which ability fired, when, which role and job was
where. Not chat, not gear, not your name, not anything from outside a duty.

Names never leave your machine. A player becomes a short hash of their account ID salted with a key
generated on your machine that is never sent anywhere, so the same person reads consistently within
your own files and means nothing to anybody else. Two people sharing the same fight produce different
handles for the same player unless they have deliberately swapped salts.

Recordings on your own disk keep real names, deliberately. That is your own data about your own
evening, and stripping identity at recording time breaks role resolution, which is most of what makes
the data useful. The sanitising happens at the moment a file is written for somebody else to read.

## How this is built

Recordings drive the features, not the other way round. Most changes here start with a real export
that turned out to be wrong or thin, and the fix is written against that file. A conclusion the data
cannot support is not printed at all: blank means not enough evidence rather than nothing happening,
and a thin reading is marked as thin rather than dressed up.

Everything lands through a pull request with the build green. There is no local compile step in the
loop; CI is the compiler.

[ROADMAP.md](ROADMAP.md) has what is done, what is being considered, and what is deliberately out of
scope. [CONTRIBUTING.md](CONTRIBUTING.md) has the house rules for code, inherited from upstream.

## Credit

This fork exists because two much larger bodies of work already existed.

- [awgil/ffxiv_bossmod](https://github.com/awgil/ffxiv_bossmod), the original plugin and the source of
  essentially all of the engine. The [wiki](https://github.com/awgil/ffxiv_bossmod/wiki) still applies
  to most of what you see.
- [FFXIV-CombatReborn/BossmodReborn](https://github.com/FFXIV-CombatReborn/BossmodReborn), the
  community fork this one is branched from, and the source of the great majority of the encounter
  modules. Their [Discord](https://discord.gg/p54TZMPnC9) is where module questions belong; it is not
  a support channel for this fork, so please do not take Rewired problems there.
- [cactbot](https://github.com/OverlayPlugin/cactbot), whose fight timelines are bundled here. They
  describe what each fight does and in what order, which is knowledge no amount of recording can
  derive.

Bugs in an encounter module very likely came from upstream and are worth reporting there so everyone
benefits. Anything about positions, exports, recording or sharing is this fork's, and belongs in
[issues here](https://github.com/monkdim/BossmodRewired/issues).

## Licence

BSD 3-Clause, inherited from upstream BossMod and unchanged. The copyright line in [LICENSE](LICENSE)
is Andrew Gilewsky's, because the overwhelming majority of this codebase is his work. Additions made
in this fork are offered under the same licence, and no separate claim is made over them.

Bundled cactbot timelines are Apache 2.0 and remain cactbot's work.
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) carries the full terms, the upstream commit the copies
were taken from, and a statement of what was changed.
