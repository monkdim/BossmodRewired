using System.IO;
using System.Reflection;

namespace BossMod.Timelines;

/// <summary>
/// Every bundled timeline, and two ways of picking the one that describes a fight.
///
/// By zone, for anything live: cactbot pairs zone to timeline itself, and `ZoneTimelines.txt`
/// carries that pairing over, so the right timeline is known the moment the zone loads. That matters because
/// the opening of a fight is exactly the part somebody wants a countdown for.
///
/// By abilities, for a finished recording: a fight that used forty of a timeline's ability IDs is that fight
/// whatever either side chose to call it, and this needs no zone at all. Slower, since it takes a fair number
/// of casts before the answer is clear, but it works on a log rather than on a running game.
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

    private static readonly Lazy<Dictionary<ushort, CactbotTimeline>> _byZone = new(LoadZones);

    /// <summary>
    /// The timeline for a zone, from cactbot's own pairing of the two.
    ///
    /// Worth the table rather than working it out from the abilities as they appear. Measured against a real
    /// M2 Savage pull, recognising the fight from overlap alone took until forty seconds in, and the opening
    /// is exactly the part somebody needs a countdown for.
    /// </summary>
    public static CactbotTimeline? ForZone(ushort zone) => _byZone.Value.GetValueOrDefault(zone);

    private static Dictionary<ushort, CactbotTimeline> LoadZones()
    {
        var res = new Dictionary<ushort, CactbotTimeline>();
        var byName = new Dictionary<string, CactbotTimeline>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in All)
        {
            byName[t.Name] = t;
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(Prefix + "ZoneTimelines.txt");
        if (stream == null)
        {
            Service.Log("[timelines] no zone table found");
            return res;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is string raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var space = line.IndexOf(' ');
            if (space <= 0 || !ushort.TryParse(line[..space], out var zone))
            {
                continue;
            }

            // Stored under cactbot's nested name, flattened the same way the files were.
            var file = line[(space + 1)..].Trim();
            if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                file = file[..^4];
            }

            if (byName.TryGetValue(file, out var timeline))
            {
                res[zone] = timeline;
            }
        }

        Service.Log($"[timelines] {res.Count} zones mapped");
        return res;
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
