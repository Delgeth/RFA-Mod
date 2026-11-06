using System.Text;
using System.Text.RegularExpressions;
using RfaFormats.Textures;

namespace RfaFormats.Interface;

/// <summary>
/// Makes Red Faction: Guerrilla's HUD documents loadable in Armageddon, for the guerrilla_hud mod,
/// from the player's own copy of Guerrilla (read only; nothing converted is ever shipped).
///
/// Both games read the same documents and textures, but give some different pictures the same
/// names (both have a sheet called ui_bms_00, for one), and Guerrilla's HUD scripts read game data
/// Armageddon no longer has. So for each document this:
///   - renames every picture it shows, and every texture they come from, with the prefix "rfg_"
///     (Armageddon's own HUD is left as it is);
///   - gathers those textures into one texture file of its own, interface-&lt;document&gt;;
///   - points the document at our own script (in the mod's folder), which drives Guerrilla's
///     elements from Armageddon's data;
///   - packs the document with the script, and the texture file, into two containers as Armageddon
///     packs its own (&lt;doc&gt;.str2_pc and &lt;doc&gt;2.str2_pc), next to loose copies of the
///     document and script (the game reads each document once at start-up);
///   - and adds the document to the player's own vint_doc_containers.asm_pc (what each container
///     holds) and rfg.vint_proj (the interface project, which must list every document), and the
///     sheets it uses to guerrilla_hud_sheets.xtbl (where each picture is on its sheet): the game's
///     own sheet table with Guerrilla's sheets added, renamed, which rfg.vint_proj names in place
///     of the game's.
/// Everything is written as overrides: new files beside the game's, and replacements for the two
/// lists, made from the game's own each time.
/// </summary>
public static partial class GuerrillaHud
{
    public const string Prefix = "rfg_";

    /// <summary>
    /// The documents converted, and the pictures their scripts show besides those the document names.
    /// The mod loads them together in Guerrilla's world, so a texture one of them brings isn't brought
    /// again by a later one: rfg_ui_hud_health brings the sheet ui_bms_00 the others' frames are cut
    /// from (and rfg_ui_hud_reticles' reticles, rfg_ui_hud_weapon_sel's plates), rfg_ui_hud (the
    /// minimap) the map sheet ui_bms_maps,
    /// rfg_ui_fullscreen (the world map) Guerrilla's world map picture.
    /// </summary>
    public static readonly IReadOnlyList<HudDocument> Documents =
    [
        new("rfg_ui_hud_health", ["ui_hud_blood_gradient", "ui_hud_radiation"]),
        new("rfg_ui_hud_reticles", ["ui_hud_reti_assault"], GuerrillaScript: true),
        new("rfg_ui_hud_weapon_sel", [], GuerrillaScript: true),
        new("rfg_ui_hud", ["ui_hud_base_square", "ui_map_icon_player"]),
        new("rfg_ui_fullscreen", ["ui_hud_base_square", "ui_map_icon_player", WorldMap]),
    ];

    /// <summary>
    /// Guerrilla's picture of its whole world (terr01_l0.vpp_pc &gt; terr01_map.str2_pc), 1024 × 1024:
    /// its zone columns and rows 2 to 13, 85⅓ pixels a zone, north up. Its full-screen map shows it.
    /// </summary>
    public const string WorldMap = "terr01_map";

    /// <summary>
    /// Guerrilla's minimap shows each zone's own picture (terr01_l0.vpp_pc &gt; terr01_&lt;col&gt;_&lt;row&gt;_map.str2_pc,
    /// 512 × 512, a metre a pixel, north up), each in a container of its own, which its streaming
    /// loaded with the zone. Here each one comes the same way, in a document of its own that only
    /// brings that texture (rfg_ui_map_&lt;col&gt;_&lt;row&gt;), which the mod loads while the player is near.
    /// (Not rfg_&lt;col&gt;_&lt;row&gt;_map: Armageddon's level streaming still knows that name, &lt;world&gt;_%02d_%02d_map.)
    /// </summary>
    public static string ZoneDocument(int column, int row) => $"rfg_ui_map_{column:00}_{row:00}";
    const int FirstZone = 2, LastZone = 13;

    /// <summary>The districts' names by zone, for the minimap (written next to our scripts, made from the player's copy).</summary>
    public const string DistrictsFile = "districts.txt";
    const string Language = "us";  // Guerrilla's strings files: territories_us.le_strings, ...

