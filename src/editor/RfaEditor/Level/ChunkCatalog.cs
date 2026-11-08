using System.IO;

namespace RfaEditor;

/// <summary>The ways the Add list sorts the game's chunks.</summary>
enum ChunkFacet { Type, Category, Setting, Pack, Container }

/// <summary>One of the game's chunks (a building or prop model), sorted for browsing: what it is, what it's for, whose it is, and which packs have it.</summary>
sealed record ChunkEntry(string Name, string Type, string Category, string Setting, IReadOnlyList<(string Pack, string? Container)> Places, bool Stitch = false)
{
    public string Of(ChunkFacet facet) => facet switch
    {
        ChunkFacet.Type => Type,
        ChunkFacet.Category => Category,
        ChunkFacet.Setting => Setting,
        _ => Places.Count > 0 ? Places[0].Pack : "",
    };

    /// <summary>Whether it is in a facet's group (a pack: any of the packs that have it; a part of a pack: of that pack).</summary>
    public bool In(ChunkFacet facet, string value, string? pack = null) => facet switch
    {
        ChunkFacet.Pack => Places.Any(p => p.Pack.Equals(value, StringComparison.OrdinalIgnoreCase)),
        ChunkFacet.Container => Places.Any(p => (pack == null || p.Pack.Equals(pack, StringComparison.OrdinalIgnoreCase)) &&
                                                (p.Container ?? "(loose in the pack)").Equals(value, StringComparison.OrdinalIgnoreCase)),
        _ => Of(facet).Equals(value, StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>Its groups for a facet: one, or for packs and their parts every one that has it.</summary>
    public IEnumerable<string> Groups(ChunkFacet facet, string? pack = null) => facet switch
    {
        ChunkFacet.Pack => Places.Select(p => p.Pack).Distinct(StringComparer.OrdinalIgnoreCase),
        ChunkFacet.Container => Places.Where(p => pack == null || p.Pack.Equals(pack, StringComparison.OrdinalIgnoreCase))
                                      .Select(p => p.Container ?? "(loose in the pack)").Distinct(StringComparer.OrdinalIgnoreCase),
        _ => [Of(facet)],
    };
}

/// <summary>
/// Sorts chunks by their names, which follow one pattern: the kind first (<c>s_</c> props,
/// <c>str_</c> structures, <c>ter_</c> terrain pieces, <c>8m_</c> / <c>15m_</c> physics pieces), then
/// whose or where (<c>cm</c> common, <c>city</c>, <c>sur</c> surface, <c>edf</c>, <c>mar</c> Marauder,
/// <c>ice</c>, <c>col</c> colonist, <c>mp</c> multiplayer), then what it is (<c>cont</c> containers,
/// <c>lite</c> lights, <c>trsh</c> trash, ...), e.g. s_cm_cont_crate_med_a.
/// </summary>
static class ChunkCatalog
{
    /// <summary>The type the stitch pieces (level geometry, .cstch_pc) are listed under.</summary>
    public const string LevelGeometry = "Level geometry";

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = "Props", ["str"] = "Structures", ["ter"] = "Terrain pieces", ["etr"] = "Terrain pieces",
        ["8m"] = "Physics pieces", ["15m"] = "Physics pieces", ["4m"] = "Physics pieces", ["monolith"] = "Monoliths",
        ["anim"] = "Animated", ["alpha"] = "See-through", ["missingchunk"] = "Placeholders", ["missingdestroyable"] = "Placeholders",
    };

    static readonly Dictionary<string, string> Settings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cm"] = "Common", ["com"] = "Common", ["city"] = "City", ["sur"] = "Surface", ["su"] = "Surface",
        ["edf"] = "EDF", ["mar"] = "Marauder", ["ice"] = "Ice", ["ic"] = "Ice", ["col"] = "Colonist",
        ["mp"] = "Multiplayer", ["crs"] = "Crossing", ["nat"] = "Natural", ["mag"] = "Magma", ["bio"] = "Bio",
        ["ultor"] = "Ultor", ["dlc"] = "DLC", ["ruin"] = "Ruins", ["cave"] = "Caves", ["min"] = "Mining",
    };

