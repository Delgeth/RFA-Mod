// Guerrilla's lit model shaders (its rfg-* surfaces' forward pass), rewritten as a material pass of
// Armageddon's renderer, which draws a scene in three passes: the G-buffer (normals), the light
// buffer (the lamps, and in the world ported from Guerrilla its sky light and sun, added by the
// guerrilla_world mod, then the game's ambient occlusion over them), and the material pass, which
// draws each surface again with its colours, lit by the light buffer. Guerrilla's own shaders light
// in one pass, with maths no light buffer can carry (its metal's lobe, the self-lit ambient), so here
// the material pass lights the surface itself, as Guerrilla's shader does, from what the mod gives it
// (b8, t16, t17: the engine never binds those registers):
//   - the sun's (or moon's) direction and colour, the two ambient colours, the time, the lamps' night
//     level (Guerrilla's Time.w);
//   - its own light as it added it to the light buffer (the sky's and the sun's, before the occlusion),
//     with the sun's shadow in alpha (t16);
//   - the light buffer as it was just before the game's occlusion (t17).
// From those: the occlusion (the light buffer now / then), the lamps' light (the light buffer less
// the mod's own, both after the occlusion) and the shadow. Guerrilla's maths below, family by family,
// is as its compiled shaders do it (constants by Guerrilla's names; the builder lays them out).
//
// Defines: FAMILY 1 the standard shaders (rfg-nodecal, rfg-nospecnodecal, rfg-singledecal,
// rfg-nospecsingledecal, rfg-standard, rfg-nospec, rfg-stitchpiece, rfg-animated, rfg-lightsource:
// Lambert + Blinn + self-illumination), 2 the metal ones (rfg-metal, rfg-e-metal, rfg-ed-metal: the
// metal lobe), 3 the BRDF ones (rfg-brdf-*: their fitted curves, which BrdfKind.x picks, and rfg-brdf /
// rfg-brdf-env with their look-up maps), 4 the blend-std-metal ones (metal and standard mixed by the
// specular map's alpha), 5 rfg-glass (blended: the ambient and the sun's shine, how much shows by the
// colour map's alpha; drawn in the material pass only, so it is in no G-buffer, casts no shadow and
// takes no occlusion, shadow or lamps), 6 rfg-eosglass (opaque mirror glass: a frame layer at UV1 over
// Guerrilla's reflection cube), 7 rfg-composite (a panel over its broken core by a mask at UV1, its
// holes where the mask has neither), 8 the vehicle paints (rfg-edf-vhc: the EDF's camouflage, its
// tint turning with the angle between the sun and the eye; BrdfKind.x 1 rfg-colorshift: that tint
// alone, unlit), 9 rfg-ed-oasisroof (a clear coat over a near-black base, its shine and rim fading
// under the dirt layer); LAYERS 0, 1 or 2: the UV sets past the first the meshes have
// (standard: Decal_Map_1 at UV1, Decal_Map_2 at UV2; the others: their layer, Environment_Map, at UV1,
// Decal_Map_1 at UV2); INSTANCED 1 for instanced meshes (each object's matrix in the vertex stream);
// SKINNED 1 for skinned ones (up to four bones a vertex: the glass's flying shards, which the game
// draws with a material's "_c" effect);
// CUTOUT 1 for alpha-tested materials (Guerrilla's flags 2): holes where the material's alpha is under
// the engine's threshold, cut the same in every pass, so these effects have their own depth (shadow)
// and G-buffer passes too (DepthVertex/DepthPixel, GeometryVertex/GeometryPixel, STIPPLED 1 for objects
// fading in or out), which write the G-buffer as Armageddon's do; BLENDED 1 for blended ones (flags 1,
// drawn in the material pass alone, by their alpha).
//
// The time (b8's) in the depth and G-buffer passes is the frame before's: the mod leaves b8 bound
// after the material pass, so a scrolling cut-out's holes move with it.

cbuffer Object : register(b2)   // the engine's, per object (vc2)
{
    float4 ProjectionRows[4];   // c0-3: the world (relative to the render offset) to the screen
    float4 EyePosition;         // c4
    float4 ObjectParams2;       // c5
    float4 Unused6;
    float4 PositionScale;       // c7
    float4 Unused8;
    float4 FogDistances;        // c9: x the most fog, y where it starts, z where it is most, w its curve
    float4 Unused10[5];
    float4 ObjectRows[3];       // c15-17: the object to the world (rows of a 3 × 4)
    float4 Unused18;
    float4 WorldToView[3];      // c19-21: IR_World2View, the world to the camera's space (rows; directions only here)
};

#if SKINNED
cbuffer Bones : register(b3)     // the engine's, per skinned object (vc3): 64 bones, three rows each
{
    float4 BoneRows[192];
};
#endif

cbuffer Material : register(b0)  // the material's constants (vc0), laid out by the builder
{
    float4 ObjectId;            // c0 the engine's IR_Object_ID (its G-buffer shaders use it)
    float4 NormalHeight;        // c1 Normal_Map_Height: x
    float4 SpecularPower;       // c2 Specular_Power: x
    float4 NormalTilingU;       // c3 the normal map's tiling: x (the G-buffer's shaders read it here)
    float4 NormalTilingV;       // c4
    float4 DiffuseColour;       // c5 Diffuse_Color
    float4 SpecularColour;      // c6 Specular_Color
    float4 Tiling;              // c7 the colour map's tiling (x, y), the specular map's (z, w)
    float4 Decals;              // c8 Decal_Map_1's tiling (x, y); Decal_Map_1's and Decal_Map_2's opacity (z, w)
    float4 Shine;               // c9 Specular_Alpha, Metal_Base_Shine, Environment_Map_Opacity, Decal_Map_1_Overlay
    float4 Glow;                // c10 Self_Illumination; 1: lit by the lamps' night level instead; illum_cycle (or < 0: no pulse); 1: specular from the colour map's alpha
    float4 Scroll;              // c11 Diffuse_Map_OffsetU, V; Decal_Map_1_OffsetU, V (each × the time in seconds)
    float4 Extra;               // c12 Decal_Map_2_OffsetU, V; 1: alpha 1 (not the colour map's); 0
    float4 BrdfDiffuse[3];      // c13-15 BRDF_Diff_1, 2, 3
    float4 BrdfSpecular[3];     // c16-18 BRDF_Spec_1, 2, 3
    float4 BrdfKind;            // c19 which BRDF (see SurfacePixel); Specular_falloff; 0; Opacity
    float4 MetalSpecular;       // c20 Metal_Specular_Color (rgb), Metal_Specular_Alpha (w)
    float4 MetalSpecularPower;  // c21 Metal_Specular_Power: x
};

