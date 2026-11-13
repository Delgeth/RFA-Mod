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
    public List<string> Make(Mode mode, string template, string name, string displayName, bool blank, string? picture, bool replace = false, bool noGround = false,
                             float size = 255, string? sky = null)
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

        if (blank) MakeBlank(mode, template, name, files, asm, noGround, size, sky);

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
            AsmPrimitive? like = keptPrimitives.FirstOrDefault(p => p.Name.Equals(file, StringComparison.OrdinalIgnoreCase)) ?? kinds.GetValueOrDefault(file) ?? skyKinds.GetValueOrDefault(file);
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
        return ext.Length > 2 && (ext[1] is 'c' or 'C') ? Path.ChangeExtension(header, ".g" + ext[2..]) : null;
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
    void MakeBlank(Mode mode, string template, string name, Dictionary<string, byte[]> files, AsmFile asm, bool noGround, float size, string? sky)
    {
        string terrainName = name + ".cterrain_pc", zoneName = name + ".rfgzone_pc";
        ZoneFile zone = ZoneFile.Read(files[zoneName]);
        // The scenery out first: what's left (starts, spawns, markers) is where the level is.
        var classes = SceneryClasses.Select(NameHashes.ClassHash).ToHashSet();
        var going = zone.Objects.Where(o => classes.Contains(o.ClassHash)).SelectMany(o => zone.WithAttached(o)).Distinct().ToList();
        int taken = going.Count;
        if (going.Count > 0) zone.Remove(going);
        var points = zone.Objects.Where(o => o.ClassHash != NameHashes.ClassHash("object_env_settings")).Select(o => o.Placement.Position).ToList();
        Vector3 middle = points.Count > 0 ? points.Aggregate((a, b) => a + b) / points.Count : Vector3.Zero;
        string groundSaid;
        if (noGround)
        {
            // No ground: the terrain files of a level that has none (Infestation's levels, in their caves,
            // have a terrain file with no height field and empty sub-zones), under this level's name.
            groundSaid = "no ground (" + NoGround(template, name, files, asm) + ")";
        }
        else
        {
            TerrainSculpt old = TerrainSculpt.Load(name, f => files.GetValueOrDefault(f), out string? why)
                                ?? throw new InvalidDataException($"its ground couldn't be read ({why}); a level from {template} can only be made with no ground");
            var before = new TerrainHeightsBefore(old);
            // The new ground's height: the old ground's under the level's objects (they stay as high above it).
            float level = points.Count > 0 ? points.Select(p => before.HeightAt(p.X, p.Z)).Where(h => !float.IsNaN(h)).DefaultIfEmpty(middle.Y).Average() : middle.Y;
            (Vector2 centre, float half) = NewGround(mode, name, files, level, size, new Vector2(middle.X, middle.Z));
            foreach (ZoneObject o in zone.Objects)
            {
                Vector3 p = o.Placement.Position;
                float under = before.HeightAt(p.X, p.Z);
                float x = Math.Clamp(p.X, centre.X - half + 2, centre.X + half - 2), z = Math.Clamp(p.Z, centre.Y - half + 2, centre.Y + half - 2);
                bool env = o.ClassHash == NameHashes.ClassHash("object_env_settings");
                o.MoveTo(env ? p : new Vector3(x, float.IsNaN(under) ? p.Y : level + (p.Y - under), z));
            }
            middle = new Vector3(centre.X, level, centre.Y);
            groundSaid = $"new ground, {half * 2:0} m square at {level:0.#} m, round the level's starts and spawns (made from nothing: its height field, " +
                         "sub-zones at every grid step, far-away ground, blend and colour maps; no foliage, walls or old roads)";
        }
        // A new, empty stitch list.
        if (files.ContainsKey(name + ".stchlst_pc")) files[name + ".stchlst_pc"] = StitchList.Write([], middle, []);
        // Its sky.
        if (sky != null) SetSky(zone, sky, middle, files, asm);
        files[zoneName] = zone.ToBytes();
        // The groups of its scenery's chunks and pieces, and their files in its cells' groups, out.
        int groups = asm.Containers.RemoveAll(c => c.Type == 14);
        foreach (AsmContainer c in asm.Containers.Where(c => c.Type == 17 && !c.Name.Equals("ns_base", StringComparison.OrdinalIgnoreCase)))
            c.Primitives.RemoveAll(p => Scenery(p.Name) && !p.Name.Contains(template, StringComparison.OrdinalIgnoreCase) && !p.Name.Contains("sky", StringComparison.OrdinalIgnoreCase));
        report.Add($"blank: {groundSaid}, an empty stitch list, {taken} scenery objects and {groups} chunk groups taken out, " +
                   $"{zone.Objects.Count} objects kept (starts, spawns, markers)");
    }

    /// <summary>
    /// The level's ground made from nothing (its terrain file keeps only its materials, which name the
    /// game's ground textures and the level's own blend and colour maps): a <paramref name="size"/> m square
    /// of flat ground at <paramref name="level"/>, centred on <paramref name="root"/> (kept in the zone), as
    /// the height field (every sample; outside the square 300 m down, so the level ends in a drop),
    /// the nine sub-zones (a vertex at every grid step, nothing drawn outside the square), the far-away
    /// ground, a blend map all of its first layer and a colour map of one colour; no foliage, barriers,
    /// occluders, roads or undergrowth; for Ruin no navigation (nothing walks there by itself; Infestation
    /// keeps its start level's, its enemies need one). Returns the square's centre and half width.
    /// </summary>
    (Vector2 Centre, float Half) NewGround(Mode mode, string name, Dictionary<string, byte[]> files, float level, float size, Vector2 root)
    {
        string terrainName = name + ".cterrain_pc";
        byte[] oldTerrain = files[terrainName];
        TerrainFile t = TerrainFile.Read(oldTerrain);
        TerrainHeightField field = TerrainHeightField.Find(oldTerrain) ?? throw new InvalidDataException("its terrain file has no height field to go by");
        int n = field.XSamples, m = field.ZSamples;
        float step = field.Step, width = field.ExtentX;
        var corner = new Vector2(BitConverter.ToSingle(t.Data, 0x04), BitConverter.ToSingle(t.Data, 0x0C));
        float half = Math.Clamp(size, 16, width) / 2;
        var centre = new Vector2(Math.Clamp(root.X, corner.X + half, corner.X + width - half), Math.Clamp(root.Y, corner.Y + half, corner.Y + width - half));
        bool Inside(int i, int j) => MathF.Abs(corner.X + i * step - centre.X) <= half + step * 0.01f && MathF.Abs(corner.Y + j * step - centre.Y) <= half + step * 0.01f;

        // The height field.
        ushort Sample(float h) => (ushort)Math.Clamp(MathF.Round((h - field.Offset) / field.Scale), 0, ushort.MaxValue);
        ushort ground = Sample(level), drop = Sample(level - 300);
        var samples = new ushort[n * m];
        var heights = new float[n * m];
        for (int j = 0; j < m; j++)
            for (int i = 0; i < n; i++)
            {
                samples[j * n + i] = Inside(i, j) ? ground : drop;
                heights[j * n + i] = samples[j * n + i] * field.Scale + field.Offset;
            }
        float top = ground * field.Scale + field.Offset, bottom = drop * field.Scale + field.Offset;
        t.HeightField = TerrainHeightField.Section(n, m, step, field.InverseStep, field.ExtentX, field.ExtentZ, field.Offset, field.Scale, samples);
        // The far-away ground, from the heights.
        var (records, blocks, farData) = RfaFormats.Guerrilla.FarGround.Build(heights, n, step, corner, width);
        t.FarRecords = records;
        t.FarBlocks.Clear();
        t.FarBlocks.AddRange(blocks);
        // Nothing else of the old ground's.
        (t.BarrierRecords, t.BarrierSection) = TerrainBarriers.Parts([]);
        t.Foliage = null;
        t.FoliageBuffer = null;
        t.FoliageMeshNames = [];
        t.FoliageMeshCount = 0;
        t.Undergrowth = null;
        t.Occluders = [];
        t.StitchNames = [];
        t.StitchPlacements = [];
        if (t.LayerMap is { } map) t.LayerMap = map with { Cells = new byte[map.Cells.Length] };
        void Word(int at, int value) => BitConverter.TryWriteBytes(t.Data.AsSpan(at), value);
        void Float(int at, float value) => BitConverter.TryWriteBytes(t.Data.AsSpan(at), value);
        Float(0x08, bottom); Float(0x14, top);
        Word(0x24, 0); Word(0x78, 0); Word(0x84, 0); Word(0x94, 0);
        Word(0xAC, 0); Word(0xB4, 0); Word(0xBC, 0); Word(0xC4, 0);
        files[terrainName] = t.Write();
        files[name + ".gterrain_pc"] = farData;

        // The sub-zones: each its part of the grid (from where the old one started), from nothing.
        var parts = files.Keys.Where(f => f.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".ctmesh_pc", StringComparison.OrdinalIgnoreCase)).ToList();
        var starts = new List<(string File, int Number, int X0, int Z0, uint Material)>();
        foreach (string file in parts)
        {
            var old = TerrainSubzone.Read(files[file], files[Path.ChangeExtension(file, ".gtmesh_pc")]);
            int number = int.TryParse(file[(name.Length + 1)..^".ctmesh_pc".Length], out int k) ? k : starts.Count;
            starts.Add((file, number, (int)MathF.Round((old.Origin.X - corner.X) / step), (int)MathF.Round((old.Origin.Y - corner.Y) / step), TerrainSubzone.GroundMaterialOf(files[file])));
        }
        var xs = starts.Select(s => s.X0).Append(n - 1).Distinct().Order().ToList();
        var zs = starts.Select(s => s.Z0).Append(m - 1).Distinct().Order().ToList();
        foreach (var (file, number, x0, z0, material) in starts)
        {
            int x1 = xs.First(x => x > x0), z1 = zs.First(z => z > z0);
            var (header, data) = TerrainSubzone.Grid(number, new Vector2(corner.X + x0 * step, corner.Y + z0 * step), x1 - x0, z1 - z0,
                (x, z) => Inside(x0 + x, z0 + z) && Inside(x0 + x + 1, z0 + z) && Inside(x0 + x, z0 + z + 1) && Inside(x0 + x + 1, z0 + z + 1),
                (x, z) => heights[(z0 + z) * n + x0 + x], (_, _) => Vector3.UnitY, material, step);
            files[file] = header;
            files[Path.ChangeExtension(file, ".gtmesh_pc")] = data;
        }

        // The blend map all of the first layer; the colour map one colour (the old one's average).
        foreach (string map in files.Keys.Where(f => f.EndsWith(".cvbm_pc", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            string dataName = Path.ChangeExtension(map, ".gvbm_pc");
            if (!files.TryGetValue(dataName, out byte[]? mapData)) continue;
            try
            {
                PegFile peg = PegFile.Read(files[map], mapData);
                PegTexture texture = peg.Textures[0];
                var image = new RgbaImage(texture.Width, texture.Height);
                bool blend = map.Contains("_alpha", StringComparison.OrdinalIgnoreCase);
                byte r = 0, g = 0, b = 0;
                if (!blend)
                {
                    RgbaImage was = peg.Decode(texture, 256);
                    long sr = 0, sg = 0, sb = 0, count = was.Pixels.Length / 4;
                    for (int p = 0; p < was.Pixels.Length; p += 4) { sr += was.Pixels[p]; sg += was.Pixels[p + 1]; sb += was.Pixels[p + 2]; }
                    (r, g, b) = ((byte)(sr / count), (byte)(sg / count), (byte)(sb / count));
                }
                for (int p = 0; p < image.Pixels.Length; p += 4) { image.Pixels[p] = r; image.Pixels[p + 1] = g; image.Pixels[p + 2] = b; image.Pixels[p + 3] = 255; }
                files[dataName] = peg.WithTextureData(texture, TextureCodec.EncodeWithMips(texture.Format, image, texture.MipLevels));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException)
            {
                report.Add($"{map} couldn't be made new ({ex.Message}); it stays as the start level's");
            }
        }

        // Navigation: none for Ruin.
        if (mode == Mode.Ruin && files.ContainsKey(name + ".navdata_pc")) files[name + ".navdata_pc"] = NavigationFile.None.ToBytes();
        return (centre, half);
    }

    /// <summary>The game's skies (effects.xtbl's effects drawing one: env_..._sky...).</summary>
    public static List<string> Skies(Workspace workspace)
    {
        try
        {
            string table = Encoding.Latin1.GetString(workspace.Read(Tables, "effects.xtbl"));
            return System.Text.RegularExpressions.Regex.Matches(table, @"<Name>(env_[A-Za-z0-9_]*sky[A-Za-z0-9_]*)</Name>").Select(m => m.Groups[1].Value)
                         .Where(s => !s.Contains("lightning") && !s.Contains("bomb")).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return ["env_sur_day_sky"]; }
    }

    /// <summary>
    /// The level's sky: every environment box naming a sky (a camera effect with "sky" in it) names
    /// this one instead, or a box round the whole level is added naming it; and the sky's effect file
    /// and the textures it draws go in the level's ns_base container (the game loads them with the level).
    /// </summary>
    void SetSky(ZoneFile zone, string sky, Vector3 middle, Dictionary<string, byte[]> files, AsmFile asm)
    {
        uint envClass = NameHashes.ClassHash("object_env_settings");
        int named = 0;
        foreach (ZoneObject o in zone.Objects.Where(o => o.ClassHash == envClass))
            foreach (string slot in new[] { "cam_effect_1", "cam_effect_2", "cam_effect_3" })
                if (o.Property(slot) is { } p && p.Text.Contains("sky", StringComparison.OrdinalIgnoreCase)) { p.SetText(sky); named++; }
        if (named == 0)
        {
            if (assets.ZoneSchema?.Class("object_env_settings") is not { } schema) { report.Add($"its sky {sky} couldn't be set: the game's zone schema wasn't found"); return; }
            const float Half = 200, Low = -500, High = 1000;
            ZoneObject box = zone.AddNew(envClass, schema.NewObject(middle), middle + new Vector3(-Half, Low, -Half), middle + new Vector3(Half, High, Half), []);
            box.Property("bb")?.SetValue([.. new[] { -Half, Low, -Half, Half, High, Half }.SelectMany(BitConverter.GetBytes)]);
            if (box.Property("cam_effect_1") is { } effect) effect.SetText(sky);
            else box.AddProperty(ZoneProperty.TextType, NameHashes.PropertyHash("cam_effect_1"), Encoding.Latin1.GetBytes(sky + "\0"));
        }
        // Its files, with the level.
        var added = new List<string>();
        void Add(string header, byte type, byte allocator, byte flags, byte split)
        {
            if (files.ContainsKey(header) || assets.ReadAnywhere(header) is not { } h) return;
            files[header] = h;
            string dataName = Path.ChangeExtension(header, ".g" + Path.GetExtension(header)[2..]);
            if (assets.ReadAnywhere(dataName) is { } d) files[dataName] = d;
            skyKinds[header] = new AsmPrimitive { Name = header, Type = type, Allocator = allocator, Flags = flags, SplitIndex = split };
            added.Add(header);
        }
        Add(sky + ".cefct_pc", 13, 1, 6, 0);
        if (files.GetValueOrDefault(sky + ".cefct_pc") is { } effectFile)
            foreach (string texture in System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(effectFile), @"(?<![A-Za-z0-9_])[A-Za-z0-9_]+\.tga")
                         .Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase))
                Add(Path.ChangeExtension(texture, ".cvbm_pc"), 1, 1, 6, 1);
        report.Add($"its sky: {sky}" + (named > 0 ? $" (named by {named} of its environment boxes)" : " (a new environment box round the level)") +
                   (added.Count > 0 ? $", its files with the level ({string.Join(", ", added)})" : ""));
    }

    /// <summary>The sky's files' kinds, for their asset list entries.</summary>
    readonly Dictionary<string, AsmPrimitive> skyKinds = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>A level the editor made: its file name, the mode whose list has it (null: none, only its files are left), whether its archive is there.</summary>
    public sealed record MadeLevel(string Name, Mode? Mode, bool Archive)
    {
        public override string ToString() =>
            $"{Name}  ({(Mode is { } m ? m.ToString() : "in no map list")}{(Archive ? "" : Mode == null ? ", its edits left" : ", its archive gone")})";
    }

    /// <summary>
    /// The levels the editor has made: those in a mode's list with no archive of the game's, and any
    /// left in no list (a level's archive in overrides\, one archive holding its zone and asset list
    /// under its own name, or only its edits, in edits\).
    /// </summary>
    public static List<MadeLevel> Made(Workspace workspace)
    {
        var made = new List<MadeLevel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Game(string level) => File.Exists(Path.Combine(workspace.GameFolder, Cache + level + ".vpp_pc"));
        bool Ours(string level) => File.Exists(Path.Combine(workspace.OverridesFolder, Cache + level + ".vpp_pc"));
        foreach (Mode mode in new[] { Mode.Ruin, Mode.Infestation })
            foreach (string level in LevelsOf(workspace, mode))
                if (!Game(level) && seen.Add(level)) made.Add(new MadeLevel(level, mode, Ours(level)));
        // In no list: archives of ours that are a level (its zone and asset list under its name).
        string cache = Path.Combine(workspace.OverridesFolder, Cache);
        if (Directory.Exists(cache))
            foreach (string path in Directory.EnumerateFiles(cache, "*.vpp_pc"))
            {
                string level = Path.GetFileNameWithoutExtension(path);
                if (Game(level) || seen.Contains(level)) continue;
                try
                {
                    Packfile pack = Packfile.FromBytes(Path.GetFileName(path), File.ReadAllBytes(path));
                    if (pack.Find(level + ".rfgzone_pc") == null || pack.Find(level + ".asm_pc") == null) continue;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
                seen.Add(level);
                made.Add(new MadeLevel(level, null, true));
            }
        // Only its edits left.
        string edits = Path.Combine(workspace.EditsFolder, Cache);
        if (Directory.Exists(edits))
            foreach (string folder in Directory.EnumerateDirectories(edits, "*.vpp_pc"))
            {
                string level = Path.GetFileNameWithoutExtension(folder);
                if (Game(level) || Ours(level) || seen.Contains(level)) continue;
                if (!File.Exists(Path.Combine(folder, level + ".rfgzone_pc")) && !File.Exists(Path.Combine(folder, level + ".asm_pc"))) continue;
                seen.Add(level);
                made.Add(new MadeLevel(level, null, false));
            }
        return made;
    }

    /// <summary>
    /// Deletes a level the editor made, all of it: its rows in both modes' lists (and Infestation's
    /// pre-game list), its own spawn resources, its name in every language's strings, its pictures
    /// (their containers and their entries in ui_images.asm_pc), and its archive with its edits and
    /// the copies kept of them. Returns what was done.
    /// </summary>
    public List<string> Remove(string name)
    {
        string archive = Cache + name + ".vpp_pc";
        if (File.Exists(Path.Combine(workspace.GameFolder, archive))) throw new InvalidDataException($"{name} is one of the game's own levels");
        var images = new List<string>();
        var resources = new List<string>();
        foreach (Mode mode in new[] { Mode.Ruin, Mode.Infestation })
        {
            var (tableName, row, key) = TableOf(mode);
            string table = Encoding.Latin1.GetString(workspace.Read(Tables, tableName));
            if (LevelFiles.RowValue(table, row, key, name, "resource_reservation") is { Length: > 0 } r) resources.Add(r);
            images.AddRange(new[] { "image_name", "level_image_name", "Load_image" }
                            .Select(t => LevelFiles.RowValue(table, row, key, name, t)).OfType<string>().Where(i => i.Length > 0));
            if (LevelFiles.WithoutRow(table, row, key, name) is { } without)
            {
                workspace.WriteOverride(Tables, tableName, Encoding.Latin1.GetBytes(without));
                report.Add($"{tableName}: its row taken out");
            }
        }
        string pregame = Encoding.Latin1.GetString(workspace.Read(Tables, "rf4_mp_pregame.xtbl"));
        if (LevelFiles.WithoutRow(pregame, "pregame", "Name", name) is { } p)
        {
            workspace.WriteOverride(Tables, "rf4_mp_pregame.xtbl", Encoding.Latin1.GetBytes(p));
            report.Add("rf4_mp_pregame.xtbl: its row taken out");
        }
        // Its own spawn resources (as New level named them: as the level), only if no other level names them.
        string spawn = Encoding.Latin1.GetString(workspace.Read(Tables, "spawn_resource.xtbl"));
        if (resources.Append(name).Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)) && !OthersUse(name))
            if (LevelFiles.WithoutRow(spawn, "spawn_resource_entry", "Name", name) is { } left)
            {
                workspace.WriteOverride(Tables, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(left));
                report.Add($"spawn_resource.xtbl: its spawn resources \"{name}\" taken out");
            }
        // Its name in every language.
        string upper = name.ToUpperInvariant();
        string[] tags = ["RUIN_MAP_" + upper, "RUIN_MAP_" + upper + "_UC", "MP_" + upper + "_NAME", "MP_" + upper + "_NAME_UC"];
        int languages = 0;
        foreach (PackfileEntry entry in workspace.Archive(Misc).Entries.Where(e => e.Name.StartsWith("multiplayer_", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".le_strings", StringComparison.OrdinalIgnoreCase)))
            if (LevelFiles.WithoutStrings(workspace.Read(Misc, entry.Name), tags) is { } fewer)
            {
                workspace.WriteOverride(Misc, entry.Name, fewer);
                languages++;
            }
        if (languages > 0) report.Add($"its name taken out of {languages} languages' multiplayer strings");
        // Its pictures: those of its own (a container New level wrote beside interface.vpp_pc), also
        // by the names New level gives them, should its row be gone already.
        images.AddRange(["ui_ruin_" + name, "ui_mp_lobby_" + name, "ui_mp_load_" + name]);
        var own = images.Distinct(StringComparer.OrdinalIgnoreCase).Where(i => File.Exists(Path.Combine(workspace.OverridesFolder, Cache, i + ".str2_pc"))).ToList();
        AsmFile list = AsmFile.Read(workspace.Read(Interface, "ui_images.asm_pc"));
        int listed = list.Containers.RemoveAll(c => own.Contains(c.Name, StringComparer.OrdinalIgnoreCase) ||
                                                    images.Skip(images.Count - 3).Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        if (listed > 0) workspace.WriteOverride(Interface, "ui_images.asm_pc", list.ToBytes());
        foreach (string image in own) File.Delete(Path.Combine(workspace.OverridesFolder, Cache, image + ".str2_pc"));
        if (own.Count > 0 || listed > 0) report.Add($"its pictures taken out ({string.Join(", ", own)})");
        // Its archive, its edits and the copies kept.
        if (workspace.RemoveNewArchive(archive)) report.Add($"{archive}, its edits and the copies kept of them deleted");
        workspace.RefreshOverrides();
        return report;
    }

    /// <summary>Whether a level other than <paramref name="name"/> names the spawn resources <paramref name="name"/>.</summary>
    bool OthersUse(string name)
    {
        foreach (var (table, row, key) in new[] { TableOf(Mode.Ruin), TableOf(Mode.Infestation), ("missions.xtbl", "mission", "Name") })
        {
            string text;
            try { text = Encoding.Latin1.GetString(workspace.Read(Tables, table)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { continue; }
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, $@"<{row}>.*?</{row}>", System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                string k = System.Text.RegularExpressions.Regex.Match(m.Value, $@"<{key}>([^<]*)</{key}>").Groups[1].Value.Trim();
                string r = System.Text.RegularExpressions.Regex.Match(m.Value, @"<resource_reservation>([^<]*)</resource_reservation>").Groups[1].Value.Trim();
                if (!k.Equals(name, StringComparison.OrdinalIgnoreCase) && r.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
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
