namespace BossMod.ReplayAnalysis;

/// <summary>
/// Where the party stood for each ability, and what shape that ability appears to be.
///
/// Shared by both exports. It used to live only in the whole-recording dump, which meant fights that did have
/// a module, and therefore had role assignments to report, got the weaker analysis of the two. The caller
/// supplies who counts as the party, how to label them, and which actions are in scope.
/// </summary>
static class PositionAnalysis
{
    /// <summary>Pets and buddies fight on your side, so "not a player" is the wrong test for hostility.</summary>
    private static bool IsHostile(Replay.Participant p)
        => p.Type is not (ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy);

    /// <summary>Where each player stood at one moment, both relative to the caster and in the world.</summary>
    private readonly record struct Sample(WDir AtCast, WDir AtHit, WDir Settled, WPos CastWorld, WPos HitWorld, WPos SettledWorld);

    /// <summary>
    /// Where everyone stood when each ability resolved, measured from the caster: "how far from the thing
    /// casting it, and in which direction" is the question a positional hint answers.
    ///
    /// Caster-relative cannot express everything, though. A knockback is about the arena, not the boss, which
    /// is usually standing in the middle of it, so when the caller can name an arena the same positions are
    /// reported from its centre as well.
    /// </summary>
    public static void Append(StringBuilder sb, Replay replay, IReadOnlyCollection<Replay.Participant> involved, Func<Replay.Participant, string> label, Func<Replay.Action, bool> inScope, ArenaEstimate? arena = null)
    {
        if (involved.Count == 0)
        {
            return;
        }

        // A knockback lands its damage first and moves people a moment later, so a sample taken at the
        // resolution is still pre-displacement. This is how long to wait before asking where they ended up.
        const double SettleSeconds = 2d;

        // ability -> player -> where they were when the cast began, when it landed, and once things settled
        var byAbility = new Dictionary<ActionID, Dictionary<Replay.Participant, List<Sample>>>();
        var resolutions = new Dictionary<ActionID, int>();
        var telegraphed = new Dictionary<ActionID, int>();

        foreach (var a in replay.Actions)
        {
            if (!IsHostile(a.Source) || !inScope(a))
            {
                continue;
            }

            var hitAt = a.Timestamp;
            var castAt = CastStart(a.Source, a.ID, hitAt);

            resolutions[a.ID] = resolutions.GetValueOrDefault(a.ID) + 1;
            if (castAt != hitAt)
            {
                telegraphed[a.ID] = telegraphed.GetValueOrDefault(a.ID) + 1;
            }

            var hitSrc = a.Source.PosRotAt(hitAt);
            var castSrc = a.Source.PosRotAt(castAt);
            var hitOrigin = new WPos(hitSrc.X, hitSrc.Z);
            var castOrigin = new WPos(castSrc.X, castSrc.Z);

            var perPlayer = byAbility.GetOrAdd(a.ID);

            foreach (var p in involved)
            {
                if (p.DeadAt(hitAt))
                {
                    continue;
                }

                var atHit = p.PosRotAt(hitAt);
                var atCast = p.PosRotAt(castAt);
                var settled = p.PosRotAt(hitAt.AddSeconds(SettleSeconds));
                var castWorld = new WPos(atCast.X, atCast.Z);
                var hitWorld = new WPos(atHit.X, atHit.Z);
                var settledWorld = new WPos(settled.X, settled.Z);
                perPlayer.GetOrAdd(p).Add(new(
                    castWorld - castOrigin, hitWorld - hitOrigin, settledWorld - hitOrigin,
                    castWorld, hitWorld, settledWorld));
            }
        }

        sb.AppendLine("========================================================================");
        arena?.Append(sb);
        sb.AppendLine("POSITIONS, relative to whatever cast the ability");
        sb.AppendLine("'cast' is where somebody stood when the cast began, which is the position they chose.");
        sb.AppendLine("'hit' is where they were when it landed, and 'moved' is how far they travelled to get there.");
        sb.AppendLine("'after' is how far they were displaced in the two seconds following, which is where a");
        sb.AppendLine("knockback shows up: the damage lands first and the shove arrives a moment later.");
        if (arena != null)
        {
            sb.AppendLine("The second line of each row repeats the same three moments measured from the arena centre,");
            sb.AppendLine("with the distance also given as a fraction of the arena, so 0.00 is dead centre and 1.00");
            sb.AppendLine("is the edge.");
        }
        sb.AppendLine();

        foreach (var (aid, perPlayer) in byAbility)
        {
            var casts = resolutions[aid];
            sb.Append(aid.ToString()).Append(" - ").Append(casts).AppendLine(" resolutions");
            sb.Append("  looks like: ").AppendLine(Classify(replay, aid, involved.Count, inScope));

            if (telegraphed.GetValueOrDefault(aid) == 0)
            {
                sb.AppendLine("  no cast bar was recorded, so 'cast' and 'hit' are the same instant");
            }

            // Several casts landing together give every player the same set of timestamps, so their spread
            // collapses to the variation between casters rather than between players. Worth saying out loud,
            // since an identical figure on every row otherwise looks like a coincidence.
            if (SimultaneousResolutions(replay, aid))
            {
                sb.AppendLine("  these resolve simultaneously, so spread measures the casters, not the players");
            }

            foreach (var (p, samples) in perPlayer)
            {
                var castOffsets = new List<WDir>(samples.Count);
                var hitOffsets = new List<WDir>(samples.Count);
                var moved = 0f;
                var pushed = 0f;
                foreach (var s in samples)
                {
                    castOffsets.Add(s.AtCast);
                    hitOffsets.Add(s.AtHit);
                    moved += (s.AtHit - s.AtCast).Length();
                    pushed += (s.Settled - s.AtHit).Length();
                }

                var (castMean, castSpread) = MeanAndSpread(castOffsets);
                var (hitMean, _) = MeanAndSpread(hitOffsets);

                sb.Append("  ").Append(label(p).PadRight(24))
                  .Append("cast (").Append(Fixed(castMean.X)).Append(',').Append(Fixed(castMean.Z)).Append(')')
                  .Append(" d=").Append(Fixed(castMean.Length()))
                  .Append(' ').Append(Octant(castMean).PadRight(8))
                  .Append("spread ").Append(Fixed(castSpread)).Append("y  |  hit d=").Append(Fixed(hitMean.Length()))
                  .Append(' ').Append(Octant(hitMean).PadRight(8))
                  .Append("moved ").Append(Fixed(moved / samples.Count))
                  .Append("y  after ").Append(Fixed(pushed / samples.Count)).AppendLine("y");

                if (arena != null)
                {
                    AppendArenaRow(sb, arena, samples);
                }
            }

            sb.AppendLine();
        }
    }

