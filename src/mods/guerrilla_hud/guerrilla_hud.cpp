// Red Faction: Guerrilla's HUD in the worlds ported from Guerrilla, Armageddon's own everywhere else.
//
// Both games build their HUD from the same kind of interface documents (VINT: elements driven by a
// Lua script). Armageddon loads its HUD (the document ui_hud) once at start-up and keeps it through
// every world. This mod adds Guerrilla's HUD documents on top of it while a world named by
// [GuerrillaHud] Worlds= is played (the ported world is "rfg"), and takes them away again in any
// other world. Each document's script (in mods\guerrilla_hud\) fades out the parts of Armageddon's
// HUD it replaces while it is loaded, and back in when it is unloaded.
//
// The documents are Guerrilla's own, made loadable by GuerrillaBuilder.exe from the player's own copy
// of Guerrilla (never shipped): see RfaFormats\Interface\GuerrillaHud.cs. Without them the mod only
// says so in the log.
//
// When: the HUD's data update (0x57BC30), which the HUD's per-frame process (0x57B520) calls every
// frame while the HUD is up, is wrapped. After the game's own update the mod looks at the world's
// name (kept in the world object, 0x1870630, at +0x29EA, as the time of day reads it to name its
// files) and loads or unloads the documents the way the game loads its menus while playing, putting
// them in a draw list of the interface as the console's vint_doc_load does (a document that is in
// none is loaded but never drawn). The interface's draw pass (0x564FF0) is wrapped too: our
// documents stay on the draw list only while the HUD is being updated, so nothing of them is left
// on loading screens or in the menus. And the start of every level load (0x55D870) is wrapped:
// all of our documents are unloaded there, as the load frees their memory (see LoadStart).
//
// Guerrilla's map ([GuerrillaHud] Map=1): its minimap (the document rfg_ui_hud) and its world map
// (rfg_ui_fullscreen, opened and closed with [GuerrillaHud] MapKey=). Guerrilla drew the map itself
// in its HUD's map elements (its "cartographer"); Armageddon kept that code only for its compass. So
// here the documents' scripts build the map from pictures, Guerrilla's own: its world map picture,
// each zone's own picture, its minimap's frame, mask and player arrow. What they need to know each
// frame, this mod tells them through Lua functions it adds to the interface's Lua (see
// RFAMod_RegisterLua): where the player is on Guerrilla's map, which way the camera looks, the
// district, and which zones' pictures are loaded. Those come as Guerrilla keeps them, each zone's
// picture on its own (here a document that only brings that texture), loaded while the player is
// near.
#define RFA_MOD_NAME "guerrilla_hud"
#include "rfa_mod.h"
#include "rfa_game.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <map>
#include <set>
#include <string>
#include <vector>
#pragma comment(lib, "user32.lib")  // the world map's key: GetAsyncKeyState, GetForegroundWindow

namespace {

// ---- The game

// The world being played: "world" for the campaign, "rfg" for the ported world.
const char* WorldName() { return reinterpret_cast<const char*>(0x1870630 + 0x29EA); }

// The HUD's data update: fills the data items its scripts read (health_hud, reticule_hud, ...).
constexpr DWORD kHudUpdate = 0x57BC30;
constexpr size_t kHudUpdatePrologue = 6;  // push ebp / mov ebp,esp / mov eax,[ebp+8]
using HudUpdateFn = void(__cdecl*)(void* hud);
HudUpdateFn g_hudUpdate;

// The interface's documents. The project (rfg.vint_proj) lists every document the game can load;
// a loaded ("open") document has its handle at +0x40 of its record.
using InProjectFn = void*(__cdecl*)(const char* name);
using OpenDocumentFn = BYTE*(__cdecl*)(const char* name);
using LoadDocumentFn = unsigned short(__cdecl*)(const char* name, int, float, int, int, int, int);
using UnloadDocumentFn = void(__cdecl*)(unsigned int handle);
using DocumentStatusFn = int(__cdecl*)(unsigned int handle);
auto const InProject = reinterpret_cast<InProjectFn>(0xC50790);
auto const OpenDocument = reinterpret_cast<OpenDocumentFn>(0x562840);
auto const LoadDocument = reinterpret_cast<LoadDocumentFn>(0x563990);      // the handle, 0xFFFF on failure
auto const UnloadDocument = reinterpret_cast<UnloadDocumentFn>(0x5628A0);
auto const DocumentStatus = reinterpret_cast<DocumentStatusFn>(0x565DA0);  // 3 once it and its textures are in
constexpr unsigned short kNoDocument = 0xFFFF;
constexpr int kDocumentReady = 3;

// A picture of the interface's sheets, by name (vint_bms_load, as Armageddon's own map code looks up
// its pictures): its number, or -1 if no sheet has it. The sheets are read at start-up, so this says
// whether a picture exists even before the texture it is cut from is loaded.
using FindPictureFn = int(__cdecl*)(const char* name);
auto const FindPicture = reinterpret_cast<FindPictureFn>(0xC4F6F0);

// A loaded document is drawn only while its handle is in one of the interface's draw lists, which
// its draw pass (0x564FF0) goes through every frame. The console's vint_doc_load puts what it loads
// in this one (0x561E30), and vint_doc_unload takes it out again before unloading (0x561EC0); the
// game's own screens are drawn through their own lists. Methods of the list (in ECX), each given
// the handle by pointer: add (only if it isn't there yet), remove.
auto const g_drawList = reinterpret_cast<void*>(0x133A2D4);
using DrawListFn = int(__fastcall*)(void* list, void* unused, const unsigned short* handle);
auto const AddToDrawList = reinterpret_cast<DrawListFn>(0x566120);
auto const RemoveFromDrawList = reinterpret_cast<DrawListFn>(0x5661C0);

// Loads a document as the menus (0x58D050) and the console's vint_doc_load (0x561E30) do while
// playing: mode 0 streams its containers in. Not as rf4_hud_post_boot_init (0x581490) loads
// ui_hud at start-up: its mode 2 reads the document there and then (0xC52540), which is only
// meant for start-up; asked for while playing, the game ended (2026-10-31).
unsigned short LoadWhilePlaying(const char* name) { return LoadDocument(name, 0, 0.0f, 0, 0, 0x80, 0); }

// The local player (as Guerrilla's object: its position at +4), and the camera manager's
// orientation (0x11B9C40 +0x50, rows right, up, forward).
auto& g_player = *reinterpret_cast<BYTE**>(0x2271490);
auto& g_cameraRotation = *reinterpret_cast<float(*)[9]>(0x11B9C90);

// The interface's memory: every interface document's containers (its textures too) are loaded into
// one pool the game makes at start-up (0xA45600: 0xA44A20 allocates the block, 0x512B30 makes the
// pool "interface" of it), 10.13 MB (12 MB when a start-up flag is set). Armageddon's own documents
// fill most of it (its HUD, and 1 MB for a loading screen); with Guerrilla's map documents in it
// too, a level load failed with "Failed aligned alloc of 1036912 bytes in interface cpu pool (max
// pool size is 10.13MB (10616832) - space left is 27728)" (2026-11-05). So with the map on, the pool
// is made bigger: the three values the start-up code makes it with (the block's size, either one,
// and the pool's).
constexpr DWORD kInterfacePoolSizes[] = {0xA47355, 0xA4736A, 0xA4739F};  // mov [ebp-238h],imm / mov ...,imm / push imm
constexpr DWORD kInterfacePoolGame[] = {0xA20000, 0xC00000, 0xA20000};
constexpr DWORD kInterfacePoolOurs = 48 * 1024 * 1024;  // with every zone's picture loaded (about 13 MB of it)

void EnlargeInterfacePool() {
    for (int i = 0; i < 3; ++i)
        if (rfa::CodeValue(kInterfacePoolSizes[i]) != kInterfacePoolGame[i]) {
            rfa::Log("The interface's memory: the game's code at 0x%08lX isn't as expected (0x%08lX); it keeps its 10 MB, "
                     "too little for Guerrilla's map pictures", kInterfacePoolSizes[i], rfa::CodeValue(kInterfacePoolSizes[i]));
            return;
        }
    for (DWORD at : kInterfacePoolSizes)
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &kInterfacePoolOurs, sizeof(kInterfacePoolOurs))) {
            rfa::Log("The interface's memory: could not change the game's code at 0x%08lX", at);
            return;
        }
    rfa::Log("The interface's memory: %lu MB (the game's 10 MB is too little for Guerrilla's map pictures)",
             kInterfacePoolOurs / (1024 * 1024));
}

// ---- The interface's document limits
//
// The game holds the interface project's documents in a fixed array of 128 records (0x133A5F8:
// pointer, capacity, count, then the records, 0x3B8 bytes each, made by 0x56A7C0 from a static
// initializer), and its reader takes at most 128 (0x566B60: "Too many VINT docs found! Need to
// increase VINT_MAX_DOCUMENTS above %d"). It keeps at most 10 documents open at once: a pool of
// 0x70-byte records (0x1339CD0), made by 0x566800 in a 0x497-byte buffer of its own, through
// 0x566850(mempool, 10). Armageddon lists 115 documents; Guerrilla's HUD brings its own, its map a
// document per zone picture (as Guerrilla keeps each zone's picture in a container of its own), and
// several of those are open while the player moves about. Both limits are raised, in plain C++:
// - the array: the game's constructor runs as it is, then the array is pointed at records of our own
//   (each made by the game's record constructor, 0x56A800) and given their number (0x47CE00); the
//   reader's 128 (three values, each checked first) becomes ours;
// - the pool: its setup (0x566850) is given a buffer of ours, made a memory pool as the game makes
//   its own (0x428240, released after as the game does, 0x427F40), and our count.
// Both are made before the game's own start-up code runs (the mods load first).
constexpr int kMaxDocuments = 256;
constexpr int kMaxOpenDocuments = 64;  // 40 filled up with the world map's zone pictures too (2026-11-07)
constexpr DWORD kDocumentLimitValues[] = {0x566BC9, 0x566BD0, 0x566BEB};  // cmp [ebp-54h],imm / push imm / mov [ebp-54h],imm
constexpr DWORD kGameMaxDocuments = 0x80;
constexpr int kDocumentRecordSize = 0x3B8;
constexpr int kOpenRecordSize = 0x70;

using ThisFn = void*(__fastcall*)(void* self, void* unused);
using ArraySetFn = void(__fastcall*)(void* self, void* unused, void* records, int capacity, int count);
using PoolInitFn = void(__fastcall*)(void* self, void* unused, void* mempool, unsigned count);
using MempoolMakeFn = void*(__fastcall*)(void* self, void* unused, void* buffer, unsigned size, const char* name, int, int);
auto const DocumentRecordMake = reinterpret_cast<ThisFn>(0x56A800);
auto const ArraySet = reinterpret_cast<ArraySetFn>(0x47CE00);
auto const MempoolMake = reinterpret_cast<MempoolMakeFn>(0x428240);
auto const MempoolRelease = reinterpret_cast<ThisFn>(0x427F40);
const char* const kOpenPoolName = reinterpret_cast<const char*>(0xF7101C);  // the game's own name for it
ThisFn g_documentArrayMake;
PoolInitFn g_openPoolInit;
constexpr size_t kMempoolObjectSize = 0x58;  // the game's local one in 0x566800

