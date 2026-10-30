using System.Globalization;
using System.Text.Json;
using BossMod;
using BossMod.ReplayAnalysis;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }
    Console.WriteLine($"PASS {name}");
    ++passed;
}

foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json"))
{
    using var fixture = JsonDocument.Parse(File.ReadAllText(path));
    var root = fixture.RootElement;
    var observations = new List<FixedPositionEvidence.Observation>();
    Vector2 point(JsonElement row, string key)
        => new(row.GetProperty(key)[0].GetSingle(), row.GetProperty(key)[1].GetSingle());
    foreach (var row in root.GetProperty("observations").EnumerateArray())
    {
        observations.Add(new(point(row, "cast"), point(row, "hit"), point(row, "settled"),
            DateTime.UnixEpoch.AddMinutes(row.GetProperty("pull").GetInt32())));
    }
    var result = FixedPositionEvidence.Evaluate(observations, true, root.GetProperty("observedPlayerDamage").GetBoolean());
    Check(result.Eligible == root.GetProperty("eligible").GetBoolean(), root.GetProperty("name").GetString()!);
    var ledger = new LearnedPositions();
    foreach (var observation in observations)
        ledger.Observe(new(123, "M2", observation.PullStart.Ticks, observation.PullStart.AddSeconds(10).Ticks,
            observation.Cast.X, observation.Cast.Y, observation.Hit.X, observation.Hit.Y,
            observation.Settled.X, observation.Settled.Y, 20, 10, root.GetProperty("observedPlayerDamage").GetBoolean(), 1));
    ledger.Rebuild();
    Check((ledger.For(123, "M2") != null) == root.GetProperty("eligible").GetBoolean(),
        "persisted evidence: " + root.GetProperty("name").GetString());
}

var twoPulls = new[]
{
    new FixedPositionEvidence.Observation(new(1, 1), new(1, 1), new(1, 1), DateTime.UnixEpoch),
    new FixedPositionEvidence.Observation(new(1.1f, 1), new(1.1f, 1), new(1.1f, 1), DateTime.UnixEpoch.AddMinutes(1))
};
Check(FixedPositionEvidence.Evaluate(twoPulls, true, true).Eligible, "stable repeated position remains eligible");
Check(!FixedPositionEvidence.Evaluate(twoPulls, false, true).Eligible, "missing pull times withhold advice");
Check(!FixedPositionEvidence.Evaluate(twoPulls, true, false).Eligible, "visual action cannot prove avoidance");
var onePull = twoPulls.Select(x => x with { PullStart = DateTime.UnixEpoch }).ToArray();
Check(!FixedPositionEvidence.Evaluate(onePull, true, true).Eligible, "repeated hits do not become independent pulls");
var rounded = twoPulls.Select(x => x with { PullStart = DateTime.UnixEpoch.AddTicks(1) }).Prepend(twoPulls[0]).ToArray();
Check(FixedPositionEvidence.Evaluate(rounded, true, true).Pulls == 1, "rounding cannot invent independent pulls");
var moving = twoPulls.Select(x => x with { Hit = x.Cast + new Vector2(4, 0) }).ToArray();
Check(!FixedPositionEvidence.Evaluate(moving, true, true).Eligible, "movement before resolution withholds fixed hint");
var pushed = twoPulls.Select(x => x with { Settled = x.Hit + new Vector2(4, 0) }).ToArray();
Check(!FixedPositionEvidence.Evaluate(pushed, true, true).Eligible, "displacement after resolution withholds fixed hint");
var nonfinite = twoPulls.Select(x => x with { Cast = new(float.NaN, 0) }).ToArray();
Check(!FixedPositionEvidence.Evaluate(nonfinite, true, true).Eligible, "nonfinite coordinates cannot qualify");
var conflict = twoPulls.Concat(twoPulls.Select(x => x with { Cast = new(20, 20), Hit = new(20, 20), Settled = new(20, 20) })).ToArray();
Check(!FixedPositionEvidence.Evaluate(conflict, true, true).Eligible, "incompatible occurrences cannot share a fixed hint");
var movingCaster = new[]
{
    new FixedPositionEvidence.Observation(new(0, 0), new(0, 0), new(0, 0), DateTime.UnixEpoch),
    new FixedPositionEvidence.Observation(new(20, 0), new(20, 0), new(20, 0), DateTime.UnixEpoch),
    new FixedPositionEvidence.Observation(new(0, 0), new(0, 0), new(0, 0), DateTime.UnixEpoch.AddMinutes(1)),
    new FixedPositionEvidence.Observation(new(20, 0), new(20, 0), new(20, 0), DateTime.UnixEpoch.AddMinutes(1))
};
Check(!FixedPositionEvidence.Evaluate(movingCaster, true, true).Eligible,
    "caster-relative agreement cannot conceal arena movement across repeated pulls");
