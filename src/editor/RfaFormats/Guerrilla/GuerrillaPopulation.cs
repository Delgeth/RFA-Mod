using System.Numerics;
using System.Text;
using RfaFormats.Meshes;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// The objects Red Faction: Guerrilla's zones place people with, as Armageddon's zone objects, for the
/// world builder. Armageddon still has Guerrilla's squad spawn nodes, patrols and navpoints (the same
/// classes, read by Guerrilla's code with fields added): a squad spawn node puts its squad down when its
/// zone streams in (spawn_prob, then respawn), and one hung under a patrol walks it (its orders Patrol:
/// the patrol path goal follows the patrol's navpoints, patrol_start then each one's links, over the
/// navigation mesh). So far: the patrols whose squad is one of Guerrilla's the tables hold
/// (<see cref="GuerrillaSquads"/>), each with its squad spawn node and every navpoint its route reaches
/// through their links, handles kept (Guerrilla's, unless taken) and links and parents moved with them.
/// Also the standing squads (<see cref="StandingSquads"/>) and every zone's pedestrian network (its
/// pedestrian navpoints and their links, <see cref="Pedestrians"/>), for the ambient people to walk.
///   - Navpoint: just_pos, Guerrilla's navpoint_data as it is (version 3, 28 bytes: Armageddon's reader
///     reads versions 1 to 4, the 4th adding only transitions' fields), obj_links (to placed ones).
///   - object_patrol: looping_patrol, combat_ready, patrol_start, OP, no_check_in, override_patrol (its
///     marauder_raid, courier_patrol and ASD_truck_partol aren't read by Armageddon's).
///   - object_squad_spawn_node: squad_def, OP, spawn_prob, no_reassignment, squad_vehicle, Guerrilla's
///     own; its default_orders, Guerrilla's "Default" (orders from what it hangs under), as Armageddon's
///     own patrols' squads say it: Patrol; a squad that rides, Guerrilla's vehicle group (vehicle_type, as
///     the vehicles build names it); the rest (Armageddon's own switches: exos, leap nodes, leashes, the
///     vehicle group of a squad that doesn't ride) the schema's defaults. Guerrilla's night_spawn_prob, respawn
///     speeds and special flags aren't read by Armageddon's: for the guerrilla_people mod later.
/// Armageddon holds 24 patrols at once (its object_patrol pool), Guerrilla 72; the guerrilla_people mod
/// gives the game Guerrilla's 72. The game keeps the zones within one of the player's loaded, so
/// <see cref="Cap"/> keeps every 3 × 3 block of zones within that (Guerrilla's world places at most 29 in one).
/// </summary>
public static class GuerrillaPopulation
{
    /// <summary>How many patrols the game holds at once (its object_patrol pool: Guerrilla's 72, which guerrilla_people gives it; Armageddon's own 24).</summary>
    public const int MostPatrols = 72;

    const uint None = 0xFFFFFFFF;
    static readonly uint Navpoint = NameHashes.ClassHash("Navpoint"), Patrol = NameHashes.ClassHash("object_patrol"),
                         SquadNode = NameHashes.ClassHash("object_squad_spawn_node");

    /// <summary>The order Volition's tools wrote each class's properties in (Armageddon's own zones).</summary>
    static readonly string[] NavpointOrder = ["just_pos", "navpoint_data", "obj_links"];
    static readonly string[] PatrolOrder = ["looping_patrol", "combat_ready", "patrol_start", "OP", "no_check_in", "override_patrol"];
    static readonly string[] SquadNodeOrder =
    [
        "squad_def", "special_anim_state", "prefer_leap_combat", "spawn_on_leap_node", "scripted_weapon_equip", "respawn_speed", "OP", "spawn_prob",
        "no_reassignment", "exo_type", "squad_vehicle", "vehicle_type", "force_player_knowledge", "squad_exo", "ambient_action_type", "default_orders",
        "leash_distance",
    ];

    /// <summary>Guerrilla's squad node properties Armageddon's reads, taken as they are.</summary>
    static readonly HashSet<string> SquadNodeOwn = ["squad_def", "OP", "spawn_prob", "no_reassignment", "squad_vehicle"];

    /// <summary>
    /// Armageddon's own settings whose schema default names one of its tables' entries (none written by
    /// the schema): the value every one of its own squad spawn nodes has (all 1,929; the vehicle group,
    /// unused with no squad vehicle, in 1,902 of them).
    /// </summary>
    static readonly Dictionary<string, string> ArmageddonsOwn = new()
    {
        ["exo_type"] = "rf_exo", ["vehicle_type"] = "Default Guerilla", ["ambient_action_type"] = "civ_stand_greet_b",
    };

    /// <summary>
    /// The patrols (by handle) of a zone whose squad is one of <paramref name="squads"/> (by the name
    /// <paramref name="squadFor"/> gives Guerrilla's: a civilian or guerrilla squad's version for the zone's group).
    /// </summary>
    public static List<uint> Patrols(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlySet<string> squads, Func<string, string> squadFor) =>
        [.. zone.Values.Where(o => o.ClassHash == Patrol && SquadOf(zone, o) is { } squad && squads.Contains(squadFor(squad.Text("squad_def") ?? ""))).Select(o => o.Handle)];

    /// <summary>
    /// How many people the squads of every 3 × 3 block of zones hold at most: Armageddon's people pool holds 100
    /// (its npc pool), some kept for those dropped in, reinforcements and vehicle crews.
    /// </summary>
    public const int MostPeople = 80;

    static readonly uint Building = NameHashes.ClassHash("rfg_mover"), Box = NameHashes.ClassHash("object_bounding_box"),
                         ActionNode = NameHashes.ClassHash("object_action_node"), GuardNode = NameHashes.ClassHash("object_guard_node");

    /// <summary>The bounding box's properties in the order Armageddon's own zones write them (Guerrilla's boxes have the same three).</summary>
    static readonly string[] BoxOrder = ["bounding_box_type", "OP", "bb"];

