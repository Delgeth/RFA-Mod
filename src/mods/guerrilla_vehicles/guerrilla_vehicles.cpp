// What Red Faction: Guerrilla's vehicles need from Armageddon's engine that it doesn't do itself.
// The vehicles are made from the player's own copy of Guerrilla by GuerrillaBuilder.exe (never
// shipped): see RfaFormats\Guerrilla\GuerrillaArsenal.cs.
//
// Driving: Armageddon still has Guerrilla's driving code for the player. Its gameplay controls
// (0x4DBBD0, Guerrilla's control_gameplay_process) read the accelerator and brake as button values
// (0x4E1280, controls_get_button_analog: actions 62 veh_accelerate and 63 veh_brake) and the handbrake
// as a held button (0x4E4800: 64 veh_handbrake), and turn them into the car's accelerator, brake pedal
// and handbrake (0x5F25E0, 0x5F2320, 0x5F2500), as Guerrilla does; steering comes from the veh_steer
// axis. But Armageddon's PC controls give those three actions no key: it has no car-driving scheme
// on PC (only on foot, exo, walker/tank and flyer, ui_options_controller_pc.lua), so a car's throttle
// stays at 0 and the player sits in it without moving. Guerrilla's own PC defaults (its
// control_config_populate_with_defaults, keyboard table) are W to accelerate, S to brake and reverse,
// Space for the handbrake.
//
// Getting out and changing seats: in a vehicle the game's use button is the vehicle action (0x4E1B70
// picks it: 31 of_action on foot, 60 exo_action in an exo, 86 veh_action in a vehicle, 97
// veh_flyer_action in a flyer), which gets the player out through the vehicle's interaction, and 90
// veh_change_seat moves them to another seat. Armageddon's PC defaults (six tables from 0xF7F078,
// action, key, mouse button) give neither a key (its walkers and tank are left by script), so the
// player gets into a car with E and can't get out. Guerrilla's are E for the vehicle action (as on
// foot) and the third mouse button for changing seats.
//
// The mod gives each of these actions its key or mouse button ([GuerrillaVehicles] Accelerate=,
// Brake=, Handbrake=, Exit=, ChangeSeat=: a key's name as the console's controls_show_key_bindings
// prints it, or Mouse1..Mouse7) whenever the game builds its key map from the bindings (0x4E86E0), if
// it has neither; one the player has bound is left as it is.
//
// The bindings: one control set per player (0xCF0 bytes from 0x11BEBA0), 139 button actions of 20
// bytes (+0 the gamepad button, +8 the keyboard key as a DirectInput scancode, 0 for none, +0xC the
// mouse button, 0 to 6, -1 for none: as the defaults are written, 0x4E5CA0), then the axis actions.
// The game's names for keys are a table of 256 strings at 0x1121900, by scancode.
//
// Room for Guerrilla's animation sets: each vehicle's interaction plays an animation set (getting in,
// driving, getting out), and the game keeps every set in one table of 140 (MAX_ANIM_SETS; 20 bytes a
// set, from 0x26321B8). Armageddon fills 125 of them itself (its 50 set files and one set for each of
// its 75 animated objects, anim_rig_objects.xtbl), so only 15 of Guerrilla's vehicles' 24 fit; one
// more stops the game at start-up ("Num_anim_sets < MAX_ANIM_SETS") and it crashes. The table is a
// global the game's code reaches by its address, written into 34 instructions (the set loaders,
// anim_set_find, the people's animation code: as the table's start, or its first set's fields at +4,
// +8 and +0xC), and its size into five (the four loaders' checks and the start-up constructor that
// builds it). No one function holds it, so the mod moves it: a table of 255 sets of its own, each of
// those instructions given its address instead (and the five the new size), all of them checked
// against the game's values first and none changed if one differs. Mods start while the game's main
// thread waits at its entry point, before its start-up code, so the game's own constructor then builds
// the new table, and nothing has used the old one. 255, as set numbers may be kept in a byte.
//
// Room for Guerrilla's vehicles: the game keeps every vehicle the vehicle table describes in one list
// of 40 (0x148BD08: the entries, how many it holds, how many it has; 0x880 bytes an entry, kept right
// after it). Its start-up constructor (0x6270C0) builds the 40 there, and a list that is full can't
// grow (0x7476F0 says no), so the table's reader (0x6309C0) drops every vehicle after the 40th without
// a word. Armageddon fills 24 itself, so only 16 of Guerrilla's fit. The game's code reaches the
// entries only through the list, never at their place after it, so the mod builds the list itself in
// place of that constructor: 255 entries of its own, each built by the game's constructor for one
// (0x627100), and the list set up with them (0x47CE00). The list's destructor does nothing.
//
// Each vehicle in the list also takes, for as long as the game runs, one of the animation library's
// entity bone infos (anim_lib_instance_manager: added by 0xD7B600 when the vehicle table is read, by
// the vehicle's number), from the same table of 138 the people and animated objects take theirs from
// while they are about (0x240 bytes each from 0x323BD50, how many are taken at 0x3393360). With every
// one taken the game asserts ("Out of animlib_entity_bone_infos", nothing in this build) and writes
// through a null pointer. Only four small functions use the table (add, remove, clear, find), each
// with its address and its size, 138, in its code, so the mod moves it as it does the animation sets:
// a table of 512 of its own (Armageddon's 24 vehicles and Guerrilla's 118 leave the others more than
// they had), the four addresses and the five sizes changed after checking them. The game clears the
// table (every entry free, -1) while it starts, after the mods.
//
// Room for the animations' triggers: an animation's triggers (when a door starts to open, a foot
// lands, the camera moves; each anim_files.xtbl entry's <Triggers>) are kept in one pool of 5750 for
// every animation (8 bytes each, a trigger and its time, from 0x34BB9B8; how many are taken at
// 0x34C6D68), which the animation library adds them to as it reads the table (0xD89D40, from
// 0xD877B0). Once it is full, every animation after gets none, without a word. Armageddon's own and
// Guerrilla's come to about 7000, so the cars after the APCs had no "door start", and getting in
// stopped at the door. Only that function reaches the pool, by its address and its size, so the mod
// moves it: a pool of 16384 of its own, the one address and the one size changed after checking them.
//
// Guerrilla's vehicles' own skeletons: the game loads every skeleton (.rig_pc) its archives list while
// it starts (0x973460, from the start-up sequence: the list, then each through 0x973550), up to 290
// (its table of loaded skeletons, 0x48 bytes each from 0x34F5808; Armageddon has 178). A vehicle
// whose table entry names one (Rig: walkers, flyers, turret trucks) takes it from that table when the
// vehicle table is read (0x62F1C0, through 0xD8A010), with no check: one that isn't loaded stops the
// game. Guerrilla's vehicles' own are new files (GuerrillaArsenal writes them, and their names in
// mods\guerrilla_vehicles\rigs.txt), in no archive's list, so the mod wraps the start-up loading and
// has the game load each of them by name after its own (0x973550, cdecl, the file's name: it opens the
// file by name, where the loader finds a new file).
//
// Room for Guerrilla's vehicles in the world: Armageddon holds fewer of them than Guerrilla. Its object
// pools are made by start-up constructors, each carving its slots out of a buffer inside the pool
// object sized for its count (0x71C5D0 sets the pool up, a memory pool is made on the buffer, 0x428240,
// and the pool's fill takes from it each slot's object, handle and byte); Armageddon made two smaller
// than Guerrilla's: the vehicle spawn nodes (parked vehicles' spots) 12 (Guerrilla 128; constructor
// 0x805710) and the cars 16 at once (Guerrilla 32; 0x5F97F0). The mod replaces both constructors with
// ones doing the same on buffers of its own, for Guerrilla's counts. And every vehicle model the vehicle
// table names takes one of the vehicle resource pool's records (0x1972900, built by the start-up code at
// 0xF29510 and 0xF29550 on a static array of 84); once they are all taken, a model gets none and its
// vehicles never spawn, without a word; Armageddon's and Guerrilla's models together are more than 84.
// The mod builds the records on an array of 255 of its own (the 16 models streamed at once stay the
// game's). The constructors' values are checked first, and nothing changed if one differs.
//
// Room for Guerrilla's walkers' blend trees: Armageddon keeps every blend tree (its .blend_treex files,
// listed in blendTree_filenames.xtbl) in a table of 24, and its own fill 23 of them (the loader stops at
// the 24th), so Guerrilla's walkers' trees (GuerrillaWalkerTrees) wouldn't fit. The table is a global the
// start-up constructor builds, its address in four instructions and its size in two, so the mod moves it
// as it does the animation sets: a table of 64 of its own, checked first, before the constructor runs.
//
// A vehicle is made only once its model has streamed in (object_spawn_vehicle, 0x761790: refused, 2,
// unless the model's record is reserved or in use; then "not yet", 1, until it has streamed in). Where a
// spawn resource is in force (our worlds, as Wrecking Crew's levels) the game streams only the models
// the resource holds, so Guerrilla's parked vehicles' spots and its traffic would be refused for good.
// Guerrilla chose its streamed vehicles by what was wanted near the player. The mod wraps that spawn:
// one refused for want of its model reserves it in the resource in force (0x761DC0, cdecl: category 8,
// owner 3, the vehicle's entry, as the game's own reservations and the Spawn menu's; it makes room only by
// letting go of a reservation nothing uses, 16 models at most) and says "not yet", so the spot asks again
// until it has streamed in.
//
// Guerrilla's lights (GuerrillaVehicleLights: each light linked to a vehicle_lights row of its type, its
// glow Guerrilla's): Armageddon turns a vehicle's lights on and off by its own rules (0x61BA60 each frame,
// 0x61B280 only when the headlight state or the half-second blink changes): headlights by the driver, tail
// lights off while braking, steady running lights. Guerrilla's (vehicle_effects_process_lights): every
// frame; headlights and fog lights while the engine runs at night (18:00 to 6:00 by the game clock, at first
// sight as the hour says, then changing at a random moment within the hour), tail lights with the brake
// light's glow while braking, else as the headlights; brake lights while braking, reverse lights while
// reversing, emergency lights blinking every half second, strobes while the engine runs. The mod wraps
// 0x61BA60: a vehicle with lights of Guerrilla's rows gets Guerrilla's rules, through the game's own
// steps for turning a light on (0x5FB700), swapping its glow, and off (0x5FB920); breaking stays the game's.
//
// Guerrilla's engine sounds (GuerrillaVehicleSounds builds them): each frame the game sets four game
// parameters on a vehicle's engine sound (0x5FABA0, cdecl: the sound, then SPEED, RPM, ENGINE_LOAD and
// DAMAGE, from the vehicle's sounds' frame 0x5FA1F0), which Guerrilla's engine layers follow. Guerrilla's
// own audio code eased the load toward the game's, 0.12 of the way every 10 ms, and played the player's
// own vehicle's stereo loops in 2D, the others' mono loops in 3D. The mod replaces that function with one
// doing the same and also easing the load as Guerrilla did, and setting RFG_PLAYER_IN_VEHICLE (1 for the
// vehicle the player is in: the game's own check, 0x605550, made in a wrapper of the frame), which the
// banks' two sets of loops fade on. Armageddon's own engines read neither.
//
// Dents: Guerrilla's cars crumple where they are hit, by a morph target in the vehicle's file (46 of its
// 74 have one; GuerrillaArsenal carries it over). Armageddon kept the whole of it, Guerrilla's code as
// it was: the vehicle loader reads the morph target (0x622110 → 0xD40580) and makes the vehicle's dent
// container (0xC2BC10), and the impact code (0x60F590) dents it (0x619810), but only while
// vehicle_deformation_enabled is on (a bool at 0x142A484, also the console's toggle of that name),
// which Guerrilla starts on and Armageddon off (none of its vehicles has a morph target, so for them
// nothing changes). The mod turns it on while the game starts ([GuerrillaVehicles] Dents=1, before
// any vehicle is loaded: the loader reads it then), after checking that the three instructions that
// read it (the toggle, the loader, the impact code) still name it.
//
// Doors: getting in, a person's step "Open Door" opens the vehicle's door (vi_door_open_action,
// 0x654AA0, Guerrilla's 0xBA1520), which records it among the vehicle's open doors (six records of 16
// bytes from the pointer at the interaction instance's +4: when, the door part, its state, the
// waypoint it was opened from), and every frame the interaction closes a door recorded open 2.5 s
// after it opened once nobody stands at that waypoint, 4 s at most (0x657500, Guerrilla's
// vehicle_interaction_process). Armageddon kept all of it as Guerrilla had it. A door recorded open is
// never opened again (the open action only refreshes the record), so if the door part has shut while
// its record still says open, a person climbs through the shut door: seen on Guerrilla's taxi. The
// mod wraps the open action: before it runs, a door of the transition that is shut (latched to the
// body, its opening at most 0.03) but still recorded open has its record cleared (0x64C140, and any
// person's note of it), so the game opens it and records it afresh, as Guerrilla would.
//
// A door's opening is measured from its turn against the vehicle (0x6134A0, Guerrilla's
// vehicle_component::get_unlatched_door_t_value): the turn's axis and angle (matrix::
// extract_axis_and_angle), over the hinge's most. For a shut door the turn is next to none, and its
// axis comes out next to nothing; Guerrilla's extract_axis_and_angle (RM 0x5A10C0) then calls it no
// turn (angle 0), Armageddon's (0xC9C8A0) a half turn (angle pi). So, depending on which way the
// vehicle faces, a shut door just unlatched to open measured pi over its 90 degrees, 2: fully open;
// the game ended the opening at once, the door never moved, and the person climbed through it (the
// second time into Guerrilla's taxi). The mod replaces the door's measurement with Guerrilla's, its
// axis and angle worked out as Guerrilla's are (the game's matrix and angle helpers otherwise); the
// shared math function stays Armageddon's for everything else. With
// [GuerrillaVehicles] DoorLog=1 (off unless set; not in the ini template) it also logs every door
// opening and closing, each latch and unlatch, and each change of a door part's state.
#define RFA_MOD_NAME "guerrilla_vehicles"
#include "rfa_mod.h"
#include "rfa_game.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <cwctype>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

