# Welcome window checks

Run `dotnet run --project Tests/Welcome/Welcome.csproj -c Release` with .NET 10.
These checks link the production version policy and cover first installs, repeated launches, patch and
major upgrades, numeric ordering, development builds, invalid state, and downgrades.

The displayed highlights live in `BossMod/Config/WelcomeRelease.cs`; update them with each release.
The installed assembly version controls when the window reappears. Closing it acknowledges only the
welcome version, without completing setup or changing sharing choices. First-run setup follows the
greeting; `/bmr welcome` reopens the greeting later.

The window has been rendered outside the game using the installed ImGui bindings at normal and 150%
text scaling. In-game focus, HUD coexistence, and setup interaction still need live verification.
