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
        new("Right fight, right little moment", "Position hints now match the boss, mechanic and recent session. New conflicts hide the affected hint."),
        new("Tiny memories, big adventures", "Saved recording history is compacted in the background. Older positions without verifiable context stay hidden."),
        new("A little patience for new strategies", "Live hints need five agreeing pulls in the current session and support from another session. Type /bmr welcome to visit again.")
    ];
}
