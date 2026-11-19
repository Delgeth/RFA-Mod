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
    const string TypeTable = "action_node_types.xtbl", PropTable = "ambient_prop_info.xtbl";

    /// <summary>
    /// Guerrilla's ambient props (ambient_prop_info.xtbl, 18 entries, each also an items_3d.xtbl entry of the same name):
    /// the items its people hold at an action node. The weapons part builds their items (GuerrillaWeapons'
    /// AmbientPropItems), the world part their effects (<see cref="PropEffects"/>), and the people part's
    /// action_nodes.txt says which type holds which and when (its "prop" lines, for guerrilla_people's props.cpp).
    /// </summary>
    static IEnumerable<XtblElement> Props(Packfile guerrillaTables) =>
        guerrillaTables.Find(PropTable) is { } entry ? XtblFile.FromBytes(guerrillaTables.Read(entry)).Entries : [];

    /// <summary>The effects Guerrilla's ambient props play (always_on_effect's perm_effect_type, anim_effect_list's anim_effect_type).</summary>
    public static IEnumerable<string> PropEffects(Packfile guerrillaTables)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Props(guerrillaTables))
        {
            if (p.Element("always_on_effect")?.ValueOf("perm_effect_type")?.Trim() is { Length: > 0 } perm) names.Add(perm);
            foreach (var e in p.Element("anim_effect_list")?.Elements ?? [])
                if (e.ValueOf("anim_effect_type")?.Trim() is { Length: > 0 } effect) names.Add(effect);
        }
        return names;
    }
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
            // Guerrilla's get_entry_point_int (RM 0xA8CC70): with node_flags script_point_enable (bit 10) the offset is in
            // the target's model tag "Action_Node" (object::get_tag_world_coords), not its own place: the builder can't
            // read a model's tags, so such a node's line also gives its parent, the offset and the heading (Tagged), and
            // guerrilla_people finds the tag on the placed parent (Samanya's weld spot is off the bench's origin).
            string Tagged(GuerrillaZoneObject node, XtblElement info, XtblElement state, bool toParent)
            {
                bool scripted = (info.Element("node_flags")?.Elements ?? []).Any(f => f.Value.Trim().Equals("script_point_enable", StringComparison.OrdinalIgnoreCase));
                if (!scripted || !toParent || node.Parent == 0xFFFFFFFF) return "";
                var offset = state.Element("object_interaction_info")?.Element("anim_offset");
                return string.Join('\t', "", node.Parent.ToString("X8"), F(Float(offset?.ValueOf("X"))), F(Float(offset?.ValueOf("Y"))), F(Float(offset?.ValueOf("Z"))),
                                   F(Float(state.Element("object_interaction_info")?.ValueOf("anim_heading"))));
            }
            (string Type, Vector3 Spot, Vector3 Facing, XtblElement Info, string Tag)? SpotOf(GuerrillaZoneObject node)
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
                return (type, spot, facing, info, Tagged(node, info, state, toParent));
            }
            foreach (var squad in objects.Values.Where(o => o.ClassHash == squadNode))
            {
                if (!objects.TryGetValue(squad.Parent, out var node) || node.ClassHash != actionNode) continue;
                if (SpotOf(node) is not { } at) { noType++; continue; }
                nodeLines.Add(string.Join('\t', "node", squad.Handle.ToString("X8"), at.Type, F(at.Spot.X), F(at.Spot.Y), F(at.Spot.Z), F(at.Facing.X), F(at.Facing.Z)) + at.Tag);
                used.Add(at.Type);
            }
            // The walkers' nodes (3b): those whose type allows ambient people (node_flags allow_ambient_peds), for
            // Guerrilla's use_action_node_goal (RM 0x67D420: an ambient person takes the nearest within 30 m,
            // find_nearest_action_node RM 0x637D10, above its wandering).
            string zoneName = Path.GetFileNameWithoutExtension(entry.Name);
            // Not a node a squad's person uses: a node holds one person (Guerrilla's reservation, has_node_reserved).
            var withSquad = objects.Values.Where(o => o.ClassHash == squadNode).Select(o => o.Parent).ToHashSet();
            foreach (var node in objects.Values.Where(o => o.ClassHash == actionNode && !withSquad.Contains(o.Handle)))
            {
                if (SpotOf(node) is not { } at) continue;
                if (!(at.Info.Element("node_flags")?.Elements ?? []).Any(f => f.Value.Trim().Equals("allow_ambient_peds", StringComparison.OrdinalIgnoreCase))) continue;
                pedLines.Add(string.Join('\t', "ped", node.Handle.ToString("X8"), at.Type, F(at.Spot.X), F(at.Spot.Y), F(at.Spot.Z), F(at.Facing.X), F(at.Facing.Z)) + at.Tag);
                used.Add(at.Type);
                pedsByZone[zoneName] = pedsByZone.GetValueOrDefault(zoneName) + 1;
            }
        }
        var lines = new List<string>
        {
            "# Guerrilla's action nodes for the people under them, for the guerrilla_people mod's actions.cpp (written by editor\\GuerrillaBuilder.exe with the people)",
            "# type<TAB>name<TAB>rest s<TAB>state<TAB>least s<TAB>most s<TAB>entry clip<TAB>exit clip[<TAB>idle clip<TAB>least s<TAB>most s]...  (- for none)",
            "# node<TAB>squad node handle<TAB>type<TAB>spot x y z<TAB>facing x z[<TAB>parent<TAB>offset x y z<TAB>heading: the spot from the parent's Action_Node tag]",
            "# ped<TAB>action node handle<TAB>type<TAB>spot x y z<TAB>facing x z  (a node the walkers use)",
            "# prop<TAB>type<TAB>its prop (an item)<TAB>the wearer's point<TAB>the item's point<TAB>entry|after<TAB>exit|complete<TAB>always-on effect<TAB>its point[<TAB>action<TAB>effect<TAB>point]...",
        };
        var props = Props(guerrillaTables).GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        int withProp = 0;
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
            // Its people's everyday lines (node_flags allow_ambient_lines: Guerrilla's play_ambient_voice_lines).
            if ((info.Element("node_flags")?.Elements ?? []).Any(f => f.Value.Trim().Equals("allow_ambient_lines", StringComparison.OrdinalIgnoreCase)))
                lines.Add($"lines\t{name}");
            // Its prop (ambinet_prop_info, sic): when it's made and taken, and what ambient_prop_info.xtbl says of it.
            var propInfo = info.Element("ambinet_prop_info");
            if (propInfo?.ValueOf("ambient_prop_type")?.Trim() is { Length: > 0 } propName && props.TryGetValue(propName, out var prop))
            {
                bool after = (propInfo.ValueOf("prop_creation_time")?.Trim() ?? "").StartsWith("After", StringComparison.OrdinalIgnoreCase);
                bool complete = (propInfo.ValueOf("prop_removal_time")?.Trim() ?? "").StartsWith("Node", StringComparison.OrdinalIgnoreCase);
                var always = prop.Element("always_on_effect");
                var propFields = new List<string> { "prop", name, XtblFile.EntryName(prop), Text(prop.ValueOf("human_attach_point")), Text(prop.ValueOf("obj_attach_point")),
                                                    after ? "after" : "entry", complete ? "complete" : "exit",
                                                    Text(always?.ValueOf("perm_effect_type")), Text(always?.ValueOf("perm_effect_attach_name")) };
                foreach (var e in prop.Element("anim_effect_list")?.Elements ?? [])
                    if (e.ValueOf("anim_action_name")?.Trim() is { Length: > 0 } action && e.ValueOf("anim_effect_type")?.Trim() is { Length: > 0 } effect)
                        propFields.AddRange([action, effect, Text(e.ValueOf("anim_effect_attach_name"))]);
                lines.Add(string.Join('\t', propFields));
                withProp++;
            }
        }
        lines.AddRange(nodeLines);
        lines.AddRange(pedLines);
        Directory.CreateDirectory(peopleFolder);
        System.IO.File.WriteAllLines(Path.Combine(peopleFolder, File), lines);
        var busiest = pedsByZone.OrderByDescending(z => z.Value).Take(8).Select(z => $"{z.Key.Replace("terr01_", "")} {z.Value}");
        return $"Action nodes: {nodeLines.Count} squad nodes under Guerrilla's action nodes and {pedLines.Count} nodes the walkers use, {used.Count} of its types " +
               $"({withProp} holding one of Guerrilla's ambient props), " +
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
