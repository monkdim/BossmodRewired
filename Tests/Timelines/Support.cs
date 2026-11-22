namespace BossMod
{
    enum ActionType { Spell }
    readonly record struct ActionID(ActionType Type, uint ID);
    record ActorCastInfo(ActionID Action);
    record ActorCastEvent(ActionID Action);
    class Actor { public bool InCombat = true; public uint NameID; public string Name = "Boss"; public bool IsAlly; public ActorCastInfo? CastInfo; }
    record ZoneChange(ushort Zone);
    class Event<T> {
        private event Action<T>? Handlers;
        public IDisposable Subscribe(Action<T> a) { Handlers += a; return new Subscription(() => Handlers -= a); }
        public void Fire(T value) => Handlers?.Invoke(value);
    }
    sealed class Subscription(Action close) : IDisposable { public void Dispose() => close(); }
    sealed class EventSubscriptions(params IDisposable[] subscriptions) : IDisposable { public void Dispose() { foreach (var s in subscriptions) s.Dispose(); } }
    public class WorldState {
        public DateTime CurrentTime = DateTime.UnixEpoch;
        public ushort CurrentZone = 1;
        internal Event<ZoneChange> CurrentZoneChanged = new();
        internal Event<MapOp> MapEffect=new();
        internal Event<DirectorOp> DirectorUpdate=new();
        internal ActorState Actors = new();
        internal PartyState Party = new();
    }
    class ActorState { public Event<Actor> CastStarted = new(); public Event<Actor,ActorCastEvent> CastEvent = new(); }
    class Event<T,U> {
        private event Action<T,U>? Handlers;
        public IDisposable Subscribe(Action<T,U> a) { Handlers += a; return new Subscription(() => Handlers -= a); }
        public void Fire(T value,U second) => Handlers?.Invoke(value,second);
    }
    class PartyState { private readonly Actor player = new(); public Actor Player() => player; }
    static class Service { public static void Log(string _) {} public static T? LuminaRow<T>(uint _) where T:struct => null; }
}
namespace BossMod.Timelines
{
    static class TimelineLibrary {
        public static CactbotTimeline? Loaded;
        public static CactbotTimeline? ForZone(ushort zone) => zone==1 ? Loaded : null;
    }
}


namespace Lumina.Excel.Sheets { struct BNpcName { public string Singular => string.Empty; } }

namespace BossMod { record MapOp(byte Index,uint State); record DirectorOp(uint UpdateID,uint Param1=0,uint Param2=0,uint Param3=0,uint Param4=0); }
