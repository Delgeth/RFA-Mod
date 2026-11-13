using System.Text;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's pickups in its zones, as Armageddon's zone objects, for the world builder. Both games
/// have the two classes, written the same way by Volition's tools:
///   - item: an item of items_3d.xtbl placed in the world (item_type), whether it comes back once taken
///     (respawn). Guerrilla's 105 ammo boxes and 7 weapon lockers are placed, as the weapons build makes those
///     items (<see cref="GuerrillaWeapons.PickupItems"/>). Guerrilla's other items (its banners and drapery, 81,
///     cloth on skeletons of their own) wait for the world's own port of them. Armageddon's item has five
///     properties Guerrilla's hasn't (shadows, its pods, its audio logs): the schema's defaults.
///   - weapon: a weapon of weapons.xtbl lying in the world (weapon_type), whether it's there from the start
///     (preplaced) and comes back (respawns): Guerrilla's 591 propane tanks and 22 singularity bombs, both made by
///     the weapons build.
/// Each keeps Guerrilla's handle unless another object has it. The names are the weapons build's: Guerrilla's, or
/// "rfg_" + it where Armageddon's table has the name (<see cref="GuerrillaWeapons.PickupNames"/>).
/// </summary>
public static class GuerrillaPickups
{
    const uint None = 0xFFFFFFFF;
    static readonly uint Item = NameHashes.ClassHash("item"), Weapon = NameHashes.ClassHash("weapon");

    /// <summary>The order Volition's tools wrote each class's properties in (Armageddon's own levels: ruin06, mp_inf01).</summary>
    static readonly string[] ItemOrder = ["casts_shadows", "op", "pod_spawn_type", "disable_pod_spawning", "respawn", "pod_group_id", "audio_log_name", "item_type"];
    static readonly string[] WeaponOrder = ["weapon_type", "op", "respawns", "preplaced"];

    /// <summary>
    /// A zone's pickups (from Guerrilla's zone, <paramref name="zone"/>) as Armageddon's objects: the items named in
    /// <paramref name="itemNames"/> and the weapons in <paramref name="weaponNames"/> (Guerrilla's name → ours); any
    /// other is counted in <paramref name="skipped"/> by its type.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, ZoneSchema schema, ISet<uint> taken,
                                           IReadOnlyDictionary<string, string> itemNames, IReadOnlyDictionary<string, string> weaponNames,
                                           IDictionary<string, int> skipped)
    {
        var placed = new List<ZoneObject>();
        foreach (var o in zone.Values.Where(o => o.ClassHash == Item || o.ClassHash == Weapon))
        {
            bool item = o.ClassHash == Item;
            string type = o.Text(item ? "item_type" : "weapon_type") ?? "";
            if (!(item ? itemNames : weaponNames).TryGetValue(type, out string? ours))
            {
                skipped[type] = skipped.TryGetValue(type, out int n) ? n + 1 : 1;
                continue;
            }
            uint handle = o.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            string className = item ? "item" : "weapon";
            var properties = (item ? ItemOrder : WeaponOrder).Select(name => name switch
            {
                "item_type" or "weapon_type" => new NewProperty(ZoneProperty.TextType, NameHashes.PropertyHash(name), Text(ours)),
                _ when o.Properties.ContainsKey(NameHashes.PropertyHash(name)) => Own(o, name),
                _ => Default(schema, className, name),
            });
            var made = ZoneObject.Create(o.ClassHash, handle, o.Number, o.BoundsMin, o.BoundsMax, [.. properties], 0);
            made.Parent = made.Sibling = made.Child = None;
            placed.Add(made);
        }
        return placed;
    }

    static NewProperty Own(GuerrillaZoneObject o, string name)
    {
        var (type, raw) = o.Properties[NameHashes.PropertyHash(name)];
        return new NewProperty(type, NameHashes.PropertyHash(name), raw);
    }

    static NewProperty Default(ZoneSchema schema, string className, string name)
    {
        var property = schema.Class(className)?.Property(name) ?? throw new InvalidDataException($"the schema's {className} has no {name}");
        return new NewProperty(property.ZoneType, NameHashes.PropertyHash(property.ZoneName),
                               property.DefaultValue() ?? throw new InvalidDataException($"the schema has no default for {className}'s {name}"));
    }

    static byte[] Text(string text) => [.. Encoding.Latin1.GetBytes(text), 0];
}
