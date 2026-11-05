using RfaFormats.Tables;
using RfaFormats.Textures;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's vehicles made usable in Armageddon, from the player's own copy of Guerrilla
/// (only read): GuerrillaArsenal.exe, for the guerrilla_world mod. For each vehicle asked for:
///   - its model converted (<see cref="GuerrillaVehicles"/>) and packed with its textures (each one of
///     Guerrilla's, from the vehicle's own texture file, as a single-texture file under its alias, its
///     compressed mip levels down to 4 × 4) into a container of its own, named as Guerrilla's
///     (&lt;mesh&gt;.ccar_pc.str2_pc) in overrides\build\pc\cache\, where the game finds a new file by name;
///   - an entry for that container added to Armageddon's vehicle asset list (an override of
///     vehicles.vpp_pc\vehicle_containers.asm_pc, the player's own with ours added), as Armageddon lists
///     its own vehicles (type 27, flags 0x80; the model a primitive of type 11, each texture of type 1);
///   - its vehicles.xtbl entry, Guerrilla's own, added to Armageddon's table, which the guerrilla_world
///     mod hands the game while it starts (mods\guerrilla_world\vehicles.xtbl). The two tables describe a
///     vehicle with nearly the same fields; a field Armageddon's table hasn't is left out (Guerrilla's
///     vehicle family, AI speed limits, ...), and so is a value Armageddon has no choice for; a field
///     Armageddon requires that Guerrilla's hasn't (its electric hit points, memory slot, the cameras'
///     vertical limits, ...) gets the default Armageddon's description gives it. For now
///     also left out: the sounds (Guerrilla's are XACT cues, Armageddon plays Wwise events), the lights
///     and other effects (Guerrilla names effects Armageddon's tables don't load), and the turrets;
///   - its vehicle_interactions.xtbl entry (where the player and passengers get in, sit, move between
///     seats and get out, and how: opening the door, climbing in, quick and diving exits, crawling out of
///     an upturned car), Guerrilla's own, added to Armageddon's table
///     (mods\guerrilla_world\vehicle_interactions.xtbl, handed over the same way). Both games name the
///     same steps, actions and animation states ("vehicle enter primary 1" ends in "driving forward",
///     which is what puts the player behind the wheel); Guerrilla's Door is Armageddon's Door_1. The
///     doors' sound cues are left out until Guerrilla's audio is ported;
///   - the animation set the interaction plays (Vehicle_Taxi, ...), Guerrilla's own, with Mason's
///     animations moved onto Darius's skeleton and packed in the vehicle's container
///     (<see cref="GuerrillaAnimationSets"/>). Without the animations a transition that names a door
///     stops the player at it ("Vehicle interaction anim missing open 'DOOR_L' anim or trigger"): the
///     door opens on the "door start" and "door end" triggers of the person's animation.
/// Guerrilla's teams are Armageddon's where it has them (Guerilla the Red Faction).
/// </summary>
public static class GuerrillaArsenal
{
    /// <summary>The vehicles built when none are named: every one that converts.</summary>
    public static readonly string[] DefaultVehicles = ["all"];

