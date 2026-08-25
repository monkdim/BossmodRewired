using Dalamud.Bindings.ImGui;

namespace BossMod;

/// <summary>
/// Countdown bars for what is about to happen, from two sources.
///
/// Enemy casts in progress work everywhere, including the great majority of modules that declare a trivial
/// phase and therefore have no timeline at all. Upcoming states come from the module's state machine, which
/// already predicts the next mechanic and the seconds until it fires; until this window existed that
/// prediction was only ever rendered as two lines of debug text.
/// </summary>
[SkipLocalsInit]
public sealed class MechanicTimersWindow : UIWindow
{
    // Cast bars are cheap but a pull of adds can put a dozen on screen at once, which is noise rather than
    // information. The nearest few by resolution time are the ones worth reacting to.
    private const int MaxCastBars = 6;

    // Casts further away than this are somebody else's problem, or another pack entirely.
    private const float RelevantRange = 60f;

    private static MechanicTimersConfig Config => Service.Config.Get<MechanicTimersConfig>();

    private readonly WorldState _ws;
    private readonly BossModuleManager _mgr;
    private readonly List<(string Label, float Remaining, float Total)> _bars = [];
    private int _castBars;

    public MechanicTimersWindow(WorldState ws, BossModuleManager mgr) : base("Mechanic timers", false, new(260f, 160f))
    {
        _ws = ws;
        _mgr = mgr;
        RespectCloseHotkey = false;
    }

    public override void PreOpenCheck()
    {
        var config = Config;

        // Bars are collected here rather than in Draw so the window can decide whether it has anything to say.
        // Gating on an active boss module would have hidden it in every fight without one, which is most of
        // them, and the cast bars need no module at all.
        _bars.Clear();
        if (config.Enable)
        {
            if (config.ShowCasts)
            {
                CollectCasts();
            }

            _castBars = _bars.Count;

            if (config.ShowStates)
            {
                CollectStates(config.MaxUpcoming);
            }
        }

        IsOpen = _bars.Count > 0;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize;
        if (config.Lock)
        {
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        }

        if (config.Transparent)
        {
            Flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
        }

        ForceMainWindow = config.Transparent;
    }

    public override void Draw()
    {
        var config = Config;
        var castBars = _castBars;
        var size = new Vector2(config.BarWidth, config.BarHeight);
        for (var i = 0; i < _bars.Count; ++i)
        {
            var (label, remaining, total) = _bars[i];

            // A state we predicted has no elapsed portion to show, so it fills as it approaches; a cast in
            // progress drains. Both read as "the bar is full when it happens".
            var fraction = total > 0f ? Math.Clamp(1f - remaining / total, 0f, 1f) : 0f;
            var imminent = remaining <= config.ImminentThreshold;

            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, imminent ? Colors.Danger : Colors.Safe);
            ImGui.ProgressBar(fraction, size, $"{label}  {remaining:f1}s");
            ImGui.PopStyleColor();

            // A rule between the two sources, so a predicted mechanic is never mistaken for one already casting.
            if (i + 1 == castBars && _bars.Count > castBars)
            {
                ImGui.Separator();
            }
        }
    }

    private void CollectCasts()
    {
        // Anchored on the player rather than the boss, since without a module there is no boss to anchor to.
        var player = _ws.Party.Player();
        if (player == null)
        {
            return;
        }

        var origin = player.Position;

        foreach (var actor in _ws.Actors)
        {
            var cast = actor.CastInfo;
            if (cast == null || actor.IsAlly || actor.IsDead)
            {
                continue;
            }

            if ((actor.Position - origin).LengthSq() > RelevantRange * RelevantRange)
            {
                continue;
            }

            var remaining = cast.NPCRemainingTime;
            if (remaining <= 0f)
            {
                continue;
            }

            _bars.Add((cast.Action.Name(), remaining, cast.NPCTotalTime));
        }

        _bars.Sort((a, b) => a.Remaining.CompareTo(b.Remaining));
        if (_bars.Count > MaxCastBars)
        {
            _bars.RemoveRange(MaxCastBars, _bars.Count - MaxCastBars);
        }
    }

    /// <summary>
    /// Walks the state machine forward from the active state. The walk stops at the first branch: past a
    /// fork the module itself does not know which way the fight goes, and a confidently wrong countdown is
    /// worse than a short list.
    /// </summary>
    private void CollectStates(int maxUpcoming)
    {
        var module = _mgr.ActiveModule;
        if (module == null)
        {
            return;
        }

        var sm = module.StateMachine;
        var state = sm.ActiveState;
        if (state == null)
        {
            return;
        }

        var remaining = Math.Max(0f, state.Duration - sm.TimeSinceTransition);
        if (state.Name.Length > 0)
        {
            _bars.Add((state.Name, remaining, Math.Max(state.Duration, 0.01f)));
        }

        var listed = 0;
        var accumulated = remaining;
        while (listed < maxUpcoming && state.NextStates?.Length == 1)
        {
            state = state.NextStates[0];
            accumulated += Math.Max(0f, state.Duration);
            if (state.Name.Length > 0)
            {
                _bars.Add((state.Name, accumulated, Math.Max(accumulated, 0.01f)));
                ++listed;
            }
        }
    }
}
