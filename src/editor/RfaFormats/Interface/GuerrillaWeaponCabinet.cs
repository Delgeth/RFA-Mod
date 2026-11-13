using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RfaFormats.Tables;

namespace RfaFormats.Interface;

/// <summary>
/// What the guerrilla_hud mod's weapon cabinet needs from the player's copy of Guerrilla, in one file,
/// cabinet.txt, next to our scripts. Guerrilla's cabinet (hud_ui_weapon_cabinet_*) is opened at a safehouse's
/// weapon locker (an object_action_node "Player Open Weapon Cabinet", in safehouses.txt) and at every ammo crate;
/// it lists the weapons the player has found or bought, and the player puts one into each of three slots. Its rules
/// read each weapon's weapons.xtbl entry: its class (the sledgehammer is never listed), its flags (non-inventory:
/// never listed; never in cabinet: its ammo isn't touched), its ammo type (melee: no ammo), Max_Rounds (and
/// Max_Rounds_Upgrade, with its "more ammo" upgrade), Ammo_Box_Restock (what a crate gives), its picture, its
/// team (the logo shown) and its name (items_inventory.xtbl's Display_Name, weapons_us.le_strings). A crate's
/// restock is scaled by the district's morale and the difficulty's "player ammo crates" (difficulty.xtbl).
/// Lines, '|' between fields:
///   weapon|guerrilla name|shown name|picture|team (-1 none, 0 Guerrilla, 1 EDF, 2 Ultor, 3 Marauder)|class|ammo type|
///          flags (non_inventory, never_in_cabinet)|max rounds|max rounds with its upgrade|ammo box restock
///   crates|easy|medium|hard|insane
///   morale|district's shown name|its morale at the start (0-100)
///   text|what|text
/// </summary>
public static partial class GuerrillaWeaponCabinet
{
    public const string FileName = "cabinet.txt";
    const string Language = "us";

    /// <summary>The screen's words: Guerrilla's tags, by what our script calls them.</summary>
    static readonly (string What, string Tag)[] Texts =
    [
        ("crate", "MENU_GH_AMMO_BOX"),             // the title at an ammo crate
        ("locker", "TOOLTIP_LOCKER"),              // and at a safehouse's locker
        ("full_ammo", "HM_FULL_AMMO"),             // said as a locker closes
        ("buy", "MENU_WC_UPGRADE_AVAILABLE"),      // a weapon not bought yet, where its ammo goes
        ("back", "MENU_BACK"),
        ("charges", "UPG_CHARGES_COUNT_NAME"),     // an upgrade level of that name shows its second name (the count)
        ("weapon", "MENU_WEAPON"),                 // over the list's "n / count" (the document's own tag)
    ];

    /// <summary>Guerrilla's team names in weapons.xtbl (Default_team), by its number.</summary>
    static readonly string[] Teams = ["Guerilla", "EDF", "Ultor", "Marauder"];

