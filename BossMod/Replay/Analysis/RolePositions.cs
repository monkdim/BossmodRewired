using Dalamud.Bindings.ImGui;

namespace BossMod.ReplayAnalysis;

// For every ability seen in an encounter, records where each party role slot was standing at the moment it resolved.
//
// This is the raw material for positional module hints: exact drop spots, tether placement, per-role safe spots.
// The other analysis passes answer "what happened"; this one answers "where was everybody when it did", which is
// the part a module author would otherwise have to reconstruct from memory.
//
// Role assignment is resolved through PartyRolesConfig, which is keyed on content ID. Replays preserve content IDs,
// so assignments made at any time resolve correctly against recordings made earlier. Anonymized replays scramble
// content IDs by design, so roles in those will read as Unassigned.
sealed class RolePositions : CommonEnumInfo
{
    // Cap on instances rendered per ability. Auto-attacks alone can produce hundreds per pull, and the tree becomes
    // unusable long before that is informative. The count is always shown so a truncated list never reads as complete.
    private const int MaxInstancesShown = 50;

    private static readonly string[] Octants = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    private sealed record class Snapshot(PartyRolesConfig.Assignment Role, string Name, Class Class, WPos Position, bool Dead, bool Hit);

    private sealed record class Instance(DateTime Timestamp, float EncounterTime, WPos Origin, WPos CasterPos, List<Snapshot> Snapshots);

    private sealed class AbilityData
    {
        public readonly List<Instance> Instances = [];
    }

    private readonly Type? _aidType;
    private readonly Dictionary<ActionID, AbilityData> _data = [];
    private int _encountersScanned;
    private int _encountersWithRoles;

    public RolePositions(List<Replay> replays, uint oid)
    {
        var moduleInfo = BossModuleRegistry.FindByOID(oid);
        _oidType = moduleInfo?.ObjectIDType;
        _aidType = moduleInfo?.ActionIDType;

        var roles = Service.Config.Get<PartyRolesConfig>();

        foreach (var replay in replays)
        {
            foreach (var enc in replay.Encounters)
            {
                if (enc.OID != oid)
                {
                    continue;
                }

                ++_encountersScanned;

                // Roles are per-person, but the origin has to be per-encounter: the same fight can be run in arenas
                // at different world coordinates, so absolute positions are not comparable across pulls.
                var origin = DeriveArenaCenter(enc);
                var anyRole = false;

                foreach (var action in replay.EncounterActions(enc))
                {
                    // Player GCDs are a rotation question, not a positional one, and including them buries the boss
                    // abilities under thousands of entries. Everything hostile or environmental is kept.
                    if (action.Source.Type == ActorType.Player)
                    {
                        continue;
                    }

                    var t = action.Timestamp;
                    var snapshots = new List<Snapshot>(enc.PartyMembers.Count);

                    foreach (var (p, cls, _) in enc.PartyMembers)
                    {
                        var role = roles[p.ContentID];
                        anyRole |= role != PartyRolesConfig.Assignment.Unassigned;

                        var hit = false;
                        foreach (var target in action.Targets)
                        {
                            if (target.Target == p)
                            {
                                hit = true;
                                break;
                            }
                        }

                        var posRot = p.PosRotAt(t);
                        snapshots.Add(new(role, p.NameAt(t).name ?? "<unknown>", cls, new(posRot.X, posRot.Z), p.DeadAt(t), hit));
                    }

                    if (snapshots.Count == 0)
                    {
                        continue;
                    }

                    // Sort by role so every instance reads in the same order, with unassigned players last.
                    snapshots.Sort((a, b) => a.Role != b.Role ? a.Role.CompareTo(b.Role) : string.CompareOrdinal(a.Name, b.Name));

                    var casterPosRot = action.Source.PosRotAt(t);
                    _data.GetOrAdd(action.ID).Instances.Add(new(t, (float)(t - enc.Time.Start).TotalSeconds, origin, new(casterPosRot.X, casterPosRot.Z), snapshots));
                }

                if (anyRole)
                {
                    ++_encountersWithRoles;
                }
            }
        }
    }

    public void Draw(UITree tree)
    {
        if (_encountersScanned > 0 && _encountersWithRoles == 0)
        {
            tree.LeafNode("No role assignments found for anyone in these pulls. Assign roles under Party roles in the config, or check whether these replays were anonymized.", Colors.TextColor2);
        }

        UITree.NodeProperties map(KeyValuePair<ActionID, AbilityData> kv)
        {
            var name = kv.Key.Type == ActionType.Spell ? _aidType?.GetEnumName(kv.Key.ID) : null;
            return new($"{kv.Key} ({name}) - {kv.Value.Instances.Count} casts", false, name == null ? Colors.TextColor2 : Colors.TextColor1);
        }

        foreach (var (aid, data) in tree.Nodes(_data, map, kv => DrawAbilityContextMenu(kv.Key, kv.Value)))
        {
            foreach (var n in tree.Node("Consensus position per role"))
            {
                DrawConsensus(tree, data);
            }

            if (data.Instances.Count > MaxInstancesShown)
            {
                tree.LeafNode($"Showing the first {MaxInstancesShown} of {data.Instances.Count} casts.", Colors.TextColor2);
            }

            foreach (var inst in tree.Nodes(data.Instances.Take(MaxInstancesShown), i => new($"T+{i.EncounterTime:f1}s ({i.Timestamp:HH:mm:ss})")))
            {
                tree.LeafNode($"caster at {Describe(inst.CasterPos, inst.Origin)}");
                tree.LeafNodes(inst.Snapshots, s => DescribeSnapshot(s, inst.Origin));
            }
        }
    }

