namespace RfaFormats.Shaders;

/// <summary>
/// One of Armageddon's compiled shader effects (.fxo_pc_dx11 in build\pc\cache\misc.vpp_pc, version 9),
/// as the game's loader reads it. All little-endian:
///
///   0x00  magic 0x4B42A1EE, version 9, the technique kinds present (a bit each, see <see cref="Kind"/>)
///   0x0C  counts (i16): texture slots, constants, bool constants; (u8) vertex shaders, pixel shaders
///   0x14  20 bytes of 0 (the game's pointers when loaded)
///   0x28  the technique map: the technique each kind uses (10 bytes, -1 none, then 2 of 0)
///   0x34  0 (a pointer), 0x38 the technique count
///   0x3C  16 bytes a texture slot, 8 bytes a constant, 8 bytes a bool constant; to a multiple of 4
///         the size of each vertex shader, then of each pixel shader (0: none); to a multiple of 4
///         12 bytes a technique; to a multiple of 4
///         then the shaders' code (DXBC) in that order, each from a multiple of 16 of the file.
///
/// A texture slot: its name's hash (lower-case CRC-32, as the materials name slots), its register (the
/// same t# and s# in the shader), flags, the address modes U/V/W and the min/mag/mip filters (the
/// game's own codes), the border colour's kind, 3 of 0. A constant: its name's hash, its register in
/// the material's constant buffer (vc0: register × 16 bytes), flags, rows and array size (a float or
/// float4 is 1, 1). A technique: its vertex and pixel shader (indexes in the two lists), its name's
/// hash and its kind's flag. The suffix of a file's name says which meshes it is for: _s still meshes,
/// _bs instanced still meshes, _c skinned ones. All the files of one name must have the same slots and
/// constants: the first one the game registers decides where a material's values go.
/// </summary>
public sealed class EffectFile
{
    public const uint Magic = 0x4B42A1EE, Version = 9;

    /// <summary>The kinds of technique: the pass each is drawn in, and its flag in a technique.</summary>
    public enum Kind
    {
        Base = 0,           // full-screen, lamps, particles, forward shaders (flag 0x2)
        Depth = 2,          // depth only: shadow maps (0x8)
        Geometry = 4,       // the G-buffer pass: normals (0x20)
        GeometryFading = 5, // the same for objects fading in or out, stippled (0x40)
        Material = 6,       // the colour pass, lit by the light buffer (0x80)
        MaterialFading = 7, // the same, stippled (0x100)
    }

