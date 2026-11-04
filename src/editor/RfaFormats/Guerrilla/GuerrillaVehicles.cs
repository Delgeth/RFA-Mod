using RfaFormats.Meshes;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's vehicle models (.ccar_pc version 27) written as Armageddon's (version 30).
/// The g-file (vertices and indices) stays as it is: both games use vertex format 2, 24 to 32 bytes.
/// Both files are one structure, the vehicle's "subinfo", with offsets from the file's start where it
/// points; Armageddon only added fields. Checked on the
/// civilian walker, which both games ship (Armageddon's own conversion of the same model):
///   header (0xA8 → 0x148)      +0x00–0x43 the same: version, seat-to-door links, tag deformation
///                              count and offsets, collision, MOPP count and table, constraint
///                              templates, mesh block, material map, materials, material count,
///                              texture names, morph target, chassis flags and submesh. Armageddon
///                              then has 0x90 bytes of its own (+0x88 a count of name-hash pairs,
///                              none here). Guerrilla's +0x44–0x93 (chassis tags, centre of mass and
///                              inertia, deformed morph target, light count and lights) at +0xD4.
///                              Armageddon then has two more arrays (count, offset; none here), and
///                              Guerrilla's splines, tread width, +0xA0 and the part count follow.
///   parts (240 → 264 each)     +0x00–0x7F the same: type, durability, mass type, flags, parent,
///                              children, rotation, position, bone name (40 bytes), submesh, tags.
///                              Armageddon then has an array of its own (count, offset: on its
///                              suspension pistons), Guerrilla's light count and lights, two more
///                              arrays (none here), Guerrilla's hinge; Guerrilla's +0x8C–0xEF
///                              (deformation offsets, morph target, sub-pieces, centre of mass and
///                              inertia) at +0xA4.
///   lights (76 → 80 each)      the same, and a word Armageddon fills at run time: the chassis's,
///                              then each part's, before the tag lists
///   tag lists                  the same (a count, then 52 bytes a tag: name hash, rotation, position)
///   mesh block (128)           version 4 or 6 → 9 (<see cref="MeshBlockConversion"/>)
///   material map (16)          the same (run-time words: one, the count, one a material)
///   materials (16)             their offsets, then the blocks, each rebuilt for one of Armageddon's
///                              shaders (<see cref="GuerrillaMaterials"/>)
///   texture names (16)         the names the materials use (renamed if asked), two NULs after each
///   the rest (16)              the same: the parts' children, the seat-to-door links, the doors'
///                              hinges (28 bytes each), the tag deformation offsets
///   collision (16)             "MCKH": Havok 5.5 → 2010 (<see cref="ChunkCollision"/>), the shapes in
///                              the same order
///   MOPP table and trees (16)  the same: an offset or −1 for each collision shape, then the trees
///                              (Volition's own "MOPP" wrapper, the same in both games)
/// Every offset is moved to where its target lands. Not carried over: the morph target that dents a
/// Guerrilla vehicle (46 of its 74 have one; none of Armageddon's 17 does, so the converted vehicle
/// keeps its shape), and tracked vehicles' splines (their files are refused for now).
/// </summary>
public static class GuerrillaVehicles
{
    const int GuerrillaVersion = 27, Version = 30;
    const int GuerrillaHeader = 0xA8, Header = 0x148;
    const int GuerrillaPart = 240, Part = 264;
    const int GuerrillaLight = 76, Light = 80;
    const int TagSize = 52;

    /// <summary>
    /// The Armageddon c-file for a Guerrilla one, and the textures its materials name (as Guerrilla
    /// names them, without ".tga"; built-ins among them). With <paramref name="rename"/>, the file
    /// names each texture by what it gives for Guerrilla's name instead.
    /// </summary>
    public static (byte[] CFile, IReadOnlyList<string> Textures) Convert(byte[] file, Func<string, string>? rename = null)
    {
        try
        {
            return ConvertFile(file, rename ?? (name => name));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("the file's sections run past its end");
        }
    }

    /// <summary>Why a Guerrilla vehicle file can't be converted yet, or null if it can.</summary>
    public static string? NotYet(byte[] file)
    {
        int I(int at) => BitConverter.ToInt32(file, at);
        if (file.Length < GuerrillaHeader || I(0) != GuerrillaVersion) return "not a Guerrilla vehicle file (version 27)";
        if (I(0x94) != 0) return "a tracked vehicle (its tread splines aren't converted yet)";
        if (I(0x1C) != 0) return "it has constraint templates (not converted yet)";
        return null;
    }

