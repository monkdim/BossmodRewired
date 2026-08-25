using Dalamud.Bindings.ImGui;
using System.IO;
using System.Threading;

namespace BossMod;

/// <summary>
/// Offers a one-click text export of everything that happened, as soon as a duty's recording is closed.
///
/// The alternative is the replay analysis window, which means picking a file, loading it, finding the right
/// encounter and expanding two nodes. That is fine for someone digging through a back catalogue and far too
/// much for someone who just finished a duty and wants to hand the result to somebody else.
/// </summary>
[SkipLocalsInit]
public sealed class DutyExportWindow : UIWindow
{
    private static DutyExportConfig Config => Service.Config.Get<DutyExportConfig>();

    private readonly MechanicDivergenceTracker _divergence;
    private readonly EventSubscription _subscription;

    private string? _pendingLog;
    private string _dutyName = "";
    private bool _wasDivergent;

    private Task<string>? _export;
    private CancellationTokenSource? _cancel;
    private float _progress;
    private string _result = "";
    private bool _resultAnnounced;

    public DutyExportWindow(ReplayManagementWindow recording, MechanicDivergenceTracker divergence)
        : base("Duty recorded", false, new(460f, 180f))
    {
        _divergence = divergence;
        _subscription = recording.RecordingFinished.Subscribe(OnRecordingFinished);
    }

    protected override void Dispose(bool disposing)
    {
        _subscription.Dispose();
        _cancel?.Cancel();
        _cancel?.Dispose();
        base.Dispose(disposing);
    }

    private void OnRecordingFinished(string logPath)
    {
        var config = Config;
        if (!config.ShowPrompt && !config.AutoExport)
        {
            return;
        }

        _wasDivergent = _divergence.HasUnacknowledged;
        if (config.OnlyWhenDivergent && !_wasDivergent)
        {
            return;
        }

        _pendingLog = logPath;
        _dutyName = Path.GetFileNameWithoutExtension(logPath);
        _result = "";
        _resultAnnounced = false;

        if (config.AutoExport)
        {
            Begin();
        }
    }

    public override void PreOpenCheck() => IsOpen = _pendingLog != null && (Config.ShowPrompt || _export != null);

    public override void Draw()
    {
        // Parsing happens on a worker thread, so the result is picked up here rather than reported from it.
        if (_export != null && _export.IsCompleted)
        {
            _result = _export.IsFaulted ? $"Export failed: {_export.Exception?.InnerException?.Message}" : _export.Result;
            _export = null;
            _cancel?.Dispose();
            _cancel = null;
        }

        if (_result.Length > 0 && !_resultAnnounced)
        {
            Service.ChatGui.Print($"[BMR] {_result}");
            _resultAnnounced = true;
        }

        ImGui.TextUnformatted(_dutyName);

        if (_wasDivergent)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Danger);
            ImGui.TextWrapped("This duty did something its module does not know about, so it is worth keeping.");
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        if (_export != null)
        {
            ImGui.TextUnformatted("Reading the recording...");
            ImGui.ProgressBar(_progress, new Vector2(-1f, 22f));
            return;
        }

        if (_result.Length > 0)
        {
            ImGui.TextWrapped(_result);
            if (ImGui.Button("Close"))
            {
                Reset();
            }

            return;
        }

        ImGui.TextWrapped($"Export every encounter in this duty as a text file to {ReplayAnalysis.EncounterDump.TargetDirectory()}?");

        if (ImGui.Button("Export to Downloads"))
        {
            Begin();
        }

        ImGui.SameLine();
        if (ImGui.Button("Not this one"))
        {
            Reset();
        }
    }

    private void Reset()
    {
        _pendingLog = null;
        _result = "";
        _wasDivergent = false;
    }

    private void Begin()
    {
        var path = _pendingLog;
        if (path == null || _export != null)
        {
            return;
        }

        _progress = 0f;
        _cancel = new();
        var token = _cancel.Token;
        _export = Task.Run(() => Run(path, token));
    }

    /// <summary>
    /// Reparses the finished log and writes one text file covering every encounter it contains. Reparsing
    /// rather than reusing the live world state is deliberate: it is the same path the analysis window takes,
    /// so the prompt cannot produce a different answer from the tooling it is a shortcut for.
    /// </summary>
    private string Run(string logPath, CancellationToken token)
    {
        var replay = ReplayParserLog.Parse(logPath, ref _progress, token);

        var sb = new StringBuilder();
        var oids = new HashSet<uint>();
        string summary;

        if (replay.Encounters.Count == 0)
        {
            // Encounters only exist where a module activated, so a duty nobody has covered yet produces none.
            // That is the content most worth capturing, so it falls back to dumping the recording wholesale
            // rather than writing an empty file and calling it done.
            sb.Append(ReplayAnalysis.RecordingDump.Build(replay));
            summary = "no boss module for this duty, exported the whole recording";
        }
        else
        {
            var replays = new List<Replay> { replay };
            foreach (var enc in replay.Encounters)
            {
                if (oids.Add(enc.OID))
                {
                    sb.Append(new ReplayAnalysis.EncounterDump(replays, enc.OID).BuildAll());
                    sb.AppendLine();
                }
            }

            summary = $"{oids.Count} encounter(s)";
        }

        var name = $"{Path.GetFileNameWithoutExtension(logPath)}.txt";
        var target = Path.Combine(ReplayAnalysis.EncounterDump.TargetDirectory(), name);
        File.WriteAllText(target, sb.ToString());

        return $"Exported {summary} to {target}";
    }
}
