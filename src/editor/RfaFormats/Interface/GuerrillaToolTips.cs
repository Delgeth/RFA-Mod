using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Interface;

/// <summary>
/// What the guerrilla_hud mod's tool tips need from the player's copy of Guerrilla, in one file, tool_tips.txt, next to
/// our scripts. Guerrilla's tool tips (misc.vpp_pc's tool_tips.xtbl) are the help boxes its game raised as things first
/// happened (ui_tool_tips_add_tip: a weapon locker opened, salvage picked up, an EDF turret got into, ...), shown by its
/// document rfg_ui_hud_help one at a time: a title and a text (string tags in its strings), an icon, where on the screen
/// (Screen_Loc) and where its arrow points (Arrow_Loc), how many times it ever plays (Play_Times; 0: never), how long
/// it stays (Duration, ms), and its flags: Pause (the game pauses until a key is pressed), Run_Paused (it shows while
/// the game is paused, as in a safehouse's screen), Minor_tip (a minor message line instead), Save_to_Profile, and
/// Always_Show (even with tool tips switched off). Its texts name buttons as [BUTTON:&lt;action&gt;], which the mod
/// puts its keys in for. Lines, '|' between fields (texts with "\n" for a new line):
///   tip|name|title|text|icon|animation|screen 1-5 (NE NW SE SW centre)|arrow 1-6 (6 none)|plays|duration ms|flags
///     (five 0/1: pause, run paused, minor, save to profile, always show)
///   text|continue|the hint a tip that paused the game shows once it can be closed (HM_PRESS_START)
/// </summary>
public static class GuerrillaToolTips
{
    public const string FileName = "tool_tips.txt";
    const string Language = "us";
    static readonly string[] Locations = ["North East", "North West", "South East", "South West", "Center", "None"];

    record Tip(string Name, string Title, string Description, string Icon, string Animation, int Screen, int Arrow,
               int Plays, int Duration, bool Pause, bool RunPaused, bool Minor, bool Profile, bool Always);

    /// <summary>The pictures the tips show (without the rfg_ prefix): their icons.</summary>
    public static List<string> Icons(Packfile misc) =>
        Tips(Table(misc)).Select(t => t.Icon).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Writes the file into <paramref name="folder"/>; returns what was written, a line each.</summary>
    public static List<string> Build(string guerrillaCache, string folder)
    {
        Packfile misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var strings = new Dictionary<uint, string>();
        foreach (string file in new[] { "scripts", "hud", "menu", "weapons" })
            if (misc.Find($"{file}_{Language}.le_strings") is { } entry)
                foreach (var (hash, text) in GuerrillaHud.ReadStrings(misc.Read(entry))) strings.TryAdd(hash, text);
        string Text(string tag) => strings.TryGetValue(VintDocument.Hash(tag.ToLowerInvariant()), out string? text) ? text : tag;

        var tips = Tips(Table(misc));
        var lines = new List<string>
        {
            "; Guerrilla's tool tips for the guerrilla_hud mod: made by GuerrillaBuilder.exe from your copy of Guerrilla",
            "; (tool_tips.xtbl and its strings). tip|name|title|text|icon|animation|screen|arrow|plays|duration ms|flags",
            "; (pause, run paused, minor, save to profile, always show)    text|what|text",
        };
        static string Bit(bool on) => on ? "1" : "0";
        foreach (Tip t in tips)
            lines.Add(string.Join('|', "tip", t.Name, Clean(Text(t.Title)), Clean(Text(t.Description)),
                                  t.Icon.Length > 0 ? GuerrillaHud.Prefix + t.Icon : "", t.Animation, t.Screen.ToString(),
                                  t.Arrow.ToString(), t.Plays.ToString(), t.Duration.ToString(),
                                  Bit(t.Pause) + Bit(t.RunPaused) + Bit(t.Minor) + Bit(t.Profile) + Bit(t.Always)));
        lines.Add("text|continue|" + Clean(Text("HM_PRESS_START")));

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, FileName), lines, new UTF8Encoding(false));
        return [$"{FileName}: {tips.Count} tool tips ({tips.Count(t => t.Plays != 0)} ever shown, {tips.Count(t => t.Pause)} pausing the game)"];
    }

    static string Table(Packfile misc) =>
        Encoding.Latin1.GetString(misc.Read(misc.Find("tool_tips.xtbl") ?? throw new FileNotFoundException("Guerrilla's misc.vpp_pc has no tool_tips.xtbl")));

    static List<Tip> Tips(string table)
    {
        var tips = new List<Tip>();
        foreach (Match match in Regex.Matches(table, @"<Tool_Tips>(.*?)</Tool_Tips>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string xml = match.Groups[1].Value;
            if (Tag(xml, "Name") is not { Length: > 0 } name) continue;
            // As tool_tip_base_data::load (RM 0x8DA270): an unknown location is the centre; Duration 5000 if absent.
            static int Location(string? name)
            {
                int i = Array.FindIndex(Locations, l => l.Equals(name ?? "", StringComparison.OrdinalIgnoreCase));
                return (i < 0 ? 4 : i) + 1;
            }
            static bool Flag(string xml, string tag) => string.Equals(Tag(xml, tag), "True", StringComparison.OrdinalIgnoreCase);
            tips.Add(new Tip(name, Tag(xml, "Title") ?? "", Tag(xml, "Description") ?? "", (Tag(xml, "Icon") ?? "").ToLowerInvariant(),
                             (Tag(xml, "Animation") ?? "").ToLowerInvariant(), Location(Tag(xml, "Screen_Loc")), Location(Tag(xml, "Arrow_Loc")),
                             int.TryParse(Tag(xml, "Play_Times"), out int plays) ? plays : 0,
                             int.TryParse(Tag(xml, "Duration"), out int duration) ? duration : 5000,
                             Flag(xml, "Pause"), Flag(xml, "Run_Paused"), Flag(xml, "Minor_tip"), Flag(xml, "Save_to_Profile"), Flag(xml, "Always_Show")));
        }
        return tips;
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