    /// <summary>
    /// A squad node's guard post: the bounding box it hangs under in Guerrilla when the box also holds guard nodes (the
    /// squad guards that area, standing at the guard nodes: Guerrilla's go_to_guard_post / guard_at_post AI), or null.
    /// Armageddon deleted the guard AI (its object_guard_node is editor-only), so the box is placed with the node under it
    /// and guerrilla_people leashes the squad to it as it spawns (Armageddon's leash to an anchor's bounding box), until
    /// Guerrilla's guard post AI is ported. Without it the squad, under Standard orders, hunts the player down walks the
    /// pathfinder won't take ("Failed pathfind", the user's Round 2: the EDF post over the Dust safehouse).
    /// </summary>
    public static GuerrillaZoneObject? GuardBoxOf(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, GuerrillaZoneObject node)
    {
        if (node.Parent == None || !zone.TryGetValue(node.Parent, out var box) || box.ClassHash != Box) return null;
        for (uint h = box.Child; h != None && zone.TryGetValue(h, out var child); h = child.Sibling)
            if (child.ClassHash == GuardNode) return box;
        return null;
    }

    /// <summary>
    /// A guard post's box with its anchor on one of its guard nodes: Guerrilla's OP and bb with the place moved to the
    /// guard node and the extent shifted by as much in the box's own axes, so the box covers the same space; or null
    /// (no OP or bb, or no guard node). Armageddon's leash walks a member who is outside the box back to the anchor's
    /// own place (leash_action → 0x810930: the anchor object's position), which for Guerrilla's boxes is the middle of
    /// the box's floor: at rfg_04_10's post 0x4A00001C that is beside a guard tower's base, among crates, 6 cm under our
    /// ground, where a guard was seen 1.4 m under the ground (2026-11-11 21:27:26) and another fell out of the world
    /// (21:28:03). Guerrilla's guards go to the guard nodes instead (go_to_guard_post_action), the places they stand
    /// on: the anchor takes the one on the floor (within 1.5 m of it) nearest the box's middle, or the nearest of all.
    /// Still the stand-in until the guard post AI is ported.
    /// </summary>
    static (byte[] Op, byte[] Bb)? GuardAnchor(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, GuerrillaZoneObject box)
    {
        if (box.Value("OP") is not { Length: >= 48 } op || box.Value("bb") is not { Length: >= 24 } bb) return null;
        float F(byte[] b, int i) => BitConverter.ToSingle(b, i * 4);
        var place = new Vector3(F(op, 0), F(op, 1), F(op, 2));
        Vector3[] axes = [new(F(op, 3), F(op, 4), F(op, 5)), new(F(op, 6), F(op, 7), F(op, 8)), new(F(op, 9), F(op, 10), F(op, 11))];
        Vector3? best = null;
        float bestScore = float.MaxValue;
        for (uint h = box.Child; h != None && zone.TryGetValue(h, out var child); h = child.Sibling)
        {
            if (child.ClassHash != GuardNode) continue;
            var at = child.Value("OP") is { Length: >= 12 } nodeOp ? new Vector3(F(nodeOp, 0), F(nodeOp, 1), F(nodeOp, 2)) : (child.BoundsMin + child.BoundsMax) / 2;
            float across = Vector2.Distance(new(at.X, at.Z), new(place.X, place.Z));
            float score = MathF.Abs(at.Y - place.Y) <= 1.5f ? across : 10000 + across;  // the floor's first
            if (score < bestScore) { bestScore = score; best = at; }
        }
        if (best is not { } anchor) return null;
        var shift = anchor - place;
        var local = new Vector3(Vector3.Dot(shift, axes[0]), Vector3.Dot(shift, axes[1]), Vector3.Dot(shift, axes[2]));
        byte[] newOp = (byte[])op.Clone(), newBb = (byte[])bb.Clone();
        for (int i = 0; i < 3; i++) BitConverter.TryWriteBytes(newOp.AsSpan(i * 4), anchor[i]);
        for (int i = 0; i < 6; i++) BitConverter.TryWriteBytes(newBb.AsSpan(i * 4), F(bb, i) - local[i % 3]);
        return (newOp, newBb);
    }

    /// <summary>
    /// A zone's squad spawn nodes that stand on their own in Armageddon (by handle, with the squad they name here,
    /// <paramref name="squadFor"/>'s name for Guerrilla's, one of <paramref name="squads"/>): those under a building or
    /// a bounding box (Guerrilla's squads defending it), an action node (sitting, working, smoking: Armageddon has no
    /// action nodes) or nothing. Armageddon makes orders from a squad node's parent only for patrols and pedestrian
    /// navpoints (0x8007D0), so these stand where Guerrilla puts them, with their own orders (Guerrilla's Default as
    /// Armageddon's Standard). Not these: the patrols' (<see cref="Patrols"/>), the Marauders' ambushes (their
    /// regions, which spawn them as a vehicle drives in, are gone from Armageddon), turrets' and vehicles' crews.
    /// </summary>
    /// <summary>
    /// A zone's Marauder ambush regions (marauder_ambush_region, a class Armageddon dropped; Guerrilla's 61 in 25 zones from
    /// 04_08 to 12_10 hold 136 of its 160 Marauder squad nodes): each region and its squad nodes on foot (squad_vehicle off;
    /// by handle, with the squad <paramref name="squadFor"/> names, one of <paramref name="squads"/>), and how many of its
    /// nodes ride (27 in all, for later). The world build places the nodes on foot with no parent and writes the regions for
    /// guerrilla_people (ambush.cpp), which spawns them as Guerrilla's ambush_region_do_frame does.
    /// </summary>
    public static List<(GuerrillaZoneObject Region, List<(uint Handle, string Squad)> OnFoot, int Riding)> AmbushRegions(
        IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlySet<string> squads, Func<string, string> squadFor) =>
        [.. zone.Values.Where(o => o.ClassHash == AmbushRegion).Select(region =>
        {
            var nodes = zone.Values.Where(o => o.ClassHash == SquadNode && o.Parent == region.Handle).ToList();
            bool Rides(GuerrillaZoneObject n) => n.Value("squad_vehicle")?.Any(b => b != 0) == true;
            List<(uint, string)> onFoot = [.. nodes.Where(n => !Rides(n)).Select(n => (n.Handle, Squad: squadFor(n.Text("squad_def") ?? "")))
                                                   .Where(n => squads.Contains(n.Squad))];
            return (region, onFoot, nodes.Count(Rides));
        })];

