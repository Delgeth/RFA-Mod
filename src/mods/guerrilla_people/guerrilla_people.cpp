// What Red Faction: Guerrilla's people need from Armageddon's engine that it doesn't do itself. The
// people are made from the player's own copy of Guerrilla (never shipped) by GuerrillaBuilder.exe: see
// RfaFormats\Guerrilla\GuerrillaPeople.cs. Armageddon's character code is Guerrilla's with fields
// added (its character table reader, 0x8860D0 and 0x886840, is Guerrilla's load_human_info_block),
// and it takes each character's bones from that character's own skeleton, so Guerrilla's people keep
// their own meshes, skeletons and animations. So far the mod gives them:
//
// Their skeletons: the game loads every skeleton its archives list while it starts (0x973460), and when
// it reads the character table it takes each character's from those, by name. Guerrilla's people's
// (greene.rig_pc, ...: none of them Armageddon's) are new files GuerrillaBuilder.exe writes into
// overrides\build\pc\cache\, in no archive's list, named in mods\guerrilla_people\rigs.txt; the mod has
// the game load each of those by name (0x973550) just before it reads its character table (0x8860D0,
// cdecl, three arguments: the first time it is called, for character.xtbl).
//
// The world's spawn resource: where a level's spawn resource is in force (the pools' state, 0x19734CC,
// isn't 0: Wrecking Crew levels, our worlds among them), the game makes only the people the resource
// holds and loads no other (its human create, 0x762D50, gives up on one that isn't loaded or on its
// way: 0x762230, 0x764ED0). The Guerrilla world's zones name its own resource, rfg_Default, which
// lists every person GuerrillaBuilder.exe made, so the game loads them itself (the mod no longer
// holds them in the level's category, 0x761D20(8, 3, ...), as it once did). Where the game would load
// its fallback resource instead, Armageddon's "Default", the mod gives it rfg_Default (see "The level's
// spawn resource" below). It writes in the log which of mods\guerrilla_people\people.txt are loaded,
// 5 and 30 seconds after the world starts.
//
// Their animations: Guerrilla kept its people's animations in its animations archive (anims.vpp_pc),
// which its game loads whole while it starts and keeps for good (anim_load_packfile: each animation
// overlaid on its place in the animation library with "keep" set, which an unload passes over). The
// people's animations are many characters' at once (every set builds on Male), and an animation packed
// in a character's own container belongs to whichever container loaded first: the next one's copy is
// passed over, and the first one's leaving takes it away from all (the container callbacks, 0x95DC00
// and 0x95DC90). So, as Guerrilla, they are loaded once and kept: GuerrillaBuilder.exe writes them into
// mods\guerrilla_people\animations\ (rfgp_<name>.anim_pc, Armageddon's packing), and once Armageddon
// has loaded its own archive (0x95E180, cdecl, the archive's path, from the start-up's anim_preload)
// the mod reads each one and overlays it the same way: its place by name (0xD8C2C0), or a new one
// (0xD875F0; the library holds 8,192 with guerrilla_vehicles), then 0xD8C0F0 with "keep" set.
//
// Room for the animations' names: the game keeps every animation's name ("x.anim", for each entry
// of anim_files.xtbl, and the sets', states' and actions' names) in one string pool, "Anim strings"
// (0x2684EC8), which a start-up initializer (0xF4A7E0, from the C runtime's list, after the mods are
// loaded) gives 128 KB of the game's own memory (0x25FC260). Armageddon's, Guerrilla's vehicles' and
// people's 4,679 animations' names alone take 126 KB, and one more than it holds stops the game while
// it starts ("Length of string pool exceeded"). The pool is one of the game's memory pools (its
// memory's start at +0x40, its size at +0x44, how much is used at +0x4C; every string it hands out is
// an address in that memory, and nothing else names the old memory), so the mod lets the initializer
// run and then gives the pool 512 KB of its own, with what is in it already. No code is changed. It
// does this whatever [GuerrillaPeople] Enabled= says: the animation lists hold the people's animations
// whether or not the rest of the mod is on.
//
// Room for Guerrilla's patrols: the game keeps every patrol route the loaded zones place (object_patrol)
// in an object pool its start-up makes (0xF2E480 → 0x7F9980: the pool set up, 0x71C5D0, a memory pool on
// the buffer inside the pool object, 0x428240, and the pool's fill, 0x7FC8D0, carving each slot's object,
// handle and byte out of it), of 24; Guerrilla's holds 72 (object_pool<object_patrol,72,1>), and its zones
// place up to 29 in one 3 × 3 block. Nothing else is sized by that count: the pool is reached only through
// its own object (0x20F3B10: how many it holds, a word at +0xA; its free list at +0xC; a byte a slot at
// +0x18, 0xFF while free). So the mod replaces the constructor with one doing the same on a buffer of its
// own, for Guerrilla's 72, after checking the constructor's buffer size and count are the game's (nothing
// changes if not). Always, whatever [GuerrillaPeople] Enabled= says: the world's zones place them.
// The world also keeps a list of each type's objects (0x1870630 +0x68, 12 bytes a type: its objects, how many it
// holds, how many it has; the zone loader 0x6D4880 adds each object made, saying "!type_objects[stub->obj_type]
// .full()" when the list is full and adding it anyway), each list as big as the count the type registers at
// start-up (0xF2E4A0 → 0x4FAD90, patrols' 24), not its pool. So the mod also replaces that registration with the
// same call (the same callbacks and name) giving Guerrilla's 72, after checking each value it pushes is the game's.
//
// Guerrilla's sides: both games keep the teams by the same numbers (0 Guerrilla's Red Faction, 1 its
// EDF, Armageddon's Legion, 2 civilians, 3 Marauders; Armageddon adds 4, its cultists) and how each
// team takes each other in a 5 × 5 table (0x21DC8D0: 0 hostile, 1 unfriendly, 2 neutral, 3 friendly,
// 4 allied), which a level's start resets to Armageddon's (0x8A0350, from 0xFCA1A8). Armageddon's make
// the Marauders the player's friends and civilians the EDF's enemies; Guerrilla's (its
// Default_human_team_dispositions): the EDF and the Marauders hostile to the guerrillas, the civilians
// friendly to them and unfriendly to the EDF, the Marauders hostile to everyone. In our worlds the mod
// writes Guerrilla's after each reset (through the game's own setter, 0x8A03A0, cdecl: the team, the
// other team, the disposition, both ways or not); the cultists keep Armageddon's.
#define RFA_MOD_NAME "guerrilla_people"
#include "rfa_mod.h"
#include "rfa_game.h"
#include <intrin.h>
#include <malloc.h>
#include <cstring>
#include <fstream>
#include <memory>
#include <string>
#include <vector>

namespace {

// ---- Our worlds

// The level's world (its territory), as guerrilla_world reads it.
const char* WorldName() { return reinterpret_cast<const char*>(0x1870630 + 0x29EA); }
std::vector<std::string> g_worlds;  // [GuerrillaPeople] Worlds=

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

bool InOurWorld() {
    const char* world = WorldName();
    for (const std::string& name : g_worlds)
        if (_stricmp(world, name.c_str()) == 0) return true;
    return false;
}

// One of the GuerrillaBuilder.exe lists, one name a line.
std::vector<std::string> ReadList(const wchar_t* file) {
    std::vector<std::string> names;
    std::ifstream list(rfa::RootDir() + L"\\mods\\guerrilla_people\\" + file);
    for (std::string line; std::getline(list, line);) {
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        if (!line.empty() && line.size() < 64) names.push_back(line);
    }
    return names;
}

// ---- Their skeletons

constexpr DWORD kReadCharacters = 0x8860D0;
constexpr size_t kReadCharactersPrologue = 9;  // push ebp / mov ebp,esp / sub esp,10Ch
using ReadCharactersFn = void(__cdecl*)(int which, void* table, int dlc);
ReadCharactersFn g_readCharacters;

// Loads one skeleton by its file's name (cdecl; nothing if it is loaded already), and finds a loaded one (-1 if none).
auto const LoadSkeleton = reinterpret_cast<void(__cdecl*)(const char* name)>(0x973550);
auto const FindSkeleton = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD8A040);
std::vector<std::string> g_skeletons;  // mods\guerrilla_people\rigs.txt
bool g_skeletonsLoaded = false;
void LogRigPool();  // (below, with the skeletons' pool)
void MasonsTableRead();  // (below, with Mason's Nano Forge light)
void LogCharacterNames();  // (below, with the characters' strings)

