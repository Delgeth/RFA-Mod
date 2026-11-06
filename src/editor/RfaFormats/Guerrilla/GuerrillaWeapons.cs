using System.Globalization;
using System.Text.RegularExpressions;
using RfaFormats.Interface;
using RfaFormats.Tables;
using RfaFormats.Textures;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's weapons made usable in Armageddon, from the player's own copy of Guerrilla
/// (only read): GuerrillaBuilder.exe's weapons, for the guerrilla_world mod, which hands the game the
/// tables at start-up. For each weapon asked for:
///   - its item (items_3d.xtbl: the model in the hand and on the ground, and its props, such as the
///     assault rifle's magazine), Guerrilla's own entry: its static mesh converted
///     (<see cref="GuerrillaStaticMeshes"/>) and packed with its low-detail textures (from Guerrilla's
///     items_containers.cpeg_pc, each a single-texture file under its alias, as the world's) in a
///     container named as the item (as Armageddon's: the item m16 is m16.str2_pc, type 22); its big
///     textures and high-detail material file (Guerrilla's "&lt;mesh&gt;_high" container) in
///     "&lt;item&gt;_high.str2_pc" (type 25), which Armageddon loads for an item flagged high_res_map
///     (Guerrilla's high_res_map_large), by that name (0x7D99F0). Both containers in an override of
///     items.vpp_pc\items_containers.asm_pc (the player's own list with ours added);
///   - its inventory item (items_inventory.xtbl), Guerrilla's, its name shown as Guerrilla shows it (its
///     strings file's text for the tag);
///   - an ammo type of its own (ammo.xtbl): Guerrilla's ammo is the weapon's, Armageddon's shared by
///     weapons naming the same type. Guerrilla's Max_Rounds counts the loaded magazine too (its
///     weapon::add_ammo keeps the reserve at most Max_Rounds − Magazine_Size), Armageddon's Max_Reserve
///     doesn't;
///   - its weapon entry (weapons.xtbl): Guerrilla's flat entry written as Armageddon's one firing mode
///     (field by field: Armageddon's own m16 is Guerrilla's, so moved, with the same numbers);
///   - what the entry names that both games have under one name but differently (the NPCs' firing
///     pattern, aim drift): Guerrilla's under "rfg_" names; one both have alike is used as it is.
/// Names: Guerrilla's, or with "rfg_" before them where Armageddon has one of that name (m16, enforcer,
/// shotgun, sledgehammer, ...). Unique_ID: a number of our own for each (<see cref="UniqueIds"/>), fixed so
/// saved games keep them. Effects (muzzle flash, smoke): the world builder adds Guerrilla's to the world's
/// effect tables (<see cref="Effects"/>); the entry names them if the world built has them. The player's
/// animations for the guns (Guerrilla's, played while he holds one): <see cref="GuerrillaPlayerAnimations"/>,
/// built with them. Left out until their own port: the sounds (Guerrilla's are XACT cues), the melee moves
/// and the bullets' impact effects (Guerrilla's bullet group), the HUD icon.
/// </summary>
public static class GuerrillaWeapons
{
    /// <summary>The weapons built when none are named.</summary>
    public static readonly string[] DefaultWeapons = ["m16", "enforcer", "autoblaster", "gauss_rifle", "rail_driver"];

    /// <summary>
    /// Guerrilla's campaign weapons (its weapons.xtbl entries outside the multiplayer ones), whose effects
    /// the world builder adds to the world's tables (<see cref="Effects"/>), so that the world needs
    /// building once for all of them.
    /// </summary>
    public static readonly string[] CampaignWeapons =
    [
        "sledgehammer", "sledge_upgrade_1", "sledge_upgrade_2", "charge_placer", "mine_placer", "rpg_launcher", "thermobaric_rockets",
        "nano_rifle", "grinder", "arc_welder", "m16", "edf_pistol", "enforcer", "sniper_rifle", "rail_driver", "gauss_rifle", "autoblaster",
        "shotgun", "gutter", "singularity_bomb", "propane_tank", "repair_tool", "grenade", "marauder_shockade",
    ];

