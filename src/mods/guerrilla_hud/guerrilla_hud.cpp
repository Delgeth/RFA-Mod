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
// on loading screens or in the menus.
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
#include <cmath>
#include <cstdio>
#include <map>
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
constexpr DWORD kInterfacePoolOurs = 32 * 1024 * 1024;

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
constexpr int kMaxOpenDocuments = 40;
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
    int zone = -1;           // the zone whose picture it brings (column × 100 + row), loaded while near
    bool loaded = false;     // the mod asked for it
    bool seen = false;       // and the game has had it open since
    bool missing = false;    // not in the game's interface project, or the game couldn't load it
    bool listed = false;     // in the draw list (only while the HUD runs)
    int closedByGame = 0;    // times the game closed it after loading it (it does when its memory runs out)
};

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

// A zone's picture's document, as GuerrillaBuilder.exe names it: rfg_ui_map_<column>_<row>.
std::string ZoneDocument(int column, int row) {
    char name[32];
    sprintf_s(name, "rfg_ui_map_%02d_%02d", column, row);
    return name;
}
int ZoneKey(int column, int row) { return column * 100 + row; }

// Zone pictures are wanted within this distance of the player (the minimap shows Guerrilla's 80 m
// and half the vehicle's speed, and its octagon's corners reach 1.4 times that), and kept loaded
// for a while after.
constexpr double kTilesWithinMetres = 250.0;
constexpr ULONGLONG kTilesKeptMs = 20000;

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
std::map<int, ULONGLONG> g_zoneWanted;          // column × 100 + row -> until when its picture is wanted
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

// Whether a zone's picture is in (its document loaded, its texture too).
bool ZoneReady(int column, int row) {
    const int key = ZoneKey(column, row);
    for (Document& doc : g_documents) {
        if (doc.zone != key || !doc.loaded) continue;
        BYTE* open = OpenDocument(doc.name.c_str());
        return open && DocumentStatus(*reinterpret_cast<unsigned short*>(open + 0x40)) == kDocumentReady;
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
    g_view.mapX = (column - kFirstZone) * kZonePixels;
    g_view.mapY = (row - kFirstZone) * kZonePixels;
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

    // The zones whose pictures could show: the player's and its neighbours within reach.
    for (int dc = -1; dc <= 1; ++dc)
        for (int dr = -1; dr <= 1; ++dr) {
            const int c = g_view.column + dc, r = g_view.row + dr;
            if (c < kFirstZone || r < kFirstZone || c >= kFirstZone + kZonesAcross || r >= kFirstZone + kZonesAcross) continue;
            // Distance from the player to that zone's square, in zones.
            const double nearX = column < c ? c - column : column > c + 1 ? column - (c + 1) : 0.0;
            const double nearY = row < r ? r - row : row > r + 1 ? row - (r + 1) : 0.0;
            if (std::sqrt(nearX * nearX + nearY * nearY) * kZoneMetres > kTilesWithinMetres) continue;
            if (HasTilePicture(c, r)) g_zoneWanted[ZoneKey(c, r)] = now + kTilesKeptMs;
        }
}

bool HeldDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

// The world map's key, pressed in the game's window, the console and menus closed: opens or closes it.
// Not with Alt or the Windows key held: Alt+Tab and Win+Tab are Windows' own, and the game still has
// the focus when Tab goes down.
void FollowTheMapKey() {
    if (!g_mapKey) return;
    const bool down = HeldDown(g_mapKey);
    const bool windowsShortcut = HeldDown(VK_MENU) || HeldDown(VK_LWIN) || HeldDown(VK_RWIN);
    if (down && !g_mapKeyDown && !windowsShortcut) {
        DWORD process = 0;
        GetWindowThreadProcessId(GetForegroundWindow(), &process);
        if (process == GetCurrentProcessId() && !MenusShown()) {
            g_mapOpen = !g_mapOpen;
            rfa::Log(g_mapOpen ? "World map opened" : "World map closed");
        }
    }
    g_mapKeyDown = down;
}

// Whether a document should be loaded now (in our world).
bool Wanted(const Document& doc) {
    if (doc.zone >= 0) {
        auto until = g_zoneWanted.find(doc.zone);
        return until != g_zoneWanted.end() && GetTickCount64() < until->second;
    }
    return true;
}

// Whether a loaded document should be on the draw list now.
bool ShouldDraw(const Document& doc, bool hudRuns) { return doc.drawn && hudRuns && (!doc.whileOpen || g_mapOpen); }

void Unload(Document& doc, BYTE* open) {
    const unsigned short handle = *reinterpret_cast<unsigned short*>(open + 0x40);
    if (doc.listed) RemoveFromDrawList(g_drawList, nullptr, &handle);
    UnloadDocument(handle);
    RFA_LOG_FIRST(200, "Unloaded %s", doc.name.c_str());
    doc.loaded = doc.seen = doc.listed = false;
}

// In our worlds, the documents wanted are loaded (and loaded again if the game closes one); the
// others the mod loaded are unloaded, and all of them in any other world.
void FollowTheWorld() {
    const bool ours = InOurWorld();
    if (static_cast<int>(ours) != g_inOurWorld) {
        rfa::Log(ours ? "World %s: Guerrilla's HUD" : "World %s: Armageddon's own HUD", WorldName());
        g_inOurWorld = ours;
        if (!ours) g_mapOpen = false;
    }
    if (ours) {
        UpdateMapView();
        FollowTheMapKey();
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
        if (open || doc.missing || (doc.loaded && !doc.seen)) continue;
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
            rfa::Log("The game couldn't load %s", doc.name.c_str());
            doc.missing = true;
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
    const bool hudRuns = GetTickCount64() - g_lastHudUpdate <= kHudGoneAfterMs;
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

// ---- What the documents' scripts ask (Lua, on the game's thread)

using rfa::game::lua_State;

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

// rfg_map_close(): closes the world map (its script's own close).
int __cdecl LuaMapClose(lua_State*) {
    if (g_mapOpen) rfa::Log("World map closed");
    g_mapOpen = false;
    return 0;
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
        for (int column = kFirstZone; column < kFirstZone + kZonesAcross; ++column)
            for (int row = kFirstZone; row < kFirstZone + kZonesAcross; ++row) {
                // Listed to be drawn like the others (they draw nothing: one empty group). Unlisted
                // and without a script, the game closed them as soon as they had loaded (2026-11-05).
                Document tiles{ZoneDocument(column, row)};
                tiles.zone = ZoneKey(column, row);
                g_documents.push_back(tiles);
            }
        const std::wstring key = rfa::IniString(L"GuerrillaHud", L"MapKey", L"Tab");
        g_mapKey = key.empty() ? 0 : KeyCode(key);
        if (!key.empty() && !g_mapKey) rfa::Log("[GuerrillaHud] MapKey=%ls isn't a key this mod knows (a letter, a digit, Tab, F1-F12)", key.c_str());
        ReadDistricts();
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
    std::string documents, worlds;
    for (const Document& doc : g_documents) documents += (documents.empty() ? "" : ", ") + doc.name;
    for (const std::string& world : g_worlds) worlds += (worlds.empty() ? "" : ", ") + world;
    rfa::Log("Guerrilla's HUD (%s) in the worlds %s%s", documents.c_str(), worlds.c_str(),
             g_mapDocument.empty() ? "" : "; the world map's key is [GuerrillaHud] MapKey=");
}
