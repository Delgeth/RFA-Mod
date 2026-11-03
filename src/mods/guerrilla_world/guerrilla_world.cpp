// What the world ported from Red Faction: Guerrilla needs that Armageddon doesn't do itself. So
// far: Guerrilla's sky and clouds, a running clock, and the light of the sky and the sun.
//
// Guerrilla's sky is a dome coloured by the time of day: nine colour curves in each time-of-day
// set (.dtodx), four bands from the horizon up on the west side (west0-west3), four on the east
// (east0-east3) and the zenith. Its shader (rfg-skybox_s, which Armageddon still ships in
// misc.vpp_pc) picks the band from how high a point of the dome is (the mean of its angle above the
// horizon and its height, 0 at the horizon to 4 straight up), blends the two bands either side,
// and blends the west colour with the east one by how far round from west to east the point is.
//
// Its clouds (rfg-skybox-clouds_s, in Guerrilla's misc.vpp_pc) are a model round the eye, an
// overhead dome and a ring along the horizon (skybox_clouds, which both games ship), each with two
// textures holding two cloud layers each: green and alpha the layers' cover, red and blue their
// light. Each layer's cover is scaled by its strength from the time of day (overhead_layer0-3 or
// horizon_layer0-3_strength) and faded in with height; the layers are laid one over another; the
// clouds are lit by blending a front and a rear light colour (clouds_front/rear_light_color, or the
// clouds_horizon_ ones) by which way they face the time of day's light, with a rim of light where
// they are thin and between the eye and the sun (cloud_backlight_strength, _power); layers 0-1 and
// 2-3 drift sideways at their own speeds (cloud_layer01_speed, cloud_layer23_speed). In front of
// them a ring of far mountains, the backdrop (rfg-skybox-matte_s, vfx_sky_backdrop_01): its picture
// mixed with a haze colour (horizon_mountain_fog_color, _density), lit by blending the mountains'
// colour on the side facing the light and on the far side (horizon_mountain_color, _back).
//
// Armageddon reads all those curves into every time-of-day set but never draws a dome for a world:
// its scene renderer's sky step (rl_scene_renderer::render_skybox, 0xD528E0) only draws the
// unlockables viewer's background. This mod wraps that step: after the game's own, in the worlds
// named by [GuerrillaWorld] Worlds= (the ported world is "rfg"), it draws Guerrilla's sky behind
// everything, as one triangle over the whole screen at the far plane, each pixel coloured the way
// Guerrilla's shader colours its dome (kSkyShader), then the clouds over it the way Guerrilla's
// cloud shader draws them (kCloudShader), from the time of day's colours this frame times
// [GuerrillaWorld] Brightness= (read again whenever rfa_mods.ini is saved). Where the world has
// drawn something nearer, the depth test keeps it. The cloud model and textures are files in
// mods\guerrilla_world\ (clouds.bin and the textures as .dds), made from the player's own copy of
// Guerrilla; without them the sky is drawn alone.
//
// The mod draws with the game's own Direct3D 11 device, which it notes when the game makes it
// (the game imports D3D11CreateDevice), and puts back everything it changes on the device context.
//
// The clock: Armageddon still has Guerrilla's game clock, and its time of day still follows it
// every frame, but the game sets the clock's speed to 0 at start-up, so the day never moves. In our
// worlds the mod runs the clock at [GuerrillaWorld] ClockSpeed= (game seconds a real second), and
// puts the game's own speed back when a world that isn't ours is played.
//
// The view distance: the game streams a world's zones (each with its far-away ground) only within a
// radius of the player, small in the campaign (its levels are walled canyons). In our worlds, whose
// zones stream in a layer of their own, the mod widens it to [GuerrillaWorld] StreamRadius= metres
// (500 by default: the 3 × 3 of our 511 m zones around the player, as Guerrilla's own 510 m; at
// most just under two zones, 5 × 5), and puts the game's own back elsewhere.
//
// The zone size: our worlds' zones are [GuerrillaWorld] ZoneSize= metres wide (511, Guerrilla's),
// so 16 × 16 of them is Guerrilla's whole map; the mod widens the game's physics world at start-up
// to hold it (for every world: the game makes it once).
//
// The light: Armageddon lights surfaces only with lamps (its levels place lamps for the sun and the
// sky); the time of day's ambient light doesn't reach them. In our worlds the mod adds light itself,
// into the light buffer the lamps light (after the game's light pass): the sky's light, the time of
// day's ambient light from above and from below blended by which way a surface faces, times
// [GuerrillaWorld] Ambient=; and the light of the sun (or by night, the moon) along its path over
// the day, tod_light_color times Sun= (both read again whenever rfa_mods.ini is saved), with its
// shine on the surfaces that have specular maps, as Armageddon's lamps give theirs. The clouds
// are lit from the same direction. That light, the sky, the clouds and the backdrop are multiplied
// by Guerrilla's colour correction (color_correct_tint, ColourCorrection=), which Guerrilla applies
// to its whole picture and Armageddon to none of it.
//
// The fog: Armageddon's is each environment box's, by distance only, and it keeps the time of day's
// fog curves without drawing with them. Guerrilla's is height fog, thicker low down and far away,
// from its districts' curves. In our worlds the mod draws Guerrilla's over what the world has drawn,
// before the sky, from the depth the light pass reads (Fog=, thickness; the world builder turns the
// boxes' own fog off there), and over the sky, the clouds and the backdrop as Guerrilla's sky shaders
// fog them, with the clouds' ambient light as Guerrilla adds it.
//
// The tone mapping: Armageddon turns its picture into the final one with an exposure the environment
// box sets (3 in our zones), a curve that clips its highlights and a colour table. In our worlds the
// mod holds that exposure at 1 and draws Guerrilla's tone mapping over the result instead: its eye
// adapting to the picture's brightness, its exposure from the time of day's curves, and its curve that
// rolls the highlights off (ToneMapping=, Exposure=).
//
// The sun: Armageddon draws none in our worlds. The mod draws Guerrilla's: the sun orbital of the
// world's global time of day (its texture, size, colour and opacity over the day) as a small bright
// disc behind the clouds, along the sun's path; and with the tone mapping, Guerrilla's sun shafts, the
// glow streaming from the sky round the sun past whatever stands in front of it (SunShafts=).
//
// And it leaves out the game's "invalid mission info hash" report for our worlds' zones, which
// carry no mission (one line a zone at every load).
//
// The precache: a world keeps the files several of its containers use once, loose, in a precache
// archive, and each streaming cell's precache group in the asset list names those it needs. Which
// precache a level mounts comes from the area of its start zone's grid place, from a table filled
// once at start-up from precache.xtbl (bio, ice, magma, natural, surface; elsewhere "world"). That
// table knows only grid places, so our worlds would get the campaign's empty world_precache: for
// them the mod answers the area as the world's own name, so a world rfg mounts rfg_precache.vpp_pc.
//
// The gameplay properties: a destructible object names an entry of gameplay_properties.xtbl (its
// flags, such as shadows, a map icon or smoke when destroyed, and its importance, team and salvage).
// Armageddon's table lacks many of Guerrilla's entries and has the rest with its own settings, so the
// world builder writes Armageddon's table with Guerrilla's settings added
// (mods\guerrilla_world\gameplay_properties.xtbl). The game reads that table once, at start-up, from
// its copy of table.vpp_pc in memory, where the loader's overrides don't reach; the mod hands it the
// builder's table there, and gives the table's names more room than the game's 24 KB, which
// Armageddon's own nearly fill. Without that file the game keeps its own table. The draw distances
// (lod_properties.xtbl: the LOD class an object names, Guerrilla's switch-off distances added) are
// handed over the same way, and so are the effects: level_objects.xtbl (what a destructible object
// shows when it breaks or is hit), effects.xtbl and vfx.xtbl (the visual effects and their files),
// each Armageddon's with Guerrilla's entries the world uses added under "rfg_" names.
#define RFA_MOD_NAME "guerrilla_world"
#include "rfa_mod.h"
#include "rfa_game.h"
#include <d3d11.h>
#include <d3dcompiler.h>
#include <math.h>
#include <stdarg.h>
#include <algorithm>
#include <vector>

namespace {

// ---- The game

// The world being played: its name ("world" for the campaign, "rfg" for the ported world) is
// kept in the world object (0x1870630) at +0x29EA, as the time of day reads it to name its files.
const char* WorldName() { return reinterpret_cast<const char*>(0x1870630 + 0x29EA); }

// The camera the frame is drawn with: the camera manager (0x11B9C40) +0x50, its rows right, up and
// forward; +0xC0, its vertical field of view in degrees (as the console and FOV mods read them).
auto& g_cameraRotation = *reinterpret_cast<float(*)[9]>(0x11B9C90);
auto& g_cameraFov = *reinterpret_cast<float*>(0x11B9D00);
// Where it is (+0x2C).
auto& g_cameraPosition = *reinterpret_cast<float(*)[3]>(0x11B9C6C);

// The time of day's light direction this frame: the time of day's update (0x52D6E0) hands the
// renderer a block of particle lighting each frame (0x1973924, by 0x7686E0): an ambient colour, a
// light colour, then this direction.
auto& g_lightDirection = *reinterpret_cast<float(*)[3]>(0x1973944);

// The time of day: up to four loaded sets (d, c, b, a) with their weights, and the time the curves
// are read at, in hours (as in the console mod). A colour curve's value is red, green, blue and a
// brightness; the game's own functions work a curve out (methods of the curve, in ECX).
auto& g_timeOfDay = *reinterpret_cast<float*>(0x10F8898);
auto& g_todSets = *reinterpret_cast<BYTE* (*)[4]>(0x12FD6B4);
auto& g_todWeights = *reinterpret_cast<float(*)[4]>(0x12FD6C4);
using ColourAtFn = float*(__fastcall*)(void* curve, void* unused, float* out, float time);
using NumberAtFn = float(__fastcall*)(void* curve, void* unused, float time);
auto const ColourAt = reinterpret_cast<ColourAtFn>(0x531B90);
auto const NumberAt = reinterpret_cast<NumberAtFn>(0x531CB0);

// The game clock (0x12F76D0: date, hour, minute, second, then this): its speed, in game seconds a
// real second (Game_clock.m_time_scale, the console's game_clock_scale_factor). Set to 0 only at
// start-up (0x51F910); setting the time (0x520410, set_time_of_day) keeps it.
auto& g_gameClockSpeed = *reinterpret_cast<float*>(0x12F76D8);

// Streaming: the stream manager (its pointer at 0x1872BFC) keeps each layer's containers loaded
// within a box of a radius around the player (stream_follows_player). The layers are two grids
// over the world (set up at 0x6B56F0): layer 0 the sub-zones, 48 × 48 cells a third of a zone wide
// (radius 170 m), layer 1 the zones, 16 × 16 (radius 255 m): the console's stream_radius and
// stream_radius_terrain. 0x6B3100 sets a layer's radius (the box; the next update streams to it),
// 0x6B3180 gives it. A layer can track only about 35 containers (its update's lists hold 35; past
// that it drops cells and the game crashes), so the zones' radius stays at most two zones (5 × 5);
// the campaign's 255 m keeps 3 × 3, so its ground stops two zones away.
auto& g_streamManager = *reinterpret_cast<void**>(0x1872BFC);
using SetStreamRadiusFn = void(__fastcall*)(void* manager, void* unused, int layer, float radius);
using StreamRadiusFn = float(__fastcall*)(void* manager, void* unused, int layer);
auto const SetStreamRadius = reinterpret_cast<SetStreamRadiusFn>(0x6B3100);
auto const StreamRadiusOf = reinterpret_cast<StreamRadiusFn>(0x6B3180);
// The setter, wrapped: in our worlds no radius may reach a box that could meet more cells than a
// layer tracks (the game sets them itself, from the zone size, at times a mode changes).
constexpr DWORD kSetStreamRadius = 0x6B3100;
constexpr size_t kSetStreamRadiusPrologue = 6;  // push ebp / mov ebp,esp / sub esp,20h
SetStreamRadiusFn g_setStreamRadius;

// The zone size: 0x6C0D60(name, &size) gives a world's zone width from its name (a world's or a
// zone's): 511 m, Guerrilla's, for names starting "mpl_" (left over from Guerrilla's multiplayer
// maps), 255 m for any other. The streaming grids' cells, the zones' places and the terrain's grid
// step all come from it, while the grid stays 16 × 16 zones: with Guerrilla's size it is
// Guerrilla's own 8 km map. Our worlds get [GuerrillaWorld] ZoneSize=.
constexpr DWORD kZoneSize = 0x6C0D60;
constexpr size_t kZoneSizePrologue = 6;  // push ebp / mov ebp,esp / sub esp,40h
using ZoneSizeFn = void(__cdecl*)(const char* name, float* size);
ZoneSizeFn g_zoneSize;

// The physics world: Havok's broadphase, made once at start-up (0x772C30, "havok_system_init"), is
// a box 2295 m out from the middle across the ground (nine 255 m zones: the 16 × 16 grid and a zone
// to spare) and 1000 m up and down; the game's out-of-the-world bounds (0x1975E2C, 0x1975E38) are
// the same box. A body outside it is dropped: a zone's ground ("Tried to delete a zone (%s) which
// has dimensions outside the broadphase", and the player falls through) or a character ("fell out
// of the world"). The 2295 m comes from two constants only that code reads (0xFB5DC0, and
// 0xFB5DC4 for -2295), so with bigger zones the mod sets them, before the game starts, to nine of
// our zones. It is one box for every world, the campaign's too.
constexpr DWORD kWorldReach = 0xFB5DC0, kWorldReachBack = 0xFB5DC4;
constexpr float kGameWorldReach = 2295.0f;
constexpr float kWorldReachZones = 9.0f;

// The time of day's ambient light: each frame the time of day's update (0x52D6E0) hands the world
// its ambient light from above (0x768520) and from below (0x768570), each a colour by pointer,
// already times its brightness (held to ldr_min..ldr_max, so at most the colour itself); the world
// keeps them at its start (0x1870630, 0x187063C), and its update gives them to the renderer's scene
// (V_ambient_render, V_back_ambient_render). Only shaders drawn straight to the screen use them:
// the ground gets no light but the light buffer's (below). The environment boxes' own ambient
// colours only reach effects.
constexpr DWORD kSetAmbient = 0x768520, kSetBackAmbient = 0x768570;
constexpr size_t kSetAmbientPrologue = 6;  // push ebp / mov ebp,esp / sub esp,0Ch
using SetAmbientFn = void(__cdecl*)(const float* colour);
SetAmbientFn g_setAmbient, g_setBackAmbient;
auto& g_worldAmbient = *reinterpret_cast<float(*)[3]>(0x1870630);
auto& g_worldBackAmbient = *reinterpret_cast<float(*)[3]>(0x187063C);

// Where a set keeps its curves (found in its reader, 0x558EE0). The sky's: west0-west3,
// east0-east3, zenith, in the order Guerrilla's shader takes them (the west bands then the zenith,
// the east bands then the zenith again).
constexpr size_t kSkyCurves[10] = {0x0, 0x340, 0x680, 0x9C0, 0x1A00, 0xD00, 0x1040, 0x1380, 0x16C0, 0x1A00};
// The clouds': for the overhead clouds [0] and the ones along the horizon [1].
constexpr size_t kCloudFront[2] = {0x4AE0, 0x5160};  // clouds_front_light_color, clouds_horizon_front_light_color
constexpr size_t kCloudRear[2] = {0x4E20, 0x54A0};   // clouds_rear_light_color, clouds_horizon_rear_light_color
constexpr size_t kCloudLayers[2][4] = {{0x5BB0, 0x5CA4, 0x5D98, 0x5E8C},   // overhead_layer0-3_strength
                                       {0x57E0, 0x58D4, 0x59C8, 0x5ABC}};  // horizon_layer0-3_strength
constexpr size_t kBacklightPower = 0x6A34, kBacklightStrength = 0x6B28;
constexpr size_t kLayer01Speed = 0x6C1C, kLayer23Speed = 0x6D10;
// The backdrop's: horizon_mountain_color (the side facing the light), _back, _fog_color, _fog_density.
constexpr size_t kMountainFront = 0x5F80, kMountainBack = 0x62C0, kMountainFog = 0x6600, kMountainFogDensity = 0x6940;

// The sun's light: tod_light_color.
constexpr size_t kSunColour = 0x1D40;

// The game's error reports (0x545D40: level, format, ...) hand their arguments on to 0x545BD0
// (level, 1, format, the arguments). Loading a zone whose header names no known mission reports
// "%s has an invalid mission info hash of 0x%08X" (this format, from 0x6B5CE0 and 0x501C30): the
// ported world's zones carry Ruin's "none" (0x01BEEBE5), 64 lines on the screen at every load; a
// real mission's name would make the level command start that mission (0x501C30 sets the current
// mission, 0x55F600). So the mod drops that one report for zones of our worlds.
constexpr DWORD kReportError = 0x545BD0;
constexpr size_t kReportErrorPrologue = 9;  // push ebp / mov ebp,esp / sub esp,12Ch
constexpr DWORD kMissionHashFormat = 0xF82734;
using ReportErrorFn = void(__cdecl*)(int level, int flag, const char* format, va_list arguments);
ReportErrorFn g_reportError;

// The game's debug print (0xCA9F10: a format and its arguments, written out with
// OutputDebugString). What it says while loading (an archive it couldn't mount, ...) reaches
// nowhere a player sees, so in our worlds the mod writes it to the log as well (the first 500).
constexpr DWORD kDebugPrint = 0xCA9F10;
constexpr size_t kDebugPrintPrologue = 6;  // push ebp / mov ebp,esp / sub esp,8
using DebugPrintFn = void(__cdecl*)(const char* format, ...);
DebugPrintFn g_debugPrint;

// The area a level's start zone is in, for its precache (0x6B4710: column, row and the grid's size
// in, the area's 64-byte name out). Its one caller, the level's streaming set-up (0x6B43ED, after
// the world's name is known), then looks for the one "<area>_precache.precache" marker among the
// world's archives and mounts build\pc\cache\<area>_precache.vpp_pc.
constexpr DWORD kPrecacheArea = 0x6B4710;
constexpr size_t kPrecacheAreaPrologue = 6;  // push ebp / mov ebp,esp / sub esp,10h
using PrecacheAreaFn = const char*(__cdecl*)(int column, int row, int gridSize);
PrecacheAreaFn g_precacheArea;

// While the game starts, it reads tables from its copy of table.vpp_pc in memory: the table reader
// (0xC8E760) asks the copy for a table by its file name (0xC8FCD0, a method of the copy: true and the
// table's bytes, or false, and the reader opens the file by name instead).
constexpr DWORD kTableCacheFind = 0xC8FCD0;
constexpr size_t kTableCacheFindPrologue = 6;  // push ebp / mov ebp,esp / sub esp,8
struct CachedTable {
    const void* data;
    DWORD size;
};
using TableCacheFindFn = bool(__fastcall*)(void* cache, void* unused, const char* name, CachedTable* out);
TableCacheFindFn g_tableCacheFind;

// A pool of names (a method of the pool: the buffer, its size, the pool's name). The gameplay
// properties' is "Gameplay Property Names", 0x6000 bytes, set up just before the table is read
// (0x7A6E50); a name takes its length and 2 or 3 bytes more, and a full pool stops the game.
constexpr DWORD kNamePoolInit = 0xC97AF0;
constexpr size_t kNamePoolInitPrologue = 6;  // push ebp / mov ebp,esp / sub esp,8
using NamePoolInitFn = void(__fastcall*)(void* pool, void* unused, void* buffer, DWORD size, const char* name);
NamePoolInitFn g_namePoolInit;

// The tables the world builder writes for the mod to hand the game: the gameplay properties, the draw
// distances (LOD classes), and the effects (visual effects, effects, level objects), each read at
// start-up (the visual effects before the effects, which name them, and those before the level
// objects, which name the effects).
struct BuiltTable {
    const char* name;         // its file name, in mods\guerrilla_world\ and as the game asks for it
    const char* what;         // for the log
    std::vector<BYTE> bytes;  // the builder's table, with a 0 after it; empty if there is none
};
BuiltTable g_builtTables[] = {
    {"gameplay_properties.xtbl", "Gameplay properties", {}},
    {"lod_properties.xtbl", "Draw distances", {}},
    {"vfx.xtbl", "Visual effects", {}},
    {"effects.xtbl", "Effects", {}},
    {"level_objects.xtbl", "Level objects", {}},
};
BuiltTable& g_gameplayTable = g_builtTables[0];
constexpr char kGameplayNames[] = "Gameplay Property Names";
constexpr DWORD kGameplayNameRoom = 0x10000;  // the world builder's GuerrillaGameplay.NamePool
std::vector<char> g_gameplayNames;

// The scene renderer's sky step: a method (the renderer in ECX).
constexpr DWORD kRenderSkybox = 0xD528E0;
constexpr size_t kRenderSkyboxPrologue = 6;  // push ebp / mov ebp,esp / sub esp,3Ch
using RenderSkyboxFn = void(__fastcall*)(void* renderer, void* unused);
RenderSkyboxFn g_renderSkybox;

// Armageddon lights with "inferred lighting": surfaces first draw their normals (the G-buffer),
// then every lamp adds its light into a light buffer, then each surface draws again as its colour
// times the light buffer (the terrain's ir_terrain_height_mesh_t). Nothing else lights it: the
// global ambient pass (ir_light_ambient) is set up but never drawn. The light pass is the scene
// renderer's method 12 (rl_pc_scene_renderer, 0xEF8BB0, "IR Light Pass"): it binds the light buffer
// as the render target and the G-buffer as textures (slot 6 = t13 the normals, 7 = t14 the depth,
// 8 = t9; table 0x111A348), clears it, and draws each lamp (0xEF67D0).
constexpr DWORD kLightPass = 0xEF8BB0;
constexpr size_t kLightPassPrologue = 5;  // push ebp / mov ebp,esp / push -1
using LightPassFn = void(__fastcall*)(void* renderer, void* unused);
LightPassFn g_lightPass;

// The exposure: Armageddon's tone mapping (rl_hdr) adapts its exposure toward Desired_brightness ÷
// the picture's brightness, held between Exposure_min and _max; its tone-mapping step (0xD22C60, the
// renderer in ECX, wrapped by the console mod) uploads them from the renderer's +0xAD8, +0xAE0 and
// +0xAE4, which the environment boxes set (ruin01's holds it at 3). The step's one caller (0xC45130,
// the compositor's tone-mapping constants, the same renderer in ECX) is wrapped here instead.
constexpr DWORD kToneMappingConstants = 0xC45130;
constexpr size_t kToneMappingConstantsPrologue = 6;  // push ebp / mov ebp,esp / sub esp,3Ch
using ToneMappingConstantsFn = void(__fastcall*)(void* renderer, void* unused);
ToneMappingConstantsFn g_toneMappingConstants;

// The compositor's last step, one of two (0xC46C10, 0xC471C0; the frame runs one, a method with one
// flag): the picture tone-mapped into its final target (8-bit; from the scene, 16-bit float, at t0, the
// bloom at t5, and with 0xC46C10 its colour table at t6). After it, in our worlds, the mod draws
// Guerrilla's tone mapping over its picture.
constexpr DWORD kFinalComposite[2] = {0xC46C10, 0xC471C0};
constexpr size_t kFinalCompositePrologue = 5;  // push ebp / mov ebp,esp / push -1
using FinalCompositeFn = void(__fastcall*)(void* compositor, void* unused, int flag);
FinalCompositeFn g_finalComposite[2];

// A small check the light pass makes once all that is bound: in each lamp's drawing and after the
// last lamp (0xD574A0: is a lighting debug view on). Where the mod notes the light buffer and normals.
constexpr DWORD kLightCheck = 0xD574A0;
constexpr size_t kLightCheckPrologue = 11;  // push ebp / mov ebp,esp / push ecx / cmp dword ptr [0x306B570],0Ch
using LightCheckFn = bool(__cdecl*)();
LightCheckFn g_lightCheck;

// A colour curve now: every loaded set's value at the time of day times the set's weight, added up
// (as the game does), the colour times its own brightness.
void TodColour(size_t offset, float (&out)[3]) {
    out[0] = out[1] = out[2] = 0;
    for (int set = 0; set < 4; ++set) {
        if (!g_todSets[set] || g_todWeights[set] == 0) continue;
        float value[4] = {0, 0, 0, 0};
        ColourAt(g_todSets[set] + offset, nullptr, value, g_timeOfDay);
        for (int i = 0; i < 3; ++i) out[i] += value[i] * value[3] * g_todWeights[set];
    }
}

// A number curve now, the same way.
float TodNumber(size_t offset) {
    float total = 0;
    for (int set = 0; set < 4; ++set)
        if (g_todSets[set] && g_todWeights[set] != 0) total += NumberAt(g_todSets[set] + offset, nullptr, g_timeOfDay) * g_todWeights[set];
    return total;
}

// ---- Settings

bool g_skyOn = true;
bool g_cloudsOn = true;
std::vector<std::string> g_worlds;

// How bright the sky and clouds are: their time-of-day colours (Guerrilla's own, which Guerrilla's
// renderer shows brighter than Armageddon's does) times this. Read again whenever rfa_mods.ini is
// saved, so it can be tuned while playing.
constexpr float kDefaultBrightness = 5.0f;
float g_brightness = kDefaultBrightness;
FILETIME g_iniWritten = {};
ULONGLONG g_lastIniCheck = 0;
bool g_tellColours = true;  // log the colours at the next draw

void ReadBrightness() {
    const float brightness = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"Brightness", L"5").c_str()));
    g_brightness = brightness > 0 ? brightness : kDefaultBrightness;
}

// How fast the game clock runs in our worlds, in game seconds a real second (48: a day in half an
// hour); 0 leaves the clock alone. Also read again whenever rfa_mods.ini is saved.
constexpr float kDefaultClockSpeed = 48.0f;
float g_clockSpeed = kDefaultClockSpeed;
bool g_clockOurs = false;      // the clock runs at our speed
float g_clockSpeedBefore = 0;  // the game's own speed, put back on leaving

void ReadClockSpeed() {
    const float speed = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"ClockSpeed", L"48").c_str()));
    g_clockSpeed = speed >= 0 ? speed : kDefaultClockSpeed;
}

// How wide our worlds' zones are, in metres: the ported world's are Guerrilla's, 511; 0 leaves the
// game's own (255). Read once: a world's files are built for one or the other.
float g_zoneWidth = 511.0f;

void ReadZoneSize() {
    const float width = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"ZoneSize", L"511").c_str()));
    g_zoneWidth = width >= 0 ? width : 511.0f;
}

float OurZoneWidth() { return g_zoneWidth > 0 ? g_zoneWidth : 255.0f; }

// How far around the player our worlds' zones stay loaded, in metres (the zones' layer, 1); 0
// leaves the game's own. Also read again whenever rfa_mods.ini is saved.
constexpr int kSubzoneLayer = 0, kZoneLayer = 1;
constexpr float kDefaultStreamRadius = 500.0f;
float g_streamRadius = kDefaultStreamRadius;
bool g_streamOurs = false;        // the zones' layer streams at our radius
float g_streamRadiusBefore = 0;   // the game's own radius, put back on leaving
void* g_streamLayers = nullptr;   // the layers we set it in (a level load makes new ones)

void ReadStreamRadius() {
    const float radius = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"StreamRadius", L"500").c_str()));
    g_streamRadius = radius >= 0 ? radius : kDefaultStreamRadius;
}

// The most a layer's radius may be in our worlds: under two of its cells (sub-zones a third of a
// zone, zones a zone), so its box meets at most 5 × 5 cells, 25 of the 35 a layer tracks (a box
// exactly four cells wide can meet six a side; Guerrilla's size makes the sub-zones' own radius,
// 511 × 0.6667, just that).
float MostRadius(int layer) {
    const float cell = layer == kSubzoneLayer ? OurZoneWidth() / 3 : OurZoneWidth();
    return 2 * cell - 1;
}

// How bright the sky's light is in our worlds: the time of day's ambient light times this. Also
// read again whenever rfa_mods.ini is saved.
float g_ambient = 1.0f;
bool g_tellAmbient = true;  // log the ambient light at the next frame in our world

void ReadAmbient() {
    const float ambient = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"Ambient", L"1").c_str()));
    g_ambient = ambient >= 0 ? ambient : 1.0f;
}

// Whether the mod lights our worlds itself (the sky's light and the sun, in the light buffer), and
// how bright the sun is: tod_light_color times this (read again whenever rfa_mods.ini is saved).
bool g_lightOn = true;
float g_sun = 1.0f;

void ReadSun() {
    const float sun = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"Sun", L"1").c_str()));
    g_sun = sun >= 0 ? sun : 1.0f;
}

// Guerrilla's colour correction, the time of day's color_correct_tint: Guerrilla multiplies its whole
// picture by it, divided by its brightest channel (TOD_tint_primary_color), and ignores its fourth
// number. Its light is pink and the tint cyan, so only the two together give its colours. Armageddon
// doesn't colour its picture by the time of day's tint (its own sets all carry the same grey), so in
// our worlds the mod multiplies what it draws by it: the light it adds, the sky, the clouds and the
// backdrop. ColourCorrection=0 leaves it out (read again whenever rfa_mods.ini is saved).
constexpr size_t kColourCorrection = 0x47A0;
bool g_colourCorrection = true;

void ReadColourCorrection() {
    g_colourCorrection = _wtoi(rfa::IniString(L"GuerrillaWorld", L"ColourCorrection", L"1").c_str()) != 0;
}

