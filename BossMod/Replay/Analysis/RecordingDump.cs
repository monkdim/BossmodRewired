namespace BossMod.ReplayAnalysis;

/// <summary>
/// Dumps a whole recording that contains no encounters.
///
/// Encounters only exist where a boss module activated, so a duty nobody has written a module for produces
/// none at all. That is precisely the content worth capturing, and an export keyed on encounters would write
/// nothing for it. This walks the recording directly instead: no phases, no state machine, no enum names,
/// just everything hostile that happened in the order it happened.
/// </summary>
static class RecordingDump
{
    private const int MaxEvents = 8000;

    private readonly record struct Event(DateTime Timestamp, int Order, string Text);

    public static string Build(Replay replay)
    {
        var events = Collect(replay);
        var sb = new StringBuilder();

        sb.Append("Recording dump for a duty with no boss module (").Append(replay.Path).AppendLine(")");
        sb.AppendLine("No encounters exist in this recording, so there are no phases or timings to report.");
        sb.AppendLine("Times are seconds from the first recorded hostile action. Positions are world coordinates.");
        sb.AppendLine();

        AppendPlayers(sb, replay);

        if (events.Count == 0)
        {
            sb.AppendLine("Nothing hostile was recorded.");
            return sb.ToString();
        }

        events.Sort((a, b) => a.Timestamp != b.Timestamp ? a.Timestamp.CompareTo(b.Timestamp) : a.Order.CompareTo(b.Order));
        var start = events[0].Timestamp;

        sb.AppendLine("--- TIMELINE ---");
        var shown = Math.Min(events.Count, MaxEvents);
        for (var i = 0; i < shown; ++i)
        {
            var rel = (float)(events[i].Timestamp - start).TotalSeconds;
            sb.Append("  T+").Append(rel.ToString("f1").PadLeft(8)).Append("  ").AppendLine(events[i].Text);
        }

        if (events.Count > MaxEvents)
        {
            sb.Append("  ... ").Append(events.Count - MaxEvents).AppendLine(" further events omitted.");
        }

        sb.AppendLine();
        AppendPositions(sb, replay);

        return sb.ToString();
    }

    /// <summary>
    /// Where everyone stood when each ability resolved, measured from the caster rather than from an arena
    /// centre. A recording with no module can span several arenas in different parts of the map, so a single
    /// centre would be meaningless; and "how far from the thing casting it, and in which direction" is the
    /// question a positional hint answers anyway.
    /// </summary>
    private static void AppendPositions(StringBuilder sb, Replay replay)
    {
        var involved = Involved(replay);
        if (involved.Count == 0)
        {
            return;
        }

        // ability -> player -> offsets from the caster at each resolution
        var byAbility = new Dictionary<ActionID, Dictionary<Replay.Participant, List<WDir>>>();
        var casterPositions = new Dictionary<ActionID, List<WPos>>();

        foreach (var a in replay.Actions)
        {
            if (a.Source.Type == ActorType.Player)
            {
                continue;
            }

            var t = a.Timestamp;
            var src = a.Source.PosRotAt(t);
            var origin = new WPos(src.X, src.Z);

            casterPositions.GetOrAdd(a.ID).Add(origin);
            var perPlayer = byAbility.GetOrAdd(a.ID);

            foreach (var p in involved)
            {
                if (p.DeadAt(t))
                {
                    continue;
                }

                var pos = p.PosRotAt(t);
                perPlayer.GetOrAdd(p).Add(new WPos(pos.X, pos.Z) - origin);
            }
        }

        sb.AppendLine("========================================================================");
        sb.AppendLine("POSITIONS, relative to whatever cast the ability, at the moment it resolved");
        sb.AppendLine();

        foreach (var (aid, perPlayer) in byAbility)
        {
            var casts = casterPositions[aid].Count;
            sb.Append(aid).Append(" - ").Append(casts).AppendLine(" resolutions");

            foreach (var (p, offsets) in perPlayer)
            {
                var (mean, spread) = MeanAndSpread(offsets);
                var name = p.NameHistory.Count > 0 ? p.NameHistory.Values[0].name : $"{p.InstanceID:X}";
                sb.Append("  ").Append(name.PadRight(22))
                  .Append("mean ").Append(Fixed(mean.X)).Append(", ").Append(Fixed(mean.Z))
                  .Append("  dist ").Append(Fixed(mean.Length()))
                  .Append("  ").Append(Octant(mean).PadRight(7))
                  .Append("spread ").Append(Fixed(spread)).AppendLine("y");
            }

            sb.AppendLine();
        }
    }

    private static (WDir Mean, float Spread) MeanAndSpread(List<WDir> offsets)
    {
        var count = offsets.Count;
        var sumX = 0f;
        var sumZ = 0f;
        for (var i = 0; i < count; ++i)
        {
            sumX += offsets[i].X;
            sumZ += offsets[i].Z;
        }

        var mean = new WDir(sumX / count, sumZ / count);

        var spread = 0f;
        for (var i = 0; i < count; ++i)
        {
            spread += (offsets[i] - mean).Length();
        }

        return (mean, spread / count);
    }

