namespace BossMod
{
    readonly record struct WDir(float X,float Z)
    {
        public static WDir operator *(float a,WDir b)=>new(a*b.X,a*b.Z);
    }
    readonly record struct WPos(float X,float Z)
    {
        public static WPos operator +(WPos p,WDir d)=>new(p.X+d.X,p.Z+d.Z);
        public static WPos operator -(WPos p,WDir d)=>new(p.X-d.X,p.Z-d.Z);
        public static WDir operator -(WPos p,WPos q)=>new(p.X-q.X,p.Z-q.Z);
        public bool InCircle(WPos p,float r)=>(this-p) is var d && d.X*d.X+d.Z*d.Z<=r*r;
    }
    readonly record struct Angle(float Rad) { public WDir ToDirection()=>new(MathF.Sin(Rad),MathF.Cos(Rad)); }
    record ActionID(uint ID);
    record ActorCastInfo(ActionID Action,Angle Rotation=default);
    record ActorCastEvent(ActionID Action, WPos TargetXZ = default);
    class Actor { public ulong InstanceID; public WPos Position; public Angle Rotation; public bool IsDead; public ActorCastInfo? CastInfo; }
    class TextHints { public readonly List<(string,bool)> Values=[]; public void Add(string s,bool risk=true)=>Values.Add((s,risk)); }
    class Arena {
        public readonly List<WPos> Markers=[];
        public readonly List<(WPos,WPos)> Lines=[];
        public void ZoneCircleOutline(WPos p,float radius,uint color)=>Markers.Add(p);
        public void AddLine(WPos p,WPos q,uint color)=>Lines.Add((p,q));
    }
    static class Colors { public const uint Safe=1,Vulnerable=2; }
    static class UICombo { public static string EnumString<T>(T v)=>v!.ToString()!; }
    class Bounds { public bool Contains(WDir p)=>p.X*p.X+p.Z*p.Z<=400; }
    class WorldState { public DateTime Now=DateTime.UnixEpoch; public DateTime FutureTime(double seconds)=>Now.AddSeconds(seconds); }
    class Party {
        public Actor[] Actors=Enumerable.Range(0,8).Select(i=>new Actor {InstanceID=(ulong)i+1}).ToArray();
        public int FindSlot(ulong id)=>Array.FindIndex(Actors,a=>a.InstanceID==id);
    }
    class BossModule {
        public WPos Center=new(100,100);
        public Actor PrimaryActor=new(){Position=new(90,100)};
        public Bounds Bounds=new();
        public Party Raid=new();
        public WorldState WorldState=new();
        public Arena Arena=new();
        public readonly Dictionary<Type,object> Components=[];
        public T? FindComponent<T>() where T:class=>Components.GetValueOrDefault(typeof(T)) as T;
    }
    class BossComponent(BossModule module) {
        protected BossModule Module=module;
        protected Party Raid=>Module.Raid;
        protected WorldState WorldState=>Module.WorldState;
        protected Arena Arena=>Module.Arena;
        public virtual void OnCastStarted(Actor actor,ActorCastInfo spell){}
        public virtual void OnEventCast(Actor actor,ActorCastEvent spell){}
        public virtual void AddHints(int slot,Actor actor,TextHints hints){}
        public virtual void DrawArenaForeground(int slot,Actor actor){}
    }
    static class Service {
        public static Config Config=new();
    }
    class Config {
        public readonly Dawntrail.Savage.M12S2Lindwurm.M12S2LindwurmConfig Current=new();
        public T Get<T>() where T:class=> (Current as T)!;
    }
}
namespace BossMod.Dawntrail.Savage.M12S2Lindwurm
{
    enum Replication2Strategy { DN,BananaCodex }
    enum Replication2Role { Boss,None,Cone1,Cone2,Stack1,Stack2,Defam1,Defam2 }
    static class RoleExtensions {
        extension(Replication2Role r) {
            public bool IsCone=>r is Replication2Role.Cone1 or Replication2Role.Cone2;
            public bool IsStack=>r is Replication2Role.Stack1 or Replication2Role.Stack2;
        }
    }
    class M12S2LindwurmConfig { public Replication2Strategy Rep2Strategy=Replication2Strategy.BananaCodex; }
    class Replication2Staging {
        public class Player { public Replication2Role? WantedRole; }
        public class Clone { public Replication2Role AssignedRole; public Actor? Target; public bool Locked; }
        public bool PlayersAssigned=true; public bool WurmsAssigned;
        public Clone?[] WurmsBySlot=new Clone?[8];
        public Player?[] PlayersBySlot=new Player?[8];
        public List<Clone> WurmClones=[];
    }
    class Replication2HeavySlam {
        public int NumCasts;
        public List<Stack> Stacks=[];
        public record Stack(Actor Target);
    }
    class Replication2HemorrhagicProjection { public int NumCasts; }
    class Replication2ReenactmentTowers {
        public record Tower(WPos Position,DateTime Activation);
        public List<Tower> Towers=[];
    }
    class Replication2ReenactmentScaldingWaves {
        public record Bait(Actor Target);
        public List<Bait> CurrentBaits=[];
    }
    class Replication2ReenactmentAOEs {
        public record AOE(WPos Origin,bool Risky=true) { public bool Check(WPos p)=>p.InCircle(Origin,3); }
        public AOE[] AOEs=[];
        public ReadOnlySpan<AOE> ActiveAOEs(int slot,Actor actor)=>AOEs;
    }
}

