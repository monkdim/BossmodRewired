using System.IO;

namespace BossMod.ReplayAnalysis;

/// <summary>
/// Turns a parsed replay into the text export, whether or not it contains encounters, and writes it out.
///
/// Both the prompt that appears after a duty and the analysis window need to do exactly this. They used to do
/// it separately, and the analysis window only offered the encounter half, so a recording with no module had
/// no export button anywhere despite the fallback existing.
/// </summary>
static class ReplayExport
{
    public static string FileName(string logPath) => $"{Path.GetFileNameWithoutExtension(logPath)}.txt";

    /// <summary>Everything worth reading about one recording, and a one-line description of what that was.</summary>
    public static (string Text, string Summary) Build(Replay replay)
    {
        var sb = new StringBuilder();

        if (replay.Encounters.Count == 0)
        {
            // Encounters only exist where a module activated, so content nobody has covered yet produces none.
            // That is the content most worth capturing, so it gets dumped wholesale rather than skipped.
            sb.Append(RecordingDump.Build(replay));
            return (sb.ToString(), "no boss module for this duty, exported the whole recording");
        }

        var replays = new List<Replay> { replay };
        var oids = new HashSet<uint>();
        foreach (var enc in replay.Encounters)
        {
            if (oids.Add(enc.OID))
            {
                sb.Append(new EncounterDump(replays, enc.OID).BuildAll());
                sb.AppendLine();
            }
        }

        return (sb.ToString(), $"{oids.Count} encounter(s)");
    }

    /// <summary>Writes the export next to the others and returns a line describing where it went.</summary>
    public static string Write(Replay replay)
    {
        var (text, summary) = Build(replay);
        var target = Path.Combine(EncounterDump.TargetDirectory(), FileName(replay.Path));
        File.WriteAllText(target, text);
        return $"Exported {summary} to {target}";
    }
}
