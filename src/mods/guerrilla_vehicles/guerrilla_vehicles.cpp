// What Red Faction: Guerrilla's vehicles need from Armageddon's engine that it doesn't do itself.
// The vehicles are made from the player's own copy of Guerrilla by GuerrillaArsenal.exe (never
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
#define RFA_MOD_NAME "guerrilla_vehicles"
#include "rfa_mod.h"
#include "rfa_game.h"
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

DWORD ValueAt(DWORD address) { return *reinterpret_cast<const DWORD*>(address); }

// Moves one of the game's tables (`name` in the log) from `game`, of `holds` entries of `entrySize`
// bytes, to `table`, of `newHolds`: each of `references` (the 4-byte values in the game's code that
// hold the table's address, or a field's in its first entry) gets the new table's, each of `limits`
// the new size. All are checked against the game's values first, and none changed if one differs.
template <size_t References, size_t Limits>
bool MoveTable(const char* name, DWORD game, DWORD entrySize, DWORD holds, const DWORD (&references)[References],
               const DWORD (&limits)[Limits], BYTE* table, DWORD newHolds) {
    for (DWORD at : references) {
        const DWORD value = ValueAt(at);
        if (value < game || value >= game + entrySize) {
            rfa::Log("%s: the game's code at 0x%08lX isn't as expected (0x%08lX); the game keeps its %lu", name, at, value, holds);
            return false;
        }
    }
    for (DWORD at : limits) {
        if (ValueAt(at) != holds) {
            rfa::Log("%s: the game's limit at 0x%08lX isn't %lu (it's %lu); the game keeps its own", name, at, holds, ValueAt(at));
            return false;
        }
    }
    const DWORD moved = reinterpret_cast<DWORD>(table);
    for (DWORD at : references) {
        const DWORD value = moved + (ValueAt(at) - game);
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &value, sizeof(value))) {
            rfa::Log("%s: could not change the game's code at 0x%08lX (part of it already points at the new table)", name, at);
            return false;
        }
    }
    for (DWORD at : limits) {
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &newHolds, sizeof(newHolds))) {
            rfa::Log("%s: could not change the game's limit at 0x%08lX", name, at);
            return false;
        }
    }
    rfa::Log("%s: the game holds %lu (its own %lu): the table moved to 0x%08lX, %u places in its code changed", name,
             newHolds, holds, moved, static_cast<unsigned>(References + Limits));
    return true;
}

// ---- Room for more vehicles (see the top)

BYTE* __fastcall BuildVehicleList(BYTE* list, void*) {
    if (reinterpret_cast<DWORD>(list) != kVehicleList) return g_buildVehicleList(list, nullptr);
    for (int i = 0; i < kMaxVehicles; ++i) BuildVehicle(g_vehicles + i * kVehicleSize, nullptr);
    SetUpList(list, nullptr, g_vehicles, kMaxVehicles, 0);
    rfa::Log("Vehicles: the game's list holds %d (its own %d), at 0x%08lX", kMaxVehicles, kGameMaxVehicles,
             reinterpret_cast<DWORD>(g_vehicles));
    return list;
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
    rfa::Log("Door check: the animation library holds %d of 4500, the game's animation entries %d of 4224; \"door start\" is trigger %d",
             *reinterpret_cast<const int*>(0x34B2C10), *reinterpret_cast<const int*>(0x25CD738), *reinterpret_cast<const int*>(0x25CFD88));
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

// The skeletons GuerrillaArsenal.exe listed, one file name a line.
void ReadSkeletonList() {
    std::ifstream list(rfa::RootDir() + L"\\mods\\guerrilla_vehicles\\rigs.txt");
    for (std::string line; std::getline(list, line);) {
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        if (!line.empty() && line.size() < 64) g_skeletons.push_back(line);
    }
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    MoveTable("Animation sets", kAnimSets, kAnimSetSize, kGameMaxAnimSets, kAnimSetReferences, kAnimSetLimits, g_animSets, kMaxAnimSets);
    MoveTable("Bone infos", kBoneInfos, kBoneInfoSize, kGameMaxBoneInfos, kBoneInfoReferences, kBoneInfoLimits, g_boneInfos, kMaxBoneInfos);
    MoveTable("Animation triggers", kTriggers, kTriggerSize, kGameMaxTriggers, kTriggerReferences, kTriggerLimits, g_triggers, kMaxTriggers);
    g_buildVehicleList = reinterpret_cast<BuildVehicleListFn>(rfa::HookFunction(kBuildVehicleList, kBuildVehicleListPrologue, reinterpret_cast<void*>(&BuildVehicleList)));
    if (!g_buildVehicleList) rfa::Log("Vehicles: could not hook the vehicle list's building; the game keeps its %d", kGameMaxVehicles);
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
