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
    }
    readonly record struct Angle(float Rad)
    {
        public WDir ToDirection()=>new(MathF.Sin(Rad),MathF.Cos(Rad));
    }
    static class AngleExtensions { public static Angle Degrees(this float f)=>new(f*MathF.PI/180); }
    readonly record struct ActionID(uint ID);
    record ActorCastInfo(ActionID Action,WPos LocXZ,Angle Rotation)
    {
        public float NPCRemainingTime=5;
    }
    record ActorCastEvent(ActionID Action);
    class Actor
    {
        public ulong InstanceID;
        public WPos Position;
        public Angle Rotation;
        public bool IsDead;
        public ActorCastInfo? CastInfo;
        public HashSet<uint> Statuses=[];
        public object? FindStatus(uint id)=>Statuses.Contains(id)?this:null;
    }
    class WorldState { public DateTime CurrentTime=DateTime.UnixEpoch; }
    class BossModule
    {
        public Actor PrimaryActor=new(){InstanceID=1};
        public WorldState WorldState=new();
        public Dictionary<uint,List<Actor>> EnemiesByOID=[];
        public List<Actor> Enemies(uint oid)=>EnemiesByOID.GetValueOrDefault(oid)??[];
    }
    class BossComponent(BossModule module)
    {
        public BossModule Module=module;
        public WorldState WorldState=>Module.WorldState;
        public virtual void OnCastStarted(Actor caster,ActorCastInfo spell){}
        public virtual void OnCastFinished(Actor caster,ActorCastInfo spell){}
        public virtual void OnEventCast(Actor caster,ActorCastEvent spell){}
        public virtual void AddHints(int slot,Actor actor,TextHints hints){}
        public virtual void AddGlobalHints(Actor actor,GlobalHints hints){}
    }
    class TextHints { public List<(string Text,bool Risk)> Values=[]; public void Add(string text,bool risk=true)=>Values.Add((text,risk)); }
    class GlobalHints { public List<string> Values=[]; public void Add(string text)=>Values.Add(text); }
    static class Colors { public const uint Safe=123; }
    abstract class AOEShape { public abstract bool Check(WPos point,WPos origin,Angle rotation); }
    class AOEShapeCone(float radius,Angle half):AOEShape
    {
        public override bool Check(WPos p,WPos origin,Angle rotation)
        {
            var v=p-origin;
            var len=MathF.Sqrt(v.X*v.X+v.Z*v.Z);
            var d=rotation.ToDirection();
            return len<=radius && (len==0 || (v.X*d.X+v.Z*d.Z)/len>=MathF.Cos(half.Rad));
        }
    }
}
namespace BossMod.Components
{
    class CastHint(BossModule module,uint aid,string hint,bool showCastTimeLeft=false):BossComponent(module)
    {
        public uint WatchedAction=aid;
        public string Hint=hint;
        public bool ShowCastTimeLeft=showCastTimeLeft;
        public List<Actor> Casters=[];
        public bool Active=>Casters.Count>0;
        public override void OnCastStarted(Actor c,ActorCastInfo s){if(s.Action.ID==WatchedAction)Casters.Add(c);}
        public override void OnCastFinished(Actor c,ActorCastInfo s){if(s.Action.ID==WatchedAction)Casters.Remove(c);}
        public override void AddGlobalHints(Actor actor,GlobalHints hints){if(Active)hints.Add(Hint);}
    }
    class CastHints(BossModule module,uint[] aids,string hint):CastHint(module,0,hint)
    {
        public override void OnCastStarted(Actor c,ActorCastInfo s){if(aids.Contains(s.Action.ID))Casters.Add(c);}
        public override void OnCastFinished(Actor c,ActorCastInfo s){if(aids.Contains(s.Action.ID))Casters.Remove(c);}
    }
    class RaidwideCast(BossModule module,uint aid):CastHint(module,aid,"Raidwide");
    class GenericAOEs(BossModule module,uint aid):BossComponent(module)
    {
        public uint WatchedAction=aid;
        public struct AOEInstance(AOEShape shape,WPos origin,Angle rotation)
        {
            public uint Color;
            public bool Risky=true;
            public bool Check(WPos pos)=>shape.Check(pos,origin,rotation);
        }
    }
    class SimpleAOEs(BossModule module,uint aid,AOEShape shape):GenericAOEs(module,aid)
    {
        public List<AOEInstance> Casters=[];
        public virtual ReadOnlySpan<AOEInstance> ActiveAOEs(int slot,Actor actor)
        {
            var span=System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Casters);
            foreach(ref var a in span){a.Color=0;a.Risky=true;}
            return span;
        }
        public override void OnCastStarted(Actor c,ActorCastInfo s){if(s.Action.ID==WatchedAction)Casters.Add(new(shape,s.LocXZ,s.Rotation));}
        public override void OnCastFinished(Actor c,ActorCastInfo s){if(s.Action.ID==WatchedAction)Casters.Clear();}
        public override void AddHints(int slot,Actor actor,TextHints hints)
        {
            foreach(var a in ActiveAOEs(slot,actor))if(a.Risky&&a.Check(actor.Position))hints.Add("Avoid cone");
        }
    }
    class CastGazes(BossModule module,uint[] aids):CastHints(module,aids,"Look away")
    {
        public override void AddHints(int slot,Actor actor,TextHints hints){if(Active)hints.Add("Look away");}
    }
    class BaitAwayIcon(BossModule module,float radius,uint icon,uint aid,double activationDelay):BossComponent(module)
    {
        public float Radius=radius;
        public List<Actor> Targets=[];
        public void OnEventIcon(Actor actor,uint id,ulong _){if(id==icon)Targets.Add(actor);}
        public WPos Center=>Targets[0].Position;
        public override void OnEventCast(Actor caster,ActorCastEvent spell){if(spell.Action.ID==aid)Targets.Clear();}
    }
    abstract class CastLineOfSightAOE(BossModule module,uint aid,float range):BossComponent(module)
    {
        public abstract ReadOnlySpan<Actor> BlockerActors();
    }
}
