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

    [PropertyDisplay("Show upcoming mechanics from cactbot timelines", tooltip: "Works in any fight cactbot has a timeline for, including ones with no boss module. Names come from cactbot.")]
    public bool ShowTimeline = true;

    [PropertyDisplay("Show where you stood last time", tooltip: "Adds the position your role held for a mechanic, learned from your own exported recordings. Only mechanics somebody actually held a spot for have anything to show.")]
    public bool ShowLearned = true;

    [PropertyDisplay("How many upcoming mechanics to list")]
    [PropertySlider(1, 10)]
    public int MaxUpcoming = 4;

    [PropertyDisplay("Highlight a bar when it is this close to resolving, in seconds")]
    [PropertySlider(0.5f, 10f)]
    public float ImminentThreshold = 3f;

    [PropertyDisplay("Overall size", tooltip: "Scales the bars and their text together, so one slider makes the whole thing smaller. The two measurements below are taken before this is applied.")]
    [PropertySlider(0.4f, 2f)]
    public float Scale = 1f;

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
