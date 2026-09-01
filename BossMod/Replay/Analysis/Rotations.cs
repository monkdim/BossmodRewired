namespace BossMod.ReplayAnalysis;

/// <summary>
/// What each player pressed over one pull, and how much of the pull they spent pressing nothing.
///
/// This is here to answer the question the positions raise but cannot settle. A position summary can say where
/// the melees went for a mechanic; it cannot say which of those places cost them anything. Two parties can
/// stand in two different spots and look equally tidy, and the difference between the strategies is entirely
/// in what the movement cost, which is measured in dropped weaponskills and nowhere else.
///
/// So the useful output is not the list of buttons. It is where the holes are, and how big.
///
/// What this cannot see is worth stating plainly, because the numbers look more authoritative than they are.
/// It sees actions, never intent: a missing weaponskill is a mechanic, a mistake or a deliberate hold, and
/// nothing here can tell those apart. It sees only actors the client had loaded, which in a full alliance
/// means the other two parties are recorded patchily or not at all. And it has no reference rotation to
/// compare against, so it can say one pull had more uptime than another and cannot say either was correct.
/// </summary>
static class Rotations
{
    /// <summary>One button, when it was pressed, relative to the pull.</summary>
    public readonly record struct Step(uint Ability, string Name, double At, bool GCD);

    /// <summary>One player's pull.</summary>
    public readonly record struct Line(
        Replay.Participant Player,
        List<Step> Steps,
        int Gcds,
        int Ogcds,
        double Recast,
        double Active,
        double Lost,
        double LostToFight,
        double LostToMovement,
        double LongestGap,
        double LongestGapAt,
        bool Reliable,
        bool Split);

    /// <summary>What Measure works out before the party is taken into account.</summary>
    private readonly record struct Solo(Line Line, List<(double From, double To)> Idle);

    // How much of the party has to be idle at once before the silence belongs to the fight rather than to
    // anybody in it. Three of eight standing still is a mechanic catching the people it was aimed at; five of
    // eight is the boss being untargetable, and charging that to a player describes the encounter as a failure
    // of the party.
    private const double QuietShare = 0.6d;

    // Below this many measurable players there is no party to compare against, and two people going quiet
    // together is as likely to be coincidence as downtime. The split is withheld rather than guessed.
    private const int EnoughForSplit = 4;

    // Half a second. Fine enough that a single dropped weaponskill lands in the right bucket, coarse enough
    // that a ten minute pull with a full alliance in it is a few tens of thousands of flags rather than
    // millions.
    private const double Slice = 0.5d;

    // Below this many weaponskills there is no interval to take a recast from, and a made-up recast produces a
    // made-up uptime. A pull this short is reported as steps and counts only.
    private const int EnoughForRecast = 5;

    // A recast outside this range is not a recast. Two and a half seconds is the base, haste effects pull it
    // toward two, and nothing legitimately sits outside these bounds; a figure that does means the intervals
    // were not what this thinks they were, and the uptime built on it is withheld rather than printed.
    private const double FastestRecast = 1.4d;
    private const double SlowestRecast = 4d;

    public static List<Line> ForWindow(Replay replay, IReadOnlyCollection<Replay.Participant> involved, DateTime from, DateTime to)
    {
        var steps = new Dictionary<Replay.Participant, List<Step>>();
        foreach (var p in involved)
        {
            if (p.Type == ActorType.Player)
            {
                steps[p] = [];
            }
        }

        foreach (var a in replay.Actions)
        {
            if (a.Timestamp < from || a.Timestamp > to || !steps.TryGetValue(a.Source, out var mine))
            {
                continue;
            }

            // A pet's casts are not the player's rotation, which is why only players are collected above, and
            // an auto-attack is not a button anybody pressed. Both would otherwise land among the off-global
            // abilities and make the count mean something other than what it says.
            if (a.ID.Type != ActionType.Spell || a.ID == ActionDefinitions.IDAutoAttack || a.ID == ActionDefinitions.IDAutoShot)
            {
                continue;
            }

            var row = Service.LuminaRow<Lumina.Excel.Sheets.Action>(a.ID.ID);
            if (row is not { } action || !action.IsPlayerAction)
            {
                continue;
            }

            // The sheet's own answer rather than the plugin's. ActionDefinitions only knows the actions the
            // autorotation has been taught, which is most of them and not all, and an action it has never
            // heard of would silently be counted as off-global.
            var gcd = action.CooldownGroup == ActionDefinitions.GCDGroup + 1
                   || action.AdditionalCooldownGroup == ActionDefinitions.GCDGroup + 1;

            mine.Add(new(a.ID.ID, action.Name.ToString(), (a.Timestamp - from).TotalSeconds, gcd));
        }

        var solo = new List<Solo>(steps.Count);
        foreach (var (player, mine) in steps)
        {
            solo.Add(Measure(player, mine));
        }

        var lines = Attribute(solo, (to - from).TotalSeconds);
        lines.Sort(static (x, y) => y.Gcds.CompareTo(x.Gcds));
        return lines;
    }

