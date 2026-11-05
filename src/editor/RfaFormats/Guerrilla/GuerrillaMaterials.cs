using System.Numerics;
using System.Text;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's model materials rebuilt for Armageddon's shaders. Guerrilla's are its own
/// ("rfg-nodecal", "rfg-nospecnodecal", "rfg-metal", "rfg-singledecal", "rfg-standard", ...: 33 in
/// its world chunk files), none of which Armageddon has, so each material is written for the
/// nearest of Armageddon's by the textures it has: colour, normal and specular map ir_default (as
/// Guerrilla's metal and brdf kinds, which lose their extras); colour and normal ir_nospec; colour
/// and specular ir_nonormal; colour only ir_nospec_nonormal; with a decal ir_singledecal, with two
/// ir_twodecal. The layer of Guerrilla's "e" shaders (in their Environment_Map slot, see
/// <see cref="LayerShaders"/>) is a decal too, and the "ed" ones' decal over it the second
/// (<see cref="DecalAtThirdUv"/>). A cut-out material (flags 2) is ir_at_default, and so is
/// any other see-through one (flags 1). Guerrilla's glass (its shaders rfg-glass, flags 1, and
/// rfg-eosglass, flags 0) is ir_carglass, the shader Armageddon's glass buildings draw their whole
/// panes with, blended (flags 0x41) as Armageddon's glass is: Guerrilla's colour map (its alpha the
/// pane's see-through), white for the pattern map, Armageddon's reflection cube
/// str_gls_cube_default_a (from its dlc01_precache), and its window panes' values (no dust, opacity
/// 1, reflection brightness 0.5 and contrast 0.1); a vehicle's glass, as Armageddon's cars' (its
/// ambulance's: brightness and contrast 0), reflects nothing, or the sky's cube made it white-hot in the
/// sun. Not ir_glass_shatter, which those buildings give
/// their broken-off pieces: it reads four UV channels, the last three the corners of each shard's
/// triangle, and discards every pixel outside it; Guerrilla's panes have no such channels, and drawn
/// with it they show in fragments.
///
/// A material block in both games: its size, its shader's and its own name hashes, flags (Guerrilla 0
/// / 1 / 2, Armageddon 0x40 | the same), the texture count (u16), the constant count and room (u8s),
/// three run-time words; Armageddon then a 0; then 12 bytes a texture (where its name starts in the
/// file's name list, its slot hash, its number: Guerrilla's from +0x20, Armageddon's +0x24), the
/// constants' name hashes, and from the next multiple of 16 room for their values (a float4 each).
/// Both games hash the shader, slot and constant names as zone property names (lower-case CRC-32), and
/// share the colour, normal and specular slots; Guerrilla's decal slots (Decal_Map_1, Decal_Map_2) are
/// Armageddon's Decal1_Map and Decal2_Map, its constants named longer (Diffuse_Map_TilingU for
/// Diffuse_TilingU, Normal_Map_Height for Normal_Height, Decal_Map_1_Opacity for Decal1_Opacity).
/// A slot the chosen shader has and the material hasn't is filled with a built-in texture (see
/// <see cref="GuerrillaBuiltIns"/>): flat normals, no specular, white. A slot whose texture Guerrilla
/// names "missing" counts as one the material hasn't (see <see cref="IsEmpty"/>).
///
/// Values: Guerrilla's shine is the half-way vector's (N · H)^Specular_Power (0–3,000, median 100),
/// Armageddon's the reflection's (R · V)^(Specular_Power × 5.12) (its power capped at 50). The
/// reflection's angle is about twice the half-way vector's, so an exponent of Guerrilla's is about four
/// times Armageddon's for the same highlight: its powers are taken / (4 × 5.12). Guerrilla's
/// Specular_Alpha scales its specular colour (Armageddon's goes past 1). Constants Guerrilla has no
/// counterpart for are given Armageddon's commonest values.
/// </summary>
public static class GuerrillaMaterials
{
    /// <summary>The built-in textures filling slots a material hasn't: Guerrilla's own built-ins for white and flat normals, and ours for no specular.</summary>
    public const string White = "misc-white", FlatNormal = "flat-normalmap", NoSpecular = "rfg_nospec";

    /// <summary>The reflection cube glass is given: Armageddon's own (in its dlc01_precache.vpp_pc), named as it is.</summary>
    public const string GlassReflection = "str_gls_cube_default_a";

    /// <summary>Guerrilla's own reflection cube (its mirror glass's reflection_map), in its misc.vpp_pc's always_loaded textures.</summary>
    public const string GuerrillaCube = "shd_cubeDefault";

    static readonly uint NormalMap = H("Normal_Map"), DiffuseMap = H("Diffuse_Map"), SpecularMap = H("Specular_Map"),
        Decal1Map = H("Decal1_Map"), Decal2Map = H("Decal2_Map"), GuerrillaDecal1 = H("Decal_Map_1"), GuerrillaDecal2 = H("Decal_Map_2"),
        GuerrillaLayer = H("Environment_Map"), PatternMap = H("Pattern_map"), ReflectionMap = H("reflection_map");

    /// <summary>Guerrilla's glass shaders.</summary>
    static readonly HashSet<uint> GuerrillaGlass = [H("rfg-glass"), H("rfg-eosglass")];