// The colour correction now: every loaded set's tint at the time of day times the set's weight,
// added up, divided by its brightest channel; white when it is off.
void ColourCorrection(float (&out)[3]) {
    out[0] = out[1] = out[2] = 1;
    if (!g_colourCorrection) return;
    float tint[3] = {0, 0, 0};
    for (int set = 0; set < 4; ++set) {
        if (!g_todSets[set] || g_todWeights[set] == 0) continue;
        float value[4] = {0, 0, 0, 0};
        ColourAt(g_todSets[set] + kColourCorrection, nullptr, value, g_timeOfDay);
        for (int i = 0; i < 3; ++i) tint[i] += value[i] * g_todWeights[set];
    }
    float brightest = tint[0];
    if (tint[1] > brightest) brightest = tint[1];
    if (tint[2] > brightest) brightest = tint[2];
    if (brightest <= 0) return;
    for (int i = 0; i < 3; ++i) out[i] = tint[i] / brightest;
}

// Guerrilla's fog, from the time of day's curves (in our sets as Guerrilla's): its colour, its
// density, the density's fall-off with height (atmosphere scale), the fog's ground, an offset into
// the distance, and the range that ground is held to around the camera. How thick it is: the density
// times Fog= (0: none; read again whenever rfa_mods.ini is saved).
constexpr size_t kFogColour = 0x2700, kFogGround = 0x30C0, kFogAtmosphereScale = 0x31B4, kFogDensity = 0x32A8,
                 kFogDensityOffset = 0x339C, kFogCameraMin = 0x6E04, kFogCameraMax = 0x6EF8;
float g_fog = 1.0f;
bool g_tellFog = true;  // log the fog at its next draw

// Guerrilla fogs its sky too: the dome, the clouds and the backdrop, each as if it were a surface
// stretched across to Guerrilla's far clip (1,000 m at full detail, game_render_set_lod_level) and up
// to the time of day's fog scale, raised by its fog offset (fog_skybox_, fog_clouds_ and fog_matte_
// offset and scale; Guerrilla's defaults 100 and 100, the backdrop's offset 10). Guerrilla's world
// has its fog follow the camera (fog_camera_follow), which takes the camera's height off the offsets.
constexpr size_t kSkyFogOffset = 0x6FEC, kSkyFogScale = 0x70E0, kCloudsFogOffset = 0x71D4, kCloudsFogScale = 0x72C8,
                 kMatteFogOffset = 0x73BC, kMatteFogScale = 0x74B0;
constexpr float kSkyFogFar = 1000;

void ReadFog() {
    const float fog = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"Fog", L"1").c_str()));
    g_fog = fog >= 0 ? fog : 1.0f;
}

// Guerrilla's tone mapping in our worlds (ToneMapping=1): Armageddon's exposure is held at 1, so the
// picture holds Guerrilla's own values, and the mod turns it into the final picture as Guerrilla does
// (see DrawToneMapping), from the time of day's curves (in our sets as Guerrilla's). Exposure=
// multiplies Guerrilla's exposure (1 = Guerrilla's). ToneMapping=0 leaves Armageddon's own (the
// environment box's exposure, its curve and colour table). Both read again whenever rfa_mods.ini is saved.
constexpr size_t kLumRange = 0x44C4, kLumOffset = 0x45B8, kEyeAdaptionBase = 0x3B3C, kEyeAdaptionAmount = 0x3C30, kIrisRate = 0x376C,
                 kLuminanceMin = 0x3954, kLuminanceMax = 0x3860, kBloomAmount = 0x40F4;
bool g_toneMapping = true;
bool g_toneMappingFailed = false;
float g_exposure = 1;

void ReadToneMapping() {
    g_toneMapping = rfa::IniInt(L"GuerrillaWorld", L"ToneMapping", 1) != 0;
    const float exposure = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"Exposure", L"1").c_str()));
    g_exposure = exposure > 0 ? exposure : 1;
}

bool GuerrillaToneMapping() { return g_toneMapping && !g_toneMappingFailed; }

// Guerrilla's particle layer with its tone mapping (Particles=; read again whenever rfa_mods.ini is
// saved): see ParticlePhase. 0 = particles drawn into the scene as Armageddon draws them.
bool g_particles = true;

void ReadParticles() { g_particles = rfa::IniInt(L"GuerrillaWorld", L"Particles", 1) != 0; }

// Guerrilla's dust round the camera (Dust=; read again whenever rfa_mods.ini is saved): see DustUpdate.
// 0: none.
bool g_dust = true;

void ReadDust() { g_dust = rfa::IniInt(L"GuerrillaWorld", L"Dust", 1) != 0; }

// Guerrilla's sun shafts, drawn with its tone mapping: how strong, times Guerrilla's own (SunShafts=;
// 0: none; read again whenever rfa_mods.ini is saved).
float g_sunShafts = 1.0f;
bool g_shaftsFailed = false;

void ReadSunShafts() {
    const float shafts = static_cast<float>(_wtof(rfa::IniString(L"GuerrillaWorld", L"SunShafts", L"1").c_str()));
    g_sunShafts = shafts >= 0 ? shafts : 1.0f;
}

bool SunShaftsOn() { return GuerrillaToneMapping() && g_sunShafts > 0 && !g_shaftsFailed; }

// The sun's shadows (SunShadows=; read again whenever rfa_mods.ini is saved): the sun's light drawn
// through the shadow map of the sun's lamp (see "The sun's shadows"). 0: it lights through everything.
bool g_sunShadows = true;

void ReadSunShadows() { g_sunShadows = rfa::IniInt(L"GuerrillaWorld", L"SunShadows", 1) != 0; }

// How bright the sky, clouds and backdrop are drawn: their time-of-day colours times Brightness=; with
// Guerrilla's tone mapping the picture holds Guerrilla's own values, so there the default 5 is
// Guerrilla's own level (× 1).
float SkyBrightness() { return GuerrillaToneMapping() ? g_brightness / kDefaultBrightness : g_brightness; }

// The fog's numbers now, as Guerrilla works them out each frame (game_render_setup_frame): x the
// density's fall-off with height k (the atmosphere scale, never quite 0); y the density at the
// camera's height, in powers of two (× 1/ln 2), e^(−k × the camera's height over the fog's ground)
// times the density, held to 0–1000; w the offset. Guerrilla's world (its terr01.gtodx,
// fog_camera_follow) keeps the fog's ground at the camera's height plus fog_ground, held between
// fog_camera_min and _max, so the camera's own height drops out.
void FogNumbers(float (&out)[4]) {
    const float k = TodNumber(kFogAtmosphereScale) + 0.00001f;
    float ground = TodNumber(kFogGround);
    const float lowest = TodNumber(kFogCameraMin), highest = TodNumber(kFogCameraMax);
    if (ground > highest) ground = highest;
    if (ground < lowest) ground = lowest;
    float density = TodNumber(kFogDensity) * g_fog * 1.442695f * expf(k * ground);
    if (density < 0) density = 0;
    if (density > 1000) density = 1000;
    out[0] = k;
    out[1] = density;
    out[2] = 0;
    out[3] = TodNumber(kFogDensityOffset);
}

// Once a second: if rfa_mods.ini was saved since, read the brightness, ambient light, sun, colour
// correction, fog, exposure, sun shafts and clock speed again.
void CheckSettings() {
    const ULONGLONG now = GetTickCount64();
    if (now - g_lastIniCheck < 1000) return;
    g_lastIniCheck = now;
    WIN32_FILE_ATTRIBUTE_DATA ini;
    if (!GetFileAttributesExW(rfa::IniPath().c_str(), GetFileExInfoStandard, &ini)) return;
    if (CompareFileTime(&ini.ftLastWriteTime, &g_iniWritten) == 0) return;
    const bool first = g_iniWritten.dwLowDateTime == 0 && g_iniWritten.dwHighDateTime == 0;
    g_iniWritten = ini.ftLastWriteTime;
    if (first) return;
    const float before = g_brightness;
    ReadBrightness();
    if (g_brightness != before) {
        rfa::Log("Sky: brightness %.3g (was %.3g)", g_brightness, before);
        g_tellColours = true;
    }
    const float ambientBefore = g_ambient;
    ReadAmbient();
    if (g_ambient != ambientBefore) {
        rfa::Log("Ground: ambient light times %.3g (was %.3g)", g_ambient, ambientBefore);
        g_tellAmbient = true;
    }
    const float sunBefore = g_sun;
    ReadSun();
    if (g_sun != sunBefore) {
        rfa::Log("Ground: sunlight times %.3g (was %.3g)", g_sun, sunBefore);
        g_tellAmbient = true;
    }
    const bool correctionBefore = g_colourCorrection;
    ReadColourCorrection();
    if (g_colourCorrection != correctionBefore) {
        rfa::Log("Colour correction (Guerrilla's tint) %s", g_colourCorrection ? "on" : "off");
        g_tellAmbient = g_tellColours = g_tellFog = true;
    }
    const float fogBefore = g_fog;
    ReadFog();
    if (g_fog != fogBefore) {
        rfa::Log("Fog: thickness times %.3g (was %.3g)", g_fog, fogBefore);
        g_tellFog = true;
    }
    const float exposureBefore = g_exposure;
    const bool toneMappingBefore = g_toneMapping;
    ReadToneMapping();
    if (g_toneMapping != toneMappingBefore) {
        rfa::Log("Tone mapping: %s", g_toneMapping ? "Guerrilla's" : "Armageddon's own");
        g_tellColours = true;
    }
    if (g_exposure != exposureBefore) rfa::Log("Tone mapping: exposure times %.3g (was %.3g)", g_exposure, exposureBefore);
    const float shaftsBefore = g_sunShafts;
    ReadSunShafts();
    if (g_sunShafts != shaftsBefore) rfa::Log("Sun shafts: times %.3g (was %.3g)", g_sunShafts, shaftsBefore);
    const bool shadowsBefore = g_sunShadows;
    ReadSunShadows();
    if (g_sunShadows != shadowsBefore) rfa::Log("Sun's shadows: %s", g_sunShadows ? "on" : "off");
    const bool particlesBefore = g_particles;
    ReadParticles();
    if (g_particles != particlesBefore) rfa::Log("Particles: %s", g_particles ? "in a layer of their own, as Guerrilla's" : "drawn into the scene, as Armageddon's");
    const bool dustBefore = g_dust;
    ReadDust();
    if (g_dust != dustBefore) rfa::Log("Dust: %s", g_dust ? "on" : "off");
    const float clockBefore = g_clockSpeed;
    ReadClockSpeed();
    if (g_clockSpeed != clockBefore && g_clockOurs) {
        g_gameClockSpeed = g_clockSpeed > 0 ? g_clockSpeed : g_clockSpeedBefore;
        g_clockOurs = g_clockSpeed > 0;
        rfa::Log("Clock: speed %.3g (was %.3g)", g_gameClockSpeed, clockBefore);
    }
    const float radiusBefore = g_streamRadius;
    ReadStreamRadius();
    if (g_streamRadius != radiusBefore) rfa::Log("Streaming: radius %.0f m (was %.0f; 0 = the game's own)", g_streamRadius, radiusBefore);
}

bool InOurWorld() {
    const char* world = WorldName();
    for (const std::string& name : g_worlds)
        if (_stricmp(world, name.c_str()) == 0) return true;
    return false;
}

// The sun's path, as Guerrilla's time of day works it out (tod_shared_calculate_orbital_position with
// the sun orbital of its terr01.gtodx: a circle turned 118° about x and 180° about y, 4.5 hours on,
// which comes to this): it rises in the east (+x, where the sky's morning colours are) at 7:30,
// crosses at 13:30 at 62° up, leaning south (-z), and sets in the west at 19:30. Toward the sun, in
// the world.
void SunToward(float (&out)[3]) {
    const float pi = 3.14159265f;
    const float turn = 2 * pi * (g_timeOfDay - 13.5f) / 24.0f;  // 0 when highest, ±90° at rising and setting
    const float lean = 28.0f * pi / 180.0f;                     // the orbit plane's tilt from straight up
    out[0] = -sinf(turn);
    out[1] = cosf(turn) * cosf(lean);
    out[2] = -cosf(turn) * sinf(lean);
}

bool SunUp() {
    float toward[3];
    SunToward(toward);
    return toward[1] >= 0;
}

// The light that shines from one direction: tod_light_color (Guerrilla's: the sun's by day, blue
// moonlight by night) times Sun=, from the sun while it is up and from the opposite side of the sky
// (the moon, roughly) while it is down, fading out as either nears the horizon. Toward the light,
// and the light.
void DirectLight(float (&toward)[3], float (&out)[3]) {
    SunToward(toward);
    if (toward[1] < 0)
        for (float& v : toward) v = -v;
    TodColour(kSunColour, out);
    float up = (toward[1] - 0.01f) / 0.1f;  // 0 at the horizon, 1 at 6° up
    up = up < 0 ? 0 : up > 1 ? 1 : up;
    for (float& c : out) c *= g_sun * up * up * (3 - 2 * up);
}

// ---- The sun's shadows
// Armageddon can't draw a light that shines from one direction over the whole map (all its light
// shaders are lamps'), but its light pass renders a lamp's shadow map, and it keeps a lamp for a sun's
// shadow: the follow player light (lights.xtbl's FollowPlayerLight: a spot lamp with an orthographic
// shadow map only, to 30 m), which an environment box's follow_player_light switches on for a level.
// Each frame the game makes it if need be and puts it over the camera (Follow_player_light_offset_*,
// 20 m up), unturned, shining straight down; the lightning lamps of its outdoor maps (use_sun_shadows)
// borrow its shadow map. In our worlds the mod switches it on (choosing its template as such a box
// would), puts it along the sun instead, aimed back at the camera, and has it drawn (not its map only)
// with next to no light of its own; when the light pass has drawn it (its shadow map just rendered and
// still bound), the mod draws the sun's light through that map (DrawLamp, below), and the sky's light
// after the pass as before.
// The follow light's template (an index into lights.xtbl's lights, -1 for none: the environment's
// choice, cleared at each level's start) and its object's handle.
auto& g_followLightTemplate = *reinterpret_cast<int*>(0x1102DE4);
auto& g_followLightHandle = *reinterpret_cast<DWORD*>(0x1101C10);
// lights.xtbl's light of a name: its index, or -1.
using FindLightTemplateFn = int(__cdecl*)(const char* name);
auto const FindLightTemplate = reinterpret_cast<FindLightTemplateFn>(0x7E82F0);
// Each frame, the player's update of its lights: the follow light put where it goes (0x7E8A80) while
// a template is chosen, else dimmed to nothing.
constexpr DWORD kFollowLights = 0x8B8C30;
constexpr size_t kFollowLightsPrologue = 6;  // push ebp / mov ebp,esp / sub esp,44h
using FollowLightsFn = void(__cdecl*)(void* player);
FollowLightsFn g_followLights;
// Makes the follow light if need be (from the template) and puts it at a place, turned so (a world
// matrix's rows right, up and forward; a spot lamp shines down its up).
struct Place { float at[3]; };
struct Turn { float rows[9]; };
constexpr DWORD kPlaceFollowLight = 0x7E8A80;
constexpr size_t kPlaceFollowLightPrologue = 6;  // push ebp / mov ebp,esp / sub esp,10h
using PlaceFollowLightFn = void(__cdecl*)(Place place, Turn turn, int lightTemplate);
PlaceFollowLightFn g_placeFollowLight;
// Finds a lamp object by its handle (the world, 0x1870630, in ECX), or null. A lamp object: its colour
// at +0x10C (its light's colour is that times its intensity, +0x128), its light (rl_light) at +0x194.
// The light's flags at +0x15C: 1 it casts shadows, 2 it only renders its shadow map (0x7E5170 sets it).
using FindLampFn = BYTE*(__fastcall*)(void* world, void* unused, DWORD handle);
auto const FindLamp = reinterpret_cast<FindLampFn>(0x61C100);
constexpr size_t kLampColour = 0x10C, kLampLight = 0x194;
using SetShadowMapOnlyFn = void(__fastcall*)(void* light, void* unused, bool only);
auto const SetShadowMapOnly = reinterpret_cast<SetShadowMapOnlyFn>(0x7E5170);
// The light's colour (+0xF0: red, green, blue and a fourth, 1).
using SetLightColourFn = void(__fastcall*)(void* light, void* unused, const float* colour);
auto const SetLightColour = reinterpret_cast<SetLightColourFn>(0x52F050);
// lights.xtbl's lights as the game keeps them (0x2A0 bytes each, from 0x1CD07FC): a lamp made from one
// copies its reach (atten_start +0x50, atten_range +0x54, metres), its cone (hotspot_size +0x5C,
// hotspot_falloff_size +0x60, degrees) and its shadow distance (+0x70), and its light's shadow setup
// is fixed then: an orthographic shadow map (4096 × 4096) as wide as the reach × tan(hotspot +
// falloff) and as deep as the reach from the lamp, its shadows fading out over the last fifth of
// the shadow distance from the camera (the template's 80 m, 50° and 30 m made a map 95 m across and
// 80 m deep). So while the game makes the follow light in our worlds the mod gives its template the
// sun's, and puts the template's own back straight after.
auto& g_lightTemplates = *reinterpret_cast<BYTE**>(0x1CD07FC);
constexpr size_t kLightTemplateSize = 0x2A0;
constexpr size_t kTemplateNumbers[5] = {0x50, 0x54, 0x5C, 0x60, 0x70};  // reach start, reach range, hotspot, falloff, shadow distance
// The sun's: the lamp 150 m along the sun with a reach of 400 m (so its map holds what stands
// between the sun and the camera and beyond), a cone of 36° (a map about 300 m across, 7 cm a
// texel), the shadows to 150 m round the camera.
constexpr float kSunLampDistance = 150.0f;
constexpr float kSunTemplateNumbers[5] = {400.0f, 10.0f, 30.0f, 6.0f, 150.0f};

BYTE* g_sunLight = nullptr;          // the follow light's light while it gives the sun its shadows
bool g_followLightOurs = false;      // its template chosen by the mod
BYTE* g_followLightSeen = nullptr;   // the follow light last logged

void __cdecl FollowLights(void* player) {
    if (!InOurWorld()) {
        g_followLightOurs = false;  // (the game clears the template at each level's start)
        g_sunLight = nullptr;
    } else if (g_sunShadows) {
        if (g_followLightTemplate < 0) {
            g_followLightTemplate = FindLightTemplate("FollowPlayerLight");
            g_followLightOurs = g_followLightTemplate >= 0;
            if (g_followLightOurs)
                RFA_LOG_FIRST(1, "Sun's shadows: the follow player light switched on in world \"%s\" (lights.xtbl's light %d)", WorldName(), g_followLightTemplate);
            else
                RFA_LOG_FIRST(1, "Sun's shadows: lights.xtbl has no FollowPlayerLight; the sun lights without shadows");
        }
    } else if (g_followLightOurs) {
        g_followLightTemplate = -1;  // dimmed to nothing by the game, as where no level asks for it
        g_followLightOurs = false;
        g_sunLight = nullptr;
    }
    // The template the sun's while the game may make the follow light from it, then its own again.
    float* numbers[5] = {};
    float own[5] = {};
    const bool sunTemplate = g_followLightOurs && g_followLightTemplate >= 0 && g_lightTemplates;
    if (sunTemplate)
        for (int i = 0; i < 5; ++i) {
            numbers[i] = reinterpret_cast<float*>(g_lightTemplates + g_followLightTemplate * kLightTemplateSize + kTemplateNumbers[i]);
            own[i] = *numbers[i];
            *numbers[i] = kSunTemplateNumbers[i];
        }
    g_followLights(player);
    if (sunTemplate)
        for (int i = 0; i < 5; ++i) *numbers[i] = own[i];
}

void __cdecl PlaceFollowLight(Place place, Turn turn, int lightTemplate) {
    const bool sun = g_followLightOurs && g_sunShadows && InOurWorld();
    if (sun) {
        // Its up along the light (the sun's, by night the moon's), so it shines back at the camera.
        float up[3], unused[3];
        DirectLight(up, unused);
        const float east[3] = {1, 0, 0}, north[3] = {0, 0, 1};
        const float* along = fabsf(up[2]) > 0.9f ? east : north;
        float right[3] = {up[1] * along[2] - up[2] * along[1], up[2] * along[0] - up[0] * along[2], up[0] * along[1] - up[1] * along[0]};
        const float length = sqrtf(right[0] * right[0] + right[1] * right[1] + right[2] * right[2]);
        for (float& v : right) v /= length;
        const float forward[3] = {right[1] * up[2] - right[2] * up[1], right[2] * up[0] - right[0] * up[2], right[0] * up[1] - right[1] * up[0]};
        for (int i = 0; i < 3; ++i) {
            turn.rows[i] = right[i];
            turn.rows[3 + i] = up[i];
            turn.rows[6 + i] = forward[i];
            place.at[i] = g_cameraPosition[i] + up[i] * kSunLampDistance;
        }
    }
    g_placeFollowLight(place, turn, lightTemplate);
    g_sunLight = nullptr;
    if (!sun) return;
    BYTE* lamp = FindLamp(reinterpret_cast<void*>(0x1870630), nullptr, g_followLightHandle);
    BYTE* light = lamp ? *reinterpret_cast<BYTE**>(lamp + kLampLight) : nullptr;
    if (!light) return;
    float* colour = reinterpret_cast<float*>(lamp + kLampColour);
    if (lamp != g_followLightSeen) {
        g_followLightSeen = lamp;
        rfa::Log("Sun's shadows: the follow player light %08X (object %p, light %p) along the sun", g_followLightHandle, lamp, light);
    }
    // Next to no light of its own: the object's colour, and its light's (that times the intensity,
    // set when the light is made).
    for (int i = 0; i < 3; ++i) colour[i] = 0.001f;
    const float faint[4] = {0.001f, 0.001f, 0.001f, 1.0f};
    SetLightColour(light, nullptr, faint);
    SetShadowMapOnly(light, nullptr, false);
    g_sunLight = light;
}

// Replace the time of day's ambient setters: the game's own, then in our worlds the world's
// ambient light times Ambient=.
void ScaleAmbient(float (&colour)[3]) {
    if (g_ambient == 1.0f || !InOurWorld()) return;
    for (float& c : colour) c *= g_ambient;
}

void __cdecl SetAmbient(const float* colour) {
    g_setAmbient(colour);
    ScaleAmbient(g_worldAmbient);
}

void __cdecl SetBackAmbient(const float* colour) {
    g_setBackAmbient(colour);
    ScaleAmbient(g_worldBackAmbient);
}

void TellAmbient() {
    if (!g_tellAmbient) return;
    g_tellAmbient = false;
    float toward[3], sun[3], tint[3];
    DirectLight(toward, sun);
    ColourCorrection(tint);
    rfa::Log("Ground at %.2f h: sky light from above %.3f %.3f %.3f, from below %.3f %.3f %.3f (Ambient=%.3g); "
             "%s toward %.2f %.2f %.2f, light %.3f %.3f %.3f (Sun=%.3g); colour correction %.3f %.3f %.3f",
             g_timeOfDay, g_worldAmbient[0], g_worldAmbient[1], g_worldAmbient[2], g_worldBackAmbient[0], g_worldBackAmbient[1],
             g_worldBackAmbient[2], g_ambient, SunUp() ? "sun" : "moon", toward[0], toward[1], toward[2], sun[0], sun[1], sun[2], g_sun,
             tint[0], tint[1], tint[2]);
}

// Each frame: in our worlds the game clock runs at ClockSpeed; in any other, at the speed it had.
void RunClock(bool ours) {
    if (ours && g_clockSpeed > 0 && !g_clockOurs) {
        g_clockSpeedBefore = g_gameClockSpeed;
        g_gameClockSpeed = g_clockSpeed;
        g_clockOurs = true;
        rfa::Log("Clock: running at %.3g game seconds a second in world \"%s\", a day in %.3g minutes (was %.3g)",
                 g_clockSpeed, WorldName(), 24 * 60 / g_clockSpeed, g_clockSpeedBefore);
    } else if (!ours && g_clockOurs) {
        g_gameClockSpeed = g_clockSpeedBefore;
        g_clockOurs = false;
        rfa::Log("Clock: back to the game's speed %.3g in world \"%s\"", g_clockSpeedBefore, WorldName());
    }
}

// The radius setter, wrapped: in our worlds, held to what each layer can track.
void __fastcall SetStreamRadiusHeld(void* manager, void* unused, int layer, float radius) {
    if ((layer == kSubzoneLayer || layer == kZoneLayer) && InOurWorld() && radius > MostRadius(layer)) {
        RFA_LOG_FIRST(2, "Streaming: layer %d's radius %.0f m held to %.0f m (more could meet more cells than the game tracks)",
                      layer, radius, MostRadius(layer));
        radius = MostRadius(layer);
    }
    g_setStreamRadius(manager, unused, layer, radius);
}

// Each frame: in our worlds the sub-zones' layer keeps the game's radius but no more than it can
// track, and the zones' layer streams at StreamRadius (set again if the game put its own back); in
// any other world the zones' radius it had, if its layers are still the ones we changed.
void RunStreaming(bool ours) {
    void* manager = g_streamManager;
    if (!manager || !*static_cast<void**>(manager)) return;  // no manager, or its layers not made yet
    void* layers = *static_cast<void**>(manager);
    if (ours && StreamRadiusOf(manager, nullptr, kSubzoneLayer) > MostRadius(kSubzoneLayer)) {
        SetStreamRadius(manager, nullptr, kSubzoneLayer, MostRadius(kSubzoneLayer));
        rfa::Log("Streaming: sub-zones of world \"%s\" kept loaded %.0f m around the player (layer %d)", WorldName(),
                 StreamRadiusOf(manager, nullptr, kSubzoneLayer), kSubzoneLayer);
    }
    const float now = StreamRadiusOf(manager, nullptr, kZoneLayer);
    const float wanted = g_streamRadius < MostRadius(kZoneLayer) ? g_streamRadius : MostRadius(kZoneLayer);
    if (ours && g_streamRadius > 0) {
        if (now == wanted) return;
        if (!g_streamOurs || layers != g_streamLayers) g_streamRadiusBefore = now;
        g_streamOurs = true;
        g_streamLayers = layers;
        SetStreamRadius(manager, nullptr, kZoneLayer, wanted);
        rfa::Log("Streaming: zones of world \"%s\" (%.0f m) kept loaded %.0f m around the player (layer %d; the game's radius %.0f m)",
                 WorldName(), OurZoneWidth(), wanted, kZoneLayer, g_streamRadiusBefore);
    } else if (g_streamOurs) {
        g_streamOurs = false;
        if (layers == g_streamLayers) {
            SetStreamRadius(manager, nullptr, kZoneLayer, g_streamRadiusBefore);
            rfa::Log("Streaming: back to the game's radius %.0f m in world \"%s\"", g_streamRadiusBefore, WorldName());
        }
    }
}

// Whether a world's or zone's name is one of ours: the world's name, or it, "_" and more.
bool NamedOurs(const char* name) {
    for (const std::string& world : g_worlds) {
        const size_t n = world.size();
        if (_strnicmp(name, world.c_str(), n) == 0 && (name[n] == 0 || name[n] == '_')) return true;
    }
    return false;
}

// Overwrites one of the game's constants (its read-only data).
bool SetGameConstant(DWORD address, float value) {
    auto constant = reinterpret_cast<float*>(address);
    DWORD old;
    if (!VirtualProtect(constant, sizeof(float), PAGE_READWRITE, &old)) return false;
    *constant = value;
    VirtualProtect(constant, sizeof(float), old, &old);
    return true;
}

// The physics world widened for our zones (see kWorldReach), if they need more than the game's.
void WidenWorld() {
    const float reach = kWorldReachZones * g_zoneWidth;
    if (reach <= kGameWorldReach) return;
    const float& now = *reinterpret_cast<const float*>(kWorldReach);
    const float& nowBack = *reinterpret_cast<const float*>(kWorldReachBack);
    if (now != kGameWorldReach || nowBack != -kGameWorldReach) {
        rfa::Log("The physics world's size isn't the expected %.0f m (%.0f, %.0f); left alone, so zones beyond it won't work",
                 kGameWorldReach, now, nowBack);
        return;
    }
    if (SetGameConstant(kWorldReach, reach) && SetGameConstant(kWorldReachBack, -reach))
        rfa::Log("The physics world reaches %.0f m from the middle (the game's %.0f m; nine %.0f m zones)", reach, kGameWorldReach, g_zoneWidth);
    else
        rfa::Log("Could not widen the physics world; zones beyond %.0f m from the middle won't work", kGameWorldReach);
}

// The zone size, wrapped: the game's, then ZoneSize= for our worlds.
void __cdecl ZoneSize(const char* name, float* size) {
    g_zoneSize(name, size);
    if (g_zoneWidth > 0 && name && size && NamedOurs(name) && *size != g_zoneWidth) {
        RFA_LOG_FIRST(1, "Zones: \"%s\" gets %.0f m zones (the game's %.0f m; ZoneSize=)", name, g_zoneWidth, *size);
        *size = g_zoneWidth;
    }
}

std::wstring DataFolder() { return rfa::RootDir() + L"\\mods\\guerrilla_world\\"; }

// Whether a zone's name (<world>_XX_YY) is one of our worlds'.
bool OurZone(const char* zone) {
    for (const std::string& name : g_worlds)
        if (_strnicmp(zone, name.c_str(), name.size()) == 0 && zone[name.size()] == '_') return true;
    return false;
}

// Replaces the game's error reports: all go through, but the invalid mission hash of our zones.
int g_missionHashReports = 0;
int g_reportsLogged = 0;  // the game's other reports in our worlds, also written to the log (the first 200)

void __cdecl ReportError(int level, int flag, const char* format, va_list arguments) {
    if (reinterpret_cast<DWORD>(format) == kMissionHashFormat) {
        va_list peek;
        va_copy(peek, arguments);
        const char* zone = va_arg(peek, const char*);
        va_end(peek);
        if (zone && OurZone(zone)) {
            if (g_missionHashReports++ == 0)
                rfa::Log("Leaving out the game's \"invalid mission info hash\" reports for our worlds' zones (first: %s)", zone);
            return;
        }
    }
    // What the game reports in our worlds (a file it couldn't load, a pool that ran out, ...) only
    // flashes on the screen; the log keeps it.
    if (format && g_reportsLogged < 200 && InOurWorld()) {
        char text[512];
        va_list copy;
        va_copy(copy, arguments);
        vsnprintf(text, sizeof(text), format, copy);
        va_end(copy);
        size_t length = strlen(text);
        while (length > 0 && (text[length - 1] == '\n' || text[length - 1] == '\r')) text[--length] = '\0';
        if (++g_reportsLogged == 200) rfa::Log("Game report (level %d): %s (the last one logged)", level, text);
        else rfa::Log("Game report (level %d): %s", level, text);
    }
    g_reportError(level, flag, format, arguments);
}

