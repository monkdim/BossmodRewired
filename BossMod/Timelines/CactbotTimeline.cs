using System.IO;
using System.Text.RegularExpressions;

namespace BossMod.Timelines;

/// <summary>
/// One line of a cactbot timeline: a moment, a name for what happens then, and the abilities that mark it.
/// </summary>
public sealed record class TimelineEntry(float Time, string Name, uint[] Abilities, string? Source, bool OnCastStart, float Duration);

/// <summary>
/// A fight as cactbot describes it: every mechanic in order, named, with the ability that marks each one.
///
/// This is knowledge the plugin cannot derive. A module says an ability exists and the game says what it is
/// called; neither says that two abilities half a second apart are the bait and the damage of one mechanic, or
/// that the boss moves north at a particular moment. The timelines say all of it, for content with a module
/// and without, and they key on ability ID and caster name, which is exactly what the recorder already keeps.
///
/// The format is plain text. A line is a time, a quoted name, and optionally a sync clause naming the ability
/// that marks it. A sync commented out with a leading hash is informational: the mechanic happens, but cactbot
/// does not use it to keep its clock. Both are worth reading here, since either way it names a real mechanic.
/// </summary>
public sealed class CactbotTimeline(string name, List<TimelineEntry> entries)
{
    public readonly string Name = name;
    public readonly List<TimelineEntry> Entries = entries;
    public readonly HashSet<uint> Abilities = [.. entries.SelectMany(e => e.Abilities)];

    // A line is "<seconds> "<name>"" followed by whatever else it carries.
    private static readonly Regex _line = new("^\\s*([0-9]+(?:\\.[0-9]+)?)\\s+\"([^\"]*)\"\\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex _sync = new("#?(Ability|StartsUsing)\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
    private static readonly Regex _id = new("id:\\s*(\\[[^\\]]*\\]|\"[^\"]*\")", RegexOptions.Compiled);
    private static readonly Regex _source = new("source:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex _duration = new("\\bduration\\s+([0-9]+(?:\\.[0-9]+)?)", RegexOptions.Compiled);
    private static readonly Regex _quoted = new("\"([^\"]*)\"", RegexOptions.Compiled);

    public static CactbotTimeline Parse(string name, TextReader reader)
    {
        var entries = new List<TimelineEntry>();

        while (reader.ReadLine() is string raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("hideall", StringComparison.Ordinal))
            {
                continue;
            }

            var m = _line.Match(line);
            if (!m.Success)
            {
                continue;
            }

            if (!float.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var time))
            {
                continue;
            }

            var rest = m.Groups[3].Value;
            var sync = _sync.Match(rest);
            var abilities = Array.Empty<uint>();
            string? source = null;
            var onCastStart = false;

            if (sync.Success)
            {
                onCastStart = sync.Groups[1].Value == "StartsUsing";
                var body = sync.Groups[2].Value;

                var ids = _id.Match(body);
                if (ids.Success)
                {
                    var found = new List<uint>();
                    foreach (Match q in _quoted.Matches(ids.Groups[1].Value))
                    {
                        Expand(q.Groups[1].Value, found);
                    }

                    abilities = [.. found];
                }

                var src = _source.Match(body);
                source = src.Success ? src.Groups[1].Value : null;
            }

            var dur = _duration.Match(rest);
            var duration = dur.Success && float.TryParse(dur.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0f;

            entries.Add(new(time, m.Groups[2].Value, abilities, source, onCastStart, duration));
        }

        entries.Sort((a, b) => a.Time.CompareTo(b.Time));
        return new(name, entries);
    }

    // Beyond this an expansion is a mistake in the pattern rather than a list of abilities.
    private const int MaxExpansion = 64;

    /// <summary>
    /// Turns one written ability ID into every ID it stands for. Most are plain hex, but around four percent
    /// are written as patterns covering a family of related abilities: "B7[67]" for a pair, "37(50|4F)" for
    /// two that share no digits. Reading those as literals would drop them.
    /// </summary>
    private static void Expand(string pattern, List<uint> into)
    {
        List<string> options = [""];

        for (var i = 0; i < pattern.Length;)
        {
            var open = pattern[i];
            if (open is not ('[' or '('))
            {
                options = [.. options.Select(o => o + open)];
                ++i;
                continue;
            }

            var close = open == '[' ? ']' : ')';
            var end = pattern.IndexOf(close, i);
            if (end < 0)
            {
                return; // malformed, and a half-read ID is worse than none
            }

            var body = pattern[(i + 1)..end];
            // Explicitly typed: a character class yields a sequence and an alternation yields an array, and
            // leaving the compiler to find a common type between them is a needless thing to depend on.
            IEnumerable<string> alternatives = open == '['
                ? body.Select(c => c.ToString())
                : body.Split('|', StringSplitOptions.RemoveEmptyEntries);

            options = [.. options.SelectMany(o => alternatives.Select(a => o + a))];
            if (options.Count > MaxExpansion)
            {
                return;
            }

            i = end + 1;
        }

        foreach (var o in options)
        {
            if (uint.TryParse(o, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                into.Add(id);
            }
        }
    }
}
