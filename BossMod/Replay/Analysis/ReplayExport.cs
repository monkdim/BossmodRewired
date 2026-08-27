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
    public static string DataFileName(string logPath) => $"{Path.GetFileNameWithoutExtension(logPath)}.json";

    /// <summary>Everything worth reading about one recording, and a one-line description of what that was.</summary>
    public static (string Text, string Summary) Build(Replay replay) => Build(replay, null, null);

    public static (string Text, string Summary) Build(Replay replay, PositionExport? export) => Build(replay, export, null);

    /// <summary>
    /// The same, collecting the positions as data on the way past when asked.
    ///
    /// A recording with several bosses in it produces one data file rather than one per boss, since the samples
    /// carry the ability they belong to and a reader can split them far more easily than it could stitch
    /// several files back together.
    /// </summary>
    public static (string Text, string Summary) Build(Replay replay, PositionExport? export, LearnedPositions? learned)
    {
        var sb = new StringBuilder();

        if (replay.Encounters.Count == 0)
        {
            // Encounters only exist where a module activated, so content nobody has covered yet produces none.
            // That is the content most worth capturing, so it gets dumped wholesale rather than skipped.
            sb.Append(RecordingDump.Build(replay, export, learned));
            return (sb.ToString(), "no boss module for this duty, exported the whole recording");
        }

        var replays = new List<Replay> { replay };
        var oids = new HashSet<uint>();
        foreach (var enc in replay.Encounters)
        {
            if (oids.Add(enc.OID))
            {
                sb.Append(new EncounterDump(replays, enc.OID).BuildAll(export, learned));
                sb.AppendLine();
            }
        }

        return (sb.ToString(), $"{oids.Count} encounter(s)");
    }

    /// <summary>Writes the export next to the others and returns a line describing where it went.</summary>
    public static string Write(Replay replay)
    {
        var export = new PositionExport();
        var learned = new LearnedPositions();
        var (text, summary) = Build(replay, export, learned);
        var dir = EncounterDump.TargetDirectory();
        var target = Path.Combine(dir, FileName(replay.Path));
        File.WriteAllText(target, text);

        // The data file is written alongside rather than instead. The text is what a person reads; this is what
        // anything else reads, and a failure to write it must not cost the export that was already produced.
        try
        {
            File.WriteAllText(Path.Combine(dir, DataFileName(replay.Path)), export.Build());
            LearnedPositions.Merge(Path.Combine(dir, LearnedPositions.FileName), learned);

            // So the next pull uses what this export just learned, without restarting the game.
            MechanicTimersWindow.ForgetLearned();
        }
        catch (Exception e)
        {
            Service.Log($"[ReplayExport] positions written to text but not to data: {e.Message}");
        }

        return $"Exported {summary} to {target}";
    }
}
