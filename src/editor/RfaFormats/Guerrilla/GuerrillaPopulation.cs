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
///   - Navpoint: just_pos, Guerrilla's navpoint_data as it is (version 3, 28 bytes: Armageddon's reader
///     reads versions 1 to 4, the 4th adding only transitions' fields), obj_links (to placed ones).
///   - object_patrol: looping_patrol, combat_ready, patrol_start, OP, no_check_in, override_patrol (its
///     marauder_raid, courier_patrol and ASD_truck_partol aren't read by Armageddon's).
///   - object_squad_spawn_node: squad_def, OP, spawn_prob, no_reassignment, squad_vehicle, Guerrilla's
///     own; its default_orders, Guerrilla's "Default" (orders from what it hangs under), as Armageddon's
///     own patrols' squads say it: Patrol; the rest (Armageddon's own switches: exos, leap nodes,
///     leashes, its vehicle groups) the schema's defaults. Guerrilla's night_spawn_prob, respawn
///     speeds and special flags aren't read by Armageddon's: for the guerrilla_people mod later.
/// Armageddon holds 24 patrols at once (its object_patrol pool; Guerrilla 72), and the game keeps the
/// zones within one of the player's loaded: <see cref="Cap"/> keeps every 3 × 3 block of zones within it
/// until the pool is made bigger.
/// </summary>
public static class GuerrillaPopulation
{
    /// <summary>How many patrols Armageddon holds at once (its object_patrol pool).</summary>
    public const int MostPatrols = 24;

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

    /// <summary>The patrols (by handle) of a zone whose squad is one of <paramref name="squads"/>.</summary>
    public static List<uint> Patrols(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlySet<string> squads) =>
        [.. zone.Values.Where(o => o.ClassHash == Patrol && SquadOf(zone, o) is { } squad && squads.Contains(squad.Text("squad_def") ?? "")).Select(o => o.Handle)];

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
    /// as Armageddon's objects (handles not in <paramref name="taken"/>, which gains theirs), each object after
    /// the ones it names.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlyList<uint> patrols, ZoneSchema schema, ISet<uint> taken)
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

        // Handles: Guerrilla's, unless taken.
        var handleOf = new Dictionary<uint, uint>();
        foreach (var o in navpoints.Concat(placedPatrols).Concat(squads))
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

            var squadProperties = SquadNodeOrder.Select(name => name switch
            {
                "default_orders" => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text("Patrol")),
                _ when ArmageddonsOwn.TryGetValue(name, out var own) => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(own)),
                _ when SquadNodeOwn.Contains(name) && squad.Properties.ContainsKey(NameHashes.PropertyHash(name)) => Own(squad, name),
                _ => Default("object_squad_spawn_node", name),
            });
            var squadObject = Make(squad, squadProperties);
            squadObject.Parent = handleOf[patrol.Handle];
            squadObject.Sibling = squadObject.Child = None;
            objects.Add(squadObject);
        }
        return objects;
    }

    static uint? Handle(byte[]? value) => value is { Length: >= 4 } ? BitConverter.ToUInt32(value) : null;

    static IEnumerable<uint> Handles(byte[]? value)
    {
        if (value == null) yield break;
        for (int at = 0; at + 4 <= value.Length; at += 4) yield return BitConverter.ToUInt32(value, at);
    }

    static byte[] Text(string text) => [.. Encoding.Latin1.GetBytes(text), 0];
}
