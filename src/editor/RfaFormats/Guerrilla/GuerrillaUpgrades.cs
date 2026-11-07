using System.Globalization;
using System.Text;
using RfaFormats.Interface;
using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's upgrades (its upgrades.xtbl: a ladder of levels for each, bought with salvage), with the
/// nine backpacks Re-Mars-tered sells as upgrades (250 salvage each, from the start), written for the guerrilla_gear
/// mod as mods\guerrilla_gear\upgrades.txt: the mod shows them in an F7 window, spends Armageddon's salvage on them,
/// keeps what is bought, and applies what it can.
///
/// Guerrilla's rules, kept: each upgrade's first level is the one you start with (owned, free; a count's first is
/// its starting count: two remote charges), the others are bought in order. A level is for sale from the start
/// (Available), or once something unlocks it: a district liberated (District_Liberated), a number of guerrilla
/// activities done (Activity_Count), or another level (its Also_Unlock and Also_Unlock_Level: when that level is
/// bought, Unlock_Action Purchased, or when it is itself unlocked, Unlocked). Buying the grinder opens its faster
/// and explosive upgrades; buying the rocket launcher opens its heat seeking, which opens its multiple rockets.
/// The world has no districts to liberate or activities yet, so those conditions are written as notes and not
/// held to; the chains between upgrades are (each level's "needs").
///
/// Left out: its placeholders for the story (Guns of Tharsis, Eos's missions, the "pistol"), its reconstructor (never
/// for sale in its campaign) and its bare "sledgehammer" entry (Mason has his hammer from the start).
///
/// The file, a line each, tab-separated:
///   costs     Easy Medium Hard Insane   (Guerrilla's MENU_UPGRADE_COST_MULTIPLIER, difficulty.xtbl)
///   upgrade   name  shown name  type  weapon (ours, or -)  effect (weapon, charges, hammer:&lt;weapon&gt;, backpack:&lt;id&gt;, none)
///   level     cost  value  shown name  description  needs (-, or upgrade:level)  Guerrilla's condition (or -)
/// </summary>
public static class GuerrillaUpgrades
{
    /// <summary>The file this writes, in the guerrilla_gear mod's folder.</summary>
    public const string ListFile = "upgrades.txt";

    static readonly HashSet<string> LeftOut = new(StringComparer.OrdinalIgnoreCase)
    {
        "guns_of_tharsis_unlock1", "guns_of_tharsis_unlock2", "guns_of_tharsis_complete", "eos_real_missions", "pistol", "reconstructor", "sledgehammer",
    };

    /// <summary>Re-Mars-tered's nine backpack upgrades: their mp_backpacks.xtbl names, in its Unique_ID order (1 Thrust ... 9 Tremor; 0 is the Jetpack).</summary>
    static readonly string[] Backpacks = ["thrust", "fleetfoot", "stealth", "vision", "commando", "shockwave", "heal", "rhino", "tremor"];
    const int BackpackCost = 250;

