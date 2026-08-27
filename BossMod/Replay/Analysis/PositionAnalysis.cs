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
    /// <summary>
    /// How much this analysis managed to say about where to stand for one ability, worst outcome first.
    ///
    /// Ordered so that merging two pulls is a maximum. A mechanic nobody held a spot for in one pull and did
    /// in the next is covered, and the pull that taught nothing should not be able to argue otherwise.
    /// </summary>
    public enum Coverage
    {
        /// <summary>It resolved, but touched nobody, so there was nothing to be standing away from.</summary>
        NeverLanded,

        /// <summary>It hit, with no cast bar and no headmarker. Nothing announced it in time to move.</summary>
        Unannounced,

        /// <summary>It announced itself and it hit, and nobody held a position for it worth reporting.</summary>
        Unheld,

        /// <summary>Position genuinely did not matter: it caught everyone wherever they stood.</summary>
        Incidental,

        /// <summary>Somebody held a spot for it, and this export says where.</summary>
        Prescribed,

        /// <summary>Nobody was touched by it, and where they stood through its cast bar is why.</summary>
        Avoided,
    }

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
    /// <remarks>
    /// Also returns what it managed to say about each ability, which is what the coverage report reads to work
    /// out which of a fight's named mechanics this export still has nothing useful to say about.
    /// </remarks>
    public static Dictionary<uint, Coverage> Append(StringBuilder sb, Replay replay, IReadOnlyCollection<Replay.Participant> involved, Func<Replay.Participant, string> label, Func<Replay.Action, bool> inScope, ArenaEstimate? arena = null)
    {
        var outcomes = new Dictionary<uint, Coverage>();
        if (involved.Count == 0)
        {
            return outcomes;
        }

        // A knockback lands its damage first and moves people a moment later, so a sample taken at the
        // resolution is still pre-displacement. This is how long to wait before asking where they ended up.
        const double SettleSeconds = 2d;

        // ability -> player -> where they were when the cast began, when it landed, and once things settled
        var byAbility = new Dictionary<ActionID, Dictionary<Replay.Participant, List<Sample>>>();
        var resolutions = new Dictionary<ActionID, int>();
        var telegraphed = new Dictionary<ActionID, int>();

        // An ability that never touched anybody teaches nothing about where to stand, only that it was
        // avoidable, so the hints below are built from the ones that connected at least once.
        var landed = new HashSet<ActionID>();

        // When a headmarker last appeared on each player. A cast bar is not the only way a mechanic announces
        // itself: savage marks its targets and resolves instantly, and filtering the hints to cast bars alone
        // threw those away. A first real savage export left out seven abilities that way, headmarkers named
        // SpreadLockon and ShareMulti among them, which are precisely the ones a positional hint is for.
        // Kept apart from the cast-bar count so the detail section can still say an ability had no cast bar,
        // which stays true of one that announced itself with a marker instead.
        // Rounded resolution moments per ability, for spotting the ones that always go off together.
        var moments = new Dictionary<ActionID, HashSet<long>>();
        var marked = new HashSet<ActionID>();
        var markers = new Dictionary<Replay.Participant, List<DateTime>>();
        foreach (var icon in replay.Icons)
        {
            if (icon.Target != null)
            {
                markers.GetOrAdd(icon.Target).Add(icon.Timestamp);
            }
        }

        // Whether an ability ever caught more than one person at once. A mechanic that gathers or scatters the
        // party is worth a hint from a single cast; one that picks somebody at random is not.
        var grouped = new HashSet<ActionID>();

        foreach (var a in replay.Actions)
        {
            if (!IsHostile(a.Source) || !inScope(a))
            {
                continue;
            }

            var playersHit = 0;
            foreach (var t in a.Targets)
            {
                if (t.Target.Type == ActorType.Player)
                {
                    ++playersHit;
                }
            }

            if (playersHit > 0)
            {
                landed.Add(a.ID);
            }

            if (playersHit > 1)
            {
                grouped.Add(a.ID);
            }

            if (WasMarked(markers, a))
            {
                marked.Add(a.ID);
            }

            var hitAt = a.Timestamp;
            var castAt = CastStart(a.Source, a.ID, hitAt);

            resolutions[a.ID] = resolutions.GetValueOrDefault(a.ID) + 1;
            moments.GetOrAdd(a.ID).Add(a.Timestamp.Ticks / PairingTicks);
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

        // Cached because both sections below want it and working it out walks every action in the replay.
        var shapes = new Dictionary<ActionID, (string Text, bool Positional)>();
        foreach (var aid in byAbility.Keys)
        {
            shapes[aid] = Classify(replay, aid, involved.Count, inScope);
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
            sb.Append("  looks like: ").AppendLine(shapes[aid].Text);

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

        AppendHints(sb, byAbility, resolutions, telegraphed, marked, landed, grouped, shapes, moments, label, arena, outcomes);
        return outcomes;
    }

    // A cast-time spread this tight means the position was chosen rather than stumbled into, and is the line
    // between a spot worth writing into a module and where somebody happened to be standing.
    private const float FixedSpot = 1.5f;
    private const float LooseSpot = 3f;

    /// <summary>
    /// The same data again, said as instructions.
    ///
    /// Everything above describes what happened. This answers the question the fork exists for: given a role,
    /// where should that person be standing when this cast goes off. It reads from the cast moment rather than
    /// the resolution, because by the time a mechanic resolves the choice has already been made.
    ///
    /// The first version of this printed every player against every ability and was useless: five exports
    /// produced two thousand rows saying a position was not held, against four hundred saying it was. A line
    /// reporting a mean position next to a note that it varied by thirty yards is worse than no line, since
    /// the mean reads as a place to stand. Only what was actually held is printed now, and only for abilities
    /// that were telegraphed, since an instant hit gives nobody anything to position for.
    /// </summary>
    private static void AppendHints(StringBuilder sb,
        Dictionary<ActionID, Dictionary<Replay.Participant, List<Sample>>> byAbility,
        Dictionary<ActionID, int> resolutions,
        Dictionary<ActionID, int> telegraphed,
        HashSet<ActionID> marked,
        HashSet<ActionID> landed,
        HashSet<ActionID> grouped,
        Dictionary<ActionID, (string Text, bool Positional)> shapes,
        Dictionary<ActionID, HashSet<long>> moments,
        Func<Replay.Participant, string> label,
        ArenaEstimate? arena,
        Dictionary<uint, Coverage> outcomes)
    {
        // Only spells, and only the best outcome for one that appears twice. Everything that joins against a
        // cactbot timeline joins on the spell ID, and nothing else has one.
        void record(ActionID aid, Coverage c)
        {
            if (aid.Type == ActionType.Spell && (!outcomes.TryGetValue(aid.ID, out var prev) || c > prev))
            {
                outcomes[aid.ID] = c;
            }
        }

        sb.AppendLine("========================================================================");
        sb.AppendLine("WHERE TO STAND, per role, at the moment each cast begins");
        sb.AppendLine("Only abilities that announced themselves first, by cast bar or headmarker, and only");
        sb.AppendLine("positions that were actually held.");
        sb.AppendLine("An ability that touched nobody is the best case here rather than the worst: the party");
        sb.AppendLine("dodged it, so where they stood is a spot proven to be safe.");
        sb.AppendLine("Anything a player wandered around is left out: a mean position with a wide spread behind it");
        sb.AppendLine("reads as a place to stand, and is not one.");
        sb.AppendLine();

        var shown = 0;
        var skippedInstant = 0;
        var skippedUnheld = 0;

        foreach (var (aid, perPlayer) in byAbility)
        {
            // An ability that touched nobody used to be skipped here, on the reasoning that it teaches
            // nothing about where to stand. That is exactly backwards for the question this section asks. A
            // telegraphed mechanic that landed on nobody is one the party dodged clean, and where they stood
            // through its cast bar is the right answer by definition, with no damage taken to argue against
            // it. Measured across five real exports it was not an edge case either: fifty-seven of the
            // ninety-two mechanics being reported as gaps were mechanics the party had played perfectly.
            var avoided = !landed.Contains(aid);

            // Nothing announced this one. If it hit, nobody had time to move for it and there is no position
            // to prescribe. If it also touched nobody then nothing announced it and nothing came of it, which
            // is a boss buffing itself rather than a mechanic the party got right.
            if (telegraphed.GetValueOrDefault(aid) == 0 && !marked.Contains(aid))
            {
                record(aid, avoided ? Coverage.NeverLanded : Coverage.Unannounced);
                if (!avoided)
                {
                    ++skippedInstant;
                }

                continue;
            }

            // Classify reads shapes from who got hit, so it has nothing to work with here and says so. What
            // the ability was aimed at does not matter when the answer is the same either way: stand where
            // they stood.
            (string Text, bool Positional) shape = avoided
                ? ("dodged every cast, so this is where the party stood to avoid it", true)
                : shapes.GetValueOrDefault(aid);

            // An ability that caught everyone wherever they were has no position to take, and printing one
            // next to a line saying position did not matter is a contradiction the reader has to resolve.
            if (!shape.Positional)
            {
                record(aid, Coverage.Incidental);
                ++shown;
                sb.Append(aid.ToString()).Append("  (").Append(shape.Text).AppendLine(")");
                sb.AppendLine("  nothing to position for: everyone was caught wherever they stood");
                sb.AppendLine();
                continue;
            }

            var casts = resolutions.GetValueOrDefault(aid);
            var isGroup = grouped.Contains(aid);
            var rows = new List<string>();

            foreach (var (p, samples) in perPlayer)
            {
                var castOffsets = new List<WDir>(samples.Count);
                foreach (var sample in samples)
                {
                    castOffsets.Add(sample.AtCast);
                }

                var (mean, spread) = MeanAndSpread(castOffsets);

                // A single cast cannot show whether a position was held, so it only earns a line when the
                // ability gathered or scattered the party, which is the case where one cast is all there is.
                // A single cast nobody was hit by is the other exception: taking no damage is proof the spot
                // was safe, which is more than a tidy-looking mean on a cast that landed can claim.
                var trustworthy = samples.Count > 1 ? spread < LooseSpot : isGroup || avoided;
                if (!trustworthy)
                {
                    continue;
                }

                var row = new StringBuilder();
                row.Append("  ").Append(label(p).PadRight(24))
                   .Append("from caster ").Append(Fixed(mean.Length())).Append("y ").Append(Octant(mean).PadRight(8));

                if (arena != null)
                {
                    var centre = new List<WDir>(samples.Count);
                    foreach (var sample in samples)
                    {
                        centre.Add(sample.CastWorld - arena.Reference);
                    }

                    var (fromCentre, _) = MeanAndSpread(centre);
                    var fraction = arena.Scale > 0f ? fromCentre.Length() / arena.Scale : 0f;
                    row.Append("from centre ").Append(fraction.ToString("f2")).Append("r ").Append(Octant(fromCentre).PadRight(8));
                }

                rows.Add(row.Append(Confidence(samples.Count, casts, spread, avoided)).ToString());
            }

            if (rows.Count == 0)
            {
                record(aid, Coverage.Unheld);
                ++skippedUnheld;
                continue;
            }

            record(aid, avoided ? Coverage.Avoided : Coverage.Prescribed);
            ++shown;
            sb.Append(aid.ToString()).Append("  (").Append(shape.Text).AppendLine(")");

            // Two mechanics that always fire together produce identical arena positions and caster distances
            // that differ by however far apart their casters stand, which reads as the same rows twice with
            // one column changed. Saying they are one mechanic is quicker than working that out.
            var partner = PartnerOf(aid, moments);
            if (partner != null)
            {
                sb.Append("  fires together with ").Append(partner.Value.ToString())
                  .AppendLine(", so measure from the centre rather than from either caster");
            }

            foreach (var row in rows)
            {
                sb.AppendLine(row);
            }

            sb.AppendLine();
        }

        if (shown == 0)
        {
            sb.AppendLine("Nobody held a position tightly enough here for it to look prescribed. That is the normal");
            sb.AppendLine("answer for content where standing anywhere works, and is worth knowing rather than hiding.");
            sb.AppendLine();
        }

        // Said out loud so a short section does not read as data having gone missing.
        if (skippedInstant > 0 || skippedUnheld > 0)
        {
            sb.Append("Left out: ").Append(skippedInstant).Append(" ability(s) with no warning at all, and ")
              .Append(skippedUnheld).AppendLine(" where nobody held a position.");
            sb.AppendLine();
        }
    }

    // Resolutions within this of each other count as the same moment. Half a second, which is longer than
    // the jitter between two casts started on the same server tick and shorter than any real gap.
    private const long PairingTicks = TimeSpan.TicksPerSecond / 2;

    /// <summary>
    /// Another ability that resolves whenever this one does. Savage pairs its mechanics constantly, and a pair
    /// reported separately looks like one mechanic listed twice with the distances inexplicably changed.
    /// </summary>
    private static ActionID? PartnerOf(ActionID aid, Dictionary<ActionID, HashSet<long>> moments)
    {
        if (!moments.TryGetValue(aid, out var mine) || mine.Count < 2)
        {
            return null;
        }

        foreach (var (other, theirs) in moments)
        {
            if (other == aid || theirs.Count < 2)
            {
                continue;
            }

            var shared = 0;
            foreach (var t in mine)
            {
                if (theirs.Contains(t))
                {
                    ++shared;
                }
            }

            // Nearly every resolution of the smaller one, so an ability that merely overlaps once is not a pair.
            if (shared >= (int)(Math.Min(mine.Count, theirs.Count) * 0.8f) && shared >= 2)
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>How much weight a single row deserves, which one cast cannot earn however tidy it looks.</summary>
    private static string Confidence(int samples, int casts, float spread, bool avoided) => samples switch
    {
        < 2 => avoided
            ? "the only cast, and it hit nobody, so the spot was safe at least once"
            : "the only cast, so this is where they were rather than where to be",
        _ => (spread < FixedSpot, avoided) switch
        {
            (true, true) => $"safe from within {spread:f1}y across {samples} of {casts} casts, none of which hit anybody",
            (true, false) => $"held to within {spread:f1}y across {samples} of {casts} casts",
            (false, true) => $"safe from roughly here, {spread:f1}y across {samples} of {casts} casts, none of which hit anybody",
            _ => $"roughly held, {spread:f1}y across {samples} of {casts} casts"
        }
    };

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

    // How long a headmarker can precede its resolution and still be its warning. Long enough for the spread
    // markers that sit on people for most of a mechanic, short enough not to claim the previous one's.
    private const double MarkerWarning = 15d;

    /// <summary>Whether anybody this ability hit was wearing a headmarker in the seconds before it landed.</summary>
    private static bool WasMarked(Dictionary<Replay.Participant, List<DateTime>> markers, Replay.Action a)
    {
        foreach (var t in a.Targets)
        {
            if (t.Target.Type != ActorType.Player || !markers.TryGetValue(t.Target, out var times))
            {
                continue;
            }

            for (var i = 0; i < times.Count; ++i)
            {
                var lead = (a.Timestamp - times[i]).TotalSeconds;
                if (lead >= 0d && lead <= MarkerWarning)
                {
                    return true;
                }
            }
        }

        return false;
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
    private static (string Text, bool Positional) Classify(Replay replay, ActionID aid, int partySize, Func<Replay.Action, bool> inScope)
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
            return ("hit nobody, so it was either dodged every time or does not target players", false);
        }

        var avgHit = hits.Average();
        var avgSpan = spans.Count > 0 ? spans.Average() : 0f;
        var avgClosest = closest.Count > 0 ? closest.Average() : 0f;

        if (avgHit < 1.5f)
        {
            // Every cast hit one person, but not the same person, so listing the roles it touched reads as a
            // contradiction: "single target, hitting tank and ranged and healer". It picked somebody.
            var role = roles.Count == 1 ? roles.First() : Role.None;
            return (role switch
            {
                Role.Tank => "single target on a tank every time, so probably a tank buster",
                Role.Healer => "single target on a healer every time",
                _ => $"single target, picking a different player from cast to cast ({Describe(roles)} caught at least once)"
            }, true);
        }

        // Everyone caught, spread across the arena: position made no difference.
        if (avgHit >= partySize - 0.5f && avgSpan > 12f)
        {
            return ($"raidwide, everyone hit wherever they stood (up to {avgSpan:f1}y apart)", false);
        }

        if (avgSpan <= 6f)
        {
            var who = avgHit >= partySize - 0.5f ? "full party" : avgHit <= 4.5f ? "light party" : "part of the party";
            return ($"{who} stack, {avgHit:f1} players within {avgSpan:f1}y of each other", true);
        }

        if (avgClosest >= 8f)
        {
            return ($"spread, {avgHit:f1} players with nobody closer than {avgClosest:f1}y", true);
        }

        return avgHit >= partySize - 0.5f
            ? ($"everyone hit, {avgSpan:f1}y apart, so a raidwide or a loose stack", true)
            : ($"{avgHit:f1} players hit, {avgSpan:f1}y apart, hitting {Describe(roles)}", true);
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
