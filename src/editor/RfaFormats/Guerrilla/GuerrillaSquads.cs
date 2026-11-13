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
        int groupSquads = 0, notPlaced = 0, forFights = 0;
        // The district versions the world's zones name (the world build's list); the others aren't written, as the
        // game keeps few squads (SquadEntries). Before the world is built, none.
        string placedFile = Path.Combine(worldFolder, PlacedList);
        var placed = File.Exists(placedFile)
            ? File.ReadAllLines(placedFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // But every guerrilla squad (guerilla_squad_1 to _6) in every group a zone uses (one with any version placed), for
        // the Red Faction fighters the campaign mod makes near the player by morale (Guerrilla's guerrilla_squad_info,
        // RM 0x7B6EA0: one guerilla_squad_N where the player is; guerrilla_people's GuerrillaPeople_GuerrillaSquadAt names it).
        var usedGroups = placed.Select(GuerrillaDistricts.GroupCodeOf).Where(code => code != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rfg in Read(guerrillaTables, SquadTable).Entries)
        {
            string name = XtblFile.EntryName(rfg), team = rfg.ValueOf("team_name")?.Trim() ?? "";
            int count = int.TryParse(rfg.ValueOf("member_count"), out int c) ? c : 0;
            if (gameSquads.Contains(name)) { leftOut.Add($"{name} (Armageddon has one of that name)"); continue; }
            if (count < 1) { leftOut.Add($"{name} (no members)"); continue; }
            if (rfg.ValueOf("special_npc")?.Trim() is { Length: > 0 } special)
            {
                if (name.Equals(GuerrillaDistricts.RadioSquad, StringComparison.OrdinalIgnoreCase))
                {
                    // Guerrilla's radio operators: a version a group, its member one of the group's own radio operators
                    // (its "Radio Operator" human group) that the group's resource streams in; none where it has none.
                    var radios = groups.TryGetValue(GuerrillaDistricts.RadioGroup, out var radioList)
                        ? radioList.ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int versions = 0;
                    foreach (var (group, people) in civiliansOf)
                    {
                        var here = people.Where(radios.Contains).ToList();
                        if (here.Count == 0) continue;
                        string version = GuerrillaDistricts.SquadFor(name, group == "default" ? null : group);
                        if (!placed.Contains(version)) { notPlaced++; continue; }
                        squads.Table!.AddElement(Squad(version, team, 1, [.. here.Select(p => (p, CivilianLoadout, 1.0))], 0));
                        versions++;
                    }
                    written.Add($"{name} (the radio operators: {versions} versions, a group's own)");
                    continue;
                }
                // The story people at the safehouses (guerrilla_people streams in one special NPC and one VIP, as
                // Guerrilla's resource manager does); the missions' own people are left for the missions.
                if (!StorySquads.Contains(name)) { leftOut.Add($"{name} (a mission's people: {special})"); continue; }
                if (!characters.Contains(special)) { leftOut.Add($"{name} (story people: {special} not made)"); continue; }
                squads.Table!.AddElement(Squad(name, team, 1, [(special, CivilianLoadout, 1.0)], 0));
                written.Add($"{name} (story: {special})");
                continue;
            }
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
                    string version = GuerrillaDistricts.SquadFor(name, group == "default" ? null : group);
                    bool forAFight = !civilians && usedGroups.Contains(GuerrillaDistricts.Code(group == "default" ? null : group));
                    if (!placed.Contains(version) && !forAFight) { notPlaced++; continue; }
                    if (!placed.Contains(version)) forFights++;
                    squads.Table!.AddElement(Squad(version, team, count, pool, people.Count));
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
        // As many squads as the game keeps (SquadEntries), and their names in the string pools the game keeps them in
        // (guerrilla_people gives both pools room).
        int squadEntries = squads.Entries.Count();
        if (squadEntries > SquadEntries)
            throw new InvalidDataException($"squads.xtbl would hold {squadEntries} squads, more than the {SquadEntries} the game keeps of it (its squad definitions' pool holds 128, and the DLC's squads take 7)");
        // As many different members (character, loadout, variant) as the game's pool of them holds (guerrilla_people's).
        int squadMembers = SquadMembers(squads);
        if (squadMembers > SquadMemberPool)
            throw new InvalidDataException($"the squads would have {squadMembers} different members, more than the {SquadMemberPool} the game holds with guerrilla_people (its own 64): every squad after the last that fits wouldn't be read");
        // As many loadouts as the game holds (guerrilla_people's), and every squad's members naming ones it has.
        var allWeapons = new HashSet<string>(gameWeapons.Concat(ported), StringComparer.OrdinalIgnoreCase);
        if (SquadProblems(squads, loadouts, allWeapons, null, gameSquads) is { Count: > 0 } squadProblems)
            throw new InvalidDataException("the squads the game would turn down: " + string.Join("; ", squadProblems));
        // Every squad the world's zones name is written (a zone naming another destroys its squad spawn node).
        var writtenNames = squads.Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (placed.Where(p => !writtenNames.Contains(p)).ToList() is { Count: > 0 } unwritten)
            throw new InvalidDataException($"the world's zones name squads the people build doesn't write: {string.Join(", ", unwritten)} (build the world and the people again)");
        int squadNames = PoolBytes(squads), resourceNames = PoolBytes(resources);
        if (squadNames > SquadNamePool)
            throw new InvalidDataException($"the squads' names need {squadNames} bytes, more than the {SquadNamePool} the game holds with guerrilla_people");
        if (resourceNames > ResourceNamePool)
            throw new InvalidDataException($"the spawn resources' names need {resourceNames} bytes, more than the {ResourceNamePool} the game holds with guerrilla_people");
        Directory.CreateDirectory(worldFolder);
        File.WriteAllBytes(Path.Combine(worldFolder, ResourceTable), resources.ToBytes());
        File.WriteAllBytes(Path.Combine(worldFolder, SquadTable), squads.ToBytes());
        File.WriteAllBytes(Path.Combine(worldFolder, LoadoutTable), loadouts.ToBytes());
        var everywhere = resourceLists.Values.SelectMany(l => l).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var withThem = characters.Where(c => !everywhere.Contains(c) && containerOf.TryGetValue(c, out var owner) && everywhere.Contains(owner)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var nowhere = characters.Where(c => !everywhere.Contains(c) && !withThem.Contains(c, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var (resource, list) in resourceLists.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            yield return $"Spawn resource: {resource}: {list.Count} of the {HumanSlots} a resource may list (the game streams 17 with the player; guerrilla_people's special NPC and VIP take the rest): {string.Join(", ", list)}";
        yield return (withThem.Count > 0 ? $"Spawn resources: with their containers {string.Join(", ", withThem)}" : "Spawn resources:") +
                     (nowhere.Count > 0 ? $"; in no resource (made, but streamed in nowhere yet): {string.Join(", ", nowhere)}" : "") +
                     (playerOverride != null ? $"; the player {playerOverride}" + (player != null ? " (Guerrilla's player, made by the people)" : ", as Armageddon's Default") : "");
        yield return $"Loadouts: {loadoutName.Count} of Guerrilla's ({string.Join(", ", loadoutName.Values)}); not yet, their weapons not ported: {leftOutLoadouts.Count}; with their grenades: {withGrenades.Count}" +
                     (leftOutGrenades.Count > 0 ? $"; grenades not yet: {string.Join(", ", leftOutGrenades)}" : "");
        yield return $"Squads: {squadEntries} in squads.xtbl of the {SquadEntries} the game keeps, {squadMembers} different members of the {SquadMemberPool} it holds with guerrilla_people (its own 64); district versions the world's zones don't name, left out: {notPlaced}; guerrilla squads written for the fights morale brings (no zone names them): {forFights}" +
                     (placed.Count == 0 ? " (the world isn't built yet: none of the district versions written)" : "");
        yield return $"Names: the squads' {squadNames} bytes of the {SquadNamePool} the game holds with guerrilla_people (its own 1,280), the spawn resources' {resourceNames} of {ResourceNamePool} (its own 1,024)";
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
    /// Guerrilla's story people at its safehouses (squads.xtbl, a special_npc each): its special NPCs (Samanya,
    /// OldCoot, RFC: the special_npc nodes) and safehouse VIPs (Carmen, Jorge, Kyle: the safehouse_vip nodes). Its
    /// other special_npc squads are its missions' people.
    /// </summary>
    public static readonly HashSet<string> StorySquads = new(["Samanya", "OldCoot", "RFC", "Carmen", "Jorge", "Kyle"], StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// How many different squad members (a character, a loadout and a variant) the game holds with guerrilla_people: the
    /// squad reader makes one record for each (pool 0x12EFEF0, 64 of the game's own), and past them every later squad fails.
    /// </summary>
    public const int SquadMemberPool = 256;

    /// <summary>A squad table's different members (leaders' and members': a character, a loadout and a variant each).</summary>
    public static int SquadMembers(XtblFile table)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var squad in table.Entries)
        {
            var members = new List<XtblElement>();
            if (squad.Element("squad_leader") is { } leader) members.Add(leader);
            members.AddRange(squad.Element("squad_member_list")?.Elements ?? []);
            foreach (var m in members)
                seen.Add($"{m.ValueOf("member_info")?.Trim()}|{m.ValueOf("weapon_info")?.Trim()}|{m.ValueOf("member_variant")?.Trim()}");
        }
        return seen.Count;
    }

    /// <summary>
    /// How many squad_loadout.xtbl entries the game holds with guerrilla_people: its own table holds 36 (0x12F33A0, made
    /// by 0x50D5B0) and drops every entry past them without a word, and a squad member naming a dropped loadout fails its
    /// whole squad, so a zone naming the squad gets "invalid squad definition" (marauder_squad_6 in the user's Round 1).
    /// </summary>
    public const int LoadoutEntries = 128;

    /// <summary>
    /// What the game's readers would turn down in a squad table and its loadout table, a line each (none: nothing): the
    /// loadouts past the <see cref="LoadoutEntries"/> the game holds; a loadout whose weapon isn't one of
    /// <paramref name="weapons"/> (the loadout reader drops it, 0x50CFFA); a squad whose leader or member names a loadout
    /// the game won't have, or a character not in <paramref name="characters"/> (the squad reader fails the squad,
    /// 0x50BD2A / 0x50BD6B). A null set isn't checked; squads named in <paramref name="armageddons"/> (Armageddon's own) are
    /// checked for loadouts only, as the game's own data is.
    /// </summary>
    public static List<string> SquadProblems(XtblFile squads, XtblFile loadouts, IReadOnlySet<string>? weapons, IReadOnlySet<string>? characters,
                                             IReadOnlySet<string>? armageddons = null)
    {
        var problems = new List<string>();
        var entries = loadouts.Entries.ToList();
        if (entries.Count > LoadoutEntries)
            problems.Add($"squad_loadout.xtbl holds {entries.Count} loadouts, more than the {LoadoutEntries} the game holds with guerrilla_people (its own 36): " +
                         $"{string.Join(", ", entries.Skip(LoadoutEntries).Select(XtblFile.EntryName))} would be dropped");
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Take(LoadoutEntries))
        {
            string name = XtblFile.EntryName(entry), weapon = entry.ValueOf("weapon_type")?.Trim() ?? "";
            if (weapons != null && !weapons.Contains(weapon)) problems.Add($"the loadout {name}'s weapon {(weapon.Length > 0 ? weapon : "(none)")} isn't in weapons.xtbl: the game drops the loadout");
            else kept.Add(name);
        }
        foreach (var squad in squads.Entries)
        {
            string name = XtblFile.EntryName(squad);
            var members = new List<XtblElement>();
            if (squad.Element("squad_leader") is { } leader) members.Add(leader);
            members.AddRange(squad.Element("squad_member_list")?.Elements ?? []);
            bool own = armageddons?.Contains(name) == true;
            var missingLoadouts = members.Select(m => m.ValueOf("weapon_info")?.Trim() ?? "").Where(l => !kept.Contains(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> missingCharacters = characters == null || own ? [] :
                members.Select(m => m.ValueOf("member_info")?.Trim() ?? "").Where(c => !characters.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missingLoadouts.Count > 0) problems.Add($"{name} names loadouts the game won't have: {string.Join(", ", missingLoadouts.Select(l => l.Length > 0 ? l : "(none)"))}");
            if (missingCharacters.Count > 0) problems.Add($"{name} names characters not in character.xtbl: {string.Join(", ", missingCharacters.Select(c => c.Length > 0 ? c : "(none)"))}");
        }
        return problems;
    }

    /// <summary>The world build's list of the squads its zones name (in mods\guerrilla_world\): the district versions the people build writes.</summary>
    public const string PlacedList = "placed_squads.txt";

    /// <summary>
    /// How many squads.xtbl entries the game keeps: it makes each squad definition from a pool of 128 (constructor
    /// 0x50CC50, pool 0x12F0370; its name table at 0x12F2E98 holds 128 too) and skips an entry the pool can't give one
    /// to without a word, so a zone naming it gets "invalid squad definition"; the DLC's dlc04_squads.xtbl takes 7.
    /// </summary>
    public const int SquadEntries = 128 - 7;

    /// <summary>
    /// How many bytes the game's string pools for the squads' and spawn resources' names hold with guerrilla_people
    /// ("Squad Def String Pool", 1,280 bytes of the game's own; "Spawn Resource Data", 1,024): past it the game stops.
    /// </summary>
    public const int SquadNamePool = 0x4000, ResourceNamePool = 0x2000;

    /// <summary>A table's entries' names in one of the game's string pools: each once, with its NUL, rounded up as the pool does.</summary>
    public static int PoolBytes(XtblFile table) =>
        table.Entries.Select(XtblFile.EntryName).Distinct(StringComparer.Ordinal).Sum(n => { int k = n.Length + 1; return k % 2 == 1 ? k + 1 : k + 2; });

    /// <summary>How many characters a spawn resource may list: the game streams 17 containers at once (its people's stream pool, 0x19728E8, set up at 0xF29530), less the player.</summary>
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
            if (gameSquads.Contains(name) || !int.TryParse(rfg.ValueOf("member_count"), out int count) || count < 1) continue;
            if (rfg.ValueOf("special_npc")?.Trim() is { Length: > 0 } special)
            {
                // As Build writes them: the radio operators' version a group with any of its radio operators, and the
                // safehouses' story people whose characters are made.
                if (name.Equals(GuerrillaDistricts.RadioSquad, StringComparison.OrdinalIgnoreCase))
                {
                    var radios = groups.TryGetValue(GuerrillaDistricts.RadioGroup, out var radioList)
                        ? radioList.ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (group, people) in groupPeople)
                        if (people.Any(radios.Contains)) expected.Add(GuerrillaDistricts.SquadFor(name, group));
                }
                else if (StorySquads.Contains(name) && characters.Contains(special)) expected.Add(name);
                continue;
            }
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
