namespace BossMod.Dawntrail.Savage.M12SLindwurm;

sealed class RavenousReach1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.RavenousReach1, new AOEShapeCone(35f, 60f.Degrees()))
{
    private bool _curtainCall;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (spell.Action.ID == (uint)AID.GrotesquerieCurtainCall)
            _curtainCall = true;
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        base.OnCastFinished(caster, spell);
        if (spell.Action.ID == WatchedAction && Casters.Count == 0)
            _curtainCall = false;
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var aoes = base.ActiveAOEs(slot, actor);
        if (_curtainCall && actor.FindStatus((uint)SID.RottingFlesh) != null)
        {
            var mutable = CollectionsMarshal.AsSpan(Casters)[..aoes.Length];
            foreach (ref var aoe in mutable)
            {
                aoe.Color = Colors.Safe;
                aoe.Risky = false;
            }
        }
        return aoes;
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (!_curtainCall)
        {
            base.AddHints(slot, actor, hints);
            return;
        }

        var aoes = ActiveAOEs(slot, actor);
        if (aoes.IsEmpty)
            return;

        var enter = actor.FindStatus((uint)SID.RottingFlesh) != null;
        var inside = aoes[0].Check(actor.Position);
        hints.Add(enter ? "Spread inside the cone!" : "Spread outside the cone!", enter != inside);
    }
}