    /// <summary>
    /// Guerrilla's shaders whose Environment_Map is no reflection but a layer over the colour at the
    /// second UV set (UV1): dirt, rust and damage streaks, snow caps, EDF grime, Ultor logos
    /// (dcl_eg_extPanel1Dirt_D, wa_snow_decal_2, ultor_decal_1, ...). Their compiled pixel shaders
    /// blend it as lerp(colour × Diffuse_Color, layer, layer alpha × Environment_Map_Opacity) and take
    /// the shine away under it, as Armageddon's ir_singledecal does its decal at UV1, so the layer is
    /// that shader's Decal1_Map. Not rfg-eosglass (glass, drawn as glass) nor rfg-ed-oasisroof, which
    /// reads its layer at UV0, over its decal: no shader of Armageddon's lays them so, and it keeps
    /// only its decal.
    /// </summary>
    static readonly HashSet<uint> LayerShaders =
    [
        H("rfg-e-metal"), H("rfg-ed-metal"), H("rfg-e-blend-std-metal"), H("rfg-ed-blend-std-metal"), H("rfg-brdf-env"),
        H("rfg-brdf-e-concrete"), H("rfg-brdf-e-dull-paint-met"), H("rfg-brdf-e-shiny-comp-metal"),
        H("rfg-brdf-e-edf-base-panels"), H("rfg-brdf-ed-edf-base-panels"),
    ];

    /// <summary>
    /// Guerrilla's shaders that read Decal_Map_1 at the third UV set (UV2), over their layer: so does
    /// Armageddon's ir_twodecal its Decal2_Map, over its Decal1_Map. (Its other decal shaders read
    /// Decal_Map_1 at UV1, as Armageddon's Decal1_Map.)
    /// </summary>
    static readonly HashSet<uint> DecalAtThirdUv = [H("rfg-ed-metal"), H("rfg-ed-blend-std-metal"), H("rfg-brdf-ed-edf-base-panels")];

    /// <summary>Where one of Armageddon's decal layers comes from: the Guerrilla constants for its opacity and tiling (none: 1).</summary>
    sealed record Layer(string Opacity, string? TilingU = null, string? TilingV = null);

    /// <summary>How many materials were given their layer as a decal, and their decal as the second (at the third UV set), in all.</summary>
    public static int LayeredMaterials, ThirdUvDecals;

    static readonly Layer LayerFromEnvironment = new("Environment_Map_Opacity"),
        LayerFromDecal1 = new("Decal_Map_1_Opacity", "Decal_Map_1_TilingU", "Decal_map_1_TilingV"),
        LayerFromDecal2 = new("Decal_Map_2_Opacity");

    /// <summary>One of Armageddon's shaders: its texture slots in its sampler order, and its constants (from the shader's header, without the engine's own).</summary>
    sealed record Shader(string Name, uint[] Slots, string[] Constants);

    static readonly Shader Default = new("ir_default", [NormalMap, DiffuseMap, SpecularMap],
        ["Normal_Height", "Specular_Power", "Diffuse_TilingU", "Diffuse_TilingV", "Diffuse_color", "Specular_Color"]);
    static readonly Shader NoSpec = new("ir_nospec", [NormalMap, DiffuseMap],
        ["Normal_Height", "Diffuse_TilingU", "Diffuse_TilingV", "Diffuse_color"]);
    static readonly Shader NoNormal = new("ir_nonormal", [DiffuseMap, SpecularMap],
        ["Specular_Power", "Diffuse_color", "Specular_Color", "Diffuse_TilingU", "Diffuse_TilingV"]);
    static readonly Shader ColourOnly = new("ir_nospec_nonormal", [DiffuseMap],
        ["Diffuse_color", "Diffuse_TilingU", "Diffuse_TilingV"]);
    static readonly Shader SingleDecal = new("ir_singledecal", [NormalMap, DiffuseMap, SpecularMap, Decal1Map],
        ["Normal_Height", "Specular_Power", "Diffuse_TilingU", "Diffuse_TilingV", "Diffuse_color", "Specular_Color",
         "Decal1_TilingU", "Decal1_TilingV", "Decal1_Opacity", "Decal1_Color"]);
    static readonly Shader TwoDecal = new("ir_twodecal", [NormalMap, DiffuseMap, SpecularMap, Decal1Map, Decal2Map],
        ["Normal_Height", "Specular_Power", "Diffuse_TilingU", "Diffuse_TilingV", "Diffuse_color", "Specular_Color",
         "Decal1_TilingU", "Decal1_TilingV", "decal2_tilingU", "decal2_tilingV", "Decal1_Opacity", "Decal2_Opacity",
         "Decal1_Color", "decal2_color", "reflection_brightness", "reflection_contrast"]);
    static readonly Shader CutOut = new("ir_at_default", [DiffuseMap, NormalMap, SpecularMap],
        ["Diffuse_TilingU", "Diffuse_TilingV", "Normal_Height", "Specular_Power", "Diffuse_color", "Specular_Color"]);
    static readonly Shader Glass = new("ir_carglass", [DiffuseMap, PatternMap, ReflectionMap],
        ["Diffuse_color", "Dust_Amount", "reflection_brightness", "reflection_contrast", "Dust_Color", "Main_Opacity"]);

    static readonly uint GlowMap = H("Glow_Map"), Decal1NormalMap = H("Decal1_Normal_Map"), GuerrillaDiffuse2 = H("Diffuse_Map2");

    /// <summary>
    /// Armageddon's shaders for Guerrilla's effect meshes (the constants in the order Armageddon's own
    /// effect materials list them, with room for 10 more as theirs have). ir_nl_carglow, unlit: the
    /// colour map × Diffuse_Color, fogged, plus a glow map × Glow_Color × Self_Illumination.
    /// ir_nl_vfxmask, unlit: a bend map (Decal1_Normal_Map) shifts the look-ups of the colour (×
    /// Diffuse_Color), glow (× Glow_Color × Self_Illumination × 4) and mask (the specular map's green,
    /// its alpha) maps; its vertex shader scrolls the colour map by Main_Scroll and the glow map by
    /// Glow_Scroll.
    /// </summary>
    static readonly Shader CarGlow = new("ir_nl_carglow", [DiffuseMap, GlowMap],
        ["Self_Illumination", "Glow_ScrollV", "Glow_ScrollU", "Glow_Color", "Diffuse_Color"]);
    static readonly Shader VfxMask = new("ir_nl_vfxmask", [DiffuseMap, GlowMap, SpecularMap, Decal1NormalMap],
        ["SSS_Falloff_Amt", "Specular_Power", "Specular_Amount", "Specular_Alpha", "Self_Illumination", "Main_ScrollV", "Main_ScrollU",
         "Main_Opacity", "Glow_ScrollV", "Glow_ScrollU", "Glow_Falloff_Power", "Glow_Falloff_Inversion", "Glow_Color", "Diffuse_Color",
         "Decal1_TilingV", "Decal1_TilingU", "Decal1_Normal_Height"]);

