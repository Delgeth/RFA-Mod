// Guerrilla's terrain shaders' lit pass (its rl_terrain_height_mesh_t: the ground, with its
// rl_terrain_height_mesh_lowlod_t further off; rl_terrain_low_lod_t: the far ground; rl_terrain_sidemap_t
// and _single_t: its cliffs; rl_terrain_road_layer2, 3 and 4: its roads), as the material pass of
// Armageddon's terrain effects. Armageddon's own vertex shaders and G-buffer passes stay (the world
// builder copies them from ir_terrain_height_mesh_t, _blend_t, _lowlod_t, ir_terrain_sidemap_t and
// ir_terrain_road_layer2, 3 and 4), so the ground, cliffs and roads are placed and depth-tested
// exactly as before; only the colour pass is Guerrilla's. Each pixel shader reads its vertex shader's
// outputs as that one writes them (registers named below).
//
// Armageddon's terrain material pass blends every draw by its alpha (SRC_ALPHA / INV_SRC_ALPHA), as
// Guerrilla's terrain pass does: the ground (alpha 1) is drawn first, then a cliff where Guerrilla's
// cliff blocks overlap the ground, by the cliff's vertex alpha. So each is lit by its own normal and the
// cliff fades over the ground, as in Guerrilla. The light: as GuerrillaSurfaces.hlsl, from the
// guerrilla_world mod's b8, t16, t17, and for the ground also the G-buffer's normals (t18): the ground's
// normal mapped by its four layers with Guerrilla's weights, which Armageddon's G-buffer pass writes
// (in the camera's space; the cliffs write theirs only where they are whole, the ring keeps the
// ground's).
//
// Defines: SURFACE 1 the ground (ir_terrain_height_mesh_t's place), 2 the ground fading to the zone's
// colour map with distance (_blend_t's), 3 a cliff (ir_terrain_sidemap_t's: two textures; one drawn as
// the same texture twice, as Armageddon's one-texture cliff shader has no blend map coordinates, so
// couldn't fade to the colour map far off as Guerrilla's do), 5 a road (ir_terrain_road_layerN's), with ROAD 2, 3 or 4 its variant (how the
// road's own texture lies among the zone's ground layers: see the road's weights below), 6 the far
// ground (ir_terrain_height_mesh_lowlod_t's: the zones' far meshes, where their sub-zones aren't loaded).

cbuffer Material : register(b0)  // the material's constants, at the registers Armageddon's shaders put them
{
#if SURFACE <= 2
    float4 Layer1ScaleOffset;   // c0
    float4 Layer2ScaleOffset;   // c1
    float4 Layer3ScaleOffset;   // c2
    float4 Layer0ScaleOffset;   // c3
    float4 SpecularPower;       // c4 (Armageddon's G-buffer pass)
    float4 LayerShineAlpha;     // c5 Terrain_layer_spec_alpha, a layer each
    float4 LayerColour[4];      // c6-9 Diffuse_color_0-3
    float4 LayerShinePower;     // c10 Terrain_layer_spec_power, a layer each
#elif SURFACE == 5
    float4 LayerShinePower;     // c0 Specular_Power: Guerrilla's Terrain_layer_spec_power, a layer each (the road's last)
    float4 Layer1ScaleOffset;   // c1
    float4 Layer2ScaleOffset;   // c2
    float4 Layer0ScaleOffset;   // c3
    float4 Tile;                // c4: across the road from its texture's u (the vertex shader's)
    float4 EdgeFade;            // c5 Edge_fade__use_spec_map: the fade at the road's sides (x scale, y offset); z 1 where the road texture's alpha is its shine, not its edge
    float4 LayerColour[4];      // c6-9 Diffuse_color_0-3 (the road's last)
    float4 LayerShineAlpha;     // c10 Terrain_layer_spec_alpha, a layer each
#elif SURFACE == 6
    float4 MaterialUnused;      // the far ground's material has no constants
#else
    float4 Xform[16];           // c0-15 Terrain_layer0-3_xform
    float4 SpecularPower;       // c16
    float4 CliffColour[2];      // c17, 18 Diffuse_color_0, 1
#endif
};

cbuffer Frame : register(b4)    // the engine's, per frame (vc4)
{
    float4 TintSaturation;
    float4 TintColour;          // c1
    float4 EngineAmbient[2];
    float4 FogColour;           // c4
    float4 FrameUnused[25];
    float4 LodBlend;            // c30 Terrain_lod_blend_info: where the ground starts fading to its colour map, and over how far
};