void __cdecl ReadCharacters(int which, void* table, int dlc) {
    if (!g_skeletonsLoaded) {
        g_skeletonsLoaded = true;
        for (const std::string& skeleton : g_skeletons) {
            LoadSkeleton(skeleton.c_str());
            if (FindSkeleton(skeleton.c_str()) >= 0) rfa::Log("Skeletons: %s loaded", skeleton.c_str());
            else rfa::Log("Skeletons: %s could not be loaded (a character that names it will stop the game)", skeleton.c_str());
        }
        LogRigPool();
    }
    g_readCharacters(which, table, dlc);
    LogCharacterNames();
    MasonsTableRead();
}

// ---- Room for the skeletons
//
// Every skeleton the game loads (its archives' own while it starts, Guerrilla's vehicles' and people's
// after them) goes into one memory pool, "rigs" (0x26AFB48), which the game's start-up (0xA46BE9) makes
// 0x90000 bytes (576 KB) of its own memory through the game's memory pools' init (0x4280C0, a method:
// the memory, its size, the name, 4, 8). Armageddon's skeletons and Guerrilla's vehicles' and people's
// filled it to within a few bytes, and one more stops the game while it starts ("Failed aligned alloc
// ... in rigs pool"). So when the game sets that pool up the mod gives it 2 MB of its own instead (the
// pool is empty then, so nothing moves; no code is changed), whatever [GuerrillaPeople] Enabled= says, as
// Guerrilla's vehicles' skeletons need the room too; it logs how full it is once the people's are loaded.
constexpr DWORD kPoolInit = 0x4280C0;
constexpr size_t kPoolInitPrologue = 7;  // push ebp / mov ebp,esp / push ecx / mov [ebp-4],ecx
using PoolInitFn = void(__fastcall*)(void* pool, void* unused, void* memory, DWORD size, const char* name, int flag, int alignment);
PoolInitFn g_poolInit;
BYTE* const kRigPool = reinterpret_cast<BYTE*>(0x26AFB48);
constexpr DWORD kGameRigPoolBytes = 0x90000;  // its own: 576 KB
constexpr DWORD kRigPoolBytes = 0x200000;     // ours: 2 MB
alignas(16) BYTE g_rigPool[kRigPoolBytes];

// The characters' strings: the character table's reader (0x8860D0) keeps every character's names (its
// name, nickname, display name, outfit image, model, skeleton, morph, light tag and each prop's names, each
// once) in a string pool, "Human info strings" (0x21DC118), which it sets up on 0x1838 bytes (6,200) it takes
// the first time (kept at 0x21B74A4) through the same memory pools' init. Armageddon's and Guerrilla's 152
// characters' come to about 10.6 KB; past its end the pool says "Length of string pool exceeded" and hands
// out nothing, and the reader's next compare with it stops the game (C0000417). So when it is set up the mod
// gives it 64 KB of its own (the pool is empty then; the game's 6,200 bytes stay taken, unused; nothing else
// names them, and nothing frees them).
BYTE* const kHumanInfoStrings = reinterpret_cast<BYTE*>(0x21DC118);
constexpr DWORD kGameHumanInfoBytes = 0x1838;   // its own: 6,200 bytes
constexpr DWORD kHumanInfoBytes = 0x10000;      // ours: 64 KB
alignas(16) BYTE g_humanInfoStrings[kHumanInfoBytes];

// The squads' and spawn resources' names, the same way: the squad table's reader (0x50C000, then 0x50BA30 an
// entry) keeps each squad's Name in "Squad Def String Pool" (0x12F2E38) on 0x500 bytes (1,280) it takes once
// (kept at 0x12EFED0), and the spawn resources' reader (0x75F190, then 0x75ED30) each resource's Name in
// "Spawn Resource Data" (0x19706E0) on 0x400 (1,024; kept at 0x1969950). Armageddon's 51 squads and Guerrilla's
// 92 need about 2.1 KB, the 83 resources about 1.1 KB, so they get 16 KB and 8 KB of their own (the people
// build checks its tables' names fit them).
BYTE* const kSquadNames = reinterpret_cast<BYTE*>(0x12F2E38);
constexpr DWORD kGameSquadNameBytes = 0x500;    // its own: 1,280 bytes
constexpr DWORD kSquadNameBytes = 0x4000;       // ours: 16 KB
alignas(16) BYTE g_squadNames[kSquadNameBytes];
BYTE* const kResourceNames = reinterpret_cast<BYTE*>(0x19706E0);
constexpr DWORD kGameResourceNameBytes = 0x400; // its own: 1,024 bytes
constexpr DWORD kResourceNameBytes = 0x2000;    // ours: 8 KB
alignas(16) BYTE g_resourceNames[kResourceNameBytes];

void __fastcall PoolInit(void* pool, void* unused, void* memory, DWORD size, const char* name, int flag, int alignment) {
    if (pool == kRigPool && size == kGameRigPoolBytes) {
        g_poolInit(pool, unused, g_rigPool, kRigPoolBytes, name, flag, alignment);
        rfa::Log("Skeletons: their pool holds %lu KB (its own %lu KB)", kRigPoolBytes / 1024, kGameRigPoolBytes / 1024);
        return;
    }
    if (pool == kHumanInfoStrings && size == kGameHumanInfoBytes) {
        g_poolInit(pool, unused, g_humanInfoStrings, kHumanInfoBytes, name, flag, alignment);
        rfa::Log("Characters' names: their pool holds %lu KB (its own %lu bytes)", kHumanInfoBytes / 1024, kGameHumanInfoBytes);
        return;
    }
    if (pool == kSquadNames && size == kGameSquadNameBytes) {
        g_poolInit(pool, unused, g_squadNames, kSquadNameBytes, name, flag, alignment);
        rfa::Log("Squads' names: their pool holds %lu KB (its own %lu bytes)", kSquadNameBytes / 1024, kGameSquadNameBytes);
        return;
    }
    if (pool == kResourceNames && size == kGameResourceNameBytes) {
        g_poolInit(pool, unused, g_resourceNames, kResourceNameBytes, name, flag, alignment);
        rfa::Log("Spawn resources' names: their pool holds %lu KB (its own %lu bytes)", kResourceNameBytes / 1024, kGameResourceNameBytes);
        return;
    }
    g_poolInit(pool, unused, memory, size, name, flag, alignment);
}

void LogCharacterNames() {
    rfa::Log("Characters' names: %lu of their pool's %lu bytes used", *reinterpret_cast<const DWORD*>(kHumanInfoStrings + 0x4C),
             *reinterpret_cast<const DWORD*>(kHumanInfoStrings + 0x44));
}

void LogRigPool() {
    rfa::Log("Skeletons: %lu of their pool's %lu bytes used", *reinterpret_cast<const DWORD*>(kRigPool + 0x4C), *reinterpret_cast<const DWORD*>(kRigPool + 0x44));
}

// ---- Room for the animations' names

constexpr DWORD kAnimStringsInit = 0xF4A7E0;
constexpr size_t kAnimStringsInitPrologue = 8;  // push ebp / mov ebp,esp / push 1002B4Ch ("Anim strings")
using AnimStringsInitFn = void(__cdecl*)();
AnimStringsInitFn g_animStringsInit;

BYTE* const kAnimStrings = reinterpret_cast<BYTE*>(0x2684EC8);  // the pool
constexpr DWORD kPoolMemory = 0x40, kPoolSize = 0x44, kPoolUsed = 0x4C;
constexpr DWORD kGameAnimStringBytes = 0x20000;  // its own: 128 KB
constexpr DWORD kAnimStringBytes = 0x80000;      // ours: 512 KB
alignas(16) char g_animStrings[kAnimStringBytes];

// ---- Room for Guerrilla's patrols (see the top)

using PoolSetUpFn = void(__fastcall*)(void* pool, void* unused);
using MempoolMakeFn = void*(__fastcall*)(void* mempool, void* unused, void* buffer, DWORD size, const char* name, int one, int zero);
using MempoolEndFn = void(__fastcall*)(void* mempool, void* unused);
using PoolFillFn = void(__fastcall*)(void* pool, void* unused, void* mempool, int count);
auto const PoolSetUp = reinterpret_cast<PoolSetUpFn>(0x71C5D0);
auto const MempoolMake = reinterpret_cast<MempoolMakeFn>(0x428240);
auto const MempoolEnd = reinterpret_cast<MempoolEndFn>(0x427F40);
auto const FillPatrolPool = reinterpret_cast<PoolFillFn>(0x7FC8D0);
constexpr DWORD kPoolName = 0xF7101C;  // "object pool", the name the game's constructors give