    private static readonly string[] Octants = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    // FFXIV world axes put north at -Z and east at +X, so a compass bearing is 180 degrees off WDir.ToAngle.
    private static string Octant(WDir offset)
    {
        if (offset.LengthSq() < 0.01f)
        {
            return "on top";
        }

        var bearing = (180f - offset.ToAngle().Deg + 360f) % 360f;
        return Octants[(int)MathF.Round(bearing / 45f) % 8];
    }

    private static string Fixed(float v) => v.ToString("f2").PadLeft(7);

    private static void AppendPlayers(StringBuilder sb, Replay replay)
    {
        var involved = Involved(replay);

        sb.AppendLine("--- PLAYERS IN THE FIGHT ---");
        if (involved.Count == 0)
        {
            sb.AppendLine("  (nobody was hit by anything hostile)");
        }
        else
        {
            foreach (var p in involved)
            {
                sb.Append("  ").AppendLine(p.NameHistory.Count > 0 ? p.NameHistory.Values[0].name : $"{p.InstanceID:X}");
            }
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Everyone the recording saw includes whatever crowd was standing around when it started, so membership
    /// is defined by being hit by something hostile rather than by being present.
    /// </summary>
    private static HashSet<Replay.Participant> Involved(Replay replay)
    {
        var involved = new HashSet<Replay.Participant>();
        foreach (var a in replay.Actions)
        {
            if (a.Source.Type == ActorType.Player)
            {
                continue;
            }

            foreach (var t in a.Targets)
            {
                if (t.Target.Type == ActorType.Player)
                {
                    involved.Add(t.Target);
                }
            }
        }

        return involved;
    }

    private static List<Event> Collect(Replay replay)
    {
        var events = new List<Event>();

        foreach (var p in replay.Participants)
        {
            if (p.Type == ActorType.Player)
            {
                continue;
            }

            foreach (var c in p.Casts)
            {
                var target = c.Target != null ? Describe(c.Target, c.Time.Start) : $"location ({c.Location.X:f2}, {c.Location.Z:f2})";
                events.Add(new(c.Time.Start, 0,
                    $"CAST   {c.ID} by {Describe(p, c.Time.Start)} ({p.PosRotAt(c.Time.Start).X:f2}, {p.PosRotAt(c.Time.Start).Z:f2}) -> {target}, {c.Time.Duration:f1}s of {c.ExpectedCastTime:f1}s"));
            }
        }

        foreach (var a in replay.Actions)
        {
            if (a.Source.Type == ActorType.Player)
            {
                continue;
            }

            var hits = new List<string>(a.Targets.Count);
            foreach (var t in a.Targets)
            {
                hits.Add(Describe(t.Target, a.Timestamp));
            }

            events.Add(new(a.Timestamp, 1,
                $"HIT    {a.ID} from {Describe(a.Source, a.Timestamp)} at ({a.TargetPos.X:f2}, {a.TargetPos.Z:f2}) -> {(hits.Count > 0 ? string.Join(", ", hits) : "nobody")}"));
        }

        foreach (var i in replay.Icons)
        {
            events.Add(new(i.Timestamp, 2, $"ICON   {i.ID} on {(i.Target != null ? Describe(i.Target, i.Timestamp) : "?")}"));
        }

        foreach (var t in replay.Tethers)
        {
            events.Add(new(t.Time.Start, 3, $"TETHER {t.ID} {Describe(t.Source, t.Time.Start)} -> {Describe(t.Target, t.Time.Start)}, {t.Time.Duration:f1}s"));
        }

        foreach (var st in replay.Statuses)
        {
            if (st.Target.Type != ActorType.Player || !InflictedByTheFight(st))
            {
                continue;
            }

            events.Add(new(st.Time.Start, 4, $"STATUS {st.ID} on {Describe(st.Target, st.Time.Start)} for {st.InitialDuration:f1}s, extra {st.StartingExtra:X}"));
        }

        foreach (var m in replay.MapEffects)
        {
            events.Add(new(m.Timestamp, 5, $"ARENA  index {m.Index} state {m.State:X8}"));
        }

        return events;
    }

    /// <summary>
    /// A status counts as part of the fight only if some non-player actor applied it. Filtering on the target
    /// alone is not enough: Free Company buffs, food and sprint all land on players with no source at all, and
    /// in a crowded instance they outnumbered real mechanic debuffs roughly two to one, taking the meaning of
    /// T+0 with them.
    /// </summary>
    private static bool InflictedByTheFight(Replay.Status st) => st.Source != null && st.Source.Type != ActorType.Player;

    private static string Describe(Replay.Participant p, DateTime t)
    {
        var name = p.NameAt(t).name;
        return name != null && name.Length > 0 ? name : $"{p.OID:X}";
    }
}