cbuffer Frame : register(b4)    // the engine's, per frame (vc4)
{
    float4 TintSaturation;
    float4 TintColour;          // c1
    float4 EngineAmbient[2];
    float4 FogColour;           // c4
    float4 FrameUnused5[3];
    float4 LightCategory;       // c8: added to the G-buffer's normal and shine, as the engine's G-buffer shaders do
    float4 AlphaThreshold;      // c9: x, the alpha under which an alpha-tested surface is cut away
    float4 FrameUnused10[4];
    float4 StippleRepeat;       // c14: IR_Stipple_Repeat_Info, the stipple pattern's repeats across the picture
};

cbuffer Guerrilla : register(b8)  // the guerrilla_world mod's
{
    float4 SunToward;           // xyz toward the sun or moon, in the world; w 1 while the mod lights the world (0: not bound)
    float4 SunColour;           // Guerrilla's V_light_color
    float4 AmbientAbove;        // V_ambient_render: on a surface facing the light
    float4 AmbientBelow;        // V_back_ambient_render: facing away from it
    float4 Time;                // x seconds; w the lamps' night level (1 at night, 0 by day)
    float4 SunTowardView;       // (the terrain's)
    float4 View;                // (the terrain's)
    float4 Tint;                // the colour correction (Guerrilla's Tint_color) the colours above already carry
};

// The ambient lifted toward 4 by a self-illumination, as Guerrilla's shaders lift it: toward 4 before
// their whole result is tinted, so toward 4 × the tint here, where the ambient comes tinted.
float3 SelfLit(float3 ambient, float glow) { return ambient + glow * (4 * Tint.rgb - ambient); }

Texture2D NormalMap : register(t0);
Texture2D DiffuseMap : register(t1);
Texture2D SpecularMap : register(t2);
Texture2D DecalMap1 : register(t3);
Texture2D DecalMap2 : register(t4);
Texture2D LayerMap : register(t5);
Texture2D BrdfDiffuseMap : register(t6);   // rfg-brdf's look-ups: by (N·L/2 + ½, 1 − N·V)
Texture2D BrdfSpecularMap : register(t7);  // and by (N·L, 1 − N·H)
TextureCube ReflectionCube : register(t8);  // rfg-eosglass's reflection_map
Texture2D AlphaMap : register(t10);         // rfg-composite's mask
Texture2D MaskMap : register(t11);          // the vehicle paints' BRDF_Mask_Map (the camouflage's three masks)
Texture2D StipplePattern : register(t14);   // the engine's, for objects fading in or out
Texture2D LightBuffer : register(t12);
Texture2D OurLight : register(t16);
Texture2D LightBefore : register(t17);
SamplerState NormalSampler : register(s0);
SamplerState DiffuseSampler : register(s1);
SamplerState SpecularSampler : register(s2);
SamplerState Decal1Sampler : register(s3);
SamplerState Decal2Sampler : register(s4);
SamplerState LayerSampler : register(s5);
SamplerState BrdfDiffuseSampler : register(s6);
SamplerState BrdfSpecularSampler : register(s7);
SamplerState ReflectionSampler : register(s8);
SamplerState AlphaSampler : register(s10);
SamplerState MaskSampler : register(s11);
SamplerState StippleSampler : register(s14);

struct Vertex
{
    float3 position : POSITION;
    int2 uv0 : TEXCOORD0;
#if LAYERS >= 1
    int2 uv1 : TEXCOORD1;
#endif
#if LAYERS >= 2
    int2 uv2 : TEXCOORD2;
#endif
    float3 normal : NORMAL;     // 0..1 a component
    float4 tangent : TANGENT;   // 0..1; w the bitangent's sign
#if INSTANCED
    float4 row0 : POSITION2;
    float4 row1 : POSITION3;
    float4 row2 : POSITION4;
#endif
#if SKINNED
    uint4 weights : BLENDWEIGHT;  // 0-255 each, out of their sum
    uint4 bones : BLENDINDICES;
#endif
};

#if SKINNED
// A skinned vertex's position or direction in its object's space, as Armageddon's skinned shaders work
// it out (ir_carglass_c's): the four bones' rows mixed by the weights, the position divided by their sum.
void SkinRows(Vertex v, out float4 rows[3], out float inverseSum)
{
    const float4 w = (float4)v.weights;
    inverseSum = 1 / (w.x + w.y + w.z + w.w);
    const int4 b = (int4)v.bones * 3;
    [unroll] for (int r = 0; r < 3; r++)
        rows[r] = w.x * BoneRows[b.x + r] + w.y * BoneRows[b.y + r] + w.z * BoneRows[b.z + r] + w.w * BoneRows[b.w + r];
}
#endif

struct Surface
{
    float4 clip : SV_Position;
    float4 uv01 : TEXCOORD0;    // UV0, UV1 (1 = the texture's width)
    float4 uv2 : TEXCOORD1;     // UV2; w the fog
    float3 fromEye : TEXCOORD2; // the eye to the surface, in the world
    float3 normal : TEXCOORD3;
    float4 tangent : TEXCOORD4;
};