namespace {

// ---- The game

// Builds a control set's key map from its bindings (cdecl, the control set's number): clears every
// key, then marks each key a button or axis action uses. Called whenever the bindings are set up or
// changed.
constexpr DWORD kBuildKeyMap = 0x4E86E0;
constexpr size_t kBuildKeyMapPrologue = 6;  // push ebp / mov ebp,esp / sub esp,18h
using BuildKeyMapFn = void(__cdecl*)(int controlSet);
BuildKeyMapFn g_buildKeyMap;

constexpr DWORD kBindings = 0x11BEBA0;
constexpr DWORD kControlSetSize = 0xCF0;
constexpr DWORD kButtonBindingSize = 20;
constexpr DWORD kKeyField = 8;
constexpr DWORD kMouseField = 0xC;
constexpr int kMouseButtons = 7;
constexpr int kControlSets = 2;

constexpr DWORD kKeyNames = 0x1121900;  // const char*[256], by scancode

// The animation set table (see the top): where it is, a set's size, how many it holds.
constexpr DWORD kAnimSets = 0x26321B8;
constexpr DWORD kAnimSetSize = 20;
constexpr DWORD kGameMaxAnimSets = 140;

// Each place in the game's code that holds the table's address (or its first set's +4, +8, +0xC): the
// address of that 4-byte value inside its instruction (sub eax,26321B8h; add ecx,26321B8h;
// mov edx,[ecx+26321B8h]; cmp [edx+26321C4h],0; push 26321B8h ...), with the function it is in.
constexpr DWORD kAnimSetReferences[] = {
    0x64EB48,                      // 0x64E9A0
    0x654904,                      // 0x654590
    0x65546D,                      // 0x6551C0
    0x656CEB,                      // 0x656BE0
    0x659CF8,                      // 0x659C30
    0x65A11A,                      // 0x659D60
    0x65E5BD,                      // 0x65E2C0
    0x661188, 0x6612FA, 0x6618D7,  // 0x661080
    0x7BA3B4,                      // 0x7BA370
    0x979B7D, 0x979B8C, 0x979B91,  // 0x979AA0
    0x97A7D4, 0x97A80F, 0x97A81C, 0x97A83C, 0x97A848,  // 0x979C20, the sets' loading
    0x97BEAB,                      // 0x97BE20, the animated objects' sets (counting)
    0x97C1F6, 0x97C2D6,            // 0x97C170, a set file's sets (counting)
    0x97C805, 0x97C904,            // 0x97C7B0, a set file's sets (filling)
    0x97CB50, 0x97CDB1,            // 0x97C950
    0x97D07B, 0x97D3A8, 0x97D64D,  // 0x97CFF0, the animated objects' sets (filling)
    0x97E55E,                      // 0x97E530
    0x98C2DD, 0x98C2F4,            // 0x98C2B0, anim_set_find (by name)
    0x98C431,                      // 0x98C3F0
    0xF4A700,                      // 0xF4A6F0, the start-up constructor (the table)
};
// Each place that holds its size, 140: cmp dword ptr [2632170h],8Ch in the four loaders, before they
// add a set (the count is at 0x2632170), and push 8Ch in the start-up constructor.
constexpr DWORD kAnimSetLimits[] = {0x97BE87, 0x97C1D2, 0x97C2B4, 0x97D057, 0xF4A6F9};

constexpr DWORD kMaxAnimSets = 255;
alignas(16) BYTE g_animSets[kMaxAnimSets * kAnimSetSize];  // the new table, built by the game's constructor

// The vehicle list (see the top): its constructor, run once at start-up (thiscall, the list); an
// entry's constructor (thiscall); and the list's setup (thiscall: the entries, how many it holds, how
// many it has).
constexpr DWORD kBuildVehicleList = 0x6270C0;
constexpr size_t kBuildVehicleListPrologue = 7;  // push ebp / mov ebp,esp / push ecx / mov [ebp-4],ecx
using BuildVehicleListFn = BYTE*(__fastcall*)(BYTE* list, void* unused);
BuildVehicleListFn g_buildVehicleList;
auto const BuildVehicle = reinterpret_cast<void*(__fastcall*)(void* vehicle, void* unused)>(0x627100);
auto const SetUpList = reinterpret_cast<void(__fastcall*)(void* list, void* unused, void* entries, int holds, int has)>(0x47CE00);
constexpr DWORD kVehicleList = 0x148BD08;
constexpr DWORD kVehicleSize = 0x880;
constexpr int kGameMaxVehicles = 40;

constexpr int kMaxVehicles = 255;
alignas(16) BYTE g_vehicles[kMaxVehicles * kVehicleSize];  // the new list's entries

// The animation library's entity bone infos (see the top): where they are, one's size, how many.
constexpr DWORD kBoneInfos = 0x323BD50;
constexpr DWORD kBoneInfoSize = 0x240;
constexpr DWORD kGameMaxBoneInfos = 138;
// The table's address in each of its four functions (mov dword ptr [ebp-0Ch],323BD50h ...).
constexpr DWORD kBoneInfoReferences[] = {
    0xD7B646,  // 0xD7B600, add
    0xD7B8D0,  // 0xD7B8C0, remove (by number)
    0xD7BDE0,  // 0xD7BDD0, clear
    0xD7C680,  // 0xD7C670, find (by number)
};
// Its size: cmp dword ptr [3393360h],8Ah before adding, and each function's cmp dword ptr [ebp-4],8Ah.
constexpr DWORD kBoneInfoLimits[] = {0xD7B613, 0xD7B663, 0xD7B8EE, 0xD7BDFE, 0xD7C69E};

constexpr DWORD kMaxBoneInfos = 512;
alignas(16) BYTE g_boneInfos[kMaxBoneInfos * kBoneInfoSize];  // the new table, cleared by the game

// The animations' triggers (see the top): where the pool is, a trigger's size, how many it holds.
constexpr DWORD kTriggers = 0x34BB9B8;
constexpr DWORD kTriggerSize = 8;
constexpr DWORD kGameMaxTriggers = 5750;
// Its address (lea edx,[ecx*8+34BB9B8h]) and its size (cmp dword ptr [34C6D68h],1676h), in 0xD89D40.
constexpr DWORD kTriggerReferences[] = {0xD89DBF};
constexpr DWORD kTriggerLimits[] = {0xD89D4F};

constexpr DWORD kMaxTriggers = 16384;
alignas(16) BYTE g_triggers[kMaxTriggers * kTriggerSize];  // the new pool, filled by the game

// The game's animation entries: one for each anim_files.xtbl entry (0x34 bytes each from 0x2597D30,
// how many at 0x25CD738; added by 0x95DCF0, which stops at 4224). Armageddon's and Guerrilla's
// vehicles' come to 3821; Guerrilla's people and walkers will pass it. Only the entries' own
// functions (0x95DCF0 to 0x95DFA2) reach the table by its address (15 values: as its start, or its
// first entry's fields; one turns an entry back into its number by subtracting it, 0x95DE80), and its
// size is in seven: the add, the look-up by number, a walk over them all, and the animation set
// loaders' four checks of a set's animation numbers (cmp [eax+edx*4],1080h after 0x95DE80, too big
// = not found). So the mod moves it as the others, to 8192.
constexpr DWORD kAnimationEntries = 0x2597D30;
constexpr DWORD kAnimationEntrySize = 0x34;
constexpr DWORD kGameMaxAnimationEntries = 4224;
constexpr DWORD kAnimationEntryReferences[] = {0x95DD0D, 0x95DD25, 0x95DD87, 0x95DDB2, 0x95DDC5, 0x95DDE4, 0x95DDFD, 0x95DE13,
                                               0x95DE1F, 0x95DE32, 0x95DE6F, 0x95DE87, 0x95DF07, 0x95DF59, 0x95DF8C};
constexpr DWORD kAnimationEntryLimits[] = {0x95DCFA, 0x95DE5C, 0x95DFA5, 0x97CB33, 0x97CD94, 0x97D388, 0x97D62D};
constexpr DWORD kMaxAnimationEntries = 8192;
alignas(16) BYTE g_animationEntries[kMaxAnimationEntries * kAnimationEntrySize];  // the new table, filled by the game

// The animation library: every animation it knows, 32 bytes each from 0x34C7570 (how many at
// 0x34B2C10; +4 the hash of its name), and a 9000-place table of their numbers by that hash (from
// 0x34B2D18, -1 for an empty place), so twice as many places as animations. Its functions, all in
// 0xD87100-0xD8C350: the reset (clears both, by their sizes in bytes), add (0xD87670, stops at
// 4500), file a name (0xD876E0, looks for an empty place: with no empty place it would never stop),
// forget a name (0xD87510), look a name up (0xD8C2C0), by number (0xD87850), the number of an entry
// (0xD8C2A0, by subtracting the table's start), a walk over them all (0xD89F40), and the start-up
// constructor (0xF60010, which only clears them). Armageddon's and Guerrilla's vehicles' come to 3814.
// Both tables move together, the names' to twice the animations' as the game has them; 8192 still
// fits any 13-bit field an animation's number might be kept in.
constexpr DWORD kLibraryAnimations = 0x34C7570;
constexpr DWORD kLibraryAnimationSize = 32;
constexpr DWORD kGameMaxLibraryAnimations = 4500;
constexpr DWORD kLibraryAnimationReferences[] = {
    0xD8710E, 0xD8786F, 0xD89F50, 0xD8C2B4, 0xF60020,  // the reset, by number, the walk, the number of one, the constructor
    0xD8759A, 0xD876BF, 0xD87765, 0xD8C337,            // the name's hash (+4): forget, add, file, look up
};
constexpr DWORD kLibraryAnimationLimits[] = {0xD87682, 0xD876B1, 0xD89F6B, 0xF60019};  // add (twice), the walk, the constructor
constexpr DWORD kLibraryNames = 0x34B2D18;
constexpr DWORD kGameLibraryNamePlaces = 9000;
constexpr DWORD kLibraryNameReferences[] = {0xD87122, 0xD8753C, 0xD87585, 0xD875A9, 0xD8770C, 0xD87750, 0xD8777B, 0xD8C322};
constexpr DWORD kLibraryNameLimits[] = {0xD8752D, 0xD87576, 0xD876FD, 0xD87741, 0xD8C2DD, 0xD8C313};  // mov ecx,2328h before each div
constexpr DWORD kLibraryAnimationBytes = 0xD87107;  // the reset's push 23280h (4500 x 32)
constexpr DWORD kLibraryNameBytes = 0xD8711B;       // and push 8CA0h (9000 x 4)
constexpr DWORD kMaxLibraryAnimations = 8192;
constexpr DWORD kLibraryNamePlaces = 2 * kMaxLibraryAnimations;
alignas(16) BYTE g_libraryAnimations[kMaxLibraryAnimations * kLibraryAnimationSize];  // the new table, cleared and filled by the game
alignas(16) int g_libraryNames[kLibraryNamePlaces];                                    // the new names' table, emptied below and by the game

// Moves the animation library's two tables, the names' first: more animations than the names'
// table has places for would make filing a name loop for ever, so the animations only move if the
// names' table has.
void MoveAnimationLibrary() {
    if (rfa::CodeValue(kLibraryAnimationBytes) != kGameMaxLibraryAnimations * kLibraryAnimationSize ||
        rfa::CodeValue(kLibraryNameBytes) != kGameLibraryNamePlaces * sizeof(int)) {
        rfa::Log("Library animations: the library's reset isn't as expected; the game keeps its %lu", kGameMaxLibraryAnimations);
        return;
    }
    std::fill(std::begin(g_libraryNames), std::end(g_libraryNames), -1);
    if (!rfa::MoveTable("Library animation names", kLibraryNames, sizeof(int), kGameLibraryNamePlaces, kLibraryNameReferences,
                        kLibraryNameLimits, reinterpret_cast<BYTE*>(g_libraryNames), kLibraryNamePlaces))
        return;
    const DWORD nameBytes = kLibraryNamePlaces * sizeof(int);
    if (!rfa::PatchCode(reinterpret_cast<void*>(kLibraryNameBytes), &nameBytes, sizeof(nameBytes)))
        rfa::Log("Library animation names: could not change the reset's size; it empties only the first %lu places", kGameLibraryNamePlaces);
    if (!rfa::MoveTable("Library animations", kLibraryAnimations, kLibraryAnimationSize, kGameMaxLibraryAnimations,
                        kLibraryAnimationReferences, kLibraryAnimationLimits, g_libraryAnimations, kMaxLibraryAnimations))
        return;
    const DWORD animationBytes = kMaxLibraryAnimations * kLibraryAnimationSize;
    if (!rfa::PatchCode(reinterpret_cast<void*>(kLibraryAnimationBytes), &animationBytes, sizeof(animationBytes)))
        rfa::Log("Library animations: could not change the reset's size; it clears only the first %lu", kGameMaxLibraryAnimations);
}

// Loads every skeleton the archives list, at start-up (cdecl, no arguments).
constexpr DWORD kLoadSkeletons = 0x973460;
constexpr size_t kLoadSkeletonsPrologue = 9;  // push ebp / mov ebp,esp / sub esp,240h
using LoadSkeletonsFn = void(__cdecl*)();
LoadSkeletonsFn g_loadSkeletons;
// Loads one skeleton by its file's name (cdecl; nothing if it is loaded already).
auto const LoadSkeleton = reinterpret_cast<void(__cdecl*)(const char* name)>(0x973550);
// A loaded skeleton's place in the table by its name, or -1.
auto const FindSkeleton = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD8A040);
std::vector<std::string> g_skeletons;  // the new files to load (mods\guerrilla_vehicles\rigs.txt)

