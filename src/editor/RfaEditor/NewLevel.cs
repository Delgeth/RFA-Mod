using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RfaFormats;
using RfaFormats.Levels;
using RfaFormats.Meshes;
using RfaFormats.Textures;
using RfaFormats.Zones;

namespace RfaEditor;

/// <summary>
/// Makes a brand-new level for one of the game's modes (Ruin, Infestation), from one of that mode's
/// levels (its template), and lists it in that mode's map list. See the project's rfa-new-level.md.
///
/// The level: build\pc\cache\&lt;name&gt;.vpp_pc, a new archive in overrides\ (the editor's own: it opens
/// and saves it as any level, its edits kept in edits\). In it, the template's archive with every file
/// of the level's own (its zone, persistent zone, terrain, far ground, sub-zones, stitch list,
/// navigation, blend and colour maps) under the new name, all of them in its ns_base container (as
/// Infestation's levels keep theirs; Ruin's keep theirs loose in dlc01_precache.vpp_pc), and its
/// asset list with every name the same way (its groups name them; ns_precache doesn't any more).
/// The names inside the files: the zones' (their text values) rewritten; the terrain's and
/// sub-zones' names of the blend and colour maps written over in place by a name as long (the new
/// level's texture tag: "x" and a hash, as long as the template's name), as are the textures' own.
///
/// Blank (the default): the template's ground made flat (at its average height: the terrain file,
/// sub-zones and far ground written as the sculptor would), its foliage cleared, its stitch list
/// empty, the objects that make its scenery taken out (movers, lights, effects, damage regions and
/// sound regions), what's left (player starts, spawn nodes, markers, regions the mode uses) moved
/// down or up with the ground under it, and the groups for its scenery's chunks and pieces dropped.
///
/// The mode's list: a copy of the template's row (Ruin: wrecking_crew_maps.xtbl; Infestation:
/// mp_levels.xtbl and rf4_mp_pregame.xtbl) with the new file name, display name (new strings in each
/// language's multiplayer_*.le_strings), unlocked, and the preview picture if one is given (a new
/// image container of its own, as the game's: &lt;image&gt;.str2_pc beside interface.vpp_pc, listed in
/// ui_images.asm_pc). The game reads its tables and strings at start-up.
/// </summary>
sealed class NewLevel
{
    public enum Mode { Ruin, Infestation }

    const string Cache = @"build\pc\cache\";
    const string Tables = Cache + "table.vpp_pc", Misc = Cache + "misc.vpp_pc", Interface = Cache + "interface.vpp_pc";

    public static (string Table, string Row, string Key) TableOf(Mode mode) => mode == Mode.Ruin
        ? ("wrecking_crew_maps.xtbl", "wc_map_list", "file_name")
        : ("mp_levels.xtbl", "mp_level_list", "Filename");

    /// <summary>The levels of a mode (their file names), from its table as the game sees it.</summary>
    public static List<string> LevelsOf(Workspace workspace, Mode mode)
    {
        var (table, row, key) = TableOf(mode);
        try { return LevelFiles.Keys(Encoding.Latin1.GetString(workspace.Read(Tables, table)), row, key); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return []; }
    }

    // The scenery a blank level leaves out (by class).
    static readonly string[] SceneryClasses =
        ["general_mover", "rfg_mover", "obj_light", "object_effect", "effect_streaming_node", "Damage_Region", "object_interior_sound_region", "ambient_sound"];

    readonly Workspace workspace;
    readonly GameAssets assets;
    readonly List<string> report = [];

    public NewLevel(Workspace workspace, GameAssets assets)
    {
        this.workspace = workspace;
        this.assets = assets;
    }