    enum EffectKind { Glow, Glow2, LightCone, AnimFade, SniperScope, MaskScroll, Shield, Animated }

    /// <summary>
    /// Guerrilla's effect shaders (read from its compiled shaders), which have no shader of Armageddon's
    /// to match by textures. glow2: the colour map × Diffuse_Color blended towards Diffuse_Color2 by
    /// Diffuse_Alpha, × 10 × Self_Illumination, unlit, alpha × Diffuse_Alpha (glow: the blend by a
    /// vertex value); light-cone: the colour map × Diffuse_Color × Diffuse_Map_Luminosity × 10 × the
    /// view's angle to the surface, alpha 1; anim-fade: the colour map × Diffuse_Color, alpha faded by
    /// a vertex value; sniper-scope: no texture, alpha 0. These are ir_nl_carglow with the colour
    /// scaled the same (what's lost: the cone's fall-off with the angle, the vertex fades). mask-scroll
    /// and shield: a scrolling map bends the look-ups of a coloured one, pulsing, × 100 ×
    /// Self_Illumination: ir_nl_vfxmask (the pulse lost; brightness and bend strength guessed).
    /// animated (the news kiosks' screens): a lit model shader whose colour map scrolls, drawn by our
    /// rewrite (gw_standard2, see <see cref="Ported"/>), as the effect meshes' other model shaders are.
    /// </summary>
    static readonly Dictionary<uint, EffectKind> EffectShaders = new()
    {
        [H("rfg-vfx-glow")] = EffectKind.Glow, [H("rfg-vfx-glow2")] = EffectKind.Glow2, [H("rfg-vfx-light-cone")] = EffectKind.LightCone,
        [H("rfg-vfx-anim-fade")] = EffectKind.AnimFade, [H("rfg-sniper-scope")] = EffectKind.SniperScope,
        [H("rfg-vfx-mask-scroll-a")] = EffectKind.MaskScroll, [H("rfg-vfx-mask-scroll-b")] = EffectKind.MaskScroll,
        [H("rfg-vfx-shield")] = EffectKind.Shield, [H("rfg-animated")] = EffectKind.Animated,
    };

    /// <summary>
    /// Guerrilla scrolls a map by its speed × the game's time; Armageddon's ir_nl_vfxmask by 10 × its
    /// speed × the fraction of a second (so it starts over each second, smooth only where 10 × the speed
    /// is whole, as Armageddon's own are). Guerrilla's speeds / 10 move as fast (assumed: both times in
    /// seconds).
    /// </summary>
    const float ScrollScale = 10;

    /// <summary>Guerrilla's mask shaders end × 100 × Self_Illumination; Armageddon's glow layer × 4 × it: 25 × as bright (a guess, to tune in the game).</summary>
    const float MaskBrightness = 25;

    /// <summary>Whether the Guerrilla material block at <paramref name="at"/> is glass (one of Guerrilla's glass shaders).</summary>
    internal static bool IsGlass(byte[] file, int at) => GuerrillaGlass.Contains(BitConverter.ToUInt32(file, at + 0x04));

    /// <summary>
    /// Armageddon's material block for the Guerrilla one at <paramref name="at"/> in <paramref name="file"/>.
    /// Its textures' names are in <paramref name="names"/>, and the built-ins it needs are added there.
    /// </summary>
    internal static byte[] Convert(byte[] file, int at, TextureNameList names, bool vehicle = false, bool guerrillaShaders = false)
    {
        int textureCount = BitConverter.ToUInt16(file, at + 0x10), constantCount = file[at + 0x12];
        uint flags = BitConverter.ToUInt32(file, at + 0x0C), guerrillaShader = BitConverter.ToUInt32(file, at + 0x04);
        if (guerrillaShaders && Ported.TryGetValue(guerrillaShader, out var ported) && VariantFor(ported, flags) is { } variant)
            return ConvertForGuerrillaShader(file, at, names, ported, variant);
        bool layered = LayerShaders.Contains(guerrillaShader), decalAtThirdUv = DecalAtThirdUv.Contains(guerrillaShader);
        var textures = new Dictionary<uint, int>();  // Armageddon's slot → where its name starts
        Layer? decal1 = null, decal2 = null;
        for (int t = 0; t < textureCount; t++)
        {
            int e = at + 0x20 + t * 12;
            uint slot = BitConverter.ToUInt32(file, e + 4);
            if (IsEmpty(names.At(BitConverter.ToInt32(file, e)))) continue;
            (uint to, Layer? layer) = slot == GuerrillaDecal1 ? decalAtThirdUv ? (Decal2Map, LayerFromDecal1) : (Decal1Map, LayerFromDecal1)
                : slot == GuerrillaDecal2 ? (Decal2Map, LayerFromDecal2)
                : slot == GuerrillaLayer && layered ? (Decal1Map, LayerFromEnvironment)
                : (slot, (Layer?)null);
            if (!textures.TryAdd(to, BitConverter.ToInt32(file, e))) continue;
            if (to == Decal1Map) decal1 = layer;
            else if (to == Decal2Map) decal2 = layer;
        }
        if (decal1 == LayerFromEnvironment) Interlocked.Increment(ref LayeredMaterials);
        if (decal2 == LayerFromDecal1) Interlocked.Increment(ref ThirdUvDecals);
        var constants = new Dictionary<uint, Vector4>();
        int hashesAt = at + 0x20 + textureCount * 12, valuesAt = at + Align(0x20 + textureCount * 12 + constantCount * 4, 16);
        for (int c = 0; c < constantCount; c++)
            constants[BitConverter.ToUInt32(file, hashesAt + c * 4)] = Read(file, valuesAt + c * 16);

        bool glass = IsGlass(file, at), cutOut = !glass && (flags & 3) != 0;
        Shader shader = glass ? Glass
            : cutOut ? CutOut
            : textures.ContainsKey(Decal2Map) ? TwoDecal
            : textures.ContainsKey(Decal1Map) ? SingleDecal
            : textures.ContainsKey(NormalMap) && textures.ContainsKey(SpecularMap) ? Default
            : textures.ContainsKey(NormalMap) ? NoSpec
            : textures.ContainsKey(SpecularMap) ? NoNormal
            : ColourOnly;

        return Block(shader, file, at, 0x40u | (glass ? 1u : cutOut ? 2u : 0u), shader.Constants.Length * 2,
            slot => textures.TryGetValue(slot, out int n) && slot != ReflectionMap ? n : names.Add(BuiltIn(slot) + ".tga"),
            constant => Value(constant, constants, glass, glass && !vehicle, decal1, decal2));
    }