// The renderer's warnings: when one of its pools runs out it sets a bit in a word (the renderer,
// [0x11BA7F0], +0x60, then +4) and draws a "RENDER_LIB WARNINGS" box with a line a bit, a message for
// each but two (0x40 and 0x400 show as an empty line). The box only shows on the screen; each bit is
// named in the log the first time it is seen in our worlds.
constexpr DWORD kRenderer = 0x11BA7F0;
constexpr size_t kRendererMetrics = 0x60, kMetricsWarnings = 0x04;
struct RenderWarning { DWORD bit; const char* text; };
constexpr RenderWarning kRenderWarnings[] = {
    {0x1, "out of dynamic indices"}, {0x2, "out of instance buffer space"}, {0x4, "out of instance blocks"},
    {0x8, "out of dynamic render groups"}, {0x10, "out of decals"}, {0x20, "out of decal blocks"},
    {0x80, "out of buffer space on the hardware"}, {0x100, "out of morph weight memory"},
    {0x200, "out of unbatched renderable trackers"}, {0x800, "out of render calls"}, {0x1000, "out of visibility list entries"},
    {0x2000, "out of GPU visibility queue entries"}, {0x4000, "out of GPU occlusion queries"}, {0x8000, "out of Edge geometry jobs"},
    {0x10000, "out of light volume buffer space"}, {0x20000, "out of batched visibility entries"}, {0x40000, "dynamic vertices stall"},
    {0x80000, "dynamic clone vertices stall"}, {0x100000, "dynamic indices stall"}, {0x200000, "dynamic instance vertices stall"},
};
DWORD g_renderWarningsLogged = 0;

void NoteRenderWarnings() {
    if (!InOurWorld()) return;
    const BYTE* renderer = *reinterpret_cast<BYTE* const*>(kRenderer);
    const BYTE* metrics = renderer ? *reinterpret_cast<const BYTE* const*>(renderer + kRendererMetrics) : nullptr;
    const DWORD warnings = metrics ? *reinterpret_cast<const DWORD*>(metrics + kMetricsWarnings) : 0;
    const DWORD fresh = warnings & ~g_renderWarningsLogged;
    if (!fresh) return;
    g_renderWarningsLogged |= fresh;
    for (DWORD bit = 1; bit; bit <<= 1) {
        if (!(fresh & bit)) continue;
        const char* text = nullptr;
        for (const RenderWarning& warning : kRenderWarnings)
            if (warning.bit == bit) text = warning.text;
        if (text) rfa::Log("Render warning (0x%X): %s", bit, text);
        else rfa::Log("Render warning (0x%X): one the game shows with no message", bit);
    }
}

// ---- Transition navpoints

// A building's navpoints come with its destructible object, and the game makes them as it streams the
// object in (navpoint's stream, 0x7F08A0, thiscall, one bool, ret 4). A transition navpoint (type 11, the
// byte at +0xF4; one joining the building's points to the ground's navigation) then looks for the nav
// cell under its position (+0x04) with the cell lookup (0x5B9AA0, cdecl, a query whose +0 points at the
// position; it returns the cell, or none), skipping the cells switched off (flags 0x08 and 0x10).
// Guerrilla switches off the ground's cells round many buildings until a mission switches them on, so
// in our worlds some of these points stand on no cell the lookup takes, and the game reports
// "Invalid transition navpoint ... can not find nav cell". Guerrilla's own navpoint stream does the same
// lookup and then, finding none, makes a navpoint at the nearest point of an open cell and links the
// two. Here the transition point is given that nearest open cell itself (the lookup asked again round
// it, ring by ring), which joins it to the ground's navigation in the same place; the game then keeps
// it in that cell as it does any other.
constexpr DWORD kNavpointStream = 0x7F08A0;
constexpr size_t kNavpointStreamPrologue = 9;  // push ebp / mov ebp,esp / sub esp,0A8h
using NavpointStreamFn = bool(__fastcall*)(BYTE* navpoint, void* unused, bool flag);
NavpointStreamFn g_navpointStream;
constexpr DWORD kFindNavCell = 0x5B9AA0;
constexpr size_t kFindNavCellPrologue = 9;  // push ebp / mov ebp,esp / sub esp,474h
using FindNavCellFn = void*(__cdecl*)(BYTE* query);
FindNavCellFn g_findNavCell;
constexpr size_t kNavpointPosition = 0x04, kNavpointHandle = 0x68, kNavpointType = 0xF4;
constexpr char kTransitionNavpoint = 11;
constexpr size_t kNavQuerySize = 0x30;   // the caller's query on its stack, with what the lookup writes back
constexpr float kNavRingStep = 0.5f;     // the rings round the point, every half metre ...
constexpr int kNavRings = 20;            // ... out to 10 m
constexpr int kNavRingDirections = 16;
thread_local BYTE* g_streamingNavpoint = nullptr;  // the navpoint this thread is streaming in, while its stream runs
int g_navpointsJoined = 0;

bool __fastcall NavpointStream(BYTE* navpoint, void* unused, bool flag) {
    BYTE* outer = g_streamingNavpoint;
    g_streamingNavpoint = navpoint;
    const bool result = g_navpointStream(navpoint, unused, flag);
    g_streamingNavpoint = outer;
    return result;
}

void* __cdecl FindNavCell(BYTE* query) {
    void* cell = g_findNavCell(query);
    BYTE* navpoint = g_streamingNavpoint;
    const float* position = navpoint ? reinterpret_cast<const float*>(navpoint + kNavpointPosition) : nullptr;
    if (cell || !navpoint || *reinterpret_cast<const char*>(navpoint + kNavpointType) != kTransitionNavpoint ||
        *reinterpret_cast<const float* const*>(query) != position || !InOurWorld())
        return cell;
    BYTE ask[kNavQuerySize];
    memcpy(ask, query, sizeof(ask));
    float around[3] = {position[0], position[1], position[2]};
    *reinterpret_cast<float**>(ask) = around;
    const DWORD handle = *reinterpret_cast<const DWORD*>(navpoint + kNavpointHandle);
    for (int ring = 1; ring <= kNavRings; ++ring) {
        const float distance = ring * kNavRingStep;
        for (int direction = 0; direction < kNavRingDirections; ++direction) {
            const float angle = direction * 6.2831853f / kNavRingDirections;
            around[0] = position[0] + distance * cosf(angle);
            around[2] = position[2] + distance * sinf(angle);
            cell = g_findNavCell(ask);
            if (!cell) continue;
            if (g_navpointsJoined++ < 50)
                rfa::Log("Pathfinding: transition navpoint %08X at (%.1f, %.1f, %.1f) stands on no open nav cell; joined to the nearest "
                         "open one, %.1f m away, as Guerrilla links such a point to the nearest open place",
                         handle, position[0], position[1], position[2], distance);
            return cell;
        }
    }
    rfa::Log("Pathfinding: transition navpoint %08X at (%.1f, %.1f, %.1f) has no open nav cell within %.0f m",
             handle, position[0], position[1], position[2], kNavRings * kNavRingStep);
    return nullptr;
}

int g_debugPrintsLogged = 0;

void __cdecl DebugPrint(const char* format, ...) {
    char text[1024];
    va_list arguments;
    va_start(arguments, format);
    vsnprintf(text, sizeof(text), format ? format : "", arguments);
    va_end(arguments);
    if (g_debugPrintsLogged < 500 && InOurWorld()) {
        char shown[1024];
        strcpy_s(shown, text);
        size_t length = strlen(shown);
        while (length > 0 && (shown[length - 1] == '\n' || shown[length - 1] == '\r')) shown[--length] = '\0';
        if (++g_debugPrintsLogged == 500) rfa::Log("Game debug print: %s (the last one logged)", shown);
        else rfa::Log("Game debug print: %s", shown);
    }
    g_debugPrint("%s", text);
}

// Replaces the area lookup: in our worlds the area is the world itself, so its own precache is mounted
// (if the world has one: a world built before the precache keeps the game's choice, as before).
const char* __cdecl PrecacheArea(int column, int row, int gridSize) {
    const char* area = g_precacheArea(column, row, gridSize);
    if (!InOurWorld()) return area;
    wchar_t precache[MAX_PATH];
    swprintf_s(precache, L"%s\\overrides\\build\\pc\\cache\\%hs_precache.vpp_pc", rfa::RootDir().c_str(), WorldName());
    if (GetFileAttributesW(precache) == INVALID_FILE_ATTRIBUTES) {
        rfa::Log("Precache: world \"%s\" has no %s_precache.vpp_pc (built before it had one); the game's \"%s\" is used", WorldName(), WorldName(), area);
        return area;
    }
    rfa::Log("Precache: world \"%s\" mounts %s_precache.vpp_pc (the game's area there is \"%s\")", WorldName(), WorldName(), area);
    return WorldName();
}

// Answers the start-up table copy's lookup of a table the world builder wrote (by its file name,
// whatever folder it is asked with) with the builder's; every other table is the game's.
bool __fastcall TableCacheFind(void* cache, void* unused, const char* name, CachedTable* out) {
    if (name && out) {
        const char* file = name;
        for (const char* c = name; *c; ++c)
            if (*c == '\\' || *c == '/') file = c + 1;
        for (const BuiltTable& table : g_builtTables) {
            if (table.bytes.empty() || _stricmp(file, table.name) != 0) continue;
            out->data = table.bytes.data();
            out->size = static_cast<DWORD>(table.bytes.size() - 1);
            rfa::Log("%s: the game reads mods\\guerrilla_world\\%s (%lu bytes)", table.what, table.name, out->size);
            return true;
        }
    }
    return g_tableCacheFind(cache, unused, name, out);
}

// The effect files, as the game streams them in: the streaming system's load handler for its VFX
// files (.cefct_pc with .gefct_pc; 0x4EAD30, registered at 0x5295D8), cdecl (the file's name, -, its
// data, -, its data file), true once the effect is in the game's map of loaded effects (0x11E9EF8; its
// lookup 0x4EB430 by the file's name), where playing an effect looks for its visual's file (the
// table's Filename, with .cefct_pc for its extension). Wrapped to write to the log, for the effects
// the world builder adds (rfg_), whether each loaded, and how many the game loads in all.
constexpr DWORD kEffectFileLoad = 0x4EAD30;
constexpr size_t kEffectFileLoadPrologue = 6;  // push ebp / mov ebp,esp / sub esp,8
using EffectFileLoadFn = bool(__cdecl*)(const char* name, void* unused1, void* data, void* unused2, void* dataFile);
EffectFileLoadFn g_effectFileLoad;
using LoadedEffectFn = void*(__cdecl*)(const char* name);
auto const LoadedEffect = reinterpret_cast<LoadedEffectFn>(0x4EB430);
int g_effectFilesLoaded = 0, g_ourEffectFilesLogged = 0;
std::vector<std::string> g_ourEffectFiles;  // the rfg_ effect files the game has loaded (see GuerrillaParticleLight)

bool __cdecl EffectFileLoad(const char* name, void* unused1, void* data, void* unused2, void* dataFile) {
    const bool loaded = g_effectFileLoad(name, unused1, data, unused2, dataFile);
    if (++g_effectFilesLoaded % 100 == 1) rfa::Log("Effects: the game has loaded %d effect files (now %s)", g_effectFilesLoaded, name ? name : "?");
    if (loaded && name && _strnicmp(name, "rfg_", 4) == 0 && strstr(name, ".cefct_pc") &&
        std::find(g_ourEffectFiles.begin(), g_ourEffectFiles.end(), name) == g_ourEffectFiles.end())
        g_ourEffectFiles.push_back(name);
    if (name && _strnicmp(name, "rfg_", 4) == 0 && g_ourEffectFilesLogged < 100) {
        ++g_ourEffectFilesLogged;
        rfa::Log("Effects: the game loads %s: %s, %s in its map of loaded effects", name, loaded ? "loaded" : "NOT LOADED",
                 LoadedEffect(name) ? "now" : "NOT");
    }
    return loaded;
}

// Playing an effect: the worker (0x4EB4B0; cdecl, ten arguments: the effect's entry in effects.xtbl,
// where, and eight more), which returns the effect object's handle or -1. Before making the object it
// may leave the visual out: with <LOD> (+0x9C) it drops it past the spawning distance (+0xA4) or, with
// View (+0x9D), out of the camera's view (0x768400(where, radius +0x84)); without a visual or a sound
// (the effect's +0x48) it gives up; and it gives up while a count it reads (0xD15220) is under 20.
// Wrapped to write to the log, for visuals the world builder adds (rfg_), each of those and the result
// (each visual's first try, and its first success after failing; up to 60 lines).
constexpr DWORD kPlayEffect = 0x4EB4B0;
constexpr size_t kPlayEffectPrologue = 6;  // push ebp / mov ebp,esp / sub esp,3Ch
using PlayEffectFn = int(__cdecl*)(BYTE* effect, const float* where, DWORD a3, DWORD a4, DWORD a5, DWORD a6, DWORD a7, DWORD a8,
                                   DWORD a9, DWORD a10);
PlayEffectFn g_playEffect;
using VisualFn = BYTE*(__cdecl*)(int index);
auto const Visual = reinterpret_cast<VisualFn>(0x4EF3F0);
using InViewFn = bool(__cdecl*)(const float* where, float radius);
auto const InView = reinterpret_cast<InViewFn>(0x768400);
using PlayCountFn = int(__cdecl*)();
auto const PlayCount = reinterpret_cast<PlayCountFn>(0xD15220);
int g_ourPlaysLogged = 0;
std::vector<std::string> g_visualsFailing, g_visualsPlayed;

int __cdecl PlayEffect(BYTE* effect, const float* where, DWORD a3, DWORD a4, DWORD a5, DWORD a6, DWORD a7, DWORD a8, DWORD a9, DWORD a10) {
    const int handle = g_playEffect(effect, where, a3, a4, a5, a6, a7, a8, a9, a10);
    BYTE* visual = effect ? Visual(*reinterpret_cast<int*>(effect + 0x44)) : nullptr;
    const char* name = visual ? reinterpret_cast<const char*>(visual) : "";
    if (_strnicmp(name, "rfg_", 4) != 0 || g_ourPlaysLogged >= 60 || !where) return handle;
    auto seen = [name](const std::vector<std::string>& names) { return std::find(names.begin(), names.end(), name) != names.end(); };
    if (seen(g_visualsPlayed) || (handle == -1 && seen(g_visualsFailing))) return handle;
    (handle == -1 ? g_visualsFailing : g_visualsPlayed).push_back(name);
    {
        ++g_ourPlaysLogged;
        const float radius = *reinterpret_cast<float*>(visual + 0x84), spawning = *reinterpret_cast<float*>(visual + 0xA4);
        float away = 0;
        for (int i = 0; i < 3; ++i) away += (where[i] - g_cameraPosition[i]) * (where[i] - g_cameraPosition[i]);
        rfa::Log("Effects: play %s at %.1f %.1f %.1f (%.1f m from the camera): handle %d; LOD %d, spawning %.0f m, view %d (in view: %d), "
                 "radius %.1f, flags 0x%X, category %d, file %s (%s), sound %d, count %d",
                 name, where[0], where[1], where[2], sqrtf(away), handle, visual[0x9C], spawning, visual[0x9D], InView(where, radius) ? 1 : 0,
                 radius, *reinterpret_cast<DWORD*>(visual + 0x88), *reinterpret_cast<int*>(visual + 0x94),
                 reinterpret_cast<const char*>(visual + 0x41), LoadedEffect(reinterpret_cast<const char*>(visual + 0x41)) ? "loaded" : "NOT loaded",
                 *reinterpret_cast<int*>(effect + 0x48), PlayCount());
    }
    return handle;
}

// Making an object in the world (0x6D44E0; the world in ECX, then its type, its zone and one more):
// none when its type's pool has none free (0x6D4650). Wrapped to write any failure to the log.
constexpr DWORD kMakeObject = 0x6D44E0;
constexpr size_t kMakeObjectPrologue = 6;  // push ebp / mov ebp,esp / sub esp,10h
using MakeObjectFn = void*(__fastcall*)(void* world, void* unused, void* type, void* zone, DWORD a3);
MakeObjectFn g_makeObject;
int g_objectFailuresLogged = 0;

void* __fastcall MakeObject(void* world, void* unused, void* type, void* zone, DWORD a3) {
    void* object = g_makeObject(world, unused, type, zone, a3);
    if (!object && g_objectFailuresLogged < 40) {
        ++g_objectFailuresLogged;
        rfa::Log("Objects: the game could not make an object (type %p, zone %p)", type, zone);
    }
    return object;
}

// Gives the gameplay properties' name pool kGameplayNameRoom bytes of the mod's own when the mod hands
// the game its table (the game's own buffer is left unused); every other pool is the game's.
void __fastcall NamePoolInit(void* pool, void* unused, void* buffer, DWORD size, const char* name) {
    if (!g_gameplayTable.bytes.empty() && name && strcmp(name, kGameplayNames) == 0) {
        g_gameplayNames.assign(kGameplayNameRoom, 0);
        rfa::Log("Gameplay properties: their names get %lu bytes (the game's %lu)", kGameplayNameRoom, size);
        g_namePoolInit(pool, unused, g_gameplayNames.data(), kGameplayNameRoom, name);
        return;
    }
    g_namePoolInit(pool, unused, buffer, size, name);
}

// ---- Shaders

// Guerrilla's sky, as its shader works it out, over the whole screen. The vertex shader makes one
// triangle that covers the screen, at the far plane; the pixel shader turns each pixel back into
// the direction it looks in and colours it, and lays Guerrilla's fog over it as its dome's shaders do
// (rfg-skybox_s): the dome (rfg_skybox, 150 m across, from 23.2 m below its middle to 75 m above)
// stretched across to the far clip and up to the fog scale (its height from the bottom times the scale
// ÷ its half-height, less that half-height), raised by the fog offset, and fogged there as the world's
// surfaces are (Guerrilla's height fog, as kFogShader); the sky's colour then mixed toward the fog's.
constexpr char kSkyShader[] = R"(
cbuffer Sky : register(b0)
{
    float4 Hemisphere[10];  // west0-3, zenith, east0-3, zenith: each colour times its brightness
    float4 Right;           // the camera's axes in the world; Right.w: tan of half the horizontal field of view
    float4 Up;              // Up.w: tan of half the vertical field of view
    float4 Forward;         // Forward.w: the far plane's depth (1, or 0 if the game's depth runs the other way)
    float4 FogColour;       // the fog's colour (times the colour correction and the sky's brightness)
    float4 FogNumbers;      // as the world's fog: x the fall-off with height, y the density at the camera's height, w the offset
    float4 DomeFog;         // x the far clip, y the fog scale, z the fog offset less the camera's height
};

static const float kDomeRadius = 75, kDomeHalfHeight = 49.088144;  // (75 + 23.176287) ÷ 2

// Guerrilla's height fog at a point (from the camera): 1 − min(2^(−(term × distance × density − offset)), 1).
float HeightFog(float3 at)
{
    float kh = at.y * FogNumbers.x;
    float term = abs(at.y) > 0.01 ? (1 - exp2(kh * -1.442695)) / kh : 1;
    return 1 - min(exp2(-(term * length(at) * FogNumbers.y - FogNumbers.w)), 1);
}

struct Pixel
{
    float4 position : SV_Position;
    float2 screen : TEXCOORD0;  // -1 to 1 across the screen, up is +1
};

Pixel SkyVertex(uint id : SV_VertexID)
{
    // (-1, -1), (3, -1), (-1, 3): one triangle over the whole screen.
    float2 screen = float2((id << 1) & 2, id & 2) * 2 - 1;
    Pixel p;
    p.position = float4(screen, Forward.w, 1);
    p.screen = screen;
    return p;
}

float4 SkyPixel(Pixel p) : SV_Target
{
    float3 look = normalize(Forward.xyz + Right.xyz * (p.screen.x * Right.w) + Up.xyz * (p.screen.y * Up.w));
    // How far round from west (-x) to east (+x), 0 to 1.
    float across = acos(clamp(-look.x, -1, 1)) / 3.14159265;
    // How high: the band, 0 at the horizon to 4 straight up (below the horizon, the horizon's).
    float height = saturate(look.y);
    float band = 2 * (asin(height) / 1.57079633 + height);
    int below = (int)min(floor(band), 3);
    float between = band - below;
    float3 west = lerp(Hemisphere[below].rgb, Hemisphere[below + 1].rgb, between);
    float3 east = lerp(Hemisphere[below + 5].rgb, Hemisphere[below + 6].rgb, between);
    float3 dome = look * kDomeRadius;
    float3 fogAt = float3(dome.x / kDomeRadius * DomeFog.x,
                          (dome.y + kDomeHalfHeight) * DomeFog.y / kDomeHalfHeight - kDomeHalfHeight + DomeFog.z,
                          dome.z / kDomeRadius * DomeFog.x);
    return float4(lerp(lerp(west, east, across), FogColour.rgb, HeightFog(fogAt)), 1);
}
)";

// Guerrilla's clouds, as its cloud shader draws them. The model's points are directions from the
// eye; they go just in front of the far plane (so behind everything the world has drawn), and come
// out with their colour already times their cover (laid over the sky with "one, one minus alpha").
// Fogged as Guerrilla's cloud and backdrop shaders fog them, each point of the model: its part
// stretched across to the far clip (its point across times the far clip ÷ the part's half-width) and
// up to the fog scale ((its height + the part's half-height) × the scale ÷ that half-height), raised
// by the fog offset, and fogged there as the world's surfaces are.
constexpr char kCloudShader[] = R"(
cbuffer Clouds : register(b0)
{
    float4 Right;      // as the sky's
    float4 Up;
    float4 Forward;    // Forward.w: 1 if the far plane's depth is 1, 0 if it is 0
    float4 Light;      // the time of day's light direction
    float4 Front;      // the clouds' front and rear light colours (times their brightness)
    float4 Rear;
    float4 Strengths;  // the four layers' strengths
    float4 Shape;      // x: the height they fade in from, y: the height they are full at, z: rim light strength, w: rim light power
    float4 Drift;      // x: layers 0-1's sideways offset, y: layers 2-3's
    float4 Ambient;    // the time of day's ambient light (added to the clouds' light)
    float4 FogColour;  // the fog's colour
    float4 FogNumbers; // as the world's fog: x the fall-off with height, y the density at the camera's height, w the offset
    float4 FogScale;   // x the far clip ÷ the part's half-width, y the fog scale ÷ its half-height, z the far clip ÷ its half-depth, w its half-height
    float4 FogRaise;   // x the fog offset less the camera's height
};

float HeightFog(float3 at)
{
    float kh = at.y * FogNumbers.x;
    float term = abs(at.y) > 0.01 ? (1 - exp2(kh * -1.442695)) / kh : 1;
    return 1 - min(exp2(-(term * length(at) * FogNumbers.y - FogNumbers.w)), 1);
}

Texture2D Layers01 : register(t0);
Texture2D Layers23 : register(t1);
SamplerState Wrapped : register(s0);

struct CloudPoint
{
    float4 position : SV_Position;
    float3 normal : TEXCOORD0;
    float3 look : TEXCOORD1;
    float4 layers : TEXCOORD2;  // xy: where layers 0-1 are read, zw: layers 2-3
    float fog : TEXCOORD3;
};

CloudPoint CloudVertex(float3 position : POSITION, float3 normal : NORMAL, float2 uv : TEXCOORD)
{
    float3 look = normalize(position);
    float depth = dot(look, Forward.xyz);
    CloudPoint p;
    p.position = float4(dot(look, Right.xyz) / Right.w, dot(look, Up.xyz) / Up.w, depth * lerp(0.000001, 0.999999, Forward.w), depth);
    p.normal = normal;
    p.look = look;
    p.layers = float4(uv.x + Drift.x, uv.y, uv.x + Drift.y, uv.y);
    p.fog = HeightFog(float3(position.x * FogScale.x, (position.y + FogScale.w) * FogScale.y + FogRaise.x, position.z * FogScale.z));
    return p;
}

float4 CloudPixel(CloudPoint p) : SV_Target
{
    float4 layers01 = Layers01.Sample(Wrapped, p.layers.xy);
    float4 layers23 = Layers23.Sample(Wrapped, p.layers.zw);
    float fade = saturate((clamp(p.look.y, -1, 1) - Shape.x) / (Shape.y - Shape.x));
    // Each layer's cover (green and alpha), and how much of what is behind it shows through.
    float4 cover = float4(layers01.g, layers01.a, layers23.g, layers23.a) * Strengths;
    float4 through = 1 - cover * fade;
    float covered = 1 - through.x * through.y * through.z * through.w;
    // Their light (red and blue), each layer over the ones before.
    float light = layers01.r * through.y + cover.y * fade * layers01.b;
    light = through.z * light + cover.z * fade * layers23.r;
    light = saturate(through.w * light + cover.w * fade * layers23.b);
    // Front or rear light by which way the cloud faces the light; a rim where it is thin, towards the sun.
    float3 lit = lerp(Front.rgb, Rear.rgb, dot(normalize(p.normal), Light.xyz) * 0.5 + 0.5);
    float rim = min(exp(-covered * Shape.w), 1);
    float towardsSun = pow(saturate(dot(normalize(p.look), Light.xyz)), 30);
    lit += Front.rgb * Shape.z * rim * towardsSun;
    float alpha = covered * covered;
    float3 colour = lerp(light * lit + Ambient.rgb, FogColour.rgb, p.fog);
    return min(float4(colour * alpha, alpha), 32);
}

// The backdrop, as Guerrilla's backdrop shader (rfg-skybox-matte_s) draws it. With it the buffer's
// Front and Rear are the mountains' colour on the side facing the light and on the far side, and
// Strengths is the mountain haze: its colour (rgb) and how thick it is (a).
float4 BackdropPixel(CloudPoint p) : SV_Target
{
    float4 picture = Layers01.Sample(Wrapped, p.layers.xy);
    float3 mountain = lerp(Rear.rgb, Front.rgb, dot(normalize(p.normal), Light.xyz) * 0.5 + 0.5);
    float3 colour = lerp(lerp(picture.rgb, Strengths.rgb, Strengths.a) * mountain, FogColour.rgb, p.fog);
    return min(float4(colour * picture.a, picture.a), 32);
}
)";

// The sky's light and the sun, added into the game's light buffer over the whole screen. Each pixel
// reads its surface's normal from the G-buffer (in the camera's space, as the game's ambient light
// shader decodes it: 0-1 to -1-1, the top half of z a flag) and gets the sky's light from above to
// below by how much it faces up (as ir_light_ambient blends them), plus the sun's light by how much
// it faces the sun. And the sun's shine, in the buffer's alpha as the game's lamps write theirs
// (ir_light_point): the sun reflected off the surface, how squarely toward the eye to the power of
// the surface's sharpness (the G-buffer normals' alpha: the square root of its Specular_Power / 50,
// the top half a flag; the power × 5.12), times how much it faces the sun and the sun's brightness.
// A surface's own shader takes that times its specular map and Specular_Color; the ground's ignores it.
// Drawn in two parts when the sun has its shadows (see "The sun's shadows"): the sun's light, through
// the sun's lamp's shadow map, as the light pass draws that lamp, then the sky's light after the pass.
// The shadow is the game's own test (ir_light_spot): the pixel's place in the camera's space (from its
// depth, as the lamps work it out: the ray through the lamp's inverse projection, times the distance
// ahead from IR_Similarity_Data), moved out a little along its normal, taken into the shadow map by
// Shadow_map_projTM, compared with the map over three texels round it, weighed as the game does; it
// fades out away from the camera and leaves at least Shadow_map_fade_params.x.
constexpr char kLightShader[] = R"(
cbuffer Light : register(b0)
{
    float4 FromAbove;   // the sky's light on a surface facing straight up
    float4 FromBelow;   // and facing straight down
    float4 SunColour;   // the sun's light on a surface facing it (0 while it is down)
    float4 SunToward;   // toward the sun, in the camera's space
    float4 UpToward;    // straight up, in the camera's space
    float4 TargetSize;  // the light buffer's width and height, and the tangents of half the field of view across and up
    float4 Parts;       // x 1: the sky's light; y 1: the sun's; z 1: the sun's through the lamp's shadow map
};

cbuffer Game : register(b1)
{
    float4 GameUnused[12];
    float4 SimilarityData;  // zw: the camera's distance ahead = 1 / (z × depth + w)
};

cbuffer Lamp : register(b2)  // the sun's lamp's, as the light pass drew it (ir_light_spot's vc0)
{
    float4 LampPosition;
    float4 InverseProjection[4];
    float4 SpotDirection;
    float4 ShadowRows[4];  // Shadow_map_projTM: x, y, depth and w in the map from the camera's space
    float4 ShadowFade;     // x the least light left, y and z the fade by distance ahead, w a texel's size
};

Texture2D Normals : register(t0);
Texture2D ShadowMap : register(t1);
Texture2D Depth : register(t2);
SamplerState Nearest : register(s0);

