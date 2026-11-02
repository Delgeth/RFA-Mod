using System.Numerics;
using System.Text;
using RfaFormats.Effects;
using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's effects (.cefct_pc versions 21 to 26, and their meshes' .gefct_pc) written
/// as Armageddon's (version 36, read by <see cref="EffectFile"/>). Both are one block of parts and
/// their data, every reference an offset from the file's start. The file is rebuilt from pieces: a
/// piece starts wherever an offset points and runs to the next such place; the pieces still used are
/// copied, each from a multiple of 16 as in both games' files, and every offset set to where its piece
/// lands. An offset counts only where its list isn't empty (Guerrilla's empty lists often keep an
/// offset into other data) and isn't −1 or 0 (none to Guerrilla's loader); none is written −1. What
/// changes:
///   the header      version 36; the lists pointed at their new places
///   texture names   the effect's bitmaps' names and the meshes' name list renamed as asked
///   emitters        0x2D4 bytes each → a 0x40-byte head, a 0x2E8-byte data block and the particle
///                   material (inline in Guerrilla's, pointed to in Armageddon's, and 0x78 bytes longer
///                   there); see <see cref="Emitter"/>
///   lights          0x3C → 0x94: Guerrilla's first, the type numbered as Armageddon does, then
///                   Armageddon's commonest values and two key lists of its own (one key: 0 and 1)
///   meshes          0x78 → 0x80 (8 bytes more at +0x28: a list of Armageddon's, none); the mesh
///                   block version 4 or 6 → 9 (<see cref="MeshBlockConversion"/>), the materials
///                   rebuilt (<see cref="GuerrillaMaterials"/>), the collision Havok 5.5 → 2010
///                   (<see cref="ChunkCollision"/>)
///   coronas         the same, but for Guerrilla's loader's fixes below
///   expressions, filters, keys and the rest: as they are
/// Guerrilla's loader takes some values otherwise in older files, which Armageddon's (refusing anything
/// under 36) doesn't, so this does: under 23 no coronas; under 25 every mesh's type 1 (normal); under
/// 26 each corona's position offset one key of 0 (and in 26 a single key of 1 made 0); under 22 the
/// emitters' self-illumination 1 and ambient and diffuse colours black (Armageddon keeps them after
/// the emitter's material; see <see cref="MaterialTail"/>).
///
/// The g-file: each mesh's part in turn (its block's checksum, 12 zeros, the indices, the vertices
/// from a multiple of 16, the checksum), each part from a multiple of 16, the block's offsets inside
/// its part: copied as it is, but for Guerrilla's 20-byte vertices (format 0: position, normal, one UV
/// set), which Armageddon's effect meshes never have and its unlit shaders for them read as format 2
/// with two UV sets: a tangent added and the UV set doubled (28 bytes).
/// </summary>
public static class GuerrillaEffects
{
    const uint None = 0xFFFFFFFF;
    const int GuerrillaEmitterSize = 0x2D4, EmitterHeadSize = 0x40, EmitterBlockSize = 0x2E8;
    const int GuerrillaLightSize = 0x3C, LightSize = 0x94, GuerrillaMeshSize = 0x78, MeshSize = 0x80, FilterSize = 0x40, CoronaSize = 0x108;

    /// <summary>The particle material's offsets (rl_particle_material_data, 0x30 bytes): each with where its count is and the count's size.</summary>
    static readonly (int At, int Count, int Size)[] MaterialOffsets = [(0x08, 0x06, 2), (0x10, 0x0C, 2), (0x14, 0x18, 2), (0x1C, 0x1A, 2), (0x20, 0x24, 2), (0x28, 0x26, 2)];
    static readonly (int At, int Count, int Size)[] CoronaOffsets =
        [(0x00, 0, 0), (0x38, 0x3C, 4), (0x48, 0x40, 4), .. Enumerable.Range(0, 23).Select(k => (0x50 + k * 8, 0x4C + k * 8, 4))];
    static readonly (int At, int Count, int Size)[] FilterOffsets = [(0x00, 0, 0), (0x18, 0x14, 4), (0x20, 0x1C, 4), (0x28, 0x24, 4), (0x30, 0x2C, 4), (0x3C, 0x38, 4)];
    static readonly (int At, int Count, int Size)[] LightOffsets = [(0x00, 0, 0), (0x0C, 0x08, 4), (0x14, 0x10, 4), (0x1C, 0x18, 4), (0x24, 0x20, 4)];
    static readonly (int At, int Count, int Size)[] MeshKeyOffsets = [(0x00, 0, 0), (0x0C, 0x08, 4), (0x14, 0x10, 4), (0x1C, 0x18, 4), (0x24, 0x20, 4)];