    public static uint TechniqueFlag(Kind kind) => kind switch
    {
        Kind.Base => 0x2, Kind.Depth => 0x8, Kind.Geometry => 0x20, Kind.GeometryFading => 0x40,
        Kind.Material => 0x80, Kind.MaterialFading => 0x100, _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public record struct TextureSlot(uint Name, byte Register, byte Flags, byte AddressU, byte AddressV, byte AddressW,
                                     byte MinFilter, byte MagFilter, byte MipFilter, byte Border);
    public record struct Constant(uint Name, byte Register, byte Flags, byte Rows, byte Count);
    public record struct Technique(short VertexShader, short PixelShader, uint Name, uint Flags);

    public uint KindsPresent { get; set; }
    public List<TextureSlot> Textures { get; } = [];
    public List<Constant> Constants { get; } = [];
    public List<Constant> Bools { get; } = [];
    public List<byte[]?> VertexShaders { get; } = [];
    public List<byte[]?> PixelShaders { get; } = [];
    /// <summary>The technique each kind 0-9 uses (-1: none), then 2 bytes of 0.</summary>
    public sbyte[] TechniqueMap { get; } = new sbyte[12];
    public List<Technique> Techniques { get; } = [];

    /// <summary>How many of the material's constant registers the shaders take (a material needs at least this much room).</summary>
    public int Registers => Constants.Concat(Bools).Select(c => c.Register + c.Rows * Math.Max(1, (int)c.Count)).DefaultIfEmpty(0).Max();

    public static EffectFile Read(byte[] d)
    {
        if (BitConverter.ToUInt32(d, 0) != Magic) throw new InvalidDataException("not a shader effect file");
        if (BitConverter.ToUInt32(d, 4) != Version) throw new InvalidDataException($"shader effect version {BitConverter.ToUInt32(d, 4)}, not {Version}");
        var e = new EffectFile { KindsPresent = BitConverter.ToUInt32(d, 8) };
        int textures = BitConverter.ToInt16(d, 0x0C), constants = BitConverter.ToInt16(d, 0x0E), bools = BitConverter.ToInt16(d, 0x10);
        int vertexShaders = d[0x12], pixelShaders = d[0x13], techniques = BitConverter.ToInt32(d, 0x38);
        for (int k = 0; k < 12; k++) e.TechniqueMap[k] = (sbyte)d[0x28 + k];
        int o = 0x3C;
        for (int t = 0; t < textures; t++, o += 16)
            e.Textures.Add(new(BitConverter.ToUInt32(d, o), d[o + 4], d[o + 5], d[o + 6], d[o + 7], d[o + 8], d[o + 9], d[o + 10], d[o + 11], d[o + 12]));
        for (int c = 0; c < constants; c++, o += 8) e.Constants.Add(new(BitConverter.ToUInt32(d, o), d[o + 4], d[o + 5], d[o + 6], d[o + 7]));
        for (int c = 0; c < bools; c++, o += 8) e.Bools.Add(new(BitConverter.ToUInt32(d, o), d[o + 4], d[o + 5], d[o + 6], d[o + 7]));
        o = Align(o, 4);
        var sizes = new int[vertexShaders + pixelShaders];
        for (int s = 0; s < sizes.Length; s++, o += 4) sizes[s] = BitConverter.ToInt32(d, o);
        o = Align(o, 4);
        for (int t = 0; t < techniques; t++, o += 12)
            e.Techniques.Add(new(BitConverter.ToInt16(d, o), BitConverter.ToInt16(d, o + 2), BitConverter.ToUInt32(d, o + 4), BitConverter.ToUInt32(d, o + 8)));
        o = Align(o, 4);
        for (int s = 0; s < sizes.Length; s++)
        {
            byte[]? code = null;
            if (sizes[s] > 0)
            {
                o = Align(o, 16);
                code = d[o..(o + sizes[s])];
                o += sizes[s];
            }
            (s < vertexShaders ? e.VertexShaders : e.PixelShaders).Add(code);
        }
        return e;
    }

    public byte[] Write()
    {
        var o = new List<byte>(0x40);
        void U32(uint v) => o.AddRange(BitConverter.GetBytes(v));
        void I16(short v) => o.AddRange(BitConverter.GetBytes(v));
        void Pad(int to) { while (o.Count % to != 0) o.Add(0); }
        U32(Magic); U32(Version); U32(KindsPresent);
        I16((short)Textures.Count); I16((short)Constants.Count); I16((short)Bools.Count);
        o.Add((byte)VertexShaders.Count); o.Add((byte)PixelShaders.Count);
        o.AddRange(new byte[20]);
        foreach (sbyte k in TechniqueMap) o.Add((byte)k);
        U32(0); U32((uint)Techniques.Count);
        foreach (var t in Textures)
        {
            U32(t.Name);
            o.AddRange([t.Register, t.Flags, t.AddressU, t.AddressV, t.AddressW, t.MinFilter, t.MagFilter, t.MipFilter, t.Border, 0, 0, 0]);
        }
        foreach (var c in Constants.Concat(Bools))
        {
            U32(c.Name);
            o.AddRange([c.Register, c.Flags, c.Rows, c.Count]);
        }
        Pad(4);
        foreach (var code in VertexShaders.Concat(PixelShaders)) U32((uint)(code?.Length ?? 0));
        Pad(4);
        foreach (var t in Techniques)
        {
            I16(t.VertexShader); I16(t.PixelShader); U32(t.Name); U32(t.Flags);
        }
        Pad(4);
        foreach (var code in VertexShaders.Concat(PixelShaders))
        {
            if (code == null) continue;
            Pad(16);
            o.AddRange(code);
        }
        return [.. o];
    }

    static int Align(int value, int to) => (value + to - 1) / to * to;
}
