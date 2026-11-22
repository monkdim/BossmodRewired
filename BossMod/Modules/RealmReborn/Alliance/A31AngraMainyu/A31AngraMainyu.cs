namespace BossMod.RealmReborn.Alliance.A31AngraMainyu;

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 111, NameID = 3231)]
public class A31AngraMainyu(WorldState ws, Actor primary) : BossModule(ws, primary, new(-145, 300), new ArenaBoundsCircle(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.FinalHourglass));
        Arena.Actors(Enemies((uint)OID.GrimReaper));
        Arena.Actors(Enemies((uint)OID.AngraMainyusDaewa));
    }
}
