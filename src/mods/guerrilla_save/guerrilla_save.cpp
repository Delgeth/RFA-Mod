// Saving in the worlds ported from Red Faction: Guerrilla ([GuerrillaSave] Worlds=, the ported world is
// "rfg").
//
// Armageddon saves its whole world into one stream in memory (the world object 0x1870630, +0x248C its
// size, 1 MB at most) and writes that stream into a save slot. Its campaign makes the stream at each
// checkpoint; its pause menu then offers SAVE GAME (the save screen writes the stream into the slot you
// pick) and LOAD CHECKPOINT (the world opened again from the stream). The rfg world is a level opened
// outside the campaign, so nothing ever made a stream there and the pause menu offered no save.
//
// Here, in those worlds:
//   - the pause menu's SAVE GAME (ui_pause_menu_pc.lua) asks the world for a save of the moment you are
//     in (rfg_save_game) and leaves the menu as Armageddon's SAVE GAME does; the menu's script waits
//     while the world makes it (rfg_save_state, a few frames: the gameplay step that makes it runs while
//     the game is paused), then opens Armageddon's own save screen, and the slot you pick gets it. LOAD
//     CHECKPOINT then goes back to the last save made. (The save screen borrows its hint bar from the
//     pause menu's document, so it is opened from there, the game's own way: opened with the menu
//     closed, its script failed on Vdo_hint_bar and the game crashed.)
//   - a save is never written into a slot on its own. Armageddon writes every checkpoint into its
//     autosave slot as soon as it is made (the save job's last step, 0x6D7000, when the world's
//     +0x2515 bit 1 says the profile owns its saves); that slot is the campaign's, so in these worlds
//     the bit is cleared and the stream stays in memory until you save it.
//   - a save of one of those worlds opens by its level's name (see OpenWorld), so that its zones get the
//     Guerrilla world's size as when the world is started; with the empty name the game gives a save,
//     the load hung.
//
// The game's save, found in its exe (see "Saving in the rfg world" in docs\game-notes.md):
//   world 0x6DA1B0(message, b) asks for one: +0x2514 bit 2, +0x2515 bit 0 the "Checkpoint" message,
//     bit 1 "write it into the autosave slot when done";
//   the gameplay step 0x542110 (through 0x542030 and 0x6DA0E0) starts it: +0x2514 bit 4;
//   the world's streaming (0x6D70D0) then makes it: 0x6D7E20 writes the header and the game's systems
//     (+0x2514 bit 5 while it is being made), the zone stream-out jobs the rest, and the last job's end
//     sets the size (+0x248C), the streaming state (+0x23F4) back to 0 and clears bits 4 and 5.
#define RFA_MOD_NAME "guerrilla_save"
#include "rfa_mod.h"
#include "rfa_game.h"

#include <string>
#include <vector>