// ---- The vehicle actions given keys

struct VehicleKey {
    int action;               // the button action
    const wchar_t* setting;
    const wchar_t* fallback;  // Guerrilla's PC default
    DWORD key;                // the scancode given, 0 if none
    int mouse;                // or the mouse button given (0 the first), -1 if none
};
VehicleKey g_vehicleKeys[] = {
    {62, L"Accelerate", L"W", 0, -1},
    {63, L"Brake", L"S", 0, -1},
    {64, L"Handbrake", L"Spacebar", 0, -1},
    {86, L"Exit", L"E", 0, -1},
    {90, L"ChangeSeat", L"Mouse3", 0, -1},
};

// A key's scancode from its name in the game's table (any case), or 0.
DWORD KeyByName(const std::wstring& name) {
    const char* const* names = reinterpret_cast<const char* const*>(kKeyNames);
    for (DWORD key = 1; key < 256; ++key) {
        const char* entry = names[key];
        if (!entry || !*entry) continue;
        size_t i = 0;
        while (entry[i] && i < name.size() && towlower(static_cast<unsigned char>(entry[i])) == towlower(name[i])) ++i;
        if (!entry[i] && i == name.size()) return key;
    }
    return 0;
}

// A setting's mouse button, Mouse1..Mouse7 (any case), as the bindings number it (0..6), or -1.
int MouseByName(const std::wstring& name) {
    if (name.size() != 6 || _wcsnicmp(name.c_str(), L"Mouse", 5) != 0) return -1;
    const int button = name[5] - L'1';
    return button >= 0 && button < kMouseButtons ? button : -1;
}

void __cdecl BuildKeyMap(int controlSet) {
    if (controlSet >= 0 && controlSet < kControlSets) {
        for (const VehicleKey& given : g_vehicleKeys) {
            if (!given.key && given.mouse < 0) continue;
            const DWORD binding = kBindings + controlSet * kControlSetSize + given.action * kButtonBindingSize;
            auto* key = reinterpret_cast<DWORD*>(binding + kKeyField);
            auto* mouse = reinterpret_cast<int*>(binding + kMouseField);
            if (*key || (*mouse >= 0 && *mouse < kMouseButtons)) continue;
            if (given.key) {
                *key = given.key;
                const char* name = reinterpret_cast<const char* const*>(kKeyNames)[given.key];
                rfa::Log("Vehicles: %ls on %s (control set %d; it had no key)", given.setting, name ? name : "?", controlSet);
            } else {
                *mouse = given.mouse;
                rfa::Log("Vehicles: %ls on Mouse%d (control set %d; it had no key)", given.setting, given.mouse + 1, controlSet);
            }
        }
    }
    g_buildKeyMap(controlSet);
}

// ---- Room for more animation sets and bone infos (see the top)

// ---- Room for more vehicles (see the top)

BYTE* __fastcall BuildVehicleList(BYTE* list, void*) {
    if (reinterpret_cast<DWORD>(list) != kVehicleList) return g_buildVehicleList(list, nullptr);
    for (int i = 0; i < kMaxVehicles; ++i) BuildVehicle(g_vehicles + i * kVehicleSize, nullptr);
    SetUpList(list, nullptr, g_vehicles, kMaxVehicles, 0);
    rfa::Log("Vehicles: the game's list holds %d (its own %d), at 0x%08lX", kMaxVehicles, kGameMaxVehicles,
             reinterpret_cast<DWORD>(g_vehicles));
    return list;
}

// ---- Doors (see the top)

constexpr DWORD kDoorOpenAction = 0x654AA0;   // cdecl bool (interaction, person, transition, seconds, door or -1 for all)
constexpr size_t kDoorOpenActionPrologue = 6;  // push ebp / mov ebp,esp / sub esp,28h
constexpr DWORD kDoorCloseAction = 0x657AD0;  // cdecl (interaction, record, seconds)
constexpr size_t kDoorCloseActionPrologue = 6;  // push ebp / mov ebp,esp / sub esp,10h
constexpr DWORD kUpdateDoor = 0x612FA0;       // thiscall (part; seconds, vehicle), each frame for an unlatched door
constexpr size_t kUpdateDoorPrologue = 6;       // push ebp / mov ebp,esp / sub esp,60h
using DoorOpenActionFn = bool(__cdecl*)(BYTE* interaction, int person, const BYTE* transition, float seconds, int door);
using DoorCloseActionFn = void(__cdecl*)(BYTE* interaction, int record, float seconds);
using UpdateDoorFn = void(__fastcall*)(BYTE* part, void* unused, float seconds, BYTE* vehicle);
DoorOpenActionFn g_doorOpenAction;
DoorCloseActionFn g_doorCloseAction;
UpdateDoorFn g_updateDoor;
auto const ClearDoorRecord = reinterpret_cast<void(__fastcall*)(BYTE* record, void* unused)>(0x64C140);  // vi_door::initialize

constexpr int kDoorRecords = 6, kDoorRecordSize = 16;                // when, part, state, waypoint
constexpr int kPeople = 8, kPersonSize = 0x130, kPersonDoorRecords = 0x3B;  // each person's note of its doors' records (3 bytes)
constexpr int kTransitionDoorPart = 8, kTransitionDoorCount = 0x1C;  // a door's part (a byte, +8 + 8 each); how many
constexpr DWORD kParts = 0x1C4, kPartsReady = 0x3BDC, kPartSize = 0xB8;  // a vehicle's parts: +8 + 0xB8 each, how many at +4
constexpr DWORD kDoorOpening = 0x30, kDoorState = 0x3C, kPartFlags = 0x54, kUnlatched = 0x10000;
constexpr int kRecordOpen = 3;
const char* const kDoorStates[] = {"still", "begin opening", "opening", "opened", "begin closing", "closing", "closed"};
const char* const kRecordStates[] = {"closed", "closed climbing", "closing", "open", "opening", "open for gunner"};

bool g_doorLog = false;
int g_doorLogLines = 0;
constexpr int kDoorLogLines = 400;
#define DOOR_LOG(...)                                                \
    do {                                                             \
        if (g_doorLog && g_doorLogLines < kDoorLogLines) {           \
            ++g_doorLogLines;                                        \
            rfa::Log(__VA_ARGS__);                                   \
        }                                                            \
    } while (0)

BYTE* DoorRecord(BYTE* interaction, int i) { return *reinterpret_cast<BYTE**>(interaction + 4) + i * kDoorRecordSize; }

BYTE* Part(BYTE* vehicle, int i) {
    if (!vehicle || i < 0 || vehicle[kParts + kPartsReady] != 1) return nullptr;
    if (i >= *reinterpret_cast<const int*>(vehicle + kParts + 4)) return nullptr;
    return vehicle + kParts + 8 + i * kPartSize;
}

const char* Named(const char* const* names, int count, int value) { return value >= 0 && value < count ? names[value] : "?"; }

std::string DoorRecords(BYTE* interaction) {
    std::string list;
    for (int i = 0; i < kDoorRecords; ++i) {
        const BYTE* r = DoorRecord(interaction, i);
        const int part = *reinterpret_cast<const int*>(r + 4);
        if (part < 0) continue;
        char one[64];
        snprintf(one, sizeof(one), " [%d: part %d %s]", i, part, Named(kRecordStates, 6, *reinterpret_cast<const int*>(r + 8)));
        list += one;
    }
    return list.empty() ? " none" : list;
}

std::string DoorPart(BYTE* vehicle, int i) {
    const BYTE* p = Part(vehicle, i);
    if (!p) return "no part";
    char text[96];
    snprintf(text, sizeof(text), "part %d %s, open %.2f, %s", i, Named(kDoorStates, 7, *reinterpret_cast<const int*>(p + kDoorState)),
             *reinterpret_cast<const float*>(p + kDoorOpening), (*reinterpret_cast<const DWORD*>(p + kPartFlags) & kUnlatched) ? "unlatched" : "latched");
    return text;
}

bool __cdecl DoorOpenAction(BYTE* interaction, int person, const BYTE* transition, float seconds, int door) {
    BYTE* vehicle = *reinterpret_cast<BYTE**>(interaction);
    const int first = door < 0 ? 0 : door;
    const int end = door < 0 ? *reinterpret_cast<const int*>(transition + kTransitionDoorCount) : door + 1;
    for (int d = first; d < end && d < 3; ++d) {
        const int part = static_cast<signed char>(transition[kTransitionDoorPart + d * 8]);
        const BYTE* p = Part(vehicle, part);
        if (!p) continue;
        DOOR_LOG("Doors: open, person %d, %s; records%s", person, DoorPart(vehicle, part).c_str(), DoorRecords(interaction).c_str());
        const bool shut = !(*reinterpret_cast<const DWORD*>(p + kPartFlags) & kUnlatched) && *reinterpret_cast<const float*>(p + kDoorOpening) <= 0.03f;
        if (!shut) continue;
        for (int i = 0; i < kDoorRecords; ++i) {
            BYTE* r = DoorRecord(interaction, i);
            if (*reinterpret_cast<const int*>(r + 4) != part || *reinterpret_cast<const int*>(r + 8) != kRecordOpen) continue;
            ClearDoorRecord(r, nullptr);
            BYTE* people = *reinterpret_cast<BYTE**>(interaction + 0xAC);
            for (int h = 0; h < kPeople && people; ++h)
                for (int k = 0; k < 3; ++k)
                    if (static_cast<signed char>(people[h * kPersonSize + kPersonDoorRecords + k]) == i)
                        people[h * kPersonSize + kPersonDoorRecords + k] = 0xFF;
            RFA_LOG_FIRST(20, "Doors: part %d is shut but was still recorded open; record cleared so it opens", part);
        }
    }
    const bool result = g_doorOpenAction(interaction, person, transition, seconds, door);
    DOOR_LOG("Doors: open action %s (%.2f s); records%s", result ? "true" : "false", seconds, DoorRecords(interaction).c_str());
    return result;
}

void __cdecl DoorCloseAction(BYTE* interaction, int record, float seconds) {
    BYTE* vehicle = *reinterpret_cast<BYTE**>(interaction);
    const int part = record >= 0 && record < kDoorRecords ? *reinterpret_cast<const int*>(DoorRecord(interaction, record) + 4) : -1;
    DOOR_LOG("Doors: close record %d (%.2f s), %s; records%s", record, seconds, DoorPart(vehicle, part).c_str(), DoorRecords(interaction).c_str());
    g_doorCloseAction(interaction, record, seconds);
    DOOR_LOG("Doors: closed record %d: %s", record, DoorPart(vehicle, part).c_str());
}

// For the log: a door's opening as the game measures it (0x6134A0, thiscall (part; vehicle): its cached
// world rotation, +0x58, against its parent's or the vehicle's, turned into the angle about the hinge
// over the hinge's most; with no hinge constraint, +4, it gives the stored t, +0x34; clamped to -1..2,
// 2 when the angle can't be had), latch and unlatch (0x615E00, 0x615CE0, cdecl (part, vehicle): free
// or make the door's body, +0, and hinge constraint, +4).
auto const DoorOpening = reinterpret_cast<float(__fastcall*)(BYTE* part, void* unused, BYTE* vehicle)>(0x6134A0);
constexpr DWORD kLatchDoor = 0x615E00, kUnlatchDoor = 0x615CE0;
constexpr size_t kLatchDoorPrologue = 6, kUnlatchDoorPrologue = 7;  // push ebp / mov ebp,esp / sub esp,8; push ebp / mov ebp,esp / push ecx / mov ecx,[ebp+8]
using LatchDoorFn = void(__cdecl*)(BYTE* part, BYTE* vehicle);
LatchDoorFn g_latchDoor, g_unlatchDoor;

std::string DoorBody(const BYTE* part) {
    const float* r = reinterpret_cast<const float*>(part + 0x58);
    char text[200];
    snprintf(text, sizeof(text), "body %08lX, hinge %08lX, t %.2f, rotation (%.2f %.2f %.2f / %.2f %.2f %.2f / %.2f %.2f %.2f)",
             *reinterpret_cast<const DWORD*>(part), *reinterpret_cast<const DWORD*>(part + 4), *reinterpret_cast<const float*>(part + 0x34),
             r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8]);
    return text;
}

void __cdecl LatchDoor(BYTE* part, BYTE* vehicle) {
    DOOR_LOG("Doors: latch part at +0x%X: %s", static_cast<unsigned>(part - vehicle), DoorBody(part).c_str());
    g_latchDoor(part, vehicle);
    DOOR_LOG("Doors: latched: %s", DoorBody(part).c_str());
}

