namespace BossMod.ReplayAnalysis;

/// <summary>Eligibility for an arena-fixed hint, separate from the descriptive position report.</summary>
static class FixedPositionEvidence
{
    public readonly record struct Observation(Vector2 Cast, Vector2 Hit, Vector2 Settled, DateTime PullStart);
    public readonly record struct Result(bool Eligible, float Spread, int Pulls);

    private const float MaxSpread = 3f;
    private const float MaxMovement = 3f;

    public static Result Evaluate(IReadOnlyList<Observation> observations, bool hasPullTimes, bool observedPlayerDamage)
    {
        if (!hasPullTimes || !observedPlayerDamage || observations.Count == 0)
        {
            return default;
        }

        var mean = Vector2.Zero;
        var starts = new List<DateTime>();
        foreach (var sample in observations)
        {
            if (!Finite(sample.Cast) || !Finite(sample.Hit) || !Finite(sample.Settled)
                || Vector2.Distance(sample.Cast, sample.Hit) > MaxMovement
                || Vector2.Distance(sample.Hit, sample.Settled) > MaxMovement)
            {
                return default;
            }

            mean += sample.Cast;
            starts.Add(sample.PullStart);
        }
        mean /= observations.Count;

        // Repeated hits are not independent evidence. Allow for rounding when reconstructing pull starts.
        starts.Sort();
        var independent = new List<DateTime>();
        foreach (var start in starts)
        {
            if (independent.Count == 0 || (start - independent[^1]).TotalSeconds > 1d)
            {
                independent.Add(start);
            }
        }

        var spread = 0f;
        foreach (var sample in observations)
        {
            var distance = Vector2.Distance(sample.Cast, mean);
            spread = Math.Max(spread, distance);
        }

        // Use the worst distance, rather than letting numerous repeated hits conceal an incompatible spot.
        return new(independent.Count >= 2 && spread < MaxSpread, spread, independent.Count);
    }

    private static bool Finite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);
}