    /// <summary>Guerrilla's teams as Armageddon's (human_team_names.xtbl).</summary>
    static readonly Dictionary<string, string> Teams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Guerilla"] = "Red Faction", ["Guerrilla"] = "Red Faction", ["Civilian"] = "Civilian", ["Marauder"] = "Marauder",
    };

    /// <summary>
    /// The fields the two games measure differently (each table's description says how), and what a
    /// value of Guerrilla's is multiplied by for Armageddon: the vehicle's price, Guerrilla's in millions,
    /// Armageddon's in whole units (most of its vehicles 20,000; Guerrilla's cars 0.02); a flyer's turn
    /// and tilt rates, Guerrilla's in revolutions a second (and a second squared), Armageddon's in
    /// degrees (its default turn 90, Guerrilla's 0.25). Armageddon's are whole numbers: a decimal stops
    /// its table reader ("Could not parse '.25' into an integer").
    /// </summary>
    static readonly Dictionary<string, double> UnitScales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Value"] = 1_000_000,
        ["FlyerParams/FlyerMaxTurnAngvel"] = 360, ["FlyerParams/FlyerMaxTurnAngaccl"] = 360,
        ["FlyerParams/FlyerMaxTiltAngvel"] = 360, ["FlyerParams/FlyerMaxTiltAngaccl"] = 360,
    };

    /// <summary>The fields of Guerrilla's entry left out for now (see the summary), whatever Armageddon's table has.</summary>
    static readonly string[] NotYet = ["Foley", "Effects", "Turrets"];

    /// <summary>The files this writes (relative to the overrides folder, then to the mod's folder), for --remove.</summary>
    public const string AssetList = @"build\pc\cache\vehicles.vpp_pc\vehicle_containers.asm_pc", Table = "vehicles.xtbl",
                        InteractionTable = "vehicle_interactions.xtbl";

    /// <summary>
    /// The vehicles' own skeletons Armageddon hasn't, one file name a line, in the guerrilla_vehicles mod's
    /// folder (mods\guerrilla_vehicles\rigs.txt, beside mods\guerrilla_world\). The game loads every
    /// skeleton its archives list while it starts, and a vehicle whose skeleton isn't loaded stops it
    /// (its table entry names it; a null where the skeleton should be): a new file isn't in any
    /// archive's list, so the mod has the game load these by name after its own.
    /// </summary>
    public const string RigList = "rigs.txt";
    static string RigListFile(string modFolder) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modFolder))!, "guerrilla_vehicles", RigList);

    /// <summary>
    /// Builds the vehicles named (Guerrilla's vehicles.xtbl entry names; "all" for every one that converts)
    /// into <paramref name="overrides"/> and <paramref name="modFolder"/> (mods\guerrilla_world\).
    /// </summary>
    public static IEnumerable<string> Build(string guerrilla, string game, string overrides, string modFolder, IReadOnlyList<string> vehicles)
    {
        string guerrillaCache = Path.Combine(guerrilla, "build", "pc", "cache"), gameCache = Path.Combine(game, "build", "pc", "cache");
        var guerrillaTables = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
        var gameTables = Packfile.Open(Path.Combine(gameCache, "misc.vpp_pc"));
        XtblFile ReadTable(Packfile pack, string name) => XtblFile.FromBytes(pack.Read(pack.Find(name) ?? throw new InvalidDataException($"no {name} in {pack.Name}")));
        var rfgVehicles = ReadTable(guerrillaTables, "vehicles.xtbl");
        var ourVehicles = ReadTable(gameTables, "vehicles.xtbl");
        var description = ourVehicles.Description ?? throw new InvalidDataException("Armageddon's vehicles.xtbl has no description");
        var groups = new HashSet<string>(ReadTable(gameTables, "vehicle_groups.xtbl").ReferenceTargets("Vehicle_Group.Name"), StringComparer.OrdinalIgnoreCase);
        var rfgInteractions = ReadTable(guerrillaTables, InteractionTable);
        var ourInteractions = ReadTable(gameTables, InteractionTable);
        var interactionDescription = ourInteractions.Description ?? throw new InvalidDataException("Armageddon's vehicle_interactions.xtbl has no description");
        var takenInteractions = new HashSet<string>(ourInteractions.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        var sets = new GuerrillaAnimationSets(guerrillaTables, gameTables, Packfile.Open(Path.Combine(guerrillaCache, "anims.vpp_pc")));
        var guerrillaArchive = Packfile.Open(Path.Combine(guerrillaCache, "vehicles_r.vpp_pc"));
        var gameArchive = Packfile.Open(Path.Combine(gameCache, "vehicles.vpp_pc"));
        var assetList = AsmFile.Read(gameArchive.Read(gameArchive.Find("vehicle_containers.asm_pc") ?? throw new InvalidDataException("no vehicle_containers.asm_pc in vehicles.vpp_pc")));

        // Guerrilla's entries, the first of each name (its table repeats a few names).
        var entries = new Dictionary<string, XtblElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in rfgVehicles.Entries) entries.TryAdd(XtblFile.EntryName(e), e);
        var wanted = vehicles.Count == 1 && vehicles[0].Equals("all", StringComparison.OrdinalIgnoreCase) ? entries.Keys.ToList() : vehicles.ToList();
        var taken = new HashSet<string>(ourVehicles.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        var takenNames = new HashSet<string>(ourVehicles.Entries.Select(e => e.ValueOf("Display_Name") ?? ""), StringComparer.OrdinalIgnoreCase);
        var containers = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);  // file name → container
        var built = new List<string>();
        var rigs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);  // Guerrilla's vehicle skeletons Armageddon hasn't: file → bytes

        foreach (string name in wanted)
        {
            if (!entries.TryGetValue(name, out var entry)) { yield return $"{name}: Guerrilla's vehicles.xtbl has no such vehicle"; continue; }
            if (taken.Contains(name)) { yield return $"{name}: Armageddon has a vehicle of that name already; left out"; continue; }
            string? meshFile = entry.Element("Mesh_Name")?.ValueOf("Filename");
            if (string.IsNullOrEmpty(meshFile)) { yield return $"{name}: no model named"; continue; }
            string mesh = Path.GetFileNameWithoutExtension(meshFile);
            string containerName = mesh + ".ccar_pc";
            if (gameArchive.Find(containerName + ".str2_pc") != null) { yield return $"{name}: Armageddon has a model named {mesh} already; left out"; continue; }

            // Its own skeleton, if it has one (walkers, flyers, turret trucks): Armageddon's if it has one of
            // that name, else Guerrilla's, as a new file the guerrilla_vehicles mod loads (see RigList).
            string? rigFile = entry.Element("Rig")?.Element("Rig")?.ValueOf("Filename")?.Trim();
            string? rig = string.IsNullOrEmpty(rigFile) ? null : Path.ChangeExtension(Path.GetFileName(rigFile), ".rig_pc").ToLowerInvariant();
            if (rig != null && gameTables.Find(rig) == null && !rigs.ContainsKey(rig))
            {
                if (guerrillaTables.Find(rig) is not { } rfgRig)
                {
                    yield return $"{name}: its skeleton {rig} is in neither game; left out (the game stops on a vehicle whose skeleton isn't loaded)";
                    continue;
                }
                rigs[rig] = guerrillaTables.Read(rfgRig);
            }

            // Its interaction (where and how people get in), Guerrilla's, and the animation set it plays.
            string? interactionName = entry.ValueOf("Vehicle_Interaction");
            var rfgInteraction = interactionName == null ? null
                : rfgInteractions.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(interactionName, StringComparison.OrdinalIgnoreCase));
            string? rfgSet = rfgInteraction?.ValueOf("Anim_Set")?.Trim();
            string? ourSet = null;
            var setLog = new List<string>();

            // The model, its textures, and the set's animations (each vehicle's container has its own).
            if (containers.ContainsKey(containerName + ".str2_pc"))
            {
                if (!string.IsNullOrEmpty(rfgSet)) (ourSet, _) = sets.Port(rfgSet, null, setLog);
            }
            else
            {
                var source = guerrillaArchive.Find(containerName + ".str2_pc");
                if (source == null) { yield return $"{name}: Guerrilla's vehicles_r.vpp_pc has no {containerName}.str2_pc"; continue; }
                var pack = guerrillaArchive.OpenContainer(source);
                byte[] Read(string file) => pack.Read(pack.Find(file) ?? throw new InvalidDataException($"{source.Name} has no {file}"));
                byte[] cFile = Read(mesh + ".ccar_pc"), gFile = Read(mesh + ".gcar_pc");
                if (GuerrillaVehicles.NotYet(cFile) is { } why) { yield return $"{name}: {why}; left out"; continue; }
                var (header, textures) = GuerrillaVehicles.Convert(cFile, t => t == GuerrillaMaterials.GlassReflection ? t : GuerrillaVehicles.Alias(t));
                var files = new List<(string Name, byte[] Data)> { (mesh + ".ccar_pc", header), (mesh + ".gcar_pc", gFile) };
                var pegs = pack.Entries.Where(e => e.Name.EndsWith(".cpeg_pc", StringComparison.OrdinalIgnoreCase))
                               .Select(e => PegFile.Read(pack.Read(e), Read(PegFile.DataFileName(e.Name)!))).ToList();
                var missing = new List<string>();
                foreach (string texture in textures)
                {
                    var made = texture == GuerrillaMaterials.GlassReflection ? GlassReflection(gameCache)
                             : GuerrillaBuiltIns.Files(texture, GuerrillaVehicles.Alias(texture)) ?? Single(pegs, texture, GuerrillaVehicles.Alias(texture));
                    if (made == null) missing.Add(texture);
                    else files.AddRange(made);
                }
                int animationCount = 0;
                if (!string.IsNullOrEmpty(rfgSet))
                {
                    (ourSet, var animations) = sets.Port(rfgSet, pack, setLog);
                    files.AddRange(animations);
                    animationCount = animations.Count;
                }
                byte[] container = Packfile.CreateContainer(files);
                containers[containerName + ".str2_pc"] = container;
                assetList.Containers.RemoveAll(c => c.Name.Equals(containerName, StringComparison.OrdinalIgnoreCase));
                assetList.Containers.Add(Entry(containerName, container, files));
                yield return $"{name}: model {mesh} ({cFile.Length:N0} → {header.Length:N0} bytes), {textures.Count - missing.Count} textures" +
                             (missing.Count > 0 ? $" ({missing.Count} not found: {string.Join(", ", missing)})" : "") +
                             (animationCount > 0 ? $", {animationCount} animations" : "");
            }
            foreach (string line in setLog) yield return $"{name}: {line}";

            // The table entry.
            var copy = entry.Copy();
            var dropped = new List<string>();
            foreach (string field in NotYet)
                if (copy.Element(field) is { } part) { part.Remove(); dropped.Add(field); }
            Prune(copy, description.Children, "", dropped);
            var rounded = new List<string>();
            FitValues(copy, description.Children, "", rounded);
            // The skeleton by the name it is loaded under (in lower case: the game's own names are).
            if (rig != null && copy.Element("Rig")?.Element("Rig")?.Element("Filename") is { } rigName)
                rigName.Value = Path.ChangeExtension(rig, ".rigx");

            // Its interaction: Guerrilla's, converted, unless Armageddon has one of that name.
            if (rfgInteraction != null && ourSet != null && !takenInteractions.Contains(interactionName!))
            {
                var interaction = ConvertInteraction(rfgInteraction, interactionDescription, ourSet, out var left);
                ourInteractions.Table!.AddElement(interaction);
                takenInteractions.Add(interactionName!);
                yield return $"{name}: interaction {interactionName} ({interaction.Element("Waypoints")?.Elements.Count() ?? 0} waypoints, " +
                             $"{interaction.Element("Waypoint_transitions")?.Elements.Count() ?? 0} transitions, animation set {ourSet})" +
                             (left.Count > 0 ? $"; left out until Guerrilla's audio is ported: {string.Join(", ", left.Distinct())}" : "");
            }
            if (rfgInteraction != null && takenInteractions.Contains(interactionName!)) Set(copy, "Vehicle_Interaction", interactionName!);
            else
            {
                // No interaction: the vehicle can't be got into (as Guerrilla's own without one, and two of
                // Armageddon's). Not Armageddon's Dummy: with it the player gets in and can't drive or get out.
                copy.Element("Vehicle_Interaction")?.Remove();
                if (interactionName != null)
                    yield return rfgInteraction == null
                        ? $"{name}: Guerrilla has no interaction {interactionName}; it can't be got into"
                        : $"{name}: its interaction's animations couldn't be had; it can't be got into yet";
            }
            string? team = copy.ValueOf("Default_Team");
            if (team != null) Set(copy, "Default_Team", Teams.TryGetValue(team, out var ours) ? ours : "Civilian");
            if (copy.ValueOf("Group") is { } group && !groups.Contains(group)) { copy.Element("Group")!.Remove(); dropped.Add($"Group {group}"); }
            string display = copy.ValueOf("Display_Name") ?? name;
            if (takenNames.Contains(display)) Set(copy, "Display_Name", display = "rfg " + display);
            var added = new List<string>();
            var unfilled = new List<string>();
            FillRequired(copy, description.Children, "", added, unfilled);
            ourVehicles.Table!.AddElement(copy);
            taken.Add(name);
            takenNames.Add(display);
            built.Add($"{name} (\"{display}\")");
            if (dropped.Count > 0) yield return $"{name}: left out of its table entry: {string.Join(", ", dropped)}";
            if (rounded.Count > 0) yield return $"{name}: decimals where Armageddon reads whole numbers, rounded: {string.Join(", ", rounded)}";
            if (added.Count > 0) yield return $"{name}: Armageddon's defaults added for the fields it requires: {string.Join(", ", added.Distinct())}";
            if (unfilled.Count > 0) yield return $"{name}: required by Armageddon, with no default to give (the game will stop on them): {string.Join(", ", unfilled)}";
        }

        string cache = Path.Combine(overrides, "build", "pc", "cache");
        Directory.CreateDirectory(cache);
        foreach (var (file, bytes) in containers) File.WriteAllBytes(Path.Combine(cache, file), bytes);
        string listFile = Path.Combine(overrides, AssetList);
        Directory.CreateDirectory(Path.GetDirectoryName(listFile)!);
        File.WriteAllBytes(listFile, assetList.ToBytes());
        Directory.CreateDirectory(modFolder);
        File.WriteAllBytes(Path.Combine(modFolder, Table), ourVehicles.ToBytes());
        File.WriteAllBytes(Path.Combine(modFolder, InteractionTable), ourInteractions.ToBytes());
        sets.Write(cache, modFolder);
        foreach (var (file, bytes) in rigs) File.WriteAllBytes(Path.Combine(cache, file), bytes);
        string rigList = RigListFile(modFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(rigList)!);
        File.WriteAllLines(rigList, rigs.Keys.Order(StringComparer.OrdinalIgnoreCase));
        if (rigs.Count > 0) yield return $"Skeletons: {rigs.Count} of Guerrilla's vehicles' own, for the guerrilla_vehicles mod to load: {string.Join(", ", rigs.Keys)}";
        yield return $"Animation sets: Armageddon's own fill {sets.GameSets} of the {GuerrillaAnimationSets.MaxAnimSets} the game holds; " +
                     $"{sets.Room} were left for Guerrilla's" +
                     (sets.LeftOutForRoom.Count > 0 ? $", so these vehicles' sets were left out (they can't be got into): {string.Join(", ", sets.LeftOutForRoom)}" : "");
        yield return built.Count == 0 ? "No vehicles built." : $"Vehicles: {string.Join(", ", built)}";
    }

    /// <summary>
    /// Guerrilla's interaction entry for Armageddon (see the summary): its waypoints, transitions and
    /// steps as they are, each transition's door (Guerrilla's Door) as Armageddon's first (Door_1), the
    /// animation set as ported (<paramref name="animSet"/>), then pruned and filled as the vehicle entries
    /// are. The doors' sound cues (Guerrilla's XACT cues) are left out; <paramref name="leftOut"/> names them.
    /// </summary>
    static XtblElement ConvertInteraction(XtblElement rfg, FieldDescription description, string animSet, out List<string> leftOut)
    {
        var copy = rfg.Copy();
        leftOut = [];
        foreach (var transition in copy.Element("Waypoint_transitions")?.Elements ?? [])
        {
            if (transition.Element("Door") is { } door)
            {
                transition.InsertAfter(door, new XtblElement("Door_1") { Value = door.Value });
                door.Remove();
            }
            foreach (string cue in new[] { "Door_open", "Door_close" })
                if (transition.Element(cue) is { } sound)
                {
                    leftOut.Add(sound.Value.Trim());
                    sound.Remove();
                }
        }
        Set(copy, "Anim_Set", animSet);
        Prune(copy, description.Children, "", []);
        FitValues(copy, description.Children, "", []);
        FillRequired(copy, description.Children, "", [], []);
        return copy;
    }

    /// <summary>Takes away everything <see cref="Build"/> wrote: the vehicles' containers (the ones the asset list names that Armageddon hasn't), the asset list, the tables and the animation sets.</summary>
    public static IEnumerable<string> Remove(string game, string overrides, string modFolder)
    {
        foreach (string line in GuerrillaAnimationSets.Remove(game, overrides, modFolder)) yield return line;
        string rigList = RigListFile(modFolder);
        if (File.Exists(rigList))
        {
            foreach (string rig in File.ReadAllLines(rigList).Select(l => l.Trim()).Where(l => l.Length > 0 && l.IndexOfAny(Path.GetInvalidFileNameChars()) < 0))
            {
                string file = Path.Combine(overrides, "build", "pc", "cache", rig);
                if (File.Exists(file)) { File.Delete(file); yield return $"Deleted {file}"; }
            }
            File.Delete(rigList);
            yield return $"Deleted {rigList}";
        }
        string listFile = Path.Combine(overrides, AssetList);
        if (File.Exists(listFile))
        {
            var gameArchive = Packfile.Open(Path.Combine(game, "build", "pc", "cache", "vehicles.vpp_pc"));
            foreach (var c in AsmFile.Read(File.ReadAllBytes(listFile)).Containers.Where(c => gameArchive.Find(c.FileName) == null))
            {
                string file = Path.Combine(overrides, "build", "pc", "cache", c.FileName);
                if (File.Exists(file)) { File.Delete(file); yield return $"Deleted {file}"; }
            }
            File.Delete(listFile);
            yield return $"Deleted {listFile}";
        }
        foreach (string name in new[] { Table, InteractionTable })
        {
            string table = Path.Combine(modFolder, name);
            if (File.Exists(table)) { File.Delete(table); yield return $"Deleted {table}"; }
        }
    }

    /// <summary>
    /// Leaves out of an entry (or a group in it) every field Armageddon's description hasn't, and every
    /// Selection value it has no choice for; <paramref name="dropped"/> names them.
    /// </summary>
    static void Prune(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, List<string> dropped)
    {
        foreach (var child in element.Elements.ToList())
        {
            if (child.Name.Equals("_Editor", StringComparison.OrdinalIgnoreCase)) continue;
            var field = fields.FirstOrDefault(f => f.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase));
            if (field == null) { child.Remove(); dropped.Add(path + child.Name); continue; }
            switch (field.Type)
            {
                case FieldType.Element:
                    Prune(child, field.Children, path + child.Name + "/", dropped);
                    break;
                case FieldType.List or FieldType.Grid when field.Item is { Type: FieldType.Element } item:
                    foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)).ToList())
                        Prune(one, item.Children, path + child.Name + "/", dropped);
                    break;
                case FieldType.Selection when field.Choices.Count > 0 && !child.HasElements &&
                                              !field.Choices.Contains(child.Value.Trim(), StringComparer.OrdinalIgnoreCase):
                    dropped.Add($"{path}{child.Name} {child.Value.Trim()}");
                    child.Remove();
                    break;
            }
        }
    }

    /// <summary>
    /// Writes an entry's numbers (in it, its groups and its lists' items) as Armageddon reads them: a
    /// field measured differently (<see cref="UnitScales"/>) converted; then a decimal in a field
    /// Armageddon's description makes a whole number (Int), which would stop its table reader, rounded,
    /// and named in <paramref name="rounded"/> (none are known; a new one wants its unit looked at).
    /// </summary>
    static void FitValues(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, List<string> rounded)
    {
        foreach (var child in element.Elements)
        {
            var field = fields.FirstOrDefault(f => f.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;
            string at = path + field.Name;
            if (field.Type == FieldType.Element) { FitValues(child, field.Children, at + "/", rounded); continue; }
            if (field.Type is FieldType.List or FieldType.Grid && field.Item is { Type: FieldType.Element } item)
            {
                foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)))
                    FitValues(one, item.Children, at + "/", rounded);
                continue;
            }
            if (child.HasElements || field.Type is not (FieldType.Int or FieldType.Float)) continue;
            if (!double.TryParse(child.Value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)) continue;
            if (UnitScales.TryGetValue(at, out double scale)) value *= scale;
            else if (field.Type != FieldType.Int || value == Math.Floor(value)) continue;
            if (field.Type == FieldType.Int)
            {
                double whole = Math.Round(value);
                if (!UnitScales.ContainsKey(at) && whole != value) rounded.Add($"{at} {child.Value.Trim()}");
                child.Value = whole.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }
            else child.Value = value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Adds to an entry (or a group in it, or each item of a list in it) every field Armageddon's
    /// description marks as required that it hasn't, with the description's default: the game's table
    /// reader stops on a missing one ("Could not find required attribute Vehicle:Neutral_Reticule").
    /// A missing group is added with its own required fields. <paramref name="unfilled"/> names the
    /// required fields with no default.
    /// </summary>
    static void FillRequired(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, List<string> added, List<string> unfilled)
    {
        foreach (var field in fields)
        {
            var child = element.Element(field.Name);
            if (child == null && field.Required)
            {
                if (field.Type == FieldType.Element) child = element.AddElement(field.Name);
                else if (field.Default != null) element.AddElement(field.Name, field.Default);
                else { unfilled.Add(path + field.Name); continue; }
                added.Add(path + field.Name);
            }
            if (child == null) continue;
            if (field.Type == FieldType.Element) FillRequired(child, field.Children, path + field.Name + "/", added, unfilled);
            else if (field.Type is FieldType.List or FieldType.Grid && field.Item is { Type: FieldType.Element } item)
                foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)))
                    FillRequired(one, item.Children, path + field.Name + "/", added, unfilled);
        }
    }

    /// <summary>Sets a field of an entry, adding it if it isn't there.</summary>
    static void Set(XtblElement entry, string field, string value)
    {
        if (entry.Element(field) is { } existing) existing.Value = value;
        else entry.AddElement(field, value);
    }

    /// <summary>The reflection cube glass is given (<see cref="GuerrillaMaterials"/>): Armageddon's own, from its dlc01_precache.vpp_pc, as it is.</summary>
    static (string Name, byte[] Data)[] GlassReflection(string gameCache)
    {
        var precache = Packfile.Open(Path.Combine(gameCache, "dlc01_precache.vpp_pc"));
        byte[] Pre(string name) => precache.Read(precache.Find(name) ?? throw new InvalidDataException($"no {name} in dlc01_precache.vpp_pc"));
        string cube = GuerrillaMaterials.GlassReflection;
        return [(cube + ".cvbm_pc", Pre(cube + ".cvbm_pc")), (cube + ".gvbm_pc", Pre(cube + ".gvbm_pc"))];
    }

    /// <summary>A texture of the vehicle's own texture files as Armageddon's single-texture files under <paramref name="name"/>, or null if none has it.</summary>
    static (string Name, byte[] Data)[]? Single(IReadOnlyList<PegFile> pegs, string texture, string name)
    {
        foreach (var peg in pegs)
        {
            var t = peg.Textures.FirstOrDefault(x => string.Equals(Path.GetFileNameWithoutExtension(x.Name), texture, StringComparison.OrdinalIgnoreCase));
            if (t == null) continue;
            int levels = t.MipLevels;
            if (t.Format.IsBlockCompressed())
            {
                levels = 1;
                while (levels < t.MipLevels && Math.Min(t.Width >> levels, t.Height >> levels) >= 4) levels++;
            }
            var (header, data) = peg.Single(t, name + ".tga", levels);
            return [(name + ".cvbm_pc", header), (name + ".gvbm_pc", data)];
        }
        return null;
    }

    /// <summary>
    /// A vehicle container's asset list entry, as Armageddon's own: the model (type 11) and each texture
    /// (type 1), with their data files' sizes; each animation (type 16, flags 0, no data file).
    /// </summary>
    static AsmContainer Entry(string name, byte[] container, IReadOnlyList<(string Name, byte[] Data)> files)
    {
        var c = Packfile.FromBytes(name, container);
        var entry = new AsmContainer { Name = name, Type = 27, Flags = 0x80, DataOffset = (uint)c.DataStart, CompressedSize = c.CompressedDataSize };
        entry.Sizes.AddRange(files.Select(f => f.Data.Length));
        foreach (var (file, data) in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            byte type = ext switch { ".ccar_pc" => 11, ".cvbm_pc" or ".cpeg_pc" => 1, ".anim_pc" => 16, _ => 0 };
            if (type == 0) continue;
            if (type == 16)
            {
                entry.Primitives.Add(new AsmPrimitive { Name = file, Type = type, Allocator = 0, Flags = 0, SplitIndex = 0, HeaderSize = data.Length, DataSize = 0 });
                continue;
            }
            string dataName = Path.ChangeExtension(file, ".g" + ext[2..]);
            int dataSize = files.FirstOrDefault(f => f.Name.Equals(dataName, StringComparison.OrdinalIgnoreCase)).Data?.Length ?? 0;
            entry.Primitives.Add(new AsmPrimitive { Name = file, Type = type, Allocator = 0, Flags = 4, SplitIndex = 0, HeaderSize = data.Length, DataSize = dataSize });
        }
        return entry;
    }
}