    /// <summary>
    /// Splits each player's idle time into the part the fight caused and the part they did.
    ///
    /// Without this the two are one number, and the number is close to useless. A fight with a thirty second
    /// phase where the boss cannot be hit charges every one of those seconds to every player, so comparing two
    /// pulls compares how much downtime each had rather than how well anybody played, and a party that did
    /// everything right reads as a party that stopped.
    ///
    /// Telling them apart needs no knowledge of the fight, which matters because the content worth measuring
    /// is the content nobody has modelled. It only needs the party: silence that most of them share at the
    /// same moment is the encounter, and silence one person has alone is theirs. That holds for a phase
    /// transition, a forced march, an add pack out of melee range, and every other reason a fight stops,
    /// without anybody having written any of them down.
    ///
    /// What it cannot do is separate a mechanic that catches half the party from a phase that stops all of it,
    /// and the threshold is where that judgement sits rather than a fact about the game.
    /// </summary>
    private static List<Line> Attribute(List<Solo> solo, double window)
    {
        var measurable = new List<Solo>();
        foreach (var s in solo)
        {
            if (s.Line.Reliable)
            {
                measurable.Add(s);
            }
        }

        var lines = new List<Line>(solo.Count);
        if (measurable.Count < EnoughForSplit || window <= 0d)
        {
            foreach (var s in solo)
            {
                lines.Add(s.Line);
            }

            return lines;
        }

        // How many of the measurable players are idle in each slice of the pull.
        var slices = (int)(window / Slice) + 1;
        var idleCount = new int[slices];
        foreach (var s in measurable)
        {
            foreach (var (a, b) in s.Idle)
            {
                for (var i = Index(a, slices); i < Index(b, slices); ++i)
                {
                    ++idleCount[i];
                }
            }
        }

        var quiet = new bool[slices];
        for (var i = 0; i < slices; ++i)
        {
            quiet[i] = idleCount[i] >= measurable.Count * QuietShare;
        }

        foreach (var s in solo)
        {
            if (!s.Line.Reliable)
            {
                lines.Add(s.Line);
                continue;
            }

            var toFight = 0d;
            foreach (var (a, b) in s.Idle)
            {
                for (var i = Index(a, slices); i < Index(b, slices); ++i)
                {
                    if (quiet[i])
                    {
                        toFight += Slice;
                    }
                }
            }

            // Never more than there was. Slicing rounds, and rounding that hands somebody more downtime than
            // they had would put a negative number in the other column.
            toFight = Math.Min(toFight, s.Line.Lost);
            lines.Add(s.Line with { LostToFight = toFight, LostToMovement = s.Line.Lost - toFight, Split = true });
        }

        return lines;
    }

    private static int Index(double at, int slices) => Math.Clamp((int)(at / Slice), 0, slices);

    /// <summary>
    /// Turns one player's buttons into the numbers worth reading: how long they were actually spending on
    /// weaponskills, how long they were not, and when each of the holes was.
    ///
    /// The idle windows come back alongside, because whether a hole belongs to the player or to the fight is
    /// not a question about the player and cannot be answered here.
    /// </summary>
    private static Solo Measure(Replay.Participant player, List<Step> steps)
    {
        var gcdAt = new List<double>();
        var ogcds = 0;
        foreach (var s in steps)
        {
            if (s.GCD)
            {
                gcdAt.Add(s.At);
            }
            else
            {
                ++ogcds;
            }
        }

        if (gcdAt.Count < EnoughForRecast)
        {
            return new(new(player, steps, gcdAt.Count, ogcds, 0d, 0d, 0d, 0d, 0d, 0d, 0d, false, false), []);
        }

        var gaps = new List<double>(gcdAt.Count - 1);
        var longest = 0d;
        var longestAt = 0d;
        for (var i = 1; i < gcdAt.Count; ++i)
        {
            var gap = gcdAt[i] - gcdAt[i - 1];
            gaps.Add(gap);
            if (gap > longest)
            {
                longest = gap;
                longestAt = gcdAt[i - 1];
            }
        }

        // The recast is read off the player rather than assumed, because it depends on their job, their spell
        // speed and whatever haste they were under, none of which a recording carries. A low quantile rather
        // than the middle one: half of every player's intervals sit above the median by definition, so the
        // median would call an ordinary rotation half idle. The quarter mark lands on the tight cluster of
        // back-to-back casts, which is the recast, and leaves the long tail where it belongs, in the gaps.
        var sorted = new List<double>(gaps);
        sorted.Sort();
        var recast = sorted[sorted.Count / 4];

        var span = gcdAt[^1] - gcdAt[0];
        if (recast is <= FastestRecast or >= SlowestRecast || span <= 0d)
        {
            return new(new(player, steps, gcdAt.Count, ogcds, recast, 0d, 0d, 0d, 0d, longest, longestAt, false, false), []);
        }

        // Each hole, and how much of it was a hole rather than the recast running. Summing the individual
        // excesses rather than subtracting a count times the recast from the span, because the two only agree
        // when every interval is at least a recast long, and the sum is the one that stays equal to the pieces
        // it is about to be split into.
        var idle = new List<(double From, double To)>();
        var lost = 0d;
        for (var i = 1; i < gcdAt.Count; ++i)
        {
            var from = gcdAt[i - 1] + recast;
            if (gcdAt[i] > from)
            {
                idle.Add((from, gcdAt[i]));
                lost += gcdAt[i] - from;
            }
        }

        // Counted from first cast to last, so the seconds before somebody engaged and after they died are not
        // charged to them; a player who dies halfway is measured on the half they played.
        var line = new Line(player, steps, gcdAt.Count, ogcds, recast, span - lost, lost, 0d, lost,
            longest, longestAt, true, false);
        return new(line, idle);
    }

