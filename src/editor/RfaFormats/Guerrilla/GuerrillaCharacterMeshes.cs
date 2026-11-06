using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's character meshes (.ccmesh_pc version 3, with their .gcmesh_pc) written as
/// Armageddon's (version 7): its people's bodies and heads, and the items that are character meshes
/// (some guns, backpacks, armbands). The g-file (vertex and index buffers) stays as it is: both games
/// use vertex format 3 for characters (position, normal, tangent, four bone weights and numbers, the
/// UV sets), and a vertex's bone numbers are its skeleton's own in both (Guerrilla's bone list is the
/// identity in all 365 of its files). The skeleton stays Guerrilla's too: Armageddon's character code
/// takes each character's bones from that character's own rig.
///
///   version 3 (0x40-byte header)                 version 7 (0x80-byte header)
///   +0x08 mesh block, +0x0C material map,        the same at the same places
///   +0x10/+0x14 materials, +0x18 texture names
///   +0x1C detail levels, +0x20 → a submesh each   the same; +0x24 → how many submeshes each (1),
///                                                +0x28 → a visibility id a submesh (0: always shown)
///   +0x24 bone count, +0x28 → bone list          +0x2C, +0x30
///   +0x2C u16 spheres, u16 cylinders             +0x34
///   +0x30 → spheres (24 B), +0x34 → cylinders    +0x38 → spheres (28 B), +0x3C → cylinders (44 B): a
///     (40 B): the hit volumes                      word more each (0 on Armageddon's people); the radius
///                                                  and height in metres, Guerrilla's in inches
///                                                +0x40/+0x44, +0x48/+0x4C two record lists, empty on
///                                                  Armageddon's NPCs (0 and −1)
///   mesh block version 4 or 6, no bone palette   version 9 with one palette (see <see cref="MeshBlock"/>)
///   materials (textures from +0x20)              Armageddon's (from +0x24), on Guerrilla's own shaders'
///                                                  rewrites (<see cref="GuerrillaMaterials"/>), skinned
///
/// Checked against Armageddon's own 3-detail-level characters (ch_red_ass_assault, monolith_*), whose
/// layout is the one Guerrilla's three levels convert to (no character mesh is in both games).
/// </summary>
public static class GuerrillaCharacterMeshes
{
    const uint Signature = 0xFAC351A9, MaterialFileMagic = 0xB01DFACE;
    const int GuerrillaVersion = 3, Version = 7, GuerrillaHeader = 0x40, Header = 0x80;
    const int GuerrillaSphere = 24, GuerrillaCylinder = 40, Sphere = 28, Cylinder = 44;

    /// <summary>
    /// Guerrilla's hit volumes give their radius and height in inches (a thigh's 4.331 is Armageddon's
    /// 0.110 m exactly), their positions in metres.
    /// </summary>
    const float InchesInAMetre = 39.37f;

    /// <summary>The most bones a palette holds: the skinned shaders' Bone_weights[64].</summary>
    public const int MostBones = 64;

    /// <summary>
    /// Why a character mesh can't be converted (yet), or null if it can: Guerrilla's version, at most
    /// <see cref="MostBones"/> bones.
    /// </summary>
    public static string? NotYet(byte[] file)
    {
        if (file.Length < GuerrillaHeader || BitConverter.ToUInt32(file, 0) != Signature) return "not a character mesh";
        if (BitConverter.ToInt32(file, 4) != GuerrillaVersion) return $"version {BitConverter.ToInt32(file, 4)}, not Guerrilla's {GuerrillaVersion}";
        int bones = BitConverter.ToInt32(file, 0x24);
        if (bones > MostBones) return $"{bones} bones, more than a palette holds ({MostBones})";
        return null;
    }