// n: the surface's normal, facing: how squarely it faces the sun (n · toward the sun). The normals
// are in the camera's space as the positions worked out here are (the game's lamps light with both).
float SunShadow(float2 at, float3 n, float facing)
{
    float depth = Depth.SampleLevel(Nearest, at, 0).x;
    float ahead = 1 / (SimilarityData.z * depth + SimilarityData.w);
    float2 corner = at * float2(2, -2) + float2(-1, 1);
    float4 near = InverseProjection[0] * corner.x + InverseProjection[1] * corner.y + InverseProjection[3];
    float3 ray = near.xyz / near.w;
    float4 surface = float4(ray / ray.z * ahead, 1);
    // So that a surface doesn't shadow itself through the map's wide texels (a texel's width in metres
    // from the map's width, its depth per metre from the map's depth), the point looked up is moved
    // out along the surface's normal, more the more it slants from the sun, and its depth held back
    // by a texel or two. Moving out instead of holding the depth far back keeps thin overhangs (a
    // roof's edge just above a wall) casting their shadow on what is under them.
    float texel = ShadowFade.w / length(ShadowRows[0].xyz);
    float slant = sqrt(saturate(1 - facing * facing));
    float4 lookup = float4(surface.xyz + n * texel * (1 + 2 * slant), 1);
    float4 inMap = float4(dot(ShadowRows[0], lookup), dot(ShadowRows[1], lookup), dot(ShadowRows[2], lookup), dot(ShadowRows[3], lookup));
    float3 mapped = inMap.xyz / inMap.w;
    if (any(mapped.xy < 0) || any(mapped.xy > 1)) return 1;
    float slope = min(slant / max(facing, 0.05), 1);
    float bias = (1 + slope) * texel * length(ShadowRows[2].xyz);
    // The test over a square three texels across centred on the point, each texel counted by how much
    // of it the square covers (the game's own lamps weigh theirs the same way): the edge slides
    // smoothly from texel to texel instead of stepping.
    uint width, height;
    ShadowMap.GetDimensions(width, height);
    float2 start = mapped.xy * float2(width, height) - 1.5;
    float2 first = floor(start);
    float2 part = start - first;
    float4 across = float4(1 - part.x, 1, 1, part.x);
    float4 down = float4(1 - part.y, 1, 1, part.y);
    float lit = 0;
    [unroll] for (int y = 0; y < 4; ++y)
        [unroll] for (int x = 0; x < 4; ++x)
        {
            int2 cell = clamp(int2(first) + int2(x, y), 0, int2(width, height) - 1);
            float stored = ShadowMap.Load(int3(cell, 0)).x;
            lit += (stored < mapped.z - bias ? 0 : 1) * across[x] * down[y];
        }
    float fade = saturate((ShadowFade.y - surface.z) * ShadowFade.z);
    return max(1 - (1 - lit / 9) * fade, ShadowFade.x);
}

float4 LightVertex(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2((id << 1) & 2, id & 2);
    return float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 LightPixel(float4 position : SV_Position) : SV_Target
{
    float2 at = position.xy / TargetSize.xy;
    float4 stored = Normals.Sample(Nearest, at);
    float3 n = stored.xyz;
    if (n.z >= 0.501961) n.z -= 0.5;
    n = n * 2 - 1;
    float length2 = dot(n, n);
    n = length2 > 0.000001 ? n * rsqrt(length2) : UpToward.xyz;
    float3 sky = lerp(FromBelow.rgb, FromAbove.rgb, dot(n, UpToward.xyz) * 0.5 + 0.5);
    float facing = dot(n, SunToward.xyz);
    float3 sun = SunColour.rgb * saturate(facing);

    float sharpness = (stored.w >= 0.501961 ? stored.w - 0.501961 : stored.w) * 14.166667;
    sharpness = sharpness * sharpness * 5.12;
    float3 eye = normalize(float3((at * float2(2, -2) + float2(-1, 1)) * TargetSize.zw, 1));  // from the camera to the surface
    float3 reflection = 2 * facing * n - SunToward.xyz;
    float toEye = max(saturate(dot(reflection, -eye)), 0.000001);
    float shine = saturate(facing) * pow(toEye, sharpness) * dot(SunColour.rgb, float3(0.3, 0.59, 0.11));
    float shadow = Parts.z > 0 && facing > 0 ? SunShadow(at, n, facing) : 1;
    return float4(sky * Parts.x + sun * Parts.y * shadow, shine * Parts.y * shadow);
}
)";

// Guerrilla's fog over what the world has drawn: for each pixel its surface's distance and height
// from the camera (the G-buffer's depth, turned into a distance with the numbers the game's lamps use,
// IR_Similarity_Data in its constants vc4), and the fog there as Guerrilla's shaders work it out per
// vertex (rfg-nodecal_s and the rest): term = (1 − e^(−k·dy)) / (k·dy), 1 when nearly level; fog =
// 1 − min(2^(−(term × distance × density − offset)), 1). Blended over the picture by that amount, as
// Guerrilla's surfaces mix in Fog_color. Pixels with nothing drawn (depth at the far plane) are the sky's.
constexpr char kFogShader[] = R"(
cbuffer Fog : register(b0)
{
    float4 FogColour;   // the fog's colour (times the colour correction)
    float4 FogNumbers;  // x the fall-off with height, y the density at the camera's height (powers of two a metre), w the offset
    float4 UpToward;    // straight up, in the camera's space; w the depth buffer's value at the far plane
    float4 TargetSize;  // the picture's width and height, and the tangents of half the field of view across and up
};

cbuffer Game : register(b1)
{
    float4 Unused[12];
    float4 SimilarityData;  // zw: the camera's distance ahead = 1 / (z × depth + w)
};

Texture2D Depth : register(t0);
SamplerState Nearest : register(s0);

float4 FogVertex(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2((id << 1) & 2, id & 2);
    return float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 FogPixel(float4 position : SV_Position) : SV_Target
{
    float2 at = position.xy / TargetSize.xy;
    float depth = Depth.Sample(Nearest, at).x;
    if (abs(depth - UpToward.w) < 0.0000001) discard;
    float ahead = 1 / (SimilarityData.z * depth + SimilarityData.w);
    float3 offset = float3((at * float2(2, -2) + float2(-1, 1)) * TargetSize.zw, 1) * ahead;  // the surface, from the camera
    float dy = dot(offset, UpToward.xyz);
    float kh = dy * FogNumbers.x;
    float term = abs(dy) > 0.01 ? (1 - exp2(kh * -1.442695)) / kh : 1;
    float fog = 1 - min(exp2(-(term * length(offset) * FogNumbers.y - FogNumbers.w)), 1);
    return float4(FogColour.rgb, saturate(fog));
}
)";

// Guerrilla's tone mapping (its rl_hdr passes 11-13, 12 and 21), drawn over Armageddon's final picture
// from the scene it was made from (held at exposure 1, so Guerrilla's own values). AdaptPixel, into a
// 1 x 1 target: the picture's brightness as Guerrilla measures it, 4.5 × each sample's luminance
// (Rec. 709; its scene buffer keeps the picture ÷ 8 and its measure takes 36 ×), each held to
// luminance_min-max, averaged over a grid, and the eye's brightness eased toward it, by
// 1 − 2^(−0.8744 × the frame's time × iris_rate). ToneMapPixel: the exposure, lum_range ÷ max(1 +
// lum_offset × (base + amount × (adapted − base)), 0.1) (eye_adaption_base, _amount), times Exposure=;
// the picture 1 − e^(−exposure × (scene + (bloom + sun shafts) × bloom_amount)), encoded for the
// screen (sRGB: Guerrilla's display gamma, 2.4). Guerrilla draws its sun shafts into the texture its
// bloom is then laid into, so they go with the bloom.
constexpr char kToneMappingShader[] = R"(
cbuffer ToneMapping : register(b0)
{
    float4 Adapt;     // x how far the eye moves toward the measure this frame (1: all the way), y z the range each sample is held to
    float4 Exposure;  // x lum_range, y lum_offset, z eye_adaption_base, w eye_adaption_amount
    float4 Picture;   // x Exposure=, y bloom_amount, z w the picture's width and height
    float4 Shafts;    // x how strong the sun shafts are (SunShafts=; 0 when none were drawn)
};

Texture2D Scene : register(t0);
Texture2D Bloom : register(t1);
Texture2D Adapted : register(t2);  // 1 x 1: the brightness the eye has adapted to
Texture2D SunShafts : register(t3);
Texture2D Particles : register(t4);  // Guerrilla's particle layer: their colour, and in alpha how much they cover (none: zeros)
SamplerState Smooth : register(s0);

float4 ToneVertex(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2((id << 1) & 2, id & 2);
    return float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 AdaptPixel(float4 position : SV_Position) : SV_Target
{
    float total = 0;
    for (int y = 0; y < 18; ++y)
        for (int x = 0; x < 32; ++x)
        {
            int2 at = int2((x + 0.5) * Picture.z / 32, (y + 0.5) * Picture.w / 18);
            float3 colour = max(Scene.Load(int3(at, 0)).rgb, 0);
            total += clamp(4.5 * dot(colour, float3(0.2125, 0.7154, 0.0721)), Adapt.y, Adapt.z);
        }
    float measured = total / 576;
    float before = Adapted.Load(int3(0, 0, 0)).r;
    return float4(Adapt.x >= 1 ? measured : before + (measured - before) * Adapt.x, 0, 0, 1);
}

float4 TonePixel(float4 position : SV_Position) : SV_Target
{
    float3 colour = max(Scene.Load(int3(position.xy, 0)).rgb, 0);
    float3 bloom = max(Bloom.Sample(Smooth, position.xy / Picture.zw).rgb, 0) * Picture.y;
    float3 shafts = max(SunShafts.Sample(Smooth, position.xy / Picture.zw).rgb, 0) * Picture.y * Shafts.x;
    float adapted = Adapted.Load(int3(0, 0, 0)).r;
    float exposure = Exposure.x / max(1 + Exposure.y * (Exposure.z + Exposure.w * (adapted - Exposure.z)), 0.1) * Picture.x;
    float4 particles = Particles.Load(int3(position.xy, 0));
    float3 laid = min(max(particles.rgb, 0), 0.996);
    float behind = saturate(1 - particles.a);
    float3 picture = saturate(1 - (1 - laid) * exp(-exposure * (colour * behind + bloom + shafts)));
    float3 encoded = picture <= 0.0031308 ? picture * 12.92 : 1.055 * pow(picture, 1 / 2.4) - 0.055;
    return float4(encoded, 1);
}
)";

// The sun, as Guerrilla's sky draws its orbitals (rl_skybox_renderable_instance::render_orbital_object):
// a square facing the camera, centred one unit from the eye toward the sun and as many units across
// as twice its scale, behind everything the world has drawn; its texture's alpha times its colour,
// added to what is there.
constexpr char kSunShader[] = R"(
cbuffer Sun : register(b0)
{
    float4 Right;    // as the sky's
    float4 Up;
    float4 Forward;
    float4 Centre;   // toward the sun (xyz), and its half-size one unit away (w)
    float4 Colour;   // its colour, times its brightness and opacity
};

Texture2D Disc : register(t0);
SamplerState Clamped : register(s0);

struct SunPoint
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

SunPoint SunVertex(uint id : SV_VertexID)
{
    // A strip of two triangles: (-1, -1), (1, -1), (-1, 1), (1, 1).
    float2 corner = float2(id & 1, id >> 1) * 2 - 1;
    float3 look = Centre.xyz + (Right.xyz * corner.x + Up.xyz * corner.y) * Centre.w;
    float depth = dot(look, Forward.xyz);
    SunPoint p;
    p.position = float4(dot(look, Right.xyz) / Right.w, dot(look, Up.xyz) / Up.w, depth * lerp(0.000001, 0.999999, Forward.w), depth);
    p.uv = corner * float2(0.5, -0.5) + 0.5;
    return p;
}

float4 SunPixel(SunPoint p) : SV_Target
{
    return float4(Colour.rgb * Disc.Sample(Clamped, p.uv).a, 0);
}
)";

// Guerrilla's sun shafts (its rl_hdr passes 27 and 15 with their vertex shaders 10 and 4; the steps
// between as Re-Mars-tered's rl_hdr_compositor::render_sun_shafts takes them), each at half the
// picture's size. SourcePixel: the sky round the sun, the picture averaged over 4 × 4 samples where
// nothing was drawn (the depth at the far plane), times a round fall-off the sun's size across
// (Guerrilla lays a texture there whose picture isn't known; a smooth disc stands in). BlurPixel: a
// 5 × 5 Gaussian. RadialPixel: 16 samples on the way from the pixel toward the sun, the nearer the
// pixel the more each counts (base − k × step ÷ 16), all times a fall-off with the distance from the
// sun across the screen, e^(curve base × (min(distance, radius) ÷ radius)^exponent) + shift (never
// below 0), and the strength ÷ 16.
constexpr char kShaftShader[] = R"(
cbuffer Shafts : register(b0)
{
    float4 Sun;       // xy the sun's place on the picture (0 to 1, down is +y), z the picture's width ÷ height, w the depth at the far plane
    float4 Texel;     // xy one texel of the half-size targets, zw one texel of the picture
    float4 Strength;  // x the strength, y the base, z the step; w the sun's size in half-size texels
    float4 Curve;     // x the radius, y the curve's base, z its exponent, w its shift
    float4 Blur;      // the Gaussian's weights 0, 1 and 2 texels off (x, y, z)
};

Texture2D Source : register(t0);
Texture2D Depth : register(t1);
SamplerState Smooth : register(s0);

static const float kTowardSun[16] = {1, 0.84375, 0.779029, 0.729367, 0.6875, 0.650614, 0.617267, 0.586601,
                                     0.558058, 0.53125, 0.505894, 0.481777, 0.458734, 0.436633, 0.415366, 0.394846};

float4 ShaftVertex(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2((id << 1) & 2, id & 2);
    return float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 SourcePixel(float4 position : SV_Position) : SV_Target
{
    float2 at = position.xy * Texel.xy;
    float3 sky = 0;
    for (int y = 0; y < 4; ++y)
        for (int x = 0; x < 4; ++x)
        {
            float2 tap = at + (float2(x, y) - 1.5) * Texel.zw;
            float depth = Depth.Load(int3(tap / Texel.zw, 0)).x;
            if (abs(depth - Sun.w) < 0.0000001)
                sky += max(Source.SampleLevel(Smooth, tap, 0).rgb, 0);
        }
    float fromSun = length(position.xy - Sun.xy / Texel.xy) / Strength.w;  // in the sun's sizes
    float disc = saturate(1 - 2 * fromSun);
    return float4(sky / 16 * disc * disc, 1);
}

float4 BlurPixel(float4 position : SV_Position) : SV_Target
{
    float2 at = position.xy * Texel.xy;
    float weights[3] = {Blur.x, Blur.y, Blur.z};
    float3 sum = 0;
    for (int y = -2; y <= 2; ++y)
        for (int x = -2; x <= 2; ++x)
            sum += Source.SampleLevel(Smooth, at + float2(x, y) * Texel.xy, 0).rgb * weights[abs(x)] * weights[abs(y)];
    return float4(sum, 1);
}

float4 RadialPixel(float4 position : SV_Position) : SV_Target
{
    float2 at = position.xy * Texel.xy;
    float2 toSun = Sun.xy - at;
    float distance = length(float2(toSun.x * Sun.z, toSun.y));
    float falloff = max(exp(Curve.y * pow(min(distance, Curve.x) / Curve.x, Curve.z)) + Curve.w, 0);
    float3 sum = 0;
    for (int k = 0; k < 16; ++k)
        sum += Source.SampleLevel(Smooth, lerp(Sun.xy, at, kTowardSun[k]), 0).rgb * (Strength.y - k * Strength.z / 16);
    return float4(sum * falloff * Strength.x / 16, 1);
}
)";

// ---- Drawing

struct LightConstants {
    float fromAbove[4];
    float fromBelow[4];
    float sunColour[4];
    float sunToward[4];
    float upToward[4];
    float targetSize[4];
    float parts[4];
};

struct FogConstants {
    float fogColour[4];
    float fogNumbers[4];
    float upToward[4];
    float targetSize[4];
};

struct SkyConstants {
    float hemisphere[10][4];
    float right[4];
    float up[4];
    float forward[4];
    float fogColour[4];
    float fogNumbers[4];
    float domeFog[4];
};

struct CloudConstants {
    float right[4];
    float up[4];
    float forward[4];
    float light[4];
    float front[4];
    float rear[4];
    float strengths[4];
    float shape[4];
    float drift[4];
    float ambient[4];
    float fogColour[4];
    float fogNumbers[4];
    float fogScale[4];
    float fogRaise[4];
};

// A cloud part from clouds.bin (see the world builder, rfgsky): which indices, overhead (0), along
// the horizon (1) or the backdrop (2), where its clouds fade in, its two textures, and half its size
// across, up and deep (from its points; Guerrilla's fog takes its part's box).
struct CloudPart {
    UINT first = 0, count = 0;
    int kind = 0;
    float fade = 0, full = 1;
    ID3D11ShaderResourceView* layers[2] = {};
    float half[3] = {1, 1, 1};
};

ID3D11Device* g_device = nullptr;
ID3D11DeviceContext* g_context = nullptr;
ID3D11VertexShader* g_skyVertex = nullptr;
ID3D11PixelShader* g_skyPixel = nullptr;
ID3D11Buffer* g_skyConstants = nullptr;
ID3D11DepthStencilState* g_skyDepth[2] = {};  // the far plane at 1 (depth test less-or-equal), at 0 (greater-or-equal)
ID3D11BlendState* g_skyBlend = nullptr;
ID3D11RasterizerState* g_skyRaster = nullptr;
bool g_drawingFailed = false;

std::vector<CloudPart> g_cloudParts;  // empty: no clouds
ID3D11VertexShader* g_cloudVertex = nullptr;
ID3D11PixelShader* g_cloudPixel = nullptr;
ID3D11PixelShader* g_backdropPixel = nullptr;
ID3D11InputLayout* g_cloudLayout = nullptr;
ID3D11Buffer* g_cloudVertices = nullptr;
ID3D11Buffer* g_cloudIndices = nullptr;
ID3D11Buffer* g_cloudConstants = nullptr;
ID3D11SamplerState* g_cloudSampler = nullptr;
ID3D11BlendState* g_cloudBlend = nullptr;
ID3D11RasterizerState* g_cloudRaster = nullptr;
float g_drift[2] = {0, 0};
ULONGLONG g_lastDrift = 0;

pD3DCompile g_compile = nullptr;

ID3DBlob* Compile(const char* source, size_t size, const char* entry, const char* target) {
    ID3DBlob* code = nullptr;
    ID3DBlob* errors = nullptr;
    if (FAILED(g_compile(source, size, "guerrilla_world", nullptr, nullptr, entry, target, D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &code, &errors))) {
        rfa::Log("The shader %s didn't compile: %s", entry, errors ? static_cast<const char*>(errors->GetBufferPointer()) : "?");
        code = nullptr;
    }
    if (errors) errors->Release();
    return code;
}

// Windows' shader compiler, loaded the first time a shader is needed.
bool LoadCompiler() {
    if (g_compile) return true;
    HMODULE compiler = LoadLibraryW(L"d3dcompiler_47.dll");
    g_compile = compiler ? reinterpret_cast<pD3DCompile>(GetProcAddress(compiler, "D3DCompile")) : nullptr;
    if (!g_compile) rfa::Log("Could not load Windows' shader compiler (d3dcompiler_47.dll); no sky or light");
    return g_compile != nullptr;
}

// Compiles the sky's shaders and makes the states it draws with, once the game has its device.
bool MakeSkyObjects() {
    if (!LoadCompiler()) return false;
    ID3DBlob* vertex = Compile(kSkyShader, sizeof(kSkyShader) - 1, "SkyVertex", "vs_4_0");
    ID3DBlob* pixel = Compile(kSkyShader, sizeof(kSkyShader) - 1, "SkyPixel", "ps_4_0");
    bool ok = vertex && pixel &&
              SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_skyVertex)) &&
              SUCCEEDED(g_device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &g_skyPixel));
    if (vertex) vertex->Release();
    if (pixel) pixel->Release();

    D3D11_BUFFER_DESC buffer = {};
    buffer.ByteWidth = sizeof(SkyConstants);
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_skyConstants));

    // Behind everything: tested against the depth buffer, never written to it.
    D3D11_DEPTH_STENCIL_DESC depth = {};
    depth.DepthEnable = TRUE;
    depth.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
    depth.DepthFunc = D3D11_COMPARISON_LESS_EQUAL;
    ok = ok && SUCCEEDED(g_device->CreateDepthStencilState(&depth, &g_skyDepth[0]));
    depth.DepthFunc = D3D11_COMPARISON_GREATER_EQUAL;
    ok = ok && SUCCEEDED(g_device->CreateDepthStencilState(&depth, &g_skyDepth[1]));

    D3D11_BLEND_DESC blend = {};
    blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_skyBlend));

    D3D11_RASTERIZER_DESC raster = {};
    raster.FillMode = D3D11_FILL_SOLID;
    raster.CullMode = D3D11_CULL_NONE;
    raster.DepthClipEnable = FALSE;  // the far plane itself must not be clipped away
    ok = ok && SUCCEEDED(g_device->CreateRasterizerState(&raster, &g_skyRaster));
    if (!ok) rfa::Log("Could not make the sky's shaders or states; no sky");
    return ok;
}

// The light pass's shaders and states.
ID3D11VertexShader* g_lightVertex = nullptr;
ID3D11PixelShader* g_lightPixel = nullptr;
ID3D11Buffer* g_lightConstants = nullptr;
ID3D11BlendState* g_lightBlend = nullptr;
ID3D11DepthStencilState* g_lightDepth = nullptr;
ID3D11RasterizerState* g_lightRaster = nullptr;
ID3D11SamplerState* g_lightSampler = nullptr;
bool g_lightFailed = false;

bool MakeLightObjects() {
    if (!LoadCompiler()) return false;
    ID3DBlob* vertex = Compile(kLightShader, sizeof(kLightShader) - 1, "LightVertex", "vs_4_0");
    ID3DBlob* pixel = Compile(kLightShader, sizeof(kLightShader) - 1, "LightPixel", "ps_4_0");
    bool ok = vertex && pixel &&
              SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_lightVertex)) &&
              SUCCEEDED(g_device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &g_lightPixel));
    if (vertex) vertex->Release();
    if (pixel) pixel->Release();

    D3D11_BUFFER_DESC buffer = {};
    buffer.ByteWidth = sizeof(LightConstants);
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_lightConstants));

    // Added to what the lamps put there.
    D3D11_BLEND_DESC blend = {};
    blend.RenderTarget[0].BlendEnable = TRUE;
    blend.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
    blend.RenderTarget[0].DestBlend = D3D11_BLEND_ONE;
    blend.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    blend.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
    blend.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
    blend.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_lightBlend));

    D3D11_DEPTH_STENCIL_DESC depth = {};  // no depth test: every pixel
    depth.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
    depth.DepthFunc = D3D11_COMPARISON_ALWAYS;
    ok = ok && SUCCEEDED(g_device->CreateDepthStencilState(&depth, &g_lightDepth));

    D3D11_RASTERIZER_DESC raster = {};
    raster.FillMode = D3D11_FILL_SOLID;
    raster.CullMode = D3D11_CULL_NONE;
    raster.DepthClipEnable = TRUE;
    ok = ok && SUCCEEDED(g_device->CreateRasterizerState(&raster, &g_lightRaster));

    D3D11_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler.MaxLOD = D3D11_FLOAT32_MAX;
    ok = ok && SUCCEEDED(g_device->CreateSamplerState(&sampler, &g_lightSampler));
    if (!ok) rfa::Log("Could not make the light pass's shaders or states; the ground keeps the game's lamps only");
    return ok;
}

// The fog's shaders and states.
ID3D11VertexShader* g_fogVertex = nullptr;
ID3D11PixelShader* g_fogPixel = nullptr;
ID3D11Buffer* g_fogConstants = nullptr;
ID3D11BlendState* g_fogBlend = nullptr;
ID3D11DepthStencilState* g_fogDepthTest = nullptr;
ID3D11RasterizerState* g_fogRaster = nullptr;
ID3D11SamplerState* g_fogSampler = nullptr;
bool g_fogFailed = false;

bool MakeFogObjects() {
    if (!LoadCompiler()) return false;
    ID3DBlob* vertex = Compile(kFogShader, sizeof(kFogShader) - 1, "FogVertex", "vs_4_0");
    ID3DBlob* pixel = Compile(kFogShader, sizeof(kFogShader) - 1, "FogPixel", "ps_4_0");
    bool ok = vertex && pixel &&
              SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_fogVertex)) &&
              SUCCEEDED(g_device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &g_fogPixel));
    if (vertex) vertex->Release();
    if (pixel) pixel->Release();

    D3D11_BUFFER_DESC buffer = {};
    buffer.ByteWidth = sizeof(FogConstants);
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_fogConstants));

    // The picture becomes the fog's colour by the fog's amount; its alpha is left as it is.
    D3D11_BLEND_DESC blend = {};
    blend.RenderTarget[0].BlendEnable = TRUE;
    blend.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA;
    blend.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
    blend.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    blend.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ZERO;
    blend.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
    blend.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_RED | D3D11_COLOR_WRITE_ENABLE_GREEN | D3D11_COLOR_WRITE_ENABLE_BLUE;
    ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_fogBlend));

    D3D11_DEPTH_STENCIL_DESC depth = {};  // no depth test: the depth is read as a texture
    depth.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
    depth.DepthFunc = D3D11_COMPARISON_ALWAYS;
    ok = ok && SUCCEEDED(g_device->CreateDepthStencilState(&depth, &g_fogDepthTest));

    D3D11_RASTERIZER_DESC raster = {};
    raster.FillMode = D3D11_FILL_SOLID;
    raster.CullMode = D3D11_CULL_NONE;
    raster.DepthClipEnable = TRUE;
    ok = ok && SUCCEEDED(g_device->CreateRasterizerState(&raster, &g_fogRaster));

    D3D11_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler.MaxLOD = D3D11_FLOAT32_MAX;
    ok = ok && SUCCEEDED(g_device->CreateSamplerState(&sampler, &g_fogSampler));
    if (!ok) rfa::Log("Could not make the fog's shaders or states; no fog");
    return ok;
}

// Guerrilla's tone mapping: its shaders, the eye's brightness (two 1 x 1 textures, last frame's and
// this frame's, swapped each frame), a copy of it to read back for the log, and its states.
struct ToneConstants {
    float adapt[4];
    float exposure[4];
    float picture[4];
    float shafts[4];
};

ID3D11VertexShader* g_toneVertex = nullptr;
ID3D11PixelShader* g_adaptPixel = nullptr;
ID3D11PixelShader* g_tonePixel = nullptr;
ID3D11Buffer* g_toneConstants = nullptr;
ID3D11Texture2D* g_eye[2] = {};
ID3D11RenderTargetView* g_eyeTarget[2] = {};
ID3D11ShaderResourceView* g_eyeView[2] = {};
ID3D11Texture2D* g_eyeReadback = nullptr;
ID3D11BlendState* g_eyeBlend = nullptr;   // written as it is
ID3D11BlendState* g_toneBlend = nullptr;  // the picture's red, green and blue
ID3D11SamplerState* g_toneSampler = nullptr;
ID3D11DepthStencilState* g_toneDepth = nullptr;
ID3D11RasterizerState* g_toneRaster = nullptr;

bool MakeToneMappingObjects() {
    if (!LoadCompiler()) return false;
    ID3DBlob* vertex = Compile(kToneMappingShader, sizeof(kToneMappingShader) - 1, "ToneVertex", "vs_4_0");
    ID3DBlob* adapt = Compile(kToneMappingShader, sizeof(kToneMappingShader) - 1, "AdaptPixel", "ps_4_0");
    ID3DBlob* tone = Compile(kToneMappingShader, sizeof(kToneMappingShader) - 1, "TonePixel", "ps_4_0");
    bool ok = vertex && adapt && tone &&
              SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_toneVertex)) &&
              SUCCEEDED(g_device->CreatePixelShader(adapt->GetBufferPointer(), adapt->GetBufferSize(), nullptr, &g_adaptPixel)) &&
              SUCCEEDED(g_device->CreatePixelShader(tone->GetBufferPointer(), tone->GetBufferSize(), nullptr, &g_tonePixel));
    for (ID3DBlob* blob : {vertex, adapt, tone})
        if (blob) blob->Release();

    D3D11_BUFFER_DESC buffer = {};
    buffer.ByteWidth = sizeof(ToneConstants);
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_toneConstants));

    D3D11_TEXTURE2D_DESC eye = {};
    eye.Width = eye.Height = 1;
    eye.MipLevels = eye.ArraySize = 1;
    eye.Format = DXGI_FORMAT_R32_FLOAT;
    eye.SampleDesc.Count = 1;
    eye.Usage = D3D11_USAGE_DEFAULT;
    eye.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    const float zero = 0;
    const D3D11_SUBRESOURCE_DATA start = {&zero, sizeof(zero), 0};
    for (int i = 0; i < 2 && ok; ++i)
        ok = SUCCEEDED(g_device->CreateTexture2D(&eye, &start, &g_eye[i])) &&
             SUCCEEDED(g_device->CreateRenderTargetView(g_eye[i], nullptr, &g_eyeTarget[i])) &&
             SUCCEEDED(g_device->CreateShaderResourceView(g_eye[i], nullptr, &g_eyeView[i]));
    eye.Usage = D3D11_USAGE_STAGING;
    eye.BindFlags = 0;
    eye.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ok = ok && SUCCEEDED(g_device->CreateTexture2D(&eye, nullptr, &g_eyeReadback));

    D3D11_BLEND_DESC blend = {};
    blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_eyeBlend));
    blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_RED | D3D11_COLOR_WRITE_ENABLE_GREEN | D3D11_COLOR_WRITE_ENABLE_BLUE;
    ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_toneBlend));

    D3D11_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler.MaxLOD = D3D11_FLOAT32_MAX;
    ok = ok && SUCCEEDED(g_device->CreateSamplerState(&sampler, &g_toneSampler));

    D3D11_DEPTH_STENCIL_DESC depth = {};  // no depth test
    depth.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
    depth.DepthFunc = D3D11_COMPARISON_ALWAYS;
    ok = ok && SUCCEEDED(g_device->CreateDepthStencilState(&depth, &g_toneDepth));

    D3D11_RASTERIZER_DESC raster = {};
    raster.FillMode = D3D11_FILL_SOLID;
    raster.CullMode = D3D11_CULL_NONE;
    raster.DepthClipEnable = TRUE;
    ok = ok && SUCCEEDED(g_device->CreateRasterizerState(&raster, &g_toneRaster));
    if (!ok) rfa::Log("Could not make the tone mapping's shaders or states; Armageddon's own tone mapping stays");
    return ok;
}