Surface SurfaceVertex(Vertex v)
{
#if INSTANCED
    float4 rows[3] = {v.row0, v.row1, v.row2};
#else
    float4 rows[3] = {ObjectRows[0], ObjectRows[1], ObjectRows[2]};
#endif
    float4 local = float4(v.position * PositionScale.xyz, 1);
    float3 n = v.normal * 2 - 1, t = v.tangent.xyz * 2 - 1;
#if SKINNED
    float4 bone[3];
    float inverseSum;
    SkinRows(v, bone, inverseSum);
    local = float4(float3(dot(bone[0], local), dot(bone[1], local), dot(bone[2], local)) * inverseSum, 1);
    // The directions made unit length again, as Armageddon's skinned shaders do (the bones are mixed by
    // the raw weights, 0-255 each; the glass's ambient reads the normal's length).
    n = normalize(float3(dot(bone[0].xyz, n), dot(bone[1].xyz, n), dot(bone[2].xyz, n)));
    t = normalize(float3(dot(bone[0].xyz, t), dot(bone[1].xyz, t), dot(bone[2].xyz, t)));
#endif
    float4 world = float4(dot(rows[0], local), dot(rows[1], local), dot(rows[2], local), 1);
    Surface s;
    s.clip = float4(dot(world, ProjectionRows[0]), dot(world, ProjectionRows[1]), dot(world, ProjectionRows[2]), dot(world, ProjectionRows[3]));
    s.uv01 = float4(v.uv0, 0, 0) / 1024;
#if LAYERS >= 1
    s.uv01.zw = v.uv1 / 1024.0;
#endif
    s.uv2 = 0;
#if LAYERS >= 2
    s.uv2.xy = v.uv2 / 1024.0;
#endif
    // The game's own fog, as its material shaders work it out.
    s.fromEye = world.xyz - EyePosition.xyz;
    float along = saturate((length(s.fromEye) - FogDistances.y) / (FogDistances.z - FogDistances.y));
    s.uv2.w = FogDistances.x * pow(along, FogDistances.w);
    s.normal = float3(dot(rows[0].xyz, n), dot(rows[1].xyz, n), dot(rows[2].xyz, n));
    s.tangent = float4(dot(rows[0].xyz, t), dot(rows[1].xyz, t), dot(rows[2].xyz, t), v.tangent.w * 2 - 1);
    return s;
}

// Guerrilla's normal maps: x from red × alpha (DXT1 maps have alpha 1, the others red 1), y from green,
// bent toward flat by Normal_Map_Height (0 flat, 1 as drawn).
float3 TangentNormal(float4 t, float height)
{
    float x = (2 * t.r - 1) * (2 * t.a - 1), y = 2 * t.g - 1;
    float z = sqrt(max(1 - x * x - y * y, 0));
    return normalize(float3(height * x, height * y, height * (z - 1) + 1));
}

float3 WorldNormal(Surface s, float3 tangentSpace)
{
    float3 n = normalize(s.normal), t = normalize(s.tangent.xyz);
    float3 b = cross(n, t) * s.tangent.w;
    return normalize(tangentSpace.x * t + tangentSpace.y * b + tangentSpace.z * n);
}

float3 Ambient(float weight) { return lerp(AmbientBelow.rgb, AmbientAbove.rgb, weight); }

// A map's UV: (uv + seconds × scroll) × tiling, the scroll's part kept to 0..1 (the same on a wrapping
// texture), so hours of play cost no precision.
float2 Scrolled(float2 uv, float2 scroll, float2 tiling, float seconds) { return uv * tiling + frac(seconds * scroll * tiling); }

// The BRDF shaders' curves (pow(x, 2.2) and "screen" mixes, as their compiled shaders have them).
float3 Screen(float3 a, float3 b) { return a + b - a * b; }
float3 Screen3(float3 a, float3 b, float3 c) { return Screen(Screen(a, b), c); }
float Gamma(float x) { return pow(max(x, 0), 2.2); }
float2 LookUp(float2 at) { return clamp(at, 0.002, 0.998); }

