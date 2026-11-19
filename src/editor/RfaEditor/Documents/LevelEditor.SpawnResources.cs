using System.IO;
using System.Text;
using RfaFormats.Levels;
using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaEditor;

// What the level's spawn nodes spawn, kept in its spawn resources (spawn_resource.xtbl: the entry its row names,
// resource_reservation; see SpawnResource): a level can only spawn what its entry holds, so a node placed for a
// squad, vehicle or exo the entry hasn't spawned nothing. Whenever a change is kept (a node added, duplicated or
// pasted, its squad, vehicle set or exo changed, undo and redo), what the zone's spawn nodes need is worked out
// from the game's tables and whatever the entry lacks is added to it:
//   squad spawns: the squad's leader and members (squads.xtbl member_info: characters, civilians among them), and
//     with squad_vehicle its vehicle set's vehicles (vehicle_type: spawn_group_vehicle.xtbl), with squad_exo its exo;
//   vehicle spawns: their set's vehicles (spawn_set: spawn_group_vehicle.xtbl vehicle_type, vehicles.xtbl names);
//   exo spawns: the exo (exo_type: exo.xtbl, the entry's exo_list).
// An entry other levels share is first split off into the level's own (its row then names that), as Spawn
// resources' "Give this level its own" does, so no other level changes. A resource streams at most 16 characters
// (GuerrillaSquads.HumanSlots: the game's 17 stream slots less the player): past that a character is left out, and
// said. The game reads its tables at start-up, so the entry applies from its next start; Live has the running game
// reserve what a node it makes needs at once (editor_reserve, see LiveReserveLines).
public partial class LevelEditor
{
    const string SpawnTablesArchive = @"build\pc\cache\table.vpp_pc";
    const int MostResourceCharacters = 16;
    static readonly string[] DlcTablePrefixes = ["", "dlc01_", "dlc02_", "dlc03_", "dlc04_"];
    static readonly (string Table, string Row, string Key)[] LevelTables =
    [
        ("wrecking_crew_maps.xtbl", "wc_map_list", "file_name"),
        ("mp_levels.xtbl", "mp_level_list", "Filename"),
        ("missions.xtbl", "mission", "Name"),
    ];

    /// <summary>What spawn nodes need: characters, vehicles (vehicles.xtbl names) and exos (exo.xtbl names).</summary>
    sealed class SpawnNeeds
    {
        public readonly List<string> Characters = [], Vehicles = [], Exos = [];
        public bool Any => Characters.Count + Vehicles.Count + Exos.Count > 0;

        public void Add(List<string> list, string? name)
        {
            if (name?.Trim() is { Length: > 0 } n && !n.Equals("None", StringComparison.OrdinalIgnoreCase) && !list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
        }
    }

    bool spawnLevelUnknownSaid;
    string? spawnNeedsChecked;  // the needs last checked against the entry: the same again needs no look
    readonly HashSet<string> spawnFullSaid = new(StringComparer.OrdinalIgnoreCase);

    static bool IsSpawnNodeClass(string cls) =>
        cls.Equals("object_squad_spawn_node", StringComparison.OrdinalIgnoreCase) ||
        cls.Equals("object_vehicle_spawn_node", StringComparison.OrdinalIgnoreCase) ||
        cls.Equals("object_exo_spawn_node", StringComparison.OrdinalIgnoreCase);

    /// <summary>A table's entry by name, the DLCs' tables searched too (as <see cref="ObjectModels"/>); null if none.</summary>
    XtblElement? SpawnTableEntry(string tableName, string entryName)
    {
        if (context.Assets.Table(tableName) is not { } table) return null;
        return DlcTablePrefixes.Select(prefix => prefix.Length == 0 ? table : context.Assets.Table(prefix + tableName)).OfType<XtblFile>()
            .SelectMany(t => t.Entries).FirstOrDefault(e => XtblFile.EntryName(e).Equals(entryName, StringComparison.OrdinalIgnoreCase));
    }

    static string? SpawnText(ZoneObject obj, string property) =>
        obj.Property(property) is { Type: ZoneProperty.TextType } p && p.Text.Trim().Length > 0 ? p.Text.Trim() : null;

    static bool SpawnFlag(ZoneObject obj, string property) => obj.Property(property) is { Size: >= 1 } p && p.Value[0] != 0;

