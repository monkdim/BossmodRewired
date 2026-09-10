namespace BossMod.Global.CrucibleOfTheUnbroken.SecondBoard.LooserfroxInkyjots;

public enum OID : uint
{
    LoosefroxInkyjots = 0x4C65,
    Helper = 0x233C,
}

sealed class LoosefroxInkyjotsStates : StateMachineBuilder
{
    public LoosefroxInkyjotsStates(BossModule module) : base(module)
    {
        TrivialPhase();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Dummy, PrimaryActorOID = (uint)OID.LoosefroxInkyjots, Contributors = "Equilius",
    GroupType = BossModuleInfo.GroupType.CrucibleOfTheUnbroken,
    GroupID = 1089u, NameID = 14561u, SortOrder = 5)]
public sealed class LoosefroxInkyjots(WorldState ws, Actor primary) : BossModule(ws, primary, new(520f, -420f), new ArenaBoundsCircle(22f));