    static (byte[], IReadOnlyList<string>) ConvertFile(byte[] g, Func<string, string> rename)
    {
        int I(int at) => BitConverter.ToInt32(g, at);
        if (NotYet(g) is { } why) throw new NotSupportedException(why);
        int partCount = I(0xA4), partsEnd = GuerrillaHeader + partCount * GuerrillaPart;
        int mesh = I(0x24), materialMap = I(0x28), materialList = I(0x2C), materialCount = I(0x30), names = I(0x34);
        int collision = I(0x10), moppCount = I(0x14), moppTable = I(0x18);
        if (partCount is < 0 or > 1000 || materialCount is < 0 or > 1000) throw new InvalidDataException("bad part or material count");

        var o = new Output();
        var moved = new List<(int From, int To, int At)>();  // Guerrilla ranges copied as they are, and where they went
        int Moved(int offset)
        {
            foreach (var (from, to, at) in moved)
                if (offset >= from && offset < to) return at + offset - from;
            throw new InvalidDataException($"an offset (0x{offset:X}) points at nothing copied");
        }
        void Copy(int from, int to)
        {
            moved.Add((from, to, o.Count));
            o.Add(g.AsSpan(from, to - from));
        }

        // The header.
        o.Add(g.AsSpan(0, 0x44));
        o.Add(new byte[0xD4 - 0x44]);
        o.Add(g.AsSpan(0x44, 0x94 - 0x44));
        o.Add(BitConverter.GetBytes(0)); o.Add(BitConverter.GetBytes(-1));
        o.Add(BitConverter.GetBytes(0)); o.Add(BitConverter.GetBytes(-1));
        o.Add(g.AsSpan(0x94, GuerrillaHeader - 0x94));
        o.Put(0x00, Version);

        // The parts.
        for (int p = 0; p < partCount; p++)
        {
            int r = GuerrillaHeader + p * GuerrillaPart;
            o.Add(g.AsSpan(r, 0x80));
            o.Add(BitConverter.GetBytes(0)); o.Add(BitConverter.GetBytes(0));        // Armageddon's own array
            o.Add(g.AsSpan(r + 0x80, 8));                                            // lights: count, offset
            o.Add(BitConverter.GetBytes(0)); o.Add(BitConverter.GetBytes(-1));       // two more arrays
            o.Add(BitConverter.GetBytes(0)); o.Add(BitConverter.GetBytes(-1));
            o.Add(g.AsSpan(r + 0x88, 4));                                            // hinge
            o.Add(g.AsSpan(r + 0x8C, GuerrillaPart - 0x8C));
        }

        // The lights and tag lists, up to the mesh block: each light gets a word more.
        var lightArrays = new List<(int At, int Count)>();
        if (I(0x8C) > 0) lightArrays.Add((I(0x90), I(0x8C)));
        var tagLists = new List<int> { I(0x44) };
        for (int p = 0; p < partCount; p++)
        {
            int r = GuerrillaHeader + p * GuerrillaPart;
            if (I(r + 0x80) > 0) lightArrays.Add((I(r + 0x84), I(r + 0x80)));
            tagLists.Add(I(r + 0x78));
        }
        foreach (var (at, count) in lightArrays)
            for (int l = 0; l < count; l++)
                tagLists.AddRange([I(at + l * GuerrillaLight + 0x38), I(at + l * GuerrillaLight + 0x44)]);
        int tagsEnd = tagLists.Where(t => t > 0).Max(t => t + 4 + I(t) * TagSize);
        int cursor = partsEnd;
        foreach (var (at, count) in lightArrays.OrderBy(a => a.At))
        {
            if (at < cursor || at + count * GuerrillaLight > mesh) throw new InvalidDataException("the lights aren't between the parts and the mesh block");
            if (at > cursor) Copy(cursor, at);
            for (int l = 0; l < count; l++)
            {
                Copy(at + l * GuerrillaLight, at + (l + 1) * GuerrillaLight);
                o.Add(BitConverter.GetBytes(0));
            }
            cursor = at + count * GuerrillaLight;
        }
        if (tagsEnd > mesh) throw new InvalidDataException("the tag lists run into the mesh block");
        if (tagsEnd > cursor) Copy(cursor, tagsEnd);

        // The mesh block.
        o.Align(128);
        int newMesh = o.Count;
        o.Add(MeshBlockConversion.FromVersion6(g, mesh));

        // The material map, the materials' offsets and the materials.
        o.Align(16);
        int newMaterialMap = o.Count;
        o.Add(g.AsSpan(materialMap, (materialCount + 2) * 4));
        o.Align(16);
        int newMaterialList = o.Count;
        o.Add(new byte[materialCount * 4]);
        o.Align(16);
        int namesEnd = Following(g, names, partCount);
        var nameList = new TextureNameList(g.AsSpan(names, namesEnd - names));
        var materials = new List<byte[]>();
        for (int k = 0; k < materialCount; k++) materials.Add(GuerrillaMaterials.Convert(g, I(materialList + k * 4), nameList, vehicle: true));
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
            o.Put(newMaterialList + k * 4, o.Count);
            o.Add(materials[k]);
        }