    /// <summary>
    /// The Armageddon effect for a Guerrilla one: its c-file, its g-file (null when it has none), and the
    /// textures it uses by Guerrilla's names (without ".tga"; the built-ins its meshes' materials are
    /// given among them, see <see cref="GuerrillaBuiltIns"/>). <paramref name="rename"/> gives each
    /// texture's name as the converted textures will be called; <paramref name="effectName"/>, if
    /// given, is the effect's own (without ".effectx"; as its file and its visual effect entry name it).
    /// </summary>
    public static (byte[] C, byte[]? G, IReadOnlyList<string> Textures) Convert(byte[] c, byte[]? g, Func<string, string> rename, string? effectName = null)
    {
        if (c.Length < 0x50 || BitConverter.ToUInt32(c, 0) != EffectFile.Signature) throw new InvalidDataException("not an effect file");
        int version = BitConverter.ToInt32(c, 4);
        if (version is < 21 or > 26) throw new NotSupportedException($"a Guerrilla effect of version {version}");
        int I(int at) => BitConverter.ToInt32(c, at);
        uint U(int at) => BitConverter.ToUInt32(c, at);
        var used = new List<string>();
        string Name(string texture)
        {
            string stem = texture.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? texture[..^4] : texture;
            if (!used.Contains(stem, StringComparer.OrdinalIgnoreCase)) used.Add(stem);
            return Renamed(texture, rename);
        }

        var source = new Source(c, version);
        var header = new Piece(c[..0x50]);
        BitConverter.TryWriteBytes(header.Bytes.AsSpan(4), EffectFile.Version);
        var name = effectName != null ? new Piece([.. Encoding.ASCII.GetBytes(effectName + ".effectx"), 0])
            : source.Field(0x10) ?? throw new InvalidDataException("the effect has no name");
        header.Link(0x10, name);
        void List(int countAt, int count, Piece? piece)
        {
            BitConverter.TryWriteBytes(header.Bytes.AsSpan(countAt), count);
            header.Link(countAt + 4, count > 0 ? piece : null);
        }
        List(0x08, Math.Max(I(0x08), 0), source.Field(0x0C));
        List(0x40, Math.Max(I(0x40), 0), source.Field(0x44));

        // The bitmaps (particle textures), renamed.
        int bitmapCount = Math.Max(I(0x1C), 0);
        var bitmaps = new Piece(new byte[bitmapCount * 4]);
        for (int k = 0; k < bitmapCount; k++)
            bitmaps.Link(k * 4, new Piece([.. Encoding.ASCII.GetBytes(Name(source.Text(U((int)U(0x20) + k * 4)))), 0]));
        List(0x1C, bitmapCount, bitmaps);

        // Emitters.
        int emitterCount = Math.Max(I(0x30), 0);
        var heads = new Piece(new byte[emitterCount * EmitterHeadSize]);
        for (int k = 0; k < emitterCount; k++) Emitter(source, (int)U(0x34) + k * GuerrillaEmitterSize, heads, k * EmitterHeadSize);
        List(0x30, emitterCount, heads);

        // Lights.
        int lightCount = Math.Max(I(0x38), 0);
        var lights = new Piece(new byte[lightCount * LightSize]);
        Piece? zeroKey = null, oneKey = null;
        for (int k = 0; k < lightCount; k++)
        {
            int from = (int)U(0x3C) + k * GuerrillaLightSize, to = k * LightSize;
            Array.Copy(c, from, lights.Bytes, to, GuerrillaLightSize);
            foreach (var (o, _, _) in LightOffsets) lights.Link(to + o, source.Field(from + o));
            // Guerrilla's type (+0x06): 1 a point light, 0 a spot (its loader); Armageddon's (+0x04): 0
            // for lights as Guerrilla's points, 1 for its spots (all with cone angles). Guerrilla's flags
            // (+0x04: 0, or 0xF00 in two lights) have no place.
            BitConverter.TryWriteBytes(lights.Bytes.AsSpan(to + 0x04), BitConverter.ToUInt16(c, from + 0x06) == 1 ? 0 : 1);
            BitConverter.TryWriteBytes(lights.Bytes.AsSpan(to + 0x3C), 1f);
            BitConverter.TryWriteBytes(lights.Bytes.AsSpan(to + 0x40), 2f);
            lights.Link(to + 0x4C, null);
            BitConverter.TryWriteBytes(lights.Bytes.AsSpan(to + 0x64), 1);
            lights.Link(to + 0x68, zeroKey ??= Key(0, 0));
            BitConverter.TryWriteBytes(lights.Bytes.AsSpan(to + 0x6C), 1);
            lights.Link(to + 0x70, oneKey ??= Key(0, 1));
        }
        List(0x38, lightCount, lights);

        // Coronas (none under version 23).
        int coronaCount = version < 23 ? 0 : Math.Max(I(0x48), 0);
        var coronas = new Piece(new byte[coronaCount * CoronaSize]);
        for (int k = 0; k < coronaCount; k++)
        {
            int from = (int)U(0x4C) + k * CoronaSize, to = k * CoronaSize;
            Array.Copy(c, from, coronas.Bytes, to, CoronaSize);
            // The position offset: its count at +0x8C, keys at +0x90 (a time and a value each).
            int keys = I(from + 0x8C);
            var keysAt = source.Field(from + 0x90);
            bool oneKeyOfOne = keys == 1 && keysAt != null && BitConverter.ToSingle(keysAt.Bytes, 4) == 1;
            foreach (var (o, _, _) in CoronaOffsets)
                if (o != 0x90 || !(version < 26 || oneKeyOfOne)) coronas.Link(to + o, source.Field(from + o));
            foreach (var (o, _, _) in MaterialOffsets) coronas.Link(to + 0x04 + o, source.Field(from + 0x04 + o));
            if (version < 26 || oneKeyOfOne)
            {
                BitConverter.TryWriteBytes(coronas.Bytes.AsSpan(to + 0x8C), 1);
                coronas.Link(to + 0x90, Key(keysAt != null && keys > 0 ? BitConverter.ToSingle(keysAt.Bytes, 0) : 0, 0));
            }
        }
        List(0x48, coronaCount, coronas);

        // Meshes, and the g-file.
        int meshCount = Math.Max(I(0x28), 0);
        var meshes = new Piece(new byte[meshCount * MeshSize]);
        Piece? meshNames = null;
        byte[]? gOut = null;
        if (meshCount > 0)
        {
            if (g == null) throw new InvalidDataException("an effect with meshes but no g-file");
            int area = (int)U(0x24);
            var names = new TextureNameList(c.AsSpan(area, c.Length - area));
            var blocks = new List<(Piece Piece, int At)>();  // every material block, for its texture names
            var g9 = new List<byte>();
            int gAt = 0;
            for (int k = 0; k < meshCount; k++)
            {
                int from = (int)U(0x2C) + k * GuerrillaMeshSize, to = k * MeshSize;
                Array.Copy(c, from, meshes.Bytes, to, 0x28);
                foreach (var (o, _, _) in MeshKeyOffsets) meshes.Link(to + o, source.Field(from + o));
                if (version < 25) BitConverter.TryWriteBytes(meshes.Bytes.AsSpan(to + 0x04), 1);
                meshes.Link(to + 0x28, null);
                Array.Copy(c, from + 0x34, meshes.Bytes, to + 0x3C, 4);                        // the materials' count
                meshes.Link(to + 0x40, source.Field(from + 0x38));                            // their run-time words
                Array.Copy(c, from + 0x40, meshes.Bytes, to + 0x48, GuerrillaMeshSize - 0x40);  // shape: material, volume, centre, inertia

                // The mesh block, and its part of the g-file.
                int block = (int)U(from + 0x28);
                byte[] mesh = MeshBlockConversion.FromVersion6(c, block);
                int gSize = I(block + 0x0C);
                gAt = (gAt + 15) / 16 * 16;
                if (gAt + gSize > g.Length) throw new InvalidDataException("a mesh's part runs past the g-file's end");
                byte[] part = g[gAt..(gAt + gSize)];
                gAt += gSize;
                if (c[block + 0x1D] == 0) part = WithTangents(part, mesh);
                while (g9.Count % 16 != 0) g9.Add(0);
                g9.AddRange(part);
                meshes.Link(to + 0x30, new Piece(mesh));

                // The materials: a run-time word, the count, a run-time word each, then the blocks from a multiple of 16.
                int map = (int)U(from + 0x2C), count = I(map + 4), first = map + (8 + count * 4 + 15) / 16 * 16;
                var materials = new List<byte>(c.AsSpan(map, 8 + count * 4).ToArray());
                var starts = new List<int>();
                for (int m = 0, at = first; m < count; m++, at += I(at))
                {
                    while (materials.Count % 16 != 0) materials.Add(0);
                    starts.Add(materials.Count);
                    materials.AddRange(GuerrillaMaterials.ConvertForEffect(c, at, names));
                }
                var mapPiece = new Piece([.. materials]);
                blocks.AddRange(starts.Select(s => (mapPiece, s)));
                meshes.Link(to + 0x34, mapPiece);

                int collision = (int)U(from + 0x3C);
                meshes.Link(to + 0x44, new Piece(ChunkCollision.Section(ChunkCollision.ReadPackfile(c, collision + 16))));
            }
            gOut = [.. g9];

            // The meshes' texture names, renamed, and the materials pointed at them.
            var final = new TextureNameList([]);
            foreach (var (piece, at) in blocks)
                for (int t = 0; t < BitConverter.ToUInt16(piece.Bytes, at + 0x10); t++)
                {
                    int e = at + 0x24 + t * 12;
                    BitConverter.TryWriteBytes(piece.Bytes.AsSpan(e), final.Add(Name(names.At(BitConverter.ToInt32(piece.Bytes, e)))));
                }
            meshNames = new Piece(final.ToArray());
        }
        List(0x28, meshCount, meshes);

        byte[] file = Layout(header, name, meshNames, out int end);
        if (meshNames == null) BitConverter.TryWriteBytes(file.AsSpan(0x24), end);  // none: the file's end, as Armageddon's
        else BitConverter.TryWriteBytes(file.AsSpan(0x24), meshNames.Place);
        try { EffectFile.Read(file); }  // every offset inside the file, every list fitting
        catch (InvalidDataException x)
        {
            string Where(int at, string what) => $"{what} {BitConverter.ToInt32(file, at - 4)} at 0x{BitConverter.ToUInt32(file, at):X}";
            throw new InvalidDataException($"{x.Message} (a file of 0x{file.Length:X} bytes: {Where(0x2C, "meshes")}, {Where(0x34, "emitters")}, {Where(0x3C, "lights")}, {Where(0x4C, "coronas")})", x);
        }
        return (file, gOut, used);
    }

