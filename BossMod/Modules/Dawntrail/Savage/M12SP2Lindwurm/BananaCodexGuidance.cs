namespace BossMod.Dawntrail.Savage.M12S2Lindwurm;

// Replication 2 positions are relative to the arena center, with the boss jump west.
// The assignment comes from locked tethers; the unassigned eighth player has role None.
sealed class BananaCodexGuidance(BossModule module) : BossComponent(module)
{
    private enum Step { Tethers, Baits, Stacks, Projection, Kick, Reenactment, Netherwrath, Replay }
    private Step _step;
    private bool _far;
    private WPos? _jumpPosition;
    private int _defamationCasts;
    private int _stackCasts;
    private int _projectionCasts;
    private readonly Replication2Staging _staging = module.FindComponent<Replication2Staging>()!;
    private bool Enabled => Service.Config.Get<M12S2LindwurmConfig>().Rep2Strategy == Replication2Strategy.BananaCodex;

    private Replication2Role? Role(int slot)
        => !_staging.PlayersAssigned || !_staging.WurmsAssigned || _staging.PlayersBySlot[slot] == null
            ? null : _staging.WurmsBySlot[slot]?.AssignedRole ?? Replication2Role.None;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch ((AID)spell.Action.ID)
        {
            case AID.FirefallSplashCast:
                _jumpPosition = null;
                _defamationCasts = _stackCasts = _projectionCasts = 0;
                _step = Step.Baits;
                break;
            case AID.SnakingKick:
                _step = Step.Kick;
                break;
            case AID.Reenactment:
                _step = Step.Reenactment;
                break;
            case AID.NetherwrathNear:
            case AID.NetherwrathFar:
                _far = spell.Action.ID == (uint)AID.NetherwrathFar;
                _step = Step.Netherwrath;
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch ((AID)spell.Action.ID)
        {
            case AID.FirefallSplash:
                _jumpPosition = spell.TargetXZ;
                break;
            case AID.ManaBurstAOE:
                if (++_defamationCasts >= 3)
                    _step = Step.Stacks;
                break;
            case AID.HeavySlam:
                if (++_stackCasts >= 2)
                    _step = Step.Projection;
                break;
            case AID.HemorrhagicProjection:
                if (++_projectionCasts >= 2)
                    _step = Step.Kick;
                break;
            case AID.SnakingKick:
                _step = Step.Reenactment;
                break;
            case AID.TimelessSpite:
                _step = Step.Replay;
                break;
        }
    }

    private (WPos? Position, string Instruction) Destination(int slot, Actor actor)
    {
        var plan = Plan(slot, actor);
        if (plan.Position is not { } position)
            return plan;
        if (!Module.Bounds.Contains(position - Module.Center))
            return (null, "Stay inside the arena; adjust for the boss position");
        var aoes = Module.FindComponent<Replication2ReenactmentAOEs>();
        if (_step == Step.Replay && aoes != null)
            foreach (var aoe in aoes.ActiveAOEs(slot, actor))
                if (aoe.Risky && aoe.Check(position))
                    return (null, "Wait for the replay AOE to clear, then move to your next spot");
        return plan;
    }

    private (WPos? Position, string Instruction) Plan(int slot, Actor actor)
    {
        if (!Enabled || actor.IsDead)
            return default;

        if (_step == Step.Tethers && _staging.WurmsAssigned)
            return (null, "Tethers locked; prepare for your jump, protean or defamation");

        if (_step == Step.Tethers)
        {
            var wanted = _staging.PlayersBySlot[slot]?.WantedRole;
            if (wanted == null)
                return (null, "Wait for your clone assignment");
            if (wanted == Replication2Role.None)
                return (null, "Take no tether; wait for tethers to lock");
            foreach (var clone in _staging.WurmClones)
                if (clone.AssignedRole == wanted && !clone.Locked && clone.Target != null)
                    return (clone.Target.Position, $"{(clone.Target == actor ? "Keep" : "Take")} the {TetherName(wanted.Value)} tether");
            return (null, "Wait for your tether to appear");
        }

        if (Role(slot) is not { } role)
            return (null, "Wait for tethers to lock");

        switch (_step)
        {
            case Step.Baits:
                if (_jumpPosition is { } jump && !jump.InCircle(Module.Center + BananaCodexPositions.Opening(Replication2Role.Boss), 3f))
                    return (null, "Boss jump landed off position; adjust to the live proteans");
                var opening = Module.Center + BananaCodexPositions.Opening(role);
                if (_jumpPosition is { } landed && (role.IsCone || role.IsStack))
                {
                    // Follow a small landing displacement without changing the bait order.
                    var shifted = opening + (landed - (Module.Center + BananaCodexPositions.Opening(Replication2Role.Boss)));
                    if (Module.Bounds.Contains(shifted - Module.Center))
                        opening = shifted;
                }
                return (opening,
                    role switch
                    {
                        Replication2Role.Boss => _jumpPosition.HasValue
                            ? "Hold the west jump position; wait for the defamations"
                            : "Bait the boss jump west; stay clear of the four baiters",
                        Replication2Role.None => "No tether: spread east for your defamation",
                        Replication2Role.Defam1 => "Defamation: spread northeast",
                        Replication2Role.Defam2 => "Defamation: spread southeast",
                        _ => "Bait your protean west; keep your own lane"
                    });
            case Step.Stacks:
                var stackRole = role is Replication2Role.Boss or Replication2Role.Cone1 or Replication2Role.Stack1 or Replication2Role.Defam1
                    ? Replication2Role.Stack1 : Replication2Role.Stack2;
                foreach (var stack in Module.FindComponent<Replication2HeavySlam>()?.Stacks ?? [])
                {
                    var stackSlot = Raid.FindSlot(stack.Target.InstanceID);
                    if (stackSlot >= 0 && Role(stackSlot) == stackRole)
                        return (stack.Target.Position, stack.Target == actor ? "Hold your stack; let your group join" : "Join your marked light-party stack");
                }
                return (null, "Join your light-party stack");
            case Step.Projection:
                return role.IsCone
                    ? (actor.Position, "Hold your cone bait; face away from the party")
                    : (null, "Avoid the player cones; prepare to get behind the boss");
            case Step.Kick:
                if (Module.PrimaryActor.CastInfo is { } kick && kick.Action.ID == (uint)AID.SnakingKick)
                    return (Module.PrimaryActor.Position - 8f * kick.Rotation.ToDirection(), "Get behind the boss for Snaking Kick");
                return (null, "Get behind the boss for Snaking Kick");
            case Step.Reenactment:
                return (null, "Reenactment: prepare for the close/far stack");
            case Step.Netherwrath:
                return (BananaCodexPositions.Netherwrath(role, Module.PrimaryActor.Position),
                    role.IsCone || role.IsStack
                        ? $"{(_far ? "Far" : "Near")} stack: {(role.IsCone == _far ? "bait" : "join")} your pair"
                        : "Wait inside the west side of the boss hitbox; then take the southwest tower");
            case Step.Replay:
                return ReplayDestination(slot, actor, role);
            default:
                return default;
        }
    }

