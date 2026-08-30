namespace BossMod.ReplayAnalysis;

/// <summary>
/// What each player put in and took, over one pull.
///
/// This exists to answer a question about positions rather than about players. A position is only worth
/// learning from a pull that went well, and "went well" is not visible in coordinates: a party that spent the
/// fight running has tidy-looking positions and no damage, and a melee standing in the wrong place is
/// indistinguishable from one standing in the right place until you notice they stopped hitting anything.
///
/// Per pull rather than per recording, because a night is several pulls of varying quality and averaging them
/// describes none of them. The denominator is the pull's own length, so a wipe forty seconds in is not
/// flattered by being compared against a seven minute clear.
/// </summary>
static class Contributions
{
    /// <summary>One player's pull.</summary>
    public readonly record struct Line(Replay.Participant Player, long Damage, long Healing, long Taken, int Deaths);

    public static List<Line> ForWindow(Replay replay, IReadOnlyCollection<Replay.Participant> involved, DateTime from, DateTime to)
    {
        // Pets, chocobos and summons are their own actors and deal their own damage, so crediting only the
        // player type would quietly lose an arcanist's entire contribution and a good part of several others'.
        // Their damage belongs to whoever owns them.
        var owners = new Dictionary<ulong, Replay.Participant>();
        foreach (var p in involved)
        {
            owners[p.InstanceID] = p;
        }

        Replay.Participant Credit(Replay.Participant actor)
            => actor.OwnerID != 0 && owners.TryGetValue(actor.OwnerID, out var owner) ? owner : actor;

        var dealt = new Dictionary<Replay.Participant, long>();
        var healed = new Dictionary<Replay.Participant, long>();
        var taken = new Dictionary<Replay.Participant, long>();

        foreach (var a in replay.Actions)
        {
            if (a.Timestamp < from || a.Timestamp > to)
            {
                continue;
            }

            // The same test the reports use: a pet is on your side even though it is not a player.
            var sourceIsFriendly = a.Source.Type is ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy;

            foreach (var t in a.Targets)
            {
                // Copied to a local first: the indexer hands back raw ulongs, and ValidEffects returns a span
                // into the struct, which would point at a temporary if called on the property directly.
                var effects = t.Effects;
                foreach (var eff in effects.ValidEffects())
                {
                    switch (eff.Type)
                    {
                        case ActionEffectType.Damage:
                        case ActionEffectType.BlockedDamage:
                        case ActionEffectType.ParriedDamage:
                            if (sourceIsFriendly && t.Target.Type != ActorType.Player)
                            {
                                Add(dealt, Credit(a.Source), eff.DamageHealValue);
                            }
                            else if (!sourceIsFriendly && t.Target.Type == ActorType.Player)
                            {
                                Add(taken, t.Target, eff.DamageHealValue);
                            }
                            break;
                        case ActionEffectType.Heal:
                            if (sourceIsFriendly)
                            {
                                Add(healed, Credit(a.Source), eff.DamageHealValue);
                            }
                            break;
                    }
                }
            }
        }

        var lines = new List<Line>(involved.Count);
        foreach (var p in involved)
        {
            lines.Add(new(p, dealt.GetValueOrDefault(p), healed.GetValueOrDefault(p), taken.GetValueOrDefault(p), Deaths(p, from, to)));
        }

        lines.Sort(static (x, y) => y.Damage.CompareTo(x.Damage));
        return lines;
    }

    private static void Add(Dictionary<Replay.Participant, long> into, Replay.Participant p, int amount)
        => into[p] = into.GetValueOrDefault(p) + amount;

    /// <summary>
    /// Deaths inside the window.
    ///
    /// Counted as the moment somebody became dead rather than the number of entries saying so, since the
    /// history records what the state is and not only when it changes. Somebody already dead when the window
    /// opens is not counted again: they died in the pull before this one.
    /// </summary>
    private static int Deaths(Replay.Participant p, DateTime from, DateTime to)
    {
        var deaths = 0;
        var wasDead = false;
        foreach (var (t, dead) in p.DeadHistory)
        {
            if (t > to)
            {
                break;
            }

            if (dead && !wasDead && t >= from)
            {
                ++deaths;
            }

            wasDead = dead;
        }

        return deaths;
    }
}
