# Banana Codex guidance checks

Run with .NET 10.

    dotnet run --project Tests/BananaCodex/BananaCodex.csproj -c Release

The executable links the production BananaCodexGuidance component. Minimal game-service substitutes allow assignment, movement, phase progression, and marker output to be checked without loading Dalamud. Fixtures contain anonymized Replication 2 event sequences, jump landing coordinates, and close/far boss positions from three November 17–18 recordings. They cover 34 mechanic windows, including one interrupted opening.

Checks cover all eight roles, the unassigned player's east spread and south stack, moving tether carriers, live stack targets, both close/far variants, pair separation, facing cues, replay tower progression and hazard vetoes. Captured off-position jumps must suppress the fixed opening marker. The full plugin build checks the real game-service signatures. In-game display remains a separate check.
