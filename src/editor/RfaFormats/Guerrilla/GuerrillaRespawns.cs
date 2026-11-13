using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Where Mason wakes after a death, for the guerrilla_save mod (its Guerrilla respawn):
/// <c>mods\guerrilla_save\respawns.txt</c>.
///
/// Guerrilla puts him, out of a mission, at the nearest safehouse in the district he died in that is
/// visible and enabled (safehouse_find_spawn_safehouse, RM 0xA6B660), at the safehouse's spot
/// (safehouse_get_start_pos_orient, RM 0xA6B860: a player_start that is its child, else the nearest
/// player_start within 50 m of it, else the safehouse's own place and facing); with no such safehouse, at
/// the nearest player_start. Armageddon has no safehouse class (the world build leaves them out) and the
/// world's player starts aren't made either, so the mod is told each safehouse's spot and district, and
/// where the player starts are. A district is the zones its territories.xtbl entry lists (terr01, each
/// Grid_item's Index, the zone's column × 100 + row), as the guerrilla_campaign mod reads it. The file:
///   safehouse &lt;name tag, or -&gt; &lt;district, spaces as _, or -&gt; &lt;visible 0/1&gt; &lt;enabled 0/1&gt; &lt;x y z&gt; &lt;9 numbers: right, up, forward&gt;
/// Every safehouse starts hidden in Guerrilla's zones (visible 0); its missions show them
/// (safehouse_set_visible), so until those are ported the mod goes by enabled alone.
///   start &lt;x y z&gt; &lt;9 numbers&gt;
/// Taken from the main zones (each safehouse is also in its persistent p_ zone, with the same children).
/// </summary>
public static class GuerrillaRespawns
{
    public const string File = "respawns.txt";

    /// <summary>Guerrilla's reach for a safehouse's player start, when it has none of its own (RM 0x1311000).</summary>
    const float StartReach = 50f;

    static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    readonly record struct Place(Vector3 Position, float[] Facing);

    /// <summary>A zone object's place and facing ("op": 3 numbers, then its 3 × 3 orientation).</summary>
    static Place? PlaceOf(GuerrillaZoneObject o)
    {
        if (o.Value("op") is not { Length: 48 } op) return null;
        float N(int i) => BitConverter.ToSingle(op, i * 4);
        return new Place(new Vector3(N(0), N(1), N(2)), Enumerable.Range(3, 9).Select(N).ToArray());
    }

    static IEnumerable<GuerrillaZoneObject> Children(GuerrillaZoneObject parent, IReadOnlyDictionary<uint, GuerrillaZoneObject> zone)
    {
        var seen = new HashSet<uint>();
        for (uint child = parent.Child; child != 0xFFFFFFFF && seen.Add(child) && zone.TryGetValue(child, out var o); child = o.Sibling) yield return o;
    }

    static bool Flag(GuerrillaZoneObject o, string name) => o.Value(name) is not { Length: > 0 } flag || flag[0] != 0;

    /// <summary>terr01's districts: each zone's (column × 100 + row) district name.</summary>
    static Dictionary<int, string> Districts(string guerrillaCache)
    {
        var cells = new Dictionary<int, string>();
        var tables = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        if (tables.Find("territories.xtbl") is not { } entry) return cells;
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(tables.Read(entry)).TrimStart('﻿'));
        foreach (var territory in document.Descendants("Territory").Where(t => (string?)t.Element("Name") == "terr01"))
            foreach (var district in territory.Descendants("District"))
            {
                string name = ((string?)district.Element("Name") ?? "").Trim();
                foreach (var cell in district.Descendants("Grid_item"))
                    if (int.TryParse(((string?)cell.Element("Index"))?.Trim(), out int index)) cells.TryAdd(index, name);
            }
        return cells;
    }

    /// <summary>Writes the file into <paramref name="saveModFolder"/>; gives a line for the log.</summary>
    public static string Write(string guerrillaCache, string saveModFolder)
    {
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint safehouseClass = NameHashes.ClassHash("object_safehouse"), startClass = NameHashes.ClassHash("player_start");
        var districts = Districts(guerrillaCache);
        var houses = new List<(GuerrillaZoneObject House, string District, Place Place, Place? OwnStart)>();
        var starts = new List<Place>();
        foreach (var (col, row) in GuerrillaWorld.ZonesIn(guerrillaCache))
        {
            if (zones.Find($"terr01_{col:00}_{row:00}.rfgzone_pc") is not { } entry) continue;
            var zone = GuerrillaZones.Objects(zones.Read(entry)).GroupBy(o => o.Handle).ToDictionary(g => g.Key, g => g.First());
            foreach (var o in zone.Values)
            {
                if (o.ClassHash == startClass && PlaceOf(o) is { } start) starts.Add(start);
                if (o.ClassHash != safehouseClass || PlaceOf(o) is not { } place) continue;
                Place? own = Children(o, zone).Where(c => c.ClassHash == startClass).Select(PlaceOf).FirstOrDefault(p => p != null);
                houses.Add((o, districts.GetValueOrDefault(col * 100 + row, ""), place, own));
            }
        }
        var lines = new List<string>
        {
            @"# Where Mason wakes after a death, for the guerrilla_save mod (written by editor\GuerrillaBuilder.exe with the world)",
            "# safehouse <name> <district> <visible 0/1> <enabled 0/1> <x y z> <right up forward>; start <x y z> <right up forward>",
        };
        int ownStarts = 0, nearStarts = 0, usable = 0;
        foreach (var (house, district, place, own) in houses)
        {
            // safehouse_get_start_pos_orient: its own player start, else the nearest within 50 m, else its own place.
            Place spot = place;
            if (own is { } o) { spot = o; ownStarts++; }
            else if (starts.Where(s => Vector3.Distance(s.Position, place.Position) <= StartReach)
                           .OrderBy(s => Vector3.Distance(s.Position, place.Position)).Cast<Place?>().FirstOrDefault() is { } near)
            { spot = near; nearStarts++; }
            bool visible = Flag(house, "visible"), enabled = Flag(house, "enabled");
            if (visible && enabled) usable++;
            string name = house.Text("display_name_tag") is { Length: > 0 } tag ? tag.Replace(' ', '_') : "-";
            lines.Add($"safehouse {name} {(district.Length > 0 ? district.Replace(' ', '_') : "-")} {(visible ? 1 : 0)} {(enabled ? 1 : 0)} " +
                      $"{F(spot.Position.X)} {F(spot.Position.Y)} {F(spot.Position.Z)} {string.Join(' ', spot.Facing.Select(F))}");
        }
        foreach (var start in starts)
            lines.Add($"start {F(start.Position.X)} {F(start.Position.Y)} {F(start.Position.Z)} {string.Join(' ', start.Facing.Select(F))}");
        Directory.CreateDirectory(saveModFolder);
        System.IO.File.WriteAllLines(Path.Combine(saveModFolder, File), lines);
        return $@"Respawns: {houses.Count} safehouses ({usable} visible and enabled at the start; {ownStarts} at their own player start, {nearStarts} at one " +
               $@"within 50 m, the rest at their own place) and {starts.Count} player starts in mods\guerrilla_save\{File}";
    }
}
