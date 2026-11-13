using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RfaFormats.Guerrilla;

/// <summary>
/// What Guerrilla's pieces play as they break: each effect material's event effects (rfg_effect_materials.xtbl:
/// a piece hitting, sliding, rolling or shifting on another material; a building's load shifting, stress, a piece
/// detaching, steel groaning, glass shattering, a collapse), an effect for each size of the event (small, medium,
/// large) and kind of piece (solid, sheet, pole, fence), most of them a sound alone (the DES_ cues).
///
/// Armageddon has the same table with the same materials, its own effects in it and a destruction sound of its
/// own beside them, and it reads the table once for every level. So Guerrilla's are not put into that table (that
/// would change Armageddon's own levels): the world build adds the effects to the world's effect tables (with
/// their sounds, see <see cref="GuerrillaEffectTables"/>) and writes Guerrilla's rows into a file of the
/// guerrilla_world mod's (<see cref="FileName"/>), which answers the game's question "which effect for this event"
/// from them while the Guerrilla world is loaded, and leaves Armageddon's destruction sound out there.
///
/// The file, a line each: "material &lt;name&gt;", then that material's rows "event &lt;kind&gt; &lt;type&gt;
/// &lt;other material or -&gt; &lt;small&gt; &lt;medium&gt; &lt;large&gt;" (an effect's name in the world's
/// tables, or - for none), the kind and type as the game numbers them (<see cref="Kinds"/>, <see cref="Types"/>).
/// A material of Guerrilla's without rows is written too: its events play nothing, as in Guerrilla.
/// Not in it: Guerrilla's "explode" rows (Small Bang, ...: no event of Armageddon's asks for them).
/// </summary>
public static class GuerrillaMaterialEvents
{
    /// <summary>The file in the guerrilla_world mod's folder.</summary>
    public const string FileName = "material_events.txt";

    const string Table = "rfg_effect_materials.xtbl";

    /// <summary>The event kinds by the game's numbers (its list at 0x10FDBA4; 6 and 7 are the melee and bullet groups').</summary>
    static readonly string[] Kinds = ["load_balance", "stress", "detach", "groan", "shatter", "collapse", "melee", "bullet", "impact", "shift", "roll", "slide"];

    /// <summary>The kinds of piece by the game's numbers.</summary>
    static readonly string[] Types = ["Solid", "Sheet", "Pole", "Fence", "Catwalk"];

    sealed record Row(int Kind, int Type, string Other, string?[] Effects);

    /// <summary>Guerrilla's materials in its table's order, each with its rows (the effects under Guerrilla's own names).</summary>
    static List<(string Material, List<Row> Rows)> Read(Packfile guerrillaMisc)
    {
        string text = Encoding.Latin1.GetString(guerrillaMisc.Read(guerrillaMisc.Find(Table) ?? throw new InvalidDataException($"no {Table} in Guerrilla's misc.vpp_pc")));
        var table = XElement.Parse(Regex.Match(text, @"<Table>.*?</Table>", RegexOptions.Singleline).Value);
        var materials = new List<(string, List<Row>)>();
        foreach (var material in table.Elements("Material"))
        {
            string name = material.Element("Name")?.Value.Trim() ?? "";
            if (name.Length == 0) continue;
            var rows = new List<Row>();
            foreach (var container in material.Element("event_effects")?.Elements() ?? [])
            {
                string tag = container.Name.LocalName;
                int kind = Array.IndexOf(Kinds, tag.EndsWith("_container", StringComparison.Ordinal) ? tag[..^"_container".Length] : "");
                if (kind < 0) continue;
                // The rows under Default, and those under the event's own name (against another material where one is named).
                foreach (var row in (container.Element("Default")?.Elements() ?? []).Concat(container.Element(Kinds[kind])?.Elements() ?? []))
                {
                    if (row.Name.LocalName is not ("Effects" or "Effect")) continue;
                    int type = Array.FindIndex(Types, t => t.Equals(row.Element("Type")?.Value.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (type < 0) continue;
                    string? Size(string size) => row.Element(size)?.Value.Trim() is { Length: > 0 } effect ? effect : null;
                    rows.Add(new Row(kind, type, row.Element("Material")?.Value.Trim() ?? "", [Size("Small"), Size("Medium"), Size("Large")]));
                }
            }
            materials.Add((name, rows));
        }
        return materials;
    }

    /// <summary>The effects (Guerrilla's effects.xtbl names) the events name: the world build adds them to the world's effect tables.</summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaMisc) =>
        Read(guerrillaMisc).SelectMany(m => m.Rows).SelectMany(r => r.Effects).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Writes the mod's file; <paramref name="worldEffects"/> are the names in the world's built effects.xtbl (an
    /// effect that isn't among them, one with neither a visual nor a sound in Guerrilla's own table, is written as
    /// none). Returns a line for the log.
    /// </summary>
    public static string Write(Packfile guerrillaMisc, IReadOnlySet<string> worldEffects, string modFolder)
    {
        var lines = new List<string>();
        int rows = 0, effects = 0, none = 0;
        foreach (var (material, materialRows) in Read(guerrillaMisc))
        {
            lines.Add($"material\t{material}");
            foreach (var row in materialRows)
            {
                var names = row.Effects.Select(e =>
                {
                    if (e == null) return "-";
                    string ours = GuerrillaEffectTables.Added(e);
                    if (worldEffects.Contains(ours)) { effects++; return ours; }
                    none++;
                    return "-";
                });
                lines.Add($"event\t{row.Kind}\t{row.Type}\t{(row.Other.Length > 0 ? row.Other : "-")}\t{string.Join("\t", names)}");
                rows++;
            }
        }
        Directory.CreateDirectory(modFolder);
        File.WriteAllLines(Path.Combine(modFolder, FileName), lines);
        return $"{lines.Count - rows} of Guerrilla's effect materials, {rows} event rows naming {effects} effects of the world's tables" +
               (none > 0 ? $" ({none} named effects are nothing in Guerrilla's own table: neither a visual nor a sound)" : "") +
               $" (mods\\guerrilla_world\\{FileName})";
    }
}
