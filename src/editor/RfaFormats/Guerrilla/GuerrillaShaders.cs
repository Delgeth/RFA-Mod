using System.Reflection;
using RfaFormats.Meshes;
using RfaFormats.Shaders;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's lit model shaders rewritten for Armageddon's renderer, as effect files of the game's
/// own kind (see <see cref="EffectFile"/>): gw_standard0/1/2 (Guerrilla's standard shaders: Lambert,
/// Blinn and self-illumination), gw_metal0/1/2 (its metal: the metal lobe), gw_brdf0/1/2 (its BRDF
/// shaders: their fitted curves or look-up maps) and gw_blendmetal0/1/2 (metal and standard mixed by
/// the specular map's alpha), the number the UV sets past the first their meshes have; each as _s
/// (still meshes) and _bs (instanced ones). They go with
/// the world's other new files (overrides\build\pc\cache\), and the guerrilla_world mod has the game
/// load them after its own (the game only loads the effect files inside its archives).
///
/// Each takes its G-buffer passes (the normals, plain and stippled) and the engine's texture slots
/// from Armageddon's ir_default as they are, and adds its own material pass, compiled from
/// Guerrilla\Shaders\GuerrillaSurfaces.hlsl, which lights the surface with Guerrilla's maths from
/// what the mod gives it. The material pass of objects fading in or out (stippled) is the plain one.
/// The materials name Guerrilla's own texture slots and these constants, register by register (the
/// first five where ir_default's G-buffer shaders read theirs):
/// IR_Object_ID (the engine's), Normal_Map_Height, Specular_Power, the normal map's tiling U and V,
/// Diffuse_Color, Specular_Color, then four values each: gw_Tiling, gw_Decals, gw_Shine, gw_Glow,
/// gw_Scroll, gw_Extra, then BRDF_Diff_1-3, BRDF_Spec_1-3, gw_Brdf, gw_MetalSpecular,
/// gw_MetalSpecularPower (see the shader's Material buffer).
/// </summary>
public static class GuerrillaShaders
{
    public enum Family { Standard = 1, Metal = 2, Brdf = 3, BlendMetal = 4 }

    /// <summary>An effect's name (without its _s / _bs): its family and how many UV sets past the first its meshes have.</summary>
    public static string Name(Family family, int layers) => $"gw_{family.ToString().ToLowerInvariant()}{layers}";

    /// <summary>The material constants in register order (0 is the engine's).</summary>
    public static readonly string[] Constants =
        ["IR_Object_ID", "Normal_Map_Height", "Specular_Power", "gw_NormalTilingU", "gw_NormalTilingV", "Diffuse_Color", "Specular_Color",
         "gw_Tiling", "gw_Decals", "gw_Shine", "gw_Glow", "gw_Scroll", "gw_Extra",
         "BRDF_Diff_1", "BRDF_Diff_2", "BRDF_Diff_3", "BRDF_Spec_1", "BRDF_Spec_2", "BRDF_Spec_3", "gw_Brdf", "gw_MetalSpecular", "gw_MetalSpecularPower"];

    /// <summary>The texture slots (Guerrilla's names) and their registers, as the shader's.</summary>
    public static readonly (string Slot, byte Register)[] Slots =
        [("Normal_Map", 0), ("Diffuse_Map", 1), ("Specular_Map", 2), ("Decal_Map_1", 3), ("Decal_Map_2", 4), ("Environment_Map", 5),
         ("BRDF_Diff_Map", 6), ("BRDF_Spec_Map", 7)];

    /// <summary>
    /// The slots an effect samples: the standard shaders' decals at UV1 and UV2; the others' layer at UV1
    /// and decal at UV2, and the BRDF ones' two look-up maps.
    /// </summary>
    public static string[] SlotsOf(Family family, int layers)
    {
        string[] uvSets = family == Family.Standard
            ? layers switch { 1 => ["Decal_Map_1"], 2 => ["Decal_Map_1", "Decal_Map_2"], _ => [] }
            : layers switch { 1 => ["Environment_Map"], 2 => ["Environment_Map", "Decal_Map_1"], _ => [] };
        string[] lookUps = family == Family.Brdf ? ["BRDF_Diff_Map", "BRDF_Spec_Map"] : [];
        return ["Normal_Map", "Diffuse_Map", "Specular_Map", .. uvSets, .. lookUps];
    }

    /// <summary>Armageddon's G-buffer shaders read these constants of ir_default's from these registers; checked before they are used.</summary>
    static readonly (string Name, byte Register)[] BaseConstants =
        [("IR_Object_ID", 0), ("Normal_Height", 1), ("Specular_Power", 2), ("Diffuse_TilingU", 3), ("Diffuse_TilingV", 4)];

    static readonly uint GeometryTechnique = 0x6614AD7B, GeometryFadingTechnique = 0xB08EBA8C,
        MaterialTechnique = 0xEA49BBC0, MaterialFadingTechnique = 0xFE6B0E83;

