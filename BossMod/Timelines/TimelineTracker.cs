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

    // How far from the current estimate an ability can be and still be taken as the same one. An ability used
    // in two phases appears twice in the timeline, so the nearer occurrence is the one meant; this only has to
    // be wide enough to survive a party killing a phase faster or slower than the timeline was written for.
    private const float ResyncWindow = 20f;

    // Cactbot's own bookkeeping entries. They start and end with dashes and mark syncs, targetability and boss
    // repositioning. Useful to cactbot's clock, not to somebody reading a countdown.
    private static bool IsBookkeeping(string name) => name.Length > 4 && name.StartsWith("--", StringComparison.Ordinal) && name.EndsWith("--", StringComparison.Ordinal);

    private readonly WorldState _ws;
    private readonly EventSubscriptions _subscriptions;

    private CactbotTimeline? _timeline;
    private DateTime _anchoredAt;
    private float _anchoredTo;
    private bool _running;

    public CactbotTimeline? Timeline => _timeline;
    public bool Running => _running;

    public TimelineTracker(WorldState ws)
    {
        _ws = ws;
        _subscriptions = new
        (
            ws.CurrentZoneChanged.Subscribe(op => EnterZone(op.Zone)),
            ws.Actors.CastEvent.Subscribe(Observe)
        );

        EnterZone(ws.CurrentZone);
    }

    public void Dispose() => _subscriptions.Dispose();

    /// <summary>Called once a frame, to notice a fight ending without waiting for the zone to change.</summary>
    public void Update()
    {
        if (_running && _ws.Party.Player() is Actor player && !player.InCombat)
        {
            _running = false;
        }
    }

    /// <summary>The next few things the timeline expects, soonest first.</summary>
    public void CollectUpcoming(List<Upcoming> into, int max, float horizon)
    {
        if (_timeline == null || !_running || max <= 0)
        {
            return;
        }

        var now = Now();

        foreach (var entry in _timeline.Entries)
        {
            if (entry.Time <= now)
            {
                continue;
            }

            var until = entry.Time - now;
            if (until > horizon)
            {
                break; // entries are sorted, so everything past here is further still
            }

            if (IsBookkeeping(entry.Name))
            {
                continue;
            }

            into.Add(new(entry.Name, until, entry.Abilities));
            if (into.Count >= max)
            {
                return;
            }
        }
    }

    private float Now() => _anchoredTo + (float)(_ws.CurrentTime - _anchoredAt).TotalSeconds;

    private void EnterZone(ushort zone)
    {
        _timeline = TimelineLibrary.ForZone(zone);
        _running = false;

        if (_timeline != null)
        {
            Service.Log($"[timelines] {_timeline.Name} for zone {zone}");
        }
    }

    private void Observe(Actor actor, ActorCastEvent cast)
    {
        if (_timeline == null || actor.IsAlly || cast.Action.Type != ActionType.Spell)
        {
            return;
        }

        var id = cast.Action.ID;
        var estimate = _running ? Now() : 0f;

        // The occurrence nearest where we think we are. Starting a fight, that is the earliest one, which is
        // what "nearest to zero" gives without needing a separate rule for it.
        var best = float.NaN;
        var bestGap = float.MaxValue;

        foreach (var entry in _timeline.Entries)
        {
            if (Array.IndexOf(entry.Abilities, id) < 0)
            {
                continue;
            }

            var gap = Math.Abs(entry.Time - estimate);
            if (gap < bestGap)
            {
                bestGap = gap;
                best = entry.Time;
            }
        }

        if (float.IsNaN(best))
        {
            return;
        }

        // Once running, an ability far from the estimate is a different use of the same ability rather than
        // proof the clock is wrong, and following it would throw the countdown across the fight.
        if (_running && bestGap > ResyncWindow)
        {
            return;
        }

        _anchoredTo = best;
        _anchoredAt = _ws.CurrentTime;
        _running = true;
    }
}