    /// <summary>
    /// How one of Guerrilla's shaders is drawn by its rewrite (<see cref="GuerrillaShaders"/>): the family and
    /// UV sets, and where its maps' tilings come from, as its compiled shader does it. A tiling named is
    /// the Guerrilla constant (U and V: its name + "U" / "V"; one it hasn't is 1), none is untiled; guarded,
    /// a tiling of 0 counts as 1 (only some of its shaders do that). Normal "Diffuse" is the colour map's.
    /// </summary>
    sealed record PortedShader(GuerrillaShaders.Family Family, int Layers, string? Diffuse, bool DiffuseGuarded, string? Normal, bool NormalGuarded,
                               bool Specular = true, bool SpecularFromAlpha = false, bool Animated = false, bool LitAtNight = false, int BrdfKind = 0);

    /// <summary>A BRDF shader (every map at UV0 × the colour map's tiling, as metal's) by its curves' number in GuerrillaSurfaces.hlsl's BrdfLight.</summary>
    static PortedShader Brdf(int layers, int kind) => new(GuerrillaShaders.Family.Brdf, layers, DiffuseTiling, false, "Diffuse", false, BrdfKind: kind);

    const string DiffuseTiling = "Diffuse_Map_Tiling", NormalTiling = "Normal_map_Tiling";

