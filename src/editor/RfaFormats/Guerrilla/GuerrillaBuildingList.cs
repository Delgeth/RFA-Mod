using System.Globalization;
using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// The Guerrilla world's buildings (its movers: rfg_mover and general_mover) as a list for the guerrilla_world mod,
/// which hands them to the HUD for Guerrilla's minimap: their outlines coloured by owner, the EDF's high- and
/// medium-priority target icons and the damage labels (Guerrilla's cartographer: get_structure_color,
/// get_structure_importance_icon, render_masked_structures, render_structures_post).
///
/// What Guerrilla reads from a mover that doesn't change in play is written here, a line each:
/// <c>zone handle parent chunk importance team game_destroyed_pct x y z</c>.
/// - handle and parent: the zone object's and its parent's (FFFFFFFF none), hex. Guerrilla leaves child pieces out of
///   its icons.
/// - chunk: its chunk file's name without extension. Guerrilla's minimap draws a building as the picture
///   "<chunk>_thumb" (render_masked_structures, RM 0x86E43A).
/// - importance and team: from its gameplay properties entry, as Guerrilla's object_mover::get_importance_level
///   (RM 0xA13830) and its team (+0xCC) take them, in Guerrilla's numbers. Importance: -1 unknown, 0 none, 1 low,
///   2 medium, 3 high (object_importance_level). Team: -1 none, 0 guerrilla, 1 EDF, 2 civilian (also "Neutral",
///   human_team_from_name RM 0xA0D2A0), 3 marauder (human_teams).
/// - game_destroyed_pct: from the same entry, the damage at which it counts as destroyed. Guerrilla's minimap shows a
///   building's health as 1 - min(1, damage / this) (RM 0x87D436).
/// - x y z: its place, for finding the buildings near a point.
/// What changes in play (its box, turn, damage, being destroyed) the mod adds from the game.
/// </summary>
public static class GuerrillaBuildingList
{
    /// <summary>The list's file, in mods\guerrilla_world\.</summary>
    public const string FileName = "buildings.txt";

    static readonly uint RfgMover = NameHashes.ClassHash("rfg_mover"), GeneralMover = NameHashes.ClassHash("general_mover");

    /// <summary>A gameplay properties entry's importance, team and game_destroyed_pct, by entry name, from the built table.</summary>
    public static IReadOnlyDictionary<string, (int Importance, int Team, float DestroyedPct)> Entries(byte[] gameplayTable)
    {
        var entries = new Dictionary<string, (int, int, float)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in XtblFile.FromBytes(gameplayTable).Entries)
        {
            string name = XtblFile.EntryName(entry);
            float.TryParse(entry.ValueOf("game_destroyed_pct")?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct);
            entries[name] = (Importance(entry.ValueOf("importance")), Team(entry.ValueOf("team")), pct);
        }
        return entries;
    }

    /// <summary>Guerrilla's object_importance_level for a table value.</summary>
    public static int Importance(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "NONE" => 0,
        "LOW" => 1,
        "MEDIUM" => 2,
        "HIGH" => 3,
        _ => -1,
    };

    /// <summary>Guerrilla's human_teams for a table value (human_team_from_name).</summary>
    public static int Team(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "GUERRILLA" or "GUERILLA" => 0,
        "EDF" => 1,
        "CIVILIAN" or "NEUTRAL" => 2,
        "MARAUDER" => 3,
        _ => -1,
    };

    /// <summary>A zone's movers as the list's lines.</summary>
    public static IEnumerable<string> Lines(string zone, IEnumerable<ZoneObject> objects,
                                            IReadOnlyDictionary<string, (int Importance, int Team, float DestroyedPct)> entries)
    {
        foreach (var o in objects.Where(o => o.ClassHash == RfgMover || o.ClassHash == GeneralMover))
        {
            string chunk = o.Property("chunk_name")?.Text is { Length: > 0 } c ? Path.GetFileNameWithoutExtension(c) : "-";
            var (importance, team, pct) = o.Property("gameplay_props")?.Text is { } g && entries.TryGetValue(g, out var e) ? e : (-1, -1, 0f);
            var at = o.Placement.Position;
            yield return string.Create(CultureInfo.InvariantCulture,
                $"{zone} {o.Handle:X8} {o.Parent:X8} {chunk} {importance} {team} {pct:0.###} {at.X:0.##} {at.Y:0.##} {at.Z:0.##}");
        }
    }
}