void* __fastcall DocumentArrayMake(void* array, void* unused) {
    g_documentArrayMake(array, unused);
    BYTE* records = static_cast<BYTE*>(VirtualAlloc(nullptr, kMaxDocuments * kDocumentRecordSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!records) {
        rfa::Log("Interface documents: no memory for %d records; the game keeps its 128", kMaxDocuments);
        return array;
    }
    for (int i = 0; i < kMaxDocuments; ++i) DocumentRecordMake(records + i * kDocumentRecordSize, nullptr);
    ArraySet(array, nullptr, records, kMaxDocuments, 0);
    rfa::Log("Interface documents: room for %d in the project (the game's 128)", kMaxDocuments);
    return array;
}

void __fastcall OpenPoolInit(void* pool, void* unused, void* gameMempool, unsigned count) {
    const size_t size = kMaxOpenDocuments * (4 + kOpenRecordSize + 1) + 256;  // links, records, flags, alignment
    void* buffer = VirtualAlloc(nullptr, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!buffer) {
        g_openPoolInit(pool, unused, gameMempool, count);
        rfa::Log("Interface documents: no memory for %d open ones; the game keeps its %u", kMaxOpenDocuments, count);
        return;
    }
    alignas(16) BYTE mempool[kMempoolObjectSize] = {};
    MempoolMake(mempool, nullptr, buffer, static_cast<unsigned>(size), kOpenPoolName, 1, 0);
    g_openPoolInit(pool, unused, mempool, kMaxOpenDocuments);
    MempoolRelease(mempool, nullptr);
    rfa::Log("Interface documents: %d open at once (the game's %u)", kMaxOpenDocuments, count);
}

// The interface keeps the documents it is loading, unloading and drawing in eight lists of handles
// (0x133A1A0, 0x2C bytes apart: data pointer, capacity, count, then room for 16 handles), each made
// by 0x566960 with its own 16 and no way to grow (the grow step, 0x7476F0, always refuses; an add
// that doesn't fit is dropped). One of them, 0x133A224, takes every document streamed in (load mode
// 0: the menus' and ours) until the interface works through it (0x563770); a document that doesn't
// get in reports itself not loading (0x565DA0) and never comes up. Entering Guerrilla's world puts
// 9 there at once (Guerrilla's HUD and the zone pictures near the player), and each HUD piece to
// come adds one; so, before they fill it, each list is given room of ours when it is made
// (0x47CE00 points it at the room, as the game's own constructor does at its 16).
constexpr DWORD kDocumentLists = 0x133A1A0;
constexpr int kDocumentListCount = 8;
constexpr DWORD kDocumentListStride = 0x2C;
constexpr DWORD kStreamingQueue = 0x133A224;  // the one streamed documents wait in
constexpr int kDocumentListRoom = 256;
constexpr int kGameDocumentListRoom = 16;
unsigned short g_documentListRoom[kDocumentListCount][kDocumentListRoom];
ThisFn g_documentListMake;

void* __fastcall DocumentListMake(void* list, void* unused) {
    void* made = g_documentListMake(list, unused);
    const DWORD at = reinterpret_cast<DWORD>(list);
    if (at >= kDocumentLists && at < kDocumentLists + kDocumentListCount * kDocumentListStride &&
        (at - kDocumentLists) % kDocumentListStride == 0) {
        const int index = static_cast<int>((at - kDocumentLists) / kDocumentListStride);
        ArraySet(list, nullptr, g_documentListRoom[index], kDocumentListRoom, 0);
        RFA_LOG_FIRST(kDocumentListCount, "Interface document list 0x%08lX: room for %d (the game's %d)", at,
                      kDocumentListRoom, kGameDocumentListRoom);
    }
    return made;
}

// How many documents wait in the streaming queue (for the log: the most there have been).
int StreamingQueueCount() { return *reinterpret_cast<const int*>(kStreamingQueue + 8); }

// The interface loads each document, and each document inside one, into one of 45 slots (0x28C3F48,
// 0x5B0 bytes each): a static array the start-up code constructs (0xF5AAD0) and destroys (0xF66610),
// which 0xC529B0 links into its free list; 0xC524B0 finds a document's slot by its handle, 0xC527C0
// a slot's number. With none free, a document asked for waits for one, without a word. In
// Guerrilla's world, Armageddon's HUD (with its 18 documents inside), Guerrilla's HUD and the zone
// pictures near the player held so many that the pause menu's pages (a dozen documents with theirs)
// never came up and couldn't be left, and a level load then waited forever (2026-11-05: the Video
// page's request still waiting in a dump of the stuck game). So the array is moved to one of ours:
// its 14 references in the game's code (each to the first slot, +0 or +4) and its count, a one-byte
// value in each of the five places that use it and in the debug display's two "%u/%u" (so the
// count stays below 128). Each is checked before anything is changed, before the start-up code
// constructs the slots.
constexpr DWORD kDocumentSlots = 0x28C3F48;
constexpr DWORD kDocumentSlotSize = 0x5B0;
constexpr BYTE kGameDocumentSlots = 45;
constexpr BYTE kDocumentSlotCount = 120;
constexpr DWORD kDocumentSlotReferences[] = {0xC5251C, 0xC52897, 0xC52A18, 0xC52A6F, 0xC52A7E, 0xC52A8C, 0xC52A9B,
                                             0xC52AAA, 0xC52ACA, 0xC52ADF, 0xC52AEE, 0xC52B07, 0xF5AAE0, 0xF66620};
constexpr DWORD kDocumentSlotCounts[] = {0xC52509, 0xC52A0A, 0xC52A54, 0xF5AAD9, 0xF66619, 0x5655F2, 0x565635};
alignas(16) BYTE g_documentSlots[kDocumentSlotCount * kDocumentSlotSize];

void MoveDocumentSlots() {
    for (DWORD at : kDocumentSlotCounts)
        if (*reinterpret_cast<const BYTE*>(at) != kGameDocumentSlots) {
            rfa::Log("Interface document slots: the game's count at 0x%08lX isn't %u; it keeps its %u", at,
                     kGameDocumentSlots, kGameDocumentSlots);
            return;
        }
    for (DWORD at : kDocumentSlotReferences) {
        const DWORD value = rfa::CodeValue(at);
        if (value < kDocumentSlots || value >= kDocumentSlots + kDocumentSlotSize) {
            rfa::Log("Interface document slots: the game's code at 0x%08lX isn't as expected (0x%08lX); it keeps its %u",
                     at, value, kGameDocumentSlots);
            return;
        }
    }
    const DWORD ours = reinterpret_cast<DWORD>(g_documentSlots);
    for (DWORD at : kDocumentSlotReferences) {
        const DWORD value = ours + (rfa::CodeValue(at) - kDocumentSlots);
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &value, sizeof(value))) {
            rfa::Log("Interface document slots: could not change the game's code at 0x%08lX", at);
            return;
        }
    }
    for (DWORD at : kDocumentSlotCounts)
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &kDocumentSlotCount, sizeof(kDocumentSlotCount))) {
            rfa::Log("Interface document slots: could not change the game's count at 0x%08lX", at);
            return;
        }
    rfa::Log("Interface document slots: %u (the game's %u), moved to 0x%08lX", kDocumentSlotCount, kGameDocumentSlots, ours);
}

// The project's reader (vint_project.cpp, 0xC50360) keeps the documents it reads from rfg.vint_proj
// in a table of its own: 128 records of 0xC8 bytes at 0x28BC7C8 (two numbers, then the document's
// file name at +8, its name at +0x48 and +0x88), their count at 0x28C3B08, and stops the game at
// the 129th ("Too many documents defined in project, increase VINT_MAX_DOCUMENTS", line 304; the
// user's first test with Guerrilla's 220, 2026-11-05). Every use of the table is in 0xC50360 to
// 0xC507E5: these values hold the address of a field of its first record.
constexpr DWORD kProjectTable = 0x28BC7C8;
constexpr DWORD kProjectRecordSize = 0xC8;
constexpr DWORD kProjectTableReferences[] = {0xC5047E, 0xC5049F, 0xC504C0, 0xC504DB, 0xC50504,
                                             0xC50524, 0xC5074E, 0xC5077E, 0xC507CF, 0xC507E2};
constexpr DWORD kProjectTableLimits[] = {0xC503B4};  // cmp [count],80h
BYTE g_projectTable[kMaxDocuments * kProjectRecordSize];

// The names in the documents' asset lists (each document's containers and the files in them) go
// into one string pool (0x133A408), 10,000 bytes, allocated and set up by 0x566A70 (the size pushed
// twice: for the allocation from the game's memory pool 0x26AEE78, and for the string pool). With
// Guerrilla's zone pictures' documents it overflowed at start-up ("Length of string pool exceeded",
// util\string_pool.cpp line 122; 2026-11-05): about 120 bytes of names each.
constexpr DWORD kAssetNameSizes[] = {0x566A96, 0x566ADC};  // push 2710h, push 2710h
constexpr DWORD kGameAssetNameBytes = 10000;
constexpr DWORD kAssetNameBytes = 64 * 1024;

// Every element of every loaded document (bitmap, text, group, tween, ...) comes from a pool of its
// type, made when the interface registers the type, with a fixed number (pushed just before the
// pool's set-up call) out of one 640 KB memory pool, "vint_perm" (made at start-up, 0xA46AC1 the
// block and 0xA46ADA the pool, 0xA0000 each). The pools hand out what they hold and then nothing:
// an element that doesn't fit is left out of its document. In Guerrilla's world, with Armageddon's
// HUD and Guerrilla's loaded, a dump showed the tweens full (820 of 820) and the bitmaps at 1,212 of
// 1,300; the Options page Video then lost its buttons' backgrounds, its script failed ("bg_x" nil)
// and the game crashed (2026-11-05). So each pool gets three times its number, and vint_perm room
// for them: the six pools are about 550 KB of its 640, three times that 1.7 MB, so 4 MB.
struct ElementPool {
    const char* type;
    DWORD sizeAt;     // the 4-byte value pushed (push imm32), or the byte (push imm8)
    bool oneByte;
    DWORD game;
    DWORD ours;
};
constexpr ElementPool kElementPools[] = {
    {"tween", 0xC6717E, false, 0x334, 3 * 0x334},          // 196 bytes each
    {"document", 0xC6EC32, false, 0xC4, 3 * 0xC4},         // 128
    {"group", 0xC6F122, false, 0x1DC, 3 * 0x1DC},          // 120
    {"text", 0xC6F822, false, 0x118, 3 * 0x118},           // 208
    {"bitmap", 0xC71AD2, false, 0x514, 3 * 0x514},         // 168
    {"animation", 0xC7258E, false, 0x16C, 3 * 0x16C},      // 88
    {"clip", 0xC6DB82, true, 0x10, 0x40},                  // one byte: stays below 128
    {"bitmap_circle", 0xC6D052, true, 0x14, 0x40},
};
constexpr DWORD kVintPermSizes[] = {0xA46AC2, 0xA46ADB};  // push 0A0000h (the block), push 0A0000h (the pool)
constexpr DWORD kGameVintPerm = 0xA0000;
constexpr DWORD kVintPermOurs = 4 * 1024 * 1024;

void RaiseElementPools() {
    for (DWORD at : kVintPermSizes)
        if (rfa::CodeValue(at) != kGameVintPerm) {
            rfa::Log("Interface elements: the game's code at 0x%08lX isn't as expected (0x%08lX); the pools keep their sizes",
                     at, rfa::CodeValue(at));
            return;
        }
    for (const ElementPool& pool : kElementPools) {
        const DWORD value = pool.oneByte ? *reinterpret_cast<const BYTE*>(pool.sizeAt) : rfa::CodeValue(pool.sizeAt);
        if (value != pool.game) {
            rfa::Log("Interface elements: the %s pool's size at 0x%08lX isn't %lu (it's %lu); the pools keep their sizes",
                     pool.type, pool.sizeAt, pool.game, value);
            return;
        }
    }
    for (DWORD at : kVintPermSizes)
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &kVintPermOurs, sizeof(kVintPermOurs))) {
            rfa::Log("Interface elements: could not change the game's code at 0x%08lX", at);
            return;
        }
    for (const ElementPool& pool : kElementPools) {
        const BYTE small = static_cast<BYTE>(pool.ours);
        const bool changed = pool.oneByte ? rfa::PatchCode(reinterpret_cast<void*>(pool.sizeAt), &small, 1)
                                          : rfa::PatchCode(reinterpret_cast<void*>(pool.sizeAt), &pool.ours, sizeof(pool.ours));
        if (!changed) {
            rfa::Log("Interface elements: could not change the %s pool's size", pool.type);
            return;
        }
    }
    rfa::Log("Interface elements: three times the game's pools (bitmaps %lu, tweens %lu, groups %lu, texts %lu, "
             "animations %lu, documents %lu), in %lu MB (the game's 640 KB)", 3 * 0x514ul, 3 * 0x334ul, 3 * 0x1DCul,
             3 * 0x118ul, 3 * 0x16Cul, 3 * 0xC4ul, kVintPermOurs / (1024 * 1024));
}