// The constructor, and the values in it that say it is as expected: it pushes the buffer's size (4
// bytes: 24 × (the patrol's 0xA8 + 5) + 5) and the count (a one-byte push).
constexpr DWORD kPatrolPoolMake = 0x7F9980, kPatrolBufferSize = 0x7F999B, kPatrolCount = 0x7F99AF;
constexpr DWORD kPatrolSize = 0xA8, kGamePatrolBuffer = 0x103D;
constexpr int kGamePatrols = 24, kPatrols = 72;  // Guerrilla's 72

// A slot's bytes: its object, its handle (4) and its byte; then room for the memory pool's alignment.
constexpr DWORD PoolBytes(int count, DWORD size) { return count * (size + 5) + 64; }
alignas(16) BYTE g_patrolBuffer[PoolBytes(kPatrols, kPatrolSize)];

// What the game's constructor does, on the mod's buffer and with Guerrilla's count.
void* __fastcall MakePatrolPool(void* pool, void*) {
    PoolSetUp(pool, nullptr);
    alignas(16) BYTE mempool[0x60];  // the game's is 0x58 bytes on its stack
    MempoolMake(mempool, nullptr, g_patrolBuffer, sizeof(g_patrolBuffer), reinterpret_cast<const char*>(kPoolName), 1, 0);
    FillPatrolPool(pool, nullptr, mempool, kPatrols);
    MempoolEnd(mempool, nullptr);
    return pool;
}

// The patrols' registration (0xF2E4A0): it pushes nine values and calls 0x4FAD90 (thiscall) on the type's entry.
constexpr DWORD kPatrolRegistration = 0xF2E4A0, kRegisterType = 0x4FAD90, kPatrolTypeEntry = 0x20F3B04;
// Each push's value and where it is in the registration's code (push imm32 at +3, +10, ...; push imm8 at +8).
struct PushedValue { DWORD at; DWORD value; };
constexpr PushedValue kPatrolPushes[] = {
    {0xF2E4A4, 0x7F7E30}, {0xF2E4AB, 0x1070}, {0xF2E4B0, 0x7F7E80}, {0xF2E4B5, 0x7F7E60}, {0xF2E4BA, 0x813EA0},
    {0xF2E4BF, 0x7F7E40}, {0xF2E4C4, 0x7F7E10}, {0xF2E4C9, 0xFBE430}, {0xF2E4CE, kPatrolTypeEntry},
};
constexpr DWORD kPatrolRegisteredCount = 0xF2E4A9;  // push 18h: the count, one byte
using RegisterTypeFn = void(__fastcall*)(void* entry, void* unused, const char* name, DWORD a, DWORD b, DWORD c, DWORD d, DWORD e,
                                         DWORD bytes, DWORD count, DWORD f);
auto const RegisterType = reinterpret_cast<RegisterTypeFn>(kRegisterType);

// The game's registration, with Guerrilla's count (and the bytes it books scaled to it).
void __cdecl RegisterPatrols() {
    RegisterType(reinterpret_cast<void*>(kPatrolTypeEntry), nullptr, reinterpret_cast<const char*>(0xFBE430), 0x7F7E10, 0x7F7E40, 0x813EA0,
                 0x7F7E60, 0x7F7E80, 0x1070 * kPatrols / kGamePatrols, kPatrols, 0x7F7E30);
}

bool PatrolRegistrationAsExpected() {
    for (const PushedValue& push : kPatrolPushes)
        if (rfa::CodeValue(push.at) != push.value) return false;
    return *reinterpret_cast<const BYTE*>(kPatrolRegisteredCount) == kGamePatrols;
}

void MakeRoomForPatrols() {
    // Both or neither: the pool and the world's list of patrols.
    if (!PatrolRegistrationAsExpected()) {
        rfa::Log("Patrols: the type's registration isn't as expected; the game keeps its %d", kGamePatrols);
        return;
    }
    if (rfa::CodeValue(kPatrolBufferSize) == kGamePatrolBuffer && *reinterpret_cast<const BYTE*>(kPatrolCount) == kGamePatrols &&
        rfa::ReplaceFunction(kPatrolPoolMake, reinterpret_cast<void*>(&MakePatrolPool))) {
        if (rfa::ReplaceFunction(kPatrolRegistration, reinterpret_cast<void*>(&RegisterPatrols)))
            rfa::Log("Patrols: the game holds %d at once (its own %d), as Guerrilla, in its pool and the world's list", kPatrols, kGamePatrols);
        else rfa::Log("Patrols: the pool holds %d, but the registration couldn't be replaced; the world's list keeps %d", kPatrols, kGamePatrols);
    }
    else rfa::Log("Patrols: the pool's constructor isn't as expected; the game keeps its %d (the world places more than fit in some places)", kGamePatrols);
}

// ---- Room for Guerrilla's squads' members (the user, 2026-11-06: "Yes, raise it")
//
// Every squad's leader and member is a record of (character, loadout, variant) the squad table's reader shares between
// squads (0x50BCE0 → 0x50BDD0: found in the list 0x12EFF0C, else a new one from its pool, 0x50C440). The pool is made
// at start-up (0xF1D740 → 0x50CB70, as the patrols': the pool set up, a memory pool on the buffer inside the pool
// object, then its fill, 0x4D64A0, carving 12-byte slots) for 64; with none left the member, so its squad, fails, and
// every later squad in the table with it. Armageddon's own squads use 43; Guerrilla's district squads bring each
// civilian and each guerrilla look with its weapons (95 in all with Armageddon's). So the mod builds it on a buffer of
// its own for 256, after checking the constructor's buffer size and count are the game's. Nothing else names the
// count (the pool object 0x12EFEF0 is reached through its initializer, its client 0x12EFED4/0x12EFF0C and its
// destructor only). Always, whatever [GuerrillaPeople] Enabled= says: the squad table holds Guerrilla's squads.
auto const FillMemberPool = reinterpret_cast<PoolFillFn>(0x4D64A0);
constexpr DWORD kMemberPoolMake = 0x50CB70, kMemberBufferSize = 0x50CB8B, kMemberCount = 0x50CB9F;
constexpr DWORD kMemberSize = 12, kGameMemberBuffer = 0x445;
constexpr int kGameMembers = 64, kMembers = 256;
alignas(16) BYTE g_memberBuffer[PoolBytes(kMembers, kMemberSize)];

void* __fastcall MakeMemberPool(void* pool, void*) {
    PoolSetUp(pool, nullptr);
    alignas(16) BYTE mempool[0x60];  // the game's is 0x58 bytes on its stack
    MempoolMake(mempool, nullptr, g_memberBuffer, sizeof(g_memberBuffer), reinterpret_cast<const char*>(kPoolName), 1, 0);
    FillMemberPool(pool, nullptr, mempool, kMembers);
    MempoolEnd(mempool, nullptr);
    return pool;
}

void MakeRoomForSquadMembers() {
    if (rfa::CodeValue(kMemberBufferSize) == kGameMemberBuffer && *reinterpret_cast<const BYTE*>(kMemberCount) == kGameMembers &&
        rfa::ReplaceFunction(kMemberPoolMake, reinterpret_cast<void*>(&MakeMemberPool)))
        rfa::Log("Squad members: the game holds %d (its own %d)", kMembers, kGameMembers);
    else rfa::Log("Squad members: the pool's constructor isn't as expected; the game keeps its %d (squads past them aren't read)", kGameMembers);
}