namespace {

constexpr DWORD kWorld = 0x1870630;
constexpr DWORD kWorldName = kWorld + 0x29EA;     // "rfg" in the ported world, "world" in the campaign's
constexpr DWORD kStreamingState = kWorld + 0x23F4; // 0 when the world isn't streaming zones out
constexpr DWORD kSavedSize = kWorld + 0x248C;      // the save in memory: its size, 0 for none
constexpr DWORD kSavedRoom = kWorld + 0x2490;      // and the room for it (0x100000)
constexpr DWORD kSaveFlags = kWorld + 0x2514;
constexpr DWORD kSaveFlags2 = kWorld + 0x2515;
constexpr BYTE kSaveAsked = 0x04;     // +0x2514 bit 2
constexpr BYTE kSaveStarted = 0x10;   // +0x2514 bit 4
constexpr BYTE kSaveBeingMade = 0x20; // +0x2514 bit 5
constexpr BYTE kWriteToAutosave = 0x02; // +0x2515 bit 1

constexpr DWORD kAskForSave = 0x6DA1B0;      // thiscall (world, bool message, bool b)
constexpr size_t kAskForSavePrologue = 6;    // push ebp / mov ebp,esp / sub esp,8
constexpr DWORD kMakeSave = 0x6D7E20;        // thiscall bool (world): the save's header and systems
constexpr size_t kMakeSavePrologue = 9;      // push ebp / mov ebp,esp / sub esp,0E8h
constexpr DWORD kOpenWorld = 0x6C91F0;       // thiscall int (world, name, bool fresh, bool b): opens a world
constexpr size_t kOpenWorldPrologue = 9;     // push ebp / mov ebp,esp / sub esp,1BCh
constexpr DWORD kSaveBuffer = 0x6CCF20;      // thiscall BYTE* (world): the save in memory's bytes
constexpr DWORD kSaveBytes = kWorld + 0x2494; // where that pointer is kept (+0x248C size, +0x2490 room)
constexpr DWORD kSaveMagic = 0x53474652;     // "RFGS"
constexpr int kSaveVersion = 0x5E;
constexpr DWORD kLocalPlayer = 0x8B92D0;     // object*(): the local player, or null
constexpr DWORD kSavegameOwned = 0x10FA278;  // byte: what savegame_is_owned() returns
constexpr DWORD kSaveWaitLimitMs = 10000;   // the first save in rfg took 15 ms

using AskForSaveFn = void(__thiscall*)(void* world, bool message, bool b);
using MakeSaveFn = bool(__thiscall*)(void* world);
using OpenWorldFn = int(__thiscall*)(void* world, const char* name, bool fresh, bool b);
using SaveBufferFn = const BYTE*(__thiscall*)(void* world);
using LocalPlayerFn = void*(__cdecl*)();
using IsDeadFn = bool(__thiscall*)(void* object);

AskForSaveFn g_askForSave = nullptr;
MakeSaveFn g_makeSave = nullptr;
OpenWorldFn g_openWorld = nullptr;
char g_savedLevel[64] = {};  // the name a save of a Guerrilla world opens with

std::vector<std::string> g_worlds;

// The save SAVE GAME asked for: being made, or how it came out ("saved" or "failed").
bool g_waitingToSave = false;
bool g_saveStarted = false;  // the game's 0x6D7E20 ran for it and wrote the header and systems
DWORD g_askedAt = 0;
const char* g_saveOutcome = "failed";

BYTE& Byte(DWORD address) { return *reinterpret_cast<BYTE*>(address); }
int Int(DWORD address) { return *reinterpret_cast<const int*>(address); }
void* World() { return reinterpret_cast<void*>(kWorld); }

std::vector<std::string> SplitList(const std::wstring& text) {
    std::vector<std::string> names;
    std::string current;
    for (wchar_t c : text) {
        if (c == L',' || c == L' ' || c == L'\t') {
            if (!current.empty()) names.push_back(current);
            current.clear();
        } else {
            current += static_cast<char>(c);
        }
    }
    if (!current.empty()) names.push_back(current);
    return names;
}

bool InGuerrillaWorld() {
    const char* name = reinterpret_cast<const char*>(kWorldName);
    for (const std::string& world : g_worlds)
        if (_stricmp(world.c_str(), name) == 0) return true;
    return false;
}

bool SaveRunning() {
    return (Byte(kSaveFlags) & (kSaveAsked | kSaveStarted | kSaveBeingMade)) != 0 || Int(kStreamingState) != 0;
}

// Why SAVE GAME can't be offered now, or nullptr when it can. The player must be there and alive, as
// for the campaign's checkpoints ("Skipping checkpoint saving due to the player being dead").
const char* WhyNoSave() {
    if (!InGuerrillaWorld()) return "not in a Guerrilla world";
    if (!Byte(kSavegameOwned)) return "the profile doesn't own its saves (savegame_is_owned)";
    void* player = reinterpret_cast<LocalPlayerFn>(kLocalPlayer)();
    if (!player) return "no player";
    void** vtable = *reinterpret_cast<void***>(player);
    if (reinterpret_cast<IsDeadFn>(vtable[4])(player)) return "the player is dead";
    if (g_waitingToSave || SaveRunning()) return "a save is being made";
    return nullptr;
}

// In a Guerrilla world, a save asked for is kept in memory: never written into the autosave slot.
void KeepSaveInMemory() {
    if (InGuerrillaWorld()) Byte(kSaveFlags2) &= static_cast<BYTE>(~kWriteToAutosave);
}

void __fastcall AskForSave(void* world, void* /*unused*/, bool message, bool b) {
    g_askForSave(world, message, b);
    KeepSaveInMemory();
}

bool __fastcall MakeSave(void* world, void* /*unused*/) {
    KeepSaveInMemory();
    const bool made = g_makeSave(world);
    if (g_waitingToSave) {
        g_saveStarted = made;
        if (!made) rfa::Log("The world couldn't start the save (0x6D7E20 said no; see the lines above)");
    }
    return made;
}

// Whether a level's name ("rfg_07_11") is one of the Guerrilla worlds'.
bool LevelIsOurs(const char* level) {
    for (const std::string& world : g_worlds) {
        const size_t n = world.size();
        if (_strnicmp(level, world.c_str(), n) == 0 && (level[n] == 0 || level[n] == '_')) return true;
    }
    return false;
}

// The level the save in memory was made in, from its header: 0x6D7E20 writes "RFGS", the version, the
// mode, then the name of the zone the player stood in as a string (2 bytes its length, the letters, a 0;
// a save made in rfg: 09 00 "rfg_07_09" 00). "" when there is no such save.
std::string SavedLevel(void* world) {
    const int size = Int(kSavedSize);
    if (size < 16) return "";
    const BYTE* data = *reinterpret_cast<const BYTE* const*>(kSaveBytes);
    if (!data) data = reinterpret_cast<SaveBufferFn>(kSaveBuffer)(world);  // as the world's open does next
    if (!data) return "";
    if (*reinterpret_cast<const DWORD*>(data) != kSaveMagic || *reinterpret_cast<const int*>(data + 4) != kSaveVersion)
        return "";
    const int length = *reinterpret_cast<const unsigned short*>(data + 12);
    if (length == 0 || length >= 64 || 14 + length > size) return "";
    return std::string(reinterpret_cast<const char*>(data + 14), length);
}

// A world opened from a save in memory (LOAD CHECKPOINT, or a slot, which the game reads into memory
// first) comes with an empty name: the save names its level, which the open reads only after it has
// asked for the zones' size by that empty name (0x6C95DD: 0x6C0D60(name, &size)). guerrilla_world gives
// the Guerrilla worlds Guerrilla's zone size by their names, so a save of one got Armageddon's 255 m:
// the streaming grids too small for its zones, and the open stopped in init_territory_streaming_resources
// (0x6DB0C0 reading a grid cell its zone wasn't put in; the load hung). Here such a save opens with its
// level's name, which the open uses for nothing else (it would read a save file only for a name ending
// in rfgs_pc).
int __fastcall OpenWorld(void* world, void* /*unused*/, const char* name, bool fresh, bool b) {
    if (!fresh && name && name[0] == 0) {
        const std::string level = SavedLevel(world);
        if (!level.empty() && LevelIsOurs(level.c_str())) {
            strncpy_s(g_savedLevel, level.c_str(), _TRUNCATE);
            rfa::Log("Opening a save made in %s: opened by that name, so its zones get the Guerrilla world's size",
                     g_savedLevel);
            return g_openWorld(world, g_savedLevel, fresh, b);
        }
    }
    return g_openWorld(world, name, fresh, b);
}

// How the save SAVE GAME asked for stands: "saving" while the world makes it, then "saved" or "failed".
const char* SaveState() {
    if (!g_waitingToSave) return g_saveOutcome;
    const DWORD waited = GetTickCount() - g_askedAt;
    if (SaveRunning()) {
        if (waited <= kSaveWaitLimitMs) return "saving";
        rfa::Log("The save wasn't done after %lu s (flags 0x%02X 0x%02X, streaming %d); stopped waiting", waited / 1000,
                 Byte(kSaveFlags), Byte(kSaveFlags2), Int(kStreamingState));
    } else if (!g_saveStarted) {
        rfa::Log("The world didn't make the save (flags 0x%02X 0x%02X)", Byte(kSaveFlags), Byte(kSaveFlags2));
    } else if (Int(kSavedSize) <= 0) {
        rfa::Log("The world's save came out empty (it may not fit in its %d KB)", Int(kSavedRoom) / 1024);
    } else {
        rfa::Log("Saved in memory: %d KB of %d KB, in %lu ms; the save screen opens", Int(kSavedSize) / 1024,
                 Int(kSavedRoom) / 1024, waited);
        g_saveOutcome = "saved";
        g_waitingToSave = false;
        return g_saveOutcome;
    }
    g_saveOutcome = "failed";
    g_waitingToSave = false;
    return g_saveOutcome;
}

// Lua: rfg_save_can_save(): in a Guerrilla world, true when SAVE GAME can save the moment you are in,
// false when not now; elsewhere nothing (nil), and the pause menu offers Armageddon's own SAVE GAME.
int __cdecl LuaCanSave(rfa::game::lua_State* L) {
    if (!InGuerrillaWorld()) return 0;
    const char* why = WhyNoSave();
    if (why) RFA_LOG_FIRST(20, "SAVE GAME not offered: %s", why);
    rfa::game::lua_pushboolean(L, why == nullptr);
    return 1;
}

// Lua: rfg_save_game() asks the world for a save of the moment you are in. True when asked.
int __cdecl LuaSaveGame(rfa::game::lua_State* L) {
    const char* why = WhyNoSave();
    if (why) {
        rfa::Log("SAVE GAME: no save (%s)", why);
        rfa::game::lua_pushboolean(L, 0);
        return 1;
    }
    g_waitingToSave = true;
    g_saveStarted = false;
    g_askedAt = GetTickCount();
    reinterpret_cast<AskForSaveFn>(kAskForSave)(World(), false, false);
    rfa::Log("SAVE GAME: asked the world for a save (flags 0x%02X 0x%02X)", Byte(kSaveFlags), Byte(kSaveFlags2));
    rfa::game::lua_pushboolean(L, 1);
    return 1;
}

// Lua: rfg_save_state() gives "saving", "saved" or "failed" for the save rfg_save_game asked for.
int __cdecl LuaSaveState(rfa::game::lua_State* L) {
    rfa::game::lua_pushstring(L, SaveState());
    return 1;
}

// The first bytes of each hooked function, as in the supported build.
bool Starts(DWORD address, std::initializer_list<BYTE> bytes) {
    const BYTE* code = reinterpret_cast<const BYTE*>(address);
    for (BYTE b : bytes)
        if (*code++ != b) return false;
    return true;
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_RegisterLua(rfa::game::lua_State* L) {
    rfa::game::lua_register(L, "rfg_save_can_save", LuaCanSave);
    rfa::game::lua_register(L, "rfg_save_game", LuaSaveGame);
    rfa::game::lua_register(L, "rfg_save_state", LuaSaveState);
}

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    g_worlds = SplitList(rfa::IniString(L"GuerrillaSave", L"Worlds", L"rfg"));
    if (!Starts(kAskForSave, {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08}) ||
        !Starts(kMakeSave, {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0xE8, 0x00, 0x00, 0x00}) ||
        !Starts(kOpenWorld, {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0xBC, 0x01, 0x00, 0x00})) {
        rfa::Log("The game's save functions aren't as expected; saving is left as the game has it");
        return;
    }
    g_askForSave = reinterpret_cast<AskForSaveFn>(
        rfa::HookFunction(kAskForSave, kAskForSavePrologue, reinterpret_cast<const void*>(&AskForSave)));
    g_makeSave = reinterpret_cast<MakeSaveFn>(
        rfa::HookFunction(kMakeSave, kMakeSavePrologue, reinterpret_cast<const void*>(&MakeSave)));
    g_openWorld = reinterpret_cast<OpenWorldFn>(
        rfa::HookFunction(kOpenWorld, kOpenWorldPrologue, reinterpret_cast<const void*>(&OpenWorld)));
    if (!g_askForSave || !g_makeSave || !g_openWorld) {
        rfa::Log("Could not hook the game's save functions; saving in the Guerrilla worlds is off");
        return;
    }
    rfa::Log("Saving in the Guerrilla worlds: SAVE GAME in the pause menu saves the moment you are in; saves stay in "
             "memory until you pick a slot (never written into the campaign's autosave slot)");
}
