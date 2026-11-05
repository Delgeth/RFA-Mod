using RfaFormats.Textures;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Textures Red Faction: Guerrilla's engine has built in rather than as files (neither game ships
/// them). Its zones' ground materials name them for the layers they leave unused: "misc-white" for
/// the colour and "flat-normalmap" for the normal (in 11 of its 102 zones, e.g. terr01_02_11). The
/// world builder makes them from nothing, under the names it gives them. One more is ours:
/// "rfg_nospec", a black specular map, for materials moved to a shader with a specular slot they
/// have no texture for (<see cref="GuerrillaMaterials"/>). And <see cref="CliffDither"/>, the cliffs'
/// dither map.
/// </summary>
public static class GuerrillaBuiltIns
{
    /// <summary>
    /// The cliff materials' Dither_map in place of the game's dither.tga: a near-black one (1/31).
    /// Armageddon's cliff shaders keep a pixel in the normals pass only where the cliff's vertex alpha
    /// (w × 2 − 1) is at least 1 − the dither map, so the game's dither pattern kept the ring where a
    /// cliff fades into the ground (Guerrilla's cliff blocks overlap the ground there) pixel by pixel,
    /// cliff's normal or ground's: a speckle in its light. With this one only the cliff itself (alpha
    /// 0xFF) writes its normals, the ring keeps the ground's, and the colour pass still blends the
    /// cliff over the ground by its alpha, as Guerrilla does. Armageddon's own levels keep dither.tga.
    /// </summary>
    public const string CliffDither = "gw_cliff_nodither";

    /// <summary>A built-in texture's files (.cvbm_pc, .gvbm_pc) under <paramref name="name"/>, or null if there is no such built-in.</summary>
    public static (string Name, byte[] Data)[]? Files(string texture, string name) => texture.ToLowerInvariant() switch
    {
        GuerrillaMaterials.White => Flat(name, [255, 255, 255, 255], 0),
        // Guerrilla's normal maps hold the normal's x and y in red and green (128 is 0) and blue 0:
        // their means are 127-130, 127-128, 0.
        GuerrillaMaterials.FlatNormal => Flat(name, [128, 128, 0, 255], PegFile.DataFlag),
        GuerrillaMaterials.NoSpecular => Flat(name, [0, 0, 0, 255], 0),
        CliffDither => Flat(name, [8, 8, 8, 255], 0),
        _ => null,
    };

    /// <summary>A texture of one colour (red, green, blue, alpha): 64 × 64, DXT1 with its levels down to 4 × 4.</summary>
    internal static (string Name, byte[] Data)[] Flat(string name, byte[] colour, ushort flags)
    {
        const int size = 64, levels = 5;
        var image = new RgbaImage(size, size);
        for (int i = 0; i < image.Pixels.Length; i++) image.Pixels[i] = colour[i % 4];
        return [(name + ".cvbm_pc", PegFile.SingleHeader(name + ".tga", size, size, TextureFormat.Dxt1, levels, flags)),
                (name + ".gvbm_pc", TextureCodec.EncodeWithMips(TextureFormat.Dxt1, image, levels))];
    }
}