    private static string TetherName(Replication2Role role) => role switch
    {
        Replication2Role.Boss => "boss jump",
        Replication2Role.Cone1 => "north cone",
        Replication2Role.Cone2 => "south cone",
        Replication2Role.Stack1 => "north stack",
        Replication2Role.Stack2 => "south stack",
        Replication2Role.Defam1 => "northeast defamation",
        Replication2Role.Defam2 => "southeast defamation",
        _ => "unassigned"
    };

    private (WPos? Position, string Instruction) ReplayDestination(int slot, Actor actor, Replication2Role role)
    {
        if (!role.IsCone && !role.IsStack)
        {
            var towers = Module.FindComponent<Replication2ReenactmentTowers>()?.Towers;
            if (towers is { Count: > 0 })
            {
                var first = towers[0];
                foreach (var tower in towers)
                    if (tower.Activation < first.Activation)
                        first = tower;
                // Only the current pair belongs to this step; future towers are previews.
                if (first.Activation <= WorldState.FutureTime(5))
                    return (first.Position, "Soak the current clone tower; then move to the next");
            }
        }

        var proteans = Module.FindComponent<Replication2ReenactmentScaldingWaves>();
        if (proteans != null)
            foreach (var bait in proteans.CurrentBaits)
                if (bait.Target == actor)
                    return (Module.Center + BananaCodexPositions.Opening(role), "Bait your replay protean west; keep your lane");

        return (null, role.IsCone || role.IsStack ? "Avoid clone towers; wait for your west protean" : "Wait for the next clone tower; avoid replay AOEs");
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        var (position, instruction) = Destination(slot, actor);
        if (!string.IsNullOrEmpty(instruction))
            hints.Add(instruction, position is { } p && !actor.Position.InCircle(p, 2f));
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var (position, _) = Destination(pcSlot, pc);
        if (position is not { } target || !Module.Bounds.Contains(target - Module.Center))
            return;

        Arena.AddLine(pc.Position, target, Colors.Safe);
        Arena.ZoneCircleOutline(target, 1.5f, pc.Position.InCircle(target, 2f) ? Colors.Safe : Colors.Vulnerable);
        if (_step == Step.Projection && Role(pcSlot) is { } role && role.IsCone)
            Arena.AddLine(pc.Position, pc.Position + 8f * pc.Rotation.ToDirection(), Colors.Vulnerable);
    }
}

static class BananaCodexPositions
{
    // The boss lands eight yalms east of the bait line. Its five-yalm splash
    // must not overlap the nearest four players when the proteans snapshot.
    public static WDir Opening(Replication2Role role) => role switch
    {
        Replication2Role.Boss => new(-10, 0),
        Replication2Role.Cone1 => new(-18, -6),
        Replication2Role.Stack1 => new(-18, -2),
        Replication2Role.Stack2 => new(-18, 2),
        Replication2Role.Cone2 => new(-18, 6),
        Replication2Role.Defam1 => new(3, -19),
        Replication2Role.Defam2 => new(3, 19),
        _ => new(19, 0)
    };

    public static WPos Netherwrath(Replication2Role role, WPos boss) => role switch
    {
        Replication2Role.Cone1 => boss + new WDir(0, -9),
        Replication2Role.Stack1 => boss + new WDir(0, -4),
        Replication2Role.Stack2 => boss + new WDir(0, 4),
        Replication2Role.Cone2 => boss + new WDir(0, 9),
        _ => boss + new WDir(-5, 0)
    };
}
