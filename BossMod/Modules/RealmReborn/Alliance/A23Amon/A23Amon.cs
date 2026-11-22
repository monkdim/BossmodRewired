namespace BossMod.RealmReborn.Alliance.A23Amon;

class BlizzagaForte(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BlizzagaForte, 10f);
class Darkness(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Darkness, new AOEShapeCone(6f, 22.5f.Degrees()));

class ThundagaForte1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ThundagaForte1, 6f);
class ThundagaForte2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ThundagaForte2, 6f);

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 102, NameID = 2821)]
public class A23Amon(WorldState ws, Actor primary) : BossModule(ws, primary, new(default, -200f), new ArenaBoundsCircle(30f))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.KumKum));
        Arena.Actors(Enemies((uint)OID.Kichiknebik));
        Arena.Actors(Enemies((uint)OID.ExperimentalByProduct66));
        Arena.Actors(Enemies((uint)OID.ExperimentalByProduct33));
    }
}
