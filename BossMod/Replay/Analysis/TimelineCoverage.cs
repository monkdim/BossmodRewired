namespace BossMod.ReplayAnalysis;

/// <summary>
/// What the fight is known to do, against what this export managed to say about it.
///
/// Every other section here is built from what a recording contains, which makes it impossible to notice
/// what a recording is missing. A cactbot timeline is an independent list of a fight's mechanics, named the
/// way raiders name them, so joining the two turns "here is what we saw" into "here is what the fight does,
/// and here is the part we still cannot teach". That list is the difference between guessing what to record
/// next and knowing.
///
/// Three answers are worth telling apart. A mechanic the log never saw is a gap in the pull, not in the
/// tooling: the party wiped before it, or never reached the phase. A mechanic that resolved and left us with
/// nothing to say is the real gap. And a mechanic we did prescribe a position for can finally be printed
/// under the name people actually shout, rather than as an ability ID.
/// </summary>
static class TimelineCoverage
{
    /// <summary>Pets and buddies fight on your side, so "not a player" is the wrong test for hostility.</summary>
    private static bool IsHostile(Replay.Participant p)
        => p.Type is not (ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy);

    /// <summary>Cactbot's own bookkeeping entries, which mark syncs and targetability rather than mechanics.</summary>
    private static bool IsBookkeeping(string name)
        => name.Length > 4 && name.StartsWith("--", StringComparison.Ordinal) && name.EndsWith("--", StringComparison.Ordinal);

    // Every occurrence of one named mechanic, folded together. A timeline names the same mechanic once per
    // repeat, and a fight that does something eight times should be one line saying eight, not eight lines.
    private readonly record struct Mechanic(string Name, float First, int Occurrences, bool Observed, PositionAnalysis.Coverage Best);

    /// <summary>
    /// Every hostile spell that resolved or was cast inside any of the given windows.
    ///
    /// Casts count as well as resolutions, because a mechanic aimed at nobody still produces a cast bar and
    /// still appears in the timeline, and reading resolutions alone would file it as never reached.
    /// </summary>
    public static HashSet<uint> Observe(IEnumerable<(Replay Replay, Replay.TimeRange Window)> windows)
    {
        var res = new HashSet<uint>();
        foreach (var (replay, window) in windows)
        {
            foreach (var a in replay.Actions)
            {
                if (a.ID.Type == ActionType.Spell && IsHostile(a.Source) && window.Contains(a.Timestamp))
                {
                    res.Add(a.ID.ID);
                }
            }

            foreach (var p in replay.Participants)
            {
                if (!IsHostile(p))
                {
                    continue;
                }

                foreach (var c in p.Casts)
                {
                    if (c.ID.Type == ActionType.Spell && window.Contains(c.Time.Start))
                    {
                        res.Add(c.ID.ID);
                    }
                }
            }
        }

        return res;
    }

