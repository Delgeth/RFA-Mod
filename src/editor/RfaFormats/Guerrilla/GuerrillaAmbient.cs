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
        int listed = 0, regions = 0;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        // Guerrilla's ambient behaviours (ambient_spawn_behavior.xtbl, which Armageddon's own code reads the same way:
        // 0x7522F0): each group's behaviours, for guerrilla_people's walkers (ambient.cpp picks one as each is made, as
        // Guerrilla's human::serialize does for its ambient_behavior). Tab-separated, the names having spaces.
        if (guerrillaTables.Find("ambient_spawn_behavior.xtbl") is { } behaviorEntry)
            foreach (var group in XtblFile.FromBytes(guerrillaTables.Read(behaviorEntry)).Entries)
                foreach (var b in group.Element("Behavior_List")?.Elements ?? [])
                    lines.Add(string.Join('\t', "behavior", XtblFile.EntryName(group), Dash(b.ValueOf("Animation_Override")), Dash(b.ValueOf("Ambient_Prop")),
                                          Number(b.ValueOf("Chance")), Number(b.ValueOf("Day_Start")), Number(b.ValueOf("Day_End")),
                                          Number(b.ValueOf("Morale_Min")), Number(b.ValueOf("Morale_Max")), Number(b.ValueOf("MaleFemaleRatio"))));
        // Its women (character.xtbl's Sex female): the behaviours' MaleFemaleRatio favours them or the men.
        if (guerrillaTables.Find("character.xtbl") is { } characterEntry)
            foreach (var character in XtblFile.FromBytes(guerrillaTables.Read(characterEntry)).Entries)
                if ((character.ValueOf("Sex")?.Trim() ?? "").Equals("female", StringComparison.OrdinalIgnoreCase))
                    lines.Add($"female\t{XtblFile.EntryName(character)}");
        uint behaviorRegion = NameHashes.ClassHash("object_ambient_behavior_region");
        foreach (var (col, row) in GuerrillaWorld.ZonesIn(guerrillaCache))
        {
            if (zones.Find($"terr01_{col:00}_{row:00}.rfgzone_pc") is not { } entry) continue;
            var objects = GuerrillaZones.Objects(zones.Read(entry));
            // Its ambient behaviour regions (object_ambient_behavior_region: AMBIENT_BEHAVIOR_NAME and _RADIUS, by their
            // hashes): within the radius, the group the walkers made there take their behaviour from.
            foreach (var region in objects.Where(o => o.ClassHash == behaviorRegion))
                if (region.Value("OP") is { Length: >= 12 } op && region.Properties.TryGetValue(BehaviorName, out var name) &&
                    region.Properties.TryGetValue(BehaviorRadius, out var radius) && radius.Value.Length >= 4)
                {
                    lines.Add(string.Join('\t', "region", BitConverter.ToSingle(op, 0).ToString("R", c), BitConverter.ToSingle(op, 4).ToString("R", c),
                                          BitConverter.ToSingle(op, 8).ToString("R", c), BitConverter.ToSingle(radius.Value).ToString("R", c),
                                          System.Text.Encoding.Latin1.GetString(name.Value).TrimEnd('\0')));
                    regions++;
                }
            var zoneMarker = objects.FirstOrDefault(o => o.ClassHash == marker);
            string? ambient = zoneMarker?.Text("ambient_spawn")?.Trim();
            if (string.IsNullOrEmpty(ambient) || !info.TryGetValue(ambient, out var spawn)) continue;
            int day = Density(spawn.ValueOf("ped_spawn_density")), night = Density(spawn.ValueOf("night_ped_spawn_density"));
            if (day + night == 0) continue;
            string? humans = GuerrillaDistricts.ZoneGroup(zoneMarker!, ambientGroups);
            string resource = GuerrillaDistricts.ResourceOf(humans != null && districtGroups.Contains(humans) ? humans : null);
            lines.Add($"zone {col} {row} {day} {night} {resource} {ambient}");
            if (spawn.ValueOf("default_ambient_behavior")?.Trim() is { Length: > 0 } behavior) lines.Add($"default\t{col}\t{row}\t{behavior}");
            listed++;
        }
        Directory.CreateDirectory(peopleFolder);
        System.IO.File.WriteAllLines(Path.Combine(peopleFolder, File), lines);
        return $"Walkers: {listed} zones where Guerrilla's people walk, {lines.Count(l => l.StartsWith("behavior\t"))} ambient behaviours, {regions} ambient " +
               $"behaviour regions, in mods\\guerrilla_people\\{File}";
    }

    /// <summary>object_ambient_behavior_region's properties (AMBIENT_BEHAVIOR_NAME, AMBIENT_BEHAVIOR_RADIUS; their names aren't known).</summary>
    const uint BehaviorName = 0x5E897FD9, BehaviorRadius = 0x8DB32C8B;

    static string Dash(string? text) => text?.Trim() is { Length: > 0 } t ? t : "-";
    static string Number(string? text) => text?.Trim() is { Length: > 0 } t ? t : "0";
}