    /// <summary>
    /// One emitter: Guerrilla's (at <paramref name="e"/>) as Armageddon's head (in <paramref name="heads"/>
    /// at <paramref name="head"/>), data block and particle material. Head: +0x00 name, +0x04 a mesh
    /// particle's data (Armageddon's own; none), +0x08 the shape's class id, +0x0C..+0x0F half res,
    /// quarter res (made half res), always on top, soft, +0x10 parent index, +0x18 the block, +0x1C the material. The
    /// block as <see cref="BlockCopy"/> says, its 28 key curves (count, offset) from +0x200 (Guerrilla's
    /// 27 with the texture animation rate after speed), emit-node emitters (+0x1D8, +0x1DC), unique
    /// data (+0x2E0 size, +0x2E4 offset: the shape's; Armageddon's cylinder-or-plane 0xFAA8D302 has a
    /// third float, 0 in all of its own), and Armageddon's own values where Guerrilla has none.
    /// </summary>
    static void Emitter(Source source, int e, Piece heads, int head)
    {
        byte[] c = source.File;
        int I(int at) => BitConverter.ToInt32(c, at);
        heads.Link(head + 0x00, source.Field(e));
        heads.Link(head + 0x04, null);
        Array.Copy(c, e + 0x38, heads.Bytes, head + 0x08, 4);
        Array.Copy(c, e + 0x3E, heads.Bytes, head + 0x0C, 4);
        // Armageddon's renderer has no quarter-res particle pass (Guerrilla's draws those at a quarter
        // and composites them onto the half-res one; none of Armageddon's own emitters sets it), so a
        // quarter-res emitter is drawn at half res, the nearest it has.
        if (heads.Bytes[head + 0x0D] != 0) (heads.Bytes[head + 0x0C], heads.Bytes[head + 0x0D]) = (1, 0);
        Array.Copy(c, e + 0x2C8, heads.Bytes, head + 0x10, 4);

        var material = new Piece([.. c.AsSpan(e + 0x04, 0x30), .. MaterialTail(c, e)]);
        foreach (var (o, _, _) in MaterialOffsets) material.Link(o, source.Field(e + 0x04 + o));
        heads.Link(head + 0x1C, material);

        var block = new Piece(new byte[EmitterBlockSize]);
        var b = block.Bytes;
        foreach (var (to, from, size) in BlockCopy) Array.Copy(c, e + from, b, to, size);
        b[0x02] = (byte)(I(e + 0x188) switch { 5 => 6, 6 => 5, var t => t });  // particle type: Guerrilla's nodes 5 and radial 6 swapped
        b[0x10] = (byte)Math.Clamp(I(e + 0x184), 0, 255);                       // tails
        // Guerrilla's use_length and use_length_mid_value: which of +0x14/+0x15 is which isn't known
        // (alike in every emitter both games have), so both on when the length is used.
        b[0x14] = b[0x15] = c[e + 0x19E];
        foreach (var (at, value) in Fixed) BitConverter.TryWriteBytes(b.AsSpan(at), value);
        for (int k = 0; k < 5; k++)
        {
            BitConverter.TryWriteBytes(b.AsSpan(0xE4 + k * 24 + 0x08), 0.33f);
            BitConverter.TryWriteBytes(b.AsSpan(0xE4 + k * 24 + 0x10), 0.66f);
        }
        for (int k = 0; k < 28; k++)
        {
            int from = k < 7 ? 0x1EC + k * 8 : k == 7 ? 0x160 : 0x1EC + (k - 1) * 8;
            Array.Copy(c, e + from, b, 0x200 + k * 8, 4);
            block.Link(0x204 + k * 8, source.Field(e + from + 4));
        }
        block.Link(0x1DC, source.Field(e + 0x180));
        int uniqueSize = Math.Max(I(e + 0x2CC), 0), extra = BitConverter.ToUInt32(c, e + 0x38) == 0xFAA8D302 ? 4 : 0;
        BitConverter.TryWriteBytes(b.AsSpan(0x2E0), uniqueSize + extra);
        var unique = source.Field(e + 0x2D0);
        block.Link(0x2E4, extra == 0 ? unique : new Piece([.. unique?.Bytes.AsSpan(0, uniqueSize).ToArray() ?? new byte[uniqueSize], .. new byte[extra]]));
        heads.Link(head + 0x18, block);
    }

