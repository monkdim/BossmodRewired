namespace BossMod.Timelines;

/// <summary>
/// Where the current fight has got to in its timeline, and what is coming next.
///
/// The clock is kept by watching abilities go off. Cactbot's timelines say what happens and when, measured
/// from the pull, so seeing an ability the timeline lists is enough to know the time: not elapsed time since
/// combat began, which drifts and means nothing after a phase change, but the fight's own position in its
/// script. Every recognised ability re-anchors it, so the countdown stays right through a slow phase or a
/// long transition.
/// </summary>
public sealed class TimelineTracker : IDisposable
{
    /// <summary>What the timeline says is coming, and how long until it does.</summary>
    /// <summary>What is coming, when, and the abilities it is made of, which is what a learned spot is keyed on.</summary>
    public readonly record struct Upcoming(string Name, float In, uint[] Abilities);

    // Cactbot's own bookkeeping entries. They start and end with dashes and mark syncs, targetability and boss
    // repositioning. Useful to cactbot's clock, not to somebody reading a countdown.
    private static bool IsBookkeeping(string name) => name.Length > 4 && name.StartsWith("--", StringComparison.Ordinal) && name.EndsWith("--", StringComparison.Ordinal);

    private readonly WorldState _ws;
    private readonly EventSubscriptions _subscriptions;

    private CactbotTimeline? _timeline;
    private DateTime _anchoredAt;
    private float _anchoredTo;
    private bool _running;
    private float _previousTime;
    private bool _wasInCombat;
    private float? _pendingCombatStart;
    private TimelineEntry? _jumpSync;

    public CactbotTimeline? Timeline => _timeline;
    public bool Running => _running;

    public TimelineTracker(WorldState ws)
    {
        _ws = ws;
        _subscriptions = new
        (
            ws.CurrentZoneChanged.Subscribe(op => EnterZone(op.Zone)),
            ws.Actors.CastStarted.Subscribe(ObserveStart),
            ws.Actors.CastEvent.Subscribe(ObserveResolution),
            ws.MapEffect.Subscribe(op => ObserveEvent("MapEffect", new Dictionary<string, string>
            {
                ["flags"] = op.State.ToString("X8"),
                ["location"] = op.Index.ToString("X2")
            })),
            ws.DirectorUpdate.Subscribe(op => ObserveEvent("ActorControl", new Dictionary<string, string>
            {
                ["command"] = op.UpdateID.ToString("X8"),
                ["data0"] = op.Param1.ToString("X2"),
                ["data1"] = op.Param2.ToString("X2"),
                ["data2"] = op.Param3.ToString("X2"),
                ["data3"] = op.Param4.ToString("X2")
            }))
        );

        EnterZone(ws.CurrentZone);
    }

    public void Dispose() => _subscriptions.Dispose();

    /// <summary>Called once a frame, to notice a fight ending without waiting for the zone to change.</summary>
    public void Update()
    {
        if (_ws.Party.Player() is Actor player)
        {
            if (!player.InCombat)
            {
                _running = false;
                _wasInCombat = false;
                return;
            }
            if (!_wasInCombat)
            {
                _wasInCombat = true;
                if (_pendingCombatStart is { } pending)
                {
                    Anchor(pending);
                    _pendingCombatStart = null;
                }
                else
                    ObserveEvent("InCombat", new Dictionary<string, string> { ["inGameCombat"] = "1" });
            }
        }
        if (!_running || _timeline == null)
            return;

        // A delayed frame can cross more than one unconditional jump.
        for (var jumps = 0; jumps < 16; ++jumps)
        {
            var now = Now();
            if (_jumpSync is { } previous && now > previous.Time + previous.WindowAfter)
                _jumpSync = null;
            var next = _timeline.Entries.FirstOrDefault(entry => entry.Time > _previousTime
                && entry.Time <= now && entry.ForceJump && entry.JumpTo.HasValue);
            if (next == null)
            {
                _previousTime = now;
                return;
            }
            var destination = next.JumpTo!.Value;
            Anchor(destination + now - next.Time);
            _previousTime = destination;
            _jumpSync = next with { Time = destination, WindowBefore = 0, ForceJump = false };
        }
    }

