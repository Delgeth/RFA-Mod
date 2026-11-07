using System.Text;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's roads and parked vehicles, as Armageddon's zone objects, for the world builder.
/// Both games have the classes, written the same way by Volition's tools:
///   - object_path_road: a road (its points, each a position, a curve handle and a width; then its
///     junctions, each another road by handle and the points joined) in path_road_struct and
///     path_road_data, its flags (ambient traffic, one way, special, no reinforcements, GPS only) in
///     path_road_flags, and the tools' own prev/NEXT, op, Outer_Radius and speed_limit. Guerrilla's 261
///     roads are kept as they are with their handles (their junctions name each other by handle, across
///     zones), in the zone and again in its persistent p_ zone as Guerrilla has them (Armageddon's own
///     levels put theirs in the zone alone).
///   - object_vehicle_spawn_node: a parked vehicle's spot: the vehicle spawn group it picks from
///     (spawn_set), its chance, and whether the player's chosen vehicle may come back there. The group is
///     Guerrilla's of that name, which the vehicles part writes into spawn_group_vehicle.xtbl as
///     <see cref="GroupPrefix"/> + its name (three of Guerrilla's names are Armageddon's too). Guerrilla's
///     night chance, special vehicle and no-parking flags aren't read by Armageddon's.
/// </summary>
public static class GuerrillaVehiclePlaces
{
    /// <summary>Guerrilla's vehicle spawn groups' names in Armageddon's table: this, then Guerrilla's name.</summary>
    public const string GroupPrefix = "rfg_";

    const uint None = 0xFFFFFFFF;
    static readonly uint Road = NameHashes.ClassHash("object_path_road"), Node = NameHashes.ClassHash("object_vehicle_spawn_node");

    /// <summary>The order Volition's tools wrote each class's properties in (Armageddon's own levels).</summary>
    static readonly string[] RoadOrder = ["prev", "NEXT", "path_road_data", "path_road_struct", "op", "Outer_Radius", "path_road_flags", "speed_limit"];
    static readonly string[] NodeOrder = ["spawn_set", "op", "spawn_prob", "player_vehicle_respawn"];

    /// <summary>
    /// A zone's roads and vehicle spawn nodes (from Guerrilla's zone, <paramref name="zone"/>) as Armageddon's
    /// objects. Roads keep Guerrilla's handles; one whose handle is taken (in <paramref name="taken"/>, which
    /// gains the handles used) is left out and named in <paramref name="problems"/>. Nodes keep Guerrilla's
    /// handles unless taken.
    /// </summary>
    public static (List<ZoneObject> Roads, List<ZoneObject> Nodes) Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, ZoneSchema schema,
                                                                          ISet<uint> taken, List<string> problems)
    {
        var roads = new List<ZoneObject>();
        var nodes = new List<ZoneObject>();
        foreach (var o in zone.Values.Where(o => o.ClassHash == Road))
        {
            if (!taken.Add(o.Handle)) { problems.Add($"road {o.Handle:X8}: its handle is another object's"); continue; }
            roads.Add(Make(o, o.Handle, RoadOrder.Where(name => o.Properties.ContainsKey(NameHashes.PropertyHash(name))).Select(name => Own(o, name))));
        }
        foreach (var o in zone.Values.Where(o => o.ClassHash == Node))
        {
            uint handle = o.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            nodes.Add(Make(o, handle, NodeOrder.Select(name => name switch
            {
                "spawn_set" => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(GroupPrefix + (o.Text(name) ?? "Default Guerilla"))),
                _ when o.Properties.ContainsKey(NameHashes.PropertyHash(name)) => Own(o, name),
                _ => Default(schema, "object_vehicle_spawn_node", name),
            })));
        }
        return (roads, nodes);
    }

    /// <summary>The roads again, for the zone's persistent p_ zone (Guerrilla keeps each road in both).</summary>
    public static List<ZoneObject> Copies(IEnumerable<ZoneObject> roads) => roads.Select(r => ZoneObject.Create(r.ClassHash, r.Handle, r.Number,
        r.BoundsMin, r.BoundsMax, [.. r.Properties.Select(p => new NewProperty(p.Type, p.NameHash, p.Value))], 0)).Select(Detached).ToList();

    static ZoneObject Make(GuerrillaZoneObject o, uint handle, IEnumerable<NewProperty> properties) =>
        Detached(ZoneObject.Create(o.ClassHash, handle, o.Number, o.BoundsMin, o.BoundsMax, [.. properties], 0));

    static ZoneObject Detached(ZoneObject obj)
    {
        obj.Parent = obj.Sibling = obj.Child = None;
        return obj;
    }

    static NewProperty Own(GuerrillaZoneObject o, string name)
    {
        var (type, raw) = o.Properties[NameHashes.PropertyHash(name)];
        return new NewProperty(type, NameHashes.PropertyHash(name), raw);
    }

    static NewProperty Default(ZoneSchema schema, string className, string name)
    {
        var property = schema.Class(className)?.Property(name) ?? throw new InvalidDataException($"the schema's {className} has no {name}");
        return new NewProperty(property.ZoneType, NameHashes.PropertyHash(property.ZoneName),
                               property.DefaultValue() ?? throw new InvalidDataException($"the schema has no default for {className}'s {name}"));
    }

    static byte[] Text(string text) => [.. Encoding.Latin1.GetBytes(text), 0];
}