// ---- Room for Guerrilla's squad loadouts (the user's Round 1, 2026-11-06: marauder_squad_6 "invalid")
//
// The game keeps squad_loadout.xtbl's entries in a fixed table at 0x12F33A0 (its items' pointer, capacity and count,
// then 36 entries of 0x1C bytes in place), set up at start-up by 0x50D5B0 (from the initializer 0xF1D850). The reader
// (0x50D160 → 0x50CF90, added by 0x7642F0) drops every entry past the 36th without a word (the table can't grow:
// 0x7476F0), and a squad member naming a dropped loadout fails its whole squad (0x50BD6B), so a zone naming the squad
// gets "invalid squad definition". Armageddon's own loadouts take 31; Guerrilla's 20 make 51. Everything reaches the
// entries through the items' pointer (0x50D530) and the count (0x55B3D0), and the exit's clean-up (0xF62490 →
// 0x926130) does nothing, so the mod builds the table on a buffer of its own for 128, after checking the constructor
// is the game's. Always, whatever [GuerrillaPeople] Enabled= says: the loadout table holds Guerrilla's loadouts.
constexpr DWORD kLoadoutTableMake = 0x50D5B0;
constexpr DWORD kLoadoutMakePush = 0x50D5B8;      // push 50D280h: each entry's constructor
constexpr DWORD kLoadoutCountPushes[] = {0x50D5BD, 0x50D5CF};  // push 24h, twice: the count
constexpr DWORD kLoadoutSizePush = 0x50D5BF;      // push 1Ch: an entry's bytes
constexpr DWORD kLoadoutSize = 0x1C;
constexpr int kGameLoadouts = 36, kLoadouts = 128;
using LoadoutMakeFn = void*(__fastcall*)(void* loadout, void*);
using ArraySetUpFn = void(__fastcall*)(void* array, void*, void* items, int capacity, int count);
auto const LoadoutMake = reinterpret_cast<LoadoutMakeFn>(0x50D280);
auto const ArraySetUp = reinterpret_cast<ArraySetUpFn>(0x47CE00);
alignas(16) BYTE g_loadoutBuffer[kLoadouts * kLoadoutSize];

void* __fastcall MakeLoadoutTable(void* table, void*) {
    for (int i = 0; i < kLoadouts; ++i) LoadoutMake(g_loadoutBuffer + i * kLoadoutSize, nullptr);
    ArraySetUp(table, nullptr, g_loadoutBuffer, kLoadouts, 0);
    return table;
}

bool LoadoutTableAsExpected() {
    if (rfa::CodeValue(kLoadoutMakePush) != 0x50D280) return false;
    for (DWORD at : kLoadoutCountPushes)
        if (*reinterpret_cast<const BYTE*>(at) != kGameLoadouts) return false;
    return *reinterpret_cast<const BYTE*>(kLoadoutSizePush) == kLoadoutSize;
}

void MakeRoomForLoadouts() {
    if (LoadoutTableAsExpected() && rfa::ReplaceFunction(kLoadoutTableMake, reinterpret_cast<void*>(&MakeLoadoutTable)))
        rfa::Log("Squad loadouts: the game holds %d (its own %d)", kLoadouts, kGameLoadouts);
    else rfa::Log("Squad loadouts: the table's constructor isn't as expected; the game keeps its %d (loadouts past them are dropped, and squads naming them aren't read)", kGameLoadouts);
}

// ---- Guerrilla's standing squads spread out from their node (the user's Round 1, 2026-11-06: "Static Spawn Error")
//
// A squad on foot with Standard orders spawns "static", at its node: the squad's spawn (0x80EC20 → 0x80E2F0, by the
// orders' type; 0x80E1C0 for Standard) gets the node's place and the static flag (+0x3E0 bit 5), and a squad of several
// (0x758C20 → 0x75B010) needs one object_spawn_point placed under the node for each member (gathered by 0x75E0C0);
// with too few the game writes "Static Spawn Error, not enough spawn points placed under spawn node 0x…" and nobody
// spawns. Armageddon's levels place the points (all but three of its own nodes of several have them). Guerrilla has no
// spawn points: its squads spread out from the node over the people's nav mesh (object_squad::spawn_squad_members), and
// its Default orders are our Standard. So in our worlds a squad whose node has fewer points than members spreads out
// from the node's place the way the game places every squad that isn't static (0x759270: from the nav cell under the
// spawn's place, cell by cell, a member to each free spot). A node with enough points keeps the game's own way.
constexpr DWORD kSquadStaticSpawn = 0x75B010;
constexpr size_t kSquadStaticSpawnPrologue = 9;  // push ebp / mov ebp,esp / sub esp,420h
constexpr DWORD kSpawnNode = 0x3B0;              // in the squad's spawn: its node (an object)
constexpr DWORD kSpawnMemberCount = 0x18;        // the count of the members' list at +0x10
using SquadStaticSpawnFn = bool(__cdecl*)(BYTE* spawn);
using SpawnPointListFn = void(__fastcall*)(void* list, void*);
using GatherSpawnPointsFn = void(__cdecl*)(void* node, void* list, int, int);
using SpreadOverNavMeshFn = void(__cdecl*)(BYTE* spawn);
using AllMembersSpawnedFn = bool(__fastcall*)(BYTE* spawn, void*);
using ObjectHandleFn = DWORD(__fastcall*)(void* object, void*);
auto const SpawnPointListMake = reinterpret_cast<SpawnPointListFn>(0xA347C0);  // farray<object*, 256>
auto const SpawnPointListFree = reinterpret_cast<SpawnPointListFn>(0x926130);
auto const GatherSpawnPoints = reinterpret_cast<GatherSpawnPointsFn>(0x75E0C0);
auto const SpreadOverNavMesh = reinterpret_cast<SpreadOverNavMeshFn>(0x759270);
auto const AllMembersSpawned = reinterpret_cast<AllMembersSpawnedFn>(0x75E840);
auto const ObjectHandle = reinterpret_cast<ObjectHandleFn>(0x424850);
SquadStaticSpawnFn g_squadStaticSpawn = nullptr;
int g_spreadSquadsLogged = 0;

int SpawnPointsUnder(void* node) {
    alignas(16) BYTE list[0x420];  // the game's is 0x410 bytes on its stack: its count at +8, the 256 in place from +0xC
    SpawnPointListMake(list, nullptr);
    GatherSpawnPoints(node, list, 0, 0);
    const int count = *reinterpret_cast<const int*>(list + 8);
    SpawnPointListFree(list, nullptr);
    return count;
}

// ---- Guerrilla's guard posts: their squads leashed to the post's box (the user's Round 2, 2026-11-06: "Yes, use the box")
//
// STAND-IN until Guerrilla's guard post AI (go_to_guard_post, guard_at_post, guard_at_post_scan and its guard nodes) is
// ported after the safe spot. In Guerrilla a squad whose node hangs under a bounding box holding guard nodes guards that
// box: its members stand at the guard nodes and fight from there. Armageddon deleted that AI (object_guard_node is
// editor-only in it), so under Standard orders the squad hunted the player down walks the pathfinder won't take ("Failed
// pathfind, probably bad navmesh in the area": the EDF post over the Dust safehouse, a 500-730 m way round, past the
// search's limit of distance x 1.5 + 150, at most 500, the same in Guerrilla). The world build places the box with the
// node under it (GuerrillaPopulation.GuardBoxOf); here, as such a node spawns its squad (0x800040, the node's spawn:
// thiscall, two arguments, the squad's handle or -1), the squad is leashed to the box with Armageddon's own leash to an
// anchor's bounding box (type 4; the squad's leash 0x810990(type, anchor), which only scripts use, sf_update_leash
// 0x94E2DF; the check 0x810710 keeps it inside the anchor's oriented box). Only in our worlds, and only for a node whose
// parent (object +0x6C) is a bounding box.
constexpr DWORD kNodeSpawn = 0x800040;
constexpr size_t kNodeSpawnPrologue = 9;  // push ebp / mov ebp,esp / sub esp,0ECh
constexpr DWORD kObjectParent = 0x6C;     // an object's parent's handle (Guerrilla's object::parent, +0x70 there)
constexpr DWORD kWorld = 0x1870630;
constexpr int kBoxLeash = 4;              // the leash's types: 1-3 Low/Medium/High (30/50/70 m round the anchor), 4 inside its box
constexpr DWORD kNoHandle = 0xFFFFFFFF;
using NodeSpawnFn = DWORD(__fastcall*)(BYTE* node, void*, DWORD a, DWORD b);
using WorldObjectFn = BYTE*(__fastcall*)(void* world, void*, DWORD handle);
using AsBoundingBoxFn = BYTE*(__fastcall*)(void* world, void*, BYTE* object);
using SetLeashFn = void(__fastcall*)(BYTE* squad, void*, int type, DWORD anchor);
auto const WorldObject = reinterpret_cast<WorldObjectFn>(0x6CB9E0);
auto const AsBoundingBox = reinterpret_cast<AsBoundingBoxFn>(0x452170);
auto const SetLeash = reinterpret_cast<SetLeashFn>(0x810990);
NodeSpawnFn g_nodeSpawn = nullptr;
int g_leashesLogged = 0;