// Guerrilla's BRDF shaders' light: the ambient (its weight × the occlusion) times the colour, and the
// sun's diffuse and specular by each shader's own curves (kind: 1 rfg-brdf(-e)-concrete, 2 -dull-met,
// 3 (-e)-dull-paint-met, 4 -paint-metal, 5 -rust-paint-met, 6 -edf-base, 7 (-e)-shiny-comp-metal,
// 8 -edf-base-panels, 9 -e-/-ed-edf-base-panels, 10 rfg-brdf and 11 rfg-brdf-env with their look-up
// maps). The specular is the specular map's red, green and blue weighing three lobes; under the layer
// and decal it is taken away (shineLeft).
float3 BrdfLight(int kind, float3 colour, float colourAlpha, float3 specularMap, float3 n, float3 toEye, float nl, float nhRaw,
                 float ao, float shineLeft, float shadow)
{
    const float hl = nl * 0.5 + 0.5, nlc = max(nl, 0), nvc = max(dot(n, toEye), 0), nhc = max(nhRaw, 0);
    const float fo = 1 - (1 - nlc) * BrdfKind.y;          // Specular_falloff
    const float cut = nlc > 0.000244 ? 1 : 0;
    const float3 d1 = BrdfDiffuse[0].rgb, d2 = BrdfDiffuse[1].rgb, d3 = BrdfDiffuse[2].rgb;
    const float3 s1 = BrdfSpecular[0].rgb, s2 = BrdfSpecular[1].rgb, s3 = BrdfSpecular[2].rgb;
    float3 sunDiffuse = 0, sunSpecular = 0;
    float ambientWeight = hl;
    if (kind == 1)
    {
        float r = Gamma(max(1 - sqrt(2 * ((1 - hl) * (1 - hl) + (1 - nvc) * (1 - nvc))), 0)) * ao;
        sunDiffuse = colour * Screen3(d1 * r, d2 * Gamma(saturate(nl)) * ao, d3 * Gamma(saturate(5 * nl)) * ao);
    }
    else if (kind >= 2 && kind <= 6)
    {
        if (kind == 4)
            sunDiffuse = colour * Screen(d1 * Gamma(saturate(nvc) * saturate(nl)), d2 * Gamma(saturate(2 * nl - 1))) * ao;
        else if (kind == 5)
            sunDiffuse = colour * Screen(d1 * Gamma(saturate(1.666667 * nl)), d2 * Gamma(saturate(5 * nl))) * ao;
        else if (kind == 6)
            sunDiffuse = colour * Screen(d1 * Gamma(saturate(2 * nl - 1)), d2 * Gamma(saturate(0.666667 * nl + 0.333333))) * ao;
        else
            sunDiffuse = colour * Screen3(d1 * Gamma(saturate(2 * nl - 1)) * ao, d2 * Gamma(saturate(nl) * min(nvc, 1)) * ao,
                                          d3 * Gamma(saturate(1 + 0.666667 * (hl + nvc - 2))) * ao);
        float3 lobes;
        if (kind == 2) lobes = Gamma(saturate((nhc - 0.82) * 7.142857) * cut);
        else if (kind == 6) lobes = Gamma(saturate((nhc - 0.92) * 14.285714) * cut);
        else lobes = float3(Gamma(saturate((nhc - 0.975) * 50) * cut), Gamma(saturate((nhc - 0.75) * 4.081633) * cut),
                            (nlc > 0.007812 ? 1 : 0) * (nhc >= 0.992188 ? 1 : 0));
        float3 S = specularMap * lobes * fo * ao;
        sunSpecular = Screen3(s1 * S.r, s2 * S.g, s3 * S.b) * Shine.x;
    }
    else if (kind == 7)
    {
        float hlp = max(hl, 0), r = hlp * (1 - nvc);
        sunDiffuse = colour * ao * (d1 * pow(nlc, 5) + d2 * Gamma(nlc * nvc) + d3 * saturate(4 * hlp * r * r * r));
        float p = log2(nhc + 0.0001);
        float3 lobes = float3(max(min(exp2(260 * p), 1) * 1.25, 0), max(min(exp2(40 * p), 1) * 1.25 - 0.1, 0), 1);
        float3 S = specularMap * lobes * (nlc >= 0.001 ? 1 : 0) * nlc * ao;
        sunSpecular = (s1 * S.r + s2 * S.g + s3 * S.b) * Shine.x;
        ambientWeight = hlp;
    }
    else if (kind == 8 || kind == 9)
    {
        sunDiffuse = colour * ao * (d1 * pow(hl, 16) + d2 * pow(hl, 4));
        float3 q = specularMap * float3(pow(nhc, 64), pow(nhc, 32), pow(nhc, 16));
        if (kind == 9) q = saturate(2 * q);
        sunSpecular = (s1 * q.r + s2 * q.g + s3 * q.b) * Shine.x * nlc * ao;
    }
    else if (kind == 10 || kind == 11)
    {
        float3 D = BrdfDiffuseMap.Sample(BrdfDiffuseSampler, LookUp(float2(hl, 1 - nvc))).rgb * ao;
        sunDiffuse = Screen3(D.r * d1, D.g * d2, D.b * d3) * colour;
        float3 S = BrdfSpecularMap.Sample(BrdfSpecularSampler, LookUp(float2(nlc, 1 - nhc))).rgb * fo * ao * specularMap;
        sunSpecular = Screen3(S.r * s1, S.g * s2, S.b * s3) * Shine.x;
    }
    float3 ambient = Ambient(ao * ambientWeight);
    if (kind == 10) ambient = SelfLit(ambient, (1 - colourAlpha) * Glow.x);  // self-lit where the colour map is see-through
    return ambient * colour + (sunDiffuse + sunSpecular * shineLeft) * SunColour.rgb * shadow;
}

// rfg-composite's mask: the panel's own (at UV1) and the core's (at UV1 shifted), each its picked
// channels (BrdfDiffuse[0], [1]: 0 or 1 each), shifted by BrdfDiffuse[2].xy.
void CompositeMask(float4 uv01, out float sub, out float top)
{
    const float4 a0 = AlphaMap.Sample(AlphaSampler, uv01.zw);
    const float4 a1 = AlphaMap.Sample(AlphaSampler, uv01.zw + BrdfDiffuse[2].xy - 0.5);
    sub = min(dot(a0.rgb, trunc(BrdfDiffuse[0].rgb)), 1);
    top = min(dot(a1.rgb, trunc(BrdfDiffuse[1].rgb)), 1);
}

// How much of the surface is there (its alpha, as SurfacePixel works it out), for the depth and
// G-buffer passes of alpha-tested materials.
float SurfaceAlpha(float4 uv01, float4 uv2, float seconds)
{
#if FAMILY == 1
    if (Extra.z > 0.5) return 1;
    return DiffuseMap.Sample(DiffuseSampler, Scrolled(uv01.xy, Scroll.xy, Tiling.xy, seconds)).a;
#elif FAMILY == 3
    const int kind = (int)(BrdfKind.x + 0.5);
    const float a = DiffuseMap.Sample(DiffuseSampler, uv01.xy * Tiling.xy).a;
    return kind >= 7 && kind <= 9 ? 1 : kind == 10 ? a + BrdfKind.w * (1 - a) : a;
#elif FAMILY == 2 || FAMILY == 5
    return DiffuseMap.Sample(DiffuseSampler, uv01.xy * Tiling.xy).a;
#elif FAMILY == 8
    return BrdfKind.x > 0.5 ? 1 : DiffuseMap.Sample(DiffuseSampler, uv01.xy * Tiling.xy).a;
#elif FAMILY == 7
    float sub, top;
    CompositeMask(uv01, sub, top);
    return min(sub + top, 1);
#else
    return 1;
#endif
}