    /// <summary>The emitter block's values copied from Guerrilla's emitter: (Armageddon's block offset, Guerrilla's offset, size).</summary>
    static readonly (int To, int From, int Size)[] BlockCopy =
    [
        (0x00, 0x3C, 1),   // is_iterative
        (0x04, 0x44, 4),   // iterations per second
        (0x08, 0x16C, 4),  // weight start
        (0x0C, 0x170, 4),  // weight end
        (0x12, 0x168, 1),  // rotation_enabled
        (0x13, 0x1B4, 1),  // is_random_start_rotation
        (0x16, 0x4B, 1),   // local_space_emission
        (0x17, 0x4A, 1),   // inherit_velocity
        (0x18, 0x84, 1),   // use_color_mid_value
        (0x19, 0x19C, 1),  // use_size_mid_value
        (0x1A, 0x86, 1),   // use_color_saturation_value
        (0x1B, 0x138, 1),  // use_additive_key_value
        (0x1C, 0x3D, 1),   // is_reverse_render
        (0x1D, 0x4C, 1),   // acceleration_enabled
        (0x1E, 0x68, 1),   // acceleration_variance_enabled
        (0x1F, 0x19F, 1),  // use_lifetime_variance
        (0x23, 0x85, 1),   // is_color_variance_by_value
        (0x24, 0x50, 12),  // acceleration start
        (0x30, 0x6C, 12),  // acceleration start variance
        (0x3C, 0x5C, 12),  // acceleration end
        (0x48, 0x78, 12),  // acceleration end variance
        (0x54, 0x88, 16),  // colour start
        (0x64, 0xC8, 16),  // colour mid A
        (0x74, 0xE8, 4),   // colour mid A offset
        (0x78, 0xEC, 16),  // colour mid B
        (0x88, 0x10C, 4),  // colour mid B offset
        (0x8C, 0xA8, 16),  // colour end
        (0x9C, 0x98, 16),  // colour start variance
        (0xAC, 0xD8, 16),  // colour mid A variance
        (0xBC, 0xFC, 16),  // colour mid B variance
        (0xCC, 0xB8, 16),  // colour end variance
        (0xDC, 0x110, 4),  // alpha mid A offset
        (0xE0, 0x114, 4),  // alpha mid B offset
        (0x160, 0x118, 4), // saturation start
        (0x164, 0x128, 4), // saturation mid A
        (0x16C, 0x130, 4), // saturation mid B
        (0x174, 0x120, 4), // saturation end
        (0x178, 0x11C, 4), // saturation start variance
        (0x17C, 0x12C, 4), // saturation mid A variance
        (0x180, 0x134, 4), // saturation mid B variance
        (0x184, 0x124, 4), // saturation end variance
        (0x188, 0x13C, 4), // additive start
        (0x18C, 0x14C, 4), // additive mid A
        (0x194, 0x154, 4), // additive mid B
        (0x19C, 0x144, 4), // additive end
        (0x1A0, 0x140, 4), // additive start variance
        (0x1A4, 0x150, 4), // additive mid A variance
        (0x1A8, 0x158, 4), // additive mid B variance
        (0x1AC, 0x148, 4), // additive end variance
        (0x1B0, 0x174, 4), // density
        (0x1B4, 0x178, 4), // density variance
        (0x1B8, 0x190, 4), // yaw variance
        (0x1BC, 0x194, 4), // pitch variance
        (0x1C0, 0x1A8, 4), // speed variance
        (0x1C4, 0x1AC, 4), // angular speed start variance
        (0x1C8, 0x1B0, 4), // angular speed end variance
        (0x1CC, 0x1A0, 4), // lifetime
        (0x1D0, 0x1A4, 4), // lifetime variance
        (0x1D4, 0x198, 4), // max particles
        (0x1D8, 0x17C, 4), // emit-node emitters' count
        (0x1FC, 0x1C0, 4), // LOD emission distance
    ];

