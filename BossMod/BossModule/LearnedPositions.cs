using System.Globalization;
using System.IO;

namespace BossMod;

/// <summary>
/// Where each role stood, learned from recordings and kept for the next pull.
///
/// The analysis has always been able to work out where a role should be, and has always written it into a
/// report nobody can read while a mechanic is landing on them. This is the same answer in a form the plugin can
/// hold: keyed on the ability, so the timer window can look up what is coming and say where to go.
///
/// Only positions that were actually held get in here. A mechanic where people wandered has nothing to teach
/// and would be worse than silence, because a hint shown during a fight is acted on rather than weighed.
/// </summary>
public sealed class LearnedPositions
{
    public const string FileName = "learned-positions.json";

    /// <summary>One role's place for one ability, measured the way the export measures it.</summary>
    public readonly record struct Spot(float Fraction, float Bearing, float FromCaster, int Samples, int Casts, float Spread, bool Avoided)
    {
        /// <summary>The compass point, named as the reports name it.</summary>
        public string Where => Fraction < 0.06f ? "middle" : $"{Compass(Bearing)} {Fraction:f2}r";

        /// <summary>How much this deserves to be believed, so a thin reading can be shown as one.</summary>
        public bool Confident => Samples > 2 && Spread < 1.5f;
    }

    private static readonly string[] Points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    public static string Compass(float bearing) => Points[(int)MathF.Round(((bearing % 360f) + 360f) % 360f / 45f) % 8];

    // ability -> role -> where that role stood. Abilities rather than mechanic names, because an ID is the one
    // thing cactbot, the game and this analysis all agree on without a translation table.
    private readonly Dictionary<uint, Dictionary<PartyRolesConfig.Assignment, Spot>> _spots = [];

    public int Count => _spots.Count;

    public IEnumerable<(uint Ability, PartyRolesConfig.Assignment Role, Spot Spot)> All()
    {
        foreach (var (ability, byRole) in _spots)
        {
            foreach (var (role, spot) in byRole)
            {
                yield return (ability, role, spot);
            }
        }
    }

    /// <summary>
    /// Folds a night's findings into what was already known and writes the result.
    ///
    /// Reading before writing is the point: the file accumulates across every fight anybody exports, so a
    /// single-boss export must never replace the rest of what has been learned.
    /// </summary>
    public static void Merge(string path, LearnedPositions fresh)
    {
        var all = Load(path);
        foreach (var (ability, role, spot) in fresh.All())
        {
            all.Learn(ability, role, spot);
        }

        File.WriteAllText(path, all.Build());
    }

    public Spot? For(uint ability, PartyRolesConfig.Assignment role)
        => _spots.TryGetValue(ability, out var byRole) && byRole.TryGetValue(role, out var spot) ? spot : null;

    /// <summary>
    /// Remembers a spot, keeping the better-supported of the two when one is already known.
    ///
    /// Exporting the same fight twice must not make the answer worse, and a night with more pulls in it should
    /// win over a night with fewer.
    /// </summary>
    public void Learn(uint ability, PartyRolesConfig.Assignment role, Spot spot)
    {
        if (role == PartyRolesConfig.Assignment.Unassigned)
        {
            return;
        }

        var byRole = _spots.GetOrAdd(ability);
        if (!byRole.TryGetValue(role, out var prev) || Better(spot, prev))
        {
            byRole[role] = spot;
        }
    }

    // More casts behind it wins; the same number of casts, tighter wins.
    private static bool Better(Spot a, Spot b) => a.Samples != b.Samples ? a.Samples > b.Samples : a.Spread < b.Spread;

    public string Build()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"schema\": 1,\n  \"abilities\": {\n");
        var first = true;
        foreach (var (ability, byRole) in _spots)
        {
            if (!first)
            {
                sb.Append(",\n");
            }

            first = false;
            sb.Append("    \"").Append(ability.ToString(CultureInfo.InvariantCulture)).Append("\": {");
            var firstRole = true;
            foreach (var (role, s) in byRole)
            {
                if (!firstRole)
                {
                    sb.Append(", ");
                }

                firstRole = false;
                sb.Append('"').Append(role.ToString()).Append("\": [")
                  .Append(F(s.Fraction)).Append(',').Append(F(s.Bearing)).Append(',').Append(F(s.FromCaster)).Append(',')
                  .Append(s.Samples.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Casts.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(F(s.Spread)).Append(',').Append(s.Avoided ? '1' : '0').Append(']');
            }

            sb.Append('}');
        }

        return sb.Append("\n  }\n}\n").ToString();
    }

    // Coordinates again, so the machine's idea of a decimal separator stays out of it.
    private static string F(float v) => v.ToString("f3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the file back, tolerating everything.
    ///
    /// This runs while the game is starting and feeds a window that draws every frame. A malformed file, a file
    /// from a newer build, a half-written file: none of them are worth taking the plugin down for, and an empty
    /// set of hints is a perfectly good answer.
    /// </summary>
    public static LearnedPositions Load(string path)
    {
        var res = new LearnedPositions();
        try
        {
            if (!File.Exists(path))
            {
                return res;
            }

            var text = File.ReadAllText(path);
            foreach (var (ability, role, spot) in Parse(text))
            {
                res.Learn(ability, role, spot);
            }
        }
        catch (Exception e)
        {
            Service.Log($"[LearnedPositions] could not read {path}: {e.Message}");
        }

        return res;
    }

    /// <summary>
    /// A reader for exactly the shape Build writes, rather than a general one.
    ///
    /// The file is ours at both ends and its shape is fixed, so a scanner that looks for the two things it
    /// needs is smaller than a parser and cannot be surprised by anything except a file it should ignore.
    /// </summary>
    private static IEnumerable<(uint Ability, PartyRolesConfig.Assignment Role, Spot Spot)> Parse(string text)
    {
        var i = text.IndexOf("\"abilities\"", StringComparison.Ordinal);
        if (i < 0)
        {
            yield break;
        }

        while (true)
        {
            var open = text.IndexOf('"', i + 1);
            if (open < 0)
            {
                yield break;
            }

            var close = text.IndexOf('"', open + 1);
            if (close < 0)
            {
                yield break;
            }

            var key = text[(open + 1)..close];
            i = close + 1;
            if (!uint.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ability))
            {
                continue;
            }

            var braceEnd = text.IndexOf('}', i);
            if (braceEnd < 0)
            {
                yield break;
            }

            var body = text[i..braceEnd];
            foreach (var part in body.Split(']'))
            {
                var q1 = part.IndexOf('"');
                var q2 = q1 < 0 ? -1 : part.IndexOf('"', q1 + 1);
                var br = part.IndexOf('[');
                if (q1 < 0 || q2 < 0 || br < 0)
                {
                    continue;
                }

                if (!Enum.TryParse<PartyRolesConfig.Assignment>(part[(q1 + 1)..q2], out var role))
                {
                    continue;
                }

                var nums = part[(br + 1)..].Split(',');
                if (nums.Length < 7)
                {
                    continue;
                }

                yield return (ability, role, new(P(nums[0]), P(nums[1]), P(nums[2]), (int)P(nums[3]), (int)P(nums[4]), P(nums[5]), P(nums[6]) > 0.5f));
            }

            i = braceEnd + 1;
        }
    }

    private static float P(string s) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
}
