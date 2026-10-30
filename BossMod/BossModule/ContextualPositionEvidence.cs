using System.IO;

namespace BossMod;

public sealed partial class LearnedPositions
{
    public sealed record Evidence(uint Ability, string Slot, long PullTicks, long EventTicks,
        float X, float Z, float HitX, float HitZ, float SettledX, float SettledZ,
        float Scale, float FromCaster, bool Qualified, uint Encounter = 0);

    // Unions of bounds and failure flags are idempotent and conservative. Re-importing an
    // overlapping recording cannot add support or erase a bad observation.
    public sealed record PullEvidence(uint Encounter, uint Ability, string Slot, long PullTicks,
        float From, float To, float MinX, float MinZ, float MaxX, float MaxZ,
        float MinScale, float MaxScale, float MaxCaster, int Failures);
    public sealed record ContextHint(uint Encounter, uint Ability, string Slot, float From, float To,
        long Session, long LastPull, Spot Spot, int SupportingSessions)
    {
        // A short streak is a candidate, not actionable advice. A different session must corroborate it.
        public bool Ready => Spot.Pulls >= 5 && SupportingSessions >= 2;
    }

    private const float OccurrenceGap = 8f;
    private const long SessionGap = 4 * TimeSpan.TicksPerHour;
    private readonly Dictionary<(uint Encounter, uint Ability, string Slot, long Second), List<PullEvidence>> _pulls = [];
    private readonly List<ContextHint> _contexts = [];
    private readonly Dictionary<(uint Encounter, uint Ability, string Slot), List<ContextHint>> _contextIndex = [];
    public IReadOnlyList<ContextHint> CandidateContexts => _contexts.Where(c => c.Spot.Worth).ToArray();
    public IReadOnlyList<ContextHint> Contexts => _contexts.Where(c => c.Ready).ToArray();
    public IEnumerable<PullEvidence> PullEvidenceRows() => _pulls.Values.SelectMany(rows => rows);

    public void Observe(Evidence row)
    {
        if (row != null && row.Slot == "")
            return;
        if (row == null || row.Ability == 0 || string.IsNullOrEmpty(row.Slot)
            || row.PullTicks <= 0 || row.EventTicks < row.PullTicks || row.EventTicks > DateTime.MaxValue.Ticks
            || !new[] { row.X, row.Z, row.HitX, row.HitZ, row.SettledX, row.SettledZ, row.Scale, row.FromCaster }.All(float.IsFinite)
            || row.Scale <= 0 || row.FromCaster < 0)
            throw new InvalidDataException("Invalid learned-position evidence");
        var into = (float)TimeSpan.FromTicks(row.EventTicks - row.PullTicks).TotalSeconds;
        var moved = Distance(row.X, row.Z, row.HitX, row.HitZ) > 3
            || Distance(row.HitX, row.HitZ, row.SettledX, row.SettledZ) > 3;
        MergePull(new(row.Encounter, row.Ability, row.Slot, row.PullTicks, into, into,
            row.X, row.Z, row.X, row.Z, row.Scale, row.Scale, row.FromCaster,
            (row.Qualified ? 0 : 1) | (moved ? 2 : 0)));
    }

    public void MergePull(PullEvidence row)
    {
        if (row == null || row.Ability == 0 || string.IsNullOrEmpty(row.Slot)
            || row.PullTicks <= 0 || row.PullTicks > DateTime.MaxValue.Ticks
            || !new[] { row.From, row.To, row.MinX, row.MinZ, row.MaxX, row.MaxZ, row.MinScale, row.MaxScale, row.MaxCaster }.All(float.IsFinite)
            || row.From < 0 || row.To < row.From || row.MinX > row.MaxX || row.MinZ > row.MaxZ
            || row.MinScale <= 0 || row.MaxScale < row.MinScale || row.MaxCaster < 0 || row.Failures is < 0 or > 3)
            throw new InvalidDataException("Invalid compact learned-position evidence");
        var second = (row.PullTicks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond;
        var found = new List<(long Second, PullEvidence Row)>();
        // A bridging event merges both sides, independent of import order.
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var delta = -1; delta <= 1; ++delta)
            {
                if (!_pulls.TryGetValue((row.Encounter, row.Ability, row.Slot, second + delta), out var rows))
                    continue;
                foreach (var prev in rows)
                {
                    if (found.Any(f => ReferenceEquals(f.Row, prev))
                        || Math.Abs(prev.PullTicks - row.PullTicks) > TimeSpan.TicksPerSecond
                        || prev.From > row.To + OccurrenceGap || row.From > prev.To + OccurrenceGap)
                        continue;
                    found.Add((second + delta, prev));
                    row = row with
                    {
                        PullTicks = Math.Min(row.PullTicks, prev.PullTicks),
                        From = Math.Min(row.From, prev.From), To = Math.Max(row.To, prev.To),
                        MinX = Math.Min(row.MinX, prev.MinX), MinZ = Math.Min(row.MinZ, prev.MinZ),
                        MaxX = Math.Max(row.MaxX, prev.MaxX), MaxZ = Math.Max(row.MaxZ, prev.MaxZ),
                        MinScale = Math.Min(row.MinScale, prev.MinScale), MaxScale = Math.Max(row.MaxScale, prev.MaxScale),
                        MaxCaster = Math.Max(row.MaxCaster, prev.MaxCaster), Failures = row.Failures | prev.Failures
                    };
                    changed = true;
                }
            }
        }
        foreach (var (bucket, prev) in found)
        {
            var key = (row.Encounter, row.Ability, row.Slot, bucket);
            var rows = _pulls[key];
            rows.Remove(prev);
            if (rows.Count == 0) _pulls.Remove(key);
        }
        var canonical = (row.PullTicks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond;
        _pulls.GetOrAdd((row.Encounter, row.Ability, row.Slot, canonical)).Add(row);
    }