var unbalanced = Enumerable.Repeat(twoPulls[0], 100).Append(twoPulls[1] with
{
    Cast = new(10, 10), Hit = new(10, 10), Settled = new(10, 10)
}).ToArray();
Check(!FixedPositionEvidence.Evaluate(unbalanced, true, true).Eligible,
    "numerous hits in one pull cannot conceal a conflicting pull");

var directory = Path.Combine(Path.GetTempPath(), $"context-learning-{Guid.NewGuid():N}");
Directory.CreateDirectory(directory);
var temp = Path.Combine(directory, "learned-positions.json");
LearnedPositions.Evidence Row(int pull, float x = 1, bool qualified = true, float at = 10, uint encounter = 1, string slot = "H2", int day = 0)
{
    var start = DateTime.UnixEpoch.AddDays(day).AddMinutes(pull);
    return new(123, slot, start.Ticks, start.AddSeconds(at).Ticks, x, 0, x, 0, x, 0, 20, 8, qualified, encounter);
}
LearnedPositions Model(params LearnedPositions.Evidence[] rows)
{
    var model = new LearnedPositions();
    foreach (var row in rows) model.Observe(row);
    model.Rebuild();
    return model;
}
void Import(params LearnedPositions.Evidence[] rows) => LearnedPositions.Merge(temp, Model(rows));
void Metadata(string name, uint oid, float center, float scale, float? sampleCenter = null)
{
    var rows = new[] { Row(0), Row(1, 1.1f) };
    var pulls = rows.Select((r, i) => new { index = i+1, oid, arena = new { center = new[] {center, center}, scale }, from = new DateTime(r.PullTicks, DateTimeKind.Utc).ToString("O") });
    var text = new StringBuilder("{\"schema\":4,\"pulls\":").Append(JsonSerializer.Serialize(pulls)).Append(",\n\"samples\": [\n");
    for (var i = 0; i < rows.Length; ++i)
    {
        var r = rows[i];
        text.Append(JsonSerializer.Serialize(new { ability = r.Ability, slot = r.Slot, pull = i+1,
            at = new DateTime(r.EventTicks, DateTimeKind.Utc).ToString("O"), cast = new[] {r.X+(sampleCenter??center), r.Z+(sampleCenter??center)} }));
        text.Append(i+1<rows.Length ? ",\n" : "\n");
    }
    text.Append("]}");
    File.WriteAllText(Path.Combine(directory, name+".json"), text.ToString());
    File.WriteAllText(Path.Combine(directory, name+".txt"), "Export format 7.");
}