    /// <summary>The next few things the timeline expects, soonest first.</summary>
    public void CollectUpcoming(List<Upcoming> into, int max, float horizon)
    {
        if (_timeline == null || !_running || max <= 0)
        {
            return;
        }

        var cursor = Now();
        var elapsed = 0f;
        for (var jumps = 0; jumps < 16; ++jumps)
        {
            var jumped = false;
            foreach (var entry in _timeline.Entries)
            {
                if (entry.Time <= cursor)
                    continue;

                var until = elapsed + entry.Time - cursor;
                if (until > horizon)
                    return;

                if (!IsBookkeeping(entry.Name))
                {
                    into.Add(new(entry.Name, until, entry.Abilities));
                    if (into.Count >= max)
                        return;
                }
                if (entry.ForceJump && entry.JumpTo is { } destination)
                {
                    cursor = destination;
                    elapsed = until;
                    jumped = true;
                    break;
                }
            }
            if (!jumped)
                return;
        }
    }

    private float Now() => _anchoredTo + (float)(_ws.CurrentTime - _anchoredAt).TotalSeconds;

    private void EnterZone(ushort zone)
    {
        _timeline = TimelineLibrary.ForZone(zone);
        _running = false;
        _wasInCombat = false;
        _pendingCombatStart = null;
        _jumpSync = null;

        if (_timeline != null)
        {
            Service.Log($"[timelines] {_timeline.Name} for zone {zone}");
        }
    }

    private void ObserveStart(Actor actor)
    {
        if (actor.CastInfo is { } cast)
            Observe(actor, cast.Action, true);
    }

    private void ObserveResolution(Actor actor, ActorCastEvent cast) => Observe(actor, cast.Action, false);

    private void ObserveEvent(string eventType, IReadOnlyDictionary<string, string> values)
    {
        if (_timeline == null)
            return;
        var estimate = _running ? Now() : 0f;
        TimelineEntry? best = null;
        var bestGap = float.MaxValue;
        foreach (var entry in SyncEntries())
        {
            if (entry.SyncEvent != eventType || entry.SyncFields.Count == 0
                || estimate < entry.Time - entry.WindowBefore || estimate > entry.Time + entry.WindowAfter)
                continue;
            try
            {
                if (!entry.SyncFields.All(field => values.TryGetValue(field.Key, out var value) && field.Value.IsMatch(value)))
                    continue;
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                continue;
            }
            var gap = Math.Abs(estimate - entry.Time);
            if (gap < bestGap)
            {
                best = entry;
                bestGap = gap;
            }
        }
        if (best == null)
            return;
        var destination = best.JumpTo ?? best.Time;
        Anchor(destination);
        if (destination == 0 && eventType == "ActorControl")
        {
            _running = false;
            _pendingCombatStart = null;
        }
        else if (_ws.Party.Player() is { InCombat: false })
            _pendingCombatStart = destination;
    }

    private void Observe(Actor actor, ActionID action, bool isStart)
    {
        if (_timeline == null || actor.IsAlly || action.Type != ActionType.Spell)
        {
            return;
        }

        var estimate = _running ? Now() : 0f;

        // The occurrence nearest where we think we are. Starting a fight, that is the earliest one, which is
        // what "nearest to zero" gives without needing a separate rule for it.
        TimelineEntry? best = null;
        var bestGap = float.MaxValue;
        string? source = null;

        foreach (var entry in SyncEntries())
        {
            if (!entry.IsCastSync || entry.OnCastStart != isStart || Array.IndexOf(entry.Abilities, action.ID) < 0)
            {
                continue;
            }

            var offset = estimate - entry.Time;
            if (_running && (offset < -entry.WindowBefore || offset > entry.WindowAfter))
                continue;

            if (entry.SyncSource is { } matcher)
            {
                source ??= (Service.LuminaRow<Lumina.Excel.Sheets.BNpcName>(actor.NameID)?.Singular.ToString() ?? actor.Name);
                try
                {
                    if (!matcher.IsMatch(source) && !matcher.IsMatch(actor.Name))
                        continue;
                }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
                {
                    continue;
                }
            }

            var gap = Math.Abs(offset);
            if (gap < bestGap)
            {
                bestGap = gap;
                best = entry;
            }
        }

        if (best != null)
            Anchor(best.JumpTo ?? best.Time);
    }

    private void Anchor(float time)
    {
        _jumpSync = null;
        _previousTime = _anchoredTo = time;
        _anchoredAt = _ws.CurrentTime;
        _running = true;
    }

    private IEnumerable<TimelineEntry> SyncEntries()
    {
        if (_timeline != null)
            foreach (var entry in _timeline.Entries)
                yield return entry;
        if (_jumpSync != null)
            yield return _jumpSync;
    }
}
