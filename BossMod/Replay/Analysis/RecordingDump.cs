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

        return sb.ToString();
    }

    private static void AppendPlayers(StringBuilder sb, Replay replay)
    {
        sb.AppendLine("--- PLAYERS SEEN ---");
        var any = false;
        foreach (var p in replay.Participants)
        {
            if (p.Type != ActorType.Player)
            {
                continue;
            }

            any = true;
            sb.Append("  ").AppendLine(p.NameHistory.Count > 0 ? p.NameHistory.Values[0].name : $"{p.InstanceID:X}");
        }

        if (!any)
        {
            sb.AppendLine("  (none)");
        }

        sb.AppendLine();
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
            // Same filter as the encounter export: a player's own buffs are rotation, not mechanics.
            if (st.Target.Type != ActorType.Player || (st.Source != null && st.Source.Type == ActorType.Player))
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

    private static string Describe(Replay.Participant p, DateTime t)
    {
        var name = p.NameAt(t).name;
        return name != null && name.Length > 0 ? name : $"{p.OID:X}";
    }
}
