namespace BossMod;

// Persistence and eligibility tests do not initialize the game's configuration services.
public enum Class { None }
enum ClassCategory { Tank, Healer, Melee, PhysRanged, Caster }
sealed class PartyRolesConfig
{
    public enum Assignment { Unassigned }
    public Assignment this[ulong id] => Assignment.Unassigned;
}
static class Service
{
    public static Configuration Config { get; } = new();
    public static void Log(string message) => Console.Error.WriteLine(message);
    public sealed class Configuration
    {
        public T Get<T>() where T : new() => new();
    }
}
static class TestExtensions
{
    public static ClassCategory GetClassCategory(this Class cls) => ClassCategory.Melee;
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> source, TKey key)
        where TKey : notnull where TValue : new()
    {
        if (!source.TryGetValue(key, out var value))
        {
            value = new();
            source.Add(key, value);
        }
        return value;
    }
}

