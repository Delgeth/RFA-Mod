using System.Numerics;
using System.Text;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's undergrowth meshes (the grass and dust rocks its zones' terrain files
/// scatter: <see cref="GuerrillaTerrain.Undergrowth"/>) made into Armageddon's foliage meshes
/// (.cfmesh_pc), which the game's undergrowth code loads by the names the zones give
/// (dust_fol_rock_01.fmeshx is dust_fol_rock_01.cfmesh_pc). Guerrilla's are file version 4 with a
/// version-5 mesh block; Armageddon's loader takes the file's version 4 or 5 but only version-9 blocks,
/// so each is written again as Armageddon's own (f_cm_trash_a and the rest):
///   the file's header (0x40 bytes): its magic, version 5, the mesh block at 0x40, the material map,
///     the material offsets, 1 material, the texture names, the detail levels' count and offset
///   the mesh block, version 9, its buffers inside it (no g-file): its submeshes (Guerrilla's 44 bytes
///     and 4 run-time bytes more), render blocks, then from multiples of 16 the indices (a triangle
///     list, as Guerrilla's) and the vertices (format 30, the same 24 bytes as Guerrilla's 20:
///     position, normal, colour, UV; the normal's 4th byte 0, as Armageddon's), 8 zero bytes and the
///     checksum (any value, at +4 too)
///   from a multiple of 16 the material map (one material), its offset, and the material: our foliage
///     shader (<see cref="ShaderName"/>), Guerrilla's material name, flags 0x40 with Guerrilla's
///     alpha-test bit (2) and both sides drawn (0x80) where Guerrilla's are (its 0x40), its one texture
///     in Diffuse_Map (under its new name), Diffuse_Color as Guerrilla's Diffuse_Color_F, no specular
///     colour, Foliage_color and Foliage_alphaClamp as Guerrilla's, Specular_Power 1
///   the texture name, then from a multiple of 16 Guerrilla's detail levels (24 bytes each) as they are.
/// </summary>
public static class GuerrillaFoliage
{
    /// <summary>The foliage shader the meshes are drawn with (our effect, gw_foliage_s / _bs; see <see cref="GuerrillaShaders"/>).</summary>
    public const string ShaderName = "gw_foliage";

    const uint Magic = 0x0FF1C1A1;
    static uint H(string name) => NameHashes.PropertyHash(name);