    /// <summary>
    /// Writes every effect into <paramref name="output"/> (the overrides' build\pc\cache), from
    /// Armageddon's ir_default in <paramref name="armageddonCache"/> (its build\pc\cache).
    /// Returns the files' names.
    /// </summary>
    public static List<string> Write(string armageddonCache, string output)
    {
        string source = Source();
        var misc = Packfile.Open(Path.Combine(armageddonCache, "misc.vpp_pc"));
        var written = new List<string>();
        foreach (var (suffix, instanced) in new[] { ("s", false), ("bs", true) })
        {
            string baseName = $"ir_default_{suffix}.fxo_pc_dx11";
            var entry = misc.Find(baseName) ?? throw new InvalidDataException($"Armageddon's misc.vpp_pc has no {baseName}");
            var baseEffect = EffectFile.Read(misc.Read(entry));
            Check(baseEffect, baseName);
            foreach (var family in Enum.GetValues<Family>())
                for (int layers = 0; layers <= 2; layers++)
                {
                    string name = $"{Name(family, layers)}_{suffix}.fxo_pc_dx11";
                    var defines = new Dictionary<string, string>
                    {
                        ["FAMILY"] = ((int)family).ToString(), ["LAYERS"] = layers.ToString(), ["INSTANCED"] = instanced ? "1" : "0",
                    };
                    byte[] vertex = HlslCompiler.Compile(source, "SurfaceVertex", "vs_4_0", "GuerrillaSurfaces.hlsl", defines);
                    byte[] pixel = HlslCompiler.Compile(source, "SurfacePixel", "ps_4_0", "GuerrillaSurfaces.hlsl", defines);
                    File.WriteAllBytes(Path.Combine(output, name), Effect(baseEffect, family, layers, vertex, pixel).Write());
                    written.Add(name);
                }
        }
        written.AddRange(WriteTerrain(misc, output));
        return written;
    }

    /// <summary>
    /// The terrain's effects (see GuerrillaTerrain.hlsl): Armageddon's own terrain effect for each, its
    /// vertex shaders and G-buffer pass as they are (the ground, cliffs and roads placed and depth-tested
    /// exactly as before), its material pass's pixel shader Guerrilla's. Each with the rows Guerrilla's
    /// shader adds: the ground its layers' shine power (c10), the two-texture cliff the zone's colour map
    /// (t10, which it fades to far off, as the ground does); the roads need none (Armageddon's road
    /// effects have every constant of Guerrilla's), and are written for still and instanced decals (_s,
    /// _bs). Our pixel shader must read what Armageddon's vertex shader writes, register by register:
    /// checked before the file is written.
    /// </summary>
    static List<string> WriteTerrain(Packfile misc, string output)
    {
        string source = Source("GuerrillaTerrain.hlsl");
        var written = new List<string>();
        var effects = new List<(string Name, string From, int Surface, int Road)>
        {
            (TerrainMaterials.GroundShader + "_t", "ir_terrain_height_mesh_t", 1, 0),
            (TerrainMaterials.GroundFadeShader + "_t", "ir_terrain_height_mesh_blend_t", 2, 0),
            (TerrainMaterials.CliffShader + "_t", "ir_terrain_sidemap_t", 3, 0),
            (TerrainMaterials.FarShader + "_t", "ir_terrain_height_mesh_lowlod_t", 6, 0),
        };
        foreach (int variant in new[] { 2, 3, 4 })
            foreach (string suffix in new[] { "_s", "_bs" })
                effects.Add((TerrainMaterials.RoadShaderName(variant) + suffix, $"ir_terrain_road_layer{variant}{suffix}", 5, variant));
        foreach (var (name, from, surface, road) in effects)
        {
            string file = from + ".fxo_pc_dx11";
            var entry = misc.Find(file) ?? throw new InvalidDataException($"Armageddon's misc.vpp_pc has no {file}");
            var e = EffectFile.Read(misc.Read(entry));
            if (e.VertexShaders.Count != 2 || e.PixelShaders.Count != 2 || e.Techniques.Count != 4 ||
                e.Techniques[2] != new EffectFile.Technique(1, 1, MaterialTechnique, 0x80) || e.Techniques[3] != new EffectFile.Technique(1, 1, MaterialFadingTechnique, 0x100))
                throw new InvalidDataException($"Armageddon's {file} isn't laid out as Guerrilla's terrain shaders expect");
            if (surface == 5 && (RoadConstants.Where((c, r) => !e.Constants.Any(k => k.Name == H(c) && k.Register == r)).Any() ||
                                 RoadTextures.Any(t => !e.Textures.Any(k => k.Name == H(t.Slot) && k.Register == t.Register))))
                throw new InvalidDataException($"Armageddon's {file} hasn't the constants and textures where Guerrilla's road shader reads them");
            byte[] pixel = HlslCompiler.Compile(source, "TerrainPixel", "ps_4_0", "GuerrillaTerrain.hlsl",
                                                new Dictionary<string, string> { ["SURFACE"] = surface.ToString(), ["ROAD"] = road.ToString() });
            CheckSignatures(e.VertexShaders[1]!, pixel, file);
            e.PixelShaders[1] = pixel;
            if (surface <= 2)
                e.Constants.Add(new(H("Terrain_layer_spec_power"), 10, 0, 1, 1));
            if (surface == 3)
            {
                var colour = e.Textures.First(t => t.Name == H("Diffuse_Map_0"));
                e.Textures.Add(colour with { Name = H("Combine_map"), Register = 10 });
            }
            string written1 = name + ".fxo_pc_dx11";
            File.WriteAllBytes(Path.Combine(output, written1), e.Write());
            written.Add(written1);
        }
        return written;
    }