void RaiseDocumentLimits() {
    RaiseElementPools();
    MoveDocumentSlots();
    if (!rfa::MoveTable("Interface project documents", kProjectTable, kProjectRecordSize, kGameMaxDocuments,
                        kProjectTableReferences, kProjectTableLimits, g_projectTable, kMaxDocuments))
        return;  // MoveTable says why; the game keeps all its 128s
    bool namesAsExpected = true;
    for (DWORD at : kAssetNameSizes) namesAsExpected = namesAsExpected && rfa::CodeValue(at) == kGameAssetNameBytes;
    if (namesAsExpected) {
        for (DWORD at : kAssetNameSizes) rfa::PatchCode(reinterpret_cast<void*>(at), &kAssetNameBytes, sizeof(kAssetNameBytes));
        rfa::Log("Interface documents: %lu bytes for their asset lists' names (the game's %lu)", kAssetNameBytes, kGameAssetNameBytes);
    } else {
        rfa::Log("Interface documents: the game's code at 0x%08lX isn't as expected; their names keep the game's %lu bytes",
                 kAssetNameSizes[0], kGameAssetNameBytes);
    }
    for (DWORD at : kDocumentLimitValues)
        if (rfa::CodeValue(at) != kGameMaxDocuments) {
            rfa::Log("Interface documents: the game's code at 0x%08lX isn't as expected (0x%08lX); it keeps its 128", at, rfa::CodeValue(at));
            return;
        }
    g_documentArrayMake = reinterpret_cast<ThisFn>(rfa::HookFunction(0x56A7C0, 7, reinterpret_cast<void*>(&DocumentArrayMake)));
    if (!g_documentArrayMake) {
        rfa::Log("Interface documents: could not hook the game's document array; it keeps its 128");
        return;
    }
    const DWORD ours = kMaxDocuments;
    for (DWORD at : kDocumentLimitValues) rfa::PatchCode(reinterpret_cast<void*>(at), &ours, sizeof(ours));
    g_openPoolInit = reinterpret_cast<PoolInitFn>(rfa::HookFunction(0x566850, 7, reinterpret_cast<void*>(&OpenPoolInit)));
    if (!g_openPoolInit) rfa::Log("Interface documents: could not hook the open documents' pool; at most 10 open at once");
    // push ebp / mov ebp,esp / push ecx / mov [ebp-4],ecx
    g_documentListMake = reinterpret_cast<ThisFn>(rfa::HookFunction(0x566960, 7, reinterpret_cast<void*>(&DocumentListMake)));
    if (!g_documentListMake) rfa::Log("Interface documents: could not hook the document lists; each keeps its 16");
}

// The loader's answer to "are its menus (or the developer console) up?" (RFALoader_MenusWant(0)).
bool MenusShown() {
    using WantFn = int(__cdecl*)(int);
    static WantFn want = [] {
        HMODULE loader = GetModuleHandleW(L"rfa_loader.dll");
        return loader ? reinterpret_cast<WantFn>(GetProcAddress(loader, "RFALoader_MenusWant")) : nullptr;
    }();
    return want && want(0) != 0;
}

// ---- The mod

struct Document {
    std::string name;
    bool drawn = true;       // put on the draw list (the zone pictures' documents only bring textures)
    bool whileOpen = false;  // drawn only while the world map is open
    bool whileShop = false;  // drawn only while a safehouse's upgrade shop is open
    bool zoneSet = false;    // one of the four documents of the zones' pictures (ZoneSetDocument)
    bool loaded = false;     // the mod asked for it
    bool seen = false;       // and the game has had it open since
    bool missing = false;    // not in the game's interface project, or the game couldn't load it
    bool listed = false;     // in the draw list (only while the HUD runs)
    int closedByGame = 0;    // times the game closed it after loading it (it does when its memory runs out)
    ULONGLONG retryAt = 0;   // a zone picture the game refused (no room for another open document): when to ask again
    ULONGLONG readyAt = 0;   // when the game first had it ready (see ZoneReady), 0 while not
};
constexpr ULONGLONG kRetryRefusedMs = 2000;

// The interface's draw pass, bool(bool), cdecl: draws every document in its draw lists, every frame,
// whatever is on screen (loading screens and menus too).
constexpr DWORD kDrawPass = 0x564FF0;
constexpr size_t kDrawPassPrologue = 9;  // push ebp / mov ebp,esp / sub esp,0E8h
using DrawPassFn = bool(__cdecl*)(bool);
DrawPassFn g_drawPass;

// Our documents are drawn only while the HUD itself is updated (it is every frame in play): once
// it hasn't been for this long (loading screens, the main menu, ...), they come off the draw list.
constexpr ULONGLONG kHudGoneAfterMs = 250;
constexpr int kClosedByGameTimes = 3;
ULONGLONG g_lastHudUpdate;
std::vector<Document> g_documents;
std::vector<std::string> g_worlds;
int g_inOurWorld = -1;  // last frame's answer, to log the switches (-1 before the first frame)

// ---- Guerrilla's map

// Guerrilla's world: 511 m zones, columns and rows 2 to 13, zone (column, row) from x (column − 8) ×
// 511 and z (7 − row) × 511 to a zone further (rows count southwards). Its world map picture covers
// those columns and rows over 1024 pixels, north up; each zone's own picture covers its zone over
// 512 pixels. Our world keeps Guerrilla's coordinates.
constexpr double kZoneMetres = 511.0;
constexpr double kMapPixels = 1024.0;
constexpr int kFirstZone = 2;
constexpr int kZonesAcross = 12;
constexpr double kZonePixels = kMapPixels / kZonesAcross;

// Every zone's own picture comes in one of four documents, by quarter of the world, as
// GuerrillaBuilder.exe makes them (GuerrillaHud.ZoneSetDocument: columns below 8 west, rows below 8
// north): loaded with the HUD and kept until the next level load. Not streamed zone by zone, as
// Guerrilla's were: the interface's memory for documents is a stack, and unloading one out of order
// freed the zones loaded after it while they were on screen; the next one loaded over them, and
// drawing them crashed the render thread (2026-11-07, three freezes: game-notes).
std::string ZoneSetDocument(int column, int row) {
    return std::string("rfg_ui_map_zones_") + (row < 8 ? "n" : "s") + (column < 8 ? "w" : "e");
}
const char* const kZoneSets[] = {"rfg_ui_map_zones_nw", "rfg_ui_map_zones_ne", "rfg_ui_map_zones_sw", "rfg_ui_map_zones_se"};
int ZoneKey(int column, int row) { return column * 100 + row; }

struct MapView {
    bool active = false;     // in our world, with a player
    double mapX = 0, mapY = 0;  // the player on Guerrilla's world map picture, in its pixels
    double heading = 0;      // which way the camera looks: 0 north, π/2 east
    double speed = 0;        // the player's speed, m/s, smoothed
    int column = 0, row = 0; // the zone the player is in
    std::string district;    // its district's name, as Guerrilla's HUD shows it
};
MapView g_view;
bool g_mapOpen = false;      // the world map is up
std::string g_mapDocument;   // rfg_ui_fullscreen
int g_mapKey = 'M';
bool g_mapKeyDown = false;
std::map<int, std::string> g_districts;          // column × 100 + row -> name (districts.txt)
std::map<int, bool> g_tilePictures;              // column × 100 + row -> the zone has its picture
double g_lastX, g_lastZ;
ULONGLONG g_lastMove;

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

// A key's virtual-key code from its name in rfa_mods.ini: a letter or digit, Tab, or F1-F12.
int KeyCode(const std::wstring& name) {
    if (name.size() == 1 && iswalnum(name[0])) return towupper(name[0]);
    if (_wcsicmp(name.c_str(), L"Tab") == 0) return VK_TAB;
    if ((name[0] == L'F' || name[0] == L'f') && name.size() <= 3) {
        const int number = _wtoi(name.c_str() + 1);
        if (number >= 1 && number <= 12) return VK_F1 + number - 1;
    }
    return 0;
}

// districts.txt (made by GuerrillaBuilder.exe): "CCRR=name" a zone a line, ; comments.
void ReadDistricts() {
    const std::wstring path = rfa::RootDir() + L"\\mods\\guerrilla_hud\\districts.txt";
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"r") != 0 || !file) {
        rfa::Log("No mods\\guerrilla_hud\\districts.txt (editor\\GuerrillaBuilder.exe build hud makes it): the minimap shows no district names");
        return;
    }
    char line[256];
    while (fgets(line, sizeof(line), file)) {
        if (line[0] == ';' || strlen(line) < 6 || line[4] != '=') continue;
        const int column = (line[0] - '0') * 10 + (line[1] - '0');
        const int row = (line[2] - '0') * 10 + (line[3] - '0');
        std::string name = line + 5;
        while (!name.empty() && (name.back() == '\n' || name.back() == '\r')) name.pop_back();
        g_districts[column * 100 + row] = name;
    }
    fclose(file);
    rfa::Log("Districts of %u zones, for the minimap", static_cast<unsigned>(g_districts.size()));
}

// Guerrilla's map icons and places (map_icons.txt, made by GuerrillaBuilder.exe): its safehouses,
// missions' and activities' starts, the world map's named places, the player's own icon and the world
// map's hints, a line each: "icon|picture|x|z|caption|description|on the world map", "place||x|z|name||1",
// "player|picture|0|0|caption|description|1", "text||0|0|what|text|1".
struct MapIcon {
    std::string kind;       // icon, place, player or text
    std::string picture;    // rfg_ui_map_icon_...
    double mapX = 0, mapY = 0;  // on Guerrilla's world map picture, in its pixels
    std::string caption, description;
    bool onWorldMap = true;
    int pair = -1;          // a M.O.A.B.'s pair number (kind "bomb"), shown once its radio tags are found
};
std::vector<MapIcon> g_mapIcons;
void FollowRadioTags();  // Guerrilla's radio tags (below)
void FollowSafehouses();  // Guerrilla's safehouses' stations (below)
bool ShopIsOpen();
void CloseShop(const char* why);

// Where a point of Guerrilla's world (x, z metres) is on its world map picture, in its pixels.
void MapPixels(double x, double z, double& mapX, double& mapY) {
    mapX = (8.0 + x / kZoneMetres - kFirstZone) * kZonePixels;  // column 8 starts at x 0, row 8 at z 0
    mapY = (8.0 - z / kZoneMetres - kFirstZone) * kZonePixels;
}

void ReadMapIcons() {
    const std::wstring path = rfa::RootDir() + L"\\mods\\guerrilla_hud\\map_icons.txt";
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"r") != 0 || !file) {
        rfa::Log("No mods\\guerrilla_hud\\map_icons.txt (editor\\GuerrillaBuilder.exe build hud makes it): the maps show no icons");
        return;
    }
    char line[1024];
    while (fgets(line, sizeof(line), file)) {
        if (line[0] == ';') continue;
        std::vector<std::string> fields(1);
        for (const char* c = line; *c && *c != '\n' && *c != '\r'; ++c) {
            if (*c == '|') fields.emplace_back();
            else fields.back() += *c;
        }
        if (fields.size() < 7 || (fields[0] != "icon" && fields[0] != "place" && fields[0] != "player" && fields[0] != "text" &&
                                  fields[0] != "bomb"))
            continue;
        MapIcon icon;
        icon.kind = fields[0];
        icon.picture = fields[1];
        MapPixels(atof(fields[2].c_str()), atof(fields[3].c_str()), icon.mapX, icon.mapY);
        icon.caption = fields[4];
        icon.description = fields[5];
        icon.onWorldMap = fields[6] != "0";
        if (fields.size() >= 8) icon.pair = atoi(fields[7].c_str());
        g_mapIcons.push_back(icon);
    }
    fclose(file);
    rfa::Log("%u map icons and places, for the maps", static_cast<unsigned>(g_mapIcons.size()));
}

// Whether a zone has its own picture (in one of the zone pictures' documents), asked once.
bool HasTilePicture(int column, int row) {
    const int key = column * 100 + row;
    auto found = g_tilePictures.find(key);
    if (found != g_tilePictures.end()) return found->second;
    char name[64];
    sprintf_s(name, "rfg_terr01_%02d_%02d_map", column, row);
    const bool has = FindPicture(name) >= 0;
    g_tilePictures[key] = has;
    return has;
}

Document* FindDocument(const std::string& name) {
    for (Document& doc : g_documents)
        if (doc.name == name) return &doc;
    return nullptr;
}

// Whether a zone's picture can be shown: its document loaded and ready (its texture in) for a second.
// The game calls a document ready a moment before its texture can be drawn: a picture shown 26 ms after
// its document opened was drawn from an empty texture record (rfg_terr01_04_11_map_sheet.tga) and
// crashed the render thread (2026-11-07, 10:58: the game froze).
constexpr ULONGLONG kReadySettleMs = 1000;

bool ZoneReady(int column, int row) {
    const std::string set = ZoneSetDocument(column, row);
    for (Document& doc : g_documents) {
        if (!doc.zoneSet || !doc.loaded || doc.name != set) continue;
        BYTE* open = OpenDocument(doc.name.c_str());
        if (!open || DocumentStatus(*reinterpret_cast<unsigned short*>(open + 0x40)) != kDocumentReady) {
            doc.readyAt = 0;
            return false;
        }
        const ULONGLONG now = GetTickCount64();
        if (doc.readyAt == 0) doc.readyAt = now;
        return now - doc.readyAt >= kReadySettleMs;
    }
    return false;
}