    /// <summary>
    /// Makes the level and its row; returns what was done (a line each), or throws InvalidDataException
    /// saying why it couldn't be. <paramref name="name"/>: the level's file name (lower case letters,
    /// digits and _); <paramref name="picture"/>: a picture file for its preview, or null for the template's.
    /// </summary>
    public List<string> Make(Mode mode, string template, string name, string displayName, bool blank, string? picture, bool replace = false, bool noGround = false)
    {
        string archive = Cache + template + ".vpp_pc", newArchive = Cache + name + ".vpp_pc";
        if (File.Exists(Path.Combine(workspace.GameFolder, newArchive))) throw new InvalidDataException($"the game already has a level {name}");
        if (File.Exists(Path.Combine(workspace.OverridesFolder, newArchive)) && !(replace && workspace.RemoveNewArchive(newArchive)))
            throw new InvalidDataException($"there is already a level {name} in overrides\\ (delete overrides\\{newArchive} to make it again, or choose another name)");
        Packfile source = workspace.Archive(archive);
        var loose = source.Entries.ToDictionary(e => e.Name, e => source.Read(e), StringComparer.OrdinalIgnoreCase);
        if (!loose.TryGetValue(template + ".asm_pc", out byte[]? asmBytes)) throw new InvalidDataException($"{archive} has no {template}.asm_pc");
        AsmFile asm = AsmFile.Read(asmBytes);
        string tag = LevelFiles.TextureTag(name, template.Length);
        bool Own(string file) => file.Contains(template, StringComparison.OrdinalIgnoreCase);
        string Renamed(string file) => file.EndsWith("vbm_pc", StringComparison.OrdinalIgnoreCase)
            ? ReplaceIgnoringCase(file, template, tag) : ReplaceIgnoringCase(file, template, name);

        // ---- The level's own files: from its ns_base container, else wherever the game has them.
        Packfile? nsBase = loose.TryGetValue("ns_base.str2_pc", out byte[]? nsBytes) ? Packfile.FromBytes("ns_base.str2_pc", nsBytes) : null;
        var inBase = nsBase?.Entries.Select(e => (e.Name, Data: nsBase.Read(e))).ToList() ?? new List<(string Name, byte[] Data)>();
        var own = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, data) in inBase.Where(f => Own(f.Name))) own[file] = data;
        var named = asm.Containers.SelectMany(c => c.Primitives).Select(p => p.Name).Where(Own).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string header in named)
        {
            if (!own.ContainsKey(header) && (loose.GetValueOrDefault(header) ?? assets.ReadAnywhere(header)) is { } h) own[header] = h;
            if (DataOf(header) is string dataName && !own.ContainsKey(dataName) && assets.ReadAnywhere(dataName) is { } d) own[dataName] = d;
        }
        foreach (string zone in new[] { template + ".rfgzone_pc", "p_" + template + ".rfgzone_pc" })
            if (loose.TryGetValue(zone, out byte[]? z)) own[zone] = z;
        if (!own.ContainsKey(template + ".cterrain_pc")) throw new InvalidDataException($"{template}.cterrain_pc wasn't found in the game's files");

        // ---- Renamed, and the names inside.
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, data) in own)
        {
            byte[] bytes = data;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".cterrain_pc" or ".ctmesh_pc" or ".cvbm_pc") bytes = LevelFiles.ReplaceSameLength(bytes, template, tag);
            else if (ext == ".rfgzone_pc")
            {
                ZoneFile zone = ZoneFile.Read(bytes);
                LevelFiles.RenameInZone(zone, template, name);
                bytes = zone.ToBytes();
            }
            files[Renamed(file)] = bytes;
        }
        report.Add($"{own.Count} files of {template}'s under the name {name} (its textures as {tag}_...)");

        if (blank) MakeBlank(template, name, files, asm, noGround);

        // ---- The asset list: every name renamed, each own file's sizes as now; ns_base holding them all.
        var ownNow = files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kinds = new Dictionary<string, AsmPrimitive>(StringComparer.OrdinalIgnoreCase);
        foreach (AsmContainer c in asm.Containers)
        {
            foreach (AsmPrimitive p in c.Primitives.Where(p => Own(p.Name)))
            {
                p.Name = Renamed(p.Name);
                if (files.TryGetValue(p.Name, out byte[]? h)) p.HeaderSize = h.Length;
                p.DataSize = DataOf(p.Name) is string dn && files.TryGetValue(dn, out byte[]? d) ? d.Length : p.DataSize;
                kinds.TryAdd(p.Name, p);
            }
            if (c.Name.Equals("ns_precache", StringComparison.OrdinalIgnoreCase)) c.Primitives.RemoveAll(p => ownNow.Contains(p.Name));
        }
        var baseFiles = inBase.Where(f => !Own(f.Name) && (!blank || !Scenery(f.Name))).ToList();
        // (its zone too, as Infestation's levels have theirs: its zone group, 1_c0807, names it, and the
        // game finds it there; the template's was in its precache archive. Not its persistent zone.)
        baseFiles.AddRange(files.Where(f => !f.Key.StartsWith("p_", StringComparison.OrdinalIgnoreCase) || !f.Key.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase))
                                .Select(f => (f.Key, f.Value)));
        byte[] baseBytes = Packfile.CreateContainer(baseFiles);
        var container = Packfile.FromBytes("ns_base.str2_pc", baseBytes);
        AsmContainer baseEntry = asm.Containers.FirstOrDefault(c => c.Name.Equals("ns_base", StringComparison.OrdinalIgnoreCase))
                                 ?? new AsmContainer { Name = "ns_base", Type = 17, Flags = 0x80 };
        if (!asm.Containers.Contains(baseEntry)) asm.Containers.Insert(0, baseEntry);
        var keptPrimitives = baseEntry.Primitives.Where(p => baseFiles.Any(f => f.Item1.Equals(p.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        baseEntry.DataOffset = (uint)container.DataStart;
        baseEntry.CompressedSize = container.CompressedDataSize;
        baseEntry.Sizes.Clear();
        baseEntry.Sizes.AddRange(container.Entries.Select(e => (int)e.Size));
        baseEntry.Primitives.Clear();
        foreach (var (file, data) in baseFiles)
        {
            if (IsDataFile(file)) continue;
            AsmPrimitive? like = keptPrimitives.FirstOrDefault(p => p.Name.Equals(file, StringComparison.OrdinalIgnoreCase)) ?? kinds.GetValueOrDefault(file);
            string? dataName = DataOf(file);
            baseEntry.Primitives.Add(new AsmPrimitive
            {
                Name = file,
                Type = like?.Type ?? (byte)0, Allocator = like?.Allocator ?? (byte)0, Flags = like?.Flags ?? (byte)2, SplitIndex = like?.SplitIndex ?? (byte)0,
                HeaderSize = data.Length,
                DataSize = dataName != null && baseFiles.FirstOrDefault(f => f.Item1.Equals(dataName, StringComparison.OrdinalIgnoreCase)).Item2 is byte[] d ? d.Length : 0,
            });
        }

        // ---- The archive.
        var archiveFiles = new List<(string Name, byte[] Data)> { (name + ".asm_pc", asm.ToBytes()) };
        foreach (var (file, data) in loose)
        {
            if (file.EndsWith(".asm_pc", StringComparison.OrdinalIgnoreCase) || file.Equals("ns_base.str2_pc", StringComparison.OrdinalIgnoreCase)) continue;
            if (Own(file)) continue;  // (the zones: below, renamed)
            archiveFiles.Add((file, data));
        }
        archiveFiles.Add(("ns_base.str2_pc", baseBytes));
        foreach (var (file, data) in files.Where(f => f.Key.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase))) archiveFiles.Add((file, data));
        string path = Path.Combine(workspace.OverridesFolder, newArchive);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Packfile.Write(path, archiveFiles, source.IsCompressed);
        report.Add($"{newArchive} written ({new FileInfo(path).Length / 1024:N0} KB): {baseFiles.Count} files in its ns_base container");

        // ---- Its row in the mode's list, its strings and picture.
        AddToMode(mode, template, name, displayName, picture);
        workspace.RefreshOverrides();
        return report;
    }

    static string ReplaceIgnoringCase(string text, string from, string to) =>
        System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(from), to.Replace("$", "$$"), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The data file (g-file) going with a header file, by the game's naming: .cXXX → .gXXX; null for one without.</summary>
    static string? DataOf(string header)
    {
        string ext = Path.GetExtension(header);
        return ext.Length > 2 && (ext[1] is 'c' or 'C') && !ext.Equals(".cefct_pc", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(header, ".g" + ext[2..]) : null;
    }

    static bool IsDataFile(string file)
    {
        string ext = Path.GetExtension(file);
        return ext.Length > 2 && (ext[1] is 'g' or 'G') && !ext.Equals(".gtodx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A file of a level's scenery (a chunk, a stitch piece, a texture, an effect, and their data files).</summary>
    static bool Scenery(string file) =>
        Path.GetExtension(file).ToLowerInvariant() is ".cchk_pc" or ".gchk_pc" or ".cstch_pc" or ".gstch_pc" or ".cvbm_pc" or ".gvbm_pc" or ".cpeg_pc" or ".gpeg_pc" or ".cefct_pc";

    /// <summary>The blank level (see the class): flat ground, no foliage, an empty stitch list, the scenery out.</summary>
    void MakeBlank(string template, string name, Dictionary<string, byte[]> files, AsmFile asm, bool noGround)
    {
        string terrainName = name + ".cterrain_pc", zoneName = name + ".rfgzone_pc";
        ZoneFile zone = ZoneFile.Read(files[zoneName]);
        TerrainHeightsBefore? before = null;
        float level = 0;
        Vector3 middle;
        string groundSaid;
        if (noGround)
        {
            // No ground: the terrain files of a level that has none (Infestation's levels, in their caves,
            // have a terrain file with no height field and empty sub-zones), under this level's name.
            groundSaid = "no ground (" + NoGround(template, name, files, asm) + ")";
            var points = zone.Objects.Select(o => o.Placement.Position).ToList();
            middle = points.Count > 0 ? points.Aggregate((a, b) => a + b) / points.Count : Vector3.Zero;
        }
        else
        {
            TerrainSculpt ground = TerrainSculpt.Load(name, f => files.GetValueOrDefault(f), out string? why)
                                   ?? throw new InvalidDataException($"its ground couldn't be read ({why}); a level from {template} can only be made with no ground");
            float sum = 0;
            int count = 0;
            for (int i = 0; i <= 32; i++)
                for (int j = 0; j <= 32; j++) { sum += ground.HeightAt(ground.Corner.X + ground.Width * i / 32, ground.Corner.Y + ground.Width * j / 32); count++; }
            level = sum / count;
            before = new TerrainHeightsBefore(ground);  // (the heights before, for what stays on the ground)
            ground.Reshape((x, z, h) => level);
            foreach (var (file, data) in ground.Files()) files[file] = data;
            // No foliage (its marks were where the ground was).
            try
            {
                if (ZoneFoliage.Read(files[terrainName], name) is { } foliage && foliage.Patches.Count > 0)
                {
                    byte[] cleared = (byte[])files[terrainName].Clone();
                    ZoneFoliage.WithMasks(cleared, name, foliage.Patches.ToDictionary(p => (p.X, p.Y, p.Z, p.Layer), _ => 0UL));
                    files[terrainName] = cleared;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { report.Add($"its foliage couldn't be cleared ({ex.Message})"); }
            middle = new Vector3(ground.Corner.X + ground.Width / 2, level, ground.Corner.Y + ground.Width / 2);
            groundSaid = $"its ground flat at {level:0.#} m, no foliage";
        }
        // An empty stitch list.
        if (files.TryGetValue(name + ".stchlst_pc", out byte[]? stitch))
            files[name + ".stchlst_pc"] = StitchList.WriteEdited(stitch, [], middle, []);
        // The scenery out; the rest moved with the ground (with no ground, left where they were).
        var classes = SceneryClasses.Select(NameHashes.ClassHash).ToHashSet();
        var going = zone.Objects.Where(o => classes.Contains(o.ClassHash)).SelectMany(o => zone.WithAttached(o)).Distinct().ToList();
        int taken = going.Count;
        if (going.Count > 0) zone.Remove(going);
        if (before != null)
            foreach (ZoneObject o in zone.Objects)
            {
                Vector3 p = o.Placement.Position;
                float under = before.HeightAt(p.X, p.Z);
                if (float.IsNaN(under)) continue;
                o.MoveTo(p + new Vector3(0, level - under, 0));
            }
        files[zoneName] = zone.ToBytes();
        // The groups of its scenery's chunks and pieces, and their files in its cells' groups, out.
        int groups = asm.Containers.RemoveAll(c => c.Type == 14);
        foreach (AsmContainer c in asm.Containers.Where(c => c.Type == 17 && !c.Name.Equals("ns_base", StringComparison.OrdinalIgnoreCase)))
            c.Primitives.RemoveAll(p => Scenery(p.Name) && !p.Name.Contains(template, StringComparison.OrdinalIgnoreCase));  // (its own textures stay)
        report.Add($"blank: {groundSaid}, an empty stitch list, {taken} scenery objects and {groups} chunk groups taken out, " +
                   $"{zone.Objects.Count} objects kept (starts, spawns, markers)");
    }

    /// <summary>Levels with no ground, whose terrain files a level with none is given (the first found).</summary>
    static readonly string[] GroundlessLevels = ["mp_inf01", "mp_inf02", "mp_inf03", "mp_inf04", "mp_inf05", "mp_inf06", "mp_inf07", "mp_inf08"];

    /// <summary>
    /// The level's terrain file and sub-zones replaced by a groundless level's (no names inside them),
    /// and its blend and colour maps taken out (nothing names them now). Returns whose they are.
    /// </summary>
    string NoGround(string template, string name, Dictionary<string, byte[]> files, AsmFile asm)
    {
        foreach (string from in GroundlessLevels)
        {
            if (from.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            byte[]? header = assets.ReadAnywhere(from + ".cterrain_pc"), data = assets.ReadAnywhere(from + ".gterrain_pc");
            if (header == null || data == null || header.Length > 64 * 1024) continue;  // (a level with ground: its terrain file is far bigger)
            var subzones = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            bool all = true;
            foreach (string own in files.Keys.Where(f => f.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".ctmesh_pc", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                string number = own[(name.Length + 1)..^".ctmesh_pc".Length];
                byte[]? c = assets.ReadAnywhere($"{from}_{number}.ctmesh_pc"), g = assets.ReadAnywhere($"{from}_{number}.gtmesh_pc");
                if (c == null || g == null) { all = false; break; }
                subzones[own] = c;
                subzones[$"{name}_{number}.gtmesh_pc"] = g;
            }
            if (!all) continue;
            files[name + ".cterrain_pc"] = header;
            files[name + ".gterrain_pc"] = data;
            foreach (var (file, bytes) in subzones) files[file] = bytes;
            // Its blend and colour maps: nothing names them now.
            var maps = files.Keys.Where(f => f.EndsWith("vbm_pc", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (string map in maps) files.Remove(map);
            foreach (AsmContainer c in asm.Containers)
                c.Primitives.RemoveAll(p => p.Name.EndsWith("vbm_pc", StringComparison.OrdinalIgnoreCase) && p.Name.Contains(template, StringComparison.OrdinalIgnoreCase));  // (named as the template's still)
            return $"as {from}'s";
        }
        throw new InvalidDataException("no level with no ground was found to take its terrain files from (Infestation's mp_inf01 to mp_inf08)");
    }

    /// <summary>The ground's heights as they were, sampled before it was flattened.</summary>
    sealed class TerrainHeightsBefore
    {
        readonly float[] heights;
        readonly Vector2 corner;
        readonly float width;
        const int N = 128;
        public TerrainHeightsBefore(TerrainSculpt ground)
        {
            corner = ground.Corner;
            width = ground.Width;
            heights = new float[(N + 1) * (N + 1)];
            for (int i = 0; i <= N; i++)
                for (int j = 0; j <= N; j++) heights[j * (N + 1) + i] = ground.HeightAt(corner.X + width * i / N, corner.Y + width * j / N);
        }
        public float HeightAt(float x, float z)
        {
            float u = (x - corner.X) / width * N, v = (z - corner.Y) / width * N;
            if (u < 0 || v < 0 || u > N || v > N) return float.NaN;
            int i = Math.Min((int)u, N - 1), j = Math.Min((int)v, N - 1);
            float fu = u - i, fv = v - j;
            float H(int a, int b) => heights[b * (N + 1) + a];
            return (H(i, j) * (1 - fu) + H(i + 1, j) * fu) * (1 - fv) + (H(i, j + 1) * (1 - fu) + H(i + 1, j + 1) * fu) * fv;
        }
    }

    /// <summary>
    /// The levels the editor has made (in a mode's list, with no archive of the game's): their file
    /// names, and whether their archive is still there.
    /// </summary>
    public static List<(string Name, Mode Mode, bool Archive)> Made(Workspace workspace)
    {
        var made = new List<(string, Mode, bool)>();
        foreach (Mode mode in new[] { Mode.Ruin, Mode.Infestation })
            foreach (string level in LevelsOf(workspace, mode))
            {
                string archive = Cache + level + ".vpp_pc";
                if (File.Exists(Path.Combine(workspace.GameFolder, archive))) continue;
                made.Add((level, mode, File.Exists(Path.Combine(workspace.OverridesFolder, archive))));
            }
        return made;
    }

    /// <summary>
    /// Takes a level the editor made out again: its row in its mode's list (and Infestation's pre-game
    /// row), its own spawn resources, its pictures, and its archive with its edits. Its strings stay
    /// (nothing shows them). Returns what was done.
    /// </summary>
    public List<string> Remove(Mode mode, string name)
    {
        string archive = Cache + name + ".vpp_pc";
        if (File.Exists(Path.Combine(workspace.GameFolder, archive))) throw new InvalidDataException($"{name} is one of the game's own levels");
        var (tableName, row, key) = TableOf(mode);
        string table = Encoding.Latin1.GetString(workspace.Read(Tables, tableName));
        string? resources = LevelFiles.RowValue(table, row, key, name, "resource_reservation");
        var images = new[] { "image_name", "level_image_name", "Load_image" }
                     .Select(t => LevelFiles.RowValue(table, row, key, name, t)).OfType<string>().Where(i => i.Length > 0).ToList();
        if (LevelFiles.WithoutRow(table, row, key, name) is { } without)
        {
            workspace.WriteOverride(Tables, tableName, Encoding.Latin1.GetBytes(without));
            report.Add($"{tableName}: its row taken out");
        }
        if (mode == Mode.Infestation)
        {
            string pregame = Encoding.Latin1.GetString(workspace.Read(Tables, "rf4_mp_pregame.xtbl"));
            if (LevelFiles.WithoutRow(pregame, "pregame", "Name", name) is { } p)
            {
                workspace.WriteOverride(Tables, "rf4_mp_pregame.xtbl", Encoding.Latin1.GetBytes(p));
                report.Add("rf4_mp_pregame.xtbl: its row taken out");
            }
        }
        if (resources != null && resources.Equals(name, StringComparison.OrdinalIgnoreCase))  // (its own, as New level made it)
        {
            string spawn = Encoding.Latin1.GetString(workspace.Read(Tables, "spawn_resource.xtbl"));
            if (LevelFiles.WithoutRow(spawn, "spawn_resource_entry", "Name", name) is { } left)
            {
                workspace.WriteOverride(Tables, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(left));
                report.Add($"spawn_resource.xtbl: its spawn resources \"{name}\" taken out");
            }
        }
        // Its pictures: those it has of its own (a container New level wrote beside interface.vpp_pc).
        var own = images.Where(i => File.Exists(Path.Combine(workspace.OverridesFolder, Cache, i + ".str2_pc"))).ToList();
        if (own.Count > 0)
        {
            AsmFile list = AsmFile.Read(workspace.Read(Interface, "ui_images.asm_pc"));
            list.Containers.RemoveAll(c => own.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
            workspace.WriteOverride(Interface, "ui_images.asm_pc", list.ToBytes());
            foreach (string image in own) File.Delete(Path.Combine(workspace.OverridesFolder, Cache, image + ".str2_pc"));
            report.Add($"its pictures {string.Join(", ", own)} taken out");
        }
        if (workspace.RemoveNewArchive(archive)) report.Add($"{archive} and its edits deleted");
        workspace.RefreshOverrides();
        return report;
    }

    // ---- The mode's list -------------------------------------------------------------------------

    void AddToMode(Mode mode, string template, string name, string displayName, string? picture)
    {
        var (tableName, row, key) = TableOf(mode);
        string table = Encoding.Latin1.GetString(workspace.Read(Tables, tableName));
        string upper = name.ToUpperInvariant();
        var strings = new List<(string Tag, string Text)>();
        var change = new Dictionary<string, string>();
        // Its own spawn resources (spawn_resource.xtbl: what it can spawn), as the template's to start
        // with, so changing them (Spawn resources...) changes no other level's.
        if (LevelFiles.RowValue(table, row, key, template, "resource_reservation") is { Length: > 0 } templateResources)
        {
            string resources = Encoding.Latin1.GetString(workspace.Read(Tables, "spawn_resource.xtbl"));
            if (SpawnResource.Read(resources, templateResources) is { } own)
            {
                own.Name = name;
                workspace.WriteOverride(Tables, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(own.WriteTo(resources, templateResources)));
                change["resource_reservation"] = name;
                report.Add($"spawn_resource.xtbl: its own spawn resources \"{name}\", as {templateResources} (change them with Spawn resources...)");
            }
        }
        if (mode == Mode.Ruin)
        {
            string id = "RUIN_MAP_" + upper;
            strings.Add((id, displayName));
            strings.Add((id + "_UC", displayName.ToUpperInvariant()));
            change["Name"] = displayName;
            change["file_name"] = name;
            change["display_name"] = id;
            change["display_name_uppercase"] = id + "_UC";
            change["is_locked"] = "False";
            change["unlocks_map"] = "";
            if (picture != null && TryPicture(LevelFiles.RowValue(table, row, key, template, "image_name"), "ui_ruin_" + name, picture) is { } image) change["image_name"] = image;
        }
        else
        {
            string id = "MP_" + upper + "_NAME";
            strings.Add((id, displayName));
            strings.Add((id + "_UC", displayName.ToUpperInvariant()));
            change["Name"] = name;
            change["Filename"] = name;
            change["Display_name"] = id;
            change["Display_name_uppercase"] = id + "_UC";
            if (picture != null)
            {
                if (TryPicture(LevelFiles.RowValue(table, row, key, template, "level_image_name"), "ui_mp_lobby_" + name, picture) is { } lobby) change["level_image_name"] = lobby;
                if (TryPicture(LevelFiles.RowValue(table, row, key, template, "Load_image"), "ui_mp_load_" + name, picture) is { } load) change["Load_image"] = load;
            }
        }
        string made = LevelFiles.WithRowCopied(table, row, key, template, name, change) ?? throw new InvalidDataException($"{tableName} has no row for {template}");
        workspace.WriteOverride(Tables, tableName, Encoding.Latin1.GetBytes(made));
        report.Add($"{tableName}: a row for {name} (\"{displayName}\"), copied from {template}'s, unlocked");
        if (mode == Mode.Infestation)
        {
            string pregame = Encoding.Latin1.GetString(workspace.Read(Tables, "rf4_mp_pregame.xtbl"));
            if (LevelFiles.WithRowCopied(pregame, "pregame", "Name", template, name, new Dictionary<string, string> { ["Name"] = name }) is { } p)
            {
                workspace.WriteOverride(Tables, "rf4_mp_pregame.xtbl", Encoding.Latin1.GetBytes(p));
                report.Add("rf4_mp_pregame.xtbl: its pre-game row, as " + template + "'s");
            }
        }
        // The strings, in every language's multiplayer strings.
        int languages = 0;
        foreach (PackfileEntry entry in workspace.Archive(Misc).Entries.Where(e => e.Name.StartsWith("multiplayer_", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".le_strings", StringComparison.OrdinalIgnoreCase)))
        {
            workspace.WriteOverride(Misc, entry.Name, LevelFiles.WithStrings(workspace.Read(Misc, entry.Name), strings));
            languages++;
        }
        report.Add($"its name in {languages} languages' multiplayer strings ({string.Join(", ", strings.Select(s => s.Tag))})");
    }

    /// <summary>The picture made (its name), or null (and why, in the report) if it couldn't be: the level is made without it.</summary>
    string? TryPicture(string? templateImage, string image, string picture)
    {
        try { return MakePicture(templateImage, image, picture); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException
                                       or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException)
        {
            report.Add($"its picture {image} couldn't be made ({ex.Message}); it shows {templateImage ?? "none"}'s");
            return null;
        }
    }

    /// <summary>
    /// A preview picture: the template's image (its container in interface.vpp_pc, its size and
    /// format) with the picture in it (scaled to fill it, its middle kept), as a new image container
    /// beside interface.vpp_pc (&lt;image&gt;.str2_pc), listed in ui_images.asm_pc. Returns its name.
    /// </summary>
    string MakePicture(string? templateImage, string image, string picture)
    {
        if (templateImage == null) throw new InvalidDataException("the template's row names no picture");
        Packfile ui = workspace.Archive(Interface);
        if (ui.Find(templateImage + ".str2_pc") is not { } templateEntry) throw new InvalidDataException($"interface.vpp_pc has no {templateImage}.str2_pc");
        Packfile templateContainer = Packfile.FromBytes(templateEntry.Name, ui.Read(templateEntry));
        byte[] header = templateContainer.Read(templateContainer.Find(templateImage + ".cvbm_pc") ?? throw new InvalidDataException($"{templateImage} has no texture"));
        byte[] data = templateContainer.Read(templateContainer.Find(templateImage + ".gvbm_pc") ?? throw new InvalidDataException($"{templateImage} has no texture data"));
        PegFile peg = PegFile.Read(header, data);
        PegTexture texture = peg.Textures[0];
        RgbaImage pixels = LoadPicture(picture, texture.Width, texture.Height);
        (byte[] newHeader, byte[] newData) = peg.Single(texture, image + ".tga", texture.MipLevels);
        PegFile made = PegFile.Read(newHeader, newData);
        newData = made.WithTextureData(made.Textures[0], TextureCodec.EncodeWithMips(texture.Format, pixels, texture.MipLevels));
        var containerFiles = new List<(string Name, byte[] Data)> { (image + ".cvbm_pc", newHeader), (image + ".gvbm_pc", newData) };
        byte[] bytes = Packfile.CreateContainer(containerFiles);
        string containerPath = Path.Combine(workspace.OverridesFolder, Cache, image + ".str2_pc");
        Directory.CreateDirectory(Path.GetDirectoryName(containerPath)!);
        File.WriteAllBytes(containerPath, bytes);
        // Listed as the template's is.
        AsmFile list = AsmFile.Read(workspace.Read(Interface, "ui_images.asm_pc"));
        AsmContainer like = list.Containers.First(c => c.Name.Equals(templateImage, StringComparison.OrdinalIgnoreCase));
        list.Containers.RemoveAll(c => c.Name.Equals(image, StringComparison.OrdinalIgnoreCase));
        var c = Packfile.FromBytes(image + ".str2_pc", bytes);
        var entry = new AsmContainer { Name = image, Type = like.Type, Flags = like.Flags, DataOffset = (uint)c.DataStart, CompressedSize = c.CompressedDataSize };
        entry.Sizes.AddRange(c.Entries.Select(e => (int)e.Size));
        AsmPrimitive p = like.Primitives[0];
        entry.Primitives.Add(new AsmPrimitive
        {
            Name = image + ".cvbm_pc", Type = p.Type, Allocator = p.Allocator, Flags = p.Flags, SplitIndex = p.SplitIndex,
            HeaderSize = newHeader.Length, DataSize = newData.Length,
        });
        list.Containers.Add(entry);
        workspace.WriteOverride(Interface, "ui_images.asm_pc", list.ToBytes());
        report.Add($"its picture: {image} ({texture.Width} × {texture.Height}, from {Path.GetFileName(picture)}), as {templateImage} is");
        return image;
    }

    /// <summary>A picture file (PNG, JPG, BMP...) scaled to fill <paramref name="width"/> × <paramref name="height"/>, its middle kept.</summary>
    static RgbaImage LoadPicture(string path, int width, int height)
    {
        BitmapDecoder decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        double scale = Math.Max(width / (double)frame.PixelWidth, height / (double)frame.PixelHeight);
        BitmapSource scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        int x = Math.Max(0, (scaled.PixelWidth - width) / 2), y = Math.Max(0, (scaled.PixelHeight - height) / 2);
        BitmapSource cut = new CroppedBitmap(scaled, new System.Windows.Int32Rect(x, y, Math.Min(width, scaled.PixelWidth - x), Math.Min(height, scaled.PixelHeight - y)));
        var bgra = new FormatConvertedBitmap(cut, PixelFormats.Bgra32, null, 0);
        var image = new RgbaImage(width, height);
        var row = new byte[bgra.PixelWidth * 4];
        for (int line = 0; line < Math.Min(height, bgra.PixelHeight); line++)
        {
            bgra.CopyPixels(new System.Windows.Int32Rect(0, line, bgra.PixelWidth, 1), row, row.Length, 0);
            for (int i = 0; i < Math.Min(width, bgra.PixelWidth); i++)
            {
                int o = (line * width + i) * 4;
                image.Pixels[o] = row[i * 4 + 2];
                image.Pixels[o + 1] = row[i * 4 + 1];
                image.Pixels[o + 2] = row[i * 4];
                image.Pixels[o + 3] = row[i * 4 + 3];
            }
        }
        return image;
    }
}