    sealed record Level(XtblElement Element, string Upgrade, int Index)
    {
        public bool Available => !string.Equals(Element.ValueOf("Available")?.Trim(), "False", StringComparison.OrdinalIgnoreCase);
        public string? District => Element.ValueOf("District_Liberated")?.Trim() is { Length: > 0 } d ? d : null;
        public int Activities => int.TryParse(Element.ValueOf("Activity_Count")?.Trim(), out int n) ? n : 0;
        public string? AlsoUnlock => Element.ValueOf("Also_Unlock")?.Trim() is { Length: > 0 } a ? a : null;
        public int AlsoUnlockLevel => int.TryParse(Element.ValueOf("Also_Unlock_Level")?.Trim(), out int n) ? n : 1;
        public bool OnPurchase => string.Equals(Element.ValueOf("Unlock_Action")?.Trim(), "Purchased", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes the list; <paramref name="weaponName"/> gives our name of a Guerrilla weapon the build made (null: not made). Returns a line for the builder's log.</summary>
    public static string Write(Packfile guerrillaMisc, Func<string, string?> weaponName, string gearFolder)
    {
        var strings = new Dictionary<uint, string>();
        foreach (var entry in guerrillaMisc.Entries.Where(e => e.Name.EndsWith("_us.le_strings", StringComparison.OrdinalIgnoreCase)))
            foreach (var (hash, text) in GuerrillaHud.ReadStrings(guerrillaMisc.Read(entry))) strings.TryAdd(hash, text);
        // A tag's text in Guerrilla's English strings (null: none).
        string? Text(string? tag) =>
            !string.IsNullOrWhiteSpace(tag) && strings.TryGetValue(VintDocument.Hash(tag.Trim().ToLowerInvariant()), out var text) && Clean(text).Length > 0 ? Clean(text) : null;

        var lines = new List<string> { "# Guerrilla's upgrades for the guerrilla_gear mod (written by editor\\GuerrillaBuilder.exe with the weapons)" };
        // Costs by difficulty, as Guerrilla scales them.
        var difficulty = XtblFile.FromBytes(guerrillaMisc.Read(guerrillaMisc.Find("difficulty.xtbl") ?? throw new InvalidDataException("no difficulty.xtbl in Guerrilla's misc.vpp_pc")));
        var costs = Descendants(difficulty.Table ?? new XtblElement("Table")).FirstOrDefault(e => e.Name.Equals("Item", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.ValueOf("Name")?.Trim(), "MENU_UPGRADE_COST_MULTIPLIER", StringComparison.OrdinalIgnoreCase));
        lines.Add(string.Join("\t", new[] { "costs" }.Concat(new[] { "Easy", "Medium", "Hard", "Insane" }.Select(l => costs?.ValueOf(l)?.Trim() ?? "1.0"))));

        var upgrades = XtblFile.FromBytes(guerrillaMisc.Read(guerrillaMisc.Find("upgrades.xtbl") ?? throw new InvalidDataException("no upgrades.xtbl in Guerrilla's misc.vpp_pc")));
        var levels = upgrades.Entries.ToDictionary(XtblFile.EntryName,
            u => (u.Element("Levels")?.Elements ?? []).Select((l, i) => new Level(l, XtblFile.EntryName(u), i)).ToList(), StringComparer.OrdinalIgnoreCase);
        // What unlocks each level (Guerrilla's Also_Unlock: upgrade:level → the level that names it).
        var unlockedBy = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels.Values.SelectMany(l => l))
            if (level.AlsoUnlock != null) unlockedBy.TryAdd($"{level.AlsoUnlock}:{level.AlsoUnlockLevel}", level);
        // The purchase a level waits for: the level that unlocks it when bought, or what that one waits for when it
        // unlocks it on being unlocked itself; and Guerrilla's other conditions along the way, as notes.
        (string Needs, List<string> Notes) Conditions(Level level, int depth = 0)
        {
            var notes = new List<string>();
            if (level.Available || depth > 8) return ("-", notes);
            if (level.District != null) notes.Add($"after liberating {level.District}");
            if (level.Activities > 0) notes.Add($"after {level.Activities} guerrilla activities");
            if (!unlockedBy.TryGetValue($"{level.Upgrade}:{level.Index}", out var source))
            {
                if (notes.Count == 0) notes.Add("a story mission unlocks it");
                return ("-", notes);
            }
            if (source.OnPurchase) return ($"{source.Upgrade}:{source.Index}", notes);
            var (needs, more) = Conditions(source, depth + 1);
            return (needs, [.. notes, .. more]);
        }

        int written = 0;
        foreach (var u in upgrades.Entries)
        {
            string name = XtblFile.EntryName(u);
            if (LeftOut.Contains(name)) continue;
            string type = u.ValueOf("Type")?.Trim() ?? "General";
            string? guerrillaWeapon = u.ValueOf("Weapon")?.Trim() is { Length: > 0 } w ? w : null;
            string ours = guerrillaWeapon != null ? weaponName(guerrillaWeapon) ?? "-" : "-";
            string effect = name switch
            {
                "remote_charge_count" => "charges",
                "sledgehammer1" => weaponName("sledge_upgrade_1") is { } hammer1 ? "hammer:" + hammer1 : "none",
                "sledgehammer2" => weaponName("sledge_upgrade_2") is { } hammer2 ? "hammer:" + hammer2 : "none",
                "jetpack" => "backpack:0",
                _ when type.Equals("Weapon", StringComparison.OrdinalIgnoreCase) && ours != "-" => "weapon",
                _ => "none",
            };
            // Its name: Guerrilla's Display_Name, unless that is a level's description (the jetpack's, the personnel
            // detector's), then its first level's name.
            string display = u.ValueOf("Display_Name")?.Trim() ?? "";
            string shownName = levels[name].Any(l => string.Equals(l.Element.ValueOf("Description")?.Trim(), display, StringComparison.OrdinalIgnoreCase)) ||
                               Text(display) == null
                ? levels[name].Select(l => Text(l.Element.ValueOf("Upgrade_Name"))).FirstOrDefault(t => t != null) ?? name
                : Text(display)!;
            lines.Add(string.Join("\t", "upgrade", name, shownName, type, ours, effect));
            foreach (var level in levels[name])
            {
                var (needs, notes) = Conditions(level);
                lines.Add(string.Join("\t", "level", Int(level.Element, "Cost"), Int(level.Element, "Value"), Text(level.Element.ValueOf("Upgrade_Name")) ?? "",
                                      Text(level.Element.ValueOf("Description")) ?? "", needs, notes.Count > 0 ? "Guerrilla: " + string.Join(", ", notes) : "-"));
            }
            written++;
        }
        // Re-Mars-tered's backpacks: their names and hints from Guerrilla's mp_backpacks.xtbl.
        var packs = guerrillaMisc.Find("mp_backpacks.xtbl") is { } file ? XtblFile.FromBytes(guerrillaMisc.Read(file)).Entries.ToList() : [];
        for (int i = 0; i < Backpacks.Length; i++)
        {
            var pack = packs.FirstOrDefault(p => XtblFile.EntryName(p).Equals(Backpacks[i], StringComparison.OrdinalIgnoreCase));
            string shown = Text(pack?.ValueOf("display_name")) ?? Backpacks[i].ToUpperInvariant();
            string about = Text(pack?.ValueOf("hint_text")) ?? "";
            lines.Add(string.Join("\t", "upgrade", "backpack_" + Backpacks[i], shown, "Backpack", "-", $"backpack:{i + 1}"));
            lines.Add(string.Join("\t", "level", "0", "0", "", "", "-", "-"));
            lines.Add(string.Join("\t", "level", BackpackCost.ToString(CultureInfo.InvariantCulture), "1", shown, about, "-", "Re-Mars-tered: for sale from the start"));
            written++;
        }
        Directory.CreateDirectory(gearFolder);
        File.WriteAllLines(Path.Combine(gearFolder, ListFile), lines, new UTF8Encoding(false));
        return $"upgrades: {written} (Guerrilla's, with Re-Mars-tered's {Backpacks.Length} backpacks) in {Path.Combine(gearFolder, ListFile)}";
    }