    /// <summary>
    /// A region's line in ambush_regions.txt: "region", its handle, its place and axes (op: the place, then the right, up and
    /// forward rows), its box in its own axes (bb: least, most), enabled, the day and night chances (day_trigger_prob,
    /// night_trigger_prob), the least and most squads it spawns (Guerrilla's MAR_PROP_MIN_SPAWNS / MAX_SPAWNS, whose names
    /// aren't known: by their hashes). Null when it lacks its place or box.
    /// </summary>
    public static string? AmbushRegionLine(GuerrillaZoneObject region)
    {
        if (region.Value("op") is not { Length: >= 48 } op || region.Value("bb") is not { Length: >= 24 } bb) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        string F(byte[] v, int i) => BitConverter.ToSingle(v, i * 4).ToString("R", c);
        float Prob(string name) => region.Value(name) is { Length: >= 4 } v ? BitConverter.ToSingle(v) : 0;
        int Count(uint hash, int fallback) => region.Properties.TryGetValue(hash, out var p) && p.Value.Length >= 4 ? BitConverter.ToInt32(p.Value) : fallback;
        bool enabled = region.Value("enabled") is not { Length: >= 1 } e || e[0] != 0;
        return $"region {region.Handle:X8} {string.Join(' ', Enumerable.Range(0, 12).Select(i => F(op, i)))} {string.Join(' ', Enumerable.Range(0, 6).Select(i => F(bb, i)))} " +
               $"{(enabled ? 1 : 0)} {Prob("day_trigger_prob").ToString("R", c)} {Prob("night_trigger_prob").ToString("R", c)} {Count(AmbushMinSpawns, 1)} {Count(AmbushMaxSpawns, 1)}";
    }

    static readonly uint AmbushRegion = NameHashes.ClassHash("marauder_ambush_region");
    const uint AmbushMinSpawns = 0xAD234E6C, AmbushMaxSpawns = 0xB8A89A9E;  // MAR_PROP_MIN_SPAWNS, MAR_PROP_MAX_SPAWNS (RM serialize 0xAA9B40: +0xD8, +0xDC)

    public static List<(uint Handle, string Squad)> StandingSquads(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlySet<string> squads,
                                                                  Func<string, string> squadFor) =>
        [.. zone.Values.Where(o => o.ClassHash == SquadNode && StoryKind(o) == null)
                       .Where(o => o.Parent == None || zone.TryGetValue(o.Parent, out var p) && (p.ClassHash == Building || p.ClassHash == Box || p.ClassHash == ActionNode))
                       .Select(o => (o.Handle, Squad: squadFor(o.Text("squad_def") ?? "")))
                       .Where(s => squads.Contains(s.Squad))];

    /// <summary>
    /// A squad node's kind of story person, as Guerrilla's resource manager streams them: "special" (its special_npc
    /// flag: HUMAN_SPECIAL, the one nearest the player; find_local_special_npc_nodes RM 0xB51C30), "vip" (its
    /// safehouse_vip flag: HUMAN_SAFEHOUSE_VIP, one a safehouse; stream_notify_select_spawn RM 0x790430), or null.
    /// </summary>
    public static string? StoryKind(GuerrillaZoneObject node) =>
        Flag(node, "special_npc") ? "special" : Flag(node, "safehouse_vip") ? "vip" : null;

    static bool Flag(GuerrillaZoneObject o, string name) => o.Value(name) is { Length: > 0 } v && v[0] != 0;

    /// <summary>
    /// A zone's story people's squad nodes (<see cref="StoryKind"/>) whose squad is one of <paramref name="squads"/>:
    /// each with its kind and its group, the top of its parents (Guerrilla's find_top_parent_object: a safehouse's
    /// VIPs share it), placed outside the people's cap (guerrilla_people spawns one special NPC and one VIP at most).
    /// </summary>
    public static List<(uint Handle, string Squad, string Kind, uint Group, Vector3 At)> StoryNodes(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone,
        IReadOnlySet<string> squads, Func<string, string> squadFor)
    {
        var list = new List<(uint, string, string, uint, Vector3)>();
        foreach (var o in zone.Values.Where(o => o.ClassHash == SquadNode))
        {
            if (StoryKind(o) is not { } kind) continue;
            string squad = squadFor(o.Text("squad_def") ?? "");
            if (!squads.Contains(squad)) continue;
            uint top = o.Handle;
            for (var at = o; at.Parent != None && zone.TryGetValue(at.Parent, out var parent); at = parent) top = parent.Handle;
            var at3 = o.Value("OP") is { Length: >= 12 } op
                ? new Vector3(BitConverter.ToSingle(op, 0), BitConverter.ToSingle(op, 4), BitConverter.ToSingle(op, 8))
                : (o.BoundsMin + o.BoundsMax) / 2;
            list.Add((o.Handle, squad, kind, top, at3));
        }
        return list;
    }

    /// <summary>
    /// The standing squads each zone keeps (<see cref="StandingSquads"/>): every 3 × 3 block of zones holds at most
    /// <see cref="MostPeople"/> people with its patrols' (<paramref name="patrolPeople"/>, a zone's), the busiest
    /// zones giving up their last squads first; <paramref name="leftOut"/> counts the squads left out.
    /// </summary>
    public static Dictionary<(int, int), List<(uint Handle, string Squad)>> CapPeople(Dictionary<(int, int), List<(uint Handle, string Squad)>> standing,
        IReadOnlyDictionary<(int, int), int> patrolPeople, Func<string, int> members, out int leftOut)
    {
        var kept = standing.ToDictionary(p => p.Key, p => p.Value.ToList());
        int Here((int, int) z) => (kept.TryGetValue(z, out var l) ? l.Sum(s => members(s.Squad)) : 0) + patrolPeople.GetValueOrDefault(z);
        IEnumerable<(int, int)> Block((int C, int R) at) => Enumerable.Range(-1, 3).SelectMany(dc => Enumerable.Range(-1, 3).Select(dr => (at.C + dc, at.R + dr)));
        leftOut = 0;
        while (true)
        {
            var worst = kept.Keys.Select(z => (Zone: z, Count: Block(z).Sum(Here))).OrderByDescending(x => x.Count).FirstOrDefault();
            if (worst.Count <= MostPeople) break;
            var busiest = Block(worst.Zone).Where(z => kept.TryGetValue(z, out var l) && l.Count > 0).OrderByDescending(Here).FirstOrDefault();
            if (!kept.ContainsKey(busiest) || kept[busiest].Count == 0) break;  // only patrols left there
            kept[busiest].RemoveAt(kept[busiest].Count - 1);
            leftOut++;
        }
        return kept;
    }

