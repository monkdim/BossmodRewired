using Dalamud.Bindings.ImGui;

namespace BossMod;

/// <summary>
/// Countdown bars for what is about to happen, from two sources.
///
/// Enemy casts in progress work everywhere, including the great majority of modules that declare a trivial
/// phase and therefore have no timeline at all. Upcoming states come from the module's state machine, which
/// already predicts the next mechanic and the seconds until it fires; until this window existed that
/// prediction was only ever rendered as two lines of debug text.
///
/// The third source is a cactbot timeline, which covers three hundred fights whether or not a module exists
/// for them and names each mechanic as raiders do. Between them: a cast bar tells you what is happening, a
/// state tells you what a module predicts, and the timeline tells you what the fight does next.
/// </summary>
[SkipLocalsInit]
public sealed class MechanicTimersWindow : UIWindow
{
    // Cast bars are cheap but a pull of adds can put a dozen on screen at once, which is noise rather than
    // information. The nearest few by resolution time are the ones worth reacting to.
    private const int MaxCastBars = 6;

    // Casts further away than this are somebody else's problem, or another pack entirely.
    private const float RelevantRange = 60f;

    // How far ahead a timeline is worth reading. Beyond this a bar is barely moving and says little.
    private const float TimelineHorizon = 30f;

    private static MechanicTimersConfig Config => Service.Config.Get<MechanicTimersConfig>();

    private readonly WorldState _ws;
    private readonly BossModuleManager _mgr;
    private readonly Timelines.TimelineTracker _timeline;
    private readonly List<Timelines.TimelineTracker.Upcoming> _fromTimeline = [];
    private readonly List<(string Label, float Remaining, float Total)> _bars = [];
    private int _castBars;

    public MechanicTimersWindow(WorldState ws, BossModuleManager mgr, Timelines.TimelineTracker timeline) : base("Mechanic timers", false, new(260f, 160f))
    {
        _ws = ws;
        _mgr = mgr;
        _timeline = timeline;
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

            if (config.ShowTimeline)
            {
                CollectTimeline(config.MaxUpcoming);
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

    /// <summary>
    /// What the fight itself does next, named as raiders name it. Unlike the other two sources this needs no
    /// module and no cast to have started, so it is the only one that can warn about a mechanic before the
    /// game gives any sign of it.
    /// </summary>
    private void CollectTimeline(int max)
    {
        _fromTimeline.Clear();
        _timeline.CollectUpcoming(_fromTimeline, max, TimelineHorizon);

        var slot = MySlot();
        foreach (var (name, seconds, abilities) in _fromTimeline)
        {
            // Filled proportionally over the horizon rather than a real duration: the timeline says when a
            // mechanic lands, not how long it has been coming.
            _bars.Add((Label(name, abilities, slot), seconds, TimelineHorizon));
        }
    }

    /// <summary>
    /// The mechanic, and where this player stood for it last time, if that was ever worth recording.
    ///
    /// This is the whole point of the fork. Everything else here says a mechanic is coming, which the game
    /// mostly manages on its own; this says where to be for it, in the seconds when that is still actionable.
    /// </summary>
    private string Label(string name, uint[] abilities, string slot)
    {
        if (slot.Length == 0 || !Config.ShowLearned)
        {
            return name;
        }

        // A mechanic is often several abilities; the best-supported reading among them wins, since they
        // describe the same moment and one of them may simply have been seen more often.
        LearnedPositions.Spot? best = null;
        foreach (var id in abilities)
        {
            var spot = Learned.For(id, slot);
            if (spot is LearnedPositions.Spot s && s.Worth && (best is not LearnedPositions.Spot b || s.Samples > b.Samples))
            {
                best = s;
            }
        }

        // Two observations is thin and says so with a question mark; three or more that agree drops it. One is
        // not shown at all, so the mark means something rather than sitting on every line.
        return best is LearnedPositions.Spot found
            ? $"{name}  {found.Where}{(found.Confident ? "" : "?")}"
            : name;
    }

    /// <summary>
    /// Which slot this player holds, which is what a learned position is filed under.
    ///
    /// An assigned slot wins where there is one. Otherwise the job answers, which is what makes this work at
    /// all outside a static: nobody in a duty finder party has configured anything, and a healer is still a
    /// healer whether or not somebody told the plugin so.
    /// </summary>
    private string MySlot()
    {
        var member = _ws.Party.Members[PartyState.PlayerSlot];
        var job = _ws.Party.Player()?.Class ?? Class.None;
        return member.ContentId != 0 ? LearnedPositions.SlotOf(job, member.ContentId) : LearnedPositions.SlotOf(job);
    }

    // Loaded once and kept, since this is read every frame and rewritten only when somebody exports. The
    // reload button in the replay window is what picks up a fresh export without a restart.
    private static LearnedPositions? _learned;
    public static LearnedPositions Learned => _learned ??= LearnedPositions.Load(
        System.IO.Path.Combine(ReplayAnalysis.EncounterDump.TargetDirectory(), LearnedPositions.FileName));

    public static void ForgetLearned() => _learned = null;

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
