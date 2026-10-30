using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BossMod;

static class ReplayPullIndex
{
    public sealed record Probe(uint Ability, string Slot, long EventTicks, float X, float Z);
    public sealed record Frame(long Ticks, uint Encounter, float X, float Z, float Scale, bool OriginalOffsets, int Pull)
    {
        public Probe? Reference { get; set; }
    }
    // Pull headers supply encounter/frame identities. One matching sample per pull verifies the origin;
    // sample lines are scanned without retaining the full export in memory.
    public static List<Frame> Read(string directory)
    {
        var result = new List<Frame>();
        if (!Directory.Exists(directory)) return result;
        string[] files;
        try { files = Directory.GetFiles(directory, "*.json"); }
        catch (Exception e)
        {
            Service.Log($"[LearnedPositions] export metadata directory unavailable: {e.Message}");
            return result;
        }
        foreach (var path in files)
        {
            if (!File.Exists(Path.ChangeExtension(path, ".txt"))) continue;
            try
            {
                var firstFrame = result.Count;
                string? stamp;
                using (var text = new StreamReader(new FileStream(Path.ChangeExtension(path, ".txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
                    stamp = text.ReadLine();
                var originalOffsets = stamp == "Export format 7.";
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[(int)Math.Min(stream.Length, 1048576)];
                var used = 0;
                while (used < buffer.Length)
                {
                    var n = stream.Read(buffer, used, Math.Min(65536, buffer.Length - used));
                    if (n == 0) break;
                    used += n;
                    var reader = new Utf8JsonReader(buffer.AsSpan(0, used), false, default);
                    var complete = false;
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("pulls")) continue;
                        if (!reader.Read() || !JsonDocument.TryParseValue(ref reader, out var pulls)) break;
                        using (pulls)
                        {
                            foreach (var pull in pulls.RootElement.EnumerateArray())
                                if (pull.TryGetProperty("oid", out var oid) && oid.TryGetUInt32(out var id) && id != 0
                                    && pull.TryGetProperty("from", out var from)
                                    && DateTimeOffset.TryParse(from.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
                                {
                                    var x = float.NaN; var z = float.NaN; var scale = float.NaN;
                                    if (pull.TryGetProperty("arena", out var arena) && arena.ValueKind == JsonValueKind.Object
                                        && arena.TryGetProperty("center", out var center) && center.ValueKind == JsonValueKind.Array && center.GetArrayLength() == 2
                                        && center[0].TryGetSingle(out var cx) && center[1].TryGetSingle(out var cz)
                                        && arena.TryGetProperty("scale", out var size) && size.TryGetSingle(out var radius))
                                    { x = cx; z = cz; scale = radius; }
                                    result.Add(new(time.UtcTicks, id, x, z, scale, originalOffsets,
                                        pull.TryGetProperty("index", out var number) ? number.GetInt32() : 0));
                                }
                        }
                        complete = true;
                        break;
                    }
                    if (complete) break;
                }
                ReadProbes(path, result.Skip(firstFrame).ToList());
            }
            catch (Exception e) { Service.Log($"[LearnedPositions] encounter metadata unavailable: {Path.GetFileName(path)}: {e.Message}"); }
        }
        return result;
    }

    private static void ReadProbes(string path, List<Frame> frames)
    {
        var missing = frames.Where(f => f.OriginalOffsets && f.Pull > 0).GroupBy(f => f.Pull).ToDictionary(g => g.Key, g => g.ToList());
        if (missing.Count == 0) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var samples = false;
        while (reader.ReadLine() is { } line)
        {
            var text = line.Trim();
            if (!samples)
            {
                if (text.StartsWith("\"samples\": [", StringComparison.Ordinal)) samples = true;
                continue;
            }
            // PositionExport's contract is one complete sample object per line.
            if (!text.StartsWith("{\"ability\":", StringComparison.Ordinal)) continue;
            using var sample = JsonDocument.Parse(text.TrimEnd(','));
            var row = sample.RootElement;
            if (!row.TryGetProperty("pull", out var pull) || !missing.TryGetValue(pull.GetInt32(), out var owners)
                || !row.TryGetProperty("slot", out var slot) || string.IsNullOrEmpty(slot.GetString())
                || !row.TryGetProperty("at", out var at)
                || !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
                || !row.TryGetProperty("cast", out var cast) || cast.GetArrayLength() != 2)
                continue;
            var probe = new Probe(row.GetProperty("ability").GetUInt32(), slot.GetString()!, when.UtcTicks,
                cast[0].GetSingle(), cast[1].GetSingle());
            if (!float.IsFinite(probe.X) || !float.IsFinite(probe.Z)) continue;
            foreach (var owner in owners) owner.Reference = probe;
            missing.Remove(pull.GetInt32());
            if (missing.Count == 0) break;
        }
    }
}
