namespace BossMod;

/// <summary>
/// Watches a fight for things the active module has never seen, and says so when the fight ends.
///
/// Every module declares its known ability, status, head marker and tether IDs as enums. Anything that fires
/// during the fight and resolves to no name in those enums is, by definition, something nobody catalogued:
/// a mechanic in a phase that was never reached, a patch change, or a fight covered only in outline.
///
/// This is the same question the "generate missing enum values" buttons answer offline, asked live. The point
/// is to know which recordings are worth going back to, rather than exporting every pull blindly.
/// </summary>
public sealed class MechanicDivergenceTracker : IDisposable
{
    public readonly record struct Finding(string Kind, uint ID, string Description);

    private static MechanicDivergenceConfig Config => Service.Config.Get<MechanicDivergenceConfig>();

    /// <summary>Findings from the fight that just ended, kept until dismissed so they can be acted on.</summary>
    public IReadOnlyList<Finding> LastFindings => _lastFindings;

    public string LastModuleName { get; private set; } = "";
    public bool HasUnacknowledged { get; private set; }

    private readonly WorldState _ws;
    private readonly BossModuleManager _mgr;
    private readonly EventSubscriptions _subscriptions;

    private readonly List<Finding> _current = [];
    private readonly List<Finding> _lastFindings = [];
    private readonly HashSet<(string, uint)> _seen = [];

    private BossModuleRegistry.Info? _info;
    private bool _watching;

    public MechanicDivergenceTracker(WorldState ws, BossModuleManager mgr)
    {
        _ws = ws;
        _mgr = mgr;
        _subscriptions = new(
            mgr.ModuleActivated.Subscribe(OnModuleActivated),
            mgr.ModuleDeactivated.Subscribe(OnModuleDeactivated),
            ws.Actors.CastStarted.Subscribe(OnCastStarted),
            ws.Actors.IconAppeared.Subscribe(OnIconAppeared),
            ws.Actors.Tethered.Subscribe(OnTethered),
            ws.Actors.StatusGain.Subscribe(OnStatusGain));
    }

    public void Dispose() => _subscriptions.Dispose();

    public void Acknowledge()
    {
        HasUnacknowledged = false;
        _lastFindings.Clear();
    }

    private void OnModuleActivated(BossModule module)
    {
        _current.Clear();
        _seen.Clear();
        _info = BossModuleRegistry.FindByOID(module.PrimaryActor.OID);
        _watching = Config.Enable;
    }

    private void OnModuleDeactivated(BossModule module)
    {
        if (!_watching)
        {
            return;
        }

        _watching = false;
        if (_current.Count == 0)
        {
            return;
        }

        _lastFindings.Clear();
        _lastFindings.AddRange(_current);
        LastModuleName = module.GetType().Name;
        HasUnacknowledged = true;

        var config = Config;
        if (config.ChatAlert)
        {
            Service.ChatGui.Print($"[BMR] {LastModuleName}: {_current.Count} mechanic(s) this module does not know about. Open the replay analysis and export this pull.");
        }

        Service.Log($"[Divergence] {LastModuleName}: {_current.Count} unknown mechanics");
    }

    private void OnCastStarted(Actor actor)
    {
        var cast = actor.CastInfo;
        if (!_watching || cast == null || actor.IsAlly || cast.Action.Type != ActionType.Spell)
        {
            return;
        }

        // ActionIDType is the one enum essentially every module declares, so a miss here is unambiguous.
        Record(_info?.ActionIDType, "ability", cast.Action.ID, $"{cast.Action.Name()} cast by {Describe(actor)}, {cast.NPCTotalTime:f1}s");
    }

    private void OnIconAppeared(Actor actor, uint iconID, ulong targetID)
    {
        if (!_watching)
        {
            return;
        }

        Record(_info?.IconIDType, "head marker", iconID, $"marker {iconID} on {Describe(actor)}");
    }

    private void OnTethered(Actor actor)
    {
        if (!_watching)
        {
            return;
        }

        var target = _ws.Actors.Find(actor.Tether.Target);
        Record(_info?.TetherIDType, "tether", actor.Tether.ID, $"tether {actor.Tether.ID} from {Describe(actor)} to {(target != null ? Describe(target) : "?")}");
    }

    private void OnStatusGain(Actor actor, int index)
    {
        if (!_watching || actor.Type != ActorType.Player)
        {
            return;
        }

        // A player's own buffs are rotation, not mechanics. Only what the fight inflicts is interesting.
        var status = actor.Statuses[index];
        var source = _ws.Actors.Find(status.SourceID);
        if (source != null && source.Type == ActorType.Player)
        {
            return;
        }

        Record(_info?.StatusIDType, "status", status.ID, $"status {status.ID} on {Describe(actor)}");
    }

    private void Record(Type? enumType, string kind, uint id, string description)
    {
        if (id == 0)
        {
            return;
        }

        // A module that declares no enum of this kind has catalogued nothing here, so everything technically
        // diverges. That is true but drowns out the useful case, so it is opt-in.
        if (enumType == null)
        {
            if (!Config.ReportUndeclaredCategories)
            {
                return;
            }
        }
        else if (enumType.GetEnumName(id) != null)
        {
            return; // the module already knows about this one
        }

        if (_seen.Add((kind, id)))
        {
            _current.Add(new(kind, id, description));
        }
    }

    private static string Describe(Actor actor) => actor.Name.Length > 0 ? actor.Name : $"{actor.OID:X}";
}
