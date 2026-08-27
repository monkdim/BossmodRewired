# cactbot timelines

These files are cactbot's, not ours. They are copied here unmodified from
https://github.com/OverlayPlugin/cactbot at commit `a13cb0172991068b5b39f80322a0d2feda1d876c`, under the Apache License 2.0
(`LICENSE.txt` in this directory).

The only change is the file names: cactbot nests them under `ui/raidboss/data`, and those
paths are flattened here, so `07-dt/raid/r10s.txt` is `07-dt_raid_r10s.txt`. Contents are
byte for byte what upstream has.

`ZoneTimelines.txt` is the exception: it is not a cactbot file. It pairs each zone with its
timeline, derived by joining cactbot's `resources/zone_id.ts` against the `zoneId` and
`timelineFile` in each trigger file under `ui/raidboss/data`, because cactbot keeps that
pairing in TypeScript rather than in the timelines themselves.

Do not hand-edit anything in this directory. To take a newer set, re-copy from upstream and
update the commit recorded here and in `THIRD-PARTY-NOTICES.md` at the repository root.