float4 SurfacePixel(Surface s) : SV_Target
{
    const int3 pixel = int3(s.clip.xy, 0);
    const float4 light = LightBuffer.Load(pixel);
    const float seconds = Time.x;

    // The colour, its layers and decals, and the normal.
#if FAMILY == 1
    // Each map scrolled by its own tiling × the scroll, kept to 0..1 (the same on a wrapping texture),
    // so long play costs no precision.
    float4 c0 = DiffuseMap.Sample(DiffuseSampler, Scrolled(s.uv01.xy, Scroll.xy, Tiling.xy, seconds));
    float3 colour = c0.rgb * DiffuseColour.rgb;
  #if LAYERS >= 1
    float4 d1 = DecalMap1.Sample(Decal1Sampler, Scrolled(s.uv01.zw, Scroll.zw, Decals.xy, seconds));
    colour = lerp(colour, d1.rgb, d1.a * Decals.z);
  #endif
  #if LAYERS >= 2
    float4 d2 = DecalMap2.Sample(Decal2Sampler, Scrolled(s.uv2.xy, Extra.xy, 1, seconds));
    colour = lerp(colour, d2.rgb, d2.a * Decals.w);
  #endif
    float3 n = WorldNormal(s, TangentNormal(NormalMap.Sample(NormalSampler, Scrolled(s.uv01.xy, Scroll.xy, float2(NormalTilingU.x, NormalTilingV.x), seconds)), NormalHeight.x));
    float3 specularMap = Glow.w > 0.5 ? c0.aaa : SpecularMap.Sample(SpecularSampler, Scrolled(s.uv01.xy, Scroll.xy, Tiling.zw, seconds)).rgb * SpecularColour.rgb;
    float alpha = Extra.z > 0.5 ? 1 : c0.a;
#elif FAMILY == 5
    // rfg-glass: the colour map untiled, its alpha how much of the pane shows; no normal map.
    float4 c0 = DiffuseMap.Sample(DiffuseSampler, s.uv01.xy);
    float3 colour = c0.rgb * DiffuseColour.rgb;
    float3 n = normalize(s.normal);
    float3 specularMap = SpecularMap.Sample(SpecularSampler, s.uv01.xy * Tiling.zw).rgb * SpecularColour.rgb;
    float alpha = min(c0.a, 1);
#elif FAMILY == 7
    // rfg-composite: the panel (Decal_Map_1) over the broken core (the colour map) by the core's mask,
    // both at UV0 × the colour map's tiling; its holes where neither mask is.
    float2 uvA = s.uv01.xy * Tiling.xy;
    float sub, top;
    CompositeMask(s.uv01, sub, top);
    float3 colour = lerp(DecalMap1.Sample(Decal1Sampler, uvA).rgb, DiffuseMap.Sample(DiffuseSampler, uvA).rgb * DiffuseColour.rgb, top);
    float3 n = WorldNormal(s, TangentNormal(NormalMap.Sample(NormalSampler, s.uv01.xy * float2(NormalTilingU.x, NormalTilingV.x)), NormalHeight.x));
    float alpha = min(sub + top, 1);
#elif FAMILY == 8
    // The vehicle paints: the colour map (at its tiling) under the decal (UV1, untiled); the camouflage
    // masks at the colour map's UV × BRDF_Map_Tiling; the paint's shine by the specular map's green.
    float2 uvM = s.uv01.xy * Tiling.xy;
    float4 c0 = DiffuseMap.Sample(DiffuseSampler, uvM);
    float4 decal = DecalMap1.Sample(Decal1Sampler, s.uv01.zw);
    float3 colour = lerp(c0.rgb * DiffuseColour.rgb, decal.rgb, decal.a * Decals.z);
    float3 mask = MaskMap.Sample(MaskSampler, uvM * Tiling.zw).rgb;
    float3 n = WorldNormal(s, TangentNormal(NormalMap.Sample(NormalSampler, uvM), NormalHeight.x));
    float paintShine = SpecularMap.Sample(SpecularSampler, uvM).g;
    float alpha = BrdfKind.x > 0.5 ? 1 : c0.a;
#elif FAMILY == 9
    // The clear coat: a base of Diffuse_Color under the decal (UV1 × its tiling), the dirt layer
    // (Environment_Map at UV0) over that; no colour map; the normal and specular maps at UV0, untiled.
    float4 decal = DecalMap1.Sample(Decal1Sampler, s.uv01.zw * Decals.xy);
    float dd = decal.a * Decals.z;
    float4 env = LayerMap.Sample(LayerSampler, s.uv01.xy);
    float e = env.a * Shine.z;
    float3 colour = lerp(lerp(DiffuseColour.rgb, decal.rgb, dd), env.rgb, e);
    float3 n = WorldNormal(s, TangentNormal(NormalMap.Sample(NormalSampler, s.uv01.xy), NormalHeight.x));
    float3 specularMap = SpecularMap.Sample(SpecularSampler, s.uv01.xy).rgb * SpecularColour.rgb;
    float alpha = 1;
#elif FAMILY == 6
    // rfg-eosglass: the frame and decal layer (Environment_Map at UV1, by its alpha) over the glass;
    // the colour map tints the reflection; the normal map at the colour map's tiling.
    float2 uvM = s.uv01.xy * Tiling.xy;
    float4 env = LayerMap.Sample(LayerSampler, s.uv01.zw);
    float3 colour = env.rgb * env.a;
    float glassShows = 1 - env.a;
    float3 tangentNormal = TangentNormal(NormalMap.Sample(NormalSampler, uvM), NormalHeight.x);
    float3 n = WorldNormal(s, tangentNormal);
    float alpha = 1;
#else
    // Metal, BRDF and blend-std-metal: every map at UV0 × the colour map's tiling; the layer at UV1,
    // its decal at UV2 (× Decal_Map_1's tiling, which the BRDF's don't have: 1).
    float2 uvM = s.uv01.xy * Tiling.xy;
    float4 c0 = DiffuseMap.Sample(DiffuseSampler, uvM);
    float3 base = c0.rgb * DiffuseColour.rgb;
    float3 n = WorldNormal(s, TangentNormal(NormalMap.Sample(NormalSampler, uvM), NormalHeight.x));
    float4 specularSample = SpecularMap.Sample(SpecularSampler, uvM);
    float3 specularMap = specularSample.rgb * SpecularColour.rgb;
    float4 layer = 0, decal = 0;
    float e = 0, d = 0;
  #if LAYERS >= 1
    layer = LayerMap.Sample(LayerSampler, s.uv01.zw);
    e = layer.a * Shine.z;
  #endif
  #if LAYERS >= 2
    decal = DecalMap1.Sample(Decal1Sampler, s.uv2.xy * Decals.xy);
    d = decal.a * Decals.z;
  #endif
    // The layer over the colour, the decal over that, and where both are, the layer over the decal
    // by 1 − Decal_Map_1_Overlay (the metal's and BRDF's way; blend-std-metal mixes them in its light).
    float3 colour = lerp(lerp(base, layer.rgb, e), decal.rgb, d) + d * e * (1 - Shine.w) * (layer.rgb - decal.rgb);
    float alpha = c0.a;
  #if FAMILY == 3
    const int kind = (int)(BrdfKind.x + 0.5);
    if (kind >= 7 && kind <= 9) alpha = 1;
    if (kind == 10) alpha = c0.a + BrdfKind.w * (1 - c0.a);
  #elif FAMILY == 4
    alpha = 1;
  #endif
#endif

#if CUTOUT
    clip(alpha - AlphaThreshold.x);
#endif

    // Not in the world ported from Guerrilla (the mod lights it): lit by the light buffer, as
    // Armageddon's own material shaders are.
    if (SunToward.w < 0.5)
    {
        float3 lit = colour * light.rgb;
        lit = lit * (1 - s.uv2.w) + s.uv2.w * FogColour.rgb;
        return float4(lit, alpha) * TintColour;
    }

    // What the light buffer holds here: the occlusion, the lamps' light, the sun's shadow.
    const float3 luminance = float3(0.3, 0.59, 0.11);
    const float4 ours = OurLight.Load(pixel);
    const float before = dot(LightBefore.Load(pixel).rgb, luminance);
    const float occlusion = before > 0.0001 ? saturate(dot(light.rgb, luminance) / before) : 1;
    const float3 lamps = max(light.rgb - ours.rgb * occlusion, 0);
    const float shadow = ours.a;

    const float3 toLight = SunToward.xyz;
    const float3 toEye = -normalize(s.fromEye);
    const float3 halfway = normalize(toEye + toLight);
    const float nl = dot(n, toLight), nlLit = saturate(nl);
    float3 rgb;

#if FAMILY == 1
    // Lambert, Blinn, and the ambient lifted toward 4 by the self-illumination.
    float3 specular = specularMap * (pow(saturate(dot(n, halfway)), SpecularPower.x + 0.00001) * Shine.x) * nlLit;
    float3 ambient = Ambient(occlusion * (nl * 0.5 + 0.5));
    float glow = Glow.y > 0.5 ? Time.w : Glow.x;
    if (Glow.z >= 0) glow *= (cos(seconds * 2 / (Glow.z + 0.01)) + 1) * 0.5;  // rfg-animated's pulse
    ambient = SelfLit(ambient, glow);
    rgb = ambient * colour + (colour * occlusion * nlLit + specular) * SunColour.rgb * shadow;
    rgb += lamps * colour;
#elif FAMILY == 2
    // The metal lobe: (N · H)^Metal_Base_Shine × the half-Lambert, no plain Lambert.
    float nh = max(dot(n, halfway), 0);
    float halfLambert = nlLit * 0.5 + 0.5;
    float lobe = pow(min(nh, 1), Shine.y);
    float3 specular = specularMap * pow(nh, SpecularPower.x + 0.00001) * Shine.x;
    float3 ambient = Ambient(occlusion * halfLambert);
  #if LAYERS == 0
    rgb = ambient * base + (base * occlusion * lobe * halfLambert + specular * occlusion) * SunColour.rgb * shadow;
  #elif LAYERS == 1
    float k = max(lerp(lobe * halfLambert, nlLit, e), 0);
    rgb = ambient * colour * occlusion + (colour * k + specular * (1 - e) * occlusion) * SunColour.rgb * shadow;
  #else
    float m = max(e, d);
    float k = occlusion * max(lerp(lobe * halfLambert, nlLit, m), 0);
    rgb = ambient * colour + (colour * k + specular * (1 - m) * occlusion) * SunColour.rgb * shadow;
  #endif
    rgb += lamps * colour;
#elif FAMILY == 3
    rgb = BrdfLight(kind, colour, c0.a, specularSample.rgb, n, toEye, nl, dot(n, halfway), occlusion, (1 - e) * (1 - d), shadow);
    rgb += lamps * colour;
#elif FAMILY == 5
    // Glass: the ambient by the half-Lambert of the normal as the vertices give it (not made unit length,
    // as Guerrilla's), and the sun's shine; no Lambert, occlusion, shadow or lamps (the light buffer
    // here is what lies behind the pane).
    float3 specular = specularMap * pow(max(dot(n, halfway), 0), SpecularPower.x + 0.00001) * Shine.x;
    rgb = Ambient(dot(s.normal, toLight) * 0.5 + 0.5) * colour + specular * SunColour.rgb;
#elif FAMILY == 7
    // The composite: the half-Lambert ambient without occlusion (as Guerrilla's, which reads none),
    // lifted by the self-illumination (0 on every one of its materials), the sun's Lambert; no shine.
    float3 ambient = Ambient(nl * 0.5 + 0.5);
    ambient = SelfLit(ambient, Glow.x);
    rgb = ambient * colour + colour * nlLit * SunColour.rgb * shadow;
    rgb += lamps * colour;
#elif FAMILY == 8
    // The paint's tint: three colours (BRDF_Spec_1-3) on the three masks, each weighed by where the
    // angle between the sun and the eye falls (a hue ramp), seen most face on (N·V to the 8th), not under
    // the decal (its alpha as it is).
    const float lv = dot(toLight, toEye);
    const float3 w = float3(saturate(abs(3 * lv) - 1), 1 - saturate(abs(3 * lv + 1) - 1), 1 - saturate(abs(3 * lv - 1) - 1));
    if (BrdfKind.x > 0.5)
        rgb = mask * w * SunColour.rgb * shadow;  // rfg-colorshift: the ramp alone, no normal, no ambient
    else
    {
        const float3 tint = BrdfSpecular[0].rgb * (mask.r * w.x) + BrdfSpecular[1].rgb * (mask.g * w.y) + BrdfSpecular[2].rgb * (mask.b * w.z);
        const float3 paint = tint * (paintShine * pow(saturate(dot(toEye, n)), 8)) * (1 - decal.a);
        rgb = Ambient(occlusion * (nl * 0.5 + 0.5)) * (colour + paint * occlusion) + (colour * occlusion * nlLit + paint * occlusion) * SunColour.rgb * shadow;
        rgb += lamps * colour;
    }
#elif FAMILY == 9
    // The coat's shine and rim (Clearcoat_Color, BrdfSpecular[0]; their powers and strengths,
    // BrdfSpecular[1]) fade under the dirt layer past Clearcoat_Dirt_Threshold (BrdfSpecular[0].w); the
    // specular map's shine under the decal and the layer.
    const float nh = max(dot(n, halfway), 0), nv = max(dot(n, toEye), 0);
    const float threshold = BrdfSpecular[0].w;
    const float dirt = saturate((1 - e) - threshold) / (1 - threshold);
    const float3 shine = specularMap * pow(nh, SpecularPower.x + 0.00001) * Shine.x * (1 - dd) * (1 - e);
    const float3 coat = BrdfSpecular[0].rgb * dirt * pow(nh, BrdfSpecular[1].x + 0.00001) * BrdfSpecular[1].y;
    const float3 rim = BrdfSpecular[0].rgb * dirt * pow(1 - nv, BrdfSpecular[1].z + 0.00001) * BrdfSpecular[1].w;
    rgb = Ambient(occlusion * (nl * 0.5 + 0.5)) * colour + (colour * occlusion * nlLit + (shine + coat + rim) * nlLit * occlusion) * SunColour.rgb * shadow;
    rgb += lamps * colour;
#elif FAMILY == 6
    // Mirror glass: the mirror direction by the vertex normal, its x turned round, with the normal map's
    // x and y added (Guerrilla's own way); the reflection a little darker in shadow, not lit by the sun's
    // angle; the layer lit as the standard shaders, the glass's shine by Specular_Color alone.
    float3 N = normalize(s.normal);
    float3 R = 2 * dot(N, toEye) * N - toEye;
    float3 direction = normalize(float3(tangentNormal.x - R.x, tangentNormal.y + R.y, R.z));
    float3 reflection = DiffuseMap.Sample(DiffuseSampler, uvM).rgb * ReflectionCube.Sample(ReflectionSampler, direction).rgb
                      * glassShows * (shadow * 0.7 + 0.3);
    float3 specular = SpecularColour.rgb * glassShows * Shine.x * pow(saturate(dot(n, halfway)), SpecularPower.x + 0.00001);
    rgb = Ambient(occlusion * (nl * 0.5 + 0.5)) * colour + (colour * occlusion * nlLit + specular) * SunColour.rgb * shadow
        + reflection * SunColour.rgb;
    rgb += lamps * colour;
#else
    // Blend-std-metal: the metal lobe and specular where the specular map's alpha is 1, the standard
    // Lambert and specular where it is 0 (the ambient ignores the layer and the decal, as Guerrilla's).
    float a = specularSample.a;
    float3 bA = base * occlusion;
    float ln = log2(saturate(dot(n, halfway)));
    float pM = exp2(ln * Shine.y), pS = exp2(ln * SpecularPower.x), pMS = exp2(ln * MetalSpecularPower.x);
    float3 specular = lerp(pS * Shine.x * SpecularColour.rgb, pMS * MetalSpecular.w * MetalSpecular.rgb, a) * specularSample.rgb;
    float halfLambert = nlLit * 0.5 + 0.5;
    float3 ambient = Ambient(occlusion * halfLambert);
  #if LAYERS == 0
    rgb = ambient * bA + (bA * lerp(nlLit, pM * nlLit, a) + specular * occlusion) * SunColour.rgb * shadow;
  #elif LAYERS == 1
    float3 sunlit = lerp(bA * lerp(nlLit, pM * halfLambert, a), layer.rgb * nlLit, e);
    rgb = ambient * bA + (sunlit + specular * (1 - e) * occlusion) * SunColour.rgb * shadow;
  #else
    float nl1 = nlLit + 0.00001, halfLambert1 = nl1 * 0.5 + 0.5;
    ambient = Ambient(occlusion * halfLambert1);
    float k = lerp(1, pM * halfLambert1 / nl1, a);
    float3 mixed = lerp(lerp(bA * k, layer.rgb, e), decal.rgb, d) + d * e * (1 - Shine.w) * (layer.rgb - decal.rgb);
    rgb = ambient * bA + (mixed * nl1 + specular * (1 - e) * (1 - d) * occlusion) * SunColour.rgb * shadow;
  #endif
    rgb += lamps * colour;
#endif

    rgb = rgb * (1 - s.uv2.w) + s.uv2.w * FogColour.rgb;
    return float4(rgb, alpha) * TintColour;
}

