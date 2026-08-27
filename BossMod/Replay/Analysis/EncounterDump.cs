using Dalamud.Bindings.ImGui;
using System.IO;

namespace BossMod.ReplayAnalysis;

/// <summary>
/// Dumps everything about one encounter as a single chronological text document: state machine timings,
/// every cast with its duration and targets, head markers, tethers, statuses and arena effects, followed by
/// a position summary per role.
///
/// The per-category analysis passes are better for answering one narrow question. This exists because writing
/// a module needs all of it at once and in time order, which is the shape a state machine and a set of hints
/// are actually built from.
/// </summary>
sealed class EncounterDump : CommonEnumInfo
{
    /// <summary>Pets and buddies fight on your side, so "not a player" is the wrong test for hostility.</summary>
    private static bool IsHostile(Replay.Participant p)
        => p.Type is not (ActorType.Player or ActorType.Pet or ActorType.Chocobo or ActorType.Buddy);

    // A long ultimate can produce tens of thousands of events. The cap keeps the file readable; the true count
    // is always written into the file so a truncated dump never reads as complete.
    // Below this a pull contains no mechanics at all, only a boss dying mid-cast.
    private const double TrivialPullSeconds = 15d;

    // How far two pulls' centres can sit apart and still be the same room. Generous, because the estimate
    // itself moves by a couple of yards between pulls; anything beyond this is a different instance.
    private const float SameArenaTolerance = 15f;

    private const int MaxEvents = 5000;

    private readonly record struct Event(DateTime Timestamp, int Order, string Text);

    private readonly Type? _aidType;
    private readonly Type? _sidType;
    private readonly Type? _iidType;
    private readonly Type? _tidType;
    private readonly uint _oid;
    private readonly string _moduleName;
    private readonly List<(Replay Replay, Replay.Encounter Encounter)> _encounters = [];
    private readonly List<Replay> _replays;

    public EncounterDump(List<Replay> replays, uint oid)
    {
        _oid = oid;
        _replays = replays;
        var moduleInfo = BossModuleRegistry.FindByOID(oid);
        _oidType = moduleInfo?.ObjectIDType;
        _aidType = moduleInfo?.ActionIDType;
        _sidType = moduleInfo?.StatusIDType;
        _iidType = moduleInfo?.IconIDType;
        _tidType = moduleInfo?.TetherIDType;
        _moduleName = moduleInfo?.ModuleType.Name ?? "no module";

        foreach (var replay in replays)
        {
            foreach (var enc in replay.Encounters)
            {
                if (enc.OID == oid)
                {
                    _encounters.Add((replay, enc));
                }
            }
        }
    }