DWORD __fastcall NodeSpawn(BYTE* node, void* edx, DWORD a, DWORD b) {
    const DWORD squadHandle = g_nodeSpawn(node, edx, a, b);
    if (squadHandle == kNoHandle || !node || !InOurWorld()) return squadHandle;
    void* world = reinterpret_cast<void*>(kWorld);
    const DWORD boxHandle = *reinterpret_cast<const DWORD*>(node + kObjectParent);
    if (boxHandle == kNoHandle) return squadHandle;
    BYTE* parent = WorldObject(world, nullptr, boxHandle);
    BYTE* squad = WorldObject(world, nullptr, squadHandle);
    if (!parent || !squad || !AsBoundingBox(world, nullptr, parent)) return squadHandle;
    SetLeash(squad, nullptr, kBoxLeash, boxHandle);
    if (g_leashesLogged < 64) {
        ++g_leashesLogged;
        rfa::Log("Squads: node 0x%08X's squad 0x%08X leashed to its guard post's box 0x%08X (Guerrilla's guard area; a stand-in until its guard post AI is ported)",
                 ObjectHandle(node, nullptr), squadHandle, boxHandle);
    }
    return squadHandle;
}

bool __cdecl SquadStaticSpawn(BYTE* spawn) {
    void* node = *reinterpret_cast<void**>(spawn + kSpawnNode);
    if (!node || !InOurWorld()) return g_squadStaticSpawn(spawn);
    const int members = *reinterpret_cast<const int*>(spawn + kSpawnMemberCount);
    const int points = SpawnPointsUnder(node);
    if (points >= members) return g_squadStaticSpawn(spawn);
    SpreadOverNavMesh(spawn);
    const bool all = AllMembersSpawned(spawn, nullptr);
    if (g_spreadSquadsLogged < 32) {
        ++g_spreadSquadsLogged;
        rfa::Log("Squads: node 0x%08X's %d spread out from it over the nav mesh, as in Guerrilla (%d spawn point(s) under it)%s",
                 ObjectHandle(node, nullptr), members, points, all ? "" : "; not all of them found room");
    }
    return all;
}

// The mods the people's animations need room from (they move the game's tables at start-up): the
// animation library and entries (guerrilla_vehicles: 8,192 for 4,500 and 4,224) and the map of every
// set's states and actions (guerrilla_gear: 16,384 for 6,344, which the game never checks; past it the
// heap is overwritten). Asked when the game starts its own start-up (every mod is loaded by then), before
// it reads any animation list.
void WarnIfRoomMissing() {
    if (!GetModuleHandleW(L"guerrilla_vehicles.dll"))
        rfa::Log("Animations: guerrilla_vehicles isn't loaded, so the game holds only its own 4,500 animations; Guerrilla's people's won't all fit (turn it on)");
    if (!GetModuleHandleW(L"guerrilla_gear.dll"))
        rfa::Log("Animations: guerrilla_gear isn't loaded, so the game's map of the sets' states and actions keeps its own 6,344 places, which Guerrilla's people's sets go past (turn it on)");
}

void __cdecl AnimStringsInit() {
    WarnIfRoomMissing();
    g_animStringsInit();
    char*& memory = *reinterpret_cast<char**>(kAnimStrings + kPoolMemory);
    DWORD& size = *reinterpret_cast<DWORD*>(kAnimStrings + kPoolSize);
    const DWORD used = *reinterpret_cast<DWORD*>(kAnimStrings + kPoolUsed);
    if (size != kGameAnimStringBytes || used > size || !memory) {
        rfa::Log("Animation names: the pool isn't as expected (size %lu, used %lu); it keeps its own", size, used);
        return;
    }
    std::memcpy(g_animStrings, memory, used);
    memory = g_animStrings;
    size = kAnimStringBytes;
    rfa::Log("Animation names: the pool holds %lu KB (its own %lu KB)", kAnimStringBytes / 1024, kGameAnimStringBytes / 1024);
}

// ---- Their animations

constexpr DWORD kLoadAnimationArchive = 0x95E180;
constexpr size_t kLoadAnimationArchivePrologue = 9;  // push ebp / mov ebp,esp / sub esp,274h
using LoadAnimationArchiveFn = int(__cdecl*)(const char* path);
LoadAnimationArchiveFn g_loadAnimationArchive;

// The animation library: an animation's place by its name ("x.anim"; -1 if none), a new place for a
// name (its place if it has one; -1 if the library is full), and the data overlaid on the place a name
// has (keep: an unload passes over it).
auto const FindAnimation = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD8C2C0);
auto const NewAnimation = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD875F0);
auto const OverlayAnimation = reinterpret_cast<int(__cdecl*)(const char* name, void* data, unsigned size, bool keep)>(0xD8C0F0);

// The names stay with their places (the library keeps the pointer), so they are kept for good.
std::vector<std::unique_ptr<std::string>> g_animationNames;

// Reads one file whole into memory aligned as the game's animation memory (16 bytes), kept for good.
void* ReadAnimation(const std::wstring& path, unsigned& size) {
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file) return nullptr;
    const std::streamoff length = file.tellg();
    if (length <= 0 || length > 64 * 1024 * 1024) return nullptr;
    void* data = _aligned_malloc(static_cast<size_t>(length), 16);
    if (!data) return nullptr;
    file.seekg(0);
    if (!file.read(static_cast<char*>(data), length)) {
        _aligned_free(data);
        return nullptr;
    }
    size = static_cast<unsigned>(length);
    return data;
}

void LoadOurAnimations() {
    const std::wstring folder = rfa::RootDir() + L"\\mods\\guerrilla_people\\animations\\";
    WIN32_FIND_DATAW found;
    HANDLE search = FindFirstFileW((folder + L"*.anim_pc").c_str(), &found);
    if (search == INVALID_HANDLE_VALUE) return;
    unsigned loaded = 0, failed = 0, noRoom = 0;
    size_t bytes = 0;
    do {
        std::wstring file = found.cFileName;
        std::string stem;
        for (wchar_t c : file.substr(0, file.size() - 8)) stem += static_cast<char>(c);  // without .anim_pc
        if (stem.empty() || stem.size() > 58) { ++failed; continue; }
        g_animationNames.push_back(std::make_unique<std::string>(stem + ".anim"));
        const char* name = g_animationNames.back()->c_str();
        if (FindAnimation(name) < 0 && NewAnimation(name) < 0) { ++noRoom; continue; }
        unsigned size = 0;
        void* data = ReadAnimation(folder + file, size);
        if (!data) { ++failed; continue; }
        // (A place that has data already, or is kept already, is passed over: the game's own wins.)
        if (OverlayAnimation(name, data, size, true) < 0) { _aligned_free(data); ++noRoom; continue; }
        ++loaded;
        bytes += size;
    } while (FindNextFileW(search, &found));
    FindClose(search);
    rfa::Log("Animations: %u of Guerrilla's people's loaded and kept (%.1f MB)", loaded, bytes / 1048576.0);
    if (noRoom) rfa::Log("Animations: %u had no place in the animation library (it is full: is guerrilla_vehicles on?); the people won't play them", noRoom);
    if (failed) rfa::Log("Animations: %u could not be read", failed);
}

int __cdecl LoadAnimationArchive(const char* path) {
    const int result = g_loadAnimationArchive(path);
    // (The animation lists have been read by now: how full their names left the pool.)
    rfa::Log("Animation names: %lu of the pool's %lu bytes used", *reinterpret_cast<const DWORD*>(kAnimStrings + kPoolUsed),
             *reinterpret_cast<const DWORD*>(kAnimStrings + kPoolSize));
    LoadOurAnimations();
    return result;
}

// ---- The world's spawn resource (how far its people have loaded, for the log)

auto const FindCharacter = reinterpret_cast<void*(__cdecl*)(const char* name)>(0x88D170);
constexpr const char* PlayerCharacterName = "rfg_PC";  // Guerrilla's player, Mason (GuerrillaPeople.PlayerCharacter)
auto const CharacterLoaded = reinterpret_cast<bool(__cdecl*)(void* character)>(0x762230);
auto const CharacterOnItsWay = reinterpret_cast<bool(__cdecl*)(void* character)>(0x764ED0);
// The pools' state (a method of it): whether a level's spawn resource is in force.
auto const ResourceInForce = reinterpret_cast<bool(__fastcall*)(void* pools, void* unused)>(0x75FC10);
void* const kPools = reinterpret_cast<void*>(0x19734CC);
std::vector<std::string> g_people;  // mods\guerrilla_people\people.txt

