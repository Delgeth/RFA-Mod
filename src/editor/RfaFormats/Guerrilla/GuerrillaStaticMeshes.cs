using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's static meshes (.csmesh_pc version 4: guns, hammers, clips, pickups, turret
/// parts) written as Armageddon's (version 6), and their high-detail material files (.mat_pc version
/// 1 → 2). The g-file (vertices and indices) stays as it is: both games use vertex format 2.
///
///   version 4 (Guerrilla)                     version 6 (Armageddon)
///   +0x00 0xC0FFEE11, version                  the same
///   +0x08 mesh block, material map,            the same
///         material offsets, material count,
///         texture names, 1
///   +0x20 a word (0)                           the same
///   +0x24 the tags: a count, 52 bytes a tag    +0x24 a word (1 in all of Armageddon's)
///         (name hash, rotation, position)      +0x28 the tags, the same
///   +0x28 0                                    +0x2C 0
///   +0x2C embedded Havok, or −1                +0x30 Armageddon's collision, or −1
///   mesh block (16)                            version 4 → 9 (<see cref="MeshBlockConversion"/>)
///   material map (16)                          the same (run-time words: one, the count, one a material)
///   material offsets (16), materials           each rebuilt for one of Armageddon's shaders
///                                              (<see cref="GuerrillaMaterials"/>)
///   texture names (16)                         the names the materials use (renamed if asked)
/// Checked against both games' guns (Guerrilla's assault_rifle, Armageddon's ppr_lod and nano_rifle):
/// the tags carry the same names in both (weapon_handle, secondary_handle, muzzle_flash), which is
/// where the game puts the gun in the hand and the flash at the muzzle.
///
/// A high-detail material file (in a gun's "_high" container with its big textures: the material the
/// model wears when it is close, the player's own gun) is the material part alone: 0xB01DFACE, version
/// 1 (Armageddon 2), material map, material offsets, count, texture names, then the same blocks.
/// </summary>
public static class GuerrillaStaticMeshes
{
    const uint Magic = 0xC0FFEE11, MaterialMagic = 0xB01DFACE;
    const int GuerrillaVersion = 4, Version = 6, MaterialVersion = 2, TagSize = 52;

    /// <summary>Why a Guerrilla static mesh can't be converted yet, or null if it can.</summary>
    public static string? NotYet(byte[] file)
    {
        if (file.Length < 0x40 || BitConverter.ToUInt32(file, 0) != Magic || BitConverter.ToInt32(file, 4) != GuerrillaVersion)
            return "not a Guerrilla static mesh (version 4)";
        if (BitConverter.ToInt32(file, 0x2C) != -1) return "it has its own collision (Guerrilla's embedded Havok; not converted yet)";
        return null;
    }

    /// <summary>
    /// The Armageddon c-file for a Guerrilla static mesh, and the textures its materials name (as Guerrilla
    /// names them, without ".tga"; built-ins among them). With <paramref name="rename"/>, the file names
    /// each texture by what it gives for Guerrilla's name instead.
    /// </summary>
    public static (byte[] CFile, IReadOnlyList<string> Textures) Convert(byte[] g, Func<string, string>? rename = null)
    {
        if (NotYet(g) is { } why) throw new NotSupportedException(why);
        int I(int at) => BitConverter.ToInt32(g, at);
        int mesh = I(0x08), materialMap = I(0x0C), materialList = I(0x10), materialCount = I(0x14), names = I(0x18);
        int word = I(0x20), tags = I(0x24);
        if (materialCount is < 0 or > 1000) throw new InvalidDataException("bad material count");
        int tagCount = I(tags);
        if (tagCount is < 0 or > 1000 || tags + 4 + tagCount * TagSize > g.Length) throw new InvalidDataException("bad tag list");

        var o = new Output();
        o.Add(g.AsSpan(0, 0x40));
        o.Put(0x04, Version);

        // The mesh block.
        int newMesh = o.Count;
        o.Add(MeshBlockConversion.FromVersion6(g, mesh));

        // The material map, the materials' offsets, the materials and the texture names.
        var (newMaterialMap, newMaterialList, newNames, textures) = Materials(o, g, materialMap, materialList, materialCount, names, word, rename);

        // The word, Armageddon's word, and the tags.
        o.Align(16);
        int newWord = o.Count;
        o.Add(g.AsSpan(word, 4));
        int newWord2 = o.Count;
        o.Add(BitConverter.GetBytes(1));
        int newTags = o.Count;
        o.Add(g.AsSpan(tags, 4 + tagCount * TagSize));

        o.Put(0x08, newMesh);
        o.Put(0x0C, newMaterialMap);
        o.Put(0x10, newMaterialList);
        o.Put(0x18, newNames);
        o.Put(0x20, newWord);
        o.Put(0x24, newWord2);
        o.Put(0x28, newTags);
        o.Put(0x2C, 0);
        o.Put(0x30, -1);
        return (o.ToArray(), textures);
    }