bool ReadWholeFile(const std::wstring& path, std::vector<BYTE>& bytes) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size;
    bool ok = GetFileSizeEx(file, &size) && size.QuadPart < (64 << 20);
    if (ok) {
        bytes.resize(static_cast<size_t>(size.QuadPart));
        DWORD read = 0;
        ok = ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr) && read == bytes.size();
    }
    CloseHandle(file);
    return ok;
}

template <typename T>
T ReadAt(const std::vector<BYTE>& bytes, size_t at) {
    T value;
    memcpy(&value, bytes.data() + at, sizeof(T));
    return value;
}

// A texture from a .dds file: 32-bit (blue, green, red, alpha, as the world builder writes them),
// with its mip levels.
ID3D11ShaderResourceView* LoadTexture(const std::wstring& path) {
    std::vector<BYTE> dds;
    if (!ReadWholeFile(path, dds) || dds.size() < 128 || memcmp(dds.data(), "DDS ", 4) != 0) return nullptr;
    const UINT height = ReadAt<UINT>(dds, 12), width = ReadAt<UINT>(dds, 16);
    const UINT mips = ReadAt<UINT>(dds, 28) ? ReadAt<UINT>(dds, 28) : 1;
    const UINT bits = ReadAt<UINT>(dds, 88), red = ReadAt<UINT>(dds, 92);
    if (ReadAt<UINT>(dds, 84) != 0 || bits != 32 || red != 0x00FF0000) return nullptr;  // only 32-bit blue-green-red-alpha
    std::vector<D3D11_SUBRESOURCE_DATA> levels(mips);
    size_t at = 128;
    for (UINT level = 0; level < mips; ++level) {
        const UINT w = width >> level ? width >> level : 1, h = height >> level ? height >> level : 1;
        if (at + size_t(w) * h * 4 > dds.size()) return nullptr;
        levels[level].pSysMem = dds.data() + at;
        levels[level].SysMemPitch = w * 4;
        at += size_t(w) * h * 4;
    }
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = mips;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_IMMUTABLE;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    ID3D11Texture2D* texture = nullptr;
    ID3D11ShaderResourceView* view = nullptr;
    if (SUCCEEDED(g_device->CreateTexture2D(&desc, levels.data(), &texture))) {
        g_device->CreateShaderResourceView(texture, nullptr, &view);
        texture->Release();
    }
    return view;
}

// The sun as Guerrilla's sky draws it: the orbital named "Sun" in the world's global time of day
// (<world>.gtodx, which the world builder writes from Guerrilla's terr01.gtodx and the game finds among
// the overrides), read once a world. Its texture (map_name, in mods\guerrilla_world\ as .dds) and three
// curves over the day: scale (its half-size, in hundredths of its distance), tint (red, green, blue
// and a brightness) and opacity. A curve's keys are half-hour slots, with a straight line between them
// round the clock, as Guerrilla's time of day fills its slots (and its game_time_of_day_get_orbital_params
// reads them: the colour times its brightness, the opacity apart, the scale ÷ 100).
struct CurveKey {
    float slot = 0;
    float value[4] = {};
};

struct Orbital {
    std::string world;  // the world it was read for
    bool found = false;
    std::string map;
    std::vector<CurveKey> scale, tint, opacity;
};

Orbital g_sunOrbital;

// The text between <tag> and </tag>, looked for from `from` and ending before `limit`.
bool Between(const std::string& text, const char* tag, size_t from, size_t limit, size_t& start, size_t& end) {
    const std::string open = std::string("<") + tag + ">", close = std::string("</") + tag + ">";
    start = text.find(open, from);
    if (start == std::string::npos || start >= limit) return false;
    start += open.size();
    end = text.find(close, start);
    return end != std::string::npos && end <= limit;
}

std::vector<CurveKey> ReadKeys(const std::string& text, const char* tag, size_t from, size_t limit) {
    std::vector<CurveKey> keys;
    size_t start, end;
    if (!Between(text, tag, from, limit, start, end)) return keys;
    size_t at = start, keyStart, keyEnd;
    while (Between(text, "key", at, end, keyStart, keyEnd)) {
        CurveKey key;
        size_t s, e;
        if (Between(text, "time", keyStart, keyEnd, s, e)) key.slot = static_cast<float>(atof(text.substr(s, e - s).c_str()));
        if (Between(text, "value", keyStart, keyEnd, s, e))
            sscanf_s(text.substr(s, e - s).c_str(), "%f %f %f %f", &key.value[0], &key.value[1], &key.value[2], &key.value[3]);
        keys.push_back(key);
        at = keyEnd;
    }
    std::sort(keys.begin(), keys.end(), [](const CurveKey& a, const CurveKey& b) { return a.slot < b.slot; });
    return keys;
}

// A curve at a time (in hours): its first `count` numbers, `otherwise` when it has no keys.
void CurveAt(const std::vector<CurveKey>& keys, float hours, int count, float otherwise, float* out) {
    for (int i = 0; i < count; ++i) out[i] = otherwise;
    if (keys.empty()) return;
    float slot = fmodf(hours * 2, 48.0f);
    if (slot < 0) slot += 48;
    size_t next = 0;  // the first key after now; the one before it, round the clock
    while (next < keys.size() && keys[next].slot <= slot) ++next;
    const CurveKey& after = keys[next % keys.size()];
    const CurveKey& before = keys[(next + keys.size() - 1) % keys.size()];
    float from = before.slot, to = after.slot;
    if (from > slot) from -= 48;
    if (to <= slot) to += 48;
    const float t = to > from ? (slot - from) / (to - from) : 0;
    for (int i = 0; i < count; ++i) out[i] = before.value[i] + (after.value[i] - before.value[i]) * t;
}

void ReadSunOrbital() {
    const char* world = WorldName();
    if (g_sunOrbital.world == world) return;
    g_sunOrbital = Orbital();
    g_sunOrbital.world = world;
    const std::wstring name(world, world + strlen(world));
    std::vector<BYTE> bytes;
    if (!ReadWholeFile(rfa::RootDir() + L"\\overrides\\build\\pc\\cache\\" + name + L".gtodx", bytes)) {
        rfa::Log("Sun: no overrides\\build\\pc\\cache\\%s.gtodx (the world builder writes it); no sun drawn", world);
        return;
    }
    const std::string text(bytes.begin(), bytes.end());
    size_t at = 0, start, end;
    while (Between(text, "orbital", at, text.size(), start, end)) {
        size_t s, e;
        if (Between(text, "name", start, end, s, e) && text.compare(s, e - s, "Sun") == 0) {
            g_sunOrbital.found = true;
            if (Between(text, "map_name", start, end, s, e)) g_sunOrbital.map = text.substr(s, e - s);
            g_sunOrbital.scale = ReadKeys(text, "scale", start, end);
            g_sunOrbital.tint = ReadKeys(text, "tint", start, end);
            g_sunOrbital.opacity = ReadKeys(text, "opacity", start, end);
            break;
        }
        at = end;
    }
    if (g_sunOrbital.found)
        rfa::Log("Sun: Guerrilla's sun orbital from %s.gtodx: texture %s, %zu scale, %zu tint and %zu opacity keys", world,
                 g_sunOrbital.map.c_str(), g_sunOrbital.scale.size(), g_sunOrbital.tint.size(), g_sunOrbital.opacity.size());
    else
        rfa::Log("Sun: %s.gtodx has no orbital named Sun; no sun drawn", world);
}

struct SunConstants {
    float right[4];
    float up[4];
    float forward[4];
    float centre[4];
    float colour[4];
};

ID3D11VertexShader* g_sunVertex = nullptr;
ID3D11PixelShader* g_sunPixel = nullptr;
ID3D11Buffer* g_sunConstants = nullptr;
ID3D11ShaderResourceView* g_sunDisc = nullptr;
ID3D11BlendState* g_sunBlend = nullptr;
ID3D11SamplerState* g_sunSampler = nullptr;
std::string g_sunDiscMap;  // the texture loaded (or tried)
bool g_sunFailed = false;

// The sun's shaders and states, and its texture (mods\guerrilla_world\<map_name>.dds, which the world
// builder writes); without the texture, no sun (logged).
bool MakeSunObjects() {
    if (!g_sunVertex) {
        ID3DBlob* vertex = Compile(kSunShader, sizeof(kSunShader) - 1, "SunVertex", "vs_4_0");
        ID3DBlob* pixel = Compile(kSunShader, sizeof(kSunShader) - 1, "SunPixel", "ps_4_0");
        bool ok = vertex && pixel &&
                  SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_sunVertex)) &&
                  SUCCEEDED(g_device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &g_sunPixel));
        for (ID3DBlob* blob : {vertex, pixel})
            if (blob) blob->Release();
        D3D11_BUFFER_DESC buffer = {};
        buffer.ByteWidth = sizeof(SunConstants);
        buffer.Usage = D3D11_USAGE_DYNAMIC;
        buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_sunConstants));
        D3D11_BLEND_DESC blend = {};  // added to the sky's red, green and blue
        blend.RenderTarget[0].BlendEnable = TRUE;
        blend.RenderTarget[0].SrcBlend = blend.RenderTarget[0].DestBlend = D3D11_BLEND_ONE;
        blend.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        blend.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ZERO;
        blend.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
        blend.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_RED | D3D11_COLOR_WRITE_ENABLE_GREEN | D3D11_COLOR_WRITE_ENABLE_BLUE;
        ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_sunBlend));
        D3D11_SAMPLER_DESC sampler = {};
        sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.MaxLOD = D3D11_FLOAT32_MAX;
        ok = ok && SUCCEEDED(g_device->CreateSamplerState(&sampler, &g_sunSampler));
        if (!ok) {
            rfa::Log("Could not make the sun's shaders or states; no sun");
            return false;
        }
    }
    if (g_sunDiscMap != g_sunOrbital.map) {
        g_sunDiscMap = g_sunOrbital.map;
        if (g_sunDisc) g_sunDisc->Release();
        const std::wstring map(g_sunDiscMap.begin(), g_sunDiscMap.end());
        g_sunDisc = g_sunDiscMap.empty() ? nullptr : LoadTexture(DataFolder() + map + L".dds");
        if (!g_sunDisc) rfa::Log("Sun: no mods\\guerrilla_world\\%s.dds (the world builder writes it); no sun drawn", g_sunDiscMap.c_str());
    }
    return true;
}

// Guerrilla's sun shaft settings (Re-Mars-tered's Rl_sun_shafts_*, their starting values): the sun's
// size in half-size texels, the strength (taken × 0.15625, and by a value already in the constants
// whose meaning isn't known, taken as 1), base and step, the fall-off's radius and curve, and the
// Gaussian's rho and multiplier.
constexpr float kShaftSunSize = 300, kShaftScale = 1, kShaftBaseLum = 1, kShaftLumStep = 1, kShaftRadius = 5, kShaftCurveBase = -4,
                kShaftCurveExponent = 1.6f, kShaftCurveShift = -0.02f, kShaftBlurRho = 0.75f, kShaftBlurMultiplier = 1;

struct ShaftConstants {
    float sun[4];
    float texel[4];
    float strength[4];
    float curve[4];
    float blur[4];
};

ID3D11VertexShader* g_shaftVertex = nullptr;
ID3D11PixelShader* g_shaftSource = nullptr;
ID3D11PixelShader* g_shaftBlur = nullptr;
ID3D11PixelShader* g_shaftRadial = nullptr;
ID3D11Buffer* g_shaftConstants = nullptr;
ID3D11Texture2D* g_shaft[2] = {};  // half the picture's size, 16-bit float: drawn into in turn
ID3D11RenderTargetView* g_shaftTarget[2] = {};
ID3D11ShaderResourceView* g_shaftView[2] = {};
UINT g_shaftWidth = 0, g_shaftHeight = 0;
ID3D11ShaderResourceView* g_shaftDepth = nullptr;  // the G-buffer's depth, kept from the light pass for the shafts
float g_farDepth = 1;                              // the depth buffer's value at the far plane (from the sky step)

bool MakeShaftObjects() {
    if (!LoadCompiler()) return false;
    ID3DBlob* vertex = Compile(kShaftShader, sizeof(kShaftShader) - 1, "ShaftVertex", "vs_4_0");
    ID3DBlob* source = Compile(kShaftShader, sizeof(kShaftShader) - 1, "SourcePixel", "ps_4_0");
    ID3DBlob* blur = Compile(kShaftShader, sizeof(kShaftShader) - 1, "BlurPixel", "ps_4_0");
    ID3DBlob* radial = Compile(kShaftShader, sizeof(kShaftShader) - 1, "RadialPixel", "ps_4_0");
    bool ok = vertex && source && blur && radial &&
              SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_shaftVertex)) &&
              SUCCEEDED(g_device->CreatePixelShader(source->GetBufferPointer(), source->GetBufferSize(), nullptr, &g_shaftSource)) &&
              SUCCEEDED(g_device->CreatePixelShader(blur->GetBufferPointer(), blur->GetBufferSize(), nullptr, &g_shaftBlur)) &&
              SUCCEEDED(g_device->CreatePixelShader(radial->GetBufferPointer(), radial->GetBufferSize(), nullptr, &g_shaftRadial));
    for (ID3DBlob* blob : {vertex, source, blur, radial})
        if (blob) blob->Release();
    D3D11_BUFFER_DESC buffer = {};
    buffer.ByteWidth = sizeof(ShaftConstants);
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_shaftConstants));
    if (!ok) rfa::Log("Could not make the sun shafts' shaders; no sun shafts");
    return ok;
}

// The two half-size targets, made again when the picture's size changes.
bool ShaftTargets(UINT width, UINT height) {
    if (g_shaft[0] && g_shaftWidth == width && g_shaftHeight == height) return true;
    for (int i = 0; i < 2; ++i) {
        if (g_shaftView[i]) g_shaftView[i]->Release();
        if (g_shaftTarget[i]) g_shaftTarget[i]->Release();
        if (g_shaft[i]) g_shaft[i]->Release();
        g_shaftView[i] = nullptr;
        g_shaftTarget[i] = nullptr;
        g_shaft[i] = nullptr;
    }
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    bool ok = true;
    for (int i = 0; i < 2 && ok; ++i)
        ok = SUCCEEDED(g_device->CreateTexture2D(&desc, nullptr, &g_shaft[i])) &&
             SUCCEEDED(g_device->CreateRenderTargetView(g_shaft[i], nullptr, &g_shaftTarget[i])) &&
             SUCCEEDED(g_device->CreateShaderResourceView(g_shaft[i], nullptr, &g_shaftView[i]));
    g_shaftWidth = ok ? width : 0;
    g_shaftHeight = ok ? height : 0;
    if (ok) rfa::Log("Sun shafts: drawn at half the picture's size, %u x %u", width, height);
    return ok;
}

// Loads the cloud model and textures (mods\guerrilla_world\) and makes what the clouds draw with.
// Without them, no clouds (logged).
void MakeCloudObjects() {
    const std::wstring folder = DataFolder();
    std::vector<BYTE> file;
    if (!ReadWholeFile(folder + L"clouds.bin", file) || file.size() < 12 || memcmp(file.data(), "GWCLOUD1", 8) != 0) {
        rfa::Log("Clouds: no mods\\guerrilla_world\\clouds.bin (made by the world builder from Guerrilla's files); the sky is drawn without clouds");
        return;
    }
    // Vertices: position, normal, texture coordinates (8 floats).
    size_t at = 8;
    const UINT vertexCount = ReadAt<UINT>(file, at);
    at += 4;
    const size_t vertexBytes = size_t(vertexCount) * 32;
    if (at + vertexBytes + 4 > file.size()) { rfa::Log("Clouds: clouds.bin is cut short"); return; }
    const BYTE* vertices = file.data() + at;
    at += vertexBytes;
    const UINT partCount = ReadAt<UINT>(file, at);
    at += 4;
    std::vector<CloudPart> parts;
    for (UINT i = 0; i < partCount; ++i) {
        if (at + 4 * 9 + 128 > file.size()) { rfa::Log("Clouds: clouds.bin is cut short"); return; }
        CloudPart part;
        part.first = ReadAt<UINT>(file, at);
        part.count = ReadAt<UINT>(file, at + 4);
        const int kind = ReadAt<int>(file, at + 8);
        part.kind = kind >= 0 && kind <= 2 ? kind : 1;
        part.fade = ReadAt<float>(file, at + 12);
        part.full = ReadAt<float>(file, at + 16);
        at += 20 + 16;  // and the model's own layer strengths, which the time of day's replace
        for (int t = 0; t < 2; ++t) {
            char name[65] = {};
            memcpy(name, file.data() + at, 64);
            at += 64;
            if (!name[0]) continue;  // the backdrop's second
            std::wstring path = folder;
            for (const char* c = name; *c; ++c) path += static_cast<wchar_t>(*c);
            part.layers[t] = LoadTexture(path + L".dds");
            if (!part.layers[t]) rfa::Log("Clouds: could not load %s.dds", name);
        }
        if (part.layers[0] && (part.layers[1] || part.kind == 2)) parts.push_back(part);
    }
    if (at + 4 > file.size()) { rfa::Log("Clouds: clouds.bin is cut short"); return; }
    const UINT indexCount = ReadAt<UINT>(file, at);
    at += 4;
    if (at + size_t(indexCount) * 4 > file.size() || parts.empty()) { rfa::Log("Clouds: clouds.bin has no usable parts"); return; }
    // Each part's box (for its fog), from the points its triangles use.
    for (CloudPart& part : parts) {
        float low[3] = {1e30f, 1e30f, 1e30f}, high[3] = {-1e30f, -1e30f, -1e30f};
        for (UINT i = part.first; i < part.first + part.count && i < indexCount; ++i) {
            const UINT index = ReadAt<UINT>(file, at + size_t(i) * 4);
            if (index >= vertexCount) continue;
            const float* point = reinterpret_cast<const float*>(vertices + size_t(index) * 32);
            for (int a = 0; a < 3; ++a) {
                if (point[a] < low[a]) low[a] = point[a];
                if (point[a] > high[a]) high[a] = point[a];
            }
        }
        for (int a = 0; a < 3; ++a) part.half[a] = high[a] > low[a] ? (high[a] - low[a]) / 2 : 1;
    }

    ID3DBlob* vertex = Compile(kCloudShader, sizeof(kCloudShader) - 1, "CloudVertex", "vs_4_0");
    ID3DBlob* pixel = Compile(kCloudShader, sizeof(kCloudShader) - 1, "CloudPixel", "ps_4_0");
    ID3DBlob* backdrop = Compile(kCloudShader, sizeof(kCloudShader) - 1, "BackdropPixel", "ps_4_0");
    const D3D11_INPUT_ELEMENT_DESC layout[] = {
        {"POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0},
        {"NORMAL", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 12, D3D11_INPUT_PER_VERTEX_DATA, 0},
        {"TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 24, D3D11_INPUT_PER_VERTEX_DATA, 0},
    };
    bool ok = vertex && pixel && backdrop &&
              SUCCEEDED(g_device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, &g_cloudVertex)) &&
              SUCCEEDED(g_device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, &g_cloudPixel)) &&
              SUCCEEDED(g_device->CreatePixelShader(backdrop->GetBufferPointer(), backdrop->GetBufferSize(), nullptr, &g_backdropPixel)) &&
              SUCCEEDED(g_device->CreateInputLayout(layout, 3, vertex->GetBufferPointer(), vertex->GetBufferSize(), &g_cloudLayout));
    if (vertex) vertex->Release();
    if (pixel) pixel->Release();
    if (backdrop) backdrop->Release();

    D3D11_BUFFER_DESC buffer = {};
    D3D11_SUBRESOURCE_DATA data = {};
    buffer.Usage = D3D11_USAGE_IMMUTABLE;
    buffer.ByteWidth = static_cast<UINT>(vertexBytes);
    buffer.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    data.pSysMem = vertices;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, &data, &g_cloudVertices));
    buffer.ByteWidth = indexCount * 4;
    buffer.BindFlags = D3D11_BIND_INDEX_BUFFER;
    data.pSysMem = file.data() + at;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, &data, &g_cloudIndices));
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.ByteWidth = sizeof(CloudConstants);
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    ok = ok && SUCCEEDED(g_device->CreateBuffer(&buffer, nullptr, &g_cloudConstants));

    D3D11_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_WRAP;
    sampler.MaxLOD = D3D11_FLOAT32_MAX;
    ok = ok && SUCCEEDED(g_device->CreateSamplerState(&sampler, &g_cloudSampler));

    // Laid over the sky: the colour comes already times its cover.
    D3D11_BLEND_DESC blend = {};
    auto& over = blend.RenderTarget[0];
    over.BlendEnable = TRUE;
    over.SrcBlend = over.SrcBlendAlpha = D3D11_BLEND_ONE;
    over.DestBlend = over.DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
    over.BlendOp = over.BlendOpAlpha = D3D11_BLEND_OP_ADD;
    over.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    ok = ok && SUCCEEDED(g_device->CreateBlendState(&blend, &g_cloudBlend));

    // Clipped as usual (so what is behind the eye is cut away), just in front of the far plane.
    D3D11_RASTERIZER_DESC raster = {};
    raster.FillMode = D3D11_FILL_SOLID;
    raster.CullMode = D3D11_CULL_NONE;
    raster.DepthClipEnable = TRUE;
    ok = ok && SUCCEEDED(g_device->CreateRasterizerState(&raster, &g_cloudRaster));
    if (!ok) {
        rfa::Log("Clouds: could not make their shaders, buffers or states; the sky is drawn without clouds");
        for (CloudPart& part : parts)
            for (auto* layer : part.layers) if (layer) layer->Release();
        return;
    }
    g_cloudParts = parts;
    rfa::Log("Clouds: %u parts, %u triangles, from mods\\guerrilla_world", static_cast<unsigned>(parts.size()), indexCount / 3);
}

void SkyColours(float (&hemisphere)[10][4]) {
    float tint[3];
    ColourCorrection(tint);
    for (int c = 0; c < 10; ++c) {
        float colour[3];
        TodColour(kSkyCurves[c], colour);
        for (int i = 0; i < 3; ++i) hemisphere[c][i] = colour[i] * SkyBrightness() * tint[i];
        hemisphere[c][3] = 1;
    }
}

// The clouds' drift: layers 0-1 and 2-3 move sideways at their speeds (texture widths per
// 240 seconds at speed 1).
void Drift() {
    const ULONGLONG now = GetTickCount64();
    const float seconds = g_lastDrift ? (now - g_lastDrift) / 1000.0f : 0.0f;
    g_lastDrift = now;
    if (seconds > 1) return;  // paused, loading
    g_drift[0] = fmodf(g_drift[0] + TodNumber(kLayer01Speed) * seconds / 240.0f, 1.0f);
    g_drift[1] = fmodf(g_drift[1] + TodNumber(kLayer23Speed) * seconds / 240.0f, 1.0f);
}

