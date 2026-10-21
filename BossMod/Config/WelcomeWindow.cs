using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using System.Diagnostics;

namespace BossMod;

public sealed class WelcomeConfig : ConfigNode
{
    public string LastSeenVersion = "";
}

/// <summary>A once-per-release hello, followed by the existing first-run walkthrough when needed.</summary>
public sealed class WelcomeWindow : UIWindow
{
    private readonly Action _openSetup;
    private readonly Action _openSettings;
    private bool _acknowledged;
    private static WelcomeConfig Config => Service.Config.Get<WelcomeConfig>();
    private static bool NeedsSetup => Service.Config.Get<SetupConfig>().CompletedVersion < SetupConfig.CurrentVersion;
    private static float Scale => ImGui.GetFontSize() / 17f;
    private static uint Ink => Color(new(0.91f, 0.88f, 0.98f, 1));

    public WelcomeWindow(Action openSetup, Action openSettings)
        : base("A little hello###BossModRewiredWelcome", false, new(580f, 580f), ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _openSetup = openSetup;
        _openSettings = openSettings;
        IsOpen = WelcomeRelease.ShouldShow(Config.LastSeenVersion, WelcomeRelease.Version);
    }

    public void Show()
    {
        _acknowledged = false;
        OpenAndBringToFront();
    }

    public override bool DrawConditions() => Service.ClientState.IsLoggedIn && !Service.Condition[ConditionFlag.InCombat];