// The level's spawn resource: the game picks the people a level loads by a resource, set as the
// level's (0x7628B0: the one to load, kept at 0x19734D0, which its spawn resource manager then holds
// in the level's category, 0x762600) or applied at once (0x7625A0); both cdecl, the resource first.
// It names them from the zone the player stands in (its obj_zone's spawn_resource_data), the mission,
// a cutscene or a script, and any name its table hasn't, or none, gives its fallback, the table's
// "Default" (made first by the table's loader, 0x75EA90, kept at 0x1969954): Armageddon's aliens,
// soldiers and Marauders. In our worlds some path gave that one (the zones all name rfg_Default), and
// its people's effects ran the effect pools out. So in our worlds the mod gives the game the world's
// own resource (rfg_Default, found by name with 0x75EC70) wherever it would load the fallback, and
// says in the log who asked for it (the return address).
constexpr DWORD kSetLevelResource = 0x7628B0;
constexpr size_t kSetLevelResourcePrologue = 7;  // push ebp / mov ebp,esp / cmp dword ptr [ebp+8],0
constexpr DWORD kApplyLevelResource = 0x7625A0;
constexpr size_t kApplyLevelResourcePrologue = 6;  // push ebp / mov ebp,esp / push ecx / push 0
using SetLevelResourceFn = int(__cdecl*)(void* resource, int again);
using ApplyLevelResourceFn = int(__cdecl*)(void* resource);
SetLevelResourceFn g_setLevelResource;
ApplyLevelResourceFn g_applyLevelResource;
auto const FindSpawnResource = reinterpret_cast<void*(__cdecl*)(const char* name)>(0x75EC70);
void* const* const kFallbackResource = reinterpret_cast<void* const*>(0x1969954);
constexpr const char* kWorldResource = "rfg_Default";
int g_swapsLogged = 0;

// The resource to load: the world's own in place of the fallback in our worlds; else as given.
void* WorldResource(void* resource, const void* caller) {
    if (!resource || !InOurWorld()) return resource;
    const char* name = *static_cast<const char* const*>(resource);  // an entry starts with its name
    const bool fallback = resource == *kFallbackResource || (name && _stricmp(name, "Default") == 0);
    if (!fallback) return resource;
    void* ours = FindSpawnResource(kWorldResource);
    if (g_swapsLogged < 20) {
        ++g_swapsLogged;
        if (ours) rfa::Log("Spawn resource: the game was loading Armageddon's \"Default\" in world \"%s\" (asked from 0x%p); the world's %s instead", WorldName(), caller, kWorldResource);
        else rfa::Log("Spawn resource: the game is loading Armageddon's \"Default\" in world \"%s\" (asked from 0x%p), and the table has no %s (the people not built?)", WorldName(), caller, kWorldResource);
    }
    return ours ? ours : resource;
}

int __cdecl SetLevelResource(void* resource, int again) {
    return g_setLevelResource(WorldResource(resource, _ReturnAddress()), again);
}

int __cdecl ApplyLevelResource(void* resource) {
    return g_applyLevelResource(WorldResource(resource, _ReturnAddress()));
}

// ---- The player: Guerrilla's own in our worlds
//
// The game picks the player's character once per level load (0x8B77B0, cdecl: the start position;
// Guerrilla's player_local_get_human_info_for_creation): multiplayer's choice; else a DLC uniform the
// player chose in Armageddon's menu (its checksum at 0x12F6BA0, 0x51A580 reads it); else the start
// zone's spawn resource's Player_Override, which in the Guerrilla world is Guerrilla's player, Mason
// (GuerrillaBuilder.exe's people); else Armageddon's RF4_PC. So that Mason is the player there whatever
// uniform is chosen, the mod sets the chosen uniform aside while the game picks, in our worlds only,
// and puts it back straight after (Armageddon's own levels keep it).
constexpr DWORD kPickPlayer = 0x8B77B0;
constexpr size_t kPickPlayerPrologue = 6;  // push ebp / mov ebp,esp / sub esp,10h
using PickPlayerFn = void*(__cdecl*)(const void* startPosition);
PickPlayerFn g_pickPlayer;
DWORD* const kChosenUniform = reinterpret_cast<DWORD*>(0x12F6BA0);
int g_uniformsLogged = 0;

// The player's animation set: when the game makes the player's human (0x8605E0 → 0x86091D, the
// player only), in multiplayer or a Wrecking Crew level (0x511930: [0x10F83FC] isn't -1; our worlds are
// those) it gives the player's character instance Armageddon's player set, "vs_player" (0x862B00,
// cdecl: the human's +0x760; the instance at +4, its sets at +0xC and +0x10, found by name 0x98C290),
// whatever set its character names. Its clips are Darius's (137 bones, bound by bone number), so on
// Guerrilla's player, Mason, nothing plays. In our worlds the player keeps the set his character gives
// him (Mason's rfg_mason; Darius's is vs_player anyway); the mod says which in the log.
constexpr DWORD kGivePlayerSet = 0x862B00;
constexpr size_t kGivePlayerSetPrologue = 10;  // push ebp / mov ebp,esp / movzx eax,byte ptr [2685118h]
using GivePlayerSetFn = void(__cdecl*)(void* human);
GivePlayerSetFn g_givePlayerSet;
auto const FindAnimationSet = reinterpret_cast<void*(__cdecl*)(const char* name)>(0x98C290);
int g_setsLogged = 0;

const char* SetName(void* set) {
    if (!set) return "none";
    for (const char* name : {"rfg_mason", "vs_player", "rfg_player"})
        if (FindAnimationSet(name) == set) return name;
    return "another of its own";
}

void __cdecl GivePlayerSet(void* human) {
    BYTE* instance = human ? *reinterpret_cast<BYTE**>(static_cast<BYTE*>(human) + 4) : nullptr;
    void* own = instance ? *reinterpret_cast<void**>(instance + 0x10) : nullptr;
    if (!own && instance) own = *reinterpret_cast<void**>(instance + 0xC);
    if (!instance || !own || !InOurWorld()) {
        g_givePlayerSet(human);
        if (instance && g_setsLogged < 10) {
            ++g_setsLogged;
            rfa::Log("Player: animation set %s (world \"%s\")", SetName(*reinterpret_cast<void**>(instance + 0x10)), WorldName());
        }
        return;
    }
    // Keep his own: the instance's current set and its base.
    *reinterpret_cast<void**>(instance + 0x10) = own;
    *reinterpret_cast<void**>(instance + 0xC) = own;
    if (g_setsLogged < 10) {
        ++g_setsLogged;
        rfa::Log("Player: keeps his own animation set, %s, in world \"%s\" (the game would give him vs_player)", SetName(own), WorldName());
    }
}

void* __cdecl PickPlayer(const void* startPosition) {
    if (!InOurWorld() || *kChosenUniform == 0) return g_pickPlayer(startPosition);
    const DWORD uniform = *kChosenUniform;
    *kChosenUniform = 0;
    void* player = g_pickPlayer(startPosition);
    *kChosenUniform = uniform;
    if (g_uniformsLogged < 5) {
        ++g_uniformsLogged;
        rfa::Log("Player: in world \"%s\" the world's own player, not the uniform chosen in Armageddon's menu (kept for its own levels)", WorldName());
    }
    return player;
}

// Writes which of the people are loaded, on their way, or neither (`after`: how long in the world).
void ReportOurPeople(const char* after) {
    rfa::Log("Squads' names: %lu of their pool's %lu bytes used; spawn resources' names: %lu of %lu",
             *reinterpret_cast<const DWORD*>(kSquadNames + 0x4C), *reinterpret_cast<const DWORD*>(kSquadNames + 0x44),
             *reinterpret_cast<const DWORD*>(kResourceNames + 0x4C), *reinterpret_cast<const DWORD*>(kResourceNames + 0x44));
    std::string loaded, coming, not_loaded, missing;
    auto add = [](std::string& list, const std::string& name) { list += (list.empty() ? "" : ", ") + name; };
    for (const std::string& name : g_people) {
        void* character = FindCharacter(name.c_str());
        if (!character) add(missing, name);
        else if (CharacterLoaded(character)) add(loaded, name);
        else if (CharacterOnItsWay(character)) add(coming, name);
        else add(not_loaded, name);
    }
    rfa::Log("People %s in the world (the world's spawn resource %s): loaded: %s; on their way: %s; not loaded: %s%s%s", after,
             ResourceInForce(kPools, nullptr) ? "in force" : "not in force", loaded.empty() ? "none" : loaded.c_str(),
             coming.empty() ? "none" : coming.c_str(), not_loaded.empty() ? "none" : not_loaded.c_str(),
             missing.empty() ? "" : "; not in the game's character table (the people not built, or the table not handed over): ",
             missing.c_str());
}