cbuffer Guerrilla : register(b8)  // the guerrilla_world mod's (as GuerrillaSurfaces.hlsl's)
{
    float4 SunToward;           // xyz toward the sun or moon, in the world; w 1 while the mod lights the world
    float4 SunColour;           // Guerrilla's V_light_color
    float4 AmbientAbove;        // V_ambient_render
    float4 AmbientBelow;        // V_back_ambient_render
    float4 Time;
    float4 SunTowardView;       // the same toward, in the camera's space
    float4 View;                // the picture's width and height; the tangents of half the field of view across and up
};

Texture2D LightBuffer : register(t12);
Texture2D OurLight : register(t16);
Texture2D LightBefore : register(t17);
Texture2D Normals : register(t18);  // the G-buffer's

#if SURFACE <= 2
Texture2D BlendMap : register(t4);
  #if SURFACE == 1
Texture2D Layer0 : register(t6);
Texture2D Layer1 : register(t7);
Texture2D Layer2 : register(t8);
Texture2D Layer3 : register(t10);
SamplerState Layer0Sampler : register(s6);
SamplerState Layer1Sampler : register(s7);
SamplerState Layer2Sampler : register(s8);
SamplerState Layer3Sampler : register(s10);
  #else
Texture2D Layer0 : register(t5);
Texture2D Layer1 : register(t6);
Texture2D Layer2 : register(t7);
Texture2D Layer3 : register(t8);
Texture2D ColourMap : register(t10);
SamplerState Layer0Sampler : register(s5);
SamplerState Layer1Sampler : register(s6);
SamplerState Layer2Sampler : register(s7);
SamplerState Layer3Sampler : register(s8);
SamplerState ColourSampler : register(s10);
  #endif
SamplerState BlendSampler : register(s4);
#elif SURFACE == 5
Texture2D BlendMap : register(t4);
Texture2D Road : register(t5);
Texture2D Layer0 : register(t6);
Texture2D Layer1 : register(t7);
Texture2D Layer2 : register(t8);
SamplerState BlendSampler : register(s4);
SamplerState RoadSampler : register(s5);
SamplerState Layer0Sampler : register(s6);
SamplerState Layer1Sampler : register(s7);
SamplerState Layer2Sampler : register(s8);
#elif SURFACE == 6
Texture2D ColourMap : register(t0);
SamplerState ColourSampler : register(s0);
#else
Texture2D Normal0 : register(t0);
Texture2D Normal1 : register(t1);
Texture2D AlphaMap : register(t2);
Texture2D Colour0 : register(t4);
Texture2D Colour1 : register(t5);
Texture2D ColourMap : register(t10);
SamplerState Normal0Sampler : register(s0);
SamplerState Normal1Sampler : register(s1);
SamplerState AlphaSampler : register(s2);
SamplerState Colour0Sampler : register(s4);
SamplerState Colour1Sampler : register(s5);
SamplerState ColourSampler : register(s10);
#endif

// What Armageddon's material vertex shaders write (TEXCOORDn = their o(n+1)).
struct Surface
{
    float4 position : SV_Position;
#if SURFACE == 1
    float4 clip : TEXCOORD0;      // o1
    float4 uv01 : TEXCOORD1;      // o2: layer 0's UV, layer 1's
    float4 uv23 : TEXCOORD2;      // o3: layer 2's, layer 3's
    float3 blendFog : TEXCOORD3;  // o4: the blend map's UV, the fog
#elif SURFACE == 2
    float2 uv1 : TEXCOORD0;       // o1: layer 1's UV
    float4 clip : TEXCOORD1;      // o2
    float4 toEyeFog : TEXCOORD2;  // o3: the surface to the eye (world), the fog
    float4 uv02 : TEXCOORD3;      // o4: layer 0's UV, layer 2's
    float4 uv3Blend : TEXCOORD4;  // o5: layer 3's, the blend map's
#elif SURFACE == 5
    float fog : TEXCOORD0;        // o1.x
    float4 clip : TEXCOORD1;      // o2
    float4 uv01 : TEXCOORD2;      // o3: layer 0's UV, layer 1's
    float4 uv2Road : TEXCOORD3;   // o4: layer 2's, the road texture's (its sign to be dropped)
    float4 blendRoad : TEXCOORD4; // o5: the blend map's UV; how much of the road shows (the vertex's normal w × 2 − 1); across the road (−1 to 1)
#elif SURFACE == 6
    float4 clip : TEXCOORD0;      // o1
    float4 uvFog : TEXCOORD1;     // o2: the colour map's UV, the fog
#else
    float2 uvSide1 : TEXCOORD0;   // o1: xform 1's UV
    float4 normal : TEXCOORD1;    // o2: the world normal, the cliff's alpha (w × 2 − 1)
    float4 clip : TEXCOORD2;      // o3
    float4 toEyeFog : TEXCOORD3;  // o4: the surface to the eye, the fog
    float4 uvSide02 : TEXCOORD4;  // o5: xform 0's UV, xform 2's
    float4 uvSide3Blend : TEXCOORD5;  // o6: xform 3's, the blend map's
#endif
};