    /// <summary>Guerrilla's shaders drawn by its rewrites: the standard, metal, BRDF and blend-std-metal families (opaque materials of the world's chunks and rocks), and its glass.</summary>
    static readonly Dictionary<uint, PortedShader> Ported = new()
    {
        [H("rfg-nodecal")] = new(GuerrillaShaders.Family.Standard, 0, DiffuseTiling, true, NormalTiling, true),
        [H("rfg-nospecnodecal")] = new(GuerrillaShaders.Family.Standard, 0, DiffuseTiling, true, NormalTiling, false, Specular: false),
        [H("rfg-lightsource")] = new(GuerrillaShaders.Family.Standard, 0, DiffuseTiling, true, null, false, LitAtNight: true),
        [H("rfg-singledecal")] = new(GuerrillaShaders.Family.Standard, 1, null, false, NormalTiling, false),
        [H("rfg-nospecsingledecal")] = new(GuerrillaShaders.Family.Standard, 1, DiffuseTiling, false, NormalTiling, false, Specular: false),
        [H("rfg-standard")] = new(GuerrillaShaders.Family.Standard, 2, null, false, NormalTiling, false),
        [H("rfg-nospec")] = new(GuerrillaShaders.Family.Standard, 2, null, false, NormalTiling, false, Specular: false),
        [H("rfg-stitchpiece")] = new(GuerrillaShaders.Family.Standard, 2, null, false, NormalTiling, false, SpecularFromAlpha: true),
        [H("rfg-animated")] = new(GuerrillaShaders.Family.Standard, 2, null, false, NormalTiling, false, Animated: true),
        [H("rfg-metal")] = new(GuerrillaShaders.Family.Metal, 0, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-e-metal")] = new(GuerrillaShaders.Family.Metal, 1, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-ed-metal")] = new(GuerrillaShaders.Family.Metal, 2, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-blend-std-metal")] = new(GuerrillaShaders.Family.BlendMetal, 0, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-e-blend-std-metal")] = new(GuerrillaShaders.Family.BlendMetal, 1, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-ed-blend-std-metal")] = new(GuerrillaShaders.Family.BlendMetal, 2, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-brdf-concrete")] = Brdf(0, 1),
        [H("rfg-brdf-e-concrete")] = Brdf(1, 1),
        [H("rfg-brdf-dull-met")] = Brdf(0, 2),
        [H("rfg-brdf-dull-paint-met")] = Brdf(0, 3),
        [H("rfg-brdf-e-dull-paint-met")] = Brdf(1, 3),
        [H("rfg-brdf-paint-metal")] = Brdf(0, 4),
        [H("rfg-brdf-rust-paint-met")] = Brdf(0, 5),
        [H("rfg-brdf-edf-base")] = Brdf(0, 6),
        [H("rfg-brdf-shiny-comp-metal")] = Brdf(0, 7),
        [H("rfg-brdf-e-shiny-comp-metal")] = Brdf(1, 7),
        [H("rfg-brdf-edf-base-panels")] = Brdf(0, 8),
        [H("rfg-brdf-e-edf-base-panels")] = Brdf(1, 9),
        [H("rfg-brdf-ed-edf-base-panels")] = Brdf(2, 9),
        [H("rfg-brdf")] = Brdf(0, 10),
        [H("rfg-brdf-env")] = Brdf(1, 11),
        // Blended (its every material has flags 1); the colour map untiled, the specular map tiled.
        [H("rfg-glass")] = new(GuerrillaShaders.Family.Glass, 0, null, false, null, false),
        // Every map at UV0 × the colour map's tiling, unguarded (13 of its 32 materials tile by 0: one texel).
        [H("rfg-eosglass")] = new(GuerrillaShaders.Family.EosGlass, 1, DiffuseTiling, false, "Diffuse", false),
        // Alpha-tested (flags 2 on all 101): both colour maps at UV0 × the colour map's tiling, the mask at UV1.
        [H("rfg-composite")] = new(GuerrillaShaders.Family.Composite, 1, DiffuseTiling, false, NormalTiling, false, Specular: false),
        // The vehicles' paint: every map at UV0 × the colour map's tiling (the masks × BRDF_Map_Tiling too), the decal at UV1.
        [H("rfg-edf-vhc")] = new(GuerrillaShaders.Family.VehiclePaint, 1, DiffuseTiling, false, "Diffuse", false),
        [H("rfg-colorshift")] = new(GuerrillaShaders.Family.VehiclePaint, 0, null, false, null, false, BrdfKind: 1),
        // The Oasis roofs: every map at UV0 untiled but the decal (UV1 × its tiling).
        [H("rfg-ed-oasisroof")] = new(GuerrillaShaders.Family.Clearcoat, 1, null, false, null, false),
    };

    /// <summary>
    /// The variant of its shader's rewrite a material is drawn with, by its flags (1 blended, 2 alpha-tested):
    /// glass blended, mirror glass opaque, composites cut out, the rest any of the three; null for one
    /// that doesn't fit (left to Armageddon's shaders).
    /// </summary>
    static GuerrillaShaders.Variant? VariantFor(PortedShader port, uint flags) => (port.Family, flags & 3) switch
    {
        (GuerrillaShaders.Family.Glass, 1) => GuerrillaShaders.Variant.Plain,
        (GuerrillaShaders.Family.Glass, _) => null,
        (GuerrillaShaders.Family.EosGlass, 0) => GuerrillaShaders.Variant.Plain,
        (GuerrillaShaders.Family.Composite, 2) => GuerrillaShaders.Variant.CutOut,
        (GuerrillaShaders.Family.EosGlass or GuerrillaShaders.Family.Composite, _) => null,
        (_, 0) => GuerrillaShaders.Variant.Plain,
        (_, 2) => GuerrillaShaders.Variant.CutOut,
        (_, 1) => GuerrillaShaders.Variant.Blended,
        _ => null,
    };

    /// <summary>How many materials were given one of Guerrilla's own shaders, in all.</summary>
    public static int GuerrillaShaderMaterials;

    /// <summary>
    /// The material block for the Guerrilla one at <paramref name="at"/> drawn by its shader's rewrite:
    /// Guerrilla's own texture slots, and its constants laid out as <see cref="GuerrillaShaders"/> says.
    /// </summary>
    static byte[] ConvertForGuerrillaShader(byte[] file, int at, TextureNameList names, PortedShader port, GuerrillaShaders.Variant variant)
    {
        int textureCount = BitConverter.ToUInt16(file, at + 0x10), constantCount = file[at + 0x12];
        var textures = new Dictionary<uint, int>();  // Guerrilla's slot → where its name starts
        for (int t = 0; t < textureCount; t++)
        {
            int e = at + 0x20 + t * 12;
            if (!IsEmpty(names.At(BitConverter.ToInt32(file, e)))) textures.TryAdd(BitConverter.ToUInt32(file, e + 4), BitConverter.ToInt32(file, e));
        }
        var constants = new Dictionary<uint, Vector4>();
        int hashesAt = at + 0x20 + textureCount * 12, valuesAt = at + Align(0x20 + textureCount * 12 + constantCount * 4, 16);
        for (int c = 0; c < constantCount; c++)
            constants[BitConverter.ToUInt32(file, hashesAt + c * 4)] = Read(file, valuesAt + c * 16);
        float X(string name, float otherwise) => constants.TryGetValue(H(name), out var v) ? v.X : otherwise;
        Vector4 Colour(string name) => constants.TryGetValue(H(name), out var v) ? v : Vector4.One;
        (float U, float V) Tiling(string? name, bool guarded)
        {
            if (name == null) return (1, 1);
            float u = X(name + "U", 1), v = X(name + "V", 1);
            return guarded ? (u == 0 ? 1 : u, v == 0 ? 1 : v) : (u, v);
        }
        bool Has(string slot) => textures.ContainsKey(H(slot));

        var diffuse = Tiling(port.Diffuse, port.DiffuseGuarded);
        var normal = port.Normal == "Diffuse" ? diffuse : Tiling(port.Normal, port.NormalGuarded);
        var specular = port.Family is GuerrillaShaders.Family.Standard or GuerrillaShaders.Family.Glass ? Tiling("Specular_Map_Tiling", false) : diffuse;
        bool metal = port.Family == GuerrillaShaders.Family.Metal, composite = port.Family == GuerrillaShaders.Family.Composite,
             paint = port.Family == GuerrillaShaders.Family.VehiclePaint, clearcoat = port.Family == GuerrillaShaders.Family.Clearcoat;
        // Metal's decal (at UV2) is its Decal_Map_1; the standard shaders' decals are Decal_Map_1 (UV1) and Decal_Map_2 (UV2).
        float decal1 = Has("Decal_Map_1") ? X("Decal_Map_1_Opacity", 1) : 0, decal2 = Has("Decal_Map_2") ? X("Decal_Map_2_Opacity", 1) : 0;
        float layer = Has("Environment_Map") ? X("Environment_Map_Opacity", 1) : 0;
        Vector4 value(string constant) => constant switch
        {
            "Normal_Map_Height" => new(X("Normal_Map_Height", 1), 0, 0, 0),
            "Specular_Power" => new(X("Specular_Power", 100), 0, 0, 0),
            "gw_NormalTilingU" => new(normal.U, 0, 0, 0),
            "gw_NormalTilingV" => new(normal.V, 0, 0, 0),
            "Diffuse_Color" => Colour("Diffuse_Color"),
            "Specular_Color" => Colour("Specular_Color"),
            "gw_Tiling" when paint => new(diffuse.U, diffuse.V, X("BRDF_Map_TilingU", 1), X("BRDF_Map_TilingV", 1)),
            "gw_Tiling" => new(diffuse.U, diffuse.V, specular.U, specular.V),
            "gw_Decals" => new(X("Decal_Map_1_TilingU", 1), X("Decal_map_1_TilingV", 1), decal1, decal2),
            "gw_Shine" => new(port.Specular ? X("Specular_Alpha", 1) : 0, X("Metal_Base_Shine", 1), layer, X("Decal_Map_1_Overlay", 1)),
            "gw_Glow" => new(metal ? 0 : X("Self_Illumination", 0), port.LitAtNight ? 1 : 0, port.Animated ? X("illum_cycle", -1) : -1, port.SpecularFromAlpha ? 1 : 0),
            "gw_Scroll" => port.Animated ? new(X("Diffuse_Map_OffsetU", 0), X("Diffuse_Map_OffsetV", 0), X("Decal_Map_1_OffsetU", 0), X("Decal_Map_1_OffsetV", 0)) : Vector4.Zero,
            "gw_Extra" => new(port.Animated ? X("Decal_Map_2_OffsetU", 0) : 0, port.Animated ? X("Decal_Map_2_OffsetV", 0) : 0, port.SpecularFromAlpha ? 1 : 0, 0),
            // The composite's mask: which channels pick the panel's (sub) and the core's (top), and the core's shift.
            "BRDF_Diff_1" when composite => new(X("Alpha_Picker_Sub_R", 0), X("Alpha_Picker_Sub_G", 0), X("Alpha_Picker_Sub_B", 0), 0),
            "BRDF_Diff_2" when composite => new(X("Alpha_Picker_Top_R", 0), X("Alpha_Picker_Top_G", 0), X("Alpha_Picker_Top_B", 0), 0),
            "BRDF_Diff_3" when composite => new(X("Shift_U", 0), X("Shift_V", 0), 0, 0),
            // The clear coat's colour and dirt threshold, its powers and strengths (shine, rim).
            "BRDF_Spec_1" when clearcoat => Colour("Clearcoat_Color") with { W = X("Clearcoat_Dirt_Threshold", 0) },
            "BRDF_Spec_2" when clearcoat => new(X("Clearcoat_Specular_Power", 1), X("Clearcoat_Specular_Alpha", 0), X("Clearcoat_Falloff_Power", 1), X("Clearcoat_Falloff_Alpha", 0)),
            "BRDF_Diff_1" or "BRDF_Diff_2" or "BRDF_Diff_3" or "BRDF_Spec_1" or "BRDF_Spec_2" or "BRDF_Spec_3" =>
                constants.TryGetValue(H(constant), out var lobe) ? lobe : Vector4.Zero,
            "gw_Brdf" => new(port.BrdfKind, X("Specular_falloff", 0), 0, X("Opacity", 1)),
            "gw_MetalSpecular" => constants.TryGetValue(H("Metal_Specular_Color"), out var metalSpecular)
                ? metalSpecular with { W = X("Metal_Specular_Alpha", 1) } : new(0, 0, 0, X("Metal_Specular_Alpha", 1)),
            "gw_MetalSpecularPower" => new(X("Metal_Specular_Power", 100), 0, 0, 0),
            _ => throw new InvalidOperationException($"no value for {constant}"),
        };

        string[] slots = GuerrillaShaders.SlotsOf(port.Family, port.Layers);
        var shader = new Shader(GuerrillaShaders.Name(port.Family, port.Layers, variant), [.. slots.Select(H)], GuerrillaShaders.Constants[1..]);
        Interlocked.Increment(ref GuerrillaShaderMaterials);
        // Blended ones are drawn in Armageddon's alpha pass, alpha-tested ones with their holes (as Guerrilla's flags say).
        bool blended = port.Family == GuerrillaShaders.Family.Glass || variant == GuerrillaShaders.Variant.Blended;
        return Block(shader, file, at, 0x40u | (blended ? 1u : 0u) | (variant == GuerrillaShaders.Variant.CutOut ? 2u : 0u), shader.Constants.Length * 2,
            slot => textures.TryGetValue(slot, out int n) ? n : names.Add(BuiltIn(slot) + ".tga"),
            value);
    }

    /// <summary>The built-in texture filling a slot a material hasn't.</summary>
    static string BuiltIn(uint slot) => slot == NormalMap ? FlatNormal : slot == SpecularMap ? NoSpecular : slot == ReflectionMap ? GlassReflection : White;

    /// <summary>Guerrilla's material flag for both sides drawn (its renderer's RL_CULL_NONE), and Armageddon's.</summary>
    const uint GuerrillaTwoSided = 0x40, TwoSided = 0x80;

    /// <summary>
    /// An Armageddon material block for <paramref name="shader"/>: the Guerrilla block at <paramref name="at"/>'s
    /// own name hash and run-time words, the flags given (and both sides drawn where Guerrilla's are:
    /// its 0x40, Armageddon's 0x80; its potted plants), room for <paramref name="room"/> constants, each
    /// slot's texture (where its name starts) and each constant's value as the functions give them.
    /// </summary>
    static byte[] Block(Shader shader, byte[] file, int at, uint flags, int room, Func<uint, int> texture, Func<string, Vector4> value)
    {
        if ((BitConverter.ToUInt32(file, at + 0x0C) & GuerrillaTwoSided) != 0) flags |= TwoSided;
        int hashes = 0x24 + shader.Slots.Length * 12, values = Align(hashes + shader.Constants.Length * 4, 16), size = values + room * 16;
        var block = new byte[size];
        void Put(int o, uint v) => BitConverter.TryWriteBytes(block.AsSpan(o), v);
        Put(0x00, (uint)size);
        Put(0x04, H(shader.Name));
        Array.Copy(file, at + 0x08, block, 0x08, 4);   // the material's own name hash
        Put(0x0C, flags);
        BitConverter.TryWriteBytes(block.AsSpan(0x10), (ushort)shader.Slots.Length);
        block[0x12] = (byte)shader.Constants.Length;
        block[0x13] = (byte)room;
        Array.Copy(file, at + 0x14, block, 0x14, 12);  // run-time words
        for (int t = 0; t < shader.Slots.Length; t++)
        {
            Put(0x24 + t * 12, (uint)texture(shader.Slots[t]));
            Put(0x28 + t * 12, shader.Slots[t]);
            Put(0x2C + t * 12, (uint)t);
        }
        for (int c = 0; c < shader.Constants.Length; c++)
        {
            Put(hashes + c * 4, H(shader.Constants[c]));
            Write(block, values + c * 16, value(shader.Constants[c]));
        }
        return block;
    }

    /// <summary>
    /// Armageddon's material block for an effect mesh's Guerrilla one at <paramref name="at"/>: Guerrilla's
    /// effect shaders (see <see cref="EffectShaders"/>) rebuilt for Armageddon's, its model shaders as
    /// <see cref="Convert"/> does. Its textures' names are in <paramref name="names"/>.
    /// </summary>
    internal static byte[] ConvertForEffect(byte[] file, int at, TextureNameList names)
    {
        // Its model shaders, and the news kiosks' scrolling screens (rfg-animated, a lit model shader), as
        // the world's chunks are drawn: by our rewrites of Guerrilla's shaders.
        if (!EffectShaders.TryGetValue(BitConverter.ToUInt32(file, at + 0x04), out var kind) || kind == EffectKind.Animated)
            return Convert(file, at, names, guerrillaShaders: true);
        int textureCount = BitConverter.ToUInt16(file, at + 0x10), constantCount = file[at + 0x12];
        uint flags = 0x40u | (BitConverter.ToUInt32(file, at + 0x0C) & 3);
        var textures = new Dictionary<uint, int>();  // Guerrilla's slot → where its name starts
        for (int t = 0; t < textureCount; t++)
        {
            int e = at + 0x20 + t * 12;
            if (!IsEmpty(names.At(BitConverter.ToInt32(file, e)))) textures.TryAdd(BitConverter.ToUInt32(file, e + 4), BitConverter.ToInt32(file, e));
        }
        var constants = new Dictionary<uint, Vector4>();
        int hashesAt = at + 0x20 + textureCount * 12, valuesAt = at + Align(0x20 + textureCount * 12 + constantCount * 4, 16);
        for (int c = 0; c < constantCount; c++)
            constants[BitConverter.ToUInt32(file, hashesAt + c * 4)] = Read(file, valuesAt + c * 16);
        Vector4 Get(string name, Vector4 otherwise) => constants.TryGetValue(H(name), out var v) ? v : otherwise;
        float X(string name, float otherwise) => constants.TryGetValue(H(name), out var v) ? v.X : otherwise;
        int Texture(uint guerrillaSlot, uint slot) => textures.TryGetValue(guerrillaSlot, out int n) ? n : names.Add(BuiltIn(slot) + ".tga");
        static Vector4 V(float x) => new(x, 0, 0, 0);
        var colour = Get("Diffuse_Color", Vector4.One);
        float selfLit = X("Self_Illumination", 1);

        switch (kind)
        {
            case EffectKind.MaskScroll or EffectKind.Shield:
            {
                // The scrolling map (Guerrilla's colour map) bends the look-ups of the coloured one
                // (Diffuse_Map2): Armageddon's bend map (Decal1_Normal_Map) and its colour and glow maps.
                var glow = kind == EffectKind.Shield ? Get("Diffuse_Color2", colour) : colour;
                float scrollU = X("Diffuse_Scroll_Speed_U", 0) / ScrollScale, scrollV = X("Diffuse_Scroll_Speed_V", 0) / ScrollScale;
                return Block(VfxMask, file, at, flags, VfxMask.Constants.Length + 10,
                    slot => slot == DiffuseMap || slot == GlowMap ? Texture(GuerrillaDiffuse2, DiffuseMap)
                        : slot == Decal1NormalMap ? Texture(DiffuseMap, DiffuseMap) : names.Add(White + ".tga"),
                    constant => constant switch
                    {
                        "Diffuse_Color" => colour,
                        "Glow_Color" => glow,
                        "Self_Illumination" => V(selfLit * MaskBrightness),
                        "Main_ScrollU" or "Glow_ScrollU" => V(scrollU),
                        "Main_ScrollV" or "Glow_ScrollV" => V(scrollV),
                        "Decal1_Normal_Height" => V(0.1f),
                        "Decal1_TilingU" or "Decal1_TilingV" or "Main_Opacity" or "SSS_Falloff_Amt" or "Glow_Falloff_Power" => V(1),
                        "Specular_Power" => V(100),
                        _ => V(0),  // Specular_Amount, Specular_Alpha, Glow_Falloff_Inversion
                    });
            }
            default:
            {
                // Unlit: the colour map × a colour, Armageddon's glow layer off.
                Vector4 diffuse = kind switch
                {
                    EffectKind.Glow2 => Vector4.Lerp(colour, Get("Diffuse_Color2", colour), X("Diffuse_Alpha", 1)) with { W = X("Diffuse_Alpha", 1) },
                    EffectKind.Glow => colour with { W = X("Diffuse_Alpha", 1) },
                    EffectKind.LightCone => colour with { W = 1 },
                    EffectKind.SniperScope => Vector4.Zero,
                    _ => colour,
                };
                float bright = kind switch
                {
                    EffectKind.Glow or EffectKind.Glow2 => 10 * selfLit,
                    EffectKind.LightCone => 10 * X("Diffuse_Map_Luminosity", 1),
                    _ => 1,
                };
                diffuse = new Vector4(diffuse.X * bright, diffuse.Y * bright, diffuse.Z * bright, diffuse.W);
                return Block(CarGlow, file, at, flags, CarGlow.Constants.Length + 10,
                    slot => slot == DiffuseMap ? Texture(DiffuseMap, DiffuseMap) : names.Add(White + ".tga"),
                    constant => constant == "Diffuse_Color" ? diffuse : Vector4.Zero);
            }
        }
    }

    /// <summary>
    /// Whether a slot's texture is Guerrilla's name for none: "missing" (in 3,363 world chunk
    /// materials, 196 decal slots, 101 specular maps and 9 environment maps; never a colour or normal
    /// map, and no file has the name). Such a slot is left out, as if the material hadn't it: filled
    /// with white it painted white decals over objects and made their metal shine pale.
    /// </summary>
    static bool IsEmpty(string texture) =>
        string.Equals(texture, "missing", StringComparison.OrdinalIgnoreCase) || string.Equals(texture, "missing.tga", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An Armageddon constant's value from the Guerrilla material's constants; its decal layers'
    /// opacity and tiling from where <paramref name="decal1"/> and <paramref name="decal2"/> say (a
    /// layer the material hasn't: opacity 0, over the white its slot is given).
    /// </summary>
    static Vector4 Value(string constant, Dictionary<uint, Vector4> from, bool glass, bool reflects, Layer? decal1 = null, Layer? decal2 = null)
    {
        Vector4? Get(string name) => from.TryGetValue(H(name), out var v) ? v : null;
        static Vector4 X(float x) => new(x, 0, 0, 0);
        Vector4 Opacity(Layer? layer) => layer is null ? X(0) : Get(layer.Opacity) ?? X(1);
        Vector4 Tiling(string? name) => name is null ? X(1) : Get(name) ?? X(1);
        return constant switch
        {
            "Diffuse_color" => Get("Diffuse_Color") ?? Vector4.One,
            "Diffuse_TilingU" => Get("Diffuse_Map_TilingU") ?? X(1),
            "Diffuse_TilingV" => Get("Diffuse_Map_TilingV") ?? X(1),
            "Normal_Height" => Get("Normal_Map_Height") ?? X(1),
            "Specular_Power" => X(Math.Clamp((Get("Specular_Power")?.X ?? 100) / (4 * 5.12f), 0, 50)),
            "Specular_Color" => (Get("Specular_Color") ?? Vector4.One) * (Get("Specular_Alpha")?.X ?? 1),
            "Decal1_TilingU" => Tiling(decal1?.TilingU),
            "Decal1_TilingV" => Tiling(decal1?.TilingV),
            "Decal1_Opacity" => Opacity(decal1),
            "Decal2_Opacity" => Opacity(decal2),
            "decal2_tilingU" => Tiling(decal2?.TilingU),
            "decal2_tilingV" => Tiling(decal2?.TilingV),
            "Decal1_Color" or "decal2_color" => Vector4.One,
            "reflection_brightness" => X(reflects ? 0.5f : 0),
            "reflection_contrast" => X(reflects ? 0.1f : 0),
            "Dust_Amount" => X(0),
            "Dust_Color" => Vector4.One,
            "Main_Opacity" => X(1),
            _ => throw new InvalidOperationException($"no value for {constant}"),
        };
    }

    static uint H(string name) => NameHashes.PropertyHash(name);
    static int Align(int value, int to) => (value + to - 1) / to * to;

    static Vector4 Read(byte[] d, int at) =>
        new(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8), BitConverter.ToSingle(d, at + 12));

    static void Write(byte[] d, int at, Vector4 v)
    {
        BitConverter.TryWriteBytes(d.AsSpan(at), v.X); BitConverter.TryWriteBytes(d.AsSpan(at + 4), v.Y);
        BitConverter.TryWriteBytes(d.AsSpan(at + 8), v.Z); BitConverter.TryWriteBytes(d.AsSpan(at + 12), v.W);
    }
}

/// <summary>A model's texture name list (after its mesh block): names one after another, each named by where it starts.</summary>
sealed class TextureNameList(ReadOnlySpan<byte> list)
{
    readonly List<byte> bytes = [.. list];

    public byte[] ToArray() => [.. bytes];

    /// <summary>The name starting at <paramref name="offset"/>.</summary>
    public string At(int offset)
    {
        int end = offset;
        while (end < bytes.Count && bytes[end] != 0) end++;
        return Encoding.ASCII.GetString([.. bytes.GetRange(offset, end - offset)]);
    }

    /// <summary>Where a name starts: where it already is, else after the others (with two NULs, as Armageddon's lists end their names).</summary>
    public int Add(string name)
    {
        for (int at = 0; at < bytes.Count; at++)
            if ((at == 0 || bytes[at - 1] == 0) && bytes[at] != 0 && At(at) == name) return at;
        int start = bytes.Count;
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        bytes.Add(0); bytes.Add(0);
        return start;
    }
}
