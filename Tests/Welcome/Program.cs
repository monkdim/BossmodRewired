using BossMod;

var cases = new (string LastSeen, string Current, bool Show, string Reason)[]
{
    ("", "1.2.3.4", true, "first install"),
    ("1.2.3.4", "1.2.3.4", false, "same release on next login"),
    ("1.2.3.4", "1.2.3.5", true, "patch update"),
    ("1.2.3.4", "1.3.0.0", true, "minor update"),
    ("1.2.3.4", "2.0.0.0", true, "major update"),
    ("1.2.3.9", "1.2.3.10", true, "numeric version ordering"),
    ("2.0.0.0", "1.0.0.0", false, "downgrade does not overwrite the seen release"),
    ("broken", "1.0.0.0", true, "unreadable previous version"),
    ("1.0.0.0", "broken", false, "unknown current version cannot repeatedly notify"),
    ("", "0.0.0.0", true, "first development build"),
    ("0.0.0.0", "0.0.0.0", false, "development reload does not repeatedly notify")
};
foreach (var test in cases)
{
    if (WelcomeRelease.ShouldShow(test.LastSeen, test.Current) != test.Show)
    {
        throw new InvalidOperationException(test.Reason);
    }
    Console.WriteLine($"PASS {test.Reason}");
}
Console.WriteLine($"{cases.Length} checks passed.");