    /// <summary>Writes the file into <paramref name="folder"/>; returns what was written, a line each.</summary>
    public static List<string> Build(string guerrillaCache, string folder)
    {
        Packfile misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var strings = new Dictionary<uint, string>();
        foreach (string file in new[] { "weapons", "menu", "hud", "scripts" })
            if (misc.Find($"{file}_{Language}.le_strings") is { } entry)
                foreach (var (hash, text) in GuerrillaHud.ReadStrings(misc.Read(entry))) strings.TryAdd(hash, text);
        string? Text(string? tag) => tag != null && strings.TryGetValue(VintDocument.Hash(tag.ToLowerInvariant()), out string? text) ? text : null;

        XtblFile weapons = Table(misc, "weapons.xtbl");
        XtblFile inventory = Table(misc, "items_inventory.xtbl");
        var lines = new List<string>
        {
            "; Guerrilla's weapon cabinet for the guerrilla_hud mod: made by GuerrillaBuilder.exe from your copy of Guerrilla",
            "; (weapons.xtbl, items_inventory.xtbl, difficulty.xtbl, territories.xtbl and their strings).",
            "; weapon|name|shown|picture|team|class|ammo type|flags|max rounds|max rounds upgraded|ammo box restock",
            "; crates|easy|medium|hard|insane    morale|district|morale at the start    text|what|text",
        };
        int count = 0;
        foreach (string name in Guerrilla.GuerrillaWeapons.CampaignWeapons)
        {
            XtblElement? weapon = weapons.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                                                     !string.Join("|", XtblFile.EntryCategory(e)).Contains("MP Overrides", StringComparison.OrdinalIgnoreCase));
            if (weapon == null) continue;
            string? Field(string field) => weapon.ValueOf(field)?.Trim() is { Length: > 0 } value ? value : null;
            var flags = (weapon.Element("Flags")?.Elements ?? []).Select(f => f.Value.Trim()).ToList();
            bool Has(string flag) => flags.Any(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase));
            string inventoryItem = Field("Inventory_Item") ?? name;
            string? displayTag = inventory.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(inventoryItem, StringComparison.OrdinalIgnoreCase))?.ValueOf("Display_Name")?.Trim();
            string shown = Text(displayTag) ?? displayTag ?? name;
            int team = Array.FindIndex(Teams, t => t.Equals(Field("Default_team") ?? "", StringComparison.OrdinalIgnoreCase));
            string picture = Field("icon_name") is { } icon ? GuerrillaHud.Prefix + icon.ToLowerInvariant() : "";
            var flagList = new List<string>();
            if (Has("non-inventory")) flagList.Add("non_inventory");
            if (Has("never in cabinet")) flagList.Add("never_in_cabinet");
            lines.Add(string.Join('|', "weapon", name, Clean(shown), picture, team.ToString(CultureInfo.InvariantCulture),
                                  Field("Weapon_Class") ?? "", Field("Ammo_Type") ?? "", string.Join(',', flagList),
                                  Field("Max_Rounds") ?? "0", Field("Max_Rounds_Upgrade") ?? Field("Max_Rounds") ?? "0", Field("Ammo_Box_Restock") ?? "0"));
            count++;
        }

        // The difficulty's "player ammo crates" (Easy, Medium, Hard, Insane).
        string difficulty = Encoding.Latin1.GetString(misc.Read(misc.Find("difficulty.xtbl") ?? throw new FileNotFoundException("Guerrilla's misc.vpp_pc has no difficulty.xtbl")));
        Match crates = Regex.Match(difficulty, @"<Name>MENU_PLAYER_AMMO_CRATES</Name>(.*?)</Item>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        string Level(string level) => crates.Success && Regex.Match(crates.Groups[1].Value, $"<{level}>([^<]*)</{level}>", RegexOptions.IgnoreCase) is { Success: true } m
            ? m.Groups[1].Value.Trim() : "1";
        lines.Add(string.Join('|', "crates", Level("Easy"), Level("Medium"), Level("Hard"), Level("Insane")));

        // Each district's morale at the start (territories.xtbl, terr01), under its name as the minimap shows it
        // (as GuerrillaHud's districts.txt names it): the crates' restock is scaled by the player's district's morale.
        Dictionary<uint, string> territoryStrings = misc.Find($"territories_{Language}.le_strings") is { } territoryFile
            ? GuerrillaHud.ReadStrings(misc.Read(territoryFile)) : [];
        string territories = Encoding.Latin1.GetString(misc.Read(misc.Find("territories.xtbl") ?? throw new FileNotFoundException("Guerrilla's misc.vpp_pc has no territories.xtbl")));
        Match terr01 = Regex.Match(territories, @"<Territory>\s*<Name>terr01</Name>.*?</Territory>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        int districts = 0;
        if (terr01.Success)
            foreach (Match district in Regex.Matches(terr01.Value, @"<District>.*?</District>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string districtName = Tag(district.Value, "Name") ?? "";
                string tag = Tag(district.Value, "Display_name") ?? "TERR_" + districtName.ToUpperInvariant().Replace(' ', '_');
                string districtShown = territoryStrings.GetValueOrDefault(VintDocument.Hash(tag.ToLowerInvariant()), districtName);
                if (Tag(district.Value, "Morale") is { } morale)
                {
                    lines.Add(string.Join('|', "morale", Clean(districtShown), morale));
                    districts++;
                }
            }

        foreach (var (what, tag) in Texts) lines.Add($"text|{what}|{Clean(Text(tag) ?? tag)}");

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, FileName), lines, new UTF8Encoding(false));
        return [$"{FileName}: {count} of Guerrilla's campaign weapons, the ammo crates' difficulty values, {districts} districts' morale"];
    }

    static XtblFile Table(Packfile misc, string name) =>
        XtblFile.FromBytes(misc.Read(misc.Find(name) ?? throw new FileNotFoundException($"Guerrilla's misc.vpp_pc has no {name}")));

    static string? Tag(string xml, string tag)
    {
        Match match = Regex.Match(xml, $"<{tag}>([^<]*)</{tag}>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    static string Clean(string? text) => (text ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
}
