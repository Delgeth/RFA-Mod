using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RfaFormats.Guerrilla;

/// <summary>
/// What Guerrilla's wheels kick up: a dust effect at each front and rear wheel while a car rolls over the ground,
/// and its braking, peeling and skidding effects. Its wheel code takes them from the effect material of the ground
/// under the wheel (rfg_effect_materials.xtbl's driving_effects: Terrain, Sand, Gravel and Ice name
/// veh_dust_fntwheel and veh_dust_bkwheel with tyre tracks, Concrete and Stone no effect and no tracks), else from
/// the wheel table (vehicle_wheel_surface_effects.xtbl: one row, Terrain, with the braking, peeling and skidding
/// effects and the front and rear ones for a material that names none), and tints them with the material's
/// Effect_tint (Terrain 235 198 84, Sand 243 213 139).
///
/// Armageddon's wheel code is the same and its tables name the same effects, but its visuals for them (files of
/// the same names) are loaded only with its own levels, so in the Guerrilla world the wheels drew nothing, and
/// its tints are its own (Terrain 235 235 235). Both tables are read once for every level, so Guerrilla's values
/// don't go into them: the world build adds Guerrilla's wheel effects to the world's effect tables (their files in
/// every zone, as the vehicles' own; <see cref="Effects"/>) and writes what Guerrilla's tables say into a file of
/// the guerrilla_world mod's (<see cref="FileName"/>), which the mod puts in place of Armageddon's values while
/// the game's wheel code runs in the Guerrilla world.
///
/// The file, a line each, the words between tabs: "wheel &lt;front&gt; &lt;back&gt; &lt;skid&gt; &lt;peel&gt;
/// &lt;brake&gt;" (the wheel table's effects), then for each of Guerrilla's materials "material &lt;name&gt;
/// &lt;tracks: 1 or 0&gt; &lt;front&gt; &lt;back&gt; &lt;red&gt; &lt;green&gt; &lt;blue&gt;" (an effect by its name
/// in the world's tables or - for none: a material naming none plays the wheel table's front and rear, as in
/// Guerrilla's code; the tint 0 to 255 each, or - three times for none). Every material is written, those with no
/// driving effects too, so that the mod gives each Guerrilla's values (no tracks, no tint) and not Armageddon's.
/// </summary>
public static class GuerrillaWheelEffects
{
    /// <summary>The file in the guerrilla_world mod's folder.</summary>
    public const string FileName = "wheel_effects.txt";

    const string MaterialTable = "rfg_effect_materials.xtbl", WheelTable = "vehicle_wheel_surface_effects.xtbl";

    /// <summary>The wheel table's effect fields, in the order the file gives them.</summary>
    static readonly string[] WheelFields = ["Tire_motion_effect_front", "Tire_motion_effect_back", "Skid_effect", "Peel_effect", "Brake_effect"];

    sealed record Material(string Name, bool Tracks, string? Front, string? Back, byte[]? Tint);

    static XElement Table(Packfile guerrillaMisc, string name)
    {
        string text = Encoding.Latin1.GetString(guerrillaMisc.Read(guerrillaMisc.Find(name) ?? throw new InvalidDataException($"no {name} in Guerrilla's misc.vpp_pc")));
        return XElement.Parse(Regex.Match(text, @"<Table>.*?</Table>", RegexOptions.Singleline).Value);
    }

    /// <summary>An element's text, or null when it is missing, empty or "none".</summary>
    static string? Text(XElement? parent, string name) =>
        parent?.Element(name)?.Value.Trim() is { Length: > 0 } value && !value.Equals("none", StringComparison.OrdinalIgnoreCase) ? value : null;

    static byte Byte(XElement colour, string name) =>
        byte.TryParse(colour.Element(name)?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value) ? value : (byte)255;

    /// <summary>Guerrilla's wheel table row (its effects, null for none) and all its materials with their driving effects and tints.</summary>
    static (string?[] Wheel, List<Material> Materials) Read(Packfile guerrillaMisc)
    {
        var row = Table(guerrillaMisc, WheelTable).Elements().FirstOrDefault();
        var wheel = WheelFields.Select(field => Text(row, field)).ToArray();
        var materials = new List<Material>();
        foreach (var material in Table(guerrillaMisc, MaterialTable).Elements("Material"))
        {
            string name = material.Element("Name")?.Value.Trim() ?? "";
            if (name.Length == 0) continue;
            var driving = material.Element("driving_effects");
            var colour = material.Element("Effect_tint");
            byte[]? tint = colour is null ? null : [Byte(colour, "R"), Byte(colour, "G"), Byte(colour, "B")];
            bool tracks = string.Equals(driving?.Element("driving_tracks")?.Value.Trim(), "True", StringComparison.OrdinalIgnoreCase);
            materials.Add(new(name, tracks, Text(driving, "driving_front_effect"), Text(driving, "driving_back_effect"), tint));
        }
        return (wheel, materials);
    }

    /// <summary>The effects Guerrilla's wheels play (under Guerrilla's names), for the world's effect tables.</summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaMisc)
    {
        var (wheel, materials) = Read(guerrillaMisc);
        return [.. wheel.Concat(materials.SelectMany(m => new[] { m.Front, m.Back })).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Writes <see cref="FileName"/> into the mod's folder with the effects under the names the world's tables give
    /// them (<paramref name="builtEffects"/>: an effect left out of them is written as none), and says what it wrote.
    /// </summary>
    public static string Write(Packfile guerrillaMisc, ISet<string> builtEffects, string modFolder)
    {
        var (wheel, materials) = Read(guerrillaMisc);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string Named(string? effect)
        {
            if (effect is null) return "-";
            string ours = GuerrillaEffectTables.Added(effect);
            if (builtEffects.Contains(ours)) return ours;
            missing.Add(effect);
            return "-";
        }
        var text = new StringBuilder();
        text.Append("wheel\t").AppendJoin('\t', wheel.Select(Named)).Append('\n');
        foreach (var m in materials)
        {
            string[] tint = m.Tint is null ? ["-", "-", "-"] : [.. m.Tint.Select(b => b.ToString(CultureInfo.InvariantCulture))];
            text.Append("material\t").Append(m.Name).Append('\t').Append(m.Tracks ? '1' : '0').Append('\t')
                .Append(Named(m.Front)).Append('\t').Append(Named(m.Back)).Append('\t')
                .AppendJoin('\t', (IEnumerable<string>)tint).Append('\n');
        }
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, FileName), text.ToString(), Encoding.Latin1);
        int driving = materials.Count(m => m.Front != null || m.Back != null);
        return $"the wheel table's {wheel.Count(e => e != null)} effects and {materials.Count} materials ({driving} with dust of their own, " +
               $"{materials.Count(m => m.Tint != null)} tinted) in mods\\guerrilla_world\\{FileName}" +
               (missing.Count > 0 ? $"; not in the world's effects, so none: {string.Join(", ", missing)}" : "");
    }
}