    /// <summary>
    /// Armageddon's .ccmesh_pc for Guerrilla's <paramref name="file"/> (its .gcmesh_pc goes with it as it is),
    /// and the textures its materials name (Guerrilla's names, without ".tga"): its own and any built-in
    /// one the materials' shaders want and Guerrilla's material hasn't (<see cref="GuerrillaBuiltIns"/>).
    /// The materials name each by <paramref name="rename"/>'s name for it (the name its file is packed
    /// under: <see cref="GuerrillaVehicles.Alias"/>, as the world's and the vehicles' textures).
    /// <paramref name="notSkinned"/>, if given, gets each material the skinned rewrites of Guerrilla's shaders
    /// can't draw yet (<see cref="GuerrillaShaders.WriteSkinned"/>): it would be drawn by Armageddon's own.
    /// <paramref name="materialFile"/>, if given (a Guerrilla .mat_pc, version 1), dresses the mesh: each of its
    /// materials takes the place of the mesh's own of the same name, as Guerrilla's renderer draws a head
    /// (its base head with the face's materials).
    /// </summary>
    public static (byte[] Header, IReadOnlyList<string> Textures) Convert(byte[] file, Func<string, string> rename, List<string>? notSkinned = null,
                                                                       byte[]? materialFile = null)
    {
        if (NotYet(file) is { } why) throw new InvalidDataException(why);
        int I(int at) => BitConverter.ToInt32(file, at);
        int mesh = I(0x08), materialMap = I(0x0C), materialList = I(0x10), materialCount = I(0x14), names = I(0x18);
        int lodCount = I(0x1C), lods = I(0x20), boneCount = I(0x24), bones = I(0x28);
        int sphereCount = BitConverter.ToUInt16(file, 0x2C), cylinderCount = BitConverter.ToUInt16(file, 0x2E);
        int spheres = I(0x30), cylinders = I(0x34);
        int submeshes = I(mesh + 0x10);
        for (int l = 0; l < lodCount; l++)
        {
            int first = I(lods + l * 4);
            if (first < 0 || first >= submeshes) throw new InvalidDataException($"detail level {l} names submesh {first} of {submeshes}");
        }

        var o = new Output();
        o.Add(new byte[Header]);

        // The mesh block, with its bone palette.
        o.Align(16);
        int newMesh = o.Count;
        o.Add(MeshBlock(file, mesh, boneCount));

        // The material map (a run-time word, the count, a run-time word each), the materials' offsets, the materials.
        o.Align(16);
        int newMaterialMap = o.Count;
        o.Add(file.AsSpan(materialMap, (materialCount + 2) * 4));
        o.Align(16);
        int newMaterialList = o.Count;
        o.Add(new byte[materialCount * 4]);
        o.Align(16);
        // The texture names run from their offset to the next section (the detail levels' list).
        int namesEnd = new[] { lods, bones, sphereCount > 0 ? spheres : int.MaxValue, cylinderCount > 0 ? cylinders : int.MaxValue }.Where(x => x > names).Min();
        var nameList = new TextureNameList(file.AsSpan(names, namesEnd - names));
        // The material file's materials by name (+0x08), with its texture names (to its end).
        var dressing = new Dictionary<uint, int>();
        TextureNameList? dressingNames = null;
        if (materialFile != null)
        {
            int M(int at) => BitConverter.ToInt32(materialFile, at);
            if (materialFile.Length < 0x20 || BitConverter.ToUInt32(materialFile, 0) != MaterialFileMagic || M(4) != 1)
                throw new InvalidDataException("not a Guerrilla material file (version 1)");
            for (int k = 0; k < M(0x10); k++) dressing.TryAdd(BitConverter.ToUInt32(materialFile, M(M(0x0C) + k * 4) + 0x08), M(M(0x0C) + k * 4));
            dressingNames = new TextureNameList(materialFile.AsSpan(M(0x14)));
        }
        var materials = new List<(byte[] Block, TextureNameList Names)>();
        for (int k = 0; k < materialCount; k++)
        {
            int at = I(materialList + k * 4);
            var (source, from, sourceNames) = dressing.TryGetValue(BitConverter.ToUInt32(file, at + 0x08), out int dressed)
                ? (materialFile!, dressed, dressingNames!) : (file, at, nameList);
            materials.Add((GuerrillaMaterials.Convert(source, from, sourceNames, guerrillaShaders: true), sourceNames));
            if (notSkinned != null && GuerrillaMaterials.NotSkinned(source, from) is { } problem) notSkinned.Add($"material {k}: {problem}");
        }
        var textures = new List<string>();
        var finalNames = new TextureNameList([]);
        foreach (var (m, materialNames) in materials)
            for (int t = 0; t < BitConverter.ToUInt16(m, 0x10); t++)
            {
                string name = materialNames.At(BitConverter.ToInt32(m, 0x24 + t * 12));
                name = name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                if (!textures.Contains(name, StringComparer.OrdinalIgnoreCase)) textures.Add(name);
                BitConverter.TryWriteBytes(m.AsSpan(0x24 + t * 12), finalNames.Add(rename(name) + ".tga"));
            }
        for (int k = 0; k < materialCount; k++)
        {
            o.Put(newMaterialList + k * 4, o.Count);
            o.Add(materials[k].Block);
            o.Align(16);
        }
        o.Align(16);
        int newNames = o.Count;
        o.Add(finalNames.ToArray());
        o.Align(16);

        // The detail levels: a first submesh each (Guerrilla's), how many (one), and a visibility id a submesh (0).
        int newLods = o.Count;
        for (int l = 0; l < lodCount; l++) o.Add(BitConverter.GetBytes(I(lods + l * 4)));
        int newLodCounts = o.Count;
        for (int l = 0; l < lodCount; l++) o.Add(BitConverter.GetBytes(1));
        int newVisibility = o.Count;
        o.Add(new byte[submeshes]);
        o.Align(4);

        // The bones, as they are (the identity: the skeleton's own numbers).
        int newBones = o.Count;
        o.Add(file.AsSpan(bones, boneCount * 4));

        // The hit volumes: a word more each, the radius and height in metres.
        int newSpheres = o.Count;
        for (int s = 0; s < sphereCount; s++)
        {
            int at = spheres + s * GuerrillaSphere;
            o.Add(file.AsSpan(at, 20));                                                           // body part, bone, position
            o.Add(BitConverter.GetBytes(BitConverter.ToSingle(file, at + 20) / InchesInAMetre));  // radius
            o.Add(BitConverter.GetBytes(0));
        }
        int newCylinders = o.Count;
        for (int c = 0; c < cylinderCount; c++)
        {
            int at = cylinders + c * GuerrillaCylinder;
            o.Add(file.AsSpan(at, 32));                                                           // body part, bone, axis, position
            o.Add(BitConverter.GetBytes(BitConverter.ToSingle(file, at + 32) / InchesInAMetre));  // radius
            o.Add(BitConverter.GetBytes(BitConverter.ToSingle(file, at + 36) / InchesInAMetre));  // height
            o.Add(BitConverter.GetBytes(0));
        }
        o.Align(16);

        o.Put(0x00, unchecked((int)Signature));
        o.Put(0x04, Version);
        o.Put(0x08, newMesh);
        o.Put(0x0C, newMaterialMap);
        o.Put(0x10, newMaterialList);
        o.Put(0x14, materialCount);
        o.Put(0x18, newNames);
        o.Put(0x1C, lodCount);
        o.Put(0x20, newLods);
        o.Put(0x24, newLodCounts);
        o.Put(0x28, newVisibility);
        o.Put(0x2C, boneCount);
        o.Put(0x30, newBones);
        o.PutShort(0x34, sphereCount);
        o.PutShort(0x36, cylinderCount);
        o.Put(0x38, newSpheres);
        o.Put(0x3C, newCylinders);
        o.Put(0x40, 0);
        o.Put(0x44, -1);
        o.Put(0x48, 0);
        o.Put(0x4C, -1);
        return (o.ToArray(), textures);
    }

