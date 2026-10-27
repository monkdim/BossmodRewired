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
            observation.Settled.X, observation.Settled.Y, 20, 10, root.GetProperty("observedPlayerDamage").GetBoolean()));
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

var temp = Path.Combine(Path.GetTempPath(), $"position-learning-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(temp, "{\"schema\":1,\"abilities\":{\"49953\":{\"H2\":[0.952,231.071,19.041,10,10,0.458,0]}}}");
    Check(LearnedPositions.Load(temp).Count == 0, "legacy confidence is not grandfathered into live hints");
    var positions = new LearnedPositions();
    var spot = new LearnedPositions.Spot(.4f, 90, 8, 20, 20, .25f, false, 2);
    Check(spot.Worth && !spot.Confident, "two pulls remain visibly thin despite twenty hits");
    Check((spot with { Pulls = 3 }).Confident, "confidence requires three independent pulls");
    LearnedPositions.Evidence Row(int pull, float x = 1, bool qualified = true)
        => new(123, "H2", DateTime.UnixEpoch.AddMinutes(pull).Ticks,
            DateTime.UnixEpoch.AddMinutes(pull).AddSeconds(10).Ticks,
            x, 0, x, 0, x, 0, 20, 8, qualified);
    positions.Observe(Row(0));
    LearnedPositions.Merge(temp, positions);
    Check(LearnedPositions.Load(temp).Count == 0, "one recording retains one pull without activating a hint");
    var fresh = new LearnedPositions();
    fresh.Observe(Row(1, 1.1f));
    LearnedPositions.Merge(temp, fresh);
    var restored = LearnedPositions.Load(temp).For(123, "H2");
    Check(restored is { Pulls: 2 }, "separate one-pull recordings combine into a hint");
    LearnedPositions.Merge(temp, fresh);
    Check(LearnedPositions.Load(temp).For(123, "H2") == restored, "re-export does not inflate pull count");
    positions = LearnedPositions.Load(temp);
    var contradiction = new LearnedPositions();
    contradiction.Observe(Row(2, 20));
    LearnedPositions.Merge(temp, contradiction);
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "new conflicting recording removes an existing hint");
    LearnedPositions.Merge(temp, fresh);
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "re-export cannot erase contradictory evidence");
    File.WriteAllText(temp, positions.Build());
    var movingEvidence = new LearnedPositions();
    movingEvidence.Observe(Row(2) with { HitX = 9 });
    LearnedPositions.Merge(temp, movingEvidence);
    Check(LearnedPositions.Load(temp).Count == 0, "rejected movement also revokes a saved hint");
    File.WriteAllText(temp, positions.Build());
    var unqualified = new LearnedPositions();
    unqualified.Observe(Row(3, qualified: false));
    LearnedPositions.Merge(temp, unqualified);
    Check(LearnedPositions.Load(temp).Count == 0, "unqualified new evidence is retained as a contradiction");
    File.WriteAllText(temp, positions.Build());
    var overlapping = new LearnedPositions();
    overlapping.Observe(Row(1, 1.1f) with { PullTicks = Row(1).PullTicks + 1 });
    LearnedPositions.Merge(temp, overlapping);
    Check(LearnedPositions.Load(temp).For(123, "H2") is { Pulls: 2, Samples: 2 }, "overlapping recordings deduplicate rounded pull times");
    var sameMomentConflict = new LearnedPositions();
    sameMomentConflict.Observe(Row(1, 20));
    LearnedPositions.Merge(temp, sameMomentConflict);
    Check(LearnedPositions.Load(temp).For(123, "H2") == null, "conflicting positions at the same moment are not overwritten");
    File.WriteAllText(temp, positions.Build());
    var otherSlot = new LearnedPositions();
    otherSlot.Observe(Row(2, 20) with { Slot = "M2" });
    LearnedPositions.Merge(temp, otherSlot);
    Check(LearnedPositions.Load(temp).For(123, "H2") == restored, "unrelated slots retain their evidence");
    File.WriteAllText(temp, positions.Build().Replace("\"scale\":20", "\"missingScale\":20"));
    Check(LearnedPositions.Load(temp).Count == 0, "missing evidence coordinates or scale fail closed");
    var stale = new LearnedPositions();
    stale.Observe(Row(0));
    stale.Observe(Row(1));
    stale.Rebuild();
    Check(stale.Count == 0, "identical unchanging readings cannot establish position freshness");
    var arenaConflict = new LearnedPositions();
    arenaConflict.Observe(Row(0));
    arenaConflict.Observe(Row(1, 1.1f) with { Scale = 25 });
    arenaConflict.Rebuild();
    Check(arenaConflict.Count == 0, "incompatible arena scales withhold a hint");
    var culture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        File.WriteAllText(temp, positions.Build());
        Check(LearnedPositions.Load(temp).For(123, "H2") == restored, "persistence remains culture independent");
    }
    finally { CultureInfo.CurrentCulture = culture; }
    File.WriteAllText(temp, "{\"schema\":99,\"abilities\":{}}");
    Check(LearnedPositions.Load(temp).Count == 0, "unknown schemas do not activate hints");
    File.WriteAllText(temp, "{\"schema\":2,\"abilities\":{\"1\":{\"H2\":[0.4,90,8,10,10,null,0,3]}}}");
    Check(LearnedPositions.Load(temp).Count == 0, "missing spread is not interpreted as perfect agreement");
    File.WriteAllText(temp, "{\"schema\":2,\"abilities\":{\"1\":{\"H2\":[0.4,90,8,10,10,0.2,0,3.5]}}}");
    Check(LearnedPositions.Load(temp).Count == 0, "fractional pull counts cannot qualify confidence");
}
finally { File.Delete(temp); }
if (args.Length > 0)
{
    var logPath = Path.Combine(Path.GetTempPath(), $"learning-logs-{Guid.NewGuid():N}.json");
    try
    {
        using var data = JsonDocument.Parse(File.ReadAllText(args[0]));
        var revoked = 0;
        foreach (var recording in data.RootElement.EnumerateArray())
        {
            var beforeHints = LearnedPositions.Load(logPath).All().Select(h => (h.Ability, h.Slot)).ToHashSet();
            var fresh = new LearnedPositions();
            foreach (var element in recording.GetProperty("rows").EnumerateArray())
                fresh.Observe(element.Deserialize<LearnedPositions.Evidence>()!);
            LearnedPositions.Merge(logPath, fresh);
            var all = LearnedPositions.Load(logPath);
            var currentHints = all.All().Select(h => (h.Ability, h.Slot)).ToHashSet();
            var removed = beforeHints.Except(currentHints).Count();
            revoked += removed;
            Console.WriteLine($"LOG {recording.GetProperty("name").GetString()}: {all.All().Count()} active slot hints; {all.EvidenceRows().Count()} retained observations; {removed} revoked");
        }
        Check(revoked > 0, "later local recordings revoke contradictory hints");
        var saved = LearnedPositions.Load(logPath);
        var before = saved.Build();
        LearnedPositions.Merge(logPath, saved);
        Check(LearnedPositions.Load(logPath).Build() == before, "local log re-import is idempotent");
        var rows = saved.EvidenceRows().ToList();
        var reverse = new LearnedPositions();
        foreach (var row in rows.AsEnumerable().Reverse()) reverse.Observe(row);
        reverse.Rebuild();
        Check(saved.All().OrderBy(x => x.Ability).ThenBy(x => x.Slot).SequenceEqual(reverse.All().OrderBy(x => x.Ability).ThenBy(x => x.Slot)), "local log import order does not change hints");
        Check(saved.For(46116, "M2") == null, "local Eye of the Hurricane movement remains withheld");
        Check(saved.For(46098, "M2") == null && saved.For(46102, "M2") == null, "local visual and preparation actions remain withheld");
        foreach (var hint in saved.All())
            Console.WriteLine($"ACTIVE {hint.Ability} {hint.Slot} pulls={hint.Spot.Pulls} spread={hint.Spot.Spread:f3}");
    }
    finally { File.Delete(logPath); }
}
Console.WriteLine($"{passed} checks passed.");
