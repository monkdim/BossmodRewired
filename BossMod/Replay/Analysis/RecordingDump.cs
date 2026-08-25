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

    /// <summary>
    /// Whether something counts as part of the fight rather than part of the party. Checking for "not a
    /// player" is not enough: a scholar's fairy and a machinist's turret are separate actors, and in the
    /// first real export the fairy was the single largest source of "hostile" actions in the file.
    /// </summary>
    private static bool IsHostile(Replay.Participant p)
        => p.Type is not (ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy);

    private readonly record struct Event(DateTime Timestamp, int Order, string Text);

    public static string Build(Replay replay)
    {
        var events = Collect(replay, Involved(replay));
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
        AppendContributions(sb, replay);
        PositionAnalysis.Append(sb, replay, Involved(replay), p => $"{p.Class} {Name(p)}", _ => true);

        return sb.ToString();
    }

    /// <summary>
    /// What each player actually contributed, and how much they got hit.
    ///
    /// Positional data is only worth building on if the run it came from was played reasonably. A pug that
    /// ate every mechanic produces confident-looking numbers describing where people should not have been
    /// standing, and nothing in the export would otherwise say so.
    /// </summary>
    private static void AppendContributions(StringBuilder sb, Replay replay)
    {
        var involved = Involved(replay);
        if (involved.Count == 0)
        {
            return;
        }

        var dealt = new Dictionary<Replay.Participant, long>();
        var healed = new Dictionary<Replay.Participant, long>();
        var taken = new Dictionary<Replay.Participant, long>();

        foreach (var a in replay.Actions)
        {
            var sourceIsPlayer = !IsHostile(a.Source);

            foreach (var t in a.Targets)
            {
                // Copied to a local first: the indexer hands back raw ulongs, and ValidEffects returns a span
                // into the struct, which would point at a temporary if called on the property directly.
                var effects = t.Effects;
                foreach (var eff in effects.ValidEffects())
                {
                    switch (eff.Type)
                    {
                        case ActionEffectType.Damage:
                        case ActionEffectType.BlockedDamage:
                        case ActionEffectType.ParriedDamage:
                            if (sourceIsPlayer && t.Target.Type != ActorType.Player)
                            {
                                Add(dealt, a.Source, eff.DamageHealValue);
                            }
                            else if (!sourceIsPlayer && t.Target.Type == ActorType.Player)
                            {
                                Add(taken, t.Target, eff.DamageHealValue);
                            }
                            break;
                        case ActionEffectType.Heal:
                            if (sourceIsPlayer)
                            {
                                Add(healed, a.Source, eff.DamageHealValue);
                            }
                            break;
                    }
                }
            }
        }

        var duration = Duration(replay);

        sb.AppendLine("========================================================================");
        sb.Append("CONTRIBUTIONS over ").Append(duration.ToString("f0")).AppendLine("s of recorded combat");
        sb.AppendLine("Use this to judge whether the positions below came from a run worth learning from.");
        sb.AppendLine();

        foreach (var p in involved.OrderByDescending(p => dealt.GetValueOrDefault(p)))
        {
            var dmg = dealt.GetValueOrDefault(p);
            var dps = duration > 0f ? dmg / duration : 0f;
            var deaths = Deaths(p);

            sb.Append("  ").Append($"{p.Class} {Name(p)}".PadRight(26))
              .Append("damage ").Append(dmg.ToString("N0").PadLeft(10))
              .Append("  dps ").Append(dps.ToString("N0").PadLeft(7))
              .Append("  healing ").Append(healed.GetValueOrDefault(p).ToString("N0").PadLeft(9))
              .Append("  taken ").Append(taken.GetValueOrDefault(p).ToString("N0").PadLeft(9))
              .Append("  deaths ").Append(deaths)
              .AppendLine();
        }

        sb.AppendLine();
    }

    private static void Add(Dictionary<Replay.Participant, long> into, Replay.Participant p, int amount)
        => into[p] = into.GetValueOrDefault(p) + amount;

    private static int Deaths(Replay.Participant p)
    {
        var deaths = 0;
        var wasDead = false;
        foreach (var dead in p.DeadHistory.Values)
        {
            if (dead && !wasDead)
            {
                ++deaths;
            }

            wasDead = dead;
        }

        return deaths;
    }

    /// <summary>Span between the first and last hostile action, rather than the whole recording, so time spent
    /// walking to the boss does not deflate everyone's damage per second.</summary>
    private static float Duration(Replay replay)
    {
        var first = DateTime.MaxValue;
        var last = DateTime.MinValue;
        foreach (var a in replay.Actions)
        {
            if (!IsHostile(a.Source))
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

        return last > first ? (float)(last - first).TotalSeconds : 0f;
    }

    private static string Name(Replay.Participant p)
        => p.NameHistory.Count > 0 ? p.NameHistory.Values[0].name : $"{p.InstanceID:X}";

    private static HashSet<Replay.Participant> Involved(Replay replay)
    {
        var involved = new HashSet<Replay.Participant>();
        foreach (var a in replay.Actions)
        {
            if (!IsHostile(a.Source))
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

    private static List<Event> Collect(Replay replay, HashSet<Replay.Participant> involved)
    {
        var events = new List<Event>();

        foreach (var p in replay.Participants)
        {
            if (!IsHostile(p))
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
            if (!IsHostile(a.Source))
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
            // Restricted to the party rather than to players generally. Bystanders standing around when the
            // recording started otherwise contribute entries at the very front of the timeline, and since
            // times are measured from the first event, they drag T+0 to before anything happened.
            if (!involved.Contains(st.Target) || !InflictedByTheFight(st))
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
