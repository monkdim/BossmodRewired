namespace BossMod;

[ConfigDisplay(Name = "Duty export", Order = 0)]
public sealed class DutyExportConfig : ConfigNode
{
    [PropertyDisplay("Ask before exporting", tooltip: "Off by default because exporting happens automatically. Turn this on to choose per duty instead.")]
    public bool ShowPrompt;

    [PropertyDisplay("Export without asking",
        tooltip: "Writes the text export for every recorded duty automatically, with no prompt. Useful when someone is capturing duties for somebody else and should not have to think about it.")]
    public bool AutoExport = true;

    [PropertyDisplay("Only offer when the fight did something the module does not know about",
        tooltip: "Keeps the prompt quiet for duties whose modules already cover everything, so it appears when there is genuinely something new to look at.")]
    public bool OnlyWhenDivergent;
}
