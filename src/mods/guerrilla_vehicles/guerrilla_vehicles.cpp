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
// Space for the handbrake. The mod gives each of the three actions its key ([GuerrillaVehicles]
// Accelerate=, Brake=, Handbrake=: a key's name as the console's controls_show_key_bindings prints it)
// whenever the game builds its key map from the bindings (0x4E86E0), if it has none; a key the player
// has bound is left as it is.
//
// The bindings: one control set per player (0xCF0 bytes from 0x11BEBA0), 139 button actions of 20
// bytes (+0 the gamepad button, +8 the keyboard key as a DirectInput scancode, 0 for none, +0xC the
// mouse button), then the axis actions. The game's names for keys are a table of 256 strings at
// 0x1121900, by scancode.
#define RFA_MOD_NAME "guerrilla_vehicles"
#include "rfa_mod.h"
#include "rfa_game.h"
#include <cwctype>
#include <string>

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
constexpr int kControlSets = 2;

constexpr DWORD kKeyNames = 0x1121900;  // const char*[256], by scancode

// ---- Driving

struct DrivingKey {
    int action;            // the button action
    const wchar_t* setting;
    const wchar_t* fallback;  // Guerrilla's PC default
    DWORD key;             // the scancode given, 0 if none
};
DrivingKey g_drivingKeys[] = {
    {62, L"Accelerate", L"W", 0},
    {63, L"Brake", L"S", 0},
    {64, L"Handbrake", L"Spacebar", 0},
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

void __cdecl BuildKeyMap(int controlSet) {
    if (controlSet >= 0 && controlSet < kControlSets) {
        for (const DrivingKey& driving : g_drivingKeys) {
            if (!driving.key) continue;
            auto* key = reinterpret_cast<DWORD*>(kBindings + controlSet * kControlSetSize + driving.action * kButtonBindingSize + kKeyField);
            if (*key) continue;
            *key = driving.key;
            const char* name = reinterpret_cast<const char* const*>(kKeyNames)[driving.key];
            rfa::Log("Driving: %ls on %s (control set %d; it had no key)", driving.setting, name ? name : "?", controlSet);
        }
    }
    g_buildKeyMap(controlSet);
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    bool any = false;
    for (DrivingKey& driving : g_drivingKeys) {
        std::wstring name = rfa::IniString(L"GuerrillaVehicles", driving.setting, driving.fallback);
        driving.key = name.empty() ? 0 : KeyByName(name);
        if (!name.empty() && !driving.key) rfa::Log("Driving: no key named \"%ls\" for %ls; it keeps the game's binding", name.c_str(), driving.setting);
        any = any || driving.key;
    }
    if (!any) {
        rfa::Log("No driving keys under [GuerrillaVehicles]; nothing to do");
        return;
    }
    g_buildKeyMap = reinterpret_cast<BuildKeyMapFn>(rfa::HookFunction(kBuildKeyMap, kBuildKeyMapPrologue, reinterpret_cast<void*>(&BuildKeyMap)));
    if (!g_buildKeyMap) {
        rfa::Log("Could not hook the key map's building; cars keep no driving keys");
        return;
    }
    rfa::Log("Driving keys for cars without their own: accelerate, brake and handbrake (Guerrilla's W, S, Space unless set)");
}