    /// <summary>
    /// Pictures from textures that aren't brought over, and one that looks the same that is: both
    /// are a single white, opaque pixel (checked in Guerrilla's sheets).
    /// </summary>
    static readonly Dictionary<string, string> SamePicture = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ui_fend_base_square"] = "ui_hud_base_square",
    };

    static readonly uint ImageHash = VintDocument.Hash("image");
    // Our sheet table, in place of the game's bitmap_sheets. Not named rfg_..., so that taking the
    // world's rfg_* files away never takes it with them (see Remove).
    const string SheetTableName = "guerrilla_hud_sheets";
    const string WholeSheetSuffix = "_sheet";                // a whole-texture picture's texture, made a sheet of one
    const string AlwaysLoaded = "interface-backend";  // in Armageddon's project, loaded from start-up

    /// <summary>
    /// Converts every document in <see cref="Documents"/>. <paramref name="scriptsFolder"/> holds our
    /// scripts (&lt;document&gt;.lua); <paramref name="overridesFolder"/> is the mod's overrides\.
    /// Returns what was done, a line each.
    /// </summary>
    public static List<string> Build(string guerrillaFolder, string gameFolder, string scriptsFolder, string overridesFolder)
    {
        var report = new List<string>();
        string guerrillaCache = Path.Combine(guerrillaFolder, "build", "pc", "cache");
        string gameCache = Path.Combine(gameFolder, "build", "pc", "cache");
        Packfile guerrillaInterface = Packfile.Open(Path.Combine(guerrillaCache, "interface.vpp_pc"));
        Packfile guerrillaMisc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        Packfile gameInterface = Packfile.Open(Path.Combine(gameCache, "interface.vpp_pc"));
        Packfile gameMisc = Packfile.Open(Path.Combine(gameCache, "misc.vpp_pc"));
        if (guerrillaInterface.FormatVersion != Packfile.GuerrillaVersion)
            throw new InvalidDataException($"{guerrillaFolder} isn't Red Faction: Guerrilla (its interface.vpp_pc is version {guerrillaInterface.FormatVersion})");

        string guerrillaSheetsText = ReadText(guerrillaMisc, "bitmap_sheets.xtbl");
        Dictionary<string, string> sheetOf = SheetPictures(guerrillaSheetsText);  // picture -> sheet

        // Guerrilla's world: its map picture, and each zone's own (see ZoneDocument).
        Packfile guerrillaWorld = Packfile.Open(Path.Combine(guerrillaCache, "terr01_l0.vpp_pc"));
        PegFile worldMap = ReadBitmap(guerrillaWorld, WorldMap)
                           ?? throw new InvalidDataException($"Guerrilla's terr01_l0.vpp_pc has no {WorldMap}");

        // Armageddon packs each document in two containers: <doc> (document and script) and <doc>2
        // (textures). Our documents are listed like the flyer's HUD, which the game streams in while
        // playing (type 11), not like ui_hud, which it reads at start-up (type 12, as the HUD's
        // preloaded parts are).
        Packfile documentTemplate = Container(gameInterface, "ui_flyer_hud.str2_pc");
        Packfile textureTemplate = Container(gameInterface, "ui_flyer_hud2.str2_pc");
        AsmFile containers = AsmFile.Read(Read(gameInterface, "vint_doc_containers.asm_pc"));
        AsmContainer documentEntry = containers.Containers.First(c => c.Name.Equals("ui_flyer_hud", StringComparison.OrdinalIgnoreCase));
        AsmContainer textureEntry = containers.Containers.First(c => c.Name.Equals("ui_flyer_hud2", StringComparison.OrdinalIgnoreCase));

        string cacheOut = Path.Combine(overridesFolder, "build", "pc", "cache");
        var sheetsBrought = new List<string>();
        var wholeTextures = new List<(string Name, int Width, int Height)>();  // whole-texture pictures, each made a sheet
        var documentNames = new List<string>();
        var texturesBrought = new HashSet<string>(StringComparer.OrdinalIgnoreCase);  // by an earlier document
        foreach (HudDocument hud in Documents)
        {
            string name = hud.Name;
            string script = Path.Combine(scriptsFolder, name + ".lua");
            if (!File.Exists(script)) throw new FileNotFoundException($"our script for {name} is missing", script);
            var document = VintDocument.Read(Read(guerrillaInterface, name + ".vint_doc"));

            // The textures it can show: those of the texture files it depends on, from Guerrilla's
            // misc.vpp_pc (always loaded there) or the document's own texture container.
            Packfile? guerrillaTextures = guerrillaInterface.Find(name + ".vint_doc2.str2_pc") is { } entry
                ? Packfile.FromBytes(entry.Name, guerrillaInterface.Read(entry)) : null;
            var textures = new Dictionary<string, (PegFile Peg, PegTexture Texture)>(StringComparer.OrdinalIgnoreCase);
            foreach (VintReference dependency in document.Dependencies.Where(d => d.Kind == 0))
            {
                string file = document.Text(dependency);
                PegFile? peg = ReadPeg(guerrillaTextures, file) ?? ReadPeg(guerrillaMisc, file);
                if (peg == null)
                {
                    report.Add($"{name}: Guerrilla's texture file {file} not found; its pictures are left out");
                    continue;
                }
                foreach (PegTexture texture in peg.Textures) textures.TryAdd(Path.GetFileNameWithoutExtension(texture.Name), (peg, texture));
            }
            foreach (PegTexture texture in worldMap.Textures) textures.TryAdd(Path.GetFileNameWithoutExtension(texture.Name), (worldMap, texture));

            // Guerrilla's map elements were drawn by its game code (the cartographer), which
            // Armageddon has only as a compass. Here each is a plain picture (its own picture, the
            // minimap's frame for one), and our script builds the map around it.
            int maps = 0;
            foreach (VintElement element in document.Elements().Where(e => e.Type.Equals("map", StringComparison.OrdinalIgnoreCase)))
            {
                document.Point(element.TypeReference, "bitmap");
                maps++;
            }

            // Each picture, and the texture it comes from (its own, or the sheet it is cut from).
            var needed = new List<string>();
            string? TextureFor(string picture)
            {
                picture = SamePicture.GetValueOrDefault(picture, picture);
                if (textures.ContainsKey(picture)) return picture;
                return sheetOf.TryGetValue(picture, out string? sheet) && textures.ContainsKey(sheet) ? sheet : null;
            }
            var renamed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VintElement element in document.Elements())
                foreach (VintProperty property in element.Properties.Where(p => p.NameHash == ImageHash && p.ValueType == VintDocument.StringValue))
                {
                    string picture = document.Text(property.AsReference);
                    if (TextureFor(picture) is not { } texture)
                    {
                        if (picture.Length > 0 && !picture.Equals("null", StringComparison.OrdinalIgnoreCase)) unknown.Add(picture);
                        continue;
                    }
                    if (!needed.Contains(texture, StringComparer.OrdinalIgnoreCase)) needed.Add(texture);
                    document.Point(property.AsReference, Prefix + SamePicture.GetValueOrDefault(picture, picture));
                    renamed.Add(picture);
                }
            foreach (string picture in hud.ScriptPictures)
                if (TextureFor(picture) is { } texture && !needed.Contains(texture, StringComparer.OrdinalIgnoreCase)) needed.Add(texture);
                else if (TextureFor(picture) == null) report.Add($"{name}: its script's picture {picture} isn't in Guerrilla's textures");
            // Guerrilla's own script, when it runs here too: the pictures it names come along.
            string? guerrillaScript = hud.GuerrillaScript
                ? ReadText(Container(guerrillaInterface, name + ".vint_doc.str2_pc"), name + ".lua") : null;
            if (guerrillaScript != null)
                foreach (Match literal in ScriptString().Matches(guerrillaScript))
                    if (sheetOf.ContainsKey(literal.Groups[1].Value) && TextureFor(literal.Groups[1].Value) is { } texture &&
                        !needed.Contains(texture, StringComparer.OrdinalIgnoreCase)) needed.Add(texture);
            var shared = needed.Where(texturesBrought.Contains).ToList();
            needed.RemoveAll(texturesBrought.Contains);
            foreach (string texture in needed) texturesBrought.Add(texture);

            // One texture file with every texture it needs, renamed. The document waits for its
            // first texture file; any others it named point at one Armageddon always has loaded.
            // A picture that is a whole texture becomes the one picture of a sheet of its own
            // (the texture renamed <picture>_sheet): Armageddon finds sheet pictures through the
            // sheets it ties to their textures at start-up, which works for ours, while a whole
            // texture asked for by name came out as some other bitmap (black, with stray shapes).
            // A document whose textures all came with the documents before it brings none: its
            // texture container is empty, as Armageddon's own ui_fade_to_black2 is, and every
            // texture file it named points at one Armageddon always has loaded.
            string textureFile = "interface-" + name;
            bool ownTextures = needed.Count > 0;
            var dependencies = document.Dependencies.Where(d => d.Kind == 0).ToList();
            for (int i = 0; i < dependencies.Count; i++) document.Point(dependencies[i], i == 0 && ownTextures ? textureFile : AlwaysLoaded);
            bool IsSheet(string texture) => sheetOf.ContainsValue(texture);
            (byte[] pegHeader, byte[] pegData) = ownTextures ? MergePegs(needed.Select(t =>
                (textures[t].Peg, textures[t].Texture, Prefix + t + (IsSheet(t) ? "" : WholeSheetSuffix) + ".tga")).ToList()) : ([], []);
            foreach (string texture in needed)
            {
                if (IsSheet(texture))
                {
                    if (!sheetsBrought.Contains(texture, StringComparer.OrdinalIgnoreCase)) sheetsBrought.Add(texture);
                }
                else if (!wholeTextures.Any(w => w.Name.Equals(texture, StringComparison.OrdinalIgnoreCase)))
                {
                    wholeTextures.Add((texture, textures[texture].Texture.Width, textures[texture].Texture.Height));
                }
            }

            foreach (var (key, value) in document.Metadata)
                if (document.Text(key).Equals("lua_script_file", StringComparison.OrdinalIgnoreCase)) document.Point(value, name + ".lua");

            byte[] documentBytes = document.ToBytes();
            byte[] scriptBytes = guerrillaScript != null
                ? Encoding.Latin1.GetBytes(GuerrillaScriptFor(name, guerrillaScript,
                                                              File.ReadAllText(Path.Combine(scriptsFolder, ShimsScript), Encoding.Latin1),
                                                              File.ReadAllText(script, Encoding.Latin1), sheetOf.Keys))
                : File.ReadAllBytes(script);
            byte[] main = Packfile.CreateContainer(documentTemplate, [(name + ".vint_doc", documentBytes), (name + ".lua", scriptBytes)]);
            byte[] art = Packfile.CreateContainer(textureTemplate, ownTextures
                ? [(textureFile + ".cpeg_pc", pegHeader), (textureFile + ".gpeg_pc", pegData)] : []);
            Write(Path.Combine(cacheOut, name + ".str2_pc"), main);
            Write(Path.Combine(cacheOut, name + "2.str2_pc"), art);
            Write(Path.Combine(cacheOut, name + ".vint_doc"), documentBytes);
            Write(Path.Combine(cacheOut, name + ".lua"), scriptBytes);

            containers.Containers.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                 c.Name.Equals(name + "2", StringComparison.OrdinalIgnoreCase));
            containers.Containers.Add(Entry(documentEntry, name, Packfile.FromBytes(name + ".str2_pc", main),
                [(name + ".vint_doc", 0), (name + ".lua", 1)]));
            containers.Containers.Add(Entry(textureEntry, name + "2", Packfile.FromBytes(name + "2.str2_pc", art),
                ownTextures ? [(textureFile + ".cpeg_pc", 0)] : []));
            documentNames.Add(name);

            report.Add($"{name}: {renamed.Count} pictures renamed, from " +
                       (ownTextures ? $"{needed.Count} textures ({string.Join(", ", needed)}) in {textureFile}" : "no texture of its own") +
                       (hud.GuerrillaScript ? "; Guerrilla's own script" : "") +
                       (shared.Count > 0 ? $", and {string.Join(", ", shared)} from the documents before it" : "") +
                       (maps > 0 ? $"; {maps} map element{(maps == 1 ? "" : "s")} made a picture" : "") +
                       (unknown.Count > 0 ? $"; not found, left as they are: {string.Join(", ", unknown)}" : ""));
        }

        // Each zone's own picture, in a document of its own that brings nothing but that texture.
        int zonePicturesBytes = 0, zonePictures = 0;
        foreach (PackfileEntry entry in guerrillaWorld.Entries)
        {
            Match zone = ZoneMap().Match(entry.Name);
            if (!zone.Success) continue;
            string tile = Path.GetFileNameWithoutExtension(entry.Name);
            PegFile? peg = ReadBitmap(guerrillaWorld, tile);
            if (peg == null || peg.Textures.Count != 1) continue;
            string name = ZoneDocument(int.Parse(zone.Groups[1].Value), int.Parse(zone.Groups[2].Value));
            string textureFile = "interface-" + name;
            (byte[] pegHeader, byte[] pegData) = MergePegs([(peg, peg.Textures[0], Prefix + tile + WholeSheetSuffix + ".tga")]);
            wholeTextures.Add((tile, peg.Textures[0].Width, peg.Textures[0].Height));
            byte[] documentBytes = VintDocument.TexturesOnly(textureFile, name, name + ".lua");
            byte[] scriptBytes = Encoding.Latin1.GetBytes(TilesScript(name));
            byte[] main = Packfile.CreateContainer(documentTemplate, [(name + ".vint_doc", documentBytes), (name + ".lua", scriptBytes)]);
            byte[] art = Packfile.CreateContainer(textureTemplate, [(textureFile + ".cpeg_pc", pegHeader), (textureFile + ".gpeg_pc", pegData)]);
            Write(Path.Combine(cacheOut, name + ".str2_pc"), main);
            Write(Path.Combine(cacheOut, name + "2.str2_pc"), art);
            Write(Path.Combine(cacheOut, name + ".vint_doc"), documentBytes);
            Write(Path.Combine(cacheOut, name + ".lua"), scriptBytes);
            containers.Containers.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                 c.Name.Equals(name + "2", StringComparison.OrdinalIgnoreCase));
            containers.Containers.Add(Entry(documentEntry, name, Packfile.FromBytes(name + ".str2_pc", main),
                [(name + ".vint_doc", 0), (name + ".lua", 1)]));
            containers.Containers.Add(Entry(textureEntry, name + "2", Packfile.FromBytes(name + "2.str2_pc", art), [(textureFile + ".cpeg_pc", 0)]));
            documentNames.Add(name);
            zonePictures++;
            zonePicturesBytes += pegData.Length;
        }
        report.Add($"rfg_ui_map_<column>_<row>: {zonePictures} zones' own pictures, a document each ({zonePicturesBytes / 1024} KB in all)");

        // The districts' names by zone, in the player's language, for the minimap.
        List<string> districts = DistrictLines(guerrillaMisc, report);
        Directory.CreateDirectory(scriptsFolder);
        File.WriteAllLines(Path.Combine(scriptsFolder, DistrictsFile), districts);

        string listsOut = Path.Combine(cacheOut, "interface.vpp_pc");
        Write(Path.Combine(listsOut, "vint_doc_containers.asm_pc"), containers.ToBytes());

        // The sheet table: the game reads its start-up tables from table.vpp_pc held in memory
        // (0xC8E760), past the overrides, so an override of bitmap_sheets.xtbl itself is never
        // seen. And the project names just one sheet table (only its first <bmp_sheet_file> is
        // read, 0xC502F5). So the project names ours instead: the game's own table with
        // Guerrilla's sheets added, under a name that isn't in table.vpp_pc, so that the game looks
        // it up by name and the loader hands over our new file.
        string projectText = ReadText(gameMisc, "rfg.vint_proj");
        string? sheetTable = sheetsBrought.Count + wholeTextures.Count > 0 ? SheetTableName : null;
        if (sheetTable != null)
        {
            var (at, length) = SheetTableInProject(projectText);
            string gameSheets = ReadText(gameMisc, projectText.Substring(at, length) + ".xtbl");
            Write(Path.Combine(cacheOut, sheetTable + ".xtbl"),
                  Encoding.Latin1.GetBytes(WithSheets(gameSheets, guerrillaSheetsText, sheetsBrought, wholeTextures)));
        }
        string miscOut = Path.Combine(cacheOut, "misc.vpp_pc");
        Write(Path.Combine(miscOut, "rfg.vint_proj"), Encoding.Latin1.GetBytes(WithDocuments(projectText, documentNames, sheetTable)));
        foreach (string old in OldFiles(overridesFolder).Where(File.Exists))
        {
            File.Delete(old);
            report.Add($"Removed {Path.GetFileName(old)}, written by an earlier version");
        }
        var wholeNamed = wholeTextures.Where(w => !ZoneMap().IsMatch(w.Name + ".str2_pc")).Select(w => w.Name).ToList();
        var namedDocuments = documentNames.Where(d => !d.StartsWith("rfg_ui_map_", StringComparison.OrdinalIgnoreCase)).ToList();
        report.Add($"Added {string.Join(", ", namedDocuments)}" +
                   (documentNames.Count > namedDocuments.Count ? $" and {documentNames.Count - namedDocuments.Count} zone pictures' documents" : "") +
                   " to vint_doc_containers.asm_pc and rfg.vint_proj" +
                   (sheetTable != null ? $", and Guerrilla's sheets {string.Join(", ", sheetsBrought)} (renamed {Prefix}...) and one-picture " +
                                         $"sheets for {string.Join(", ", wholeNamed)}" +
                                         (zonePictures > 0 ? $" and {zonePictures} zone pictures" : "") +
                                         $" to the game's sheet table as {sheetTable}.xtbl, which rfg.vint_proj now names" : ""));
        return report;
    }

    /// <summary>
    /// Takes away everything <see cref="Build"/> writes into <paramref name="overridesFolder"/>, so
    /// the game is back to its own HUD everywhere. All of it goes together: the project override
    /// names our sheet table, which holds every picture of the game's own HUD too, so the table
    /// must never go while the project override stays. Returns what was removed, a line each.
    /// </summary>
    public static List<string> Remove(string overridesFolder, string? scriptsFolder = null)
    {
        string cacheOut = Path.Combine(overridesFolder, "build", "pc", "cache");
        var files = new List<string>
        {
            Path.Combine(cacheOut, "misc.vpp_pc", "rfg.vint_proj"),
            Path.Combine(cacheOut, "interface.vpp_pc", "vint_doc_containers.asm_pc"),
            Path.Combine(cacheOut, SheetTableName + ".xtbl"),
        };
        foreach (string document in Documents.Select(d => d.Name).Concat(ZoneDocuments()))
            foreach (string file in new[] { ".str2_pc", "2.str2_pc", ".vint_doc", ".lua" })
                files.Add(Path.Combine(cacheOut, document + file));
        if (scriptsFolder != null) files.Add(Path.Combine(scriptsFolder, DistrictsFile));
        files.AddRange(OldFiles(overridesFolder));
        var report = new List<string>();
        foreach (string file in files.Where(File.Exists))
        {
            File.Delete(file);
            report.Add($"Removed {(file.StartsWith(overridesFolder, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(overridesFolder, file) : file)}");
        }
        if (report.Count == 0) report.Add("Nothing of Guerrilla's HUD was installed");
        return report;
    }

    /// <summary>Files earlier versions wrote that this one doesn't: an override of bitmap_sheets.xtbl (never read by the game), and the sheet table's old name.</summary>
    static IEnumerable<string> OldFiles(string overridesFolder)
    {
        string cacheOut = Path.Combine(overridesFolder, "build", "pc", "cache");
        yield return Path.Combine(cacheOut, "misc.vpp_pc", "bitmap_sheets.xtbl");
        yield return Path.Combine(cacheOut, "rfg_bitmap_sheets.xtbl");
        foreach (string quarter in new[] { "nw", "ne", "sw", "se" })  // the zone pictures, four to a document at first
            foreach (string file in new[] { ".str2_pc", "2.str2_pc", ".vint_doc", ".lua" })
                yield return Path.Combine(cacheOut, "rfg_map_tiles_" + quarter + file);
    }

    /// <summary>Every zone's picture's document name, whether Guerrilla has that zone or not.</summary>
    static IEnumerable<string> ZoneDocuments()
    {
        for (int column = FirstZone; column <= LastZone; column++)
            for (int row = FirstZone; row <= LastZone; row++)
                yield return ZoneDocument(column, row);
    }

    static byte[] Read(Packfile archive, string file) =>
        archive.Read(archive.Find(file) ?? throw new FileNotFoundException($"{archive.Name} has no {file}"));

    static string ReadText(Packfile archive, string file) => Encoding.Latin1.GetString(Read(archive, file));

    static Packfile Container(Packfile archive, string file) => Packfile.FromBytes(file, Read(archive, file));

    static PegFile? ReadPeg(Packfile? archive, string file)
    {
        if (archive?.Find(file + ".cpeg_pc") is not { } header || archive.Find(file + ".gpeg_pc") is not { } data) return null;
        return PegFile.Read(archive.Read(header), archive.Read(data));
    }

    /// <summary>Our shims for Guerrilla's scripts (what they call that Armageddon's interface Lua lacks), next to our scripts.</summary>
    public const string ShimsScript = "rfg_guerrilla_shims.lua";

    /// <summary>
    /// A document's script made of Guerrilla's own (read from the player's copy) and ours: our
    /// shims first, then Guerrilla's script with its init and cleanup renamed
    /// &lt;document&gt;_guerrilla_init and _guerrilla_cleanup (ours, last, has the document's own and
    /// calls them), every string that names one of Guerrilla's pictures renamed as the document's
    /// pictures are (rfg_...), its property calls made through the shims (tints differ), and its
    /// document clock calls naming the document, then our script.
    /// </summary>
    public static string GuerrillaScriptFor(string document, string guerrillaScript, string shims, string ours, IEnumerable<string> pictures)
    {
        var pictureNames = new HashSet<string>(pictures, StringComparer.OrdinalIgnoreCase);
        string script = Regex.Replace(guerrillaScript, $@"\b{Regex.Escape(document)}_(init|cleanup)\b", m => $"{document}_guerrilla_{m.Groups[1].Value}");
        script = ScriptString().Replace(script, m => pictureNames.Contains(m.Groups[1].Value) ? $"\"{Prefix}{m.Groups[1].Value}\"" : m.Value);
        // Properties through the shims' rfg_vint_set_property / rfg_vint_get_property: Armageddon's
        // Lua takes and gives tints as 0-255, Guerrilla's scripts as 0-1.
        script = PropertyCall().Replace(script, m => "rfg_" + m.Value);
        // The document's clock: both games read and set the clock of the document whose script is
        // running when no document is named; here each call names its own.
        string thisDocument = $"vint_document_find(\"{document}\")";
        script = GetTimeIndexCall().Replace(script, $"vint_get_time_index({thisDocument})");
        script = SetTimeIndexCall().Replace(script, m => $"vint_set_time_index({m.Groups[1].Value.Trim()}, {thisDocument})");
        const string newline = "\r\n";
        return shims.TrimEnd() + newline + newline +
               $"-- ---- Guerrilla's own script for {document}, from your copy of Guerrilla" + newline +
               script.TrimEnd() + newline + newline +
               $"-- ---- Ours for {document} (guerrilla_hud)" + newline +
               ours;
    }

    /// <summary>The script of a document of zone pictures: nothing to do, its init and cleanup only.</summary>
    static string TilesScript(string document) =>
        $"-- {document}: Guerrilla's zone pictures for its minimap, made by GuerrillaBuilder.exe from your copy of Guerrilla.\r\n" +
        "-- The document only brings them (the guerrilla_hud mod loads it while you're near); the minimap shows them.\r\n" +
        $"function {document}_init()\r\nend\r\n\r\nfunction {document}_cleanup()\r\nend\r\n";

    /// <summary>A single bitmap (&lt;name&gt;.cvbm_pc and .gvbm_pc, laid out as a texture file) in its own container &lt;name&gt;.str2_pc.</summary>
    static PegFile? ReadBitmap(Packfile archive, string name)
    {
        if (archive.Find(name + ".str2_pc") is not { } entry) return null;
        Packfile container = Packfile.FromBytes(entry.Name, archive.Read(entry));
        if (container.Find(name + ".cvbm_pc") is not { } header || container.Find(name + ".gvbm_pc") is not { } data) return null;
        return PegFile.Read(container.Read(header), container.Read(data));
    }

    /// <summary>
    /// Guerrilla's districts by zone, a line each, "CCRR=name": the zones each district of terr01
    /// lists (territories.xtbl, Grid_index), and its name as Guerrilla's HUD shows it, from the
    /// strings file territories_&lt;language&gt;.le_strings by its tag (its Display_name, or
    /// TERR_&lt;its name&gt;; the Tutorial district shows TERR_PARKER), or its own name if that has no
    /// string.
    /// </summary>
    static List<string> DistrictLines(Packfile guerrillaMisc, List<string> report)
    {
        Dictionary<uint, string> strings = guerrillaMisc.Find($"territories_{Language}.le_strings") is { } file
            ? ReadStrings(guerrillaMisc.Read(file)) : [];
        string table = ReadText(guerrillaMisc, "territories.xtbl");
        Match territory = Regex.Match(table, @"<Territory>\s*<Name>terr01</Name>.*?</Territory>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!territory.Success) throw new InvalidDataException("Guerrilla's territories.xtbl has no terr01");
        table = territory.Value;
        var lines = new List<string>
        {
            "; Guerrilla's districts by zone (column and row), as its HUD names them: made by GuerrillaBuilder.exe from",
            "; your copy of Guerrilla (territories.xtbl and its strings). The minimap shows the name of the zone you're in.",
        };
        int count = 0;
        foreach (Match district in DistrictBlocks().Matches(table))
        {
            string name = TagValue(district.Value, "Name") ?? "";
            string tag = TagValue(district.Value, "Display_name") ?? "TERR_" + name.ToUpperInvariant().Replace(' ', '_');
            string shown = strings.GetValueOrDefault(VintDocument.Hash(tag.ToLowerInvariant()), name);
            foreach (Match index in GridIndexes().Matches(district.Value)) lines.Add($"{index.Groups[1].Value}={shown}");
            count++;
        }
        report.Add($"{DistrictsFile}: {count} districts, {lines.Count - 2} zones, names from territories_{Language}.le_strings");
        return lines;
    }

    static string? TagValue(string xml, string tag)
    {
        Match match = Regex.Match(xml, $"<{tag}>([^<]*)</{tag}>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// A strings file (.le_strings): signature (u32), version (u16), count (u32), then per string its
    /// tag's hash (u32, <see cref="VintDocument.Hash"/> of the tag in lower case), its length in
    /// bytes (u16) and the text (UTF-16, NUL-terminated), the next string starting on a 4-byte boundary.
    /// </summary>
    internal static Dictionary<uint, string> ReadStrings(byte[] data)
    {
        var strings = new Dictionary<uint, string>();
        int count = (int)BitConverter.ToUInt32(data, 6);
        int at = 10;
        for (int i = 0; i < count && at + 6 <= data.Length; i++)
        {
            uint hash = BitConverter.ToUInt32(data, at);
            int length = BitConverter.ToUInt16(data, at + 4);
            if (at + 6 + length > data.Length) break;
            strings.TryAdd(hash, Encoding.Unicode.GetString(data, at + 6, length).TrimEnd('\0'));
            at = (at + 6 + length + 3) & ~3;
        }
        return strings;
    }

    static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// A new texture file pair holding these textures under new names: each texture's 48-byte
    /// entry as it was (but where its data starts, and its name's address cleared), its data on a
    /// 16-byte boundary.
    /// </summary>
    static (byte[] Header, byte[] Data) MergePegs(List<(PegFile Peg, PegTexture Texture, string Name)> textures)
    {
        const int headerSize = 0x18, entrySize = 48;
        var data = new MemoryStream();
        var entries = new byte[textures.Count * entrySize];
        var names = new MemoryStream();
        for (int i = 0; i < textures.Count; i++)
        {
            var (peg, texture, name) = textures[i];
            while (data.Length % 16 != 0) data.WriteByte(0);
            byte[] source = peg.HeaderBytes;
            Array.Copy(source, headerSize + texture.Index * entrySize, entries, i * entrySize, entrySize);
            BitConverter.TryWriteBytes(entries.AsSpan(i * entrySize), (uint)data.Length);
            BitConverter.TryWriteBytes(entries.AsSpan(i * entrySize + 20), 0u);  // its name's address: 0 in Armageddon's files, left-over pointers in Guerrilla's
            data.Write(peg.TextureData(texture));
            names.Write(Encoding.Latin1.GetBytes(name));
            names.WriteByte(0);
        }
        var header = new byte[headerSize + entries.Length + names.Length];
        BitConverter.TryWriteBytes(header.AsSpan(0), PegFile.Magic);
        BitConverter.TryWriteBytes(header.AsSpan(4), PegFile.Version);
        BitConverter.TryWriteBytes(header.AsSpan(6), (ushort)0);                  // platform
        BitConverter.TryWriteBytes(header.AsSpan(8), (uint)header.Length);
        BitConverter.TryWriteBytes(header.AsSpan(12), (uint)data.Length);
        BitConverter.TryWriteBytes(header.AsSpan(16), (ushort)textures.Count);    // bitmaps
        BitConverter.TryWriteBytes(header.AsSpan(20), (ushort)textures.Count);    // textures
        BitConverter.TryWriteBytes(header.AsSpan(22), (ushort)16);                // alignment
        entries.CopyTo(header, headerSize);
        names.ToArray().CopyTo(header, headerSize + entries.Length);
        return (header, data.ToArray());
    }

    /// <summary>
    /// An asset list entry for a new container, copied from one of Armageddon's (its type, flags
    /// and its primitives' types): where the data starts, its compressed size, the size of every
    /// file in it, and per primitive (by the template primitive it copies) its file's size and its
    /// g-file's.
    /// </summary>
    static AsmContainer Entry(AsmContainer template, string name, Packfile container, List<(string File, int Like)> primitives)
    {
        var entry = new AsmContainer
        {
            Name = name,
            Type = template.Type,
            Flags = template.Flags,
            DataOffset = (uint)container.DataStart,
            CompressedSize = container.CompressedDataSize,
        };
        entry.Sizes.AddRange(container.Entries.Select(e => (int)e.Size));
        foreach (var (file, like) in primitives)
        {
            AsmPrimitive model = template.Primitives[like];
            string? dataFile = PegFile.DataFileName(file);
            entry.Primitives.Add(new AsmPrimitive
            {
                Name = file,
                Type = model.Type,
                Allocator = model.Allocator,
                Flags = model.Flags,
                SplitIndex = model.SplitIndex,
                HeaderSize = (int)container.Find(file)!.Size,
                DataSize = dataFile != null && container.Find(dataFile) is { } data ? (int)data.Size : 0,
            });
        }
        return entry;
    }

    /// <summary>Picture name -> the sheet it is cut from, from a bitmap_sheets.xtbl.</summary>
    static Dictionary<string, string> SheetPictures(string sheetsText)
    {
        var sheetOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match sheet in SheetBlocks().Matches(sheetsText))
        {
            var pictureNames = NameTags().Matches(sheet.Value).Select(m => m.Groups[2].Value).ToList();
            foreach (string picture in pictureNames.Skip(1)) sheetOf.TryAdd(picture, pictureNames[0]);
        }
        return sheetOf;
    }

    /// <summary>
    /// Armageddon's sheet table with Guerrilla's sheets added before its end, every name in them
    /// (the sheet's and its pictures') prefixed, and a sheet of one picture for each whole texture
    /// (the sheet named after its renamed texture, the picture after the picture), laid out as the
    /// game's own entries are.
    /// </summary>
    static string WithSheets(string gameText, string guerrillaText, List<string> sheets, List<(string Name, int Width, int Height)> wholeTextures)
    {
        var added = new StringBuilder();
        foreach (Match block in SheetBlocks().Matches(guerrillaText))
        {
            string sheet = NameTags().Match(block.Value).Groups[2].Value;
            if (!sheets.Contains(sheet, StringComparer.OrdinalIgnoreCase)) continue;
            added.Append(NameTags().Replace(block.Value, m => m.Groups[1].Value + Prefix + m.Groups[2].Value + m.Groups[3].Value));
        }
        foreach (var (name, width, height) in wholeTextures)
            added.Append($"\t<BitmapSheets>\n\t\t<Name>{Prefix}{name}{WholeSheetSuffix}</Name>\n\t\t<Images>\n\t\t\t<Properties>\n" +
                         $"\t\t\t\t<Name>{Prefix}{name}</Name>\n\t\t\t\t<StartX>0</StartX>\n\t\t\t\t<StartY>0</StartY>\n" +
                         $"\t\t\t\t<ImageWidth>{width}</ImageWidth>\n\t\t\t\t<ImageHeight>{height}</ImageHeight>\n\t\t\t\t</Properties>\n" +
                         "\t\t\t</Images>\n\t\t<_Editor>\n\t\t\t<Category>Entries</Category>\n\t\t\t</_Editor>\n\t\t</BitmapSheets>\n");
        int end = gameText.LastIndexOf("</Table>", StringComparison.OrdinalIgnoreCase);
        if (end < 0) throw new InvalidDataException("the game's sheet table has no </Table>");
        int lineStart = gameText.LastIndexOf('\n', end) + 1;
        string newline = gameText.Contains("\r\n") ? "\r\n" : "\n";
        return gameText[..lineStart] + added.ToString().ReplaceLineEndings(newline) + gameText[lineStart..];
    }

    /// <summary>The sheet table the project names (the game reads only its first &lt;bmp_sheet_file&gt;, 0xC502F5): where its file name is.</summary>
    static (int At, int Length) SheetTableInProject(string projectText)
    {
        int entry = projectText.IndexOf("<bmp_sheet_file>", StringComparison.OrdinalIgnoreCase);
        int start = entry < 0 ? -1 : projectText.IndexOf("<filename>", entry, StringComparison.OrdinalIgnoreCase);
        int end = start < 0 ? -1 : projectText.IndexOf("</filename>", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) throw new InvalidDataException("rfg.vint_proj names no sheet table (<bmp_sheet_file><filename>)");
        start += "<filename>".Length;
        return (start, end - start);
    }

    /// <summary>
    /// Armageddon's interface project with these documents added to the end of its list, and its
    /// sheet table swapped for ours, if there is one.
    /// </summary>
    static string WithDocuments(string projectText, List<string> documents, string? sheetTable)
    {
        string newline = projectText.Contains("\r\n") ? "\r\n" : "\n";
        int end = projectText.LastIndexOf("</documents>", StringComparison.OrdinalIgnoreCase);
        if (end < 0) throw new InvalidDataException("rfg.vint_proj has no </documents>");
        int lineStart = projectText.LastIndexOf('\n', end) + 1;
        var added = new StringBuilder();
        foreach (string document in documents)
            added.Append($"\t<document>{newline}\t\t<filename>{document}.vint_doc</filename>{newline}" +
                         $"\t\t<doc_name>{document}</doc_name>{newline}\t\t</document>{newline}");
        projectText = projectText[..lineStart] + added + projectText[lineStart..];
        if (sheetTable == null) return projectText;
        var (at, length) = SheetTableInProject(projectText);
        return projectText[..at] + sheetTable + projectText[(at + length)..];
    }

    [GeneratedRegex(@"^[ \t]*<BitmapSheets>.*?</BitmapSheets>[ \t]*\r?\n", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SheetBlocks();

    // A name: the opening tag, the name, the closing tag (tags match whatever their case).
    [GeneratedRegex(@"(<Name>)([^<]*)(</Name>)", RegexOptions.IgnoreCase)]
    private static partial Regex NameTags();

    // A zone's own map picture's container in Guerrilla's world archive: column, row.
    [GeneratedRegex(@"^terr01_(\d\d)_(\d\d)_map\.str2_pc$", RegexOptions.IgnoreCase)]
    private static partial Regex ZoneMap();

    [GeneratedRegex(@"<District>.*?</District>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DistrictBlocks();

    [GeneratedRegex(@"<Index>\s*(\d{4})\s*</Index>", RegexOptions.IgnoreCase)]
    private static partial Regex GridIndexes();

    // A Lua string that is a single name ("ui_hud_reti_tank"): the name.
    [GeneratedRegex("\"([A-Za-z0-9_]+)\"")]
    private static partial Regex ScriptString();

    // A call of vint_set_property or vint_get_property (the name, before its "(").
    [GeneratedRegex(@"(?<![A-Za-z0-9_])vint_[sg]et_property(?=\s*\()")]
    private static partial Regex PropertyCall();

    // vint_get_time_index() with no document named.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])vint_get_time_index\s*\(\s*\)")]
    private static partial Regex GetTimeIndexCall();

    // vint_set_time_index(time) with no document named: the time.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])vint_set_time_index\s*\(([^(),]*)\)")]
    private static partial Regex SetTimeIndexCall();
}

/// <param name="ScriptPictures">Pictures its script shows that the document doesn't name (they come along too).</param>
/// <param name="GuerrillaScript">
/// Its script is Guerrilla's own, from the player's copy, between our shims and our script for it
/// (see <see cref="GuerrillaHud.GuerrillaScriptFor"/>); otherwise it is our script alone.
/// </param>
public sealed record HudDocument(string Name, IReadOnlyList<string> ScriptPictures, bool GuerrillaScript = false);
