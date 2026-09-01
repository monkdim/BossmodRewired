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
        double LongestGap,
        double LongestGapAt,
        bool Reliable);

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

        var lines = new List<Line>(steps.Count);
        foreach (var (player, mine) in steps)
        {
            lines.Add(Measure(player, mine));
        }

        lines.Sort(static (x, y) => y.Gcds.CompareTo(x.Gcds));
        return lines;
    }

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