    /// <summary>The file <see cref="WriteBackpacks"/> writes, in the guerrilla_gear mod's folder.</summary>
    public const string BackpackFile = "backpacks.txt";

    /// <summary>
    /// Guerrilla's ten backpacks' numbers (its mp_backpacks.xtbl, which Re-Mars-tered's single player uses too),
    /// for the guerrilla_gear mod: a line "pack &lt;Unique_ID&gt; &lt;Name&gt;" each, then each of its values as
    /// "&lt;path&gt;\t&lt;value&gt;", the path its fields' names joined by dots (activatable.uses_fuel.fuel_use_per_second).
    /// Its editor notes and effects lists are left out; the jetpack's numbers in Guerrilla's code
    /// (player_update_jetpack: Jetpack_acceleration 18, the climb it tapers to 5 m/s, Jetpack_charge_rate 0.1,
    /// the charge ×2 with the Jetpack Recharge upgrade) are the mod's own.
    /// </summary>
    public static string WriteBackpacks(Packfile guerrillaMisc, string gearFolder)
    {
        var file = guerrillaMisc.Find("mp_backpacks.xtbl") ?? throw new InvalidDataException("no mp_backpacks.xtbl in Guerrilla's misc.vpp_pc");
        var lines = new List<string> { "# Guerrilla's backpacks for the guerrilla_gear mod (written by editor\\GuerrillaBuilder.exe with the weapons)" };
        int packs = 0;
        foreach (var pack in XtblFile.FromBytes(guerrillaMisc.Read(file)).Entries)
        {
            string name = XtblFile.EntryName(pack);
            if (!int.TryParse(pack.ValueOf("Unique_ID")?.Trim(), out int id)) continue;
            lines.Add($"pack\t{id}\t{name}");
            Flatten(pack, "", lines);
            packs++;
        }
        Directory.CreateDirectory(gearFolder);
        File.WriteAllLines(Path.Combine(gearFolder, BackpackFile), lines, new UTF8Encoding(false));
        return $"backpacks: {packs} of Guerrilla's in {Path.Combine(gearFolder, BackpackFile)}";
    }

    static readonly HashSet<string> NotValues = new(StringComparer.OrdinalIgnoreCase) { "_Editor", "Name", "Unique_ID" };

    // Each field's own text (a field may hold text and fields both: <activatable>0.0<deactivatable>...), by its path.
    static void Flatten(XtblElement element, string path, List<string> lines)
    {
        foreach (var child in element.Elements)
        {
            if (path.Length == 0 && NotValues.Contains(child.Name)) continue;
            string at = path.Length == 0 ? child.Name : path + "." + child.Name;
            string own = Clean(XtblElement.Decode(string.Concat(child.Parts.OfType<XtblText>().Select(t => t.Raw))));
            if (own.Length > 0) lines.Add($"{at}\t{own}");
            Flatten(child, at, lines);
        }
    }

    static string Int(XtblElement level, string field) =>
        int.TryParse(level.ValueOf(field)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v.ToString(CultureInfo.InvariantCulture) : "0";

    /// <summary>A string as one field of the file: no tabs or line breaks.</summary>
    static string Clean(string text) => text.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ").Trim();

    static IEnumerable<XtblElement> Descendants(XtblElement element)
    {
        foreach (var child in element.Elements)
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
