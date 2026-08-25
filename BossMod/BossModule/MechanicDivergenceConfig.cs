namespace BossMod;

[ConfigDisplay(Name = "Unknown mechanic alerts", Order = 1)]
public sealed class MechanicDivergenceConfig : ConfigNode
{
    [PropertyDisplay("Watch for mechanics the active module does not know about")]
    public bool Enable = true;

    [PropertyDisplay("Show a window when a fight ends with unknown mechanics")]
    public bool ShowWindow = true;

    [PropertyDisplay("Print a chat message as well")]
    public bool ChatAlert = true;

    [PropertyDisplay("Also report categories the module does not track at all",
        tooltip: "Many modules declare no status or tether enum. With this off, only categories the module already tracks are checked, so an alert means the module knows about this kind of thing and has never seen this one. With it on, every uncatalogued status in such a fight is listed, which is thorough but noisy.")]
    public bool ReportUndeclaredCategories;
}
