using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's people's footsteps: the entries of footsteps.xtbl its characters name (character.xtbl's
/// FootstepsControl: GUR_PC Mason's, GUR_NPC everyone else's, MAR_NPC the Marauders'). An entry has, for each
/// ground material (and kind of piece: a steel catwalk), the effect of the left and of the right foot walking and
/// running, and the effects of a jump and of a landing; in Guerrilla every one of them is a sound alone (a cue of
/// four waves). Both games' tables are one, and Armageddon's reader takes the jump rows too (its own table has
/// none); Armageddon has entries of the same names with its own Mars effects, which its own levels keep.
///
/// So: the world build adds the entries' effects to the world's effect tables (<see cref="Effects"/>; their sounds
/// come with them, see <see cref="GuerrillaEffectTables"/>) and writes Guerrilla's entries into the mod's
/// footsteps.xtbl under "rfg_" + their names (<see cref="Write"/>: Armageddon's rows and any other part's kept,
/// this part's own listed in <see cref="OwnRows"/>), which guerrilla_world hands the game. The people's build
/// then has each of Guerrilla's characters name our entry (<see cref="NameFor"/>).
///
/// Guerrilla's entry also names up to four cues of its own, played with every step of a kind (Walk, Run, Jump,
/// Land: Mason's backpack on a run, a jump and a landing; a Marauder's cloth and gear). Armageddon's table has no
/// such fields, so each rides on the effects of its kind: those effects' events play it too
/// (<see cref="AlsoPlays"/>, <see cref="GuerrillaSoundBanks.EffectSound"/>).
/// </summary>
public static class GuerrillaFootsteps
{
    /// <summary>The table, and this part's list of its own rows in the mod's folder.</summary>
    public const string Table = "footsteps.xtbl", OwnRows = "people_footsteps.txt";

    /// <summary>A row's fields that name an effect, by what the foot does.</summary>
    static readonly (string Kind, string[] Fields)[] Kinds =
    [
        ("Walk", ["LeftFootstepWalking", "RightFootstepWalking"]), ("Run", ["LeftFootstepRunning", "RightFootstepRunning"]),
        ("Jump", ["JumpDefault"]), ("Land", ["LandDefault"]),
    ];

    static XtblFile Read(Packfile tables, string table) =>
        XtblFile.FromBytes(tables.Read(tables.Find(table) ?? throw new InvalidDataException($"no {table} among the tables")));

    /// <summary>Guerrilla's entries that its characters name, by name.</summary>
    static SortedDictionary<string, XtblElement> People(Packfile guerrillaTables)
    {
        var named = Read(guerrillaTables, "character.xtbl").Entries.Select(c => c.ValueOf("FootstepsControl")?.Trim()).OfType<string>()
            .Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var people = new SortedDictionary<string, XtblElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Read(guerrillaTables, Table).Entries)
            if (named.Contains(XtblFile.EntryName(entry))) people.TryAdd(XtblFile.EntryName(entry), entry);
        return people;
    }

    static IEnumerable<XtblElement> Leaves(XtblElement e) => e.Elements.SelectMany(c => c.HasElements ? Leaves(c) : [c]);

    /// <summary>What kind of step a field's effect is for (Walk, Run, Jump, Land), or null: not an effect's field.</summary>
    static string? KindOf(XtblElement field) =>
        field.Value.Trim().Length == 0 ? null : Kinds.FirstOrDefault(k => k.Fields.Contains(field.Name, StringComparer.OrdinalIgnoreCase)).Kind;

    /// <summary>The effects (Guerrilla's names) the people's footsteps name, for the world builder.</summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaTables) =>
        [.. People(guerrillaTables).Values.SelectMany(Leaves).Where(f => KindOf(f) != null).Select(f => f.Value.Trim())
              .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Each effect (Guerrilla's name) whose sound has a second cue played with it: the entry's own cue for that
    /// kind of step. An effect an entry uses for two kinds (Guerrilla's catwalk row names a walking effect for the
    /// right foot running) gets none.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AlsoPlays(Packfile guerrillaTables)
    {
        var also = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in People(guerrillaTables).Values)
        {
            var kindsOf = Leaves(entry).Where(f => KindOf(f) != null).GroupBy(f => f.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(f => KindOf(f)!).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var (effect, kinds) in kindsOf)
                if (kinds.Count == 1 && entry.ValueOf(kinds[0])?.Trim() is { Length: > 0 } cue) also.TryAdd(effect, cue);
        }
        return also;
    }

    /// <summary>Our name for one of Guerrilla's entries (Armageddon has entries of the same names).</summary>
    public static string OurName(string name) => "rfg_" + name;

    static (string Path, DateTime Written, HashSet<string> Names)? read;

    /// <summary>
    /// What one of Guerrilla's characters names for its footsteps: our entry where the mod's table has it, else
    /// Guerrilla's own name (Armageddon's entry of that name, its Mars steps, until the world is built).
    /// </summary>
    public static string NameFor(string name, string modFolder)
    {
        string path = Path.Combine(modFolder, Table);
        if (!File.Exists(path)) return name;
        var written = File.GetLastWriteTimeUtc(path);
        if (read is not { } known || known.Path != path || known.Written != written)
            read = known = (path, written, XtblFile.FromBytes(File.ReadAllBytes(path)).Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return known.Names.Contains(OurName(name)) ? OurName(name) : name;
    }

    /// <summary>
    /// Writes Guerrilla's people's entries into the mod's table; <paramref name="worldEffects"/> are the names in the
    /// world's built effects.xtbl (an effect not among them is left out of its row). A line for the log.
    /// </summary>
    public static string Write(Packfile guerrillaTables, Packfile gameTables, string modFolder, IReadOnlySet<string> worldEffects)
    {
        string path = Path.Combine(modFolder, Table), ownList = Path.Combine(modFolder, OwnRows);
        var table = File.Exists(path) ? XtblFile.FromBytes(File.ReadAllBytes(path)) : Read(gameTables, Table);
        var old = File.Exists(ownList) ? File.ReadAllLines(ownList).Select(l => l.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        foreach (var entry in table.Entries.Where(e => old.Contains(XtblFile.EntryName(e))).ToList()) table.RemoveEntry(entry);
        var written = new List<string>();
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int effects = 0;
        foreach (var (name, entry) in People(guerrillaTables))
        {
            var copy = entry.Copy();
            copy.Element("Name")!.Value = OurName(name);
            // Guerrilla's own cues of the entry: Armageddon's table has no such fields (they play with the effects, see AlsoPlays).
            foreach (var (kind, _) in Kinds) copy.Element(kind)?.Remove();
            // Its rows. One naming an effect the world's tables haven't is left out whole (the game requires a row's
            // effects, and stops at start-up on a row without one).
            foreach (var row in new[] { "FootstepsControl", "JumpControl" }.SelectMany(grid => copy.Element(grid)?.Elements ?? []).ToList())
            {
                var fields = row.Elements.Where(f => KindOf(f) != null).ToList();
                var lacking = fields.Select(f => GuerrillaEffectTables.Added(f.Value.Trim())).Where(e => !worldEffects.Contains(e)).ToList();
                if (lacking.Count > 0)
                {
                    missing.UnionWith(lacking);
                    row.Remove();
                    continue;
                }
                foreach (var field in fields) field.Value = GuerrillaEffectTables.Added(field.Value.Trim());
                effects += fields.Count;
            }
            table.Table!.AddElement(copy);
            written.Add(OurName(name));
        }
        Directory.CreateDirectory(modFolder);
        File.WriteAllBytes(path, table.ToBytes());
        File.WriteAllLines(ownList, written);
        read = null;
        return $"Footsteps: {written.Count} of Guerrilla's people's entries in {Table} ({string.Join(", ", written)}), {effects} effects named" +
               (missing.Count > 0 ? $"; ROWS LEFT OUT, the world's tables haven't their effects: {string.Join(", ", missing)}" : "") +
               "; the people's build makes Guerrilla's characters name them";
    }

    /// <summary>Takes this part's rows out of the mod's table again (the file stays for the other parts' rows), and its list.</summary>
    public static void Remove(string modFolder)
    {
        string path = Path.Combine(modFolder, Table), ownList = Path.Combine(modFolder, OwnRows);
        if (!File.Exists(ownList)) return;
        var own = File.ReadAllLines(ownList).Select(l => l.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            var table = XtblFile.FromBytes(File.ReadAllBytes(path));
            foreach (var entry in table.Entries.Where(e => own.Contains(XtblFile.EntryName(e))).ToList()) table.RemoveEntry(entry);
            File.WriteAllBytes(path, table.ToBytes());
        }
        File.Delete(ownList);
        read = null;
    }
}
