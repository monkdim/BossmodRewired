using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace BossMod;

/// <summary>
/// Appears after a fight that did something its module has never seen, so a recording worth going back to
/// announces itself instead of being found weeks later.
/// </summary>
[SkipLocalsInit]
public sealed class MechanicDivergenceWindow : UIWindow
{
    private readonly MechanicDivergenceTracker _tracker;

    public MechanicDivergenceWindow(MechanicDivergenceTracker tracker) : base("Unknown mechanics", false, new(480f, 260f))
    {
        _tracker = tracker;
    }

    public override void PreOpenCheck()
        => IsOpen = _tracker.HasUnacknowledged && Service.Config.Get<MechanicDivergenceConfig>().ShowWindow;

    public override void Draw()
    {
        var findings = _tracker.LastFindings;

        ImGui.PushStyleColor(ImGuiCol.Text, Colors.Danger);
        ImGui.TextUnformatted($"{_tracker.LastModuleName} did {findings.Count} thing(s) the module does not know about.");
        ImGui.PopStyleColor();

        ImGui.TextWrapped("This pull is worth keeping. The replay is still being written until you leave the duty, so export it from the replay analysis window once you are out.");
        ImGui.Separator();

        using (var table = ImRaii.Table("divergence", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableSetupColumn("What happened", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                for (var i = 0; i < findings.Count; ++i)
                {
                    var f = findings[i];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(f.Kind);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(f.ID.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(f.Description);
                }
            }
        }

        ImGui.Separator();

        if (ImGui.Button("Copy list"))
        {
            var sb = new StringBuilder();
            sb.Append("// unknown mechanics in ").AppendLine(_tracker.LastModuleName);
            for (var i = 0; i < findings.Count; ++i)
            {
                var f = findings[i];
                sb.Append("//   ").Append(f.Kind.PadRight(12)).Append(f.ID.ToString().PadRight(8)).AppendLine(f.Description);
            }

            ImGui.SetClipboardText(sb.ToString());
        }

        ImGui.SameLine();
        if (ImGui.Button("Dismiss"))
        {
            _tracker.Acknowledge();
        }
    }
}