    static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cont"] = "Containers", ["crate"] = "Containers", ["trsh"] = "Trash and debris", ["debris"] = "Trash and debris",
        ["rubble"] = "Trash and debris", ["lite"] = "Lights", ["light"] = "Lights", ["lightpole"] = "Lights",
        ["ind"] = "Industrial", ["tech"] = "Technology", ["furn"] = "Furniture", ["res"] = "Residential",
        ["app"] = "Appliances", ["sign"] = "Signs", ["wall"] = "Walls", ["hway"] = "Highways", ["road"] = "Highways",
        ["tun"] = "Tunnels", ["tnl"] = "Tunnels", ["flor"] = "Floors", ["floor"] = "Floors", ["bldg"] = "Buildings",
        ["bld"] = "Buildings", ["office"] = "Buildings", ["house"] = "Buildings", ["door"] = "Doors", ["tower"] = "Towers",
        ["veh"] = "Vehicles", ["rock"] = "Rocks", ["pipe"] = "Pipes", ["pipes"] = "Pipes", ["fence"] = "Fences",
        ["brdg"] = "Bridges", ["bridge"] = "Bridges", ["stair"] = "Stairs", ["stairs"] = "Stairs", ["barricade"] = "Barricades",
        ["cage"] = "Cages", ["vent"] = "Vents", ["claw"] = "Claws", ["beam"] = "Beams", ["largebeam"] = "Beams",
        ["checkpoint"] = "Checkpoints", ["edfcheckpoint"] = "Checkpoints", ["desk"] = "Furniture", ["terminal"] = "Technology",
        ["generator"] = "Industrial", ["power"] = "Industrial", ["tank"] = "Containers", ["pyln"] = "Highways",
    };

    /// <summary>A chunk's type, category and setting from its name ("Other" where the name doesn't say).</summary>
    public static (string Type, string Category, string Setting) Classify(string name)
    {
        string[] words = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return ("Other", "Other", "Other");
        if (!Types.TryGetValue(words[0], out string? type))
        {
            // Not the usual pattern (the Guerrilla world's own chunks, for one): what the words say, anywhere.
            return ("Other", First(words, Categories) ?? "Other", First(words, Settings) ?? "Other");
        }
        string setting = words.Length > 1 && Settings.TryGetValue(words[1], out string? s) ? s : First(words.Skip(1), Settings) ?? "Other";
        string category = First(words.Skip(2), Categories) ?? First(words.Skip(1), Categories) ?? "Other";
        // Words run together ("0103vent", "EDFcheckpoint"): look inside them too.
        if (category == "Other")
            foreach (string word in words.Skip(1))
                if (Categories.FirstOrDefault(c => c.Key.Length >= 4 && word.Contains(c.Key, StringComparison.OrdinalIgnoreCase)) is { Key: not null } inside)
                {
                    category = inside.Value;
                    break;
                }
        return (type, category, setting);
    }

    static string? First(IEnumerable<string> words, Dictionary<string, string> names) =>
        words.Select(w => names.TryGetValue(w, out string? n) ? n : null).FirstOrDefault(n => n != null);

    /// <summary>A pack's name as the browser shows it: its archive's name (rfg_l0 for the Guerrilla world's).</summary>
    public static string PackName(string archive) => Path.GetFileNameWithoutExtension(archive);

    public static string ContainerName(string? container) => container == null ? "(loose in the pack)" : Path.GetFileNameWithoutExtension(container);

    public static string Title(ChunkFacet facet) => facet switch
    {
        ChunkFacet.Type => "type",
        ChunkFacet.Category => "category",
        ChunkFacet.Setting => "setting",
        ChunkFacet.Pack => "pack",
        _ => "part of the pack",
    };
}
