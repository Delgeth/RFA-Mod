using System.Globalization;
using System.Numerics;
using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's action nodes for the people who stand under them (piece 3a): its zones' object_action_node, each an
/// animation_type of action_node_types.xtbl (its Ambient_Behaviors: a looping state for min..max s, an entry and an
/// exit clip, idle clips now and then, an offset and heading from the node or its parent, a rest after), and the squad
/// spawn nodes under each. Armageddon has no action nodes, so guerrilla_people takes a person spawned from such a squad
/// node to its node's spot and plays the type there (actions.cpp). Written with the people
/// (mods\guerrilla_people\action_nodes.txt), from Guerrilla's own zones: a squad node keeps Guerrilla's handle where the
/// world build places it, so no world build is needed.
/// </summary>
public static class GuerrillaActions
{
    public const string File = "action_nodes.txt";
    const string TypeTable = "action_node_types.xtbl";
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string Write(string guerrillaCache, Packfile guerrillaTables, string peopleFolder)
    {
        var types = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find(TypeTable) ?? throw new InvalidDataException($"no {TypeTable} in Guerrilla's misc.vpp_pc"))).Entries
            .GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint actionNode = NameHashes.ClassHash("object_action_node"), squadNode = NameHashes.ClassHash("object_squad_spawn_node");
        var nodeLines = new List<string>();
        var pedLines = new List<string>();
        var pedsByZone = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var used = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int noType = 0;
        foreach (var entry in zones.Entries.Where(e => e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase)))
        {
            var objects = GuerrillaZones.Objects(zones.Read(entry)).GroupBy(o => o.Handle).ToDictionary(g => g.Key, g => g.First());
            // A node's spot: the node's (or, snapped to its parent, the parent's) place and axes, the state's offset in
            // them, its heading turned from their forward; null when its type has no state or it has no place.
            (string Type, Vector3 Spot, Vector3 Facing, XtblElement Info)? SpotOf(GuerrillaZoneObject node)
            {
                string type = node.Text("animation_type")?.Trim() ?? "";
                if (!types.TryGetValue(type, out var info) || First(info) is not { } state) return null;
                bool toParent = (info.ValueOf("snap_to_target")?.Trim() ?? "").Equals("Parent", StringComparison.OrdinalIgnoreCase);
                var reference = toParent && objects.TryGetValue(node.Parent, out var parent) ? parent : node;
                if (Frame(reference) is not { } frame) return null;
                var offset = state.Element("object_interaction_info")?.Element("anim_offset");
                var local = new Vector3(Float(offset?.ValueOf("X")), Float(offset?.ValueOf("Y")), Float(offset?.ValueOf("Z")));
                var spot = frame.At + frame.Right * local.X + frame.Up * local.Y + frame.Forward * local.Z;
                float heading = Float(state.Element("object_interaction_info")?.ValueOf("anim_heading")) * MathF.PI / 180;
                var facing = Vector3.Normalize(new Vector3(frame.Forward.X, 0, frame.Forward.Z) * MathF.Cos(heading) + new Vector3(frame.Right.X, 0, frame.Right.Z) * MathF.Sin(heading));
                if (float.IsNaN(facing.X)) facing = new Vector3(0, 0, 1);
                return (type, spot, facing, info);
            }
            foreach (var squad in objects.Values.Where(o => o.ClassHash == squadNode))
            {
                if (!objects.TryGetValue(squad.Parent, out var node) || node.ClassHash != actionNode) continue;
                if (SpotOf(node) is not { } at) { noType++; continue; }
                nodeLines.Add(string.Join('\t', "node", squad.Handle.ToString("X8"), at.Type, F(at.Spot.X), F(at.Spot.Y), F(at.Spot.Z), F(at.Facing.X), F(at.Facing.Z)));
                used.Add(at.Type);
            }
            // The walkers' nodes (3b): those whose type allows ambient people (node_flags allow_ambient_peds), for
            // Guerrilla's use_action_node_goal (RM 0x67D420: an ambient person takes the nearest within 30 m,
            // find_nearest_action_node RM 0x637D10, above its wandering).
            string zoneName = Path.GetFileNameWithoutExtension(entry.Name);
            foreach (var node in objects.Values.Where(o => o.ClassHash == actionNode))
            {
                if (SpotOf(node) is not { } at) continue;
                if (!(at.Info.Element("node_flags")?.Elements ?? []).Any(f => f.Value.Trim().Equals("allow_ambient_peds", StringComparison.OrdinalIgnoreCase))) continue;
                pedLines.Add(string.Join('\t', "ped", node.Handle.ToString("X8"), at.Type, F(at.Spot.X), F(at.Spot.Y), F(at.Spot.Z), F(at.Facing.X), F(at.Facing.Z)));
                used.Add(at.Type);
                pedsByZone[zoneName] = pedsByZone.GetValueOrDefault(zoneName) + 1;
            }
        }
        var lines = new List<string>
        {
            "# Guerrilla's action nodes for the people under them, for the guerrilla_people mod's actions.cpp (written by editor\\GuerrillaBuilder.exe with the people)",
            "# type<TAB>name<TAB>rest s<TAB>state<TAB>least s<TAB>most s<TAB>entry clip<TAB>exit clip[<TAB>idle clip<TAB>least s<TAB>most s]...  (- for none)",
            "# node<TAB>squad node handle<TAB>type<TAB>spot x y z<TAB>facing x z",
            "# ped<TAB>action node handle<TAB>type<TAB>spot x y z<TAB>facing x z  (a node the walkers use)",
        };
        foreach (string name in used)
        {
            var info = types[name];
            var state = First(info)!;
            var fields = new List<string> { "type", name, F(Float(info.ValueOf("cooldown_period"))), Text(state.ValueOf("anim_state")), F(Float(state.ValueOf("min_time"))),
                                            F(Float(state.ValueOf("max_time"))), Text(state.ValueOf("entry_anim")), Text(state.ValueOf("exit_anim")) };
            foreach (var idle in state.Element("state_action_grid")?.Elements ?? [])
                if (idle.ValueOf("state_action")?.Trim() is { Length: > 0 } action)
                    fields.AddRange([action, F(Float(idle.ValueOf("action_cooldown_min"))), F(Float(idle.ValueOf("action_cooldown_max")))]);
            lines.Add(string.Join('\t', fields));
        }
        lines.AddRange(nodeLines);
        lines.AddRange(pedLines);
        Directory.CreateDirectory(peopleFolder);
        System.IO.File.WriteAllLines(Path.Combine(peopleFolder, File), lines);
        var busiest = pedsByZone.OrderByDescending(z => z.Value).Take(8).Select(z => $"{z.Key.Replace("terr01_", "")} {z.Value}");
        return $"Action nodes: {nodeLines.Count} squad nodes under Guerrilla's action nodes and {pedLines.Count} nodes the walkers use, {used.Count} of its types, " +
               $"written to {File} (guerrilla_people takes their people to them){(noType > 0 ? $"; {noType} under a node whose type has no state" : "")}; " +
               $"the walkers' nodes by zone, the most: {string.Join(", ", busiest)}";
    }

    /// <summary>A type's first anim_state_entry with a state (Guerrilla's nodes use one).</summary>
    static XtblElement? First(XtblElement type) =>
        type.Element("anim_state_list")?.Elements.FirstOrDefault(e => e.ValueOf("anim_state")?.Trim() is { Length: > 0 });

    /// <summary>An object's place and axes from its OP (the place, then the right, up and forward rows).</summary>
    static (Vector3 At, Vector3 Right, Vector3 Up, Vector3 Forward)? Frame(GuerrillaZoneObject o)
    {
        if (o.Value("OP") is not { Length: >= 48 } op) return null;
        Vector3 V(int at) => new(BitConverter.ToSingle(op, at), BitConverter.ToSingle(op, at + 4), BitConverter.ToSingle(op, at + 8));
        return (V(0), V(12), V(24), V(36));
    }

    static float Float(string? text) => float.TryParse(text?.Trim(), NumberStyles.Float, C, out float f) ? f : 0;
    static string F(float f) => f.ToString("0.###", C);
    static string Text(string? text) => text?.Trim() is { Length: > 0 } t ? t : "-";
}
