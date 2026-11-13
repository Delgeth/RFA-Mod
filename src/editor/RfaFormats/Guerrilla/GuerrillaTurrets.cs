using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's vehicles' turrets (its turrets.xtbl entries their Turrets mounts name), added to Armageddon's
/// turrets.xtbl, which the guerrilla_world mod hands the game while it starts. The two tables are one
/// (Armageddon's reader is Guerrilla's: the turret's fields of view, turning speeds, its parts by the
/// vehicle's component names, its offsets, flags and weapon); Guerrilla's PRotMaxRampTime is Armageddon's
/// VRotMaxRampTime, and Armageddon's TurretTop asks for Create_Weapon_Physics (Guerrilla's "Weapon Physics"
/// flag says it). A turret named as one of Armageddon's gets "rfg_" before its name. Its weapon is
/// Guerrilla's, as the weapons part names it (Guerrilla's name, "rfg_" + it where Armageddon's table has
/// one of that name); a turret whose weapon the weapons table hasn't (not built yet) is left out, and the
/// vehicle's mount with it.
/// </summary>
public sealed class GuerrillaTurrets
{
    public const string Table = "turrets.xtbl";

    readonly Dictionary<string, XtblElement> rfg;
    readonly XtblFile ours;
    readonly FieldDescription description;
    readonly HashSet<string> armageddons, weapons;
    readonly Dictionary<string, (string? Name, string? Why)> done = new(StringComparer.OrdinalIgnoreCase);

    readonly GuerrillaSoundBanks? sounds;

