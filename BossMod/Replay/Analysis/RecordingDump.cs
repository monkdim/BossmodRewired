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

    // How long the recording has to go quiet before what follows counts as a separate fight. A dungeon is one
    // recording containing several bosses with corridors between them, and treating it as one long encounter
    // puts every boss on the same clock and averages four arenas into one.
    private const double IdleGap = 45d;

    // How far the party as a whole has to shift between two consecutive hostile actions for the second one to
    // count as a different fight. An idle gap alone is not enough: an alliance raid never goes quiet for
    // forty-five seconds, and Syrcus Tower came back as one eight hundred second encounter spanning every
    // boss in it. Changing rooms moves everybody at once, and nothing inside a fight does that, because a
    // party cannot run this far between two actions that are seconds apart.
    private const float RoomChange = 40f;

    // The same idea for a walk that never takes one long stride. Syrcus Tower's middle section stayed merged
    // because its rooms are joined by corridors with trash in them, so the party drifted between bosses a few
    // yards at a time and no single step was ever large. Distance from where the current fight started catches
    // that, and is still far more than a party moves inside one arena.
    private const float RoomDrift = 60f;

    // Below this a segment is a trash pull, and a full positional breakdown of three mobs dying in eight
    // seconds is noise. They are still listed, just not analysed.
    private const double MinFightSeconds = 20d;
    private const int MinFightActions = 30;

    /// <summary>
    /// Whether something counts as part of the fight rather than part of the party. Checking for "not a
    /// player" is not enough: a scholar's fairy and a machinist's turret are separate actors, and in the
    /// first real export the fairy was the single largest source of "hostile" actions in the file.
    /// </summary>
    private static bool IsHostile(Replay.Participant p)
        => p.Type is not (ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy);

    private readonly record struct Event(DateTime Timestamp, int Order, string Text);

    /// <summary>One stretch of continuous fighting, named after whatever did the most in it.</summary>
    private readonly record struct Fight(DateTime Start, DateTime End, string Label, uint OID, int Actions)
    {
        public double Seconds => (End - Start).TotalSeconds;
        public bool WorthAnalysing => Seconds >= MinFightSeconds && Actions >= MinFightActions;
    }

    public static string Build(Replay replay)
    {
        var involved = Involved(replay);
        var events = Collect(replay, involved);
        var fights = Fights(replay, involved);
        var sb = new StringBuilder();

        sb.Append("Recording dump for a duty with no boss module (").Append(replay.Path).AppendLine(")");
        sb.AppendLine("No encounters exist in this recording, so there are no phases or timings to report.");
        sb.AppendLine("It has been split into fights wherever the recording went quiet for " + IdleGap + " seconds or more,");
        sb.AppendLine("or the party moved to another room, since content is several bosses with walking between them");
        sb.AppendLine("rather than one long encounter.");
        sb.AppendLine("Times are seconds from the start of the fight they fall in. Positions are world coordinates.");
        sb.AppendLine();

        AppendFightList(sb, fights);

        AppendPlayers(sb, replay);

        if (events.Count == 0)
        {
            sb.AppendLine("Nothing hostile was recorded. Everything here died before it acted, which is what an");
            sb.AppendLine("unrestricted party running old content at level looks like. There are no mechanics to read.");
            return sb.ToString();
        }

        events.Sort((a, b) => a.Timestamp != b.Timestamp ? a.Timestamp.CompareTo(b.Timestamp) : a.Order.CompareTo(b.Order));

        sb.AppendLine("--- TIMELINE ---");
        var shown = Math.Min(events.Count, MaxEvents);
        var current = -1;
        var start = fights.Count > 0 ? fights[0].Start : events[0].Timestamp;
        for (var i = 0; i < shown; ++i)
        {
            // Events that arrive before the first hostile action, or in a corridor between two fights, belong
            // to the fight they lead into, so the index only ever moves forward.
            var next = fights.Count > 0 ? FightIndex(fights, events[i].Timestamp) : -1;
            if (next > current)
            {
                current = next;
                start = fights[current].Start;
                sb.AppendLine();
                sb.Append("  === FIGHT ").Append(current + 1).Append(": ").Append(fights[current].Label).AppendLine(" ===");
            }

            var rel = (float)(events[i].Timestamp - start).TotalSeconds;
            sb.Append("  T+").Append(rel.ToString("f1").PadLeft(8)).Append("  ").AppendLine(events[i].Text);
        }

        if (events.Count > MaxEvents)
        {
            sb.Append("  ... ").Append(events.Count - MaxEvents).AppendLine(" further events omitted.");
        }

        sb.AppendLine();
        AppendContributions(sb, replay);

        if (involved.Count == 0)
        {
            return sb.ToString();
        }

        for (var i = 0; i < fights.Count; ++i)
        {
            var fight = fights[i];
            if (!fight.WorthAnalysing)
            {
                continue;
            }

            sb.AppendLine("========================================================================");
            sb.Append("POSITIONS for fight ").Append(i + 1).Append(": ").AppendLine(fight.Label);

            // No encounter means no module activated, but the registry is still worth asking: a module that
            // exists and failed to start still declares the arena, and a declaration beats an estimate.
            var arena = ArenaEstimate.ForFight(replay, fight.OID, involved, fight.Start, fight.End);
            var coverage = PositionAnalysis.Append(sb, replay, involved, Label,
                a => a.Timestamp >= fight.Start && a.Timestamp <= fight.End, arena,
                t => (t - fight.Start).TotalSeconds);

            // Per fight rather than per recording, since a recording is a night of several different ones and
            // each has a timeline of its own.
            var window = new Replay.TimeRange(fight.Start, fight.End);
            TimelineCoverage.Append(sb, TimelineCoverage.Observe([(replay, window)]), coverage);
        }

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

    /// <summary>
    /// Role first where one is known, since "Tank 1" is the answer to where should I stand and "DRK Funni
    /// Bnnuy" is not. Falls back to job and name, which is all there is for anyone the party roles config has
    /// never seen.
    /// </summary>
    private static string Label(Replay.Participant p)
    {
        var role = Service.Config.Get<PartyRolesConfig>()[p.ContentID];
        return role != PartyRolesConfig.Assignment.Unassigned ? $"{role} {p.Class}" : $"{p.Class} {Name(p)}";
    }

    private static string Name(Replay.Participant p)
        => p.NameHistory.Count > 0 ? p.NameHistory.Values[0].name : $"{p.InstanceID:X}";

    private static void AppendFightList(StringBuilder sb, List<Fight> fights)
    {
        if (fights.Count == 0)
        {
            return;
        }

        sb.AppendLine("--- FIGHTS ---");
        for (var i = 0; i < fights.Count; ++i)
        {
            var fight = fights[i];
            sb.Append("  ").Append((i + 1).ToString().PadLeft(2)).Append(". ")
              .Append(fight.Label.PadRight(30))
              .Append(fight.Seconds.ToString("f0").PadLeft(4)).Append("s  ")
              .Append(fight.Actions.ToString().PadLeft(5)).Append(" actions")
              .AppendLine(fight.WorthAnalysing ? "" : "  (too short to analyse, probably trash)");
        }

        sb.AppendLine();
    }

    /// <summary>Which fight a moment falls in. Gaps belong to the fight they lead into, so nothing is dropped
    /// from the timeline just because it happened while the party was walking.</summary>
    private static int FightIndex(List<Fight> fights, DateTime t)
    {
        for (var i = 0; i < fights.Count; ++i)
        {
            if (t <= fights[i].End)
            {
                return i;
            }
        }

        return Math.Max(0, fights.Count - 1);
    }

    /// <summary>Middle of the party at one moment, or nothing when there is no party to speak of.</summary>
    private static WPos? Centroid(HashSet<Replay.Participant> party, DateTime t)
    {
        if (party.Count == 0)
        {
            return null;
        }

        var sumX = 0f;
        var sumZ = 0f;
        foreach (var p in party)
        {
            var posRot = p.PosRotAt(t);
            sumX += posRot.X;
            sumZ += posRot.Z;
        }

        return new WPos(sumX / party.Count, sumZ / party.Count);
    }

    private static List<Fight> Fights(Replay replay, HashSet<Replay.Participant> party)
    {
        var hostile = new List<(DateTime Time, Replay.Participant Source)>();
        foreach (var a in replay.Actions)
        {
            if (IsHostile(a.Source))
            {
                hostile.Add((a.Timestamp, a.Source));
            }
        }

        if (hostile.Count == 0)
        {
            return [];
        }

        hostile.Sort((x, y) => x.Time.CompareTo(y.Time));

        var fights = new List<Fight>();
        var counts = new Dictionary<Replay.Participant, int>();
        var start = hostile[0].Time;
        var prev = start;
        var actions = 0;
        var prevCentre = Centroid(party, start);
        var fightCentre = prevCentre;

        foreach (var (t, src) in hostile)
        {
            var centre = Centroid(party, t);
            var walked = prevCentre is WPos a && centre is WPos b ? (b - a).Length() : 0f;
            var drifted = fightCentre is WPos c && centre is WPos d ? (d - c).Length() : 0f;
            prevCentre = centre;

            if ((t - prev).TotalSeconds > IdleGap || walked > RoomChange || drifted > RoomDrift)
            {
                var (label, oid) = Busiest(counts, start);
                fights.Add(new(start, prev, label, oid, actions));
                counts.Clear();
                actions = 0;
                start = t;
                fightCentre = centre;
            }

            counts[src] = counts.GetValueOrDefault(src) + 1;
            ++actions;
            prev = t;
        }

        var (lastLabel, lastOID) = Busiest(counts, start);
        fights.Add(new(start, prev, lastLabel, lastOID, actions));
        return fights;
    }

    /// <summary>Whoever acted most during a stretch, which for a boss fight is the boss and for a trash pull is
    /// whichever mob lived longest. Either way it is the most recognisable name available.</summary>
    private static (string Label, uint OID) Busiest(Dictionary<Replay.Participant, int> counts, DateTime t)
    {
        Replay.Participant? best = null;
        var bestCount = 0;
        foreach (var (p, n) in counts)
        {
            if (n > bestCount)
            {
                bestCount = n;
                best = p;
            }
        }

        return best != null ? (Describe(best, t), best.OID) : ("unknown", 0u);
    }

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
            var roles = Service.Config.Get<PartyRolesConfig>();
            foreach (var p in involved)
            {
                sb.Append("  ").Append(roles[p.ContentID].ToString().PadRight(11))
                  .Append(p.Class.ToString().PadRight(6))
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
