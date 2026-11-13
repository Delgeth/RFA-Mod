using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's time of day for a world ported from it: its sun and moons (the world's
/// .gtodx) and a set of curves for each of its districts (_Default.dtodx and _&lt;district&gt;.dtodx),
/// all XML, read by Armageddon by the world's and the districts' names.
///
/// The orbitals are Guerrilla's terr01.gtodx with texture names without the extension (as Armageddon's
/// world.gtodx; vfx_sun_01, deimos_d and phobos_d are in its misc.vpp_pc bitmaps). The sun's corona
/// effect keeps Guerrilla's name (env_sun_corona_a): Armageddon reads an orbital's effect but never
/// plays it, so the guerrilla_world mod draws it (from corona.bin, see <see cref="GuerrillaSky.WriteCorona"/>).
///
/// The sets: Guerrilla gives each district its own (terr01_oasis, _eos, _badlands, ...; terr01_default
/// where no district is) and blends those of the zone you are in and its neighbours by where you stand
/// in it; Armageddon does the same with the districts of its territory (see <see cref="GuerrillaTerritory"/>),
/// loading &lt;world&gt;_&lt;district&gt;.dtodx for each. Both games keep a set as the same 68 curves (each
/// 48 half-hour slots filled in between keys the same way), read from the same file format, and give a
/// curve a set leaves empty or out the same default (their set_defaults: the same value for every
/// curve). So each set is written from code as Guerrilla's own: every curve in the games' order
/// (<see cref="Curves"/>), with Guerrilla's keys as they are. The guerrilla_world mod draws the sky, the
/// clouds, the backdrop, the sun's light, the height fog and the tone mapping from them as Guerrilla
/// does; Armageddon itself reads the ambient, the sun's and the fog's colours, the particles' light,
/// the bloom and the colour correction. Only where Armageddon reads a curve otherwise than Guerrilla is
/// it converted:
/// - the ambient light from above and below: Armageddon turns a key's brightness b into
///   (b − ldr_min) / ldr_max held to 0–1, where Guerrilla (its HDR on) uses it as it is, so the
///   brightness is folded into the colour and set to ldr_max, at every half hour (see <see cref="Folded"/>);
/// - ldr_min and ldr_max themselves: Guerrilla's districts that key them have 0 and 3 all day; Badlands
///   and Eos leave them empty, which Guerrilla never reads but Armageddon would take as the default 0
///   to 32 (dividing the brightness of the ambient, the sun's and the fog's colours and the
///   environment boxes' ambient by 32), so those two get 0 and 3 too.
/// </summary>
public static class GuerrillaTimeOfDay
{
    /// <summary>A set's curves, in the order every set of both games has them (Guerrilla's default set, an older one, lacks ten, which are written empty).</summary>
    public static readonly IReadOnlyList<string> Curves =
    [
        "east0", "east1", "east2", "east3", "west0", "west1", "west2", "west3", "zenith",
        "fog_ground", "fog_atmosphere_scale", "fog_density_new", "fog_density_offset", "fog_color",
        "ambient_color", "back_ambient_color", "tod_light_color", "particle_ambient_color", "particle_tod_light_color",
        "ldr_min", "ldr_max", "bloom_exposure", "iris_rate", "luminance_max", "luminance_min", "luminance_mask_max",
        "eye_adaption_base", "eye_adaption_amount", "eye_fade_min", "eye_fade_max", "brightpass_threshold_new", "brightpass_offset_new",
        "bloom_amount", "bloom_theta", "bloom_slope_A", "bloom_slope_B", "tonemap_lum_range", "tonemap_lum_offset",
        "fog_camera_min", "fog_camera_max", "fog_skybox_offset", "fog_skybox_scale", "fog_clouds_offset", "fog_clouds_scale",
        "fog_matte_offset", "fog_matte_scale", "color_correct_saturation", "color_correct_tint",
        "clouds_front_light_color", "clouds_rear_light_color", "clouds_horizon_front_light_color", "clouds_horizon_rear_light_color",
        "horizon_mountain_color", "horizon_mountain_color_back", "horizon_mountain_fog_color", "horizon_mountain_fog_density",
        "horizon_layer0_strength", "horizon_layer1_strength", "horizon_layer2_strength", "horizon_layer3_strength",
        "overhead_layer0_strength", "overhead_layer1_strength", "overhead_layer2_strength", "overhead_layer3_strength",
        "cloud_backlight_strength", "cloud_backlight_power", "cloud_layer01_speed", "cloud_layer23_speed",
    ];

    /// <summary>ldr_min and ldr_max as Guerrilla's districts that key them have them, all day.</summary>
    const float LdrMin = 0, LdrMax = 3;

    /// <summary>
    /// The orbitals file and the sets of curves, from Guerrilla's misc.vpp_pc: one named Default
    /// (Guerrilla's terr01_default) and one for each of the districts, by their names in the territory,
    /// from their sets in Guerrilla (terr01_ and Guerrilla's name in lower case, spaces as _).
    /// </summary>
    public static (byte[] Orbitals, IReadOnlyList<(string Name, byte[] Set)> Sets) Build(Packfile guerrillaMisc, IEnumerable<(string Name, string Guerrilla)> districts)
    {
        string Text(string name) =>
            Encoding.Latin1.GetString(guerrillaMisc.Read(guerrillaMisc.Find(name) ?? throw new InvalidDataException($"no {name} in {guerrillaMisc.Name}")));
        string orbitals = Text("terr01.gtodx").Replace(".cvbm_pc</map_name>", "</map_name>");
        var sets = new List<(string, byte[])>();
        foreach (var (name, theirs) in districts.Prepend(("Default", "Default")))
        {
            string file = $"terr01_{theirs.ToLowerInvariant().Replace(' ', '_')}.dtodx";
            sets.Add((name, Encoding.Latin1.GetBytes(Set(Read(Text(file), file)))));
        }
        return (Encoding.Latin1.GetBytes(orbitals), sets);
    }