float3 Ambient(float weight) { return lerp(AmbientBelow.rgb, AmbientAbove.rgb, weight); }

// Guerrilla's terrain normal maps: x from red × alpha, y from green (no height bend).
float3 Decoded(float4 t)
{
    float x = (2 * t.r - 1) * (2 * t.a - 1), y = 2 * t.g - 1;
    return float3(x, y, sqrt(max(1 - x * x - y * y, 0)));
}

// The lamps' light, the occlusion and the sun's shadow at this pixel (see GuerrillaSurfaces.hlsl).
void Light(int3 pixel, float4 light, out float occlusion, out float3 lamps, out float shadow)
{
    const float3 luminance = float3(0.3, 0.59, 0.11);
    const float4 ours = OurLight.Load(pixel);
    const float before = dot(LightBefore.Load(pixel).rgb, luminance);
    occlusion = before > 0.0001 ? saturate(dot(light.rgb, luminance) / before) : 1;
    lamps = max(light.rgb - ours.rgb * occlusion, 0);
    shadow = ours.a;
}

float FarFade(float distance) { return LodBlend.y > 0 ? saturate((distance - LodBlend.x) / LodBlend.y) : 0; }

#if SURFACE <= 2 || SURFACE >= 5
// The normal as the G-buffer holds it, in the camera's space (Armageddon's G-buffer pass wrote it:
// the ground's mapped by its layers' normal maps, the far ground's its vertices').
float3 ViewNormal(int3 pixel)
{
    float3 n = Normals.Load(pixel).xyz;
    if (n.z >= 0.501961) n.z -= 0.5;
    n = n * 2 - 1;
    return dot(n, n) > 0.000001 ? normalize(n) : float3(0, 1, 0);
}

// The ground's light, and the roads' (Guerrilla's are the same): its normal from the G-buffer, the sun
// and the eye in the camera's space; half-Lambert ambient by the occlusion, the sun by the shadow,
// with a shine. Mixed by lod2 toward Guerrilla's ground further off (its LOD 2,
// rl_terrain_height_mesh_lowlod_t, from 250 m: the front ambient alone, no occlusion; the vertex
// normal, which the G-buffer's fades to with the same distance).
float3 GroundLit(float4 position, float4 light, float3 colour, float shineAmount, float shinePower, float lod2 = 0)
{
    const int3 pixel = int3(position.xy, 0);
    float occlusion, shadow;
    float3 lamps;
    Light(pixel, light, occlusion, lamps, shadow);
    const float3 n = ViewNormal(pixel);
    const float2 at = position.xy / View.xy;
    const float3 toEye = -normalize(float3((at * float2(2, -2) + float2(-1, 1)) * View.zw, 1));
    const float3 toLight = SunTowardView.xyz;
    const float3 halfway = normalize(toEye + toLight);
    const float nl = dot(n, toLight), nlLit = saturate(nl);
    const float shine = nlLit * shineAmount * exp2((shinePower + 0.00001) * log2(saturate(dot(n, halfway))));
    const float3 near = Ambient(occlusion * (nl * 0.5 + 0.5)) * colour + (colour * occlusion * nlLit + shine) * SunColour.rgb * shadow;
    const float3 far = AmbientAbove.rgb * colour + colour * nlLit * SunColour.rgb * shadow;
    return lerp(near, far, lod2) + lamps * colour;
}
#endif