    /// <summary>The road effects' constants in register order, and the textures our road shader reads, as Armageddon's road effects have them.</summary>
    static readonly string[] RoadConstants =
        ["Specular_Power", "Terrain_layer1_scale_offset", "Terrain_layer2_scale_offset", "Terrain_layer0_scale_offset", "Tile", "Edge_fade__use_spec_map",
         "Diffuse_color_0", "Diffuse_color_1", "Diffuse_color_2", "Diffuse_color_3", "Terrain_layer_spec_alpha"];
    static readonly (string Slot, byte Register)[] RoadTextures =
        [("Alpha_Map_0", 4), ("Diffuse_Map_3", 5), ("Diffuse_Map_0", 6), ("Diffuse_Map_1", 7), ("Diffuse_Map_2", 8)];

    /// <summary>That every input of our pixel shader is an output of Armageddon's vertex shader at the same register, with no component it hasn't.</summary>
    static void CheckSignatures(byte[] vertex, byte[] pixel, string file)
    {
        var outputs = HlslCompiler.Signature(vertex, "Output");
        foreach (var input in HlslCompiler.Signature(pixel, "Input"))
        {
            var match = outputs.FirstOrDefault(o => o.Register == input.Register);
            if (match.Name == null || !match.Name.Equals(input.Name, StringComparison.OrdinalIgnoreCase) || match.Index != input.Index ||
                input.Mask.Any(c => c != ' ' && !match.Mask.Contains(c)))
                throw new InvalidDataException($"Guerrilla's terrain shader for {file} reads {input.Name}{input.Index} (register {input.Register}, {input.Mask}), " +
                                               $"which Armageddon's vertex shader doesn't write there ({match.Name}{match.Index} {match.Mask})");
        }
    }

    static string Source(string file = "GuerrillaSurfaces.hlsl")
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(file)
            ?? throw new InvalidOperationException($"{file} isn't in RfaFormats");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>That Armageddon's ir_default is still laid out as these effects take it.</summary>
    static void Check(EffectFile e, string name)
    {
        bool techniques = e.Techniques.Count == 4 && e.VertexShaders.Count == 4 && e.PixelShaders.Count == 4
            && e.Techniques[0] == new EffectFile.Technique(0, 0, GeometryTechnique, 0x20)
            && e.Techniques[1] == new EffectFile.Technique(1, 1, GeometryFadingTechnique, 0x40);
        bool constants = BaseConstants.All(c => e.Constants.Any(k => k.Name == H(c.Name) && k.Register == c.Register));
        if (!techniques || !constants) throw new InvalidDataException($"Armageddon's {name} isn't laid out as Guerrilla's shaders expect");
    }

    static EffectFile Effect(EffectFile from, Family family, int layers, byte[] vertex, byte[] pixel)
    {
        var e = new EffectFile { KindsPresent = from.KindsPresent };
        from.TechniqueMap.CopyTo(e.TechniqueMap, 0);
        // The G-buffer passes as Armageddon's; the material pass (plain and stippled) ours.
        e.VertexShaders.AddRange([from.VertexShaders[0], from.VertexShaders[1], vertex]);
        e.PixelShaders.AddRange([from.PixelShaders[0], from.PixelShaders[1], pixel]);
        e.Techniques.AddRange([from.Techniques[0], from.Techniques[1],
                               new(2, 2, MaterialTechnique, 0x80), new(2, 2, MaterialFadingTechnique, 0x100)]);
        // ir_default's slots, the engine's with them; our own layers with the colour map's sampling.
        var diffuse = from.Textures.First(t => t.Name == H("Diffuse_Map"));
        e.Textures.AddRange(from.Textures);
        foreach (string slot in SlotsOf(family, layers).Where(s => !from.Textures.Any(t => t.Name == H(s))))
            e.Textures.Add(diffuse with { Name = H(slot), Register = Slots.First(s => s.Slot == slot).Register });
        for (int r = 0; r < Constants.Length; r++)
        {
            var baseConstant = from.Constants.FirstOrDefault(c => c.Register == r);
            e.Constants.Add(new(H(Constants[r]), (byte)r, baseConstant.Flags, 1, 1));
        }
        return e;
    }

    static uint H(string name) => Zones.NameHashes.PropertyHash(name);
}