// Where the player is on Guerrilla's map, which way the camera looks, and the zone pictures wanted.
void UpdateMapView() {
    BYTE* player = g_player;
    g_view.active = player != nullptr;
    if (!player) return;
    const float* position = reinterpret_cast<const float*>(player + 4);
    const double x = position[0], z = position[2];
    const double column = 8.0 + x / kZoneMetres, row = 8.0 - z / kZoneMetres;  // column 8 starts at x 0, row 8 at z 0
    MapPixels(x, z, g_view.mapX, g_view.mapY);
    const float* forward = &g_cameraRotation[6];
    g_view.heading = std::atan2(static_cast<double>(forward[0]), static_cast<double>(forward[2]));

    // The player's speed, smoothed over about half a second (Guerrilla's minimap zooms out with the
    // vehicle's average speed).
    const ULONGLONG now = GetTickCount64();
    if (g_lastMove != 0 && now > g_lastMove) {
        const double seconds = (now - g_lastMove) / 1000.0;
        const double moved = std::sqrt((x - g_lastX) * (x - g_lastX) + (z - g_lastZ) * (z - g_lastZ));
        const double speed = seconds > 0 && moved < 200.0 ? moved / seconds : 0.0;  // a teleport isn't speed
        const double blend = seconds / (seconds + 0.5);
        g_view.speed += (speed - g_view.speed) * blend;
    }
    g_lastX = x;
    g_lastZ = z;
    g_lastMove = now;

    g_view.column = static_cast<int>(std::floor(column));
    g_view.row = static_cast<int>(std::floor(row));
    auto district = g_districts.find(g_view.column * 100 + g_view.row);
    g_view.district = district != g_districts.end() ? district->second : "";
}

bool HeldDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

bool GameFocused() {
    DWORD process = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &process);
    return process == GetCurrentProcessId();
}

// A key that has just gone down (was: its state last time).
bool Pressed(int key, bool& was) {
    const bool down = HeldDown(key);
    const bool pressed = down && !was;
    was = down;
    return pressed;
}

// ---- Guerrilla's world map: what it shows, its keys, and the game paused while it's open
//
// Guerrilla's zoom (fullscreen_map_get_zoom_info, RM 0x841220) runs from 0.1 to 0.8 and is shown as
// (zoom - 0.1) / 0.7 (fullscreen_map_get_zoom_pct, 0x841340); its map opened zoomed all the way out
// (Zoom_map_out_on_start, fsm_determine_zoom_level 0x840540, which zooms at half a unit a second).
// How much of the world a zoom shows is ours: all of it (the picture's 1024 pixels over the screen's
// height) at the least, kViewLeastMetres at the most, evenly in between on a log scale.
// Guerrilla paused its game while the map was up (a game state of its own). Armageddon's pause is
// counted (0x12F76E0): 0x520280(a, b, c, d) pauses at the first count, 0x520310(a, b) unpauses at the
// last; its console's "pause" command calls them with (1, 1, 0, 0) and (1, 1), and its UI screens
// pause through the same (0x58CC36). So the map holds one count while it's open, as a menu does.
constexpr double kZoomMin = 0.1, kZoomMax = 0.8;
constexpr double kZoomPerSecond = 0.5;    // Guerrilla's
constexpr double kZoomPerNotch = 0.05;    // a step of the mouse wheel
constexpr int kWheelNotch = 120;          // WHEEL_DELTA
constexpr double kViewMostMetres = kZonesAcross * kZoneMetres;
constexpr double kViewLeastMetres = 400.0;
constexpr double kPanScreensPerSecond = 0.6;           // panning moves this much of the screen's height a second
constexpr double kZonePicturesBelowMetres = 2000.0;    // the zones' own pictures under a view this close
constexpr size_t kFullMapTiles = 16;
constexpr double kScreenAspect = 16.0 / 9.0;           // the documents' layout, 1280 x 720
constexpr ULONGLONG kSwallowPauseMs = 500;             // after Esc closes the map, the pause menu it asked for isn't opened
auto& g_wheelTotal = *reinterpret_cast<int*>(0x2939B2C);         // the mouse wheel's running total (0xCDF220)
auto& g_pauseMenuRequest = *reinterpret_cast<BYTE*>(0x135B3F1);  // "open the pause menu" (the HUD opens it at 0x57B774)
using PauseFn = void(__cdecl*)(int, int, int, int);
using UnpauseFn = void(__cdecl*)(int, int);
auto const PauseGame = reinterpret_cast<PauseFn>(0x520280);
auto const UnpauseGame = reinterpret_cast<UnpauseFn>(0x520310);

struct FullMap {
    double centreX = 512, centreY = 512;  // the world map picture's pixel in the middle of the screen
    double zoom = kZoomMin;
    bool paused = false;                  // the map holds one of the game's pause counts
    ULONGLONG lastStep = 0;
    int lastWheel = 0;
    bool escDown = false, centreDown = false;
    ULONGLONG swallowPauseUntil = 0;
    int hudSteps = 0, scriptSteps = 0;   // for the log: which of the two ran while the game was paused
};
FullMap g_full;

double ZoomPercent() { return (g_full.zoom - kZoomMin) / (kZoomMax - kZoomMin); }
// How many metres of the world the screen's height shows.
double ViewMetres() { return kViewMostMetres * std::pow(kViewLeastMetres / kViewMostMetres, ZoomPercent()); }
double ViewPixels() { return ViewMetres() / kZoneMetres * kZonePixels; }

void OpenMap() {
    if (g_mapOpen) return;
    g_mapOpen = true;
    g_full.centreX = g_view.mapX;
    g_full.centreY = g_view.mapY;
    g_full.zoom = kZoomMin;
    g_full.lastStep = 0;
    g_full.lastWheel = g_wheelTotal;
    g_full.escDown = HeldDown(VK_ESCAPE);
    g_full.centreDown = HeldDown('X');
    if (!g_full.paused) {
        PauseGame(1, 1, 0, 0);
        g_full.paused = true;
    }
    rfa::Log("World map opened; the game paused");
}

void CloseMap(const char* why) {
    if (g_mapOpen)
        rfa::Log("World map closed (%s); while it was open the HUD's update ran %d times, its script %d", why, g_full.hudSteps,
                 g_full.scriptSteps);
    g_full.hudSteps = g_full.scriptSteps = 0;
    g_mapOpen = false;
    if (g_full.paused) {
        UnpauseGame(1, 1);
        g_full.paused = false;
    }
}

// The world map's key, pressed in the game's window, the console and menus closed: opens or closes it.
// Not with Alt or the Windows key held: Alt+Tab and Win+Tab are Windows' own, and the game still has
// the focus when Tab goes down.
void FollowTheMapKey() {
    if (!g_mapKey || ShopIsOpen()) return;
    const bool down = HeldDown(g_mapKey);
    const bool windowsShortcut = HeldDown(VK_MENU) || HeldDown(VK_LWIN) || HeldDown(VK_RWIN);
    if (down && !g_mapKeyDown && !windowsShortcut && GameFocused()) {
        if (g_mapOpen) CloseMap("its key");
        else if (!MenusShown()) OpenMap();
    }
    g_mapKeyDown = down;
}

// The world map's zone pictures: those of the zones under the view (nearest the middle first, at most
// kFullMapTiles: up to 4 × 3 zones are under a view of 2 km, 16 with the edges), when the view is close
// enough to want them.
std::vector<std::pair<int, int>> FullMapZones() {
    std::vector<std::pair<int, int>> zones;
    if (!g_mapOpen || ViewMetres() >= kZonePicturesBelowMetres) return zones;
    const double halfHeight = ViewPixels() / 2, halfWidth = halfHeight * kScreenAspect;
    std::vector<std::pair<double, std::pair<int, int>>> found;
    for (int c = kFirstZone; c < kFirstZone + kZonesAcross; ++c)
        for (int r = kFirstZone; r < kFirstZone + kZonesAcross; ++r) {
            const double left = (c - kFirstZone) * kZonePixels, top = (r - kFirstZone) * kZonePixels;
            if (left > g_full.centreX + halfWidth || left + kZonePixels < g_full.centreX - halfWidth ||
                top > g_full.centreY + halfHeight || top + kZonePixels < g_full.centreY - halfHeight || !HasTilePicture(c, r))
                continue;
            const double dx = left + kZonePixels / 2 - g_full.centreX, dy = top + kZonePixels / 2 - g_full.centreY;
            found.push_back({dx * dx + dy * dy, {c, r}});
        }
    std::sort(found.begin(), found.end());
    for (size_t i = 0; i < found.size() && i < kFullMapTiles; ++i) zones.push_back(found[i].second);
    return zones;
}

// Each frame while the map is open, from the HUD's update and from the map's own script (whichever
// the game runs while paused; a step moves by the time since the last one): its keys (Guerrilla's
// PAN, ZOOM, CENTER and BACK, on the keyboard and the mouse wheel) and the zone pictures it wants.
void StepFullMap(bool fromScript) {
    const ULONGLONG now = GetTickCount64();
    if (g_mapOpen) ++(fromScript ? g_full.scriptSteps : g_full.hudSteps);
    if (!g_mapOpen) {
        g_full.lastStep = 0;
        return;
    }
    double seconds = g_full.lastStep ? (now - g_full.lastStep) / 1000.0 : 0.0;
    if (seconds > 0.1) seconds = 0.1;  // a stall isn't a long press
    g_full.lastStep = now;
    const int notches = (g_wheelTotal - g_full.lastWheel) / kWheelNotch;
    g_full.lastWheel += notches * kWheelNotch;
    FollowTheMapKey();
    if (!g_mapOpen) return;
    const bool focused = GameFocused();
    if (Pressed(VK_ESCAPE, g_full.escDown) && focused) {
        g_full.swallowPauseUntil = now + kSwallowPauseMs;
        CloseMap("Esc");
        return;
    }
    if (focused) {
        const double right = (HeldDown('D') || HeldDown(VK_RIGHT) ? 1.0 : 0.0) - (HeldDown('A') || HeldDown(VK_LEFT) ? 1.0 : 0.0);
        const double down = (HeldDown('S') || HeldDown(VK_DOWN) ? 1.0 : 0.0) - (HeldDown('W') || HeldDown(VK_UP) ? 1.0 : 0.0);
        const double pan = kPanScreensPerSecond * ViewPixels() * seconds;
        g_full.centreX += right * pan;
        g_full.centreY += down * pan;
        const double zoomIn = (HeldDown(VK_PRIOR) || HeldDown('E') || HeldDown(VK_ADD) || HeldDown(VK_OEM_PLUS) ? 1.0 : 0.0) -
                              (HeldDown(VK_NEXT) || HeldDown('Q') || HeldDown(VK_SUBTRACT) || HeldDown(VK_OEM_MINUS) ? 1.0 : 0.0);
        g_full.zoom += zoomIn * kZoomPerSecond * seconds + notches * kZoomPerNotch;
        if (Pressed('X', g_full.centreDown)) {
            g_full.centreX = g_view.mapX;
            g_full.centreY = g_view.mapY;
        }
    }
    g_full.zoom = g_full.zoom < kZoomMin ? kZoomMin : g_full.zoom > kZoomMax ? kZoomMax : g_full.zoom;
    g_full.centreX = g_full.centreX < 0 ? 0 : g_full.centreX > kMapPixels ? kMapPixels : g_full.centreX;
    g_full.centreY = g_full.centreY < 0 ? 0 : g_full.centreY > kMapPixels ? kMapPixels : g_full.centreY;
}

// While the map is open the game is paused already, and the pause menu isn't opened over it: Esc closes
// the map (the game asks for its menu on the same key), and a request for it otherwise (borderless asks
// for it when the window loses the focus) is dropped. With the pause menu up, the game stops the HUD's
// update, which loads the map's zone pictures (2026-11-07: none loaded after an Alt+Tab). Called from
// the HUD's update, which runs before the HUD opens the menu (0x57B774).
void SwallowPauseMenuRequest() {
    if (!g_pauseMenuRequest) return;
    if (g_mapOpen || ShopIsOpen() || (GetTickCount64() < g_full.swallowPauseUntil && HeldDown(VK_ESCAPE))) {
        g_pauseMenuRequest = 0;
        RFA_LOG_FIRST(20, "The pause menu wasn't opened: the world map is open (or Esc just closed it)");
    }
}

// Whether a document should be loaded now (in our world): all of them, for as long as the level lasts
// (see the zone pictures' documents above).
bool Wanted(const Document&) { return true; }

