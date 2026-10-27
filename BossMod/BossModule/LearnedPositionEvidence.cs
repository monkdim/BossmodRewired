using System.IO;
using System.Text.Json.Serialization;

namespace BossMod;

public sealed partial class LearnedPositions
{
    // Absolute pull/event times identify overlapping recordings without relying on filenames.
    // Arena-relative coordinates retain world distances, rather than mixing caster-relative positions.
    [method: JsonConstructor]
    public sealed record Evidence(uint Ability, string Slot, long PullTicks, long EventTicks,
        float X, float Z, float HitX, float HitZ, float SettledX, float SettledZ,
        float Scale, float FromCaster, bool Qualified);
    public sealed record EvidenceFile(int Schema, List<Evidence> Evidence);

    private readonly Dictionary<(uint Ability, string Slot), List<Evidence>> _evidence = [];
    private readonly Dictionary<(uint Ability, string Slot, long Moment), List<Evidence>> _events = [];

    public IEnumerable<Evidence> EvidenceRows() => _evidence.Values.SelectMany(rows => rows);

    public void Observe(Evidence row)
    {
        if (row != null && row.Slot == "")
            return; // An unassigned/non-player entry cannot teach a role hint.
        if (row == null || row.Ability == 0 || string.IsNullOrEmpty(row.Slot)
            || row.PullTicks <= 0 || row.EventTicks < row.PullTicks || row.EventTicks > DateTime.MaxValue.Ticks
            || !new[] { row.X, row.Z, row.HitX, row.HitZ, row.SettledX, row.SettledZ, row.Scale, row.FromCaster }.All(float.IsFinite)
            || row.Scale <= 0 || row.FromCaster < 0)
            throw new InvalidDataException("Invalid learned-position evidence");

        // Exact repeated observations are idempotent. Different coordinates at the same instant survive:
        // two players sharing a slot, or conflicting exports, must not overwrite each other.
        var moment = row.EventTicks / (TimeSpan.TicksPerSecond / 4);
        for (var offset = -1; offset <= 1; ++offset)
            if (_events.TryGetValue((row.Ability, row.Slot, moment + offset), out var existing)
                && existing.Any(prev => Math.Abs(prev.PullTicks - row.PullTicks) <= TimeSpan.TicksPerSecond
                    && Math.Abs(prev.EventTicks - row.EventTicks) <= TimeSpan.TicksPerSecond / 4
                    && prev with { PullTicks = row.PullTicks, EventTicks = row.EventTicks } == row))
                return;
        _evidence.GetOrAdd((row.Ability, row.Slot)).Add(row);
        _events.GetOrAdd((row.Ability, row.Slot, moment)).Add(row);
    }

    public void Rebuild()
    {
        _spots.Clear();
        foreach (var ((ability, slot), rows) in _evidence)
        {
            if (rows.Any(r => !r.Qualified || r.FromCaster > 60 || Math.Abs(r.Scale - rows[0].Scale) > .01f))
                continue;
            var starts = new List<long>();
            foreach (var row in rows.OrderBy(r => r.PullTicks))
                if (starts.Count == 0 || row.PullTicks - starts[^1] > TimeSpan.TicksPerSecond)
                    starts.Add(row.PullTicks);
            if (starts.Count < 2)
                continue;
            // Equal weight per pull prevents a multi-hit mechanic or a long pull dominating the mean.
            var pulls = rows.GroupBy(r => starts.Last(t => t <= r.PullTicks)).ToList();
            var x = pulls.Average(p => p.Average(r => (double)r.X));
            var z = pulls.Average(p => p.Average(r => (double)r.Z));
            double distance(float ax, float az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
            var spread = rows.Max(r => distance(r.X, r.Z, x, z));
            if (spread <= 0 || spread >= 3 || rows.Any(r => distance(r.X, r.Z, r.HitX, r.HitZ) > 3
                || distance(r.HitX, r.HitZ, r.SettledX, r.SettledZ) > 3))
                continue;
            var radius = Math.Sqrt(x * x + z * z);
            var bearing = (Math.Atan2(x, -z) * 180 / Math.PI + 360) % 360;
            Learn(ability, slot, new((float)(radius / rows[0].Scale), (float)bearing,
                (float)pulls.Average(p => p.Average(r => (double)r.FromCaster)), rows.Count, rows.Count,
                (float)spread, false, starts.Count));
        }
    }
}
