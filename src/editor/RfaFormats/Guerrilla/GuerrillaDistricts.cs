using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Who lives where in the Guerrilla world: Guerrilla's people by district, as the spawn resources and squads of
/// Armageddon hold them.
///
/// Guerrilla takes each zone's guerrillas and civilians from its "human group" (the zone's ambient_spawn entry in
/// ambient_spawn_info.xtbl → its npc_spawn_group in spawn_group_human.xtbl: "Guerilla Dust", "Guerilla Parker",
/// "Guerilla Oasis", "Guerilla Eos" and their unions, the district versions of the same 72 civilians), and streams
/// a few of them in at a time (its spawn resource's populations: 2 EDF, 3 guerrilla types, rotating; its
/// resource manager holds at most 9 guerrilla, 3 EDF and 2 Marauder types). Its squads name only a team and a size:
/// a civilian squad's members are the zone's civilians, a guerrilla squad's the zone's people too (its progression
/// names Jayde, whose looks the group gives).
///
/// Armageddon dropped the populations: a spawn resource names its characters (npc_list), and the game streams
/// those in, 16 at most at once (its human allocator, 0x26B07B0, set up at 0xA477E2 with 16 slots). A squad names
/// each member. So here:
///   - a spawn resource a human group (<see cref="ResourceOf"/>): the EDF and Marauder types Guerrilla's squads
///     use at the campaign's start (<see cref="DistrictEdf"/>, <see cref="DistrictMarauders"/>) and as many of the
///     group's civilians as the rest of the slots hold (<see cref="CivilianSlots"/>), spread over its list; the
///     zones with no group (the wastes) take rfg_Default, with every EDF and Marauder type made and the civilians
///     every group shares (Bev, Torvald, Olga, Dustin);
///   - each zone names its group's resource (the world builder);
///   - each civilian and guerrilla squad has a version a group (<see cref="SquadFor"/>), its members that
///     group's civilians, and each zone's squad nodes name their group's version.
/// </summary>
public static class GuerrillaDistricts
{
    /// <summary>The zones with no human group (and any group not made): Guerrilla's EDF and Marauders made, and the civilians every group shares.</summary>
    public const string DefaultResource = GuerrillaSquads.SpawnResource;

    /// <summary>How many human slots a district's resource fills (the game's 16, one kept for the player's suit in case it takes one).</summary>
    public const int Slots = 15;

    /// <summary>The EDF types Guerrilla's squads take at the campaign's start (squad_progression, Primary EDF level 1).</summary>
    public static readonly string[] DistrictEdf = ["EDF_Trooper", "EDF_Grunt_Braun", "EDF_Officer_May"];

    /// <summary>The Marauder type Guerrilla's Marauder squads take (Marauder progression).</summary>
    public static readonly string[] DistrictMarauders = ["Eddie"];

    /// <summary>The civilians in every one of Guerrilla's district groups, for the zones with none.</summary>
    public static readonly string[] SharedCivilians = ["Bev", "Torvald", "Olga", "Dustin"];

    /// <summary>A district resource's slots for its group's civilians.</summary>
    public const int CivilianSlots = Slots - 3 - 1;

    /// <summary>Guerrilla's squads whose members come from the zone's group, by their name's start.</summary>
    static readonly string[] GroupSquads = ["civilian_squad_", "guerilla_squad_"];