void __cdecl UnlatchDoor(BYTE* part, BYTE* vehicle) {
    DOOR_LOG("Doors: unlatch part at +0x%X: %s, measured %.2f", static_cast<unsigned>(part - vehicle), DoorBody(part).c_str(), DoorOpening(part, nullptr, vehicle));
    g_unlatchDoor(part, vehicle);
    DOOR_LOG("Doors: unlatched: %s, measured %.2f", DoorBody(part).c_str(), DoorOpening(part, nullptr, vehicle));
}

void __fastcall UpdateDoor(BYTE* part, void* unused, float seconds, BYTE* vehicle) {
    const int before = *reinterpret_cast<const int*>(part + kDoorState);
    if (before == 1) {
        const float* v = reinterpret_cast<const float*>(vehicle + 0x10);
        DOOR_LOG("Doors: part at +0x%X begins opening: measured %.2f, %s, %s; vehicle rotation (%.2f %.2f %.2f / %.2f %.2f %.2f / %.2f %.2f %.2f)",
                 static_cast<unsigned>(part - vehicle), DoorOpening(part, nullptr, vehicle),
                 (*reinterpret_cast<const DWORD*>(part + kPartFlags) & kUnlatched) ? "unlatched" : "latched", DoorBody(part).c_str(),
                 v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]);
    }
    g_updateDoor(part, unused, seconds, vehicle);
    const int after = *reinterpret_cast<const int*>(part + kDoorState);
    if (after != before)
        DOOR_LOG("Doors: part at +0x%X %s -> %s, open %.2f, %s", static_cast<unsigned>(part - vehicle), Named(kDoorStates, 7, before),
                 Named(kDoorStates, 7, after), *reinterpret_cast<const float*>(part + kDoorOpening),
                 (*reinterpret_cast<const DWORD*>(part + kPartFlags) & kUnlatched) ? "unlatched" : "latched");
}

void MeasureDoorsAsGuerrilla();  // below, with the door's measurement

void HookDoors() {
    MeasureDoorsAsGuerrilla();
    g_doorOpenAction = reinterpret_cast<DoorOpenActionFn>(rfa::HookFunction(kDoorOpenAction, kDoorOpenActionPrologue, reinterpret_cast<void*>(&DoorOpenAction)));
    if (!g_doorOpenAction) {
        rfa::Log("Doors: could not wrap the door open action; a door left recorded open won't open again");
        return;
    }
    g_doorLog = rfa::IniInt(L"GuerrillaVehicles", L"DoorLog", 0) != 0;
    if (!g_doorLog) return;
    g_doorCloseAction = reinterpret_cast<DoorCloseActionFn>(rfa::HookFunction(kDoorCloseAction, kDoorCloseActionPrologue, reinterpret_cast<void*>(&DoorCloseAction)));
    g_updateDoor = reinterpret_cast<UpdateDoorFn>(rfa::HookFunction(kUpdateDoor, kUpdateDoorPrologue, reinterpret_cast<void*>(&UpdateDoor)));
    g_latchDoor = reinterpret_cast<LatchDoorFn>(rfa::HookFunction(kLatchDoor, kLatchDoorPrologue, reinterpret_cast<void*>(&LatchDoor)));
    g_unlatchDoor = reinterpret_cast<LatchDoorFn>(rfa::HookFunction(kUnlatchDoor, kUnlatchDoorPrologue, reinterpret_cast<void*>(&UnlatchDoor)));
    rfa::Log("Doors: logging door openings, closings and door parts' states (DoorLog=1; the first %d lines)", kDoorLogLines);
}

// ---- The door's opening, measured as Guerrilla measures it (see the top)

constexpr DWORD kMeasureDoor = 0x6134A0;  // thiscall float (part; vehicle), from the door update 0x612FA0 and 0x613440
const BYTE kMeasureDoorStart[] = {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x9C, 0x00, 0x00, 0x00};  // push ebp / mov ebp,esp / sub esp,9Ch

struct Rotation { float m[9]; };  // rows of three
auto const Rotate = reinterpret_cast<void(__fastcall*)(Rotation* self, void* unused, const Rotation* by)>(0xC9E090);  // matrix::rotate
auto const CapAngle = reinterpret_cast<float(__cdecl*)(float angle, float lowest)>(0xCA5010);  // fl_angle_cap_radians
auto const AxisAndAngle = reinterpret_cast<void(__fastcall*)(const Rotation* self, void* unused, float* axis, float* angle)>(0xC9C8A0);

// Guerrilla's matrix::extract_axis_and_angle (RM 0x5A10C0), in the precision it works in.
void GuerrillaAxisAndAngle(const Rotation& r, float axis[3], float& angle) {
    const float* m = r.m;
    const float trace = static_cast<float>(static_cast<double>(m[0]) + m[4] + m[8]);
    const float cosine = static_cast<float>((static_cast<double>(trace) - 1.0) * 0.5);
    auto none = [&] {
        angle = 0;
        axis[0] = 1;
        axis[1] = axis[2] = 0;
    };
    if (cosine > 0.99999994039535522) return none();
    if (cosine <= -0.99999994039535522) {
        AxisAndAngle(&r, nullptr, axis, &angle);  // a half turn (no door turns so far): the game's own
        return;
    }
    angle = static_cast<float>(acos(static_cast<double>(cosine)));
    axis[0] = static_cast<float>(static_cast<double>(m[5]) - m[7]);
    axis[1] = static_cast<float>(static_cast<double>(m[6]) - m[2]);
    axis[2] = static_cast<float>(static_cast<double>(m[1]) - m[3]);
    const float length2 = static_cast<float>(static_cast<double>(axis[0]) * axis[0] + static_cast<double>(axis[1]) * axis[1] +
                                             static_cast<double>(axis[2]) * axis[2]);
    if (length2 < 1.0e-8f) return none();  // Armageddon's: a half turn
    const float inverse = static_cast<float>(1.0 / static_cast<float>(sqrt(static_cast<double>(length2))));
    for (int k = 0; k < 3; ++k) axis[k] = static_cast<float>(static_cast<double>(axis[k]) * inverse);
}

// Guerrilla's vehicle_component::get_unlatched_door_t_value (RM 0xB7AA70): with no hinge, the stored
// t; else the door's world turn (+0x58) against its parent part's or the vehicle's (+0x10) and its own
// at rest (its data +0x1C), the angle about the hinge's axis (negative when the turn's axis lies along
// it), capped from -pi/2, over the hinge's most; -1 when it can't be had, at most 2.
float __fastcall MeasureDoor(BYTE* part, void* unused, BYTE* vehicle) {
    if (!*reinterpret_cast<void* const*>(part + 4)) return *reinterpret_cast<const float*>(part + 0x34);
    const BYTE* data = *reinterpret_cast<BYTE* const*>(part + 0x50);
    const BYTE* hinge = *reinterpret_cast<BYTE* const*>(data + 0xA0);
    const float* hingeAxis = reinterpret_cast<const float*>(hinge);
    const int parent = *reinterpret_cast<const int*>(data + 0x10);
    const BYTE* parentPart = parent == -1 ? nullptr : Part(vehicle, parent);
    const Rotation parentTurn = *reinterpret_cast<const Rotation*>(parentPart ? parentPart + 0x58 : vehicle + 0x10);
    const Rotation rest = *reinterpret_cast<const Rotation*>(data + 0x1C);
    Rotation turn = *reinterpret_cast<const Rotation*>(part + 0x58);
    Rotate(&turn, nullptr, &parentTurn);
    Rotate(&turn, nullptr, &rest);
    float axis[3], angle;
    GuerrillaAxisAndAngle(turn, axis, angle);
    const float along = static_cast<float>(static_cast<double>(axis[0]) * hingeAxis[0] + static_cast<double>(axis[1]) * hingeAxis[1] +
                                           static_cast<double>(axis[2]) * hingeAxis[2]);
    if (along > 0.7f) angle = -angle;
    angle = CapAngle(angle, -1.5707963705062866f);
    float t = static_cast<float>(static_cast<double>(angle) / *reinterpret_cast<const float*>(hinge + 0x18));
    if (!(static_cast<double>(t) + 1.0 >= 0.0)) t = -1.0f;
    if (static_cast<double>(t) - 2.0 >= 0.0) t = 2.0f;
    return t;
}

void MeasureDoorsAsGuerrilla() {
    if (memcmp(reinterpret_cast<const void*>(kMeasureDoor), kMeasureDoorStart, sizeof(kMeasureDoorStart)) != 0) {
        rfa::Log("Doors: the game's door measurement (0x%08lX) isn't as expected; shut doors may read open", kMeasureDoor);
        return;
    }
    if (rfa::ReplaceFunction(kMeasureDoor, reinterpret_cast<const void*>(&MeasureDoor)))
        rfa::Log("Doors: measured as Guerrilla measures them (a shut door's next-to-no turn is none, not a half turn)");
}

// ---- Room for Guerrilla's vehicles in the world (see the top)

using PoolSetUpFn = void(__fastcall*)(void* pool, void* unused);
using MempoolMakeFn = void*(__fastcall*)(void* mempool, void* unused, void* buffer, DWORD size, const char* name, int one, int zero);
using MempoolEndFn = void(__fastcall*)(void* mempool, void* unused);
using PoolFillFn = void(__fastcall*)(void* pool, void* unused, void* mempool, int count);
auto const PoolSetUp = reinterpret_cast<PoolSetUpFn>(0x71C5D0);
auto const MempoolMake = reinterpret_cast<MempoolMakeFn>(0x428240);
auto const MempoolEnd = reinterpret_cast<MempoolEndFn>(0x427F40);
auto const FillSpawnNodePool = reinterpret_cast<PoolFillFn>(0x805760);
auto const FillAutomobilePool = reinterpret_cast<PoolFillFn>(0x5F9840);
constexpr DWORD kPoolName = 0xF7101C;  // "object pool", the name the game's constructors give

// The two constructors, and the values in them that say they are as expected: each pushes the
// buffer's size (4 bytes) and the count (a one-byte push).
constexpr DWORD kSpawnNodePoolMake = 0x805710, kSpawnNodeBufferSize = 0x80572B, kSpawnNodeCount = 0x80573F;
constexpr DWORD kAutomobilePoolMake = 0x5F97F0, kAutomobileBufferSize = 0x5F980B, kAutomobileCount = 0x5F981F;
constexpr DWORD kSpawnNodeSize = 0xB0, kAutomobileSize = 0x5DE8;
constexpr int kGameSpawnNodes = 12, kSpawnNodes = 128;   // Guerrilla's 128
constexpr int kGameAutomobiles = 16, kAutomobiles = 32;  // Guerrilla's 32

// A slot's bytes: its object, its handle (4) and its byte; then room for the memory pool's alignment.
constexpr DWORD PoolBytes(int count, DWORD size) { return count * (size + 5) + 64; }
alignas(16) BYTE g_spawnNodeBuffer[PoolBytes(kSpawnNodes, kSpawnNodeSize)];
alignas(16) BYTE g_automobileBuffer[PoolBytes(kAutomobiles, kAutomobileSize)];

// What the game's constructor does, on the mod's buffer and with its count.
void* MakePool(void* pool, BYTE* buffer, DWORD size, PoolFillFn fill, int count) {
    PoolSetUp(pool, nullptr);
    alignas(16) BYTE mempool[0x60];  // the game's is 0x58 bytes on its stack
    MempoolMake(mempool, nullptr, buffer, size, reinterpret_cast<const char*>(kPoolName), 1, 0);
    fill(pool, nullptr, mempool, count);
    MempoolEnd(mempool, nullptr);
    return pool;
}
void* __fastcall MakeSpawnNodePool(void* pool, void*) {
    return MakePool(pool, g_spawnNodeBuffer, sizeof(g_spawnNodeBuffer), FillSpawnNodePool, kSpawnNodes);
}
void* __fastcall MakeAutomobilePool(void* pool, void*) {
    return MakePool(pool, g_automobileBuffer, sizeof(g_automobileBuffer), FillAutomobilePool, kAutomobiles);
}

// The vehicle records: the start-up code builds the records (0xF29510: the array constructor 0x422890,
// stdcall (array, 0x1C bytes, 84, the record's constructor 0x75FD20)), then the vehicle resource pool
// on them (0xF29550: 0x7650F0, thiscall (pool, 84, the records, 16, the streamed list)).
using VectorMakeFn = void(__stdcall*)(void* array, DWORD size, int count, void* constructor);
using ResourcePoolMakeFn = void(__fastcall*)(void* pool, void* unused, int count, void* records, int streamed, void* list);
auto const VectorMake = reinterpret_cast<VectorMakeFn>(0x422890);
auto const ResourcePoolMake = reinterpret_cast<ResourcePoolMakeFn>(0x7650F0);
constexpr DWORD kRecordsMake = 0xF29510, kRecordsPoolMake = 0xF29550;
constexpr DWORD kGameRecords = 0x1971FB8, kRecordConstructor = 0x75FD20, kVehicleResourcePool = 0x1972900, kStreamedList = 0x19709C8;
constexpr DWORD kRecordSize = 0x1C;
constexpr int kGameVehicleRecords = 84, kVehicleRecords = 255, kStreamedVehicles = 16;
alignas(16) BYTE g_vehicleRecords[kVehicleRecords * kRecordSize];

