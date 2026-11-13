using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Interface;

/// <summary>
/// Guerrilla's strings for the guerrilla_hud mod, in one file, hud_messages.txt, next to our scripts: the campaign
/// gives Guerrilla's string tags (its HUD messages through GuerrillaCampaign_NextMessage, an activity's screens through
/// GuerrillaCampaign_Activity), and Guerrilla's scripts set some of their texts by tag, which Armageddon's string tables
/// don't have. A strings file keeps only each tag's hash (<see cref="VintDocument.Hash"/> of the tag in lower case), so
/// every string of Guerrilla's hud, activity, menu, scripts and mission strings is written by its hash, and the mod
/// hashes a tag to find it; the tags in <see cref="Tags"/> (the campaign's messages) by name too. The texts are
/// Guerrilla's, with their pictures ([IMAGE:UI_HUD_GUERILLA_LOGO_WHITE]) named as the HUD's documents have them
/// (rfg_...), a new line written as a backslash and n; a number goes where Guerrilla's text has it (%ls, %s, %d or %i).
/// Lines:
///   text|tag|text
///   hash|XXXXXXXX|text
/// </summary>
public static class GuerrillaHudMessages
{
    public const string FileName = "hud_messages.txt";
    const string Language = "us";

    /// <summary>The tags guerrilla_campaign raises (its liberation, district sources and converts).</summary>
    public static readonly string[] Tags =
    [
        "HM_CIVILIAN_KILLED", "HM_EDF_PROPERTY_DESTROYED", "HM_GUERRILLA_ASSIST", "HM_GUERRILLAS_ASSIST",
        "HM_GUERRILLA_KILLED", "HM_PROPAGANDA_DESTROYED", "HM_TRUST_BETRAYED",
    ];

    /// <summary>The pictures the texts show (without the rfg_ prefix), for the messages document to bring.</summary>
    public static readonly string[] Pictures = ["ui_hud_guerilla_logo_white"];

    /// <summary>Writes the file into <paramref name="folder"/>; returns what was written, a line each.</summary>
    public static List<string> Build(string guerrillaCache, string folder)
    {
        Packfile misc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var strings = new Dictionary<uint, string>();
        foreach (string file in new[] { "hud", "activity", "menu", "scripts", "mission" })
            if (misc.Find($"{file}_{Language}.le_strings") is { } entry)
                foreach (var (hash, text) in GuerrillaHud.ReadStrings(misc.Read(entry))) strings.TryAdd(hash, text);

        var lines = new List<string>
        {
            "; Guerrilla's HUD messages the guerrilla_campaign mod raises, for the guerrilla_hud mod: made by",
            "; GuerrillaBuilder.exe from your copy of Guerrilla (its hud, activity, menu, scripts and mission strings).",
            "; text|tag|text    hash|its tag's hash|text",
        };
        var missing = new List<string>();
        foreach (string tag in Tags)
        {
            if (!strings.TryGetValue(VintDocument.Hash(tag.ToLowerInvariant()), out string? text))
            {
                missing.Add(tag);
                continue;
            }
            lines.Add(string.Join('|', "text", tag, Clean(Pictured(text))));
        }

        foreach (var (hash, text) in strings.OrderBy(pair => pair.Key))
            lines.Add($"hash|{hash:X8}|{Clean(Pictured(text))}");

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, FileName), lines, new UTF8Encoding(false));
        return [$"{FileName}: {Tags.Length - missing.Count} message texts, {strings.Count} of Guerrilla's strings" +
                (missing.Count > 0 ? "; not in Guerrilla's strings: " + string.Join(", ", missing) : "")];
    }

    // [IMAGE:NAME] as the HUD's documents name the picture: [image:rfg_name].
    static string Pictured(string text) =>
        Regex.Replace(text, @"\[image:([^\]]+)\]", m => "[image:" + GuerrillaHud.Prefix + m.Groups[1].Value.ToLowerInvariant() + "]",
                      RegexOptions.IgnoreCase);

    static string Clean(string text) => text.Replace('|', '/').Replace("\r", "").Replace("\n", "\\n");
}