    /// <summary>
    /// Armageddon's values where Guerrilla has none: the saturation and additive keys' mid offsets (0.25
    /// and 0.75, Guerrilla's fixed ones), +0x15C and +0x1F0 (1, as in nearly all of its own); and its own
    /// five keyed groups from +0xE4 (start, mid A, mid A offset, mid B, mid B offset, end: 0 0 0.33 0 0.66
    /// 0, written in <see cref="Emitter"/>).
    /// </summary>
    static readonly (int At, float Value)[] Fixed = [(0x168, 0.25f), (0x170, 0.75f), (0x190, 0.25f), (0x198, 0.75f), (0x15C, 1f), (0x1F0, 1f)];

    /// <summary>
    /// The 0x78 bytes that follow an emitter's material in Armageddon's files (its materials lie 0xA8
    /// apart; the game reads them as part of the material), from Guerrilla's emitter at <paramref name="e"/>,
    /// which keeps the same values (equal in nearly all the emitters both games have): +0x38 and +0x3C
    /// (Guerrilla's +0x1B8, +0x1BC; −1 for none), +0x40 the self-illumination (+0x1C4), the ambient and
    /// diffuse colours at +0x44 and +0x50 (+0x1C8, +0x1D4), +0x5C and +0x60 (+0x1E0, +0x1E4; the latter
    /// new in version 25, 0.8 in Armageddon's ports of older ones). Under version 22 Guerrilla's loader
    /// takes the self-illumination as 1 and the colours as black. +0x30 a count and +0x34 the offset of
    /// a list of Armageddon's own: none (0, as in most of its own); then zeros. Without these the game
    /// took the texture list after Guerrilla's 0x30 bytes for them, and turned a texture's handle into
    /// a pointer as an offset.
    /// </summary>
    static byte[] MaterialTail(byte[] c, int e)
    {
        var tail = new byte[0x78];
        int version = BitConverter.ToInt32(c, 4);
        void Copy(int at, int from, int size) => Array.Copy(c, e + from, tail, at - 0x30, size);
        Copy(0x38, 0x1B8, 8);
        if (version < 22) BitConverter.TryWriteBytes(tail.AsSpan(0x40 - 0x30), 1f);
        else Copy(0x40, 0x1C4, 0x1C);  // the self-illumination, ambient and diffuse colours
        Copy(0x5C, 0x1E0, 4);
        if (version < 25) BitConverter.TryWriteBytes(tail.AsSpan(0x60 - 0x30), 0.8f);
        else Copy(0x60, 0x1E4, 4);
        return tail;
    }