void __cdecl MakeVehicleRecords() {
    VectorMake(g_vehicleRecords, kRecordSize, kVehicleRecords, reinterpret_cast<void*>(kRecordConstructor));
}
void __cdecl MakeVehicleResourcePool() {
    ResourcePoolMake(reinterpret_cast<void*>(kVehicleResourcePool), nullptr, kVehicleRecords, g_vehicleRecords, kStreamedVehicles,
                     reinterpret_cast<void*>(kStreamedList));
}

BYTE CodeByte(DWORD address) { return *reinterpret_cast<const BYTE*>(address); }

void MakeRoomForVehiclesInTheWorld() {
    // Each constructor pushes its buffer's size (the game's count × (the object + 5) + alignment) and its count.
    if (rfa::CodeValue(kSpawnNodeBufferSize) == 0x881 && CodeByte(kSpawnNodeCount) == kGameSpawnNodes &&
        rfa::ReplaceFunction(kSpawnNodePoolMake, reinterpret_cast<void*>(&MakeSpawnNodePool)))
        rfa::Log("Vehicle spawn nodes: the game holds %d (its own %d)", kSpawnNodes, kGameSpawnNodes);
    else rfa::Log("Vehicle spawn nodes: the pool's constructor isn't as expected; the game keeps its %d", kGameSpawnNodes);

    if (rfa::CodeValue(kAutomobileBufferSize) == 0x5DED9 && CodeByte(kAutomobileCount) == kGameAutomobiles &&
        rfa::ReplaceFunction(kAutomobilePoolMake, reinterpret_cast<void*>(&MakeAutomobilePool)))
        rfa::Log("Cars: the game holds %d at once (its own %d)", kAutomobiles, kGameAutomobiles);
    else rfa::Log("Cars: the pool's constructor isn't as expected; the game keeps its %d", kGameAutomobiles);

    // The records' constructor (push the record's constructor, push 54h, push 1Ch, push the records)
    // and the pool's (push the list, push 10h, push the records, push 54h).
    const bool recordsAsExpected = rfa::CodeValue(kRecordsMake + 4) == kRecordConstructor && CodeByte(kRecordsMake + 9) == kGameVehicleRecords &&
                                   CodeByte(kRecordsMake + 11) == kRecordSize && rfa::CodeValue(kRecordsMake + 13) == kGameRecords &&
                                   rfa::CodeValue(kRecordsPoolMake + 4) == kStreamedList && CodeByte(kRecordsPoolMake + 9) == kStreamedVehicles &&
                                   rfa::CodeValue(kRecordsPoolMake + 11) == kGameRecords && CodeByte(kRecordsPoolMake + 16) == kGameVehicleRecords;
    if (recordsAsExpected && rfa::ReplaceFunction(kRecordsMake, reinterpret_cast<void*>(&MakeVehicleRecords)) &&
        rfa::ReplaceFunction(kRecordsPoolMake, reinterpret_cast<void*>(&MakeVehicleResourcePool)))
        rfa::Log("Vehicle records: the game holds %d vehicle models (its own %d); %d streamed at once, as the game", kVehicleRecords,
                 kGameVehicleRecords, kStreamedVehicles);
    else rfa::Log("Vehicle records: the start-up code isn't as expected; the game keeps its %d (vehicles past them can't spawn)", kGameVehicleRecords);
}

// ---- The world's lists of spawn nodes and cars, as big as their pools
//
// Besides each type's pool, the world (0x1870630) keeps a list of the objects of each type (+0x68 + type x 12:
// pointer, capacity, count) and stops a zone's load on a full one ("!type_objects[stub->obj_type].full()",
// 0x6D49DB; the user's 21:23 run hung there: object_vehicle_spawn_node 12 of 12). A list's capacity is the
// count its type registers at start-up (0x4FAD90, thiscall: name, five handlers, the object's size, the count,
// one more handler; it only records them, the world sizes the lists from them later), not its pool's size. The
// pools above are made 128 spawn nodes and 32 cars, so their types register those: the cars' count is a one-byte
// value in their registration (`push 10h` at 0xF23058), changed to 32 (checked first); the spawn nodes' (`push
// 0Ch` at 0xF33918) can't hold 128 in one byte, so their registration (0xF33910) is replaced by one making the same
// call with 128, after its bytes are checked to be the game's.
auto const RegisterType = reinterpret_cast<void*(__fastcall*)(void* type, void* unused, const char* name, DWORD a, DWORD b, DWORD c, DWORD d,
                                                               DWORD e, DWORD size, DWORD count, DWORD f)>(0x4FAD90);
constexpr DWORD kCarCountValue = 0xF23059;            // push 10h: automobile's count
constexpr DWORD kSpawnNodeRegistration = 0xF33910;
const BYTE kSpawnNodeRegistrationBytes[] = {0x55, 0x8B, 0xEC, 0x68, 0x30, 0xDB, 0x7F, 0x00, 0x6A, 0x0C, 0x68, 0xC0, 0x08, 0x00, 0x00,
                                            0x68, 0x80, 0xDB, 0x7F, 0x00, 0x68, 0x60, 0xDB, 0x7F, 0x00, 0x68, 0xA0, 0x3E, 0x81, 0x00,
                                            0x68, 0x40, 0xDB, 0x7F, 0x00, 0x68, 0x10, 0xDB, 0x7F, 0x00, 0x68, 0x40, 0x0E, 0xFC, 0x00,
                                            0xB9, 0xD0, 0x8C, 0x10, 0x02, 0xE8};

// object_vehicle_spawn_node's registration, as the game's (0xF33910) but for kSpawnNodes.
void __cdecl RegisterSpawnNodeType() {
    RegisterType(reinterpret_cast<void*>(0x2108CD0), nullptr, reinterpret_cast<const char*>(0xFC0E40), 0x7FDB10, 0x7FDB40, 0x813EA0, 0x7FDB60,
                 0x7FDB80, 0x8C0, static_cast<DWORD>(kSpawnNodes), 0x7FDB30);
}

void RegisterBiggerTypes() {
    if (*reinterpret_cast<const BYTE*>(kCarCountValue) == kGameAutomobiles) {
        const BYTE cars = static_cast<BYTE>(kAutomobiles);
        if (rfa::PatchCode(reinterpret_cast<void*>(kCarCountValue), &cars, 1)) rfa::Log("Cars: the world's list of them holds %d (its own %d)", kAutomobiles, kGameAutomobiles);
        else rfa::Log("Cars: could not change the game's code; the world's list of them holds %d", kGameAutomobiles);
    } else rfa::Log("Cars: the type's registration (0x%08lX) isn't as expected; the world's list of them holds what it says", kCarCountValue);
    if (memcmp(reinterpret_cast<const void*>(kSpawnNodeRegistration), kSpawnNodeRegistrationBytes, sizeof(kSpawnNodeRegistrationBytes)) != 0) {
        rfa::Log("Vehicle spawn nodes: the type's registration (0x%08lX) isn't as expected; the world's list of them holds %d", kSpawnNodeRegistration, kGameSpawnNodes);
        return;
    }
    if (rfa::ReplaceFunction(kSpawnNodeRegistration, reinterpret_cast<void*>(&RegisterSpawnNodeType)))
        rfa::Log("Vehicle spawn nodes: the world's list of them holds %d (its own %d)", kSpawnNodes, kGameSpawnNodes);
    else rfa::Log("Vehicle spawn nodes: could not change the game's code; the world's list of them holds %d", kGameSpawnNodes);
}

// ---- Vehicles the game would refuse for want of their model: reserved (see the top)

constexpr DWORD kSpawnVehicle = 0x761790;     // cdecl (the spawn's parameters, the vehicle's table entry first): 0 made, 1 not yet, 2 refused
constexpr size_t kSpawnVehiclePrologue = 6;   // push ebp / mov ebp,esp / sub esp,0Ch
using SpawnVehicleFn = int(__cdecl*)(void** parameters);
SpawnVehicleFn g_spawnVehicle;
auto const VehicleRecord = reinterpret_cast<void*(__cdecl*)(void* entry)>(0x7618A0);
auto const RecordReserved = reinterpret_cast<bool(__fastcall*)(void* record, void* unused)>(0x75FE90);
auto const RecordInUse = reinterpret_cast<bool(__fastcall*)(void* record, void* unused)>(0x75FEB0);
auto const ReserveInResource = reinterpret_cast<bool(__cdecl*)(int category, int owner, void* entry)>(0x761DC0);
constexpr int kMissionCategory = 8, kReservationOwner = 3;  // as the game's own and the Spawn menu's (rfa_qol)
auto& g_spawnResourceState = *reinterpret_cast<const volatile int*>(0x19734CC);  // 0: no spawn resource in force

int __cdecl SpawnVehicle(void** parameters) {
    const int result = g_spawnVehicle(parameters);
    if (result != 2 || !parameters || !parameters[0] || !g_spawnResourceState) return result;
    void* entry = parameters[0];
    void* record = VehicleRecord(entry);
    if (!record || RecordReserved(record, nullptr) || RecordInUse(record, nullptr)) return result;  // refused for another reason
    const char* name = *reinterpret_cast<const char* const*>(entry);
    if (!ReserveInResource(kMissionCategory, kReservationOwner, entry)) {
        RFA_LOG_FIRST(40, "Spawning: no room to stream %s in (16 models at once); it isn't made", name ? name : "?");
        return result;
    }
    RFA_LOG_FIRST(200, "Spawning: %s reserved; it is made once it has streamed in", name ? name : "?");
    return 1;  // not yet: the spot (or spawner) asks again
}

void ReserveWantedVehicles() {
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C};
    if (memcmp(reinterpret_cast<const void*>(kSpawnVehicle), opening, sizeof(opening)) != 0) {
        rfa::Log("Spawning: the game's vehicle spawn isn't as expected; vehicles whose model isn't streamed aren't made");
        return;
    }
    g_spawnVehicle = reinterpret_cast<SpawnVehicleFn>(rfa::HookFunction(kSpawnVehicle, kSpawnVehiclePrologue, reinterpret_cast<void*>(&SpawnVehicle)));
    rfa::Log(g_spawnVehicle ? "Spawning: a vehicle refused for want of its model reserves it and waits (parked vehicles, traffic)"
                            : "Spawning: could not hook the game's vehicle spawn");
}

// ---- Room for Guerrilla's walkers' blend trees (see the top)

// The table (0x40 bytes a tree), built by the start-up constructor 0xF4A230 (the array constructor 0x422890:
// the table, 0x40, 24, a tree's constructor 0x963420), read by the loader (0x961D70: full at `cmp ecx,18h`,
// the table's address in `add edx,...`) and the look-up by name (0x962980: the table and its first tree's
// +4). Four addresses and two one-byte sizes.
constexpr DWORD kBlendTrees = 0x25CD828, kBlendTreeSize = 0x40;
constexpr int kGameBlendTrees = 24, kBlendTreeCount = 64;
constexpr DWORD kBlendTreeReferences[] = {0xF4A23D, 0x961E50, 0x9629C4, 0x9629AD};  // the last: the table + 4
constexpr DWORD kBlendTreeSizes[] = {0xF4A239, 0x961E35};                         // push 18h, cmp ecx,18h
alignas(16) BYTE g_blendTrees[kBlendTreeCount * kBlendTreeSize];

void MoveBlendTrees() {
    for (DWORD at : kBlendTreeReferences) {
        const DWORD value = rfa::CodeValue(at);
        if (value < kBlendTrees || value >= kBlendTrees + kBlendTreeSize) {
            rfa::Log("Blend trees: the game's code at 0x%08lX isn't as expected; the game keeps its %d", at, kGameBlendTrees);
            return;
        }
    }
    for (DWORD at : kBlendTreeSizes)
        if (CodeByte(at) != kGameBlendTrees) {
            rfa::Log("Blend trees: the game's size at 0x%08lX isn't %d; the game keeps its own", at, kGameBlendTrees);
            return;
        }
    for (DWORD at : kBlendTreeReferences) {
        const DWORD moved = reinterpret_cast<DWORD>(g_blendTrees) + (rfa::CodeValue(at) - kBlendTrees);
        rfa::PatchCode(reinterpret_cast<void*>(at), &moved, sizeof(moved));
    }
    const BYTE count = kBlendTreeCount;
    for (DWORD at : kBlendTreeSizes) rfa::PatchCode(reinterpret_cast<void*>(at), &count, 1);
    rfa::Log("Blend trees: the game holds %d (its own %d, all taken by its own trees)", kBlendTreeCount, kGameBlendTrees);
}

// ---- Guerrilla's lights: when each is on (see the top)

