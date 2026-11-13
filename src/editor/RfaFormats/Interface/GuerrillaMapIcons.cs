using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RfaFormats.Guerrilla;
using RfaFormats.Zones;

namespace RfaFormats.Interface;

/// <summary>
/// Guerrilla's map icons and places, for the guerrilla_hud mod's minimap and world map: a text file
/// (<see cref="FileName"/>, next to our scripts) made from the player's own copy of Guerrilla.
///
/// Guerrilla's cartographer drew an icon for each campaign object that adds one (cartographer::add_icon
/// with a minimap element): a safehouse, a mission's start, an activity's start. Which picture each
/// element shows, and what the world map's highlight box says about it, is Guerrilla's
/// minimap_icon_info.xtbl (IconName, FSCaption, FSDescription, VisibleOnFSMap). The objects are in
/// Guerrilla's persistent zones (zonescript_terr01.vpp_pc &gt; p_terr01_&lt;col&gt;_&lt;row&gt;.rfgzone_pc), where
/// Armageddon never makes them (it has no code for their classes). Only the places are written here:
/// the missions' and activities' scripts aren't ported, so their icons are places to go, nothing more.
///
/// The world map's named places are Guerrilla's terr01.fsmib (misc.vpp_pc; read by
/// fullscreen_map_terr_info_init): a count (u8), then 35 bytes each: x, z (u8, 2 m steps from the zone's
/// north-west corner), the zone (u8: its column in the high nibble, its row in the low one) and a text
/// (32 bytes): a string tag (MENU_TOWN_OF_DUST) for a place, or a picture name (ui_...) for a district's
/// liberated flag (cartographer::add_district_liberated_icon; not written: no district can be
/// liberated yet). Then the districts' boundaries, which this doesn't read.
///
/// A line each, fields split by '|':
///   icon|&lt;picture&gt;|&lt;x&gt;|&lt;z&gt;|&lt;caption&gt;|&lt;description&gt;|&lt;on the world map: 1 or 0&gt;
///   place||&lt;x&gt;|&lt;z&gt;|&lt;name&gt;||1
///   player|&lt;picture&gt;|0|0|&lt;caption&gt;|&lt;description&gt;|1      (the player's own icon, MAP_ELEMENT_PLAYER)
///   text||0|0|&lt;what&gt;|&lt;text&gt;|1                             (the world map's hints: pan, zoom, back, beacon, center)
///   bomb|&lt;picture&gt;|&lt;x&gt;|&lt;z&gt;|&lt;caption&gt;|&lt;description&gt;|1|&lt;pair&gt;   (a M.O.A.B., shown once its radio tags are found)
///
/// And <see cref="RadioTagsFile"/>: Guerrilla's "Blast from the past" (object_bftp_node, 72 in its persistent
/// zones, 36 pairs by pair_number): tag_node 1 is a hidden radio tag, found by walking within 1.5 m of it on
/// foot (blast_from_the_past_process_car_bombs, RM 0xAA4C60: distance squared under 2.25); every third tag
/// found shows its pair's M.O.A.B. (bftp_touch_tag 0xA8DB50, spawn_bomb_from_tag). A line each,
/// "tag|pair|x|y|z" or "bomb|pair|x|y|z", then Guerrilla's messages for it, "text|what|text".
/// x and z in metres, Guerrilla's world (ours keeps its coordinates); the picture renamed as the HUD's
/// pictures are (rfg_...); the texts in the player's language.
/// </summary>
public static partial class GuerrillaMapIcons
{
    public const string FileName = "map_icons.txt";
    public const string RadioTagsFile = "radio_tags.txt";

