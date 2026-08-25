namespace BossMod;

[ConfigDisplay(Name = "Duty export prompt", Order = 0)]
public sealed class DutyExportConfig : ConfigNode
{
    [PropertyDisplay("Offer to export after every recorded duty")]
    public bool ShowPrompt = true;

    [PropertyDisplay("Export without asking",
        tooltip: "Writes the text export for every recorded duty automatically, with no prompt. Useful when someone is capturing duties for somebody else and should not have to think about it.")]
    public bool AutoExport;

    [PropertyDisplay("Only offer when the fight did something the module does not know about",
        tooltip: "Keeps the prompt quiet for duties whose modules already cover everything, so it appears when there is genuinely something new to look at.")]
    public bool OnlyWhenDivergent;
}