using LightFrameFn = void(__cdecl*)(BYTE* vehicle);
constexpr DWORD kLightFrame = 0x61BA60;          // cdecl (vehicle): its lights, each frame
constexpr size_t kLightFramePrologue = 7;        // push ebp / mov ebp,esp / push ecx / mov eax,[ebp+8]
LightFrameFn g_lightFrame;
auto const ApplyLights = reinterpret_cast<void(__cdecl*)(BYTE* vehicle, int headlights, int blink)>(0x61B280);  // the game's own
auto const VehicleParts = reinterpret_cast<BYTE*(__fastcall*)(BYTE* partsOwner, void* unused)>(0x4F3850);       // on vehicle + 0x1C4
auto const VehicleShown = reinterpret_cast<bool(__fastcall*)(BYTE* vehicle, void* unused)>(0x4C3180);
auto const PartState = reinterpret_cast<DWORD(__fastcall*)(BYTE* part, void* unused)>(0x76A5F0);
auto const StartGlares = reinterpret_cast<void(__fastcall*)(BYTE* light, void* unused, int primary, int secondary, int renderLight, BYTE* vehicle)>(0x5FB700);
auto const StopGlares = reinterpret_cast<void(__fastcall*)(BYTE* light, void* unused)>(0x5FB920);
auto const BrakeLightsOn = reinterpret_cast<bool(__cdecl*)(BYTE* vehicle)>(0x61B9D0);
auto const ObjectSubtype = reinterpret_cast<int(__fastcall*)(BYTE* object, void* unused)>(0x423530);
auto const ObjectHandle = reinterpret_cast<DWORD(__fastcall*)(BYTE* object, void* unused)>(0x424850);
auto const VehicleDriver = reinterpret_cast<int(__cdecl*)(BYTE* vehicle, int seat)>(0x603AE0);
auto const HumanAlive = reinterpret_cast<bool(__cdecl*)(DWORD handle)>(0x5F2B20);
auto const TimerUp = reinterpret_cast<bool(__fastcall*)(BYTE* timer, void* unused)>(0xC8B120);
auto const TimerSet = reinterpret_cast<void(__fastcall*)(BYTE* timer, void* unused, int ms)>(0xC8B030);
auto const BlinkTime = reinterpret_cast<int(__cdecl*)()>(0x61BC80);
// A vehicle: +0x4FB0 bit 13 its engine running; +0x4FB4 bit 13 its headlights, 14 lights by script, 15 the
// blink; +0x5048 the blink's timer; +0x3DF4 its forward speed. Its parts' light instances: count +0x30E8,
// 0x44 bytes each from +0x30EC (+0x04 its vehicle_lights row, +0x08 its part or -1, +0x24 the glow playing,
// +0x2C its file light (+0 Guerrilla's type), +0x40 flags: 2 on, 4 broken, 8 to break, 0x10 changed).
// A vehicle_lights row (36 bytes from *0x14A9F00, how many at 0x14A9F04): +0 its name, +0x14, +0x18 its
// glows, +0x1C its render light. The game clock: the hour 0x12F76D4 (a byte), its speed 0x12F76D8; the
// frame's time 0x11217C8.
constexpr DWORD kLightRows = 0x14A9F00, kLightRowCount = 0x14A9F04, kHour = 0x12F76D4, kClockSpeed = 0x12F76D8, kFrameTime = 0x11217C8;
constexpr float kReverseSpeed = -1.0f;  // Guerrilla's: reversing below this forward speed
constexpr DWORD kHeadlightMesh = 0x05C0B2B7, kFoglightMesh = 0x35DAB18F, kTaillightMesh = 0x85C7B697, kBrakelightMesh = 0x13E6369F,
                kReverselightMesh = 0x86A3E58C, kEmergencylightMesh = 0x1BB5B29A, kStrobelightMesh = 0xDE8638BA;

struct LightState {
    DWORD handle;
    LONGLONG seen;
};
std::vector<LightState> g_lightStates;  // Guerrilla's lit vehicles seen (their headlights set at first sight)
int g_brakeGlow = -2;                   // the rfg_Brake_light row's glow (-2: not looked up yet)

int BrakeGlow() {
    if (g_brakeGlow != -2) return g_brakeGlow;
    g_brakeGlow = -1;
    const BYTE* rows = *reinterpret_cast<BYTE* const*>(kLightRows);
    const int count = *reinterpret_cast<const int*>(kLightRowCount);
    for (int i = 0; rows && i < count; ++i) {
        const char* name = *reinterpret_cast<const char* const*>(rows + i * 36);
        if (name && _stricmp(name, "rfg_Brake_light") == 0) g_brakeGlow = *reinterpret_cast<const int*>(rows + i * 36 + 0x14);
    }
    return g_brakeGlow;
}

bool Bit(const BYTE* vehicle, DWORD at, int bit) { return (*reinterpret_cast<const DWORD*>(vehicle + at) >> bit) & 1; }
void SetBit(BYTE* vehicle, DWORD at, int bit, bool on) {
    DWORD& word = *reinterpret_cast<DWORD*>(vehicle + at);
    word = on ? word | (1u << bit) : word & ~(1u << bit);
}

// Guerrilla's: night is from 18:00 to 6:00.
bool Night() { const int hour = *reinterpret_cast<const BYTE*>(kHour); return hour >= 18 || hour <= 6; }

void RefreshRender(BYTE* vehicle) {
    auto refresh = reinterpret_cast<void(__fastcall*)(BYTE*, void*, int)>((*reinterpret_cast<void***>(vehicle))[0x44 / 4]);
    refresh(vehicle, nullptr, 8);
}

// Guerrilla's vehicle_effects_process_lights, on the game's light instances: each light on by its type's
// rule, with its row's glows (a tail light while braking with the brake light's), as the game's own
// (0x61B280) turns them on, swaps and off.
void __cdecl LightFrame(BYTE* vehicle) {
    BYTE* parts = vehicle ? VehicleParts(vehicle + 0x1C4, nullptr) : nullptr;
    const int count = parts ? *reinterpret_cast<const int*>(parts + 0x30E8) : 0;
    bool ours = false, breaking = false;
    for (int i = 0; i < count; ++i) {
        const BYTE* light = parts + 0x30EC + i * 0x44;
        const BYTE* row = *reinterpret_cast<BYTE* const*>(light + 4);
        const char* name = row ? *reinterpret_cast<const char* const*>(row) : nullptr;
        if (name && _strnicmp(name, "rfg_", 4) == 0) ours = true;
        if (light[0x40] & 8) breaking = true;
    }
    if (!ours) {
        g_lightFrame(vehicle);
        return;
    }
    // The headlights: at first sight as the hour says; then, when the hour says otherwise, they change at a
    // random moment (Guerrilla: within an hour of game time, max_delay 1.0).
    LARGE_INTEGER now, frequency;
    QueryPerformanceCounter(&now);
    QueryPerformanceFrequency(&frequency);
    const DWORD handle = ObjectHandle(vehicle, nullptr);
    auto state = std::find_if(g_lightStates.begin(), g_lightStates.end(), [&](const LightState& s) { return s.handle == handle; });
    bool headlights = Bit(vehicle, 0x4FB4, 13);
    if (state == g_lightStates.end()) {
        g_lightStates.erase(std::remove_if(g_lightStates.begin(), g_lightStates.end(),
                                           [&](const LightState& s) { return now.QuadPart - s.seen > 10 * frequency.QuadPart; }), g_lightStates.end());
        g_lightStates.push_back({handle, now.QuadPart});
        headlights = Night();
    } else {
        state->seen = now.QuadPart;
        const float chance = *reinterpret_cast<const float*>(kClockSpeed) * *reinterpret_cast<const float*>(kFrameTime) / 3600.0f;
        if (headlights != Night() && static_cast<float>(std::rand()) / RAND_MAX < chance) headlights = !headlights;
    }
    // The blink (Guerrilla's emergency lights), every half second, as the game's.
    bool blink = Bit(vehicle, 0x4FB4, 15);
    if (TimerUp(vehicle + 0x5048, nullptr)) {
        blink = !blink;
        TimerSet(vehicle + 0x5048, nullptr, BlinkTime());
    }
    if (breaking) {  // the game's own breaks them (its death effect); Guerrilla's rules again next frame
        ApplyLights(vehicle, headlights, blink);
        return;
    }
    SetBit(vehicle, 0x4FB4, 13, headlights);
    SetBit(vehicle, 0x4FB4, 15, blink);
    const bool running = Bit(vehicle, 0x4FB0, 13) || Bit(vehicle, 0x4FB4, 14);
    const bool braking = BrakeLightsOn(vehicle);
    const bool reversing = ObjectSubtype(vehicle, nullptr) == 5 && VehicleDriver(vehicle, 0) && HumanAlive(ObjectHandle(vehicle, nullptr)) &&
                           *reinterpret_cast<const float*>(vehicle + 0x3DF4) < kReverseSpeed;
    const bool shown = VehicleShown(vehicle, nullptr);
    bool changed = false;
    for (int i = 0; i < count; ++i) {
        BYTE* light = parts + 0x30EC + i * 0x44;
        const BYTE* row = *reinterpret_cast<BYTE* const*>(light + 4);
        const BYTE* file = *reinterpret_cast<BYTE* const*>(light + 0x2C);
        if (!row || !file || (light[0x40] & 4)) continue;
        int primary = *reinterpret_cast<const int*>(row + 0x14), secondary = *reinterpret_cast<const int*>(row + 0x18);
        const int render = *reinterpret_cast<const int*>(row + 0x1C);
        bool on = false;
        switch (*reinterpret_cast<const DWORD*>(file)) {
        case kHeadlightMesh:
        case kFoglightMesh: on = running && headlights; break;
        case kTaillightMesh:
            if (braking) { on = true; primary = BrakeGlow(); secondary = -1; }
            else on = running && headlights;
            break;
        case kBrakelightMesh: on = braking; break;
        case kReverselightMesh: on = reversing; break;
        case kEmergencylightMesh: on = running && blink; break;
        case kStrobelightMesh: on = running; break;
        default: on = running && headlights; break;
        }
        const int part = *reinterpret_cast<const int*>(light + 8);
        if (!shown || (part != -1 && (PartState(parts + 8 + part * 0xB8, nullptr) & 0x4020000))) on = false;
        const bool lit = light[0x40] & 2;
        if (on && !lit) {
            StartGlares(light, nullptr, primary, secondary, render, vehicle);
            light[0x40] |= 2 | 0x10;
            changed = true;
        } else if (on && *reinterpret_cast<const int*>(light + 0x24) != primary) {
            StopGlares(light, nullptr);
            StartGlares(light, nullptr, primary, secondary, render, vehicle);
        } else if (!on && lit) {
            StopGlares(light, nullptr);
            light[0x40] = static_cast<BYTE>((light[0x40] & ~2) | 0x10);
            changed = true;
        }
    }
    if (changed) RefreshRender(vehicle);
}

void HookLights() {
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x51, 0x8B, 0x45, 0x08};
    if (memcmp(reinterpret_cast<const void*>(kLightFrame), opening, sizeof(opening)) != 0) {
        rfa::Log("Lights: the game's light frame isn't as expected; Guerrilla's vehicles' lights follow Armageddon's rules");
        return;
    }
    g_lightFrame = reinterpret_cast<LightFrameFn>(rfa::HookFunction(kLightFrame, kLightFramePrologue, reinterpret_cast<void*>(&LightFrame)));
    rfa::Log(g_lightFrame ? "Lights: Guerrilla's vehicles' lights on by Guerrilla's rules (headlights at night, tail lights brake, emergency lights blink)"
                          : "Lights: could not hook the game's light frame");
}

// ---- Guerrilla's engine sounds: the player's own vehicle, the load eased (see the top)

using EngineFrameFn = void(__cdecl*)(void* vehicle);
constexpr DWORD kEngineFrame = 0x5FA1F0;       // cdecl (vehicle): the vehicle's sounds each frame
constexpr size_t kEngineFramePrologue = 9;     // push ebp / mov ebp,esp / sub esp,1B0h
constexpr DWORD kSetEngineValues = 0x5FABA0;   // cdecl (the engine's sound, speed, RPM, load, damage)
auto const PlayerInVehicle = reinterpret_cast<bool(__cdecl*)(void* vehicle)>(0x605550);
auto const SoundObject = reinterpret_cast<DWORD(__cdecl*)(int sound)>(0xB84D20);  // the sound's Wwise game object
auto const SetGameParameter = reinterpret_cast<void(__cdecl*)(DWORD parameter, float value, DWORD gameObject)>(0xB852C0);
// The game's game parameters' ids, which it works out while it starts: RPM, ENGINE_LOAD, SPEED, DAMAGE.
constexpr DWORD kRpmId = 0x13032A8, kLoadId = 0x1303278, kSpeedId = 0x13032AC, kDamageId = 0x130326C;

// Wwise's id of a name: FNV-1 (32 bits) of its lower-case letters (as the game's GetIDFromString).
constexpr DWORD WwiseId(const char* name) {
    DWORD h = 2166136261u;
    for (; *name; ++name) h = (h * 16777619u) ^ static_cast<BYTE>(*name >= 'A' && *name <= 'Z' ? *name + 32 : *name);
    return h;
}
constexpr DWORD kPlayerInVehicleId = WwiseId("RFG_PLAYER_IN_VEHICLE");  // the banks' (GuerrillaVehicleSounds)

EngineFrameFn g_engineFrame;
bool g_playerInThisVehicle;  // while the game does a vehicle's sounds

struct EasedEngine {
    float load;
    LONGLONG at;  // when it was last set (performance counter)
};
std::vector<std::pair<DWORD, EasedEngine>> g_easedEngines;  // by the engine sound's game object

void __cdecl EngineFrame(void* vehicle) {
    g_playerInThisVehicle = vehicle && PlayerInVehicle(vehicle);
    g_engineFrame(vehicle);
    g_playerInThisVehicle = false;
}