    /// <summary>A key list of one float key: its time and value.</summary>
    static Piece Key(float time, float value) => new([.. BitConverter.GetBytes(time), .. BitConverter.GetBytes(value)]);

    /// <summary>A texture's name renamed (a ".tga" kept on it).</summary>
    static string Renamed(string texture, Func<string, string> rename) =>
        texture.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? rename(texture[..^4]) + ".tga" : rename(texture);

    /// <summary>A mesh's part of the g-file with its 20-byte vertices (format 0) made 28 (format 2: a tangent added, the UV set twice); its version-9 block set to match.</summary>
    static byte[] WithTangents(byte[] part, byte[] mesh)
    {
        int vertices = BitConverter.ToInt32(mesh, 0x1C), at = BitConverter.ToInt32(mesh, 0x3C);
        if (mesh[0x20] != 20 || at + vertices * 20 + 4 != part.Length) throw new InvalidDataException("a format-0 mesh's part isn't laid out as expected");
        var o = new List<byte>(part.AsSpan(0, at).ToArray());
        for (int v = 0; v < vertices; v++)
        {
            int p = at + v * 20;
            o.AddRange(part.AsSpan(p, 16));                 // position, normal
            o.AddRange(Tangent(part.AsSpan(p + 12, 4)));
            o.AddRange(part.AsSpan(p + 16, 4));             // the UV set
            o.AddRange(part.AsSpan(p + 16, 4));             // and again
        }
        o.AddRange(part.AsSpan(part.Length - 4, 4));        // the checksum
        BitConverter.TryWriteBytes(mesh.AsSpan(0x0C), o.Count);
        mesh[0x20] = 28; mesh[0x21] = 2; mesh[0x22] = 2;
        return [.. o];
    }