    /// <summary>
    /// The same three moments again, measured from the arena centre. Caster-relative says a knockback moved
    /// somebody thirteen yards; this says whether those thirteen yards ended at the edge or back in the middle,
    /// which is the difference between a mechanic you survive and one you do not.
    /// </summary>
    private static void AppendArenaRow(StringBuilder sb, ArenaEstimate arena, List<Sample> samples)
    {
        var center = arena.Reference;
        var cast = new List<WDir>(samples.Count);
        var hit = new List<WDir>(samples.Count);
        var settled = new List<WDir>(samples.Count);
        foreach (var s in samples)
        {
            cast.Add(s.CastWorld - center);
            hit.Add(s.HitWorld - center);
            settled.Add(s.SettledWorld - center);
        }

        var (castMean, _) = MeanAndSpread(cast);
        var (hitMean, _) = MeanAndSpread(hit);
        var (settledMean, _) = MeanAndSpread(settled);

        sb.Append(' ', 26).Append("from centre: cast ").Append(Describe(castMean, arena.Scale))
          .Append(", hit ").Append(Describe(hitMean, arena.Scale))
          .Append(", after ").AppendLine(Describe(settledMean, arena.Scale));
    }

    private static string Describe(WDir offset, float radius)
    {
        var d = offset.Length();
        var fraction = radius > 0f ? d / radius : 0f;
        return $"{d,6:f2}y {Octant(offset),-8}({fraction:f2}r)";
    }

    /// <summary>
    /// When the cast that produced this resolution began. Matched by ability and by which cast of it finished
    /// nearest the resolution, since an instant ability has no cast at all and returns the resolution itself.
    /// </summary>
    private static DateTime CastStart(Replay.Participant source, ActionID id, DateTime resolution)
    {
        var best = resolution;
        var bestDelta = 2d; // beyond a couple of seconds it is a different cast of the same ability
        foreach (var c in source.Casts)
        {
            if (c.ID != id)
            {
                continue;
            }

            var delta = Math.Abs((c.Time.End - resolution).TotalSeconds);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = c.Time.Start;
            }
        }

