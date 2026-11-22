using System.Text.Json;
using BossMod;
using Lindwurm=BossMod.Dawntrail.Savage.M12SLindwurm;
using Angra=BossMod.RealmReborn.Alliance.A31AngraMainyu;
using Amon=BossMod.RealmReborn.Alliance.A23Amon;
using Phlegethon=BossMod.RealmReborn.Alliance.A14Phlegethon;
using Thunder=BossMod.Stormblood.Alliance.A33ThunderGod;
using Pharos=BossMod.Heavensward.Dungeon.PharosSiriusHard;

int checks=0;
void Check(bool pass,string message)
{
    if(!pass)throw new Exception(message);
    checks++;
}
using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/saved-events.json")));
var module=new BossModule();
var actor=new Actor(){InstanceID=2};
foreach(var saved in fixture.RootElement.GetProperty("cones").EnumerateArray())
{
    var component=new Lindwurm.RavenousReach1(module);
    bool curtain=saved.GetProperty("curtain").GetBoolean();
    var origin=new WPos(saved.GetProperty("x").GetSingle(),saved.GetProperty("z").GetSingle());
    var rotation=new Angle(saved.GetProperty("rotation").GetSingle());
    var cast=new ActorCastInfo(new((uint)Lindwurm.AID.RavenousReach1),origin,rotation);
    if(curtain)component.OnEventCast(module.PrimaryActor,new(new((uint)Lindwurm.AID.GrotesquerieCurtainCall)));
    component.OnCastStarted(module.PrimaryActor,cast);
    actor.Statuses.Add((uint)Lindwurm.SID.RottingFlesh);
    actor.Position=origin+5f*rotation.ToDirection();
    var aoes=component.ActiveAOEs(0,actor);
    Check(aoes.Length==1 && aoes[0].Risky!=curtain,"Rotting Flesh uses role-specific cone risk");
    Check(!curtain||aoes[0].Color==Colors.Safe,"Required cone is highlighted");
    var hints=new TextHints();
    component.AddHints(0,actor,hints);
    Check(curtain?hints.Values.Any(h=>h.Text.Contains("inside")&&!h.Risk):hints.Values.Any(h=>h.Risk),"Inside-cone hint follows phase");
    actor.Statuses.Clear();
    aoes=component.ActiveAOEs(0,actor);
    Check(aoes[0].Risky,"Another player's safe cone cannot contaminate normal danger drawing");
    hints=new();
    component.AddHints(0,actor,hints);
    Check(hints.Values.Any(h=>h.Risk),"Unmarked player inside cone is warned");
    actor.Position=origin-5f*rotation.ToDirection();
    hints=new();
    component.AddHints(0,actor,hints);
    Check(curtain?hints.Values.Any(h=>!h.Risk):hints.Values.Count==0,"Opposite side avoids cone");
    component.OnCastFinished(module.PrimaryActor,cast);
    Check(component.ActiveAOEs(0,actor).IsEmpty,"Cone clears on completion or interruption");
    component.OnCastStarted(module.PrimaryActor,cast);
    actor.Statuses.Add((uint)Lindwurm.SID.RottingFlesh);
    Check(component.ActiveAOEs(0,actor)[0].Risky,"Later ordinary cone never inherits Curtain Call phase");
    actor.Statuses.Clear();
}
var swords=new Dictionary<uint,string>{{14153,"Move left"},{14154,"Move right"},{14156,"Rotate right"},{14157,"Rotate left"},{14159,"Move out"},{14160,"Move in"}};
foreach(var id in fixture.RootElement.GetProperty("swords").EnumerateArray().Select(e=>e.GetUInt32()).Concat(swords.Keys))
{
    if(!swords.TryGetValue(id,out var instruction))continue;
    var component=new Thunder.HolySword(module);
    var cast=new ActorCastInfo(new(id),default,default);
    module.PrimaryActor.CastInfo=cast;
    component.OnCastStarted(module.PrimaryActor,cast);
    var hints=new GlobalHints();
    component.AddGlobalHints(actor,hints);
    Check(hints.Values.Single().StartsWith(instruction),"Holy Sword instruction matches Cactbot event");
    component.OnCastFinished(module.PrimaryActor,cast);
    hints=new();
    component.AddGlobalHints(actor,hints);
    Check(hints.Values.Count==0,"Interrupted sword clears the countdown");
}
var vision=new Angra.DoubleVision(module);
var visionCast=new ActorCastInfo(new((uint)Angra.AID.DoubleVision),default,default);
vision.OnCastStarted(module.PrimaryActor,visionCast);
foreach(var (status,text) in new[]{(Angra.SID.BrandOfTheSullen,"red"),(Angra.SID.BrandOfTheIreful,"white")})
{
    actor.Statuses.Add((uint)status);
    var hints=new TextHints();
    vision.AddHints(0,actor,hints);
    Check(hints.Values.Single().Text.Contains(text),"Double Vision swaps to the opposite color");
    actor.Statuses.Clear();
}
var gaze=new Angra.MortalGaze(module);
foreach(var id in new[]{Angra.AID.MortalGaze1,Angra.AID.MortalGaze2})
{
    var cast=new ActorCastInfo(new((uint)id),default,default);
    gaze.OnCastStarted(module.PrimaryActor,cast);
    var hints=new TextHints();
    gaze.AddHints(0,actor,hints);
    Check(hints.Values.Any(h=>h.Text=="Look away"),"Boss and helper gaze both warn");
    gaze.OnCastFinished(module.PrimaryActor,cast);
}
actor.Statuses.Add((uint)Angra.SID.Doom);
var doomHints=new TextHints();
gaze.AddHints(0,actor,doomHints);
Check(doomHints.Values.Single().Text.Contains("cleanse"),"Doom instruction persists after gaze ends");
actor.Statuses.Clear();
var flare=new Angra.Level100Flare(module);
flare.OnEventIcon(actor,(uint)Angra.IconID.Level100Flare,actor.InstanceID);
Check(flare.Radius==12,"Flare uses game Action sheet's 12-yalm radius");
actor.Position=new(3,5);
Check(flare.Center==actor.Position,"Flare circle follows marker");
flare.OnEventCast(module.PrimaryActor,new(new((uint)Angra.AID.Level100Flare2)));
Check(flare.Targets.Count==0,"Flare marker clears on damage");
var death=new Angra.Level150Death(module);
Check(death.Radius==12,"Death uses game Action sheet's 12-yalm radius");
death.OnEventIcon(actor,(uint)Angra.IconID.Level150Death,actor.InstanceID);
actor.Position=new(8,9);
Check(death.Center==actor.Position,"Death circle follows marker");
death.OnEventCast(module.PrimaryActor,new(new((uint)Angra.AID.Level150Death2)));
Check(death.Targets.Count==0,"Death marker clears on resolution");
foreach(var duration in fixture.RootElement.GetProperty("phlegethon").EnumerateArray())
{
    var flareHint=new Phlegethon.AncientFlare1(module);
    var cast=new ActorCastInfo(new((uint)Phlegethon.AID.AncientFlare1),default,default){NPCRemainingTime=duration.GetSingle()};
    flareHint.OnCastStarted(module.PrimaryActor,cast);
    var hints=new GlobalHints();
    flareHint.AddGlobalHints(actor,hints);
    Check(flareHint.ShowCastTimeLeft&&hints.Values.Single().Contains("platform"),"Ancient Flare asks for platform with countdown");
    flareHint.OnCastFinished(module.PrimaryActor,cast);
    Check(!flareHint.Active,"Interrupted Ancient Flare clears its instruction");
}
foreach(var id in fixture.RootElement.GetProperty("progenitor").EnumerateArray())
{
    BossMod.Components.CastHint raidwide=id.GetUInt32()==5329?new Pharos.BlueFlame(module):new Pharos.BigBurst(module);
    var cast=new ActorCastInfo(new(id.GetUInt32()),default,default);
    raidwide.OnCastStarted(module.PrimaryActor,cast);
    var hints=new GlobalHints();
    raidwide.AddGlobalHints(actor,hints);
    Check(raidwide is BossMod.Components.RaidwideCast && hints.Values.Single()=="Raidwide","Progenitor's two recorded actions warn for raidwide damage");
}
module.EnemiesByOID[(uint)Amon.OID.IceCage]=[new(){IsDead=true},new(){Position=new(2,3)},new(){Position=new(3,4)}];
var cover=new Amon.CurtainCall(module);
Check(cover.BlockerActors().Length==2,"Amon excludes destroyed ice cover marked dead");

using(var coverData=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/amon-cover.json"))))
foreach(var cast in coverData.RootElement.EnumerateArray())
foreach(var point in cast.GetProperty("points").EnumerateArray())
{
    float x=point.GetProperty("x").GetSingle(),z=point.GetProperty("z").GetSingle();
    double distance=Math.Sqrt(x*x+z*z);
    bool safe=false;
    foreach(var b in cast.GetProperty("blockers").EnumerateArray())
    {
        double bx=b[0].GetDouble(),bz=b[1].GetDouble(),r=b[2].GetDouble();
        if(distance>Math.Sqrt(bx*bx+bz*bz)+.2 && bx*x+bz*z>0 && Math.Abs(bx*z-bz*x)/distance<r)
            safe=true;
    }
    Check(safe==point.GetProperty("safe").GetBoolean(),"Ice shadow calculation agrees with recorded geometry");
    Check(!safe||!point.GetProperty("damage").GetBoolean(),"Recorded Curtain Call did not damage a player in predicted cover");
}
Console.WriteLine(checks+" encounter checks passed.");