    /// <summary>A vehicle set's vehicles (spawn_group_vehicle.xtbl's vehicle_type), into the needs.</summary>
    void AddVehicleSet(SpawnNeeds needs, string? set)
    {
        if (set == null || set.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
        if (SpawnTableEntry("spawn_group_vehicle.xtbl", set) is not { } entry) return;
        foreach (XtblElement list in entry.ElementsNamed("vehicle_list"))
            foreach (XtblElement vehicle in list.ElementsNamed("vehicle_type")) needs.Add(needs.Vehicles, vehicle.Value);
    }

    /// <summary>What one object spawns (nothing for anything but a squad, vehicle or exo spawn node).</summary>
    void AddSpawnNeeds(SpawnNeeds needs, ZoneObject obj)
    {
        switch (names.Class(obj.ClassHash).ToLowerInvariant())
        {
            case "object_squad_spawn_node":
                if (SpawnText(obj, "squad_def") is string squad && SpawnTableEntry("squads.xtbl", squad) is { } entry)
                {
                    needs.Add(needs.Characters, entry.Element("squad_leader")?.ValueOf("member_info"));
                    foreach (XtblElement list in entry.ElementsNamed("squad_member_list"))
                        foreach (XtblElement member in list.ElementsNamed("squad_member")) needs.Add(needs.Characters, member.ValueOf("member_info"));
                }
                if (SpawnFlag(obj, "squad_vehicle")) AddVehicleSet(needs, SpawnText(obj, "vehicle_type"));
                if (SpawnFlag(obj, "squad_exo")) needs.Add(needs.Exos, SpawnText(obj, "exo_type"));
                break;
            case "object_vehicle_spawn_node":
                AddVehicleSet(needs, SpawnText(obj, "spawn_set"));
                break;
            case "object_exo_spawn_node":
                needs.Add(needs.Exos, SpawnText(obj, "exo_type"));
                break;
        }
    }

    /// <summary>
    /// The lines that have the running game hold what a node it is about to make spawns (editor_reserve, before its
    /// editor_create): its characters, its vehicles, and each exo as the character it is made of (exo.xtbl's
    /// Character: the game holds an exo as its character, 0x761D70).
    /// </summary>
    IEnumerable<string> LiveReserveLines(ZoneObject obj)
    {
        if (!IsSpawnNodeClass(names.Class(obj.ClassHash))) yield break;
        var needs = new SpawnNeeds();
        AddSpawnNeeds(needs, obj);
        foreach (string exo in needs.Exos)
            needs.Add(needs.Characters, SpawnTableEntry("exo.xtbl", exo)?.ValueOf("Character"));
        foreach (string character in needs.Characters) yield return "editor_reserve character " + character;
        foreach (string vehicle in needs.Vehicles) yield return "editor_reserve vehicle " + vehicle;
    }

    /// <summary>The level's row naming its spawn resources: its table, row and key tags, its key, and the entry; null if none.</summary>
    (string Table, string Row, string KeyTag, string Key, string Entry)? SpawnLevelRow()
    {
        static string Bare(string name) => Path.GetFileName(name).Split('.')[0];
        var keys = new List<string>();
        void Key(string? name)
        {
            if (name == null) return;
            string bare = Bare(name);
            if (bare.Length > 0 && !keys.Contains(bare, StringComparer.OrdinalIgnoreCase)) keys.Add(bare);
            if (bare.StartsWith("p_", StringComparison.OrdinalIgnoreCase)) Key(bare[2..]);
        }
        Key(zoneName);
        Key(container);
        Key(archive);
        foreach (var (tableName, row, keyTag) in LevelTables)
        {
            string table;
            try { table = Encoding.Latin1.GetString(context.Workspace.Read(SpawnTablesArchive, tableName)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { continue; }
            foreach (string key in keys)
                if (LevelFiles.RowValue(table, row, keyTag, key, "resource_reservation") is { Length: > 0 } entry)
                    return (tableName, row, keyTag, key, entry);
        }
        return null;
    }

    /// <summary>
    /// After a change is kept: whatever the zone's spawn nodes need that the level's spawn resources lack is added to
    /// them (see the top). Quiet when nothing is missing.
    /// </summary>
    void KeepSpawnResourcesStocked()
    {
        var needs = new SpawnNeeds();
        try
        {
            foreach (ZoneObject obj in zone.Objects)
                if (IsSpawnNodeClass(names.Class(obj.ClassHash))) AddSpawnNeeds(needs, obj);
        }
        catch (InvalidOperationException) { return; }  // (the objects changed under us: next time)
        if (!needs.Any) return;
        string signature = string.Join("|", new[] { needs.Characters, needs.Vehicles, needs.Exos }
            .Select(list => string.Join(",", list.Order(StringComparer.OrdinalIgnoreCase))));
        if (signature == spawnNeedsChecked) return;
        spawnNeedsChecked = signature;
        if (SpawnLevelRow() is not { } level)
        {
            if (!spawnLevelUnknownSaid)
            {
                spawnLevelUnknownSaid = true;
                context.Log($"Spawn resources: no Ruin, multiplayer or mission row names {zoneName}'s spawn resources, so what its spawn nodes need isn't added to them " +
                            "(the game may not spawn a squad, vehicle or exo it hasn't).");
            }
            return;
        }
        try
        {
            string resources = Encoding.Latin1.GetString(context.Workspace.Read(SpawnTablesArchive, "spawn_resource.xtbl"));
            SpawnResource entry = SpawnResource.Read(resources, level.Entry) ?? new SpawnResource { Name = level.Entry };
            var added = new List<string>();
            var left = new List<string>();
            void Missing(List<string> have, List<string> want, string what, int most = int.MaxValue)
            {
                foreach (string name in want)
                {
                    if (have.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (have.Count >= most)
                    {
                        if (spawnFullSaid.Add(name)) left.Add(name);
                        continue;
                    }
                    have.Add(name);
                    added.Add($"{name} ({what})");
                }
            }
            Missing(entry.Characters, needs.Characters, "character", MostResourceCharacters);
            Missing(entry.Vehicles, needs.Vehicles, "vehicle");
            Missing(entry.Exos, needs.Exos, "exo");
            if (left.Count > 0)
                context.Log($"Spawn resources: {level.Entry} already lists {MostResourceCharacters} characters, as many as the game streams; left out: {string.Join(", ", left)}. " +
                            "Take some out with Spawn resources, or use squads of fewer kinds of people.");
            if (added.Count == 0) return;

            // An entry other levels name too is split off into this level's own first.
            int sharing = 0;
            foreach (var (tableName, row, keyTag) in LevelTables)
            {
                try
                {
                    string table = Encoding.Latin1.GetString(context.Workspace.Read(SpawnTablesArchive, tableName));
                    sharing += LevelFiles.Keys(table, row, keyTag).Count(k => LevelFiles.RowValue(table, row, keyTag, k, "resource_reservation") is string e &&
                                                                              e.Equals(level.Entry, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { }
            }
            string from = level.Entry;
            if (sharing > 1)
            {
                var taken = SpawnResource.Names(resources).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string own = level.Key;
                for (int n = 2; taken.Contains(own); n++) own = $"{level.Key}_{n}";
                entry.Name = own;
                string table = Encoding.Latin1.GetString(context.Workspace.Read(SpawnTablesArchive, level.Table));
                string changed = LevelFiles.WithRowValue(table, level.Row, level.KeyTag, level.Key, "resource_reservation", own)
                                 ?? throw new InvalidDataException($"{level.Table}'s row for {level.Key} couldn't be changed");
                context.Workspace.WriteOverride(SpawnTablesArchive, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(entry.WriteTo(resources, from)));
                context.Workspace.WriteOverride(SpawnTablesArchive, level.Table, Encoding.Latin1.GetBytes(changed));
                context.Log($"Spawn resources: {level.Key} shared {from} with {sharing - 1} other level(s); it has its own now, \"{own}\" ({level.Table} names it).");
            }
            else context.Workspace.WriteOverride(SpawnTablesArchive, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(entry.WriteTo(resources)));
            context.Log($"Spawn resources: added to {entry.Name}, for the level's spawn nodes: {string.Join(", ", added)}. " +
                        "The game reads its tables at start-up: restart it for a normal load to have them (Live spawns them now).");
            context.OverridesChanged();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            spawnNeedsChecked = null;  // (tried again at the next change)
            context.Log("Spawn resources: what the spawn nodes need couldn't be added: " + ex.Message);
        }
    }
}