// ---- The depth (shadow) and G-buffer passes of alpha-tested materials (CUTOUT), cutting the holes
// SurfacePixel cuts, the G-buffer written as Armageddon's G-buffer shaders write it (ir_at_default's,
// line by line): the normal in the camera's space, the shine, the object's id.

float4 WorldPosition(Vertex v, out float4 rows[3])
{
#if INSTANCED
    rows[0] = v.row0; rows[1] = v.row1; rows[2] = v.row2;
#else
    rows[0] = ObjectRows[0]; rows[1] = ObjectRows[1]; rows[2] = ObjectRows[2];
#endif
    const float4 local = float4(v.position * PositionScale.xyz, 1);
    return float4(dot(rows[0], local), dot(rows[1], local), dot(rows[2], local), 1);
}

float4 ClipPosition(float4 world) { return float4(dot(world, ProjectionRows[0]), dot(world, ProjectionRows[1]), dot(world, ProjectionRows[2]), dot(world, ProjectionRows[3])); }

void UvSets(Vertex v, out float4 uv01, out float4 uv2)
{
    uv01 = float4(v.uv0, 0, 0) / 1024;
#if LAYERS >= 1
    uv01.zw = v.uv1 / 1024.0;
#endif
    uv2 = 0;
#if LAYERS >= 2
    uv2.xy = v.uv2 / 1024.0;
#endif
}

