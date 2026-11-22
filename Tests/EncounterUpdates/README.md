# Encounter update checks

Run `dotnet run --project Tests/EncounterUpdates/EncounterUpdates.csproj -c Release`.

The harness links the production encounter components against a minimal framework stand-in. The full plugin build separately checks compatibility with the real framework. Fixtures contain mechanic events and relative geometry extracted from 36 saved recordings: 12 M12S, eight World of Darkness, seven Labyrinth, six Syrcus, two Orbonne and one Pharos Sirius (Hard). They exclude player names and account identifiers.

Checks cover 186 Ravenous Reach casts (34 Curtain Calls), recorded Holy Sword variants, 15 Ancient Flares, marker following and clearing, debuff-dependent instructions, and Progenitor's initial raidwide warnings. Six Amon casts include 32 sheltered player observations with no recorded damage. Cover checks reproduce the line-of-sight geometry; they do not render the game overlay. Thunder God's uncaptured rotate-left variant is checked separately against the upstream callout mapping.

Angra's 12-yalm effect radii and Progenitor's 50-yalm raidwide ranges were also checked against the installed game's Action sheet. Amon alone is promoted to Contributed. Progenitor's estimated arena and incomplete mechanic coverage remain WIP.
