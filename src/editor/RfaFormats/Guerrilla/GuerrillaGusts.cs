using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's gusts: puffs of dust and dust devils its dust system plays on the ground ahead of the player, chosen by
/// the ground's effect material (rfg_effect_materials.xtbl's Gust_Effects: Terrain, Sand and the others name
/// env_dust_gust_a far off, env_dust_gust_b near, env_gust_devil_a far off, each with a chance in percent). Armageddon's
/// table has no Gust_Effects, and it is read once for every level, so the world build adds Guerrilla's gust effects to
/// the world's effect tables (<see cref="Effects"/>: their files in every zone) and writes each material's list into a
/// file of the guerrilla_world mod's (<see cref="FileName"/>), whose dust plays them as Guerrilla's does.
///
/// The file, a line each, the words between tabs: "material &lt;name&gt;", then that material's gusts "gust
/// &lt;effect&gt; &lt;chance, 0 to 1&gt; &lt;near: 1, or 0 far&gt; &lt;fade-in seconds&gt;", in Guerrilla's order (the
/// effect by its name in the world's tables). A material whose gusts are all left out of the world's tables isn't written.
/// </summary>
public static class GuerrillaGusts
{
    /// <summary>The file in the guerrilla_world mod's folder.</summary>
    public const string FileName = "gusts.txt";

    const string MaterialTable = "rfg_effect_materials.xtbl";

    sealed record Gust(string Effect, float Chance, bool Near, float FadeIn);

    static float Number(XElement element, string name) =>
        float.TryParse(element.Element(name)?.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;

    /// <summary>Guerrilla's materials with gusts, in its table's order, each with its gusts.</summary>
    static List<(string Material, List<Gust> Gusts)> Read(Packfile guerrillaMisc)
    {
        string text = Encoding.Latin1.GetString(guerrillaMisc.Read(guerrillaMisc.Find(MaterialTable) ?? throw new InvalidDataException($"no {MaterialTable} in Guerrilla's misc.vpp_pc")));
        var table = XElement.Parse(Regex.Match(text, @"<Table>.*?</Table>", RegexOptions.Singleline).Value);
        var materials = new List<(string, List<Gust>)>();
        foreach (var material in table.Elements("Material"))
        {
            string name = material.Element("Name")?.Value.Trim() ?? "";
            var gusts = (material.Element("Gust_Effects")?.Element("Effect_List")?.Elements("Effect_Element") ?? [])
                .Select(e => (Effect: e.Element("Effect")?.Value.Trim() ?? "", Element: e))
                .Where(e => e.Effect.Length > 0 && !e.Effect.Equals("none", StringComparison.OrdinalIgnoreCase))
                .Select(e => new Gust(e.Effect, Number(e.Element, "Percent_Chance") / 100,
                                      string.Equals(e.Element.Element("Near_Far")?.Value.Trim(), "Near", StringComparison.OrdinalIgnoreCase),
                                      Number(e.Element, "Fade_in_time")))
                .ToList();
            if (name.Length > 0 && gusts.Count > 0) materials.Add((name, gusts));
        }
        return materials;
    }

    /// <summary>The effects Guerrilla's gusts play (under Guerrilla's names), for the world's effect tables.</summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaMisc) =>
        [.. Read(guerrillaMisc).SelectMany(m => m.Gusts).Select(g => g.Effect).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Writes <see cref="FileName"/> into the mod's folder with the effects under the names the world's tables give them
    /// (<paramref name="builtEffects"/>: a gust whose effect isn't among them is left out), and says what it wrote.
    /// </summary>
    public static string Write(Packfile guerrillaMisc, ISet<string> builtEffects, string modFolder)
    {
        var text = new StringBuilder();
        int materials = 0, gusts = 0;
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (material, list) in Read(guerrillaMisc))
        {
            var kept = list.Where(g => builtEffects.Contains(GuerrillaEffectTables.Added(g.Effect))).ToList();
            foreach (var g in list.Except(kept)) missing.Add(g.Effect);
            if (kept.Count == 0) continue;
            materials++;
            text.Append("material\t").Append(material).Append('\n');
            foreach (var g in kept)
            {
                gusts++;
                text.Append("gust\t").Append(GuerrillaEffectTables.Added(g.Effect)).Append('\t')
                    .Append(g.Chance.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t').Append(g.Near ? '1' : '0').Append('\t')
                    .Append(g.FadeIn.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            }
        }
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, FileName), text.ToString(), Encoding.Latin1);
        return $"{gusts} gusts on {materials} of Guerrilla's materials in mods\\guerrilla_world\\{FileName}" +
               (missing.Count > 0 ? $"; not in the world's effects, left out: {string.Join(", ", missing)}" : "");
    }
}