    /// <summary>A tangent for a packed normal (bytes 0..255 for −1..1, as both games pack them): any direction across it.</summary>
    static byte[] Tangent(ReadOnlySpan<byte> normal)
    {
        var n = new Vector3(normal[0], normal[1], normal[2]) / 127.5f - Vector3.One;
        var t = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        if (!float.IsFinite(t.X)) t = Vector3.UnitX;
        static byte B(float x) => (byte)Math.Clamp(MathF.Round(x * 127.5f + 127.5f), 0, 255);
        return [B(t.X), B(t.Y), B(t.Z), 0xFF];
    }

    /// <summary>
    /// The file: the header at 0, the name after it, then every piece reachable from them, each the first
    /// time it is met (parts before their data) and from a multiple of 16; <paramref name="last"/> (the
    /// meshes' name list) at the end. <paramref name="end"/> is the file's length.
    /// </summary>
    static byte[] Layout(Piece header, Piece name, Piece? last, out int end)
    {
        var order = new List<Piece>();
        int at = 0;
        void Place(Piece p)
        {
            at = (at + 15) / 16 * 16;
            p.Place = at;
            at += p.Bytes.Length;
            order.Add(p);
        }
        Place(header);
        Place(name);
        var stack = new Stack<(Piece Piece, int Next)>();
        stack.Push((header, 0));
        while (stack.Count > 0)
        {
            var (piece, next) = stack.Pop();
            if (next >= piece.Links.Count) continue;
            stack.Push((piece, next + 1));
            var to = piece.Links[next].To;
            if (to.Place >= 0 || to == last) continue;
            Place(to);
            stack.Push((to, 0));
        }
        if (last != null) Place(last);
        end = at;
        var file = new byte[end];
        foreach (var p in order)
        {
            p.Bytes.CopyTo(file, p.Place);
            foreach (var (link, to) in p.Links) BitConverter.TryWriteBytes(file.AsSpan(p.Place + link), (uint)to.Place);
        }
        return file;
    }

    /// <summary>Part of the new file: its bytes, the offsets in it to other pieces, and where it lands.</summary>
    sealed class Piece(byte[] bytes)
    {
        public readonly byte[] Bytes = bytes;
        public readonly List<(int At, Piece To)> Links = [];
        public int Place = -1;

        /// <summary>An offset at <paramref name="at"/> to another piece, or none (−1).</summary>
        public void Link(int at, Piece? to)
        {
            Links.RemoveAll(l => l.At == at);
            if (to == null) BitConverter.TryWriteBytes(Bytes.AsSpan(at), None);
            else Links.Add((at, to));
        }
    }

    /// <summary>
    /// The Guerrilla file cut into pieces: every offset in it that counts (as version 26's parts lay
    /// them out, which all of 21 to 26 share), and a piece starting at each place one points to.
    /// </summary>
    sealed class Source
    {
        public readonly byte[] File;
        readonly int[] starts;
        readonly int[] fields;   // every offset field, counting or not
        readonly HashSet<int> live;
        readonly Dictionary<int, Piece> pieces = [];

