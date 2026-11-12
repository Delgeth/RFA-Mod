using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Levels;

/// <summary>
/// A level's spawn resources: what the game sets aside (loads) for it to spawn, an entry of
/// spawn_resource.xtbl that the level's row names (resource_reservation: Ruin's wrecking_crew_maps,
/// Infestation's mp_levels, the story's missions). Its characters (npc_list: character.xtbl's
/// Character names), vehicles (vehicle_list: vehicles.xtbl's), exos (exo_list: exo.xtbl's), the
/// player's look (Player_Override: a Character), how many of each side's people the world may have
/// about (the populations), and rotate_slots. A level can only spawn what its entry holds.
/// </summary>
public sealed class SpawnResource
{
    public string Name { get; set; } = "";
    public List<string> Characters { get; } = [];
    public List<string> Vehicles { get; } = [];
    public List<string> Exos { get; } = [];
    public string? PlayerLook { get; set; }
    public bool? RotateSlots { get; set; }
    /// <summary>edf, gue, mar, civ, cul: each side's population, where the entry gives it.</summary>
    public Dictionary<string, string> Populations { get; } = [];

    public static readonly string[] Sides = ["edf", "gue", "mar", "civ", "cul"];
    const string Row = "spawn_resource_entry";

    /// <summary>Every entry's name, in the table's order.</summary>
    public static List<string> Names(string table) => LevelFiles.Keys(table, Row, "Name");

    /// <summary>An entry of the table by its name; null if it hasn't one.</summary>
    public static SpawnResource? Read(string table, string name)
    {
        string? row = RowText(table, name)?.Value;
        if (row == null) return null;
        var entry = new SpawnResource { Name = name };
        entry.Characters.AddRange(Values(row, "npc_list", "npc_group"));
        entry.Vehicles.AddRange(Values(row, "vehicle_list", "vehicle_group"));
        entry.Exos.AddRange(Values(row, "exo_list", "exo_info"));
        Match look = Regex.Match(row, @"<Player_Override>([^<]*)</Player_Override>");
        if (look.Success && look.Groups[1].Value.Trim().Length > 0) entry.PlayerLook = look.Groups[1].Value.Trim();
        Match rotate = Regex.Match(row, @"<rotate_slots>([^<]*)</rotate_slots>");
        if (rotate.Success) entry.RotateSlots = rotate.Groups[1].Value.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        foreach (string side in Sides)
            if (Regex.Match(row, $@"<{side}_population>([^<]*)</{side}_population>") is { Success: true } m) entry.Populations[side] = m.Groups[1].Value.Trim();
        return entry;
    }

    /// <summary>
    /// The table with this entry in it: the entry of its name changed (what this class knows of
    /// rewritten, the rest of it kept, such as its editor category), or, if it hasn't one, added
    /// after its last entry as a copy of <paramref name="like"/>'s (or a new one) changed so.
    /// </summary>
    public string WriteTo(string table, string? like = null)
    {
        Match? found = RowText(table, Name);
        string row;
        if (found != null) row = found.Value;
        else
        {
            row = (like != null ? RowText(table, like)?.Value : null)
                  ?? $"\t<{Row}>\r\n\t\t<Name></Name>\r\n\t\t<_Editor>\r\n\t\t\t<Category>Entries</Category>\r\n\t\t\t</_Editor>\r\n\t\t</{Row}>\r\n";
            row = Regex.Replace(row, @"<Name>[^<]*</Name>", $"<Name>{SecurityElement.Escape(Name)}</Name>");
        }
        // What this class writes, out; then written again before the entry's end.
        foreach (string list in new[] { "npc_list", "vehicle_list", "exo_list" })
            row = Regex.Replace(row, $@"[ \t]*<{list}>.*?</{list}>\r?\n?", "", RegexOptions.Singleline);
        row = Regex.Replace(row, @"[ \t]*<Player_Override>[^<]*</Player_Override>\r?\n?", "");
        row = Regex.Replace(row, @"[ \t]*<rotate_slots>[^<]*</rotate_slots>\r?\n?", "");
        foreach (string side in Sides) row = Regex.Replace(row, $@"[ \t]*<{side}_population>[^<]*</{side}_population>\r?\n?", "");
        var add = new StringBuilder();
        foreach (string side in Sides)
            if (Populations.TryGetValue(side, out string? value) && value.Trim().Length > 0) add.Append($"\t\t<{side}_population>{SecurityElement.Escape(value.Trim())}</{side}_population>\r\n");
        if (RotateSlots is bool r) add.Append($"\t\t<rotate_slots>{(r ? "True" : "False")}</rotate_slots>\r\n");
        if (Exos.Count > 0)
        {
            add.Append("\t\t<exo_list>\r\n");
            foreach (string exo in Exos) add.Append($"\t\t\t<exo_info>{SecurityElement.Escape(exo)}</exo_info>\r\n");
            add.Append("\t\t\t</exo_list>\r\n");
        }
        if (Characters.Count > 0)
        {
            add.Append("\t\t<npc_list>\r\n");
            foreach (string c in Characters) add.Append($"\t\t\t<npc_info>\r\n\t\t\t\t<npc_group>{SecurityElement.Escape(c)}</npc_group>\r\n\t\t\t\t</npc_info>\r\n");
            add.Append("\t\t\t</npc_list>\r\n");
        }
        if (Vehicles.Count > 0)
        {
            add.Append("\t\t<vehicle_list>\r\n");
            foreach (string v in Vehicles) add.Append($"\t\t\t<vehicle_info>\r\n\t\t\t\t<vehicle_group>{SecurityElement.Escape(v)}</vehicle_group>\r\n\t\t\t\t</vehicle_info>\r\n");
            add.Append("\t\t\t</vehicle_list>\r\n");
        }
        if (PlayerLook is { Length: > 0 } look) add.Append($"\t\t<Player_Override>{SecurityElement.Escape(look)}</Player_Override>\r\n");
        int end = row.LastIndexOf($"</{Row}>", StringComparison.Ordinal);
        int lineStart = row.LastIndexOf('\n', end) + 1;  // (before the closing tag's own indent)
        row = row.Insert(lineStart, add.ToString());
        if (!row.EndsWith('\n')) row += "\r\n";
        if (found != null) return table.Remove(found.Index, found.Length).Insert(found.Index, row);
        Match last = Rows().Matches(table).Last();
        return table.Insert(last.Index + last.Length, row);
    }

    static Regex Rows() => new($@"[ \t]*<{Row}>.*?</{Row}>\r?\n?", RegexOptions.Singleline);

    static Match? RowText(string table, string name) =>
        Rows().Matches(table).FirstOrDefault(m => Regex.Match(m.Value, @"<Name>([^<]*)</Name>").Groups[1].Value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<string> Values(string row, string list, string tag)
    {
        Match block = Regex.Match(row, $@"<{list}>(.*?)</{list}>", RegexOptions.Singleline);
        if (!block.Success) yield break;
        foreach (Match m in Regex.Matches(block.Groups[1].Value, $@"<{tag}>([^<]*)</{tag}>"))
            if (m.Groups[1].Value.Trim() is { Length: > 0 } v) yield return v;
    }

    /// <summary>The names of a table's rows of one element (e.g. character.xtbl's Character), for the choices.</summary>
    public static List<string> RowNames(string table, string rowTag) => LevelFiles.Keys(table, rowTag, "Name").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
}
