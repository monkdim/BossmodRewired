# Contextual position learning checks

Run `dotnet run --project Tests/PositionLearning/PositionLearning.csproj -c Release` with .NET 10.
This console harness links production learning, storage and migration code. Configuration services unused
by the tests have stand-ins; no game install or installed user data is required.

`Fixtures/` contains anonymized observations from Shantotto, Enuo, Dancing Green, Shadow Lord and M11S.
`ContextFixtures/` preserves cast/hit/settled world coordinates from October 22/29 M11S sessions and the
October 29 M12S session. Names, account IDs, actor IDs and paths are absent; dates are remapped to 2020.
The source exported ability/timestamp pairs were checked against raw replay events. These tests exercise
learning on replay-derived observations, not the full in-game parser/UI pipeline or mechanic outcomes.

The checks cover:
- independent pull support, idempotent overlapping imports, conservative bounds and failure flags;
- separate encounters and occurrences, ambiguous-window vetoes and latest-session selection;
- immediate revocation, provisional recovery, five-pull live confirmation and separate-session corroboration;
- stale/future clock rejection, live arena normalization and quarantining unknown legacy frames;
- streaming schema-3 migration, reference-sample origin verification, compressed persistence and corruption.

## Context policy

A candidate needs at least two independent pulls; a contradicted candidate needs three fresh agreeing pulls.
Candidates are diagnostic evidence, not live advice. Live display needs five agreeing current-session pulls
and at least three stable pulls in a separate historical session showing this mechanic/slot supports a fixed
position. Historical locations are not averaged into the new strategy. Sessions split after four hours without
an observation; live advice also requires a fresh current-pull clock, a matching active encounter/occurrence
and a point inside its live arena. Ambiguity or a new failure suppresses the affected hint.

Positions describe recent observations. They do not establish that a mechanic was resolved correctly, infer
arbitrary target assignments, or provide movement instructions. Revocation happens on export, not while a log
is still being recorded. The first M12S progression session remains provisional rather than becoming advice
from a few coincidental pulls. The M11S fixture verifies recovery of a stable newer strategy.

## Storage and migration

Schema 4 is one atomic JSON container: `schema: 4`, `encoding: "gzip"`, `coordinates: "scoped-world"`, and
a base64 `pulls` payload containing a gzip JSON array. Each record has 14 fields:
encounter OID, action ID, slot, UTC pull ticks, occurrence start/end seconds, min X/Z, max X/Z,
min/max historical scale, maximum caster distance and failure bits (1 = unqualified, 2 = movement).
World-space bounds enclose every input position; union and bitwise OR retain conflicts without raw events.
Record identities and bounds make re-exporting idempotent. Current arena estimates never move world points.

Existing schema-3 histories stream through a background migration. Matching local format-7 exports provide
pull identity and the original frame. A matching ability/slot/timestamp reference sample verifies that origin.
Missing, mismatched or ambiguous origins remain under encounter 0 and cannot activate live hints. Old schema
1/2 summaries cannot reconstruct evidence. Export format 8 records new evidence in world coordinates.

The timer starts loading in the background and uses an indexed context lookup. Both ordinary PR builds and
release publishing run this suite; release publishing also runs welcome version checks before packaging.
