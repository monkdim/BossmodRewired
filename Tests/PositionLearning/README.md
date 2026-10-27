# Fixed-position learning checks

Run `dotnet run --project Tests/PositionLearning/PositionLearning.csproj -c Release` with .NET 10.
The console harness links the production eligibility and persistence sources and needs no game install.
Only configuration services unused by these tests are replaced with small stand-ins.

Fixtures retain position samples from the October 2026 investigation: Shantotto's moving donut,
Enuo's rotating cones, Dancing Green and M11S visual casts, M11S movement before a stack resolves,
Shadow Lord's multi-hit stack, and an M11S position that remains stable over two pulls.
Names, account handles, actor IDs, local file paths, and absolute timestamps are omitted. Pull numbers
are remapped to distinct test dates; the original cast, hit, and settled coordinates are retained.
`observedPlayerDamage` is whether this action had a recorded player damage effect, including on other
players. It is an eligibility requirement, not a claim that damage is always a failure.

These checks exercise the new policy on real observations and verify persistence migration. They do
not replace replay-parser integration, separate-session evaluation, or watching the live window in a duty.

Schema 3 retains absolute pull/event identities, arena-relative cast/hit/settled positions and
qualification flags. Separate recordings accumulate; re-importing the same observation does not add
support. All retained observations participate in rebuilding hints, including rejected/conflicting pulls.
Old schema 1/2 summaries are ignored because they cannot reconstruct evidence. Export format 7 makes
older recordings eligible for re-export. No installed user data is modified by these tests.

An optional argument accepts a JSON array of `{ "name": "recording", "rows": [Evidence...] }` to run
the production merger against local replay-derived observations. The local audit verifies these rows'
ability/timestamp correspondence and damage qualification against raw recordings before conversion.
This does not initialize the full in-game replay-analysis pipeline or test the live timer window.