    public static void Append(StringBuilder sb, HashSet<uint> observed, IReadOnlyDictionary<uint, PositionAnalysis.Coverage> coverage)
    {
        var timeline = Timelines.TimelineLibrary.Best(observed);
        if (timeline == null)
        {
            return;
        }

        // Grouped by name rather than by ability, since that is the unit somebody talks about. Two abilities
        // under one name is common: a mechanic that hits and a mechanic that leaves a puddle are one thing.
        var order = new List<string>();
        var byName = new Dictionary<string, Mechanic>();

        foreach (var entry in timeline.Entries)
        {
            if (entry.Abilities.Length == 0 || IsBookkeeping(entry.Name))
            {
                continue;
            }

            var seen = false;
            var best = PositionAnalysis.Coverage.NeverLanded;
            foreach (var id in entry.Abilities)
            {
                if (observed.Contains(id))
                {
                    seen = true;
                }

                if (coverage.TryGetValue(id, out var c) && c > best)
                {
                    best = c;
                }
            }

            if (byName.TryGetValue(entry.Name, out var prev))
            {
                byName[entry.Name] = prev with
                {
                    Occurrences = prev.Occurrences + 1,
                    Observed = prev.Observed || seen,
                    Best = best > prev.Best ? best : prev.Best,
                };
            }
            else
            {
                order.Add(entry.Name);
                byName[entry.Name] = new(entry.Name, entry.Time, 1, seen, best);
            }
        }

        if (byName.Count == 0)
        {
            return;
        }

        var taught = new List<Mechanic>();
        var blind = new List<Mechanic>();
        var unreached = new List<Mechanic>();
        var inert = 0;

        foreach (var name in order)
        {
            var m = byName[name];
            if (!m.Observed)
            {
                unreached.Add(m);
            }
            else if (m.Best is PositionAnalysis.Coverage.Avoided or PositionAnalysis.Coverage.Prescribed or PositionAnalysis.Coverage.Incidental)
            {
                taught.Add(m);
            }
            else if (m.Best == PositionAnalysis.Coverage.NeverLanded)
            {
                // No cast bar, no marker, no tether, and it touched nobody. Two bosses acting on each other,
                // or an ability with no player-facing part. Counted rather than listed: there is nothing to
                // learn about it, so putting it on a list of work to do would be inventing work.
                ++inert;
            }
            else
            {
                blind.Add(m);
            }
        }

        sb.AppendLine("========================================================================");
        sb.Append("COVERAGE against the ").Append(byName.Count).Append(" named mechanics in cactbot's '")
          .Append(timeline.Name).AppendLine("' timeline");
        sb.AppendLine("Names and timings are cactbot's. Everything else here is built from the recording, which is");
        sb.AppendLine("why it can only describe what it saw; this section is the list of what it did not.");
        sb.AppendLine("T+ is cactbot's own time from the pull, so it says where in the fight to go looking.");
        sb.AppendLine();

        Section(sb, blind,
            "NO POSITION YET, and this is the list worth working from",
            "These resolved in the recording and it still could not say where to stand for them. Either nobody",
            "held a spot, or nothing announced them in time to move. A pull where people play them properly is",
            "what would fill these in.");

        Section(sb, unreached,
            "NEVER REACHED in this recording",
            "The timeline lists these and the recording contains no sign of them. A gap in the pull rather than",
            "in the tooling: wiped before them, or the phase was never entered. Nothing to fix except get further.");

        if (inert > 0)
        {
            sb.Append("Not counted anywhere below: ").Append(inert).AppendLine(" named in the timeline that announced");
            sb.AppendLine("themselves in no way and touched nobody, which is the bosses acting on each other rather");
            sb.AppendLine("than a mechanic anybody has to play around.");
            sb.AppendLine();
        }

        Section(sb, taught,
            "COVERED, and now under the name people say out loud",
            "A position was found for these, or the party dodged them outright, or they were established as",
            "catching everyone wherever they stood. The sections above give the numbers; this says which",
            "mechanic each of them is.");
    }

    private static void Section(StringBuilder sb, List<Mechanic> mechanics, string heading, params string[] blurb)
    {
        sb.Append("--- ").Append(heading).Append(": ").Append(mechanics.Count).AppendLine(" ---");
        if (mechanics.Count == 0)
        {
            sb.AppendLine("  (none)");
            sb.AppendLine();
            return;
        }

        foreach (var line in blurb)
        {
            sb.AppendLine(line);
        }

        foreach (var m in mechanics)
        {
            sb.Append("  T+").Append(m.First.ToString("f1").PadLeft(7)).Append("  ").Append(m.Name.PadRight(34))
              .Append(m.Occurrences > 1 ? $"x{m.Occurrences}".PadLeft(5) : "     ")
              .Append("  ").AppendLine(Explain(m));
        }

        sb.AppendLine();
    }

    private static string Explain(Mechanic m) => !m.Observed
        ? "not in this recording"
        : m.Best switch
        {
            PositionAnalysis.Coverage.Avoided => "dodged clean, so the spot is proven safe",
            PositionAnalysis.Coverage.Prescribed => "position found",
            PositionAnalysis.Coverage.Incidental => "position does not matter",
            PositionAnalysis.Coverage.Unheld => "nobody held a position",
            PositionAnalysis.Coverage.Unannounced => "hit with no warning, nothing to position for in advance",
            _ => "no cast bar and it touched nobody, so probably not a mechanic",
        };
}
