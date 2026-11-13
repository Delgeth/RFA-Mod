using System.IO;
using RfaFormats;

namespace RfaEditor;

/// <summary>
/// The two folders the editor works with: the game folder (read only, never written to) and the
/// mod folder, bin\, whose overrides\ folder is where edited files go. The editor itself lives in
/// bin\editor\.
///
/// Overrides work as in the loader: overrides\&lt;archive path&gt;\&lt;file&gt; replaces that archive's
/// copy of the file, and also any other archive's copy of the same size (identical copies, such as
/// each table in both table.vpp_pc and misc.vpp_pc).
///
/// Archives that aren't the game's but ours (new ones in overrides\, such as the Guerrilla world's
/// rfg_l1.vpp_pc, which the Guerrilla builder made) have their changes in edits\ instead (beside
/// overrides\, laid out the same way: edits\build\pc\cache\rfg_l1.vpp_pc\1h_c0312.str2_pc), which
/// the loader uses as it does overrides, so the archives stay as they were built. Beside each,
/// &lt;file&gt;.rfabase notes the size and compressed size the archive's copy had: if the world is built
/// again and its copy is different, the change was made to the old one, and the loader and the
/// editor leave it out.
///
/// Older versions of the editor changed those archives themselves (see
/// <see cref="Packfile.RewriteArchive"/>), keeping each file's copy from before in editor_originals\;
/// those changes are still read and put back, and a file new to an archive (which an edit can't
/// add) is still added that way. Building the Guerrilla world again writes the archives anew: then
/// the kept copies are stale, and are forgotten (each archive's size and time are noted when the
/// editor writes it).
///
/// Loose files are listed as archives too (see "Loose files" below): the new files in overrides\
/// outside any archive's folder (the Guerrilla port's characters, vehicles, weapons, sounds and
/// scripts), which the loader gives the game by name, and the mods' own data in mods\.
/// </summary>
sealed class Workspace
{
    readonly Dictionary<string, Packfile> archives = new(StringComparer.OrdinalIgnoreCase);
    (string Key, Packfile Container)? lastContainer;
    Dictionary<string, List<string>>? overrides;  // file name -> override paths, once looked at
    List<(string Archive, string Name)>? assetLists;
    List<string>? allArchives;

    public string GameFolder { get; }
    public string ModFolder { get; }
    public string OverridesFolder => Path.Combine(ModFolder, "overrides");
    /// <summary>Where the editor keeps our archives' own copies of the files it changed in them.</summary>
    public string OriginalsFolder => Path.Combine(ModFolder, "editor_originals");
    /// <summary>Where changes to our own archives go (see the class): edits\ beside overrides\.</summary>
    public string EditsFolder => Path.Combine(ModFolder, "edits");
    const string BaseSuffix = ".rfabase";  // beside an edit: the sizes of the archive's copy it was made to

    string EditPath(string archive, string fileName) => Path.Combine(EditsFolder, archive, fileName);