    /// <summary>Whether a squad of Guerrilla's takes its members from the zone's people (civilian and guerrilla squads).</summary>
    public static bool FromGroup(string squad) => GroupSquads.Any(s => squad.StartsWith(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>A human group's short name, as our names carry it ("Guerilla Dust Oasis" → "dust_oasis"; none → "default").</summary>
    public static string Key(string? group) => group == null ? "default"
        : group.Replace("Guerilla", "", StringComparison.OrdinalIgnoreCase).Replace("Default", "", StringComparison.OrdinalIgnoreCase).Trim()
               .Replace(' ', '_').ToLowerInvariant() is { Length: > 0 } key ? key : "default";

    /// <summary>A human group's spawn resource (rfg_Default for none, or the "Default Guerilla" group).</summary>
    public static string ResourceOf(string? group) => Key(group) == "default" ? DefaultResource : "rfg_" + Key(group);

    /// <summary>The longest name Armageddon's squads.xtbl takes (its description's MaxLength for Name; a longer one stops the level's load).</summary>
    public const int MostSquadName = 20;

    /// <summary>
    /// A group's short code for squad names: the first two letters of each district in it ("Guerilla Dust Parker
    /// Eos" → dupaeo; the zones with none → df).
    /// </summary>
    public static string Code(string? group) => Key(group) == "default" ? "df"
        : string.Concat(Key(group).Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => word.Length > 2 ? word[..2] : word));

    /// <summary>Guerrilla's group squads' kinds, by their name's start, under the short names ours carry.</summary>
    static readonly (string Start, string Short)[] GroupSquadShort = [("civilian_squad_", "csq"), ("guerilla_squad_", "gsq")];

    /// <summary>
    /// A civilian or guerrilla squad's version for a group (civilian_squad_2 in Dust → rfg_du_csq_2; at most
    /// <see cref="MostSquadName"/> characters, as the game takes them), or the squad itself for any other.
    /// </summary>
    public static string SquadFor(string squad, string? group)
    {
        if (!FromGroup(squad)) return squad;
        var (start, kind) = GroupSquadShort.First(k => squad.StartsWith(k.Start, StringComparison.OrdinalIgnoreCase));
        string name = $"rfg_{Code(group)}_{kind}_{squad[start.Length..]}";
        if (name.Length > MostSquadName)
            throw new InvalidDataException($"the squad name {name} (Guerrilla's {squad} in {group ?? "no group"}) is longer than the {MostSquadName} characters the game takes");
        return name;
    }

    /// <summary>Guerrilla's squad a name of <see cref="SquadFor"/>'s is a version of (rfg_du_csq_2 → civilian_squad_2), or the name itself.</summary>
    public static string GuerrillaSquadOf(string ours)
    {
        var parts = ours.Split('_');
        if (parts.Length == 4 && parts[0] == "rfg" && GroupSquadShort.FirstOrDefault(k => k.Short == parts[2]) is { Start: { } start })
            return start + parts[3];
        return ours;
    }

    /// <summary>Guerrilla's human groups (spawn_group_human.xtbl): name → its characters, in its order.</summary>
    public static Dictionary<string, List<string>> Groups(Packfile guerrillaTables)
    {
        var table = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("spawn_group_human.xtbl") ?? throw new InvalidDataException("no spawn_group_human.xtbl")));
        return table.Entries.ToDictionary(XtblFile.EntryName,
            e => (e.Element("human_list")?.Elements ?? []).Select(h => h.Value.Trim()).Where(h => h.Length > 0).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Guerrilla's ambient spawn entries (ambient_spawn_info.xtbl): a zone's ambient_spawn → its human group.</summary>
    public static Dictionary<string, string> AmbientGroups(Packfile guerrillaTables)
    {
        var table = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("ambient_spawn_info.xtbl") ?? throw new InvalidDataException("no ambient_spawn_info.xtbl")));
        return table.Entries.Where(e => e.ValueOf("npc_spawn_group")?.Trim() is { Length: > 0 })
                    .ToDictionary(XtblFile.EntryName, e => e.ValueOf("npc_spawn_group")!.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The human group a Guerrilla zone's marker (obj_zone) names through its ambient_spawn, or null.</summary>
    public static string? ZoneGroup(GuerrillaZoneObject zoneMarker, IReadOnlyDictionary<string, string> ambientGroups) =>
        zoneMarker.Text("ambient_spawn") is { Length: > 0 } ambient && ambientGroups.TryGetValue(ambient, out var group) ? group : null;

    /// <summary>
    /// The groups whose zones' people get a resource of their own: those a zone of Guerrilla's world names (by
    /// <paramref name="zoneGroups"/>), but the "Default" ones (which go with rfg_Default).
    /// </summary>
    public static List<string> DistrictGroups(IEnumerable<string?> zoneGroups) =>
        [.. zoneGroups.OfType<string>().Where(g => Key(g) != "default").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// A group's civilians a resource holds: of those made (<paramref name="made"/>), at most <paramref name="room"/>,
    /// spread over its list (Guerrilla's order: the shared ones first, then the district's own).
    /// </summary>
    public static List<string> Civilians(IReadOnlyList<string> group, IReadOnlySet<string> made, int room = CivilianSlots)
    {
        var have = group.Where(made.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (have.Count <= room) return have;
        return [.. Enumerable.Range(0, room).Select(i => have[(int)((long)i * have.Count / room)])];
    }

    /// <summary>
    /// Every resource of the Guerrilla world and the characters it lists: rfg_Default (every EDF and Marauder type
    /// made, as one a container, and the shared civilians), and one a district group (the start's EDF and
    /// Marauders, and the group's civilians). Each lists one character a container (<paramref name="containerOf"/>).
    /// </summary>
    public static Dictionary<string, List<string>> Resources(IReadOnlyDictionary<string, List<string>> groups, IEnumerable<string> districtGroups,
                                                             IReadOnlySet<string> made, IReadOnlyDictionary<string, string> containerOf,
                                                             Func<string, string?> teamOf)
    {
        var resources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var soldiers = made.Where(c => teamOf(c) is { } t && (t.Equals("EDF", StringComparison.OrdinalIgnoreCase) || t.Equals("Marauder", StringComparison.OrdinalIgnoreCase)));
        var shared = SharedCivilians.Where(made.Contains);
        resources[DefaultResource] = GuerrillaSquads.ResourceCharacters(soldiers.Concat(shared), containerOf);
        var core = DistrictEdf.Concat(DistrictMarauders).Where(made.Contains).ToList();
        foreach (string group in districtGroups)
            if (groups.TryGetValue(group, out var list))
                resources[ResourceOf(group)] = GuerrillaSquads.ResourceCharacters(core.Concat(Civilians(list, made, Slots - core.Count)), containerOf);
        return resources;
    }

    // ---- Vehicles' crews

    /// <summary>
    /// The people a vehicle of <paramref name="team"/> can be crewed by in a zone naming <paramref name="resource"/>
    /// (one of <see cref="Resources"/>): the characters of that team the resource streams in. Guerrilla crews its
    /// vehicles from the zone's own people (its vehicle spawn groups name only vehicles): its civilians' cars by the
    /// zone's group (team Guerilla, whose characters its civilians share), its EDF's and Marauders' by theirs. A
    /// character that shares another's container comes with it but isn't listed.
    /// </summary>
    public static List<string> Crew(IReadOnlyDictionary<string, List<string>> resources, string resource, string team, Func<string, string?> teamOf) =>
        resources.TryGetValue(resource, out var list)
            ? [.. list.Where(c => teamOf(c) is { } t && t.Equals(CrewTeam(team), StringComparison.OrdinalIgnoreCase))]
            : [];

    /// <summary>The team whose characters crew a vehicle of a team: Guerrilla's civilians are its guerrillas' characters.</summary>
    static string CrewTeam(string team) => team.Equals("Civilian", StringComparison.OrdinalIgnoreCase) ? "Guerilla" : team;

    /// <summary>The crews table's file name (in mods\guerrilla_people\), for the vehicles' code to read at run time.</summary>
    public const string CrewList = "crews.txt";

    /// <summary>
    /// The crews table: for each resource and team (EDF, Marauder, Guerilla: civilians and guerrillas), the
    /// characters that can crew a vehicle there (<see cref="Crew"/>), a line each, tab-separated: resource, team,
    /// the characters by commas. A zone names its resource (spawn_resource_data).
    /// </summary>
    public static List<string> CrewLines(IReadOnlyDictionary<string, List<string>> resources, Func<string, string?> teamOf)
    {
        var lines = new List<string> { "# resource\tteam\tcharacters (Guerrilla's people the resource streams in; GuerrillaBuilder.exe's people writes this)" };
        foreach (string resource in resources.Keys.Order(StringComparer.OrdinalIgnoreCase))
            foreach (string team in new[] { "EDF", "Marauder", "Guerilla" })
                if (Crew(resources, resource, team, teamOf) is { Count: > 0 } crew) lines.Add($"{resource}\t{team}\t{string.Join(",", crew)}");
        return lines;
    }
}