        // The texture names.
        o.Align(16);
        int newNames = o.Count;
        o.Add(finalNames.ToArray());

        // The rest, up to the collision, as it is.
        o.Align(16);
        if (namesEnd % 16 != 0 || collision % 16 != 0 || moppTable % 16 != 0) throw new InvalidDataException("a section isn't on a 16-byte boundary");
        if (namesEnd < collision) Copy(namesEnd, collision);

        // The collision, Havok 5.5 → 2010.
        o.Align(16);
        int newCollision = o.Count;
        if (System.Text.Encoding.ASCII.GetString(g, collision, 4) != "MCKH") throw new InvalidDataException("no collision where the header says");
        var shapes = ChunkCollision.ReadPackfile(g, collision + 16);
        if (shapes.Count != I(collision + 12)) throw new InvalidDataException($"the collision has {shapes.Count} shapes where its header says {I(collision + 12)}");
        o.Add(ChunkCollision.Section(shapes));

        // The MOPP table and trees, as they are.
        o.Align(16);
        if (moppTable < collision + I(collision + 8)) throw new InvalidDataException("the MOPP table isn't after the collision");
        Copy(moppTable, g.Length);
        o.Align(16);

        // Every offset moved to where its target landed (0 and −1 mean none).
        int Offset(int value) => value is 0 or -1 ? value : Moved(value);
        o.Put(0x04, Offset(I(0x04)));
        o.Put(0x0C, Offset(I(0x0C)));
        o.Put(0x10, newCollision);
        o.Put(0x18, Offset(moppTable));
        o.Put(0x24, newMesh);
        o.Put(0x28, newMaterialMap);
        o.Put(0x2C, newMaterialList);
        o.Put(0x34, newNames);
        o.Put(0x38, 0);                              // no morph target
        o.Put(0xD4, Moved(I(0x44)));                 // the chassis's tags
        o.Put(0x118, -1);                            // no deformed morph target
        if (I(0x8C) > 0) o.Put(0x120, Moved(I(0x90)));
        for (int p = 0; p < partCount; p++)
        {
            int r = GuerrillaHeader + p * GuerrillaPart, a = Header + p * Part;
            if (I(r + 0x14) > 0) o.Put(a + 0x18, Moved(I(r + 0x18)));
            o.Put(a + 0x78, Moved(I(r + 0x78)));
            if (I(r + 0x80) > 0) o.Put(a + 0x8C, Moved(I(r + 0x84)));
            if (I(r + 0x88) != -1) o.Put(a + 0xA0, Moved(I(r + 0x88)));
            o.Put(a + 0xC0, -1);                     // no deformed morph target
        }
        foreach (var (at, count) in lightArrays)
            for (int l = 0; l < count; l++)
            {
                int light = at + l * GuerrillaLight, newLight = Moved(light);
                o.Put(newLight + 0x38, Offset(I(light + 0x38)));
                o.Put(newLight + 0x44, Offset(I(light + 0x44)));
            }
        int newMoppTable = Moved(moppTable);
        for (int m = 0; m < moppCount; m++)
            if (I(moppTable + m * 4) != -1) o.Put(newMoppTable + m * 4, Moved(I(moppTable + m * 4)));
        return (o.ToArray(), textures);
    }

    /// <summary>
    /// Where the texture names end: at the next section after them (the parts' children, the seat-to-door
    /// links, the doors' hinges, the morph target, the tag deformation offsets or the collision).
    /// </summary>
    static int Following(byte[] g, int names, int partCount)
    {
        int I(int at) => BitConverter.ToInt32(g, at);
        var starts = new List<int> { I(0x04), I(0x0C), I(0x10), I(0x38) };
        for (int p = 0; p < partCount; p++)
        {
            int r = GuerrillaHeader + p * GuerrillaPart;
            if (I(r + 0x14) > 0) starts.Add(I(r + 0x18));
            starts.Add(I(r + 0x88));
        }
        return starts.Where(s => s > names).Min();
    }

    /// <summary>A Guerrilla texture's name in Armageddon (its name's hash, as the world's: so a texture both use is one).</summary>
    public static string Alias(string texture) => "rfg_" + NameHashes.PropertyHash(texture).ToString("x8");

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