        return best;
    }

    /// <summary>Whether this ability tends to resolve several at once, which changes what spread measures.</summary>
    private static bool SimultaneousResolutions(Replay replay, ActionID aid)
    {
        var times = new HashSet<DateTime>();
        var total = 0;
        foreach (var a in replay.Actions)
        {
            if (a.ID == aid && IsHostile(a.Source))
            {
                ++total;
                times.Add(a.Timestamp);
            }
        }

        return total > times.Count;
    }

    /// <summary>
    /// Names the shape of a mechanic from what it did, since a recording with no module has nobody's word for
    /// it. How many players it hit, and how far apart they were standing when it landed, separates a stack
    /// from a spread from something that ignored position entirely.
    ///
    /// The labels hedge where the data genuinely cannot decide. A raidwide that happens to land while everyone
    /// is stacked looks exactly like a stack, and saying so is more useful than picking one.
    /// </summary>
    private static string Classify(Replay replay, ActionID aid, int partySize, Func<Replay.Action, bool> inScope)
    {
        var hits = new List<int>();
        var spans = new List<float>();
        var closest = new List<float>();
        var roles = new HashSet<Role>();

        foreach (var a in replay.Actions)
        {
            if (a.ID != aid || !IsHostile(a.Source) || !inScope(a))
            {
                continue;
            }

            var positions = new List<WPos>();
            foreach (var t in a.Targets)
            {
                if (t.Target.Type != ActorType.Player)
                {
                    continue;
                }

                roles.Add(t.Target.Class.GetRole());
                var pr = t.Target.PosRotAt(a.Timestamp);
                positions.Add(new(pr.X, pr.Z));
            }

            if (positions.Count == 0)
            {
                continue;
            }

            hits.Add(positions.Count);

            var maxSpan = 0f;
            var minSpan = float.MaxValue;
            for (var i = 0; i < positions.Count; ++i)
            {
                for (var j = i + 1; j < positions.Count; ++j)
                {
                    var d = (positions[i] - positions[j]).Length();
                    maxSpan = Math.Max(maxSpan, d);
                    minSpan = Math.Min(minSpan, d);
                }
            }

            spans.Add(maxSpan);
            if (minSpan < float.MaxValue)
            {
                closest.Add(minSpan);
            }
        }

        if (hits.Count == 0)
        {
            return "hit nobody, so it was either dodged every time or does not target players";
        }

        var avgHit = hits.Average();
        var avgSpan = spans.Count > 0 ? spans.Average() : 0f;
        var avgClosest = closest.Count > 0 ? closest.Average() : 0f;

        if (avgHit < 1.5f)
        {
            var role = roles.Count == 1 ? roles.First() : Role.None;
            return role switch
            {
                Role.Tank => "single target on a tank, so probably a tank buster",
                Role.Healer => "single target on a healer",
                _ => $"single target, hitting {Describe(roles)}"
            };
        }

        // Everyone caught, spread across the arena: position made no difference.
        if (avgHit >= partySize - 0.5f && avgSpan > 12f)
        {
            return $"raidwide, everyone hit wherever they stood (up to {avgSpan:f1}y apart)";
        }

        if (avgSpan <= 6f)
        {
            var who = avgHit >= partySize - 0.5f ? "full party" : avgHit <= 4.5f ? "light party" : "part of the party";
            return $"{who} stack, {avgHit:f1} players within {avgSpan:f1}y of each other";
        }

        if (avgClosest >= 8f)
        {
            return $"spread, {avgHit:f1} players with nobody closer than {avgClosest:f1}y";
        }

        return avgHit >= partySize - 0.5f
            ? $"everyone hit, {avgSpan:f1}y apart, so a raidwide or a loose stack"
            : $"{avgHit:f1} players hit, {avgSpan:f1}y apart, hitting {Describe(roles)}";
    }

    private static string Describe(HashSet<Role> roles)
        => roles.Count == 0 ? "nobody" : string.Join(" and ", roles.Where(r => r != Role.None).Select(r => r.ToString().ToLowerInvariant()));


    private static (WDir Mean, float Spread) MeanAndSpread(List<WDir> offsets)
    {
        var count = offsets.Count;
        var sumX = 0f;
        var sumZ = 0f;
        for (var i = 0; i < count; ++i)
        {
            sumX += offsets[i].X;
            sumZ += offsets[i].Z;
        }

        var mean = new WDir(sumX / count, sumZ / count);

        var spread = 0f;
        for (var i = 0; i < count; ++i)
        {
            spread += (offsets[i] - mean).Length();
        }

        return (mean, spread / count);
    }

    private static readonly string[] Octants = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    // FFXIV world axes put north at -Z and east at +X, so a compass bearing is 180 degrees off WDir.ToAngle.
    private static string Octant(WDir offset)
    {
        if (offset.LengthSq() < 0.01f)
        {
            return "on top";
        }

        var bearing = (180f - offset.ToAngle().Deg + 360f) % 360f;
        return Octants[(int)MathF.Round(bearing / 45f) % 8];
    }

    private static string Fixed(float v) => v.ToString("f2").PadLeft(7);
}
