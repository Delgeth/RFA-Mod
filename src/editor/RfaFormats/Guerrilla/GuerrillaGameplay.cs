using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's gameplay properties for a world ported from it. A destructible object
/// names an entry of gameplay_properties.xtbl (its gameplay_props): its flags (shadows, shown on the
/// map, a plume of smoke when destroyed, regrown when streamed in, kept from anchoring, a building,
/// invulnerable, ...), importance, team, salvage, control and how much of it must fall to count as
/// destroyed. Both games have the table, and Armageddon's still names most of Guerrilla's entries, but
/// none of them with Guerrilla's settings (Armageddon took the shadows off nearly all and kept many
/// props from anchoring), and it lacks 121 that Guerrilla's world uses.
///
/// So the table is Armageddon's own with Guerrilla's settings added: each Guerrilla entry the world's
/// objects name is converted to Armageddon's fields (its flags but force_dynamic and
/// child_gives_control, which Armageddon has no bit for; its other fields as they are; salvage_chance
/// 1, as every one of Armageddon's entries has it; its LOD_Properties and value left out, Armageddon's
/// entries having neither); where an Armageddon entry has the same settings the object names that
/// entry, else a new entry "rfg_" and Guerrilla's name (one for each distinct setting, after the
/// most used of its names). Armageddon's own entries stay as they are, so its own levels are as
/// before.
///
/// The game reads the table at start-up from its table cache and keeps the names in a pool of 0x6000
/// bytes (each name taking its length and 2 or 3 more), which Armageddon's own nearly fill; the
/// guerrilla_world mod hands the game this table there and gives the pool <see cref="NamePool"/> bytes.
/// Its name lookup holds 2,048 names. Without the mod the game keeps its own table, and a name it
/// doesn't have is its Default entry.
/// </summary>
public static class GuerrillaGameplay
{
    /// <summary>The room the guerrilla_world mod gives the game's gameplay property names, in bytes.</summary>
    public const int NamePool = 0x10000;

    /// <summary>How many names the game's lookup holds.</summary>
    const int LookupRoom = 2048;

    /// <summary>The flag names Armageddon's chunk_flags know (its name table, 0x24577B0), in its order.</summary>
    static readonly string[] ArmageddonFlags =
    [
        "casts_shadows", "regenerate", "show_on_map", "prevent_anchor", "disable_collapse_effect", "casts_drop_shadows", "regrow_on_stream",
        "inherit_damage_pct", "invulnerable", "plume_on_death", "one_of_many", "mining", "supply_crate", "touch_terrain", "kiosk", "propaganda",
        "no_cover", "building", "no_physics", "no_melt_in_magma", "light_as_vehicle", "show_red_reticule",
    ];

    /// <summary>Armageddon's entries' fields after the flags, in its order (salvage_chance comes after _Editor).</summary>
    static readonly string[] Fields = ["importance", "team", "salvage_material", "salvage_num_pieces", "control", "game_destroyed_pct", "fully_destroyed_pct"];

    /// <summary>
    /// The table (Armageddon's gameplay_properties.xtbl with Guerrilla's settings added), each of
    /// <paramref name="used"/> (Guerrilla's names) to the entry its objects name now, how many entries
    /// were added, and the names' room in the game's pool.
    /// </summary>
    public sealed record Table(byte[] Text, IReadOnlyDictionary<string, string> EntryOf, int Added, int PoolBytes);

