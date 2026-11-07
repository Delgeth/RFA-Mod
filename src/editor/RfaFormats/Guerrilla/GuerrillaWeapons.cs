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
    /// <summary>
    /// The bus Guerrilla's weapon sounds play on: Armageddon's own weapons' (the bus, in init.bnk_pc, that its
    /// weapons' actor-mixer 0x09B587DD in wep_shared.bnk_pc outputs to), so the game's sound settings take them
    /// as its own guns. It must be a bus of init.bnk_pc: a bank whose sounds name anything else (the mixer
    /// itself, as the first build did) is refused whole by Wwise at start-up ("Failed to load soundbank").
    /// </summary>
    const uint WeaponBus = 0x19F76996;

    /// <summary>The weapons' sound banks' part name and prefix (GuerrillaSoundBanks; the vehicles' are "vehicles", rfg_).</summary>
    const string SoundPart = "weapons", SoundBankPrefix = "rfgw_";

    /// <summary>
    /// The weapons Guerrilla's vehicles' turrets fire (its turrets.xtbl names them; the vehicles' build ports the turrets
    /// and names these by the same rule as <see cref="Ours"/>). Not the player's: no player animations of ours.
    /// </summary>
    public static readonly string[] TurretWeapons =
    [
        "turret_mg_vehicle", "turret_mg_flyer", "turret_mg_nogunner", "turret_mg_nogunner_quiet", "turret_dummy", "turret_artillery_gun",
        "turret_rocket_vehicle", "turret_rocket_nogunner", "turret_rocket_rover1", "turret_rocket_rover2", "turret_jetter_rocket",
        "turret_edfrocket_flyer", "turret_missilepod", "turret_missilepod2", "turret_gauss_vehicle", "turret_gauss_flyer", "turret_gaussH2H",
        "turret_supergauss_burst", "turret_HeavyTank", "turret_MediumTank", "turret_arttank_single", "turret_harpoon",
        // the second weapons of three turrets (their SecondaryWeapon): the jetter's gun, the artillery tank's burst, the gauss tank's single shot
        "turret_jetter_mg", "turret_arttank_burst", "turret_supergauss_solo",
    ];

    /// <summary>The weapons built when none are named.</summary>
    public static readonly string[] DefaultWeapons = ["m16", "enforcer", "autoblaster", "gauss_rifle", "rail_driver", "grenade", "marauder_shockade", "edf_pistol", "sniper_rifle", "shotgun", "rpg_launcher", "thermobaric_rockets",
         "charge_placer", "mine_placer", "singularity_bomb", "grinder", "arc_welder", "nano_rifle", "propane_tank",
         "sledgehammer", "sledge_upgrade_1", "sledge_upgrade_2", "gutter", .. TurretWeapons];

    /// <summary>
    /// Items Guerrilla's people carry that aren't weapons, built with every weapons build under Guerrilla's
    /// names (Armageddon's code asks for them by name): the riot shield (a character with the "riot shield"
    /// flag gets the item riot_shield, 0x84E2B1 → 0x7CF440 → equip 0x84FD90).
    /// </summary>
    public static readonly string[] PeoplesItems = ["riot_shield"];

    /// <summary>
    /// Guerrilla's items its world places for the player (GuerrillaPickups puts them in the zones), built with every
    /// weapons build: its ammo box (105 in its zones), and its weapon locker (one in each safehouse but one; it
    /// stands there: Guerrilla's safehouse weapon cabinet is the upgrades' piece).
    /// </summary>
    public static readonly string[] PickupItems = ["ammo_box", "weapon_locker"];

    /// <summary>
    /// Guerrilla's backpacks' models, worn by the player (the guerrilla_gear mod's backpacks): the jetpack Mason
    /// wears in its campaign (Kyle_JetPack, with its straps over his coat, Jetpack_straps), and the nine
    /// multiplayer packs Re-Mars-tered sells (their items in items.vpp_pc and items_mp.vpp_pc; Thrust's and
    /// Heal's static models, the others character models with their own skeletons that follow the wearer's
    /// bones, inherit_bone_transforms). Their multiplayer pickup (Touch_Script give_backpack, which Armageddon
    /// hasn't) is left out: they are worn, not lying about.
    /// Off for now (<see cref="BuildBackpackModels"/>): nothing puts them on Mason yet (Armageddon picks a
    /// character's props once, at spawn; the run-time attach isn't written), and built they would only take
    /// 2.7 MB of the item preload pool and 9 skeletons. Turn it on with that attach.
    /// </summary>
    static readonly bool BuildBackpackModels = false;

    public static readonly string[] BackpackItems =
    [
        "Kyle_JetPack", "Jetpack_straps", "multi_object_backpack_thrust", "multi_object_backpack_fleetfoot", "multi_object_backpack_stealth",
        "multi_object_backpack_vision", "multi_object_backpack_commando", "multi_object_backpack_shockwave", "multi_object_backpack_heal",
        "multi_object_backpack_rhino", "multi_object_backpack_tremor",
    ];

    /// <summary>Guerrilla's weapons its world places (GuerrillaPickups): its propane tanks and its singularity bombs' caches.</summary>
    public static readonly string[] PickupWeapons = ["propane_tank", "singularity_bomb"];

    /// <summary>
    /// Our names for the pickups (Guerrilla's name to ours, as a build names them: Guerrilla's, or "rfg_" + it where
    /// Armageddon's items_3d / weapons table has the name), for the world builder's zones.
    /// </summary>
    public static (IReadOnlyDictionary<string, string> Items, IReadOnlyDictionary<string, string> Weapons) PickupNames(string game)
    {
        var gameMisc = Packfile.Open(Path.Combine(game, "build", "pc", "cache", "misc.vpp_pc"));
        var items = Read(gameMisc, "items_3d.xtbl");
        var weapons = Read(gameMisc, "weapons.xtbl");
        return (PickupItems.ToDictionary(n => n, n => Ours(n, items), StringComparer.OrdinalIgnoreCase),
                PickupWeapons.ToDictionary(n => n, n => Ours(n, weapons), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Weapons only Guerrilla's NPCs use (Mason never throws a grenade; the user, 2026-11-06: "No, as in
    /// Guerrilla"): no player animations of ours for them, never dropped when their NPC dies ("dont drop on
    /// death"), and their item can't be picked up (no touch script).
    /// </summary>
    static readonly HashSet<string> NpcOnly = new(StringComparer.OrdinalIgnoreCase) { "grenade", "marauder_shockade" };

    /// <summary>
    /// The explosions Guerrilla's code gives the weapons whose table entry names none (its exe has the names
    /// "rocket", "thermo_rocket", "grinder_explosive", next to one another at RM 0xEDFAD0, and "remote_charge").
    /// </summary>
    static readonly Dictionary<string, string> CodeExplosions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rpg_launcher"] = "rocket", ["thermobaric_rockets"] = "thermo_rocket", ["grinder"] = "grinder_explosive", ["charge_placer"] = "remote_charge",
    };

    /// <summary>
    /// How many melee moves the game holds with the guerrilla_gear mod (which Guerrilla's weapons need): 64 (the game's
    /// own pool, built by 0x881BC0, holds 32), 22 of them Armageddon's own (its add-ons none).
    /// </summary>
    const int GameMeleeMoves = 64;

    /// <summary>
    /// Guerrilla's melee moves its campaign weapons use (the ones Armageddon reads: each weapon's standing and crouching
    /// primary move, a melee weapon's tertiary too), most needed first: those past the game's room are left out.
    /// </summary>
    static readonly string[] MeleePriority =
    [
        "sledgehammer_melee_side", "sledgehammer_melee_vert", "sledgehammer_melee_side_u1", "sledgehammer_melee_vert_u1",
        "sledgehammer_melee_side_u2", "sledgehammer_melee_vert_u2", "gutter stand sideways", "gutter stand overhead", "ar_melee", "rpg_melee",
        "remote_charge_melee", "pistol_melee", "ar_melee_shotgun", "propane_tank_melee",
    ];

    /// <summary>Guerrilla's weapon classes whose weapon fires a projectile of its own (Guerrilla's inline Projectile_Info).</summary>
    static readonly HashSet<string> ThrownKinds = new(StringComparer.OrdinalIgnoreCase)
        { "thrown", "thrown_grenade", "launcher", "thrown_charge", "thrown_mine", "singularity_bomb", "grinder", "harpoon" };

    /// <summary>The thrown weapons' projectiles' Unique_IDs, ours, fixed (Armageddon's projectiles use up to 227 and DLC 4's 250).</summary>
    static readonly Dictionary<string, int> ProjectileIds = new(StringComparer.OrdinalIgnoreCase) { ["grenade"] = 241, ["marauder_shockade"] = 242, ["rpg_launcher"] = 243, ["thermobaric_rockets"] = 244,
        ["charge_placer"] = 245, ["mine_placer"] = 246, ["singularity_bomb"] = 247, ["grinder"] = 248, ["propane_tank"] = 249,
        // The turrets' (228-240 and 251-255 were left: DLC 4's is 250).
        ["turret_rocket_vehicle"] = 228, ["turret_rocket_nogunner"] = 229, ["turret_rocket_rover1"] = 230, ["turret_rocket_rover2"] = 231,
        ["turret_jetter_rocket"] = 232, ["turret_edfrocket_flyer"] = 233, ["turret_missilepod"] = 234, ["turret_missilepod2"] = 235,
        ["turret_gauss_vehicle"] = 236, ["turret_gauss_flyer"] = 237, ["turret_gaussH2H"] = 238, ["turret_supergauss_burst"] = 239,
        ["turret_HeavyTank"] = 240, ["turret_MediumTank"] = 251, ["turret_arttank_single"] = 252, ["turret_harpoon"] = 253,
        ["turret_arttank_burst"] = 213, ["turret_supergauss_solo"] = 214 };  // (213-221 and 223-226 are free too)

    /// <summary>
    /// How many projectiles the game holds with the guerrilla_gear mod (which Guerrilla's weapons need): 256 (the
    /// game's own pool holds 100), of them Armageddon's own 88 and its add-on's 3 (DLC 4: dlc04_projectiles.xtbl);
    /// and their names' room: 8 KB with the mod (the game's 2 KB), each name and its 0, the add-on's 66 bytes.
    /// A projectile past either is dropped by the game without a word, so the builder leaves it out.
    /// </summary>
    const int GameProjectiles = 256, AddOnProjectiles = 3, ProjectileNameBytes = 0x2000, AddOnProjectileNameBytes = 66;

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
        ["m16"] = 141, ["enforcer"] = 142, ["autoblaster"] = 143, ["gauss_rifle"] = 144, ["rail_driver"] = 145, ["grenade"] = 146, ["marauder_shockade"] = 147, ["edf_pistol"] = 148, ["sniper_rifle"] = 149, ["shotgun"] = 150, ["rpg_launcher"] = 151, ["thermobaric_rockets"] = 152,
        ["charge_placer"] = 153, ["mine_placer"] = 154, ["singularity_bomb"] = 155, ["grinder"] = 156, ["arc_welder"] = 157, ["nano_rifle"] = 158,
        ["propane_tank"] = 159, ["sledgehammer"] = 160, ["sledge_upgrade_1"] = 161, ["sledge_upgrade_2"] = 162, ["gutter"] = 163,
        ["turret_mg_vehicle"] = 164, ["turret_mg_flyer"] = 165, ["turret_mg_nogunner"] = 166, ["turret_mg_nogunner_quiet"] = 167, ["turret_dummy"] = 168,
        ["turret_artillery_gun"] = 169, ["turret_rocket_vehicle"] = 170, ["turret_rocket_nogunner"] = 171, ["turret_rocket_rover1"] = 172,
        ["turret_rocket_rover2"] = 173, ["turret_jetter_rocket"] = 174, ["turret_edfrocket_flyer"] = 175, ["turret_missilepod"] = 176,
        ["turret_missilepod2"] = 177, ["turret_gauss_vehicle"] = 178, ["turret_gauss_flyer"] = 179, ["turret_gaussH2H"] = 180,
        ["turret_supergauss_burst"] = 181, ["turret_HeavyTank"] = 182, ["turret_MediumTank"] = 183, ["turret_arttank_single"] = 184, ["turret_harpoon"] = 185,
        ["turret_jetter_mg"] = 186, ["turret_arttank_burst"] = 187, ["turret_supergauss_solo"] = 188,
    };

    /// <summary>The files this writes (relative to the overrides folder, then to the mod's folder), for --remove.</summary>
    public const string ItemList = @"build\pc\cache\items.vpp_pc\items_containers.asm_pc";

    /// <summary>The character-mesh items' skeletons (new files in overrides\build\pc\cache\), listed in mods\guerrilla_gear\ for the guerrilla_vehicles mod to load.</summary>
    public const string RigList = "rigs.txt";
    public static readonly string[] TableFiles =
        ["weapons.xtbl", "items_3d.xtbl", "items_inventory.xtbl", "ammo.xtbl", "weapon_firing_patterns.xtbl", "aim_drift.xtbl", "camera_shake.xtbl", "projectiles.xtbl",
         "melee.xtbl"];

    /// <summary>Armageddon's item container types: a preloaded item's model (General (preloaded)); an item's big textures and material file.</summary>
    const byte ItemContainer = 22, HighContainer = 25;

    /// <summary>The fields of Guerrilla's weapon entries naming an effect the weapon plays.</summary>
    static readonly string[] EffectFields =
        ["Muzzle_Flash", "Muzzle_Smoke", "Tracer_Effect", "Special_Hit_Effect", "Special_Effect", "Secondary_Special_Effect", "Overheated_effect",
         "Attached_Effect", "Attached_Effect2", "Attached_Effect3", "Attached_Effect4"];  // the last two: a thrown or launched projectile's (a grenade's trail and light)

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
    /// The effects (Guerrilla's effects.xtbl names) Guerrilla's campaign weapons and its vehicles' turrets play themselves: the world
    /// builder adds these to the world's effect tables under "rfg_" names, as it does the dust it plays everywhere.
    /// </summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaMisc)
    {
        return EffectsOf(guerrillaMisc);
    }

    /// <summary>
    /// The explosions (Guerrilla's explosions.xtbl names) Guerrilla's campaign weapons' and its vehicles' turrets' shots set off (a weapon's
    /// Explosion and AIExplosion, such as the gauss rifle's): the world builder adds these to the world's
    /// explosion table under "rfg_" names, with their effects (its worldExplosions).
    /// </summary>
    public static IReadOnlyList<string> Explosions(Packfile guerrillaMisc) => ExplosionsOf(guerrillaMisc);

    /// <summary>
    /// The HUD pictures Guerrilla's weapons show (its weapons.xtbl, the campaign's and the turrets'), named as its
    /// bitmap sheets name them (in lower case: two of the table's are spelled Ui_...): each weapon's icon (its
    /// icon_name; Guerrilla's weapon select shows it), its reticle (reticule_name) and its reticle while aiming
    /// finely (fine_aim_reticule, or null). The HUD build brings them with Guerrilla's weapon select and reticles
    /// (as "rfg_" + the name), and the weapons build names each weapon's icon, so the HUD can tell its reticles
    /// by it.
    /// </summary>
    public static IReadOnlyList<(string Weapon, string Icon, string? Reticle, string? FineAimReticle)> HudPictures(Packfile guerrillaMisc)
    {
        var weapons = Read(guerrillaMisc, "weapons.xtbl");
        var pictures = new List<(string, string, string?, string?)>();
        string? Picture(XtblElement entry, string field) => entry.ValueOf(field)?.Trim() is { Length: > 0 } v ? v.ToLowerInvariant() : null;
        foreach (string name in CampaignWeapons.Concat(TurretWeapons))
            if (CampaignEntry(weapons, name) is { } entry && Picture(entry, "icon_name") is { } icon)
                pictures.Add((name, icon, Picture(entry, "reticule_name"), Picture(entry, "fine_aim_reticule")));
        return pictures;
    }

    static IReadOnlyList<string> ExplosionsOf(Packfile guerrillaMisc)
    {
        var weapons = Read(guerrillaMisc, "weapons.xtbl");
        var explosions = new List<string>();
        foreach (string name in CampaignWeapons.Concat(TurretWeapons))
            if (CampaignEntry(weapons, name) is { } entry)
                foreach (var element in Descendants(entry).Where(e => e.Name.Equals("Explosion", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("AIExplosion", StringComparison.OrdinalIgnoreCase)))
                    if (element.Value is { Length: > 0 } explosion && !explosions.Contains(explosion, StringComparer.OrdinalIgnoreCase)) explosions.Add(explosion);
        return explosions;
    }

    static IReadOnlyList<string> EffectsOf(Packfile guerrillaMisc)
    {
        var weapons = Read(guerrillaMisc, "weapons.xtbl");
        var effects = new List<string>();
        foreach (string name in CampaignWeapons.Concat(TurretWeapons))
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
        foreach (string name in PeoplesItems)
            foreach (string line in b.PeoplesItem(name)) yield return line;
        foreach (string name in PickupItems)
            foreach (string line in b.PickupItem(name)) yield return line;
        if (BuildBackpackModels)
            foreach (string name in BackpackItems)
                foreach (string line in b.BackpackItem(name)) yield return line;
        // Their sounds: Guerrilla's cues as banks of Armageddon's kind (rfgw_<Guerrilla's bank>), listed for the game.
        foreach (string line in b.Sounds.Log.Distinct()) yield return $"sounds: {line}";
        foreach (string line in b.Sounds.Write(game, overrides, modFolder)) yield return $"sounds: {line}";

        string cache = Path.Combine(overrides, "build", "pc", "cache");
        Directory.CreateDirectory(cache);
        foreach (var (file, bytes) in b.Containers) File.WriteAllBytes(Path.Combine(cache, file), bytes);
        // The character-mesh items' skeletons: new files the guerrilla_vehicles mod has the game load with its own
        // at start-up (it reads mods\guerrilla_gear\rigs.txt beside its own list); an earlier build's that aren't
        // made now go.
        string rigList = Path.Combine(GearFolder(modFolder), RigList);
        foreach (string old in File.Exists(rigList) ? File.ReadAllLines(rigList).Select(l => l.Trim()) : [])
            if (old.Length > 0 && !b.Rigs.ContainsKey(old) && old.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(Path.Combine(cache, old)))
                File.Delete(Path.Combine(cache, old));
        foreach (var (file, bytes) in b.Rigs) File.WriteAllBytes(Path.Combine(cache, file), bytes);
        Directory.CreateDirectory(GearFolder(modFolder));
        File.WriteAllLines(rigList, b.Rigs.Keys.Order(StringComparer.OrdinalIgnoreCase));
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
        // Guerrilla's upgrades (guerrilla_gear's F7 window), naming the weapons as this build did.
        var armageddonWeapons = Read(Packfile.Open(Path.Combine(game, "build", "pc", "cache", "misc.vpp_pc")), "weapons.xtbl");
        var made = b.Tables.First(t => t.File == "weapons.xtbl").Table.Entries.Select(XtblFile.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var guerrillaMisc = Packfile.Open(Path.Combine(guerrilla, "build", "pc", "cache", "misc.vpp_pc"));
        yield return GuerrillaUpgrades.Write(guerrillaMisc, name => Ours(name, armageddonWeapons) is { } ours && made.Contains(ours) ? ours : null, GearFolder(modFolder));
        // Its backpacks' numbers (guerrilla_gear's backpacks).
        yield return GuerrillaUpgrades.WriteBackpacks(guerrillaMisc, GearFolder(modFolder));
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
        foreach (string line in GuerrillaSoundBanks.Remove(game, overrides, modFolder, SoundPart)) yield return line;
        string rigList = Path.Combine(GearFolder(modFolder), RigList);
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
        string ammoPickup = Path.Combine(GearFolder(modFolder), AmmoPickupFile);
        if (File.Exists(ammoPickup)) { File.Delete(ammoPickup); yield return $"Deleted {ammoPickup}"; }
    }

    /// <summary>The work of one Build: both games' files, the tables being written and what has been made.</summary>
    sealed class Builder
    {
        readonly Packfile guerrillaMisc, guerrillaItems, gameItems, gameMisc;
        readonly Packfile? guerrillaItemsMp;  // items_mp.vpp_pc: the multiplayer's items (six of the backpacks)
        readonly XtblFile rfgWeapons, rfgItems, rfgInventory, rfgPatterns, rfgDrift, rfgShakes, gameShakes;
        readonly XtblFile weapons, items, inventory, ammo, patterns, drift, projectiles, meleeMoves, rfgMelee;
        readonly HashSet<string> meleeRoom;  // Guerrilla's melee moves that fit in the game's table
        readonly HashSet<string> worldExplosions;
        readonly Dictionary<XtblFile, IReadOnlySet<string>> commonFields = [];  // each table's fields all of Armageddon's entries have
        readonly Dictionary<uint, string> strings;
        readonly HashSet<string> worldEffects;
        readonly PegFile lowTextures;
        readonly List<PegFile> lowPegs;  // the items' shared low-detail textures: items.vpp_pc's, and items_mp.vpp_pc's (mp_items_containers)
        readonly Dictionary<string, string> itemsDone = new(StringComparer.OrdinalIgnoreCase);  // Guerrilla's item → ours
        readonly HashSet<string> heldItems = new(StringComparer.OrdinalIgnoreCase);  // items a weapon is held by (they need a weapon_handle tag)
        readonly Dictionary<string, string?> supportDone = new(StringComparer.OrdinalIgnoreCase); // "table:name" → ours (null: none)

        public readonly Dictionary<string, byte[]> Containers = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, byte[]> Rigs = new(StringComparer.OrdinalIgnoreCase);  // the character-mesh items' skeletons (x.rig_pc)
        public readonly AsmFile ItemAssets;
        public readonly GuerrillaSoundBanks Sounds;
        public readonly List<string> Built = [];
        public readonly List<GuerrillaPlayerAnimations.PlayerWeapon> PlayerWeapons = [];  // the guns built and their animation groups
        public IEnumerable<(string File, XtblFile Table)> Tables =>
            [("weapons.xtbl", weapons), ("items_3d.xtbl", items), ("items_inventory.xtbl", inventory), ("ammo.xtbl", ammo),
             ("weapon_firing_patterns.xtbl", patterns), ("aim_drift.xtbl", drift), ("camera_shake.xtbl", gameShakes), ("projectiles.xtbl", projectiles),
             ("melee.xtbl", meleeMoves)];

        public Builder(string guerrilla, string game, string modFolder)
        {
            string guerrillaCache = Path.Combine(guerrilla, "build", "pc", "cache"), gameCache = Path.Combine(game, "build", "pc", "cache");
            guerrillaMisc = Packfile.Open(Path.Combine(guerrillaCache, "misc.vpp_pc"));
            guerrillaItems = Packfile.Open(Path.Combine(guerrillaCache, "items.vpp_pc"));
            guerrillaItemsMp = File.Exists(Path.Combine(guerrillaCache, "items_mp.vpp_pc")) ? Packfile.Open(Path.Combine(guerrillaCache, "items_mp.vpp_pc")) : null;
            gameMisc = Packfile.Open(Path.Combine(gameCache, "misc.vpp_pc"));
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
            projectiles = Read(gameMisc, "projectiles.xtbl");
            meleeMoves = Read(gameMisc, "melee.xtbl");
            rfgMelee = Read(guerrillaMisc, "melee.xtbl");
            meleeRoom = MeleePriority.Take(Math.Max(0, GameMeleeMoves - meleeMoves.Entries.Count())).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            foreach (var table in new[] { weapons, items, inventory, ammo, patterns, drift, gameShakes, projectiles, meleeMoves }) commonFields[table] = GuerrillaTableFit.CommonFields(table);
            byte[] ReadItems(string file) => guerrillaItems.Read(guerrillaItems.Find(file) ?? throw new InvalidDataException($"no {file} in Guerrilla's items.vpp_pc"));
            lowTextures = PegFile.Read(ReadItems("items_containers.cpeg_pc"), ReadItems("items_containers.gpeg_pc"));
            lowPegs = [lowTextures];
            if (guerrillaItemsMp?.Find("mp_items_containers.cpeg_pc") is { } mpHeader && guerrillaItemsMp.Find("mp_items_containers.gpeg_pc") is { } mpData)
                lowPegs.Add(PegFile.Read(guerrillaItemsMp.Read(mpHeader), guerrillaItemsMp.Read(mpData)));
            ItemAssets = AsmFile.Read(gameItems.Read(gameItems.Find("items_containers.asm_pc") ?? throw new InvalidDataException("no items_containers.asm_pc in Armageddon's items.vpp_pc")));
            Sounds = new GuerrillaSoundBanks(guerrillaCache, guerrillaMisc, SoundPart, SoundBankPrefix);
        }

        /// <summary>One weapon: its item, inventory item, ammo and entry.</summary>
        public IEnumerable<string> Weapon(string name)
        {
            if (CampaignEntry(rfgWeapons, name) is not { } g) { yield return $"{name}: Guerrilla's weapons.xtbl has no such weapon (outside its multiplayer copies)"; yield break; }
            if (!UniqueIds.TryGetValue(name, out int id)) { yield return $"{name}: has no Unique_ID of ours yet (add it to GuerrillaWeapons.UniqueIds); left out"; yield break; }
            string kind = g.ValueOf("Weapon_Class")?.Trim() ?? "";
            // Thrown and launched weapons fire a projectile of their own; the nano rifle shoots bullets (its nano cloud is
            // the bullet's explosion), the arc welder an electric beam.
            // (A launcher without a projectile of its own, the artillery gun's blanks, shoots as a gun: its explosion where it hits.)
            bool thrown = ThrownKinds.Contains(kind) && g.Element("Projectile_Info") != null, melee = kind.StartsWith("melee", StringComparison.OrdinalIgnoreCase);
            if (!kind.Equals("bullet", StringComparison.OrdinalIgnoreCase) && !kind.Equals("nano_rifle", StringComparison.OrdinalIgnoreCase) &&
                !kind.Equals("arc_welder", StringComparison.OrdinalIgnoreCase) && !kind.Equals("launcher", StringComparison.OrdinalIgnoreCase) && !thrown && !melee)
            { yield return $"{name}: a {kind} weapon; not converted yet"; yield break; }
            if (thrown && projectiles.Entries.Count() + AddOnProjectiles >= GameProjectiles)
            {
                yield return $"{name}: left out: the game's projectile table is full (it holds {GameProjectiles}: Armageddon's own, its add-on's and " +
                             "the Guerrilla weapons' built before this one fill it)";
                yield break;
            }
            string ours = Ours(name, weapons);
            var notes = new List<string>();

            heldItems.Add(g.ValueOf("Item_3d")?.Trim() ?? "");
            string? item = Item(g.ValueOf("Item_3d")?.Trim() ?? "", notes);
            if (item != null && NpcOnly.Contains(name))
                items.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(item, StringComparison.OrdinalIgnoreCase))?.Element("Touch_Script")?.Remove();
            string? inventoryItem = item == null ? null : InventoryItem(g.ValueOf("Inventory_Item")?.Trim() ?? "", notes);
            foreach (string note in notes) yield return $"{name}: {note}";
            notes.Clear();
            if (item == null || inventoryItem == null) { yield return $"{name}: left out (its item or inventory item couldn't be made)"; yield break; }

            // A thrown weapon's projectile (Guerrilla's inline Projectile_Info), an entry of Armageddon's projectile table.
            string? projectile = null;
            if (thrown)
            {
                projectile = Projectile(g, name, notes);
                foreach (string note in notes) yield return $"{name}: {note}";
                notes.Clear();
                if (projectile == null) { yield return $"{name}: left out (its projectile couldn't be made)"; yield break; }
            }

            int magazine = Int(g, "Magazine_Size"), maxRounds = Int(g, "Max_Rounds"), restock = Int(g, "Ammo_Box_Restock"), magazines = Int(g, "Num_Magazines");
            // A melee weapon (the sledgehammer, the gutter) uses no ammo (as Armageddon's own sledgehammer).
            string? ammoType = melee ? null : AmmoType(Ours(ours, ammo), Math.Max(0, maxRounds - magazine), restock);
            string? pattern = Support(g.ValueOf("NPC_Firing_Pattern"), rfgPatterns, patterns, "Firing_Pattern", notes);
            string? aimDrift = Support(g.ValueOf("NPC_Aim_Drift"), rfgDrift, drift, "Profile", notes);
            var entry = WeaponEntry(g, ours, id, item, inventoryItem, ammoType, Math.Max(0, magazines - 1) * magazine, pattern, aimDrift, projectile, notes);
            var description = weapons.Description ?? throw new InvalidDataException("Armageddon's weapons.xtbl has no description");
            var dropped = new List<string>();
            var added = new List<string>();
            var unfilled = new List<string>();
            GuerrillaTableFit.Prune(entry, description.Children, "", dropped, commonFields[weapons]);
            GuerrillaTableFit.FitValues(entry, description.Children, "", []);
            GuerrillaTableFit.FillRequired(entry, description.Children, "", added, unfilled);
            weapons.Table!.AddElement(entry);
            if (g.ValueOf("Animation_Group")?.Trim() is { Length: > 0 } group && !NpcOnly.Contains(name) && !TurretWeapons.Contains(name, StringComparer.OrdinalIgnoreCase))
                PlayerWeapons.Add(new(ours, group));
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
        XtblElement WeaponEntry(XtblElement g, string name, int id, string item, string inventoryItem, string? ammoType, int pickupRounds,
                                string? pattern, string? aimDrift, string? projectile, List<string> notes)
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
            // Armageddon's code reads 15 of Guerrilla's 17 weapon classes, not grinder nor arc_welder: those two as
            // Armageddon's own weapons of their kind are (its lightning gun, its launchers), launcher.
            string weaponClass = G("Weapon_Class") ?? "bullet";
            w.AddElement("Weapon_Class", weaponClass.Equals("grinder", StringComparison.OrdinalIgnoreCase) || weaponClass.Equals("arc_welder", StringComparison.OrdinalIgnoreCase)
                                          ? "launcher" : weaponClass);
            string group = G("Animation_Group") ?? "Default";
            w.AddElement("Animation_Group", group.Equals("Nano", StringComparison.OrdinalIgnoreCase) ? "Nano Rifle" : group);
            Copy(g, "Player_Move_Speed", w);
            Copy(g, "NPC_Move_Speed", w);
            Copy(g, "Zoom_Magnification", w);
            if (G("Alert_Level_Multiplier") is { } alert) w.AddElement("Alert_Multiplier", alert);
            var flags = w.AddElement("Flags");
            foreach (string flag in guerrillaFlags.Where(f => SameWeaponFlags.Contains(f, StringComparer.OrdinalIgnoreCase))) flags.AddElement("Flag", flag);
            if (NpcOnly.Contains(XtblFile.EntryName(g)) && !guerrillaFlags.Contains("dont drop on death", StringComparer.OrdinalIgnoreCase)) flags.AddElement("Flag", "dont drop on death");
            // Guerrilla gives the rail driver's scope its own HUD module, which shows people through walls
            // (human::set_equipped_inv_item, RM 0xAB4D20, by the weapon): Armageddon's "xray zoom", as its own rail driver has.
            if (XtblFile.EntryName(g).Equals("rail_driver", StringComparison.OrdinalIgnoreCase)) flags.AddElement("Flag", "xray zoom");
            w.AddElement("Sound_Data");
            bool melee = weaponClass.StartsWith("melee", StringComparison.OrdinalIgnoreCase);
            string? Attack(string field) => MeleeMove(g.Element("MeleeAttacks")?.ValueOf(field), notes);
            // Where the weapon goes when put away (the hammer on Mason's back).
            if (Descendant(g, "Attachment_prop") is { } holster) w.AddElement("Attachment_prop", holster);
            if (!melee && Attack("StandingPrimary") is { } bash)
            {
                // The melee control with this weapon in hand (Armageddon keeps Guerrilla's standing and crouching ones).
                var attacks = w.AddElement("MeleeAttacks");
                attacks.AddElement("StandingPrimary", bash);
                attacks.AddElement("CrouchingPrimary", Attack("CrouchingPrimary") ?? bash);
            }

            bool charge = (G("Weapon_Class") ?? "").Equals("thrown_charge", StringComparison.OrdinalIgnoreCase);
            bool arc = (G("Weapon_Class") ?? "").Equals("arc_welder", StringComparison.OrdinalIgnoreCase);
            var slots = w.AddElement("Firing_Mode_Slots");
            var mode = slots.AddElement("Firing_Mode_Slot").AddElement("Potential_Firing_Modes").AddElement("Potential_Firing_Mode");
            mode.AddElement("Name", "Default");
            var f = mode.AddElement("Firing_Mode");

            // What a shot is: a bullet (one through people and thin walls with Guerrilla's flag), its tracers.
            var shot = f.AddElement("Shot_Type");
            if (projectile != null)
            {
                // A thrown weapon throws its projectile as Armageddon's own throwables (flare_throw, nano_grenade)
                // launch theirs: Guerrilla's start speed, its projectile's own fuse and explosion.
                var launcher = shot.AddElement("Launcher");
                launcher.AddElement("Projectile", projectile);
                launcher.AddElement("Launch_Speed", Descendant(g, "Start_Speed") ?? Descendant(g, "Max_Speed") ?? "20");
                launcher.AddElement("Delete_Old_Projectiles", "False");
                launcher.AddElement("Force_Muzzle_Orient", "False");
                launcher.AddElement("Launch_To_Aim_Position", "True");
                launcher.AddElement("Autoaim_With_Lockon", "False");
                // Guerrilla lets Mason have only so many remote charges out (can_fire_charge, RM 0xA953B0: none thrown
                // past upgrade_get_max_charges, its upgrade remote_charge_count's level, 2 to start with).
                if (charge) launcher.AddElement("Max_Simultaneous_Projectiles", MaxCharges().ToString(CultureInfo.InvariantCulture));
                // Its spin as it leaves the hand (the same units in both games: the propane tank's X 20 in each).
                if (g.Element("Projectile_Info")?.Element("Angular_Velocity") is { } spin &&
                    new[] { "X", "Y", "Z" }.Any(a => double.TryParse(spin.ValueOf(a)?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v != 0))
                {
                    var ourSpin = launcher.AddElement("Angular_Velocity");
                    foreach (string axis in new[] { "X", "Y", "Z" }) ourSpin.AddElement(axis, spin.ValueOf(axis)?.Trim() is { Length: > 0 } v ? v : "0.0");
                }
            }
            else if (arc) ElectricBeam(shot, g, notes);
            else if (melee)
            {
                // A melee weapon's attack is a swing (Armageddon's own sledgehammer: its side swing first, its overhead
                // second): Guerrilla's primary standing and crouching moves.
                var swing = shot.AddElement("Melee");
                if (Attack("StandingPrimary") is { } standing)
                {
                    swing.AddElement("Standing", standing);
                    swing.AddElement("Crouching", Attack("CrouchingPrimary") ?? standing);
                }
                else notes.Add("its swing: its melee move couldn't be made; it won't swing");
            }
            else if (guerrillaFlags.Contains("penetrating bullets", StringComparer.OrdinalIgnoreCase))
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
            string trigger = (G("Trigger_Type") ?? "single").ToLowerInvariant();
            if (arc) f.AddElement("Trigger_Type").AddElement("Continuous").AddElement("Round_Discharge_Time_ms", G("Default_Refire_Delay") ?? "100");
            else f.AddElement("Trigger_Type").AddElement(trigger == "automatic" ? "Automatic" : "Single");
            // Guerrilla's grinder spins up while its trigger is held (weapon::charge_grinder, RM 0xBE4940); Armageddon has no such trigger.
            if (trigger == "charge") notes.Add("its trigger: Guerrilla's spins the weapon up while held; Armageddon has no such trigger: one shot a pull until its code is ported");
            f.AddElement("Barrel_Behavior").AddElement("Single").AddElement("Index", "0");
            Copy(g, "Shots_Per_Round", f);
            if (ammoType == null)
            {
                f.AddElement("Magazine_Size", "0");
                f.AddElement("Ammo_Per_Round", "0");
            }
            else
            {
                Copy(g, "Magazine_Size", f);
                f.AddElement("Ammo_Type", ammoType);
                f.AddElement("Ammo_Per_Round", "1");
                if (G("Ammo_Box_Restock") is { } restock) f.AddElement("Rounds_From_Ammo_Box", restock);
                f.AddElement("Rounds_On_Pickup", pickupRounds.ToString(CultureInfo.InvariantCulture));
            }
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
                    // (Guerrilla's arc welder's electricity passes no damage to buildings.)
                    ours.AddElement("Structural_Damage", arc ? "0" : damage.ValueOf("NPC_Damage")?.Trim() ?? "0");
                }
            Copy(g, "Headshot_Multiplier", f);
            foreach (var (field, ourField) in new[] { ("Explosion", "Explosion"), ("AIExplosion", "AI_Explosion") })
                if (projectile == null && G(field) is { } explosion)
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
            // Its shot, the player's and an NPC's (Guerrilla's Fire_Sound and first NPC_Fire_Sound cues), each Guerrilla's
            // own wave, volume and distances.
            if (Descendant(g, "Fire_Sound") is { } fire && FireSound(fire) is { } fireEvent) sounds.AddElement("sound_event", fireEvent);
            if (Descendant(g, "NPC_Fire_Sound") is { } npcFire && FireSound(npcFire) is { } npcEvent) sounds.AddElement("Npc_sound_event", npcEvent);
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
            // Its picture in the weapon select: Guerrilla's own, as the HUD build brings it (Guerrilla's weapon select shows
            // the picture the game names here; the reticles go by it too: GuerrillaHud).
            if (G("icon_name") is { } icon) f.AddElement("Icon_Name", GuerrillaHud.Prefix + icon.ToLowerInvariant());
            var modeFlags = f.AddElement("Flags");
            foreach (string flag in guerrillaFlags)
                if (FiringModeFlags.TryGetValue(flag, out var ourFlag)) modeFlags.AddElement("Flag", ourFlag);
            if (charge)
            {
                // How Armageddon's engine runs a detonator (its own charge_placer's firing mode): the charge in hand
                // never needs reloading, and the reload control sets the charges off as the alt fire does.
                modeFlags.AddElement("Flag", "keep magazine filled");
                modeFlags.AddElement("Flag", "reload is remote trigger");
                RemoteSlot(slots, pattern);
            }
            // A melee weapon's second swing (Guerrilla's tertiary move: the hammer's overhead), its alt fire.
            if (melee && Attack("StandingTertiary") is { } second) SwingSlot(slots, second, Attack("CrouchingTertiary") ?? second, pattern);

            notes.Add("not yet: its other sounds (reloading, dry fire, its projectile's), its melee hits' effects (Guerrilla's melee group " + (G("Melee_Group") ?? "none") + "), " +
                      "its bullets' impact effects (Guerrilla's bullet group " + (G("Bullet_Group") ?? "Standard") + "; Armageddon's default group's play)");
            foreach (string field in new[] { "Special_Effect", "Secondary_Special_Effect", "Special_Hit_Effect" })
                if (!(arc && field == "Special_Effect") && Descendant(g, field) is { } special) notes.Add($"{field} {special}: Armageddon's firing modes have no such field; left out until its code is ported");
            return w;
        }

        /// <summary>
        /// Guerrilla's arc welder as Armageddon's electric beam (its own lightning gun's shot, which plays Guerrilla's
        /// lightning effect): Guerrilla's arcs to the people in a cone in front of it (Fire_Cone_Angle) out to its
        /// range, two of them (a third with an upgrade; weapon::process_arc_welder, RM 0xBE96E0); Armageddon's beam
        /// fixes on someone near the aim line and jumps from him to others near him.
        /// </summary>
        void ElectricBeam(XtblElement shot, XtblElement g, List<string> notes)
        {
            double D(string field, double fallback) =>
                double.TryParse(Descendant(g, field), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
            string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            var beam = shot.AddElement("Electric_Beam");
            if (Effect(g, "Special_Effect", notes) is { } lightning)
            {
                beam.AddElement("Beam_Effect", lightning);
                beam.AddElement("Arc_Effect", lightning);
            }
            double halfCone = D("Fire_Cone_Angle", 50) / 2, range = D("Range_Max", 10);
            double radius = range / 2 * Math.Tan(halfCone * Math.PI / 180);
            beam.AddElement("Beam_Attach_Radius", N(radius));      // the cone's width halfway out
            beam.AddElement("Beam_Attach_Max_Angle", N(halfCone));  // it lets go of him outside the cone
            beam.AddElement("Maximum_Jumps", "1");                   // two people, as Guerrilla's two arcs
            beam.AddElement("Jump_Radius", N(range));
            beam.AddElement("Arcs_Per_Jump", "1");
            beam.AddElement("Blocked_By_Terrain_Only", "False");
            notes.Add($"its arcs: Armageddon's electric beam, fixing on someone within {N(radius)} m of the aim line (Guerrilla's {N(halfCone * 2)} degree cone halfway " +
                      $"out) and jumping to one more within {N(range)} m (Guerrilla's two arcs, each from the gun); the third arc (an upgrade), its hit and " +
                      "electrocution effects: not yet");
        }

        /// <summary>
        /// A weapon's firing sound: a looping cue (an automatic gun's: Guerrilla's m16 plays WEP_ASSAULT_PC_LOOP while the
        /// trigger is held) as a loop with its stop event, paired in the game's loop list as Armageddon's own automatic
        /// guns' are (its WEP_ENFORCER_PC: the game posts the pair's stop when the firing stops); any other, a one-shot.
        /// </summary>
        string? FireSound(string cue) =>
            Sounds.Sounds.FindCue(cue) is { LoopCount: not 0 } ? Sounds.Loop(cue, WeaponBus) : Sounds.OneShot(cue, WeaponBus);

        /// <summary>A melee weapon's second swing, a second firing mode (as Armageddon's own sledgehammer's overhead).</summary>
        static void SwingSlot(XtblElement slots, string standing, string crouching, string? pattern)
        {
            var mode = slots.AddElement("Firing_Mode_Slot").AddElement("Potential_Firing_Modes").AddElement("Potential_Firing_Mode");
            mode.AddElement("Name", "Default");
            var f = mode.AddElement("Firing_Mode");
            var swing = f.AddElement("Shot_Type").AddElement("Melee");
            swing.AddElement("Standing", standing);
            swing.AddElement("Crouching", crouching);
            f.AddElement("Trigger_Type").AddElement("Single");
            f.AddElement("Barrel_Behavior").AddElement("Single").AddElement("Index", "0");
            f.AddElement("Magazine_Size", "0");
            f.AddElement("Ammo_Per_Round", "0");
            if (pattern != null) f.AddElement("NPC_Firing_Pattern", pattern);
            f.AddElement("Desired_Interface_Position", "none");
        }

        /// <summary>
        /// Our melee move for Guerrilla's <paramref name="name"/> (its melee.xtbl): Guerrilla's, under "rfg_" (every one
        /// differs from Armageddon's: Armageddon's hammer breaks buildings with 150,000, Guerrilla's 1,500, and its moves
        /// name Armageddon's sets), each set's data for our sets (<see cref="OurSets"/>). Only <see cref="MeleeRoom"/> fit
        /// in the game's table; the rest are left out (noted). Null if it can't be made.
        /// </summary>
        string? MeleeMove(string? name, List<string> notes)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            string key = "MeleeMove:" + name;
            if (supportDone.TryGetValue(key, out var done)) return done;
            var g = rfgMelee.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (g == null) { notes.Add($"melee move {name}: Guerrilla's melee.xtbl has none"); return supportDone[key] = null; }
            if (!meleeRoom.Contains(name))
            {
                notes.Add($"melee move {name}: left out, the game's melee table is full (it holds {GameMeleeMoves}; Armageddon's own and " +
                          $"{meleeRoom.Count} of Guerrilla's fill it)");
                return supportDone[key] = null;
            }
            string ours = Ours(name, meleeMoves);
            var entry = g.Copy();
            GuerrillaTableFit.Set(entry, "Name", ours);
            Editor(entry, g);
            if (entry.Element("MoveDataList") is { } list)
                foreach (var data in list.Elements.ToList())
                {
                    if (data.ValueOf("CameraShake")?.Trim() is { Length: > 0 } shake)
                    {
                        if (Support(shake, rfgShakes, gameShakes, "Camera_Shake", notes) is { } ourShake) GuerrillaTableFit.Set(data, "CameraShake", ourShake);
                        else data.Element("CameraShake")?.Remove();
                    }
                    string[] sets = OurSets(data.ValueOf("AnimSetName")?.Trim() ?? "");
                    GuerrillaTableFit.Set(data, "AnimSetName", sets[0]);
                    foreach (string other in sets.Skip(1))
                    {
                        var copy = data.Copy();
                        GuerrillaTableFit.Set(copy, "AnimSetName", other);
                        list.AddElement(copy);
                    }
                }
            // Guerrilla's flag "always knock down" (anyone hit) is Armageddon's two, for NPCs and for the player.
            foreach (var flags in Descendants(entry).Where(e => e.Name.Equals("Flags", StringComparison.OrdinalIgnoreCase)).ToList())
                foreach (var flag in flags.Elements.Where(f => f.Value.Trim().Equals("always knock down", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    flag.Value = "always knock down npc";
                    if (!flags.Elements.Any(f => f.Value.Trim().Equals("always knock down player", StringComparison.OrdinalIgnoreCase)))
                        flags.AddElement("Flag", "always knock down player");
                }
            var description = meleeMoves.Description ?? throw new InvalidDataException("Armageddon's melee.xtbl has no description");
            var dropped = new List<string>();
            GuerrillaTableFit.Prune(entry, description.Children, "", dropped, commonFields[meleeMoves]);
            GuerrillaTableFit.FitValues(entry, description.Children, "", []);
            GuerrillaTableFit.FillRequired(entry, description.Children, "", [], []);
            meleeMoves.Table!.AddElement(entry);
            notes.Add($"melee move {name}: Guerrilla's, as {ours}" + (dropped.Count > 0 ? $" (left out: {string.Join(", ", dropped.Distinct())})" : ""));
            return supportDone[key] = ours;
        }

        /// <summary>
        /// The animation sets a melee move's data for one of Guerrilla's sets goes to: its player's (Player.xtbl) to
        /// Mason's (rfg_mason), Darius's with Guerrilla's guns (rfg_player) and Darius's own (vs_player); its people's
        /// to theirs as the people's port names them ("rfg_" + Guerrilla's file: rfg_male.xtbl, ...), the men's also
        /// to Armageddon's own people (npc_table) for its NPCs carrying Guerrilla's weapons.
        /// </summary>
        static string[] OurSets(string set) =>
            set.Equals("Player.xtbl", StringComparison.OrdinalIgnoreCase) ? [GuerrillaPlayerAnimations.MasonSetFile, GuerrillaPlayerAnimations.SetFile, "vs_player.xtbl"]
            : set.Equals("Male.xtbl", StringComparison.OrdinalIgnoreCase) ? ["rfg_male.xtbl", "npc_table.xtbl"]
            : ["rfg_" + set.ToLowerInvariant()];

        /// <summary>
        /// The detonator of the remote charges, a second firing mode (Guerrilla's alt fire): every charge out goes off,
        /// one after another 250 ms apart, as Guerrilla's detonate_charges (RM 0xAC55D0) sets them (its stagger, RM
        /// 0x1694728). Armageddon's Remote shot does this itself (Type All, Delay_ms between the explosions).
        /// </summary>
        static void RemoteSlot(XtblElement slots, string? pattern)
        {
            var mode = slots.AddElement("Firing_Mode_Slot").AddElement("Potential_Firing_Modes").AddElement("Potential_Firing_Mode");
            mode.AddElement("Name", "Default");
            var f = mode.AddElement("Firing_Mode");
            var remote = f.AddElement("Shot_Type").AddElement("Remote");
            remote.AddElement("Type", "All");
            remote.AddElement("Delay_ms", "250");
            f.AddElement("Trigger_Type").AddElement("Single");
            f.AddElement("Barrel_Behavior").AddElement("Single").AddElement("Index", "0");
            f.AddElement("Magazine_Size", "0");
            f.AddElement("Ammo_Per_Round", "0");
            if (pattern != null) f.AddElement("NPC_Firing_Pattern", pattern);  // the weapon's own (Armageddon requires one)
            f.AddElement("Desired_Interface_Position", "none");  // the HUD shows the charges once, by the first mode
            f.AddElement("Flags").AddElement("Flag", "non-fixed");
        }

        /// <summary>How many remote charges Mason may have out to start with: Guerrilla's upgrade remote_charge_count, its first level.</summary>
        int MaxCharges()
        {
            var upgrade = Read(guerrillaMisc, "upgrades.xtbl").Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals("remote_charge_count", StringComparison.OrdinalIgnoreCase));
            var level = upgrade?.Element("Levels")?.Elements.FirstOrDefault();
            return int.TryParse(level?.ValueOf("Value")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 2;
        }

        /// <summary>A value of Guerrilla's tweak table (tweak_table.xtbl), or the fallback.</summary>
        string Tweak(string name, string fallback) =>
            Read(guerrillaMisc, "tweak_table.xtbl").Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.ValueOf("Value")?.Trim() is { Length: > 0 } v ? v : fallback;

        /// <summary>A stage of a projectile's stage branch, as Armageddon writes them.</summary>
        static XtblElement Stage(XtblElement stages, string description, string type, string gravity = "1.0")
        {
            var s = stages.AddElement("Stage");
            s.AddElement("Description", description);
            s.AddElement("Type").AddElement(type);
            s.AddElement("Gravity_Multiplier", gravity);
            s.AddElement("Triggers");
            s.AddElement("Drag", "0.0");
            return s;
        }

        /// <summary>An effect a stage plays as long as it lasts, on the projectile.</summary>
        static void StageEffect(XtblElement stage, string effect, string stop = "Stop Looping", string? tag = null, bool looping = true)
        {
            var e = (stage.Element("Effects") ?? stage.AddElement("Effects")).AddElement("Effect");
            e.AddElement("Effect_Name", effect);
            if (tag != null) e.AddElement("Mesh_Tag_Name", tag);
            e.AddElement("Looping", looping ? "True" : "False");
            e.AddElement("Parented", "True");
            e.AddElement("Stop_Type", stop);
            e.AddElement("Check_For_Obstruction", "False");
        }

        /// <summary>
        /// A remote charge's or a proximity mine's way (Guerrilla's thrown_charge, thrown_mine): it sticks where it lands
        /// (Guerrilla's "attach on impact" / "attach to ground": Armageddon's Attach collide behaviour), stays there with
        /// no lifetime, and goes off when set off. A charge: by the detonator (Armageddon's Remote trigger), also before it
        /// has stuck (Guerrilla's charges go off in the air too), its light on once stuck (Attached_Effect2, "effect should
        /// start once attached"). A mine: armed after Guerrilla's Proxy_mine_activation_delay_ms, its light on, then set off
        /// by anyone within Proxy_mine_radius but its owner's team (Guerrilla's process_mine, RM 0xA71B10, skips only its owner
        /// and Mason's squad; smart mines, an upgrade, all but enemies), going off Proxy_mine_detonation_delay_ms later.
        /// </summary>
        void StickyStages(XtblElement p, XtblElement g, string kind, List<string> notes)
        {
            bool mine = kind.Equals("thrown_mine", StringComparison.OrdinalIgnoreCase), singularity = kind.Equals("singularity_bomb", StringComparison.OrdinalIgnoreCase);
            if (singularity) { SingularityStages(p, g, notes); return; }
            var attach = p.AddElement("Default_Collide_Behavior").AddElement("Attach");
            attach.AddElement("Minimum_Speed", "0.0");
            attach.AddElement("Maximum_Angle", "90");
            attach.AddElement("Maintain_Orient", "False");
            attach.AddElement("Attach_Offset", "0.0");
            attach.AddElement("Attach_On_First_Collision_Only", "False");
            attach.AddElement("Can_Attach_To_Head", mine ? "False" : "True");
            if (mine) NotOnPeople(p);
            var branches = p.AddElement("Stage_Branchs");
            var main = branches.AddElement("Stage_Branch");
            main.AddElement("Description", mine ? "Lay, arm, wait for someone" : "Stick, wait for the detonator");
            var stages = main.AddElement("Stages");
            var flying = Stage(stages, mine ? "Fly until it lands" : "Fly until it sticks", "Idle");
            if (Effect(g, "Attached_Effect", notes) is { } trail) StageEffect(flying, trail);
            var triggers = flying.Element("Triggers")!;
            triggers.AddElement("Trigger").AddElement("Type").AddElement("Attach").AddElement("Object_Type", "Any");
            if (!mine)
            {
                var inAir = triggers.AddElement("Trigger");
                inAir.AddElement("Type").AddElement("Remote");
                inAir.AddElement("Branch_Jump", "1");
            }
            Stage(stages, "Stay (no lifetime)", "Set_Lifetime_Timer").Element("Type")!.Element("Set_Lifetime_Timer")!.AddElement("Delay_ms", "0");
            string? light = Effect(g, "Attached_Effect2", notes);
            if (mine)
            {
                string arm = Tweak("Proxy_mine_activation_delay_ms", "1500"), radius = Tweak("Proxy_mine_radius", "3"), delay = Tweak("Proxy_mine_detonation_delay_ms", "600");
                Stage(stages, "Arm", "Idle").Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Timed").AddElement("Delay_ms", arm);
                var armed = Stage(stages, "Armed: wait for someone", "Idle");
                if (light != null) StageEffect(armed, light, "Kill Entirely");
                var near = armed.Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Proximity");
                // Guerrilla's skips the mine's owner and his squad (RM 0xA71C73, 0xA71DB6): Armageddon's nearest, its owner's team.
                near.AddElement("Triggered_By", "Exclude Owner's Team");
                near.AddElement("Trigger_Radius", radius);
                var tripped = Stage(stages, "Tripped", "Idle");
                if (light != null) StageEffect(tripped, light, "Kill Entirely");
                tripped.Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Timed").AddElement("Delay_ms", delay);
                notes.Add($"its mine: armed {arm} ms after it lands, set off by anyone not on Mason's team within {radius} m, goes off {delay} ms later " +
                          "(Guerrilla's also by guerrillas outside his squad; its 4.5 m for vehicles and its smart-mine upgrade: not yet)");
            }
            else
            {
                var waiting = Stage(stages, "Wait for the detonator", "Idle");
                if (light != null) StageEffect(waiting, light, "Kill Entirely", BlinkingLight);
                waiting.Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Remote");
            }
            Stage(stages, "Go off", "Detonate");
            if (!mine)
            {
                var setOff = branches.AddElement("Stage_Branch");
                setOff.AddElement("Description", "Set off in the air");
                Stage(setOff.AddElement("Stages"), "Go off", "Detonate");
            }
        }

        /// <summary>The tag of Guerrilla's remote charge's and singularity bomb's skeletons where their light blinks.</summary>
        const string BlinkingLight = "blinking_light";

        /// <summary>A projectile that doesn't stick to people (Guerrilla's "dont attach to humans", a mine's "attach to ground"): it falls off them.</summary>
        static void NotOnPeople(XtblElement p)
        {
            var o = p.AddElement("Override_Collision_Behaviors").AddElement("Override_Collision_Behavior");
            o.AddElement("Collision_Object_Type", "Human");
            o.AddElement("Collision_Behavior").AddElement("None");
        }

        /// <summary>
        /// Guerrilla's singularity bomb: it sticks where it lands (not to people), its light blinking; Time_Until_Propelled
        /// after it is thrown it pulls everything near into it (Armageddon's Vacuum, as its developers moved Guerrilla's
        /// into their cut singularity_bomb projectile: 8 m, a constant of Guerrilla's singularity_bomb_vacuum, RM 0xAD69F0),
        /// playing its implosion (Attached_Effect4), until Time_Until_Drop, when it goes off.
        /// </summary>
        void SingularityStages(XtblElement p, XtblElement g, List<string> notes)
        {
            var info = g.Element("Projectile_Info")!;
            int Ms(string field) => double.TryParse(info.ValueOf(field)?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (int)v : 0;
            int pull = Ms("Time_Until_Propelled") is > 0 and var t ? t : 5000, end = Math.Max(pull + 1, Ms("Time_Until_Drop") is > 0 and var d ? d : 9300);
            var attach = p.AddElement("Default_Collide_Behavior").AddElement("Attach");
            attach.AddElement("Minimum_Speed", "0.0");
            attach.AddElement("Maximum_Angle", "90");
            attach.AddElement("Maintain_Orient", "False");
            attach.AddElement("Attach_Offset", "0.0");
            attach.AddElement("Attach_On_First_Collision_Only", "False");
            attach.AddElement("Can_Attach_To_Head", "False");
            NotOnPeople(p);
            var main = p.AddElement("Stage_Branchs").AddElement("Stage_Branch");
            main.AddElement("Description", "Wait, pull, go off");
            var stages = main.AddElement("Stages");
            var wait = Stage(stages, "Wait", "Idle");
            if (Effect(g, "Attached_Effect2", notes) is { } light) StageEffect(wait, light, "Kill Entirely", BlinkingLight);
            wait.Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Timed").AddElement("Delay_ms", pull.ToString(CultureInfo.InvariantCulture));
            var vacuumStage = Stage(stages, "Pull everything in", "Vacuum");
            var vacuum = vacuumStage.Element("Type")!.Element("Vacuum")!;
            vacuum.AddElement("Ignore_Owner", "False");
            vacuum.AddElement("Radius", "8.0");
            vacuum.AddElement("Destination_Y_Offset", "1.5");
            var offset = vacuum.AddElement("Random_Destination_Offset");
            foreach (string axis in new[] { "X", "Y", "Z" }) offset.AddElement(axis, "1.0");
            if (Effect(g, "Attached_Effect4", notes) is { } implosion) StageEffect(vacuumStage, implosion, "Kill Entirely", looping: false);
            vacuumStage.Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Timed").AddElement("Delay_ms", (end - pull).ToString(CultureInfo.InvariantCulture));
            Stage(stages, "Go off", "Detonate");
            notes.Add($"its singularity: pulls everything within 8 m into it {pull / 1000.0:0.0##} s after it is thrown, goes off at {end / 1000.0:0.0##} s; " +
                      "its camera shake while pulling (Secondary_Camera_Shake): not yet");
        }

        /// <summary>
        /// Guerrilla's grinder blade: it flies nearly straight (its Gravity -1, a tenth of the world's), its trail behind it,
        /// bounces off what it hits by its own physics (Armageddon's None, as its developers' cut grinder_blade), and cuts
        /// whoever it hits: Guerrilla's damage (Damage_Scaling_Max) as Armageddon's damage on contact. Guerrilla takes its
        /// effects away at the first hit ("immediate effect detach").
        /// </summary>
        void GrinderStages(XtblElement p, XtblElement g, string gravity, List<string> notes)
        {
            p.AddElement("Default_Collide_Behavior").AddElement("None");
            var damage = p.AddElement("Collide_Damage_Info");
            var max = damage.AddElement("Max_Damage");
            var guerrilla = g.Element("Damage_Scaling_Max");
            string Value(string field)
            {
                string v = guerrilla?.ValueOf(field)?.Trim() ?? "0";
                // -1: "as for a vehicle" (the player's vehicle's figure in Guerrilla's tables): the vehicle damage.
                return v == "-1" ? guerrilla?.ValueOf("Vehicle_Damage")?.Trim() ?? "0" : v;
            }
            foreach (string field in new[] { "NPC_Damage", "Player_Damage", "Vehicle_Damage", "Player_Vehicle_Damage" }) max.AddElement(field, Value(field));
            max.AddElement("Structural_Damage", "0.0");
            var main = p.AddElement("Stage_Branchs").AddElement("Stage_Branch");
            main.AddElement("Description", "Fly, then bounce about");
            var stages = main.AddElement("Stages");
            var fly = Stage(stages, "Fly", "Idle", gravity);
            if (Effect(g, "Attached_Effect", notes) is { } trail) StageEffect(fly, trail);
            fly.Element("Triggers")!.AddElement("Trigger").AddElement("Type").AddElement("Contact").AddElement("Object_Type", "Any");
            Stage(stages, "Bounce about", "Idle", gravity);
            notes.Add($"its blade: cuts for up to {Value("NPC_Damage")} (Guerrilla's), bounces off by its physics, gone after 10 s; its sparks on each bounce (Attached_Effect2): not yet");
        }

        /// <summary>
        /// A thrown weapon's projectile (Guerrilla's Projectile_Info inside the weapon) as an entry of Armageddon's
        /// projectiles.xtbl, under the weapon's name: its model (an item), its speed, what it does when it hits
        /// (Guerrilla's "bounce on impact": Bounce, else Detonate), its explosion, and one stage branch: flying with
        /// its effects (Attached_Effect and Attached_Effect2: a grenade's trail and light) until its fuse runs out
        /// (a timed trigger, Fuse_Time), then going off. Null if it can't be made.
        /// </summary>
        string? Projectile(XtblElement g, string name, List<string> notes)
        {
            if (g.Element("Projectile_Info") is not { } info) { notes.Add("its projectile: the weapon has no Projectile_Info"); return null; }
            if (!ProjectileIds.TryGetValue(name, out int id)) { notes.Add("its projectile has no Unique_ID of ours yet (add it to GuerrillaWeapons.ProjectileIds)"); return null; }
            if (projectiles.Entries.Count() + AddOnProjectiles >= GameProjectiles)
            {
                notes.Add($"its projectile: left out, the game's projectile table is full (it holds {GameProjectiles}: Armageddon's own, " +
                          "its add-on's and the Guerrilla weapons' built before this one fill it)");
                return null;
            }
            string ours = Ours(name, projectiles);
            int nameBytes = projectiles.Entries.Sum(e => XtblFile.EntryName(e).Length + 1) + AddOnProjectileNameBytes;
            if (nameBytes + ours.Length + 1 > ProjectileNameBytes)
            {
                notes.Add($"its projectile: left out, the game's room for projectile names is full ({nameBytes} of {ProjectileNameBytes} bytes)");
                return null;
            }
            string? P(string field) => info.ValueOf(field)?.Trim() is { Length: > 0 } v ? v : null;
            string? model = P("Model") is { } m ? Item(m, notes) : null;
            if (model == null) { notes.Add($"its projectile's model ({P("Model") ?? "none"}) couldn't be made"); return null; }
            var flags = info.Element("Projectile_Flags")?.Elements.Select(e => e.Value.Trim()).ToList() ?? [];
            var weaponFlags = g.Element("Flags")?.Elements.Select(e => e.Value.Trim()).ToList() ?? [];
            // A propane tank has both: it bounces off what it can't break, and goes off when it hits hard (it goes off on impact).
            bool bounce = flags.Contains("bounce on impact", StringComparer.OrdinalIgnoreCase) && !flags.Contains("detonate on impact", StringComparer.OrdinalIgnoreCase);
            bool rocket = flags.Contains("rocket flight", StringComparer.OrdinalIgnoreCase);
            int Ms(string field) => double.TryParse(P(field), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (int)v : 0;
            int fuse = Ms("Fuse_Time"), drop = rocket ? Ms("Time_Until_Drop") : 0;
            // Guerrilla's Gravity is the pull itself (m/s/s: -9.8 the world's, 0 none, a rocket's; the grinder's -1), Armageddon's a
            // multiple of the world's. A rocket flies straight until it hits, or until its fuel runs out (Time_Until_Drop) and it falls.
            string gravity = double.TryParse(P("Gravity"), NumberStyles.Float, CultureInfo.InvariantCulture, out double gv)
                           ? (Math.Abs(gv) < 1e-6 ? 0.0 : -gv / 9.8).ToString("0.0##", CultureInfo.InvariantCulture)
                           : rocket ? "0.0" : "1.0";

            var p = new XtblElement("Projectile");
            p.AddElement("Name", ours);
            p.AddElement("Unique_ID", id.ToString(CultureInfo.InvariantCulture));
            p.AddElement("Object_Item", model);
            p.AddElement("Min_Speed", "0.0");
            p.AddElement("Max_Speed", P("Max_Speed") ?? P("Start_Speed") ?? "20");
            string kind = g.ValueOf("Weapon_Class")?.Trim() ?? "";
            bool sticky = kind.Equals("thrown_mine", StringComparison.OrdinalIgnoreCase) || kind.Equals("thrown_charge", StringComparison.OrdinalIgnoreCase) ||
                          kind.Equals("singularity_bomb", StringComparison.OrdinalIgnoreCase);
            bool grinder = kind.Equals("grinder", StringComparison.OrdinalIgnoreCase);
            // A harpoon (the Marauders' harpoon turret: Guerrilla's "attach on impact") sticks in what it hits.
            bool harpoon = flags.Contains("attach on impact", StringComparer.OrdinalIgnoreCase) && !sticky;
            int lifetime = Math.Max(10000, Math.Max(fuse, Math.Max(drop, Ms("Time_Until_Drop"))) + 5000);
            if (grinder) lifetime = 10000;  // Guerrilla's process_grinder takes a blade away 10 s after it is fired (RM 0xA71F62)
            p.AddElement("Max_Lifetime_ms", lifetime.ToString(CultureInfo.InvariantCulture));
            if (harpoon)
            {
                var attach = p.AddElement("Default_Collide_Behavior").AddElement("Attach");
                attach.AddElement("Minimum_Speed", "0.0");
                attach.AddElement("Maximum_Angle", "90");
                attach.AddElement("Maintain_Orient", "True");
                attach.AddElement("Attach_Offset", "0.0");
                attach.AddElement("Attach_On_First_Collision_Only", "True");
                attach.AddElement("Can_Attach_To_Head", "True");
                notes.Add("its harpoon: sticks in what it hits; Guerrilla's pull on it (the harpoon's tether): not yet");
            }
            else if (!sticky && !grinder) p.AddElement("Default_Collide_Behavior").AddElement(bounce ? "Bounce" : "Detonate");
            if (grinder) notes.Add("its explosion (grinder_explosive): only with Guerrilla's explosive-grinder upgrade, not yet");
            else if ((g.ValueOf("Explosion")?.Trim() is { Length: > 0 } named ? named : CodeExplosions.GetValueOrDefault(name)) is { } explosion)
            {
                string ourExplosion = GuerrillaEffectTables.Added(explosion);
                if (worldExplosions.Contains(ourExplosion)) p.AddElement("Explosion", ourExplosion);
                else notes.Add($"Explosion {explosion}: not in the world's explosion table (build the world again: it adds the weapons' explosions it can); left out");
            }
            var ourFlags = p.AddElement("Projectile_Flags");
            if (weaponFlags.Contains("shatter", StringComparer.OrdinalIgnoreCase)) ourFlags.AddElement("Flag", "shatter");
            if (flags.Contains("detonate on explosion impact", StringComparer.OrdinalIgnoreCase)) ourFlags.AddElement("Flag", "detonate on explosion hit");
            if (flags.Contains("align orient to flightpath", StringComparer.OrdinalIgnoreCase)) ourFlags.AddElement("Flag", "align orient to flightpath");
            Editor(p, g);
            if (sticky) StickyStages(p, g, kind, notes);
            else if (grinder) GrinderStages(p, g, gravity, notes);
            else
            {
                var branch = p.AddElement("Stage_Branchs").AddElement("Stage_Branch");
                branch.AddElement("Description", fuse > 0 ? "Fuse" : "Flight");
                var stages = branch.AddElement("Stages");
                var flight = stages.AddElement("Stage");
                flight.AddElement("Description", fuse > 0 ? "Wait for the fuse" : "Fly");
                flight.AddElement("Type").AddElement("Idle");
                flight.AddElement("Gravity_Multiplier", gravity);
                var effects = flight.AddElement("Effects");
                // A grenade's trail and light (Attached_Effect, Attached_Effect2); a rocket's burning trail (its Attached_Effect;
                // its 2 is the trail after the fuel, its 3 the one before, its 4 the ignition).
                foreach (string field in rocket ? new[] { "Attached_Effect" } : new[] { "Attached_Effect", "Attached_Effect2" })
                    if (Effect(g, field, notes) is { } effect)
                    {
                        var e = effects.AddElement("Effect");
                        e.AddElement("Effect_Name", effect);
                        e.AddElement("Looping", "True");
                        e.AddElement("Parented", "True");
                        e.AddElement("Stop_Type", "Stop Looping");
                        e.AddElement("Check_For_Obstruction", "False");
                    }
                var triggers = flight.AddElement("Triggers");
                if (fuse > 0) triggers.AddElement("Trigger").AddElement("Type").AddElement("Timed").AddElement("Delay_ms", fuse.ToString(CultureInfo.InvariantCulture));
                else if (drop > 0) triggers.AddElement("Trigger").AddElement("Type").AddElement("Timed").AddElement("Delay_ms", drop.ToString(CultureInfo.InvariantCulture));
                flight.AddElement("Drag", "0.0");
                if (fuse == 0 && drop > 0)
                {
                    // Out of fuel: it falls, its after-fuel trail behind it, until it hits.
                    var falling = stages.AddElement("Stage");
                    falling.AddElement("Description", "Out of fuel");
                    falling.AddElement("Type").AddElement("Idle");
                    falling.AddElement("Gravity_Multiplier", "1.0");
                    var fallEffects = falling.AddElement("Effects");
                    if (Effect(g, "Attached_Effect2", notes) is { } after)
                    {
                        var e = fallEffects.AddElement("Effect");
                        e.AddElement("Effect_Name", after);
                        e.AddElement("Looping", "True");
                        e.AddElement("Parented", "True");
                        e.AddElement("Stop_Type", "Stop Looping");
                        e.AddElement("Check_For_Obstruction", "False");
                    }
                    falling.AddElement("Triggers");
                    falling.AddElement("Drag", "0.0");
                }
                if (fuse > 0)
                {
                    var detonate = stages.AddElement("Stage");
                    detonate.AddElement("Description", "Go off");
                    detonate.AddElement("Type").AddElement("Detonate");
                    detonate.AddElement("Gravity_Multiplier", "1.0");
                    detonate.AddElement("Triggers");
                    detonate.AddElement("Drag", "0.0");
                }
            }
            var description = projectiles.Description ?? throw new InvalidDataException("Armageddon's projectiles.xtbl has no description");
            var dropped = new List<string>();
            var added = new List<string>();
            var unfilled = new List<string>();
            GuerrillaTableFit.Prune(p, description.Children, "", dropped, commonFields[projectiles]);
            GuerrillaTableFit.FitValues(p, description.Children, "", []);
            GuerrillaTableFit.FillRequired(p, description.Children, "", added, unfilled);
            projectiles.Table!.AddElement(p);
            notes.Add($"its projectile {ours}: model {model}, {(rocket ? "a rocket, " : "")}" +
                      $"{(sticky ? "sticks where it lands" : harpoon ? "sticks in what it hits" : grinder ? "cuts and bounces" : bounce ? "bounces" : "goes off on impact")}" +
                      (fuse > 0 ? $", goes off {fuse / 1000.0:0.0##} s after it is thrown" : "") + (drop > 0 ? $", falls after {drop / 1000.0:0.0##} s" : "") +
                      (dropped.Count > 0 ? $"; left out of its entry (Armageddon's table hasn't them): {string.Join(", ", dropped)}" : "") +
                      (unfilled.Count > 0 ? $"; required by Armageddon, with no default: {string.Join(", ", unfilled)}" : ""));
            notes.Add($"its projectile's mass {P("Mass") ?? "?"}, restitution {P("Restitution") ?? "?"} and friction {P("Friction") ?? "?"}: " +
                      "Armageddon's projectile table has no fields for them (its item's model's physics)");
            return ours;
        }

        /// <summary>An item Guerrilla's people carry (not a weapon), under Guerrilla's name (see <see cref="PeoplesItems"/>).</summary>
        /// <summary>
        /// A pickup item of Guerrilla's world (see <see cref="PickupItems"/>). Guerrilla's ammo box is used with the action
        /// button (ask_pickup) and refills each weapon by its Ammo_Box_Restock × the difficulty's ammo crate factor
        /// (player_use_ammo_box): ours is walked into, Armageddon's ammo pickup (ammo_pickup), which gives each
        /// weapon's ammo type its Ammo_Pickup_Tiers: Guerrilla's same numbers (see AmmoType); it stays there, as
        /// Guerrilla's (no_pickup_delete).
        /// </summary>
        /// <summary>A backpack's model (see <see cref="BackpackItems"/>): its item, with no pickup.</summary>
        public IEnumerable<string> BackpackItem(string name)
        {
            var notes = new List<string>();
            string? ours = Item(name, notes);
            foreach (string note in notes) yield return $"{name}: {note}";
            if (ours == null) { yield return $"{name}: left out"; yield break; }
            var entry = items.Entries.First(e => XtblFile.EntryName(e).Equals(ours, StringComparison.OrdinalIgnoreCase));
            entry.Element("Touch_Script")?.Remove();
            // Loaded with the game, as the guns are (Guerrilla's "MP and WC backpack" and "Multiplayer" aren't Armageddon's).
            GuerrillaTableFit.Set(entry, "streaming_category", "General (preloaded)");
            Built.Add($"{name} as {ours} (a backpack's model)");
        }

        public IEnumerable<string> PickupItem(string name)
        {
            var notes = new List<string>();
            string? ours = Item(name, notes);
            foreach (string note in notes) yield return $"{name}: {note}";
            if (ours == null) { yield return $"{name}: left out"; yield break; }
            var entry = items.Entries.First(e => XtblFile.EntryName(e).Equals(ours, StringComparison.OrdinalIgnoreCase));
            if (entry.Element("Item_Flags")?.Elements.Any(f => f.Value.Trim().Equals("is_ammo_box", StringComparison.OrdinalIgnoreCase)) == true)
            {
                entry.Element("Touch_Script")?.Remove();
                var pickup = entry.AddElement("Touch_Script").AddElement("ammo_pickup");
                pickup.AddElement("sp_tier", "3");
                pickup.AddElement("mp_tier", "3");
                yield return $"{name}: walked into, it refills every weapon's ammo as Guerrilla's ammo box does (Armageddon's ammo pickup; Guerrilla's is used with the action button)";
            }
            Built.Add($"{name} as {ours} (an item Guerrilla's world places)");
        }

        public IEnumerable<string> PeoplesItem(string name)
        {
            var notes = new List<string>();
            string? ours = Item(name, notes);
            foreach (string note in notes) yield return $"{name}: {note}";
            if (ours == null) yield return $"{name}: left out";
            else if (!ours.Equals(name, StringComparison.OrdinalIgnoreCase)) yield return $"{name}: made as {ours} (Armageddon has an item of that name), which its code won't ask for";
            else Built.Add($"{name} (an item Guerrilla's people carry)");
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
            var characterMesh = g.Element("character_mesh");
            string? meshFile = characterMesh != null ? characterMesh.Element("character_mesh")?.ValueOf("Filename")?.Trim() : g.Element("Mesh")?.ValueOf("Filename")?.Trim();
            if (string.IsNullOrEmpty(meshFile)) { notes.Add($"item {name}: names no model"); return null; }
            string mesh = Path.GetFileNameWithoutExtension(meshFile);
            string ours = Ours(name, items);

            // Whether it gets a _high container (Guerrilla's high_res_map, with its <mesh>_high or <item>_high
            // container's material): then its own container, which the game preloads with every "General
            // (preloaded)" item into a pool of 17 MB (item_preload, shared with Armageddon's own and its 8.8 MB
            // low-detail peg: see BaseTextureSize),
            // carries small copies of its textures (BaseTextureSize) under names of their own (LowAlias), the
            // _high container the full ones under the usual names, as Armageddon's own guns keep their big
            // textures out of their own containers. One with no _high keeps its textures, at most OnlyTextureSize.
            bool wantsHigh = g.Element("Item_Flags")?.Elements.Any(e => e.Value.Trim().StartsWith("high_res_map", StringComparison.OrdinalIgnoreCase)) ?? false;
            // Its archive: items.vpp_pc, or items_mp.vpp_pc for the multiplayer's (six of the backpacks).
            var archive = guerrillaItems.Find(name + ".str2_pc") == null && guerrillaItemsMp?.Find(name + ".str2_pc") != null ? guerrillaItemsMp : guerrillaItems;
            var high = wantsHigh ? archive.Find(mesh + "_high.str2_pc") ?? archive.Find(name + "_high.str2_pc") : null;
            var highPack = high != null ? archive.OpenContainer(high) : null;
            var materialFile = highPack?.Entries.FirstOrDefault(e => e.Name.EndsWith(".mat_pc", StringComparison.OrdinalIgnoreCase));
            Func<string, string> baseName = materialFile != null ? LowAlias : GuerrillaVehicles.Alias;
            int baseSize = materialFile != null ? BaseTextureSize : OnlyTextureSize;

            // The model, with its low-detail textures.
            var source = archive.Find(name + ".str2_pc");
            if (source == null) { notes.Add($"item {name}: Guerrilla's items.vpp_pc and items_mp.vpp_pc have no {name}.str2_pc"); return null; }
            var pack = archive.OpenContainer(source);
            string cExt = characterMesh != null ? ".ccmesh_pc" : ".csmesh_pc", gExt = characterMesh != null ? ".gcmesh_pc" : ".gsmesh_pc";
            var c = pack.Find(mesh + cExt);
            var gFile = pack.Find(mesh + gExt);
            if (c == null || gFile == null) { notes.Add($"item {name}: its container has no {mesh}{cExt} / {gExt}"); return null; }
            byte[] cBytes = pack.Read(c);
            byte[] header;
            IReadOnlyList<string> textures;
            string? ourRig = null;
            if (characterMesh != null)
            {
                // A character mesh (a gun with moving parts: the shotgun's pump, the pistol's slide): Guerrilla's
                // version 3 as Armageddon's 7 (GuerrillaCharacterMeshes, the people's converter), and its skeleton,
                // Guerrilla's file as it is, loaded at start-up with the game's (mods\guerrilla_gear\rigs.txt).
                if (GuerrillaCharacterMeshes.NotYet(cBytes) is { } whyNot) { notes.Add($"item {name}: {whyNot}"); return null; }
                string rig = Path.ChangeExtension(Path.GetFileName(characterMesh.Element("rig")?.ValueOf("Filename")?.Trim() ?? ""), ".rig_pc").ToLowerInvariant();
                if (rig == ".rig_pc" || guerrillaMisc.Find(rig) is not { } rigEntry) { notes.Add($"item {name}: its skeleton {rig} isn't in Guerrilla's misc.vpp_pc"); return null; }
                ourRig = gameMisc.Find(rig) != null ? "rfg_" + rig : rig;  // a name of Armageddon's own would load its skeleton, not this one
                Rigs[ourRig] = guerrillaMisc.Read(rigEntry);
                var notSkinned = new List<string>();
                (header, textures) = GuerrillaCharacterMeshes.Convert(cBytes, baseName, notSkinned);
                if (notSkinned.Count > 0) notes.Add($"item {name}: parts drawn unskinned (no skinned shader of Armageddon's for them): {string.Join(", ", notSkinned.Distinct())}");
            }
            else
            {
                if (GuerrillaStaticMeshes.NotYet(cBytes) is { } why) { notes.Add($"item {name}: {why}"); return null; }
                (header, textures) = GuerrillaStaticMeshes.Convert(cBytes, baseName, heldItems.Contains(name));
            }
            var ownPegs = pack.Entries.Where(e => e.Name.EndsWith(".cpeg_pc", StringComparison.OrdinalIgnoreCase))
                              .Select(e => PegFile.Read(pack.Read(e), pack.Read(pack.Find(PegFile.DataFileName(e.Name)!)!))).ToList();
            var files = new List<(string Name, byte[] Data)> { (mesh + cExt, header), (mesh + gExt, pack.Read(gFile)) };
            var missing = new List<string>();
            foreach (string texture in textures)
            {
                var made = GuerrillaBuiltIns.Files(texture, baseName(texture)) ?? Single([.. ownPegs, .. lowPegs], texture, baseName, baseSize);
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
                    var (pHeader, pTextures) = GuerrillaStaticMeshes.Convert(pBytes!, baseName);
                    files.Add((pickup + ".csmesh_pc", pHeader));
                    files.Add((pickup + ".gsmesh_pc", pack.Read(pg!)));
                    foreach (string texture in pTextures)
                    {
                        var made = GuerrillaBuiltIns.Files(texture, baseName(texture)) ?? Single([.. ownPegs, .. lowPegs], texture, baseName, baseSize);
                        if (made == null) missing.Add(texture);
                        else if (!files.Any(x => x.Name.Equals(made[0].Name, StringComparison.OrdinalIgnoreCase))) files.AddRange(made);
                    }
                    pickupKept = true;
                }
            }
            AddContainer(ours, ItemContainer, files);
            notes.Add($"item {name} as {ours}: {(characterMesh != null ? "character model" : "model")} {mesh} ({cBytes.Length:N0} → {header.Length:N0} bytes)" +
                      (ourRig != null ? $", skeleton {ourRig}" : "") + $", {textures.Count - missing.Count} low-detail textures" +
                      (missing.Count > 0 ? $" ({missing.Count} not found: {string.Join(", ", missing)})" : ""));

            // The big textures and the material file that wears them (Guerrilla's <mesh>_high or <item>_high container).
            var entry = g.Copy();
            GuerrillaTableFit.Set(entry, "Name", ours);
            Editor(entry, g);
            if (!pickupKept) entry.Element("pickup_static_mesh")?.Remove();
            if (entry.Element("character_mesh") is { } ourCharacterMesh && ourRig != null)
            {
                if (ourCharacterMesh.Element("rig") is { } rigField) GuerrillaTableFit.Set(rigField, "Filename", Path.ChangeExtension(ourRig, ".rigx"));
                if (ourCharacterMesh.ValueOf("Anim_set")?.Trim() is { Length: > 0 } set && !set.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    GuerrillaTableFit.Set(ourCharacterMesh, "Anim_set", "none");
                    notes.Add($"item {name}: its own animations ({set}: its moving parts) not ported yet; it holds still");
                }
            }
            var flags = entry.Element("Item_Flags");
            if (flags != null)
                foreach (var flag in flags.Elements.Where(e => e.Value.Trim().StartsWith("high_res_map", StringComparison.OrdinalIgnoreCase)).ToList()) flag.Remove();
            if (highPack != null)
            {
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
            // The one every turret's and walker's weapon shares (both games have it): Armageddon's own.
            if (name.Equals("generic_inventory_item", StringComparison.OrdinalIgnoreCase) &&
                inventory.Entries.Any(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
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
            string ours = Fitted(Ours(name, ourTable), ourTable, name);
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
                (byte kind, byte flags) = ext switch
                {
                    ".csmesh_pc" => ((byte)6, (byte)6), ".ccmesh_pc" => ((byte)7, (byte)6), ".cvbm_pc" or ".cpeg_pc" => ((byte)1, (byte)4), _ => ((byte)0, (byte)0)
                };
                if (kind == 0) continue;
                string dataName = Path.ChangeExtension(file, ".g" + ext[2..]);
                int dataSize = files.FirstOrDefault(x => x.Name.Equals(dataName, StringComparison.OrdinalIgnoreCase)).Data?.Length ?? 0;
                entry.Primitives.Add(new AsmPrimitive { Name = file, Type = kind, Allocator = 0, Flags = flags, SplitIndex = 0, HeaderSize = data.Length, DataSize = dataSize });
            }
            ItemAssets.Containers.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            ItemAssets.Containers.Add(entry);
        }
    }

    /// <summary>
    /// A Guerrilla texture from these texture files as an Armageddon single-texture file pair under its alias, or
    /// <paramref name="rename"/>'s name for it (compressed mips down to 4 × 4), or null. A texture bigger than
    /// <paramref name="maxSize"/> (its larger side) starts at the first of its mip levels that isn't.
    /// </summary>
    static (string Name, byte[] Data)[]? Single(IReadOnlyList<PegFile> pegs, string texture, Func<string, string>? rename = null, int maxSize = int.MaxValue)
    {
        string name = (rename ?? GuerrillaVehicles.Alias)(texture);
        foreach (var peg in pegs)
        {
            var t = peg.Textures.FirstOrDefault(x => string.Equals(Path.GetFileNameWithoutExtension(x.Name), texture, StringComparison.OrdinalIgnoreCase));
            if (t == null) continue;
            int first = 0;
            while (first + 1 < t.MipLevels && Math.Max(t.Width >> first, t.Height >> first) > maxSize) first++;
            int width = Math.Max(1, t.Width >> first), height = Math.Max(1, t.Height >> first);
            int levels = t.MipLevels - first;
            if (t.Format.IsBlockCompressed())
            {
                levels = 1;
                while (first + levels < t.MipLevels && Math.Min(width >> levels, height >> levels) >= 4) levels++;
            }
            if (first == 0)
            {
                var (header, data) = peg.Single(t, name + ".tga", levels);
                return [(name + ".cvbm_pc", header), (name + ".gvbm_pc", data)];
            }
            // The smaller copy: its levels from `first` on (an item's texture: one frame, no tiles).
            int skip = 0, size = 0;
            for (int level = 0; level < first; level++) skip += t.Format.LevelSize(Math.Max(1, t.Width >> level), Math.Max(1, t.Height >> level));
            for (int level = 0; level < levels; level++) size += t.Format.LevelSize(Math.Max(1, width >> level), Math.Max(1, height >> level));
            byte[] all = peg.TextureData(t);
            if (skip + size > all.Length) throw new InvalidDataException($"{t.Name}'s mip levels run past its data");
            var smaller = new byte[(size + 15) & ~15];
            Array.Copy(all, skip, smaller, 0, size);
            return [(name + ".cvbm_pc", PegFile.SingleHeader(name + ".tga", width, height, t.Format, levels, t.Flags)), (name + ".gvbm_pc", smaller)];
        }
        return null;
    }

    /// <summary>
    /// The biggest side of the textures an item's own container carries when its _high container has them full
    /// size, and when it has none. The game preloads the containers into its item_preload pool, and after them
    /// Armageddon's own items_containers.gpeg_pc (8.8 MB, its items' low-detail textures). At the game's 17 MB
    /// (Armageddon's own containers about 4.7 MB) even 128 px left the peg no room (the user's 19:38 run); the
    /// guerrilla_gear mod makes the pool 32 MB, and at these sizes ours take about 5.7 MB of it.
    /// </summary>
    const int BaseTextureSize = 128, OnlyTextureSize = 256;

    /// <summary>
    /// The name of the small copy of a texture an item's own container carries while its _high container has it
    /// full size: "rfl_" + its hash, as long as the alias ("rfg_" + its hash), so the high material, which names
    /// the alias, never finds the small one.
    /// </summary>
    static string LowAlias(string texture) => "rfl_" + RfaFormats.Zones.NameHashes.PropertyHash(texture).ToString("x8");

    /// <summary>Guerrilla's campaign entry of a weapon: the one of its name outside the "MP Overrides" copies, which single player skips (is_mp_override, RM 0xBDA230).</summary>
    static XtblElement? CampaignEntry(XtblFile weapons, string name) =>
        weapons.Entries.FirstOrDefault(e => XtblFile.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                            !string.Join("|", XtblFile.EntryCategory(e)).Contains("MP Overrides", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A name as long as the table's Name field allows (its description's MaxLength): longer ones are cut and
    /// end in "_" + four hex digits of Guerrilla's name's hash, so two cut alike stay apart
    /// ("rfg_weapon_fire_thermobaric", 27, as camera_shake.xtbl's 24: "rfg_weapon_fire_the_" + 4). The game
    /// copies a name into a buffer of that size, and a longer one stops it (C0000417).
    /// </summary>
    static string Fitted(string ours, XtblFile table, string guerrillaName)
    {
        int? max = table.Description?.Children.FirstOrDefault(f => f.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))?.MaxLength;
        if (max is not int limit || ours.Length <= limit || limit < 8) return ours;
        string tail = "_" + (RfaFormats.Zones.NameHashes.PropertyHash(guerrillaName) & 0xFFFF).ToString("x4");
        return ours[..(limit - tail.Length)] + tail;
    }

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
