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
    }
    readonly HashSet<string> gameWeapons;

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

    public void Write(string modFolder) => File.WriteAllBytes(Path.Combine(modFolder, Table), ours.ToBytes());
}
