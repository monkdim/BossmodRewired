using System.Reflection;

namespace BossMod;

static class WelcomeRelease
{
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    public static bool ShouldShow(string lastSeen, string current)
        => System.Version.TryParse(current, out var installed)
        && (!System.Version.TryParse(lastSeen, out var seen) || installed > seen);

    public readonly record struct Note(string Title, string Body);

    // Update these alongside each release. Older releases remain available through the release-history link.
    public static readonly Note[] Notes =
    [
        new("A little more help on your adventures", "Lindwurm's Curtain Call cone now tells you to enter or avoid it based on Rotting Flesh. Amon's radar loads with the default module setting."),
        new("Eyes up, little adventurer", "Angra's marked circles follow their players at the corrected size, with brand and Doom reminders. Phlegethon and Thunder God gain mechanic callouts. Progenitor gains initial raidwide warnings in its work-in-progress module."),
        new("Timely little reminders", "Fight timers now follow cast starts, sync windows, timeline jumps, combat starts, wipe resets, map effects and director updates. Type /bmr welcome to visit again.")
    ];
}
