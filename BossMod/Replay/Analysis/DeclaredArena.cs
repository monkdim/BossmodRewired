namespace BossMod.ReplayAnalysis;

/// <summary>
/// The arena a boss module declares, which for any fight somebody has written a module for is the real one.
///
/// Nothing derives this in the first place. Every module carries it as a literal a person read off the fight
/// and typed in, and the client never hands the actual map collision to a plugin in any usable form. But the
/// modules are loaded, so for a moduled encounter there is no reason to estimate: instantiating the module
/// away from a live world is enough to read its centre and bounds back out.
///
/// Note the qualified type names below: this namespace has its own unrelated ArenaBounds, the analysis-window
/// tool that draws player movement, and an unqualified reference binds to that one instead of the real bounds.
///
/// It is worth more than the encounters it covers. Running the estimate alongside a declaration that is known
/// to be right is the only way to find out how far short of the wall a party actually gets, and that
/// correction is what makes an estimate trustworthy for the content with no module at all.
/// </summary>
sealed record class DeclaredArena(WPos Center, float Radius, float NearEdge, float MaxReach, string Shape, bool CenterIsReliable)
{
    // Instantiating a module is not free and an encounter dump asks for the same OID once per pull.
    private static readonly Dictionary<uint, DeclaredArena?> _cache = [];

    public static DeclaredArena? ForOID(uint oid)
    {
        if (_cache.TryGetValue(oid, out var cached))
        {
            return cached;
        }

        DeclaredArena? res = null;
        try
        {
            using var module = BossModuleRegistry.CreateModuleForTimeline(oid);
            if (module != null)
            {
                var bounds = module.Bounds;

                // Modules are built here against a placeholder primary actor sitting at the origin. Most name
                // their centre as a literal and are unaffected, but the ones that take it from the boss's
                // position report the origin instead, and a centre of exactly zero in a real arena is not a
                // coordinate anybody fights at.
                var center = module.Center;
                var reliable = center != default;

                res = new(center, bounds.Radius, NearEdgeOf(bounds), ReachOf(bounds), Describe(bounds), reliable);
            }
        }
        catch (Exception e)
        {
            // A module that will not build away from a live world is not worth failing an export over; the
            // estimate still stands on its own.
            Service.Log($"[export] could not read declared arena for {oid:X}: {e.Message}");
        }

        _cache[oid] = res;
        return res;
    }

    /// <summary>
    /// How far the nearest wall is. In a square arena people line the walls and stay out of the corners, so
    /// this is the distance a party's outermost positions actually converge on, and measuring their coverage
    /// against the corner instead makes a fully explored arena look two thirds explored.
    /// </summary>
    private static float NearEdgeOf(BossMod.ArenaBounds bounds) => bounds is ABRect r ? Math.Min(r.HalfWidth, r.HalfHeight) : bounds.Radius;

    /// <summary>
    /// How far from the centre it is possible to stand, which is not the radius for anything with corners:
    /// a rectangle stores the longer half-side as its radius, so a square arena reaches a further 1.41 times
    /// into its corners. Comparing a party's reach against the radius instead would make every square arena
    /// look like the estimate had overshot.
    /// </summary>
    private static float ReachOf(BossMod.ArenaBounds bounds) => bounds is ABRect r
        ? MathF.Sqrt(r.HalfWidth * r.HalfWidth + r.HalfHeight * r.HalfHeight)
        : bounds.Radius;

    /// <summary>The bounds subclass names the shape outright, which is more than any amount of position data
    /// can establish.</summary>
    private static string Describe(BossMod.ArenaBounds bounds) => bounds switch
    {
        ArenaBoundsCircle => "circle",
        ArenaBoundsSquare sq => $"square, {sq.HalfWidth * 2f:f1}y across",
        ArenaBoundsRect r => $"rectangle, {r.HalfWidth * 2f:f1} by {r.HalfHeight * 2f:f1}y",
        ArenaBoundsCustom => "custom shape, so the reach below is only its bounding radius",
        _ => bounds.GetType().Name
    };
}
