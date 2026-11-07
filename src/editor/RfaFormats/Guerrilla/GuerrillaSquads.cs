using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's squads (the people its zones' squad spawn nodes put down) in Armageddon's
/// squad tables, for GuerrillaPeople. The two games keep squads differently:
///   - Guerrilla's squads.xtbl gives a squad's team and how many it is (member_count); each member's
///     character and weapon come from squad_progression.xtbl when it spawns: the team's progression set
///     (the EDF's "Primary EDF", ...), at the level the campaign is at (the EDF's progression, the
///     guerrillas' tech level), a weighted list of (character, loadout); a loadout
///     (squad_loadout.xtbl) is a weapon and maybe grenades.
///   - Armageddon's squads.xtbl names each member: a leader and a member list, each a character
///     (member_info) and a loadout (weapon_info) of its squad_loadout.xtbl.
/// So each of Guerrilla's squads is written with member_count members picked from its team's
/// progression at level 1 (the start of the campaign) as Guerrilla's weights would spread them: member k
/// takes the entry its share of the weights falls on. Only Guerrilla's characters made so far
/// (GuerrillaPeople) and loadouts whose weapon has been ported (GuerrillaArsenal --weapons; never
/// Armageddon's own) are picked from; a squad with none to pick isn't written. Picking members by the
/// campaign's level as each squad spawns, as Guerrilla does, is for the guerrilla_people mod later.
/// Guerrilla's loadouts go into squad_loadout.xtbl under their own names (rfg_ where Armageddon has one
/// of that name), their weapon by its ported name, and their grenades (a type and a count, which
/// Armageddon's loadout reader kept) where the grenade's weapon is ported (<see cref="GrenadeWeapons"/>).
/// Both tables are Armageddon's with Guerrilla's added (in mods\guerrilla_world\, which guerrilla_world
/// hands the game at start-up).
/// </summary>
public static class GuerrillaSquads
{
    public const string SquadTable = "squads.xtbl", LoadoutTable = "squad_loadout.xtbl", ResourceTable = "spawn_resource.xtbl";

    /// <summary>
    /// The Guerrilla world's main spawn resource (spawn_resource.xtbl), which its zones with no human group name
    /// (spawn_resource_data; the others name their district's, <see cref="GuerrillaDistricts"/>): where one is in
    /// force the game streams in and makes only the people it lists. Guerrilla's "Default" (101 of its 102 zones)
    /// reserves character types by team (two EDF, three guerrillas) from the zone's people; Armageddon's lists
    /// characters. Each lists Guerrilla's characters made so far for it, and the player Armageddon's own "Default"
    /// gives (Player_Override: Darius), or Guerrilla's own player once the people tool has made him (Mason: the game
    /// takes the start zone's resource's Player_Override for the player, 0x8B77B0, unless a DLC uniform is chosen,
    /// which the guerrilla_people mod sets aside in our worlds). Without them the zones took Armageddon's "Default":
    /// its aliens, Marauders and soldiers streamed into the world.
    /// </summary>
    public const string SpawnResource = "rfg_Default";

    /// <summary>The level of the progression the squads' members are picked at: the campaign's first.</summary>
    const int Level = 1;