    /// <summary>
    /// Guerrilla's safehouses' stations, for the guerrilla_hud mod: each safehouse's upgrade shop (its
    /// object_upgrade_node, upgrade_type Weapons, at the safehouse's own spot) and weapon cabinet (an
    /// object_action_node whose animation_type is "Player Open Weapon Cabinet"), neither of which Armageddon
    /// makes, and the words Guerrilla's upgrade screen shows. "shop|x|y|z", "cabinet|x|y|z", "text|what|text".
    /// </summary>
    public const string SafehousesFile = "safehouses.txt";
    static readonly (string What, string Tag)[] ShopTags =
    [
        ("title", "MENU_UPGRADES"), ("price", "MENU_PRICE"), ("credits", "MENU_CREDITS"), ("select", "MENU_SELECT"),
        ("back", "MENU_BACK"), ("locked", "MENU_LOCKED"), ("use", "MENU_USE"),
    ];
    const string Language = "us";
    const float SamePlaceMetres = 5f;  // two objects with the same icon this close are one icon (a raid node per level)

    /// <summary>
    /// Guerrilla's object classes that add an icon, and the minimap element each adds (minimap_elements:
    /// see docs). Missions and activities are shown where they start; their end points (convoy, courier)
    /// only showed while one ran, and aren't written.
    /// </summary>
    static readonly (string Class, string Element)[] IconClasses =
    [
        ("object_safehouse", "MAP_ELEMENT_SAFE_HOUSE"),
        ("object_mission_start_node", "MAP_ELEMENT_MISSION_START"),
        ("object_demolitions_master_node", "MAP_ELEMENT_DEMOLITIONS_MASTER"),
        ("object_house_arrest_node", "MAP_ELEMENT_HOUSE_ICON"),
        ("object_raid_node", "MAP_ELEMENT_RAID_ICON"),
        ("object_riding_shotgun_node", "MAP_ELEMENT_RIDING_SHOTGUN"),
        ("object_area_defense_node", "MAP_ELEMENT_AREA_DEFENSE"),
        ("object_delivery_node", "MAP_ELEMENT_GUERRILLA_EXPRESS"),  // Guerrilla Express: a delivery's start (start_node)
    ];

    static readonly (string What, string Tag)[] HintTags =
    [
        ("pan", "HM_FSM_PAN"), ("zoom", "MENU_ZOOM"), ("back", "MENU_BACK"), ("beacon", "HM_FSM_PLACE_NAV"),
        ("center", "HM_FSM_CENTER"), ("salvage", "MENU_SALVAGE"),
    ];

    sealed record Element(string Picture, string? Caption, string? Description, bool OnWorldMap);

