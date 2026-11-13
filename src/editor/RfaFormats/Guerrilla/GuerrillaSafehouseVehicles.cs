using System.Globalization;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's safehouses' parked vehicles, for the guerrilla_vehicles mod (its safehouses.cpp, Guerrilla's
/// object_safehouse::respawn_vehicles ported): <c>mods\guerrilla_vehicles\safehouses.txt</c>.
///
/// In Guerrilla a safehouse (object_safehouse) owns what its zone attaches to it: its vehicle spawn nodes, its
/// buildings and the trigger_region that is its area (object_safehouse::stream, RM 0xAB4570, fills its lists from
/// its children). Armageddon has no such class and the world build places the nodes on their own (their parent
/// link dropped), so the mod is told here which nodes are whose, and where each safehouse's area is: Guerrilla
/// refills a safehouse's spots when the player walks into that area (safehouse_do_frame_postsim, RM 0xA93B40).
/// The file:
///   safehouse &lt;Guerrilla's handle&gt; &lt;its name tag, or -&gt; &lt;enabled 0/1&gt;
///   area box &lt;x y z&gt; &lt;right xyz&gt; &lt;up xyz&gt; &lt;forward xyz&gt; &lt;min xyz&gt; &lt;max xyz&gt;   (its region: an oriented box, its local min and max)
///   area sphere &lt;x y z&gt; &lt;radius&gt;
///   node &lt;x y z&gt; &lt;spawn group&gt; &lt;player_vehicle_respawn 0/1&gt;   (each of its vehicle spawn nodes, where the world build put it)
/// Taken from the main zones (each safehouse is also in its persistent p_ zone, with the same children).
/// </summary>
public static class GuerrillaSafehouseVehicles
{
    public const string File = "safehouses.txt";

    static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    static float[]? Floats(GuerrillaZoneObject o, string name)
    {
        if (o.Value(name) is not { Length: >= 4 } raw || raw.Length % 4 != 0) return null;
        var values = new float[raw.Length / 4];
        for (int i = 0; i < values.Length; i++) values[i] = BitConverter.ToSingle(raw, i * 4);
        return values;
    }

    static IEnumerable<GuerrillaZoneObject> Children(GuerrillaZoneObject parent, IReadOnlyDictionary<uint, GuerrillaZoneObject> zone)
    {
        var seen = new HashSet<uint>();
        for (uint child = parent.Child; child != 0xFFFFFFFF && seen.Add(child) && zone.TryGetValue(child, out var o); child = o.Sibling) yield return o;
    }

    /// <summary>Writes the file into <paramref name="vehiclesModFolder"/>; gives a line for the log.</summary>
    public static string Write(string guerrillaCache, string vehiclesModFolder)
    {
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint safehouseClass = NameHashes.ClassHash("object_safehouse"), regionClass = NameHashes.ClassHash("trigger_region"),
             nodeClass = NameHashes.ClassHash("object_vehicle_spawn_node");
        var lines = new List<string>
        {
            @"# Guerrilla's safehouses' parked vehicles, for the guerrilla_vehicles mod (written by editor\GuerrillaBuilder.exe with the vehicles)",
        };
        int houses = 0, areas = 0, nodes = 0;
        var problems = new List<string>();
        foreach (var (col, row) in GuerrillaWorld.ZonesIn(guerrillaCache))
        {
            if (zones.Find($"terr01_{col:00}_{row:00}.rfgzone_pc") is not { } entry) continue;
            var zone = GuerrillaZones.Objects(zones.Read(entry)).GroupBy(o => o.Handle).ToDictionary(g => g.Key, g => g.First());
            foreach (var house in zone.Values.Where(o => o.ClassHash == safehouseClass))
            {
                string name = house.Text("display_name_tag") is { Length: > 0 } tag ? tag.Replace(' ', '_') : "-";
                bool enabled = house.Value("enabled") is not { Length: > 0 } flag || flag[0] != 0;
                lines.Add($"safehouse {house.Handle:X8} {name} {(enabled ? 1 : 0)}");
                houses++;
                foreach (var child in Children(house, zone))
                {
                    if (child.ClassHash == regionClass)
                    {
                        float[]? op = Floats(child, "op");
                        string shape = child.Text("trigger_shape")?.Trim().ToLowerInvariant() ?? "";
                        if (op is not { Length: >= 12 }) { problems.Add($"{house.Handle:X8}'s area {child.Handle:X8} has no place"); continue; }
                        string at = $"{F(op[0])} {F(op[1])} {F(op[2])}";
                        if (shape == "sphere" && Floats(child, "outer_radius") is { Length: >= 1 } radius)
                            lines.Add($"area sphere {at} {F(radius[0])}");
                        else if (shape == "box" && Floats(child, "bb") is { Length: >= 6 } box)
                            lines.Add($"area box {at} {string.Join(' ', op.Skip(3).Take(9).Select(F))} {string.Join(' ', box.Take(6).Select(F))}");
                        else { problems.Add($"{house.Handle:X8}'s area {child.Handle:X8} is a {(shape.Length > 0 ? shape : "shape")} not known"); continue; }
                        areas++;
                    }
                    else if (child.ClassHash == nodeClass && Floats(child, "op") is { Length: >= 3 } place)
                    {
                        string group = GuerrillaVehiclePlaces.GroupPrefix + (child.Text("spawn_set")?.Trim() is { Length: > 0 } set ? set : "Default Guerilla");
                        bool respawn = child.Value("player_vehicle_respawn") is { Length: > 0 } r && r[0] != 0;
                        lines.Add($"node {F(place[0])} {F(place[1])} {F(place[2])} {group.Replace(' ', '_')} {(respawn ? 1 : 0)}");
                        nodes++;
                    }
                }
            }
        }
        Directory.CreateDirectory(vehiclesModFolder);
        System.IO.File.WriteAllLines(Path.Combine(vehiclesModFolder, File), lines);
        return $@"Safehouses: {houses} with {areas} areas and {nodes} parked vehicles' spots in mods\guerrilla_vehicles\{File}" +
               (problems.Count > 0 ? $"; left out: {string.Join("; ", problems)}" : "");
    }
}
