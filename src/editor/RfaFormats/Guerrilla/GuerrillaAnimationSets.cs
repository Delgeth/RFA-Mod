using RfaFormats.Meshes;
using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's animation sets made usable in Armageddon, for GuerrillaArsenal. A set
/// (a Skeleton_Set table, such as Guerrilla's vehicle_taxi.xtbl) names the animation each state and
/// action plays: for a vehicle, getting in, sitting, driving, getting out, diving out. Both games read
/// sets the same way while they start: the list of sets (anim_set_filenames.xtbl), every animation's
/// triggers and flags (anim_files.xtbl), then each listed set's table by its file name. For each set
/// asked for:
///   - its animations, from the vehicle's own container or Guerrilla's anims.vpp_pc, moved from Mason's
///     skeleton onto Darius's (<see cref="GuerrillaAnimations"/>), to be packed in the vehicle's
///     container as Armageddon packs its own vehicles' player animations;
///   - each animation's anim_files.xtbl entry, Guerrilla's own, added to Armageddon's table: its
///     triggers (when a door opens and is open, when the camera and the person's place hand over, the
///     footsteps) and its flags. Its sounds and voice lines (Guerrilla's are XACT cues) are left out
///     until Guerrilla's audio is ported;
///   - the set's table, Guerrilla's own, as a new file the game finds by name (it isn't in the game's
///     start-up copy of its tables), listed in anim_set_filenames.xtbl;
///   - the state and action names it uses that Armageddon's tables haven't (Guerrilla's code names some
///     itself, as "driver look back", which Armageddon's code doesn't), added to anim_states.xtbl and
///     anim_actions.xtbl, so the set reads whole.
/// The changed lists are handed to the game by the guerrilla_world mod (mods\guerrilla_world\). A name
/// Armageddon already uses (a set, an animation) gets the prefix rfg_; the others keep Guerrilla's.
/// </summary>
public sealed class GuerrillaAnimationSets
{
    /// <summary>The tables this changes, written to the mod's folder.</summary>
    public const string SetList = "anim_set_filenames.xtbl", FileList = "anim_files.xtbl", StateList = "anim_states.xtbl", ActionList = "anim_actions.xtbl";

    /// <summary>
    /// Guerrilla's people's skeletons all start with the same 52 bones in the same order (its player's,
    /// player.rig_pc), and some add their own after them: Mason's camera (53), EDF soldiers' shoulder
    /// pads (54, 55), long coats and jackets (59, 63, 64). An animation is made for one of them, told by
    /// its bone count, and is moved onto Darius's skeleton (Armageddon's player_v2.rig_pc) from the first
    /// of Guerrilla's skeletons with that count (Mason's own for 53). An animation for any other count is
    /// for a vehicle's own skeleton (a turret, a flyer, a walker's legs), the same in Armageddon, and is
    /// only re-packed.
    /// </summary>
    const string PeopleRig = "player.rig_pc", MasonRig = "mason.rig_pc", DariusRig = "player_v2.rig_pc";

    /// <summary>
    /// How many animation sets the game holds with the guerrilla_vehicles mod: 255 (the game's own table
    /// has 140 places, MAX_ANIM_SETS, and one more stops it with "Num_anim_sets &lt; MAX_ANIM_SETS" and a
    /// crash; the mod gives it a bigger one). Armageddon fills 125 itself: the sets in the files its
    /// list names (50) and one for each object with animations of its own (anim_rig_objects.xtbl, 75:
    /// doors, machines). Sets past the room left are left out.
    /// </summary>
    public const int MaxAnimSets = 255;
    const string RigObjects = "anim_rig_objects.xtbl";

    /// <summary>The fields of an anim_files.xtbl entry left out for now (see the summary), and Guerrilla's that Armageddon's entries haven't.</summary>
    static readonly string[] LeftOutOfFiles = ["Sounds", "Voice_Lines", "OldProps"];

