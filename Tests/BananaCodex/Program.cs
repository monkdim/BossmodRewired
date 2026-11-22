using BossMod;
using BossMod.Dawntrail.Savage.M12S2Lindwurm;
using System.Reflection;

int checks=0;
void Check(bool pass,string message)
{
    if(!pass)throw new Exception(message);
    checks++;
    Console.WriteLine("PASS "+message);
}
var module=new BossModule();
foreach(var player in module.Raid.Actors) player.Position=module.Center;
var staging=new Replication2Staging();
module.Components[typeof(Replication2Staging)]=staging;
var guide=new BananaCodexGuidance(module);
var method=typeof(BananaCodexGuidance).GetMethod("Destination",BindingFlags.NonPublic|BindingFlags.Instance)!;
(WPos? Position,string Instruction) Destination(int slot=0)
    => ((WPos?,string))method.Invoke(guide,[slot,module.Raid.Actors[slot]])!;
void Start(AID id) => guide.OnCastStarted(module.PrimaryActor,new(new((uint)id)));
void Hit(AID id) => guide.OnEventCast(module.PrimaryActor,new(new((uint)id)));
void ResetDrawing(){module.Arena.Markers.Clear();module.Arena.Lines.Clear();}

Check(Destination().Position==null,"No personal destination before clone assignment");
staging.PlayersBySlot[0]=new(){WantedRole=Replication2Role.Cone1};
var carrier=module.Raid.Actors[1];
carrier.Position=new(110,95);
staging.WurmClones.Add(new(){AssignedRole=Replication2Role.Cone1,Target=carrier});
Check(Destination().Position==carrier.Position,"Tether acquisition points at current carrier");
carrier.Position=new(108,93);
Check(Destination().Position==carrier.Position,"Tether acquisition follows moving carrier");
staging.WurmsAssigned=true;
Start(AID.FirefallSplashCast);
Check(Destination().Position==new WPos(119,100) && Destination().Instruction.Contains("No tether"),"Unassigned eighth player gets east defamation marker");
foreach(var role in Enum.GetValues<Replication2Role>())
{
    staging.WurmsBySlot[0]=new(){AssignedRole=role};
    var p=Destination().Position!.Value;
    Check(module.Bounds.Contains(p-module.Center),role+" opening marker stays in arena");
    ResetDrawing();
    guide.DrawArenaForeground(0,module.Raid.Actors[0]);
    Check(module.Arena.Markers.Count==1 && module.Arena.Lines.Count==1,role+" gets one personal marker and line");
}
var baitRoles=new[]{Replication2Role.Cone1,Replication2Role.Stack1,Replication2Role.Stack2,Replication2Role.Cone2};
var boss=module.Center+BananaCodexPositions.Opening(Replication2Role.Boss);
foreach(var role in baitRoles)
{
    var bait=module.Center+BananaCodexPositions.Opening(role);
    Check(!bait.InCircle(boss,5),"Jump splash misses "+role);
}
foreach(var role in new[]{Replication2Role.None,Replication2Role.Defam1,Replication2Role.Defam2})
{
    var spread=module.Center+BananaCodexPositions.Opening(role);
    Check(!boss.InCircle(spread,20),"Defamation misses jump target for "+role);
    foreach(var other in Enum.GetValues<Replication2Role>().Where(r=>r!=role))
        Check(!(module.Center+BananaCodexPositions.Opening(other)).InCircle(spread,20),"Defamation separation "+role+"/"+other);
}
staging.WurmsBySlot[0]=null;
staging.PlayersBySlot[1]=new(){WantedRole=Replication2Role.Stack2};
staging.WurmsBySlot[1]=new(){AssignedRole=Replication2Role.Stack2};
var slam=new Replication2HeavySlam();
slam.Stacks.Add(new(carrier));
module.Components[typeof(Replication2HeavySlam)]=slam;
Hit(AID.ManaBurstAOE);
Check(Destination().Instruction.Contains("defamation"),"Wait until all three defamations resolve");
Hit(AID.ManaBurstAOE);
Hit(AID.ManaBurstAOE);
Check(Destination().Position==carrier.Position,"No-tether player joins south light-party stack");
carrier.Position=new(92,103);
Check(Destination().Position==carrier.Position,"Stack destination follows actual stack target");
Hit(AID.HeavySlam);
Check(Destination().Position==carrier.Position,"First stack event keeps stack guidance active");
slam.NumCasts=2;
Hit(AID.HeavySlam);
Check(Destination().Instruction.Contains("Avoid the player cones"),"Both stacks resolving advances to projection");
staging.WurmsBySlot[0]=new(){AssignedRole=Replication2Role.Cone1};
Check(Destination().Instruction.Contains("face away"),"Projection player receives facing instruction");
var projection=new Replication2HemorrhagicProjection(){NumCasts=1};
module.Components[typeof(Replication2HemorrhagicProjection)]=projection;
Hit(AID.HemorrhagicProjection);
Check(Destination().Instruction.Contains("face away"),"First projection keeps the second cone active");
Hit(AID.HemorrhagicProjection);
module.PrimaryActor.CastInfo=new(new((uint)AID.SnakingKick),new(0));
Check(Destination().Position==new WPos(90,92),"Kick marker uses boss cast facing");
Start(AID.NetherwrathNear);
Check(Destination().Instruction.Contains("join your pair"),"Cone joins near-stack partner");
Start(AID.NetherwrathFar);
Check(Destination().Instruction.Contains("bait your pair"),"Cone baits far stack");
var near1=BananaCodexPositions.Netherwrath(Replication2Role.Stack1,boss);
var near2=BananaCodexPositions.Netherwrath(Replication2Role.Stack2,boss);
var far1=BananaCodexPositions.Netherwrath(Replication2Role.Cone1,boss);
var far2=BananaCodexPositions.Netherwrath(Replication2Role.Cone2,boss);
Check(near1.InCircle(far1,6) && near2.InCircle(far2,6),"Both pairs fit stack radius");
Check(!near1.InCircle(near2,6) && !far1.InCircle(near2,6) && !far2.InCircle(near1,6),"Opposite pair stays outside each stack");
staging.WurmsBySlot[0]=null;
Hit(AID.TimelessSpite);
var towers=new Replication2ReenactmentTowers();
towers.Towers.Add(new(new(86,114),module.WorldState.FutureTime(2)));
towers.Towers.Add(new(new(86,86),module.WorldState.FutureTime(6)));
module.Components[typeof(Replication2ReenactmentTowers)]=towers;
Check(Destination().Position==new WPos(86,114),"No-tether player sees current southwest tower");
towers.Towers.RemoveAt(0);
module.WorldState.Now=module.WorldState.Now.AddSeconds(4);
Check(Destination().Position==new WPos(86,86),"Tower guidance advances northwest after first resolution");
var aoes=new Replication2ReenactmentAOEs(){AOEs=[new(new(86,86))]};
module.Components[typeof(Replication2ReenactmentAOEs)]=aoes;
ResetDrawing();
guide.DrawArenaForeground(0,module.Raid.Actors[0]);
Check(module.Arena.Markers.Count==0,"Replay hazard vetoes a destination marker");
Service.Config.Current.Rep2Strategy=Replication2Strategy.DN;
Check(Destination().Position==null && string.IsNullOrEmpty(Destination().Instruction),"DN selection disables Banana personal guidance");

