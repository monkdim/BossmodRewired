using Dalamud.Bindings.ImGui;

namespace BossMod;

[ConfigDisplay(Name = "Boss modules and radar", Order = 1)]
public sealed class BossModuleConfig : ConfigNode
{
    public bool RadarResize;

    public override void DrawCustom(UITree tree, WorldState ws)
    {
        if (ImGui.Button("Recenter Window"))
        {
            Service.BossModWindow?.RecenterWindow();
        }
    }

    // boss module settings
    [PropertyDisplay("Minimal maturity for the module to be loaded", tooltip: "Some modules will have the \"WIP\" status and will not automatically load unless you change this")]
    public BossModuleInfo.Maturity MinMaturity = BossModuleInfo.Maturity.Contributed;

    [PropertyDisplay("Allow modules to automatically use actions", tooltip: "Example: modules can automatically use anti-knockback abilities before a knockback happens")]
    public bool AllowAutomaticActions = true;

    [PropertyDisplay("Show testing radar and hint window", tooltip: "Useful for configuring your radar and hint windows without being inside of a boss encounter", separator: true)]
    public bool ShowDemo = false;

    // radar window settings
    [PropertyDisplay("Enable radar", separator: true)]
    public bool EnableRadar = true;

    [PropertyDisplay("Enable projecting radar into the 3D world")]
    public bool ProjectRadarInto3DWorld = false;

    [PropertyDisplay("Show actor triangles in the 3D world", tooltip: "Show ordinary actor triangles. Mechanic markers, including knockback destinations, remain visible when this is disabled.")]
    public bool ShowActorTrianglesIn3DWorld = true;

    [PropertyDisplay("Include drawing arena outline into the 3D world", tooltip: "If projecting the radar into the 3D world is enabled, the outline can also be drawn")]
    public bool EnableArenaOutlineIn3DWorld = true;

    [PropertyDisplay("Allow drawing text and icon billboards into the 3D world", tooltip: "If projecting the radar into the 3D world is enabled, the outline can also be drawn")]
    public bool EnableTextIconBillboards = true;

    [PropertyDisplay("Billboard height offset", tooltip: "How many yalms billboards should appear above ground. Includes gazes, text and icons.")]
    [PropertySlider(0f, 20f, Speed = 0.1f, Logarithmic = true)]
    public float BillboardHeightOffset = 5f;

    [PropertyDisplay("Text billboard font size", tooltip: "Change text size of 3D world billboards")]
    [PropertySlider(17f, 250f, Speed = 0.5f, Logarithmic = true)]
    public float TextBillboardFontSize = 110f;

    [PropertyDisplay("Icon billboard font size", tooltip: "Change icon size of 3D world billboards", separator: true)]
    [PropertySlider(17f, 250f, Speed = 0.5f, Logarithmic = true)]
    public float IconBillboardFontSize = 110f;

    [PropertyDisplay("Lock radar and hint window movement and mouse interaction")]
    public bool Lock = false;

    [PropertyDisplay("Transparent radar window background", tooltip: "Removes the black window around the radar; this will not work if you move the radar to a different monitor")]
    public bool TrishaMode = true;

    [PropertyDisplay("Add opaque background to the arena in the radar")]
    public bool OpaqueArenaBackground = true;

    [PropertyDisplay("Show outlines and shadows on various radar markings")]
    public bool ShowOutlinesAndShadows = true;

    [PropertyDisplay("Radar arena scale factor", tooltip: "Scale of the arena inside of the radar window")]
    [PropertySlider(0.1f, 10f, Speed = 0.1f, Logarithmic = true)]
    public float ArenaScale = 1f;

    [PropertyDisplay("Radar element thickness scale factor", tooltip: "Globally scales the outline thickness of radar elements")]
    [PropertySlider(0.1f, 10f, Speed = 0.1f, Logarithmic = true)]
    public float ThicknessScale = 1f;

    [PropertyDisplay("Rotate radar to match camera orientation")]
    public bool RotateArena = true;

    [PropertyDisplay("Rotate map by 180° if rotating map is off")]
    public bool FlipArena = false;

    [PropertyDisplay("Give radar extra space for rotations", tooltip: "If you are using the above setting, you can give the radar extra space on the sides before the edges are clipped in order to account for rotating your camera during an encounter or to give the cardinal directions space.")]
    [PropertySlider(1f, 2f, Speed = 0.1f, Logarithmic = true)]
    public float SlackForRotations = 1.5f;

    [PropertyDisplay("Show arena border in radar")]
    public bool ShowBorder = true;

    [PropertyDisplay("Change arena border color if player is at risk", tooltip: "Changes the white border to red when you are standing somewhere you are likely to be hit by a mechanic")]
    public bool ShowBorderRisk = true;

    [PropertyDisplay("Pulse screen edges when player is at risk", tooltip: "A glow pulses in the risky arena border color (Enemy color) while a player warning is active. Works independently of the radar and 3D projection settings.")]
    public bool ShowScreenRiskBorder = false;

    [PropertyDisplay("Screen danger pulse intensity")]
    [PropertySlider(0f, 10f, Speed = 0.1f)]
    public float ScreenRiskBorderIntensity = 2.5f;

    [PropertyDisplay("Show cardinal direction names on radar")]
    public bool ShowCardinals = false;

    [PropertyDisplay("Cardinal direction font size")]
    [PropertySlider(0.1f, 100f, Speed = 1f)]
    public float CardinalsFontSize = 17f;

    [PropertyDisplay("Waymark font size")]
    [PropertySlider(0.1f, 100f, Speed = 1f)]
    public float WaymarkFontSize = 22f;

    [PropertyDisplay("Actor triangle scale factor")]
    [PropertySlider(0.1f, 10f, Speed = 0.1f)]
    public float ActorScale = 1f;

    [PropertyDisplay("Show waymarks on radar")]
    public bool ShowWaymarks = false;

    [PropertyDisplay("Show signs on radar ('attack', 'bind', 'ignore', and shape markers)")]
    public bool ShowSigns = false;

    [PropertyDisplay("Always show all alive party members")]
    public bool ShowIrrelevantPlayers = false;

    [PropertyDisplay("Show role-based colors on otherwise uncolored players in the radar")]
    public bool ColorPlayersBasedOnRole = false;

    [PropertyDisplay("Always show focus targeted party member", separator: true)]
    public bool ShowFocusTargetPlayer = false;

    // hint window settings
    [PropertyDisplay("Show text hints in separate window", tooltip: "Separates the radar window from the hints window, allowing you to reposition the hints window")]
    public bool HintsInSeparateWindow = false;

    [PropertyDisplay("Make separate hints window transparent")]
    public bool HintsInSeparateWindowTransparent = false;

    [PropertyDisplay("Show mechanic sequence and timer hints")]
    public bool ShowMechanicTimers = true;

    [PropertyDisplay("Show raidwide hints")]
    public bool ShowGlobalHints = true;

    [PropertyDisplay("Show player hints and warnings", separator: true)]
    public bool ShowPlayerHints = true;

    // misc. settings
    [PropertyDisplay("Show movement hints in world", tooltip: "Not used very much, but can show you arrows in the game world to indicate where to move for certain mechanics")]
    public bool ShowWorldArrows = false;

    [PropertyDisplay("Show melee range indicator")]
    public bool ShowMeleeRangeIndicator = false;

    [PropertyDisplay("Maximum load distance", tooltip: "Maximum load distance in yalms")]
    [PropertySlider(0.1f, 500f, Speed = 0.1f, Logarithmic = true)]
    public float MaxLoadDistance = 500f;
}