// Whether a loaded document should be on the draw list now.
// While a shop is open only its screen is drawn (Guerrilla hid its HUD for it).
bool ShouldDraw(const Document& doc, bool hudRuns) {
    if (ShopIsOpen() && !doc.whileShop && !doc.zoneSet) return false;
    return doc.drawn && hudRuns && (!doc.whileOpen || g_mapOpen) && (!doc.whileShop || ShopIsOpen());
}

void Unload(Document& doc, BYTE* open) {
    const unsigned short handle = *reinterpret_cast<unsigned short*>(open + 0x40);
    if (doc.listed) RemoveFromDrawList(g_drawList, nullptr, &handle);
    UnloadDocument(handle);
    RFA_LOG_FIRST(200, "Unloaded %s", doc.name.c_str());
    doc.loaded = doc.seen = doc.listed = false;
    doc.readyAt = 0;
}

// The game's state (0x53FE80, cdecl: the top of its state stack, [0x10F8BE8] into 0x1307898, -1 for
// none); 3 is the level load (registered by 0x55D6D0, see LoadStart).
using GameStateFn = int(__cdecl*)();
auto const GameState = reinterpret_cast<GameStateFn>(0x53FE80);
constexpr int kLoadingState = 3;
constexpr ULONGLONG kAfterLoadStartMs = 1000;  // the load's state may not be on the stack yet in the frame it starts
ULONGLONG g_loadStartedAt = 0;

bool LevelLoading() {
    return GameState() == kLoadingState || (g_loadStartedAt != 0 && GetTickCount64() - g_loadStartedAt < kAfterLoadStartMs);
}

// In our worlds, the documents wanted are loaded (and loaded again if the game closes one); the
// others the mod loaded are unloaded, and all of them in any other world.
void FollowTheWorld() {
    const bool ours = InOurWorld();
    if (static_cast<int>(ours) != g_inOurWorld) {
        rfa::Log(ours ? "World %s: Guerrilla's HUD" : "World %s: Armageddon's own HUD", WorldName());
        g_inOurWorld = ours;
        if (!ours) CloseMap("another world");
    }
    // Nothing is loaded or unloaded during a level load, nor the player read (see LoadStart): the HUD's
    // update can run again in the frame the load starts, and once did (2026-11-07, the reload crash at
    // 04:05:37: it loaded all 20 documents again into the memory the load was freeing).
    if (LevelLoading()) {
        RFA_LOG_FIRST(20, "A level load runs: Guerrilla's HUD documents wait for it to end");
        return;
    }
    if (ours) {
        UpdateMapView();
        if (g_mapOpen) StepFullMap(false);
        else FollowTheMapKey();
        FollowRadioTags();
        FollowSafehouses();
    } else {
        g_view.active = false;
    }
    for (Document& doc : g_documents) {
        const bool wanted = ours && Wanted(doc);
        if (!wanted && !doc.loaded) continue;  // most zone pictures, most of the time: nothing to ask the game
        BYTE* open = OpenDocument(doc.name.c_str());
        if (open && !doc.seen) {
            if (doc.loaded) RFA_LOG_FIRST(200, "The game has %s open", doc.name.c_str());
            doc.seen = true;
        }
        if (!wanted) {
            if (doc.loaded && open) Unload(doc, open);  // one still loading is unloaded once it is open
            continue;
        }
        if (open || doc.missing || (doc.loaded && !doc.seen) || GetTickCount64() < doc.retryAt) continue;
        if (doc.loaded) {
            // The game closes a document whose containers don't fit its interface memory (see
            // EnlargeInterfacePool); loading it again and again wouldn't help.
            if (++doc.closedByGame > kClosedByGameTimes) {
                rfa::Log("The game closed %s %d times; leaving it closed (out of interface memory? see the game's "
                         "\"interface cpu pool\" message)", doc.name.c_str(), kClosedByGameTimes);
                doc.loaded = false;
                doc.missing = true;
                continue;
            }
            rfa::Log("The game closed %s; loading it again", doc.name.c_str());
        }
        if (!InProject(doc.name.c_str())) {
            rfa::Log("%s isn't in the game's interface project: build Guerrilla's HUD from your copy "
                     "of Guerrilla first (editor\\GuerrillaBuilder.exe build hud)", doc.name.c_str());
            doc.missing = true;
            continue;
        }
        RFA_LOG_FIRST(200, "Loading %s", doc.name.c_str());  // before the game's call, so a log ending here points at it
        const unsigned short handle = LoadWhilePlaying(doc.name.c_str());
        if (handle == kNoDocument) {
            // A zone's picture is asked for again a little later: the game refuses one when its room for
            // open documents is full (kMaxOpenDocuments), which frees up as other zones' pictures go.
            if (doc.zoneSet) {
                RFA_LOG_FIRST(40, "The game couldn't load %s (no room for another open document?); asking again in %llu s",
                              doc.name.c_str(), kRetryRefusedMs / 1000);
                doc.retryAt = GetTickCount64() + kRetryRefusedMs;
            } else {
                rfa::Log("The game couldn't load %s", doc.name.c_str());
                doc.missing = true;
            }
            continue;
        }
        doc.loaded = true;
        doc.seen = false;
        doc.listed = ShouldDraw(doc, true);
        if (doc.listed) AddToDrawList(g_drawList, nullptr, &handle);
        RFA_LOG_FIRST(200, "Loaded %s (handle %u)%s", doc.name.c_str(), handle, doc.listed ? ", and listed it to be drawn" : "");
    }
}

void __cdecl HudUpdate(void* hud) {
    g_hudUpdate(hud);
    g_lastHudUpdate = GetTickCount64();
    FollowTheWorld();
    SwallowPauseMenuRequest();
    // For the log: an element pool past the game's own size (each pool: +0x0C in use, +0x10 room).
    struct PoolWatch { const char* type; DWORD pool; DWORD game; DWORD logged; };
    static PoolWatch watched[] = {{"tween", 0x28EEB00, 0x334, 0}, {"document", 0x28F15CC, 0xC4, 0},
                                  {"group", 0x28F1684, 0x1DC, 0}, {"text", 0x28F18D0, 0x118, 0},
                                  {"bitmap", 0x28F19FC, 0x514, 0}, {"animation", 0x28F1B4C, 0x16C, 0}};
    for (PoolWatch& watch : watched) {
        const DWORD inUse = *reinterpret_cast<const DWORD*>(watch.pool + 0x0C);
        if (inUse > watch.game && inUse > watch.logged + 50) {
            watch.logged = inUse;
            RFA_LOG_FIRST(30, "Interface elements: %lu %ss in use (the game had room for %lu, now %lu)", inUse, watch.type,
                          watch.game, *reinterpret_cast<const DWORD*>(watch.pool + 0x10));
        }
    }
    // For the log: the most documents the streaming queue has held (the game's lists held 16).
    static int mostQueued = kGameDocumentListRoom / 2;
    if (const int queued = StreamingQueueCount(); queued > mostQueued) {
        mostQueued = queued;
        RFA_LOG_FIRST(30, "Interface: %d documents waiting in the streaming queue (the game had room for %d)", queued,
                      kGameDocumentListRoom);
    }
}

// Before each draw pass: our loaded documents on the draw list while the HUD runs (the world map
// only while it is open), off it otherwise.
bool __cdecl DrawPass(bool flag) {
    // While the world map is open the game is paused, which may stop the HUD's update: still ours.
    const bool hudRuns = g_mapOpen || ShopIsOpen() || GetTickCount64() - g_lastHudUpdate <= kHudGoneAfterMs;
    for (Document& doc : g_documents) {
        const bool draw = ShouldDraw(doc, hudRuns);
        if (!doc.loaded || doc.listed == draw) continue;
        BYTE* open = OpenDocument(doc.name.c_str());
        if (!open) continue;
        const unsigned short handle = *reinterpret_cast<unsigned short*>(open + 0x40);
        if (draw) AddToDrawList(g_drawList, nullptr, &handle);
        else RemoveFromDrawList(g_drawList, nullptr, &handle);
        doc.listed = draw;
        RFA_LOG_FIRST(40, draw ? "%s shown" : "%s hidden", doc.name.c_str());
    }
    return g_drawPass(flag);
}

// Every level load starts in the game's loading state (state 3, registered by 0x55D6D0): a reload
// (death, a restart, the console's level), another world, or the main menu. Its start, 0x55D870
// (called through the state's table), prints "LOAD SCREEN: Start time" and closes the screens
// that are up (0x565C80). The load frees the interface memory our documents' containers are in,
// but leaves the documents themselves open. After one reload, the game was told to unload the map
// pictures the player had left behind; it released pictures whose memory was already used for
// other pictures, and crashed (2026-11-06, rfg_ui_map_07_11). So the mod unloads all of its
// documents as each load starts, while their memory is still theirs, as the game does with its own
// screens there. The ones still wanted load again once the HUD runs.
constexpr DWORD kLoadStart = 0x55D870;
constexpr size_t kLoadStartPrologue = 6;  // push ebp / mov ebp,esp / sub esp,8
using LoadStartFn = void(__cdecl*)();
LoadStartFn g_loadStart;

void __cdecl LoadStart() {
    g_loadStartedAt = GetTickCount64();
    CloseMap("a level load");  // and the game unpaused
    CloseShop("a level load");
    int unloaded = 0, forgotten = 0;
    for (Document& doc : g_documents) {
        doc.closedByGame = 0;
        if (!doc.loaded) continue;
        if (BYTE* open = OpenDocument(doc.name.c_str())) {
            Unload(doc, open);
            ++unloaded;
        } else {
            doc.loaded = doc.seen = doc.listed = false;  // still waiting: the load empties the game's queue
            doc.readyAt = 0;
            ++forgotten;
        }
    }
    if (unloaded || forgotten)
        RFA_LOG_FIRST(40, "A load starts: unloaded %d of Guerrilla's HUD documents (and forgot %d still waiting)", unloaded, forgotten);
    g_loadStart();
}

// ---- Guerrilla's radio tags ("Blast from the past")
//
// Guerrilla hid 36 radio tags about its world (object_bftp_node with tag_node 1), each paired with a
// M.O.A.B. (the node with the same pair_number and tag_node 0). Its blast_from_the_past_process_car_bombs
// (RM 0xAA4C60) finds a tag when the player is on foot within 1.5 m of it (distance squared under 2.25,
// [0x1311020]); bftp_touch_tag (0xA8DB50) then counts it, and every third tag found
// (blast_from_the_past_tags_collected() % 3 == 0) shows that tag's pair's M.O.A.B. on the maps
// (spawn_bomb_from_tag: the bomb node's add_icon with MAP_ELEMENT_CARBOMB) and says so
// (HM_BFTP_SPAWN_BOMB); otherwise it says how many more tags are needed (HM_BFTP_TAG_FOUND for one,
// HM_BFTP_TAGS_FOUND), each a HUD message for 4 s; here Armageddon's own message line (0x581820, which
// shows one for 5 s). Not yet: the bomb itself (the item bftp_bomb, which a car drives up to, to start
// Guerrilla's car bomb activity), the handbook's entry, the tool tips, the sound, the stats.
// The tags found are kept in mods\guerrilla_hud\radio_tags_found.txt, one list for every save.
struct RadioTag {
    bool tag = false;  // a radio tag, else a M.O.A.B.
    int pair = 0;
    float x = 0, y = 0, z = 0;
};
std::vector<RadioTag> g_radioTags;
std::map<std::string, std::string> g_radioTagTexts;  // Guerrilla's messages: tag_found, tags_found, spawn_bomb
std::set<int> g_tagsFound;                           // the pairs whose radio tag was found
std::vector<int> g_tagsFoundOrder;                   // and in the order found
std::set<int> g_bombsShown;                          // the pairs whose M.O.A.B. is on the maps
constexpr float kTagReachSquared = 2.25f;
constexpr DWORD kHumanVehicle = 0x1160;              // the vehicle a human is in (as guerrilla_vehicles reads it)
using AddHudMessageFn = void(__cdecl*)(const char* text);
auto const AddHudMessage = reinterpret_cast<AddHudMessageFn>(0x581820);

std::wstring RadioTagsFoundPath() { return rfa::RootDir() + L"\\mods\\guerrilla_hud\\radio_tags_found.txt"; }

// Which M.O.A.B.s the tags found show: Guerrilla's rule, every third tag shows its own pair's.
void ShowBombsOfTagsFound() {
    g_bombsShown.clear();
    for (size_t i = 0; i < g_tagsFoundOrder.size(); ++i)
        if ((i + 1) % 3 == 0) g_bombsShown.insert(g_tagsFoundOrder[i]);
}

