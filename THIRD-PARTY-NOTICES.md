# Third-party notices

BossMod Rewired is distributed under the BSD 3-Clause License (see `LICENSE`). It also
includes material from the projects below, under their own terms.

## cactbot

Fight timelines under `BossMod/Timelines/Cactbot/` are taken from cactbot.

- Upstream: https://github.com/OverlayPlugin/cactbot
- Licence: Apache License, Version 2.0
- Full licence text: `BossMod/Timelines/Cactbot/LICENSE.txt`
- Taken from commit `a13cb0172991068b5b39f80322a0d2feda1d876c` (2026-08-24)

### Changes made

The timeline files themselves are unmodified. The only change is to how they are stored:
cactbot keeps them in nested directories under `ui/raidboss/data`, and here those paths are
flattened into the file name, so `07-dt/raid/r10s.txt` becomes `07-dt_raid_r10s.txt`. No
line of any timeline has been altered, added or removed.

cactbot ships no NOTICE file, so there is none to reproduce here.

### What they are used for

The timelines describe what a fight does and in what order, keyed on ability ID and caster
name. This plugin reads them to name mechanics and to know what is coming; it does not
reproduce cactbot's triggers, its callouts, or any of its user interface.
