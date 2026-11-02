using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's draw distances for a world ported from it. Guerrilla gives an object the
/// LOD class its gameplay entry names (LOD_Properties: 1_clutter_extra_small to 5_clutter_extra_lrg,
/// on 434 of its entries), and lod_properties.xtbl gives each class the distance at which the object
/// is switched off (25, 50, 100, 150 and 200 m; its Default, 0, never). Armageddon names a class on
/// the object itself (lod_props), and its classes of the same names start a fade at nearer distances
/// (15 to 80 m) lasting a second.
///
/// So the table is Armageddon's own with an entry "rfg_" and the class's name for each of Guerrilla's
/// classes the world's objects have, starting its fade at Guerrilla's distance and lasting 1 ms (as
/// Armageddon's own None: Guerrilla switches the object off at once). An object with no class, or
/// Guerrilla's 0, keeps Armageddon's None (never). The game reads this table at start-up as it does
/// the gameplay properties, so the guerrilla_world mod hands it the game the same way (see
/// <see cref="GuerrillaGameplay"/>); its name lookup holds 64.
/// </summary>
public static class GuerrillaLod
{
    /// <summary>Armageddon's class that never fades.</summary>
    public const string Never = "None";

    /// <summary>How many classes the game's lookup holds.</summary>
    const int LookupRoom = 64;

    /// <summary>The table, each of Guerrilla's classes to the entry its objects name now, and how many entries were added.</summary>
    public sealed record Table(byte[] Text, IReadOnlyDictionary<string, string> EntryOf, int Added);

    /// <summary>Armageddon's lod_properties.xtbl (from its misc.vpp_pc) with an entry for each of Guerrilla's <paramref name="classes"/> (from its misc.vpp_pc).</summary>
    public static Table Build(Packfile armageddonMisc, Packfile guerrillaMisc, IEnumerable<string> classes)
    {
        string Text(Packfile archive) =>
            Encoding.Latin1.GetString(archive.Read(archive.Find("lod_properties.xtbl") ?? throw new InvalidDataException($"no lod_properties.xtbl in {archive.Name}")));
        string ours = Text(armageddonMisc);
        var taken = Names(ours).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(Never)) throw new InvalidDataException($"Armageddon's lod_properties.xtbl has no {Never}");
        var distances = Distances(Text(guerrillaMisc));

        var entryOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = new StringBuilder();
        foreach (string name in classes.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!distances.TryGetValue(name, out float distance)) throw new InvalidDataException($"Guerrilla's lod_properties.xtbl has no {name}");
            if (distance <= 0)
            {
                entryOf[name] = Never;
                continue;
            }
            string entry = "rfg_" + name;
            if (!taken.Add(entry)) throw new InvalidDataException($"Armageddon's lod_properties.xtbl already has an entry {entry}");
            entryOf[name] = entry;
            text.Append($"\t<LOD>\n\t\t<Name>{entry}</Name>\n\t\t<lod_fade_duration>1</lod_fade_duration>\n\t\t<_Editor>\n\t\t\t<Category>Entries</Category>\n\t\t\t</_Editor>\n")
                .Append($"\t\t<lod_start_fade_distance>{distance.ToString("0.###", CultureInfo.InvariantCulture)}</lod_start_fade_distance>\n\t\t</LOD>\n");
        }
        if (taken.Count > LookupRoom) throw new InvalidDataException($"{taken.Count} LOD classes; the game's lookup holds {LookupRoom}");

        // Added as the table's last entries, before its closing tag, written as Armageddon's are.
        int end = ours.IndexOf("</Table>", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Armageddon's lod_properties.xtbl has no table");
        int at = ours.LastIndexOf('\n', end) + 1;
        string newline = ours.Contains("\r\n") ? "\r\n" : "\n";
        return new Table(Encoding.Latin1.GetBytes(ours[..at] + text.ToString().Replace("\n", newline) + ours[at..]), entryOf, entryOf.Values.Count(e => e != Never));
    }

    static IEnumerable<string> Names(string text) =>
        Regex.Matches(Body(text), @"<LOD>\s*<Name>([^<]*)</Name>").Select(m => m.Groups[1].Value.Trim());

    /// <summary>Guerrilla's classes and their off distances.</summary>
    static Dictionary<string, float> Distances(string text)
    {
        var distances = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(Body(text), @"<LOD>(.*?)</LOD>", RegexOptions.Singleline))
        {
            string name = Regex.Match(m.Groups[1].Value, @"<Name>([^<]*)</Name>").Groups[1].Value.Trim();
            string off = Regex.Match(m.Groups[1].Value, @"<lod_off_distance>([^<]*)</lod_off_distance>").Groups[1].Value.Trim();
            if (name.Length > 0 && float.TryParse(off, NumberStyles.Float, CultureInfo.InvariantCulture, out float distance)) distances.TryAdd(name, distance);
        }
        return distances;
    }

    static string Body(string text) =>
        Regex.Match(text, @"<Table>(.*?)</Table>", RegexOptions.Singleline) is { Success: true } t ? t.Groups[1].Value
            : throw new InvalidDataException("a lod_properties.xtbl without a table");
}
