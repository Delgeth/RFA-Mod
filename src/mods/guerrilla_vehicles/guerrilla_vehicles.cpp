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

// ---- Room for more animation sets (see the top)

DWORD ValueAt(DWORD address) { return *reinterpret_cast<const DWORD*>(address); }

bool MoveAnimSets() {
    for (DWORD at : kAnimSetReferences) {
        const DWORD value = ValueAt(at);
        if (value < kAnimSets || value >= kAnimSets + kAnimSetSize) {
            rfa::Log("Animation sets: the game's code at 0x%08lX isn't as expected (0x%08lX); the game keeps its %lu sets", at, value, kGameMaxAnimSets);
            return false;
        }
    }
    for (DWORD at : kAnimSetLimits) {
        if (ValueAt(at) != kGameMaxAnimSets) {
            rfa::Log("Animation sets: the game's limit at 0x%08lX isn't %lu (it's %lu); the game keeps its sets", at, kGameMaxAnimSets, ValueAt(at));
            return false;
        }
    }
    const DWORD table = reinterpret_cast<DWORD>(g_animSets);
    for (DWORD at : kAnimSetReferences) {
        const DWORD moved = table + (ValueAt(at) - kAnimSets);
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &moved, sizeof(moved))) {
            rfa::Log("Animation sets: could not change the game's code at 0x%08lX (part of it already points at the new table)", at);
            return false;
        }
    }
    for (DWORD at : kAnimSetLimits) {
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &kMaxAnimSets, sizeof(kMaxAnimSets))) {
            rfa::Log("Animation sets: could not change the game's limit at 0x%08lX", at);
            return false;
        }
    }
    rfa::Log("Animation sets: the game holds %lu (its own %lu): the table moved to 0x%08lX, %u places in its code changed",
             kMaxAnimSets, kGameMaxAnimSets, table, static_cast<unsigned>(std::size(kAnimSetReferences) + std::size(kAnimSetLimits)));
    return true;
}

// ---- Guerrilla's vehicles' own skeletons (see the top)

void __cdecl LoadSkeletons() {
    g_loadSkeletons();
    for (const std::string& skeleton : g_skeletons) {
        LoadSkeleton(skeleton.c_str());
        if (FindSkeleton(skeleton.c_str()) >= 0) rfa::Log("Skeletons: %s loaded", skeleton.c_str());
        else rfa::Log("Skeletons: %s could not be loaded (a vehicle that names it will stop the game)", skeleton.c_str());
    }
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
    MoveAnimSets();
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
