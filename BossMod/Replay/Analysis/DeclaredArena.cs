using System.Globalization;
using System.IO;

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
    // Concurrent because exports read it from the thread pool. Only the game's own thread ever writes.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, DeclaredArena?> _cache = new();

    /// <summary>
    /// Records what a module declared while it was actually running, which is both safer and better than
    /// building one later. Safer because it happens on the game's own thread, where a module constructor is
    /// allowed to touch game memory. Better because a live module's centre comes from the real boss rather
    /// than the placeholder actor an offline one is handed, so it never reads as the origin.
    ///
    /// This is what puts a declaration in reach of a background export: the fight being exported is the fight
    /// that was just played, so its module has already been through here.
    /// </summary>
    public static void Remember(BossModule module)
    {
        if (!Service.Framework.IsInFrameworkUpdateThread)
        {
            return;
        }

        var bounds = module.Bounds;
        _cache[module.PrimaryActor.OID] = new(module.Center, bounds.Radius, NearEdgeOf(bounds), ReachOf(bounds), Describe(bounds), module.Center != default);
    }

    public static DeclaredArena? ForOID(uint oid)
    {
        if (_cache.TryGetValue(oid, out var cached))
        {
            return cached;
        }

        // Nothing remembered, so it would have to be built, and building one runs an arbitrary module
        // constructor out of the nine hundred in the tree. A fair number touch game memory, which is only
        // safe on the game's own thread, and exports run on the thread pool. Off that thread this declines
        // rather than risks it, without caching the refusal as though it were an answer.
        if (!Service.Framework.IsInFrameworkUpdateThread)
        {
            return null;
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
    /// What a module declared after a particular director update, which is the only way to describe an arena
    /// that moves.
    ///
    /// <see cref="Remember"/> keeps one value per encounter, taken when the module loaded. For most fights
    /// that is the whole truth. For the ones that rebuild the arena partway through it is the opening state
    /// and nothing else: M11S shifts its centre fifteen yalms when a platform collapses, and a single
    /// remembered value says it never moved. Measured against the wrong centre, a position is wrong by the
    /// distance the centre travelled, which in a twenty yalm arena is most of the way across it.
    ///
    /// Keyed by the update that caused the change rather than by a phase number, so nothing here knows
    /// anything about any fight. The module already contains the mapping from update to arena; this records
    /// what the module did with it, and the export replays the same updates back over the table.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(uint OID, uint UpdateID, uint Param1), DeclaredArena> _afterDirector = new();

    private static string? _path;

    /// <summary>
    /// Points the table at a file and loads whatever is in it.
    ///
    /// Persisted because the alternative restricts this to fights played since the game started, and the
    /// export people actually run is a bulk re-export of a month of recordings, usually days later. In that
    /// path no module ever loads, nothing is remembered, and every pull silently falls back to an estimate.
    /// That is how a fork built on declared arenas shipped a corpus that never used one.
    /// </summary>
    public static void UseFile(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                // Shape runs to the end of the line because it contains commas and spaces.
                var f = line.Split('\t', 10);
                if (f.Length < 10)
                {
                    continue;
                }

                var ci = CultureInfo.InvariantCulture;
                _afterDirector[(uint.Parse(f[0], ci), uint.Parse(f[1], ci), uint.Parse(f[2], ci))] = new(
                    new(float.Parse(f[3], ci), float.Parse(f[4], ci)),
                    float.Parse(f[5], ci), float.Parse(f[6], ci), float.Parse(f[7], ci), f[9], f[8] == "1");
            }
        }
        catch (Exception e)
        {
            // A table that will not load costs accuracy, not the session. Estimates still stand on their own.
            Service.Logger.Error($"[export] could not read arena table from {path}: {e.Message}");
        }
    }

    /// <summary>
    /// Records the arena a live module is showing, against the director update that last reached it.
    ///
    /// Must be called a tick after the update rather than during it: the module is a subscriber to the same
    /// event, and nothing orders the subscribers, so reading its arena in the handler reads the arena the
    /// update was about to replace.
    /// </summary>
    public static void RememberAfterDirector(BossModule module, uint updateID, uint param1)
    {
        if (!Service.Framework.IsInFrameworkUpdateThread)
        {
            return;
        }

        var bounds = module.Bounds;
        var key = (module.PrimaryActor.OID, updateID, param1);
        var now = new DeclaredArena(module.Center, bounds.Radius, NearEdgeOf(bounds), ReachOf(bounds), Describe(bounds), module.Center != default);
        if (_afterDirector.TryGetValue(key, out var was) && was == now)
        {
            return;
        }

        _afterDirector[key] = now;
        Save();
    }

    /// <summary>What the arena became after this update, if it has ever been seen happening.</summary>
    public static DeclaredArena? AfterDirector(uint oid, uint updateID, uint param1)
        => _afterDirector.GetValueOrDefault((oid, updateID, param1));

    private static void Save()
    {
        var path = _path;
        if (path == null)
        {
            return;
        }

        try
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var (k, v) in _afterDirector)
            {
                sb.Append(k.OID.ToString(ci)).Append('\t').Append(k.UpdateID.ToString(ci)).Append('\t').Append(k.Param1.ToString(ci)).Append('\t')
                  .Append(v.Center.X.ToString(ci)).Append('\t').Append(v.Center.Z.ToString(ci)).Append('\t')
                  .Append(v.Radius.ToString(ci)).Append('\t').Append(v.NearEdge.ToString(ci)).Append('\t').Append(v.MaxReach.ToString(ci)).Append('\t')
                  .Append(v.CenterIsReliable ? '1' : '0').Append('\t')
                  // Newlines would break the line-per-entry format. No shape string contains one, but a
                  // future bounds type naming itself across two lines would corrupt every entry after it.
                  .Append(v.Shape.ReplaceLineEndings(" ")).Append('\n');
            }

            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception e)
        {
            Service.Logger.Error($"[export] could not write arena table to {path}: {e.Message}");
        }
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
