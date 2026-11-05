using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's glass tints for a world ported from it. glass.xtbl (the same in both
/// games) gives a colour to a pane whose material has one of its textures (Bitmap_name), and the game
/// tints the pane's shatter with it. Our worlds' textures are renamed ("rfg_" and a hash of the name),
/// so none would match: the table is Armageddon's own with each of Guerrilla's entries added again
/// under its texture's new name. The game reads it at start-up as it does the gameplay properties,
/// so the guerrilla_world mod hands it the game the same way (see <see cref="GuerrillaGameplay"/>).
/// </summary>
public static class GuerrillaGlass
{
    /// <summary>How many entries the game's table holds.</summary>
    const int Room = 50;

    /// <summary>The table, and how many entries were added.</summary>
    public sealed record Table(byte[] Text, int Added);

    /// <summary>
    /// Armageddon's glass.xtbl (from its misc.vpp_pc) with Guerrilla's entries (from its misc.vpp_pc)
    /// added, each naming its texture by <paramref name="rename"/> (the name without ".tga" to our
    /// world's).
    /// </summary>
    public static Table Build(Packfile armageddonMisc, Packfile guerrillaMisc, Func<string, string> rename)
    {
        static string Text(Packfile archive) =>
            Encoding.Latin1.GetString(archive.Read(archive.Find("glass.xtbl") ?? throw new InvalidDataException($"no glass.xtbl in {archive.Name}")));
        string ours = Text(armageddonMisc);
        int count = Entries(ours).Count();

        var text = new StringBuilder();
        int added = 0;
        foreach (string entry in Entries(Text(guerrillaMisc)))
        {
            var bitmap = Regex.Match(entry, @"<Bitmap_name>([^<]*)</Bitmap_name>");
            if (!bitmap.Success) continue;
            string name = bitmap.Groups[1].Value.Trim();
            if (name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            text.Append('\t').Append(entry.Replace(bitmap.Value, $"<Bitmap_name>{rename(name)}.tga</Bitmap_name>")).Append('\n');
            added++;
        }
        if (count + added > Room) throw new InvalidDataException($"{count + added} glass tints; the game's table holds {Room}");

        // Added as the table's last entries, before its closing tag, written as Armageddon's are.
        int end = ours.IndexOf("</Table>", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Armageddon's glass.xtbl has no table");
        int at = ours.LastIndexOf('\n', end) + 1;
        string newline = ours.Contains("\r\n") ? "\r\n" : "\n";
        return new Table(Encoding.Latin1.GetBytes(ours[..at] + text.ToString().Replace("\n", newline) + ours[at..]), added);
    }

    /// <summary>The table's entries, each from its opening tag to its closing one, with "\n" line ends.</summary>
    static IEnumerable<string> Entries(string text) =>
        Regex.Matches(text.Replace("\r\n", "\n"), @"<Glass>.*?</Glass>", RegexOptions.Singleline).Select(m => m.Value);
}
