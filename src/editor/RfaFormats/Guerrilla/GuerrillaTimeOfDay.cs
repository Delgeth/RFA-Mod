using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's time of day for a world ported from it: its sun and moons (the world's
/// .gtodx) and a set of curves for each of its districts (_Default.dtodx and _&lt;district&gt;.dtodx),
/// all XML, read by Armageddon by the world's and the districts' names.
///
/// The orbitals are Guerrilla's terr01.gtodx with Armageddon's names: its corona effect
/// (env_sun_corona; Guerrilla's effects don't load in it) and texture names without the extension (as
/// its world.gtodx; vfx_sun_01, deimos_d and phobos_d are in its misc.vpp_pc bitmaps).
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
///   (b − ldr_min) / ldr_max held to 0–1, where Guerrilla (its HDR on) uses it as it is, so each key's
///   brightness is folded into its colour and set to ldr_max (see <see cref="Linear"/>);
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
        string orbitals = Text("terr01.gtodx")
            .Replace("<effect_name>env_sun_corona_a</effect_name>", "<effect_name>env_sun_corona</effect_name>")
            .Replace(".cvbm_pc</map_name>", "</map_name>");
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
                "ambient_color" or "back_ambient_color" => Linear(Of(name), name, LdrMax),
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
    /// A colour curve's keys with each key's brightness folded into its colour, and the brightness
    /// set to the given one. For the ambient light: Armageddon (without Tod_hdr_enabled) turns an
    /// ambient brightness b into (b − ldr_min) / ldr_max held to 0–1, so a colour is never brighter
    /// than its own red, green and blue; Guerrilla used the brightness as it is.
    /// </summary>
    static List<Key> Linear(List<Key> keys, string name, float brightness) =>
        [.. keys.Select(k =>
        {
            float[] v = Values(k.Value);
            if (v.Length != 4) throw new InvalidDataException($"{name} isn't a colour curve");
            for (int i = 0; i < 3; i++) v[i] *= v[3];
            v[3] = brightness;
            return k with { Value = string.Join(" ", v.Select(Number)) };
        })];

    static float[] Values(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>A number as the games' files write them: six decimals.</summary>
    static string Number(float value) => value.ToString("F6", CultureInfo.InvariantCulture);
}