struct Depth
{
    float4 clip : SV_Position;
    float4 uv01 : TEXCOORD0;
    float4 uv2 : TEXCOORD1;
};

Depth DepthVertex(Vertex v)
{
    float4 rows[3];
    Depth d;
    d.clip = ClipPosition(WorldPosition(v, rows));
    UvSets(v, d.uv01, d.uv2);
    return d;
}

float4 DepthPixel(Depth d) : SV_Target
{
    const float alpha = SurfaceAlpha(d.uv01, d.uv2, Time.x);
    clip(alpha - AlphaThreshold.x);
    return float4(0, 0, 0, alpha);
}

struct Geometry
{
    float4 clip : SV_Position;
    float4 uv01 : TEXCOORD0;
    float4 uv2 : TEXCOORD1;
    float3 normal : TEXCOORD2;      // in the camera's space
    float3 tangent : TEXCOORD3;
    float4 bitangentId : TEXCOORD4; // the bitangent; w the object's id as the G-buffer keeps it
    float4 screen : TEXCOORD5;      // the clip position again (the stipple's)
};

Geometry GeometryVertex(Vertex v)
{
    float4 rows[3];
    Geometry g;
    g.clip = ClipPosition(WorldPosition(v, rows));
    g.screen = g.clip;
    UvSets(v, g.uv01, g.uv2);
    const float3 n = v.normal * 2 - 1;
    const float4 t = v.tangent * 2 - 1;
    const float3 nWorld = normalize(float3(dot(rows[0].xyz, n), dot(rows[1].xyz, n), dot(rows[2].xyz, n)));
    const float3 tWorld = normalize(float3(dot(rows[0].xyz, t.xyz), dot(rows[1].xyz, t.xyz), dot(rows[2].xyz, t.xyz)));
    g.normal = float3(dot(nWorld, WorldToView[0].xyz), dot(nWorld, WorldToView[1].xyz), dot(nWorld, WorldToView[2].xyz));
    g.tangent = float3(dot(tWorld, WorldToView[0].xyz), dot(tWorld, WorldToView[1].xyz), dot(tWorld, WorldToView[2].xyz));
    // The tangent's w (0 to 1) gives the bitangent's sign and, with the object's number, its id.
    const float w = t.w * 0.5 + 0.5;
    const bool up = w >= 0.5;
    g.bitangentId.xyz = cross(g.normal, g.tangent) * (up ? 1 : -1);
    g.bitangentId.w = (ObjectParams2.x * 32640 + (w * 255 + (up ? -128 : 0))) * (1.0 / 65535);
    return g;
}

