namespace BossMod;

internal sealed class EnumMetadata(string[] names, ulong[] rawValues, Array? values, string[]? displayNames,
    Attribute[][]? attributes, Func<Enum, ulong> rawValue, Func<ulong, Enum> valueFactory)
{
    public readonly string[] Names = names;
    public readonly ulong[] RawValues = rawValues;
    public readonly Array? Values = values;
    public readonly string[] DisplayNames = displayNames ?? names;
    public readonly Attribute[][]? Attributes = attributes;
    public readonly Func<Enum, ulong> RawValue = rawValue;
    public readonly Func<ulong, Enum> ValueFactory = valueFactory;

    public int IndexOf(ulong raw)
    {
        var len = RawValues.Length;
        for (var i = 0; i < len; ++i)
        {
            if (RawValues[i] == raw)
            {
                return i;
            }
        }
        return -1;
    }
}

// Lazy compile-time enum tables. Convention ID enums retain only names/raw values; enums used by UI/config/strategies also contain boxed value tables
// and member attributes.
internal static partial class GeneratedEnumMetadata
{
    private static readonly Dictionary<Type, Lazy<EnumMetadata>> _byType = Build();

    private static Dictionary<Type, Lazy<EnumMetadata>> Build()
    {
        Dictionary<Type, Lazy<EnumMetadata>> result = [];
        Register(result);
        return result;
    }

    private static partial void Register(Dictionary<Type, Lazy<EnumMetadata>> metadata);

    public static bool IsRegistered(Type enumType) => _byType.ContainsKey(enumType);

    public static EnumMetadata Get(Type enumType)
        => _byType.GetValueOrDefault(enumType)?.Value ?? throw new ArgumentException($"No generated enum metadata for {enumType.FullName}");

    public static T[] Values<T>() where T : Enum
        => Get(typeof(T)).Values as T[] ?? throw new ArgumentException($"Enum {typeof(T).FullName} has name-only generated metadata");

    public static Array Values(Type enumType)
        => Get(enumType).Values ?? throw new ArgumentException($"Enum {enumType.FullName} has name-only generated metadata");

    public static string[] Names<T>() where T : Enum => Get(typeof(T)).Names;
    public static string[] Names(Type enumType) => Get(enumType).Names;
    public static int Count(Type enumType) => Get(enumType).RawValues.Length;

    public static string? Name(Type enumType, ulong raw)
    {
        var metadata = Get(enumType);
        var index = metadata.IndexOf(raw);
        return index >= 0 ? metadata.Names[index] : null;
    }

    public static Enum ValueByRaw(Type enumType, ulong raw) => Get(enumType).ValueFactory(raw);

    public static int IndexOf(Enum value)
    {
        var metadata = Get(value.GetType());
        return metadata.IndexOf(metadata.RawValue(value));
    }

    public static string DisplayName(Enum value)
    {
        var metadata = Get(value.GetType());
        var index = metadata.IndexOf(metadata.RawValue(value));
        return index >= 0 ? metadata.DisplayNames[index] : value.ToString();
    }

    public static TAttribute? Attribute<TAttribute>(Enum value) where TAttribute : Attribute
    {
        var metadata = Get(value.GetType());
        var index = metadata.IndexOf(metadata.RawValue(value));
        if (index < 0 || metadata.Attributes == null)
        {
            return null;
        }
        var ats = metadata.Attributes[index];
        var len = ats.Length;
        for (var i = 0; i < len; ++i)
        {
            if (ats[i] is TAttribute typed)
            {
                return typed;
            }
        }
        return null;
    }

    public static object Parse(Type enumType, string name)
    {
        var metadata = Get(enumType);
        ulong combined = 0;
        var foundAny = false;
        var names = name.Split(',');
        var len = names.Length;
        for (var i = 0; i < len; ++i)
        {
            var token = names[i].Trim();
            var found = false;
            var namesA = metadata.Names;
            var lenN = namesA.Length;
            for (var j = 0; j < lenN; ++j)
            {
                if (namesA[j] != token)
                {
                    continue;
                }
                combined |= metadata.RawValues[j];
                foundAny = found = true;
                break;
            }
            if (!found)
            {
                if (name.IndexOf(',') < 0)
                {
                    if (ulong.TryParse(token, out var unsigned))
                    {
                        return metadata.ValueFactory(unsigned);
                    }
                    if (long.TryParse(token, out var signed))
                    {
                        return metadata.ValueFactory((ulong)signed);
                    }
                }
                throw new ArgumentException($"Requested value '{name}' was not found in enum {enumType.FullName}");
            }
        }
        if (foundAny)
        {
            return metadata.ValueFactory(combined);
        }
        throw new ArgumentException($"Requested value '{name}' was not found in enum {enumType.FullName}");
    }

    public static T Parse<T>(string name) where T : Enum => (T)Parse(typeof(T), name);
}

internal static class GeneratedEnumExtensions
{
    public static string? GeneratedEnumName(this Type enumType, ulong raw) => GeneratedEnumMetadata.Name(enumType, raw);
    public static Array GeneratedEnumValues(this Type enumType) => GeneratedEnumMetadata.Values(enumType);
    public static string[] GeneratedEnumNames(this Type enumType) => GeneratedEnumMetadata.Names(enumType);
}
