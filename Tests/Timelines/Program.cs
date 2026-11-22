using BossMod;
using BossMod.Timelines;
using System.Reflection;
int checks=0;
void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks++; }
var input = """
0 "Comment" #Ability { id: "AA" } jump 90
1 "Opener" StartsUsing { id: "AB", source: ["Helper", "Boss"] }
10 "Fork" Ability { id: "AC" } window 2,4 jump "branch"
20 "Wrong branch"
50 label "branch"
51 "Next"
55 "--sync--" forcejump 60
61 "Finish"
""";
var timeline = CactbotTimeline.Parse("test", new StringReader(input));
Check(!timeline.Entries[0].IsCastSync && timeline.Entries[0].Abilities.Single()==0xAA && timeline.Entries[0].JumpTo==null, "Commented cue retains ability metadata but cannot synchronize");
Check(timeline.Entries[2].JumpTo==50, "Forward label resolves");
TimelineLibrary.Loaded=timeline;
var ws=new WorldState();
var actor=new Actor();
using var tracker=new TimelineTracker(ws);
void Resolve(uint id) => ws.Actors.CastEvent.Fire(actor,new(new(ActionType.Spell,id)));
void Start(uint id) { actor.CastInfo=new(new(ActionType.Spell,id)); ws.Actors.CastStarted.Fire(actor); }
void Time(double seconds) { ws.CurrentTime=DateTime.UnixEpoch.AddSeconds(seconds); tracker.Update(); }
float Now() => (float)typeof(TimelineTracker).GetMethod("Now",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(tracker,null)!;
Resolve(0xAA);
Resolve(0xAB);
Check(!tracker.Running,"Comment and wrong event type do not anchor");
actor.Name="Wrong caster"; Start(0xAB); Check(!tracker.Running,"Wrong source cannot anchor"); actor.Name="Boss"; Start(0xAB);
Check(tracker.Running && Now()==1,"Matching cast start anchors");
Time(5);
Resolve(0xAC);
Check(Now()==6,"Outside asymmetric sync window is ignored");
Time(12);
Resolve(0xAC);
Check(Now()==50,"Inside asymmetric window jumps to branch");
Time(17.5);
Check(Now()==60.5,"Forcejump advances when crossing its timestamp");
ws.Party.Player().InCombat=false;
tracker.Update();
Check(!tracker.Running,"Leaving combat clears timer");
var dir=args[0];
var files=Directory.GetFiles(dir,"*.txt").Where(p=>Path.GetFileName(p) is not ("ZoneTimelines.txt" or "LICENSE.txt")).ToArray();
int entryCount=0, syncs=0, jumps=0;
foreach (var path in files) {
    using var stream=File.OpenText(path);
    var t=CactbotTimeline.Parse(Path.GetFileName(path),stream);
    entryCount+=t.Entries.Count;
    syncs+=t.Entries.Count(e=>e.IsCastSync && e.Abilities.Length>0);
    jumps+=t.Entries.Count(e=>e.JumpTo.HasValue);
}
Console.WriteLine($"Parsed {files.Length} timeline files, {entryCount} entries, {syncs} cast syncs, {jumps} resolved jump destinations.");


var quoted=CactbotTimeline.Parse("quoted",new StringReader("""10 "A" Ability { id: ['B7[67]', "AB"], source: 'Boss' }"""));
Check(quoted.Entries.Single().Abilities.Order().SequenceEqual(new uint[]{0xAB,0xB76,0xB77}.Order()),"Single-quoted IDs and nested character classes parse");
var branchTimeline=CactbotTimeline.Parse("branches",new StringReader("""
0 "--sync--" InCombat { inGameCombat: '1' } window 0,1
0 "--reset--" ActorControl { command: "4000000F" } window 0,100000 jump 0
0 "--phase--" ActorControl { command: '80000027', data0: '15', data1: '02', data2: '330A' } window 0,1 jump "phase2"
3000.5 label "phase2"
3002 "--map--" MapEffect { flags: '00020001', location: '0B' } window 2,2
3004 "--sync--" forcejump "next"
3005 "Wrong branch"
3050 label "next"
3051 "Correct branch"
"""));
TimelineLibrary.Loaded=branchTimeline;
var world=new WorldState();
world.Party.Player().InCombat=false;
using(var live=new TimelineTracker(world))
{
    live.Update();
    world.DirectorUpdate.Fire(new(0x80000027,0x15,2,0x330A));
    live.Update();
    Check(!live.Running,"Phase cue received out of combat waits for the pull");
    world.Party.Player().InCombat=true;
    live.Update();
    var entries=new List<TimelineTracker.Upcoming>();
    live.CollectUpcoming(entries,10,8);
    Check(live.Running && entries.Any(e=>e.Name=="Correct branch") && entries.All(e=>e.Name!="Wrong branch"),"Pending phase-two cue survives combat start and lookahead follows forcejump");
    world.CurrentTime=world.CurrentTime.AddSeconds(1);
    world.MapEffect.Fire(new(11,0x00020001));
    entries.Clear();
    live.CollectUpcoming(entries,10,8);
    Check(entries.Any(e=>e.Name=="Correct branch" && Math.Abs(e.In-3)<.01),"Map effect synchronizes the timer");
    world.CurrentTime=world.CurrentTime.AddSeconds(3);
    live.Update();
    entries.Clear();
    live.CollectUpcoming(entries,10,8);
    Check(entries.All(e=>e.Name!="Wrong branch"),"Crossed forcejump stays on the correct branch");
    world.DirectorUpdate.Fire(new(0x4000000F));
    Check(!live.Running,"Wipe director event stops the timer");
}


int replays=0, eventsReplayed=0, curtainSyncs=0, phaseTwoSyncs=0, resetChecks=0;
foreach(var fixture in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Fixtures"),"replay-*.json"))
{
    using var data=System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixture));
    using var timelineFile=File.OpenText(Path.Combine(dir,data.RootElement.GetProperty("timeline").GetString()+".txt"));
    TimelineLibrary.Loaded=CactbotTimeline.Parse("recorded",timelineFile);
    var state=new WorldState();
    state.Party.Player().InCombat=false;
    using var live=new TimelineTracker(state);
    var caster=new Actor();
    foreach(var e in data.RootElement.GetProperty("events").EnumerateArray())
    {
        state.CurrentTime=DateTime.UnixEpoch.AddSeconds(e.GetProperty("time").GetDouble());
        live.Update();
        string kind=e.GetProperty("kind").GetString()!;
        uint id=e.TryGetProperty("action",out var action)?action.GetUInt32():0;
        switch(kind)
        {
            case "combat":
                state.Party.Player().InCombat=e.GetProperty("combat").GetBoolean();
                break;
            case "start":
                caster.Name=e.GetProperty("source").GetString()!;
                caster.CastInfo=new(new(ActionType.Spell,id));
                state.Actors.CastStarted.Fire(caster);
                break;
            case "resolve":
                caster.Name=e.GetProperty("source").GetString()!;
                state.Actors.CastEvent.Fire(caster,new(new(ActionType.Spell,id)));
                break;
            case "director":
                var command=e.GetProperty("command").GetUInt32();
                state.DirectorUpdate.Fire(new(command,e.GetProperty("p1").GetUInt32(),e.GetProperty("p2").GetUInt32(),e.GetProperty("p3").GetUInt32(),e.GetProperty("p4").GetUInt32()));
                if(command==0x4000000F)
                {
                    Check(!live.Running,"Recorded wipe stops the live clock");
                    resetChecks++;
                }
                break;
            case "map":
                state.MapEffect.Fire(new(e.GetProperty("index").GetByte(),e.GetProperty("state").GetUInt32()));
                break;
        }
        live.Update();
        if(live.Running && kind=="resolve" && id==0xBEC0)
        {
            var time=(float)typeof(TimelineTracker).GetMethod("Now",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(live,null)!;
            Check(Math.Abs(time-251.7f)<.1,"Recorded Curtain Call synchronizes to its timeline entry");
            curtainSyncs++;
        }
        if(live.Running && kind=="start" && id==0xB528)
        {
            var time=(float)typeof(TimelineTracker).GetMethod("Now",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(live,null)!;
            Check(Math.Abs(time-3010.7f)<.1,"Recorded phase-two opener synchronizes to phase two");
            phaseTwoSyncs++;
        }
        var upcoming=new List<TimelineTracker.Upcoming>();
        live.CollectUpcoming(upcoming,5,25);
        Check(upcoming.All(u=>float.IsFinite(u.In)&&u.In>0&&u.In<=25),"Recorded event stream produces bounded countdowns");
        eventsReplayed++;
    }
    replays++;
}
Check(replays>0&&curtainSyncs>0&&phaseTwoSyncs>0&&resetChecks>0,"Saved pulls exercise Curtain Call, phase-two transitions and wipe resets");
Console.WriteLine($"Replayed {eventsReplayed} events from {replays} recordings. Curtain Call syncs {curtainSyncs}, phase-two opener syncs {phaseTwoSyncs}, wipe resets {resetChecks}.");



TimelineLibrary.Loaded=CactbotTimeline.Parse("late force sync",new StringReader("""
1 "Start" StartsUsing { id: "AB" }
10 "--sync--" Ability { id: "D0" } window 1,2 forcejump 20
21 "Next"
"""));
var lateWorld=new WorldState();
using(var lateTracker=new TimelineTracker(lateWorld))
{
    var caster=new Actor(){CastInfo=new(new(ActionType.Spell,0xAB))};
    lateWorld.Actors.CastStarted.Fire(caster);
    lateWorld.CurrentTime=lateWorld.CurrentTime.AddSeconds(9.5);
    lateTracker.Update();
    lateWorld.Actors.CastEvent.Fire(caster,new(new(ActionType.Spell,0xD0)));
    var upcoming=new List<TimelineTracker.Upcoming>();
    lateTracker.CollectUpcoming(upcoming,2,5);
    Check(upcoming.Single().Name=="Next" && Math.Abs(upcoming.Single().In-1)<.01,"Late forcejump event uses the translated sync window");
}

var ranged=CactbotTimeline.Parse("ranges",new StringReader("""2 "Orb" Ability { id: ["4F8[5-7]"], source: "Boss" }""" ));
Check(ranged.Abilities.SetEquals(new uint[]{0x4F85,0x4F86,0x4F87}),"Hex character ranges retain the middle action");
Console.WriteLine(checks+" timeline checks passed.");
