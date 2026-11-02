namespace RfaFormats.Effects;

/// <summary>
/// An effect (.cefct_pc: a visual effect template, "71BW"; the meshes' vertices in the .gefct_pc),
/// Armageddon's version 36. The file is one block the game loads whole and points into: every
/// reference in it is an offset from the file's start (−1 for none), which the game's loader
/// (0xB6C300) turns into a pointer, walking the parts below; the parts' own data (names, curves,
/// keys) lies after them, pointed to. Little-endian. The header (Guerrilla's vfx_effect_template::
/// vfx_data, its 32-bit layout):
///   +0x00 signature, +0x04 version, +0x08 expressions (count, offset), +0x10 the name's offset
///   ("&lt;name&gt;.effectx", in the name area from 0x50: 0x50 bytes in version 36, 0x40 in Guerrilla's),
///   +0x14 duration, +0x18 radius, +0x1C bitmaps (count, offset of a list of the textures' names'
///   offsets), +0x24 the meshes' bitmaps (offset; the file's end where it has no meshes), +0x28
///   meshes, +0x30 emitters, +0x38 lights, +0x40 filters, +0x48 coronas (count, offset each)
/// Each part (sizes and the offsets in it as the loader fixes them; every one of the game's 639
/// effects walks so, each offset inside its file):
///   mesh 0x80 bytes: +0x00, +0x0C, +0x14, +0x1C, +0x24, +0x30, +0x34, +0x40, +0x44, and at +0x28 a
///     list (count at +0x2C) of 0x14-byte entries with offsets at +0x00, +0x04, +0x10
///   emitter 0x40 bytes: +0x00, +0x04, +0x1C, and +0x18 its data block (0x2E8 bytes) with offsets at
///     +0x1DC and every 8 bytes from +0x204 to +0x2E4
///   light 0x94 bytes: +0x00, +0x0C, +0x14, +0x1C, +0x24, +0x4C, +0x68, +0x70
///   filter 0x40 bytes: +0x00, +0x18, +0x20, +0x28, +0x30, +0x3C
///   corona 0x108 bytes: +0x00, +0x38, and every 8 bytes from +0x48 to +0x100
///   expression 0x14 bytes: a list at +0x10 (count at +0x0C) of 0x0C-byte parameters, each with an
///     offset at +0x08
/// The materials (a corona's at its +0x04, an emitter's where its +0x1C points) hold offsets too,
/// which the loader turns into pointers as it loads each material (0xB6F820), not in its walk (see
/// <see cref="MaterialOffsetFields"/>). An emitter's material is 0xA8 bytes, Guerrilla's 0x30 and
/// 0x78 of Armageddon's own (its own files' materials lie 0xA8 apart), which the game reads as part
/// of it: a count at +0x30 and an offset at +0x34 (0 for none, else turned into a pointer too).
/// </summary>
public sealed class EffectFile
{
    public const uint Signature = 0x57423137;  // "71BW"
    public const int Version = 36;

    /// <summary>A list of parts: where its count and offset are (in the header or the part holding it), each part's size, the offsets in it, the lists in it, and blocks it points to.</summary>
    sealed record Part(int CountAt, int OffsetAt, int Size, int[] Offsets, Part[] Lists, Block[] Blocks);

    /// <summary>A block an offset in a part points to (at <paramref name="OffsetAt"/>), and the offsets in it.</summary>
    sealed record Block(int OffsetAt, int Size, int[] Offsets);

    static readonly int[] HeaderOffsets = [0x0C, 0x10, 0x20, 0x24, 0x2C, 0x34, 0x3C, 0x44, 0x4C];

    static readonly Part[] Parts =
    [
        new(0x1C, 0x20, 4, [0x00], [], []),  // the bitmaps' names
        new(0x28, 0x2C, 0x80, [0x00, 0x0C, 0x14, 0x1C, 0x24, 0x28, 0x30, 0x34, 0x40, 0x44], [new(0x2C, 0x28, 0x14, [0x00, 0x04, 0x10], [], [])], []),
        new(0x30, 0x34, 0x40, [0x00, 0x04, 0x18, 0x1C], [], [new(0x18, 0x2E8, [0x1DC, .. Enumerable.Range(0, 29).Select(k => 0x204 + k * 8)])]),
        new(0x38, 0x3C, 0x94, [0x00, 0x0C, 0x14, 0x1C, 0x24, 0x4C, 0x68, 0x70], [], []),
        new(0x40, 0x44, 0x40, [0x00, 0x18, 0x20, 0x28, 0x30, 0x3C], [], []),
        new(0x48, 0x4C, 0x108, [0x00, 0x38, .. Enumerable.Range(0, 24).Select(k => 0x48 + k * 8)], [], []),
        new(0x08, 0x0C, 0x14, [0x10], [new(0x0C, 0x10, 0x0C, [0x08], [], [])], []),
    ];

    const uint None = 0xFFFFFFFF;

    byte[] bytes;

    EffectFile(byte[] bytes) => this.bytes = bytes;

    /// <summary>Reads a version-36 effect (the bytes are copied).</summary>
    public static EffectFile Read(byte[] bytes)
    {
        if (bytes.Length < 0x50 || BitConverter.ToUInt32(bytes, 0) != Signature) throw new InvalidDataException("not an effect file");
        if (BitConverter.ToInt32(bytes, 4) != Version) throw new NotSupportedException($"an effect of version {BitConverter.ToInt32(bytes, 4)}, not {Version}");
        var file = new EffectFile((byte[])bytes.Clone());
        foreach (int at in file.OffsetFields())
            if (BitConverter.ToUInt32(file.bytes, at) is var value && value != None && value > bytes.Length)
                throw new InvalidDataException($"the offset at 0x{at:X} points past the file's end");
        return file;
    }

