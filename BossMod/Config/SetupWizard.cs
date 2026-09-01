using Dalamud.Bindings.ImGui;

namespace BossMod;

/// <summary>Whether the first-run walkthrough has been shown, and what was decided in it.</summary>
public sealed class SetupConfig : ConfigNode
{
    // Bumped when the walkthrough gains a step worth going back for. Existing installs are the ones most
    // likely to have something to say and would otherwise never be asked anything, since they have no first
    // run left to have.
    public const int CurrentVersion = 1;

    public int CompletedVersion = 0;

    [PropertyDisplay("Share anonymised fight data", tooltip: "Sends the position summaries your exports already produce, with handles instead of names. Asked during setup; changeable here whenever you like.")]
    public bool ShareFightData = false;

    [PropertyDisplay("Relay address", tooltip: "The relay that receives shared exports. Leave empty to use whatever address this build ships with, if any.")]
    public string ShareEndpoint = "";

    [PropertyDisplay("Also record damage, healing and deaths", tooltip: "Adds what each player dealt and took, per pull, to the exports written on this machine. It is what tells you whether a pull was worth learning positions from. Off by default, and kept out of anything shared unless the setting below is also on.")]
    public bool CaptureContributions = false;

    [PropertyDisplay("Include those figures when sharing", tooltip: "Off by default and deliberately separate. The question asked during setup was about positions, so these numbers are removed before an export is sent unless this is turned on as well.")]
    public bool ShareContributions = false;

    [PropertyDisplay("Also record what everybody pressed", tooltip: "Adds every ability each player used, per pull, and how much of the pull they spent on weaponskills. It is what turns a position into a cost: a spot that drops two casts is a worse spot than one that drops none. Off by default, and heavier than the rest, since a busy pull is a few thousand button presses.")]
    public bool CaptureRotations = false;

    [PropertyDisplay("Include the uptime summary when sharing", tooltip: "Off by default and separate again. Only the summary can ever be shared, never the button list itself: an alliance raid's positions already fill most of what the relay will carry, and the presses would not fit beside them. Those stay on this machine whatever this is set to.")]
    public bool ShareRotations = false;

    /// <summary>
    /// Whether the question has ever actually been answered.
    ///
    /// Kept apart from the answer itself, because "no" and "never asked" are different states and only one of
    /// them is worth asking about again. Closing the window is the second, never the first.
    /// </summary>
    public bool ShareDecided = false;
}

/// <summary>
/// The walkthrough shown once, covering the settings worth having before a first pull and ending on the one
/// question that has to be asked rather than assumed.
///
/// It exists because a setting nobody navigates to is a setting nobody uses. The radar is the most useful
/// thing here and lives behind a config tree; roles are what make the position hints work at all and are
/// invisible until somebody goes looking. Both were being missed by people who would have wanted them.
///
/// The data question sits last on purpose. By then they have seen what the plugin does for them and read how
/// their identity is handled, which is the difference between a decision and a dismissal.
/// </summary>
public sealed class SetupWizard : UIWindow
{
    private readonly Action _openRoleSettings;
    private int _page;

    private static SetupConfig Config => Service.Config.Get<SetupConfig>();
    private static BossModuleConfig Radar => Service.Config.Get<BossModuleConfig>();

    public SetupWizard(Action openRoleSettings) : base("BossMod Rewired: first-time setup", false, new(540f, 420f))
    {
        _openRoleSettings = openRoleSettings;
        RespectCloseHotkey = false;
        IsOpen = Config.CompletedVersion < SetupConfig.CurrentVersion;
    }

    public override void OnClose()
    {
        // Closing settles the walkthrough but never the question. Dismissing a window is not somebody saying
        // yes to anything, so an unanswered question stays unanswered and the setting stays off.
        var config = Config;
        config.CompletedVersion = SetupConfig.CurrentVersion;
        config.Modified.Fire();

        // Leaving the demo up after setup would be a radar nobody asked for following them around.
        if (Radar.ShowDemo)
        {
            Radar.ShowDemo = false;
            Radar.Modified.Fire();
        }
    }

    public override void Draw()
    {
        switch (_page)
        {
            case 0: Welcome(); break;
            case 1: RadarPage(); break;
            case 2: RolesPage(); break;
            case 3: IdentityPage(); break;
            default: SharingPage(); return; // draws its own buttons, since neither may be the default
        }

        ImGui.Separator();
        if (_page > 0 && ImGui.Button("Back"))
        {
            --_page;
        }

        if (_page > 0)
        {
            ImGui.SameLine();
        }

        if (ImGui.Button("Next"))
        {
            ++_page;
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"step {_page + 1} of 5");
    }

    private static void Welcome()
    {
        ImGui.TextWrapped("This is a fork of BossMod Reborn that tries to answer a different question.");
        ImGui.Spacing();
        ImGui.TextWrapped("Reborn tells you a mechanic is coming. This also tries to tell you where you, in your role, should be standing for it, learned from recordings of fights you have actually been in.");
        ImGui.Spacing();
        ImGui.TextWrapped("Four short steps. Two are settings worth having before your next pull, one is a thing to do later, and the last is a question about data that you get to answer rather than have assumed.");
    }

