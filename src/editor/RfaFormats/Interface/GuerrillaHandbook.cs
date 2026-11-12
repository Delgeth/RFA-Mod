using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Interface;

/// <summary>
/// What the guerrilla_hud mod's Guerrilla Handbook needs from the player's copy of Guerrilla, in one file,
/// handbook.txt, next to our scripts. Guerrilla's handbook (hud_ui_handbook_*, its pause menu's HANDBOOK, the
/// document rfg_ui_handbook) lists the entries of misc.vpp_pc's guerrilla_handbook.xtbl, a page for each category in
/// the order the table first names it (hud_ui_handbook_load_table_data makes a page as it meets a category). Each
/// entry has a title and a text (string tags in menu_us.le_strings, or the words themselves), a picture of its own
/// (Image: interface.vpp_pc's ui_handbook_&lt;x&gt;.str2_pc) or a video (Video), an icon (Info: a weapon's), and
/// whether it is open from the start (Unlocked); the rest are opened as the game goes (the mod: a weapon in the
/// cabinet opens the entry of its name, a radio tag found the radio tags' page's entry of its number).
/// Lines, '|' between fields (texts with "\n" for a new line):
///   page|number|category tag|shown name|kind (gameplay, actions, combat, vehicles, background, bftp, cutscene)
///   entry|name|page number|title|text|picture|video|icon|unlocked (1 or 0)|voice line|the picture's document
///   text|what|text
/// </summary>
public static class GuerrillaHandbook
{
    public const string FileName = "handbook.txt";
    const string Language = "us";