    /// <summary>
    /// A character's mesh block (version 4 or 6) as Armageddon's version 9 with one bone palette that
    /// keeps the skeleton's numbers (palette entry n is bone n, for the <paramref name="bones"/> bones):
    ///   +0x00 version, checksum, block size, g size   as the static blocks' (<see cref="MeshBlockConversion"/>)
    ///   +0x10 3 (every character of Armageddon's; its static meshes have 1), submeshes, table, vertices
    ///   +0x20 vertex size, format, UV sets, 0, then 1 1 1 0 0 0 (floats)
    ///   +0x3C vertex offset, index count, index offset; +0x48 index size, topology, render blocks
    ///   +0x4C the palettes' bytes, a run-time word, how many palettes (1), a run-time word
    ///   +0x5C the palette (a bone a byte), then each palette's size and first byte (2 bytes)
    ///   the submesh table at the next 16 (48 bytes a submesh), 20 bytes a render block,
    ///   then a u32 a render block, its palette (0), 8 zero bytes, the checksum.
    /// </summary>
    static byte[] MeshBlock(byte[] file, int at, int bones)
    {
        int I(int o) => BitConverter.ToInt32(file, at + o);
        if (I(0) is not (6 or 4)) throw new InvalidDataException($"mesh block version {I(0)}, not 6 or 4");
        if (bones is < 1 or > MostBones) throw new InvalidDataException($"{bones} bones");
        int submeshes = I(0x10), table = I(0x14), blocks = BitConverter.ToUInt16(file, at + 0x2E);
        uint checksum = BitConverter.ToUInt32(file, at + 4);
        if (BitConverter.ToUInt32(file, at + table + submeshes * 44 + blocks * 20) != checksum)
            throw new InvalidDataException("the mesh block doesn't end with its checksum where its counts say");

        int table9 = (0x5C + bones + 2 + 15) / 16 * 16;
        int size = table9 + submeshes * 48 + blocks * 20 + blocks * 4 + 8 + 4;
        var b = new byte[size];
        void Put(int o, int v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);
        Put(0x00, 9);
        BitConverter.TryWriteBytes(b.AsSpan(0x04), checksum);
        Put(0x08, size);
        Put(0x0C, I(0x0C));                       // the g-file's size
        Put(0x10, 3);
        Put(0x14, submeshes);
        Put(0x18, table9);
        Put(0x1C, I(0x18));                       // vertices
        Array.Copy(file, at + 0x1C, b, 0x20, 4);  // vertex size, format, UV sets, 0
        foreach (int o in new[] { 0x24, 0x28, 0x2C }) BitConverter.TryWriteBytes(b.AsSpan(o), 1f);
        Put(0x3C, I(0x20));                       // vertex offset (in the g-file)
        Put(0x40, I(0x24));                       // index count
        Put(0x44, I(0x28));                       // index offset
        Array.Copy(file, at + 0x2C, b, 0x48, 4);  // index size, topology, render blocks
        Put(0x4C, bones);                         // the palettes' bytes
        Put(0x54, 1);                             // one palette
        for (int n = 0; n < bones; n++) b[0x5C + n] = (byte)n;
        b[0x5C + bones] = (byte)bones;            // palette 0: its size, its first byte
        b[0x5C + bones + 1] = 0;
        for (int s = 0; s < submeshes; s++)
            Array.Copy(file, at + table + s * 44, b, table9 + s * 48, 44);
        Array.Copy(file, at + table + submeshes * 44, b, table9 + submeshes * 48, blocks * 20);
        // Each render block's palette (all the one), then 8 zero bytes: already zero.
        BitConverter.TryWriteBytes(b.AsSpan(size - 4), checksum);
        return b;
    }

    /// <summary>The file being written.</summary>
    sealed class Output
    {
        readonly List<byte> bytes = [];
        public int Count => bytes.Count;
        public void Add(ReadOnlySpan<byte> data) => bytes.AddRange(data);
        public void Align(int to) { while (bytes.Count % to != 0) bytes.Add(0); }
        public void Put(int at, int value) { for (int k = 0; k < 4; k++) bytes[at + k] = (byte)(value >> (8 * k)); }
        public void PutShort(int at, int value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); }
        public byte[] ToArray() => [.. bytes];
    }
}