// What the game does (each value on the sound's game object), and Guerrilla's: the load eased toward the
// game's, 0.12 of the way each 10 ms (Guerrilla's audio thread's), and RFG_PLAYER_IN_VEHICLE.
void __cdecl SetEngineValues(int sound, float speed, float rpm, float load, float damage) {
    const DWORD object = SoundObject(sound);
    LARGE_INTEGER now, frequency;
    QueryPerformanceCounter(&now);
    QueryPerformanceFrequency(&frequency);
    auto found = std::find_if(g_easedEngines.begin(), g_easedEngines.end(), [&](const auto& e) { return e.first == object; });
    float eased = load;
    if (found != g_easedEngines.end()) {
        const double ms = static_cast<double>(now.QuadPart - found->second.at) * 1000.0 / static_cast<double>(frequency.QuadPart);
        if (ms < 1000) eased = found->second.load + (load - found->second.load) * static_cast<float>(1 - std::pow(0.88, ms / 10));
        found->second = {eased, now.QuadPart};
    } else {
        // Forget engines not heard of for 10 s (their vehicles gone).
        g_easedEngines.erase(std::remove_if(g_easedEngines.begin(), g_easedEngines.end(), [&](const auto& e) {
                                 return (now.QuadPart - e.second.at) > 10 * frequency.QuadPart; }), g_easedEngines.end());
        g_easedEngines.push_back({object, {load, now.QuadPart}});
    }
    SetGameParameter(*reinterpret_cast<const DWORD*>(kRpmId), rpm, object);
    SetGameParameter(*reinterpret_cast<const DWORD*>(kLoadId), eased, object);
    SetGameParameter(*reinterpret_cast<const DWORD*>(kSpeedId), speed, object);
    SetGameParameter(*reinterpret_cast<const DWORD*>(kDamageId), damage, object);
    SetGameParameter(kPlayerInVehicleId, g_playerInThisVehicle ? 1.0f : 0.0f, object);
    // What the game feeds the player's own vehicle's engine sound, once a second (at most 600 lines): for Round 1's
    // "engine silent" in the Kitsune, whose bank, events, loops and volumes are all as built.
    if (g_playerInThisVehicle) {
        static LONGLONG lastLogged;
        if (now.QuadPart - lastLogged > frequency.QuadPart) {
            lastLogged = now.QuadPart;
            RFA_LOG_FIRST(600, "Engine sounds: the player's vehicle: sound %d (object 0x%08lX), RPM %.0f, load %.2f (eased %.2f), speed %.1f, damage %.2f",
                          sound, object, rpm, load, eased, speed, damage);
        }
    }
}

void HookEngineSounds() {
    // The function's code as expected: its opening, and the four ids it reads.
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x51, 0x8B, 0x45, 0x08};
    if (memcmp(reinterpret_cast<const void*>(kSetEngineValues), opening, sizeof(opening)) != 0 || rfa::CodeValue(0x5FABC0) != kRpmId ||
        rfa::CodeValue(0x5FABDA) != kLoadId || rfa::CodeValue(0x5FABF3) != kSpeedId || rfa::CodeValue(0x5FAC0D) != kDamageId) {
        rfa::Log("Engine sounds: the game's code isn't as expected; Guerrilla's engines play as the game sets them (the others' 3D sound in the player's vehicle too)");
        return;
    }
    g_engineFrame = reinterpret_cast<EngineFrameFn>(rfa::HookFunction(kEngineFrame, kEngineFramePrologue, reinterpret_cast<void*>(&EngineFrame)));
    if (!g_engineFrame || !rfa::ReplaceFunction(kSetEngineValues, reinterpret_cast<void*>(&SetEngineValues))) {
        rfa::Log("Engine sounds: could not hook the vehicles' sounds; Guerrilla's engines play as the game sets them");
        return;
    }
    rfa::Log("Engine sounds: the load eased as Guerrilla's, and RFG_PLAYER_IN_VEHICLE set on each vehicle's engine (1 for the player's own)");
}

// ---- A log of the turrets' item parts as the game makes them
//
// For Round 3's Kitsune with no turret to be seen: each base, middle or top a turret makes from an item (0x83D250,
// cdecl: the turret's physics, the turret, the part (0 base, 1 middle, 2 top), its collision filter, a position;
// true if made; a part that's one of the vehicle's own is made elsewhere, 0x83C310). The turret's info is the
// turret's +0x98: its name at +0, each part's item at +0xD8 + 8 x part (Guerrilla's item_info: name +0, static mesh +4,
// character mesh +8; the game draws a part from the static mesh alone), the weapon's info at +0x104, whose item
// (+0x14) the game takes for a top the table names none for.
constexpr DWORD kMakeTurretPart = 0x83D250;
constexpr size_t kMakeTurretPartPrologue = 9;  // push ebp / mov ebp,esp / sub esp,124h
using MakeTurretPartFn = bool(__cdecl*)(void* physics, BYTE* turret, int part, DWORD filter, void* where);
MakeTurretPartFn g_makeTurretPart;

const char* Text(const BYTE* record, DWORD at) {
    const char* text = record ? *reinterpret_cast<const char* const*>(record + at) : nullptr;
    return text && *text ? text : "-";
}

bool __cdecl MakeTurretPart(void* physics, BYTE* turret, int part, DWORD filter, void* where) {
    const bool made = g_makeTurretPart(physics, turret, part, filter, where);
    const BYTE* info = turret ? *reinterpret_cast<const BYTE* const*>(turret + 0x98) : nullptr;
    if (info && part >= 0 && part <= 2) {
        static const char* const kParts[] = {"base", "middle", "top"};
        const BYTE* item = *reinterpret_cast<const BYTE* const*>(info + 0xD8 + part * 8);
        const BYTE* weapon = *reinterpret_cast<const BYTE* const*>(info + 0x104);
        const BYTE* weaponItem = !item && part == 2 && weapon ? *reinterpret_cast<const BYTE* const*>(weapon + 0x14) : nullptr;
        const BYTE* shown = item ? item : weaponItem;
        RFA_LOG_FIRST(300, "Turrets: %s's %s %s: item %s%s (static mesh %s, character mesh %s)", Text(info, 0), kParts[part],
                      made ? "made" : "NOT made", Text(shown, 0), weaponItem ? " (the weapon's, the table names none)" : "", Text(shown, 4), Text(shown, 8));
    }
    return made;
}

void HookTurretParts() {
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x24, 0x01, 0x00, 0x00};
    if (memcmp(reinterpret_cast<const void*>(kMakeTurretPart), opening, sizeof(opening)) != 0) {
        rfa::Log("Turrets: the game's turret part code isn't as expected; their parts aren't logged");
        return;
    }
    g_makeTurretPart = reinterpret_cast<MakeTurretPartFn>(rfa::HookFunction(kMakeTurretPart, kMakeTurretPartPrologue, reinterpret_cast<void*>(&MakeTurretPart)));
    if (!g_makeTurretPart) rfa::Log("Turrets: could not hook the turret part code; their parts aren't logged");
}

// ---- Dents (see the top)

constexpr DWORD kDeformationEnabled = 0x142A484;  // vehicle_deformation_enabled, a bool
// The address's 4 bytes in each instruction that reads it: the console toggle (0x5FB9E0), the
// vehicle loader's (0x622110) and the impact code's (0x60F590).
constexpr DWORD kDeformationReads[] = {0x5FB9E6, 0x6221CC, 0x61052D};

void TurnOnDents() {
    for (DWORD at : kDeformationReads) {
        if (rfa::CodeValue(at) != kDeformationEnabled) {
            rfa::Log("Dents: the game's code at 0x%08lX isn't as expected (0x%08lX); vehicles keep their shape", at, rfa::CodeValue(at));
            return;
        }
    }
    *reinterpret_cast<bool*>(kDeformationEnabled) = true;
    rfa::Log("Dents: vehicle_deformation_enabled on (Guerrilla's vehicles dent where they're hit)");
}

// ---- Room for the vehicle interactions
//
// vehicle_interactions.xtbl's reader (0x65F360), run by its set-up (0x660710, at start-up), keeps the table in
// two places, each sized for Armageddon's handful of interactions:
// - its records, waypoints and transitions in one block of 64 KB taken from the Permanent Mempool (`push 10000h`
//   at 0x660719, kept at 0x14E696C, used [0x14E6970]): 32 bytes a record (count + 3), 36 a waypoint, 48 a
//   transition, 12 a step of a transition (Interactions/Interaction). After each it checks the use against
//   10000h (`cmp [14E6970h],10000h` at 0x65F4C9, 0x65F4FE, 0x65F7AA, 0x66001E, 0x6604E5) and, once over, stops
//   reading: the rest of the table is left out, its records without names. Ours need about 70 KB: the block
//   ran out in the 37th interaction (Col_TrashTruck_1) and the last 4 (from Min_DumpTruck_1) had no name;
// - their names (each interaction's, waypoint's Name and Parent_Component, transition's Door_N) in a string
//   pool, "Vehicle interaction info strings" (0x14E6990), of 8 KB: `push 2000h` at 0x660733 (the memory, kept
//   at 0x14E6968) and at 0x660760 (the pool); ours take about 5.5 KB.
// A record with no name ended the game when a vehicle's interaction was looked up (0x628461, _stricmp with a
// null: the user's 20:02 run). The user, 2026-11-06: "Yes, raise it": the block 256 KB (the six values) and
// the pool 32 KB (the two), each group checked against the game's values before any is written, all or
// nothing; the log says how much of each the table took, and names any record still without a name.
constexpr DWORD kInteractionBlockValues[] = {0x66071A, 0x65F4CF, 0x65F504, 0x65F7B0, 0x660024, 0x6604EB};  // push / cmp ...,10000h
constexpr DWORD kInteractionBlockMemory = 0x14E696C, kInteractionBlockUsed = 0x14E6970;
constexpr DWORD kGameInteractionBlock = 0x10000, kInteractionBlock = 0x40000;
constexpr DWORD kInteractionNameValues[] = {0x660734, 0x660761};  // push 2000h: the memory, the string pool made over it
constexpr DWORD kInteractionNamesPool = 0x14E6990;              // the pool (size +0x44, bytes used +0x4C)
constexpr DWORD kInteractionNamesMemory = 0x14E6968;
constexpr DWORD kGameInteractionNames = 0x2000, kInteractionNames = 0x8000;
constexpr DWORD kInteractionSetup = 0x660710;
constexpr size_t kInteractionSetupPrologue = 5;  // push ebp / mov ebp,esp / push 0
using InteractionSetupFn = void(__cdecl*)();
InteractionSetupFn g_interactionSetup;
DWORD g_interactionBlock = kGameInteractionBlock;

// The interactions' records (32 bytes each, the name first): how many, and where (the reader's).
constexpr DWORD kInteractionCount = 0x14E6960, kInteractionRecords = 0x14E6964;

void __cdecl InteractionSetup() {
    g_interactionSetup();
    const int count = *reinterpret_cast<const int*>(kInteractionCount);
    BYTE* records = *reinterpret_cast<BYTE**>(kInteractionRecords);
    rfa::Log("Vehicle interactions: %d read; their records, waypoints and transitions %lu of %lu bytes, their names %lu of %lu bytes", count,
             *reinterpret_cast<const DWORD*>(kInteractionBlockUsed), g_interactionBlock,
             *reinterpret_cast<const DWORD*>(kInteractionNamesPool + 0x4C), *reinterpret_cast<const DWORD*>(kInteractionNamesPool + 0x44));
    // A record left without a name (the reader stopped before it, or the pool refused it) ends the game when a
    // vehicle's interaction is looked up (_stricmp with a null). Each one is named "" instead, and said here: its
    // vehicle then finds no interaction (no getting in) rather than stopping the game.
    static const char kNoName[] = "";
    int unnamed = 0, first = -1;
    for (int i = 0; records && i < count; ++i) {
        const char*& name = *reinterpret_cast<const char**>(records + i * 32);
        if (name) continue;
        name = kNoName;
        if (first < 0) first = i;
        ++unnamed;
    }
    if (unnamed > 0)
        rfa::Log("Vehicle interactions: %d of the %d have no name (the first, number %d: the reader stopped, or the names' pool refused it); "
                 "their vehicles can't be got into", unnamed, count, first);
}

// Writes `value` over each of `places` once all hold `game`; all or nothing. False (and nothing changed) otherwise.
template <size_t N>
bool RaiseAll(const char* what, const DWORD (&places)[N], DWORD game, DWORD value) {
    for (DWORD at : places)
        if (rfa::CodeValue(at) != game) {
            rfa::Log("%s: the game's code at 0x%08lX isn't as expected (0x%08lX); it keeps its %lu bytes", what, at, rfa::CodeValue(at), game);
            return false;
        }
    for (size_t i = 0; i < N; ++i)
        if (!rfa::PatchCode(reinterpret_cast<void*>(places[i]), &value, sizeof(value))) {
            for (size_t k = 0; k < i; ++k) rfa::PatchCode(reinterpret_cast<void*>(places[k]), &game, sizeof(game));
            rfa::Log("%s: could not change the game's code; it keeps its %lu bytes", what, game);
            return false;
        }
    rfa::Log("%s: %lu bytes (the game's %lu)", what, value, game);
    return true;
}

void MoreInteractionRoom() {
    if (*reinterpret_cast<const DWORD*>(kInteractionNamesMemory) != 0 || *reinterpret_cast<const DWORD*>(kInteractionBlockMemory) != 0) {
        rfa::Log("Vehicle interactions: the game has already set them up; it keeps its %lu and %lu bytes", kGameInteractionBlock, kGameInteractionNames);
        return;
    }
    if (RaiseAll("Vehicle interactions' records, waypoints and transitions", kInteractionBlockValues, kGameInteractionBlock, kInteractionBlock))
        g_interactionBlock = kInteractionBlock;
    RaiseAll("Vehicle interaction names", kInteractionNameValues, kGameInteractionNames, kInteractionNames);
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x6A, 0x00};
    if (memcmp(reinterpret_cast<const void*>(kInteractionSetup), opening, sizeof(opening)) == 0)
        g_interactionSetup = reinterpret_cast<InteractionSetupFn>(rfa::HookFunction(kInteractionSetup, kInteractionSetupPrologue, reinterpret_cast<void*>(&InteractionSetup)));
}

