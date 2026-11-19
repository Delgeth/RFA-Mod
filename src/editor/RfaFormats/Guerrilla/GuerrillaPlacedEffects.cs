using System.Text;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's effects placed in its world zones (object_effect), as Armageddon's zone objects, for the
/// world builder. Guerrilla's world has 25: the EDF's force fields (gp_edf_forcefield_a, 4, in zones 05_03, 05_04 and
/// 06_03) and the Mohole's smoke and ash (env_mohole_a 2, env_mohole_ash 19, zone 10_09). Each of Guerrilla's names
/// a visual (vfx.xtbl) and a sound (none in all 25), whether it loops, and its placement (oP); three force fields are
/// children of an object_dummy, which our world doesn't place, so all are placed on their own.
///
/// Armageddon's object_effect names both an effects.xtbl entry (effect_type) and a visual, and its own zones write
/// the same name in both, with its other properties in this order: scale_factor, set_id, visual, oP, stagger_time,
/// looping, max_snd_play_dist_ovrd, sound, effect_type (<see cref="Order"/>). Guerrilla's effects.xtbl has an entry
/// of each visual's name, so the world build adds those effects (<see cref="Effects"/>: their files in every zone)
/// and each placed effect names the added ones ("rfg_" names); one whose effect wasn't added is left out. The
/// handle is Guerrilla's unless taken; the flags 0, as Armageddon's own (the high byte of Guerrilla's is its tools').
/// </summary>
public static class GuerrillaPlacedEffects
{
    static readonly uint PlacedEffect = NameHashes.ClassHash("object_effect");

    /// <summary>The order Armageddon's own zones write an object_effect's properties in.</summary>
    static readonly string[] Order = ["scale_factor", "set_id", "visual", "oP", "stagger_time", "looping", "max_snd_play_dist_ovrd", "sound", "effect_type"];

    /// <summary>A zone's placed effects that name a visual.</summary>
    static IEnumerable<GuerrillaZoneObject> Placed(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone) =>
        zone.Values.Where(o => o.ClassHash == PlacedEffect && Visual(o) != null && o.Value("oP") is { Length: 48 });

    static string? Visual(GuerrillaZoneObject effect) =>
        effect.Text("visual")?.Trim() is { Length: > 0 } name && !name.Equals("none", StringComparison.OrdinalIgnoreCase) ? name : null;

    /// <summary>The effects the world's placed effects show (Guerrilla's names), for the world's effect tables.</summary>
    public static IReadOnlyList<string> Effects(IEnumerable<IReadOnlyDictionary<uint, GuerrillaZoneObject>> zones) =>
        [.. zones.SelectMany(Placed).Select(o => Visual(o)!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    static NewProperty Text(string name, string value) =>
        new(4, NameHashes.PropertyHash(name), [.. Encoding.Latin1.GetBytes(value), 0]);

    static NewProperty Value(string name, byte[] value) => new(5, NameHashes.PropertyHash(name), value);

    /// <summary>
    /// A zone's placed effects as Armageddon's objects (handles not in <paramref name="taken"/>, which gains theirs),
    /// naming the effects and visuals the world's tables have (<paramref name="builtEffects"/>, <paramref name="builtVisuals"/>);
    /// one whose effect isn't among them is left out, counted in <paramref name="leftOut"/>.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, ISet<uint> taken, ISet<string> builtEffects,
                                           ISet<string> builtVisuals, out int leftOut)
    {
        leftOut = 0;
        var made = new List<ZoneObject>();
        foreach (var effect in Placed(zone).OrderBy(o => o.Number))
        {
            string effectName = GuerrillaEffectTables.Added(Visual(effect)!);
            if (!builtEffects.Contains(effectName))
            {
                leftOut++;
                continue;
            }
            string visual = builtVisuals.Contains(effectName) ? effectName : "none";
            byte looping = effect.Value("looping") is { Length: 1 } loops ? loops[0] : (byte)1;
            var values = new Dictionary<string, NewProperty>
            {
                ["scale_factor"] = Value("scale_factor", BitConverter.GetBytes(1.0f)),
                ["set_id"] = Value("set_id", BitConverter.GetBytes(-1)),
                ["visual"] = Text("visual", visual),
                ["oP"] = Value("oP", effect.Value("oP")!),
                ["stagger_time"] = Value("stagger_time", BitConverter.GetBytes(0.0f)),
                ["looping"] = Value("looping", [looping]),
                ["max_snd_play_dist_ovrd"] = Value("max_snd_play_dist_ovrd", BitConverter.GetBytes(-1.0f)),
                ["sound"] = Text("sound", "none"),
                ["effect_type"] = Text("effect_type", effectName),
            };
            uint handle = effect.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            made.Add(ZoneObject.Create(PlacedEffect, handle, effect.Number, effect.BoundsMin, effect.BoundsMax, [.. Order.Select(name => values[name])], 0));
        }
        return made;
    }
}