    private static List<Line> Attribute(List<Solo> solo, double window)
    {
        var measurable = new List<Solo>();
        foreach (var s in solo)
        {
            if (s.Line.Reliable)
            {
                measurable.Add(s);
            }
        }

        var lines = new List<Line>(solo.Count);
        if (measurable.Count < EnoughForSplit || window <= 0d)
        {
            foreach (var s in solo)
            {
                lines.Add(s.Line);
            }

            return lines;
        }

        // How many of the measurable players are idle in each slice of the pull.
        var slices = (int)(window / Slice) + 1;
        var idleCount = new int[slices];
        foreach (var s in measurable)
        {
            foreach (var (a, b) in s.Idle)
            {
                for (var i = Index(a, slices); i < Index(b, slices); ++i)
                {
                    ++idleCount[i];
                }
            }
        }

        var quiet = new bool[slices];
        for (var i = 0; i < slices; ++i)
        {
            quiet[i] = idleCount[i] >= measurable.Count * QuietShare;
        }

        foreach (var s in solo)
        {
            if (!s.Line.Reliable)
            {
                lines.Add(s.Line);
                continue;
            }

            var toFight = 0d;
            foreach (var (a, b) in s.Idle)
            {
                for (var i = Index(a, slices); i < Index(b, slices); ++i)
                {
                    if (quiet[i])
                    {
                        toFight += Slice;
                    }
                }
            }

            // Never more than there was. Slicing rounds, and rounding that hands somebody more downtime than
            // they had would put a negative number in the other column.
            toFight = Math.Min(toFight, s.Line.Lost);
            lines.Add(s.Line with { LostToFight = toFight, LostToMovement = s.Line.Lost - toFight, Split = true });
        }

        return lines;
    }

    private static int Index(double at, int slices) => Math.Clamp((int)(at / Slice), 0, slices);

    /// <summary>
    /// Turns one player's buttons into the two numbers worth reading: how long they were actually spending on
    /// weaponskills, and how long they were not.
    /// </summary>
    private static Line Measure(Replay.Participant player, List<Step> steps)
    {
        var gcdAt = new List<double>();
        var ogcds = 0;
        foreach (var s in steps)
        {
            if (s.GCD)
            {
                gcdAt.Add(s.At);
            }
            else
            {
                ++ogcds;
            }
        }

        if (gcdAt.Count < EnoughForRecast)
        {
            return new(player, steps, gcdAt.Count, ogcds, 0d, 0d, 0d, 0d, 0d, false);
        }

        var gaps = new List<double>(gcdAt.Count - 1);
        var longest = 0d;
        var longestAt = 0d;
        for (var i = 1; i < gcdAt.Count; ++i)
        {
            var gap = gcdAt[i] - gcdAt[i - 1];
            gaps.Add(gap);
            if (gap > longest)
            {
                longest = gap;
                longestAt = gcdAt[i - 1];
            }
        }

        // The recast is read off the player rather than assumed, because it depends on their job, their spell
        // speed and whatever haste they were under, none of which a recording carries. A low quantile rather
        // than the middle one: half of every player's intervals sit above the median by definition, so the
        // median would call an ordinary rotation half idle. The quarter mark lands on the tight cluster of
        // back-to-back casts, which is the recast, and leaves the long tail where it belongs, in the gaps.
        var sorted = new List<double>(gaps);
        sorted.Sort();
        var recast = sorted[sorted.Count / 4];

        var span = gcdAt[^1] - gcdAt[0];
        var reliable = recast is > FastestRecast and < SlowestRecast && span > 0d;

        // Time spent on weaponskills, against time that could have been. Counted from first cast to last, so
        // the seconds before somebody engaged and after they died are not charged to them; a player who dies
        // halfway is measured on the half they played.
        var active = reliable ? Math.Min(gcdAt.Count * recast, span) : 0d;
        var lost = reliable ? span - active : 0d;

        return new(player, steps, gcdAt.Count, ogcds, recast, active, lost, longest, longestAt, reliable);
    }
}