    /// <summary>A navpoint's type (navpoint_data +4: 1 patrol, 2 pedestrian, 10 path, 11 transition, 14 stairs, ...), or -1.</summary>
    static int NavpointType(GuerrillaZoneObject navpoint) => navpoint.Value("navpoint_data") is { Length: >= 8 } data ? BitConverter.ToInt32(data, 4) : -1;

    /// <summary>The navpoint type of Guerrilla's pedestrian network.</summary>
    const int PedestrianNavpoint = 2;

    /// <summary>A zone's pedestrian navpoints (type 2): the network its ambient people walk.</summary>
    public static List<GuerrillaZoneObject> Pedestrians(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone) =>
        [.. zone.Values.Where(o => o.ClassHash == Navpoint && NavpointType(o) == PedestrianNavpoint)];

    /// <summary>
    /// The navpoint types of Guerrilla's node network for people (`_navpoint_type`): path 10, transition 11, ladder
    /// transition 12, ladder 13, stairs 14, transition jump 15, transition leap over 16, path door 23, transition door
    /// 24. Not the roads' (5-9, 22), the holes' (17-21, made as things break), ground 0, floating 3, supply drop-off 4.
    /// </summary>
    static readonly HashSet<int> NodalNavpoints = [10, 11, 12, 13, 14, 15, 16, 23, 24];

    /// <summary>
    /// A zone's node network for people: the ways along walls' tops, up stairs and ladders and through doors, that join
    /// the pieces of the nav mesh its switched-off cells cut apart (cells with flag 0x08: the ground under buildings and
    /// walls, 18 % of the map's people's cells, which only one mission ever switches on). Without it a person on one
    /// piece can't reach another ("Failed pathfind, probably bad navmesh in the area": rfg_04_10's patrol 0x4A0700AC
    /// starts on a piece its route can leave only by the wall-top path from transition 0x4A0008FA to 0x4A000916).
    /// Guerrilla's search (rfg_pf_pathfind) walks these between the mesh's pieces, and so does Armageddon's, whose own
    /// campaign has 4,590 such navpoints (path 3,226, transition 561, stairs 642, ladder 32), 3,562 of them a building's
    /// children. Guerrilla's busiest 3 × 3 window of zones holds 2,537 navpoints of every type, under the navpoint pool
    /// of 5,120 (Guerrilla's own is 3,272).
    /// </summary>
    public static List<GuerrillaZoneObject> Nodal(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone) =>
        [.. zone.Values.Where(o => o.ClassHash == Navpoint && NodalNavpoints.Contains(NavpointType(o)))];

    /// <summary>What <see cref="Convert"/> placed of a zone's node network (<see cref="Nodal"/>).</summary>
    public record struct NodalCount(int Placed, int OnMovers, int LeftOut, int GuardAnchors)
    {
        public int OnOwnCell, OnNearestCell, WithoutCell, LadderPairs, LaddersAlone, LadderTopsAsLadderNodes;
        /// <summary>Pedestrian navpoints with no switched-on cell under them given the nearest (version 4), and those with none within 10 m.</summary>
        public int PedestriansOnNearestCell, PedestriansWithoutCell;
        /// <summary>Each object placed, by Guerrilla's handle: the handle it has in the zone (Guerrilla's unless taken).</summary>
        public Dictionary<uint, uint> Handles;
        public float FarthestPedestrianCell;
        /// <summary>Each of these navpoints with its cell found (written as version 4 or not): its handle as placed, the cell's handle.</summary>
        public List<(uint Navpoint, ulong Cell)> Cells;
        public float FarthestCell;
    }

    /// <summary>
    /// The navpoint types that stand on a cell of the people's mesh: transition 11, ladder transition 12, transition jump
    /// 15, leap over 16, hole transition 21, transition door 24. Armageddon's own (navpoint_data version 4, 40 bytes)
    /// carry it: Guerrilla's version 3 (28 bytes) and then a word and the cell's handle, a u64 (the cell's zone
    /// prefix, column × 16 + row, × 2^24; the layer, people 0, × 2^15; the cell's number), which the serializer
    /// (navpoint vtable slot 5, 0x7EFA70) turns into the navpoint's cell (+0x98) and its stream-in (slot 6, 0x7F08A0)
    /// takes in place of a lookup from version 4 on. A version 3 transition gets none in a zone that isn't the start
    /// zone ("Transition navpoint (0x…) at <…> with invalid navcell", 0x5D17D0 at load: every one of rfg_05_04's and
    /// rfg_06_03's in the run of 2026-11-11 22:17), and one on a switched-off cell none at all ("Invalid ladder or
    /// hole transition navpoint handle … can not find nav cell! This will break pathfinding", 0x7F0B30). Guerrilla's
    /// stream-in (navpoint::stream, RM 0xA921C0) finds the switched-on cell under it (bb_pf_get_nav_cell, ENABLED)
    /// and, for a transition with none, the nearest point on the mesh (bb_pf_find_closest_point_on_nav_mesh), where
    /// it makes a second transition linked to it; the builder does that search here. Version 4 (the cell named) for a
    /// plain transition, and for any of these with no switched-on cell under it (the nearest one, within 10 m); the
    /// others stay version 3, whose stream-in still finds the cell under them (the navpoint's +0xD4, the version,
    /// below 4: 0x7F0A2D). A ladder transition with no mesh near, a ladder's top on a wall joined to the paths there
    /// (rfg_07_12's 0x7C000770 and 0x7C0007AE), is written as a ladder node, as Armageddon's ladders end.
    /// </summary>
    static readonly HashSet<int> CellNavpoints = [11, 12, 15, 16, 21, 24];
    const int TransitionNavpoint = 11, LadderTransitionNavpoint = 12, LadderNavpoint = 13, GuerrillaNavpointData = 28;

