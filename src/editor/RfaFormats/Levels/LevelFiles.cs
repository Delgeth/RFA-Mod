using System.Text;
using System.Text.RegularExpressions;
using RfaFormats.Interface;
using RfaFormats.Zones;

namespace RfaFormats.Levels;

/// <summary>
/// The pieces of making a level of the game's under a new name (see the editor's New level): its
/// files' names, the names inside them, and its rows in the game's mode tables and strings. See the
/// project's rfa-new-level.md for what a level is.
/// </summary>
public static class LevelFiles
{
    /// <summary>
    /// A name as long as <paramref name="length"/> for a new level's own textures (its blend map's and
    /// colour map's): the terrain files name them inside, so the new name is written over the old one
    /// in place, and has to be as long. "x" and a hash of the level's name, in letters and digits.
    /// </summary>
    public static string TextureTag(string level, int length)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in level.ToLowerInvariant()) hash = unchecked((hash ^ c) * 1099511628211);
        const string digits = "abcdefghijklmnopqrstuvwxyz0123456789";
        var tag = new StringBuilder("x");
        while (tag.Length < length)
        {
            tag.Append(digits[(int)(hash % 36)]);
            hash /= 36;
            if (hash == 0) hash = 1099511628211;
        }
        return tag.ToString(0, Math.Max(1, length));
    }

    /// <summary>A copy of a file with every <paramref name="from"/> (any case) replaced by <paramref name="to"/>, as long (ASCII).</summary>
    public static byte[] ReplaceSameLength(byte[] data, string from, string to)
    {
        if (from.Length != to.Length) throw new ArgumentException($"{to} isn't as long as {from}");
        byte[] copy = (byte[])data.Clone();
        byte[] lower = Encoding.ASCII.GetBytes(from.ToLowerInvariant()), upper = Encoding.ASCII.GetBytes(from.ToUpperInvariant());
        byte[] lowerTo = Encoding.ASCII.GetBytes(to.ToLowerInvariant()), upperTo = Encoding.ASCII.GetBytes(to.ToUpperInvariant());
        for (int i = 0; i + lower.Length <= copy.Length; i++)
        {
            bool match = true;
            for (int k = 0; k < lower.Length && match; k++) match = copy[i + k] == lower[k] || copy[i + k] == upper[k];
            if (!match) continue;
            bool allUpper = copy.AsSpan(i, upper.Length).SequenceEqual(upper) && !upper.AsSpan().SequenceEqual(lower);
            (allUpper ? upperTo : lowerTo).CopyTo(copy, i);
            i += lower.Length - 1;
        }
        return copy;
    }

    /// <summary>Every text and list value of a zone's objects naming <paramref name="from"/> (any case) named <paramref name="to"/> instead (upper case where it was). Returns how many changed.</summary>
    public static int RenameInZone(ZoneFile zone, string from, string to)
    {
        int changed = 0;
        var pattern = new Regex(Regex.Escape(from), RegexOptions.IgnoreCase);
        foreach (ZoneObject o in zone.Objects)
            foreach (ZoneProperty p in o.Properties.ToList())
            {
                if (p.Type is not (ZoneProperty.TextType or ZoneProperty.ListType)) continue;
                string text = Encoding.Latin1.GetString(p.Value);
                if (!pattern.IsMatch(text)) continue;
                string renamed = pattern.Replace(text, m => m.Value == m.Value.ToUpperInvariant() && m.Value != m.Value.ToLowerInvariant() ? to.ToUpperInvariant() : to);
                p.SetValue(Encoding.Latin1.GetBytes(renamed));
                changed++;
            }
        return changed;
    }

    /// <summary>
    /// A strings file (.le_strings: see GuerrillaHud.ReadStrings) with these strings added at its end
    /// (each tag's hash, the lower case tag's, and its text), or their text changed if it has them.
    /// </summary>
    public static byte[] WithStrings(byte[] file, IReadOnlyList<(string Tag, string Text)> strings)
    {
        int count = (int)BitConverter.ToUInt32(file, 6);
        var entries = new List<(uint Hash, byte[] Text)>();
        int at = 10;
        for (int i = 0; i < count && at + 6 <= file.Length; i++)
        {
            uint hash = BitConverter.ToUInt32(file, at);
            int length = BitConverter.ToUInt16(file, at + 4);
            entries.Add((hash, file.AsSpan(at + 6, length).ToArray()));
            at = (at + 6 + length + 3) & ~3;
        }
        foreach (var (tag, text) in strings)
        {
            uint hash = VintDocument.Hash(tag.ToLowerInvariant());
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            int found = entries.FindIndex(e => e.Hash == hash);
            if (found >= 0) entries[found] = (hash, bytes);
            else entries.Add((hash, bytes));
        }
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(file, 0, 6);
        w.Write((uint)entries.Count);
        foreach (var (hash, text) in entries)
        {
            w.Write(hash);
            w.Write((ushort)text.Length);
            w.Write(text);
            while (stream.Length % 4 != 0) w.Write((byte)0);  // (the next starts on a 4-byte boundary)
        }
        w.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// A table (.xtbl) with a copy of one of its rows added after its last row: the row of element
    /// <paramref name="rowTag"/> whose <paramref name="keyTag"/> is <paramref name="key"/>, changed by
    /// <paramref name="change"/> (tag → its new text; a tag the row hasn't is left out). Any row whose
    /// <paramref name="keyTag"/> is already the new key is taken out first. Null if the row isn't found.
    /// </summary>
    public static string? WithRowCopied(string table, string rowTag, string keyTag, string key, string newKey, IReadOnlyDictionary<string, string> change)
    {
        var rows = new Regex($@"[ \t]*<{rowTag}>.*?</{rowTag}>\r?\n?", RegexOptions.Singleline);
        string KeyOf(string row) => Regex.Match(row, $@"<{keyTag}>([^<]*)</{keyTag}>").Groups[1].Value.Trim();
        // An earlier copy of the new one: out.
        table = rows.Replace(table, m => KeyOf(m.Value).Equals(newKey, StringComparison.OrdinalIgnoreCase) ? "" : m.Value);
        Match? template = rows.Matches(table).FirstOrDefault(m => KeyOf(m.Value).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (template == null) return null;
        string row = template.Value;
        foreach (var (tag, text) in change)
            row = Regex.Replace(row, $@"<{tag}>[^<]*</{tag}>", $"<{tag}>{System.Security.SecurityElement.Escape(text)}</{tag}>");
        if (!row.EndsWith('\n')) row += "\r\n";
        Match last = rows.Matches(table).Last();
        return table.Insert(last.Index + last.Length, row);
    }

    /// <summary>A table without its rows of element <paramref name="rowTag"/> whose <paramref name="keyTag"/> is <paramref name="key"/>; null if it has none.</summary>
    public static string? WithoutRow(string table, string rowTag, string keyTag, string key)
    {
        var rows = new Regex($@"[ \t]*<{rowTag}>.*?</{rowTag}>\r?\n?", RegexOptions.Singleline);
        bool any = false;
        string left = rows.Replace(table, m =>
        {
            if (!Regex.Match(m.Value, $@"<{keyTag}>([^<]*)</{keyTag}>").Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return m.Value;
            any = true;
            return "";
        });
        return any ? left : null;
    }

    /// <summary>A table with one tag of one row (the first whose key is <paramref name="key"/>) given a new text; null if the row or tag isn't found.</summary>
    public static string? WithRowValue(string table, string rowTag, string keyTag, string key, string tag, string text)
    {
        foreach (Match m in Regex.Matches(table, $@"<{rowTag}>.*?</{rowTag}>", RegexOptions.Singleline))
        {
            if (!Regex.Match(m.Value, $@"<{keyTag}>([^<]*)</{keyTag}>").Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = new Regex($@"<{tag}>[^<]*</{tag}>");
            if (!value.IsMatch(m.Value)) return null;
            string row = value.Replace(m.Value, $"<{tag}>{System.Security.SecurityElement.Escape(text)}</{tag}>", 1);
            return table.Remove(m.Index, m.Length).Insert(m.Index, row);
        }
        return null;
    }

    /// <summary>The value of a tag in a row of a table (the first), trimmed; null if none.</summary>
    public static string? RowValue(string table, string rowTag, string keyTag, string key, string tag)
    {
        foreach (Match m in Regex.Matches(table, $@"<{rowTag}>.*?</{rowTag}>", RegexOptions.Singleline))
        {
            if (!Regex.Match(m.Value, $@"<{keyTag}>([^<]*)</{keyTag}>").Groups[1].Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            Match value = Regex.Match(m.Value, $@"<{tag}>([^<]*)</{tag}>");
            return value.Success ? value.Groups[1].Value.Trim() : null;
        }
        return null;
    }

    /// <summary>Every row's key in a table (e.g. each map's file name).</summary>
    public static List<string> Keys(string table, string rowTag, string keyTag) =>
        Regex.Matches(table, $@"<{rowTag}>.*?</{rowTag}>", RegexOptions.Singleline)
             .Select(m => Regex.Match(m.Value, $@"<{keyTag}>([^<]*)</{keyTag}>").Groups[1].Value.Trim()).Where(k => k.Length > 0).ToList();
}
