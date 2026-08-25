namespace BossMod.ReplayAnalysis;

/// <summary>
/// The playable area of one fight, reconstructed from where the party actually stood.
///
/// Modules declare their arena, but a duty nobody has written a module for declares nothing, and that is
/// exactly the content worth exporting. Several mechanics cannot be described without one: a knockback is
/// survivable or lethal depending on how close to the edge it starts, and "stay near the middle" is not a
/// statement about the boss, which is usually standing in the middle itself.
///
/// The estimate is a lower bound. It is built from occupied positions, and a party never quite reaches the
/// wall, so the real arena is always at least this big and usually a little bigger.
/// </summary>
sealed record class ArenaEstimate(WPos Center, float Radius, float HalfWidth, float HalfHeight, string Shape, int Samples)
{
    /// <summary>What the module said, when there was a module to ask.</summary>
    public DeclaredArena? Declared { get; init; }

    /// <summary>The centre positions are measured from. A declaration beats an estimate, except where the
    /// module took its centre from the boss and reported the origin instead.</summary>
    public WPos Reference => Declared is { CenterIsReliable: true } d ? d.Center : Center;

    /// <summary>What a distance is divided by to become a fraction of the arena.</summary>
    public float Scale => Declared?.MaxReach ?? Radius;

    /// <summary>
    /// Both readings of the same arena, together. For a moduled fight the estimate is redundant, and it is
    /// printed anyway: it is the only way to find out how far short of the wall a party gets, and that number
    /// is what decides whether the estimate can be trusted on content with no module to check it against.
    /// </summary>
    public static ArenaEstimate? ForFight(uint oid, IReadOnlyCollection<Replay.Participant> occupants, DateTime start, DateTime end)
    {
        var declared = DeclaredArena.ForOID(oid);
        var estimate = Derive(occupants, start, end);

        if (estimate != null)
        {
            return estimate with { Declared = declared };
        }

        // Too few samples to estimate, but a declaration needs none.
        return declared != null
            ? new(declared.Center, declared.Radius, declared.Radius, declared.Radius, "", 0) { Declared = declared }
            : null;
    }

    // Below this there is not enough coverage for the extremes to mean anything, and a confident-looking
    // radius drawn from a handful of samples is worse than saying nothing.
    private const int MinSamples = 64;

    // Trimmed rather than absolute, because a single sample from a cutscene, a death teleport or the moment
    // before a wall goes up sits outside the fighting area and would set the size on its own.
    private const float Trim = 0.995f;