    /// <summary>
    /// The Armageddon material file (version 2) for a Guerrilla one (version 1), and the textures it names
    /// (as <see cref="Convert"/>).
    /// </summary>
    public static (byte[] File, IReadOnlyList<string> Textures) ConvertMaterialFile(byte[] g, Func<string, string>? rename = null)
    {
        int I(int at) => BitConverter.ToInt32(g, at);
        if (g.Length < 0x20 || BitConverter.ToUInt32(g, 0) != MaterialMagic || I(4) != 1)
            throw new NotSupportedException("not a Guerrilla material file (version 1)");
        int materialMap = I(0x08), materialList = I(0x0C), materialCount = I(0x10), names = I(0x14);
        if (materialCount is < 0 or > 1000) throw new InvalidDataException("bad material count");
        var o = new Output();
        o.Add(g.AsSpan(0, 0x20));
        o.Put(0x04, MaterialVersion);
        var (newMaterialMap, newMaterialList, newNames, textures) = Materials(o, g, materialMap, materialList, materialCount, names, g.Length, rename);
        o.Align(16);
        o.Put(0x08, newMaterialMap);
        o.Put(0x0C, newMaterialList);
        o.Put(0x14, newNames);
        return (o.ToArray(), textures);
    }

    /// <summary>
    /// Writes the material map, the material offsets, the materials (rebuilt for Armageddon's shaders) and
    /// the texture names (Guerrilla's run from <paramref name="names"/> to <paramref name="namesEnd"/>).
    /// </summary>
    static (int Map, int List, int Names, IReadOnlyList<string> Textures) Materials(Output o, byte[] g, int materialMap, int materialList,
        int materialCount, int names, int namesEnd, Func<string, string>? rename)
    {
        int I(int at) => BitConverter.ToInt32(g, at);
        rename ??= name => name;
        o.Align(16);
        int newMap = o.Count;
        o.Add(g.AsSpan(materialMap, (materialCount + 2) * 4));
        o.Align(16);
        int newList = o.Count;
        o.Add(new byte[materialCount * 4]);
        o.Align(16);
        if (namesEnd < names) throw new InvalidDataException("the texture names aren't before what follows them");
        var nameList = new TextureNameList(g.AsSpan(names, namesEnd - names));
        var materials = new List<byte[]>();
        for (int k = 0; k < materialCount; k++) materials.Add(GuerrillaMaterials.Convert(g, I(materialList + k * 4), nameList, guerrillaShaders: true));
        var textures = new List<string>();
        var finalNames = new TextureNameList([]);
        foreach (byte[] m in materials)
            for (int t = 0; t < BitConverter.ToUInt16(m, 0x10); t++)
            {
                string name = nameList.At(BitConverter.ToInt32(m, 0x24 + t * 12));
                name = name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                if (!textures.Contains(name, StringComparer.OrdinalIgnoreCase)) textures.Add(name);
                BitConverter.TryWriteBytes(m.AsSpan(0x24 + t * 12), finalNames.Add(rename(name) + ".tga"));
            }
        for (int k = 0; k < materialCount; k++)
        {
            o.Put(newList + k * 4, o.Count);
            o.Add(materials[k]);
        }
        o.Align(16);
        int newNames = o.Count;
        o.Add(finalNames.ToArray());
        return (newMap, newList, newNames, textures);
    }

    /// <summary>The file being written.</summary>
    sealed class Output
    {
        readonly List<byte> bytes = [];
        public int Count => bytes.Count;
        public void Add(ReadOnlySpan<byte> data) => bytes.AddRange(data);
        public void Align(int to) { while (bytes.Count % to != 0) bytes.Add(0); }
        public void Put(int at, int value) { byte[] b = BitConverter.GetBytes(value); for (int k = 0; k < 4; k++) bytes[at + k] = b[k]; }
        public byte[] ToArray() => [.. bytes];
    }
}