// radio_tags.txt (made by GuerrillaBuilder.exe) and the list of tags found.
void ReadRadioTags() {
    FILE* file = nullptr;
    if (_wfopen_s(&file, (rfa::RootDir() + L"\\mods\\guerrilla_hud\\radio_tags.txt").c_str(), L"r") != 0 || !file) {
        rfa::Log("No mods\\guerrilla_hud\\radio_tags.txt (editor\\GuerrillaBuilder.exe build hud makes it): no radio tags");
        return;
    }
    char line[1024];
    while (fgets(line, sizeof(line), file)) {
        if (line[0] == ';') continue;
        std::vector<std::string> fields(1);
        for (const char* c = line; *c && *c != '\n' && *c != '\r'; ++c) {
            if (*c == '|') fields.emplace_back();
            else fields.back() += *c;
        }
        if (fields[0] == "text" && fields.size() >= 3) {
            g_radioTagTexts[fields[1]] = fields[2];
        } else if ((fields[0] == "tag" || fields[0] == "bomb") && fields.size() >= 5) {
            RadioTag node;
            node.tag = fields[0] == "tag";
            node.pair = atoi(fields[1].c_str());
            node.x = static_cast<float>(atof(fields[2].c_str()));
            node.y = static_cast<float>(atof(fields[3].c_str()));
            node.z = static_cast<float>(atof(fields[4].c_str()));
            g_radioTags.push_back(node);
        }
    }
    fclose(file);
    if (_wfopen_s(&file, RadioTagsFoundPath().c_str(), L"r") == 0 && file) {
        while (fgets(line, sizeof(line), file)) {
            if (line[0] == ';' || line[0] < '0' || line[0] > '9') continue;
            const int pair = atoi(line);
            if (g_tagsFound.insert(pair).second) g_tagsFoundOrder.push_back(pair);
        }
        fclose(file);
    }
    ShowBombsOfTagsFound();
    rfa::Log("%u radio tags and M.O.A.B.s; %u tags found so far, %u M.O.A.B.s shown", static_cast<unsigned>(g_radioTags.size()),
             static_cast<unsigned>(g_tagsFound.size()), static_cast<unsigned>(g_bombsShown.size()));
}

void SaveRadioTagsFound() {
    FILE* file = nullptr;
    if (_wfopen_s(&file, RadioTagsFoundPath().c_str(), L"w") != 0 || !file) {
        rfa::Log("Couldn't write mods\\guerrilla_hud\\radio_tags_found.txt");
        return;
    }
    fputs("; Guerrilla's radio tags you have found (their pair numbers, in the order found), kept by guerrilla_hud.\n"
          "; Every third shows its M.O.A.B. on the maps. Delete this file to start the hunt again.\n", file);
    for (int pair : g_tagsFoundOrder) fprintf(file, "%d\n", pair);
    fclose(file);
}

// Guerrilla's message with the number of tags still needed put in for its %ls.
std::string RadioTagMessage(const char* what, int number) {
    std::string text = g_radioTagTexts.count(what) ? g_radioTagTexts[what] : what;
    const size_t at = text.find("%ls");
    if (at != std::string::npos) text.replace(at, 3, std::to_string(number));
    return text;
}

// Each HUD update in our world: a radio tag the player walks up to on foot is found (bftp_touch_tag).
void FollowRadioTags() {
    BYTE* player = g_player;
    if (!player || g_radioTags.empty()) return;
    const DWORD vehicle = *reinterpret_cast<const DWORD*>(player + kHumanVehicle);
    if (vehicle != 0xFFFFFFFF && vehicle != 0) return;
    const float* position = reinterpret_cast<const float*>(player + 4);
    for (const RadioTag& node : g_radioTags) {
        if (!node.tag || g_tagsFound.count(node.pair)) continue;
        const float dx = node.x - position[0], dy = node.y - position[1], dz = node.z - position[2];
        if (dx * dx + dy * dy + dz * dz >= kTagReachSquared) continue;
        g_tagsFound.insert(node.pair);
        g_tagsFoundOrder.push_back(node.pair);
        SaveRadioTagsFound();
        const int left = 3 - static_cast<int>(g_tagsFoundOrder.size() % 3);
        std::string message;
        if (left == 3) {
            g_bombsShown.insert(node.pair);
            message = RadioTagMessage("spawn_bomb", 0);
        } else {
            message = left == 1 ? RadioTagMessage("tag_found", 1) : RadioTagMessage("tags_found", left);
        }
        AddHudMessage(message.c_str());
        rfa::Log("Radio tag %d found (%u of 36): %s", node.pair, static_cast<unsigned>(g_tagsFoundOrder.size()), message.c_str());
    }
}

// ---- Guerrilla's safehouses: the upgrade shop
//
// Each of Guerrilla's safehouses has an upgrade shop (its object_upgrade_node, at the safehouse's own spot) and a
// weapon cabinet (an object_action_node "Player Open Weapon Cabinet"), which Armageddon never makes: safehouses.txt
// (made by GuerrillaBuilder.exe) has where they are. Walking up to a shop on foot shows Guerrilla's use prompt
// (rfg_ui_messages), and the game's own use key (the action 0x4E1B70 gives for what the player is doing, held as
// 0x4E4800 reads it, so whatever the player bound) opens Guerrilla's upgrade screen (rfg_ui_upgrades). Guerrilla ran
// it as a game state of its own (hud_ui_upgrades_process, RM 0x87FD20) with the game paused; here it takes one of
// the game's pause counts, as the world map does. The upgrades and buying are guerrilla_gear's (its F7 window's):
// the list through its exported GuerrillaGear_Upgrade, a purchase at once (GuerrillaGear_BuyNow, on this thread).
// Keys while it's open: W / S, the arrows or the mouse wheel pick, the use key, Enter or Space buy, Esc or Tab
// close. Not yet: the weapon cabinet (its screen, rfg_ui_safehouse_weapons), Guerrilla's purchase message box.
struct GuerrillaGearUpgrade {  // as guerrilla_gear's upgrades.cpp has it
    int size;
    char name[64];
    char shown[96];
    char type[32];
    char levelName[96];
    char levelText[256];
    int bought;
    int levels;
    int cost;
    int available;
};
using GearCountFn = int(__cdecl*)();
using GearUpgradeFn = bool(__cdecl*)(int, GuerrillaGearUpgrade*);
using GearBuyNowFn = void(__cdecl*)(BYTE*, int);
using GearStatusFn = void(__cdecl*)(char*, int);
struct GearApi {
    GearCountFn count = nullptr;
    GearUpgradeFn upgrade = nullptr;
    GearBuyNowFn buyNow = nullptr;
    GearStatusFn status = nullptr;
};

GearApi& Gear() {
    static GearApi api;
    static bool asked = false;
    if (!asked) {
        asked = true;
        if (HMODULE gear = GetModuleHandleW(L"guerrilla_gear.dll")) {
            api.count = reinterpret_cast<GearCountFn>(GetProcAddress(gear, "GuerrillaGear_UpgradeCount"));
            api.upgrade = reinterpret_cast<GearUpgradeFn>(GetProcAddress(gear, "GuerrillaGear_Upgrade"));
            api.buyNow = reinterpret_cast<GearBuyNowFn>(GetProcAddress(gear, "GuerrillaGear_BuyNow"));
            api.status = reinterpret_cast<GearStatusFn>(GetProcAddress(gear, "GuerrillaGear_Status"));
        }
        if (!api.count || !api.upgrade || !api.buyNow || !api.status)
            rfa::Log("Safehouses: guerrilla_gear.dll's upgrades aren't there (an older guerrilla_gear, or it's off): the shops stay shut");
    }
    return api;
}
bool GearReady() {
    const GearApi& api = Gear();
    return api.count && api.upgrade && api.buyNow && api.status;
}

struct Station {
    bool shop = false;  // an upgrade shop, else a weapon cabinet
    float x = 0, y = 0, z = 0;
};
std::vector<Station> g_stations;
std::map<std::string, std::string> g_shopTexts;  // the upgrade screen's words: title, price, credits, select, back, locked, use
constexpr float kStationReachSquared = 2.5f * 2.5f;
using UseActionFn = int(__cdecl*)(int player);
using ButtonHeldFn = bool(__cdecl*)(int action, int player);
auto const UseAction = reinterpret_cast<UseActionFn>(0x4E1B70);
auto const ButtonHeld = reinterpret_cast<ButtonHeldFn>(0x4E4800);
auto& g_localPlayer = *reinterpret_cast<int*>(0x11C0580);
auto const GetSalvage = reinterpret_cast<bool(__cdecl*)(int* out)>(0x54C2B0);

struct Shop {
    bool open = false;
    bool paused = false;
    int nearStation = -1;   // the station the player could use now
    bool useDown = true;    // the use key's last state (true at first: a key held then isn't a press)
    std::vector<GuerrillaGearUpgrade> list;
    int highlighted = 0;    // in list
    int top = 0;            // the list's first row shown
    int lastWheel = 0;
    bool upDown = false, wDown = false, downDown = false, sDown = false, buyDown = false, closeDown = false;
    std::string status;     // the last purchase's outcome, shown for a while
    ULONGLONG statusUntil = 0;
};
Shop g_shop;
constexpr int kShopRows = 4;  // Guerrilla's screen has four rows (MAX_BUTTONS)
constexpr ULONGLONG kShopStatusMs = 3000;

// safehouses.txt (made by GuerrillaBuilder.exe): "shop|x|y|z", "cabinet|x|y|z", "text|what|text".
void ReadSafehouses() {
    FILE* file = nullptr;
    if (_wfopen_s(&file, (rfa::RootDir() + L"\\mods\\guerrilla_hud\\safehouses.txt").c_str(), L"r") != 0 || !file) {
        rfa::Log("No mods\\guerrilla_hud\\safehouses.txt (editor\\GuerrillaBuilder.exe build hud makes it): no safehouse shops");
        return;
    }
    char line[512];
    while (fgets(line, sizeof(line), file)) {
        if (line[0] == ';') continue;
        std::vector<std::string> fields(1);
        for (const char* c = line; *c && *c != '\n' && *c != '\r'; ++c) {
            if (*c == '|') fields.emplace_back();
            else fields.back() += *c;
        }
        if (fields[0] == "text" && fields.size() >= 3) {
            g_shopTexts[fields[1]] = fields[2];
        } else if ((fields[0] == "shop" || fields[0] == "cabinet") && fields.size() >= 4) {
            Station station;
            station.shop = fields[0] == "shop";
            station.x = static_cast<float>(atof(fields[1].c_str()));
            station.y = static_cast<float>(atof(fields[2].c_str()));
            station.z = static_cast<float>(atof(fields[3].c_str()));
            g_stations.push_back(station);
        }
    }
    fclose(file);
    rfa::Log("%u safehouse stations", static_cast<unsigned>(g_stations.size()));
}

void RefreshShopList() {
    GearApi& api = Gear();
    g_shop.list.clear();
    const int count = api.count();
    for (int i = 0; i < count; ++i) {
        GuerrillaGearUpgrade upgrade{};
        upgrade.size = sizeof(upgrade);
        if (api.upgrade(i, &upgrade)) g_shop.list.push_back(upgrade);
    }
    if (g_shop.highlighted >= static_cast<int>(g_shop.list.size())) g_shop.highlighted = static_cast<int>(g_shop.list.size()) - 1;
    if (g_shop.highlighted < 0) g_shop.highlighted = 0;
}

void OpenShop() {
    g_shop.open = true;
    g_shop.highlighted = g_shop.top = 0;
    g_shop.lastWheel = g_wheelTotal;
    g_shop.upDown = g_shop.wDown = g_shop.downDown = g_shop.sDown = g_shop.buyDown = g_shop.closeDown = true;  // keys held as it opens aren't presses
    g_shop.status.clear();
    RefreshShopList();
    if (!g_shop.paused) {
        PauseGame(1, 1, 0, 0);
        g_shop.paused = true;
    }
    rfa::Log("Upgrade shop opened (%u upgrades); the game paused", static_cast<unsigned>(g_shop.list.size()));
}

void CloseShop(const char* why) {
    if (g_shop.open) rfa::Log("Upgrade shop closed (%s)", why);
    g_shop.open = false;
    g_shop.useDown = true;
    if (g_shop.paused) {
        UnpauseGame(1, 1);
        g_shop.paused = false;
    }
}

