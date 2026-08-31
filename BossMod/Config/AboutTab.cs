using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Diagnostics;
using System.IO;

namespace BossMod;

public sealed class AboutTab(DirectoryInfo? replayDir)
{
    private static readonly Color TitleColor = Color.FromComponents(255u, 165u, default);
    private static readonly Color SectionBgColor = Color.FromComponents(38u, 38u, 38u);
    private static readonly Color BorderColor = Color.FromComponents(178u, 178u, 178u, 204u);

    private string _lastErrorMessage = "";

    public void Draw()
    {
        using var wrap = ImRaii.TextWrapPos(0);

        ImGui.TextUnformatted("BossMod Rewired provides a boss fight radar, learned positions for each role, mechanic timers, cooldown planning, autorotation and AI. Every part of it can be turned off on its own.");
        ImGui.TextUnformatted("It is a fork of BossMod Reborn, itself a fork of awgil's BossMod. Problems with positions, recording, exports or sharing belong on this fork's GitHub, or in the Feedback tab. Problems with an encounter module are usually upstream's and are best reported there.");
        ImGui.TextUnformatted("Do not run this alongside BossMod Reborn or the original BossMod. Two of them detour the same game functions and the game does not survive it.");
        ImGui.Spacing();
        DrawSection("Radar",
        [
            "Provides an on-screen window that contains an area mini-map showing player positions, boss position(s), various imminent AOEs, and other mechanics.",
            "Useful because you don't have to remember what ability names mean.",
            "See exactly whether you're getting clipped by incoming AOEs or not.",
            "Enabled for supported bosses, visible in the \"Supported bosses\" tab.",
        ]);
        ImGui.Spacing();
        DrawSection("Autorotation",
        [
            "Executes fully optimal rotations to the best of its ability.",
            "Go to the \"Autorotation presets\" tab to create a preset.",
            "Maturity of each rotation module is present in a tooltip.",
            "Guide for using this feature can be found on the wiki.",
        ]);
        ImGui.Spacing();
        DrawSection("Cooldown planner",
        [
            "Creates a CD plan for supported bosses.",
            "Replaces autorotations in specific fights.",
            "Allows you to time specific abilities to cast at specific times.",
            "Guide for using this feature can be found on the wiki.",
        ]);
        ImGui.Spacing();
        DrawSection("AI",
        [
            "Automates movement during boss fights.",
            "Automatically moves your character based on safe zones determined by a boss's module, visible on the radar.",
            "Should not be used in when playing with unknown players.",
            "Can be hooked by other plugins to automate entire duties.",
        ]);
        ImGui.Spacing();
        DrawSection("Replays",
        [
            "Useful for creating boss modules, analyzing problems with them, and making CD plans.",
            "When asking for help, make sure to provide a replay! Please note that replays will contain your player name!",
            "Enabled in Settings > Show replay management UI (or enable auto recording).",
            $"Files are located in '{replayDir}'.",
        ]);
        ImGui.Spacing();
        ImGui.Spacing();

        // This fork first, then the two projects it is built on. The order used to be the other way round,
        // which sent anybody looking for help here to somebody else's issue tracker.
        if (ImGui.Button("BossMod Rewired GitHub", new(220, 0)))
        {
            _lastErrorMessage = OpenLink("https://github.com/monkdim/BossmodRewired");
        }

        ImGui.SameLine();
        if (ImGui.Button("BossMod Wiki", new(130, 0)))
        {
            _lastErrorMessage = OpenLink("https://github.com/awgil/ffxiv_bossmod/wiki");
        }

        ImGui.SameLine();
        if (ImGui.Button("Open replay folder", new(180, 0)) && replayDir != null)
        {
            _lastErrorMessage = OpenDirectory(replayDir);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("A fork of BossMod Reborn, itself a fork of awgil's BossMod. Encounter modules are largely");
        ImGui.TextDisabled("their work; module bugs are worth reporting upstream so everybody gets the fix.");

        if (_lastErrorMessage.Length > 0)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, Colors.TextColor3);
            ImGui.TextUnformatted(_lastErrorMessage);
        }
    }

    private static void DrawSection(string title, string[] bulletPoints)
    {
        using var colorBackground = ImRaii.PushColor(ImGuiCol.ChildBg, SectionBgColor.ABGR);
        using var colorBorder = ImRaii.PushColor(ImGuiCol.Border, BorderColor.ABGR);
        var height = ImGui.GetTextLineHeightWithSpacing() * (bulletPoints.Length + 2);
        using var section = ImRaii.Child(title, new(0, height), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding);

        if (!section)
        {
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, TitleColor.ABGR))
        {
            ImGui.TextUnformatted(title);
        }

        ImGui.Separator();
        ImGui.PushTextWrapPos();
        foreach (var point in bulletPoints)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextUnformatted(point);
        }
        ImGui.PopTextWrapPos();
    }

    private static string OpenLink(string link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            return "";
        }
        catch (Exception e)
        {
            Service.Log($"Error opening link {link}: {e}");
            return $"Failed to open link '{link}', open it manually in the browser.";
        }
    }

    private static string OpenDirectory(DirectoryInfo dir)
    {
        if (!dir.Exists)
        {
            return $"Directory '{dir}' not found.";
        }

        try
        {
            Process.Start(new ProcessStartInfo(dir.FullName) { UseShellExecute = true });
            return "";
        }
        catch (Exception e)
        {
            Service.Log($"Error opening directory {dir}: {e}");
            return $"Failed to open folder '{dir}', open it manually.";
        }
    }
}
