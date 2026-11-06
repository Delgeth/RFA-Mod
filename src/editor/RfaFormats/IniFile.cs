namespace RfaFormats;

/// <summary>
/// Reads and sets values in an ini file (the same files Windows' GetPrivateProfileString reads):
/// [section], key=value, ; comments. The editor and the Guerrilla builder keep GameDir and
/// GuerrillaDir in rfa_mods.ini with it.
/// </summary>
public static class IniFile
{
    public static string? Read(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        string current = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
                current = line[1..^1].Trim();
            else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && !line.StartsWith(';'))
            {
                int equals = line.IndexOf('=');
                if (equals > 0 && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(equals + 1)..].Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Sets a key in an ini file, keeping everything else as it is (comments too): the key's line
    /// in its section is replaced, or added at the end of the section, or the section is added.
    /// </summary>
    public static void Write(string path, string section, string key, string value)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        int sectionAt = -1, endOfSection = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (sectionAt >= 0)
                {
                    endOfSection = i;
                    break;
                }
                if (line[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase)) sectionAt = i;
            }
            else if (sectionAt >= 0 && !line.StartsWith(';') && line.IndexOf('=') is int equals and > 0 &&
                     line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                File.WriteAllLines(path, lines);
                return;
            }
        }
        if (sectionAt < 0)
        {
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            // After the section's last line that isn't blank.
            int at = endOfSection;
            while (at > sectionAt + 1 && lines[at - 1].Trim().Length == 0) at--;
            lines.Insert(at, $"{key}={value}");
        }
        File.WriteAllLines(path, lines);
    }
}