// ---- Guerrilla's sides

constexpr DWORD kResetDispositions = 0x8A0350;
constexpr size_t kResetDispositionsPrologue = 5;  // push ebp / mov ebp,esp / push ecx / push esi
using ResetDispositionsFn = void(__cdecl*)();
ResetDispositionsFn g_resetDispositions;
auto const SetDisposition = reinterpret_cast<void(__cdecl*)(int team, int other, int disposition, bool bothWays)>(0x8A03A0);
auto& g_dispositions = *reinterpret_cast<int(*)[5][5]>(0x21DC8D0);

// Guerrilla's: Default_human_team_dispositions (RM 0x1310F38), row the team, column the other.
constexpr int kHostile = 0, kUnfriendly = 1, kFriendly = 3, kAllied = 4;
constexpr int kGuerrillaDispositions[4][4] = {
    {kAllied, kHostile, kFriendly, kHostile},     // Guerrilla's Red Faction (the player's team)
    {kHostile, kAllied, kUnfriendly, kHostile},   // the EDF
    {kFriendly, kUnfriendly, kAllied, kHostile},  // civilians
    {kHostile, kHostile, kHostile, kAllied},      // Marauders
};

bool DispositionsAreGuerrillas() {
    for (int t = 0; t < 4; ++t)
        for (int o = 0; o < 4; ++o)
            if (t != o && g_dispositions[t][o] != kGuerrillaDispositions[t][o]) return false;
    return true;
}

void SetGuerrillaDispositions() {
    if (DispositionsAreGuerrillas()) return;
    for (int t = 0; t < 4; ++t)
        for (int o = 0; o < 4; ++o)
            if (t != o) SetDisposition(t, o, kGuerrillaDispositions[t][o], false);
    rfa::Log("Sides: Guerrilla's (the EDF and the Marauders hostile to the player's side, civilians friendly to it and unfriendly to the EDF, the Marauders hostile to all)");
}

void __cdecl ResetDispositions() {
    g_resetDispositions();
    if (InOurWorld()) SetGuerrillaDispositions();
}

// ---- A jump turned down, in the log
//
// Armageddon's jump (0x85A5A0, cdecl: the human and three flags; Guerrilla's human_jump) turns a jump
// down when the human can't jump now (its checks below; and the jump timers: Jump_Delay_ms after a
// landing, 1.1 s between the player's jumps), else sets one to come (+0x11E8 1) 0.1 s later (+0x11EC),
// with its height (+0x11F0). The mod writes in the log a jump turned down by one of its other checks
// (not the timers: a press again too soon is normal), with each check's value, for the first few after
// each world starts. (Written while Mason didn't leave the ground; the cause was his jump animations:
// Guerrilla's had a small hop of their own that held him down, and Armageddon's jump, fall and landing
// clips moved onto him fixed it.) Nothing is changed.
constexpr DWORD kHumanJump = 0x85A5A0;
constexpr size_t kHumanJumpPrologue = 6;  // push ebp / mov ebp,esp / sub esp,28h
using HumanJumpFn = void(__cdecl*)(void* human, bool first, bool second, bool third);
HumanJumpFn g_humanJump;
constexpr int kJumpsToLog = 12;
int g_jumpsLogged = 0;

// The jump's own checks (each as the jump calls it), to tell which turned one down.
auto const JumpBlocked = reinterpret_cast<bool(__cdecl*)(void* human)>(0x866D70);
auto const JumpBusy = reinterpret_cast<bool(__cdecl*)(void* human, int what)>(0x8669B0);
auto const ActionPlaying = reinterpret_cast<int(__thiscall*)(void* actions, int category)>(0x98DAA0);
auto const JumpRefused = reinterpret_cast<bool(__cdecl*)(void* human)>(0x8803D0);

const char* Whose(void* human) {
    void* character = *reinterpret_cast<void**>(static_cast<BYTE*>(human) + 0x4FC);
    if (character && character == FindCharacter(PlayerCharacterName)) return "Mason (rfg_PC)";
    return "Armageddon's character";
}

void __cdecl HumanJump(void* human, bool first, bool second, bool third) {
    g_humanJump(human, first, second, third);
    if (!human || g_jumpsLogged >= kJumpsToLog) return;
    BYTE* h = static_cast<BYTE*>(human);
    if (h[0x11E8]) return;  // set to come
    const bool blocked = JumpBlocked(human), busy = JumpBusy(human, 0), refused = JumpRefused(human);
    const int state = *reinterpret_cast<int*>(h + 0x770), mode = *reinterpret_cast<int*>(h + 0x730), action = ActionPlaying(h + 0x6F0, -1);
    const DWORD b0Bit21 = (*reinterpret_cast<DWORD*>(h + 0xB0) >> 21) & 1, b4Bit11 = (*reinterpret_cast<DWORD*>(h + 0xB4) >> 11) & 1;
    if (!blocked && !busy && !refused && state != 2 && mode != 4 && !action && !(b0Bit21 && !b4Bit11)) return;  // a timer: pressed again too soon
    ++g_jumpsLogged;
    rfa::Log("Jump: %s in world \"%s\" (flags %d %d %d): turned down; checks: blocked %d, state +0x770 %d, +0x730 %d, "
             "+0xB0 bit 21 %lu, +0xB4 bit 11 %lu, busy %d, an action playing %d, refused %d",
             Whose(human), WorldName(), first, second, third, blocked, state, mode, b0Bit21, b4Bit11, busy, action, refused);
}

// ---- Mason's Nano Forge light ([GuerrillaPeople] MasonNanoLight=)
//
// Mason's character entry takes Armageddon's player's fields where Guerrilla's has none (GuerrillaBuilder.exe's
// people), among them its Persistent_Effects: one, nano_light, the Nano Forge's glow, an always-on cyan lamp
// (its effect's RF_Light01, colour 0 0.30 0.75) about 5 cm off muzzle_flash0 on his left forearm. Guerrilla's
// Mason has none, and next to his coat that lamp lights the coat's reflective tape. MasonNanoLight=1 (the
// default) keeps it, as Armageddon's player has it; 0 takes it out of his character's effects. The game
// starts a character's persistent effects when it makes the human, so a change shows the next time Mason is
// made (the level loaded again, or a save). The mod reads the setting once a second (the F7 Graphics window's
// switch saves it), and keeps his effects as the table gave them, to put the lamp back.
// A character's persistent effects, as the table reader keeps them (0x888FFE-0x8893BA): +0x20C a list of
// 16-byte entries (the effect's number first, as 0x4EF370 gives it for a name), +0x210 how many (8 at most).
constexpr size_t kEffectList = 0x20C, kEffectCount = 0x210, kEffectBytes = 16;
constexpr int kMostEffects = 8;
auto const EffectNumber = reinterpret_cast<int(__cdecl*)(const char* name)>(0x4EF370);
BYTE* g_masonEntry = nullptr;                      // his character entry, when last read
BYTE g_masonEffects[kMostEffects * kEffectBytes];  // its effects as the table gave them
int g_masonEffectCount = 0;
int g_nanoLightShown = -1;                         // the setting last applied (-1: none yet)