        public Source(byte[] file, int version)
        {
            File = file;
            var list = new List<int>();
            var all = new List<int>();
            uint U(int at) => BitConverter.ToUInt32(file, at);
            int I(int at) => BitConverter.ToInt32(file, at);
            // An offset field; it counts when its list isn't empty (where it has a count) and it isn't none.
            void Add(int at, int countAt = 0, int countSize = 0)
            {
                all.Add(at);
                int count = countSize switch { 0 => 1, 1 => file[countAt], 2 => BitConverter.ToUInt16(file, countAt), _ => I(countAt) };
                if (count > 0 && U(at) is var v && v != None && v != 0)
                {
                    if (v > file.Length) throw new InvalidDataException($"the offset at 0x{at:X} points past the file's end");
                    list.Add(at);
                }
            }
            void AddAll(int element, (int At, int Count, int Size)[] offsets)
            {
                foreach (var (at, count, size) in offsets) Add(element + at, element + count, size);
            }
            void Each(int countAt, int size, Action<int> element)
            {
                int count = I(countAt);
                uint at = U(countAt + 4);
                if (count <= 0 || at == None || at == 0) return;
                if (at + (long)count * size > file.Length) throw new InvalidDataException($"a list at 0x{at:X} runs past the file's end");
                for (int k = 0; k < count; k++) element((int)at + k * size);
            }
            void Material(int m)
            {
                AddAll(m, MaterialOffsets);
                int constants = BitConverter.ToUInt16(file, m + 0x0C);
                uint at = U(m + 0x10);
                if (constants > 0 && at != None && at != 0 && at + (long)constants * 32 <= file.Length)
                    for (int k = 0; k < constants; k++) Add((int)at + k * 32 + 0x18, (int)at + k * 32 + 0x1C, 1);
            }
            Add(0x10);
            foreach (int at in new[] { 0x0C, 0x20, 0x2C, 0x34, 0x3C, 0x44, 0x4C }) Add(at, at - 4, 4);
            Each(0x08, 0x14, x =>
            {
                Add(x + 0x10, x + 0x0C, 4);
                int count = I(x + 0x0C);
                uint at = U(x + 0x10);
                if (count > 0 && at != None && at != 0) for (int k = 0; k < count; k++) Add((int)at + k * 0x0C + 0x08);
            });
            Each(0x1C, 4, x => Add(x));
            Each(0x28, GuerrillaMeshSize, x =>
            {
                AddAll(x, MeshKeyOffsets);
                Add(x + 0x28); Add(x + 0x2C); Add(x + 0x38, x + 0x34, 4); Add(x + 0x3C);
            });
            Each(0x30, GuerrillaEmitterSize, x =>
            {
                Add(x + 0x00);
                Add(x + 0x164, x + 0x160, 4);
                Add(x + 0x180, x + 0x17C, 4);
                for (int k = 0; k < 27; k++) Add(x + 0x1F0 + k * 8, x + 0x1EC + k * 8, 4);
                Add(x + 0x2D0, x + 0x2CC, 4);
                Material(x + 0x04);
            });
            Each(0x38, GuerrillaLightSize, x => AddAll(x, LightOffsets));
            Each(0x40, FilterSize, x => AddAll(x, FilterOffsets));
            if (version >= 23) Each(0x48, CoronaSize, x => { AddAll(x, CoronaOffsets); Material(x + 0x04); });
            live = [.. list];
            fields = [.. all.Distinct().Order()];
            var startSet = new SortedSet<int> { file.Length };
            foreach (int f in live) startSet.Add((int)U(f));
            if (I(0x28) > 0) startSet.Add((int)U(0x24));  // the meshes' name list
            starts = [.. startSet];
        }

        /// <summary>The piece the offset at <paramref name="at"/> points to, or null where it doesn't count.</summary>
        public Piece? Field(int at) => live.Contains(at) ? At(BitConverter.ToUInt32(File, at)) : null;

        /// <summary>The piece starting at <paramref name="offset"/>, its own offsets linked in turn (those that don't count made −1).</summary>
        Piece At(uint offset)
        {
            if (pieces.TryGetValue((int)offset, out var piece)) return piece;
            int start = (int)offset, index = Array.BinarySearch(starts, start);
            if (index < 0 || index + 1 >= starts.Length) throw new InvalidDataException($"no piece starts at 0x{offset:X}");
            int end = starts[index + 1];
            piece = new Piece(File[start..end]);
            pieces[start] = piece;
            int f = Array.BinarySearch(fields, start);
            for (f = f < 0 ? ~f : f; f < fields.Length && fields[f] < end; f++)
                piece.Link(fields[f] - start, Field(fields[f]));
            return piece;
        }

        /// <summary>The text an offset points to.</summary>
        public string Text(uint offset)
        {
            int end = Array.IndexOf(File, (byte)0, (int)offset);
            return Encoding.ASCII.GetString(File, (int)offset, (end < 0 ? File.Length : end) - (int)offset);
        }
    }
}
