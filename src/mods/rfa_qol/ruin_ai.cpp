// Enemies that fight in Ruin levels. A part of rfa_qol.dll; [Mods] ruin_ai=0 turns it off, and the
// setting itself is [Ruin] HostileAI= (0 the game's way, the default; 1 enemies fight), which Options >
// Experimentals changes while you play.
//
// The game's own Ruin levels have no enemies, and Ruin mode switches the AI's hostility off where it
// asks "is this Ruin?" (ruin_is_ruin_mode's own test, 0x50E1D0, a byte at 0x12F3A73):
//   0x8CBAC0  whether one character is hostile to another: in Ruin always "no", before the teams
//             are looked at (its call at 0x8CBAD5);
//   0x734EF0  an AI's awareness of a target: always "not aware" in Ruin (0x734F42);
//   0x874E60  a character's reaction to being hit: skipped in Ruin (0x874E71);
//   0x85E0C0  the damage a hit does: none in Ruin for one kind of hit (0x85E166).
// A level made with the editor can have squads (squad spawn nodes), and they then walk about but never
// fight: you are never their enemy. With the setting on, those four calls ask our function instead,
// which answers "not Ruin" while Ruin's enemies are wanted and the game's own answer otherwise; every
// other Ruin rule (the score, the timer, the menus) still sees Ruin.
#define RFA_MOD_NAME "ruin_ai"
#include "rfa_mod.h"
#include "rfa_qol.h"

namespace ruin_ai {
namespace {

constexpr DWORD kRuinMode = 0x50E1D0;
constexpr DWORD kCalls[] = {0x8CBAD5, 0x734F42, 0x874E71, 0x85E166};

auto const RuinMode = reinterpret_cast<bool(__cdecl*)()>(kRuinMode);

bool g_hostile = false;

void ReadSetting() { g_hostile = rfa::IniInt(L"Ruin", L"HostileAI", 0) != 0; }

// What the four calls ask now: Ruin, unless its enemies are to fight.
bool __cdecl RuinModeForAi() { return g_hostile ? false : RuinMode(); }

}  // namespace

void Init() {
    ReadSetting();
    int patched = 0;
    for (DWORD at : kCalls) {
        BYTE* call = reinterpret_cast<BYTE*>(at);
        // Only the call we expect (E8 to 0x50E1D0); anything else is left alone.
        if (call[0] != 0xE8 || at + 5 + *reinterpret_cast<int32_t*>(call + 1) != kRuinMode) {
            rfa::Log("0x%08X isn't the call to 0x%08X it should be; left as it is", static_cast<unsigned>(at),
                     static_cast<unsigned>(kRuinMode));
            continue;
        }
        BYTE bytes[5] = {0xE8};
        const int32_t relative = static_cast<int32_t>(reinterpret_cast<DWORD>(&RuinModeForAi) - (at + 5));
        memcpy(bytes + 1, &relative, 4);
        if (rfa::PatchCode(call, bytes, sizeof(bytes))) ++patched;
    }
    rfa::Log("%d of %d of the AI's Ruin checks ask us; enemies %s in Ruin levels ([Ruin] HostileAI=%d)", patched,
             static_cast<int>(sizeof(kCalls) / sizeof(kCalls[0])), g_hostile ? "fight" : "don't fight (the game's way)", g_hostile ? 1 : 0);
}

void SettingChanged(const char* section, const char* key) {
    if (_stricmp(section, "Ruin") != 0 || _stricmp(key, "HostileAI") != 0) return;
    ReadSetting();
    rfa::Log("enemies %s in Ruin levels now", g_hostile ? "fight" : "don't fight");
}

}  // namespace ruin_ai