    readonly Packfile guerrillaTables, gameTables, guerrillaAnims;
    readonly Dictionary<int, Rig> peopleRigs = [];  // Guerrilla's people's skeletons, by bone count
    readonly Rig darius;
    readonly XtblFile setList, fileList, stateList, actionList;
    readonly XtblElement fileEntries;
    readonly Dictionary<string, XtblElement> rfgFileEntries = new(StringComparer.OrdinalIgnoreCase);  // Guerrilla's anim_files.xtbl, by file name (x.animx)
    readonly HashSet<string> gameAnimations, gameSets, ourStates, ourActions;  // Armageddon's own animations (x.animx) and sets (x.xtbl); the states and actions, ours added
    readonly Dictionary<string, string> setNames = new(StringComparer.OrdinalIgnoreCase);  // Guerrilla's set → ours (or "" if it couldn't be had)
    readonly Dictionary<string, byte[]> animations = new(StringComparer.OrdinalIgnoreCase);  // our .anim_pc file → its bytes
    readonly List<(string File, byte[] Data)> setTables = [];
    readonly List<string> noRoom = [];  // Guerrilla's sets left out for want of room
    bool statesChanged, actionsChanged;

    /// <summary>How many sets Armageddon fills itself, and how many of ours fit.</summary>
    public int GameSets { get; }
    public int Room => MaxAnimSets - GameSets;

    /// <summary>Guerrilla's sets left out because the game holds no more (see <see cref="MaxAnimSets"/>).</summary>
    public IReadOnlyList<string> LeftOutForRoom => noRoom;