    /// <summary>The page kinds by category: which of the document's page setters shows its entries.</summary>
    static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MENU_CORE_GAMEPLAY"] = "gameplay",
        ["MENU_GUERRILLA_ACTIONS"] = "actions",
        ["MENU_COMBAT"] = "combat",
        ["MENU_VEHICLES"] = "vehicles",
        ["MENU_BACKGROUND"] = "background",
        ["MENU_BFTP"] = "bftp",
        ["MENU_CINEMATICS"] = "cutscene",
    };

    /// <summary>The screen's words: Guerrilla's tags, by what our script calls them.</summary>
    static readonly (string What, string Tag)[] Texts =
    [
        ("title", "MENU_HANDBOOK"),        // its title, and the pause menu's item
        ("new", "MENU_GH_NEW_TEXT"),       // beside an entry not read yet
        ("back", "MENU_BACK"),
        ("select", "MENU_SELECT"),
    ];

    /// <summary>
    /// Each entry's picture is in a document of its own, which the mod loads while its entry is shown (as Guerrilla
    /// streamed each picture's own container), one at a time: rfg_ui_hbp_&lt;the picture without ui_handbook_&gt;.
    /// </summary>
    public const string PictureDocumentPrefix = GuerrillaHud.Prefix + "ui_hbp_";

    public static string PictureDocument(string picture) =>
        PictureDocumentPrefix + (picture.StartsWith("ui_handbook_", StringComparison.OrdinalIgnoreCase) ? picture["ui_handbook_".Length..] : picture).ToLowerInvariant();

    /// <summary>
    /// The radio tags' portraits (ui_handbook_id_&lt;n&gt;) are left out for now (with their recordings, later).
    /// </summary>
    public static bool BringsPicture(string picture) => !picture.StartsWith("ui_handbook_id_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The pictures the handbook's entries show (without .tga), those <see cref="BringsPicture"/> keeps.</summary>
    public static List<string> Pictures(Packfile misc) =>
        Entries(Table(misc)).Select(e => e.Picture).Where(p => p.Length > 0 && BringsPicture(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Writes the file into <paramref name="folder"/>; returns what was written, a line each.</summary>
    public static List<string> Build(string guerrillaCache, string folder)
    {
        Packfile misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var strings = new Dictionary<uint, string>();
        // menu_us has nearly all; scripts_us the radio tags page's own (TOOLTIP_BLAST_FROM_THE_PAST_DESC).
        foreach (string file in new[] { "menu", "hud", "weapons", "scripts" })
            if (misc.Find($"{file}_{Language}.le_strings") is { } entry)
                foreach (var (hash, text) in GuerrillaHud.ReadStrings(misc.Read(entry))) strings.TryAdd(hash, text);
        string Text(string tag) => strings.TryGetValue(VintDocument.Hash(tag.ToLowerInvariant()), out string? text) ? text : tag;

        var entries = Entries(Table(misc));
        var pages = new List<string>();
        foreach (var e in entries)
            if (!pages.Contains(e.Category, StringComparer.OrdinalIgnoreCase)) pages.Add(e.Category);
        var lines = new List<string>
        {
            "; Guerrilla's handbook for the guerrilla_hud mod: made by GuerrillaBuilder.exe from your copy of Guerrilla",
            "; (guerrilla_handbook.xtbl and its strings). page|number|category|shown|kind",
            "; entry|name|page|title|text|picture|video|icon|unlocked|voice line|picture's document    text|what|text",
        };
        for (int i = 0; i < pages.Count; i++)
            lines.Add(string.Join('|', "page", (i + 1).ToString(), pages[i], Clean(Text(pages[i])), Kinds.GetValueOrDefault(pages[i], "background")));
        int pictures = 0;
        foreach (var e in entries)
        {
            string picture = e.Picture.Length > 0 && BringsPicture(e.Picture) ? GuerrillaHud.Prefix + e.Picture : "";
            string pictureDocument = picture.Length > 0 ? PictureDocument(e.Picture) : "";
            if (picture.Length > 0) pictures++;
            string icon = e.Icon.Length > 0 && !e.Icon.EndsWith(".bik", StringComparison.OrdinalIgnoreCase) ? GuerrillaHud.Prefix + e.Icon : "";
            string video = e.Video.Length > 0 ? e.Video : e.Icon.EndsWith(".bik", StringComparison.OrdinalIgnoreCase) ? e.Icon : "";
            lines.Add(string.Join('|', "entry", Clean(e.Name), (pages.FindIndex(p => p.Equals(e.Category, StringComparison.OrdinalIgnoreCase)) + 1).ToString(),
                                  Clean(Text(e.Title)), Clean(Text(e.Description)), picture, video, icon, e.Unlocked ? "1" : "0", e.VoiceLine,
                                  pictureDocument));
        }
        foreach (var (what, tag) in Texts) lines.Add($"text|{what}|{Clean(Text(tag))}");

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, FileName), lines, new UTF8Encoding(false));
        return [$"{FileName}: {entries.Count} entries on {pages.Count} pages ({entries.Count(e => e.Unlocked)} open from the start), " +
                $"{pictures} with their pictures, each in a document of its own (the radio tags' portraits left out for now)"];
    }

    record Entry(string Name, string Category, string Title, string Description, string Picture, string Video, string Icon, bool Unlocked, string VoiceLine);

    static string Table(Packfile misc) =>
        Encoding.Latin1.GetString(misc.Read(misc.Find("guerrilla_handbook.xtbl") ?? throw new FileNotFoundException("Guerrilla's misc.vpp_pc has no guerrilla_handbook.xtbl")));

    static List<Entry> Entries(string table)
    {
        var entries = new List<Entry>();
        foreach (Match match in Regex.Matches(table, @"<Handbook_Entry>(.*?)</Handbook_Entry>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string xml = match.Groups[1].Value;
            string? category = Tag(xml, "Category");
            string? name = Tag(xml, "Name");
            if (category == null || name == null) continue;
            string File(string block) => Regex.Match(xml, $@"<{block}>\s*<Filename>([^<]*)</Filename>", RegexOptions.IgnoreCase) is { Success: true } m
                ? m.Groups[1].Value.Trim() : "";
            entries.Add(new Entry(name, category, Tag(xml, "Title") ?? name, Tag(xml, "Description") ?? "",
                                  Path.GetFileNameWithoutExtension(File("Image")), File("Video"), Tag(xml, "Info") ?? "",
                                  string.Equals(Tag(xml, "Unlocked"), "True", StringComparison.OrdinalIgnoreCase), Tag(xml, "Voice_Line") ?? ""));
        }
        return entries;
    }

    static string? Tag(string xml, string tag)
    {
        Match match = Regex.Match(xml, $"<{tag}>([^<]*)</{tag}>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    // Guerrilla's texts mark a new line with "\n" (as the two characters, in either case): kept so, one line a field.
    static string Clean(string? text) =>
        (text ?? "").Replace('|', '/').Replace("\r", "").Replace("\n", "\\n").Replace("\\N", "\\n").Trim();
}
