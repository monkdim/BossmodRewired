using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BossMod;

/// <summary>
/// Where each slot stood, learned from recordings and kept for the next pull.
///
/// The analysis has always been able to work out where a slot should be, and has always written it into a
/// report nobody can read while a mechanic is landing on them. This is the same answer in a form the plugin can
/// hold: keyed on the ability, so the timer window can look up what is coming and say where to go.
///
/// Only positions that were actually held get in here. A mechanic where people wandered has nothing to teach
/// and would be worse than silence, because a hint shown during a fight is acted on rather than weighed.
/// </summary>
public sealed class LearnedPositions
{
    public const string FileName = "learned-positions.json";

    /// <summary>One slot's place for one ability, measured the way the export measures it.</summary>
    public readonly record struct Spot(float Fraction, float Bearing, float FromCaster, int Samples, int Casts, float Spread, bool Avoided)
    {
        /// <summary>The compass point, named as the reports name it.</summary>
        public string Where => Fraction < 0.06f ? "middle" : $"{Compass(Bearing)} {Fraction:f2}r";

        /// <summary>
        /// Whether this is worth showing during a fight at all.
        ///
        /// One observation is where somebody happened to be, and the reports say exactly that: "the only
        /// cast, so this is where they were rather than where to be". A hint on a bar cannot carry that
        /// sentence, and is acted on rather than weighed, so the live path has to be stricter than the report
        /// rather than looser. Measured on a real export, twenty of twenty-one learned spots came from a
        /// single cast, so without this the window would have been almost entirely guesses.
        ///
        /// The file still keeps them. A second run of the same duty turns a guess into evidence, and that is
        /// the behaviour worth encouraging.
        /// </summary>
        public bool Worth => Samples > 1;

        /// <summary>How much this deserves to be believed, so a thin reading can be shown as one.</summary>
        public bool Confident => Samples > 2 && Spread < 1.5f;
    }

    private static readonly string[] Points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    public static string Compass(float bearing) => Points[(int)MathF.Round(((bearing % 360f) + 360f) % 360f / 45f) % 8];

    /// <summary>
    /// What a position gets filed under.
    ///
    /// An assigned slot is the better answer and wins whenever there is one, because "OT" is a specific person
    /// with a specific job to do and the position that follows from it. But a slot is only assigned by somebody
    /// who went and configured it, which in practice means a static. Every duty finder party, every alliance
    /// raid, every piece of levelling content has eight or twenty-four people the config has never seen.
    ///
    /// So the job answers when the config does not. It is coarser, and it is available for everybody without
    /// anybody doing anything, which is the difference between learning from one player in a full alliance and
    /// learning from all of them. Casters and physical ranged are kept apart rather than lumped as "Ranged",
    /// since where they stand is one of the things they do not share.
    /// </summary>
    public static string SlotOf(Class cls, ulong contentID)
    {
        var role = Service.Config.Get<PartyRolesConfig>()[contentID];
        return role != PartyRolesConfig.Assignment.Unassigned ? role.ToString() : SlotOf(cls);
    }

    /// <summary>The job's own answer, with no configuration behind it.</summary>
    public static string SlotOf(Class cls) => cls.GetClassCategory() switch
    {
        ClassCategory.Tank => "Tank",
        ClassCategory.Healer => "Healer",
        ClassCategory.Melee => "Melee",
        ClassCategory.PhysRanged => "PhysRanged",
        ClassCategory.Caster => "Caster",
        _ => ""
    };

    // ability -> slot -> where that slot stood. Abilities rather than mechanic names, because an ID is the one
    // thing cactbot, the game and this analysis all agree on without a translation table. Slots are strings
    // rather than the roles enum because half of them are jobs, and a file anybody may read is better off
    // saying "Healer" than carrying a second enum nobody outside this build can resolve.
    private readonly Dictionary<uint, Dictionary<string, Spot>> _spots = [];

    public int Count => _spots.Count;

    public IEnumerable<(uint Ability, string Slot, Spot Spot)> All()
    {
        foreach (var (ability, bySlot) in _spots)
        {
            foreach (var (slot, spot) in bySlot)
            {
                yield return (ability, slot, spot);
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
        foreach (var (ability, slot, spot) in fresh.All())
        {
            all.Learn(ability, slot, spot);
        }

        File.WriteAllText(path, all.Build());
    }

    public Spot? For(uint ability, string slot)
        => slot.Length > 0 && _spots.TryGetValue(ability, out var bySlot) && bySlot.TryGetValue(slot, out var spot) ? spot : null;

    /// <summary>
    /// Remembers a spot, keeping the better-supported of the two when one is already known.
    ///
    /// Exporting the same fight twice must not make the answer worse, and a night with more pulls in it should
    /// win over a night with fewer.
    /// </summary>
    public void Learn(uint ability, string slot, Spot spot)
    {
        if (slot.Length == 0)
        {
            return;
        }

        var bySlot = _spots.GetOrAdd(ability);
        if (!bySlot.TryGetValue(slot, out var prev) || Better(spot, prev))
        {
            bySlot[slot] = spot;
        }
    }

    // More casts behind it wins; the same number of casts, tighter wins.
    private static bool Better(Spot a, Spot b) => a.Samples != b.Samples ? a.Samples > b.Samples : a.Spread < b.Spread;

    public string Build()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"schema\": 1,\n  \"abilities\": {\n");
        var first = true;
        foreach (var (ability, bySlot) in _spots)
        {
            if (!first)
            {
                sb.Append(",\n");
            }

            first = false;
            sb.Append("    \"").Append(ability.ToString(CultureInfo.InvariantCulture)).Append("\": {");
            var firstSlot = true;
            foreach (var (slot, s) in bySlot)
            {
                if (!firstSlot)
                {
                    sb.Append(", ");
                }

                firstSlot = false;
                sb.Append('"').Append(slot).Append("\": [")
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
            foreach (var (ability, slot, spot) in Parse(text))
            {
                res.Learn(ability, slot, spot);
            }
        }
        catch (Exception e)
        {
            Service.Log($"[LearnedPositions] could not read {path}: {e.Message}");
        }

        return res;
    }

    /// <summary>
    /// Reads the shape Build writes, with a real parser.
    ///
    /// The first version scanned for quote pairs by hand and recovered nothing at all from its own output: it
    /// walked the gaps between the keys rather than the keys. Load silently returned empty, so Merge had
    /// nothing to merge into and every export overwrote the file with only its own findings.
    ///
    /// Writing still goes through StringBuilder, because a serializer honouring the machine's locale would
    /// write coordinates with a decimal comma. Reading has no such hazard: JSON numbers are invariant by
    /// specification, so a real parser is both safer and shorter than being clever here.
    /// </summary>
    private static IEnumerable<(uint Ability, string Slot, Spot Spot)> Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("abilities", out var abilities) || abilities.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var byAbility in abilities.EnumerateObject())
        {
            if (!uint.TryParse(byAbility.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ability) || byAbility.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var bySlot in byAbility.Value.EnumerateObject())
            {
                if (bySlot.Name.Length == 0 || bySlot.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var n = new List<float>(7);
                foreach (var v in bySlot.Value.EnumerateArray())
                {
                    n.Add(v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) ? f : 0f);
                }

                if (n.Count >= 7)
                {
                    yield return (ability, bySlot.Name, new(n[0], n[1], n[2], (int)n[3], (int)n[4], n[5], n[6] > 0.5f));
                }
            }
        }
    }
}
