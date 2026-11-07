using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's walkers' footsteps (footsteps.xtbl: for each ground material, the effect each foot leaves
/// walking and running), which a vehicle names in Rig/Footsteps. Both games' tables are one; a name
/// Armageddon has (Walker_Civ_PC, its own walker's: Armageddon's Mars effects) gets "rfg_" before it, and
/// the effects are Guerrilla's, under the names the world builder adds them by (<see cref="Effects"/>: an
/// effect the world's tables haven't yet is left out of its row until the world is built again). Written
/// into the mod's folder (Armageddon's rows, any other part's, and these; this part's own rows listed in
/// mods\guerrilla_vehicles\footstep_entries.txt), which guerrilla_world hands the game.
/// </summary>
public static class GuerrillaVehicleFootsteps
{
    public const string Table = "footsteps.xtbl", OwnRows = "footstep_entries.txt";

    static Dictionary<string, XtblElement> Guerrilla(Packfile guerrillaTables) =>
        XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find(Table) ?? throw new InvalidDataException($"no {Table} in Guerrilla's misc.vpp_pc"))).Entries
            .GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    static IEnumerable<XtblElement> Leaves(XtblElement e) => e.Elements.SelectMany(c => c.HasElements ? Leaves(c) : [c]);

    static bool IsEffect(XtblElement field) => field.Name.Contains("Footstep", StringComparison.OrdinalIgnoreCase) && !field.Name.Contains("Material") &&
                                               !field.Name.Contains("Shape") && field.Value.Trim().Length > 0;

    /// <summary>The effects (Guerrilla's names) of the footsteps Guerrilla's vehicles name, for the world builder.</summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaTables)
    {
        var footsteps = Guerrilla(guerrillaTables);
        var names = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("vehicles.xtbl")!)).Entries
            .Select(v => v.Element("Rig")?.ValueOf("Footsteps")?.Trim()).OfType<string>().Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
        var effects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
            if (footsteps.TryGetValue(name, out var entry))
                foreach (var field in Leaves(entry).Where(IsEffect)) effects.Add(field.Value.Trim());
        return [.. effects];
    }

    /// <summary>Our name for one of Guerrilla's footsteps entries.</summary>
    public static string OurName(string name, Packfile gameTables) =>
        XtblFile.FromBytes(gameTables.Read(gameTables.Find(Table)!)).Entries.Any(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase)) ? "rfg_" + name : name;

    /// <summary>Writes the table with Guerrilla's entries <paramref name="names"/> (Guerrilla's names); a line for the log.</summary>
    public static string Write(Packfile guerrillaTables, Packfile gameTables, string modFolder, string vehiclesModFolder, IEnumerable<string> names, ISet<string> worldEffects)
    {
        string path = Path.Combine(modFolder, Table), ownList = Path.Combine(vehiclesModFolder, OwnRows);
        var table = File.Exists(path) ? XtblFile.FromBytes(File.ReadAllBytes(path)) : XtblFile.FromBytes(gameTables.Read(gameTables.Find(Table)!));
        var old = File.Exists(ownList) ? File.ReadAllLines(ownList).Select(l => l.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        foreach (var entry in table.Entries.Where(e => old.Contains(XtblFile.EntryName(e))).ToList()) table.RemoveEntry(entry);
        var footsteps = Guerrilla(guerrillaTables);
        var written = new List<string>();
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!footsteps.TryGetValue(name, out var entry)) continue;
            var copy = entry.Copy();
            string ours = OurName(name, gameTables);
            copy.Element("Name")!.Value = ours;
            foreach (var field in Leaves(copy).Where(IsEffect).ToList())
            {
                string effect = GuerrillaEffectTables.Added(field.Value.Trim());
                if (worldEffects.Contains(effect)) field.Value = effect;
                else { missing.Add(effect); field.Remove(); }
            }
            table.Table!.AddElement(copy);
            written.Add(ours);
        }
        Directory.CreateDirectory(modFolder);
        Directory.CreateDirectory(vehiclesModFolder);
        File.WriteAllBytes(path, table.ToBytes());
        File.WriteAllLines(ownList, written);
        return $"Footsteps: {written.Count} of Guerrilla's in {Table} ({string.Join(", ", written)})" +
               (missing.Count > 0 ? $"; effects the world's tables haven't yet (build the world again, then the vehicles): {string.Join(", ", missing)}" : "");
    }
}
