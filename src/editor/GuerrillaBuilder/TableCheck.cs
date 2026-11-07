using System.Globalization;
using System.IO;
using RfaFormats;
using RfaFormats.Guerrilla;
using RfaFormats.Tables;

namespace GuerrillaBuilder;

/// <summary>
/// Checks every table a build wrote (the .xtbl files in mods\ and overrides\) against the table's own
/// description, as Armageddon's table reader takes them at start-up, so what would stop the game with
/// "Could not find required attribute ...", "... is not a Boolean value" or the like shows up in the
/// build instead. For each entry and each field the description names, recursively (groups, the
/// items of lists and grids, the chosen group of a combo): a required field that is missing, a
/// number that isn't one or is out of range, a choice that isn't one of the description's choices
/// (Armageddon's yes/no fields are choices), a flag that isn't one of its flags.
///
/// The descriptions mark nearly every field required, and Armageddon's own entries leave many of
/// them out without harm; so a problem counts only if no entry of Armageddon's own copy of the
/// table has it (same kind, same field): what the game's own entries do, its reader takes. A table
/// Armageddon hasn't is checked without that allowance.
/// </summary>
static class TableCheck
{
    sealed record Problem(string Kind, string Path, string Entry, string Detail)
    {
        public string Signature => Kind + " " + Path;

        /// <summary>
        /// A missing required field always stops the game's reader ("Could not find required attribute",
        /// and the next value read in its place: "... is not a Boolean value"); the rest (a choice, flag or
        /// number the description doesn't allow) stop it for some tables and not others, so they're
        /// listed to look into but don't fail the build.
        /// </summary>
        public bool Stops => Kind is "missing" or "too long:";
    }

    /// <summary>Checks the tables; logs what it finds. False if any table has a problem the game would stop on.</summary>
    public static bool Run(Setup setup, BuildLog log)
    {
        if (setup.Game == null) return true;
        string cache = Path.Combine(setup.Game, "build", "pc", "cache");
        var archives = new[] { "misc.vpp_pc", "table.vpp_pc" }
            .Select(a => Path.Combine(cache, a)).Where(File.Exists).Select(Packfile.Open).ToList();
        var files = new List<string>();
        foreach (string folder in new[] { Path.Combine(setup.ModFolder, "mods"), setup.Overrides })
            if (Directory.Exists(folder)) files.AddRange(Directory.EnumerateFiles(folder, "*.xtbl", SearchOption.AllDirectories));

        int checkedTables = 0, bad = 0, doubtful = 0;
        foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            XtblFile table;
            try { table = XtblFile.FromBytes(File.ReadAllBytes(file)); }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException)
            {
                log.Line($"Table check: {Relative(setup, file)} can't be read: {e.Message}");
                bad++;
                continue;
            }
            if (table.Description == null) continue;
            checkedTables++;

            // What Armageddon's own entries of this table get away with (a table written under a name of
            // its own is compared with the game's table it copies).
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string name = Path.GetFileName(file);
            if (RenamedTables.TryGetValue(name, out string? original)) name = original;
            foreach (var archive in archives)
                if (archive.Find(name) is { } entry)
                {
                    var own = XtblFile.FromBytes(archive.Read(entry));
                    if (own.Description == null) break;
                    foreach (var e in own.Entries) foreach (var p in CheckEntry(e, own.Description.Children)) allowed.Add(p.Signature);
                    break;
                }