// The shop's keys, each HUD update while it's open.
void StepShop() {
    if (!GameFocused()) return;
    const int notches = (g_wheelTotal - g_shop.lastWheel) / kWheelNotch;
    g_shop.lastWheel += notches * kWheelNotch;
    int move = -notches;
    if (Pressed(VK_UP, g_shop.upDown)) move -= 1;
    if (Pressed('W', g_shop.wDown)) move -= 1;
    if (Pressed(VK_DOWN, g_shop.downDown)) move += 1;
    if (Pressed('S', g_shop.sDown)) move += 1;
    const int count = static_cast<int>(g_shop.list.size());
    if (count > 0 && move != 0) {
        g_shop.highlighted = (g_shop.highlighted + move) % count;
        if (g_shop.highlighted < 0) g_shop.highlighted += count;
    }
    if (g_shop.highlighted < g_shop.top) g_shop.top = g_shop.highlighted;
    if (g_shop.highlighted >= g_shop.top + kShopRows) g_shop.top = g_shop.highlighted - kShopRows + 1;
    const bool use = ButtonHeld(UseAction(g_localPlayer), g_localPlayer);
    const bool buy = use || HeldDown(VK_RETURN) || HeldDown(VK_SPACE);
    const bool buyPressed = buy && !g_shop.buyDown;
    g_shop.buyDown = buy;
    if (buyPressed && count > 0 && g_player) {
        const GuerrillaGearUpgrade& chosen = g_shop.list[g_shop.highlighted];
        if (chosen.available) {
            Gear().buyNow(g_player, g_shop.highlighted);
            char status[256] = {};
            Gear().status(status, sizeof(status));
            g_shop.status = status;
            g_shop.statusUntil = GetTickCount64() + kShopStatusMs;
            RefreshShopList();
        }
    }
    const bool close = HeldDown(VK_ESCAPE) || HeldDown(VK_TAB) || HeldDown(VK_BACK);
    if (close && !g_shop.closeDown) {
        g_full.swallowPauseUntil = GetTickCount64() + kSwallowPauseMs;
        CloseShop("its key");
    }
    g_shop.closeDown = close;
}

// Each HUD update in our world: the station the player is at (on foot), and the use key there.
void FollowSafehouses() {
    if (g_shop.open) {
        StepShop();
        return;
    }
    g_shop.nearStation = -1;
    BYTE* player = g_player;
    if (!player || g_stations.empty()) return;
    const DWORD vehicle = *reinterpret_cast<const DWORD*>(player + kHumanVehicle);
    const float* position = reinterpret_cast<const float*>(player + 4);
    if (vehicle == 0xFFFFFFFF || vehicle == 0)
        for (size_t i = 0; i < g_stations.size(); ++i) {
            const Station& station = g_stations[i];
            if (!station.shop) continue;  // the weapon cabinets come later
            const float dx = station.x - position[0], dy = station.y - position[1], dz = station.z - position[2];
            if (dx * dx + dy * dy + dz * dz < kStationReachSquared) g_shop.nearStation = static_cast<int>(i);
        }
    const bool use = ButtonHeld(UseAction(g_localPlayer), g_localPlayer);
    if (use && !g_shop.useDown && g_shop.nearStation >= 0 && !g_mapOpen && !MenusShown() && GameFocused() && GearReady()) OpenShop();
    g_shop.useDown = use;
}

bool ShopIsOpen() { return g_shop.open; }

// ---- What the documents' scripts ask (Lua, on the game's thread)

using rfa::game::lua_State;

// rfg_station_prompt() -> near, kind
//   Whether the player is at one of Guerrilla's safehouse stations now (on foot, within reach): its kind,
//   "shop" (or "cabinet", later). For Guerrilla's use prompt.
int __cdecl LuaStationPrompt(lua_State* L) {
    using namespace rfa::game;
    const bool atStation = !g_shop.open && g_shop.nearStation >= 0;
    lua_pushboolean(L, atStation);
    lua_pushstring(L, atStation && g_stations[g_shop.nearStation].shop ? "shop" : "cabinet");
    return 2;
}

// rfg_shop() -> open, count, highlighted, top, salvage, status
//   The upgrade shop: whether it's open; how many upgrades it lists; the one picked and the first row
//   shown (1 to the count); the player's salvage; the last purchase's outcome ("" after a while).
int __cdecl LuaShop(lua_State* L) {
    using namespace rfa::game;
    int salvage = 0;
    if (!GetSalvage(&salvage)) salvage = 0;
    lua_pushboolean(L, g_shop.open);
    lua_pushnumber(L, static_cast<double>(g_shop.list.size()));
    lua_pushnumber(L, g_shop.highlighted + 1);
    lua_pushnumber(L, g_shop.top + 1);
    lua_pushnumber(L, salvage);
    lua_pushstring(L, GetTickCount64() < g_shop.statusUntil ? g_shop.status.c_str() : "");
    return 6;
}

// rfg_shop_item(i) -> name, shown, type, level_name, level_text, bought, levels, cost, available
//   The i-th upgrade the shop lists (guerrilla_gear's): its name, as shown, its kind; its next level's
//   name and description ("" when all are bought); levels bought and in all; the next one's cost; whether it
//   can be bought now. Nothing for an i out of range.
int __cdecl LuaShopItem(lua_State* L) {
    using namespace rfa::game;
    const int index = static_cast<int>(lua_tonumber(L, 1)) - 1;
    if (index < 0 || index >= static_cast<int>(g_shop.list.size())) return 0;
    const GuerrillaGearUpgrade& u = g_shop.list[index];
    lua_pushstring(L, u.name);
    lua_pushstring(L, u.shown);
    lua_pushstring(L, u.type);
    lua_pushstring(L, u.levelName);
    lua_pushstring(L, u.levelText);
    lua_pushnumber(L, u.bought);
    lua_pushnumber(L, u.levels);
    lua_pushnumber(L, u.cost);
    lua_pushboolean(L, u.available != 0);
    return 9;
}

// rfg_shop_text(what) -> Guerrilla's word for it on the upgrade screen (safehouses.txt): title, price,
//   credits, select, back, locked, use; "" if unknown.
int __cdecl LuaShopText(lua_State* L) {
    using namespace rfa::game;
    const char* what = lua_tolstring(L, 1, nullptr);
    auto found = what ? g_shopTexts.find(what) : g_shopTexts.end();
    lua_pushstring(L, found != g_shopTexts.end() ? found->second.c_str() : "");
    return 1;
}

// rfg_map_view() -> active, map_x, map_y, heading, speed, column, row, district, map_open
//   active: in Guerrilla's world, with a player; map_x, map_y: the player on Guerrilla's world map
//   picture, in its pixels (0-1024); heading: which way the camera looks (radians, 0 north, π/2
//   east); speed: the player's (m/s, smoothed); column, row: the zone the player is in; district:
//   its name ("" if unknown); map_open: whether the world map is open.
int __cdecl LuaMapView(lua_State* L) {
    using namespace rfa::game;
    lua_pushboolean(L, g_view.active);
    lua_pushnumber(L, g_view.mapX);
    lua_pushnumber(L, g_view.mapY);
    lua_pushnumber(L, g_view.heading);
    lua_pushnumber(L, g_view.speed);
    lua_pushnumber(L, g_view.column);
    lua_pushnumber(L, g_view.row);
    lua_pushstring(L, g_view.district.c_str());
    lua_pushboolean(L, g_mapOpen);
    return 9;
}

// rfg_map_tiles() -> count, column, row, column, row, ...
//   The zones around the player whose own pictures are loaded (rfg_terr01_<column>_<row>_map).
int __cdecl LuaMapTiles(lua_State* L) {
    using namespace rfa::game;
    if (!g_view.active) {
        lua_pushnumber(L, 0);
        return 1;
    }
    int zones[9][2];
    int count = 0;
    for (int dc = -1; dc <= 1; ++dc)
        for (int dr = -1; dr <= 1; ++dr) {
            const int c = g_view.column + dc, r = g_view.row + dr;
            if (c < kFirstZone || r < kFirstZone || c >= kFirstZone + kZonesAcross || r >= kFirstZone + kZonesAcross) continue;
            if (!HasTilePicture(c, r) || !ZoneReady(c, r)) continue;
            zones[count][0] = c;
            zones[count][1] = r;
            ++count;
        }
    lua_pushnumber(L, count);
    for (int i = 0; i < count; ++i) {
        lua_pushnumber(L, zones[i][0]);
        lua_pushnumber(L, zones[i][1]);
    }
    return 1 + 2 * count;
}

// rfg_map_icon_count() -> how many icons and places map_icons.txt has.
int __cdecl LuaMapIconCount(lua_State* L) {
    rfa::game::lua_pushnumber(L, static_cast<double>(g_mapIcons.size()));
    return 1;
}

// rfg_map_icon(i) -> kind, picture, map_x, map_y, caption, description, on_world_map
//   The i-th line (1 to the count) of map_icons.txt: kind "icon" (its picture, rfg_ui_map_icon_...),
//   "place" (a named place, its name in caption), "player" (the player's icon: its picture and texts)
//   or "text" (a hint of the world map: what it is in caption, its text in description); where it is
//   on Guerrilla's world map picture, in its pixels (as rfg_map_view's map_x, map_y); what the world
//   map's highlight box says of it; whether the world map shows it (the minimap shows every icon).
//   Nothing for an i out of range.
int __cdecl LuaMapIcon(lua_State* L) {
    using namespace rfa::game;
    const int index = static_cast<int>(lua_tonumber(L, 1)) - 1;
    if (index < 0 || index >= static_cast<int>(g_mapIcons.size())) return 0;
    const MapIcon& icon = g_mapIcons[index];
    lua_pushstring(L, icon.kind.c_str());
    lua_pushstring(L, icon.picture.c_str());
    lua_pushnumber(L, icon.mapX);
    lua_pushnumber(L, icon.mapY);
    lua_pushstring(L, icon.caption.c_str());
    lua_pushstring(L, icon.description.c_str());
    lua_pushboolean(L, icon.onWorldMap);
    return 7;
}

// rfg_map_icon_shown(i) -> whether the i-th line of map_icons.txt is shown now: a M.O.A.B. ("bomb") once
//   its radio tags are found, everything else always.
int __cdecl LuaMapIconShown(lua_State* L) {
    using namespace rfa::game;
    const int index = static_cast<int>(lua_tonumber(L, 1)) - 1;
    bool shown = index >= 0 && index < static_cast<int>(g_mapIcons.size());
    if (shown && g_mapIcons[index].kind == "bomb") shown = g_bombsShown.count(g_mapIcons[index].pair) != 0;
    lua_pushboolean(L, shown);
    return 1;
}

// rfg_map_close(): closes the world map (its script's own close).
int __cdecl LuaMapClose(lua_State*) {
    CloseMap("its script");
    return 0;
}

// rfg_map_full() -> open, centre_x, centre_y, view, zoom_pct, district
//   The world map's view: open; the world map picture's pixel in the middle of the screen; how many of
//   its pixels the screen's height shows; Guerrilla's zoom (0 to 1); the district's name under the
//   middle ("" outside them). Steps the map's keys first (see StepFullMap).
int __cdecl LuaMapFull(lua_State* L) {
    using namespace rfa::game;
    StepFullMap(true);
    const int column = static_cast<int>(std::floor(g_full.centreX / kZonePixels)) + kFirstZone;
    const int row = static_cast<int>(std::floor(g_full.centreY / kZonePixels)) + kFirstZone;
    auto district = g_districts.find(ZoneKey(column, row));
    lua_pushboolean(L, g_mapOpen);
    lua_pushnumber(L, g_full.centreX);
    lua_pushnumber(L, g_full.centreY);
    lua_pushnumber(L, ViewPixels());
    lua_pushnumber(L, ZoomPercent());
    lua_pushstring(L, district != g_districts.end() ? district->second.c_str() : "");
    return 6;
}

// rfg_map_full_tiles() -> count; rfg_map_full_tile(i) -> column, row
//   The zones under the world map's view whose own pictures are loaded (rfg_terr01_<column>_<row>_map),
//   when it is zoomed in far enough to want them: how many (at most kFullMapTiles), then each (1 to the
//   count; a C function has room for 20 values only, so not all at once).
std::vector<std::pair<int, int>> g_fullMapTiles;

int __cdecl LuaMapFullTiles(lua_State* L) {
    using namespace rfa::game;
    g_fullMapTiles.clear();
    for (const auto& zone : FullMapZones())
        if (ZoneReady(zone.first, zone.second)) g_fullMapTiles.push_back(zone);
    lua_pushnumber(L, static_cast<double>(g_fullMapTiles.size()));
    return 1;
}

int __cdecl LuaMapFullTile(lua_State* L) {
    using namespace rfa::game;
    const int index = static_cast<int>(lua_tonumber(L, 1)) - 1;
    if (index < 0 || index >= static_cast<int>(g_fullMapTiles.size())) return 0;
    lua_pushnumber(L, g_fullMapTiles[index].first);
    lua_pushnumber(L, g_fullMapTiles[index].second);
    return 2;
}

