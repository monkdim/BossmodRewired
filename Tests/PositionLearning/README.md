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

Schema 2 learned files require independent pull counts. Schema 1 files are ignored because their sample
counts and spread cannot establish the new requirements. Export format 6 makes earlier exports eligible
for re-export. Separate recording summaries still compete rather than accumulate; re-exporting therefore
cannot manufacture independent support. Cross-session provenance and contextual movement advice remain
separate work.