    /// <summary>The effect's name (without ".effectx").</summary>
    public string Name
    {
        get
        {
            int at = BitConverter.ToInt32(bytes, 0x10), end = Array.IndexOf(bytes, (byte)0, at);
            string name = System.Text.Encoding.ASCII.GetString(bytes, at, (end < 0 ? bytes.Length : end) - at);
            return name.EndsWith(".effectx", StringComparison.OrdinalIgnoreCase) ? name[..^8] : name;
        }
    }

    /// <summary>Where each offset the game turns into a pointer is, walking the parts in the loader's order.</summary>
    public IReadOnlyList<int> OffsetFields()
    {
        var fields = new List<int>(HeaderOffsets);
        foreach (var part in Parts) Walk(part, 0, fields);
        return fields;
    }

    void Walk(Part part, int holder, List<int> fields)
    {
        int count = BitConverter.ToInt32(bytes, holder + part.CountAt);
        uint at = BitConverter.ToUInt32(bytes, holder + part.OffsetAt);
        if (count <= 0 || at == None) return;
        if (at + (long)count * part.Size > bytes.Length) throw new InvalidDataException($"a list at 0x{at:X} runs past the file's end");
        for (int k = 0; k < count; k++)
        {
            int element = (int)at + k * part.Size;
            foreach (int field in part.Offsets) fields.Add(element + field);
            foreach (var list in part.Lists) Walk(list, element, fields);
            foreach (var block in part.Blocks)
            {
                uint target = BitConverter.ToUInt32(bytes, element + block.OffsetAt);
                if (target == None) continue;
                if (target + block.Size > bytes.Length) throw new InvalidDataException($"a block at 0x{target:X} runs past the file's end");
                foreach (int field in block.Offsets) fields.Add((int)target + field);
            }
        }
    }

    /// <summary>
    /// Where each material is: the coronas' (in them, at +0x04) and the emitters' (where an emitter's
    /// +0x1C points).
    /// </summary>
    public IReadOnlyList<int> Materials()
    {
        var materials = new List<int>();
        void Each(int countAt, int offsetAt, int size, Action<int> element)
        {
            int count = BitConverter.ToInt32(bytes, countAt);
            uint at = BitConverter.ToUInt32(bytes, offsetAt);
            if (count <= 0 || at == None) return;
            for (int k = 0; k < count; k++) element((int)at + k * size);
        }
        Each(0x48, 0x4C, 0x108, corona => materials.Add(corona + 0x04));
        Each(0x30, 0x34, 0x40, emitter =>
        {
            uint material = BitConverter.ToUInt32(bytes, emitter + 0x1C);
            if (material != None) materials.Add((int)material);
        });
        return materials;
    }

    /// <summary>
    /// The offsets in the materials: from the file's start as the others, −1 for none, but turned into
    /// pointers as each material loads rather than in the loader's walk (Re-Mars-tered's et_ptr_offset
    /// counts from the field itself, its own files' change). A material (rl_particle_material_data, 0x30 bytes): +0x06
    /// its textures' count and +0x08 their offset (12 bytes each), +0x0C its shader constants' count and
    /// +0x10 their offset (32 bytes each, each with its keyframes' offset at +0x18 and count at +0x1C),
    /// the vertex shader's constants' references and engine constants (+0x14, +0x1C, counts at +0x18,
    /// +0x1A) and the pixel shader's (+0x20, +0x28, counts at +0x24, +0x26), its flags at +0x2C.
    /// </summary>
    public IReadOnlyList<int> MaterialOffsetFields()
    {
        var fields = new List<int>();
        foreach (int material in Materials())
        {
            foreach (int field in new[] { 0x08, 0x10, 0x14, 0x1C, 0x20, 0x28 }) fields.Add(material + field);
            int constants = BitConverter.ToUInt16(bytes, material + 0x0C);
            uint at = BitConverter.ToUInt32(bytes, material + 0x10);
            if (constants == 0 || at == None || at + (long)constants * 32 > bytes.Length) continue;
            for (int k = 0; k < constants; k++) fields.Add((int)at + k * 32 + 0x18);
        }
        // An emitter's material's own offset at +0x34 (0 for none, below anything Insert moves).
        int emitters = BitConverter.ToInt32(bytes, 0x30);
        uint heads = BitConverter.ToUInt32(bytes, 0x34);
        for (int k = 0; k < emitters && heads != None; k++)
        {
            uint material = BitConverter.ToUInt32(bytes, (int)heads + k * 0x40 + 0x1C);
            if (material != None && material + 0x38L <= bytes.Length) fields.Add((int)material + 0x34);
        }
        return fields;
    }

    /// <summary>Makes room for <paramref name="count"/> zero bytes at <paramref name="at"/>: what was there moves on, and every offset to it or past it with it.</summary>
    public void Insert(int at, int count)
    {
        if (at < 0x50 || at > bytes.Length) throw new ArgumentOutOfRangeException(nameof(at));
        // Every field found before any changes (the materials are found through offsets that move), each
        // once (emitters can share a data block, whose offsets the walk meets for each).
        foreach (int field in OffsetFields().Concat(MaterialOffsetFields()).Distinct().ToList())
        {
            uint value = BitConverter.ToUInt32(bytes, field);
            if (value != None && value >= at) BitConverter.TryWriteBytes(bytes.AsSpan(field), value + (uint)count);
        }
        bytes = [.. bytes.AsSpan(0, at), .. new byte[count], .. bytes.AsSpan(at)];
    }

    /// <summary>The file's bytes.</summary>
    public byte[] ToBytes() => (byte[])bytes.Clone();
}
