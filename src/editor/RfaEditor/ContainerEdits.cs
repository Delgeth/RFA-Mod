using System.IO;
using RfaFormats;

namespace RfaEditor;

/// <summary>
/// Changing files that live inside a .str2_pc container. The game streams a container in whole,
/// so a changed file means a rebuilt container (Packfile.Rebuild) saved to overrides\. The game
/// sets memory aside for each container from the asset lists (.asm_pc), which record the size of
/// its compressed data and the size of every file in it, so every asset list entry for that
/// container (and every group entry naming its files) is updated to match and overridden too.
/// Used for textures (which keep their size)
/// and zones (which grow when objects are added or text gets longer: with the old sizes in the
/// asset list, the game stopped loading the level).
/// </summary>
static class ContainerEdits
{
    /// <summary>Replaces files in a container (as the game sees it now). Returns what was written, for the status bar.</summary>
    public static string Replace(Workspace workspace, string archive, string container, IReadOnlyDictionary<string, byte[]> files)
    {
        using IDisposable batch = workspace.Batch();  // our own archives (the Guerrilla world's) written once, at the end
        Packfile before = workspace.Container(archive, container);
        byte[] rebuilt = before.Rebuild(files);
        Packfile after = Packfile.FromBytes(container, rebuilt);
        List<string> lists = UpdateAssetLists(workspace, archive, container, before, after);
        string path = workspace.WriteOverride(archive, container, rebuilt);
        return $"rebuilt {workspace.RelativeToMod(path)}" +
               (lists.Count > 0 ? $" and updated {string.Join(", ", lists)}." : ". No asset list mentions this container, so none was updated.");
    }

    /// <summary>
    /// Puts files into a container, like <see cref="Replace"/>; but if that makes every file in it
    /// the game's own again, goes back to the game's own container instead (<see cref="Restore"/>).
    /// </summary>
    public static string Put(Workspace workspace, string archive, string container, IReadOnlyDictionary<string, byte[]> files)
    {
        var replacing = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
        Packfile current = workspace.Container(archive, container);
        Packfile original = Packfile.FromBytes(container, workspace.ReadOriginal(archive, container));
        bool allOriginal = original.Entries.All(e =>
            (replacing.TryGetValue(e.Name, out byte[]? replacement) ? replacement : current.Read(current.Find(e.Name)!))
            .AsSpan().SequenceEqual(original.Read(e)));
        if (!allOriginal) return Replace(workspace, archive, container, replacing);
        return !workspace.IsChanged(archive, container) ? $"{container} is the game's own." : Restore(workspace, archive, container);
    }

    /// <summary>Goes back to the game's own container, and puts its asset list entries back. Returns what was done.</summary>
    public static string Restore(Workspace workspace, string archive, string container)
    {
        using IDisposable batch = workspace.Batch();
        Packfile current = workspace.Container(archive, container);
        Packfile original = Packfile.FromBytes(container, workspace.ReadOriginal(archive, container));
        List<string> lists = UpdateAssetLists(workspace, archive, container, current, original);
        workspace.DeleteOverride(archive, container);
        return $"{container} is the game's own again" + (lists.Count > 0 ? $"; {string.Join(", ", lists)} put back." : ".");
    }

