# Roadmap

What this fork is for, what it already does, and what is being considered. Nothing here is a promise.

## The point

BossMod Reborn tells you a mechanic is coming. This fork is trying to tell you where *you*, in your role,
should be standing for it, with a note when the mechanic needs more than a shape on a radar.

Getting there needs data from real fights, so a large part of the work is capture and analysis rather than
module content.

## Done

- **Records every duty automatically**, module or not, on by default.
- **Exports each duty as readable text** to `Downloads/Current Duties` with no interaction.
- **Exports a whole folder of recordings** in one pass from the replay window, skipping ones already
  written, for a backlog nobody exported as it happened.
- **Recordings with no module** fall back to a whole-recording dump, so uncovered content is not lost.
- **Positions**: where each player stood when every ability resolved, as an offset from whatever cast it,
  with distance, compass direction, and spread across resolutions.
- **Mechanic shapes** inferred from what happened: stack, spread, raidwide, light party, probable tank
  buster, hedged where the data cannot decide.
- **Pulls pooled per boss**, so a mechanic that fires once a pull still has enough samples across a
  progression session to say whether its position was chosen or incidental.
- **Where to stand, per role**, for every ability that hit somebody: distance and direction from the caster
  and from the arena centre, taken at the moment the cast begins, with a plain statement of how firmly each
  spot was held across casts.
- **Arena read from the module where one exists**, since every module carries the real centre and shape as
  a literal somebody typed in, and estimated from where the party stood where none does. Moduled fights
  print both, so how far short of the wall a party gets is a measured number rather than a guess.
- **Recordings split into fights** wherever the log goes quiet, so a dungeon reads as its bosses rather than
  one long encounter on a single clock.
- **Contributions**: damage, DPS, healing, damage taken and deaths per player, to judge whether a recording
  came from a run worth learning from.
- **Roles** assigned automatically on duty entry, skipped in extreme, savage and ultimate where people
  assign deliberately.
- **Mechanic timer bars** for enemy casts in any fight, plus upcoming states where a module defines them.
- **Unknown mechanic alerts** when a fight does something its module has never seen.
- **Refuses to load** alongside upstream BossMod Reborn, which crashes the game.
- **Structured export** alongside the text: every sample in world coordinates with the ability, the pull,
  the job and the slot it belongs to, for consumers that are not a person reading prose.
- **Names stripped at export**, not at recording. A player reads as a short salted hash of their account
  ID, consistent within one person's files and meaningless outside them, so roles still resolve and
  recordings stay useful.
- **Optional sharing** through a relay, asked outright during first-run setup with no default, so exports
  from several people can be pooled into one pile of evidence.
- **Positions learned for everybody**, not only for players somebody configured. An unassigned player is
  filed by job, and a slot only teaches a position when the people in it agree on one.
- **Per-role positional hints on the timer bars**, which was the end goal above.

## Being considered

### What good play looks like, per level and per fight

The recordings already hold every action anybody took, with timings, levels and jobs, and the damage that
followed. Exports currently throw all of it away, because the question so far has been where to stand.

There are two different questions worth asking of it, and conflating them would answer neither. One is
throughput: what a job actually puts out at a given level, measured rather than assumed. The other is
whether a rotation suited the fight it was used in, which is a different thing entirely, since the best
sequence on a dummy is regularly the wrong one during a mechanic.

Both need a lot more recordings than one person produces, which is what the sharing work above is for.

### Timelines for fights that lack them

Roughly 900 of the modules with a state machine declare a trivial phase and have no timings, so their timer
bars can only show casts already in progress. Recorded timings can supply the rest, but distinguishing a
fixed duration from a variable one needs several recordings of the same fight.

## Out of scope

Nothing here is being built by this fork. What it adds is analysis and information: capture, positions,
timings, hints and notes. Reading them and pressing the buttons is the player's job.

- **Automation of play.** Note that the fork inherits upstream's autorotation and AI modules, which do
  automate play. They come with the codebase, are off by default, and are not being developed here.
- **Anything that reads or writes another player's client.**
