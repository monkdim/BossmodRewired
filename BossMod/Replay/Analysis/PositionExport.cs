using System.Globalization;

namespace BossMod.ReplayAnalysis;

/// <summary>
/// The same analysis again, as data rather than as prose.
///
/// The text export is written to be read by a person, and that costs precision: a position becomes a distance
/// and one of eight compass points, which is a forty-five degree bucket. At twenty yards that is seven yards
/// of slop, which is fine for "stand north-east" and useless for anything that wants to compute with it.
///
/// This writes the positions themselves. Every sample the analysis took, in world coordinates, with the moment
/// it came from and who it belonged to, so that anything downstream can recompute what the text says and check
/// it, compare a recording against a strategy drawn somewhere else, or diff two nights of the same fight.
///
/// Written by hand rather than through a serializer, for one reason worth stating: every number here is a
/// coordinate, and a serializer that respects the machine's locale will write half of Europe a decimal comma
/// and quietly produce a file that parses into nonsense.
/// </summary>
sealed class PositionExport
{
    /// <summary>One player, one resolution of one ability, in world coordinates.</summary>
    public readonly record struct Row(uint Ability, string Who, DateTime When, double Elapsed, WPos Cast, WPos Hit, WPos Settled, WPos Caster);

    /// <summary>What the analysis concluded about an ability, so a reader need not re-derive it.</summary>
    public readonly record struct AbilityInfo(uint ID, string Name, string Shape, bool Positional, int Resolutions, bool Telegraphed, bool Marked, bool Landed);

    public string Boss = "";
    public uint OID;
    public ushort Zone;
    public string? Timeline;
    public WPos? ArenaCenter;
    public float ArenaScale;
    public string? ArenaShape;

    // Each pull names its own boss. A night's recording holds several, and one boss field at the top would be
    // whichever encounter happened to be written last.
    public readonly List<(int Index, uint OID, string Boss, DateTime From, DateTime To)> Pulls = [];
    public readonly List<Row> Rows = [];
    public readonly List<AbilityInfo> Abilities = [];

    private readonly HashSet<uint> _described = [];

    public void Add(Row row) => Rows.Add(row);

    /// <summary>
    /// Records what an ability turned out to be, once.
    ///
    /// The analysis runs per fight and per pull, so the same ability is described again every time it appears.
    /// This is a lookup table rather than a log, and a reader indexing it by ID should not have to decide which
    /// of five identical entries to believe.
    /// </summary>
    public void Describe(AbilityInfo info)
    {
        if (_described.Add(info.ID))
        {
            Abilities.Add(info);
        }
    }

    private static string N(float v) => v.ToString("f3", CultureInfo.InvariantCulture);
    private static string N(double v) => v.ToString("f3", CultureInfo.InvariantCulture);
    private static string Pos(WPos p) => $"[{N(p.X)},{N(p.Z)}]";
    private static string T(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Escapes what JSON requires escaping. Ability and player names are the only free text here.</summary>
    private static string Str(string? s)
    {
        if (s == null)
        {
            return "null";
        }

        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    public string Build()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");

        // Bumped whenever a field changes meaning, so a reader can refuse a file it would misread rather than
        // silently interpreting an old one under new rules.
        sb.Append("  \"schema\": 1,\n");
        sb.Append("  \"boss\": ").Append(Str(Boss)).Append(",\n");
        sb.Append("  \"oid\": ").Append(OID.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("  \"zone\": ").Append(Zone.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        sb.Append("  \"timeline\": ").Append(Str(Timeline)).Append(",\n");

        sb.Append("  \"arena\": ");
        if (ArenaCenter is WPos c)
        {
            sb.Append("{\"center\": ").Append(Pos(c))
              .Append(", \"scale\": ").Append(N(ArenaScale))
              .Append(", \"shape\": ").Append(Str(ArenaShape)).Append("},\n");
        }
        else
        {
            sb.Append("null,\n");
        }

        sb.Append("  \"pulls\": [\n");
        for (var i = 0; i < Pulls.Count; ++i)
        {
            var (index, oid, boss, from, to) = Pulls[i];
            sb.Append("    {\"index\": ").Append(index.ToString(CultureInfo.InvariantCulture))
              .Append(", \"oid\": ").Append(oid.ToString(CultureInfo.InvariantCulture))
              .Append(", \"boss\": ").Append(Str(boss))
              .Append(", \"from\": ").Append(Str(T(from)))
              .Append(", \"to\": ").Append(Str(T(to)))
              .Append(", \"duration\": ").Append(N((to - from).TotalSeconds)).Append('}')
              .Append(i + 1 < Pulls.Count ? ",\n" : "\n");
        }

        sb.Append("  ],\n");

        sb.Append("  \"abilities\": [\n");
        for (var i = 0; i < Abilities.Count; ++i)
        {
            var a = Abilities[i];
            sb.Append("    {\"id\": ").Append(a.ID.ToString(CultureInfo.InvariantCulture))
              .Append(", \"name\": ").Append(Str(a.Name))
              .Append(", \"shape\": ").Append(Str(a.Shape))
              .Append(", \"positional\": ").Append(a.Positional ? "true" : "false")
              .Append(", \"resolutions\": ").Append(a.Resolutions.ToString(CultureInfo.InvariantCulture))
              .Append(", \"telegraphed\": ").Append(a.Telegraphed ? "true" : "false")
              .Append(", \"marked\": ").Append(a.Marked ? "true" : "false")
              .Append(", \"landed\": ").Append(a.Landed ? "true" : "false")
              .Append('}')
              .Append(i + 1 < Abilities.Count ? ",\n" : "\n");
        }

        sb.Append("  ],\n");

        // The samples themselves, one line each. Flat rather than nested under abilities, because every reader
        // of this file wants to group it a different way and a flat list is the one shape none of them has to
        // undo first.
        sb.Append("  \"samples\": [\n");
        for (var i = 0; i < Rows.Count; ++i)
        {
            var r = Rows[i];
            sb.Append("    {\"ability\": ").Append(r.Ability.ToString(CultureInfo.InvariantCulture))
              .Append(", \"who\": ").Append(Str(r.Who))
              .Append(", \"at\": ").Append(Str(T(r.When)))
              .Append(", \"t\": ").Append(N(r.Elapsed))
              .Append(", \"cast\": ").Append(Pos(r.Cast))
              .Append(", \"hit\": ").Append(Pos(r.Hit))
              .Append(", \"settled\": ").Append(Pos(r.Settled))
              .Append(", \"caster\": ").Append(Pos(r.Caster))
              .Append('}')
              .Append(i + 1 < Rows.Count ? ",\n" : "\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }
}
