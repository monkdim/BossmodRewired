using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.IO;
using System.Threading;

namespace BossMod;

/// <summary>
/// Exports every recording in the log folder in one pass.
///
/// The per-duty prompt covers a duty as it finishes and the analysis window covers recordings already loaded
/// into it, which leaves the case of a folder that has been quietly filling up for a week with nothing to
/// point at it. Somebody capturing on another person's behalf has exactly that folder and no reason to open
/// an analysis window at all.
///
/// Parsing runs on a worker thread and reports back through plain fields. A recording is a few hundred
/// thousand operations and a folder of them takes minutes, so doing this on the UI thread would freeze the
/// game for the whole run.
/// </summary>
public sealed class BulkExport : IDisposable
{
    private Task? _task;
    private CancellationTokenSource? _cancel;

    private int _done;
    private int _total;
    private int _failed;
    private int _skipped;
    private string _current = "";
    private string _result = "";
    private float _fileProgress;

    private bool _skipExisting = true;

    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
    }

    /// <summary>The button itself, drawn inline with the window's other controls.</summary>
    public void DrawButton(DirectoryInfo? logDir)
    {
        using var disabled = ImRaii.Disabled(_task != null || logDir == null);
        if (ImGui.Button("Export every recording") && logDir != null)
        {
            Start(logDir);
        }
    }

    /// <summary>Progress and results, which need their own lines and so cannot live with the button.</summary>
    public void DrawProgress()
    {
        // The worker cannot touch ImGui, so completion is noticed here instead of reported from it.
        if (_task != null && _task.IsCompleted)
        {
            _result = _task.IsFaulted
                ? $"Export failed: {_task.Exception?.InnerException?.Message}"
                : Describe();
            _task = null;
            _cancel?.Dispose();
            _cancel = null;
            Service.ChatGui.Print($"[BMR] {_result}");
        }

        if (_task != null)
        {
            ImGui.TextUnformatted($"Exporting {Math.Min(_done + 1, _total)} of {_total}: {_current}");

            // Two bars because the two take wildly different times: a short dungeon is seconds and a two hour
            // ultimate session is not, so overall progress alone looks stuck for minutes at a time.
            ImGui.ProgressBar(_total > 0 ? (float)_done / _total : 0f, new Vector2(-1f, 22f));
            ImGui.ProgressBar(_fileProgress, new Vector2(-1f, 10f));

            if (ImGui.Button("Stop"))
            {
                _cancel?.Cancel();
            }

            return;
        }

        // Deliberately not persisted: which recordings are worth redoing changes with every update to the
        // export, so the useful default is "only the new ones" every time the window is opened fresh.
        ImGui.Checkbox("Skip recordings already exported", ref _skipExisting);

        if (_result.Length > 0)
        {
            ImGui.TextUnformatted(_result);
        }
    }

    private string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("Exported ").Append(_done - _failed - _skipped).Append(" of ").Append(_total);

        if (_skipped > 0)
        {
            sb.Append(", skipped ").Append(_skipped).Append(" already written");
        }

        if (_failed > 0)
        {
            sb.Append(", ").Append(_failed).Append(" failed, see the log");
        }

        return sb.Append(" to ").Append(ReplayAnalysis.EncounterDump.TargetDirectory()).ToString();
    }

    private void Start(DirectoryInfo logDir)
    {
        if (_task != null)
        {
            return;
        }

        _done = _failed = _skipped = 0;
        _total = 0;
        _current = "";
        _result = "";
        _fileProgress = 0f;
        _cancel = new();

        var token = _cancel.Token;
        var skipExisting = _skipExisting;
        _task = Task.Run(() => Run(logDir, skipExisting, token));
    }

    private void Run(DirectoryInfo logDir, bool skipExisting, CancellationToken token)
    {
        List<FileInfo> logs;
        try
        {
            logs = [.. logDir.EnumerateFiles("*.log", new EnumerationOptions { RecurseSubdirectories = true })];
        }
        catch (Exception e)
        {
            Service.Log($"[export] could not read {logDir.FullName}: {e.Message}");
            return;
        }

        // Oldest first, so a run stopped halfway has covered the backlog rather than a random slice of it.
        logs.Sort(static (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
        _total = logs.Count;

        var target = ReplayAnalysis.EncounterDump.TargetDirectory();

        foreach (var log in logs)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            _current = log.Name;
            _fileProgress = 0f;

            try
            {
                if (skipExisting && File.Exists(Path.Combine(target, ReplayAnalysis.ReplayExport.FileName(log.FullName))))
                {
                    ++_skipped;
                }
                else
                {
                    ReplayAnalysis.ReplayExport.Write(ReplayParserLog.Parse(log.FullName, ref _fileProgress, token));
                }
            }
            catch (Exception e)
            {
                // One truncated recording, and there is always one, should not end the run.
                Service.Log($"[export] could not export {log.Name}: {e.Message}");
                ++_failed;
            }

            ++_done;
        }
    }
}