// ---- Guerrilla's data items
//
// Guerrilla's own HUD scripts (run as they are, from the player's copy: see GuerrillaHud.cs) read
// the data items Guerrilla's game filled (WeaponFire, ...), which Armageddon doesn't have. These
// functions let our scripts make such an item and fill it from Armageddon's own items, so that
// Guerrilla's script reads it as it read Guerrilla's: the engine's data items (the same as Armageddon's
// health_hud and the rest). An item is made once (0xC58910: its handle, name and the type of each
// field; Armageddon's own items are made at start-up by 0x57A240) and kept; a field is set with a
// value (a 16-byte variant, made by the game's constructor for its type) on the item the handle
// names (0xC581A0), and the engine tells the item's subscribers when one changes (0xC576E0 compares
// first), as Armageddon's HUD update (0x57DE80) sets health_hud.
using MakeItemFn = void*(__cdecl*)(DWORD* handle, const char* name, const int* types, int count, const void* defaults, int defaultsCount);
using FindItemFn = void*(__cdecl*)(DWORD* handle);
using SetFieldFn = void(__fastcall*)(void* item, void* unused, int index, const void* value);
using StringValueFn = void*(__fastcall*)(void* value, void* unused, const char* text);
using IntValueFn = void*(__fastcall*)(void* value, void* unused, int number);
using FloatValueFn = void*(__fastcall*)(void* value, void* unused, float number);
using ReleaseValueFn = void(__fastcall*)(void* value, void* unused);
auto const MakeItem = reinterpret_cast<MakeItemFn>(0xC58910);
auto const FindItem = reinterpret_cast<FindItemFn>(0xC581A0);
auto const SetField = reinterpret_cast<SetFieldFn>(0xC576E0);
auto const StringValue = reinterpret_cast<StringValueFn>(0x55C6E0);
auto const BoolValue = reinterpret_cast<IntValueFn>(0x55C740);
auto const FloatValue = reinterpret_cast<FloatValueFn>(0x55C710);
auto const IntValue = reinterpret_cast<IntValueFn>(0x5709D0);
auto const ReleaseValue = reinterpret_cast<ReleaseValueFn>(0xC54460);
constexpr int kMaxItemFields = 0x24;  // 0xC576E0 takes no field past this
constexpr size_t kValueSize = 16;

struct GuerrillaItem {
    std::string name;   // kept: the engine may keep the name's address
    std::string types;  // a letter a field: b(oolean), i(nteger), f(loat), s(tring)
    DWORD handle = 0;
};
std::map<std::string, GuerrillaItem> g_items;  // never taken away: the engine keeps its items

// The engine's code for a field's type, as Armageddon's own items give them (health_hud's: 4 a
// string, 5 a boolean, 3 a float, 1 an integer).
int FieldType(char letter) {
    switch (letter) {
    case 'b': return 5;
    case 'f': return 3;
    case 's': return 4;
    default: return 1;
    }
}

// rfg_dataitem_make(name, types): makes the data item `name` (once), its fields' types a letter each.
int __cdecl LuaDataItemMake(lua_State* L) {
    using namespace rfa::game;
    const char* name = lua_tolstring(L, 1, nullptr);
    const char* types = lua_tolstring(L, 2, nullptr);
    if (!name || !types) return 0;
    if (g_items.count(name)) return 0;
    GuerrillaItem& item = g_items[name];
    item.name = name;
    item.types = types;
    if (item.types.size() > kMaxItemFields) item.types.resize(kMaxItemFields);
    std::vector<int> codes;
    for (char letter : item.types) codes.push_back(FieldType(letter));
    // The engine holds 128 items (0x28D4B90, 0x26C bytes each; 0xC581A0 refuses a handle past them).
    if (!MakeItem(&item.handle, item.name.c_str(), codes.data(), static_cast<int>(codes.size()), nullptr, 0))
        rfa::Log("Could not make Guerrilla's data item %s: the engine's 128 data items may all be in use", name);
    else
        rfa::Log("Made Guerrilla's data item %s (%u fields): handle 0x%08lX", name, static_cast<unsigned>(codes.size()), item.handle);
    return 0;
}

// rfg_dataitem_set(name, values...): sets the item's fields, in order, each as its type.
int __cdecl LuaDataItemSet(lua_State* L) {
    using namespace rfa::game;
    const char* name = lua_tolstring(L, 1, nullptr);
    auto found = name ? g_items.find(name) : g_items.end();
    if (found == g_items.end()) return 0;
    GuerrillaItem& item = found->second;
    void* object = FindItem(&item.handle);
    if (!object) return 0;
    const int given = lua_gettop(L) - 1;
    for (int i = 0; i < static_cast<int>(item.types.size()) && i < given; ++i) {
        alignas(8) BYTE value[kValueSize] = {};
        const int at = i + 2;
        switch (item.types[i]) {
        case 'b': BoolValue(value, nullptr, lua_toboolean(L, at)); break;
        case 'f': FloatValue(value, nullptr, static_cast<float>(lua_tonumber(L, at))); break;
        case 's': {
            const char* text = lua_tolstring(L, at, nullptr);
            StringValue(value, nullptr, text ? text : "");
            break;
        }
        default: IntValue(value, nullptr, static_cast<int>(lua_tonumber(L, at))); break;
        }
        SetField(object, nullptr, i, value);  // the engine copies the value (a string's text too)
        ReleaseValue(value, nullptr);
    }
    return 0;
}

// rfg_string_find(text, part) -> where part starts and ends in text (from 1), or nil: Lua's
// string.find for a plain part, for Guerrilla's scripts (the game's Lua has no string library).
int __cdecl LuaStringFind(lua_State* L) {
    using namespace rfa::game;
    const char* text = lua_tolstring(L, 1, nullptr);
    const char* part = lua_tolstring(L, 2, nullptr);
    const char* at = text && part ? strstr(text, part) : nullptr;
    if (!at) {
        lua_pushnil(L);
        return 1;
    }
    lua_pushnumber(L, static_cast<double>(at - text + 1));
    lua_pushnumber(L, static_cast<double>(at - text + strlen(part)));
    return 2;
}

// The game's window: the process's visible top-level window with a client area.
BOOL CALLBACK FindGameWindow(HWND window, LPARAM found) {
    DWORD process = 0;
    GetWindowThreadProcessId(window, &process);
    if (process != GetCurrentProcessId() || !IsWindowVisible(window) || GetWindow(window, GW_OWNER)) return TRUE;
    *reinterpret_cast<HWND*>(found) = window;
    return FALSE;
}

// rfg_get_screen_dim() -> width, height: the game window's size in pixels, as Guerrilla's gave it.
int __cdecl LuaScreenDim(lua_State* L) {
    using namespace rfa::game;
    HWND window = nullptr;
    EnumWindows(FindGameWindow, reinterpret_cast<LPARAM>(&window));
    RECT client = {};
    if (window) GetClientRect(window, &client);
    lua_pushnumber(L, client.right - client.left);
    lua_pushnumber(L, client.bottom - client.top);
    return 2;
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_RegisterLua(lua_State* L) {
    rfa::game::lua_register(L, "rfg_map_view", LuaMapView);
    rfa::game::lua_register(L, "rfg_map_tiles", LuaMapTiles);
    rfa::game::lua_register(L, "rfg_map_close", LuaMapClose);
    rfa::game::lua_register(L, "rfg_map_icon_count", LuaMapIconCount);
    rfa::game::lua_register(L, "rfg_map_icon", LuaMapIcon);
    rfa::game::lua_register(L, "rfg_map_icon_shown", LuaMapIconShown);
    rfa::game::lua_register(L, "rfg_map_full", LuaMapFull);
    rfa::game::lua_register(L, "rfg_map_full_tiles", LuaMapFullTiles);
    rfa::game::lua_register(L, "rfg_map_full_tile", LuaMapFullTile);
    rfa::game::lua_register(L, "rfg_station_prompt", LuaStationPrompt);
    rfa::game::lua_register(L, "rfg_shop", LuaShop);
    rfa::game::lua_register(L, "rfg_shop_item", LuaShopItem);
    rfa::game::lua_register(L, "rfg_shop_text", LuaShopText);
    rfa::game::lua_register(L, "rfg_dataitem_make", LuaDataItemMake);
    rfa::game::lua_register(L, "rfg_dataitem_set", LuaDataItemSet);
    rfa::game::lua_register(L, "rfg_string_find", LuaStringFind);
    rfa::game::lua_register(L, "rfg_get_screen_dim", LuaScreenDim);
}

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    RaiseDocumentLimits();  // The builder adds the HUD's documents to the interface project whatever the settings
    g_worlds = SplitList(rfa::IniString(L"GuerrillaHud", L"Worlds", L"rfg"));
    for (const std::string& name : SplitList(rfa::IniString(L"GuerrillaHud", L"Documents", L"rfg_ui_hud_health")))
        g_documents.push_back({name});
    if (rfa::IniInt(L"GuerrillaHud", L"Reticles", 1) && !FindDocument("rfg_ui_hud_reticles"))
        g_documents.push_back({"rfg_ui_hud_reticles"});
    if (rfa::IniInt(L"GuerrillaHud", L"WeaponSelect", 1) && !FindDocument("rfg_ui_hud_weapon_sel"))
        g_documents.push_back({"rfg_ui_hud_weapon_sel"});
    if (rfa::IniInt(L"GuerrillaHud", L"Map", 1)) {
        if (!FindDocument("rfg_ui_hud")) g_documents.push_back({"rfg_ui_hud"});
        g_mapDocument = "rfg_ui_fullscreen";
        Document map{g_mapDocument};
        map.whileOpen = true;  // loaded with the HUD, so it opens at once; drawn only while open
        g_documents.push_back(map);
        // Guerrilla's prompts (its use prompt at the safehouses' stations) and its upgrade screen, loaded with
        // the HUD; the screen drawn only while a shop is open.
        if (!FindDocument("rfg_ui_messages")) g_documents.push_back({"rfg_ui_messages"});
        Document shop{"rfg_ui_upgrades"};
        shop.whileShop = true;
        g_documents.push_back(shop);
        for (const char* set : kZoneSets) {
            // Listed to be drawn like the others (they draw nothing: one empty group). Unlisted and
            // without a script, the game closed such documents as soon as they had loaded (2026-11-05).
            Document tiles{set};
            tiles.zoneSet = true;
            g_documents.push_back(tiles);
        }
        const std::wstring key = rfa::IniString(L"GuerrillaHud", L"MapKey", L"Tab");
        g_mapKey = key.empty() ? 0 : KeyCode(key);
        if (!key.empty() && !g_mapKey) rfa::Log("[GuerrillaHud] MapKey=%ls isn't a key this mod knows (a letter, a digit, Tab, F1-F12)", key.c_str());
        ReadDistricts();
        ReadMapIcons();
        ReadRadioTags();
        ReadSafehouses();
        EnlargeInterfacePool();
    }
    if (g_worlds.empty() || g_documents.empty()) {
        rfa::Log("No worlds or no documents under [GuerrillaHud]; nothing to do");
        return;
    }
    g_hudUpdate = reinterpret_cast<HudUpdateFn>(rfa::HookFunction(kHudUpdate, kHudUpdatePrologue, reinterpret_cast<void*>(&HudUpdate)));
    if (!g_hudUpdate) {
        rfa::Log("Could not hook the HUD's update; Guerrilla's HUD stays off");
        return;
    }
    g_drawPass = reinterpret_cast<DrawPassFn>(rfa::HookFunction(kDrawPass, kDrawPassPrologue, reinterpret_cast<void*>(&DrawPass)));
    if (!g_drawPass) rfa::Log("Could not hook the interface's draw pass; Guerrilla's HUD may stay up on loading screens and menus");
    g_loadStart = reinterpret_cast<LoadStartFn>(rfa::HookFunction(kLoadStart, kLoadStartPrologue, reinterpret_cast<void*>(&LoadStart)));
    if (!g_loadStart) rfa::Log("Could not hook the start of level loads; a reload may crash when the HUD unloads a map picture after it");
    std::string documents, worlds;
    for (const Document& doc : g_documents) documents += (documents.empty() ? "" : ", ") + doc.name;
    for (const std::string& world : g_worlds) worlds += (worlds.empty() ? "" : ", ") + world;
    rfa::Log("Guerrilla's HUD (%s) in the worlds %s%s", documents.c_str(), worlds.c_str(),
             g_mapDocument.empty() ? "" : "; the world map's key is [GuerrillaHud] MapKey=");
}
