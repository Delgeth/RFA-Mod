using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// What Guerrilla's traffic needs to know of each zone, for the guerrilla_vehicles mod (its traffic spawner,
/// Guerrilla's ambient vehicle spawner ported): <c>mods\guerrilla_vehicles\traffic.txt</c>. Guerrilla takes it
/// from the zone's ambient_spawn (its obj_zone marker) → ambient_spawn_info.xtbl: the vehicle spawn group its
/// traffic picks from, how dense the traffic is by day and by night (None, Low, Medium, High), and the human
/// group its drivers come from (here the zone's spawn resource, whose people the people part lists by side in
/// mods\guerrilla_people\crews.txt). The file:
///   grid &lt;zone width&gt; &lt;column at x = 0&gt; &lt;row at z = 0&gt;   (a zone's column = floor(x / width) + column, row = row − floor(z / width))
///   zone &lt;col&gt; &lt;row&gt; &lt;vehicle group&gt; &lt;day density 0-3&gt; &lt;night density 0-3&gt; &lt;spawn resource&gt;
///   group &lt;vehicle group&gt; &lt;its vehicles built, by commas&gt;   (each group the zones name)
///   people &lt;col&gt; &lt;row&gt; &lt;spawn resource&gt;   (every zone: whose people crew its vehicles)
///   edf &lt;driveby|harass|flyby&gt; &lt;vehicles&gt;   (the EDF's ambient actions' groups: Default EDF, Default EDF NoTurret, EDF_flyer)
///   gunners &lt;vehicles&gt;   (the vehicles built with a turret on a passenger seat: their crews have a gunner)
/// A zone whose group none of the vehicles built are in, or with no traffic, has no zone line.
/// </summary>
public static class GuerrillaTraffic
{
    public const string File = "traffic.txt";

    static int Density(string? value) => value?.Trim().ToLowerInvariant() switch { "low" => 1, "medium" => 2, "high" => 3, _ => 0 };

    /// <summary>Writes the file into <paramref name="vehiclesModFolder"/>; <paramref name="groups"/> are the vehicle spawn groups written (Guerrilla's names); gives a line for the log.</summary>
    public static string Write(string guerrillaCache, Packfile guerrillaTables, IReadOnlyDictionary<string, List<string>> groups, IEnumerable<string> gunnerVehicles, string vehiclesModFolder)
    {
        var info = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("ambient_spawn_info.xtbl") ?? throw new InvalidDataException("no ambient_spawn_info.xtbl in Guerrilla's misc.vpp_pc"))).Entries
            .GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var ambientGroups = GuerrillaDistricts.AmbientGroups(guerrillaTables);
        var districtGroups = GuerrillaDistricts.DistrictGroups(ambientGroups.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint marker = NameHashes.ClassHash("obj_zone");
        var lines = new List<string>
        {
            "# Guerrilla's traffic by zone, for the guerrilla_vehicles mod (written by editor\\GuerrillaBuilder.exe with the vehicles)",
            $"grid {GuerrillaWorld.ZoneWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)} 8 7",
        };
        int listed = 0;
        var named = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, row) in GuerrillaWorld.ZonesIn(guerrillaCache))
        {
            if (zones.Find($"terr01_{col:00}_{row:00}.rfgzone_pc") is not { } entry) continue;
            var zoneMarker = GuerrillaZones.Objects(zones.Read(entry)).FirstOrDefault(o => o.ClassHash == marker);
            if (zoneMarker != null)
            {
                string? zoneHumans = GuerrillaDistricts.ZoneGroup(zoneMarker, ambientGroups);
                lines.Add($"people {col} {row} {GuerrillaDistricts.ResourceOf(zoneHumans != null && districtGroups.Contains(zoneHumans) ? zoneHumans : null)}");
            }
            string? ambient = zoneMarker?.Text("ambient_spawn")?.Trim();
            if (string.IsNullOrEmpty(ambient) || !info.TryGetValue(ambient, out var spawn)) continue;
            string? group = spawn.ValueOf("vehicle_spawn_group")?.Trim();
            int day = Density(spawn.ValueOf("veh_spawn_density")), night = Density(spawn.ValueOf("night_veh_spawn_density"));
            if (string.IsNullOrEmpty(group) || !groups.ContainsKey(group) || day + night == 0) continue;
            named.Add(group);
            string? humans = GuerrillaDistricts.ZoneGroup(zoneMarker!, ambientGroups);
            string resource = GuerrillaDistricts.ResourceOf(humans != null && districtGroups.Contains(humans) ? humans : null);
            lines.Add($"zone {col} {row} {GuerrillaVehiclePlaces.GroupPrefix}{group} {day} {night} {resource}");
            listed++;
        }
        foreach (string group in named) lines.Add($"group {GuerrillaVehiclePlaces.GroupPrefix}{group} {string.Join(",", groups[group])}");
        foreach (var (action, group) in new[] { ("driveby", "Default EDF"), ("harass", "Default EDF NoTurret"), ("flyby", "EDF_flyer") })
            if (groups.TryGetValue(group, out var vehicles) && vehicles.Count > 0) lines.Add($"edf {action} {string.Join(",", vehicles)}");
        if (gunnerVehicles.Any()) lines.Add($"gunners {string.Join(",", gunnerVehicles)}");
        Directory.CreateDirectory(vehiclesModFolder);
        System.IO.File.WriteAllLines(Path.Combine(vehiclesModFolder, File), lines);
        return $"Traffic: {listed} zones with traffic in mods\\guerrilla_vehicles\\{File}";
    }
}