    /// <summary>
    /// Points every asset list entry that describes the container as it was (<paramref name="before"/>)
    /// at the container as it is now: where its data starts, its compressed size, and the sizes of
    /// its files. An entry's size list is the size of every file in the container, in the
    /// container's order, and each primitive gives its own file's size and its g-file's (as in all
    /// 799 entries of world_l0, mp_inf01 and missions checked), so they are all set from the
    /// container as it is now (which also mends a list saved before sizes were kept up to date).
    /// The same list's group entries, which have no container of their own (no size list) and
    /// name what a part of the world loads from many containers (1_c0812 names world_08_03's zone
    /// beside 1h_c0812, which holds it), follow too: the game sets memory aside for a file by them,
    /// so a zone grown without them overran it (and froze the game in its destruction links). In
    /// the game's lists a group gives a file the same sizes as a container holding it, every time
    /// (59,115 of 59,115), so a group's primitive naming one of the container's files is taken to
    /// mean this one unless another container holds that file at the group's sizes and this one
    /// didn't. An asset list that ends up the same as the game's own is no longer overridden.
    /// Returns the asset lists changed.
    /// </summary>
    static List<string> UpdateAssetLists(Workspace workspace, string containerArchive, string containerFile, Packfile before, Packfile after,
                                         IReadOnlyList<AsmPrimitive>? added = null, IReadOnlyList<AsmContainer>? groups = null)
    {
        string name = Path.GetFileNameWithoutExtension(containerFile);
        List<int> sizesAfter = after.Entries.Select(e => (int)e.Size).ToList();
        int? SizeIn(Packfile pack, string file) => file.Length > 0 && pack.Find(file) is { } entry ? (int)entry.Size : null;
        void SetSizes(AsmPrimitive primitive)  // to its file's size in the container as it is now, and its g-file's
        {
            if (SizeIn(after, primitive.Name) is int header) primitive.HeaderSize = header;
            if (primitive.DataSize != 0 && SizeIn(after, DataFileOf(primitive.Name)) is int dataSize) primitive.DataSize = dataSize;
        }
        // Files taken out (a chunk the editor added, then the zone put back): gone from the lists too.
        var gone = before.Entries.Select(e => e.Name).Where(n => after.Find(n) == null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The zone's group (1_c0812: what loading the zone sets memory aside for) is the one naming this
        // container's zone file: in the game's levels it is in the list naming the container; in the
        // Guerrilla world in another (rfg_l0's groups, rfg_l1's containers), which is updated as well.
        var zoneFiles = after.Entries.Where(e => e.Extension == "rfgzone_pc").Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsZoneGroup(AsmContainer c) => c.Sizes.Count == 0 && c.Primitives.Any(p => zoneFiles.Contains(p.Name));
        var lists = new List<(string Archive, string List, AsmFile Asm, List<AsmContainer> Entries, bool ZoneGroup)>();
        foreach ((string listArchive, string list) in workspace.AssetLists())
        {
            // Only the lists of the container's own level: its archive's, or its world's beside it (the
            // Guerrilla world's rfg_l0 and rfg_l1). Other levels have containers of the same name and
            // size too (every Ruin map's ns_base.str2_pc is the same empty 2 KB), which aren't this one.
            if (!SameLevel(listArchive, containerArchive)) continue;
            string archive = listArchive;
            AsmFile asm = AsmFile.Read(workspace.Read(archive, list));
            var entries = asm.Containers.Where(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                                    c.DataOffset == before.DataStart &&
                                                    c.CompressedSize == before.CompressedDataSize).ToList();
            bool zoneGroup = zoneFiles.Count > 0 && asm.Containers.Any(IsZoneGroup);
            // (A list with only the zone's group is taken when it's ours, the Guerrilla world's, or files are
            // being added; the game's own such lists are left as they were.)
            if (entries.Count > 0 || (zoneGroup && (workspace.IsNewArchive(archive) || added != null)))
                lists.Add((archive, list, asm, entries, zoneGroup));
        }
        // A chunk's own group (s_edf_tech_flyer.rfgchunkx: how the game finds a model a zone names) goes
        // where the zone's group is (as the Guerrilla builder puts its chunks'), else beside the container.
        bool groupsWithZoneGroup = lists.Any(l => l.ZoneGroup);

        var changed = new List<string>();
        foreach (var (archive, list, asm, entries, zoneGroup) in lists)
        {
            foreach (AsmContainer entry in entries)
            {
                entry.DataOffset = (uint)after.DataStart;
                entry.CompressedSize = after.CompressedDataSize;
                if (entry.Sizes.Count == sizesAfter.Count || entry.Sizes.Count == before.Entries.Count)
                {
                    entry.Sizes.Clear();
                    entry.Sizes.AddRange(sizesAfter);
                }
                entry.Primitives.RemoveAll(p => gone.Contains(p.Name));
                foreach (AsmPrimitive primitive in added ?? [])
                    if (!entry.Primitives.Any(p => p.Name.Equals(primitive.Name, StringComparison.OrdinalIgnoreCase)))
                        entry.Primitives.Add(Copy(primitive));
                foreach (AsmPrimitive primitive in entry.Primitives) SetSizes(primitive);
            }
            var elsewhere = asm.Containers.Where(c => c.Sizes.Count > 0 && !entries.Contains(c))
                               .SelectMany(c => c.Primitives).ToLookup(p => p.Name, StringComparer.OrdinalIgnoreCase);
            var emptied = new List<AsmContainer>();
            foreach (AsmContainer group in asm.Containers.Where(c => c.Sizes.Count == 0))
            {
                bool zone = IsZoneGroup(group);
                if (group.Primitives.RemoveAll(p => gone.Contains(p.Name) && !elsewhere[p.Name].Any()) > 0 && group.Primitives.Count == 0) emptied.Add(group);
                if (added != null && zone)
                    foreach (AsmPrimitive primitive in added)
                        if (!group.Primitives.Any(p => p.Name.Equals(primitive.Name, StringComparison.OrdinalIgnoreCase)))
                            group.Primitives.Add(Copy(primitive));
                foreach (AsmPrimitive primitive in group.Primitives)
                {
                    if (SizeIn(after, primitive.Name) == null) continue;  // not one of this container's files
                    bool wasThis = SizeIn(before, primitive.Name) == primitive.HeaderSize &&
                                   (primitive.DataSize == 0 || SizeIn(before, DataFileOf(primitive.Name)) == primitive.DataSize);
                    bool another = elsewhere[primitive.Name].Any(p => p.HeaderSize == primitive.HeaderSize && p.DataSize == primitive.DataSize);
                    if (wasThis || !another) SetSizes(primitive);
                }
            }
            foreach (AsmContainer group in emptied) asm.Containers.Remove(group);  // (a chunk's own group whose files were taken out)
            if (zoneGroup || !groupsWithZoneGroup)
                foreach (AsmContainer group in groups ?? [])
                    if (!asm.Containers.Any(c => c.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var copy = new AsmContainer { Name = group.Name, Type = group.Type, Flags = group.Flags };
                        copy.Primitives.AddRange(group.Primitives.Select(Copy));
                        asm.Containers.Add(copy);
                    }
            byte[] updated = asm.ToBytes();
            if (updated.AsSpan().SequenceEqual(workspace.ReadOriginal(archive, list)))
                workspace.DeleteOverride(archive, list);
            else
                workspace.WriteOverride(archive, list, updated);
            changed.Add(list);
        }
        return changed;
    }

    /// <summary>
    /// After a file loose in an archive (not in a container) changed size: every asset list entry
    /// that gives its size as it was (a primitive of that name and size: the game sets memory aside
    /// for the file by it, and Ruin maps' lists name their zone and stitch list, loose in
    /// dlc01_precache.vpp_pc, from ruin01.vpp_pc's list) gives its new size. Names are the level's
    /// own (ruin01.stchlst_pc), and only entries with the old size are changed. A list that ends up
    /// the game's own is no longer overridden. Returns the lists changed.
    /// </summary>
    public static List<string> UpdateLooseSize(Workspace workspace, string fileName, int oldSize, int newSize)
    {
        var changed = new List<string>();
        if (oldSize == newSize) return changed;
        using IDisposable batch = workspace.Batch();
        foreach ((string archive, string list) in workspace.AssetLists())
        {
            AsmFile asm;
            try { asm = AsmFile.Read(workspace.Read(archive, list)); }
            catch (InvalidDataException) { continue; }
            bool any = false;
            foreach (AsmContainer container in asm.Containers)
                foreach (AsmPrimitive primitive in container.Primitives)
                {
                    if (primitive.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && primitive.HeaderSize == oldSize)
                    {
                        primitive.HeaderSize = newSize;
                        any = true;
                    }
                    // A g-file's size is its header file's primitive's data size (a sub-zone made over at every grid step grows both).
                    else if (primitive.DataSize == oldSize && primitive.DataSize != 0 && DataFileOf(primitive.Name).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        primitive.DataSize = newSize;
                        any = true;
                    }
                }
            if (!any) continue;
            byte[] updated = asm.ToBytes();
            if (updated.AsSpan().SequenceEqual(workspace.ReadOriginal(archive, list))) workspace.DeleteOverride(archive, list);
            else workspace.WriteOverride(archive, list, updated);
            changed.Add(list);
        }
        return changed;
    }

    /// <summary>Whether two archives are one level's: the same, or the same world's (rfg_l0 and rfg_l1, world_l0 and world_l1, x and x_precache) in one folder.</summary>
    internal static bool SameLevel(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        static string World(string archive)
        {
            string name = Path.GetFileNameWithoutExtension(archive);
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)(_l\d+|_precache)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : name;
        }
        return (Path.GetDirectoryName(a) ?? "").Equals(Path.GetDirectoryName(b) ?? "", StringComparison.OrdinalIgnoreCase) &&
               World(a).Equals(World(b), StringComparison.OrdinalIgnoreCase);
    }

    static AsmPrimitive Copy(AsmPrimitive p) => new()
    {
        Name = p.Name, Type = p.Type, Allocator = p.Allocator, Flags = p.Flags, SplitIndex = p.SplitIndex, HeaderSize = p.HeaderSize, DataSize = p.DataSize,
    };

    /// <summary>
    /// Adds files to a container (those it hasn't already), as <see cref="Replace"/> rebuilds it,
    /// and to every asset list entry describing it: a primitive for each header file
    /// (<paramref name="primitives"/>: its kind, as the game's lists give it), its zone's group too,
    /// and <paramref name="groups"/> (a chunk's own group) in the lists naming the container, where
    /// they have none. Used to bring another level's chunks (models and their textures) into a zone.
    /// Returns what was written, for the log.
    /// </summary>
    public static string AddFiles(Workspace workspace, string archive, string container, IReadOnlyList<(string Name, byte[] Data)> files,
                                  IReadOnlyList<AsmPrimitive> primitives, IReadOnlyList<AsmContainer> groups)
    {
        using IDisposable batch = workspace.Batch();
        Packfile before = workspace.Container(archive, container);
        var adding = files.Where(f => before.Find(f.Name) == null).DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (adding.Count == 0 && groups.Count == 0) return $"{container} has them already.";
        var all = before.Entries.Select(e => (Name: e.Name, Data: before.Read(e))).Concat(adding).ToList();
        byte[] rebuilt = Packfile.CreateContainer(before, all);
        Packfile after = Packfile.FromBytes(container, rebuilt);
        var newPrimitives = primitives.Where(p => adding.Any(f => f.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        List<string> lists = UpdateAssetLists(workspace, archive, container, before, after, newPrimitives, groups);
        string path = workspace.WriteOverride(archive, container, rebuilt);
        return $"added {adding.Count} file(s) to {workspace.RelativeToMod(path)} > {container}" +
               (lists.Count > 0 ? $" and to {string.Join(", ", lists)}" : " (no asset list names the container: the game won't know them)");
    }

    /// <summary>The file that holds a file's data: its g-file ("rock.cpeg_pc": "rock.gpeg_pc"); none for files without one.</summary>
    static string DataFileOf(string file)
    {
        string extension = Path.GetExtension(file);
        return extension.Length > 2 && extension[1] == 'c' ? Path.ChangeExtension(file, "g" + extension[2..]) : "";
    }
}
