using Dalamud.Bindings.ImGui;

namespace BossMod.ReplayAnalysis;

sealed class AnalysisManager : IDisposable
{
    private class Lazy<T>(Func<T> init)
    {
        private readonly Func<T> _init = init;
        private T? _impl;

        public T Get() => _impl ??= _init();
    }

    private sealed class Global(List<Replay> replays)
    {
        private readonly Lazy<UnknownActionEffects> _unkEffects = new(() => new(replays));
        private readonly Lazy<ParticipantInfo> _participantInfo = new(() => new(replays));
        private readonly Lazy<AbilityInfo> _abilityInfo = new(() => new(replays));
        private readonly Lazy<ClassDefinitions> _classDefinitions = new(() => new(replays));
        private readonly Lazy<ClientActions> _clientActions = new(() => new(replays));
        private readonly Lazy<EffectResultMispredict> _effectResultMissing = new(() => new(replays, true));
        private readonly Lazy<EffectResultMispredict> _effectResultUnexpected = new(() => new(replays, false));
        private readonly Lazy<EffectResultReorder> _effectResultReorder = new(() => new(replays));

        public void Draw(UITree tree)
        {
            foreach (var n in tree.Node("Unknown action effects"))
            {
                _unkEffects.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Participant info", false, Colors.TextColor1, () => _participantInfo.Get().DrawContextMenu()))
            {
                _participantInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Ability info", false, Colors.TextColor1, () => _abilityInfo.Get().DrawContextMenu()))
            {
                _abilityInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Player class definitions"))
            {
                _classDefinitions.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Client action weirdness"))
            {
                _clientActions.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Effect results: missing confirmations"))
            {
                _effectResultMissing.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Effect results: unexpected confirmations"))
            {
                _effectResultUnexpected.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Effect results: reorders"))
            {
                _effectResultReorder.Get().Draw(tree);
            }
        }
    }

    private class PerEncounter
    {
        private readonly Lazy<StateTransitionTimings> _transitionTimings;
        private readonly Lazy<ParticipantInfo> _participantInfo;
        private readonly Lazy<AbilityInfo> _abilityInfo;
        private readonly Lazy<StatusInfo> _statusInfo;
        private readonly Lazy<IconInfo> _iconInfo;
        private readonly Lazy<TetherInfo> _tetherInfo;
        private readonly Lazy<MapEffectInfo> _mapEffectInfo;
        private readonly Lazy<DirectorInfo> _directorInfo;
        private readonly Lazy<ArenaBounds> _arenaBounds;
        private readonly Lazy<RolePositions> _rolePositions;
        private readonly Lazy<EncounterDump> _encounterDump;
        private readonly Lazy<TEASpecific>? _teaSpecific;
        private readonly Lazy<TOPSpecific>? _topSpecific;

        public PerEncounter(List<Replay> replays, uint oid)
        {
            _transitionTimings = new(() => new(replays, oid));
            _participantInfo = new(() => new(replays, oid));
            _abilityInfo = new(() => new(replays, oid));
            _statusInfo = new(() => new(replays, oid));
            _iconInfo = new(() => new(replays, oid));
            _tetherInfo = new(() => new(replays, oid));
            _mapEffectInfo = new(() => new(replays, oid));
            _directorInfo = new(() => new(replays, oid));
            _arenaBounds = new(() => new(replays, oid));
            _rolePositions = new(() => new(replays, oid));
            _encounterDump = new(() => new(replays, oid));
            if (oid == (uint)Shadowbringers.Ultimate.TEA.OID.BossP1)
            {
                _teaSpecific = new(() => new(replays, oid));
            }

            if (oid == (uint)Endwalker.Ultimate.TOP.OID.Boss)
            {
                _topSpecific = new(() => new(replays, oid));
            }
        }

        public void Draw(UITree tree)
        {
            foreach (var n in tree.Node("Export everything", false, Colors.TextColor1, () => _encounterDump.Get().DrawContextMenu()))
            {
                _encounterDump.Get().Draw(tree);
            }

            foreach (var n in tree.Node("State transition timings"))
            {
                _transitionTimings.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Participant info", false, Colors.TextColor1, () => _participantInfo.Get().DrawContextMenu()))
            {
                _participantInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Ability info", false, Colors.TextColor1, () => _abilityInfo.Get().DrawContextMenu()))
            {
                _abilityInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Status info", false, Colors.TextColor1, () => _statusInfo.Get().DrawContextMenu()))
            {
                _statusInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Icon info", false, Colors.TextColor1, () => _iconInfo.Get().DrawContextMenu()))
            {
                _iconInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Tether info", false, Colors.TextColor1, () => _tetherInfo.Get().DrawContextMenu()))
            {
                _tetherInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Map effect info", false, Colors.TextColor1))
            {
                _mapEffectInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Director update info", false, Colors.TextColor1))
            {
                _directorInfo.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Arena bounds", false, Colors.TextColor1, () => _arenaBounds.Get().DrawContextMenu()))
            {
                _arenaBounds.Get().Draw(tree);
            }

            foreach (var n in tree.Node("Role positions", false, Colors.TextColor1, () => _rolePositions.Get().DrawContextMenu()))
            {
                _rolePositions.Get().Draw(tree);
            }

            if (_teaSpecific != null)
            {
                foreach (var n in tree.Node("TEA-specific analysis"))
                {
                    _teaSpecific.Get().Draw(tree);
                }
            }

            if (_topSpecific != null)
            {
                foreach (var n in tree.Node("TOP-specific analysis"))
                {
                    _topSpecific.Get().Draw(tree);
                }
            }
        }
    }

    private readonly List<Replay> _replays;
    private string _exportResult = "";
    private readonly Global _global;
    private readonly Dictionary<uint, PerEncounter> _perEncounter = []; // key = encounter OID
    private readonly UITree _tree = new();

    public AnalysisManager(List<Replay> replays)
    {
        _replays = replays;
        _global = new(_replays);
        InitEncounters();
    }

    public void Dispose()
    {
    }

    public void Draw()
    {
        ImGui.TextUnformatted($"{_replays.Count} logs found");

        // Sits above everything else and outside the encounter tree on purpose. The per-encounter export only
        // exists where a module activated, so a recording of content nobody has covered yet had no export
        // button at all, which is precisely the content worth exporting.
        if (ImGui.Button("Export all loaded recordings to text"))
        {
            ExportAll();
        }

        if (_exportResult.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(_exportResult);
        }

        foreach (var n in _tree.Node("Global analysis"))
        {
            _global.Draw(_tree);
        }
        foreach (var n in _tree.Nodes(_perEncounter, kv => new($"Encounter analysis for {kv.Key:X} ({BossModuleRegistry.FindByOID(kv.Key)?.ModuleType.Name})")))
        {
            n.Value.Draw(_tree);
        }
    }

    private void ExportAll()
    {
        var written = 0;
        var failed = 0;

        foreach (var replay in _replays)
        {
            try
            {
                Service.ChatGui.Print($"[BMR] {ReplayExport.Write(replay)}");
                ++written;
            }
            catch (Exception e)
            {
                // One unreadable recording should not stop the rest from being written.
                Service.ChatGui.Print($"[BMR] Could not export {replay.Path}: {e.Message}");
                ++failed;
            }
        }

        _exportResult = failed > 0
            ? $"wrote {written}, {failed} failed, see chat"
            : $"wrote {written} to {EncounterDump.TargetDirectory()}";
    }

    private void InitEncounters()
    {
        foreach (var replay in _replays)
        {
            foreach (var e in replay.Encounters)
            {
                if (!_perEncounter.ContainsKey(e.OID))
                {
                    _perEncounter[e.OID] = new(_replays, e.OID);
                }
            }
        }
    }
}