    /// <summary>A key: its time (a half hour, 0–47) and its value (one number, or red, green, blue and a brightness that multiplies them), as the file writes them.</summary>
    record struct Key(string Time, string Value);

    /// <summary>A Guerrilla set's curves by name, each its keys as written; a curve not of the games' set may only be empty (Guerrilla's default set's old brightpass_threshold and _offset).</summary>
    static Dictionary<string, List<Key>> Read(string text, string file)
    {
        var curves = new Dictionary<string, List<Key>>();
        string root = Regex.Match(text, @"<root>(.*)</root>", RegexOptions.Singleline) is { Success: true } m ? m.Groups[1].Value
                      : throw new InvalidDataException($"{file} isn't a time of day set");
        foreach (Match curve in Regex.Matches(root, @"<(\w+)>(.*?)</\1>", RegexOptions.Singleline))
        {
            var keys = Regex.Matches(curve.Groups[2].Value, @"<key>\s*<time>([^<]*)</time>\s*<value>([^<]*)</value>\s*</key>")
                            .Select(k => new Key(k.Groups[1].Value.Trim(), k.Groups[2].Value.Trim())).ToList();
            string name = curve.Groups[1].Value;
            if (!Curves.Contains(name) && keys.Count > 0) throw new InvalidDataException($"{file} keys a curve no set has: {name}");
            curves[name] = keys;
        }
        return curves;
    }

    /// <summary>One district's set: Guerrilla's curves in the games' order, converted as above.</summary>
    static string Set(Dictionary<string, List<Key>> guerrilla)
    {
        List<Key> Of(string name) => guerrilla.GetValueOrDefault(name) ?? [];
        List<Key> Ldr(string name, float value)
        {
            var keys = Of(name);
            if (keys.Count == 0) return [new Key("24", Number(value))];
            if (keys.Any(k => Values(k.Value)[0] != value)) throw new InvalidDataException($"Guerrilla's {name} isn't {value} all day");
            return keys;
        }
        var text = new StringBuilder("<root>\r\n");
        foreach (string name in Curves)
        {
            var keys = name switch
            {
                "ambient_color" or "back_ambient_color" => Folded(Of(name), name, LdrMax),
                "ldr_min" => Ldr(name, LdrMin),
                "ldr_max" => Ldr(name, LdrMax),
                _ => Of(name),
            };
            text.Append($"\t<{name}>\r\n");
            foreach (var (time, value) in keys)
                text.Append($"\t\t<key>\r\n\t\t\t<time>{time}</time>\r\n\t\t\t<value>{value}</value>\r\n\t\t</key>\r\n");
            text.Append($"\t</{name}>\r\n");
        }
        return text.Append("</root>\r\n").ToString();
    }

    /// <summary>
    /// A colour curve for the ambient light, keyed at every half hour with Guerrilla's value there:
    /// its colour and its brightness each filled in between Guerrilla's keys (as both games fill a
    /// curve's half-hour slots), then the colour times the brightness, the brightness set to the given
    /// one. Armageddon (without Tod_hdr_enabled) turns an ambient brightness b into (b − ldr_min) /
    /// ldr_max held to 0–1, so a colour is never brighter than its own red, green and blue; Guerrilla
    /// uses the brightness as it is. Folding only Guerrilla's own keys made the light drift between
    /// them (Armageddon fills in the product, Guerrilla multiplies what it filled in: up to 1.9 times
    /// at 8:00 in Oasis); keyed every half hour it is Guerrilla's at each, and within a half hour a
    /// straight line between them. (A key's time is the slot it fills, 0–47.)
    /// </summary>
    static List<Key> Folded(List<Key> keys, string name, float brightness)
    {
        if (keys.Count == 0) return keys;
        var known = keys.Select(k => (Time: float.Parse(k.Time, CultureInfo.InvariantCulture), Value: Values(k.Value))).OrderBy(k => k.Time).ToList();
        if (known.Any(k => k.Value.Length != 4)) throw new InvalidDataException($"{name} isn't a colour curve");
        var folded = new List<Key>();
        for (int slot = 0; slot < 48; slot++)
        {
            float[] v = Filled(known, slot);
            for (int i = 0; i < 3; i++) v[i] *= v[3];
            v[3] = brightness;
            folded.Add(new Key(slot.ToString(CultureInfo.InvariantCulture), string.Join(" ", v.Select(Number))));
        }
        return folded;
    }

    /// <summary>A curve's value at a half hour, as both games fill a slot between keys: on the straight line between the keys either side of it, round the clock.</summary>
    static float[] Filled(List<(float Time, float[] Value)> keys, int slot)
    {
        int next = 0;
        while (next < keys.Count && keys[next].Time <= slot) next++;
        var (afterTime, after) = keys[next % keys.Count];
        var (beforeTime, before) = keys[(next - 1 + keys.Count) % keys.Count];
        if (beforeTime > slot) beforeTime -= 48;
        if (afterTime <= slot) afterTime += 48;
        float t = afterTime > beforeTime ? (slot - beforeTime) / (afterTime - beforeTime) : 0;
        return [.. before.Zip(after, (a, b) => a + (b - a) * t)];
    }

    static float[] Values(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>A number as the games' files write them: six decimals.</summary>
    static string Number(float value) => value.ToString("F6", CultureInfo.InvariantCulture);
}