void DrawClouds(const SkyConstants& sky) {
    if (g_cloudParts.empty()) return;
    Drift();
    CloudConstants constants = {};
    memcpy(constants.right, sky.right, sizeof(constants.right));
    memcpy(constants.up, sky.up, sizeof(constants.up));
    memcpy(constants.forward, sky.forward, sizeof(constants.forward));
    if (g_lightOn) {
        float toward[3], unused[3];  // lit by our sun (or moon)
        DirectLight(toward, unused);
        for (int i = 0; i < 3; ++i) constants.light[i] = toward[i];
    } else {
        float length = sqrtf(g_lightDirection[0] * g_lightDirection[0] + g_lightDirection[1] * g_lightDirection[1] + g_lightDirection[2] * g_lightDirection[2]);
        for (int i = 0; i < 3; ++i) constants.light[i] = length > 0 ? g_lightDirection[i] / length : (i == 1 ? 1.0f : 0.0f);
    }
    constants.shape[2] = TodNumber(kBacklightStrength);
    constants.shape[3] = TodNumber(kBacklightPower);
    constants.drift[0] = g_drift[0];
    constants.drift[1] = g_drift[1];
    // The ambient light and the fog's colour and numbers, as the sky's (its own scale and offset each part).
    float correction[3];
    ColourCorrection(correction);
    for (int i = 0; i < 3; ++i) constants.ambient[i] = g_worldAmbient[i] * correction[i] * SkyBrightness();
    memcpy(constants.fogColour, sky.fogColour, sizeof(constants.fogColour));
    memcpy(constants.fogNumbers, sky.fogNumbers, sizeof(constants.fogNumbers));

    UINT stride = 32, offset = 0;
    g_context->IASetInputLayout(g_cloudLayout);
    g_context->IASetVertexBuffers(0, 1, &g_cloudVertices, &stride, &offset);
    g_context->IASetIndexBuffer(g_cloudIndices, DXGI_FORMAT_R32_UINT, 0);
    g_context->VSSetShader(g_cloudVertex, nullptr, 0);
    g_context->VSSetConstantBuffers(0, 1, &g_cloudConstants);
    g_context->PSSetConstantBuffers(0, 1, &g_cloudConstants);
    g_context->PSSetSamplers(0, 1, &g_cloudSampler);
    const float noFactor[4] = {1, 1, 1, 1};
    g_context->OMSetBlendState(g_cloudBlend, noFactor, 0xFFFFFFFF);
    g_context->RSSetState(g_cloudRaster);
    // In the model's order: the overhead clouds, the ones along the horizon, then the backdrop in front of them.
    float tint[3];
    ColourCorrection(tint);
    for (const CloudPart& part : g_cloudParts) {
        float colour[3];
        const float fogScale = TodNumber(part.kind == 2 ? kMatteFogScale : kCloudsFogScale);
        const float fogOffset = TodNumber(part.kind == 2 ? kMatteFogOffset : kCloudsFogOffset);
        constants.fogScale[0] = kSkyFogFar / part.half[0];
        constants.fogScale[1] = fogScale / part.half[1];
        constants.fogScale[2] = kSkyFogFar / part.half[2];
        constants.fogScale[3] = part.half[1];
        constants.fogRaise[0] = fogOffset - g_cameraPosition[1];
        if (part.kind == 2) {
            TodColour(kMountainFront, colour);
            for (int i = 0; i < 3; ++i) constants.front[i] = colour[i] * SkyBrightness() * tint[i];
            TodColour(kMountainBack, colour);
            for (int i = 0; i < 3; ++i) constants.rear[i] = colour[i] * SkyBrightness() * tint[i];
            TodColour(kMountainFog, colour);  // mixed into the picture before it is lit
            for (int i = 0; i < 3; ++i) constants.strengths[i] = colour[i];
            constants.strengths[3] = TodNumber(kMountainFogDensity);
            if (g_tellColours)
                rfa::Log("Backdrop: lit side %.3f %.3f %.3f, far side %.3f %.3f %.3f, haze %.3f %.3f %.3f at %.2f", constants.front[0],
                         constants.front[1], constants.front[2], constants.rear[0], constants.rear[1], constants.rear[2],
                         constants.strengths[0], constants.strengths[1], constants.strengths[2], constants.strengths[3]);
        } else {
            TodColour(kCloudFront[part.kind], colour);
            for (int i = 0; i < 3; ++i) constants.front[i] = colour[i] * SkyBrightness() * tint[i];
            TodColour(kCloudRear[part.kind], colour);
            for (int i = 0; i < 3; ++i) constants.rear[i] = colour[i] * SkyBrightness() * tint[i];
            for (int i = 0; i < 4; ++i) constants.strengths[i] = TodNumber(kCloudLayers[part.kind][i]);
            constants.shape[0] = part.fade;
            constants.shape[1] = part.full > part.fade ? part.full : part.fade + 0.01f;
            if (g_tellColours)
                rfa::Log("Clouds (%s): front %.3f %.3f %.3f, rear %.3f %.3f %.3f, layers %.2f %.2f %.2f %.2f, rim %.2f ^%.2f, light from %.2f %.2f %.2f",
                         part.kind == 0 ? "overhead" : "horizon", constants.front[0], constants.front[1], constants.front[2], constants.rear[0],
                         constants.rear[1], constants.rear[2], constants.strengths[0], constants.strengths[1], constants.strengths[2],
                         constants.strengths[3], constants.shape[2], constants.shape[3], constants.light[0], constants.light[1], constants.light[2]);
        }
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (FAILED(g_context->Map(g_cloudConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) continue;
        memcpy(mapped.pData, &constants, sizeof(constants));
        g_context->Unmap(g_cloudConstants, 0);
        g_context->PSSetShader(part.kind == 2 ? g_backdropPixel : g_cloudPixel, nullptr, 0);
        g_context->PSSetShaderResources(0, 2, part.layers);
        g_context->DrawIndexed(part.count, part.first, 0);
    }
}

// The sun over the sky, before the clouds: toward the sun, its scale, colour and opacity from the sun
// orbital's curves now, the colour (times its brightness and Guerrilla's light scale, 1) times the
// opacity, the colour correction and the sky's brightness.
void DrawSun(const SkyConstants& sky) {
    ReadSunOrbital();
    if (!g_sunOrbital.found || g_sunFailed) return;
    if (!MakeSunObjects()) {
        g_sunFailed = true;
        return;
    }
    if (!g_sunDisc) return;
    float scale, opacity, tint[4], correction[3], toward[3];
    CurveAt(g_sunOrbital.scale, g_timeOfDay, 1, 0, &scale);
    CurveAt(g_sunOrbital.opacity, g_timeOfDay, 1, 0, &opacity);
    CurveAt(g_sunOrbital.tint, g_timeOfDay, 4, 1, tint);
    if (opacity <= 0 || scale <= 0) return;
    ColourCorrection(correction);
    SunToward(toward);
    SunConstants constants = {};
    memcpy(constants.right, sky.right, sizeof(constants.right));
    memcpy(constants.up, sky.up, sizeof(constants.up));
    memcpy(constants.forward, sky.forward, sizeof(constants.forward));
    for (int i = 0; i < 3; ++i) {
        constants.centre[i] = toward[i];
        constants.colour[i] = tint[i] * tint[3] * opacity * correction[i] * SkyBrightness();
    }
    constants.centre[3] = scale / 100;
    if (g_tellColours)
        rfa::Log("Sun at %.2f h: toward %.2f %.2f %.2f, %.4f across, colour %.3f %.3f %.3f (opacity %.2f)", g_timeOfDay, toward[0], toward[1],
                 toward[2], 2 * constants.centre[3], constants.colour[0], constants.colour[1], constants.colour[2], opacity);
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (FAILED(g_context->Map(g_sunConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) return;
    memcpy(mapped.pData, &constants, sizeof(constants));
    g_context->Unmap(g_sunConstants, 0);
    g_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
    g_context->IASetInputLayout(nullptr);
    g_context->VSSetShader(g_sunVertex, nullptr, 0);
    g_context->PSSetShader(g_sunPixel, nullptr, 0);
    g_context->VSSetConstantBuffers(0, 1, &g_sunConstants);
    g_context->PSSetConstantBuffers(0, 1, &g_sunConstants);
    g_context->PSSetShaderResources(0, 1, &g_sunDisc);
    g_context->PSSetSamplers(0, 1, &g_sunSampler);
    const float noFactor[4] = {1, 1, 1, 1};
    g_context->OMSetBlendState(g_sunBlend, noFactor, 0xFFFFFFFF);
    g_context->Draw(4, 0);
    g_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
}

const char* FormatName(DXGI_FORMAT format) {
    switch (format) {
    case DXGI_FORMAT_R16G16B16A16_FLOAT: return "16-bit float (HDR)";
    case DXGI_FORMAT_R11G11B10_FLOAT: return "11/10-bit float (HDR)";
    case DXGI_FORMAT_R8G8B8A8_UNORM: return "8-bit";
    case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB: return "8-bit sRGB";
    case DXGI_FORMAT_R10G10B10A2_UNORM: return "10-bit";
    default: return "other";
    }
}

// Everything on the device context the sky, clouds, fog and tone mapping change, to put back afterwards.
struct SavedState {
    D3D11_PRIMITIVE_TOPOLOGY topology = D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED;
    ID3D11InputLayout* layout = nullptr;
    ID3D11Buffer* vertexBuffer = nullptr;
    UINT vertexStride = 0, vertexOffset = 0;
    ID3D11Buffer* indexBuffer = nullptr;
    DXGI_FORMAT indexFormat = DXGI_FORMAT_UNKNOWN;
    UINT indexOffset = 0;
    ID3D11VertexShader* vertexShader = nullptr;
    ID3D11PixelShader* pixelShader = nullptr;
    ID3D11GeometryShader* geometryShader = nullptr;
    ID3D11Buffer* vertexConstants = nullptr;
    ID3D11Buffer* pixelConstants[3] = {};
    ID3D11ShaderResourceView* pixelResources[3] = {};
    ID3D11SamplerState* pixelSampler = nullptr;
    ID3D11BlendState* blend = nullptr;
    float blendFactor[4] = {};
    UINT sampleMask = 0;
    ID3D11RasterizerState* raster = nullptr;

    void Save() {
        g_context->IAGetPrimitiveTopology(&topology);
        g_context->IAGetInputLayout(&layout);
        g_context->IAGetVertexBuffers(0, 1, &vertexBuffer, &vertexStride, &vertexOffset);
        g_context->IAGetIndexBuffer(&indexBuffer, &indexFormat, &indexOffset);
        g_context->VSGetShader(&vertexShader, nullptr, nullptr);
        g_context->PSGetShader(&pixelShader, nullptr, nullptr);
        g_context->GSGetShader(&geometryShader, nullptr, nullptr);
        g_context->VSGetConstantBuffers(0, 1, &vertexConstants);
        g_context->PSGetConstantBuffers(0, 3, pixelConstants);
        g_context->PSGetShaderResources(0, 3, pixelResources);
        g_context->PSGetSamplers(0, 1, &pixelSampler);
        g_context->OMGetBlendState(&blend, blendFactor, &sampleMask);
        g_context->RSGetState(&raster);
    }

    void Restore() {
        g_context->IASetPrimitiveTopology(topology);
        g_context->IASetInputLayout(layout);
        g_context->IASetVertexBuffers(0, 1, &vertexBuffer, &vertexStride, &vertexOffset);
        g_context->IASetIndexBuffer(indexBuffer, indexFormat, indexOffset);
        g_context->VSSetShader(vertexShader, nullptr, 0);
        g_context->PSSetShader(pixelShader, nullptr, 0);
        g_context->GSSetShader(geometryShader, nullptr, 0);
        g_context->VSSetConstantBuffers(0, 1, &vertexConstants);
        g_context->PSSetConstantBuffers(0, 3, pixelConstants);
        g_context->PSSetShaderResources(0, 3, pixelResources);
        g_context->PSSetSamplers(0, 1, &pixelSampler);
        g_context->OMSetBlendState(blend, blendFactor, sampleMask);
        g_context->RSSetState(raster);
        for (IUnknown* held : {static_cast<IUnknown*>(layout), static_cast<IUnknown*>(vertexBuffer), static_cast<IUnknown*>(indexBuffer),
                               static_cast<IUnknown*>(vertexShader), static_cast<IUnknown*>(pixelShader), static_cast<IUnknown*>(geometryShader),
                               static_cast<IUnknown*>(vertexConstants), static_cast<IUnknown*>(pixelConstants[0]), static_cast<IUnknown*>(pixelConstants[1]),
                               static_cast<IUnknown*>(pixelConstants[2]),
                               static_cast<IUnknown*>(pixelResources[0]), static_cast<IUnknown*>(pixelResources[1]), static_cast<IUnknown*>(pixelResources[2]),
                               static_cast<IUnknown*>(pixelSampler), static_cast<IUnknown*>(blend), static_cast<IUnknown*>(raster)})
            if (held) held->Release();
    }
};

// Draws the sky and the clouds on whatever the game is drawing to, behind what it has drawn, and
// puts the device context back as it was.
void DrawSky() {
    ID3D11RenderTargetView* target = nullptr;
    ID3D11DepthStencilView* depthView = nullptr;
    g_context->OMGetRenderTargets(1, &target, &depthView);
    ID3D11DepthStencilState* gameDepth = nullptr;
    UINT stencilRef = 0;
    g_context->OMGetDepthStencilState(&gameDepth, &stencilRef);
    D3D11_DEPTH_STENCIL_DESC gameDepthDesc = {};
    gameDepthDesc.DepthFunc = D3D11_COMPARISON_LESS;  // what no state means
    if (gameDepth) gameDepth->GetDesc(&gameDepthDesc);
    const bool reversed = gameDepthDesc.DepthFunc == D3D11_COMPARISON_GREATER || gameDepthDesc.DepthFunc == D3D11_COMPARISON_GREATER_EQUAL;
    g_farDepth = reversed ? 0.0f : 1.0f;
    D3D11_VIEWPORT viewport = {};
    UINT viewports = 1;
    g_context->RSGetViewports(&viewports, &viewport);

    static bool told = false;
    if (!told) {
        told = true;
        D3D11_RENDER_TARGET_VIEW_DESC targetDesc = {};
        if (target) target->GetDesc(&targetDesc);
        rfa::Log("Sky: drawing into a %s target, %.0f x %.0f, %s; the game's depth test %d (%s)",
                 target ? FormatName(targetDesc.Format) : "missing", viewport.Width, viewport.Height,
                 depthView ? "with a depth buffer" : "WITHOUT a depth buffer", static_cast<int>(gameDepthDesc.DepthFunc),
                 reversed ? "far is 0" : "far is 1");
    }

    // Without a depth buffer the sky would cover what has been drawn: leave it.
    if (target && depthView && viewport.Width > 0 && viewport.Height > 0) {
        SkyConstants constants = {};
        SkyColours(constants.hemisphere);
        if (g_tellColours)
            rfa::Log("Sky at %.2f h, brightness %.3g: west horizon %.3f %.3f %.3f, east horizon %.3f %.3f %.3f, zenith %.3f %.3f %.3f",
                     g_timeOfDay, g_brightness, constants.hemisphere[0][0], constants.hemisphere[0][1], constants.hemisphere[0][2],
                     constants.hemisphere[5][0], constants.hemisphere[5][1], constants.hemisphere[5][2], constants.hemisphere[4][0],
                     constants.hemisphere[4][1], constants.hemisphere[4][2]);
        const float tanHalfVertical = tanf(g_cameraFov * 3.14159265f / 360.0f);
        for (int i = 0; i < 3; ++i) {
            constants.right[i] = g_cameraRotation[i];
            constants.up[i] = g_cameraRotation[3 + i];
            constants.forward[i] = g_cameraRotation[6 + i];
        }
        constants.right[3] = tanHalfVertical * viewport.Width / viewport.Height;
        constants.up[3] = tanHalfVertical;
        constants.forward[3] = reversed ? 0.0f : 1.0f;
        // Guerrilla's fog over the sky: the world's fog colour and numbers, the dome's scale and offset.
        float fogColour[3], correction[3];
        TodColour(kFogColour, fogColour);
        ColourCorrection(correction);
        for (int i = 0; i < 3; ++i) constants.fogColour[i] = fogColour[i] * correction[i] * SkyBrightness();
        FogNumbers(constants.fogNumbers);
        constants.domeFog[0] = kSkyFogFar;
        constants.domeFog[1] = TodNumber(kSkyFogScale);
        constants.domeFog[2] = TodNumber(kSkyFogOffset) - g_cameraPosition[1];
        if (g_tellColours)
            rfa::Log("Sky's fog at %.2f h: dome scale %.0f offset %.0f, clouds %.0f %.0f, backdrop %.0f %.0f (camera at %.0f m); fog colour "
                     "%.3f %.3f %.3f, fall-off %.4f, density %.5f, offset %.3f",
                     g_timeOfDay, constants.domeFog[1], TodNumber(kSkyFogOffset), TodNumber(kCloudsFogScale), TodNumber(kCloudsFogOffset),
                     TodNumber(kMatteFogScale), TodNumber(kMatteFogOffset), g_cameraPosition[1], constants.fogColour[0], constants.fogColour[1],
                     constants.fogColour[2], constants.fogNumbers[0], constants.fogNumbers[1], constants.fogNumbers[3]);
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(g_context->Map(g_skyConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            memcpy(mapped.pData, &constants, sizeof(constants));
            g_context->Unmap(g_skyConstants, 0);
            SavedState saved;
            saved.Save();

            g_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            g_context->IASetInputLayout(nullptr);
            g_context->VSSetShader(g_skyVertex, nullptr, 0);
            g_context->PSSetShader(g_skyPixel, nullptr, 0);
            g_context->GSSetShader(nullptr, nullptr, 0);
            g_context->VSSetConstantBuffers(0, 1, &g_skyConstants);
            g_context->PSSetConstantBuffers(0, 1, &g_skyConstants);
            const float noFactor[4] = {1, 1, 1, 1};
            g_context->OMSetBlendState(g_skyBlend, noFactor, 0xFFFFFFFF);
            g_context->OMSetDepthStencilState(g_skyDepth[reversed ? 1 : 0], 0);
            g_context->RSSetState(g_skyRaster);
            g_context->Draw(3, 0);
            DrawSun(constants);
            if (g_cloudsOn) DrawClouds(constants);

            saved.Restore();
        }
        g_tellColours = false;
    }
    g_context->OMSetDepthStencilState(gameDepth, stencilRef);
    if (gameDepth) gameDepth->Release();
    if (target) target->Release();
    if (depthView) depthView->Release();
}

// What the sun's light is drawn through when it has its shadows: the sun's lamp's shadow map, the
// G-buffer's depth, the game's constants (vc4) and the lamp's (vc0), as the light pass drew the lamp.
struct SunShadowInputs {
    ID3D11ShaderResourceView* map;
    ID3D11ShaderResourceView* depth;
    ID3D11Buffer* game;
    ID3D11Buffer* lamp;
};

// Adds the sky's light and the sun into the light buffer the light pass fills (its lamps' light), over
// the whole buffer, and puts the device context back as it was: either or both, the sun through its
// lamp's shadow map when given.
void DrawLight(ID3D11RenderTargetView* buffer, ID3D11ShaderResourceView* normals, bool withSky, bool withSun, const SunShadowInputs* shadow) {
    ID3D11Resource* resource = nullptr;
    buffer->GetResource(&resource);
    ID3D11Texture2D* texture = nullptr;
    D3D11_TEXTURE2D_DESC desc = {};
    if (resource && SUCCEEDED(resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&texture)))) {
        texture->GetDesc(&desc);
        texture->Release();
    }
    if (resource) resource->Release();
    if (desc.Width == 0 || desc.Height == 0) return;
    static bool told = false;
    if (!told) {
        told = true;
        D3D11_SHADER_RESOURCE_VIEW_DESC normalsDesc = {};
        normals->GetDesc(&normalsDesc);
        rfa::Log("Light: adding the sky's light and the sun to the game's light buffer (%s, format %d, %u x %u); normals format %d",
                 FormatName(desc.Format), static_cast<int>(desc.Format), desc.Width, desc.Height, static_cast<int>(normalsDesc.Format));
    }

    LightConstants constants = {};
    float toward[3], sun[3], tint[3];
    DirectLight(toward, sun);
    ColourCorrection(tint);
    for (int i = 0; i < 3; ++i) {
        constants.fromAbove[i] = g_worldAmbient[i] * tint[i];
        constants.fromBelow[i] = g_worldBackAmbient[i] * tint[i];
        constants.sunColour[i] = sun[i] * tint[i];
    }
    // The camera's space: x along its right, y its up, z its forward (its rows in the world).
    const float* axes[3] = {&g_cameraRotation[0], &g_cameraRotation[3], &g_cameraRotation[6]};
    for (int i = 0; i < 3; ++i) {
        constants.sunToward[i] = toward[0] * axes[i][0] + toward[1] * axes[i][1] + toward[2] * axes[i][2];
        constants.upToward[i] = axes[i][1];
    }
    constants.targetSize[0] = static_cast<float>(desc.Width);
    constants.targetSize[1] = static_cast<float>(desc.Height);
    constants.targetSize[3] = tanf(g_cameraFov * 3.14159265f / 360.0f);
    constants.targetSize[2] = constants.targetSize[3] * desc.Width / desc.Height;
    constants.parts[0] = withSky ? 1.0f : 0.0f;
    constants.parts[1] = withSun ? 1.0f : 0.0f;
    constants.parts[2] = shadow ? 1.0f : 0.0f;
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (FAILED(g_context->Map(g_lightConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) return;
    memcpy(mapped.pData, &constants, sizeof(constants));
    g_context->Unmap(g_lightConstants, 0);

    SavedState saved;
    saved.Save();
    ID3D11RenderTargetView* gameTargets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT] = {};
    ID3D11DepthStencilView* gameDepthView = nullptr;
    g_context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, gameTargets, &gameDepthView);
    ID3D11DepthStencilState* gameDepth = nullptr;
    UINT stencilRef = 0;
    g_context->OMGetDepthStencilState(&gameDepth, &stencilRef);
    D3D11_VIEWPORT gameViewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE] = {};
    UINT viewportCount = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
    g_context->RSGetViewports(&viewportCount, gameViewports);

    const D3D11_VIEWPORT viewport = {0, 0, static_cast<float>(desc.Width), static_cast<float>(desc.Height), 0, 1};
    g_context->OMSetRenderTargets(1, &buffer, nullptr);
    g_context->RSSetViewports(1, &viewport);
    g_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    g_context->IASetInputLayout(nullptr);
    g_context->VSSetShader(g_lightVertex, nullptr, 0);
    g_context->PSSetShader(g_lightPixel, nullptr, 0);
    g_context->GSSetShader(nullptr, nullptr, 0);
    g_context->PSSetConstantBuffers(0, 1, &g_lightConstants);
    g_context->PSSetShaderResources(0, 1, &normals);
    if (shadow) {
        ID3D11Buffer* buffers[2] = {shadow->game, shadow->lamp};
        g_context->PSSetConstantBuffers(1, 2, buffers);
        ID3D11ShaderResourceView* resources[2] = {shadow->map, shadow->depth};
        g_context->PSSetShaderResources(1, 2, resources);
    }
    g_context->PSSetSamplers(0, 1, &g_lightSampler);
    const float noFactor[4] = {1, 1, 1, 1};
    g_context->OMSetBlendState(g_lightBlend, noFactor, 0xFFFFFFFF);
    g_context->OMSetDepthStencilState(g_lightDepth, 0);
    g_context->RSSetState(g_lightRaster);
    g_context->Draw(3, 0);

    g_context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, gameTargets, gameDepthView);
    g_context->RSSetViewports(viewportCount, gameViewports);
    g_context->OMSetDepthStencilState(gameDepth, stencilRef);
    saved.Restore();
    for (ID3D11RenderTargetView* target : gameTargets)
        if (target) target->Release();
    if (gameDepthView) gameDepthView->Release();
    if (gameDepth) gameDepth->Release();
}

// For the fog, drawn later in the frame (at the sky step): the G-buffer's depth (t14) and the game's
// own constants (vc4, whose IR_Similarity_Data turns depth into distance), noted during the light pass.
bool g_noteFog = false;
ID3D11ShaderResourceView* g_fogDepth = nullptr;
ID3D11Buffer* g_gameConstants = nullptr;

void DropFogInputs() {
    if (g_fogDepth) g_fogDepth->Release();
    if (g_gameConstants) g_gameConstants->Release();
    g_fogDepth = nullptr;
    g_gameConstants = nullptr;
}

// Draws Guerrilla's fog over what the world has drawn into the game's current target, and puts the
// device context back as it was.
void DrawFog() {
    ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT] = {};
    ID3D11DepthStencilView* depthView = nullptr;
    g_context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, &depthView);
    ID3D11DepthStencilState* gameDepth = nullptr;
    UINT stencilRef = 0;
    g_context->OMGetDepthStencilState(&gameDepth, &stencilRef);
    D3D11_DEPTH_STENCIL_DESC gameDepthDesc = {};
    gameDepthDesc.DepthFunc = D3D11_COMPARISON_LESS;  // what no state means
    if (gameDepth) gameDepth->GetDesc(&gameDepthDesc);
    const bool reversed = gameDepthDesc.DepthFunc == D3D11_COMPARISON_GREATER || gameDepthDesc.DepthFunc == D3D11_COMPARISON_GREATER_EQUAL;
    D3D11_VIEWPORT viewport = {};
    UINT viewports = 1;
    g_context->RSGetViewports(&viewports, &viewport);
    D3D11_BUFFER_DESC gameConstantsDesc = {};
    g_gameConstants->GetDesc(&gameConstantsDesc);

    FogConstants constants = {};
    float colour[3], tint[3];
    TodColour(kFogColour, colour);
    ColourCorrection(tint);
    for (int i = 0; i < 3; ++i) constants.fogColour[i] = colour[i] * tint[i];
    FogNumbers(constants.fogNumbers);
    // The camera's space: x along its right, y its up, z its forward (its rows in the world).
    const float* axes[3] = {&g_cameraRotation[0], &g_cameraRotation[3], &g_cameraRotation[6]};
    for (int i = 0; i < 3; ++i) constants.upToward[i] = axes[i][1];
    constants.upToward[3] = reversed ? 0.0f : 1.0f;
    constants.targetSize[0] = viewport.Width;
    constants.targetSize[1] = viewport.Height;
    constants.targetSize[3] = tanf(g_cameraFov * 3.14159265f / 360.0f);
    constants.targetSize[2] = viewport.Height > 0 ? constants.targetSize[3] * viewport.Width / viewport.Height : 0;
    if (g_tellFog) {
        g_tellFog = false;
        rfa::Log("Fog at %.2f h: colour %.4f %.4f %.4f, fall-off %.4f, density at the camera %.5f, offset %.3f (Fog=%.3g)",
                 g_timeOfDay, constants.fogColour[0], constants.fogColour[1], constants.fogColour[2], constants.fogNumbers[0],
                 constants.fogNumbers[1], constants.fogNumbers[3], g_fog);
    }

    D3D11_MAPPED_SUBRESOURCE mapped;
    if (!targets[0] || viewport.Width <= 0 || viewport.Height <= 0 || gameConstantsDesc.ByteWidth < 13 * 16) {
        RFA_LOG_FIRST(1, "Fog: nothing to draw into, or the game's constants are not the ones expected (%u bytes); no fog",
                      gameConstantsDesc.ByteWidth);
    } else if (SUCCEEDED(g_context->Map(g_fogConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        memcpy(mapped.pData, &constants, sizeof(constants));
        g_context->Unmap(g_fogConstants, 0);
        SavedState saved;
        saved.Save();
        g_context->OMSetRenderTargets(1, &targets[0], nullptr);  // the depth is read, so it can't stay bound for testing
        g_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        g_context->IASetInputLayout(nullptr);
        g_context->VSSetShader(g_fogVertex, nullptr, 0);
        g_context->PSSetShader(g_fogPixel, nullptr, 0);
        g_context->GSSetShader(nullptr, nullptr, 0);
        ID3D11Buffer* buffers[2] = {g_fogConstants, g_gameConstants};
        g_context->PSSetConstantBuffers(0, 2, buffers);
        g_context->PSSetShaderResources(0, 1, &g_fogDepth);
        g_context->PSSetSamplers(0, 1, &g_fogSampler);
        const float noFactor[4] = {1, 1, 1, 1};
        g_context->OMSetBlendState(g_fogBlend, noFactor, 0xFFFFFFFF);
        g_context->OMSetDepthStencilState(g_fogDepthTest, 0);
        g_context->RSSetState(g_fogRaster);
        g_context->Draw(3, 0);
        saved.Restore();
        g_context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, depthView);
        g_context->OMSetDepthStencilState(gameDepth, stencilRef);
    }
    for (ID3D11RenderTargetView* target : targets)
        if (target) target->Release();
    if (depthView) depthView->Release();
    if (gameDepth) gameDepth->Release();
}

// Where the sun is on the picture (0 to 1 across, and down): false while it is down or behind the camera.
bool SunOnPicture(UINT width, UINT height, float (&at)[2]) {
    float toward[3];
    SunToward(toward);
    if (toward[1] < 0 || height == 0) return false;
    const float* axes[3] = {&g_cameraRotation[0], &g_cameraRotation[3], &g_cameraRotation[6]};  // right, up, forward
    float along[3];
    for (int a = 0; a < 3; ++a) along[a] = toward[0] * axes[a][0] + toward[1] * axes[a][1] + toward[2] * axes[a][2];
    if (along[2] < 0.01f) return false;
    const float tanUp = tanf(g_cameraFov * 3.14159265f / 360.0f), tanAcross = tanUp * width / height;
    at[0] = 0.5f + 0.5f * along[0] / (along[2] * tanAcross);
    at[1] = 0.5f - 0.5f * along[1] / (along[2] * tanUp);
    return true;
}

// Guerrilla's sun shafts for this frame (see kShaftShader), from the scene and the G-buffer's depth,
// into the first half-size target: the view to read them from, or none. Draws with the tone mapping's
// sampler, depth and rasterizer states (set by the caller, who puts the device context back).
ID3D11ShaderResourceView* DrawShafts(ID3D11ShaderResourceView* scene, UINT width, UINT height, const float (&sun)[2]) {
    if (!g_shaftVertex && !MakeShaftObjects()) {
        g_shaftsFailed = true;
        return nullptr;
    }
    const UINT halfWidth = (width + 1) / 2, halfHeight = (height + 1) / 2;
    if (!ShaftTargets(halfWidth, halfHeight)) {
        g_shaftsFailed = true;
        return nullptr;
    }
    ShaftConstants constants = {};
    constants.sun[0] = sun[0];
    constants.sun[1] = sun[1];
    constants.sun[2] = static_cast<float>(width) / height;
    constants.sun[3] = g_farDepth;
    constants.texel[0] = 1.0f / halfWidth;
    constants.texel[1] = 1.0f / halfHeight;
    constants.texel[2] = 1.0f / width;
    constants.texel[3] = 1.0f / height;
    constants.strength[0] = kShaftScale * 0.15625f;
    constants.strength[1] = kShaftBaseLum;
    constants.strength[2] = kShaftLumStep;
    constants.strength[3] = kShaftSunSize;
    constants.curve[0] = kShaftRadius;
    constants.curve[1] = kShaftCurveBase;
    constants.curve[2] = kShaftCurveExponent;
    constants.curve[3] = kShaftCurveShift;
    // The Gaussian, each way: e^(−x² ÷ 2rho²) for 0, 1 and 2 texels off, the five adding to 1, with the
    // multiplier shared between the two ways.
    float weights[3], total = 0;
    for (int i = 0; i < 3; ++i) {
        weights[i] = expf(-static_cast<float>(i * i) / (2 * kShaftBlurRho * kShaftBlurRho));
        total += i ? 2 * weights[i] : weights[i];
    }
    for (int i = 0; i < 3; ++i) constants.blur[i] = weights[i] / total * sqrtf(kShaftBlurMultiplier);
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (FAILED(g_context->Map(g_shaftConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) return nullptr;
    memcpy(mapped.pData, &constants, sizeof(constants));
    g_context->Unmap(g_shaftConstants, 0);
    RFA_LOG_FIRST(1, "Sun shafts: Guerrilla's, toward the sun in world \"%s\" (SunShafts=%.3g)", WorldName(), g_sunShafts);

    const float noFactor[4] = {1, 1, 1, 1};
    const D3D11_VIEWPORT half = {0, 0, static_cast<float>(halfWidth), static_cast<float>(halfHeight), 0, 1};
    g_context->RSSetViewports(1, &half);
    g_context->VSSetShader(g_shaftVertex, nullptr, 0);
    g_context->PSSetConstantBuffers(0, 1, &g_shaftConstants);
    g_context->OMSetBlendState(g_eyeBlend, noFactor, 0xFFFFFFFF);
    ID3D11ShaderResourceView* none[2] = {};
    struct Pass {
        ID3D11PixelShader* shader;
        ID3D11RenderTargetView* target;
        ID3D11ShaderResourceView* inputs[2];
    };
    const Pass passes[3] = {{g_shaftSource, g_shaftTarget[0], {scene, g_shaftDepth}},
                            {g_shaftBlur, g_shaftTarget[1], {g_shaftView[0], nullptr}},
                            {g_shaftRadial, g_shaftTarget[0], {g_shaftView[1], nullptr}}};
    for (const Pass& pass : passes) {
        g_context->PSSetShaderResources(0, 2, none);
        g_context->OMSetRenderTargets(1, &pass.target, nullptr);
        g_context->PSSetShaderResources(0, 2, pass.inputs);
        g_context->PSSetShader(pass.shader, nullptr, 0);
        g_context->Draw(3, 0);
    }
    g_context->PSSetShaderResources(0, 2, none);
    g_context->OMSetRenderTargets(0, nullptr, nullptr);
    return g_shaftView[0];
}

// Guerrilla's tone mapping, drawn over Armageddon's final picture (its target, 8-bit) from the scene it
// was made from (16-bit float, at exposure 1) and its bloom: the eye's brightness eased toward this
// frame's measure, the sun shafts, then the picture. Puts the device context back as it was.
int g_eyeNow = 0;           // which of the two textures holds the eye's brightness
bool g_eyeFresh = true;     // start the eye at the measure (the first frame, or after a gap)
LARGE_INTEGER g_toneLast = {};
ULONGLONG g_lastEyeLog = 0;
bool g_eyeReadPending = false;

// Guerrilla's particle layer (see ParticlePhase): this frame's particles, once the phase has drawn them.
ID3D11ShaderResourceView* g_particleView = nullptr;
bool g_particlesReady = false;

void DrawToneMapping(ID3D11RenderTargetView* target, ID3D11ShaderResourceView* scene, ID3D11ShaderResourceView* bloom, UINT width, UINT height) {
    // The frame's time, for the eye (a new start after a gap of more than a second).
    LARGE_INTEGER now, frequency;
    QueryPerformanceCounter(&now);
    QueryPerformanceFrequency(&frequency);
    float seconds = g_toneLast.QuadPart ? static_cast<float>(now.QuadPart - g_toneLast.QuadPart) / frequency.QuadPart : 0;
    g_toneLast = now;
    if (seconds > 1) g_eyeFresh = true;
    if (seconds > 0.25f) seconds = 0.25f;

    ToneConstants constants = {};
    constants.adapt[0] = g_eyeFresh ? 1.0f : 1 - exp2f(-0.87439f * seconds * TodNumber(kIrisRate));
    constants.adapt[1] = TodNumber(kLuminanceMin);
    constants.adapt[2] = TodNumber(kLuminanceMax);
    if (constants.adapt[2] < constants.adapt[1]) constants.adapt[2] = constants.adapt[1];
    constants.exposure[0] = TodNumber(kLumRange);
    constants.exposure[1] = TodNumber(kLumOffset);
    constants.exposure[2] = TodNumber(kEyeAdaptionBase);
    constants.exposure[3] = TodNumber(kEyeAdaptionAmount);
    constants.picture[0] = g_exposure;
    constants.picture[1] = TodNumber(kBloomAmount);
    constants.picture[2] = static_cast<float>(width);
    constants.picture[3] = static_cast<float>(height);
    float sunAt[2] = {};
    const bool shafts = SunShaftsOn() && g_shaftDepth && SunOnPicture(width, height, sunAt);
    constants.shafts[0] = shafts ? g_sunShafts : 0.0f;
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (FAILED(g_context->Map(g_toneConstants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) return;
    memcpy(mapped.pData, &constants, sizeof(constants));
    g_context->Unmap(g_toneConstants, 0);
    g_eyeFresh = false;
    const int next = 1 - g_eyeNow;

    SavedState saved;
    saved.Save();
    ID3D11RenderTargetView* gameTargets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT] = {};
    ID3D11DepthStencilView* gameDepthView = nullptr;
    g_context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, gameTargets, &gameDepthView);
    ID3D11DepthStencilState* gameDepth = nullptr;
    UINT stencilRef = 0;
    g_context->OMGetDepthStencilState(&gameDepth, &stencilRef);
    D3D11_VIEWPORT gameViewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE] = {};
    UINT viewportCount = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
    g_context->RSGetViewports(&viewportCount, gameViewports);

    const float noFactor[4] = {1, 1, 1, 1};
    g_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    g_context->IASetInputLayout(nullptr);
    g_context->VSSetShader(g_toneVertex, nullptr, 0);
    g_context->GSSetShader(nullptr, nullptr, 0);
    g_context->PSSetConstantBuffers(0, 1, &g_toneConstants);
    g_context->PSSetSamplers(0, 1, &g_toneSampler);
    g_context->OMSetDepthStencilState(g_toneDepth, 0);
    g_context->RSSetState(g_toneRaster);

    // The eye: this frame's measure, and last frame's brightness eased toward it.
    const D3D11_VIEWPORT one = {0, 0, 1, 1, 0, 1};
    g_context->OMSetRenderTargets(1, &g_eyeTarget[next], nullptr);
    g_context->RSSetViewports(1, &one);
    ID3D11ShaderResourceView* eyeInputs[3] = {scene, nullptr, g_eyeView[g_eyeNow]};
    g_context->PSSetShaderResources(0, 3, eyeInputs);
    g_context->PSSetShader(g_adaptPixel, nullptr, 0);
    g_context->OMSetBlendState(g_eyeBlend, noFactor, 0xFFFFFFFF);
    g_context->Draw(3, 0);

    // The sun shafts (with none drawn the shader reads nothing there).
    ID3D11ShaderResourceView* sunShafts = nullptr;
    if (shafts) {
        ID3D11ShaderResourceView* eyeNone[3] = {};
        g_context->PSSetShaderResources(0, 3, eyeNone);
        sunShafts = DrawShafts(scene, width, height, sunAt);
        g_context->VSSetShader(g_toneVertex, nullptr, 0);
        g_context->PSSetConstantBuffers(0, 1, &g_toneConstants);
    }

    // The picture, over Armageddon's.
    const D3D11_VIEWPORT whole = {0, 0, static_cast<float>(width), static_cast<float>(height), 0, 1};
    g_context->OMSetRenderTargets(1, &target, nullptr);
    g_context->RSSetViewports(1, &whole);
    ID3D11ShaderResourceView* pictureInputs[5] = {scene, bloom, g_eyeView[next], sunShafts, g_particlesReady ? g_particleView : nullptr};
    g_context->PSSetShaderResources(0, 5, pictureInputs);
    g_particlesReady = false;
    g_context->PSSetShader(g_tonePixel, nullptr, 0);
    g_context->OMSetBlendState(g_toneBlend, noFactor, 0xFFFFFFFF);
    g_context->Draw(3, 0);

    ID3D11ShaderResourceView* none[5] = {};
    g_context->PSSetShaderResources(0, 5, none);
    g_context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, gameTargets, gameDepthView);
    g_context->RSSetViewports(viewportCount, gameViewports);
    g_context->OMSetDepthStencilState(gameDepth, stencilRef);
    saved.Restore();
    for (ID3D11RenderTargetView* gameTarget : gameTargets)
        if (gameTarget) gameTarget->Release();
    if (gameDepthView) gameDepthView->Release();
    if (gameDepth) gameDepth->Release();
    g_eyeNow = next;

    // Every 10 seconds, the eye's brightness read back (a frame or two later, never waiting) for the log.
    if (g_eyeReadPending) {
        D3D11_MAPPED_SUBRESOURCE eye;
        if (SUCCEEDED(g_context->Map(g_eyeReadback, 0, D3D11_MAP_READ, D3D11_MAP_FLAG_DO_NOT_WAIT, &eye))) {
            const float adapted = *static_cast<const float*>(eye.pData);
            g_context->Unmap(g_eyeReadback, 0);
            g_eyeReadPending = false;
            float lit = 1 + constants.exposure[1] * (constants.exposure[2] + constants.exposure[3] * (adapted - constants.exposure[2]));
            if (lit < 0.1f) lit = 0.1f;
            static int told = 0;
            if (told++ < 60)
                rfa::Log("Tone mapping at %.2f h: the eye's brightness %.3f, exposure %.3f (range %.3g, offset %.3g, base %.3g, amount %.3g, "
                         "Exposure=%.3g)", g_timeOfDay, adapted, constants.exposure[0] / lit * g_exposure, constants.exposure[0],
                         constants.exposure[1], constants.exposure[2], constants.exposure[3], g_exposure);
        }
    } else if (GetTickCount64() - g_lastEyeLog >= 10000) {
        g_lastEyeLog = GetTickCount64();
        g_context->CopyResource(g_eyeReadback, g_eye[g_eyeNow]);
        g_eyeReadPending = true;
    }
}