            var problems = table.Entries.SelectMany(e => CheckEntry(e, table.Description.Children))
                                        .Where(p => !allowed.Contains(p.Signature)).ToList();
            if (problems.Count == 0) continue;
            var stopping = problems.Where(p => p.Stops).ToList();
            var others = problems.Where(p => !p.Stops).ToList();
            if (stopping.Count > 0)
            {
                bad++;
                Report(log, $"Table check: {Relative(setup, file)}: {stopping.Select(p => p.Entry).Distinct().Count()} entries the game's reader stops on:", stopping);
            }
            if (others.Count > 0)
            {
                doubtful++;
                Report(log, $"Table check: {Relative(setup, file)}: {others.Select(p => p.Entry).Distinct().Count()} entries with values Armageddon's description doesn't allow (to look into: the reader stops on some):", others);
            }
        }
        if (!SquadsCheck(setup, archives, log)) bad++;
        log.Line(bad == 0
            ? $"Table check: none of the {checkedTables} tables lacks a field the game requires" + (doubtful > 0 ? $"; {doubtful} have values to look into (above)" : "")
            : $"Table check: {bad} of {checkedTables} tables have entries the game stops on at start-up (above)" + (doubtful > 0 ? $"; {doubtful} have values to look into" : ""));
        return bad == 0;
    }

    /// <summary>
    /// The squads the world's tables (mods\guerrilla_world\) hand the game, as its squad and loadout readers take them
    /// (GuerrillaSquads.SquadProblems): the loadouts past the ones the game holds, loadouts whose weapon it hasn't,
    /// and squads naming a loadout or character it won't have, each of which it turns down without a word until a zone
    /// names the squad ("invalid squad definition"). False if any.
    /// </summary>
    static bool SquadsCheck(Setup setup, List<Packfile> archives, BuildLog log)
    {
        string world = Path.Combine(setup.ModFolder, "mods", "guerrilla_world");
        string squadsFile = Path.Combine(world, GuerrillaSquads.SquadTable), loadoutsFile = Path.Combine(world, GuerrillaSquads.LoadoutTable);
        if (!File.Exists(squadsFile) || !File.Exists(loadoutsFile)) return true;
        XtblFile? Game(string name) => archives.Select(a => a.Find(name) is { } e ? XtblFile.FromBytes(a.Read(e)) : null).FirstOrDefault(t => t != null);
        XtblFile? Ours(string name) => File.Exists(Path.Combine(world, name)) ? XtblFile.FromBytes(File.ReadAllBytes(Path.Combine(world, name))) : Game(name);
        HashSet<string>? Names(XtblFile? table) => table?.Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var squads = XtblFile.FromBytes(File.ReadAllBytes(squadsFile));
        var problems = GuerrillaSquads.SquadProblems(squads, XtblFile.FromBytes(File.ReadAllBytes(loadoutsFile)),
                                                     Names(Ours("weapons.xtbl")), Names(Ours("character.xtbl")), Names(Game(GuerrillaSquads.SquadTable)));
        if (problems.Count == 0)
        {
            log.Line($"Squads check: all {squads.Entries.Count()} squads' members name loadouts and characters the game has");
            return true;
        }
        log.Line($"Squads check: {problems.Count} thing(s) the game would turn down (its squads naming them aren't read):");
        foreach (string problem in problems) log.Line("  " + problem);
        return false;
    }

    static void Report(BuildLog log, string heading, List<Problem> problems)
    {
        log.Line(heading);
        foreach (var group in problems.GroupBy(p => p.Signature).OrderByDescending(g => g.Count()).Take(12))
        {
            var first = group.First();
            var entries = group.Select(p => p.Entry).Distinct().ToList();
            string names = string.Join(", ", entries.Take(3)) + (entries.Count > 3 ? ", ..." : "");
            log.Line($"  {first.Kind} {first.Path}{(first.Detail.Length > 0 ? $" ({first.Detail})" : "")}: {group.Count()} time(s), in {names}");
        }
    }

    static IEnumerable<Problem> CheckEntry(XtblElement entry, IReadOnlyList<FieldDescription> fields)
    {
        string name = XtblFile.EntryName(entry);
        var found = new List<Problem>();
        CheckFields(entry, fields, "", name, found);
        return found;
    }

    static void CheckFields(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, string entry, List<Problem> found)
    {
        foreach (var field in fields)
        {
            string at = path + field.Name;
            var child = element.Element(field.Name);
            if (child == null)
            {
                if (field.Required && field.Type != FieldType.Flags && field.Type != FieldType.List && field.Type != FieldType.Grid)
                    found.Add(new("missing", at, entry, ""));
                continue;
            }
            CheckValue(child, field, at, entry, found);
        }
    }

    static void CheckValue(XtblElement child, FieldDescription field, string at, string entry, List<Problem> found)
    {
        switch (field.Type)
        {
            case FieldType.Int:
            case FieldType.Float:
                if (!child.HasElements && field.Check(child.Value.Trim()) is { } why) found.Add(new("bad value of", at, entry, $"\"{child.Value.Trim()}\" {why}"));
                break;
            case FieldType.Selection:
                if (field.Choices.Count > 0 && !child.HasElements &&
                    !field.Choices.Any(c => c.Equals(child.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                    found.Add(new("not a choice of", at, entry, $"\"{child.Value.Trim()}\"; choices {string.Join("/", field.Choices.Take(6))}"));
                break;
            case FieldType.Flags:
                if (field.Flags.Count > 0)
                    foreach (var flag in child.ElementsNamed("Flag"))
                        if (!field.Flags.Any(f => f.Equals(flag.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                            found.Add(new("unknown flag in", at, entry, $"\"{flag.Value.Trim()}\""));
                break;
            case FieldType.Element:
                CheckFields(child, field.Children, at + "/", entry, found);
                break;
            case FieldType.Vector:
            case FieldType.Color:
                foreach (var part in field.Children)
                    if (child.Element(part.Name) is { } value && !double.TryParse(value.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        found.Add(new("bad value of", at + "/" + part.Name, entry, $"\"{value.Value.Trim()}\" must be a number"));
                break;
            case FieldType.ComboElement:
                // The group chosen: its one child names one of the description's choices.
                foreach (var chosen in child.Elements)
                    if (field.Children.FirstOrDefault(c => c.Name.Equals(chosen.Name, StringComparison.OrdinalIgnoreCase)) is { } choice)
                        CheckFields(chosen, choice.Children, at + "/" + choice.Name + "/", entry, found);
                break;
            case FieldType.List:
            case FieldType.Grid:
                if (field.Item is { } item)
                    foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)))
                        CheckValue(one, item, at + "/" + item.Name, entry, found);
                break;
            default:
                // Text: the reader copies it into a buffer of the description's MaxLength with a checked
                // copy, which ends the game ("invalid parameter", 0xC0000417) when it's longer.
                if (!child.HasElements && field.MaxLength is int most && child.Value.Trim().Length > most)
                    found.Add(new("too long:", at, entry, $"\"{child.Value.Trim()}\" has {child.Value.Trim().Length} characters, at most {most}"));
                break;
        }
    }

    static string Relative(Setup setup, string file) => Path.GetRelativePath(setup.ModFolder, file);

    /// <summary>Tables the parts write under a name of their own, and the game's table each copies.</summary>
    static readonly Dictionary<string, string> RenamedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["guerrilla_hud_sheets.xtbl"] = "bitmap_sheets.xtbl",  // the HUD's sheet table (GuerrillaHud)
    };
}
