using System.IO;
using System.Text.RegularExpressions;

namespace BossMod.Timelines;

/// <summary>
/// One line of a cactbot timeline: a moment, a name for what happens then, and the abilities that mark it.
/// </summary>
public sealed record class TimelineEntry(float Time, string Name, uint[] Abilities, string? Source, bool OnCastStart, float Duration)
{
    public bool IsCastSync { get; init; }
    public string? SyncEvent { get; init; }
    public IReadOnlyDictionary<string, Regex> SyncFields { get; init; } = new Dictionary<string, Regex>();
    public Regex? SyncSource { get; init; }
    public float WindowBefore { get; init; } = 2.5f;
    public float WindowAfter { get; init; } = 2.5f;
    public float? JumpTo { get; init; }
    public bool ForceJump { get; init; }
}

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
    private static readonly Regex _sync = new("#?(Ability|StartsUsing|InCombat|ActorControl|MapEffect)\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
    private const string StringValue = """(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')""";
    private const string Values = @"(\[(?:\s*" + StringValue + @"\s*,?)*\s*\]|" + StringValue + ")";
    private static readonly Regex _id = new(@"\bid:\s*" + Values, RegexOptions.Compiled);
    private static readonly Regex _source = new(@"\bsource:\s*" + Values, RegexOptions.Compiled);
    private static readonly Regex _field = new(@"\b(\w+)\s*:\s*" + Values, RegexOptions.Compiled);
    private static readonly Regex _duration = new("\\bduration\\s+([0-9]+(?:\\.[0-9]+)?)", RegexOptions.Compiled);
    private static readonly Regex _quoted = new(StringValue, RegexOptions.Compiled);
    private const string Number = @"\d+(?:\.\d+)?";
    private static readonly Regex _label = new("^(" + Number + ")\\s+label\\s+\"([^\"]+)\"");
    private static readonly Regex _window = new(@"\bwindow\s+(" + Number + @")(?:\s*,\s*(" + Number + "))?");
    private static readonly Regex _jump = new("\\b(jump|forcejump)\\s+(?:\"([^\"]+)\"|(" + Number + "))");

    private static float ReadNumber(string value) => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static string ReadString(Match value) => value.Value[1..^1].Replace(@"\\", @"\").Replace("\\'", "'").Replace("\\\"", "\"");

    public static CactbotTimeline Parse(string name, TextReader reader)
    {
        var entries = new List<TimelineEntry>();
        var labels = new Dictionary<string, float>(StringComparer.Ordinal);
        var unresolvedJumps = new Dictionary<int, string>();
        var sources = new Dictionary<string, Regex?>(StringComparer.Ordinal);

        while (reader.ReadLine() is string raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("hideall", StringComparison.Ordinal))
            {
                continue;
            }

            var label = _label.Match(line);
            if (label.Success)
            {
                labels[label.Groups[2].Value] = ReadNumber(label.Groups[1].Value);
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
            Regex? sourceMatcher = null;
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
                        Expand(ReadString(q), found);
                    }

                    abilities = [.. found];
                }

                var src = _source.Match(body);
                if (src.Success)
                {
                    var expressions = _quoted.Matches(src.Groups[1].Value).Select(ReadString).ToArray();
                    source = expressions.FirstOrDefault();
                    if (expressions.Length > 0 && !sources.TryGetValue(src.Value, out sourceMatcher))
                    {
                        try
                        {
                            sourceMatcher = new("^(?:" + string.Join("|", expressions) + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25));
                        }
                        catch (ArgumentException)
                        {
                            sourceMatcher = new("(?!)");
                        }
                        sources.Add(src.Value, sourceMatcher);
                    }
                }
            }

            var dur = _duration.Match(rest);
            var duration = dur.Success && float.TryParse(dur.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0f;

            var active = rest.Split('#', 2)[0];
            var window = _window.Match(active);
            var before = 2.5f;
            var after = 2.5f;
            if (window.Success)
            {
                before = ReadNumber(window.Groups[1].Value);
                after = window.Groups[2].Success ? ReadNumber(window.Groups[2].Value) : before / 2;
                if (!window.Groups[2].Success)
                    before = after;
            }

            var jump = _jump.Match(active);
            var liveSync = sync.Success && sync.Index < active.Length && !sync.Value.StartsWith('#');
            var syncEvent = liveSync ? sync.Groups[1].Value : null;
            var fields = new Dictionary<string, Regex>(StringComparer.Ordinal);
            if (syncEvent is "InCombat" or "ActorControl" or "MapEffect")
            {
                foreach (Match field in _field.Matches(sync.Groups[2].Value))
                {
                    var expressions = _quoted.Matches(field.Groups[2].Value).Select(ReadString);
                    try
                    {
                        fields[field.Groups[1].Value] = new("^(?:" + string.Join("|", expressions) + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25));
                    }
                    catch (ArgumentException)
                    {
                        fields[field.Groups[1].Value] = new("(?!)");
                    }
                }
            }
            if (jump.Groups[2].Success)
                unresolvedJumps.Add(entries.Count, jump.Groups[2].Value);
            entries.Add(new(time, m.Groups[2].Value, abilities, source, onCastStart, duration)
            {
                IsCastSync = abilities.Length > 0 && syncEvent is "Ability" or "StartsUsing",
                SyncEvent = syncEvent,
                SyncFields = fields,
                SyncSource = sourceMatcher,
                WindowBefore = before,
                WindowAfter = after,
                JumpTo = jump.Groups[3].Success ? ReadNumber(jump.Groups[3].Value) : null,
                ForceJump = jump.Success && jump.Groups[1].Value == "forcejump"
            });
        }

        foreach (var (index, nameOfLabel) in unresolvedJumps)
            if (labels.TryGetValue(nameOfLabel, out var destination))
                entries[index] = entries[index] with { JumpTo = destination };

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
                ? ExpandCharacterClass(body)
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

    private static IEnumerable<string> ExpandCharacterClass(string body)
    {
        for (var i = 0; i < body.Length; ++i)
        {
            if (i + 2 < body.Length && body[i + 1] == '-' && body[i] <= body[i + 2])
            {
                for (var c = (int)body[i]; c <= body[i + 2]; ++c)
                    yield return ((char)c).ToString();
                i += 2;
            }
            else
                yield return body[i].ToString();
        }
    }
}