// The texture behind a view (false if none or not a 2D texture).
bool TextureOf(ID3D11View* view, D3D11_TEXTURE2D_DESC& desc) {
    desc = {};
    if (!view) return false;
    ID3D11Resource* resource = nullptr;
    view->GetResource(&resource);
    ID3D11Texture2D* texture = nullptr;
    const bool ok = resource && SUCCEEDED(resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&texture)));
    if (texture) {
        texture->GetDesc(&desc);
        texture->Release();
    }
    if (resource) resource->Release();
    return ok;
}

// Right after Armageddon's last composite step in our worlds: its target and the scene (t0) and bloom
// (t5) it drew from, still bound, then Guerrilla's tone mapping over its picture.
void ToneMapAfterComposite() {
    if (!GuerrillaToneMapping() || !g_context || !g_device) return;
    ID3D11RenderTargetView* target = nullptr;
    g_context->OMGetRenderTargets(1, &target, nullptr);
    ID3D11ShaderResourceView* scene = nullptr;
    ID3D11ShaderResourceView* bloom = nullptr;
    g_context->PSGetShaderResources(0, 1, &scene);
    g_context->PSGetShaderResources(5, 1, &bloom);
    D3D11_TEXTURE2D_DESC targetDesc, sceneDesc, bloomDesc;
    if (TextureOf(target, targetDesc) && TextureOf(scene, sceneDesc) && TextureOf(bloom, bloomDesc) &&
        sceneDesc.Format == DXGI_FORMAT_R16G16B16A16_FLOAT && sceneDesc.Width == targetDesc.Width && sceneDesc.Height == targetDesc.Height) {
        if (!g_toneVertex && !MakeToneMappingObjects()) {
            g_toneMappingFailed = true;
        } else {
            RFA_LOG_FIRST(1, "Tone mapping: Guerrilla's, over the final picture in world \"%s\" (%u x %u)", WorldName(), targetDesc.Width,
                          targetDesc.Height);
            DrawToneMapping(target, scene, bloom, targetDesc.Width, targetDesc.Height);
        }
    } else {
        RFA_LOG_FIRST(1, "Tone mapping: the last composite step left no picture, scene and bloom of the kinds expected; Armageddon's own stays");
    }
    for (ID3D11View* view : {static_cast<ID3D11View*>(target), static_cast<ID3D11View*>(scene), static_cast<ID3D11View*>(bloom)})
        if (view) view->Release();
    if (g_shaftDepth) g_shaftDepth->Release();
    g_shaftDepth = nullptr;
}

// During the light pass in our worlds, the first time its check runs (all is bound by then), the
// mod notes the light buffer it draws into and the G-buffer's normals (and, for the fog, its depth and
// the game's constants); once the pass is done it adds its light there.
bool g_inLightPass = false;
ID3D11RenderTargetView* g_lightBuffer = nullptr;
ID3D11ShaderResourceView* g_lightNormals = nullptr;

bool __cdecl LightCheck() {
    if (g_inLightPass && !g_lightBuffer) {
        g_context->OMGetRenderTargets(1, &g_lightBuffer, nullptr);
        g_context->PSGetShaderResources(13, 1, &g_lightNormals);
    }
    if (g_noteFog && !g_fogDepth) {
        g_context->PSGetShaderResources(14, 1, &g_fogDepth);
        g_context->PSGetConstantBuffers(4, 1, &g_gameConstants);
    }
    return g_lightCheck();
}

// The light pass draws each lamp with 0xEF67D0 (the scene renderer in ECX; the lamp's entry in the
// frame's list of lamps, its light at +0x10, and the entry whose shadow map was just rendered into the
// shared map): the shadowed spot lamp's shader
// (ir_light_spot) then has the shadow map at t0 and the lamp's constants (Shadow_map_projTM, ...) at
// b0, the G-buffer's normals and depth at t13 and t14, the game's constants at b4. After the sun's lamp
// is drawn, while they are all still bound, the mod draws the sun's light through that shadow map.
constexpr DWORD kDrawLamp = 0xEF67D0;
constexpr size_t kDrawLampPrologue = 6;  // push ebx / mov ebx,esp / sub esp,8
using DrawLampFn = void(__fastcall*)(void* renderer, void* unused, BYTE* entry, BYTE* shadowEntry);
DrawLampFn g_drawLamp;
constexpr size_t kEntryLight = 0x10;
bool g_sunDrawn = false;  // the sun's light drawn through its shadow map in this light pass

// Once: what the sun's lamp was drawn with (its shadow map, and its constants read back from the card).
void TellSunLamp(ID3D11ShaderResourceView* map, ID3D11Buffer* lamp) {
    ID3D11Resource* resource = nullptr;
    map->GetResource(&resource);
    ID3D11Texture2D* texture = nullptr;
    D3D11_TEXTURE2D_DESC desc = {};
    if (resource && SUCCEEDED(resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&texture)))) {
        texture->GetDesc(&desc);
        texture->Release();
    }
    if (resource) resource->Release();
    D3D11_SHADER_RESOURCE_VIEW_DESC view = {};
    map->GetDesc(&view);
    rfa::Log("Sun's shadows: the sun's lamp's shadow map %u x %u, format %d (viewed as %d)", desc.Width, desc.Height,
             static_cast<int>(desc.Format), static_cast<int>(view.Format));
    D3D11_BUFFER_DESC bufferDesc = {};
    lamp->GetDesc(&bufferDesc);
    D3D11_BUFFER_DESC stagingDesc = {};
    stagingDesc.ByteWidth = bufferDesc.ByteWidth;
    stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Buffer* staging = nullptr;
    if (FAILED(g_device->CreateBuffer(&stagingDesc, nullptr, &staging))) return;
    g_context->CopyResource(staging, lamp);
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(g_context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped))) {
        const float* f = static_cast<const float*>(mapped.pData);
        const char* names[11] = {"light position", "inverse projection", "", "", "", "spot direction",
                                 "shadow map projection", "", "", "", "shadow fade"};
        for (UINT row = 0; row < 11 && row * 16 + 16 <= bufferDesc.ByteWidth; ++row)
            rfa::Log("Sun's shadows: the lamp's constants [%u] %g %g %g %g %s", row, f[row * 4], f[row * 4 + 1], f[row * 4 + 2], f[row * 4 + 3], names[row]);
        g_context->Unmap(staging, 0);
    }
    staging->Release();
}

// Draws the sun's light through the sun's lamp's shadow map with what the game has bound for the lamp
// (its map at t0 and constants at b0, the G-buffer's normals and depth at t13 and t14, the game's
// constants at b4, the light buffer as the target); false, and the sun left to the pass's end, when
// any of them isn't bound (logged once, with which).
bool DrawSunThroughLamp() {
    ID3D11RenderTargetView* buffer = nullptr;
    g_context->OMGetRenderTargets(1, &buffer, nullptr);
    ID3D11ShaderResourceView* map = nullptr;
    ID3D11ShaderResourceView* normals = nullptr;
    ID3D11ShaderResourceView* depth = nullptr;
    g_context->PSGetShaderResources(0, 1, &map);
    g_context->PSGetShaderResources(13, 1, &normals);
    g_context->PSGetShaderResources(14, 1, &depth);
    ID3D11Buffer* lamp = nullptr;
    ID3D11Buffer* game = nullptr;
    g_context->PSGetConstantBuffers(0, 1, &lamp);
    g_context->PSGetConstantBuffers(4, 1, &game);
    const bool all = buffer && map && normals && depth && lamp && game;
    if (all) {
        static bool told = false;
        if (!told) {
            told = true;
            TellSunLamp(map, lamp);
            rfa::Log("Sun's shadows: the sun's light drawn through the follow player light's shadow map in world \"%s\"", WorldName());
        }
        const SunShadowInputs shadow = {map, depth, game, lamp};
        DrawLight(buffer, normals, false, true, &shadow);
        g_sunDrawn = true;
    } else {
        RFA_LOG_FIRST(1, "Sun's shadows: the follow player light drawn without everything bound (the light buffer %d, its shadow map %d, "
                         "normals %d, depth %d, its constants %d, the game's %d; this is logged once); the sun lights without shadows",
                      buffer != nullptr, map != nullptr, normals != nullptr, depth != nullptr, lamp != nullptr, game != nullptr);
    }
    for (IUnknown* held : {static_cast<IUnknown*>(buffer), static_cast<IUnknown*>(map), static_cast<IUnknown*>(normals),
                           static_cast<IUnknown*>(depth), static_cast<IUnknown*>(lamp), static_cast<IUnknown*>(game)})
        if (held) held->Release();
    return all;
}

// Sets a texture slot of the renderer's draw state (0xC2F490: the state in ECX, the slot, the texture,
// or null to clear it). The lamp's drawing clears its shadow map's slot (0xEF73ED) right after it has
// drawn the lamp (0xEF72CE), the shared map being drawn into next for the next lamp. While the sun's
// lamp is drawn, the mod draws the sun's light just before the first clearing, everything still bound.
constexpr DWORD kSetTexture = 0xC2F490;
constexpr size_t kSetTexturePrologue = 7;  // push ebp / mov ebp,esp / push ecx / mov [ebp-4],ecx
using SetTextureFn = void(__fastcall*)(void* state, void* unused, int slot, void* texture);
SetTextureFn g_setTexture;
bool g_drawingSunLamp = false;  // the sun's lamp being drawn, its light not yet drawn through its map

void __fastcall SetTexture(void* state, void* unused, int slot, void* texture) {
    if (g_drawingSunLamp && !texture) {
        g_drawingSunLamp = false;
        DrawSunThroughLamp();
    }
    g_setTexture(state, unused, slot, texture);
}

void __fastcall DrawLamp(void* renderer, void* unused, BYTE* entry, BYTE* shadowEntry) {
    const bool sunLamp = g_inLightPass && entry && g_sunLight && *reinterpret_cast<BYTE**>(entry + kEntryLight) == g_sunLight &&
                         !g_sunDrawn && g_lightVertex && g_setTexture;
    g_drawingSunLamp = sunLamp;
    g_drawLamp(renderer, unused, entry, shadowEntry);
    if (g_drawingSunLamp) {  // the lamp drawn without clearing a texture: try once more after it
        g_drawingSunLamp = false;
        DrawSunThroughLamp();
    }
}

void __fastcall LightPass(void* renderer, void* unused) {
    NoteRenderWarnings();
    const bool ours = g_context && InOurWorld();
    g_inLightPass = ours && g_lightOn && !g_lightFailed;
    if (g_inLightPass && !g_lightVertex && !MakeLightObjects()) {
        g_lightFailed = true;
        g_inLightPass = false;
    }
    g_sunDrawn = false;
    DropFogInputs();
    if (g_shaftDepth) g_shaftDepth->Release();
    g_shaftDepth = nullptr;
    g_noteFog = ours && ((g_fog > 0 && !g_fogFailed) || SunShaftsOn());
    g_lightPass(renderer, unused);
    g_noteFog = false;
    if (g_inLightPass) {
        g_inLightPass = false;
        if (g_lightBuffer && g_lightNormals) {
            RFA_LOG_FIRST(1, "Light: lighting world \"%s\" with the sky's light and the sun", WorldName());
            DrawLight(g_lightBuffer, g_lightNormals, true, !g_sunDrawn, nullptr);
        } else {
            RFA_LOG_FIRST(1, "Light: a light pass showed no light buffer or normals (this is logged once); no light added to it");
        }
    }
    if (g_lightBuffer) g_lightBuffer->Release();
    if (g_lightNormals) g_lightNormals->Release();
    g_lightBuffer = nullptr;
    g_lightNormals = nullptr;
}

// Replaces the scene renderer's sky step: the game's own, then, in our worlds, the clock,
// Guerrilla's fog over what the world has drawn, and Guerrilla's sky behind it.
void __fastcall RenderSkybox(void* renderer, void* unused) {
    g_renderSkybox(renderer, unused);
    RFA_LOG_FIRST(1, "Sky: the game's sky step runs (world \"%s\")", WorldName());
    const bool ours = InOurWorld();
    if (ours) {
        CheckSettings();
        TellAmbient();
    }
    RunClock(ours);
    RunStreaming(ours);
    if (ours && g_fog > 0 && !g_fogFailed && g_context) {
        if (!g_fogDepth || !g_gameConstants) {
            RFA_LOG_FIRST(1, "Fog: the light pass showed no depth or constants (this is logged once); no fog");
        } else if (!g_fogVertex && !MakeFogObjects()) {
            g_fogFailed = true;
        } else {
            RFA_LOG_FIRST(1, "Fog: drawing Guerrilla's fog in world \"%s\"", WorldName());
            DrawFog();
        }
    }
    // The depth stays for the sun shafts, drawn with the tone mapping at the end of the frame.
    if (ours && SunShaftsOn() && g_fogDepth && !g_shaftDepth) {
        g_shaftDepth = g_fogDepth;
        g_shaftDepth->AddRef();
    }
    DropFogInputs();
    if (!ours || !g_skyOn || g_drawingFailed || !g_context) return;
    if (!g_skyVertex) {
        if (!MakeSkyObjects()) {
            g_drawingFailed = true;
            return;
        }
        if (g_cloudsOn) MakeCloudObjects();
    }
    RFA_LOG_FIRST(1, "Sky: drawing Guerrilla's sky in world \"%s\"", WorldName());
    DrawSky();
}

// Replaces the compositor's tone-mapping constants: in our worlds with Guerrilla's tone mapping, the
// exposure range the game uploads is held at 1 for the call (so the picture holds Guerrilla's own
// values), and the renderer's own put back after.
void __fastcall ToneMappingConstants(void* renderer, void* unused) {
    float* exposure = reinterpret_cast<float*>(static_cast<BYTE*>(renderer) + 0xAD8);  // desired brightness, -, min, max
    const bool ours = InOurWorld();
    if (ours)
        RFA_LOG_FIRST(1, "Exposure: the game's own in world \"%s\": desired brightness %.3g, held between %.3g and %.3g", WorldName(),
                      exposure[0], exposure[2], exposure[3]);
    if (!ours || !GuerrillaToneMapping()) {
        g_toneMappingConstants(renderer, unused);
        return;
    }
    const float before[2] = {exposure[2], exposure[3]};
    exposure[2] = exposure[3] = 1;
    g_toneMappingConstants(renderer, unused);
    exposure[2] = before[0];
    exposure[3] = before[1];
}

// A view's texture, for the log: its format, size and mip levels.
void DescribeView(ID3D11View* view, char* out, size_t size) {
    ID3D11Resource* resource = nullptr;
    view->GetResource(&resource);
    ID3D11Texture2D* texture = nullptr;
    D3D11_TEXTURE2D_DESC desc = {};
    if (resource && SUCCEEDED(resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&texture)))) {
        texture->GetDesc(&desc);
        texture->Release();
    }
    if (resource) resource->Release();
    sprintf_s(out, size, "%s (format %d), %u x %u, %u mip levels", FormatName(desc.Format), static_cast<int>(desc.Format), desc.Width,
              desc.Height, desc.MipLevels);
}

// Logs, once, what the compositor's last step drew into and what it left bound to draw from (the
// picture at t0, the bloom at t5, ...), to place Guerrilla's own tone mapping there.
void TellComposite(int which) {
    static bool told = false;
    if (told || !g_context) return;
    told = true;
    char text[160];
    ID3D11RenderTargetView* target = nullptr;
    g_context->OMGetRenderTargets(1, &target, nullptr);
    if (target) {
        DescribeView(target, text, sizeof(text));
        target->Release();
    } else {
        sprintf_s(text, "none");
    }
    rfa::Log("Composite: the last step (0x%X) drew into %s", kFinalComposite[which], text);
    ID3D11ShaderResourceView* views[8] = {};
    g_context->PSGetShaderResources(0, 8, views);
    for (int slot = 0; slot < 8; ++slot) {
        if (!views[slot]) continue;
        DescribeView(views[slot], text, sizeof(text));
        rfa::Log("Composite: t%d left bound: %s", slot, text);
        views[slot]->Release();
    }
}

void __fastcall FinalComposite0(void* compositor, void* unused, int flag) {
    g_finalComposite[0](compositor, unused, flag);
    if (!InOurWorld()) return;
    NoteRenderWarnings();
    TellComposite(0);
    ToneMapAfterComposite();
}

void __fastcall FinalComposite1(void* compositor, void* unused, int flag) {
    g_finalComposite[1](compositor, unused, flag);
    if (!InOurWorld()) return;
    NoteRenderWarnings();
    TellComposite(1);
    ToneMapAfterComposite();
}

// ---- The game's device

using CreateDeviceFn = HRESULT(WINAPI*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT, const D3D_FEATURE_LEVEL*, UINT, UINT,
                                        ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);
CreateDeviceFn g_createDevice;

// Notes the device the game makes (the last one, if it makes several), and keeps it alive.
// ---- Guerrilla's particle layer

// Guerrilla draws its particles into a buffer of their own (premultiplied colour, and in alpha how
// much they cover) and lays it over the picture only in its final tone mapping (rl_hdr pass 21):
// picture = 1 − (1 − particles) · e^(−exposure · (scene · (1 − cover) + bloom)). So particles keep
// the colour they are drawn in, dim only what is behind them, and never feed the bloom or the eye.
// Armageddon draws them into the lit scene before its bloom, where Guerrilla's colours (made for
// Guerrilla's way) burn out: an EDF screen's glow added to its lit screen turns white and blooms.
//
// Armageddon draws them all in one phase (0xC35FC0, thiscall, one argument, ret 4): the half-res
// ones (0xC35DD0, laid into the scene at its end), the full-resolution ones (0xC35040), then a last
// step (0xC35CF0, none) for the heat distortion, which reads the scene. All of them draw into the
// scene (half floats) with premultiplied blending (colour one / one − source alpha, alpha the same,
// all four written), so a scene cleared to nothing ends the phase holding exactly Guerrilla's
// particle buffer. In our worlds with Guerrilla's tone mapping (Particles=1): at the phase's start
// the scene is copied aside and cleared; at the last step's start (or the phase's end) what it then
// holds is copied into our layer and the scene put back, so the distortion, the bloom and the eye see
// the scene without particles; the tone mapping lays the layer over it. Otherwise the game draws its
// particles its own way.
constexpr DWORD kParticlePhase = 0xC35FC0, kParticleLast = 0xC35CF0;
constexpr size_t kParticlePhasePrologue = 5;  // push ebp / mov ebp,esp / push −1 (both)
using ParticlePhaseFn = void(__fastcall*)(void* renderer, void* unused, int value);
using ParticleLastFn = void(__fastcall*)(void* renderer, void* unused);
ParticlePhaseFn g_particlePhase;
ParticleLastFn g_particleLast;
ID3D11Texture2D* g_particleSceneCopy = nullptr;  // the scene as the phase found it
ID3D11Texture2D* g_particleTaken = nullptr;      // what the phase drew, as drawn (multisampled if the scene is)
ID3D11Texture2D* g_particleResolved = nullptr;   // ... and resolved, when it is
ID3D11Texture2D* g_particleScene = nullptr;      // the scene being drawn into, while the phase runs
D3D11_TEXTURE2D_DESC g_particleDesc = {};
bool g_particleLayerFailed = false;

bool ParticlesWanted() {
    return g_particles && !g_particleLayerFailed && GuerrillaToneMapping() && g_toneVertex && g_context && g_device && InOurWorld();
}

// The layer's textures for a scene of this description (made again when it changes).
bool MakeParticleTextures(const D3D11_TEXTURE2D_DESC& scene) {
    if (g_particleSceneCopy && scene.Width == g_particleDesc.Width && scene.Height == g_particleDesc.Height &&
        scene.Format == g_particleDesc.Format && scene.SampleDesc.Count == g_particleDesc.SampleDesc.Count &&
        scene.SampleDesc.Quality == g_particleDesc.SampleDesc.Quality && scene.MipLevels == g_particleDesc.MipLevels)
        return true;
    for (ID3D11Texture2D** texture : {&g_particleSceneCopy, &g_particleTaken, &g_particleResolved}) {
        if (*texture) (*texture)->Release();
        *texture = nullptr;
    }
    if (g_particleView) g_particleView->Release();
    g_particleView = nullptr;
    g_particleDesc = scene;
    D3D11_TEXTURE2D_DESC copy = scene;
    copy.Usage = D3D11_USAGE_DEFAULT;
    copy.BindFlags = 0;
    copy.CPUAccessFlags = 0;
    copy.MiscFlags = 0;
    const bool multisampled = scene.SampleDesc.Count > 1;
    D3D11_TEXTURE2D_DESC taken = copy;
    if (!multisampled) taken.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_TEXTURE2D_DESC resolved = copy;
    resolved.SampleDesc = {1, 0};
    resolved.MipLevels = 1;
    resolved.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    bool ok = SUCCEEDED(g_device->CreateTexture2D(&copy, nullptr, &g_particleSceneCopy)) &&
              SUCCEEDED(g_device->CreateTexture2D(&taken, nullptr, &g_particleTaken)) &&
              (!multisampled || SUCCEEDED(g_device->CreateTexture2D(&resolved, nullptr, &g_particleResolved)));
    ok = ok && SUCCEEDED(g_device->CreateShaderResourceView(multisampled ? g_particleResolved : g_particleTaken, nullptr, &g_particleView));
    if (!ok) {
        g_particleLayerFailed = true;
        rfa::Log("Particles: could not make the particle layer's textures; the game draws its particles its own way");
        return false;
    }
    rfa::Log("Particles: drawn into a layer of their own and laid over the picture as Guerrilla does (%u x %u, format %d, %u samples)",
             scene.Width, scene.Height, static_cast<int>(scene.Format), scene.SampleDesc.Count);
    return true;
}

// At the phase's start: the scene being drawn into, copied aside and cleared.
void SetSceneAside() {
    if (g_particleScene || !ParticlesWanted()) return;
    ID3D11RenderTargetView* target = nullptr;
    g_context->OMGetRenderTargets(1, &target, nullptr);
    if (!target) return;
    D3D11_TEXTURE2D_DESC desc;
    ID3D11Resource* resource = nullptr;
    target->GetResource(&resource);
    ID3D11Texture2D* scene = nullptr;
    if (resource && TextureOf(target, desc) && desc.Format == DXGI_FORMAT_R16G16B16A16_FLOAT &&
        SUCCEEDED(resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&scene))) && MakeParticleTextures(desc)) {
        if (g_particlesReady) RFA_LOG_FIRST(3, "Particles: last frame's layer was never laid in (the tone mapping didn't draw)");
        g_context->CopyResource(g_particleSceneCopy, scene);
        const float nothing[4] = {0, 0, 0, 0};
        g_context->ClearRenderTargetView(target, nothing);
        g_particleScene = scene;  // (the reference kept until the scene is put back)
        scene = nullptr;
    } else {
        RFA_LOG_FIRST(1, "Particles: the scene the particles draw into isn't a half-float target; the game draws them its own way there");
    }
    if (scene) scene->Release();
    if (resource) resource->Release();
    target->Release();
}

// Once the particles are drawn: what the scene holds is the layer, and the scene is put back.
void TakeParticles() {
    if (!g_particleScene) return;
    g_context->CopyResource(g_particleTaken, g_particleScene);
    if (g_particleResolved) g_context->ResolveSubresource(g_particleResolved, 0, g_particleTaken, 0, g_particleDesc.Format);
    g_context->CopyResource(g_particleScene, g_particleSceneCopy);
    g_particleScene->Release();
    g_particleScene = nullptr;
    g_particlesReady = true;
}

void __fastcall ParticlePhase(void* renderer, void* unused, int value) {
    SetSceneAside();
    g_particlePhase(renderer, unused, value);
    TakeParticles();
}

void __fastcall ParticleLast(void* renderer, void* unused) {
    TakeParticles();
    g_particleLast(renderer, unused);
}

// ---- Guerrilla's dust

// Guerrilla blows dust round the camera with a system of its own (dust_update, every frame), which
// Armageddon has dropped (its dust_system_init is empty) though it keeps all it rests on: the weather's
// wind, the effects' wind and expression variables, and the effect player. Each frame:
//   - how much of the sky round the camera is blocked (gameaudio_update_listener_los): one 50 m ray a
//     frame, in turn along 26 directions, each blocked if it hits the world within 20 m; the share
//     blocked, each direction weighed (up 2, up and out 1, level 0.5, down 0.1);
//   - the effects' variable wind_los: the wind's speed in m/s times the share of the sky left open
//     (so none indoors), set for every effect (the camera effect's expressions show its haze, its dust
//     and then its grit as wind_los grows from 0 to 45);
//   - the camera effect (weather.xtbl's first Dust_effects, env_dust_camera_a; the world builder adds
//     it as rfg_env_dust_camera_a) kept playing at the camera, turned as the effects' wind blows and
//     moved upwind by up to a quarter of its radius (Dust_max_offset) as the wind nears its top speed.
// In our worlds the mod does the same after the game's own effects update (0x4F8E10, cdecl, the frame's
// seconds; it turns the weather's wind into the effects' wind and ages the effects). Not done yet: Guerrilla's gusts of dust on the ground
// round the player (each ground material's own) and the fading of the dust right by buildings.
constexpr DWORD kEffectsUpdate = 0x4F8E10;
constexpr size_t kEffectsUpdatePrologue = 6;  // push ebp / mov ebp,esp / sub esp,5Ch
using EffectsUpdateFn = void(__cdecl*)(float seconds);
EffectsUpdateFn g_effectsUpdate;