struct GBuffer
{
    float4 normal : SV_Target0;
    float4 other : SV_Target1;
};

GBuffer GeometryPixel(Geometry g)
{
    clip(SurfaceAlpha(g.uv01, g.uv2, Time.x) - AlphaThreshold.x);
#if STIPPLED
    const float2 at = g.screen.xy / g.screen.w + 1;
    if (StipplePattern.Sample(StippleSampler, float2(at.x * StippleRepeat.x * 0.5, (1 - at.y * 0.5) * StippleRepeat.y)).x - 0.5 < 0) discard;
#endif
    // The normal map where SurfacePixel samples it.
#if FAMILY == 1
    const float2 uvN = Scrolled(g.uv01.xy, Scroll.xy, float2(NormalTilingU.x, NormalTilingV.x), Time.x);
#elif FAMILY == 7
    const float2 uvN = g.uv01.xy * float2(NormalTilingU.x, NormalTilingV.x);
#else
    const float2 uvN = g.uv01.xy * Tiling.xy;
#endif
    const float3 ts = TangentNormal(NormalMap.Sample(NormalSampler, uvN), NormalHeight.x);
    float3 n = ts.x * normalize(g.tangent) + ts.y * normalize(g.bitangentId.xyz) + ts.z * normalize(g.normal);
    n = normalize(n);
    GBuffer o;
    o.normal.xy = n.xy * 0.5 + LightCategory.xy + 0.5;
    o.normal.z = max(1 - abs(n.z), 0) * (127.0 / 255) + LightCategory.z;
    o.normal.w = max(sqrt(min(50, SpecularPower.x)) * (18.0 / 255), 1.0 / 255) + LightCategory.w;
    o.other = float4(1, g.bitangentId.w, 0, 1);
    return o;
}
