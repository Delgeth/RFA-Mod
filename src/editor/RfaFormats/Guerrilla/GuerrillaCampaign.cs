using RfaFormats.Tables;

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
    }

    public static IEnumerable<string> Remove(string campaignFolder)
    {
        string teams = Path.Combine(campaignFolder, VehicleTeams);
        if (File.Exists(teams))
        {
            File.Delete(teams);
            yield return $"Deleted {teams}";
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