    private static void RadarPage()
    {
        ImGui.TextWrapped("The radar is the part you will look at most. It is drawn as a window, so it can go wherever suits you.");
        ImGui.Spacing();

        var radar = Radar;
        var dirty = false;

        // The demo radar exists so this can be done outside an encounter, which is the only reason a setup
        // step for it is possible at all.
        var demo = radar.ShowDemo;
        if (ImGui.Checkbox("Show a practice radar now, so you can place and size it", ref demo))
        {
            radar.ShowDemo = demo;
            dirty = true;
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Drag it where you want it and use the scale below. Turning this off again happens automatically when you finish setup.");
        ImGui.Spacing();

        var scale = radar.ArenaScale;
        if (ImGui.SliderFloat("Size", ref scale, 0.5f, 3f, "%.2f"))
        {
            radar.ArenaScale = scale;
            dirty = true;
        }

        var transparent = radar.TrishaMode;
        if (ImGui.Checkbox("No window background, just the arena", ref transparent))
        {
            radar.TrishaMode = transparent;
            dirty = true;
        }

        var rotate = radar.RotateArena;
        if (ImGui.Checkbox("Rotate with the camera", ref rotate))
        {
            radar.RotateArena = rotate;
            dirty = true;
        }

        var locked = radar.Lock;
        if (ImGui.Checkbox("Lock it in place once you are happy", ref locked))
        {
            radar.Lock = locked;
            dirty = true;
        }

        if (dirty)
        {
            radar.Modified.Fire();
        }
    }

    private void RolesPage()
    {
        ImGui.TextWrapped("Role assignments are what turn \"somebody stood here\" into \"the second healer stands here\".");
        ImGui.Spacing();
        ImGui.TextWrapped("They are worth doing, and they cannot be done now: assigning them needs a party to assign, so it has to happen once you are in a duty with one.");
        ImGui.Spacing();
        ImGui.TextWrapped("Next time you are in a full party, open the settings and set them once. Without them the plugin still works, but everything it learns is filed under you alone rather than under a role anybody can use.");
        ImGui.Spacing();

        if (ImGui.Button("Show me where that is"))
        {
            _openRoleSettings();
        }
    }

    private static void IdentityPage()
    {
        ImGui.TextWrapped("Before the last question, here is what an export of a fight actually contains.");
        ImGui.Spacing();
        ImGui.TextWrapped("One row per player per mechanic, like this:");
        ImGui.Spacing();
        ImGui.TextUnformatted("  {\"ability\": 46518, \"who\": \"H1\",");
        ImGui.TextUnformatted("   \"t\": 14.6, \"cast\": [98.2, 104.7]}");
        ImGui.Spacing();
        ImGui.TextWrapped("An ability, a role, a time, and where somebody stood. No character names anywhere: a player with no role assigned reads as a short handle instead, and that handle is a hash of an account number with a private key that is generated on your machine and never leaves it.");
        ImGui.Spacing();
        ImGui.TextWrapped("So the same person reads the same way throughout one file, which is what makes the file useful, and means nothing at all to anybody outside it.");
        ImGui.Spacing();
        ImGui.TextDisabled("Sharing that key with friends is what lets several people's recordings of the same pull be pooled. That is in the settings under Sharing, and is entirely optional.");
    }

    private void SharingPage()
    {
        ImGui.TextWrapped("Would you like those summaries shared?");
        ImGui.Spacing();
        ImGui.TextWrapped("What it is for: the plugin can only tell you where to stand for fights it has seen enough of. Pooling recordings is what makes that work for content you personally have not run twenty times, and for roles other than your own.");
        ImGui.Spacing();
        ImGui.TextWrapped("What is sent: the position summaries above, after a fight ends. Not chat, not your name, not who you played with, not anything outside a duty.");
        ImGui.Spacing();
        ImGui.TextWrapped("Either answer is a normal answer, and you can change it later in the settings under Sharing.");

        // Said plainly rather than hidden, because a yes that quietly does nothing is worse than a no.
        if (ExportUploader.Endpoint.Length == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("This build has no relay set, so yes will not send anything until an address is filled in under Sharing.");
        }

        ImGui.Separator();

        // Deliberately identical buttons. The moment one of them is the obvious one to press, this stops being
        // a question and becomes a default wearing the costume of one.
        var size = new Vector2(200f, 0f);
        if (ImGui.Button("Yes, share them", size))
        {
            Decide(true);
        }

        ImGui.SameLine();
        if (ImGui.Button("No, keep them local", size))
        {
            Decide(false);
        }

        ImGui.Spacing();
        if (ImGui.Button("Back"))
        {
            --_page;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("step 5 of 5");
    }

    private void Decide(bool share)
    {
        var config = Config;
        config.ShareFightData = share;
        config.ShareDecided = true;
        config.Modified.Fire();
        IsOpen = false;
    }
}
