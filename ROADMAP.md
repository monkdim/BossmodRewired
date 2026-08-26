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

## Being considered

### Automatic upload of exports

Send exports somewhere central after each duty, so people capturing on someone else's behalf never have to
send files by hand.

A Discord webhook is the obvious mechanism: no server, no accounts, revocable, and roughly forty lines. The
URL belongs in config rather than in the repository, defaulting to empty, so the plugin does nothing unusual
for anyone who has not set one.

The hard part is not technical. **Recordings contain the names of every random player in the party**, and
they have not agreed to anything. Two ways out:

- The existing anonymize option, which scrambles names and content IDs but also breaks role resolution, and
  role resolution is most of what makes the data useful.
- A name-stripping export mode that keeps roles, jobs and positions but replaces people with their role slot.
  Not built. This is the better answer, since the names were never the useful part.

If this is built, it should be the webhook and the name-stripping mode together.

### Structured export for other tools

The text export is shaped for reading. Rotation analysis wants a table: action, timestamp, GCD or oGCD,
buffs active, target. A JSON export alongside the text would serve consumers that are not a person reading
prose, including the HealAssist side of this project.

The recording already contains all of it; only the projection is missing.

### Per-role positional hints

The end goal. Take the measured positions above, decide which are prescribed by the mechanic rather than
incidental, and render them as the arena hints a player actually follows. The spread figure already
separates a fixed spot from somebody who happened to be standing there.

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