// effects.xtbl's entries by name (0x4EF370: the entry's number, or −1) and by number (0x4EF320: the
// entry; +0x44 its visual's number); playing one (0x4EB460: the entry's number, where, the turn, and
// seven more, given as Armageddon's own camera effects give them), which returns the effect object's
// handle or −1. An object by its handle (the world, 0x1870630, in ECX; 0x4D4360), or null: an effect
// object's place at +0x04, its turn (rows right, up, forward) at +0x10, flags at +0x57 (8: moved, to be
// drawn there; as Guerrilla's set_render_flags(4)). As Armageddon's camera effects do once one plays,
// two of its flags cleared (0x7B20A0(false), 0x4F8DA0(false)); stopping it 0x4F7AA0(1), as Guerrilla's
// dust does.
using EffectNumberFn = int(__cdecl*)(const char* name);
auto const EffectNumber = reinterpret_cast<EffectNumberFn>(0x4EF370);
using EffectEntryFn = BYTE*(__cdecl*)(int number);
auto const EffectEntry = reinterpret_cast<EffectEntryFn>(0x4EF320);
using EffectsPlayFn = int(__cdecl*)(int effect, const float* where, const float* turn, bool a4, int a5, int a6, int a7, bool a8, DWORD a9, DWORD a10);
auto const EffectsPlay = reinterpret_cast<EffectsPlayFn>(0x4EB460);
const float* const kIdentityTurn = reinterpret_cast<const float*>(0x290CB30);
using ObjectFromHandleFn = BYTE*(__fastcall*)(void* world, void* unused, int handle);
auto const ObjectFromHandle = reinterpret_cast<ObjectFromHandleFn>(0x4D4360);
using ObjectFlagFn = void(__fastcall*)(BYTE* object, void* unused, bool on);
auto const ClearObjectFlagA = reinterpret_cast<ObjectFlagFn>(0x7B20A0);
auto const ClearObjectFlagB = reinterpret_cast<ObjectFlagFn>(0x4F8DA0);
using StopEffectFn = void(__fastcall*)(BYTE* object, void* unused, int style);
auto const StopEffect = reinterpret_cast<StopEffectFn>(0x4F7AA0);
constexpr size_t kObjectPlace = 0x04, kObjectTurn = 0x10, kObjectFlags = 0x57;
void* const kWorld = reinterpret_cast<void*>(0x1870630);

// The weather's wind (0x6FB620: its speed in mph and its direction in degrees; started at the middle
// of the zone's wind_min_speed and wind_max_speed, 50 and 80 in all of Guerrilla's zones), its top
// speed (0x10FF83C, the zone's wind_max_speed), and the effects' wind filter ([0x26BF284]: its turn at
// +0x50, which the game's effects update sets from the wind's direction; Guerrilla's at +0x54).
using WindFn = void(__cdecl*)(float* speed, float* direction);
auto const Wind = reinterpret_cast<WindFn>(0x6FB620);
auto& g_windTopSpeed = *reinterpret_cast<float*>(0x10FF83C);
BYTE* const* const kWindFilter = reinterpret_cast<BYTE* const*>(0x26BF284);
constexpr size_t kWindFilterTurn = 0x50;

// The effects' expression variables (0x124FAEC: each a pointer to its record, its name's hash at +0x0C;
// number 4 is wind_los, as in Guerrilla) and setting one for every effect (0xB63140: the name's hash and
// the value; Guerrilla's vfx_expressions_set_global_variable).
BYTE* const* const kExpressionVariables = reinterpret_cast<BYTE* const*>(0x124FAEC);
constexpr int kWindLos = 4;
using SetEffectVariableFn = void(__cdecl*)(DWORD nameHash, float value);
auto const SetEffectVariable = reinterpret_cast<SetEffectVariableFn>(0xB63140);

// A ray through the world (0x7993C0, cdecl: the ray, its hits; true if it hit), as Guerrilla's
// havok_raycast with one more word: from, to, its radius, +0x1C −1 (an object it passes through, it
// seems), +0x20 and +0x24 0, the collision filter +0x28 (0x1975F00: layer 28, Guerrilla's
// COLLIDE_RAY_WORLD_ONLY), which hits +0x2C (0: the nearest), +0x30 0; 0x424AD0 sets those defaults
// (the game's own filter, layer 26). Its hits come back in a Havok array of Guerrilla's hit_info (0x38
// bytes: where it hit, ..., +0x24 how far along the ray), here one of the mod's own the game may fill
// but not free (its capacity's top bit set).
struct Ray {
    float from[3], to[3];
    float radius;
    int passThrough;
    DWORD unknown20, unknown24, filter, nearest, unknown30;
};
static_assert(sizeof(Ray) == 0x34, "the game's ray input");
struct RayHit {
    float at[3], middle[3], normal[3];
    float along;
    DWORD rest[4];
};
static_assert(sizeof(RayHit) == 0x38, "the game's ray hit");
struct RayHits {
    RayHit* hits;
    int count;
    DWORD capacityAndFlags;
};
using RayInitFn = void(__fastcall*)(Ray* ray, void* unused);
auto const RayInit = reinterpret_cast<RayInitFn>(0x424AD0);
using CastRayFn = bool(__cdecl*)(const Ray* ray, RayHits* hits);
auto const CastRay = reinterpret_cast<CastRayFn>(0x7993C0);
auto& g_worldOnlyRays = *reinterpret_cast<DWORD*>(0x1975F00);

// Guerrilla's 26 directions round the listener, in its order, and their weights.
struct SkyDirection { float x, y, z, weight; };
constexpr SkyDirection kSkyDirections[26] = {
    {0, 1, 0, 2},           {0, -1, 0, 0.1f},          {-1, 0.707f, 0, 1},         {1, -0.353f, 0, 0.1f},
    {0, 0, 1, 0.5f},        {0, 0, -1, 0.5f},          {1, 0.707f, 0, 1},          {-1, -0.353f, 0, 0.1f},
    {0, 0.707f, -1, 1},     {0, -0.353f, 1, 0.1f},     {-1, 0, 0, 0.5f},           {1, 0, 0, 0.5f},
    {0, -0.353f, -1, 0.1f}, {0, 0.707f, 1, 1},         {-0.707f, 0.707f, 0.707f, 1}, {0.707f, -0.353f, -0.707f, 0.1f},
    {0.707f, 0.707f, 0.707f, 1}, {-0.707f, -0.353f, -0.707f, 0.1f}, {-0.707f, 0, 0.707f, 0.5f}, {0.707f, 0, -0.707f, 0.5f},
    {-0.707f, 0.707f, -0.707f, 1}, {0.707f, -0.353f, 0.707f, 0.1f}, {0.707f, 0.707f, -0.707f, 1}, {-0.707f, -0.353f, 0.707f, 0.1f},
    {0.707f, 0, 0.707f, 0.5f},   {-0.707f, 0, -0.707f, 0.5f},
};
constexpr float kSkyRayLength = 50, kSkyBlockedWithin = 20;
constexpr float kMphToMetresPerSecond = 0.44704f;
constexpr float kDustMaxOffset = 0.25f;  // weather.xtbl's Dust_max_offset, 25 (%)
constexpr const char* kDustEffect = "rfg_env_dust_camera_a";

bool g_skyBlocked[26] = {};
int g_skyNext = 0;
int g_dustHandle = -1;
int g_dustEffect = -2;  // −2: not looked up yet; −1: the world has none
bool g_dustLogged = false;

// One more of the 26 rays; the share of the sky blocked round the camera.
float SkyBlocked() {
    const SkyDirection& direction = kSkyDirections[g_skyNext];
    Ray ray;
    RayInit(&ray, nullptr);
    for (int i = 0; i < 3; ++i) ray.from[i] = g_cameraPosition[i];
    ray.to[0] = g_cameraPosition[0] + direction.x * kSkyRayLength;
    ray.to[1] = g_cameraPosition[1] + direction.y * kSkyRayLength;
    ray.to[2] = g_cameraPosition[2] + direction.z * kSkyRayLength;
    ray.radius = 0.1f;
    ray.filter = g_worldOnlyRays;
    ray.nearest = 0;
    RayHit hits[4] = {};
    RayHits found = {hits, 0, 0x80000000u | 4};
    const bool hit = CastRay(&ray, &found) && found.count > 0 && found.hits;
    g_skyBlocked[g_skyNext] = hit && found.hits[0].along * kSkyRayLength < kSkyBlockedWithin;
    g_skyNext = (g_skyNext + 1) % 26;
    float blocked = 0, all = 0;
    for (int i = 0; i < 26; ++i) {
        all += kSkyDirections[i].weight;
        if (g_skyBlocked[i]) blocked += kSkyDirections[i].weight;
    }
    return blocked / all;
}

void StopDust() {
    if (g_dustHandle == -1) return;
    if (BYTE* effect = ObjectFromHandle(kWorld, nullptr, g_dustHandle)) StopEffect(effect, nullptr, 1);
    g_dustHandle = -1;
}

void DustUpdate() {
    if (!InOurWorld()) {
        g_dustHandle = -1;  // (gone with the world's objects)
        g_dustLogged = false;
        return;
    }
    if (!g_dust) {
        StopDust();
        return;
    }
    float speed = 0, direction = 0;
    Wind(&speed, &direction);
    const float open = 1 - SkyBlocked();
    const float windLos = (open > 0 ? open : 0) * speed * kMphToMetresPerSecond;
    if (const BYTE* variable = kExpressionVariables[kWindLos]) SetEffectVariable(*reinterpret_cast<const DWORD*>(variable + 0x0C), windLos);

    if (g_dustEffect == -2) {
        g_dustEffect = EffectNumber(kDustEffect);
        if (g_dustEffect < 0) rfa::Log("Dust: the game has no effect %s (build the world again with this version's GuerrillaWorld.exe); no dust", kDustEffect);
    }
    if (g_dustEffect < 0) return;
    BYTE* effect = g_dustHandle != -1 ? ObjectFromHandle(kWorld, nullptr, g_dustHandle) : nullptr;
    if (!effect) {
        g_dustHandle = EffectsPlay(g_dustEffect, g_cameraPosition, kIdentityTurn, true, -1, -1, -1, false, 0, 0);
        effect = g_dustHandle != -1 ? ObjectFromHandle(kWorld, nullptr, g_dustHandle) : nullptr;
        if (!effect) {
            RFA_LOG_FIRST(3, "Dust: %s didn't play (handle %d)", kDustEffect, g_dustHandle);
            g_dustHandle = -1;
            return;
        }
        effect[kObjectFlags] |= 8;
        ClearObjectFlagA(effect, nullptr, false);
        ClearObjectFlagB(effect, nullptr, false);
        if (!g_dustLogged) {
            g_dustLogged = true;
            rfa::Log("Dust: %s plays round the camera; the wind %.0f mph (top %.0f) from %.0f degrees, %.0f %% of the sky open, wind_los %.1f",
                     kDustEffect, speed, g_windTopSpeed, direction, open * 100, windLos);
        }
    }
    // At the camera, turned as the effects' wind blows, upwind by up to a quarter of its radius.
    float* place = reinterpret_cast<float*>(effect + kObjectPlace);
    float* turn = reinterpret_cast<float*>(effect + kObjectTurn);
    const BYTE* filter = *kWindFilter;
    if (filter) memcpy(turn, filter + kWindFilterTurn, 9 * sizeof(float));
    const BYTE* entry = EffectEntry(g_dustEffect);
    const BYTE* visual = entry ? Visual(*reinterpret_cast<const int*>(entry + 0x44)) : nullptr;
    const float radius = visual ? *reinterpret_cast<const float*>(visual + 0x84) : 0;
    const float upwind = g_windTopSpeed > 0 ? radius * kDustMaxOffset * speed / g_windTopSpeed : 0;
    for (int i = 0; i < 3; ++i) place[i] = g_cameraPosition[i] - upwind * turn[6 + i];
    effect[kObjectFlags] |= 8;
}

// ---- Guerrilla's particle light

// Guerrilla lights a particle with the time of day's particle_ambient_color times its emitter's own
// ambient colour (rl_particle_renderer::render: Ambient_color = the scene's particle ambient × the
// material's). Armageddon takes the emitter's ambient colour as the light itself: its artists cut
// Guerrilla's 0.4-0.6 greys to about 0.01 when they ported the effects both games have, and with the
// world's ambient light at 0 Guerrilla's dust haze (ambient 1) still drew white. So in our worlds the
// mod gives each emitter of Guerrilla's loaded effects (the rfg_ files the world builder adds) that
// product each frame: the time of day's particle_ambient_color (its sets' curves at +0x2A40, blended
// as the game blends them; the block the game hands the renderer, 0x1973924, reads 0 in our worlds)
// times Guerrilla's own ambient, kept from when the effect was first seen; nothing while no set is
// loaded. A loaded effect (the game's map by file name, LoadedEffect) keeps its data at +0x0C: the file
// as loaded, its offsets made pointers (signature +0, version 36 +4, the emitters' count +0x30 and
// heads +0x34, 0x40 bytes each, the material at +0x1C; the material's ambient colour at +0x44).
constexpr size_t kParticleAmbient = 0x2A40;  // particle_ambient_color
constexpr DWORD kEffectSignature = 0x57423137, kEffectVersion = 36;
constexpr size_t kEffectData = 0x0C, kEffectEmitterCount = 0x30, kEffectEmitters = 0x34, kEmitterHeadSize = 0x40,
                 kEmitterMaterial = 0x1C, kMaterialAmbient = 0x44;
struct Colour3 { float r, g, b; };
std::vector<std::pair<std::string, std::vector<Colour3>>> g_guerrillaAmbients;  // by file, emitter by emitter
bool g_particleLightLogged = false;

void GuerrillaParticleLight() {
    float light[3];
    TodColour(kParticleAmbient, light);
    if (light[0] == 0 && light[1] == 0 && light[2] == 0) return;
    int lit = 0;
    for (const std::string& file : g_ourEffectFiles) {
        const BYTE* effect = static_cast<const BYTE*>(LoadedEffect(file.c_str()));
        BYTE* data = effect ? *reinterpret_cast<BYTE* const*>(effect + kEffectData) : nullptr;
        if (!data) continue;
        if (*reinterpret_cast<const DWORD*>(data) != kEffectSignature || *reinterpret_cast<const DWORD*>(data + 4) != kEffectVersion) {
            RFA_LOG_FIRST(3, "Particles: %s isn't laid out as expected in memory; its light left as it is", file.c_str());
            continue;
        }
        const int count = *reinterpret_cast<const int*>(data + kEffectEmitterCount);
        BYTE* heads = *reinterpret_cast<BYTE* const*>(data + kEffectEmitters);
        if (count <= 0 || count > 256 || !heads) continue;
        auto kept = std::find_if(g_guerrillaAmbients.begin(), g_guerrillaAmbients.end(), [&](const auto& entry) { return entry.first == file; });
        if (kept == g_guerrillaAmbients.end()) {
            // First seen: as converted from Guerrilla's file.
            std::vector<Colour3> own(count, Colour3{0, 0, 0});
            for (int i = 0; i < count; ++i)
                if (const BYTE* material = *reinterpret_cast<BYTE* const*>(heads + i * kEmitterHeadSize + kEmitterMaterial))
                    own[i] = *reinterpret_cast<const Colour3*>(material + kMaterialAmbient);
            g_guerrillaAmbients.emplace_back(file, std::move(own));
            kept = g_guerrillaAmbients.end() - 1;
        }
        for (int i = 0; i < count && i < static_cast<int>(kept->second.size()); ++i) {
            BYTE* material = *reinterpret_cast<BYTE* const*>(heads + i * kEmitterHeadSize + kEmitterMaterial);
            if (!material) continue;
            auto* ambient = reinterpret_cast<Colour3*>(material + kMaterialAmbient);
            const Colour3& own = kept->second[i];
            *ambient = Colour3{light[0] * own.r, light[1] * own.g, light[2] * own.b};
            ++lit;
        }
    }
    if (lit && !g_particleLightLogged) {
        g_particleLightLogged = true;
        rfa::Log("Particles: Guerrilla's particle light: %d emitters lit by the time of day's particle ambient (%.3f %.3f %.3f) times their own",
                 lit, light[0], light[1], light[2]);
    }
}

void __cdecl EffectsUpdate(float seconds) {
    g_effectsUpdate(seconds);
    DustUpdate();
    if (InOurWorld()) GuerrillaParticleLight();
}

HRESULT WINAPI CreateDevice(IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
                            const D3D_FEATURE_LEVEL* levels, UINT levelCount, UINT sdk, ID3D11Device** device,
                            D3D_FEATURE_LEVEL* level, ID3D11DeviceContext** context) {
    const HRESULT result = g_createDevice(adapter, driver, software, flags, levels, levelCount, sdk, device, level, context);
    if (SUCCEEDED(result) && device && *device && context && *context) {
        if (g_context) rfa::Log("Sky: the game made another Direct3D device; the sky uses the newest");
        (*device)->AddRef();
        (*context)->AddRef();
        g_device = *device;
        g_context = *context;
    }
    return result;
}

std::vector<std::string> SplitList(const std::wstring& text) {
    std::vector<std::string> names;
    std::string current;
    for (wchar_t c : text) {
        if (c == L',' || c == L' ') {
            if (!current.empty()) names.push_back(current);
            current.clear();
        } else {
            current += static_cast<char>(c);
        }
    }
    if (!current.empty()) names.push_back(current);
    return names;
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    g_skyOn = rfa::IniInt(L"GuerrillaWorld", L"Sky", 1) != 0;
    g_cloudsOn = rfa::IniInt(L"GuerrillaWorld", L"Clouds", 1) != 0;
    g_worlds = SplitList(rfa::IniString(L"GuerrillaWorld", L"Worlds", L"rfg"));
    g_lightOn = rfa::IniInt(L"GuerrillaWorld", L"Light", 1) != 0;
    ReadBrightness();
    ReadClockSpeed();
    ReadZoneSize();
    ReadStreamRadius();
    ReadAmbient();
    ReadSun();
    ReadColourCorrection();
    ReadFog();
    ReadToneMapping();
    ReadSunShafts();
    ReadSunShadows();
    ReadParticles();
    ReadDust();
    // Our worlds' zone size (and a physics world wide enough for them), and the streaming radii held
    // to what the game can track.
    if (g_zoneWidth > 0) {
        g_zoneSize = reinterpret_cast<ZoneSizeFn>(rfa::HookFunction(kZoneSize, kZoneSizePrologue, reinterpret_cast<void*>(&ZoneSize)));
        if (g_zoneSize) {
            rfa::Log("Those worlds' zones are %.0f m wide (ZoneSize=)", g_zoneWidth);
            WidenWorld();
        } else {
            rfa::Log("Could not hook the game's zone size; ZoneSize= does nothing (and a world built for it won't load right)");
        }
    }
    g_setStreamRadius = reinterpret_cast<SetStreamRadiusFn>(
        rfa::HookFunction(kSetStreamRadius, kSetStreamRadiusPrologue, reinterpret_cast<void*>(&SetStreamRadiusHeld)));
    if (!g_setStreamRadius) rfa::Log("Could not hook the game's streaming radius; the game's own radii aren't held in those worlds");
    g_setAmbient = reinterpret_cast<SetAmbientFn>(rfa::HookFunction(kSetAmbient, kSetAmbientPrologue, reinterpret_cast<void*>(&SetAmbient)));
    g_setBackAmbient = reinterpret_cast<SetAmbientFn>(rfa::HookFunction(kSetBackAmbient, kSetAmbientPrologue, reinterpret_cast<void*>(&SetBackAmbient)));
    if (!g_setAmbient || !g_setBackAmbient) rfa::Log("Could not hook the time of day's ambient light; Ambient= does nothing");
    g_reportError = reinterpret_cast<ReportErrorFn>(rfa::HookFunction(kReportError, kReportErrorPrologue, reinterpret_cast<void*>(&ReportError)));
    if (!g_reportError) rfa::Log("Could not hook the game's error reports; the invalid mission hash lines stay");
    g_debugPrint = reinterpret_cast<DebugPrintFn>(rfa::HookFunction(kDebugPrint, kDebugPrintPrologue, reinterpret_cast<void*>(&DebugPrint)));
    if (!g_debugPrint) rfa::Log("Could not hook the game's debug print; what it says while loading our worlds isn't logged");
    g_precacheArea = reinterpret_cast<PrecacheAreaFn>(rfa::HookFunction(kPrecacheArea, kPrecacheAreaPrologue, reinterpret_cast<void*>(&PrecacheArea)));
    if (!g_precacheArea) rfa::Log("Could not hook the game's precache areas; our worlds' shared files won't be found");
    g_navpointStream = reinterpret_cast<NavpointStreamFn>(
        rfa::HookFunction(kNavpointStream, kNavpointStreamPrologue, reinterpret_cast<void*>(&NavpointStream)));
    g_findNavCell = reinterpret_cast<FindNavCellFn>(rfa::HookFunction(kFindNavCell, kFindNavCellPrologue, reinterpret_cast<void*>(&FindNavCell)));
    if (!g_navpointStream || !g_findNavCell)
        rfa::Log("Could not hook the navpoints' streaming or the nav cell lookup; buildings' transition points on switched-off cells stay unjoined");
    // Guerrilla's gameplay properties, draw distances and effects, if the world builder has written them (the
    // game reads the tables while it starts, before any world).
    bool anyTable = false;
    for (BuiltTable& table : g_builtTables) {
        std::wstring file = DataFolder();
        for (const char* c = table.name; *c; ++c) file += static_cast<wchar_t>(*c);
        if (ReadWholeFile(file, table.bytes) && !table.bytes.empty()) {
            table.bytes.push_back(0);
            anyTable = true;
        } else {
            table.bytes.clear();
            rfa::Log("%s: no mods\\guerrilla_world\\%s (made by the world builder); the game keeps its own", table.what, table.name);
        }
    }
    if (anyTable) {
        g_namePoolInit = reinterpret_cast<NamePoolInitFn>(rfa::HookFunction(kNamePoolInit, kNamePoolInitPrologue, reinterpret_cast<void*>(&NamePoolInit)));
        g_tableCacheFind = reinterpret_cast<TableCacheFindFn>(
            rfa::HookFunction(kTableCacheFind, kTableCacheFindPrologue, reinterpret_cast<void*>(&TableCacheFind)));
        if (!g_namePoolInit || !g_tableCacheFind) {
            rfa::Log("Could not hook the game's table reading; the game keeps its own tables (gameplay properties, draw distances, effects)");
            for (BuiltTable& table : g_builtTables) table.bytes.clear();  // (a hook that did go in only passes through)
        }
        g_effectFileLoad = reinterpret_cast<EffectFileLoadFn>(
            rfa::HookFunction(kEffectFileLoad, kEffectFileLoadPrologue, reinterpret_cast<void*>(&EffectFileLoad)));
        if (!g_effectFileLoad) rfa::Log("Could not watch the game's effect files loading (the log won't say which load)");
        g_playEffect = reinterpret_cast<PlayEffectFn>(rfa::HookFunction(kPlayEffect, kPlayEffectPrologue, reinterpret_cast<void*>(&PlayEffect)));
        g_makeObject = reinterpret_cast<MakeObjectFn>(rfa::HookFunction(kMakeObject, kMakeObjectPrologue, reinterpret_cast<void*>(&MakeObject)));
        if (!g_playEffect || !g_makeObject) rfa::Log("Could not watch the game's effects playing (the log won't say why one doesn't)");
    }
    if (!g_skyOn && !g_lightOn && g_fog <= 0 && g_clockSpeed <= 0) {
        rfa::Log("Guerrilla's sky, light, fog and clock are off (Sky=0, Light=0, Fog=0, ClockSpeed=0)");
        return;
    }
    if ((g_skyOn || g_lightOn || g_fog > 0) &&
        !rfa::HookImport(GetModuleHandleW(nullptr), "d3d11.dll", "D3D11CreateDevice", &CreateDevice, &g_createDevice)) {
        rfa::Log("Could not watch for the game's Direct3D device; no sky, light or fog");
        g_skyOn = g_lightOn = false;
        g_fogFailed = true;
    }
    // The light pass, for the light and for the depth the fog reads (Fog= can be raised from 0 while
    // playing, so it is wrapped either way; it only passes through when neither is wanted).
    if (g_lightOn || !g_fogFailed) {
        g_lightPass = reinterpret_cast<LightPassFn>(rfa::HookFunction(kLightPass, kLightPassPrologue, reinterpret_cast<void*>(&LightPass)));
        g_lightCheck = reinterpret_cast<LightCheckFn>(rfa::HookFunction(kLightCheck, kLightCheckPrologue, reinterpret_cast<void*>(&LightCheck)));
        if (!g_lightPass || !g_lightCheck) {
            rfa::Log("Could not hook the game's light pass; no light added and no fog");
            g_lightOn = false;  // (a hook that did go in only passes through)
            g_fogFailed = true;
        }
        // For the sun's shadows: the follow player light switched on and put along the sun, and each
        // lamp's drawing (hooks that didn't go in leave the sun unshadowed, and only pass through).
        if (g_lightOn) {
            g_followLights = reinterpret_cast<FollowLightsFn>(
                rfa::HookFunction(kFollowLights, kFollowLightsPrologue, reinterpret_cast<void*>(&FollowLights)));
            g_placeFollowLight = reinterpret_cast<PlaceFollowLightFn>(
                rfa::HookFunction(kPlaceFollowLight, kPlaceFollowLightPrologue, reinterpret_cast<void*>(&PlaceFollowLight)));
            g_drawLamp = reinterpret_cast<DrawLampFn>(rfa::HookFunction(kDrawLamp, kDrawLampPrologue, reinterpret_cast<void*>(&DrawLamp)));
            g_setTexture = reinterpret_cast<SetTextureFn>(rfa::HookFunction(kSetTexture, kSetTexturePrologue, reinterpret_cast<void*>(&SetTexture)));
            if (!g_followLights || !g_placeFollowLight || !g_drawLamp || !g_setTexture)
                rfa::Log("Could not hook the follow player light or the light pass's lamps; the sun lights without shadows");
        }
    }
    // Guerrilla's particle layer (with its tone mapping).
    g_particlePhase = reinterpret_cast<ParticlePhaseFn>(rfa::HookFunction(kParticlePhase, kParticlePhasePrologue, reinterpret_cast<void*>(&ParticlePhase)));
    g_particleLast = reinterpret_cast<ParticleLastFn>(rfa::HookFunction(kParticleLast, kParticlePhasePrologue, reinterpret_cast<void*>(&ParticleLast)));
    if (!g_particlePhase || !g_particleLast) {
        rfa::Log("Could not hook the particle phase; the game draws its particles its own way");
        g_particleLayerFailed = true;  // (a hook that did go in only passes through)
    }
    // Guerrilla's dust, after the game's effects update each frame.
    g_effectsUpdate = reinterpret_cast<EffectsUpdateFn>(rfa::HookFunction(kEffectsUpdate, kEffectsUpdatePrologue, reinterpret_cast<void*>(&EffectsUpdate)));
    if (!g_effectsUpdate) rfa::Log("Could not hook the game's effects update; no dust");
    g_toneMappingConstants = reinterpret_cast<ToneMappingConstantsFn>(
        rfa::HookFunction(kToneMappingConstants, kToneMappingConstantsPrologue, reinterpret_cast<void*>(&ToneMappingConstants)));
    g_finalComposite[0] = reinterpret_cast<FinalCompositeFn>(
        rfa::HookFunction(kFinalComposite[0], kFinalCompositePrologue, reinterpret_cast<void*>(&FinalComposite0)));
    g_finalComposite[1] = reinterpret_cast<FinalCompositeFn>(
        rfa::HookFunction(kFinalComposite[1], kFinalCompositePrologue, reinterpret_cast<void*>(&FinalComposite1)));
    if (!g_toneMappingConstants || !g_finalComposite[0] || !g_finalComposite[1]) {
        rfa::Log("Could not hook the game's tone mapping; Armageddon's own stays in those worlds");
        g_toneMappingFailed = true;  // (the hooks that did go in only pass through then)
    }
    g_renderSkybox = reinterpret_cast<RenderSkyboxFn>(
        rfa::HookFunction(kRenderSkybox, kRenderSkyboxPrologue, reinterpret_cast<void*>(&RenderSkybox)));
    if (!g_renderSkybox) {
        rfa::Log("Could not hook the game's sky step; no sky, clock or setting changes while playing");
        return;
    }
    if (g_skyOn)
        rfa::Log(g_cloudsOn ? "Guerrilla's sky and clouds are drawn in the worlds named by Worlds= in [GuerrillaWorld]"
                            : "Guerrilla's sky is drawn (no clouds) in the worlds named by Worlds= in [GuerrillaWorld]");
    if (g_lightOn) rfa::Log("The sky's light and the sun light those worlds (Light=; Ambient=%.3g, Sun=%.3g)", g_ambient, g_sun);
    if (!g_fogFailed) rfa::Log("Guerrilla's fog is drawn in those worlds (Fog=%.3g)", g_fog);
    if (!g_toneMappingFailed)
        rfa::Log(g_toneMapping ? "Guerrilla's tone mapping draws those worlds' picture (ToneMapping=1, Exposure=%.3g)"
                               : "Armageddon's own tone mapping in those worlds (ToneMapping=0)", g_exposure);
    if (g_clockSpeed > 0) rfa::Log("The game clock runs at %.3g game seconds a second in those worlds (ClockSpeed=)", g_clockSpeed);
    if (g_streamRadius > 0) rfa::Log("Those worlds' zones stay loaded %.0f m around the player (StreamRadius=)", g_streamRadius);
}
