namespace BossMod;

[ConfigDisplay(Name = "Mechanic timers", Order = 1)]
public sealed class MechanicTimersConfig : ConfigNode
{
    [PropertyDisplay("Show the mechanic timer bars")]
    public bool Enable = true;

    [PropertyDisplay("Show enemy casts in progress", tooltip: "Works in every fight, including the ones whose module has no timings.")]
    public bool ShowCasts = true;

    [PropertyDisplay("Show upcoming mechanics from the module's timeline", tooltip: "Only fights whose module defines timed states have anything to show here. Most dungeon and normal raid modules do not.")]
    public bool ShowStates = true;

    [PropertyDisplay("How many upcoming mechanics to list")]
    [PropertySlider(1, 10)]
    public int MaxUpcoming = 4;

    [PropertyDisplay("Highlight a bar when it is this close to resolving, in seconds")]
    [PropertySlider(0.5f, 10f)]
    public float ImminentThreshold = 3f;

    [PropertyDisplay("Bar width")]
    [PropertySlider(100f, 600f)]
    public float BarWidth = 240f;

    [PropertyDisplay("Bar height")]
    [PropertySlider(10f, 60f)]
    public float BarHeight = 24f;

    [PropertyDisplay("Lock position and click through")]
    public bool Lock;

    [PropertyDisplay("Hide the window background")]
    public bool Transparent;
}