    public override void PreDraw()
    {
        var scale = Scale;
        var display = ImGui.GetIO().DisplaySize;
        var width = Math.Min(580f * scale, Math.Max(280f, display.X - 32f));
        ImGui.SetNextWindowSizeConstraints(new(width, 0), new(width, Math.Max(200f, display.Y - 64f)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 18f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22, 18) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(15, 10) * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.105f, 0.085f, 0.15f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.15f, 0.12f, 0.21f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.19f, 0.15f, 0.26f, 1));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.91f, 0.88f, 0.98f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.40f, 0.29f, 0.60f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.51f, 0.37f, 0.73f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.34f, 0.24f, 0.53f, 1));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(7);
        ImGui.PopStyleVar(4);
    }

    public override void OnClose()
    {
        Acknowledge();
        base.OnClose();
    }

    private void Acknowledge()
    {
        if (_acknowledged)
        {
            return;
        }
        _acknowledged = true;
        // Opening or updating never changes setup completion, sharing choices, or any gameplay setting.
        if (WelcomeRelease.ShouldShow(Config.LastSeenVersion, WelcomeRelease.Version))
        {
            Config.LastSeenVersion = WelcomeRelease.Version;
            Config.Modified.Fire();
        }
        if (NeedsSetup)
        {
            _openSetup();
        }
    }

    public override void Draw()
    {
        var scale = Scale;
        var width = ImGui.GetContentRegionAvail().X;
        Hero(width, scale);
        ImGui.Dummy(new(0, 12f * scale));
        ImGui.TextColored(new Vector4(0.73f, 0.88f, 0.82f, 1), $"WHAT'S NEW  /  v{WelcomeRelease.Version}");
        ImGui.Dummy(new(0, 4f * scale));
        for (var i = 0; i < WelcomeRelease.Notes.Length; ++i)
        {
            Note(WelcomeRelease.Notes[i], i, width, scale);
        }
        ImGui.Dummy(new(0, 8f * scale));
        if (ImGui.Button(NeedsSetup ? "Let's get you settled" : "Off we go!", new(width, 0)))
        {
            IsOpen = false;
            Acknowledge();
        }
        if (!NeedsSetup)
        {
            if (ImGui.SmallButton("Settings"))
            {
                IsOpen = false;
                Acknowledge();
                _openSettings();
            }
            ImGui.SameLine();
        }
        if (ImGui.SmallButton("Full release history"))
        {
            Process.Start(new ProcessStartInfo("https://github.com/monkdim/BossmodRewired/releases") { UseShellExecute = true });
        }
        ImGui.Spacing();
        ImGui.TextDisabled("A little less guessing. A little more adventuring.");
    }

    private static void Hero(float width, float scale)
    {
        var p = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p, p + new Vector2(width, 153f * scale), Color(new(0.20f, 0.15f, 0.29f, 1)), 15f * scale);
        Cat(p + new Vector2(77, 77) * scale, scale);
        var textX = Math.Min(155f * scale, width * 0.36f);
        ImGui.SetCursorScreenPos(p + new Vector2(textX, 22f * scale));
        ImGui.TextColored(new Vector4(0.98f, 0.73f, 0.69f, 1), "BOSSMOD REWIRED");
        ImGui.SetCursorScreenPos(p + new Vector2(textX, 51f * scale));
        ImGui.SetWindowFontScale(1.24f);
        ImGui.TextUnformatted(NeedsSetup ? "Hello, adventurer!" : "Welcome back!");
        ImGui.SetWindowFontScale(1f);
        ImGui.SetCursorScreenPos(p + new Vector2(textX, 88f * scale));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - textX - 16f * scale);
        ImGui.TextUnformatted(NeedsSetup ? "Your little companion for the next big adventure." : "A fresh update, a tiny wave, and a few things to catch up on.");
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(p + new Vector2(0, 153f * scale));
        ImGui.Dummy(Vector2.Zero);
    }

    private static void Note(WelcomeRelease.Note note, int index, float width, float scale)
    {
        var p = ImGui.GetCursorScreenPos();
        var textWidth = Math.Max(100f, width - 66f * scale);
        var bodyHeight = ImGui.CalcTextSize(note.Body, false, textWidth).Y;
        var height = Math.Max(71f * scale, bodyHeight + 42f * scale);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p, p + new Vector2(width, height), Color(new(0.15f, 0.12f, 0.21f, 1)), 12f * scale);
        Vector4[] accents = [new(0.77f, 0.67f, 0.95f, 1), new(0.98f, 0.73f, 0.69f, 1), new(0.65f, 0.86f, 0.78f, 1)];
        var accent = accents[index % accents.Length];
        draw.AddCircleFilled(p + new Vector2(23, 25) * scale, 5f * scale, Color(accent));
        ImGui.SetCursorScreenPos(p + new Vector2(43, 12) * scale);
        ImGui.TextColored(accent, note.Title);
        ImGui.SetCursorScreenPos(p + new Vector2(43, 34) * scale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
        ImGui.TextUnformatted(note.Body);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(p + new Vector2(0, height + 8f * scale));
        ImGui.Dummy(Vector2.Zero);
    }

    private static void Cat(Vector2 p, float scale)
    {
        var draw = ImGui.GetWindowDrawList();
        Vector2 at(float x, float y) => p + new Vector2(x, y) * scale;
        var lavender = Color(new(0.77f, 0.67f, 0.95f, 1));
        var peach = Color(new(0.98f, 0.73f, 0.69f, 1));
        var dark = Color(new(0.20f, 0.15f, 0.29f, 1));
        draw.AddCircleFilled(p, 57f * scale, Color(new(0.29f, 0.23f, 0.39f, 1)));
        draw.AddTriangleFilled(at(-34, -12), at(-34, -47), at(-8, -29), lavender);
        draw.AddTriangleFilled(at(34, -12), at(34, -47), at(8, -29), lavender);
        draw.AddTriangleFilled(at(-29, -22), at(-29, -36), at(-18, -28), peach);
        draw.AddTriangleFilled(at(29, -22), at(29, -36), at(18, -28), peach);
        draw.AddCircleFilled(at(0, -3), 38f * scale, lavender);
        draw.AddCircleFilled(at(-24, 8), 7f * scale, peach);
        draw.AddCircleFilled(at(24, 8), 7f * scale, peach);
        draw.AddCircleFilled(at(-13, -6), 3f * scale, dark);
        draw.AddCircleFilled(at(13, -6), 3f * scale, dark);
        draw.AddTriangleFilled(at(-3, 4), at(3, 4), at(0, 8), dark);
        draw.AddLine(at(0, 8), at(0, 10), dark, 1.5f * scale);
        draw.AddLine(at(-7, 10), at(-4, 13), dark, 1.5f * scale);
        draw.AddLine(at(-4, 13), at(0, 10), dark, 1.5f * scale);
        draw.AddLine(at(0, 10), at(4, 13), dark, 1.5f * scale);
        draw.AddLine(at(4, 13), at(7, 10), dark, 1.5f * scale);
        draw.AddCircleFilled(at(-19, 32), 10f * scale, lavender);
        draw.AddCircleFilled(at(19, 32), 10f * scale, lavender);
        draw.AddCircleFilled(at(42, -39), 3f * scale, peach);
        draw.AddLine(at(-49, -34), at(-39, -34), Ink, 2f * scale);
        draw.AddLine(at(-44, -39), at(-44, -29), Ink, 2f * scale);
        draw.AddLine(at(42, 35), at(52, 35), Ink, 2f * scale);
        draw.AddLine(at(47, 30), at(47, 40), Ink, 2f * scale);
    }

    private static uint Color(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
}