    /// <summary>Guerrilla's navpoint_data (version 3, 28 bytes) as Armageddon's version 4 naming <paramref name="cell"/>.</summary>
    static byte[] WithCell(byte[] data, ulong cell)
    {
        var v4 = new byte[GuerrillaNavpointData + 12];
        data.CopyTo(v4, 0);
        BitConverter.TryWriteBytes(v4.AsSpan(0), 4);
        BitConverter.TryWriteBytes(v4.AsSpan(GuerrillaNavpointData + 4), cell);
        return v4;
    }

    /// <summary>
    /// Each ladder navpoint (type 13) with the other end of its ladder: the ladder navpoint of the same parent most
    /// nearly above or below it (within 2 m across, at least 1 m apart in height). Armageddon's ladders link their two
    /// ends to each other (its 32 ladder navpoints: each one's links hold the other end, and a path or transition
    /// node), and the game checks it whenever a building's children are made again, as on a Nano Forge repair ("Node
    /// at bottom of ladder should be marked as a ladder node. Top node: 0x…", 0x663EA0: a ladder node with no link to
    /// a ladder or ladder transition node: the navpoint queue 0x662BA0 runs it for each type 13, then joins the pair
    /// with 0x663AD0 and 0x662F30). Most of Guerrilla's are paired by their links; on its guard, cooling and water
    /// towers the two ends link only to the way on at each end (a transition or path below, a path above), about 1 m
    /// apart across and the ladder's height apart, on the building's body beside the ladder object's own mover, so
    /// neither game can join them and nobody climbs those towers by the network. Switching a ladder off finds the
    /// pair through these links in both games (set_enabled(false): the upper made a hole in the floor, type 19, the
    /// lower a drop point, 20), so a pair made here goes the same way.
    /// </summary>
    static Dictionary<uint, uint> LadderPairs(IReadOnlyList<GuerrillaZoneObject> navpoints)
    {
        static Vector3? At(GuerrillaZoneObject n) => n.Value("just_pos") is { Length: >= 12 } p
            ? new Vector3(BitConverter.ToSingle(p, 0), BitConverter.ToSingle(p, 4), BitConverter.ToSingle(p, 8)) : null;
        // Only the ladder nodes Guerrilla links to no other ladder end (a ladder or ladder transition node among the
        // placed): the ladders session's 54 of its 269 (2026-11-11, the shared note ladders.md: every one on the
        // guard towers 0202tower_guard, the cooling towers 0102cooling_tower_a, two of them a building, and the
        // water towers 0101tower_water_a); the others already have their partner, and stacked or side-by-side
        // ladders mustn't be joined across.
        var typeOf = navpoints.ToDictionary(n => n.Handle, NavpointType);
        bool HasEnd(GuerrillaZoneObject n) =>
            Handles(n.Value("obj_links")).Any(h => typeOf.TryGetValue(h, out int t) && (t == LadderNavpoint || t == LadderTransitionNavpoint));
        var ladders = navpoints.Where(n => NavpointType(n) == LadderNavpoint && At(n) != null && !HasEnd(n)).ToList();
        var pairs = new Dictionary<uint, uint>();
        foreach (var a in ladders)
        {
            if (pairs.ContainsKey(a.Handle)) continue;
            var pa = At(a)!.Value;
            var b = ladders.Where(n => n.Handle != a.Handle && n.Parent == a.Parent && !pairs.ContainsKey(n.Handle))
                           .Select(n => (Node: n, At: At(n)!.Value))
                           .Where(n => Vector2.Distance(new(n.At.X, n.At.Z), new(pa.X, pa.Z)) <= 2 && MathF.Abs(n.At.Y - pa.Y) >= 1)
                           .OrderBy(n => Vector2.Distance(new(n.At.X, n.At.Z), new(pa.X, pa.Z))).Select(n => n.Node).FirstOrDefault();
            if (b == null) continue;
            pairs[a.Handle] = b.Handle;
            pairs[b.Handle] = a.Handle;
        }
        return pairs;
    }

