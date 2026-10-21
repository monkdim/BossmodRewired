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
    positions.Learn(123, "H2", spot);
    positions.Learn(123, "H2", spot with { Samples = 100, Pulls = 1 });
    File.WriteAllText(temp, positions.Build());
    var restored = LearnedPositions.Load(temp).For(123, "H2");
    Check(restored == spot, "schema 2 roundtrip preserves independent support");
    var fresh = new LearnedPositions();
    fresh.Learn(123, "H2", spot);
    LearnedPositions.Merge(temp, fresh);
    LearnedPositions.Merge(temp, fresh);
    Check(LearnedPositions.Load(temp).For(123, "H2") == spot, "re-export does not inflate pull count");
    var culture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        File.WriteAllText(temp, positions.Build());
        Check(LearnedPositions.Load(temp).For(123, "H2") == spot, "persistence remains culture independent");
    }
    finally { CultureInfo.CurrentCulture = culture; }
    File.WriteAllText(temp, "{\"schema\":3,\"abilities\":{}}");
    Check(LearnedPositions.Load(temp).Count == 0, "unknown schemas do not activate hints");
    File.WriteAllText(temp, "{\"schema\":2,\"abilities\":{\"1\":{\"H2\":[0.4,90,8,10,10,null,0,3]}}}");
    Check(LearnedPositions.Load(temp).Count == 0, "missing spread is not interpreted as perfect agreement");
    File.WriteAllText(temp, "{\"schema\":2,\"abilities\":{\"1\":{\"H2\":[0.4,90,8,10,10,0.2,0,3.5]}}}");
    Check(LearnedPositions.Load(temp).Count == 0, "fractional pull counts cannot qualify confidence");
}
finally { File.Delete(temp); }
Console.WriteLine($"{passed} checks passed.");