    public void Rebuild()
    {
        _contexts.Clear();
        _contextIndex.Clear();
        foreach (var group in PullEvidenceRows().GroupBy(r => (r.Encounter, r.Ability, r.Slot)))
        {
            if (group.Key.Encounter == 0)
                continue; // Ambiguous legacy encounter mappings remain quarantined.
            var ordered = group.OrderBy(r => r.From).ThenBy(r => r.To).ToList();
            var occurrences = new List<List<PullEvidence>>();
            var end = float.NegativeInfinity;
            foreach (var row in ordered)
            {
                if (row.From > end + OccurrenceGap)
                    occurrences.Add([]);
                occurrences[^1].Add(row);
                end = Math.Max(end, row.To);
            }
            foreach (var occurrence in occurrences)
            {
                var timeline = occurrence.GroupBy(r => (r.PullTicks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond)
                    .Select(g => g.Aggregate(Union)).OrderBy(r => r.PullTicks).ToList();
                var sessionStart = 0;
                for (var i = 1; i < timeline.Count; ++i)
                    if (timeline[i].PullTicks - timeline[i - 1].PullTicks > SessionGap)
                        sessionStart = i;
                var session = timeline[sessionStart].PullTicks;
                var required = sessionStart == 0 ? 2 : 3;
                var run = new List<PullEvidence>();
                foreach (var row in timeline.Skip(sessionStart))
                {
                    if (row.Failures != 0 || row.MaxCaster > 60)
                    {
                        run.Clear();
                        required = 3;
                        continue;
                    }
                    if (run.Count > 0 && !Describe(run.Append(row).ToList(), out _))
                    {
                        run.Clear();
                        required = 3;
                    }
                    run.Add(row);
                }
                if (run.Count < required || !Describe(run, out var spot) || spot.Pulls < required || spot.Spread <= 0)
                {
                    // Keep the veto's time interval so another nearby occurrence cannot fill its place.
                    AddContext(new(group.Key.Encounter, group.Key.Ability, group.Key.Slot,
                        occurrence.Min(r => r.From), occurrence.Max(r => r.To), session,
                        timeline[^1].PullTicks, default, 0));
                    continue;
                }
                var supportingSessions = 1;
                foreach (var historical in Sessions(timeline.Take(sessionStart)))
                {
                    var prior = StableRun(historical);
                    if (prior.Count >= 3 && Describe(prior, out var priorSpot) && priorSpot.Pulls >= 3
                        && priorSpot.Spread > 0)
                        ++supportingSessions;
                }
                AddContext(new(group.Key.Encounter, group.Key.Ability, group.Key.Slot,
                    occurrence.Min(r => r.From), occurrence.Max(r => r.To), session,
                    timeline[^1].PullTicks, spot, supportingSessions));
            }
        }
    }

    private static bool Describe(List<PullEvidence> rows, out Spot spot)
    {
        spot = default;
        // Timing drift can make occurrence intervals overlap across pulls. Each pull still gets one vote.
        rows = rows.GroupBy(r => (r.PullTicks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond)
            .Select(g => g.Aggregate((a, b) => a with
            {
                MinX = Math.Min(a.MinX, b.MinX), MinZ = Math.Min(a.MinZ, b.MinZ),
                MaxX = Math.Max(a.MaxX, b.MaxX), MaxZ = Math.Max(a.MaxZ, b.MaxZ),
                MinScale = Math.Min(a.MinScale, b.MinScale), MaxScale = Math.Max(a.MaxScale, b.MaxScale),
                MaxCaster = Math.Max(a.MaxCaster, b.MaxCaster)
            })).ToList();
        if (rows.Count == 0)
            return false;
        var x = rows.Average(r => ((double)r.MinX + r.MaxX) / 2);
        var z = rows.Average(r => ((double)r.MinZ + r.MaxZ) / 2);
        // Corners conservatively enclose every original point, without retaining the raw event list.
        var spread = rows.Max(r => Math.Max(Math.Max(Distance(r.MinX, r.MinZ, x, z), Distance(r.MinX, r.MaxZ, x, z)),
            Math.Max(Distance(r.MaxX, r.MinZ, x, z), Distance(r.MaxX, r.MaxZ, x, z))));
        if (spread >= 3)
            return false;
        var pulls = rows.Select(r => r.PullTicks / TimeSpan.TicksPerSecond).Distinct().Count();
        spot = new((float)(Math.Sqrt(x * x + z * z) / rows[0].MinScale),
            (float)((Math.Atan2(x, -z) * 180 / Math.PI + 360) % 360), rows.Max(r => r.MaxCaster),
            pulls, pulls, (float)spread, false, pulls, (float)x, (float)z);
        return true;
    }

    internal Spot? CandidateFor(uint ability, string slot, uint encounter, double elapsed)
    {
        if (encounter == 0 || !double.IsFinite(elapsed) || elapsed < 0)
            return null;
        if (!_contextIndex.TryGetValue((encounter, ability, slot), out var contexts)) return null;
        var matches = contexts.Where(c => c.Ability == ability && c.Slot == slot && c.Encounter == encounter
            && elapsed >= c.From - OccurrenceGap && elapsed <= c.To + OccurrenceGap).Take(2).ToList();
        return matches.Count == 1 && matches[0].Spot.Worth ? matches[0].Spot : null;
    }

    public Spot? For(uint ability, string slot, uint encounter, double elapsed, long currentPullTicks,
        float centerX, float centerZ, float nearEdge, float maxReach = 0)
    {
        if (encounter == 0 || !double.IsFinite(elapsed) || elapsed < 0 || currentPullTicks <= 0
            || !float.IsFinite(centerX) || !float.IsFinite(centerZ) || !float.IsFinite(nearEdge) || nearEdge <= 0
            || !float.IsFinite(maxReach) || maxReach < 0)
            return null;
        // Include provisional overlapping contexts when checking ambiguity: never fall back to an old hint.
        if (!_contextIndex.TryGetValue((encounter, ability, slot), out var contexts)) return null;
        ContextHint? context = null;
        foreach (var candidate in contexts)
        {
            if (elapsed < candidate.From - OccurrenceGap || elapsed > candidate.To + OccurrenceGap) continue;
            if (context != null) return null;
            context = candidate;
        }
        if (context == null) return null;
        if (!context.Ready || currentPullTicks < context.LastPull - TimeSpan.TicksPerSecond
            || currentPullTicks - context.LastPull > SessionGap) return null;
        var x = context.Spot.WorldX - centerX;
        var z = context.Spot.WorldZ - centerZ;
        if (Math.Sqrt(x * x + z * z) > (maxReach > 0 ? maxReach : nearEdge * 1.5f)) return null;
        return context.Spot with { Fraction = MathF.Sqrt(x * x + z * z) / nearEdge,
            Bearing = (float)((Math.Atan2(x, -z) * 180 / Math.PI + 360) % 360) };
    }

    private static IEnumerable<List<PullEvidence>> Sessions(IEnumerable<PullEvidence> rows)
    {
        var session = new List<PullEvidence>();
        foreach (var row in rows)
        {
            if (session.Count > 0 && row.PullTicks - session[^1].PullTicks > SessionGap)
            {
                yield return session;
                session = [];
            }
            session.Add(row);
        }
        if (session.Count > 0) yield return session;
    }

    private void AddContext(ContextHint context)
    {
        _contexts.Add(context);
        _contextIndex.GetOrAdd((context.Encounter, context.Ability, context.Slot)).Add(context);
    }

    private static List<PullEvidence> StableRun(IEnumerable<PullEvidence> rows)
    {
        var run = new List<PullEvidence>();
        foreach (var row in rows)
        {
            if (row.Failures != 0 || row.MaxCaster > 60)
            {
                run.Clear();
                continue;
            }
            if (run.Count > 0 && !Describe(run.Append(row).ToList(), out _)) run.Clear();
            run.Add(row);
        }
        return run;
    }

    private static double Distance(double ax, double az, double bx, double bz)
        => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    private static PullEvidence Union(PullEvidence a, PullEvidence b) => a with
    {
        PullTicks = Math.Min(a.PullTicks, b.PullTicks), From = Math.Min(a.From, b.From), To = Math.Max(a.To, b.To),
        MinX = Math.Min(a.MinX, b.MinX), MinZ = Math.Min(a.MinZ, b.MinZ), MaxX = Math.Max(a.MaxX, b.MaxX), MaxZ = Math.Max(a.MaxZ, b.MaxZ),
        MinScale = Math.Min(a.MinScale, b.MinScale), MaxScale = Math.Max(a.MaxScale, b.MaxScale),
        MaxCaster = Math.Max(a.MaxCaster, b.MaxCaster), Failures = a.Failures | b.Failures
    };
}
