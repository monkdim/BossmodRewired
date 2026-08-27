using System.IO;
using System.Reflection;

namespace BossMod.Timelines;

/// <summary>
/// Every bundled timeline, and the means to work out which one describes a given recording.
///
/// Deliberately not keyed on zone or duty name. Cactbot files it's timelines by expansion and content type
/// under names of its own, and any table mapping those to our zones would be another thing to maintain and
/// another thing to be wrong. The abilities are the identity: a fight that used forty of a timeline's ability
/// IDs is that fight, whatever either side chose to call it.
/// </summary>
public static class TimelineLibrary
{
    // Under this, an overlap is two fights that happen to share a few generic abilities rather than a match.
    private const int MinShared = 5;

    private static readonly Lazy<List<CactbotTimeline>> _all = new(Load);

    public static IReadOnlyList<CactbotTimeline> All => _all.Value;

    /// <summary>The timeline that best describes a recording, or nothing when none of them do.</summary>
    public static CactbotTimeline? Best(IReadOnlyCollection<uint> observed)
    {
        if (observed.Count == 0)
        {
            return null;
        }

        CactbotTimeline? best = null;
        var bestShared = 0;
        var bestFraction = 0f;

        foreach (var timeline in All)
        {
            var shared = 0;
            foreach (var id in observed)
            {
                if (timeline.Abilities.Contains(id))
                {
                    ++shared;
                }
            }

            if (shared < MinShared)
            {
                continue;
            }

            // Count first, then how much of the timeline that count represents. A long fight sharing sixty
            // abilities beats a short one sharing eight, but between two that share the same number the one
            // it accounts for more of is the likelier answer.
            var fraction = (float)shared / timeline.Abilities.Count;
            if (shared > bestShared || (shared == bestShared && fraction > bestFraction))
            {
                best = timeline;
                bestShared = shared;
                bestFraction = fraction;
            }
        }

        return best;
    }

    private static List<CactbotTimeline> Load()
    {
        var res = new List<CactbotTimeline>();
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(".txt", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                var timeline = CactbotTimeline.Parse(name[Prefix.Length..^4], reader);

                // A file with nothing timed in it is not a timeline, whatever it is called.
                if (timeline.Entries.Count > 0)
                {
                    res.Add(timeline);
                }
            }
            catch (Exception e)
            {
                // One unreadable timeline is not worth losing the other three hundred over.
                Service.Log($"[timelines] could not read {name}: {e.Message}");
            }
        }

        Service.Log($"[timelines] loaded {res.Count}");
        return res;
    }

    private const string Prefix = "BossMod.Timelines.Cactbot.";
}