    /// <summary>Writes the file into <paramref name="folder"/>; returns what was written, a line each.</summary>
    public static List<string> Build(string guerrillaCache, string folder)
    {
        var report = new List<string>();
        Packfile misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        Packfile zoneScripts = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        var strings = new Dictionary<uint, string>();
        foreach (string file in new[] { "hud", "menu", "mission", "platform_pc" })
            if (misc.Find($"{file}_{Language}.le_strings") is { } entry)
                foreach (var (hash, text) in GuerrillaHud.ReadStrings(misc.Read(entry))) strings.TryAdd(hash, text);
        string? Text(string? tag) => tag != null && strings.TryGetValue(VintDocument.Hash(tag.ToLowerInvariant()), out string? text) ? text : null;

        Dictionary<string, Element> elements = Elements(Encoding.Latin1.GetString(misc.Read(misc.Find("minimap_icon_info.xtbl")
            ?? throw new FileNotFoundException("Guerrilla's misc.vpp_pc has no minimap_icon_info.xtbl"))));
        Dictionary<string, string> missionNames = MissionNames(misc, Text);

        var lines = new List<string>
        {
            "; Guerrilla's map icons and places, for the minimap and the world map: made by GuerrillaBuilder.exe from your",
            "; copy of Guerrilla (its persistent zones, minimap_icon_info.xtbl, terr01.fsmib and its strings).",
            "; icon|picture|x|z|caption|description|on the world map    place||x|z|name||1    (x, z in metres)",
            "; player|picture|0|0|caption|description|1    text||0|0|what|text|1 (the world map's hints)",
        };
        if (elements.TryGetValue("MAP_ELEMENT_PLAYER", out Element? player))
            lines.Add(Line("player", GuerrillaHud.Prefix + player.Picture, 0, 0, Text(player.Caption), Text(player.Description), true));
        // The world map's hints, as Guerrilla's map wrote them (hud and menu strings).
        foreach (var (what, tag) in HintTags)
            lines.Add(Line("text", "", 0, 0, what, Text(tag) ?? tag, true));
        var placed = new List<(string Picture, float X, float Z)>();
        var radioTags = new List<string>
        {
            "; Guerrilla's radio tags and M.O.A.B.s (object_bftp_node in its persistent zones): made by GuerrillaBuilder.exe",
            "; from your copy of Guerrilla. tag|pair|x|y|z (a radio tag), bomb|pair|x|y|z (its pair's M.O.A.B.), text|what|text.",
        };
        uint bftpClass = NameHashes.ClassHash("object_bftp_node");
        elements.TryGetValue("MAP_ELEMENT_CARBOMB", out Element? carBomb);
        int tags = 0, bombs = 0;
        var counts = new SortedDictionary<string, int>();
        var classes = IconClasses.ToDictionary(c => NameHashes.ClassHash(c.Class), c => c);
        foreach (PackfileEntry entry in zoneScripts.Entries.Where(e => e.Name.StartsWith("p_terr01_", StringComparison.OrdinalIgnoreCase) &&
                                                                           e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase))
                                                           .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            foreach (GuerrillaZoneObject zoneObject in GuerrillaZones.Objects(zoneScripts.Read(entry)))
            {
                if (zoneObject.ClassHash == bftpClass && zoneObject.Value("pair_number") is { Length: >= 4 } pairValue)
                {
                    int pair = BitConverter.ToInt32(pairValue, 0);
                    bool tag = Flag(zoneObject, "tag_node") == true;
                    var centre = (zoneObject.BoundsMin + zoneObject.BoundsMax) / 2;
                    radioTags.Add(string.Join('|', tag ? "tag" : "bomb", pair.ToString(CultureInfo.InvariantCulture), Number(centre.X), Number(centre.Y), Number(centre.Z)));
                    if (tag) tags++;
                    else
                    {
                        bombs++;
                        if (carBomb != null)
                            lines.Add(Line("bomb", GuerrillaHud.Prefix + carBomb.Picture, centre.X, centre.Z, Text(carBomb.Caption), Text(carBomb.Description),
                                           carBomb.OnWorldMap) + "|" + pair.ToString(CultureInfo.InvariantCulture));
                    }
                    continue;
                }
                if (!classes.TryGetValue(zoneObject.ClassHash, out var kind) || !elements.TryGetValue(kind.Element, out Element? element)) continue;
                if (Flag(zoneObject, "DISABLED") == true || Flag(zoneObject, "enabled") == false) continue;  // a house arrest Guerrilla never offers
                if (kind.Class == "object_delivery_node" && Flag(zoneObject, "start_node") != true) continue;  // only where a delivery starts
                float x = (zoneObject.BoundsMin.X + zoneObject.BoundsMax.X) / 2, z = (zoneObject.BoundsMin.Z + zoneObject.BoundsMax.Z) / 2;
                if (placed.Any(p => p.Picture == element.Picture && Math.Abs(p.X - x) < SamePlaceMetres && Math.Abs(p.Z - z) < SamePlaceMetres)) continue;
                placed.Add((element.Picture, x, z));
                string? caption = Text(element.Caption), description = Text(element.Description);
                if (kind.Class == "object_safehouse" && Text(zoneObject.Text("display_name_tag")) is { } safehouse) caption = safehouse;
                if (kind.Class == "object_mission_start_node" && zoneObject.Text("mission_info") is { } mission &&
                    missionNames.TryGetValue(mission, out string? missionName)) description = missionName;
                // Its minimap rule (the mod: which of Guerrilla's minimap rules the icon follows), after an empty pair field.
                lines.Add(Line("icon", GuerrillaHud.Prefix + element.Picture, x, z, caption, description, element.OnWorldMap) + "||" +
                          MinimapRule(kind.Class));
                counts[kind.Class] = counts.GetValueOrDefault(kind.Class) + 1;
            }

        // The safehouses' stations: the shops in the persistent zones and the plain ones (the same objects), the
        // weapon cabinets in the plain zones.
        var stations = new List<string>
        {
            "; Guerrilla's safehouse stations: made by GuerrillaBuilder.exe from your copy of Guerrilla's zones.",
            "; shop|x|y|z (an upgrade shop), cabinet|x|y|z (a weapon cabinet), text|what|text (the upgrade screen's words).",
        };
        uint upgradeClass = NameHashes.ClassHash("object_upgrade_node"), actionClass = NameHashes.ClassHash("object_action_node");
        var stationsAt = new List<(string Kind, System.Numerics.Vector3 At)>();
        foreach (PackfileEntry entry in zoneScripts.Entries.Where(e => e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase))
                                                           .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            foreach (GuerrillaZoneObject zoneObject in GuerrillaZones.Objects(zoneScripts.Read(entry)))
            {
                string? kind = zoneObject.ClassHash == upgradeClass ? "shop"
                    : zoneObject.ClassHash == actionClass && zoneObject.Text("animation_type") == "Player Open Weapon Cabinet" ? "cabinet" : null;
                if (kind == null) continue;
                var at = (zoneObject.BoundsMin + zoneObject.BoundsMax) / 2;
                if (stationsAt.Any(s => s.Kind == kind && System.Numerics.Vector3.Distance(s.At, at) < SamePlaceMetres)) continue;
                stationsAt.Add((kind, at));
                stations.Add(string.Join('|', kind, Number(at.X), Number(at.Y), Number(at.Z)));
            }
        foreach (var (what, tag) in ShopTags) stations.Add($"text|{what}|{Clean(Text(tag) ?? tag)}");

        int places = 0;
        if (misc.Find("terr01.fsmib") is { } fsmib)
            foreach (var (x, z, text) in Places(misc.Read(fsmib)))
            {
                if (text.StartsWith("ui_", StringComparison.OrdinalIgnoreCase)) continue;  // a district's liberated flag
                lines.Add(Line("place", "", x, z, Text(text) ?? text, null, true));
                places++;
            }
        else report.Add($"{FileName}: Guerrilla's misc.vpp_pc has no terr01.fsmib; the world map names no places");

        // Guerrilla's messages when a radio tag is found (hud strings): one more needed, n more needed (its %ls
        // the number), a M.O.A.B. found.
        foreach (var (what, tag) in new[] { ("tag_found", "HM_BFTP_TAG_FOUND"), ("tags_found", "HM_BFTP_TAGS_FOUND"), ("spawn_bomb", "HM_BFTP_SPAWN_BOMB") })
            radioTags.Add($"text|{what}|{Clean(Text(tag) ?? tag)}");

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, FileName), lines, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(folder, RadioTagsFile), radioTags, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(folder, SafehousesFile), stations, new UTF8Encoding(false));
        report.Add($"{SafehousesFile}: {stationsAt.Count(s => s.Kind == "shop")} upgrade shops and {stationsAt.Count(s => s.Kind == "cabinet")} weapon cabinets");
        report.Add($"{RadioTagsFile}: {tags} radio tags and {bombs} M.O.A.B.s");
        report.Add($"{FileName}: {counts.Values.Sum()} icons ({string.Join(", ", counts.Select(c => $"{c.Value} {c.Key}"))}) and {places} places");
        return report;
    }

    static string Line(string kind, string picture, float x, float z, string? caption, string? description, bool onWorldMap) =>
        string.Join('|', kind, picture, x.ToString("0.#", CultureInfo.InvariantCulture), z.ToString("0.#", CultureInfo.InvariantCulture),
                    Clean(caption), Clean(description), onWorldMap ? "1" : "0");

    /// <summary>
    /// Which of Guerrilla's minimap rules an icon of a class follows (RM cartographer::render_sticky_icons 0x878180 and
    /// the activities' start icons): "safehouse" (only the nearest, pinned to the rim at any distance), "mission"
    /// (pinned, within 250 m), or an activity's start ("demo", "areadefense", "shotgun", "housearrest", "raid",
    /// "express": only inside the minimap's circle, by the activity's own conditions).
    /// </summary>
    static string MinimapRule(string objectClass) => objectClass switch
    {
        "object_safehouse" => "safehouse",
        "object_mission_start_node" => "mission",
        "object_demolitions_master_node" => "demo",
        "object_area_defense_node" => "areadefense",
        "object_riding_shotgun_node" => "shotgun",
        "object_house_arrest_node" => "housearrest",
        "object_raid_node" => "raid",
        "object_delivery_node" => "express",
        _ => "",
    };

    static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    static string Clean(string? text) => (text ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>A one-byte flag property, or null if the object hasn't it.</summary>
    static bool? Flag(GuerrillaZoneObject zoneObject, string name) => zoneObject.Value(name) is { Length: >= 1 } value ? value[0] != 0 : null;

    /// <summary>minimap_icon_info.xtbl: each element by name (MAP_ELEMENT_...).</summary>
    static Dictionary<string, Element> Elements(string table)
    {
        var elements = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
        foreach (Match block in DescriptionBlocks().Matches(table))
        {
            string? name = Tag(block.Value, "Name"), picture = Tag(block.Value, "IconName");
            if (name == null || picture == null) continue;
            elements.TryAdd(name, new Element(picture, Tag(block.Value, "FSCaption"), Tag(block.Value, "FSDescription"),
                                              !"False".Equals(Tag(block.Value, "VisibleOnFSMap"), StringComparison.OrdinalIgnoreCase)));
        }
        return elements;
    }

    /// <summary>missions.xtbl: each mission's name (as the zones' mission_info names it) to its title in the player's language.</summary>
    static Dictionary<string, string> MissionNames(Packfile misc, Func<string?, string?> text)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (misc.Find("missions.xtbl") is not { } entry) return names;
        foreach (Match block in MissionBlocks().Matches(Encoding.Latin1.GetString(misc.Read(entry))))
            if (Tag(block.Value, "Name") is { } name && text(Tag(block.Value, "description")) is { } title) names.TryAdd(name, title);
        return names;
    }

    /// <summary>terr01.fsmib's records: where each is (metres) and its text (see the class's summary).</summary>
    static IEnumerable<(float X, float Z, string Text)> Places(byte[] data)
    {
        const int record = 35;
        int count = data.Length > 0 ? data[0] : 0;
        for (int i = 0, at = 1; i < count && at + record <= data.Length; i++, at += record)
        {
            int column = data[at + 2] >> 4, row = data[at + 2] & 0xF;
            // cartographer::get_district_boundry_point: x = column × 511 − 4088 + 2 × x, z = 4088 − row × 511 − 2 × z.
            float x = column * 511f - 4088f + 2f * data[at], z = 4088f - row * 511f - 2f * data[at + 1];
            int length = Array.IndexOf(data, (byte)0, at + 3, 32) is int end and >= 0 ? end - (at + 3) : 32;
            yield return (x, z, Encoding.Latin1.GetString(data, at + 3, length));
        }
    }

    static string? Tag(string xml, string tag)
    {
        Match match = Regex.Match(xml, $"<{tag}>([^<]*)</{tag}>", RegexOptions.IgnoreCase);
        return match.Success && match.Groups[1].Value.Trim().Length > 0 ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"<Description>.*?</Description>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionBlocks();

    [GeneratedRegex(@"<Mission>.*?</Mission>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MissionBlocks();
}