    /// <summary>Armageddon's table (from its misc.vpp_pc) with the settings of Guerrilla's entries (from its misc.vpp_pc) that <paramref name="used"/> names.</summary>
    public static Table Build(Packfile armageddonMisc, Packfile guerrillaMisc, IEnumerable<string> used)
    {
        string Text(Packfile archive) =>
            Encoding.Latin1.GetString(archive.Read(archive.Find("gameplay_properties.xtbl") ?? throw new InvalidDataException($"no gameplay_properties.xtbl in {archive.Name}")));
        string ours = Text(armageddonMisc);
        var armageddon = Entries(ours);
        var guerrilla = Entries(Text(guerrillaMisc)).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var taken = armageddon.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bySettings = new Dictionary<string, string>();
        foreach (var e in armageddon) bySettings.TryAdd(e.Key, e.Name);

        // Each name's settings in Armageddon's fields, and how many objects name it.
        var counts = used.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var entryOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var added = new Dictionary<string, Entry>();  // by settings
        foreach (var (name, count) in counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!guerrilla.TryGetValue(name, out var theirs)) continue;  // not in Guerrilla's table either: the game's Default
            var converted = theirs.ForArmageddon();
            if (bySettings.TryGetValue(converted.Key, out string? same))
            {
                entryOf[name] = armageddon.Any(e => e.Key == converted.Key && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? name : same;
                continue;
            }
            if (!added.TryGetValue(converted.Key, out var entry))
            {
                string newName = "rfg_" + theirs.Name;
                if (!taken.Add(newName)) throw new InvalidDataException($"Armageddon's gameplay_properties.xtbl already has an entry {newName}");
                added[converted.Key] = entry = converted with { Name = newName };
            }
            entryOf[name] = entry.Name;
        }

        int pool = taken.Sum(NameRoom);
        if (pool > NamePool) throw new InvalidDataException($"the gameplay property names take {pool} bytes; the mod gives the game {NamePool}");
        if (taken.Count >= LookupRoom) throw new InvalidDataException($"{taken.Count} gameplay property names; the game's lookup holds {LookupRoom}");

        // Added as the table's last entries, before its closing tag, written as Armageddon's are.
        var text = new StringBuilder();
        foreach (var e in added.Values)
        {
            text.Append($"\t<Gameplay>\n\t\t<Name>{e.Name}</Name>\n\t\t<chunk_flags>\n");
            foreach (string flag in e.Flags) text.Append($"\t\t\t<Flag>{flag}</Flag>\n");
            text.Append("\t\t\t</chunk_flags>\n");
            foreach (string field in Fields) text.Append($"\t\t<{field}>{e.Values[field]}</{field}>\n");
            text.Append($"\t\t<_Editor>\n\t\t\t<Category>{e.Category}</Category>\n\t\t\t</_Editor>\n\t\t<salvage_chance>{e.SalvageChance}</salvage_chance>\n\t\t</Gameplay>\n");
        }
        int end = ours.IndexOf("</Table>", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Armageddon's gameplay_properties.xtbl has no table");
        int at = ours.LastIndexOf('\n', end) + 1;
        string newline = ours.Contains("\r\n") ? "\r\n" : "\n";
        byte[] table = Encoding.Latin1.GetBytes(ours[..at] + text.ToString().Replace("\n", newline) + ours[at..]);
        return new Table(table, entryOf, added.Count, pool);
    }

    /// <summary>What a name takes in the game's pool: its length and its end, rounded up to even, and 1 or 2 more.</summary>
    static int NameRoom(string name) => name.Length + 1 + (name.Length % 2 == 0 ? 1 : 2);

    /// <summary>An entry: its name, flags (in its order), its other fields as written, its editor category and salvage_chance (null where it has none).</summary>
    sealed record Entry(string Name, IReadOnlyList<string> Flags, IReadOnlyDictionary<string, string> Values, string Category, string? SalvageChance)
    {
        /// <summary>Guerrilla's entry in Armageddon's fields: the flags Armageddon knows, salvage_chance 1.</summary>
        public Entry ForArmageddon() =>
            this with { Flags = [.. Flags.Where(f => ArmageddonFlags.Contains(f, StringComparer.OrdinalIgnoreCase))], SalvageChance = "1.0" };

        /// <summary>Its settings, to compare by: the flags as a set, the words in lower case, the numbers as numbers.</summary>
        public string Key => string.Join("|",
            [string.Join(",", Flags.Select(f => f.ToLowerInvariant()).Distinct().Order()),
             .. Fields.Select(f => Values.TryGetValue(f, out string? v) ? Normal(v) : "?"),
             SalvageChance is null ? "?" : Normal(SalvageChance)]);

        static string Normal(string value) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number.ToString("R", CultureInfo.InvariantCulture) : value.Trim().ToLowerInvariant();
    }

    /// <summary>A table's entries (its &lt;Table&gt;'s &lt;Gameplay&gt; elements).</summary>
    static List<Entry> Entries(string text)
    {
        string table = Regex.Match(text, @"<Table>(.*?)</Table>", RegexOptions.Singleline) is { Success: true } t ? t.Groups[1].Value
            : throw new InvalidDataException("a gameplay_properties.xtbl without a table");
        var entries = new List<Entry>();
        foreach (Match m in Regex.Matches(table, @"<Gameplay>(.*?)</Gameplay>", RegexOptions.Singleline))
        {
            string body = m.Groups[1].Value;
            string? Field(string name) => Regex.Match(body, $@"<{name}>([^<]*)</{name}>") is { Success: true } f ? f.Groups[1].Value.Trim() : null;
            string name = Field("Name") ?? throw new InvalidDataException("a gameplay property without a name");
            var flags = Regex.Match(body, @"<chunk_flags>(.*?)</chunk_flags>", RegexOptions.Singleline) is { Success: true } c
                ? Regex.Matches(c.Groups[1].Value, @"<Flag>([^<]*)</Flag>").Select(f => f.Groups[1].Value.Trim()).Where(f => f.Length > 0).ToList() : [];
            var values = new Dictionary<string, string>();
            foreach (string field in Fields)
                values[field] = Field(field) ?? throw new InvalidDataException($"gameplay property {name} has no {field}");
            string category = Regex.Match(body, @"<_Editor>\s*<Category>([^<]*)</Category>") is { Success: true } k ? k.Groups[1].Value.Trim() : "Entries";
            entries.Add(new Entry(name, flags, values, category, Field("salvage_chance")));
        }
        return entries;
    }
}