// ---- Room for the vehicle spawn groups
//
// spawn_group_vehicle.xtbl's groups (each a list of vehicles a spawn node, a squad that rides or the game's own
// code picks from) live in a fixed pool, "vehicle group" (0x19691C0), of 32 places: its start-up constructor
// (0x7581D0) lays a memory pool over a buffer inside the object (+0x30, `push 4A5h`) and sets the pool up for
// `push 20h` places (a one-byte value) of 37 bytes (a 32-byte group, a 4-byte link, a 1-byte flag). The reader
// (0x757670) asks it for a place for each group and stops the game when there is none ("NULL != veh_group_info",
// int 3 at 0x757748: the user's 20:50 run). Armageddon's own 23 groups fit; with Guerrilla's 108 they're 131.
// Guerrilla's own pool holds 136 (object_pool<vehicle_spawn_group_info,136>). The user, 2026-11-06: OK'd: the mod
// runs the constructor itself, as the gear part does for its pools: the pool's base, a memory pool over the mod's
// buffer, the pool's set-up for 136, the memory pool dropped; the constructor's own values checked first.
constexpr DWORD kGroupPool = 0x19691C0;
constexpr DWORD kGroupPoolConstructor = 0x7581D0;
constexpr size_t kGroupPoolConstructorPrologue = 6;  // push ebp / mov ebp,esp / sub esp,5Ch
constexpr DWORD kGameGroups = 32, kGroups = 136;
constexpr DWORD kGroupPlace = 32 + 4 + 1;            // a group, its link, its flag
constexpr DWORD kGameGroupMemory = 0x4A5;
// The constructor's own values: its buffer's size (push 4A5h), its count (push 20h, one byte) and the buffer's
// place in the object (add eax,30h).
constexpr DWORD kGroupMemoryValue = 0x7581EB, kGroupCountValue = 0x7581FF, kGroupBufferValue = 0x7581F4;
alignas(16) BYTE g_groupPoolMemory[kGroups * kGroupPlace + 64];

auto const PoolBase = reinterpret_cast<void(__fastcall*)(BYTE* pool, void* unused)>(0x71C5D0);
auto const MempoolOver = reinterpret_cast<void(__fastcall*)(void* mempool, void* unused, void* memory, DWORD size, const void* name,
                                                             DWORD first, DWORD second)>(0x428240);
auto const MempoolDone = reinterpret_cast<void(__fastcall*)(void* mempool, void* unused)>(0x427F40);
auto const GroupPoolInit = reinterpret_cast<void(__fastcall*)(BYTE* pool, void* unused, void* mempool, DWORD holds)>(0x758220);
const void* const kObjectPoolName = reinterpret_cast<const void*>(0xF7101C);  // "object pool", the name the game gives it

BYTE* __fastcall GroupPoolConstructor(BYTE* pool, void* unused) {
    PoolBase(pool, nullptr);
    alignas(16) BYTE mempool[0x60];  // the game's takes 0x58 bytes of its stack
    MempoolOver(mempool, nullptr, g_groupPoolMemory, sizeof(g_groupPoolMemory), kObjectPoolName, 1, 0);
    GroupPoolInit(pool, nullptr, mempool, kGroups);
    MempoolDone(mempool, nullptr);
    return pool;
}

// After the groups are read (0x757670, cdecl: -1 at start-up), how full the pool is: a byte a place, 0xFF while free.
constexpr DWORD kGroupReader = 0x757670;
constexpr size_t kGroupReaderPrologue = 9;  // push ebp / mov ebp,esp / sub esp,110h
using GroupReaderFn = void(__cdecl*)(int which);
GroupReaderFn g_groupReader;

void __cdecl GroupReader(int which) {
    g_groupReader(which);
    const WORD holds = *reinterpret_cast<const WORD*>(kGroupPool + 0xA);
    const BYTE* flags = *reinterpret_cast<BYTE* const*>(kGroupPool + 0x18);
    int used = 0;
    for (int i = 0; flags && i < holds; ++i)
        if (flags[i] != 0xFF) ++used;
    rfa::Log("Vehicle spawn groups: %d of %u places in use", used, holds);
}

void MoreSpawnGroups() {
    if (*reinterpret_cast<const WORD*>(kGroupPool + 0xA) != 0) {
        rfa::Log("Vehicle spawn groups: the game has already built its pool; it keeps its %lu", kGameGroups);
        return;
    }
    if (rfa::CodeValue(kGroupMemoryValue) != kGameGroupMemory || *reinterpret_cast<const BYTE*>(kGroupCountValue) != kGameGroups ||
        *reinterpret_cast<const BYTE*>(kGroupBufferValue) != 0x30) {
        rfa::Log("Vehicle spawn groups: the game's constructor (0x%08lX) isn't as expected; it keeps its %lu", kGroupPoolConstructor, kGameGroups);
        return;
    }
    if (!rfa::HookFunction(kGroupPoolConstructor, kGroupPoolConstructorPrologue, reinterpret_cast<void*>(&GroupPoolConstructor))) {
        rfa::Log("Vehicle spawn groups: could not change the game's code; it keeps its %lu", kGameGroups);
        return;
    }
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x10, 0x01, 0x00, 0x00};
    if (memcmp(reinterpret_cast<const void*>(kGroupReader), opening, sizeof(opening)) == 0)
        g_groupReader = reinterpret_cast<GroupReaderFn>(rfa::HookFunction(kGroupReader, kGroupReaderPrologue, reinterpret_cast<void*>(&GroupReader)));
    rfa::Log("Vehicle spawn groups: the game holds %lu (its own %lu)", kGroups, kGameGroups);
}

// ---- Guerrilla's vehicles' own skeletons (see the top)

// A check of the doors' animations (getting into cars stops at the door: the game finds no "door
// start" trigger on the person's animation, 0xD8B260). Once the animation tables are read (just
// before the skeletons load), the log says how full the animation library is (0x34B2C10, of 4500;
// 32-byte entries from 0x34C7570; names in a 9000-place hash, 0xD8C2C0) and the game's animation
// entries (0x25CD738, of 4224), and, for a few door animations (the APC's, which work, and the
// cars'), whether each is in the library and how many triggers it has (entry +0x10).
auto const FindAnimation = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD8C2C0);
auto const AnimationEntry = reinterpret_cast<const BYTE*(__cdecl*)(int index)>(0xD87850);
void LogDoorAnimations() {
    rfa::Log("Door check: the animation library holds %d of %lu, the game's animation entries %d of %lu; \"door start\" is trigger %d",
             *reinterpret_cast<const int*>(0x34B2C10), rfa::CodeValue(kLibraryAnimationLimits[0]), *reinterpret_cast<const int*>(0x25CD738),
             rfa::CodeValue(kAnimationEntryLimits[0]), *reinterpret_cast<const int*>(0x25CFD88));
    for (const char* name : {"pc_enter_lav.anim", "hvy_wlkr_wlk.anim", "apc_dr_opn_htch.anim", "apc_dr_exit_cab.anim",
                             "taxi_dr_opn_door.anim", "taxi_dr_exit_cab.anim", "mnatv_dr_opn_l.anim", "mnatv_dr_exit_l.anim"}) {
        const int index = FindAnimation(name);
        const BYTE* entry = index >= 0 ? AnimationEntry(index) : nullptr;
        if (!entry) { rfa::Log("Door check: %s isn't in the library", name); continue; }
        const int count = *reinterpret_cast<const int*>(entry + 0x10);
        const int* triggers = *reinterpret_cast<const int* const*>(entry + 0x14);
        std::string list;
        for (int i = 0; i < count && i < 8 && triggers; ++i) list += " " + std::to_string(triggers[2 * i]) + "@" + std::to_string(triggers[2 * i + 1]);
        rfa::Log("Door check: %s is library entry %d with %d triggers (trigger@time:%s)", name, index, count, list.c_str());
    }
}

void __cdecl LoadSkeletons() {
    g_loadSkeletons();
    for (const std::string& skeleton : g_skeletons) {
        LoadSkeleton(skeleton.c_str());
        if (FindSkeleton(skeleton.c_str()) >= 0) rfa::Log("Skeletons: %s loaded", skeleton.c_str());
        else rfa::Log("Skeletons: %s could not be loaded (a vehicle that names it will stop the game)", skeleton.c_str());
    }
    LogDoorAnimations();
}

// The skeletons GuerrillaBuilder.exe listed, one file name a line: the vehicles', and Guerrilla's guns'
// (mods\guerrilla_gear\rigs.txt: a gun of character mesh, such as the shotgun, has a skeleton of its own,
// looked up by name when its container loads, so it too must be loaded with the game's).
void ReadSkeletonList() {
    for (const wchar_t* file : {L"\\mods\\guerrilla_vehicles\\rigs.txt", L"\\mods\\guerrilla_gear\\rigs.txt"}) {
        std::ifstream list(rfa::RootDir() + file);
        for (std::string line; std::getline(list, line);) {
            while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
            if (!line.empty() && line.size() < 64 && std::find(g_skeletons.begin(), g_skeletons.end(), line) == g_skeletons.end())
                g_skeletons.push_back(line);
        }
    }
}

}  // namespace

void StartTraffic();  // traffic.cpp (with convoys.cpp, edf.cpp, crews.cpp on its frame)
namespace gv { void StartFlyers(); }  // flyers.cpp

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    rfa::MoveTable("Animation sets", kAnimSets, kAnimSetSize, kGameMaxAnimSets, kAnimSetReferences, kAnimSetLimits, g_animSets, kMaxAnimSets);
    rfa::MoveTable("Bone infos", kBoneInfos, kBoneInfoSize, kGameMaxBoneInfos, kBoneInfoReferences, kBoneInfoLimits, g_boneInfos, kMaxBoneInfos);
    rfa::MoveTable("Animation triggers", kTriggers, kTriggerSize, kGameMaxTriggers, kTriggerReferences, kTriggerLimits, g_triggers, kMaxTriggers);
    rfa::MoveTable("Animation entries", kAnimationEntries, kAnimationEntrySize, kGameMaxAnimationEntries, kAnimationEntryReferences,
                   kAnimationEntryLimits, g_animationEntries, kMaxAnimationEntries);
    MoveAnimationLibrary();
    g_buildVehicleList = reinterpret_cast<BuildVehicleListFn>(rfa::HookFunction(kBuildVehicleList, kBuildVehicleListPrologue, reinterpret_cast<void*>(&BuildVehicleList)));
    if (!g_buildVehicleList) rfa::Log("Vehicles: could not hook the vehicle list's building; the game keeps its %d", kGameMaxVehicles);
    MakeRoomForVehiclesInTheWorld();
    RegisterBiggerTypes();
    MoreInteractionRoom();
    MoreSpawnGroups();
    MoveBlendTrees();
    ReserveWantedVehicles();
    HookEngineSounds();
    HookTurretParts();
    HookLights();
    StartTraffic();
    gv::StartFlyers();
    if (rfa::IniInt(L"GuerrillaVehicles", L"Dents", 1) != 0) TurnOnDents();
    else rfa::Log("Dents: off ([GuerrillaVehicles] Dents=0)");
    HookDoors();
    ReadSkeletonList();
    if (!g_skeletons.empty()) {
        g_loadSkeletons = reinterpret_cast<LoadSkeletonsFn>(rfa::HookFunction(kLoadSkeletons, kLoadSkeletonsPrologue, reinterpret_cast<void*>(&LoadSkeletons)));
        if (g_loadSkeletons) rfa::Log("Skeletons: %u of Guerrilla's vehicles' own to load after the game's", static_cast<unsigned>(g_skeletons.size()));
        else rfa::Log("Skeletons: could not hook the game's loading of them; vehicles with skeletons of their own will stop the game");
    }
    bool any = false;
    for (VehicleKey& given : g_vehicleKeys) {
        std::wstring name = rfa::IniString(L"GuerrillaVehicles", given.setting, given.fallback);
        given.mouse = name.empty() ? -1 : MouseByName(name);
        given.key = name.empty() || given.mouse >= 0 ? 0 : KeyByName(name);
        if (!name.empty() && !given.key && given.mouse < 0)
            rfa::Log("Vehicles: no key named \"%ls\" for %ls; it keeps the game's binding", name.c_str(), given.setting);
        any = any || given.key || given.mouse >= 0;
    }
    if (!any) {
        rfa::Log("No vehicle keys under [GuerrillaVehicles]; nothing to do");
        return;
    }
    g_buildKeyMap = reinterpret_cast<BuildKeyMapFn>(rfa::HookFunction(kBuildKeyMap, kBuildKeyMapPrologue, reinterpret_cast<void*>(&BuildKeyMap)));
    if (!g_buildKeyMap) {
        rfa::Log("Could not hook the key map's building; cars keep no driving keys and can't be left");
        return;
    }
    rfa::Log("Vehicle keys for actions without their own: accelerate, brake, handbrake, get out, change seat (Guerrilla's W, S, Space, E, Mouse3 unless set)");
}
