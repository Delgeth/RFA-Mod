using System.Globalization;
using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's tables for the guerrilla_campaign mod, which runs Guerrilla's campaign systems (the
/// alert level and the EDF's answer to it, EDF control, morale, ...) from Guerrilla's own numbers.
/// Each is copied as it is from the player's own copy of Guerrilla (its misc.vpp_pc) into
/// mods\guerrilla_campaign\tables\, where the mod reads it by name; never shipped.
/// </summary>
public static class GuerrillaCampaign
{
    /// <summary>The folder in mods\guerrilla_campaign\ the tables go into.</summary>
    public const string TablesFolder = "tables";
    /// <summary>Each of Guerrilla's vehicles and its team, a line each (name, a tab, the team).</summary>
    public const string VehicleTeams = "vehicle_teams.txt";
    /// <summary>Guerrilla's restricted areas (the EDF's bases), a line each: see <see cref="RestrictedAreas"/>.</summary>
    public const string RestrictedAreasFile = "restricted_areas.txt";
    /// <summary>Guerrilla's kill regions (its pits and minefields), a line each: see <see cref="KillRegions"/>.</summary>
    public const string KillRegionsFile = "kill_regions.txt";
    const int MostPolygonPoints = 64;  // restricted_area_polygon_data: vector2[64] each

    /// <summary>
    /// The tables the mod reads: the alert level's bands and decay, what each kind of damage and death
    /// adds to it, the difficulty's multipliers, the EDF's and the guerrillas' reinforcements, the
    /// districts, the missions (which district each belongs to) and the activities.
    /// </summary>
    public static readonly string[] Tables =
    [
        "alert_level.xtbl", "alert_value.xtbl", "difficulty.xtbl", "reinforcement_profile.xtbl", "guerrilla_reinforcement.xtbl",
        "territories.xtbl", "missions.xtbl", "activity.xtbl", "activity_demolitions_master.xtbl",
    ];

    public static IEnumerable<string> Build(string guerrilla, string campaignFolder)
    {
        var guerrillaTables = Packfile.Open(Path.Combine(guerrilla, "build", "pc", "cache", "misc.vpp_pc"));
        string folder = Path.Combine(campaignFolder, TablesFolder);
        Directory.CreateDirectory(folder);
        var missing = new List<string>();
        int copied = 0;
        foreach (string table in Tables)
        {
            if (guerrillaTables.Find(table) is not { } entry) { missing.Add(table); continue; }
            File.WriteAllBytes(Path.Combine(folder, table), guerrillaTables.Read(entry));
            copied++;
        }
        yield return $"Campaign tables: {copied} of Guerrilla's tables copied into {folder} (the alert level, reinforcements, districts, activities)";
        if (missing.Count > 0) yield return $"Campaign tables: Guerrilla's misc.vpp_pc has no {string.Join(", ", missing)}; the mod keeps Guerrilla's usual values for those";

        // Whose each vehicle is (Guerrilla's vehicles.xtbl, Default_Team: EDF, Guerilla, Civilian, Marauder): the alert
        // rises when the EDF's are wrecked. The vehicles' own table can't say: Armageddon's has no EDF to name.
        if (guerrillaTables.Find("vehicles.xtbl") is { } vehicles)
        {
            var lines = new List<string>();
            foreach (var entry in XtblFile.FromBytes(guerrillaTables.Read(vehicles)).Entries)
                if (entry.ValueOf("Default_Team")?.Trim() is { Length: > 0 } team)
                    lines.Add($"{XtblFile.EntryName(entry)}\t{team}");
            File.WriteAllLines(Path.Combine(campaignFolder, VehicleTeams), lines);
            yield return $"Campaign tables: {lines.Count} vehicles' teams ({lines.Count(l => l.EndsWith("\tEDF", StringComparison.OrdinalIgnoreCase))} the EDF's) in {VehicleTeams}";
        }

        yield return RestrictedAreas(Path.Combine(guerrilla, "build", "pc", "cache"), campaignFolder);
        yield return KillRegions(Path.Combine(guerrilla, "build", "pc", "cache"), campaignFolder);
    }

    /// <summary>
    /// Guerrilla's kill regions: the trigger_regions whose region_type is "kill human" (25 in its zones: 21 at
    /// the bottoms of pits and chasms, region_kill_type cliff, and 4 minefields, mine). Armageddon's own
    /// trigger_region never reads region_type, so the mod does what Guerrilla's trigger_region_do_frame does
    /// with them. A line each: region, its handle, its zone, cliff or mine, box or sphere, enabled (0/1),
    /// not_in_mission (0/1), not_in_activity (0/1), then its place and turn (op: position, right, up,
    /// forward; 12 numbers), its box around that place (bb: least, then most corner; 6 numbers) and its
    /// radius (outer_radius, for a sphere). A region that is in a zone and again in that zone's p_ file (the
    /// same handle) is written once.
    /// </summary>
    static string KillRegions(string guerrillaCache, string campaignFolder)
    {
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint regionClass = NameHashes.ClassHash("trigger_region");
        var lines = new List<string> { "# Guerrilla's kill regions, for the guerrilla_campaign mod (written by editor\\GuerrillaBuilder.exe with the people)" };
        var written = new HashSet<uint>();
        int cliffs = 0, mines = 0;
        var problems = new List<string>();
        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static float[]? Floats(GuerrillaZoneObject o, string name)
        {
            if (o.Value(name) is not { Length: > 0 } v || v.Length % 4 != 0) return null;
            var f = new float[v.Length / 4];
            for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToSingle(v, i * 4);
            return f;
        }
        // The zones before their p_ files, so a region in both is named by its zone.
        foreach (var entry in zones.Entries.Where(e => e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase))
                                           .OrderBy(e => e.Name.StartsWith("p_", StringComparison.OrdinalIgnoreCase)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            string zone = Path.GetFileNameWithoutExtension(entry.Name);
            foreach (var region in GuerrillaZones.Objects(zones.Read(entry)).Where(o => o.ClassHash == regionClass))
            {
                if (!string.Equals(region.Text("region_type")?.Trim(), "kill human", StringComparison.OrdinalIgnoreCase)) continue;
                if (!written.Add(region.Handle)) continue;
                bool mine = string.Equals(region.Text("region_kill_type")?.Trim(), "mine", StringComparison.OrdinalIgnoreCase);
                bool sphere = string.Equals(region.Text("trigger_shape")?.Trim(), "sphere", StringComparison.OrdinalIgnoreCase);
                bool enabled = region.Value("enabled") is not { Length: > 0 } on || on[0] != 0;  // the schema's default: true
                string flags = region.Text("trigger_flags") ?? "";
                bool notInMission = flags.Contains("not_in_mission", StringComparison.OrdinalIgnoreCase);
                bool notInActivity = flags.Contains("not_in_activity", StringComparison.OrdinalIgnoreCase);
                float[]? op = Floats(region, "op"), bb = Floats(region, "bb"), radius = Floats(region, "outer_radius");
                if (op is not { Length: >= 12 } || bb is not { Length: >= 6 })
                {
                    problems.Add($"{region.Handle:X8} in {zone} has no place or box");
                    continue;
                }
                lines.Add($"region {region.Handle:X8} {zone} {(mine ? "mine" : "cliff")} {(sphere ? "sphere" : "box")} {(enabled ? 1 : 0)} {(notInMission ? 1 : 0)} {(notInActivity ? 1 : 0)} " +
                          $"{string.Join(' ', op.Take(12).Select(F))} {string.Join(' ', bb.Take(6).Select(F))} {F(radius is { Length: >= 1 } r ? r[0] : 0f)}");
                if (mine) mines++;
                else cliffs++;
            }
        }
        Directory.CreateDirectory(campaignFolder);
        File.WriteAllLines(Path.Combine(campaignFolder, KillRegionsFile), lines);
        return $"Kill regions: {cliffs + mines} in {KillRegionsFile} ({cliffs} pits, {mines} minefields)" +
               (problems.Count > 0 ? $"; left out: {string.Join("; ", problems)}" : "");
    }

    /// <summary>
    /// Guerrilla's restricted areas (object_restricted_area, 40 in its zones: its EDF bases), which Armageddon
    /// has no class for, so the world converter leaves them out. Each is a "yellow" polygon (inside it the
    /// EDF who see the player take him for an enemy, and the alert level doesn't fall) and a wider "warning"
    /// polygon (may be empty), in world coordinates (x, z), as object_restricted_area::serialize reads them
    /// (yellow_polygon, warning_polygon: vector2 each, at most 64), with its disabled and allow_ambient_peds
    /// switches. A line each: area, its handle, its zone, disabled (0/1), allow_ambient_peds (0/1), the yellow
    /// polygon's number of points and its points (x z ...), the warning polygon's the same way.
    /// </summary>
    static string RestrictedAreas(string guerrillaCache, string campaignFolder)
    {
        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint areaClass = NameHashes.ClassHash("object_restricted_area");
        var lines = new List<string> { "# Guerrilla's restricted areas, for the guerrilla_campaign mod (written by editor\\GuerrillaBuilder.exe with the people)" };
        int areas = 0, empty = 0;
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Polygon(byte[]? value)
        {
            int count = Math.Min((value?.Length ?? 0) / 8, MostPolygonPoints);
            var parts = new List<string> { count.ToString(CultureInfo.InvariantCulture) };
            for (int i = 0; i < count; i++) parts.Add($"{F(BitConverter.ToSingle(value!, i * 8))} {F(BitConverter.ToSingle(value!, i * 8 + 4))}");
            return string.Join(" ", parts);
        }
        static string Switch(byte[]? value) => value is { Length: > 0 } && value.Any(b => b != 0) ? "1" : "0";
        foreach (var entry in zones.Entries.Where(e => e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            foreach (var area in GuerrillaZones.Objects(zones.Read(entry)).Where(o => o.ClassHash == areaClass))
            {
                var yellow = area.Value("yellow_polygon");
                if ((yellow?.Length ?? 0) < 24) { empty++; continue; }  // fewer than three points: no area
                lines.Add($"area {area.Handle:X8} {Path.GetFileNameWithoutExtension(entry.Name)} {Switch(area.Value("disabled"))} {Switch(area.Value("allow_ambient_peds"))} " +
                          $"{Polygon(yellow)} {Polygon(area.Value("warning_polygon"))}");
                areas++;
            }
        Directory.CreateDirectory(campaignFolder);
        File.WriteAllLines(Path.Combine(campaignFolder, RestrictedAreasFile), lines);
        return $"Restricted areas: {areas} in {RestrictedAreasFile}" + (empty > 0 ? $" ({empty} without a yellow polygon left out)" : "");
    }

    public static IEnumerable<string> Remove(string campaignFolder)
    {
        foreach (string written in new[] { VehicleTeams, RestrictedAreasFile, KillRegionsFile })
        {
            string file = Path.Combine(campaignFolder, written);
            if (!File.Exists(file)) continue;
            File.Delete(file);
            yield return $"Deleted {file}";
        }
        string folder = Path.Combine(campaignFolder, TablesFolder);
        if (!Directory.Exists(folder)) yield break;
        foreach (string table in Tables)
        {
            string file = Path.Combine(folder, table);
            if (!File.Exists(file)) continue;
            File.Delete(file);
            yield return $"Deleted {file}";
        }
        if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }
}
