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
        new("A little hello, just for you", "First visit or fresh update? Your new welcome window brings the highlights together."),
        new("Your cozy corner stays yours", "Updating keeps your settings. First-time adventurers can head straight into the existing setup."),
        new("Catch up whenever you like", "Missed something? Type /bmr welcome to come back, or open the full release history below.")
    ];
}
