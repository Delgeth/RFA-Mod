using RfaFormats.Meshes;
using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's walkers' legs in Armageddon's limb table (ik_limbs.xtbl), so their feet are placed on the
/// ground as Armageddon's walkers' are. Armageddon gives a walker every limb whose three bones (Anchor,
/// Joint, IK) its rig has, up to six; Guerrilla has no such table (its walkers' feet only follow their
/// animations). The civilian and flamer walkers name their legs as Armageddon's people do (l-thigh, l-calf,
/// l-foot), so its "human left/right foot" rows already fit them; a walker whose rig names them with
/// underscores (Walker_Min's l_thigh, l_calf, l_foot) gets rows of its own, "rfg &lt;rig&gt; left/right foot",
/// copied from the human foot rows with its bones. Written into mods\guerrilla_world\ik_limbs.xtbl, which
/// the people build adds Mason's arms to as well: each build takes out only its own rows.
/// </summary>
public static class GuerrillaWalkerLimbs
{
    public const string Table = "ik_limbs.xtbl";
    const string Prefix = "rfg walker ";

    static bool Ours(XtblElement row) => XtblFile.EntryName(row).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    static XtblFile WrittenWithoutOurs(Packfile gameTables, string path)
    {
        var table = XtblFile.FromBytes(File.Exists(path) ? File.ReadAllBytes(path) : gameTables.Read(gameTables.Find(Table) ?? throw new InvalidDataException($"no {Table} in Armageddon's misc.vpp_pc")));
        foreach (var row in table.Entries.Where(Ours).ToList()) table.RemoveEntry(row);
        return table;
    }

    /// <summary>Writes the rows for the walkers' rigs <paramref name="rigFiles"/> (vehicles.xtbl's Rig/Rig/Filename, Guerrilla's); a line for the log.</summary>
    public static string Write(Packfile guerrillaTables, Packfile gameTables, string modFolder, IEnumerable<string> rigFiles)
    {
        string path = Path.Combine(modFolder, Table);
        var table = WrittenWithoutOurs(gameTables, path);
        var written = new List<string>();
        foreach (string rigFile in rigFiles.Select(r => Path.ChangeExtension(r.Trim(), ".rig_pc")).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entry = guerrillaTables.Find(rigFile);
            if (entry == null) continue;
            var bones = Rig.Read(guerrillaTables.Read(entry)).Bones.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string side in new[] { "left", "right" })
            {
                string s = side[..1];
                if (bones.Contains($"{s}-thigh") && bones.Contains($"{s}-calf") && bones.Contains($"{s}-foot")) continue;  // the human row fits
                if (!(bones.Contains($"{s}_thigh") && bones.Contains($"{s}_calf") && bones.Contains($"{s}_foot"))) continue;  // no legs of this kind
                var human = table.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals($"human {side} foot", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException($"Armageddon's {Table} has no \"human {side} foot\"");
                var row = human.Copy();
                string name = $"{Prefix}{Path.GetFileNameWithoutExtension(rigFile).Replace("walker_", "", StringComparison.OrdinalIgnoreCase)} {side} foot";
                row.Element("Name")!.Value = name;
                row.Element("Anchor")!.Value = $"{s}_thigh";
                row.Element("Joint")!.Value = $"{s}_calf";
                row.Element("IK")!.Value = $"{s}_foot";
                table.Table!.AddElement(row);
                written.Add(name);
            }
        }
        if (written.Count == 0 && !File.Exists(path)) return "Walker legs: none needed rows of their own";
        Directory.CreateDirectory(modFolder);
        File.WriteAllBytes(path, table.ToBytes());
        return written.Count > 0 ? $"Walker legs: {string.Join(", ", written)} in {Table}" : $"Walker legs: none needed rows of their own ({Table} kept for the other builds' rows)";
    }

    /// <summary>Takes this build's rows out; the file goes only if nothing but Armageddon's rows is left in it.</summary>
    public static IEnumerable<string> Remove(Packfile gameTables, string modFolder)
    {
        string path = Path.Combine(modFolder, Table);
        if (!File.Exists(path)) yield break;
        var left = WrittenWithoutOurs(gameTables, path);
        var armageddons = XtblFile.FromBytes(gameTables.Read(gameTables.Find(Table)!)).Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (left.Entries.All(e => armageddons.Contains(XtblFile.EntryName(e)))) { File.Delete(path); yield return $"Deleted {path}"; }
        else { File.WriteAllBytes(path, left.ToBytes()); yield return $"{Table}: the walkers' legs taken out (another build's rows are kept)"; }
    }
}