    /// <summary>
    /// The people's cell a navpoint stands on, from the zones' people's layers (<paramref name="layers"/>: each with its
    /// zone): the switched-on cell (flags without 0x08 or 0x10) whose triangle holds it across and whose height there
    /// is within 2 m of it (the nearest in height); else, as Guerrilla's transitions do, the switched-on cell nearest to
    /// it (within 10 m), with how far. Its handle as Armageddon's navpoint_data version 4 has it.
    /// </summary>
    /// <paramref name="heightTolerance"/> is how far over or under it a cell holding it across may be (2 m for the node
    /// network; any height for a pedestrian, which the game's lookup finds a cell for wherever a switched-on one holds it).
    public static Func<Vector3, (ulong Handle, float Off)?> CellFinder(IReadOnlyList<((int Col, int Row) Zone, NavigationLayer Layer)> layers,
                                                                     float heightTolerance = 2)
    {
        const float Reach = 10;
        float Tolerance = heightTolerance;
        return at =>
        {
            (ulong Handle, float Off)? inside = null, nearest = null;
            float bestHeight = Tolerance, bestOff = Reach;
            foreach (var ((col, row), layer) in layers)
                for (int i = 0; i < layer.Cells.Length && i < 0x8000; i++)
                {
                    var cell = layer.Cells[i];
                    if ((cell.Flags & 0x18) != 0 || cell.Vertices.Length != 3) continue;
                    Vector3 a = layer.Vertices[cell.Vertices[0]];
                    if (MathF.Abs(a.X - at.X) > 40 || MathF.Abs(a.Z - at.Z) > 40) continue;  // a cell is a few metres across
                    Vector3 b = layer.Vertices[cell.Vertices[1]], c = layer.Vertices[cell.Vertices[2]];
                    ulong handle = (ulong)(uint)((col * 16 + row) << 24) | (uint)i;
                    var (holds, height) = Across(a, b, c, at);
                    if (holds && MathF.Abs(height - at.Y) <= bestHeight)
                    {
                        bestHeight = MathF.Abs(height - at.Y);
                        inside = (handle, 0);
                    }
                    else if (inside == null)
                    {
                        float off = Vector3.Distance(at, ClosestOnTriangle(a, b, c, at));
                        if (off < bestOff) { bestOff = off; nearest = (handle, off); }
                    }
                }
            return inside ?? nearest;
        };

        static (bool Inside, float Height) Across(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
        {
            float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (MathF.Abs(d) < 1e-6f) return (false, 0);
            float u = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Z - c.Z)) / d;
            float v = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Z - c.Z)) / d;
            float w = 1 - u - v;
            return (u >= -1e-4f && v >= -1e-4f && w >= -1e-4f, u * a.Y + v * b.Y + w * c.Y);
        }

        // The point of triangle abc nearest to p (Ericson, Real-Time Collision Detection 5.1.5).
        static Vector3 ClosestOnTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + d1 / (d1 - d3) * ab;
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + d2 / (d2 - d6) * ac;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (d4 - d3) / ((d4 - d3) + (d5 - d6)) * (c - b);
            float denom = 1 / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }

    /// <summary>A patrol's squad spawn node (the one hung under it), or null.</summary>
    static GuerrillaZoneObject? SquadOf(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, GuerrillaZoneObject patrol) =>
        zone.Values.FirstOrDefault(o => o.ClassHash == SquadNode && o.Parent == patrol.Handle);

    /// <summary>
    /// The patrols each zone keeps: every 3 × 3 block of zones holds at most <see cref="MostPatrols"/>,
    /// the busiest zones giving up their last ones first; the patrols left out are counted in <paramref name="leftOut"/>.
    /// </summary>
    public static Dictionary<(int, int), List<uint>> Cap(Dictionary<(int, int), List<uint>> patrols, out int leftOut)
    {
        var kept = patrols.ToDictionary(p => p.Key, p => p.Value.ToList());
        int Block((int C, int R) at) => Enumerable.Range(-1, 3).SelectMany(dc => Enumerable.Range(-1, 3).Select(dr => (at.C + dc, at.R + dr)))
                                                  .Sum(z => kept.TryGetValue(z, out var l) ? l.Count : 0);
        leftOut = 0;
        while (true)
        {
            var worst = kept.Keys.Select(z => (Zone: z, Count: Block(z))).OrderByDescending(x => x.Count).FirstOrDefault();
            if (worst.Count <= MostPatrols) break;
            // The zone in that block with the most patrols gives one up.
            var (c, r) = worst.Zone;
            var busiest = Enumerable.Range(-1, 3).SelectMany(dc => Enumerable.Range(-1, 3).Select(dr => (c + dc, r + dr)))
                                    .Where(kept.ContainsKey).OrderByDescending(z => kept[z].Count).First();
            kept[busiest].RemoveAt(kept[busiest].Count - 1);
            leftOut++;
        }
        return kept;
    }

    /// <summary>
    /// The patrols named (handles in <paramref name="zone"/>), with their squad spawn nodes and route navpoints,
    /// the standing squads' nodes (<see cref="StandingSquads"/>) and the zone's pedestrian network
    /// (<see cref="Pedestrians"/>), as Armageddon's objects (handles not in
    /// <paramref name="taken"/>, which gains theirs), each object after the ones it names. Each squad node names
    /// the squad <paramref name="squadFor"/> gives for Guerrilla's. Also the zone's node network (<see cref="Nodal"/>):
    /// a navpoint on a building or other mover in Guerrilla is placed only where <paramref name="placedMover"/> gives
    /// that mover (the zone file's object made from it), as its child, so it goes with it when it's destroyed (people
    /// would otherwise walk the air where a wall was); the patrols' and pedestrians' navpoints on a placed mover are
    /// attached to it too.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlyList<uint> patrols,
                                           IReadOnlyList<(uint Handle, string Squad)> standing, Func<string, string> squadFor, ZoneSchema schema, ISet<uint> taken,
                                           Func<uint, ZoneObject?> placedMover, Func<Vector3, (ulong Handle, float Off)?> cellAt,
                                           Func<Vector3, (ulong Handle, float Off)?> pedestrianCellAt, out NodalCount nodal)
    {
        nodal = default;
        nodal.Cells = [];
        // The navpoints the routes reach, through their links (within the zone).
        var navpoints = new List<GuerrillaZoneObject>();
        var reached = new HashSet<uint>();
        var squads = new List<GuerrillaZoneObject>();
        var placedPatrols = new List<GuerrillaZoneObject>();
        foreach (uint handle in patrols)
        {
            if (!zone.TryGetValue(handle, out var patrol) || SquadOf(zone, patrol) is not { } squad) continue;
            placedPatrols.Add(patrol);
            squads.Add(squad);
            var queue = new Queue<uint>();
            if (Handle(patrol.Value("patrol_start")) is { } start) queue.Enqueue(start);
            while (queue.Count > 0)
            {
                uint h = queue.Dequeue();
                if (!zone.TryGetValue(h, out var navpoint) || navpoint.ClassHash != Navpoint || !reached.Add(h)) continue;
                navpoints.Add(navpoint);
                foreach (uint next in Handles(navpoint.Value("obj_links"))) queue.Enqueue(next);
            }
        }

        // The pedestrian network: every pedestrian navpoint of the zone (type 2), whose links are all to others of the
        // zone's (Guerrilla's 1,756 in 16 zones, 40 networks), for the ambient people to walk.
        foreach (var navpoint in Pedestrians(zone))
            if (reached.Add(navpoint.Handle)) navpoints.Add(navpoint);

        // The node network: each one on a mover only where that mover is placed (else it would hang in the air).
        foreach (var navpoint in Nodal(zone))
        {
            if (navpoint.Parent != None && placedMover(navpoint.Parent) == null)
            {
                nodal.LeftOut++;
                continue;
            }
            if (!reached.Add(navpoint.Handle)) continue;
            navpoints.Add(navpoint);
            nodal.Placed++;
        }

        var standingNodes = standing.Where(s => zone.ContainsKey(s.Handle)).Select(s => zone[s.Handle]).ToList();
        // The guard posts' boxes (GuardBoxOf), each once, with the standing nodes under it.
        var guardBoxes = standingNodes.Select(n => (Node: n, Box: GuardBoxOf(zone, n))).Where(x => x.Box != null)
                                      .GroupBy(x => x.Box!.Handle).ToDictionary(g => g.First().Box!, g => g.Select(x => x.Node).ToList());

        // Handles: Guerrilla's, unless taken.
        var handleOf = new Dictionary<uint, uint>();
        foreach (var o in navpoints.Concat(placedPatrols).Concat(squads).Concat(guardBoxes.Keys).Concat(standingNodes))
        {
            uint handle = o.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            handleOf[o.Handle] = handle;
        }
        uint Moved(uint h) => handleOf.TryGetValue(h, out uint m) ? m : None;
        nodal.Handles = new Dictionary<uint, uint>(handleOf);

        var objects = new List<ZoneObject>();
        ZoneObject Make(GuerrillaZoneObject o, IEnumerable<NewProperty> properties) =>
            ZoneObject.Create(o.ClassHash, handleOf[o.Handle], o.Number, o.BoundsMin, o.BoundsMax, [.. properties], 0);
        NewProperty Own(GuerrillaZoneObject o, string name, byte[]? value = null)
        {
            var (type, raw) = o.Properties.TryGetValue(NameHashes.PropertyHash(name), out var p) ? p : throw new InvalidDataException($"a {name} is missing");
            return new NewProperty(type, NameHashes.PropertyHash(name), value ?? raw);
        }
        NewProperty Default(string className, string name)
        {
            var property = schema.Class(className)?.Property(name) ?? throw new InvalidDataException($"the schema's {className} has no {name}");
            return new NewProperty(property.ZoneType, NameHashes.PropertyHash(property.ZoneName),
                                   property.DefaultValue() ?? throw new InvalidDataException($"the schema has no default for {className}'s {name}"));
        }

        var ladderPartner = LadderPairs(navpoints);
        nodal.LadderPairs = ladderPartner.Count / 2;
        var placedType = navpoints.ToDictionary(n => n.Handle, NavpointType);
        bool LinksToLadder(GuerrillaZoneObject n) =>
            Handles(n.Value("obj_links")).Any(h => placedType.TryGetValue(h, out int t) && (t == LadderNavpoint || t == LadderTransitionNavpoint));
        foreach (var navpoint in navpoints)
        {
            var links = Handles(navpoint.Value("obj_links")).Where(handleOf.ContainsKey).Select(Moved).ToList();
            if (ladderPartner.TryGetValue(navpoint.Handle, out uint partner) && !links.Contains(Moved(partner))) links.Add(Moved(partner));
            int type = NavpointType(navpoint);
            if (type == LadderNavpoint && !ladderPartner.ContainsKey(navpoint.Handle) && !LinksToLadder(navpoint)) nodal.LaddersAlone++;
            var data = navpoint.Value("navpoint_data");
            if (CellNavpoints.Contains(type) && data is { Length: GuerrillaNavpointData } && navpoint.Value("just_pos") is { Length: >= 12 } pos)
            {
                var at = new Vector3(BitConverter.ToSingle(pos, 0), BitConverter.ToSingle(pos, 4), BitConverter.ToSingle(pos, 8));
                var cell = cellAt(at);
                if (cell == null && type == LadderTransitionNavpoint && LinksToLadder(navpoint))
                {
                    // A ladder's top on a wall or roof, joined to the paths there, with no mesh near: Armageddon ends its
                    // ladders there with a ladder node (both its ends are type 13), which needs no cell.
                    data = (byte[])data.Clone();
                    BitConverter.TryWriteBytes(data.AsSpan(4), LadderNavpoint);
                    nodal.LadderTopsAsLadderNodes++;
                }
                else if (cell == null) nodal.WithoutCell++;  // left as Guerrilla's: the game looks for one itself
                else if (cell.Value.Off > 0)
                {
                    // None under it: the nearest, as Guerrilla's stream-in finds for a transition (version 4 names it).
                    data = WithCell(data, cell.Value.Handle);
                    nodal.Cells.Add((Moved(navpoint.Handle), cell.Value.Handle));
                    nodal.OnNearestCell++;
                    nodal.FarthestCell = MathF.Max(nodal.FarthestCell, cell.Value.Off);
                }
                else
                {
                    // On its own cell: a plain transition names it (Armageddon's stream-in looks only the others up by
                    // their place, which version 3 keeps for them).
                    if (type == TransitionNavpoint) data = WithCell(data, cell.Value.Handle);
                    nodal.Cells.Add((Moved(navpoint.Handle), cell.Value.Handle));
                    nodal.OnOwnCell++;
                }
            }
            else if (type == PedestrianNavpoint && data is { Length: GuerrillaNavpointData } && navpoint.Value("just_pos") is { Length: >= 12 } pedPos)
            {
                // A pedestrian's cell (the run of 2026-11-12 15:24, Eos: 29 of Guerrilla's pedestrians reported "Invalid ladder or
                // hole transition navpoint handle (0x…) … can not find nav cell", the stream-in's report for types 2, 12, 15, 16
                // and 21, 0x7F0B30). Armageddon's stream-in looks a pedestrian's cell up by place (version 3) and reports one with
                // none; Guerrilla's (navpoint::stream, RM 0xA921C0) does the same lookup and keeps quiet. Those 29 stand on
                // buildings' floors and walkways (28 hang on their rfg_mover), 1-3.7 m over the ground's cells Guerrilla switches
                // off under its buildings (0x08), linked only to other pedestrians. Such a one is given the switched-on cell
                // nearest to it, as the transitions are (0.8-4.4 m across for those 29). Only one with no switched-on cell over
                // or under it at all: the game's lookup finds the others a cell (the 213 more of those zones the first build
                // gave the nearest with the node network's 2 m test, with none reported), and they stay version 3.
                var at = new Vector3(BitConverter.ToSingle(pedPos, 0), BitConverter.ToSingle(pedPos, 4), BitConverter.ToSingle(pedPos, 8));
                var cell = pedestrianCellAt(at);
                if (cell == null) nodal.PedestriansWithoutCell++;
                else if (cell.Value.Off > 0)
                {
                    data = WithCell(data, cell.Value.Handle);
                    nodal.Cells.Add((Moved(navpoint.Handle), cell.Value.Handle));
                    nodal.PedestriansOnNearestCell++;
                    nodal.FarthestPedestrianCell = MathF.Max(nodal.FarthestPedestrianCell, cell.Value.Off);
                }
            }
            var properties = new List<NewProperty> { Own(navpoint, "just_pos"), Own(navpoint, "navpoint_data", data) };
            if (links.Count > 0) properties.Add(Own(navpoint, "obj_links", [.. links.SelectMany(BitConverter.GetBytes)]));
            var obj = Make(navpoint, properties);
            obj.Parent = obj.Sibling = obj.Child = None;
            if (navpoint.Parent != None && placedMover(navpoint.Parent) is { } mover)
            {
                // The mover's first child now, its children before it following as siblings (placed before this navpoint).
                obj.Parent = mover.Handle;
                obj.Sibling = mover.Child;
                mover.Child = obj.Handle;
                if (NodalNavpoints.Contains(NavpointType(navpoint))) nodal.OnMovers++;
            }
            objects.Add(obj);
        }
        for (int i = 0; i < placedPatrols.Count; i++)
        {
            var patrol = placedPatrols[i];
            var squad = squads[i];
            var patrolProperties = PatrolOrder.Select(name => name switch
            {
                "patrol_start" => Own(patrol, name, BitConverter.GetBytes(Handle(patrol.Value(name)) is { } s ? Moved(s) : None)),
                _ when patrol.Properties.ContainsKey(NameHashes.PropertyHash(name)) => Own(patrol, name),
                _ => Default("object_patrol", name),
            });
            var patrolObject = Make(patrol, patrolProperties);
            patrolObject.Parent = patrolObject.Sibling = None;
            patrolObject.Child = handleOf[squad.Handle];
            objects.Add(patrolObject);

            var squadObject = Make(squad, SquadNodeProperties(squad, "Patrol"));
            squadObject.Parent = handleOf[patrol.Handle];
            squadObject.Sibling = squadObject.Child = None;
            objects.Add(squadObject);
        }
        // Each guard post's box as Guerrilla's (its place, orientation and extent), before the nodes under it.
        var boxOfNode = new Dictionary<uint, uint>();
        foreach (var (box, nodes) in guardBoxes)
        {
            var anchor = GuardAnchor(zone, box);
            if (anchor != null) nodal.GuardAnchors++;
            var boxObject = Make(box, BoxOrder.Select(name => name switch
            {
                "OP" when anchor != null => Own(box, name, anchor.Value.Op),
                "bb" when anchor != null => Own(box, name, anchor.Value.Bb),
                _ => Own(box, name),
            }));
            boxObject.Parent = boxObject.Sibling = None;
            boxObject.Child = handleOf[nodes[0].Handle];
            objects.Add(boxObject);
            foreach (var node in nodes) boxOfNode[node.Handle] = handleOf[box.Handle];
        }
        foreach (var node in standingNodes)
        {
            var squadObject = Make(node, SquadNodeProperties(node, Orders(node.Text("default_orders"))));
            squadObject.Parent = squadObject.Sibling = squadObject.Child = None;
            if (boxOfNode.TryGetValue(node.Handle, out uint boxHandle))
            {
                // Under its guard post's box; the box's other nodes follow it as its siblings.
                squadObject.Parent = boxHandle;
                var siblings = guardBoxes.First(g => handleOf[g.Key.Handle] == boxHandle).Value;
                int at = siblings.IndexOf(node);
                if (at + 1 < siblings.Count) squadObject.Sibling = handleOf[siblings[at + 1].Handle];
            }
            objects.Add(squadObject);
        }
        return objects;

        // A squad node's properties in Armageddon's order: its squad (by squadFor's name), Guerrilla's own it reads,
        // the orders given, the vehicle group a squad that rides names (Guerrilla's, as the vehicles build writes it:
        // EDF_scout_Gauss → rfg_EDF_scout_Gauss), Armageddon's own choices, the schema's defaults.
        IEnumerable<NewProperty> SquadNodeProperties(GuerrillaZoneObject node, string orders) => SquadNodeOrder.Select(name => name switch
        {
            "default_orders" => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(orders)),
            "squad_def" => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(squadFor(node.Text("squad_def") ?? ""))),
            "vehicle_type" when node.Value("squad_vehicle")?.Any(b => b != 0) == true && node.Text(name)?.Trim() is { Length: > 0 } group =>
                new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(GuerrillaVehiclePlaces.GroupPrefix + group)),
            _ when ArmageddonsOwn.TryGetValue(name, out var own) => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(own)),
            _ when SquadNodeOwn.Contains(name) && node.Properties.ContainsKey(NameHashes.PropertyHash(name)) => Own(node, name),
            _ => Default("object_squad_spawn_node", name),
        });
    }

    /// <summary>
    /// A standing squad's orders by Armageddon's names: Guerrilla's zones use Default (Armageddon's Standard: a squad
    /// not allied to the player waits where it stands, 0x8007D0) and Wait; the names are numbered differently in the
    /// two games, so by name.
    /// </summary>
    static string Orders(string? guerrilla) => guerrilla?.Trim().Equals("Wait", StringComparison.OrdinalIgnoreCase) == true ? "Wait" : "Standard";

    static uint? Handle(byte[]? value) => value is { Length: >= 4 } ? BitConverter.ToUInt32(value) : null;

    static IEnumerable<uint> Handles(byte[]? value)
    {
        if (value == null) yield break;
        for (int at = 0; at + 4 <= value.Length; at += 4) yield return BitConverter.ToUInt32(value, at);
    }

    static byte[] Text(string text) => [.. Encoding.Latin1.GetBytes(text), 0];
}
