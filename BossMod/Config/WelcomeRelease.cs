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
        new("A little banana buddy", "Lindwurm's Replication 2 now gives you a personal marker, a connecting line and an instruction for each step when Banana Codex is selected."),
        new("Your spot, even without a tether", "The guide follows your actual assignment through baits, defamations, stacks, cone facing, close/far pairs and replay towers."),
        new("Ready for your next adventure", "Keep Replication 2 strategy set to Banana Codex in Lindwurm phase-two settings. Type /bmr welcome to visit again.")
    ];
}