Service.Config.Current.Rep2Strategy=Replication2Strategy.BananaCodex;
staging.PlayersAssigned=false;
Check(Destination().Position==null,"Incomplete player assignments cannot infer no-tether role");
staging.PlayersAssigned=true;
Start(AID.FirefallSplashCast);
guide.OnEventCast(module.PrimaryActor,new(new((uint)AID.FirefallSplash),new(110,100)));
Check(Destination().Position==null && Destination().Instruction.Contains("off position"),"Off-strategy jump suppresses stale template marker");
Start(AID.FirefallSplashCast);
module.Raid.Actors[0].IsDead=true;
Check(Destination().Position==null,"Dead players receive no destination");
module.Raid.Actors[0].IsDead=false;
int windows=0, jumps=0;
using(var data=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/replication2-events.json"))))
foreach(var pull in data.RootElement.EnumerateArray())
{
    var replay=new BananaCodexGuidance(module);
    var replayMethod=method;
    slam.NumCasts=0;
    projection.NumCasts=0;
    foreach(var e in pull.GetProperty("events").EnumerateArray())
    {
        var aid=(AID)e.GetProperty("action").GetUInt32();
        if(e.GetProperty("start").GetBoolean())
            replay.OnCastStarted(module.PrimaryActor,new(new((uint)aid)));
        else
        {
            if(aid==AID.HeavySlam)slam.NumCasts++;
            if(aid==AID.HemorrhagicProjection)projection.NumCasts++;
            replay.OnEventCast(module.PrimaryActor,new(new((uint)aid),new(90,100)));
        }
    }
    windows++;
}
Check(windows==34,"Replayed event order from all 34 newer Replication 2 windows");
using(var data=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/jump-landings.json"))))
foreach(var landing in data.RootElement.EnumerateArray())
{
    Start(AID.FirefallSplashCast);
    var point=new WPos(landing[0].GetSingle(),landing[1].GetSingle());
    guide.OnEventCast(module.PrimaryActor,new(new((uint)AID.FirefallSplash),point));
    var destination=Destination();
    if(!point.InCircle(boss,3))
        Check(destination.Position==null,"Captured off-position jump vetoes fixed spot");
    else
        foreach(var role in baitRoles)
            Check(!(module.Center+BananaCodexPositions.Opening(role)).InCircle(point,5),"Bait spot avoids captured west jump splash");
    jumps++;
}
Check(jumps==33,"Checked all 33 captured jump resolutions");

using(var data=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/netherwrath-bosses.json"))))
foreach(var cast in data.RootElement.EnumerateArray())
{
    module.PrimaryActor.Position=new(cast.GetProperty("x").GetSingle(),cast.GetProperty("z").GetSingle());
    Start(cast.GetProperty("far").GetBoolean()?AID.NetherwrathFar:AID.NetherwrathNear);
    foreach(var role in Enum.GetValues<Replication2Role>())
    {
        staging.WurmsBySlot[0]=role==Replication2Role.None?null:new(){AssignedRole=role};
        var plan=Destination();
        Check(plan.Position is not {} point || module.Bounds.Contains(point-module.Center),"Netherwrath marker stays in arena at captured boss position");
    }
}
Console.WriteLine(checks+" checks passed.");





