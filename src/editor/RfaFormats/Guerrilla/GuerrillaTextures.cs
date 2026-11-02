using RfaFormats.Textures;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's textures found by name in its archives, and written as Armageddon's
/// single-texture files (.cvbm_pc, .gvbm_pc) under new names. Guerrilla keeps its effects' textures in
/// pegs (several textures a file pair) at an archive's top (effects.vpp_pc's vfx_containers, misc.vpp_pc's
/// always_loaded, items.vpp_pc's items_containers, effects_mp.vpp_pc's vfx_mp_containers), in the
/// effects' own containers (in effects.vpp_pc and the world's terr01_l0/l1.vpp_pc), and loose beside
/// the destroyed objects' effects in terr01_precache.vpp_pc (its top holds both); this looks in every
/// texture file of the archives given, at their top and in their containers, the first of a name
/// winning. A texture keeps its entry (its flip-book tiles among it: an effect's particles step
/// through them) and, compressed, its mip levels down to 4 × 4 (Guerrilla sizes the smaller ones by
/// their pixels, Armageddon by whole blocks). Of the 187 textures Guerrilla's effects name, 3 are its
/// built-ins (see <see cref="GuerrillaBuiltIns"/>) and 14 it never shipped (in a few multiplayer,
/// version-21, blood and vehicle effects); those aren't found.
/// </summary>
public sealed class GuerrillaTextures
{
    /// <summary>The archives (in a game's build\pc\cache) holding every texture Guerrilla's effects name and ship.</summary>
    public static readonly string[] EffectArchives =
        ["effects.vpp_pc", "misc.vpp_pc", "items.vpp_pc", "effects_mp.vpp_pc", "terr01_precache.vpp_pc", "terr01_l0.vpp_pc", "terr01_l1.vpp_pc"];

    /// <summary>Where a texture is: its archive, the container in it (null at its top), and its header file.</summary>
    readonly Dictionary<string, (string Archive, string? Container, string Header)> where = new(StringComparer.OrdinalIgnoreCase);
    readonly string folder;
    (string Archive, string? Container, Packfile Pack)? lastPack;

    /// <summary>Every texture in the archives named (those missing from the folder left out), from <paramref name="cacheFolder"/>.</summary>
    public GuerrillaTextures(string cacheFolder, IEnumerable<string> archives)
    {
        folder = cacheFolder;
        foreach (string name in archives)
        {
            string path = Path.Combine(cacheFolder, name);
            if (!File.Exists(path)) continue;
            var archive = Packfile.Open(path);
            Index(name, null, archive);
            foreach (var e in archive.Entries.Where(e => e.IsContainer)) Index(name, e.Name, Packfile.FromBytes(e.Name, archive.Read(e)));
        }
    }

    void Index(string archive, string? container, Packfile pack)
    {
        foreach (var e in pack.Entries.Where(e => IsHeader(e.Name)))
            foreach (string texture in PegFile.TextureNames(pack.Read(e)))
                where.TryAdd(Stem(texture), (archive, container, e.Name));
    }

    /// <summary>How many textures were found.</summary>
    public int Count => where.Count;

    /// <summary>Whether Guerrilla has a texture of this name (without ".tga").</summary>
    public bool Has(string texture) => where.ContainsKey(texture);

    /// <summary>A texture's files as Armageddon's (.cvbm_pc, .gvbm_pc) under <paramref name="name"/> (without ".tga"), or null if Guerrilla hasn't it.</summary>
    public (string Name, byte[] Data)[]? Files(string texture, string name)
    {
        if (!where.TryGetValue(texture, out var at)) return null;
        var pack = Open(at.Archive, at.Container);
        var dataFile = pack.Find(PegFile.DataFileName(at.Header) ?? "") ?? throw new InvalidDataException($"{at.Header} has no data file");
        var peg = PegFile.Read(pack.Read(pack.Find(at.Header)!), pack.Read(dataFile));
        var t = peg.Textures.First(x => string.Equals(Stem(x.Name), texture, StringComparison.OrdinalIgnoreCase));
        int levels = t.MipLevels;
        if (t.Format.IsBlockCompressed())
        {
            levels = 1;
            while (levels < t.MipLevels && Math.Min(t.Width >> levels, t.Height >> levels) >= 4) levels++;
        }
        var (header, data) = peg.Single(t, name + ".tga", levels);
        return [(name + ".cvbm_pc", header), (name + ".gvbm_pc", data)];
    }

    /// <summary>An archive, or a container in it, opened (the last one kept, as textures are asked for container by container).</summary>
    Packfile Open(string archive, string? container)
    {
        if (lastPack is { } last && last.Archive == archive && last.Container == container) return last.Pack;
        var pack = Packfile.Open(Path.Combine(folder, archive));
        if (container != null) pack = Packfile.FromBytes(container, pack.Read(pack.Find(container)!));
        lastPack = (archive, container, pack);
        return pack;
    }

    static bool IsHeader(string file) =>
        file.EndsWith(".cpeg_pc", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cvbm_pc", StringComparison.OrdinalIgnoreCase);

    static string Stem(string texture) => texture.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? texture[..^4] : texture;
}