    public static ArenaEstimate? Derive(IReadOnlyCollection<Replay.Participant> occupants, DateTime start, DateTime end)
    {
        var xs = new List<float>();
        var zs = new List<float>();

        foreach (var p in occupants)
        {
            var hist = p.PosRotHistory;
            var count = hist.Count;
            for (var i = 0; i < count; ++i)
            {
                var t = hist.Keys[i];
                if (t < start)
                {
                    continue;
                }
                if (t > end)
                {
                    break;
                }

                var posRot = hist.Values[i];
                xs.Add(posRot.X);
                zs.Add(posRot.Z);
            }
        }

        if (xs.Count < MinSamples)
        {
            return null;
        }

        xs.Sort();
        zs.Sort();

        var minX = Percentile(xs, 1f - Trim);
        var maxX = Percentile(xs, Trim);
        var minZ = Percentile(zs, 1f - Trim);
        var maxZ = Percentile(zs, Trim);

        var center = new WPos((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        var halfWidth = (maxX - minX) * 0.5f;
        var halfHeight = (maxZ - minZ) * 0.5f;

        // Reach is measured separately along the cardinals and the diagonals. A circle reaches equally far in
        // both; a square reaches about 1.41 times further into its corners. That ratio is the only thing in
        // the sample that distinguishes the two, since neither shape leaves a trace anywhere else.
        var cardinal = new List<float>();
        var diagonal = new List<float>();
        var all = new List<float>(xs.Count);

        // xs and zs were sorted independently and no longer pair up, so distances need a second pass.
        foreach (var p in occupants)
        {
            var hist = p.PosRotHistory;
            var count = hist.Count;
            for (var i = 0; i < count; ++i)
            {
                var t = hist.Keys[i];
                if (t < start)
                {
                    continue;
                }
                if (t > end)
                {
                    break;
                }

                var posRot = hist.Values[i];
                var offset = new WPos(posRot.X, posRot.Z) - center;
                var d = offset.Length();
                all.Add(d);

                if (d < 1f)
                {
                    continue; // no meaningful bearing near the middle
                }

                var bearing = (180f - offset.ToAngle().Deg + 360f) % 360f;
                var octant = (int)MathF.Round(bearing / 45f) % 8;
                (octant % 2 == 0 ? cardinal : diagonal).Add(d);
            }
        }

        all.Sort();
        var radius = Percentile(all, Trim);

        return new(center, radius, halfWidth, halfHeight, DescribeShape(cardinal, diagonal, halfWidth, halfHeight), all.Count);
    }

    /// <summary>How the arena reads in one line, with the caveat attached, since the number invites more trust
    /// than it has earned.</summary>
    public void Append(StringBuilder sb)
    {
        sb.AppendLine("--- ARENA ---");

        if (Declared != null)
        {
            sb.Append("  declared by the module: centre (")
              .Append(Declared.Center.X.ToString("f2")).Append(", ").Append(Declared.Center.Z.ToString("f2")).Append(')')
              .Append("  radius ").Append(Declared.Radius.ToString("f1")).Append('y')
              .Append("  ").AppendLine(Declared.Shape);

            if (!Declared.CenterIsReliable)
            {
                sb.AppendLine("  that centre reads as the origin, so the module takes it from the boss; the estimated one is used instead");
            }
        }

        if (Samples > 0)
        {
            sb.Append("  estimated from where the party stood: centre (")
              .Append(Center.X.ToString("f2")).Append(", ").Append(Center.Z.ToString("f2")).Append(')')
              .Append("  reach ").Append(Radius.ToString("f1")).Append('y')
              .Append("  extent ").Append((HalfWidth * 2f).ToString("f1")).Append(" by ").Append((HalfHeight * 2f).ToString("f1")).Append('y')
              .Append("  ").AppendLine(Shape);
            sb.Append("  from ").Append(Samples).AppendLine(" position samples. Nobody stands against the wall, so this always reads small.");
        }

        // The whole reason both are printed. Content with a module does not need an estimate; it needs to
        // say how wrong the estimate was, so the estimate can be corrected everywhere there is nothing to
        // check it against.
        if (Declared != null && Samples > 0 && Declared.MaxReach > 0f)
        {
            sb.Append("  CALIBRATION: the party reached ").Append((Radius / Declared.MaxReach).ToString("f2"))
              .Append(" of the ").Append(Declared.MaxReach.ToString("f1")).Append("y it could have");
            if (Declared.CenterIsReliable)
            {
                sb.Append(", and the estimated centre was ").Append((Center - Declared.Center).Length().ToString("f2")).Append("y off");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
    }

    private static string DescribeShape(List<float> cardinal, List<float> diagonal, float halfWidth, float halfHeight)
    {
        if (halfWidth > 0f && halfHeight > 0f)
        {
            var aspect = Math.Max(halfWidth, halfHeight) / Math.Min(halfWidth, halfHeight);
            if (aspect > 1.35f)
            {
                return halfWidth > halfHeight ? "long east to west" : "long north to south";
            }
        }

        if (cardinal.Count < MinSamples / 4 || diagonal.Count < MinSamples / 4)
        {
            return "shape unclear, the party did not cover enough of it";
        }

        cardinal.Sort();
        diagonal.Sort();
        var ratio = Percentile(diagonal, Trim) / Percentile(cardinal, Trim);

        return ratio switch
        {
            > 1.25f => "corners reachable, so square or rectangular",
            < 1.12f => "reach is even in every direction, so probably circular",
            _ => "shape unclear, somewhere between a circle and a square"
        };
    }

    private static float Percentile(List<float> sorted, float p)
    {
        var idx = (int)MathF.Round(p * (sorted.Count - 1));
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }
}