void ApplyMasonNanoLight() {
    BYTE* entry = static_cast<BYTE*>(FindCharacter(PlayerCharacterName));
    if (!entry) return;
    if (entry != g_masonEntry) {
        const int count = *reinterpret_cast<int*>(entry + kEffectCount);
        const BYTE* list = *reinterpret_cast<BYTE**>(entry + kEffectList);
        if (count < 0 || count > kMostEffects || (count > 0 && !list)) {
            rfa::Log("Mason's Nano Forge light: his character's persistent effects aren't as expected (%d); left as they are", count);
            g_masonEntry = entry;
            g_masonEffectCount = -1;
            return;
        }
        g_masonEntry = entry;
        g_masonEffectCount = count;
        if (count > 0) std::memcpy(g_masonEffects, list, count * kEffectBytes);
        g_nanoLightShown = -1;
    }
    if (g_masonEffectCount < 0) return;
    const int wanted = rfa::IniInt(L"GuerrillaPeople", L"MasonNanoLight", 1) != 0 ? 1 : 0;
    if (wanted == g_nanoLightShown) return;
    g_nanoLightShown = wanted;
    const int nanoLight = EffectNumber("nano_light");
    BYTE* list = *reinterpret_cast<BYTE**>(entry + kEffectList);
    int kept = 0;
    for (int i = 0; i < g_masonEffectCount; ++i) {
        const BYTE* effect = g_masonEffects + i * kEffectBytes;
        if (!wanted && nanoLight >= 0 && *reinterpret_cast<const int*>(effect) == nanoLight) continue;
        std::memcpy(list + kept * kEffectBytes, effect, kEffectBytes);
        ++kept;
    }
    *reinterpret_cast<int*>(entry + kEffectCount) = kept;
    rfa::Log("Mason's Nano Forge light: %s (MasonNanoLight=%d; %d of his %d persistent effects; shows the next time he is made: load the level again)",
             wanted ? "on, as Armageddon's player has it" : "off, as Guerrilla's Mason", wanted, kept, g_masonEffectCount);
}

// After the character table is read: his effects as it gave them, then the setting.
void MasonsTableRead() {
    g_masonEntry = nullptr;
    ApplyMasonNanoLight();
}

// ---- Once a second in the game's play (its gameplay update, 0x4FE860, cdecl)

constexpr DWORD kGameplayFrame = 0x4FE860;
constexpr size_t kGameplayFramePrologue = 8;  // push ebp / mov ebp,esp / push 0F4240h
using GameplayFrameFn = void(__cdecl*)();
GameplayFrameFn g_gameplayFrame;
DWORD g_lastCheck = 0, g_enteredAt = 0;
bool g_wasOurs = false;
int g_reports = 0;  // the people's reports written since entering the world (at 5 s and 30 s)

void __cdecl GameplayFrame() {
    g_gameplayFrame();
    const DWORD now = GetTickCount();
    if (now - g_lastCheck < 1000) return;
    g_lastCheck = now;
    const bool ours = InOurWorld();
    if (ours != g_wasOurs) {
        g_wasOurs = ours;
        g_enteredAt = now;
        g_reports = 0;
        g_jumpsLogged = 0;
        rfa::Log("World \"%s\": %s", WorldName(), ours ? "Guerrilla's people and sides" : "Armageddon's own");
    }
    ApplyMasonNanoLight();
    if (!ours) return;
    if (g_resetDispositions) SetGuerrillaDispositions();
    if (!g_people.empty() && g_reports < 2 && now - g_enteredAt >= (g_reports == 0 ? 5000u : 30000u)) {
        ReportOurPeople(g_reports == 0 ? "5 s" : "30 s");
        ++g_reports;
    }
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    // Before the game's own initializers run (the mods are loaded first): always, see above.
    g_poolInit = reinterpret_cast<PoolInitFn>(rfa::HookFunction(kPoolInit, kPoolInitPrologue, reinterpret_cast<void*>(&PoolInit)));
    if (!g_poolInit) rfa::Log("Skeletons: could not hook the memory pools' set-up; their pool keeps its 576 KB (too few for Guerrilla's people and vehicles)");
    g_animStringsInit = reinterpret_cast<AnimStringsInitFn>(rfa::HookFunction(kAnimStringsInit, kAnimStringsInitPrologue, reinterpret_cast<void*>(&AnimStringsInit)));
    if (!g_animStringsInit) rfa::Log("Animation names: could not hook the pool's start-up; it keeps its 128 KB (too few for Guerrilla's people and vehicles)");
    MakeRoomForPatrols();
    MakeRoomForSquadMembers();
    MakeRoomForLoadouts();
    if (rfa::IniInt(L"GuerrillaPeople", L"Enabled", 1) == 0) {
        rfa::Log("Off ([GuerrillaPeople] Enabled=0)");
        return;
    }
    g_worlds = SplitList(rfa::IniString(L"GuerrillaPeople", L"Worlds", L"rfg"));
    g_skeletons = ReadList(L"rigs.txt");
    g_people = ReadList(L"people.txt");
    if (!g_skeletons.empty()) {
        g_readCharacters = reinterpret_cast<ReadCharactersFn>(rfa::HookFunction(kReadCharacters, kReadCharactersPrologue, reinterpret_cast<void*>(&ReadCharacters)));
        if (g_readCharacters) rfa::Log("Skeletons: %u of Guerrilla's people's own to load before the character table", static_cast<unsigned>(g_skeletons.size()));
        else rfa::Log("Skeletons: could not hook the character table's reading; Guerrilla's people's skeletons won't be loaded, and a character naming one stops the game");
    }
    g_loadAnimationArchive = reinterpret_cast<LoadAnimationArchiveFn>(rfa::HookFunction(kLoadAnimationArchive, kLoadAnimationArchivePrologue, reinterpret_cast<void*>(&LoadAnimationArchive)));
    if (!g_loadAnimationArchive) rfa::Log("Animations: could not hook the animation archive's loading; Guerrilla's people's animations won't be loaded (they stand still)");
    g_setLevelResource = reinterpret_cast<SetLevelResourceFn>(rfa::HookFunction(kSetLevelResource, kSetLevelResourcePrologue, reinterpret_cast<void*>(&SetLevelResource)));
    g_applyLevelResource = reinterpret_cast<ApplyLevelResourceFn>(rfa::HookFunction(kApplyLevelResource, kApplyLevelResourcePrologue, reinterpret_cast<void*>(&ApplyLevelResource)));
    if (!g_setLevelResource || !g_applyLevelResource)
        rfa::Log("Spawn resource: could not hook the level's spawn resource; Armageddon's own people may load in our worlds");
    g_pickPlayer = reinterpret_cast<PickPlayerFn>(rfa::HookFunction(kPickPlayer, kPickPlayerPrologue, reinterpret_cast<void*>(&PickPlayer)));
    if (!g_pickPlayer) rfa::Log("Player: could not hook the player's choice; a DLC uniform chosen in Armageddon's menu still wins in our worlds");
    g_givePlayerSet = reinterpret_cast<GivePlayerSetFn>(rfa::HookFunction(kGivePlayerSet, kGivePlayerSetPrologue, reinterpret_cast<void*>(&GivePlayerSet)));
    if (!g_givePlayerSet) rfa::Log("Player: could not hook the player's animation set; Guerrilla's player gets Armageddon's (and stands still)");
    g_resetDispositions = reinterpret_cast<ResetDispositionsFn>(rfa::HookFunction(kResetDispositions, kResetDispositionsPrologue, reinterpret_cast<void*>(&ResetDispositions)));
    if (!g_resetDispositions) rfa::Log("Sides: could not hook the teams' reset; Armageddon's dispositions stay");
    g_humanJump = reinterpret_cast<HumanJumpFn>(rfa::HookFunction(kHumanJump, kHumanJumpPrologue, reinterpret_cast<void*>(&HumanJump)));
    if (!g_humanJump) rfa::Log("Jump: could not hook the jump; a jump turned down isn't written in the log");
    g_gameplayFrame = reinterpret_cast<GameplayFrameFn>(rfa::HookFunction(kGameplayFrame, kGameplayFramePrologue, reinterpret_cast<void*>(&GameplayFrame)));
    if (!g_gameplayFrame) rfa::Log("Could not hook the gameplay update; Guerrilla's sides are set only at a level's start, and the people's loading isn't reported");
    g_squadStaticSpawn = reinterpret_cast<SquadStaticSpawnFn>(rfa::HookFunction(kSquadStaticSpawn, kSquadStaticSpawnPrologue, reinterpret_cast<void*>(&SquadStaticSpawn)));
    if (!g_squadStaticSpawn) rfa::Log("Squads: could not hook the static spawn; a standing squad of several with no spawn points under its node doesn't spawn");
    g_nodeSpawn = reinterpret_cast<NodeSpawnFn>(rfa::HookFunction(kNodeSpawn, kNodeSpawnPrologue, reinterpret_cast<void*>(&NodeSpawn)));
    if (!g_nodeSpawn) rfa::Log("Squads: could not hook the squad nodes' spawn; Guerrilla's guard posts' squads aren't leashed to their boxes (they chase the player)");
    rfa::Log("Guerrilla's people in world(s) %ls: %u character(s) made by editor\\GuerrillaBuilder.exe", rfa::IniString(L"GuerrillaPeople", L"Worlds", L"rfg").c_str(),
             static_cast<unsigned>(g_people.size()));
}
