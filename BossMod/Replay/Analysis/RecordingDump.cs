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
        AppendContributions(sb, replay);
        AppendPositions(sb, replay);

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
            var sourceIsPlayer = a.Source.Type == ActorType.Player;

            foreach (var t in a.Targets)
            {
                for (var i = 0; i < ActionEffects.MaxCount; ++i)
                {
                    var eff = t.Effects[i];
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
            if (a.Source.Type == ActorType.Player)
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
            sb.Append("  looks like: ").AppendLine(Classify(replay, aid, involved.Count));

            foreach (var (p, offsets) in perPlayer)
            {
                var (mean, spread) = MeanAndSpread(offsets);
                sb.Append("  ").Append($"{p.Class} {Name(p)}".PadRight(26))
                  .Append("mean ").Append(Fixed(mean.X)).Append(", ").Append(Fixed(mean.Z))
                  .Append("  dist ").Append(Fixed(mean.Length()))
                  .Append("  ").Append(Octant(mean).PadRight(7))
                  .Append("spread ").Append(Fixed(spread)).AppendLine("y");
            }

            sb.AppendLine();
        }
    }

    /// <summary>
    /// Names the shape of a mechanic from what it did, since a recording with no module has nobody's word for
    /// it. How many players it hit, and how far apart they were standing when it landed, separates a stack
    /// from a spread from something that ignored position entirely.
    ///
    /// The labels hedge where the data genuinely cannot decide. A raidwide that happens to land while everyone
    /// is stacked looks exactly like a stack, and saying so is more useful than picking one.
    /// </summary>
    private static string Classify(Replay replay, ActionID aid, int partySize)
    {
        var hits = new List<int>();
        var spans = new List<float>();
        var closest = new List<float>();
        var roles = new HashSet<Role>();

        foreach (var a in replay.Actions)
        {
            if (a.ID != aid || a.Source.Type == ActorType.Player)
            {
                continue;
            }

            var positions = new List<WPos>();
            foreach (var t in a.Targets)
            {
                if (t.Target.Type != ActorType.Player)
                {
                    continue;
                }

                roles.Add(t.Target.Class.GetRole());
                var pr = t.Target.PosRotAt(a.Timestamp);
                positions.Add(new(pr.X, pr.Z));
            }

            if (positions.Count == 0)
            {
                continue;
            }

            hits.Add(positions.Count);

            var maxSpan = 0f;
            var minSpan = float.MaxValue;
            for (var i = 0; i < positions.Count; ++i)
            {
                for (var j = i + 1; j < positions.Count; ++j)
                {
                    var d = (positions[i] - positions[j]).Length();
                    maxSpan = Math.Max(maxSpan, d);
                    minSpan = Math.Min(minSpan, d);
                }
            }

            spans.Add(maxSpan);
            if (minSpan < float.MaxValue)
            {
                closest.Add(minSpan);
            }
        }

        if (hits.Count == 0)
        {
            return "hit nobody, so it was either dodged every time or does not target players";
        }

        var avgHit = hits.Average();
        var avgSpan = spans.Count > 0 ? spans.Average() : 0f;
        var avgClosest = closest.Count > 0 ? closest.Average() : 0f;

        if (avgHit < 1.5f)
        {
            var role = roles.Count == 1 ? roles.First() : Role.None;
            return role switch
            {
                Role.Tank => "single target on a tank, so probably a tank buster",
                Role.Healer => "single target on a healer",
                _ => $"single target, hitting {Describe(roles)}"
            };
        }

        // Everyone caught, spread across the arena: position made no difference.
        if (avgHit >= partySize - 0.5f && avgSpan > 12f)
        {
            return $"raidwide, everyone hit wherever they stood (up to {avgSpan:f1}y apart)";
        }

        if (avgSpan <= 6f)
        {
            var who = avgHit >= partySize - 0.5f ? "full party" : avgHit <= 4.5f ? "light party" : "part of the party";
            return $"{who} stack, {avgHit:f1} players within {avgSpan:f1}y of each other";
        }

        if (avgClosest >= 8f)
        {
            return $"spread, {avgHit:f1} players with nobody closer than {avgClosest:f1}y";
        }

        return avgHit >= partySize - 0.5f
            ? $"everyone hit, {avgSpan:f1}y apart, so a raidwide or a loose stack"
            : $"{avgHit:f1} players hit, {avgSpan:f1}y apart, hitting {Describe(roles)}";
    }

    private static string Describe(HashSet<Role> roles)
        => roles.Count == 0 ? "nobody" : string.Join(" and ", roles.Where(r => r != Role.None).Select(r => r.ToString().ToLowerInvariant()));

    private static string Name(Replay.Participant p)
        => p.NameHistory.Count > 0 ? p.NameHistory.Values[0].name : $"{p.InstanceID:X}";

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
                sb.Append("  ").Append(p.Class.ToString().PadRight(6))
                  .Append(p.Class.GetRole().ToString().PadRight(8))
                  .AppendLine(Name(p));
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
