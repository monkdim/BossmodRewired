using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace BossMod;

public sealed partial class LearnedPositions
{
    private static readonly JsonSerializerOptions StorageOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new PullConverter() }
    };
    private bool _needsCompaction;

    private sealed record PackedFile(int Schema, string Encoding, string Coordinates, string Pulls);

    public string Build()
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, true))
        using (var writer = new Utf8JsonWriter(gzip))
        {
            writer.WriteStartArray();
            foreach (var row in PullEvidenceRows().OrderBy(r => r.Encounter).ThenBy(r => r.Ability)
                .ThenBy(r => r.Slot, StringComparer.Ordinal).ThenBy(r => r.PullTicks).ThenBy(r => r.From))
                JsonSerializer.Serialize(writer, row, StorageOptions);
            writer.WriteEndArray();
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", 4);
            writer.WriteString("encoding", "gzip");
            writer.WriteString("coordinates", "scoped-world");
            writer.WriteBase64String("pulls", packed.ToArray());
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static LearnedPositions Load(string path, string? exportDirectory = null)
    {
        var result = Read(path, exportDirectory);
        result.Rebuild();
        return result;
    }

    // Called on a worker thread by the timer window. Migration does not stall a game frame.
    public static LearnedPositions LoadAndCompact(string path, string? exportDirectory = null)
    {
        lock (MergeGate)
        {
            var result = Load(path, exportDirectory);
            if (result._needsCompaction)
                WriteAtomic(path, result);
            return result;
        }
    }

    private static void WriteAtomic(string path, LearnedPositions result)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, result.Build());
        File.Move(temporary, path, true);
    }

    private static LearnedPositions Read(string path, string? exportDirectory = null)
    {
        var result = new LearnedPositions();
        if (!File.Exists(path))
            return result;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
            var probe = new byte[(int)Math.Min(stream.Length, 128)];
            stream.ReadExactly(probe);
            stream.Position = 0;
            if (Encoding.UTF8.GetString(probe).Contains("\"encoding\":\"gzip\"", StringComparison.Ordinal))
            {
                var packed = JsonSerializer.Deserialize<PackedFile>(stream, StorageOptions);
                if (packed is not { Schema: 4, Encoding: "gzip", Coordinates: "scoped-world" })
                    throw new InvalidDataException("Unknown evidence encoding");
                using var bytes = new MemoryStream(Convert.FromBase64String(packed.Pulls));
                using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
                foreach (var row in ReadArray<PullEvidence>(gzip)) result.MergePull(row);
                if (gzip.ReadByte() != -1) throw new InvalidDataException("Unexpected compressed evidence suffix");
                return result;
            }
            var header = new List<byte>();
            while (header.Count < 4096)
            {
                var b = stream.ReadByte();
                if (b < 0) break;
                header.Add((byte)b);
                if (b == '[') break;
            }
            var prefix = Encoding.UTF8.GetString(header.ToArray());
            if (!prefix.EndsWith('['))
                return result;
            using var document = JsonDocument.Parse(prefix + "]}");
            if (!document.RootElement.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out var version)
                || version is not (3 or 4))
                return result;
            var property = version == 3 ? "evidence" : "pulls";
            result._needsCompaction = true;
            if (!document.RootElement.TryGetProperty(property, out _))
                return result;
            var start = stream.Position - 1;
            var end = stream.Length - 1;
            while (end > start)
            {
                stream.Position = end;
                if (!char.IsWhiteSpace((char)stream.ReadByte())) break;
                --end;
            }
            stream.Position = end;
            if (stream.ReadByte() != '}')
                throw new InvalidDataException("Incomplete evidence file");
            stream.Position = start;
            using var array = new ArrayStream(stream, end - start);
            List<ReplayPullIndex.Frame>? index = null;
            var scopes = new Dictionary<(long Second, float Scale), ReplayPullIndex.Frame?>();
            ReplayPullIndex.Frame? frame(long ticks, float scale)
            {
                var key = ((ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond, scale);
                if (scopes.TryGetValue(key, out var cached)) return cached;
                index ??= ReplayPullIndex.Read(exportDirectory ?? Path.GetDirectoryName(path)!);
                var near = index.Where(p => Math.Abs(p.Ticks - key.Item1 * TimeSpan.TicksPerSecond) <= 2 * TimeSpan.TicksPerSecond).ToList();
                var matches = near.Where(p => p.OriginalOffsets && p.Reference != null && float.IsFinite(p.X) && float.IsFinite(p.Z)
                    && float.IsFinite(p.Scale) && Math.Abs(p.Scale - scale) < .001f)
                    .DistinctBy(p => (p.Encounter, p.X, p.Z, p.Scale)).Take(2).ToArray();
                return scopes[key] = near.Select(p => p.Encounter).Distinct().Count() == 1 && matches.Length == 1 ? matches[0] : null;
            }

            if (version == 3)
            {
                var verification = new Dictionary<ReplayPullIndex.Frame, (bool Seen, bool Conflict)>();
                foreach (var row in ReadArray<Evidence>(array))
                {
                    // Schema 3 stored naive/local replay ticks. Schema 4 always uses UTC.
                    var localPull = new DateTime(row.PullTicks, DateTimeKind.Unspecified);
                    var pull = localPull.ToUniversalTime().Ticks;
                    var when = new DateTime(row.EventTicks, DateTimeKind.Unspecified).ToUniversalTime().Ticks;
                    var original = TimeZoneInfo.Local.IsAmbiguousTime(localPull) || TimeZoneInfo.Local.IsInvalidTime(localPull) ? null : frame(pull, row.Scale);
                    if (original?.Reference is { } referenceSample && row.Ability == referenceSample.Ability && row.Slot == referenceSample.Slot
                        && Math.Abs(when - referenceSample.EventTicks) <= TimeSpan.TicksPerMillisecond * 15)
                    {
                        var prior = verification.GetValueOrDefault(original);
                        verification[original] = (true, prior.Conflict || Distance(row.X + original.X, row.Z + original.Z, referenceSample.X, referenceSample.Z) > .01);
                    }
                    result.Observe(row with { PullTicks = pull, EventTicks = when, Encounter = 0 });
                }
                var translated = new LearnedPositions();
                foreach (var row in result.PullEvidenceRows())
                {
                    var original = row.MaxScale - row.MinScale < .001f ? frame(row.PullTicks, row.MinScale) : null;
                    if (original != null && verification.GetValueOrDefault(original) is { Seen: true, Conflict: false })
                        translated.MergePull(row with { Encounter = original.Encounter,
                            MinX = row.MinX + original.X, MaxX = row.MaxX + original.X,
                            MinZ = row.MinZ + original.Z, MaxZ = row.MaxZ + original.Z });
                    else translated.MergePull(row);
                }
                result = translated;
                result._needsCompaction = true;
            }
            else
            {
                foreach (var row in ReadArray<PullEvidence>(array))
                {
                    result.MergePull(row);
                }
            }
        }
        catch (Exception e)
        {
            Service.Log($"[LearnedPositions] could not read {path}: {e.Message}");
            return new(); // A partial read must never activate a partial history.
        }
        return result;
    }

    private static IEnumerable<T> ReadArray<T>(Stream stream)
    {
        var reader = JsonSerializer.DeserializeAsyncEnumerable<T>(stream, StorageOptions).GetAsyncEnumerator();
        try
        {
            while (true)
            {
                var next = reader.MoveNextAsync();
                if (!(next.IsCompletedSuccessfully ? next.Result : next.AsTask().GetAwaiter().GetResult()))
                    break;
                yield return reader.Current ?? throw new InvalidDataException("Null evidence row");
            }
        }
        finally { reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class ArrayStream(Stream inner, long remaining) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            remaining -= read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class PullConverter : JsonConverter<PullEvidence>
    {
        public override PullEvidence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var a = document.RootElement;
            if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 14)
                throw new JsonException("Expected a compact pull record");
            return new(a[0].GetUInt32(), a[1].GetUInt32(), a[2].GetString()!, a[3].GetInt64(),
                a[4].GetSingle(), a[5].GetSingle(), a[6].GetSingle(), a[7].GetSingle(), a[8].GetSingle(),
                a[9].GetSingle(), a[10].GetSingle(), a[11].GetSingle(), a[12].GetSingle(), a[13].GetInt32());
        }
        public override void Write(Utf8JsonWriter writer, PullEvidence row, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(row.Encounter); writer.WriteNumberValue(row.Ability); writer.WriteStringValue(row.Slot);
            writer.WriteNumberValue(row.PullTicks); writer.WriteNumberValue(row.From); writer.WriteNumberValue(row.To);
            writer.WriteNumberValue(row.MinX); writer.WriteNumberValue(row.MinZ); writer.WriteNumberValue(row.MaxX); writer.WriteNumberValue(row.MaxZ);
            writer.WriteNumberValue(row.MinScale); writer.WriteNumberValue(row.MaxScale); writer.WriteNumberValue(row.MaxCaster); writer.WriteNumberValue(row.Failures);
            writer.WriteEndArray();
        }
    }
}
