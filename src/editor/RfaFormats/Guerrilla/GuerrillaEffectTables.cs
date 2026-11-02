using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's effect tables for a world ported from it: what its destructible objects
/// show when they break, are hit or stand burning. A destructible object names its entry of
/// level_objects.xtbl (its Props); the entry names effects of effects.xtbl (Death_Effect,
/// Collision_impact, Bullet_impact, Custom_Effect, effect_name, Effect; and Death_Explosion, of
/// explosions.xtbl); an effect names its visual (vfx.xtbl) and sound; the visual names its file
/// (Filename, "&lt;file&gt;.effectx") and how it is loaded (Streaming_Category: Preload, from
/// effects.vpp_pc at start-up; Environmental, with the level, from files its archives hold). Both
/// games have all four tables alike, and Armageddon's level_objects.xtbl still names most of
/// Guerrilla's entries, with its own settings and effects.
///
/// So the three tables are Armageddon's own with Guerrilla's entries added under "rfg_" names: each
/// level object the world's objects name, with its effect names made the added ones (or left out
/// where the effect isn't added) and explosions Armageddon lacks left out (its physical materials are
/// all Armageddon's too); each effect they name that has a visual (sound left out: Guerrilla's are
/// XACT cues, which Armageddon's Wwise doesn't know); each visual, Environmental, its file the
/// converted effect "rfg_&lt;file&gt;" (see <see cref="GuerrillaEffects"/>), which the world's
/// containers hold. Every added entry is fitted to the shape of Armageddon's own (see <see cref="Fit"/>:
/// the fields Armageddon's all have added, the lists left without what they must hold gone).
/// Armageddon's own entries stay as they are, so its own levels are as before.
///
/// Room: the game keeps the visuals in an array of <see cref="VisualLimit"/> (0x1200BF4 its count,
/// 0x121FDE8 the entries, 0xB4 bytes each; one past it is left out), which Armageddon's 904 nearly
/// fill; its effects in a pool of <see cref="EffectLimit"/>. The visuals added are those the most
/// placed objects show, as many as fit; the rest, and the effects showing them, wait. Level objects
/// are made as many as the table has. The game reads all three at start-up from its table cache, where
/// the guerrilla_world mod hands it these.
/// </summary>
public static class GuerrillaEffectTables
{
    /// <summary>How many visual effects (vfx.xtbl entries) the game keeps; how many effects (effects.xtbl entries).</summary>
    public const int VisualLimit = 950, EffectLimit = 1224;

    /// <summary>The name an added entry (or a converted effect's file) has: Guerrilla's with "rfg_" before it.</summary>
    public static string Added(string guerrillaName) => "rfg_" + guerrillaName;

    /// <summary>
    /// The tables (Armageddon's with Guerrilla's entries added), each of Guerrilla's level object names
    /// the world uses to its added entry, each to the effect files (Guerrilla's names, without
    /// extension) its added effects show, every effect file to convert, and what didn't fit.
    /// </summary>
    public sealed record Tables(byte[] Visuals, byte[] Effects, byte[] LevelObjects, IReadOnlyDictionary<string, string> PropsOf,
                                IReadOnlyDictionary<string, IReadOnlyList<string>> FilesOf, IReadOnlyList<string> Files,
                                int VisualsAdded, int EffectsAdded, IReadOnlyList<string> VisualsLeft, IReadOnlyList<string> ExplosionsLeft);

    /// <summary>The fields of a level object naming an effect (each holding just the name).</summary>
    static readonly string[] EffectFields =
        ["Death_Effect", "Collision_impact", "Bullet_impact", "Melee_impact", "Custom_Effect", "Prefix_Effect", "Release_Effect", "Startup_Effect", "effect_name", "Effect"];

    /// <summary>Fields naming another entry (an effect, an explosion, a visual, its file, the entry itself), never given a value of Armageddon's (see <see cref="Fit"/>).</summary>
    static readonly HashSet<string> References = [.. EffectFields, "Death_Explosion", "Name", "Filename", "visual"];

