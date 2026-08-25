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

    /// <summary>What a distance is divided by to become a fraction of the arena. The nearest wall rather than
    /// the corner, so 1.00 means standing against a wall, which is where a knockback puts you.</summary>
    public float Scale => Declared?.NearEdge ?? Radius;

    /// <summary>
    /// Both readings of the same arena, together. For a moduled fight the estimate is redundant, and it is
    /// printed anyway: it is the only way to find out how far short of the wall a party gets, and that number
    /// is what decides whether the estimate can be trusted on content with no module to check it against.
    /// </summary>
    public static ArenaEstimate? ForFight(Replay replay, uint oid, IReadOnlyCollection<Replay.Participant> occupants, DateTime start, DateTime end)
    {
        var declared = DeclaredArena.ForOID(oid);

        // Narrowed to the fighting itself. An encounter begins when the module activates and ends when it
        // deactivates, and neither edge is the fight: the party is walking in at one end and, in a linear
        // dungeon, already walking out at the other. Those yards land in the same bounding box as the arena.
        var (from, to) = CombatWindow(replay, start, end);
        var estimate = Derive(occupants, from, to);

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

    // The smallest an arena is assumed to be. Without a floor, a party that fought in one corner of a large
    // room would have the rest of the room ruled out as though it were a corridor.
    private const float MinArena = 25f;

    // How far past the party's usual distance from the middle a sample can be and still be part of the same
    // room. Generous, because the point is to reject a corridor hundreds of yards long, not to trim the edges.
    private const float Spill = 3f;

    /// <summary>The span actually spent fighting, being the first and last hostile action inside the window.</summary>
    private static (DateTime From, DateTime To) CombatWindow(Replay replay, DateTime start, DateTime end)
    {
        var first = DateTime.MaxValue;
        var last = DateTime.MinValue;

        foreach (var a in replay.Actions)
        {
            if (a.Timestamp < start || a.Timestamp > end || a.Source.Type is ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy)
            {
                continue;
            }

            if (a.Timestamp < first)
            {
                first = a.Timestamp;
            }

            if (a.Timestamp > last)
            {
                last = a.Timestamp;
            }
        }

        return last > first ? (first, last) : (start, end);
    }

    public static ArenaEstimate? Derive(IReadOnlyCollection<Replay.Participant> occupants, DateTime start, DateTime end)
    {
        var samples = Collect(occupants, start, end);
        if (samples.Count < MinSamples)
        {
            return null;
        }

        // Where the party actually spent the fight, taken as a median so that the minority of samples from
        // walking in cannot move it. Anchoring on the boss was tried first and is worse: a boss can be pulled
        // across the room, can share its identifier with adds spawned elsewhere, and in the fights that needed
        // this most it put the anchor somewhere the party never stood.
        var anchor = Median(samples);

        var spread = new List<float>(samples.Count);
        foreach (var p in samples)
        {
            spread.Add((p - anchor).Length());
        }

        spread.Sort();

        // Scaled to how far this particular party ranges rather than fixed, since a 15 yard arena and a 45
        // yard one are both normal and a single threshold cannot serve both.
        var cutoff = Math.Max(MinArena, Spill * spread[spread.Count / 2]);
        var cutoffSq = cutoff * cutoff;

        var kept = new List<WPos>(samples.Count);
        foreach (var p in samples)
        {
            if ((p - anchor).LengthSq() <= cutoffSq)
            {
                kept.Add(p);
            }
        }

        // If almost everything was rejected the assumption was wrong, so use it all rather than describe a
        // corner of the room as the whole of it.
        if (kept.Count < MinSamples)
        {
            kept = samples;
        }

        var xs = new List<float>(kept.Count);
        var zs = new List<float>(kept.Count);
        foreach (var p in kept)
        {
            xs.Add(p.X);
            zs.Add(p.Z);
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

        var all = new List<float>(kept.Count);
        foreach (var p in kept)
        {
            all.Add((p - center).Length());
        }

        all.Sort();

        return new(center, Percentile(all, Trim), halfWidth, halfHeight, DescribeShape(halfWidth, halfHeight), all.Count);
    }

    private static List<WPos> Collect(IReadOnlyCollection<Replay.Participant> occupants, DateTime start, DateTime end)
    {
        var samples = new List<WPos>();

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
                samples.Add(new(posRot.X, posRot.Z));
            }
        }

        return samples;
    }

    /// <summary>Component-wise median, which is not a point anybody stood on but is reliably inside the room.</summary>
    private static WPos Median(List<WPos> samples)
    {
        var xs = new List<float>(samples.Count);
        var zs = new List<float>(samples.Count);
        foreach (var p in samples)
        {
            xs.Add(p.X);
            zs.Add(p.Z);
        }

        xs.Sort();
        zs.Sort();
        return new(xs[xs.Count / 2], zs[zs.Count / 2]);
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
        if (Declared != null && Samples > 0 && Declared.NearEdge > 0f)
        {
            // Reported against both distances rather than one, because which of them a party reaches is the
            // thing being measured. In a square arena people line the walls and avoid the corners, so a reach
            // of 1.00 against the wall and 0.71 against the corner is full coverage, not two thirds of it.
            sb.Append("  CALIBRATION: the party reached ").Append(Radius.ToString("f1")).Append("y, which is ")
              .Append((Radius / Declared.NearEdge).ToString("f2")).Append(" of the ").Append(Declared.NearEdge.ToString("f1")).Append("y wall");

            if (Declared.MaxReach > Declared.NearEdge + 0.01f)
            {
                sb.Append(" and ").Append((Radius / Declared.MaxReach).ToString("f2")).Append(" of the ").Append(Declared.MaxReach.ToString("f1")).Append("y corner");
            }

            if (Declared.CenterIsReliable)
            {
                sb.Append("; centre ").Append((Center - Declared.Center).Length().ToString("f2")).Append("y off");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
    }

    /// <summary>
    /// What can honestly be said about the outline, which is less than it first appears.
    ///
    /// This used to compare reach along the cardinals against reach along the diagonals, on the reasoning that
    /// a square reaches about 1.41 times further into its corners than a circle does. Real exports settled it:
    /// parties do not stand in corners. Four of the six arenas in one Windurst run were declared rectangles or
    /// squares and every one of them was reported as probably circular, because nobody had been near enough to
    /// a corner to leave a trace of it. A confident wrong answer is worse than none, so the only claims left
    /// are the ones the samples actually support: how far the occupied area reaches on each axis.
    /// </summary>
    private static string DescribeShape(float halfWidth, float halfHeight)
    {
        if (halfWidth <= 0f || halfHeight <= 0f)
        {
            return "too little coverage to say anything about the outline";
        }

        var aspect = Math.Max(halfWidth, halfHeight) / Math.Min(halfWidth, halfHeight);
        var elongation = aspect switch
        {
            > 1.35f => halfWidth > halfHeight ? ", noticeably longer east to west" : ", noticeably longer north to south",
            _ => ""
        };

        return $"occupied area only{elongation}; a circle and a square look alike from inside, since nobody stands in the corners";
    }

    private static float Percentile(List<float> sorted, float p)
    {
        var idx = (int)MathF.Round(p * (sorted.Count - 1));
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }
}