    public void Draw(UITree tree)
    {
        tree.LeafNode($"{_encounters.Count} recorded pull(s) of {_moduleName}.");

        // Buttons rather than a right-click menu. The menu is still there for consistency with the other
        // passes, but a hidden right-click is a poor way to expose the one action people actually want,
        // and it is worse on a trackpad.
        if (ImGui.Button("Export to file"))
        {
            Export();
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy to clipboard"))
        {
            ImGui.SetClipboardText(BuildAll());
            Service.ChatGui.Print("[BMR] Encounter dump copied to clipboard.");
        }

        tree.LeafNode($"Export writes to: {TargetDirectory()}", Colors.TextColor2);
    }

    public void DrawContextMenu()
    {
        if (ImGui.MenuItem("Export everything to a file"))
        {
            Export();
        }

        if (ImGui.MenuItem("Copy everything to clipboard"))
        {
            ImGui.SetClipboardText(BuildAll());
        }
    }

    private void Export()
    {
        var export = new PositionExport();
        var learned = new LearnedPositions();
        var text = BuildAll(export, learned);
        var name = $"encounter-{_moduleName}-{_oid:X}.txt";

        try
        {
            var path = Path.Combine(TargetDirectory(), name);
            File.WriteAllText(path, text);

            // Alongside rather than instead: the text is what a person reads, this is what anything else does,
            // and losing the second must not cost the first.
            try
            {
                File.WriteAllText(Path.ChangeExtension(path, ".json"), export.Build());
                LearnedPositions.Merge(Path.Combine(TargetDirectory(), LearnedPositions.FileName), learned);

                // So the next pull uses what this export just learned, without restarting the game.
                MechanicTimersWindow.ForgetLearned();
            }
            catch (Exception inner)
            {
                Service.Log($"[EncounterDump] positions written to text but not to data: {inner.Message}");
            }

            // The path is the whole point of exporting rather than copying, so it goes to chat where it can be
            // read without digging through logs.
            Service.ChatGui.Print($"[BMR] Encounter dump written to {path}");
            Service.Log($"[EncounterDump] wrote {text.Length} chars to {path}");
        }
        catch (Exception e)
        {
            Service.ChatGui.Print($"[BMR] Could not write the dump: {e.Message}. Copied to clipboard instead.");
            ImGui.SetClipboardText(text);
        }
    }

    public const string FolderName = "Current Duties";

    // Resolved once. The label in the export prompt is drawn every frame, and creating a directory that often
    // to answer a question whose answer never changes would be silly.
    private static string? _targetDirectory;

    /// <summary>
    /// A "Current Duties" folder inside the user's Downloads, created on first use, so exports collect in one
    /// place instead of scattering. Under Wine, Downloads is normally symlinked to the host's real one, so the
    /// folder shows up in Finder or Explorer without hunting through a prefix. Falls back to somewhere
    /// writable rather than failing.
    /// </summary>
    public static string TargetDirectory()
    {
        if (_targetDirectory != null)
        {
            return _targetDirectory;
        }

        var root = ResolveRoot();
        try
        {
            var folder = Path.Combine(root, FolderName);
            Directory.CreateDirectory(folder);
            return _targetDirectory = folder;
        }
        catch (Exception e)
        {
            // A read-only or otherwise unusable Downloads should degrade to writing loose files, not to
            // failing every export.
            Service.Log($"[EncounterDump] could not create {FolderName} under {root}: {e.Message}");
            return _targetDirectory = root;
        }
    }

    private static string ResolveRoot()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
        {
            var downloads = Path.Combine(profile, "Downloads");
            if (Directory.Exists(downloads))
            {
                return downloads;
            }

            if (Directory.Exists(profile))
            {
                return profile;
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 && Directory.Exists(documents) ? documents : Path.GetTempPath();
    }

    public string BuildAll() => BuildAll(null, null);

    public string BuildAll(PositionExport? export) => BuildAll(export, null);

    /// <summary>
    /// The text export, and optionally the same analysis collected as data on the way past.
    ///
    /// One pass produces both, so the two can never disagree about what was seen.
    /// </summary>
    public string BuildAll(PositionExport? export, LearnedPositions? learned)
    {
        if (export != null)
        {
            export.Boss = _moduleName;
            export.OID = _oid;
            for (var i = 0; i < _encounters.Count; ++i)
            {
                var enc = _encounters[i].Encounter;
                export.Zone = enc.Zone;
                export.Pulls.Add((i + 1, _oid, _moduleName, enc.Time.Start, enc.Time.End));
            }
        }

        var sb = new StringBuilder();
        sb.Append("Encounter dump for ").Append(_moduleName).Append(" (OID ").Append($"{_oid:X}").AppendLine(")");
        sb.Append(_encounters.Count).AppendLine(" recorded pull(s).");
        sb.AppendLine("Times are seconds from the start of each pull. Positions are world coordinates.");
        sb.AppendLine();

        for (var i = 0; i < _encounters.Count; ++i)
        {
            var (replay, enc) = _encounters[i];
            BuildOne(sb, replay, enc, i + 1);
        }

        AppendPositions(sb, _replays, export, learned);
        return sb.ToString();
    }

    private void BuildOne(StringBuilder sb, Replay replay, Replay.Encounter enc, int index)
    {
        var start = enc.Time.Start;
        float rel(DateTime t) => (float)(t - start).TotalSeconds;

        sb.AppendLine("========================================================================");
        sb.Append("PULL ").Append(index).Append("  zone ").Append(enc.Zone)
          .Append("  duration ").Append(enc.Time.Duration.ToString("f1")).AppendLine("s");
        sb.Append("source: ").AppendLine(replay.Path);
        sb.AppendLine();

        var roles = Service.Config.Get<PartyRolesConfig>();
        sb.AppendLine("--- PARTY ---");
        foreach (var (p, cls, level) in enc.PartyMembers)
        {
            var role = roles[p.ContentID];
            sb.Append("  ").Append(role.ToString().PadRight(11))
              .Append(cls.ToString().PadRight(12))
              .Append("Lv").Append(level).Append("  ")
              .AppendLine(p.NameAt(start).name ?? "<unknown>");
        }

        // A pull this short cannot contain a mechanic. Almost always an unrestricted party running old content
        // at level, where the boss dies during its opening cast, and the export would otherwise look broken
        // rather than empty for a reason.
        if (enc.Time.Duration < TrivialPullSeconds)
        {
            sb.Append("  This pull lasted ").Append(enc.Time.Duration.ToString("f1"))
              .AppendLine("s. Nothing lived long enough to use a mechanic, so there is nothing here to learn from.");
        }

        sb.AppendLine();

        // The state machine is what a timer bar reads from, so it is dumped even when it is a single trivial phase.
        sb.AppendLine("--- STATE MACHINE ---");
        if (enc.States.Count == 0)
        {
            sb.AppendLine("  (none: this module uses a trivial phase, so there are no per-state timings to read)");
        }
        else
        {
            var stateStart = start;
            foreach (var st in enc.States)
            {
                sb.Append("  T+").Append(rel(stateStart).ToString("f1").PadLeft(7))
                  .Append("  ").Append($"{st.ID:X8}")
                  .Append(" '").Append(st.Name).Append('\'')
                  .Append(st.Comment.Length > 0 ? $" ({st.Comment})" : "")
                  .Append("  actual ").Append(rel(st.Exit).ToString("f1"))
                  .Append("s, expected ").Append(st.ExpectedDuration.ToString("f1")).AppendLine("s");
                stateStart = st.Exit;
            }
        }
        sb.AppendLine();

        var events = CollectEvents(replay, enc);
        events.Sort((a, b) => a.Timestamp != b.Timestamp ? a.Timestamp.CompareTo(b.Timestamp) : a.Order.CompareTo(b.Order));

        sb.AppendLine("--- TIMELINE ---");
        var shown = Math.Min(events.Count, MaxEvents);
        for (var i = 0; i < shown; ++i)
        {
            sb.Append("  T+").Append(rel(events[i].Timestamp).ToString("f1").PadLeft(7)).Append("  ").AppendLine(events[i].Text);
        }

        if (events.Count > MaxEvents)
        {
            sb.Append("  ... ").Append(events.Count - MaxEvents).AppendLine(" further events omitted.");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Where the party stood, per pull. Uses the same analysis as a recording with no module, since there is
    /// no reason a fight somebody has written a module for should get the weaker of the two; the only
    /// difference is that roles are known here, so rows are labelled by role rather than by name.
    /// </summary>
    /// <summary>
    /// Where the party stood, pooled across every pull of this boss when that is possible.
    ///
    /// A mechanic that fires once a pull gives one sample, and one sample cannot show whether a position was
    /// chosen or stumbled into. Twenty pulls of the same fight can, and a progression session is exactly where
    /// twenty pulls exist. Pooling them is the difference between "the tank was here" and "the tank was here
    /// on eighteen of twenty pulls, within a yard", and only the second is worth writing into a module.
    /// </summary>
    private void AppendPositions(StringBuilder sb, List<Replay> replays, PositionExport? export, LearnedPositions? learned)
    {
        var roles = Service.Config.Get<PartyRolesConfig>();
        string label(Replay.Participant p) => Label(roles[p.ContentID], p);

        // Coverage is worked out across every pull whatever happens below, and merged rather than printed per
        // pull. Positions have to be pooled carefully, since pulls in different arena instances cannot share a
        // centre, but whether a mechanic was ever reached has nothing to do with where the room was: a
        // mechanic seen on one pull out of seven is covered, and saying so six more times is noise.
        var coverage = new Dictionary<uint, PositionAnalysis.Coverage>();
        void merge(Dictionary<uint, PositionAnalysis.Coverage> from)
        {
            foreach (var (id, c) in from)
            {
                if (!coverage.TryGetValue(id, out var prev) || c > prev)
                {
                    coverage[id] = c;
                }
            }
        }

        var pool = Poolable();
        if (pool != null)
        {
            var (pooled, party, from, to) = pool.Value;

            sb.AppendLine("========================================================================");
            sb.Append("POSITIONS pooled across all ").Append(_encounters.Count).AppendLine(" pulls");
            sb.AppendLine("Every pull of this boss together, so a mechanic that fires once per pull still has");
            sb.AppendLine("enough samples to say whether its position was chosen or incidental.");
            sb.AppendLine();

            var arena = ArenaEstimate.ForFight(pooled, _oid, party, from, to);
            Record(export, arena);
            merge(PositionAnalysis.Append(sb, pooled, party, label, InAnyPull, arena, ElapsedIntoPull, export, learned));
            AppendCoverage(sb, coverage, export);
            return;
        }

        for (var i = 0; i < _encounters.Count; ++i)
        {
            var (replay, enc) = _encounters[i];
            var party = new List<Replay.Participant>(enc.PartyMembers.Count);
            foreach (var (p, _, _) in enc.PartyMembers)
            {
                party.Add(p);
            }

            sb.AppendLine("========================================================================");
            sb.Append("POSITIONS for pull ").AppendLine((i + 1).ToString());

            // The module declares the real arena, so nothing here is guessed. The estimate runs alongside it
            // anyway: comparing the two is what tells us how far short of the wall a party gets, which is the
            // correction the content with no module to check against has to borrow.
            var arena = ArenaEstimate.ForFight(replay, enc.OID, party, enc.Time.Start, enc.Time.End);

            if (i == 0)
            {
                Record(export, arena);
            }

            merge(PositionAnalysis.Append(sb, replay, party, label,
                a => enc.Time.Contains(a.Timestamp),
                arena, ElapsedIntoPull, export, learned));
        }

        AppendCoverage(sb, coverage, export);
    }

    /// <summary>The arena the positions are measured against, so a reader can put them on a map.</summary>
    private static void Record(PositionExport? export, ArenaEstimate? arena)
    {
        if (export != null && arena != null)
        {
            export.ArenaCenter = arena.Reference;
            export.ArenaScale = arena.Scale;
            export.ArenaShape = arena.Shape;
        }
    }

    /// <summary>What this boss is known to do, against what the pulls above managed to teach.</summary>
    private void AppendCoverage(StringBuilder sb, Dictionary<uint, PositionAnalysis.Coverage> coverage, PositionExport? export)
    {
        var windows = new List<(Replay, Replay.TimeRange)>(_encounters.Count);
        foreach (var (replay, enc) in _encounters)
        {
            windows.Add((replay, enc.Time));
        }

        var observed = TimelineCoverage.Observe(windows);
        if (export != null)
        {
            export.Timeline = Timelines.TimelineLibrary.Best(observed)?.Name;
        }

        TimelineCoverage.Append(sb, observed, coverage);
    }

    /// <summary>
    /// How far into its own pull a moment falls.
    ///
    /// This is what lets the position analysis pool seven pulls and still tell one use of an ability from
    /// another. Wall-clock time would put every pull in a bucket of its own, which is the opposite of pooling;
    /// time into the pull puts the same moment from all seven together and keeps a later moment apart.
    /// </summary>
    private double ElapsedIntoPull(DateTime t)
    {
        for (var i = 0; i < _encounters.Count; ++i)
        {
            var time = _encounters[i].Encounter.Time;
            if (time.Contains(t))
            {
                return (t - time.Start).TotalSeconds;
            }
        }

        return 0d;
    }

    /// <summary>Whether an action happened during any pull, so the gaps between them are left out.</summary>
    private bool InAnyPull(Replay.Action a)
    {
        for (var i = 0; i < _encounters.Count; ++i)
        {
            if (_encounters[i].Encounter.Time.Contains(a.Timestamp))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The pulls, the party and the span to pool over, or nothing when pooling would be a lie.
    ///
    /// Two things rule it out. Pulls from different recordings cannot be walked as one, since the analysis
    /// reads actions from a single replay. And pulls in arena instances at different world coordinates cannot
    /// share a centre, so measuring them all from one would put half of them somewhere nobody stood.
    /// </summary>
    private (Replay Replay, List<Replay.Participant> Party, DateTime From, DateTime To)? Poolable()
    {
        if (_encounters.Count < 2)
        {
            return null;
        }

        var replay = _encounters[0].Item1;
        var from = DateTime.MaxValue;
        var to = DateTime.MinValue;
        var party = new List<Replay.Participant>();
        var seen = new HashSet<Replay.Participant>();
        WPos? centre = null;

        for (var i = 0; i < _encounters.Count; ++i)
        {
            var (r, enc) = _encounters[i];
            if (!ReferenceEquals(r, replay))
            {
                return null;
            }

            var members = new List<Replay.Participant>(enc.PartyMembers.Count);
            foreach (var (p, _, _) in enc.PartyMembers)
            {
                members.Add(p);
                if (seen.Add(p))
                {
                    party.Add(p);
                }
            }

            // Each pull's own reading of where it happened. Far apart means separate instances of the arena.
            var here = ArenaEstimate.Derive(members, enc.Time.Start, enc.Time.End);
            if (here != null)
            {
                if (centre is WPos first && (here.Center - first).Length() > SameArenaTolerance)
                {
                    return null;
                }

                centre ??= here.Center;
            }

            if (enc.Time.Start < from)
            {
                from = enc.Time.Start;
            }

            if (enc.Time.End > to)
            {
                to = enc.Time.End;
            }
        }

        return party.Count > 0 && to > from ? (replay, party, from, to) : null;
    }

    /// <summary>
    /// What to call a player in a file somebody else may read. Their role where one is assigned, and their job
    /// beside a handle where none is. Never their name: the recording keeps that, the export travels.
    /// </summary>
    private static string Label(PartyRolesConfig.Assignment role, Replay.Participant p)
        => role != PartyRolesConfig.Assignment.Unassigned
            ? $"{role} {p.Class}"
            : $"{p.Class} {SharedIdentity.Handle(p.ContentID)}";

    private List<Event> CollectEvents(Replay replay, Replay.Encounter enc)
    {
        var events = new List<Event>();
        var window = enc.Time;

        // Cast starts, which is what a hint has to fire before. Player casts are excluded; a party's rotation is
        // thousands of lines and answers a different question.
        foreach (var p in replay.Participants)
        {
            if (!IsHostile(p))
            {
                continue;
            }

            foreach (var c in p.Casts)
            {
                if (c.Time.Start < window.Start || c.Time.Start > window.End)
                {
                    continue;
                }

                var target = c.Target != null ? Describe(c.Target, c.Time.Start) : $"location {Pos(c.Location.X, c.Location.Z)}";
                events.Add(new(c.Time.Start, 0,
                    $"CAST   {Ability(c.ID)} by {Describe(p, c.Time.Start)} {Pos(p.PosRotAt(c.Time.Start))} -> {target}, {c.Time.Duration:f1}s of {c.ExpectedCastTime:f1}s{(c.Interruptible ? ", interruptible" : "")}"));
            }
        }

        foreach (var a in replay.EncounterActions(enc))
        {
            if (!IsHostile(a.Source))
            {
                continue;
            }

            var hits = new List<string>(a.Targets.Count);
            foreach (var t in a.Targets)
            {
                hits.Add(Describe(t.Target, a.Timestamp));
            }

            events.Add(new(a.Timestamp, 1,
                $"HIT    {Ability(a.ID)} from {Describe(a.Source, a.Timestamp)} at {Pos(a.TargetPos.X, a.TargetPos.Z)} -> {(hits.Count > 0 ? string.Join(", ", hits) : "nobody")}"));
        }

        foreach (var i in replay.EncounterIcons(enc))
        {
            var name = _iidType?.GetEnumName(i.ID);
            events.Add(new(i.Timestamp, 2,
                $"ICON   {i.ID}{(name != null ? $" ({name})" : "")} on {(i.Target != null ? Describe(i.Target, i.Timestamp) : "?")} {(i.Target != null ? Pos(i.Target.PosRotAt(i.Timestamp)) : "")}"));
        }

        foreach (var t in replay.EncounterTethers(enc))
        {
            var name = _tidType?.GetEnumName(t.ID);
            events.Add(new(t.Time.Start, 3,
                $"TETHER {t.ID}{(name != null ? $" ({name})" : "")} {Describe(t.Source, t.Time.Start)} -> {Describe(t.Target, t.Time.Start)}, {t.Time.Duration:f1}s"));
        }

        foreach (var st in replay.EncounterStatuses(enc))
        {
            // Statuses on enemies are mostly the boss's own bookkeeping; the ones that shape player behaviour
            // are the ones landing on players.
            if (st.Target.Type != ActorType.Player)
            {
                continue;
            }

            // ...and only the ones some non-player actor inflicted. Filtering out player-applied statuses was
            // not enough: Free Company buffs, food and sprint arrive with no source at all and sailed straight
            // through, outnumbering real mechanic debuffs two to one in a crowded instance.
            if (st.Source == null || st.Source.Type == ActorType.Player)
            {
                continue;
            }

            var name = _sidType?.GetEnumName(st.ID);
            events.Add(new(st.Time.Start, 4,
                $"STATUS {st.ID}{(name != null ? $" ({name})" : "")} on {Describe(st.Target, st.Time.Start)} for {st.InitialDuration:f1}s, extra {st.StartingExtra:X}"));
        }

        foreach (var m in replay.EncounterMapEffects(enc))
        {
            events.Add(new(m.Timestamp, 5, $"ARENA  index {m.Index} state {m.State:X8}"));
        }

        return events;
    }

    private string Ability(ActionID aid)
    {
        var name = aid.Type == ActionType.Spell ? _aidType?.GetEnumName(aid.ID) : null;
        return name != null ? $"{name} ({aid.ID})" : aid.ToString();
    }

    private string Describe(Replay.Participant p, DateTime t)
    {
        if (p.Type == ActorType.Player)
        {
            // The timeline names whoever a mechanic hit, hundreds of times over a pull, and a name there
            // would undo everything the labels above are careful about.
            var role = Service.Config.Get<PartyRolesConfig>()[p.ContentID];
            return role != PartyRolesConfig.Assignment.Unassigned ? role.ToString() : SharedIdentity.Handle(p.ContentID);
        }

        var name = _oidType?.GetEnumName(p.OID);
        return name ?? (p.NameAt(t).name is string n && n.Length > 0 ? n : $"{p.OID:X}");
    }

    private static string Pos(Vector4 posRot) => $"({posRot.X:f2}, {posRot.Z:f2})";

    private static string Pos(float x, float z) => $"({x:f2}, {z:f2})";
}
