# Timeline checks

Run `dotnet run --project Tests/Timelines/Timelines.csproj -c Release -- BossMod/Timelines/Cactbot`.

This harness links the production parser and tracker to minimal world-state stand-ins. It parses all 312 bundled timeline text files and checks event kinds, source matching, sync windows, hexadecimal ID patterns, forward labels, jumps, force-jump previews, delayed force-jump syncs, phase transitions outside combat, map effects and wipe resets.

The anonymized fixtures retain 52,587 relevant events from 12 saved M12S recordings and two Orbonne recordings. Replay checks cover 34 Curtain Call syncs, 78 phase-two opener syncs, 240 wipe resets, and bounded upcoming countdowns. These are recorded-event checks, not in-game visual tests or a claim that every fight or cactbot feature is supported. AddedCombatant and other unsupported event families remain outside this tracker.
