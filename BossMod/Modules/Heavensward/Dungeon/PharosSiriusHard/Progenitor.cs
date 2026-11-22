namespace BossMod.Heavensward.Dungeon.PharosSiriusHard;

sealed class ProgenitorStates : StateMachineBuilder
{
    public ProgenitorStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BlueFlame>()
            .ActivateOnEnter<BigBurst>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(ProgenitorStates), ObjectIDType = typeof(OID), ActionIDType = typeof(AID),
    PrimaryActorOID = (uint)OID.Boss, Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Dungeon)]
public sealed class Progenitor(WorldState ws, Actor primary) : BossModule(ws, primary, new(-98.8f, -288.1f), new ArenaBoundsCircle(20));