float4 TerrainPixel(Surface s) : SV_Target
{
    const int3 pixel = int3(s.position.xy, 0);
    const float4 light = LightBuffer.Load(pixel);

#if SURFACE <= 2
    // The ground: four layers mixed by the blend map, a shine a layer (by its colour map's alpha).
  #if SURFACE == 1
    const float2 uv0 = s.uv01.xy, uv1 = s.uv01.zw, uv2 = s.uv23.xy, uv3 = s.uv23.zw, uvBlend = s.blendFog.xy;
    const float fog = s.blendFog.z, distance = s.clip.w;
  #else
    const float2 uv0 = s.uv02.xy, uv1 = s.uv1, uv2 = s.uv02.zw, uv3 = s.uv3Blend.xy, uvBlend = s.uv3Blend.zw;
    const float fog = s.toEyeFog.w, distance = length(s.toEyeFog.xyz);
  #endif
    // Guerrilla's ground beyond 130 m blurs its colour maps (its LOD 1: a mip bias to 2 by 170 m).
    const float bias = 2 * saturate((distance - 130) * 0.05);
    const float4 b = BlendMap.Sample(BlendSampler, uvBlend);
    const float4 w = float4((1 - b.b) * (1 - b.g) * (1 - b.r), b.b * (1 - b.g) * (1 - b.r), b.g * (1 - b.r), b.r);
    const float4 d0 = Layer0.SampleBias(Layer0Sampler, uv0, bias), d1 = Layer1.SampleBias(Layer1Sampler, uv1, bias);
    const float4 d2 = Layer2.SampleBias(Layer2Sampler, uv2, bias), d3 = Layer3.SampleBias(Layer3Sampler, uv3, bias);
    float3 colour = w.x * d0.rgb * LayerColour[0].rgb + w.y * d1.rgb * LayerColour[1].rgb + w.z * d2.rgb * LayerColour[2].rgb + w.w * d3.rgb * LayerColour[3].rgb;
    float shineAmount = dot(float4(d0.a, d1.a, d2.a, d3.a) * LayerShineAlpha, w);
    const float shinePower = dot(LayerShinePower, w);
    float lod2 = 0;
  #if SURFACE == 2
    // Fading to the zone's colour map with distance (the G-buffer's normal fades to flat with it), and
    // its light to Guerrilla's further off (see GroundLit).
    const float fade = FarFade(distance);
    const float3 far = ColourMap.Sample(ColourSampler, uvBlend).rgb;
    colour = lerp(colour, far * far * far, fade);
    shineAmount *= 1 - fade;
    lod2 = fade;
  #endif
    if (SunToward.w < 0.5)
    {
        float3 lit = colour * light.rgb;
        return float4(lit * (1 - fog) + fog * FogColour.rgb, 1) * TintColour;
    }
    const float3 rgb = GroundLit(s.position, light, colour, shineAmount, shinePower, lod2);
    return float4(rgb * (1 - fog) + fog * FogColour.rgb, 1) * TintColour;
#elif SURFACE == 5
    // A road: three of the zone's ground layers mixed by its blend map as the ground's first three are
    // (its fourth, red, isn't in Guerrilla's roads either), and the road's own texture by how much of
    // it shows: its alpha (or all of it, where its alpha is its shine), the vertex's weight (none at the
    // road's ends and joins) and the fade at its sides. Drawn whole (the ground is cut open under it).
    const float4 b = BlendMap.Sample(BlendSampler, s.blendRoad.xy);
    const float4 d0 = Layer0.Sample(Layer0Sampler, s.uv01.xy), d1 = Layer1.Sample(Layer1Sampler, s.uv01.zw);
    const float4 d2 = Layer2.Sample(Layer2Sampler, s.uv2Road.xy), d3 = Road.Sample(RoadSampler, abs(s.uv2Road.zw));
    const float road = saturate(d3.a + EdgeFade.z) * saturate(s.blendRoad.z) * saturate((1 - abs(s.blendRoad.w)) * EdgeFade.x + EdgeFade.y);
    // Where the variants lay the road among the layers: layer2 under the second and third layers (blue
    // and green), layer3 under the third, layer4 over all three.
  #if ROAD == 2
    const float4 w = float4((1 - road) * (1 - b.b) * (1 - b.g), b.b * (1 - b.g), b.g, road * (1 - b.b) * (1 - b.g));
  #elif ROAD == 3
    const float4 w = float4((1 - b.b) * (1 - road) * (1 - b.g), b.b * (1 - road) * (1 - b.g), b.g, road * (1 - b.g));
  #else
    const float4 w = float4((1 - b.b) * (1 - b.g) * (1 - road), b.b * (1 - b.g) * (1 - road), b.g * (1 - road), road);
  #endif
    const float3 colour = w.x * d0.rgb * LayerColour[0].rgb + w.y * d1.rgb * LayerColour[1].rgb + w.z * d2.rgb * LayerColour[2].rgb + w.w * d3.rgb * LayerColour[3].rgb;
    const float fog = s.fog;
    if (SunToward.w < 0.5)
    {
        float3 lit = colour * light.rgb;
        return float4(lit * (1 - fog) + fog * FogColour.rgb, 1) * TintColour;
    }
    const float shineAmount = dot(float4(d0.a, d1.a, d2.a, saturate(d3.a + 1 - EdgeFade.z)) * LayerShineAlpha, w);
    const float shinePower = dot(LayerShinePower, w);
    const float3 rgb = GroundLit(s.position, light, colour, shineAmount, shinePower);
    return float4(rgb * (1 - fog) + fog * FogColour.rgb, 1) * TintColour;
#elif SURFACE == 6
    // The far ground: the zone's colour map, cubed, lit as Guerrilla's far ground (rl_terrain_low_lod_t):
    // half-Lambert ambient by the occlusion, the sun by its vertex normal, without shadow.
    const float3 c = ColourMap.Sample(ColourSampler, s.uvFog.xy).rgb;
    const float3 colour = c * c * c;
    const float fog = s.uvFog.z;
    if (SunToward.w < 0.5)
    {
        float3 lit = colour * light.rgb;
        return float4(lit * (1 - fog) + fog * FogColour.rgb, 1) * TintColour;
    }
    float occlusion, shadow;
    float3 lamps;
    Light(pixel, light, occlusion, lamps, shadow);
    const float nl = dot(ViewNormal(pixel), SunTowardView.xyz);
    const float3 rgb = Ambient(occlusion * (nl * 0.5 + 0.5)) * colour + colour * saturate(nl) * SunColour.rgb + lamps * colour;
    return float4(rgb * (1 - fog) + fog * FogColour.rgb, 1) * TintColour;
#else
    // A cliff: its textures projected from the two sides (x and z), mixed by how the slope faces;
    // two textures mixed by the blend map's alpha; lit in Guerrilla's own (skewed) tangent frame.
    const float3 N = s.normal.xyz;
    const float side = saturate((abs(N.x) / (abs(N.z) + abs(N.x)) - 0.45) * 10);
    const float2 uvA = s.uvSide02.xy, uvB = s.uvSide1, uvC = s.uvSide02.zw, uvD = s.uvSide3Blend.xy, uvBlend = s.uvSide3Blend.zw;
    const float fog = s.toEyeFog.w, distance = length(s.toEyeFog.xyz);
    const float4 c0 = lerp(Colour0.Sample(Colour0Sampler, uvA), Colour0.Sample(Colour0Sampler, uvB), side);
    const float3 n0 = lerp(Decoded(Normal0.Sample(Normal0Sampler, uvA)), Decoded(Normal0.Sample(Normal0Sampler, uvB)), side);
    const float4 c1 = lerp(Colour1.Sample(Colour1Sampler, uvC), Colour1.Sample(Colour1Sampler, uvD), side);
    const float3 n1 = lerp(Decoded(Normal1.Sample(Normal1Sampler, uvC)), Decoded(Normal1.Sample(Normal1Sampler, uvD)), side);
    const float mix = (AlphaMap.Sample(AlphaSampler, uvBlend).a * 255 - 1) * 0.003953;
    float3 colour = lerp(c0.rgb * CliffColour[0].rgb, c1.rgb * CliffColour[1].rgb, mix);
    float3 n = lerp(n0, n1, mix);
    // Far off, as the ground: to the zone's colour map and flat.
    const float fade = FarFade(distance);
    const float3 far = ColourMap.Sample(ColourSampler, uvBlend).rgb;
    colour = lerp(colour, far * far * far, fade);
    n = lerp(n, float3(0, 0, 1), fade);
    const float alpha = saturate(s.normal.w);
    if (SunToward.w < 0.5)
    {
        float3 lit = colour * light.rgb;
        return float4(lit * (1 - fog) + fog * FogColour.rgb, alpha) * TintColour;
    }
    float occlusion, shadow;
    float3 lamps;
    Light(pixel, light, occlusion, lamps, shadow);
    // Guerrilla's cliff frame: T = (−|N.z|, 0, |N.x|), B = (N.y|N.x|, N.x² + N.z², N.y|N.z|) (not unit, not
    // always at right angles to N); the sun in it. Its cliffs have no shine (no Layer1_spec_* in its data).
    const float3 T = float3(-abs(N.z), 0, abs(N.x));
    const float3 B = float3(N.y * abs(N.x), N.x * N.x + N.z * N.z, N.y * abs(N.z));
    const float3 L = normalize(float3(dot(SunToward.xyz, T), dot(SunToward.xyz, B), dot(SunToward.xyz, N)));
    const float nl = dot(n, L), nlLit = saturate(nl);
    float3 rgb = Ambient(occlusion * (nl * 0.5 + 0.5)) * colour + colour * occlusion * nlLit * SunColour.rgb * shadow;
    rgb += lamps * colour;
    return float4(rgb * (1 - fog) + fog * FogColour.rgb, alpha) * TintColour;
#endif
}
