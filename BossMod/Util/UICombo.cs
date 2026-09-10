using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace BossMod;

public static class UICombo
{
    public static string EnumString(Enum value) => GeneratedEnumMetadata.DisplayName(value);

    public static bool Enum<T>(string label, ref T value, Func<T, string>? print = null, Func<T, bool>? filter = null) where T : Enum
    {
        // T can itself be System.Enum for callers that only know the concrete
        // enum at runtime (for example config fields and encounter OIDs).
        var type = value.GetType();
        var rawValues = GeneratedEnumMetadata.Values(type);
        var values = new T[rawValues.Length];
        for (var i = 0; i < rawValues.Length; ++i)
            values[i] = (T)rawValues.GetValue(i)!;
        var current = Array.IndexOf(values, value);
        if (current < 0)
            current = 0;

        print ??= static item => EnumString(item);
        filter ??= static _ => true;
        if (!EnumIndex(label, type, ref current, index => print(values[index]), index => filter(values[index])))
            return false;
        value = values[current];
        return true;
    }

    public static bool EnumIndex(string label, Type type, ref int value, Func<int, string>? print = null, Func<int, bool>? filter = null)
    {
        var values = GeneratedEnumMetadata.Values(type);
        print ??= index => EnumString((Enum)values.GetValue(index)!);
        filter ??= static _ => true;
        var result = false;
        var width = 300 * ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(width);

        var currentLabel = print(value);
        var showLabelPopup = ImGui.CalcTextSize(currentLabel).X > width;
        if (ImGui.BeginCombo($"###{label}", currentLabel))
        {
            showLabelPopup = false;
            for (var i = 0; i < values.Length; ++i)
            {
                if (!filter(i))
                    continue;
                if (ImGui.Selectable(print(i), i == value))
                {
                    value = i;
                    result = true;
                }
            }
            ImGui.EndCombo();
        }
        if (showLabelPopup && ImGui.IsItemHovered())
            ImGui.SetTooltip(currentLabel);

        if (!label.StartsWith('#'))
        {
            ImGui.SameLine();
            ImGui.TextWrapped(label);
        }
        return result;
    }

    public static bool Radio(Type type, ref int value, bool oneLine, Func<int, string>? print = null)
    {
        var values = GeneratedEnumMetadata.Values(type);
        print ??= index => EnumString((Enum)values.GetValue(index)!);
        var original = value;
        var result = false;
        for (var i = 0; i < values.Length; ++i)
        {
            if (ImGui.RadioButton(print(i), i == value))
            {
                value = i;
                result = i != original;
            }
            if (oneLine && i + 1 < values.Length)
                ImGui.SameLine();
        }
        return result;
    }

    public static bool Int(string label, string[] values, ref int value)
    {
        var result = false;
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo(label, value < values.Length ? values[value] : value.ToString()))
        {
            for (var i = 0; i < values.Length; ++i)
            {
                if (ImGui.Selectable(values[i], value == i))
                {
                    value = i;
                    result = true;
                }
            }
            ImGui.EndCombo();
        }
        return result;
    }

    public static bool Bool(string label, string[] values, ref bool value)
    {
        var raw = value ? 1 : 0;
        if (!Int(label, values, ref raw))
            return false;
        value = raw != 0;
        return true;
    }
}