    /// <param name="sounds">Where the turrets' sounds go (Guerrilla's cues as events); null leaves them out.</param>
    public GuerrillaTurrets(Packfile guerrillaTables, Packfile gameTables, string modFolder, GuerrillaSoundBanks? sounds = null)
    {
        this.sounds = sounds;
        XtblFile Read(Packfile p, string n) => XtblFile.FromBytes(p.Read(p.Find(n) ?? throw new InvalidDataException($"no {n} in {p.Name}")));
        rfg = Read(guerrillaTables, Table).Entries.GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase)
                                          .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        ours = Read(gameTables, Table);
        description = ours.Description ?? throw new InvalidDataException("Armageddon's turrets.xtbl has no description");
        armageddons = new(ours.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        string weaponTable = Path.Combine(modFolder, "weapons.xtbl");
        var weaponFile = File.Exists(weaponTable) ? XtblFile.FromBytes(File.ReadAllBytes(weaponTable)) : Read(gameTables, "weapons.xtbl");
        weapons = new(weaponFile.Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        gameWeapons = new(Read(gameTables, "weapons.xtbl").Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
        // Guerrilla's weapons' own models (Item_3d: a top-less turret's gun, see Convert) and Armageddon's items' names
        // (the weapons part makes Guerrilla's items under ItemName's names).
        rfgGuns = Read(guerrillaTables, "weapons.xtbl").Entries.GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase)
                                                        .ToDictionary(g => g.Key, g => g.First().ValueOf("Item_3d")?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);
        gameItems = new(Read(gameTables, "items_3d.xtbl").Entries.Select(XtblFile.EntryName), StringComparer.OrdinalIgnoreCase);
    }
    readonly HashSet<string> gameWeapons, gameItems;
    readonly Dictionary<string, string> rfgGuns;

    /// <summary>The list of the items the turrets name, for the weapons part (see <see cref="Write"/>).</summary>
    public const string ItemList = "turret_items.txt";
    readonly SortedSet<string> neededItems = new(StringComparer.OrdinalIgnoreCase);                    // Guerrilla's names
    readonly SortedDictionary<string, string> gunsAsTops = new(StringComparer.OrdinalIgnoreCase);      // our weapon → Guerrilla's gun item

    /// <summary>Our name for Guerrilla's items_3d entry, as the weapons part makes it: Guerrilla's, or "rfg_" + it where Armageddon's table has one.</summary>
    string ItemName(string guerrillaItem) => gameItems.Contains(guerrillaItem) ? "rfg_" + guerrillaItem : guerrillaItem;

    /// <summary>How many were added, and those left out with why.</summary>
    public int Added => done.Values.Count(d => d.Name != null);
    public IEnumerable<string> LeftOut => done.Where(d => d.Value.Name == null).Select(d => $"{d.Key} ({d.Value.Why})");

    /// <summary>Guerrilla's turret as Armageddon's table names it once added (made the first time), or null with <paramref name="why"/>.</summary>
    public string? Ported(string turret, out string? why)
    {
        if (!done.TryGetValue(turret, out var result)) done[turret] = result = Convert(turret);
        why = result.Why;
        return result.Name;
    }

    (string?, string?) Convert(string turret)
    {
        if (!rfg.TryGetValue(turret, out var entry)) return (null, "not in Guerrilla's turrets.xtbl");
        var copy = entry.Copy();
        string? rfgWeapon = copy.ValueOf("Weapon")?.Trim();
        // Its weapons, as the weapons part names them (Guerrilla's name, "rfg_" + it where Armageddon has one).
        foreach (string field in new[] { "Weapon", "SecondaryWeapon" })
        {
            string? weapon = copy.ValueOf(field)?.Trim();
            if (string.IsNullOrEmpty(weapon)) continue;
            string ourWeapon = gameWeapons.Contains(weapon) ? "rfg_" + weapon : weapon;
            if (!weapons.Contains(ourWeapon)) return (null, $"its weapon {weapon} isn't built yet");
            copy.Element(field)!.Value = ourWeapon;
        }
        // Its sounds: turning (played while it turns, stopped after) and a hatch's opening and closing.
        foreach (string field in new[] { "Turning_loop_cue", "Door_Open_Cue", "Door_Close_Cue" })
            if (copy.Element(field) is { } cue && cue.Value.Trim().Length > 0)
            {
                string? eventName = sounds == null ? null : field == "Turning_loop_cue" ? sounds.Loop(cue.Value.Trim()) : sounds.OneShot(cue.Value.Trim());
                if (eventName != null) cue.Value = eventName;
                else cue.Remove();
            }
        string name = armageddons.Contains(turret) ? "rfg_" + turret : turret;
        copy.Element("Name")!.Value = name;
        string? mass = copy.ValueOf("Mass");
        if (copy.Element("PRotMaxRampTime") is { } ramp && copy.Element("VRotMaxRampTime") == null)
        {
            copy.InsertAfter(ramp, new XtblElement("VRotMaxRampTime") { Value = ramp.Value });
            ramp.Remove();
        }
        bool physics = copy.Element("Flags")?.Elements.Any(f => f.Value.Trim().Equals("Weapon Physics", StringComparison.OrdinalIgnoreCase)) == true;
        // Its parts' items, under our names for Guerrilla's (the weapons part makes them from the list Write leaves):
        // Guerrilla's turret_middle_1, turret_base_1, rocket_turret_middle, ... are models of its own, not Armageddon's
        // items of the same names.
        foreach (string part in new[] { "TurretBase", "TurretMiddle", "TurretTop" })
            if (copy.Element(part)?.Element("Item") is { } item && item.Value.Trim() is { Length: > 0 } itemName)
            {
                neededItems.Add(itemName);
                item.Value = ItemName(itemName);
            }
        // A top for a turret that has none (nearly all of Guerrilla's: every ring turret, the MG and rocket mounts). Both
        // games then make the top from the weapon's own model (Guerrilla's create_item_component, Armageddon's
        // 0x83D250), and Armageddon only through a named top item gives the weapon a physics body of its own
        // (Create_Weapon_Physics, 0x83D342): through the fallback the gun floated off over the Kitsune (Round 2). A top
        // is drawn from its item's static mesh alone (item +4, 0x978530): the weapon's gun, a character mesh
        // (turret_head_1: HMG_Weapon.cmeshX, its static mesh the collision's), drew nothing as the top (Round 3). So
        // the top is an unseen one, the weapons part's "<gun>_null" (Guerrilla's empty null_weapon model, a static
        // mesh, with a turret_head tag at its origin where the weapon hangs), and the weapon keeps its gun, drawn by
        // the weapon object (which draws character meshes: Armageddon's rocket_turret_head is one) at the ring's
        // turret_body, as Guerrilla's, firing from its own barrels (the "gun" lines of the list).
        var topPart = copy.Element("TurretTop");
        bool topless = topPart == null || (topPart.Element("Item")?.Value.Trim() is not { Length: > 0 } &&
                                           topPart.Element("Component")?.Value.Trim() is not { Length: > 0 });
        if (topless && rfgWeapon != null && rfgGuns.GetValueOrDefault(rfgWeapon) is { Length: > 0 } gun &&
            copy.ValueOf("Weapon")?.Trim() is { Length: > 0 } primary)
        {
            if (topPart == null)
            {
                topPart = new XtblElement("TurretTop");
                if (copy.Element("TurretMiddle") is { } middle) copy.InsertAfter(middle, topPart);
                else copy.AddElement(topPart);
            }
            string topItem = ItemName(gun + "_null");
            if (topPart.Element("Item") is { } item) item.Value = topItem;
            else topPart.AddElement("Item", topItem);
            neededItems.Add(gun);
            gunsAsTops[primary] = gun;
        }
        if (copy.Element("TurretTop") is { } top && top.Elements.Any() && top.Element("Create_Weapon_Physics") == null)
            top.AddElement("Create_Weapon_Physics", physics ? "True" : "False");
        // The flag itself is no flag of Armageddon's (its turrets carry it as Create_Weapon_Physics, above).
        foreach (var flag in copy.Element("Flags")?.Elements.Where(f => f.Value.Trim().Equals("Weapon Physics", StringComparison.OrdinalIgnoreCase)).ToList() ?? [])
            flag.Remove();
        // Empty parts (Guerrilla writes TurretTop and TurretBase always) as Armageddon's: left out.
        foreach (string part in new[] { "TurretTop", "TurretBase" })
            if (copy.Element(part) is { } p && !p.Elements.Any()) p.Remove();
        GuerrillaArsenal.FitEntry(copy, description);
        // Its mass, which Armageddon's description lost but its reader (Guerrilla's) still reads by name.
        if (mass != null && copy.Element("Mass") == null) copy.AddElement("Mass", mass);
        ours.Table!.AddElement(copy);
        armageddons.Add(name);
        return (name, null);
    }

    /// <summary>
    /// Writes turrets.xtbl and the list of the items its turrets name, for the weapons part (built after the
    /// vehicles), which makes them: "item &lt;Guerrilla's item&gt;" for each (made under <see cref="ItemName"/>'s
    /// name), and "gun &lt;our weapon&gt; &lt;Guerrilla's item&gt;" for a weapon a turret of which now has the unseen
    /// top "&lt;item&gt;_null" (the weapons part makes it; the weapon keeps its own model, the gun).
    /// </summary>
    public void Write(string modFolder)
    {
        File.WriteAllBytes(Path.Combine(modFolder, Table), ours.ToBytes());
        File.WriteAllLines(Path.Combine(modFolder, ItemList),
            [.. neededItems.Select(i => "item " + i), .. gunsAsTops.Select(g => $"gun {g.Key} {g.Value}")]);
    }

    /// <summary>What the turrets name of the weapons part (for the build's log).</summary>
    public string ItemsSummary => $"{neededItems.Count} items for their parts ({string.Join(", ", neededItems)}); " +
                                  $"{gunsAsTops.Count} weapons' models as tops ({string.Join(", ", gunsAsTops.Keys)})";
}