    /// <summary>
    /// The tables for the level objects named in <paramref name="used"/> (Guerrilla's names, each with how
    /// many objects name it), from Armageddon's and Guerrilla's misc.vpp_pc; <paramref name="hasFile"/>
    /// says whether Guerrilla's effect file (its name without extension) can be had.
    /// </summary>
    public static Tables Build(Packfile armageddonMisc, Packfile guerrillaMisc, IReadOnlyDictionary<string, int> used, Func<string, bool> hasFile)
    {
        string Text(Packfile archive, string table) =>
            Encoding.Latin1.GetString(archive.Read(archive.Find(table) ?? throw new InvalidDataException($"no {table} in {archive.Name}")));
        string ourVisuals = Text(armageddonMisc, "vfx.xtbl"), ourEffects = Text(armageddonMisc, "effects.xtbl"), ourObjects = Text(armageddonMisc, "level_objects.xtbl");
        var explosions = Entries(Text(armageddonMisc, "explosions.xtbl"), "Explosion").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var objects = Entries(Text(guerrillaMisc, "level_objects.xtbl"), "Level_Object");
        var effects = Entries(Text(guerrillaMisc, "effects.xtbl"), "Effect");
        var visuals = Entries(Text(guerrillaMisc, "vfx.xtbl"), "Effect");
        var takenVisuals = Entries(ourVisuals, "Effect").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenEffects = Entries(ourEffects, "Effect").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenObjects = Entries(ourObjects, "Level_Object").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each used level object's effects, and each effect's visual (where it has one in Guerrilla's vfx.xtbl and its file can be had).
        string? VisualOf(string effect) =>
            effects.TryGetValue(effect, out var e) && Field(e, "visual") is { Length: > 0 } v && visuals.TryGetValue(v, out var x) && hasFile(FileOf(x)) ? v : null;
        var effectsOf = used.Keys.Where(objects.ContainsKey).ToDictionary(p => p,
            p => EffectFields.SelectMany(f => Leaves(objects[p], f)).Where(n => VisualOf(n) != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        // The visuals the most placed objects show, as many as the game has room for.
        var placed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (p, list) in effectsOf)
            foreach (string v in list.Select(e => VisualOf(e)!).Distinct(StringComparer.OrdinalIgnoreCase))
                placed[v] = placed.GetValueOrDefault(v) + used[p];
        // (The entries of its <Table>: the two in <TableTemplates> are the editor's.)
        int visualRoom = Math.Max(0, VisualLimit - Count(ourVisuals, "Effect"));
        var ranked = placed.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key).ToList();
        var addedVisuals = ranked.Take(visualRoom).Where(v => !takenVisuals.Contains(Added(v))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedEffects = effectsOf.Values.SelectMany(l => l).Where(e => addedVisuals.Contains(VisualOf(e)!) && !takenEffects.Contains(Added(e)))
                                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int effectRoom = EffectLimit - Count(ourEffects, "Effect");
        if (addedEffects.Count > effectRoom) throw new InvalidDataException($"{addedEffects.Count} effects to add; the game has room for {effectRoom}");
        var effectSet = addedEffects.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The entries: Guerrilla's, renamed, with the fields above made Armageddon's, then fitted to the
        // shape of Armageddon's own (see Fit).
        var visualShape = new Shape(ourVisuals, "Effect", Text(guerrillaMisc, "vfx.xtbl"));
        var visualText = new StringBuilder();
        foreach (string v in ranked.Where(addedVisuals.Contains))
        {
            string body = Rename(visuals[v], Added(v));
            body = Replace(body, "Filename", Added(FileOf(visuals[v])) + ".effectx");
            body = Replace(body, "Streaming_Category", "Environmental");
            visualText.Append($"\t<Effect>{Fit(body, "Effect", visualShape)}</Effect>\n");
        }
        var effectShape = new Shape(ourEffects, "Effect", Text(guerrillaMisc, "effects.xtbl"));
        var effectText = new StringBuilder();
        foreach (string e in addedEffects)
        {
            string body = Rename(effects[e], Added(e));
            body = Replace(body, "visual", Added(VisualOf(e)!));
            body = Regex.Replace(body, @"\s*<sound>[^<]*</sound>", "");
            effectText.Append($"\t<Effect>{Fit(body, "Effect", effectShape)}</Effect>\n");
        }
        var objectShape = new Shape(ourObjects, "Level_Object", Text(guerrillaMisc, "level_objects.xtbl"));
        var objectText = new StringBuilder();
        var propsOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filesOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var explosionsLeft = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in used.Keys.Where(objects.ContainsKey).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string name = Added(p);
            if (takenObjects.Contains(name)) throw new InvalidDataException($"Armageddon's level_objects.xtbl already has an entry {name}");
            string body = Rename(objects[p], name);
            foreach (string field in EffectFields)
                body = Regex.Replace(body, $@"(\s*)<{field}>([^<]*)</{field}>", m =>
                {
                    string effect = m.Groups[2].Value.Trim();
                    return effect.Length == 0 ? m.Value : effectSet.Contains(effect) ? $"{m.Groups[1].Value}<{field}>{Added(effect)}</{field}>" : "";
                });
            // Explosions Armageddon hasn't left out.
            body = Regex.Replace(body, @"\s*<Death_Explosion>([^<]*)</Death_Explosion>", m =>
            {
                string explosion = m.Groups[1].Value.Trim();
                if (explosion.Length == 0 || explosions.Contains(explosion)) return m.Value;
                explosionsLeft.Add(explosion);
                return "";
            });
            objectText.Append($"\t<Level_Object>{Fit(body, "Level_Object", objectShape)}</Level_Object>\n");
            propsOf[p] = name;
            filesOf[p] = effectsOf[p].Where(effectSet.Contains).Select(e => FileOf(visuals[VisualOf(e)!])).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        var files = addedVisuals.Select(v => FileOf(visuals[v])).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return new Tables(Append(ourVisuals, visualText), Append(ourEffects, effectText), Append(ourObjects, objectText), propsOf, filesOf, files,
                          addedVisuals.Count, addedEffects.Count, ranked.Skip(visualRoom).ToList(), [.. explosionsLeft]);
    }

    /// <summary>A visual's file (its Filename without ".effectx").</summary>
    static string FileOf(string visualEntry) =>
        Path.GetFileNameWithoutExtension(Field(visualEntry, "Filename") ?? throw new InvalidDataException("a visual effect without a file"));

    /// <summary>A table's entries (its &lt;Table&gt;'s elements of that tag), by name, as written (the first of a name).</summary>
    static Dictionary<string, string> Entries(string text, string tag)
    {
        string table = Regex.Match(text, @"<Table>(.*?)</Table>", RegexOptions.Singleline) is { Success: true } t ? t.Groups[1].Value
            : throw new InvalidDataException("a table file without a table");
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(table, $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline))
            if (Field(m.Groups[1].Value, "Name") is { Length: > 0 } name) entries.TryAdd(name, m.Groups[1].Value);
        return entries;
    }

    /// <summary>
    /// What a table's own entries hold, element by element: for each element's tag, the children every
    /// element of that tag has, and each such child's commonest text where it holds one. Tags Armageddon's
    /// entries never have (Guerrilla's Melee_impact, which the game still reads and requires a definition
    /// in) take their shape from Guerrilla's table.
    /// </summary>
    sealed class Shape
    {
        public readonly Dictionary<string, HashSet<string>> Always = [];
        public readonly Dictionary<(string Parent, string Child), string> Commonest = [];

        public Shape(string table, string tag, string guerrillaTable)
        {
            Learn(table, tag);
            var guerrilla = new Shape();
            guerrilla.Learn(guerrillaTable, tag);
            foreach (var (name, always) in guerrilla.Always) Always.TryAdd(name, always);
            foreach (var (key, value) in guerrilla.Commonest) Commonest.TryAdd(key, value);
        }

        Shape() { }

        void Learn(string table, string tag)
        {
            var seen = new Dictionary<string, int>();
            var children = new Dictionary<string, Dictionary<string, int>>();
            var texts = new Dictionary<(string, string), Dictionary<string, int>>();
            foreach (string body in Regex.Matches(Regex.Match(table, @"<Table>(.*?)</Table>", RegexOptions.Singleline).Groups[1].Value,
                                                  $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline).Select(m => m.Groups[1].Value))
                foreach (var e in XElement.Parse($"<{tag}>{body}</{tag}>").DescendantsAndSelf())
                {
                    string name = e.Name.LocalName;
                    seen[name] = seen.GetValueOrDefault(name) + 1;
                    var mine = children.TryGetValue(name, out var c) ? c : children[name] = [];
                    foreach (var child in e.Elements().GroupBy(x => x.Name.LocalName))
                    {
                        mine[child.Key] = mine.GetValueOrDefault(child.Key) + 1;
                        if (!child.First().HasElements)
                        {
                            var counts = texts.TryGetValue((name, child.Key), out var t) ? t : texts[(name, child.Key)] = [];
                            counts[child.First().Value.Trim()] = counts.GetValueOrDefault(child.First().Value.Trim()) + 1;
                        }
                    }
                }
            foreach (var (name, count) in seen)
                Always[name] = [.. children[name].Where(c => c.Value == count).Select(c => c.Key)];
            foreach (var (key, counts) in texts)
                Commonest[key] = counts.MaxBy(c => c.Value).Key;
        }
    }

    /// <summary>
    /// An entry's text fitted to the shape of the table's own entries, which the game may require (a
    /// custom_effects without an effect stopped it while it started: "Could not find required attribute
    /// custom_effects:effect"): where every one of Armageddon's elements of a tag has a child this one
    /// lacks, a child holding a word (Kill_On_Detach, a bullet impact's Bullet_group, an effect's
    /// ignore_gpu_occluded) is added with Armageddon's commonest, and an element missing one that names
    /// another entry (an effect left out, an explosion Armageddon hasn't) or holds others goes, until
    /// none lacks anything. Empty elements are written open and closed, as the game's own.
    /// </summary>
    static string Fit(string body, string tag, Shape shape)
    {
        var entry = XElement.Parse($"<{tag}>{body}</{tag}>");
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var e in entry.DescendantsAndSelf().Reverse().ToList())
            {
                if (e != entry && e.Parent == null) continue;  // gone with its parent
                if (!shape.Always.TryGetValue(e.Name.LocalName, out var always)) continue;
                foreach (string child in always.Where(c => e.Element(c) == null).ToList())
                {
                    if (!References.Contains(child) && shape.Commonest.TryGetValue((e.Name.LocalName, child), out string? value))
                        e.Add(new XElement(child, value));
                    else if (e != entry)
                    {
                        e.Remove();
                        break;
                    }
                    else continue;
                    changed = true;
                }
                if (e != entry && e.Parent == null) changed = true;
            }
        }
        return string.Concat(entry.Nodes().Select(n => Write(n, 2)));
    }

    /// <summary>A node written as the game's tables are: tabs, each element on its line, an empty one open and closed.</summary>
    static string Write(XNode node, int depth)
    {
        if (node is not XElement e) return "";
        string tabs = "\n" + new string('\t', depth), name = e.Name.LocalName;
        if (!e.HasElements) return $"{tabs}<{name}>{System.Security.SecurityElement.Escape(e.Value)}</{name}>";
        return $"{tabs}<{name}>{string.Concat(e.Elements().Select(c => Write(c, depth + 1)))}{tabs}</{name}>";
    }

    /// <summary>How many entries of that tag a table has (names repeated or not).</summary>
    static int Count(string text, string tag) =>
        Regex.Matches(Regex.Match(text, @"<Table>(.*?)</Table>", RegexOptions.Singleline).Groups[1].Value, $@"<{tag}>.*?</{tag}>", RegexOptions.Singleline).Count;

    /// <summary>The first such field's text in an entry, or null.</summary>
    static string? Field(string entry, string field) =>
        Regex.Match(entry, $@"<{field}>([^<]*)</{field}>", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    /// <summary>Every such field's text in an entry (only those holding text).</summary>
    static IEnumerable<string> Leaves(string entry, string field) =>
        Regex.Matches(entry, $@"<{field}>([^<]*)</{field}>").Select(m => m.Groups[1].Value.Trim()).Where(v => v.Length > 0);

    /// <summary>An entry with its (first) name made <paramref name="name"/>.</summary>
    static string Rename(string entry, string name) => new Regex(@"<Name>[^<]*</Name>").Replace(entry, $"<Name>{name}</Name>", 1);

    /// <summary>An entry with its first such field's text made <paramref name="value"/>.</summary>
    static string Replace(string entry, string field, string value) =>
        new Regex($@"<{field}>[^<]*</{field}>", RegexOptions.IgnoreCase).Replace(entry, $"<{field}>{value}</{field}>", 1);

    /// <summary>A table with entries added as its last, before its closing tag, in its own line ends.</summary>
    static byte[] Append(string table, StringBuilder entries)
    {
        int end = table.IndexOf("</Table>", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("a table file without a table");
        int at = table.LastIndexOf('\n', end) + 1;
        string newline = table.Contains("\r\n") ? "\r\n" : "\n";
        return Encoding.Latin1.GetBytes(table[..at] + entries.ToString().Replace("\r\n", "\n").Replace("\n", newline) + table[at..]);
    }
}