    public GuerrillaAnimationSets(Packfile guerrillaTables, Packfile gameTables, Packfile guerrillaAnims)
    {
        (this.guerrillaTables, this.gameTables, this.guerrillaAnims) = (guerrillaTables, gameTables, guerrillaAnims);
        var people = Rig.Read(Read(guerrillaTables, PeopleRig));
        var mason = Rig.Read(Read(guerrillaTables, MasonRig));
        peopleRigs[mason.Bones.Count] = mason;
        foreach (var entry in guerrillaTables.Entries.Where(e => e.Name.EndsWith(".rig_pc", StringComparison.OrdinalIgnoreCase)))
        {
            Rig rig;
            try { rig = Rig.Read(guerrillaTables.Read(entry)); }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException) { continue; }
            if (rig.Bones.Count >= people.Bones.Count && !peopleRigs.ContainsKey(rig.Bones.Count) &&
                Enumerable.Range(0, people.Bones.Count).All(i => rig.Bones[i].Name.Equals(people.Bones[i].Name, StringComparison.OrdinalIgnoreCase) &&
                                                                 rig.Bones[i].Parent == people.Bones[i].Parent))
                peopleRigs[rig.Bones.Count] = rig;
        }
        darius = Rig.Read(Read(gameTables, DariusRig));
        setList = XtblFile.FromBytes(Read(gameTables, SetList));
        fileList = XtblFile.FromBytes(Read(gameTables, FileList));
        stateList = XtblFile.FromBytes(Read(gameTables, StateList));
        actionList = XtblFile.FromBytes(Read(gameTables, ActionList));
        fileEntries = fileList.Table?.Element("Files")?.Element("Anim_files") ?? throw new InvalidDataException($"Armageddon's {FileList} has no Files/Anim_files");
        gameAnimations = new(fileEntries.Elements.Select(AnimationName).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        gameSets = new(setList.Entries.Select(e => e.ValueOf("Name")?.Trim()).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        ourStates = new(stateList.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        ourActions = new(actionList.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        // Armageddon's own sets: each listed file's (a file this can't find counts as one), and its rig objects'.
        int ownSets = 0;
        foreach (string name in setList.Entries.Select(e => e.ValueOf("Name")?.Trim()).OfType<string>())
            ownSets += gameTables.Find(name) is { } file
                ? Math.Max(1, XtblFile.FromBytes(gameTables.Read(file)).Entries.Count(e => e.Name.Equals("Skeleton_Set", StringComparison.OrdinalIgnoreCase)))
                : 1;
        if (gameTables.Find(RigObjects) is { } rigObjects)
            ownSets += XtblFile.FromBytes(gameTables.Read(rigObjects)).Entries.Count(e => e.Name.Equals("Anim_Rig_Object", StringComparison.OrdinalIgnoreCase));
        GameSets = ownSets;

        var rfgFiles = XtblFile.FromBytes(Read(guerrillaTables, FileList)).Table?.Element("Files")?.Element("Anim_files");
        foreach (var e in rfgFiles?.Elements ?? [])
            if (AnimationName(e) is { } name) rfgFileEntries.TryAdd(name, e);
    }

    static byte[] Read(Packfile pack, string name) =>
        pack.Read(pack.Find(name) ?? throw new InvalidDataException($"no {name} in {pack.Name}"));

    /// <summary>An anim_files.xtbl entry's animation (x.animx).</summary>
    static string? AnimationName(XtblElement entry) => entry.Element("Animation")?.ValueOf("Filename")?.Trim();

    /// <summary>
    /// Guerrilla's set <paramref name="set"/> (its Skeleton_Set name, as an interaction's Anim_Set names
    /// it) for Armageddon: its name there (null if it can't be had), and its animations to pack in the
    /// vehicle's container, looked for first in <paramref name="container"/> (Guerrilla's vehicle
    /// container), then in Guerrilla's anims.vpp_pc. Each set and animation is converted once; a set
    /// asked for again gives its animations again (each vehicle's container has its own copies).
    /// </summary>
    public (string? Set, IReadOnlyList<(string Name, byte[] Data)> Animations) Port(string set, Packfile? container, List<string> log)
    {
        if (setNames.TryGetValue(set, out var known) && known.Length == 0) return (null, []);
        if (known == null && setTables.Count >= Room)
        {
            setNames[set] = "";
            noRoom.Add(set);
            log.Add($"animation set {set}: left out, the game holds {MaxAnimSets} animation sets and Armageddon's own and ours already fill them");
            return (null, []);
        }
        string rfgFile = set + ".xtbl";
        var rfgSet = guerrillaTables.Find(rfgFile);
        if (rfgSet == null) { setNames[set] = ""; log.Add($"animation set {set}: Guerrilla's misc.vpp_pc has no {rfgFile}"); return (null, []); }
        var table = XtblFile.FromBytes(guerrillaTables.Read(rfgSet));
        var skeletonSet = table.Entries.FirstOrDefault(e => e.Name.Equals("Skeleton_Set", StringComparison.OrdinalIgnoreCase));
        if (skeletonSet == null) { setNames[set] = ""; log.Add($"animation set {set}: {rfgFile} has no Skeleton_Set"); return (null, []); }

        // Its animations, each moved onto Darius's skeleton (all of them, or the set is left out).
        var files = Descendants(skeletonSet, "Animation").Select(a => a.ValueOf("Filename")?.Trim()).OfType<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // Guerrilla's x.animx → ours
        var made = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);  // our .anim_pc files new in this set
        var missing = new List<string>();
        foreach (string file in files)
        {
            string name = gameAnimations.Contains(file) ? "rfg_" + file : file;
            string pc = Path.ChangeExtension(name, ".anim_pc"), rfgPc = Path.ChangeExtension(file, ".anim_pc");
            ours[file] = name;
            if (animations.ContainsKey(pc) || made.ContainsKey(pc)) continue;
            Packfile? source = container?.Find(rfgPc) != null ? container : guerrillaAnims.Find(rfgPc) != null ? guerrillaAnims : null;
            if (source == null) { missing.Add(file); continue; }
            byte[] bytes = source.Read(source.Find(rfgPc)!);
            if (bytes.Length < 0x30) { missing.Add($"{file} (too short to be an animation)"); continue; }
            bool additive = rfgFileEntries.TryGetValue(file, out var rfgEntry) &&
                            (rfgEntry.Element("Flags")?.Elements.Any(f => f.Value.Trim().Equals("Additive", StringComparison.OrdinalIgnoreCase)) ?? false);
            try
            {
                made[pc] = !peopleRigs.TryGetValue(bytes[0xB], out var skeleton) ? GuerrillaAnimations.Recode(bytes)
                         : additive ? GuerrillaAnimations.RetargetAdditive(bytes, skeleton, darius)
                         : GuerrillaAnimations.Retarget(bytes, skeleton, darius);
            }
            catch (InvalidDataException e) { missing.Add($"{file} ({e.Message})"); }
        }
        if (missing.Count > 0)
        {
            setNames[set] = "";
            log.Add($"animation set {set}: left out, its animations can't all be had: {string.Join(", ", missing)}");
            return (null, []);
        }
        foreach (var (file, name) in ours)
        {
            string pc = Path.ChangeExtension(name, ".anim_pc");
            if (!made.TryGetValue(pc, out var bytes)) continue;
            animations[pc] = bytes;
            AddFileEntry(file, name, log);
        }

        // Its table, with our names, and the state and action names Armageddon hasn't.
        string ourSet = gameSets.Contains(rfgFile) || gameTables.Find(rfgFile) != null ? "rfg_" + set : set;
        string ourFile = ourSet + ".xtbl";
        if (!setNames.ContainsKey(set))
        {
            if (skeletonSet.Element("Name") is { } nameField) nameField.Value = ourSet;
            if (skeletonSet.Element("Variant_Type") is { } variant)
            {
                log.Add($"animation set {set}: left out its Variant_Type {variant.Value.Trim()} (Armageddon's sets have none)");
                variant.Remove();
            }
            foreach (var animation in Descendants(skeletonSet, "Animation"))
                if (animation.Element("Filename") is { } filename && ours.TryGetValue(filename.Value.Trim(), out var renamed))
                    filename.Value = renamed;
            var added = new List<string>();
            foreach (var id in Descendants(skeletonSet, "State").Select(s => s.ValueOf("ID")?.Trim()).OfType<string>())
                if (ourStates.Add(id)) { stateList.NewEntry(id, ["Entries", id[..1].ToLowerInvariant()]); statesChanged = true; added.Add($"state \"{id}\""); }
            foreach (var id in Descendants(skeletonSet, "Action").Select(s => s.ValueOf("ID")?.Trim()).OfType<string>())
                if (ourActions.Add(id)) { actionList.NewEntry(id, ["Entries", id[..1].ToLowerInvariant()]); actionsChanged = true; added.Add($"action \"{id}\""); }
            setTables.Add((ourFile, table.ToBytes()));
            var listed = guerrillaTables.Find(SetList) is { } list
                ? XtblFile.FromBytes(guerrillaTables.Read(list)).Entries.FirstOrDefault(e => string.Equals(e.ValueOf("Name")?.Trim(), rfgFile, StringComparison.OrdinalIgnoreCase))
                : null;
            var listing = listed?.Copy() ?? new XtblElement("Anim_Set_Filename");
            if (listing.Element("Name") is { } listedName) listedName.Value = ourFile;
            else listing.AddElement("Name", ourFile);
            setList.Table!.AddElement(listing);
            setNames[set] = ourSet;
            log.Add($"animation set {set}" + (ourSet != set ? $" (as {ourSet})" : "") + $": {files.Count} animations" +
                    (added.Count > 0 ? $"; names Armageddon's tables lacked, added: {string.Join(", ", added)}" : ""));
        }
        return (ourSet, ours.Values.Select(n => Path.ChangeExtension(n, ".anim_pc")).Select(pc => (pc, animations[pc])).ToList());
    }

    /// <summary>Guerrilla's anim_files.xtbl entry for an animation, under our name, added to Armageddon's (or a plain one if Guerrilla's table hasn't it).</summary>
    void AddFileEntry(string rfgFile, string ourFile, List<string> log)
    {
        var entry = rfgFileEntries.TryGetValue(rfgFile, out var rfg) ? rfg.Copy() : new XtblElement("Anim_file");
        var animation = entry.Element("Animation") ?? entry.AddElement("Animation");
        if (animation.Element("Filename") is { } filename) filename.Value = ourFile;
        else animation.AddElement("Filename", ourFile);
        // In the vehicle's container, loaded with it (as Armageddon's own vehicles' animations).
        if (animation.Element("Preload") is { } preload) preload.Value = "False";
        else animation.AddElement("Preload", "False");
        foreach (var part in entry.Elements.ToList())
        {
            bool leftOut = LeftOutOfFiles.Contains(part.Name, StringComparer.OrdinalIgnoreCase);
            if (leftOut && part.HasElements) log.Add($"animation {rfgFile}: its {part.Name} left out until Guerrilla's audio is ported");
            if (leftOut || (!part.HasElements && part.Value.Trim().Length == 0)) part.Remove();
        }
        fileEntries.AddElement(entry);
    }

    /// <summary>
    /// Writes the sets' tables (new files, in <paramref name="cache"/>: overrides\build\pc\cache\) and the
    /// lists they change (in <paramref name="modFolder"/>, for guerrilla_world to hand the game; a list
    /// with nothing of ours added is deleted there, so the game keeps its own).
    /// </summary>
    public void Write(string cache, string modFolder)
    {
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(modFolder);
        // The set tables an earlier run wrote that this one doesn't (the old list names them).
        string oldList = Path.Combine(modFolder, SetList);
        if (File.Exists(oldList))
            foreach (string name in XtblFile.FromBytes(File.ReadAllBytes(oldList)).Entries.Select(e => e.ValueOf("Name")?.Trim()).OfType<string>())
                if (!gameSets.Contains(name) && !setTables.Any(t => t.File.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(Path.Combine(cache, name)))
                    File.Delete(Path.Combine(cache, name));
        foreach (var (file, data) in setTables) File.WriteAllBytes(Path.Combine(cache, file), data);
        void Put(string name, XtblFile table, bool changed)
        {
            string path = Path.Combine(modFolder, name);
            if (changed) File.WriteAllBytes(path, table.ToBytes());
            else if (File.Exists(path)) File.Delete(path);
        }
        Put(SetList, setList, setTables.Count > 0);
        Put(FileList, fileList, animations.Count > 0);
        Put(StateList, stateList, statesChanged);
        Put(ActionList, actionList, actionsChanged);
    }

    /// <summary>Takes away what <see cref="Write"/> wrote: the sets' tables (the ones our list has that the game's hasn't) and the lists.</summary>
    public static IEnumerable<string> Remove(string game, string overrides, string modFolder)
    {
        string ourList = Path.Combine(modFolder, SetList);
        if (File.Exists(ourList))
        {
            var gameTables = Packfile.Open(Path.Combine(game, "build", "pc", "cache", "misc.vpp_pc"));
            var theirs = new HashSet<string>(XtblFile.FromBytes(Read(gameTables, SetList)).Entries.Select(e => e.ValueOf("Name")?.Trim()).OfType<string>(),
                                             StringComparer.OrdinalIgnoreCase);
            foreach (string name in XtblFile.FromBytes(File.ReadAllBytes(ourList)).Entries.Select(e => e.ValueOf("Name")?.Trim()).OfType<string>())
            {
                if (theirs.Contains(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                string file = Path.Combine(overrides, "build", "pc", "cache", name);
                if (File.Exists(file)) { File.Delete(file); yield return $"Deleted {file}"; }
            }
        }
        foreach (string name in new[] { SetList, FileList, StateList, ActionList })
        {
            string path = Path.Combine(modFolder, name);
            if (File.Exists(path)) { File.Delete(path); yield return $"Deleted {path}"; }
        }
    }

    /// <summary>Every element of a name inside another, at any depth, in document order.</summary>
    static IEnumerable<XtblElement> Descendants(XtblElement element, string name)
    {
        foreach (var child in element.Elements)
        {
            if (child.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) yield return child;
            foreach (var inner in Descendants(child, name)) yield return inner;
        }
    }
}