    /// <summary>Armageddon's foliage mesh for Guerrilla's, and the name of the texture its material uses (Guerrilla's, without ".tga"), <paramref name="rename"/> giving its new name.</summary>
    public static (byte[] Mesh, string Texture) Convert(byte[] g, Func<string, string> rename)
    {
        uint U(int at) => BitConverter.ToUInt32(g, at);
        int I(int at) => BitConverter.ToInt32(g, at);
        if (U(0) != Magic || U(4) != 4 || U(8) != 0x40 || U(20) != 1)
            throw new InvalidDataException("not one of Guerrilla's undergrowth meshes (a version-4 .cfmesh_pc with its mesh block at 0x40 and one material)");
        int materialOffsets = I(16), textureNames = I(24), lodCount = I(28), lods = I(32);

        // Guerrilla's version-5 block.
        const int m = 0x40;
        if (U(m) != 5) throw new InvalidDataException($"the mesh block is version {U(m)}, not 5");
        int size5 = I(m + 4), submeshes = I(m + 8), table = I(m + 0x0C), vertices = I(m + 0x10);
        byte vertexSize = g[m + 0x14], format = g[m + 0x15], uvSets = g[m + 0x16];
        int vertexOffset = I(m + 0x18), indexCount = I(m + 0x1C), indexOffset = I(m + 0x20);
        byte indexSize = g[m + 0x24], topology = g[m + 0x25];
        int renderBlocks = BitConverter.ToUInt16(g, m + 0x26);
        if ((vertexSize, format, uvSets, indexSize, topology) != (24, 20, 1, 2, 1))
            throw new InvalidDataException($"an undergrowth mesh with {vertexSize}-byte vertices of format {format}, {uvSets} UV sets, {indexSize}-byte indices, topology {topology}");
        var submeshBytes = Enumerable.Range(0, submeshes).Select(s => g[(m + table + 44 * s)..(m + table + 44 * s + 44)]).ToList();
        if (submeshBytes.Sum(s => BitConverter.ToInt32(s, 0)) != renderBlocks) throw new InvalidDataException("the submeshes' render blocks don't add up");
        int blocksAt = m + table + 44 * submeshes;
        int indicesAt = m + indexOffset + 0x40, verticesAt = m + vertexOffset + 0x40;  // counted from the block's +0x40
        if (indicesAt != To(blocksAt + 20 * renderBlocks, 16) || verticesAt != To(indicesAt + 2 * indexCount, 16))
            throw new InvalidDataException("the mesh block's buffers aren't where its counts put them");
        byte[] vertexData = g[verticesAt..(verticesAt + 24 * vertices)];
        for (int v = 0; v < vertices; v++) vertexData[24 * v + 15] = 0;

        // The version-9 block.
        var b = new List<byte>(new byte[0x60]);
        uint checksum = 2166136261;
        foreach (byte x in g.AsSpan(m, size5)) checksum = (checksum ^ x) * 16777619;
        if (checksum == 0) checksum = 1;
        void Put(List<byte> to, int at, uint value) { var bytes = BitConverter.GetBytes(value); for (int k = 0; k < 4; k++) to[at + k] = bytes[k]; }
        void Pad(List<byte> to, int multiple) { while (to.Count % multiple != 0) to.Add(0); }
        Put(b, 0, 9);
        Put(b, 4, checksum);
        Put(b, 0x10, 0);                    // buffers inside the block
        Put(b, 0x14, (uint)submeshes);
        Put(b, 0x18, 0x60);
        Put(b, 0x1C, (uint)vertices);
        b[0x20] = 24; b[0x21] = 30; b[0x22] = 1; b[0x23] = 0;
        Put(b, 0x24, BitConverter.SingleToUInt32Bits(1)); Put(b, 0x28, BitConverter.SingleToUInt32Bits(1)); Put(b, 0x2C, BitConverter.SingleToUInt32Bits(1));
        foreach (byte[] s in submeshBytes) { b.AddRange(s); b.AddRange(new byte[4]); }
        for (int r = 0; r < renderBlocks; r++)
        {
            byte[] block = g[(blocksAt + 20 * r)..(blocksAt + 20 * r + 20)];
            block[2] = block[3] = 0;        // the half after the material's number, unused
            b.AddRange(block);
        }
        Pad(b, 16);
        int indices9 = b.Count;
        b.AddRange(g.AsSpan(indicesAt, 2 * indexCount).ToArray());
        Pad(b, 16);
        int vertices9 = b.Count;
        b.AddRange(vertexData);
        b.AddRange(new byte[8]);            // version 8 and later: a count (0) and a run-time word
        Pad(b, 4);
        b.AddRange(BitConverter.GetBytes(checksum));
        Put(b, 8, (uint)b.Count);
        Put(b, 0x3C, (uint)vertices9); Put(b, 0x40, (uint)indexCount); Put(b, 0x44, (uint)indices9);
        b[0x48] = 2; b[0x49] = 1; b[0x4A] = (byte)renderBlocks; b[0x4B] = (byte)(renderBlocks >> 8);

        // Guerrilla's material: its texture (one) and constants.
        int mat = I(materialOffsets);
        uint guerrillaFlags = U(mat + 0x0C), materialName = U(mat + 8);
        int textureAt = textureNames + I(mat + 0x20);
        string texture = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(g, textureAt, Array.IndexOf(g, (byte)0, textureAt) - textureAt));
        int constantCount = g[mat + 0x12], hashesAt = mat + 0x20 + 12 * BitConverter.ToUInt16(g, mat + 0x10);
        int valuesAt = mat + To(hashesAt + 4 * constantCount - mat, 16);
        var constants = new Dictionary<uint, Vector4>();
        for (int c = 0; c < constantCount; c++)
            constants[U(hashesAt + 4 * c)] = new(BitConverter.ToSingle(g, valuesAt + 16 * c), BitConverter.ToSingle(g, valuesAt + 16 * c + 4),
                                                 BitConverter.ToSingle(g, valuesAt + 16 * c + 8), BitConverter.ToSingle(g, valuesAt + 16 * c + 12));
        Vector4 Get(string name, Vector4 otherwise) => constants.TryGetValue(H(name), out var v) ? v : otherwise;
        (uint Hash, Vector4 Value)[] ours =
        [
            (H("Diffuse_Color"), Get("Diffuse_Color_F", Vector4.One)),
            (H("Specular_Color"), Vector4.Zero),
            (H("Foliage_color"), Get("Foliage_color", Vector4.One)),
            (H("Foliage_alphaClamp"), Get("Foliage_alphaClamp", Vector4.Zero)),
            (H("Specular_Power"), new(1, 0, 0, 0)),
        ];
        uint flags = 0x40 | (guerrillaFlags & 2) | ((guerrillaFlags & 0x40) != 0 ? 0x80u : 0);
        const int room = 13;
        int ourHashes = 0x24 + 12, ourValues = To(ourHashes + 4 * ours.Length, 16);
        var material = new List<byte>(new byte[ourValues + 16 * room]);
        Put(material, 0, (uint)material.Count);
        Put(material, 4, H(ShaderName));
        Put(material, 8, materialName);
        Put(material, 0x0C, flags);
        material[0x10] = 1; material[0x11] = 0; material[0x12] = (byte)ours.Length; material[0x13] = room;
        Put(material, 0x24, 0); Put(material, 0x28, H("Diffuse_Map")); Put(material, 0x2C, 0);
        for (int k = 0; k < ours.Length; k++)
        {
            Put(material, ourHashes + 4 * k, ours[k].Hash);
            float[] v = [ours[k].Value.X, ours[k].Value.Y, ours[k].Value.Z, ours[k].Value.W];
            for (int j = 0; j < 4; j++) Put(material, ourValues + 16 * k + 4 * j, BitConverter.SingleToUInt32Bits(v[j]));
        }

        // The file.
        var o = new List<byte>(new byte[0x40]);
        o.AddRange(b);
        Pad(o, 16);
        int materialMap = o.Count;
        o.AddRange(new byte[16]); Put(o, materialMap + 4, 1);
        int offsets = o.Count;
        o.AddRange(new byte[4]);
        Pad(o, 16);
        Put(o, offsets, (uint)o.Count);
        o.AddRange(material);
        int names = o.Count;
        string name = rename(texture) + ".tga";
        o.AddRange(Encoding.ASCII.GetBytes(name));
        o.AddRange(new byte[((name.Length + 3) & ~1) - name.Length]);
        Pad(o, 16);
        int ourLods = o.Count;
        o.AddRange(g.AsSpan(lods, 24 * lodCount).ToArray());
        uint[] header = [Magic, 5, 0x40, (uint)materialMap, (uint)offsets, 1, (uint)names, (uint)lodCount, (uint)ourLods];
        for (int k = 0; k < header.Length; k++) Put(o, 4 * k, header[k]);
        return ([.. o], texture);
    }

    static int To(int p, int multiple) => (p + multiple - 1) / multiple * multiple;
}