try
{
    File.WriteAllText(temp, "{\"schema\":2,\"abilities\":{}}");
    Check(LearnedPositions.Load(temp).Count == 0, "unsupported summary files do not activate hints");
    Import(Row(0));
    Check(LearnedPositions.Load(temp).Count == 0, "one pull persists without inventing support");
    Import(Row(1, 1.1f));
    var supported = LearnedPositions.Load(temp);
    var spot = supported.For(123, "H2");
    Check(spot is { Pulls: 2, Confident: false }, "separate one-pull recordings combine into a thin hint");
    var before = supported.Build();
    Import(Row(1, 1.1f));
    Check(LearnedPositions.Load(temp).Build() == before, "duplicate exports are byte-for-byte idempotent");
    Import(Row(1, 1.1f) with { PullTicks = Row(1).PullTicks + 1 });
    Check(LearnedPositions.Load(temp).For(123, "H2") is { Pulls: 2 }, "rounded overlapping pulls cannot inflate support");
    Import(Row(2, 20));
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "a new positional contradiction immediately hides a hint");
    Import(Row(0), Row(1, 1.1f));
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "re-exporting old agreement cannot erase a newer contradiction");
    Import(Row(3, 20.1f));
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "two new agreeing pulls do not reactivate a contradicted hint");
    Import(Row(4, 20.2f));
    Check(LearnedPositions.Load(temp).For(123, "H2") is { Pulls: 3 }, "three fresh agreeing pulls establish the changed position");
    Check(LearnedPositions.Load(temp).PullEvidenceRows().Count() == 5, "historical conflicts remain stored after reactivation");
    Import(Row(5, 20.1f) with { HitX = 30 });
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "movement before resolution revokes a current hint");
    Import(Row(6, 20), Row(7, 20.1f));
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "movement failures require fresh independent agreement");
    Import(Row(8, 20.2f));
    Check(LearnedPositions.Load(temp).For(123, "H2") is { Pulls: 3 }, "a new stable run can recover after a movement failure");
    Check(LearnedPositions.Load(temp).PullEvidenceRows().Any(r => (r.Failures & 2) != 0), "compaction retains movement failure provenance");
    Import(Row(0, 5, day: 1), Row(1, 5.1f, day: 1));
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "a new session supersedes old advice before reaching sufficient support");
    Import(Row(2, 5.2f, day: 1));
    Check(LearnedPositions.Load(temp).For(123, "H2") is { Pulls: 3 }, "older session failures do not permanently poison new agreement");
    Import(Row(9, qualified: false));
    Check(LearnedPositions.Load(temp).For(123, "H2") is { Pulls: 3 }, "late-arriving historical evidence cannot displace the latest session");

    var repeated = Model(Enumerable.Range(0, 100).Select(i => Row(0, 1 + i * .001f, at: 10 + i * .01f)).ToArray());
    Check(repeated.Count == 0 && repeated.PullEvidenceRows().Count() == 1, "a multi-hit burst compacts to one pull without activating a hint");
    var occurrences = Model(Row(0), Row(1, 1.1f), Row(0, 20, at: 100), Row(1, 20.1f, at: 100));
    Check(occurrences.CandidateContexts.Count == 2, "different occurrences retain separate positions");
    Check(occurrences.For(123, "H2") == null, "an unscoped lookup cannot choose among occurrences");
    Check(occurrences.CandidateFor(123, "H2", 1, 10) is { } early && early.Fraction < .1f
        && occurrences.CandidateFor(123, "H2", 1, 100) is { } late && late.Fraction > .9f, "live time selects the matching occurrence");
    Check(occurrences.CandidateFor(123, "H2", 2, 10) == null, "the wrong encounter cannot receive a hint");
    Check(occurrences.CandidateFor(123, "H2", 1, double.NaN) == null && occurrences.CandidateFor(123, "H2", 1, -1) == null, "unknown or invalid fight timing cannot choose a hint");
    var ambiguous = Model(Row(0), Row(1, 1.1f), Row(0, 20, at: 25), Row(1, 20.1f, at: 25));
    Check(ambiguous.CandidateFor(123, "H2", 1, 17) == null, "ambiguous occurrence windows withhold advice");
    var isolated = Model(Row(0), Row(1, 1.1f), Row(0, 30, encounter: 2), Row(1, 30.1f, encounter: 2));
    Check(isolated.CandidateFor(123, "H2", 1, 10) is { } a && a.Fraction < .1f
        && isolated.CandidateFor(123, "H2", 2, 10) is { } b && b.Fraction > 1, "the same ability in another encounter does not contaminate the context");
    var badSlot = Model(Row(0), Row(1, 1.1f), Row(0, 30, slot: "M2"));
    Check(badSlot.For(123, "H2") != null, "unrelated role slots stay isolated");
    var unknown = Model(Row(0, encounter: 0), Row(1, 1.1f, encounter: 0));
    Check(unknown.Count == 0, "unmapped legacy evidence remains quarantined");
    var scales = Model(Row(0), Row(1, 1.1f) with { Scale = 25 });
    Check(scales.For(123, "H2") != null, "estimated arena sizes do not change world-coordinate agreement");
    Check(Model(Row(0), Row(1)).Count == 0, "identical stale readings cannot establish freshness");
    var wide = Model(Row(0), Row(0, 15, at: 10.1f), Row(1), Row(1, 15, at: 10.1f));
    Check(wide.Count == 0, "compact bounds preserve incompatible positions within one occurrence");
    var bridgeRows = new[] { Row(0, 1, at: 10), Row(0, 2, at: 24), Row(0, 1.5f, at: 17) };
    Check(Model(bridgeRows).Build() == Model(bridgeRows.Reverse().ToArray()).Build(), "bridging interval compaction is import-order independent");
    var ordered = LearnedPositions.Load(temp);
    var reversed = new LearnedPositions();
    foreach (var row in ordered.PullEvidenceRows().Reverse()) reversed.MergePull(row);
    reversed.Rebuild();
    Check(ordered.Build() == reversed.Build() && ordered.CandidateContexts.SequenceEqual(reversed.CandidateContexts), "compact history and active contexts survive reversed imports");
    var culture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        File.WriteAllText(temp, ordered.Build());
        Check(LearnedPositions.Load(temp).For(123, "H2") == ordered.For(123, "H2"), "compact persistence is culture independent");
    }
    finally { CultureInfo.CurrentCulture = culture; }
    File.WriteAllText(temp, "{\"schema\":4,\"pulls\":[[1,123,\"H2\",100]]}");
    Check(!LearnedPositions.Load(temp).PullEvidenceRows().Any(), "malformed compact rows fail closed");
    File.WriteAllText(temp, ordered.Build()[..^2]);
    Check(!LearnedPositions.Load(temp).PullEvidenceRows().Any(), "a truncated history cannot activate partial hints");
    File.WriteAllText(temp, "{\"schema\":99,\"pulls\":[]}");
    Check(LearnedPositions.Load(temp).Count == 0, "unknown schemas do not activate hints");

    var corroborated = Model(
        Row(0, 1, day: -1), Row(1, 1.1f, day: -1), Row(2, 1.2f, day: -1),
        Row(0, 1), Row(1, 1.1f), Row(2, 1.2f), Row(3, 1.1f), Row(4, 1));
    Check(corroborated.For(123, "H2", 1, 10, Row(5).PullTicks, 0, 0, 20) is { Pulls: 5 }, "live advice requires five current pulls corroborated by a separate session");
    Check(Model(Row(0), Row(1, 1.1f), Row(2, 1.2f), Row(3, 1.1f), Row(4)).For(123, "H2", 1, 10, Row(5).PullTicks, 0, 0, 20) == null,
        "one-session coincidences remain provisional despite five agreeing pulls");
    Check(corroborated.For(123, "H2", 1, 10, Row(0, day: 1).PullTicks, 0, 0, 20) == null, "a stale session cannot identify a new day's strategy");
    Check(corroborated.For(123, "H2", 2, 10, Row(5).PullTicks, 0, 0, 20) == null, "live lookup checks encounter identity");
    Check(corroborated.For(123, "H2", 1, 100, Row(5).PullTicks, 0, 0, 20) == null, "live lookup checks mechanic occurrence");
    Check(corroborated.For(123, "H2", 1, 10, Row(0).PullTicks, 0, 0, 20) == null, "live lookup cannot use future-pull evidence");
    corroborated.Observe(Row(5, 20)); corroborated.Rebuild();
    Check(corroborated.For(123, "H2", 1, 10, Row(6).PullTicks, 0, 0, 20) == null, "a fresh contradiction revokes certified live advice");
    var packedText = ordered.Build();
    Check(JsonDocument.Parse(packedText).RootElement.GetProperty("encoding").GetString() == "gzip", "compact evidence uses a single atomic compressed container");
    File.WriteAllText(temp, packedText.Replace("H4sI", "X4sI"));
    Check(LearnedPositions.Load(temp).Count == 0, "a corrupt compressed payload cannot expose partial history");

    var changedStrategy = Model(
        Row(0, 20, day: -1), Row(1, 20.1f, day: -1), Row(2, 20.2f, day: -1),
        Row(0), Row(1, 1.1f), Row(2, 1.2f), Row(3, 1.1f), Row(4));
    Check(changedStrategy.For(123, "H2", 1, 10, Row(5).PullTicks, 0, 0, 20) is { } newPlace && newPlace.Fraction < .1f,
        "a corroborated mechanic can learn a new strategy without averaging old positions");
    Check(changedStrategy.For(123, "H2", 1, 10, Row(5).PullTicks, 100, 100, 20) == null,
        "a world position outside the current arena cannot become relative advice");
    Check(changedStrategy.For(123, "H2", 1, 10, Row(5).PullTicks, float.NaN, 0, 20) == null,
        "an unknown live arena frame withholds a hint");
    var longArena = Model(Row(0,45,day:-1),Row(1,45.1f,day:-1),Row(2,45.2f,day:-1),
        Row(0,45),Row(1,45.1f),Row(2,45.2f),Row(3,45.1f),Row(4,45));
    Check(longArena.For(123,"H2",1,10,Row(5).PullTicks,0,0,10,51) != null,
        "rectangular arena reach is distinct from its nearest-wall display scale");
    var certifiedRows = new[] {Row(0, day:-1),Row(1,1.1f,day:-1),Row(2,1.2f,day:-1),Row(0),Row(1,1.1f),Row(2,1.2f),Row(3,1.1f),Row(4)};
    var overlapVeto = Model(certifiedRows.Concat(certifiedRows.Select(r => r with { EventTicks = r.PullTicks + TimeSpan.FromSeconds(25).Ticks, X = r.X+10, HitX = r.HitX+10, SettledX = r.SettledX+10 })).ToArray());
    overlapVeto.Observe(Row(5, qualified:false, at:25));overlapVeto.Rebuild();
    Check(overlapVeto.For(123, "H2", 1, 10, Row(6).PullTicks, 0, 0, 20) != null
        && overlapVeto.For(123, "H2", 1, 17, Row(6).PullTicks, 0, 0, 20) == null,
        "a revoked overlapping occurrence remains a veto instead of aliasing another hint");
    using (var encoded = JsonDocument.Parse(ordered.Build()))
    {
        var bytes = Convert.FromBase64String(encoded.RootElement.GetProperty("pulls").GetString()!);
        bytes[^8] ^= 255;
        File.WriteAllText(temp, JsonSerializer.Serialize(new {schema=4,encoding="gzip",coordinates="scoped-world",pulls=Convert.ToBase64String(bytes)}));
        Check(!LearnedPositions.Load(temp).PullEvidenceRows().Any(), "corrupt gzip integrity data cannot expose partially read history");
    }

    var legacyRows = new[] { Row(0), Row(1, 1.1f) }.Select(r => r with
    {
        PullTicks = new DateTime(r.PullTicks, DateTimeKind.Utc).ToLocalTime().Ticks,
        EventTicks = new DateTime(r.EventTicks, DateTimeKind.Utc).ToLocalTime().Ticks,
        Encounter = 0
    }).ToArray();
    Metadata("recording", 1, 0, 20);
    File.WriteAllText(Path.Combine(directory, "recording.txt"), "Export format 7.");
    File.WriteAllText(temp, JsonSerializer.Serialize(new { schema = 3, evidence = legacyRows }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    var migrated = LearnedPositions.LoadAndCompact(temp);
    Check(migrated.CandidateFor(123, "H2", 1, 10) is { Pulls: 2 }, "legacy UTC context migration recovers an unambiguous encounter");
    Check(JsonDocument.Parse(File.ReadAllText(temp)).RootElement.GetProperty("schema").GetInt32() == 4, "background migration persists compact schema 4");
    Metadata("recording", 1, 100, 20);
    File.WriteAllText(temp, JsonSerializer.Serialize(new { schema = 3, evidence = legacyRows }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    Check(LearnedPositions.Load(temp).CandidateFor(123, "H2", 1, 10) is { } translated && Math.Abs(translated.WorldX-101.05f)<.001f && translated.WorldZ==100,
        "verified legacy arena offsets migrate to absolute world positions");
    Metadata("recording", 1, 100, 20, sampleCenter: 0);
    Check(LearnedPositions.Load(temp).PullEvidenceRows().All(r => r.Encounter == 0), "an incorrect legacy origin cannot pass a size-only migration check");
    Metadata("recording", 1, 100, 25);
    Check(LearnedPositions.Load(temp).PullEvidenceRows().All(r => r.Encounter==0), "mismatched legacy arena frames stay quarantined");
    Metadata("other", 2, 0, 20);
    File.WriteAllText(Path.Combine(directory, "other.txt"), "Export format 7.");
    File.WriteAllText(temp, JsonSerializer.Serialize(new { schema = 3, evidence = legacyRows }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    Check(LearnedPositions.Load(temp).Count == 0, "ambiguous legacy encounter metadata stays quarantined");
    var missingExports = LearnedPositions.Load(temp, Path.Combine(directory, "deleted-exports"));
    Check(missingExports.PullEvidenceRows().Count() == 2 && missingExports.PullEvidenceRows().All(r => r.Encounter == 0),
        "deleted export metadata does not discard legacy evidence or guess its context");
}
finally { Directory.Delete(directory, true); }
foreach (var fixturePath in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ContextFixtures"), "*.json"))
{
    using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
    var evidence = fixture.RootElement.EnumerateArray().Select(e => e.Deserialize<LearnedPositions.Evidence>()!).ToList();
    var replayModel = new LearnedPositions();
    foreach (var row in evidence) replayModel.Observe(row);
    replayModel.Rebuild();
    if (Path.GetFileName(fixturePath).StartsWith("m11"))
    {
        var last = evidence.Where(e => e.Ability == 46111).Max(e => e.PullTicks);
        Check(replayModel.For(46111, "M2", 19180, 142, last+TimeSpan.TicksPerMinute, 100, 100, 20) is { Pulls: 5 }, "raw-derived M11S stable strategy recovers across sessions");
        Check(replayModel.For(46116, "M2", 19180, 150, last+TimeSpan.TicksPerMinute, 100, 100, 20) == null, "raw-derived M11S moving stack stays withheld");
        var clone = new LearnedPositions();
        foreach(var row in evidence.AsEnumerable().Reverse()) clone.Observe(row);
        clone.Rebuild();
        Check(clone.Build() == replayModel.Build(), "raw-derived compact history is invariant under reversed sample order");
    }
    else
    {
        Check(!replayModel.Contexts.Any(), "raw-derived M12S first-session prog remains provisional");
        Check(replayModel.PullEvidenceRows().Any(p => (p.Failures & 2) != 0), "raw-derived M12S movement failures survive compaction");
    }
}
Console.WriteLine($"{passed} checks passed.");
