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
/// of that name), their weapon by its ported name; their grenades are left out until Guerrilla's are
/// ported. Both tables are Armageddon's with Guerrilla's added (in mods\guerrilla_world\, which
/// guerrilla_world hands the game at start-up).
/// </summary>
public static class GuerrillaSquads
{
    public const string SquadTable = "squads.xtbl", LoadoutTable = "squad_loadout.xtbl", ResourceTable = "spawn_resource.xtbl";

    /// <summary>
    /// The Guerrilla world's spawn resource (spawn_resource.xtbl), which its zones name (spawn_resource_data):
    /// where one is in force the game streams in and makes only the people it lists. Guerrilla's "Default"
    /// (101 of its 102 zones) reserves character types by team (two EDF, three guerrillas) from the zone's
    /// people; Armageddon's lists characters. Ours lists Guerrilla's characters made so far, and the player
    /// Armageddon's own "Default" gives (Player_Override: Darius), or Guerrilla's own player once the people
    /// tool has made him (Mason: the game takes the start zone's resource's Player_Override for the player,
    /// 0x8B77B0, unless a DLC uniform is chosen, which the guerrilla_people mod sets aside in our worlds). Without it
    /// the zones took Armageddon's "Default": its aliens, Marauders and soldiers streamed into the world.
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
                                            IReadOnlyDictionary<string, string> containerOf, string? player = null)
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
        foreach (var rfg in Read(guerrillaTables, LoadoutTable).Entries)
        {
            string name = XtblFile.EntryName(rfg);
            string? weapon = rfg.ValueOf("weapon_type")?.Trim() is { Length: > 0 } w ? Weapon(w) : null;
            if (weapon == null) { leftOutLoadouts.Add(name); continue; }
            string ours = gameLoadouts.Contains(name) ? "rfg_" + name : name;
            var entry = new XtblElement("squad_loadout");
            entry.AddElement("Name", ours);
            entry.AddElement("weapon_type", weapon);
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

        var written = new List<string>();
        var leftOut = new List<string>();
        foreach (var rfg in Read(guerrillaTables, SquadTable).Entries)
        {
            string name = XtblFile.EntryName(rfg), team = rfg.ValueOf("team_name")?.Trim() ?? "";
            int count = int.TryParse(rfg.ValueOf("member_count"), out int c) ? c : 0;
            if (gameSquads.Contains(name)) { leftOut.Add($"{name} (Armageddon has one of that name)"); continue; }
            if (count < 1 || rfg.ValueOf("special_npc") is { Length: > 0 }) { leftOut.Add($"{name} (story people)"); continue; }
            if (!ProgressionOf.TryGetValue(team, out var set) || !progression.TryGetValue(set, out var pool) || pool.Count == 0) { leftOut.Add($"{name} ({team}: none of its people made yet)"); continue; }
            double total = pool.Sum(p => p.Weight);
            (string Character, string Loadout) Member(int k)
            {
                double at = (k + 0.5) * total / count, sum = 0;
                foreach (var p in pool) if ((sum += p.Weight) >= at) return (p.Character, p.Loadout);
                return (pool[^1].Character, pool[^1].Loadout);
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
                }
            }
            entry.AddElement("_Editor").AddElement("Category", "Guerrilla:" + team);
            squads.Table!.AddElement(entry);
            written.Add($"{name} ({count})");
        }

        // The world's spawn resource: Guerrilla's characters made so far, one for each container (the
        // others come with it), and the player Armageddon's Default gives.
        var listed = ResourceCharacters(characters, containerOf);
        var (resources, playerOverride) = WithSpawnResource(gameTables, listed, containerOf, player);

        Directory.CreateDirectory(worldFolder);
        File.WriteAllBytes(Path.Combine(worldFolder, ResourceTable), resources.ToBytes());
        File.WriteAllBytes(Path.Combine(worldFolder, SquadTable), squads.ToBytes());
        File.WriteAllBytes(Path.Combine(worldFolder, LoadoutTable), loadouts.ToBytes());
        var withThem = characters.Where(c => !listed.Contains(c, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        yield return $"Spawn resource: {SpawnResource} with {string.Join(", ", listed)}" +
                     (withThem.Count > 0 ? $" (and with their containers {string.Join(", ", withThem)})" : "") +
                     (playerOverride != null ? $"; the player {playerOverride}" + (player != null ? " (Guerrilla's player, made by the people)" : ", as Armageddon's Default") : "");
        yield return $"Loadouts: {loadoutName.Count} of Guerrilla's ({string.Join(", ", loadoutName.Values)}); not yet, their weapons not ported: {leftOutLoadouts.Count}; grenades left out until Guerrilla's are ported";
        yield return written.Count == 0 ? "Squads: none of Guerrilla's can be had yet" : $"Squads: {written.Count} of Guerrilla's, members picked from its progression at level {Level}: {string.Join(", ", written)}";
        if (leftOut.Count > 0) yield return $"Squads not yet: {string.Join(", ", leftOut)}";
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
    /// Armageddon's spawn resources with the Guerrilla world's added: <paramref name="characters"/>, and the player
    /// Armageddon's Default gives (its name, or null). Throws if two of the characters share a container: the game
    /// would drop the whole entry (see <see cref="ResourceCharacters"/>).
    /// </summary>
    static (XtblFile Table, string? Player) WithSpawnResource(Packfile gameTables, IEnumerable<string> characters, IReadOnlyDictionary<string, string> containerOf,
                                                          string? playerCharacter = null)
    {
        var resources = XtblFile.FromBytes(gameTables.Read(gameTables.Find(ResourceTable) ?? throw new InvalidDataException($"no {ResourceTable} in {gameTables.Name}")));
        var resource = new XtblElement("spawn_resource_entry");
        resource.AddElement("Name", SpawnResource);
        var list = characters.Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var shared in list.GroupBy(c => containerOf.GetValueOrDefault(c, c), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            throw new InvalidDataException($"the spawn resource {SpawnResource} would list {string.Join(", ", shared)}, which share the container {shared.Key}: " +
                                           "Armageddon drops a resource listing two characters of one container, and its zones then load its own \"Default\"");
        if (list.Count > 0)
        {
            var npcs = resource.AddElement("npc_list");
            foreach (string character in list) npcs.AddElement("npc_info").AddElement("npc_group", character);
        }
        string? player = playerCharacter ?? resources.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals("Default", StringComparison.OrdinalIgnoreCase))?.ValueOf("Player_Override")?.Trim();
        if (!string.IsNullOrEmpty(player)) resource.AddElement("Player_Override", player);
        resource.AddElement("_Editor").AddElement("Category", "Entries:Guerrilla");
        resources.Table!.AddElement(resource);
        return (resources, string.IsNullOrEmpty(player) ? null : player);
    }

    /// <summary>
    /// For the world builder, which builds before the people (so it needn't wait on them): the zones name the
    /// Guerrilla world's spawn resource (<see cref="SpawnResource"/>) whether or not GuerrillaPeople has
    /// run, so if <paramref name="worldFolder"/>'s table hasn't it yet this writes Armageddon's table with it
    /// added holding no one but the player (as Armageddon's own ruins' resources); GuerrillaPeople writes
    /// it again with its people. Returns whether it wrote the table.
    /// </summary>
    public static bool EnsureSpawnResource(Packfile gameTables, string worldFolder)
    {
        if (SpawnResourceIn(worldFolder) != null) return false;
        Directory.CreateDirectory(worldFolder);
        File.WriteAllBytes(Path.Combine(worldFolder, ResourceTable), WithSpawnResource(gameTables, [], new Dictionary<string, string>()).Table.ToBytes());
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
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rfg in Read(guerrillaTables, SquadTable).Entries)
        {
            string name = XtblFile.EntryName(rfg), team = rfg.ValueOf("team_name")?.Trim() ?? "";
            if (gameSquads.Contains(name) || !int.TryParse(rfg.ValueOf("member_count"), out int count) || count < 1 || rfg.ValueOf("special_npc") is { Length: > 0 }) continue;
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

    /// <summary>The world's spawn resource's name if the tables in <paramref name="worldFolder"/> have it, else null (the zones keep the schema's default).</summary>
    public static string? SpawnResourceIn(string worldFolder)
    {
        string file = Path.Combine(worldFolder, ResourceTable);
        return File.Exists(file) && XtblFile.FromBytes(File.ReadAllBytes(file)).Entries.Any(e => XtblFile.EntryName(e).Equals(SpawnResource, StringComparison.OrdinalIgnoreCase))
            ? SpawnResource : null;
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