    /// <summary>
    /// Each weapon's Unique_ID (one byte in Armageddon; saved games name weapons by it), ours, fixed: only
    /// ever add to this list. Armageddon uses up to 140, its DLC 4 125–127, 200 and 220–223; ours from 141.
    /// </summary>
    static readonly Dictionary<string, int> UniqueIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m16"] = 141, ["enforcer"] = 142, ["autoblaster"] = 143, ["gauss_rifle"] = 144, ["rail_driver"] = 145,
    };

    /// <summary>The files this writes (relative to the overrides folder, then to the mod's folder), for --remove.</summary>
    public const string ItemList = @"build\pc\cache\items.vpp_pc\items_containers.asm_pc";
    public static readonly string[] TableFiles =
        ["weapons.xtbl", "items_3d.xtbl", "items_inventory.xtbl", "ammo.xtbl", "weapon_firing_patterns.xtbl", "aim_drift.xtbl", "camera_shake.xtbl"];

    /// <summary>Armageddon's item container types: a preloaded item's model (General (preloaded)); an item's big textures and material file.</summary>
    const byte ItemContainer = 22, HighContainer = 25;

    /// <summary>The fields of Guerrilla's weapon entries naming an effect the weapon plays.</summary>
    static readonly string[] EffectFields =
        ["Muzzle_Flash", "Muzzle_Smoke", "Tracer_Effect", "Special_Hit_Effect", "Special_Effect", "Secondary_Special_Effect", "Overheated_effect"];

    /// <summary>Guerrilla's teams as Armageddon's (human_team_names.xtbl); EDF has none yet.</summary>
    static readonly Dictionary<string, string> Teams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Guerilla"] = "Red Faction", ["Guerrilla"] = "Red Faction", ["Civilian"] = "Civilian", ["Marauder"] = "Marauder",
    };

    /// <summary>Guerrilla's weapon flags that are Armageddon's weapon's flags (the same names), and those that went into its firing modes (as named there).</summary>
    static readonly string[] SameWeaponFlags =
        ["can fine aim", "can scope", "held in left hand", "draw projectile in opposite hand", "non-inventory", "mp selectable", "dont drop on death"];
    static readonly Dictionary<string, string> FiringModeFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shatter"] = "shatter", ["looping effects"] = "looping fire effects", ["auto-aim curved trail"] = "auto aim curved trail",
        ["use even spread"] = "distribute spread evenly",
    };

    /// <summary>
    /// The effects (Guerrilla's effects.xtbl names) Guerrilla's campaign weapons play themselves: the world
    /// builder adds these to the world's effect tables under "rfg_" names, as it does the dust it plays everywhere.
    /// </summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaMisc)
    {
        return EffectsOf(guerrillaMisc);
    }

    /// <summary>
    /// The explosions (Guerrilla's explosions.xtbl names) Guerrilla's campaign weapons' shots set off (a weapon's
    /// Explosion and AIExplosion, such as the gauss rifle's): the world builder adds these to the world's
    /// explosion table under "rfg_" names, with their effects (its worldExplosions).
    /// </summary>
    public static IReadOnlyList<string> Explosions(Packfile guerrillaMisc)
    {
        var weapons = Read(guerrillaMisc, "weapons.xtbl");
        var explosions = new List<string>();
        foreach (string name in CampaignWeapons)
            if (CampaignEntry(weapons, name) is { } entry)
                foreach (var element in Descendants(entry).Where(e => e.Name.Equals("Explosion", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("AIExplosion", StringComparison.OrdinalIgnoreCase)))
                    if (element.Value is { Length: > 0 } explosion && !explosions.Contains(explosion, StringComparer.OrdinalIgnoreCase)) explosions.Add(explosion);
        return explosions;
    }

    static IReadOnlyList<string> EffectsOf(Packfile guerrillaMisc)
    {
        var weapons = Read(guerrillaMisc, "weapons.xtbl");
        var effects = new List<string>();
        foreach (string name in CampaignWeapons)
            if (CampaignEntry(weapons, name) is { } entry)
                foreach (string field in EffectFields)
                    foreach (var element in Descendants(entry).Where(e => e.Name.Equals(field, StringComparison.OrdinalIgnoreCase)))
                        if (element.Value is { Length: > 0 } effect && !effects.Contains(effect, StringComparer.OrdinalIgnoreCase)) effects.Add(effect);
        return effects;
    }

    /// <summary>
    /// Builds the weapons named (Guerrilla's weapons.xtbl entry names) into <paramref name="overrides"/> and
    /// <paramref name="modFolder"/> (mods\guerrilla_world\). Returns what was done, a line each.
    /// </summary>
    public static IEnumerable<string> Build(string guerrilla, string game, string overrides, string modFolder, IReadOnlyList<string> weapons)
    {
        var b = new Builder(guerrilla, game, modFolder);
        foreach (string name in weapons)
            foreach (string line in b.Weapon(name)) yield return line;

        string cache = Path.Combine(overrides, "build", "pc", "cache");
        Directory.CreateDirectory(cache);
        foreach (var (file, bytes) in b.Containers) File.WriteAllBytes(Path.Combine(cache, file), bytes);
        string listFile = Path.Combine(overrides, ItemList);
        Directory.CreateDirectory(Path.GetDirectoryName(listFile)!);
        File.WriteAllBytes(listFile, b.ItemAssets.ToBytes());
        Directory.CreateDirectory(modFolder);
        KeepPeoplesItems(b.Tables.First(t => t.File == GuerrillaPeople.ItemTable).Table, modFolder);
        foreach (var (file, table) in b.Tables) File.WriteAllBytes(Path.Combine(modFolder, file), table.ToBytes());
        // The player's animations for them (Guerrilla's), with the guerrilla_gear mod's files.
        foreach (string line in GuerrillaPlayerAnimations.Build(guerrilla, game, overrides, modFolder, GearFolder(modFolder), b.PlayerWeapons)) yield return line;
        // What Guerrilla scales the rounds of a gun an NPC drops by, at each difficulty (guerrilla_gear's dropped guns).
        yield return WriteAmmoPickup(guerrilla, GearFolder(modFolder));
        yield return b.Built.Count == 0 ? "No weapons built." : $"Weapons: {string.Join(", ", b.Built)}";
    }

    /// <summary>
    /// The people's build adds its props' items (Mason's coat, ...) to the items table this writes, and
    /// names them in mods\guerrilla_people\prop_items.txt; this table starts from Armageddon's, so those
    /// already in the one written before are kept.
    /// </summary>
    static void KeepPeoplesItems(XtblFile items, string modFolder)
    {
        string itemsPath = Path.Combine(modFolder, GuerrillaPeople.ItemTable);
        string listPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modFolder))!, "guerrilla_people", GuerrillaPeople.PropItemList);
        if (!File.Exists(itemsPath) || !File.Exists(listPath)) return;
        var theirs = File.ReadAllLines(listPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var have = items.Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in XtblFile.FromBytes(File.ReadAllBytes(itemsPath)).Entries)
            if (theirs.Contains(XtblFile.EntryName(entry)) && have.Add(XtblFile.EntryName(entry)))
                items.Table!.AddElement(entry.Copy());
    }

    /// <summary>
    /// The file the guerrilla_gear mod reads Guerrilla's "player ammo pickup" difficulty from (difficulty.xtbl's
    /// MENU_PLAYER_AMMO_PICKUP: Easy 2.0, Medium 1.0, Hard 0.9, Insane 0.8 in the Steam Edition): Guerrilla scales
    /// the rounds a gun an NPC drops carries by it (weapon::adjust_dropped_ammo). Armageddon's own table has 1.0
    /// at every difficulty, so its value isn't Guerrilla's. One "level value" a line.
    /// </summary>
    public const string AmmoPickupFile = "ammo_pickup.txt";

    static string WriteAmmoPickup(string guerrilla, string gearFolder)
    {
        var misc = Packfile.Open(Path.Combine(guerrilla, "build", "pc", "cache", "misc.vpp_pc"));
        var item = Descendants(Read(misc, "difficulty.xtbl").Table ?? new XtblElement("Table"))
            .FirstOrDefault(e => e.Name.Equals("Item", StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(e.ValueOf("Name")?.Trim(), "MENU_PLAYER_AMMO_PICKUP", StringComparison.OrdinalIgnoreCase));
        if (item == null) return "dropped guns: Guerrilla's difficulty.xtbl has no MENU_PLAYER_AMMO_PICKUP; guerrilla_gear takes 1.0 at every difficulty";
        string[] levels = ["Easy", "Medium", "Hard", "Insane"];
        var lines = levels.Select(l => $"{l} {item.ValueOf(l)?.Trim() ?? "1.0"}").ToList();
        Directory.CreateDirectory(gearFolder);
        File.WriteAllLines(Path.Combine(gearFolder, AmmoPickupFile), lines);
        return $"dropped guns: Guerrilla's player ammo pickup by difficulty ({string.Join(", ", lines)}) in {Path.Combine(gearFolder, AmmoPickupFile)}";
    }

    /// <summary>The guerrilla_gear mod's folder (mods\guerrilla_gear\), beside the world's.</summary>
    static string GearFolder(string modFolder) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modFolder))!, "guerrilla_gear");

    /// <summary>Takes away everything <see cref="Build"/> wrote: the items' containers (the ones our asset list names that Armageddon's hasn't), the asset list and the tables.</summary>
    public static IEnumerable<string> Remove(string game, string overrides, string modFolder)
    {
        string listFile = Path.Combine(overrides, ItemList);
        if (File.Exists(listFile))
        {
            var gameItems = Packfile.Open(Path.Combine(game, "build", "pc", "cache", "items.vpp_pc"));
            foreach (var c in AsmFile.Read(File.ReadAllBytes(listFile)).Containers.Where(c => gameItems.Find(c.FileName) == null))
            {
                string file = Path.Combine(overrides, "build", "pc", "cache", c.FileName);
                if (File.Exists(file)) { File.Delete(file); yield return $"Deleted {file}"; }
            }
            File.Delete(listFile);
            yield return $"Deleted {listFile}";
        }
        foreach (string name in TableFiles)
        {
            string table = Path.Combine(modFolder, name);
            if (File.Exists(table)) { File.Delete(table); yield return $"Deleted {table}"; }
        }
        foreach (string line in GuerrillaPlayerAnimations.Remove(overrides, modFolder, GearFolder(modFolder))) yield return line;
        string ammoPickup = Path.Combine(GearFolder(modFolder), AmmoPickupFile);
        if (File.Exists(ammoPickup)) { File.Delete(ammoPickup); yield return $"Deleted {ammoPickup}"; }
    }

    /// <summary>The work of one Build: both games' files, the tables being written and what has been made.</summary>
    sealed class Builder
    {
        readonly Packfile guerrillaMisc, guerrillaItems, gameItems;
        readonly XtblFile rfgWeapons, rfgItems, rfgInventory, rfgPatterns, rfgDrift, rfgShakes, gameShakes;
        readonly XtblFile weapons, items, inventory, ammo, patterns, drift;
        readonly HashSet<string> worldExplosions;
        readonly Dictionary<XtblFile, IReadOnlySet<string>> commonFields = [];  // each table's fields all of Armageddon's entries have
        readonly Dictionary<uint, string> strings;
        readonly HashSet<string> worldEffects;
        readonly PegFile lowTextures;
        readonly Dictionary<string, string> itemsDone = new(StringComparer.OrdinalIgnoreCase);  // Guerrilla's item → ours
        readonly Dictionary<string, string?> supportDone = new(StringComparer.OrdinalIgnoreCase); // "table:name" → ours (null: none)

        public readonly Dictionary<string, byte[]> Containers = new(StringComparer.OrdinalIgnoreCase);
        public readonly AsmFile ItemAssets;
        public readonly List<string> Built = [];
        public readonly List<GuerrillaPlayerAnimations.PlayerWeapon> PlayerWeapons = [];  // the guns built and their animation groups
        public IEnumerable<(string File, XtblFile Table)> Tables =>
            [("weapons.xtbl", weapons), ("items_3d.xtbl", items), ("items_inventory.xtbl", inventory), ("ammo.xtbl", ammo),
             ("weapon_firing_patterns.xtbl", patterns), ("aim_drift.xtbl", drift), ("camera_shake.xtbl", gameShakes)];

        public Builder(string guerrilla, string game, string modFolder)
        {
            string guerrillaCache = Path.Combine(guerrilla, "build", "pc", "cache"), gameCache = Path.Combine(game, "build", "pc", "cache");
            guerrillaMisc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
            guerrillaItems = Packfile.Open(Path.Combine(guerrillaCache, "items.vpp_pc"));
            var gameMisc = Packfile.Open(Path.Combine(gameCache, "misc.vpp_pc"));
            gameItems = Packfile.Open(Path.Combine(gameCache, "items.vpp_pc"));
            rfgWeapons = Read(guerrillaMisc, "weapons.xtbl");
            rfgItems = Read(guerrillaMisc, "items_3d.xtbl");
            rfgInventory = Read(guerrillaMisc, "items_inventory.xtbl");
            rfgPatterns = Read(guerrillaMisc, "weapon_firing_patterns.xtbl");
            rfgDrift = Read(guerrillaMisc, "aim_drift.xtbl");
            rfgShakes = Read(guerrillaMisc, "camera_shake.xtbl");
            gameShakes = Read(gameMisc, "camera_shake.xtbl");
            weapons = Read(gameMisc, "weapons.xtbl");
            items = Read(gameMisc, "items_3d.xtbl");
            inventory = Read(gameMisc, "items_inventory.xtbl");
            ammo = Read(gameMisc, "ammo.xtbl");
            patterns = Read(gameMisc, "weapon_firing_patterns.xtbl");
            drift = Read(gameMisc, "aim_drift.xtbl");
            strings = guerrillaMisc.Find("weapons_us.le_strings") is { } stringsFile ? GuerrillaHud.ReadStrings(guerrillaMisc.Read(stringsFile)) : [];
            // The effects the world built has added (the world builder's effects.xtbl, beside these tables).
            string worldEffectTable = Path.Combine(modFolder, "effects.xtbl");
            worldEffects = File.Exists(worldEffectTable)
                ? XtblFile.FromBytes(File.ReadAllBytes(worldEffectTable)).Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            string worldExplosionTable = Path.Combine(modFolder, "explosions.xtbl");
            worldExplosions = File.Exists(worldExplosionTable)
                ? XtblFile.FromBytes(File.ReadAllBytes(worldExplosionTable)).Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            foreach (var table in new[] { weapons, items, inventory, ammo, patterns, drift, gameShakes }) commonFields[table] = GuerrillaTableFit.CommonFields(table);
            byte[] ReadItems(string file) => guerrillaItems.Read(guerrillaItems.Find(file) ?? throw new InvalidDataException($"no {file} in Guerrilla's items.vpp_pc"));
            lowTextures = PegFile.Read(ReadItems("items_containers.cpeg_pc"), ReadItems("items_containers.gpeg_pc"));
            ItemAssets = AsmFile.Read(gameItems.Read(gameItems.Find("items_containers.asm_pc") ?? throw new InvalidDataException("no items_containers.asm_pc in Armageddon's items.vpp_pc")));
        }

        /// <summary>One weapon: its item, inventory item, ammo and entry.</summary>
        public IEnumerable<string> Weapon(string name)
        {
            if (CampaignEntry(rfgWeapons, name) is not { } g) { yield return $"{name}: Guerrilla's weapons.xtbl has no such weapon (outside its multiplayer copies)"; yield break; }
            if (!UniqueIds.TryGetValue(name, out int id)) { yield return $"{name}: has no Unique_ID of ours yet (add it to GuerrillaWeapons.UniqueIds); left out"; yield break; }
            string kind = g.ValueOf("Weapon_Class")?.Trim() ?? "";
            if (!kind.Equals("bullet", StringComparison.OrdinalIgnoreCase)) { yield return $"{name}: a {kind} weapon; only bullet weapons are converted yet"; yield break; }
            string ours = Ours(name, weapons);
            var notes = new List<string>();

            string? item = Item(g.ValueOf("Item_3d")?.Trim() ?? "", notes);
            string? inventoryItem = item == null ? null : InventoryItem(g.ValueOf("Inventory_Item")?.Trim() ?? "", notes);
            foreach (string note in notes) yield return $"{name}: {note}";
            notes.Clear();
            if (item == null || inventoryItem == null) { yield return $"{name}: left out (its item or inventory item couldn't be made)"; yield break; }

            int magazine = Int(g, "Magazine_Size"), maxRounds = Int(g, "Max_Rounds"), restock = Int(g, "Ammo_Box_Restock"), magazines = Int(g, "Num_Magazines");
            string ammoType = AmmoType(ours, Math.Max(0, maxRounds - magazine), restock);
            string? pattern = Support(g.ValueOf("NPC_Firing_Pattern"), rfgPatterns, patterns, "Firing_Pattern", notes);
            string? aimDrift = Support(g.ValueOf("NPC_Aim_Drift"), rfgDrift, drift, "Profile", notes);
            var entry = WeaponEntry(g, ours, id, item, inventoryItem, ammoType, Math.Max(0, magazines - 1) * magazine, pattern, aimDrift, notes);
            var description = weapons.Description ?? throw new InvalidDataException("Armageddon's weapons.xtbl has no description");
            var dropped = new List<string>();
            var added = new List<string>();
            var unfilled = new List<string>();
            GuerrillaTableFit.Prune(entry, description.Children, "", dropped, commonFields[weapons]);
            GuerrillaTableFit.FitValues(entry, description.Children, "", []);
            GuerrillaTableFit.FillRequired(entry, description.Children, "", added, unfilled);
            weapons.Table!.AddElement(entry);
            if (g.ValueOf("Animation_Group")?.Trim() is { Length: > 0 } group) PlayerWeapons.Add(new(ours, group));
            Built.Add($"{name} as {ours} (Unique_ID {id}; give_player_item {inventoryItem})");
            foreach (string note in notes) yield return $"{name}: {note}";
            if (dropped.Count > 0) yield return $"{name}: left out of its weapon entry (Armageddon's table hasn't them): {string.Join(", ", dropped)}";
            if (added.Count > 0) yield return $"{name}: Armageddon's defaults added for the fields it requires: {string.Join(", ", added.Distinct())}";
            if (unfilled.Count > 0) yield return $"{name}: required by Armageddon, with no default (the game will stop on them): {string.Join(", ", unfilled)}";
        }

        /// <summary>
        /// Guerrilla's flat weapon entry as Armageddon's: the weapon's own fields, then one firing mode with
        /// what fires (see the class summary).
        /// </summary>
        XtblElement WeaponEntry(XtblElement g, string name, int id, string item, string inventoryItem, string ammoType, int pickupRounds,
                                string? pattern, string? aimDrift, List<string> notes)
        {
            string? G(string field) => g.ValueOf(field)?.Trim() is { Length: > 0 } v ? v : null;
            var guerrillaFlags = g.Element("Flags")?.Elements.Select(f => f.Value.Trim()).ToList() ?? [];

            var w = new XtblElement("Weapon");
            w.AddElement("Name", name);
            Editor(w, g);
            w.AddElement("Unique_ID", id.ToString(CultureInfo.InvariantCulture));
            w.AddElement("Inventory_Item", inventoryItem);
            w.AddElement("Item_3d", item);
            if (G("Default_team") is { } team && Teams.TryGetValue(team, out var ourTeam)) w.AddElement("Default_team", ourTeam);
            Copy(g, "reticule_name", w);
            Copy(g, "fine_aim_reticule", w);
            Copy(g, "Weapon_Class", w);
            string group = G("Animation_Group") ?? "Default";
            w.AddElement("Animation_Group", group.Equals("Nano", StringComparison.OrdinalIgnoreCase) ? "Nano Rifle" : group);
            Copy(g, "Player_Move_Speed", w);
            Copy(g, "NPC_Move_Speed", w);
            Copy(g, "Zoom_Magnification", w);
            if (G("Alert_Level_Multiplier") is { } alert) w.AddElement("Alert_Multiplier", alert);
            var flags = w.AddElement("Flags");
            foreach (string flag in guerrillaFlags.Where(f => SameWeaponFlags.Contains(f, StringComparer.OrdinalIgnoreCase))) flags.AddElement("Flag", flag);
            // Guerrilla gives the rail driver's scope its own HUD module, which shows people through walls
            // (human::set_equipped_inv_item, RM 0xAB4D20, by the weapon): Armageddon's "xray zoom", as its own rail driver has.
            if (XtblFile.EntryName(g).Equals("rail_driver", StringComparison.OrdinalIgnoreCase)) flags.AddElement("Flag", "xray zoom");
            w.AddElement("Sound_Data");

            var mode = w.AddElement("Firing_Mode_Slots").AddElement("Firing_Mode_Slot").AddElement("Potential_Firing_Modes")
                        .AddElement("Potential_Firing_Mode");
            mode.AddElement("Name", "Default");
            var f = mode.AddElement("Firing_Mode");

            // What a shot is: a bullet (one through people and thin walls with Guerrilla's flag), its tracers.
            var shot = f.AddElement("Shot_Type");
            if (guerrillaFlags.Contains("penetrating bullets", StringComparer.OrdinalIgnoreCase))
            {
                var penetrating = shot.AddElement("Penetrating_Bullet");
                if (G("Max_AI_Penetrating_Distance") is { } distance) penetrating.AddElement("Max_AI_Penetrating_Dist", distance);
                if (Effect(g, "Tracer_Effect", notes) is { } tracer) penetrating.AddElement("Tracer_Effect", tracer);
                if (Descendant(g, "Tracer_Frequency") is { } frequency) penetrating.AddElement("Tracer_Frequency", frequency);
            }
            else
            {
                var bullet = shot.AddElement("Bullet");
                // Guerrilla names a tracer effect for few weapons; without one its tracer system draws its own (as Armageddon's).
                if (Effect(g, "Tracer_Effect", notes) is { } tracer) bullet.AddElement("Tracer_Effect", tracer);
                if (Descendant(g, "Tracer_Frequency") is { } frequency) bullet.AddElement("Tracer_Frequency", frequency);
            }
            f.AddElement("Trigger_Type").AddElement((G("Trigger_Type") ?? "single").ToLowerInvariant() == "automatic" ? "Automatic" : "Single");
            f.AddElement("Barrel_Behavior").AddElement("Single").AddElement("Index", "0");
            Copy(g, "Shots_Per_Round", f);
            Copy(g, "Magazine_Size", f);
            f.AddElement("Ammo_Type", ammoType);
            f.AddElement("Ammo_Per_Round", "1");
            if (G("Ammo_Box_Restock") is { } restock) f.AddElement("Rounds_From_Ammo_Box", restock);
            f.AddElement("Rounds_On_Pickup", pickupRounds.ToString(CultureInfo.InvariantCulture));
            if (G("Dropped_ammo_scale") is { } dropped) f.AddElement("Dropped_Ammo_Scale", dropped);
            // A weapon firing several shots a round (the autoblaster's 5) throws each into a cone round the aimed
            // shot (Fire_Cone_Angle); Armageddon has no such cone (its shotgun's pellets take the spread alone), so the
            // cone is added to the spreads: each pellet can stray as far as in Guerrilla, though not in its cone's way.
            double cone = Int(g, "Shots_Per_Round") > 1 && double.TryParse(G("Fire_Cone_Angle"), NumberStyles.Float, CultureInfo.InvariantCulture, out double c) ? c : 0;
            foreach (string field in new[] { "Spread_Min", "Spread_Max", "Spread_Fine_Aim_Min", "Spread_Fine_Aim_Max", "NPC_Spread_Min", "NPC_Spread_Max" })
                if (G(field) is { } spread)
                    f.AddElement(field, cone > 0 && double.TryParse(spread, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                        ? (v + cone).ToString("0.###", CultureInfo.InvariantCulture) : spread);
            if (cone > 0) notes.Add($"its {G("Shots_Per_Round")} shots a round: Guerrilla's {cone} degree cone added to each spread (Armageddon has no cone of its own; the exact cone waits for code of ours)");
            // Guerrilla's shots to the widest spread and time back to the narrowest, and its running spread.
            if (G("To_Spread_Max") is { } toMax) f.AddElement("Spread_Shots_To_Max", toMax);
            if (G("To_Spread_Min") is { } toMin) f.AddElement("Spread_Cooldown_Time_ms", toMin);
            if (G("Spread_Multiplier_Run") is { } run) f.AddElement("Spread_Multiplier_Moving", run);
            foreach (string field in new[] { "Range_Red", "Min_Engagement_Distance", "Max_Engagement_Distance", "Range_Max" }) Copy(g, field, f);
            if (pattern != null) f.AddElement("NPC_Firing_Pattern", pattern);
            if (aimDrift != null) f.AddElement("NPC_Aim_Drift", aimDrift);
            foreach (string block in new[] { "Damage_Scaling_Min", "Damage_Scaling_Max" })
                if (g.Element(block) is { } damage)
                {
                    var ours = f.AddElement(block);
                    foreach (string field in new[] { "Threshold", "NPC_Damage", "Player_Damage", "Vehicle_Damage", "Player_Vehicle_Damage" }) Copy(damage, field, ours);
                    // A building takes the bullet's damage as Guerrilla's object_mover::process_bullet_hit (RM
                    // 0xAA2610) passes it: what the shot does by its kind of target, the NPC figure for an object.
                    ours.AddElement("Structural_Damage", damage.ValueOf("NPC_Damage")?.Trim() ?? "0");
                }
            Copy(g, "Headshot_Multiplier", f);
            foreach (var (field, ourField) in new[] { ("Explosion", "Explosion"), ("AIExplosion", "AI_Explosion") })
                if (G(field) is { } explosion)
                {
                    string ourExplosion = GuerrillaEffectTables.Added(explosion);
                    if (worldExplosions.Contains(ourExplosion)) f.AddElement(ourField, ourExplosion);
                    else notes.Add($"{field} {explosion}: not in the world's explosion table (build the world again: it adds the weapons' explosions it can); left out");
                }
            if (G("Default_Refire_Delay") is { } refire)
            {
                f.AddElement("Player_Refire_Delay_ms", refire);
                f.AddElement("NPC_Refire_Delay_ms", G("NPC_Refire_Delay") ?? refire);
            }
            if (G("Reload_Delay") is { } reload) f.AddElement("Empty_Reload_Delay_ms", reload);
            if (G("Ragdoll_Force_Shoot") is { } ragdoll) f.AddElement("Ragdoll_Force", ragdoll);
            if (G("RecoilImpulse") is { } impulse) f.AddElement("Recoil_Impulse", impulse);
            f.AddElement("Gib_Chance", "0");
            f.AddElement("Gib_Head_On_Death_Chance", "0");
            if (G("Autoaim_override") is { } autoaim) f.AddElement("Autoaim_Override", autoaim);
            if (G("NPC_autoaim") is { } npcAutoaim) f.AddElement("NPC_Autoaim", npcAutoaim);
            var sounds = f.AddElement("Sounds");
            if (Descendant(g, "Sound_Radius") is { } radius) sounds.AddElement("Sound_Radius", radius);
            var visuals = f.AddElement("Visuals");
            foreach (string field in new[] { "Muzzle_Flash", "Muzzle_Smoke", "Overheated_effect" })
                if (Effect(g, field, notes) is { } effect) visuals.AddElement(field, effect);
            foreach (string field in new[] { "Fire_Camera_Shake", "Player_Hit_Camera_Shake" })
                if (Descendant(g, field) is { } shake)
                {
                    if (Support(shake, rfgShakes, gameShakes, "Camera_Shake", notes) is { } ourShake) visuals.AddElement(field, ourShake);
                }
            if (G("Bullet_hole_scale") is { } holeScale) f.AddElement("Bullet_hole_scale", holeScale);
            if (G("Recoil_Kick") is { } kick) f.AddElement("Camera_Recoil_Kick", kick);
            var modeFlags = f.AddElement("Flags");
            foreach (string flag in guerrillaFlags)
                if (FiringModeFlags.TryGetValue(flag, out var ourFlag)) modeFlags.AddElement("Flag", ourFlag);

            notes.Add("not yet: its sounds (Guerrilla's XACT cues, with the audio port), its melee moves (with the sledgehammer), " +
                      "its bullets' impact effects (Guerrilla's bullet group " + (G("Bullet_Group") ?? "Standard") + "; Armageddon's default group's play), " +
                      "its HUD icon (" + (G("icon_name") ?? "none") + ")");
            foreach (string field in new[] { "Special_Effect", "Secondary_Special_Effect", "Special_Hit_Effect" })
                if (Descendant(g, field) is { } special) notes.Add($"{field} {special}: Armageddon's firing modes have no such field; left out until its code is ported");
            return w;
        }

        /// <summary>
        /// Our item for Guerrilla's items_3d entry <paramref name="name"/> (with its props, made first): its
        /// containers and entry. Null if it can't be made yet (a character mesh, a model that doesn't convert).
        /// </summary>
        string? Item(string name, List<string> notes)
        {
            if (itemsDone.TryGetValue(name, out var done)) return done;
            var g = rfgItems.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (g == null) { notes.Add($"item {name}: Guerrilla's items_3d.xtbl has none"); return null; }
            if (g.Element("character_mesh") != null) { notes.Add($"item {name}: a character mesh, which waits for the character-mesh converter"); return null; }
            string? meshFile = g.Element("Mesh")?.ValueOf("Filename")?.Trim();
            if (string.IsNullOrEmpty(meshFile)) { notes.Add($"item {name}: names no model"); return null; }
            string mesh = Path.GetFileNameWithoutExtension(meshFile);
            string ours = Ours(name, items);

            // The model, with its low-detail textures.
            var source = guerrillaItems.Find(name + ".str2_pc");
            if (source == null) { notes.Add($"item {name}: Guerrilla's items.vpp_pc has no {name}.str2_pc"); return null; }
            var pack = guerrillaItems.OpenContainer(source);
            var c = pack.Find(mesh + ".csmesh_pc");
            var gFile = pack.Find(mesh + ".gsmesh_pc");
            if (c == null || gFile == null) { notes.Add($"item {name}: its container has no {mesh}.csmesh_pc / .gsmesh_pc"); return null; }
            byte[] cBytes = pack.Read(c);
            if (GuerrillaStaticMeshes.NotYet(cBytes) is { } why) { notes.Add($"item {name}: {why}"); return null; }
            var (header, textures) = GuerrillaStaticMeshes.Convert(cBytes, GuerrillaVehicles.Alias);
            var ownPegs = pack.Entries.Where(e => e.Name.EndsWith(".cpeg_pc", StringComparison.OrdinalIgnoreCase))
                              .Select(e => PegFile.Read(pack.Read(e), pack.Read(pack.Find(PegFile.DataFileName(e.Name)!)!))).ToList();
            var files = new List<(string Name, byte[] Data)> { (mesh + ".csmesh_pc", header), (mesh + ".gsmesh_pc", pack.Read(gFile)) };
            var missing = new List<string>();
            foreach (string texture in textures)
            {
                var made = GuerrillaBuiltIns.Files(texture, GuerrillaVehicles.Alias(texture)) ?? Single([.. ownPegs, lowTextures], texture);
                if (made == null) missing.Add(texture);
                else if (!files.Any(x => x.Name.Equals(made[0].Name, StringComparison.OrdinalIgnoreCase))) files.AddRange(made);
            }
            // The model it is on the ground (pickup_static_mesh), if it has one of its own and it converts; else
            // the item shows its hand model there too (Armageddon's draws Mesh where there is no pickup model).
            bool pickupKept = false;
            if (g.Element("pickup_static_mesh") is { } pickupField && (pickupField.ValueOf("Filename") ?? pickupField.Value).Trim() is { Length: > 0 } pickupFile)
            {
                string pickup = Path.GetFileNameWithoutExtension(pickupFile);
                var pc = pack.Find(pickup + ".csmesh_pc");
                var pg = pack.Find(pickup + ".gsmesh_pc");
                byte[]? pBytes = pc == null ? null : pack.Read(pc);
                string? pickupWhy = pc == null || pg == null ? $"its container has no {pickup}.csmesh_pc" : GuerrillaStaticMeshes.NotYet(pBytes!);
                if (pickupWhy != null) notes.Add($"item {name}: its ground model {pickup} left out ({pickupWhy}); the hand model shows there");
                else if (pickup.Equals(mesh, StringComparison.OrdinalIgnoreCase)) pickupKept = true;
                else
                {
                    var (pHeader, pTextures) = GuerrillaStaticMeshes.Convert(pBytes!, GuerrillaVehicles.Alias);
                    files.Add((pickup + ".csmesh_pc", pHeader));
                    files.Add((pickup + ".gsmesh_pc", pack.Read(pg!)));
                    foreach (string texture in pTextures)
                    {
                        var made = GuerrillaBuiltIns.Files(texture, GuerrillaVehicles.Alias(texture)) ?? Single([.. ownPegs, lowTextures], texture);
                        if (made == null) missing.Add(texture);
                        else if (!files.Any(x => x.Name.Equals(made[0].Name, StringComparison.OrdinalIgnoreCase))) files.AddRange(made);
                    }
                    pickupKept = true;
                }
            }
            AddContainer(ours, ItemContainer, files);
            notes.Add($"item {name} as {ours}: model {mesh} ({cBytes.Length:N0} → {header.Length:N0} bytes), {textures.Count - missing.Count} low-detail textures" +
                      (missing.Count > 0 ? $" ({missing.Count} not found: {string.Join(", ", missing)})" : ""));

            // The big textures and the material file that wears them (Guerrilla's <mesh>_high or <item>_high container).
            var entry = g.Copy();
            GuerrillaTableFit.Set(entry, "Name", ours);
            Editor(entry, g);
            if (!pickupKept) entry.Element("pickup_static_mesh")?.Remove();
            var flags = entry.Element("Item_Flags");
            bool wantsHigh = flags?.Elements.Any(e => e.Value.Trim().StartsWith("high_res_map", StringComparison.OrdinalIgnoreCase)) ?? false;
            if (flags != null)
                foreach (var flag in flags.Elements.Where(e => e.Value.Trim().StartsWith("high_res_map", StringComparison.OrdinalIgnoreCase)).ToList()) flag.Remove();
            var high = wantsHigh ? guerrillaItems.Find(mesh + "_high.str2_pc") ?? guerrillaItems.Find(name + "_high.str2_pc") : null;
            if (high != null)
            {
                var highPack = guerrillaItems.OpenContainer(high);
                var materialFile = highPack.Entries.FirstOrDefault(e => e.Name.EndsWith(".mat_pc", StringComparison.OrdinalIgnoreCase));
                var highPegs = highPack.Entries.Where(e => e.Name.EndsWith(".cpeg_pc", StringComparison.OrdinalIgnoreCase))
                                       .Select(e => PegFile.Read(highPack.Read(e), highPack.Read(highPack.Find(PegFile.DataFileName(e.Name)!)!))).ToList();
                if (materialFile != null)
                {
                    var (material, highTextures) = GuerrillaStaticMeshes.ConvertMaterialFile(highPack.Read(materialFile), GuerrillaVehicles.Alias);
                    var highFiles = new List<(string Name, byte[] Data)>();
                    var highMissing = new List<string>();
                    foreach (string texture in highTextures)
                    {
                        var made = GuerrillaBuiltIns.Files(texture, GuerrillaVehicles.Alias(texture)) ?? Single(highPegs, texture);
                        if (made == null) highMissing.Add(texture);
                        else if (!highFiles.Any(x => x.Name.Equals(made[0].Name, StringComparison.OrdinalIgnoreCase))) highFiles.AddRange(made);
                    }
                    highFiles.Add((materialFile.Name, material));
                    AddContainer(ours + "_high", HighContainer, highFiles);
                    (flags ?? entry.AddElement("Item_Flags")).AddElement("Flag", "high_res_map");
                    notes.Add($"item {name}: its high-detail material and {highTextures.Count - highMissing.Count} big textures in {ours}_high" +
                              (highMissing.Count > 0 ? $" ({highMissing.Count} not found: {string.Join(", ", highMissing)})" : ""));
                }
            }

            // Its entry: the touch script as Armageddon writes it (an element), Guerrilla's pickup sounds left out
            // (XACT), the streaming category Armageddon's name for it, its props ours.
            if (entry.Element("Touch_Script") is { } touch && !touch.HasElements)
            {
                string script = touch.Value.Trim();
                touch.Remove();
                if (script.Length > 0) entry.AddElement("Touch_Script").AddElement(script);
            }
            foreach (string sound in new[] { "Touch_Sound", "Touch_Sound_2", "Collision_Sound" }) entry.Element(sound)?.Remove();
            if (entry.Element("streaming_category") is { } category && category.Value.Trim().Equals("General", StringComparison.OrdinalIgnoreCase))
                category.Value = "General (preloaded)";
            itemsDone[name] = ours;  // before the props, in case one names this item
            foreach (var prop in entry.Element("Props")?.Elements.ToList() ?? [])
            {
                string propName = prop.ValueOf("Name")?.Trim() ?? "";
                if (Item(propName, notes) is { } ourProp) GuerrillaTableFit.Set(prop, "Name", ourProp);
                else { notes.Add($"item {name}: its prop {propName} left out"); prop.Remove(); }
            }
            var description = items.Description ?? throw new InvalidDataException("Armageddon's items_3d.xtbl has no description");
            var dropped = new List<string>();
            GuerrillaTableFit.Prune(entry, description.Children, "", dropped, commonFields[items]);
            GuerrillaTableFit.FitValues(entry, description.Children, "", []);
            GuerrillaTableFit.FillRequired(entry, description.Children, "", [], []);
            items.Table!.AddElement(entry);
            if (dropped.Count > 0) notes.Add($"item {name}: left out of its entry: {string.Join(", ", dropped)}");
            return ours;
        }

        /// <summary>Our inventory item for Guerrilla's <paramref name="name"/>: its entry, named as Guerrilla shows it.</summary>
        string? InventoryItem(string name, List<string> notes)
        {
            var g = rfgInventory.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (g == null) { notes.Add($"inventory item {name}: Guerrilla's items_inventory.xtbl has none"); return null; }
            string ours = Ours(name, inventory);
            var entry = g.Copy();
            GuerrillaTableFit.Set(entry, "Name", ours);
            Editor(entry, g);
            if (entry.ValueOf("Display_Name")?.Trim() is { Length: > 0 } tag && strings.TryGetValue(VintDocument.Hash(tag.ToLowerInvariant()), out var shown))
                GuerrillaTableFit.Set(entry, "Display_Name", shown);
            var description = inventory.Description ?? throw new InvalidDataException("Armageddon's items_inventory.xtbl has no description");
            GuerrillaTableFit.Prune(entry, description.Children, "", [], commonFields[inventory]);
            GuerrillaTableFit.FillRequired(entry, description.Children, "", [], []);
            inventory.Table!.AddElement(entry);
            return ours;
        }

        /// <summary>
        /// An ammo type of the weapon's own (named as the weapon): its reserve, and what Armageddon's ammo
        /// pickups give at each difficulty, from Guerrilla's ammo box refill and its difficulty table's
        /// ammo crate factors (Easy 1.5, Medium 1, Hard 0.95, Insane 0.85).
        /// </summary>
        string AmmoType(string name, int reserve, int restock)
        {
            var entry = new XtblElement("Ammo");
            entry.AddElement("Name", name);
            Editor(entry, null);
            entry.AddElement("Max_Reserve", reserve.ToString(CultureInfo.InvariantCulture));
            entry.AddElement("Max_Reserve_MP", reserve.ToString(CultureInfo.InvariantCulture));
            var tiers = entry.AddElement("Ammo_Pickup_Tiers");
            for (int t = 0; t < 3; t++)
            {
                var tier = tiers.AddElement("Ammo_Pickup_Tier");
                foreach (var (difficulty, factor) in new[] { ("Easy", 1.5), ("Medium", 1.0), ("Hard", 0.95), ("Insane", 0.85) })
                    tier.AddElement(difficulty, ((int)Math.Round(restock * factor)).ToString(CultureInfo.InvariantCulture));
            }
            ammo.Table!.AddElement(entry);
            return name;
        }

        /// <summary>
        /// What a weapon names in a table both games have (NPC firing patterns, aim drift): Armageddon's entry
        /// of the name if it is Guerrilla's alike, else Guerrilla's added under "rfg_" (or its own name, where
        /// Armageddon has none). Null if Guerrilla's table hasn't it.
        /// </summary>
        string? Support(string? name, XtblFile theirs, XtblFile ourTable, string tag, List<string> notes)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            string key = tag + ":" + name;
            if (supportDone.TryGetValue(key, out var done)) return done;
            var g = theirs.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (g == null) { notes.Add($"{tag} {name}: Guerrilla's table has none; left out"); return supportDone[key] = null; }
            if (Same(theirs, ourTable, tag, name)) return supportDone[key] = name;
            string ours = Ours(name, ourTable);
            var entry = g.Copy();
            GuerrillaTableFit.Set(entry, "Name", ours);
            Editor(entry, g);
            if (ourTable.Description is { } description)
            {
                GuerrillaTableFit.Prune(entry, description.Children, "", [], commonFields[ourTable]);
                GuerrillaTableFit.FitValues(entry, description.Children, "", []);
                GuerrillaTableFit.FillRequired(entry, description.Children, "", [], []);
            }
            ourTable.Table!.AddElement(entry);
            notes.Add($"{tag} {name}: Guerrilla's, added as {ours}");
            return supportDone[key] = ours;
        }

        /// <summary>The effect a weapon's field names, as the world's tables have it ("rfg_" + Guerrilla's), or null (noted) if the world built hasn't it.</summary>
        string? Effect(XtblElement g, string field, List<string> notes)
        {
            if (Descendant(g, field) is not { } effect) return null;
            string ours = GuerrillaEffectTables.Added(effect);
            if (worldEffects.Contains(ours)) return ours;
            notes.Add($"{field} {effect}: not in the world's effect tables yet (build the world again); left out");
            return null;
        }

        /// <summary>A container of ours with its asset list entry (Armageddon's item kind: each model type 6, each texture type 1, a material file type 9).</summary>
        void AddContainer(string name, byte type, List<(string Name, byte[] Data)> files)
        {
            byte[] container = Packfile.CreateContainer(files);
            Containers[name + ".str2_pc"] = container;
            var c = Packfile.FromBytes(name, container);
            var entry = new AsmContainer { Name = name, Type = type, Flags = 0x80, DataOffset = (uint)c.DataStart, CompressedSize = c.CompressedDataSize };
            entry.Sizes.AddRange(files.Select(f => f.Data.Length));
            foreach (var (file, data) in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".mat_pc")
                {
                    entry.Primitives.Add(new AsmPrimitive { Name = file, Type = 9, Allocator = 0, Flags = 0, SplitIndex = 0, HeaderSize = data.Length, DataSize = 0 });
                    continue;
                }
                (byte kind, byte flags) = ext switch { ".csmesh_pc" => ((byte)6, (byte)6), ".cvbm_pc" or ".cpeg_pc" => ((byte)1, (byte)4), _ => ((byte)0, (byte)0) };
                if (kind == 0) continue;
                string dataName = Path.ChangeExtension(file, ".g" + ext[2..]);
                int dataSize = files.FirstOrDefault(x => x.Name.Equals(dataName, StringComparison.OrdinalIgnoreCase)).Data?.Length ?? 0;
                entry.Primitives.Add(new AsmPrimitive { Name = file, Type = kind, Allocator = 0, Flags = flags, SplitIndex = 0, HeaderSize = data.Length, DataSize = dataSize });
            }
            ItemAssets.Containers.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            ItemAssets.Containers.Add(entry);
        }
    }

    /// <summary>A Guerrilla texture from these texture files as an Armageddon single-texture file pair under its alias (compressed mips down to 4 × 4), or null.</summary>
    static (string Name, byte[] Data)[]? Single(IReadOnlyList<PegFile> pegs, string texture)
    {
        string name = GuerrillaVehicles.Alias(texture);
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

    /// <summary>Guerrilla's campaign entry of a weapon: the one of its name outside the "MP Overrides" copies, which single player skips (is_mp_override, RM 0xBDA230).</summary>
    static XtblElement? CampaignEntry(XtblFile weapons, string name) =>
        weapons.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                            !string.Join("|", XtblFile.EntryCategory(e)).Contains("MP Overrides", StringComparison.OrdinalIgnoreCase));

    /// <summary>Guerrilla's name, or with "rfg_" before it where Armageddon's table has an entry of that name.</summary>
    static string Ours(string name, XtblFile table) =>
        table.Entries.Any(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase)) ? "rfg_" + name : name;

    /// <summary>Whether both tables' entries of the name are alike (their text without spaces and editor notes).</summary>
    static bool Same(XtblFile theirs, XtblFile ours, string tag, string name)
    {
        string? Text(XtblFile table)
        {
            var entry = table.Entries.FirstOrDefault(e => e.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) && XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            var copy = entry.Copy();
            copy.Element("_Editor")?.Remove();
            return Regex.Replace(XmlText(copy), @"\s+", "");
        }
        string? a = Text(theirs), b = Text(ours);
        return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    static XtblFile Read(Packfile pack, string name) =>
        XtblFile.FromBytes(pack.Read(pack.Find(name) ?? throw new InvalidDataException($"no {name} in {pack.Name}")));

    static IEnumerable<XtblElement> Descendants(XtblElement element)
    {
        foreach (var child in element.Elements)
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    /// <summary>The text of the first element of the name anywhere in an entry (Guerrilla groups some fields, e.g. Visuals, Sounds), or null.</summary>
    static string? Descendant(XtblElement entry, string name) =>
        Descendants(entry).FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !e.HasElements)?.Value is { Length: > 0 } v ? v : null;

    static int Int(XtblElement entry, string field) =>
        int.TryParse(entry.ValueOf(field)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>
    /// Gives an entry of ours the editor block every entry of Armageddon's has (the weapon reader requires it:
    /// "Could not find required attribute Weapon:_Editor"; it reads the category to skip multiplayer
    /// override copies, 0x74B920, as Guerrilla's does): Guerrilla's entry's own, or a category of ours.
    /// </summary>
    static void Editor(XtblElement entry, XtblElement? guerrilla)
    {
        var own = entry.Element("_Editor");
        if (own?.Element("Category") != null) return;
        own?.Remove();
        string category = guerrilla?.Element("_Editor")?.ValueOf("Category")?.Trim() is { Length: > 0 } c ? c : "Entries:Guerrilla";
        entry.AddElement("_Editor").AddElement("Category", category);
    }

    /// <summary>Copies a field of Guerrilla's entry (with its value) into ours, if it has it.</summary>
    static void Copy(XtblElement from, string field, XtblElement to)
    {
        if (from.ValueOf(field)?.Trim() is { Length: > 0 } value) to.AddElement(field, value);
    }

    /// <summary>An element's XML as written (the element must be in no file: a copy).</summary>
    static string XmlText(XtblElement element)
    {
        var document = new XtblElement("root");
        document.AddElement(element);
        return XtblXml.Write(document);
    }
}
