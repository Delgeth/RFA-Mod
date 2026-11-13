using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// What Guerrilla's walkers need to know of each zone, for the guerrilla_people mod (its ambient people spawner,
/// Guerrilla's do_frame_spawn_npc ported): <c>mods\guerrilla_people\ambient.txt</c>. Guerrilla takes it from the
/// player's zone's ambient_spawn (its obj_zone marker) → ambient_spawn_info.xtbl: how many people walk there by day
/// and by night (ped_spawn_density, night_ped_spawn_density: None, Low, Medium, High; an entry without one has
/// none), and the human group they come from (here the zone's spawn resource, whose civilians the people part lists
/// in mods\guerrilla_people\crews.txt). The file:
///   grid &lt;zone width&gt; &lt;column at x = 0&gt; &lt;row at z = 0&gt;   (a zone's column = floor(x / width) + column, row = row − floor(z / width))
///   zone &lt;col&gt; &lt;row&gt; &lt;day density 0-3&gt; &lt;night density 0-3&gt; &lt;spawn resource&gt; &lt;Guerrilla's ambient_spawn entry&gt;
/// A zone where no one walks, by day or by night, has no line.
/// </summary>
public static class GuerrillaAmbient
{
    public const string File = "ambient.txt";

    static int Density(string? value) => value?.Trim().ToLowerInvariant() switch { "low" => 1, "medium" => 2, "high" => 3, _ => 0 };

    /// <summary>Writes the file into <paramref name="peopleFolder"/>; gives a line for the log.</summary>
    public static string Write(string guerrillaCache, Packfile guerrillaTables, string peopleFolder)
    {
        var info = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("ambient_spawn_info.xtbl") ?? throw new InvalidDataException("no ambient_spawn_info.xtbl in Guerrilla's misc.vpp_pc"))).Entries
            .GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var ambientGroups = GuerrillaDistricts.AmbientGroups(guerrillaTables);
        var districtGroups = GuerrillaDistricts.DistrictGroups(ambientGroups.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint marker = NameHashes.ClassHash("obj_zone");
        var lines = new List<string>
        {
            "# Guerrilla's walkers by zone, for the guerrilla_people mod (written by editor\\GuerrillaBuilder.exe with the people)",
            $"grid {GuerrillaWorld.ZoneWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)} 8 7",
        };
        int listed = 0;
        foreach (var (col, row) in GuerrillaWorld.ZonesIn(guerrillaCache))
        {
            if (zones.Find($"terr01_{col:00}_{row:00}.rfgzone_pc") is not { } entry) continue;
            var zoneMarker = GuerrillaZones.Objects(zones.Read(entry)).FirstOrDefault(o => o.ClassHash == marker);
            string? ambient = zoneMarker?.Text("ambient_spawn")?.Trim();
            if (string.IsNullOrEmpty(ambient) || !info.TryGetValue(ambient, out var spawn)) continue;
            int day = Density(spawn.ValueOf("ped_spawn_density")), night = Density(spawn.ValueOf("night_ped_spawn_density"));
            if (day + night == 0) continue;
            string? humans = GuerrillaDistricts.ZoneGroup(zoneMarker!, ambientGroups);
            string resource = GuerrillaDistricts.ResourceOf(humans != null && districtGroups.Contains(humans) ? humans : null);
            lines.Add($"zone {col} {row} {day} {night} {resource} {ambient}");
            listed++;
        }
        Directory.CreateDirectory(peopleFolder);
        System.IO.File.WriteAllLines(Path.Combine(peopleFolder, File), lines);
        return $"Walkers: {listed} zones where Guerrilla's people walk, in mods\\guerrilla_people\\{File}";
    }
}