    /// <summary>The edit of a file of one of our archives, or null: none, or made to a copy the archive no longer has (built again since).</summary>
    string? EditFor(string archive, string fileName)
    {
        string path = EditPath(archive, fileName);
        if (!File.Exists(path)) return null;
        string basePath = path + BaseSuffix;
        if (!File.Exists(basePath)) return path;
        PackfileEntry? now = Archive(archive).Find(fileName);
        string[] sizes = File.ReadAllText(basePath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return now != null && sizes.Length == 2 && sizes[0] == now.Size.ToString() && sizes[1] == now.CompressedSize.ToString() ? path : null;
    }

    public Workspace(string gameFolder, string modFolder)
    {
        GameFolder = gameFolder;
        ModFolder = modFolder;
    }

    /// <summary>bin\: the folder above bin\editor\ when it has rfa_mods.ini, else the editor's own folder.</summary>
    public static string FindModFolder()
    {
        string editorFolder = AppContext.BaseDirectory.TrimEnd('\\');
        string? parent = Path.GetDirectoryName(editorFolder);
        return parent != null && File.Exists(Path.Combine(parent, "rfa_mods.ini")) ? parent : editorFolder;
    }

    /// <summary>
    /// The game folder, found the same way the launcher finds it: GameDir in rfa_mods.ini, or the
    /// mod folder itself when the mod was copied into the game folder. Null if neither works.
    /// </summary>
    public static string? FindGameFolder(string modFolder)
    {
        string? gameDir = IniFile.Read(Path.Combine(modFolder, "rfa_mods.ini"), "Launcher", "GameDir");
        if (RfaFormats.GameFolder.IsGameFolder(gameDir)) return gameDir!.TrimEnd('\\');
        if (RfaFormats.GameFolder.IsGameFolder(modFolder)) return modFolder;
        return null;
    }

    // ---- The game's files ----------------------------------------------------------------------

    /// <summary>
    /// Every archive, as a path relative to the game folder: the game's own, then the new ones in
    /// overrides\ (an archive file there that the game folder hasn't, such as the Guerrilla world's
    /// build\pc\cache\rfg_l0.vpp_pc; the loader gives it to the game as if it were in the game folder),
    /// then the folders of loose files (see <see cref="IsLooseFolder"/>). In this order the game's
    /// own come first, as the game finds a name in its archives before the loader's new files.
    /// </summary>
    public IReadOnlyList<string> Archives() => allArchives ??= [.. RfaFormats.GameFolder.Archives(GameFolder), .. NewArchives(), .. LooseFolders().Keys];

    /// <summary>Where an archive is: in the game folder, or in overrides\ if it is a new one; for loose files, their folder.</summary>
    public string ArchivePath(string archive) =>
        LooseFolders().TryGetValue(archive, out LooseFolder? loose) ? loose.Folder
        : IsNewArchive(archive) ? Path.Combine(OverridesFolder, archive) : Path.Combine(GameFolder, archive);

    /// <summary>True for an archive in overrides\ that the game hasn't.</summary>
    public bool IsNewArchive(string archive)
    {
        if (isNew.TryGetValue(archive, out bool known)) return known;
        return isNew[archive] = !IsLooseFolder(archive) && !File.Exists(Path.Combine(GameFolder, archive)) && File.Exists(Path.Combine(OverridesFolder, archive));
    }
    readonly Dictionary<string, bool> isNew = new(StringComparer.OrdinalIgnoreCase);

    IEnumerable<string> NewArchives()
    {
        string build = Path.Combine(OverridesFolder, "build");
        if (!Directory.Exists(build)) return [];
        return Directory.EnumerateFiles(build, "*.vpp_pc", SearchOption.AllDirectories)
            .Where(path => !Path.GetDirectoryName(path)!.EndsWith(".vpp_pc", StringComparison.OrdinalIgnoreCase))  // not an override of a file in an archive
            .Select(path => Path.GetRelativePath(OverridesFolder, path))
            .Where(archive => !File.Exists(Path.Combine(GameFolder, archive)))
            .OrderBy(archive => archive, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An archive, by its path relative to the game folder (or a folder of loose files). Opened once, then kept.</summary>
    public Packfile Archive(string archive)
    {
        if (!archives.TryGetValue(archive, out Packfile? pack))
        {
            pack = Open(archive)();
            archives[archive] = pack;
        }
        return pack;
    }

    /// <summary>A way to open an archive afresh, apart from this workspace's own (for reading it on another thread).</summary>
    public Func<Packfile> Open(string archive)
    {
        if (LooseFolders().TryGetValue(archive, out LooseFolder? loose))
        {
            string name = archive, folder = loose.Folder;
            return () => Packfile.FromLooseFiles(name, LooseFilesIn(folder, loose.NewFiles ? Path.GetDirectoryName(archive) : null));
        }
        string path = ArchivePath(archive);
        return () => Packfile.Open(path);
    }

    /// <summary>The game's own copy of a file in an archive (never the override). For one of our archives, its copy from before the editor changed it.</summary>
    public byte[] ReadOriginal(string archive, string fileName)
    {
        if (IsLooseFolder(archive))
        {
            if (KeptLoose(archive, fileName) is not string keptLoose) return File.ReadAllBytes(LoosePath(archive, fileName));
            return keptLoose.EndsWith(AbsentSuffix) ? throw new FileNotFoundException($"{archive} had no {fileName} before the editor added it") : File.ReadAllBytes(keptLoose);
        }
        if (IsNewArchive(archive) && KeptOriginal(archive, fileName) is string kept)
            return File.Exists(kept) ? File.ReadAllBytes(kept) : throw new FileNotFoundException($"{archive} had no {fileName} before the editor added it");
        Packfile pack = Archive(archive);
        return pack.Read(pack.Find(fileName) ?? throw new FileNotFoundException($"{archive} has no {fileName}"));
    }

    /// <summary>A file in an archive as the game will see it: the override if one applies, else the game's copy.</summary>
    public byte[] Read(string archive, string fileName)
    {
        if (IsLooseFolder(archive)) return File.ReadAllBytes(LoosePath(archive, fileName));
        if (IsNewArchive(archive))
        {
            if (pending.TryGetValue(archive, out var waiting) && waiting.TryGetValue(fileName, out byte[]? soon))
                return soon ?? throw new FileNotFoundException($"{archive} has no {fileName}");
            if (EditFor(archive, fileName) is string edit) return File.ReadAllBytes(edit);
            Packfile own = Archive(archive);
            return own.Read(own.Find(fileName) ?? throw new FileNotFoundException($"{archive} has no {fileName}"));
        }
        return OverrideFor(archive, fileName) is string overridden ? File.ReadAllBytes(overridden) : ReadOriginal(archive, fileName);
    }

    /// <summary>A .str2_pc container in an archive, as the game will see it (your rebuilt one if you
    /// have one). The last one opened is kept.</summary>
    public Packfile Container(string archive, string containerName)
    {
        string key = archive + ">" + containerName;
        if (lastContainer?.Key == key) return lastContainer.Value.Container;
        Packfile container = Packfile.FromBytes(containerName, Read(archive, containerName));
        lastContainer = (key, container);
        return container;
    }

    /// <summary>
    /// A file by name only, as the game will see it: the override if there is one, else the game's
    /// own copy from the first archive that has it. Null if no archive has it. (Used for tables,
    /// which the game looks up by name.)
    /// </summary>
    public byte[]? ReadAsGameSeesIt(string fileName)
    {
        foreach (string archive in Archives())
            if (Archive(archive).Find(fileName) != null) return Read(archive, fileName);
        return null;
    }

    /// <summary>Every asset list (.asm_pc) in the game: which archive, and its name.</summary>
    public IReadOnlyList<(string Archive, string Name)> AssetLists()
    {
        assetLists ??= Archives()
            .SelectMany(archive => Archive(archive).Entries.Where(e => e.Extension == "asm_pc").Select(e => (archive, e.Name)))
            .ToList();
        return assetLists;
    }

    // ---- Overrides -----------------------------------------------------------------------------

    /// <summary>Where the override for a file in an archive goes: overrides\ mirrors the game folder,
    /// with a folder named after each archive, e.g. overrides\build\pc\cache\table.vpp_pc\weapons.xtbl.</summary>
    public string OverridePath(string archive, string fileName) => Path.Combine(OverridesFolder, archive, fileName);

    /// <summary>
    /// The override the game will use for this archive's copy of a file, or null: one in the
    /// archive's own folder, else one for another archive whose copy is the same size.
    /// </summary>
    public string? OverrideFor(string archive, string fileName)
    {
        if (IsNewArchive(archive) || IsLooseFolder(archive)) return null;  // ours: changed in place (see IsChanged)
        overrides ??= ScanOverrides();
        if (!overrides.TryGetValue(fileName, out List<string>? paths)) return null;
        string? own = paths.FirstOrDefault(p => ArchiveOf(p).Equals(archive, StringComparison.OrdinalIgnoreCase));
        if (own != null) return own;
        uint? size = Archive(archive).Find(fileName)?.Size;
        return paths.FirstOrDefault(p => TryArchiveSize(ArchiveOf(p), fileName) == size);
    }

    /// <summary>
    /// Whether the game will see a changed copy of a file: an override of the game's, or a file the
    /// editor changed in one of our archives.
    /// </summary>
    public bool IsChanged(string archive, string fileName) =>
        IsLooseFolder(archive) ? KeptLoose(archive, fileName) != null
        : IsNewArchive(archive) ? EditFor(archive, fileName) != null || KeptOriginal(archive, fileName) != null : OverrideFor(archive, fileName) != null;

    /// <summary>
    /// Writes an override for this archive's copy of a file. In one of our archives it goes to
    /// edits\ instead, with the sizes of the archive's copy beside it (see the class); a file new to
    /// one of our archives is added to the archive itself (an edit can only replace a file), and the
    /// archive's path returned.
    /// </summary>
    public string WriteOverride(string archive, string fileName, byte[] data)
    {
        if (IsLooseFolder(archive)) return WriteLoose(archive, fileName, data);
        if (IsNewArchive(archive))
        {
            if (Archive(archive).Find(fileName) is not { } own)
            {
                ChangeInOwnArchive(archive, fileName, data);
                return ArchivePath(archive);
            }
            string edit = EditPath(archive, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(edit)!);
            File.WriteAllBytes(edit, data);
            File.WriteAllText(edit + BaseSuffix, $"{own.Size} {own.CompressedSize}");
            Changed();
            return edit;
        }
        string path = OverridePath(archive, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        Changed();
        return path;
    }

    /// <summary>Deletes the override the game would use for this archive's copy of a file, if any.</summary>
    public void DeleteOverride(string archive, string fileName)
    {
        if (IsLooseFolder(archive))
        {
            PutBackLoose(archive, fileName);
            return;
        }
        if (IsNewArchive(archive))
        {
            string edit = EditPath(archive, fileName);
            if (File.Exists(edit) || File.Exists(edit + BaseSuffix))
            {
                File.Delete(edit);
                File.Delete(edit + BaseSuffix);
                Changed();
            }
            // A change made by an older version, in the archive itself: its copy from before back.
            if (KeptOriginal(archive, fileName) is not string kept) return;
            ChangeInOwnArchive(archive, fileName, File.Exists(kept) ? File.ReadAllBytes(kept) : null, putBack: true);
            return;
        }
        if (OverrideFor(archive, fileName) is string path) File.Delete(path);
        Changed();
    }

    // ---- Our own archives (see the class) ------------------------------------------------------

    readonly Dictionary<string, Dictionary<string, byte[]?>> pending = new(StringComparer.OrdinalIgnoreCase);  // archive -> file -> bytes (null: taken out)
    readonly List<(string Archive, string File)> putBack = [];  // kept copies to forget once their archive is written
    int batches;

    /// <summary>
    /// Holds back writing our archives until the returned scope ends, so a save that changes several
    /// files in one archive (a container and its asset list) writes it once: they are big.
    /// </summary>
    public IDisposable Batch()
    {
        batches++;
        return new BatchScope(this);
    }

    sealed class BatchScope(Workspace workspace) : IDisposable
    {
        bool done;
        public void Dispose()
        {
            if (done) return;
            done = true;
            if (--workspace.batches == 0) workspace.WriteOwnArchives();
        }
    }

    string OriginalsOf(string archive) => Path.Combine(OriginalsFolder, archive);
    string StampOf(string archive) => Path.Combine(OriginalsOf(archive), ".written");
    const string AbsentSuffix = ".absent";  // a file the editor added to the archive

    /// <summary>The kept copy of a file of one of our archives (or a path ending .absent: it had none), or null if the editor hasn't changed it.</summary>
    string? KeptOriginal(string archive, string fileName)
    {
        ForgetStaleOriginals(archive);
        string kept = Path.Combine(OriginalsOf(archive), fileName);
        return File.Exists(kept) ? kept : File.Exists(kept + AbsentSuffix) ? kept + AbsentSuffix : null;
    }

    /// <summary>The kept copies are of the archive as the editor last wrote it; if it was written since (built again), they are forgotten.</summary>
    void ForgetStaleOriginals(string archive)
    {
        string folder = OriginalsOf(archive), stamp = StampOf(archive);
        if (!Directory.Exists(folder)) return;
        string path = ArchivePath(archive);
        if (File.Exists(stamp) && File.Exists(path) && File.ReadAllText(stamp).Trim() == Stamp(path)) return;
        if (File.Exists(stamp) || !File.Exists(path) || Directory.EnumerateFiles(folder).Any())
            Directory.Delete(folder, recursive: true);
    }

    static string Stamp(string path)
    {
        var info = new FileInfo(path);
        return $"{info.Length} {info.LastWriteTimeUtc.Ticks}";
    }

    void ChangeInOwnArchive(string archive, string fileName, byte[]? data, bool putBack = false)
    {
        if (!putBack && KeptOriginal(archive, fileName) == null)
        {
            // Keep the archive's own copy (as it is on disk) before the first change.
            string kept = Path.Combine(OriginalsOf(archive), fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            Packfile own = Archive(archive);
            if (own.Find(fileName) is { } entry) File.WriteAllBytes(kept, own.Read(entry));
            else File.WriteAllBytes(kept + AbsentSuffix, []);
            if (!File.Exists(StampOf(archive))) File.WriteAllText(StampOf(archive), Stamp(ArchivePath(archive)));
        }
        if (!pending.TryGetValue(archive, out var files)) pending[archive] = files = new(StringComparer.OrdinalIgnoreCase);
        files[fileName] = data;
        if (putBack) this.putBack.Add((archive, fileName));
        lastContainer = null;
        if (batches == 0) WriteOwnArchives();
    }

    /// <summary>Writes the changes held back for our archives (see <see cref="Batch"/>).</summary>
    void WriteOwnArchives()
    {
        foreach (var (archive, files) in pending)
        {
            string path = ArchivePath(archive);
            archives.Remove(archive);  // its directory changes
            Packfile.RewriteArchive(path, files);
            Directory.CreateDirectory(OriginalsOf(archive));
            File.WriteAllText(StampOf(archive), Stamp(path));
        }
        pending.Clear();
        foreach (var (archive, file) in putBack)
        {
            string kept = Path.Combine(OriginalsOf(archive), file);
            File.Delete(kept);
            File.Delete(kept + AbsentSuffix);
        }
        putBack.Clear();
        Changed();
    }

    // ---- Loose files ---------------------------------------------------------------------------
    // Files that aren't in any archive but that the game gets all the same, each folder of them
    // listed as one "archive" of loose files (Packfile.FromLooseFiles), by a name of its own:
    //   build\pc\cache\(new files)       the new files in overrides\build\pc\cache\: the loader hands
    //                                    one to the game when the game looks for that name and finds
    //                                    it in none of its archives (so a name an archive has is the
    //                                    archive's; the editor finds names the same way, archives first)
    //   mods\guerrilla_gear\animations   the data a mod reads from its folder (the Guerrilla port's
    //                                    tables, animations, pictures and scripts, which it gives the game)
    // Changing one (saving a table or a zone in it, a texture in a container) writes the file in
    // place, as the game reads it there; the copy from before is kept in editor_originals\ (as for our
    // archives), so restoring puts it back. If the file is written anew since (the Guerrilla builder
    // ran again), the kept copy is forgotten with the change.

    /// <summary>A folder of loose files: where it is, and whether it holds new files (else a mod's data).</summary>
    sealed record LooseFolder(string Folder, bool NewFiles);

    /// <summary>The name a folder's new files are listed under, beside the folder's archives.</summary>
    public const string NewFilesName = "(new files)";

    Dictionary<string, LooseFolder>? looseFolders;

    /// <summary>True for a folder of loose files listed as an archive (see above).</summary>
    public bool IsLooseFolder(string archive) => LooseFolders().ContainsKey(archive);

    /// <summary>True for a folder of new files in overrides\ (see above); false for a mod's data, or an archive.</summary>
    public bool IsNewFiles(string archive) => LooseFolders().TryGetValue(archive, out LooseFolder? loose) && loose.NewFiles;

    /// <summary>The folders of loose files, in order: the new files in overrides\ (by folder), then each mod's folders.</summary>
    Dictionary<string, LooseFolder> LooseFolders()
    {
        if (looseFolders != null) return looseFolders;
        var found = new Dictionary<string, LooseFolder>(StringComparer.OrdinalIgnoreCase);
        // New files: in every folder of overrides\ but its top (its read-me) and the archives' own.
        if (Directory.Exists(OverridesFolder))
            foreach (string folder in Directory.EnumerateDirectories(OverridesFolder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(OverridesFolder, folder);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(".vpp_pc", StringComparison.OrdinalIgnoreCase))) continue;
                if (LooseFilesIn(folder, relative).Count > 0) found[Path.Combine(relative, NewFilesName)] = new LooseFolder(folder, NewFiles: true);
            }
        // The mods' data: every folder in mods\ (the mods themselves are in mods\ itself).
        string mods = Path.Combine(ModFolder, "mods");
        if (Directory.Exists(mods))
            foreach (string folder in Directory.EnumerateDirectories(mods, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (LooseFilesIn(folder, null).Count > 0) found[Path.GetRelativePath(ModFolder, folder)] = new LooseFolder(folder, NewFiles: false);
        return looseFolders = found;
    }

    /// <summary>
    /// The loose files of a folder, by name. For new files (<paramref name="gameRelative"/>: the
    /// folder's place in the game folder) not the new archives, which are listed as archives, nor
    /// a file the game folder has at that place, which the loader leaves out.
    /// </summary>
    List<string> LooseFilesIn(string folder, string? gameRelative) =>
        Directory.EnumerateFiles(folder)
            .Where(path => gameRelative == null || (!path.EndsWith(".vpp_pc", StringComparison.OrdinalIgnoreCase) &&
                                                    !File.Exists(Path.Combine(GameFolder, gameRelative, Path.GetFileName(path)))))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

    string LoosePath(string archive, string fileName) => Path.Combine(LooseFolders()[archive].Folder, fileName);

    /// <summary>Where a loose file is on disk (for a folder of loose files: see <see cref="IsLooseFolder"/>).</summary>
    public string LooseFilePath(string archive, string fileName) => LoosePath(archive, fileName);

    Dictionary<string, string>? archiveNames;  // file name -> the first archive (the game's, or a new one) with it directly inside

    /// <summary>
    /// The first archive (the game's own, or a new one in overrides\) with a file of this name
    /// directly inside, or null. A new file of a name an archive has is never the game's: the
    /// game finds the archive's first.
    /// </summary>
    public string? GameArchiveWith(string fileName)
    {
        if (archiveNames == null)
        {
            archiveNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string archive in Archives().Where(a => !IsLooseFolder(a)))
            {
                try
                {
                    foreach (PackfileEntry entry in Archive(archive).Entries) archiveNames.TryAdd(entry.Name, archive);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { }  // (one that can't be read has none)
            }
        }
        return archiveNames.TryGetValue(fileName, out string? found) ? found : null;
    }

    string KeptLoosePath(string archive, string fileName) => Path.Combine(OriginalsFolder, archive, fileName);
    const string WrittenSuffix = ".written";  // beside a kept copy: the size and time of the file as the editor wrote it

    /// <summary>
    /// The copy from before the editor changed a loose file (a path ending .absent: there was none),
    /// or null if it hasn't, or if the file was written anew since (then the kept copy is forgotten).
    /// </summary>
    string? KeptLoose(string archive, string fileName)
    {
        string kept = KeptLoosePath(archive, fileName), written = kept + WrittenSuffix;
        string? found = File.Exists(kept) ? kept : File.Exists(kept + AbsentSuffix) ? kept + AbsentSuffix : null;
        if (found == null) return null;
        string now = LoosePath(archive, fileName);
        if (File.Exists(written) && File.Exists(now) && File.ReadAllText(written).Trim() == Stamp(now)) return found;
        File.Delete(found);
        File.Delete(written);
        return null;
    }

    /// <summary>Writes a loose file in place, keeping its copy from before the first change.</summary>
    string WriteLoose(string archive, string fileName, byte[] data)
    {
        string path = LoosePath(archive, fileName), kept = KeptLoosePath(archive, fileName);
        if (KeptLoose(archive, fileName) == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            if (File.Exists(path)) File.Copy(path, kept, overwrite: true);
            else File.WriteAllBytes(kept + AbsentSuffix, []);
        }
        File.WriteAllBytes(path, data);
        File.WriteAllText(kept + WrittenSuffix, Stamp(path));
        Changed();
        return path;
    }

    /// <summary>Puts a changed loose file's copy from before back (or takes away one the editor added).</summary>
    void PutBackLoose(string archive, string fileName)
    {
        if (KeptLoose(archive, fileName) is not string kept) return;
        string path = LoosePath(archive, fileName);
        if (kept.EndsWith(AbsentSuffix)) File.Delete(path);
        else File.Copy(kept, path, overwrite: true);
        File.Delete(kept);
        File.Delete(KeptLoosePath(archive, fileName) + WrittenSuffix);
        Changed();
    }

    /// <summary>
    /// A way to sum up what the editor lists besides the game's own archives: each folder of
    /// overrides\ and mods\ (but the archives' override folders) with how many files it has and their
    /// total size, so new archives, new files and the mods' data. It reads only the disk, so it can
    /// be run on another thread. When the sum changes (the Guerrilla builder wrote new files while the
    /// editor was open), the explorer and the search are made again.
    /// </summary>
    public Func<string> ListedSignatureReader()
    {
        string[] roots = [OverridesFolder, Path.Combine(ModFolder, "mods")];
        return () =>
        {
            var parts = new List<string>();
            foreach (string root in roots.Where(Directory.Exists))
                foreach (DirectoryInfo folder in new DirectoryInfo(root).EnumerateDirectories("*", SearchOption.AllDirectories))
                {
                    if (Path.GetRelativePath(root, folder.FullName).Split(Path.DirectorySeparatorChar)
                            .Any(part => part.EndsWith(".vpp_pc", StringComparison.OrdinalIgnoreCase))) continue;  // overrides of an archive's files
                    var files = folder.EnumerateFiles().ToList();
                    if (files.Count > 0) parts.Add($"{folder.FullName}:{files.Count}:{files.Sum(f => f.Length)}");
                }
            return string.Join("|", parts);
        };
    }

    /// <summary>Forget what is in overrides\, so it is looked at again (after files there changed).</summary>
    public void RefreshOverrides() => Changed();

    void Changed()
    {
        isNew.Clear();
        overrides = null;
        allArchives = null;
        lastContainer = null;
        if (looseFolders != null)
        {
            foreach (string loose in looseFolders.Keys) archives.Remove(loose);  // their file lists are read again
            looseFolders = null;
        }
    }

    string ArchiveOf(string overridePath) => Path.GetRelativePath(OverridesFolder, Path.GetDirectoryName(overridePath)!);

    uint? TryArchiveSize(string archive, string fileName)
    {
        try
        {
            return File.Exists(ArchivePath(archive)) ? Archive(archive).Find(fileName)?.Size : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    Dictionary<string, List<string>> ScanOverrides()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(OverridesFolder)) return found;
        foreach (string path in Directory.EnumerateFiles(OverridesFolder, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetDirectoryName(path)!.EndsWith(".vpp_pc", StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(path);
            if (!found.TryGetValue(name, out List<string>? paths)) found[name] = paths = [];
            paths.Add(path);
        }
        return found;
    }

    public string RelativeToMod(string path) => Path.GetRelativePath(ModFolder, path);
}
