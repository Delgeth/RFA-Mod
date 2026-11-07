using System.Text;
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
                         ActionNode = NameHashes.ClassHash("object_action_node");

    /// <summary>
    /// A zone's squad spawn nodes that stand on their own in Armageddon (by handle, with the squad they name here,
    /// <paramref name="squadFor"/>'s name for Guerrilla's, one of <paramref name="squads"/>): those under a building or
    /// a bounding box (Guerrilla's squads defending it), an action node (sitting, working, smoking: Armageddon has no
    /// action nodes) or nothing. Armageddon makes orders from a squad node's parent only for patrols and pedestrian
    /// navpoints (0x8007D0), so these stand where Guerrilla puts them, with their own orders (Guerrilla's Default as
    /// Armageddon's Standard). Not these: the patrols' (<see cref="Patrols"/>), the Marauders' ambushes (their
    /// regions, which spawn them as a vehicle drives in, are gone from Armageddon), turrets' and vehicles' crews.
    /// </summary>
    public static List<(uint Handle, string Squad)> StandingSquads(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlySet<string> squads,
                                                                  Func<string, string> squadFor) =>
        [.. zone.Values.Where(o => o.ClassHash == SquadNode)
                       .Where(o => o.Parent == None || zone.TryGetValue(o.Parent, out var p) && (p.ClassHash == Building || p.ClassHash == Box || p.ClassHash == ActionNode))
                       .Select(o => (o.Handle, Squad: squadFor(o.Text("squad_def") ?? "")))
                       .Where(s => squads.Contains(s.Squad))];

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
    /// the squad <paramref name="squadFor"/> gives for Guerrilla's.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlyList<uint> patrols,
                                           IReadOnlyList<(uint Handle, string Squad)> standing, Func<string, string> squadFor, ZoneSchema schema, ISet<uint> taken)
    {
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

        var standingNodes = standing.Where(s => zone.ContainsKey(s.Handle)).Select(s => zone[s.Handle]).ToList();

        // Handles: Guerrilla's, unless taken.
        var handleOf = new Dictionary<uint, uint>();
        foreach (var o in navpoints.Concat(placedPatrols).Concat(squads).Concat(standingNodes))
        {
            uint handle = o.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            handleOf[o.Handle] = handle;
        }
        uint Moved(uint h) => handleOf.TryGetValue(h, out uint m) ? m : None;

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

        foreach (var navpoint in navpoints)
        {
            var links = Handles(navpoint.Value("obj_links")).Where(handleOf.ContainsKey).Select(Moved).ToList();
            var properties = new List<NewProperty> { Own(navpoint, "just_pos"), Own(navpoint, "navpoint_data") };
            if (links.Count > 0) properties.Add(Own(navpoint, "obj_links", [.. links.SelectMany(BitConverter.GetBytes)]));
            var obj = Make(navpoint, properties);
            obj.Parent = obj.Sibling = obj.Child = None;
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
        foreach (var node in standingNodes)
        {
            var squadObject = Make(node, SquadNodeProperties(node, Orders(node.Text("default_orders"))));
            squadObject.Parent = squadObject.Sibling = squadObject.Child = None;
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