    public void DrawContextMenu()
    {
        if (ImGui.MenuItem("Copy all abilities and role positions"))
        {
            var sb = new StringBuilder();
            foreach (var (aid, data) in _data)
            {
                AppendAbility(sb, aid, data);
            }

            ImGui.SetClipboardText(sb.ToString());
        }
    }

    private void DrawAbilityContextMenu(ActionID aid, AbilityData data)
    {
        if (ImGui.MenuItem("Copy role positions for this ability"))
        {
            var sb = new StringBuilder();
            AppendAbility(sb, aid, data);
            ImGui.SetClipboardText(sb.ToString());
        }
    }

    private void DrawConsensus(UITree tree, AbilityData data)
    {
        foreach (var (role, mean, spread, samples) in Consensus(data))
        {
            // Spread is what tells you whether a position is prescribed by the mechanic or just where somebody
            // happened to be standing. A tight spread across pulls is a real spot worth drawing in a module.
            var confidence = spread switch
            {
                < 1f => "fixed spot",
                < 3f => "roughly consistent",
                _ => "varies, probably not a fixed spot"
            };
            tree.LeafNode($"{role,-10} mean {mean.X:f2}, {mean.Z:f2}  spread {spread:f2}y over {samples} casts - {confidence}");
        }
    }

    // Mean position and mean distance from that mean, per role, across every cast of one ability.
    private static List<(PartyRolesConfig.Assignment Role, WPos Mean, float Spread, int Samples)> Consensus(AbilityData data)
    {
        var byRole = new Dictionary<PartyRolesConfig.Assignment, List<WPos>>();
        foreach (var inst in data.Instances)
        {
            foreach (var s in inst.Snapshots)
            {
                if (s.Dead)
                {
                    continue; // a corpse is not standing anywhere meaningful
                }

                // Store arena-relative so pulls in differently placed arena instances can be averaged together.
                byRole.GetOrAdd(s.Role).Add(new(s.Position.X - inst.Origin.X, s.Position.Z - inst.Origin.Z));
            }
        }

        var res = new List<(PartyRolesConfig.Assignment, WPos, float, int)>(byRole.Count);
        foreach (var (role, positions) in byRole)
        {
            var sumX = 0f;
            var sumZ = 0f;
            for (var i = 0; i < positions.Count; ++i)
            {
                sumX += positions[i].X;
                sumZ += positions[i].Z;
            }

            var mean = new WPos(sumX / positions.Count, sumZ / positions.Count);

            var spread = 0f;
            for (var i = 0; i < positions.Count; ++i)
            {
                spread += (positions[i] - mean).Length();
            }

            res.Add((role, mean, spread / positions.Count, positions.Count));
        }

        res.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return res;
    }

    private void AppendAbility(StringBuilder sb, ActionID aid, AbilityData data)
    {
        var name = aid.Type == ActionType.Spell ? _aidType?.GetEnumName(aid.ID) : null;
        sb.Append("// ").Append(aid).Append(' ').Append(name ?? "unnamed").Append(" - ").Append(data.Instances.Count).AppendLine(" casts");
        sb.AppendLine("// consensus position per role, arena-relative:");
        foreach (var (role, mean, spread, samples) in Consensus(data))
        {
            sb.Append("//   ").Append(role.ToString().PadRight(10))
              .Append("new WDir(").Append(mean.X.ToString("f2")).Append("f, ").Append(mean.Z.ToString("f2")).Append("f)")
              .Append("  spread ").Append(spread.ToString("f2")).Append("y over ").Append(samples).AppendLine(" casts");
        }

        sb.AppendLine();
    }

    private static string DescribeSnapshot(Snapshot s, WPos origin)
    {
        var state = s.Dead ? " DEAD" : s.Hit ? " HIT" : "";
        return $"{s.Role,-10} {s.Class,-12} {Describe(s.Position, origin)}{state}  ({s.Name})";
    }

    private static string Describe(WPos pos, WPos origin)
    {
        var offset = pos - origin;
        return $"({pos.X,7:f2}, {pos.Z,7:f2})  r={offset.Length(),6:f2}  {Octant(offset)}";
    }

    // FFXIV world axes put north at -Z and east at +X, so a compass bearing is 180 degrees off WDir.ToAngle.
    private static string Octant(WDir offset)
    {
        if (offset.LengthSq() < 0.01f)
        {
            return "center";
        }

        var bearing = (180f - offset.ToAngle().Deg + 360f) % 360f;
        return Octants[(int)MathF.Round(bearing / 45f) % 8];
    }

    // Midpoint of the bounding box of every position any party member occupied during the pull. Arenas are laid out
    // around their centre and players cover them fairly evenly over a full fight, so this lands close enough to make
    // relative positions comparable. It is only ever used as an origin, never reported as the true arena centre.
    private static WPos DeriveArenaCenter(Replay.Encounter enc)
    {
        var minX = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxZ = float.MinValue;

        foreach (var (p, _, _) in enc.PartyMembers)
        {
            foreach (var posRot in p.PosRotHistory.Values)
            {
                minX = Math.Min(minX, posRot.X);
                minZ = Math.Min(minZ, posRot.Z);
                maxX = Math.Max(maxX, posRot.X);
                maxZ = Math.Max(maxZ, posRot.Z);
            }
        }

        return minX > maxX ? default : new((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
    }
}