    /// <summary>Guerrilla's teams' progression sets (squad_progression.xtbl), by a squad's team_name.</summary>
    static readonly Dictionary<string, string> ProgressionOf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EDF"] = "Primary EDF", ["Marauder"] = "Marauder", ["Guerilla"] = "Guerrilla",
    };

    /// <summary>
    /// Writes both tables into <paramref name="worldFolder"/> for the squads whose members can be had from
    /// <paramref name="characters"/> (Guerrilla's character names made so far) and the weapons the
    /// folder's weapons.xtbl has, and the world's spawn resource; returns the squads written and what was
    /// left out. <paramref name="containerOf"/> gives each character's container (its own name, or the
    /// one it shares): the spawn resource lists one character for each (see <see cref="ResourceCharacters"/>).
    /// </summary>
    public static IEnumerable<string> Build(Packfile guerrillaTables, Packfile gameTables, string worldFolder, IReadOnlySet<string> characters,
                                            IReadOnlyDictionary<string, string> containerOf, Func<string, string?> teamOf, string? player = null)
    {
        XtblFile Read(Packfile pack, string name) => XtblFile.FromBytes(pack.Read(pack.Find(name) ?? throw new InvalidDataException($"no {name} in {pack.Name}")));
        var squads = Read(gameTables, SquadTable);
        var loadouts = Read(gameTables, LoadoutTable);
        var gameSquads = new HashSet<string>(squads.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        var gameLoadouts = new HashSet<string>(loadouts.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);

        // The weapons ported so far: Guerrilla's name → the name its weapon entry has here.
        string weaponsFile = Path.Combine(worldFolder, "weapons.xtbl");
        var gameWeapons = new HashSet<string>(Read(gameTables, "weapons.xtbl").Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        var ported = File.Exists(weaponsFile)
            ? XtblFile.FromBytes(File.ReadAllBytes(weaponsFile)).Entries.Select(XtblFile.EntryName).Where(n => !gameWeapons.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        string? Weapon(string guerrilla) => ported.Contains(guerrilla) ? guerrilla : ported.Contains("rfg_" + guerrilla) ? "rfg_" + guerrilla : null;

        // Guerrilla's loadouts whose weapon is ported, under our names.
        var loadoutName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var leftOutLoadouts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var withGrenades = new List<string>();
        var leftOutGrenades = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rfg in Read(guerrillaTables, LoadoutTable).Entries)
        {
            string name = XtblFile.EntryName(rfg);
            string? weapon = rfg.ValueOf("weapon_type")?.Trim() is { Length: > 0 } w ? Weapon(w) : null;
            if (weapon == null) { leftOutLoadouts.Add(name); continue; }
            string ours = gameLoadouts.Contains(name) ? "rfg_" + name : name;
            var entry = new XtblElement("squad_loadout");
            entry.AddElement("Name", ours);
            entry.AddElement("weapon_type", weapon);
            // Its grenades, as Guerrilla's (Armageddon's loadout reader kept them: a type and a count), once their weapon is ported.
            if (rfg.Element("grenades") is { } grenades && grenades.ValueOf("grenade_type")?.Trim() is { Length: > 0 } type)
            {
                if (GrenadeWeapons.TryGetValue(type, out var grenade) && ported.Contains(grenade))
                {
                    var ourGrenades = entry.AddElement("grenades");
                    ourGrenades.AddElement("grenade_type", type);
                    ourGrenades.AddElement("grenade_count", grenades.ValueOf("grenade_count")?.Trim() is { Length: > 0 } count ? count : "1");
                    withGrenades.Add(ours);
                }
                else leftOutGrenades.Add($"{name} ({type}: {(GrenadeWeapons.TryGetValue(type, out var g) ? g : "no weapon")} not ported)");
            }
            entry.AddElement("_Editor").AddElement("Category", "Entries:Guerrilla");
            loadouts.Table!.AddElement(entry);
            loadoutName[name] = ours;
        }

        // Each progression set's level-1 entries we can have: (character, our loadout, weight).
        var progression = new Dictionary<string, List<(string Character, string Loadout, double Weight)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in Read(guerrillaTables, "squad_progression.xtbl").Entries)
        {
            var list = new List<(string, string, double)>();
            foreach (var property in set.Element($"squad_level_{Level}")?.Element("progression_properties")?.Elements ?? [])
            {
                string? human = property.ValueOf("human_type")?.Trim(), loadout = property.ValueOf("weapon_type")?.Trim();
                if (human == null || loadout == null || !characters.Contains(human) || !loadoutName.TryGetValue(loadout, out var ours)) continue;
                double weight = double.TryParse(property.ValueOf("spawn_weight"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) ? w : 1;
                if (weight > 0) list.Add((human, ours, weight));
            }
            progression[XtblFile.EntryName(set)] = list;
        }

        // The world's spawn resources (GuerrillaDistricts): rfg_Default for the zones with no human group, one for
        // each district group the zones name; each lists one character a container (the others come with it).
        var groups = GuerrillaDistricts.Groups(guerrillaTables);
        var districtGroups = GuerrillaDistricts.DistrictGroups(GuerrillaDistricts.AmbientGroups(guerrillaTables).Values);
        var resourceLists = GuerrillaDistricts.Resources(groups, districtGroups, characters, containerOf, teamOf);
        bool Civilian(string character) => teamOf(character) is { } t && t.Equals(GuerrillaTeam, StringComparison.OrdinalIgnoreCase);
        // A group's civilians, for its squads: those its resource lists (rfg_Default's: the shared ones).
        var civiliansOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = [.. resourceLists[DefaultResourceName].Where(Civilian)],
        };
        foreach (string group in districtGroups)
            if (resourceLists.TryGetValue(GuerrillaDistricts.ResourceOf(group), out var listedHere)) civiliansOf[group] = [.. listedHere.Where(Civilian)];

        // The guerrilla squads' loadouts: Guerrilla's progression's (Jayde's, whose looks the group gives), those ported.
        var guerrillaLoadouts = new List<(string Character, string Loadout, double Weight)>();
        foreach (var property in Read(guerrillaTables, "squad_progression.xtbl").Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(ProgressionOf[GuerrillaTeam], StringComparison.OrdinalIgnoreCase))
                                 ?.Element($"squad_level_{Level}")?.Element("progression_properties")?.Elements ?? [])
            if (property.ValueOf("weapon_type")?.Trim() is { } l && loadoutName.TryGetValue(l, out var ours))
                guerrillaLoadouts.Add(("", ours, double.TryParse(property.ValueOf("spawn_weight"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) ? w : 1));

        var written = new List<string>();
        var leftOut = new List<string>();
        int groupSquads = 0;
        foreach (var rfg in Read(guerrillaTables, SquadTable).Entries)
        {
            string name = XtblFile.EntryName(rfg), team = rfg.ValueOf("team_name")?.Trim() ?? "";
            int count = int.TryParse(rfg.ValueOf("member_count"), out int c) ? c : 0;
            if (gameSquads.Contains(name)) { leftOut.Add($"{name} (Armageddon has one of that name)"); continue; }
            if (count < 1 || rfg.ValueOf("special_npc") is { Length: > 0 }) { leftOut.Add($"{name} (story people)"); continue; }
            if (GuerrillaDistricts.FromGroup(name))
            {
                // A version for each group: its members that group's civilians, a civilian squad unarmed (Armageddon's
                // squads require a loadout: its own civilians', CivilianLoadout), a guerrilla squad with Guerrilla's
                // guerrilla loadouts.
                bool civilians = team.Equals("Civilian", StringComparison.OrdinalIgnoreCase);
                if (!civilians && guerrillaLoadouts.Count == 0) { leftOut.Add($"{name} (none of the guerrillas' weapons ported yet)"); continue; }
                foreach (var (group, people) in civiliansOf)
                {
                    if (people.Count == 0) continue;
                    List<(string, string, double)> pool = civilians ? [.. people.Select(p => (p, CivilianLoadout, 1.0))]
                                                                    : [.. people.SelectMany(p => guerrillaLoadouts.Select(l => (p, l.Loadout, l.Weight)))];
                    squads.Table!.AddElement(Squad(GuerrillaDistricts.SquadFor(name, group == "default" ? null : group), team, count, pool, people.Count));
                    groupSquads++;
                }
                written.Add($"{name} ({count}, a version a group)");
                continue;
            }
            if (!ProgressionOf.TryGetValue(team, out var set) || !progression.TryGetValue(set, out var progressionPool) || progressionPool.Count == 0) { leftOut.Add($"{name} ({team}: none of its people made yet)"); continue; }
            squads.Table!.AddElement(Squad(name, team, count, progressionPool, 0));
            written.Add($"{name} ({count})");
        }

        var (resources, playerOverride) = WithSpawnResources(gameTables, resourceLists, containerOf, player);
        Directory.CreateDirectory(worldFolder);
        File.WriteAllBytes(Path.Combine(worldFolder, ResourceTable), resources.ToBytes());
        File.WriteAllBytes(Path.Combine(worldFolder, SquadTable), squads.ToBytes());
        File.WriteAllBytes(Path.Combine(worldFolder, LoadoutTable), loadouts.ToBytes());
        var everywhere = resourceLists.Values.SelectMany(l => l).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var withThem = characters.Where(c => !everywhere.Contains(c) && containerOf.TryGetValue(c, out var owner) && everywhere.Contains(owner)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var nowhere = characters.Where(c => !everywhere.Contains(c) && !withThem.Contains(c, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var (resource, list) in resourceLists.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            yield return $"Spawn resource: {resource}: {list.Count} of the game's 16 human slots: {string.Join(", ", list)}";
        yield return (withThem.Count > 0 ? $"Spawn resources: with their containers {string.Join(", ", withThem)}" : "Spawn resources:") +
                     (nowhere.Count > 0 ? $"; in no resource (made, but streamed in nowhere yet): {string.Join(", ", nowhere)}" : "") +
                     (playerOverride != null ? $"; the player {playerOverride}" + (player != null ? " (Guerrilla's player, made by the people)" : ", as Armageddon's Default") : "");
        yield return $"Loadouts: {loadoutName.Count} of Guerrilla's ({string.Join(", ", loadoutName.Values)}); not yet, their weapons not ported: {leftOutLoadouts.Count}; with their grenades: {withGrenades.Count}" +
                     (leftOutGrenades.Count > 0 ? $"; grenades not yet: {string.Join(", ", leftOutGrenades)}" : "");
        yield return written.Count == 0 ? "Squads: none of Guerrilla's can be had yet" : $"Squads: {written.Count} of Guerrilla's ({groupSquads} versions of its civilian and guerrilla squads, a group each), members picked from its progression at level {Level}: {string.Join(", ", written)}";
        if (leftOut.Count > 0) yield return $"Squads not yet: {string.Join(", ", leftOut)}";
    }

    /// <summary>
    /// The weapon each of Guerrilla's grenade types is (its loadout reader's, which Armageddon's kept, 0x50D070: the
    /// type's name to the weapon's, looked up when the table is read). A loadout carries its grenades once that weapon
    /// is ported under Guerrilla's own name (the gear build's).
    /// </summary>
    static readonly Dictionary<string, string> GrenadeWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["normal"] = "grenade", ["singularity"] = "singularity_bomb", ["marauder"] = "marauder_shockade",
    };

    /// <summary>The name rfg_Default goes under in <see cref="GuerrillaDistricts.Resources"/>.</summary>
    const string DefaultResourceName = SpawnResource;

    /// <summary>Guerrilla's guerrillas' team (its civilians and guerrillas share their characters).</summary>
    const string GuerrillaTeam = "Guerilla";

    /// <summary>
    /// The loadout of Guerrilla's civilians, who carry nothing: Armageddon's squad reader drops a member without a
    /// loadout it knows (0x50BD47, 0x50BD6B), so they take the one Armageddon's own civilians have.
    /// </summary>
    const string CivilianLoadout = "Civilian Melee";

    /// <summary>
    /// A squad entry of Armageddon's: <paramref name="count"/> members picked from <paramref name="pool"/> (character,
    /// loadout, weight) as the weights spread them, member k taking the entry its share falls on; with
    /// <paramref name="people"/> > 0 (a group's squad, its pool every person × each loadout) each member is a
    /// different person in turn where it can.
    /// </summary>
    static XtblElement Squad(string name, string team, int count, IReadOnlyList<(string Character, string Loadout, double Weight)> pool, int people)
    {
        double total = pool.Sum(p => p.Weight);
        (string Character, string Loadout) Member(int k)
        {
            double at = (k + 0.5) * total / count, sum = 0;
            (string Character, string Loadout) picked = (pool[^1].Character, pool[^1].Loadout);
            foreach (var p in pool) if ((sum += p.Weight) >= at) { picked = (p.Character, p.Loadout); break; }
            // A group's squad: a different person for each member, in turn from the one its name's size starts at.
            if (people > 0)
            {
                var persons = pool.Select(p => p.Character).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                picked.Character = persons[(k + count) % persons.Count];
            }
            return picked;
        }
        var entry = new XtblElement("Squads");
        entry.AddElement("Name", name);
        entry.AddElement("team_name", team);
        entry.AddElement("squad_value", "1");
        var leader = entry.AddElement("squad_leader");
        var (character, loadout) = Member(0);
        leader.AddElement("member_info", character);
        leader.AddElement("weapon_info", loadout);
        if (count > 1)
        {
            var members = entry.AddElement("squad_member_list");
            for (int k = 1; k < count; k++)
            {
                var member = members.AddElement("squad_member");
                (character, loadout) = Member(k);
                member.AddElement("member_info", character);
                member.AddElement("weapon_info", loadout);
                // Its variant (variant_names.xtbl: Armageddon's creepers' Regular, quills, ...), which the description
                // requires of a member: empty. The squad reader (0x50BCE0) hashes the name (0xC8BD00, seed 0), and an
                // empty name hashes to 0, the variant it gives a member that names none ([0x10724B8], 0): Guerrilla's
                // people have no variants.
                member.AddElement("member_variant", "");
            }
        }
        entry.AddElement("_Editor").AddElement("Category", "Guerrilla:" + team);
        return entry;
    }

    /// <summary>
    /// The characters a spawn resource lists for <paramref name="characters"/>: one for each container (the
    /// character it is named after where that one is among them, else the first by name). Armageddon's
    /// table loader drops a whole entry that lists two characters with the same container (0x75EFA0 compares
    /// every pair's container, character +0x60, and the loader leaves out an entry it fails, 0x75EC0D), and
    /// any zone naming it then gets the table's fallback, Armageddon's "Default"; a character sharing a
    /// container loads with it (as Armageddon's own: ch_red_ass_m7jerk, sharing ch_red_ass_soldier's, is in
    /// none of its resources).
    /// </summary>
    public static List<string> ResourceCharacters(IEnumerable<string> characters, IReadOnlyDictionary<string, string> containerOf)
    {
        var listed = new List<string>();
        foreach (var group in characters.GroupBy(c => containerOf.GetValueOrDefault(c, c), StringComparer.OrdinalIgnoreCase))
            listed.Add(group.FirstOrDefault(c => c.Equals(group.Key, StringComparison.OrdinalIgnoreCase)) ?? group.Order(StringComparer.OrdinalIgnoreCase).First());
        return [.. listed.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Armageddon's spawn resources with the Guerrilla world's added (<paramref name="lists"/>: each resource's
    /// characters), each with the player Armageddon's Default gives, or <paramref name="playerCharacter"/> (its
    /// name, or null). Throws if a resource lists two characters sharing a container (the game would drop the
    /// whole entry, see <see cref="ResourceCharacters"/>) or more than the game streams at once.
    /// </summary>
    static (XtblFile Table, string? Player) WithSpawnResources(Packfile gameTables, IReadOnlyDictionary<string, List<string>> lists,
                                                           IReadOnlyDictionary<string, string> containerOf, string? playerCharacter = null)
    {
        var resources = XtblFile.FromBytes(gameTables.Read(gameTables.Find(ResourceTable) ?? throw new InvalidDataException($"no {ResourceTable} in {gameTables.Name}")));
        string? player = playerCharacter ?? resources.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals("Default", StringComparison.OrdinalIgnoreCase))?.ValueOf("Player_Override")?.Trim();
        foreach (var (name, characters) in lists.OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase))
        {
            var resource = new XtblElement("spawn_resource_entry");
            resource.AddElement("Name", name);
            var list = characters.Order(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var shared in list.GroupBy(c => containerOf.GetValueOrDefault(c, c), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                throw new InvalidDataException($"the spawn resource {name} would list {string.Join(", ", shared)}, which share the container {shared.Key}: " +
                                               "Armageddon drops a resource listing two characters of one container, and its zones then load its own \"Default\"");
            if (list.Count > HumanSlots)
                throw new InvalidDataException($"the spawn resource {name} would list {list.Count} characters, more than the {HumanSlots} the game streams in at once");
            if (list.Count > 0)
            {
                var npcs = resource.AddElement("npc_list");
                foreach (string character in list) npcs.AddElement("npc_info").AddElement("npc_group", character);
            }
            if (!string.IsNullOrEmpty(player)) resource.AddElement("Player_Override", player);
            resource.AddElement("_Editor").AddElement("Category", "Entries:Guerrilla");
            resources.Table!.AddElement(resource);
        }
        return (resources, string.IsNullOrEmpty(player) ? null : player);
    }

    /// <summary>How many characters' containers the game streams in at once (its human allocator, 0x26B07B0: 16 slots).</summary>
    const int HumanSlots = 16;

    /// <summary>
    /// The Guerrilla world's spawn resources' names: rfg_Default and one for each district group its zones name
    /// (<see cref="GuerrillaDistricts"/>), from Guerrilla's tables.
    /// </summary>
    public static List<string> SpawnResources(Packfile guerrillaTables) =>
        [SpawnResource, .. GuerrillaDistricts.DistrictGroups(GuerrillaDistricts.AmbientGroups(guerrillaTables).Values).Select(GuerrillaDistricts.ResourceOf)];

    /// <summary>
    /// For the world builder, which builds before the people (so it needn't wait on them): the zones name the
    /// Guerrilla world's spawn resources (<see cref="SpawnResources"/>) whether or not GuerrillaPeople has run, so
    /// if <paramref name="worldFolder"/>'s table lacks any of them this writes Armageddon's table with them added
    /// holding no one but the player (as Armageddon's own ruins' resources): a zone naming a resource the table
    /// hasn't gets Armageddon's "Default" (its aliens and soldiers). GuerrillaPeople writes them again with its
    /// people. Returns whether it wrote the table.
    /// </summary>
    public static bool EnsureSpawnResources(Packfile guerrillaTables, Packfile gameTables, string worldFolder)
    {
        var names = SpawnResources(guerrillaTables);
        string file = Path.Combine(worldFolder, ResourceTable);
        if (File.Exists(file))
        {
            var have = XtblFile.FromBytes(File.ReadAllBytes(file)).Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.All(have.Contains)) return false;
        }
        Directory.CreateDirectory(worldFolder);
        File.WriteAllBytes(file, WithSpawnResources(gameTables, names.ToDictionary(n => n, _ => new List<string>()), new Dictionary<string, string>()).Table.ToBytes());
        return true;
    }

    /// <summary>
    /// The characters the world builder plans the squads for: GuerrillaPeople's default ones, and any
    /// other its last run made (mods\guerrilla_people\people.txt, beside <paramref name="worldFolder"/>).
    /// </summary>
    public static HashSet<string> PlannedCharacters(string worldFolder)
    {
        var characters = new HashSet<string>(GuerrillaPeople.DefaultCharacters, StringComparer.OrdinalIgnoreCase);
        string people = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldFolder)) ?? worldFolder, "guerrilla_people", GuerrillaPeople.PeopleList);
        if (File.Exists(people)) characters.UnionWith(File.ReadAllLines(people).Select(l => l.Trim()).Where(l => l.Length > 0));
        return characters;
    }

    /// <summary>
    /// The squads <see cref="Build"/> writes for <paramref name="characters"/>, worked out from Guerrilla's own
    /// tables (its squads, and its progression at the level they are picked at) so the world builder can
    /// place their patrols before GuerrillaPeople runs. Weapons aren't asked about: a squad whose members'
    /// weapons aren't ported yet isn't written, and the game drops its zones' squad nodes with a warning
    /// ("has an invalid squad definition ... will be destroyed", 0x7FECE3), as for any squad it lacks.
    /// </summary>
    public static HashSet<string> Expected(Packfile guerrillaTables, Packfile gameTables, IReadOnlySet<string> characters)
    {
        XtblFile Read(Packfile pack, string name) => XtblFile.FromBytes(pack.Read(pack.Find(name) ?? throw new InvalidDataException($"no {name} in {pack.Name}")));
        var gameSquads = new HashSet<string>(Read(gameTables, SquadTable).Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        var sets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in Read(guerrillaTables, "squad_progression.xtbl").Entries)
            if ((set.Element($"squad_level_{Level}")?.Element("progression_properties")?.Elements ?? [])
                .Any(p => p.ValueOf("human_type")?.Trim() is { } human && characters.Contains(human)))
                sets.Add(XtblFile.EntryName(set));
        // Each group's civilians among them (null: the zones with none, rfg_Default's shared civilians).
        var groups = GuerrillaDistricts.Groups(guerrillaTables);
        var groupPeople = new List<(string? Group, List<string> People)> { (null, [.. GuerrillaDistricts.SharedCivilians.Where(characters.Contains)]) };
        foreach (string group in GuerrillaDistricts.DistrictGroups(GuerrillaDistricts.AmbientGroups(guerrillaTables).Values))
            if (groups.TryGetValue(group, out var list)) groupPeople.Add((group, GuerrillaDistricts.Civilians(list, characters)));
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rfg in Read(guerrillaTables, SquadTable).Entries)
        {
            string name = XtblFile.EntryName(rfg), team = rfg.ValueOf("team_name")?.Trim() ?? "";
            if (gameSquads.Contains(name) || !int.TryParse(rfg.ValueOf("member_count"), out int count) || count < 1 || rfg.ValueOf("special_npc") is { Length: > 0 }) continue;
            if (GuerrillaDistricts.FromGroup(name))
            {
                // A version for each group whose resource holds any of its civilians (as Build writes them).
                foreach (var (group, people) in groupPeople)
                    if (people.Count > 0) expected.Add(GuerrillaDistricts.SquadFor(name, group));
                continue;
            }
            if (ProgressionOf.TryGetValue(team, out var set) && sets.Contains(set)) expected.Add(name);
        }
        return expected;
    }

    /// <summary>The names of Guerrilla's squads the tables in <paramref name="worldFolder"/> hold (those Armageddon's own hasn't), for the world builder.</summary>
    public static HashSet<string> Ported(string worldFolder, Packfile gameTables)
    {
        string file = Path.Combine(worldFolder, SquadTable);
        if (!File.Exists(file)) return new(StringComparer.OrdinalIgnoreCase);
        var game = new HashSet<string>(XtblFile.FromBytes(gameTables.Read(gameTables.Find(SquadTable)!)).Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        return XtblFile.FromBytes(File.ReadAllBytes(file)).Entries.Select(XtblFile.EntryName).Where(n => !game.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Takes the three tables away.</summary>
    public static IEnumerable<string> Remove(string worldFolder)
    {
        foreach (string name in new[] { SquadTable, LoadoutTable, ResourceTable })
        {
            string path = Path.Combine(worldFolder, name);
            if (File.Exists(path)) { File.Delete(path); yield return $"Deleted {path}"; }
        }
    }
}
