// What Red Faction: Guerrilla's player gear (its weapons, its upgrades, and later its backpacks) needs
// from Armageddon's engine that it doesn't do itself. Its upgrades, bought in an F7 window, are in
// upgrades.cpp. The weapons are made from the player's own copy
// of Guerrilla by GuerrillaBuilder.exe (never shipped): see RfaFormats\Guerrilla\GuerrillaWeapons.cs;
// the guerrilla_world mod hands the game their tables at start-up.
//
// Room for more inventory items: every weapon a player can carry has an inventory item of its own
// (items_inventory.xtbl; a weapon is given by its inventory item, and the game finds the weapon whose
// Inventory_Item it is). The game keeps them in one table of 85 (MAX_INV_ITEM_TYPES; 0x30 bytes an
// item, from 0x19F8578, how many at 0x19F8570), filled while it starts (the loader 0x7A7710: "Too many
// inventory item types. Get a programmer to increase MAX_INV_ITEM_TYPES." at 85). Armageddon's own are
// 75 and its Path to War add-on's 4 more while one of its levels is loaded; Guerrilla's campaign weapons
// are about 25. The table is a global the game's code reaches by its address, written into five
// instructions: the loader (its next entry, `add edx,19F8578h` at 0x7A782A), the lookup by name
// (0x7A7DA0: `mov dword ptr [ebp-8],19F8578h` at 0x7A7DB7), the lookup by name hash (0x7A82F0: the
// hash at +4, `add ecx,19F857Ch` at 0x7A8317, and the entry, `add eax,19F8578h` at 0x7A832D), and the
// start-up constructor (0xF2C330: `push 19F8578h` at 0xF2C33C, which builds each entry with 0x963420
// through the vector constructor 0x422890). Its size is in two: the loader's check (`cmp dword ptr
// [19F8570h],55h` at 0x7A77F0) and the constructor's `push 55h` at 0xF2C338. Both sizes are one-byte
// values in the instructions (signed: at most 127), so the table can grow to 127 there. Nothing else
// reaches the table (a scan of every 4-byte value in the exe found these five; the other matches are
// parts of call and jump instructions), and each item's number in it is a byte (+0x24).
//
// The mod moves the table to one of 127 of its own, all or nothing: each of those values is checked
// against the game's first. The game's start-up constructor runs after the mods load and builds the
// new table's entries; the mod builds them too in case it has already run.
//
// Room for the animations' triggers. While the game reads its animation sets (0x979C20, at start-up) it
// copies each anim_files.xtbl entry's lists into five static pools, a record each: effects (0x97B940,
// 16 bytes, 64 at 0x261C4D0), voice lines (0x97B820, 8, 320 at 0x261C8D0), sounds (0x97B640, 16, 960 at
// 0x261D2D0), props (0x97ADA0, 36, 256 at 0x2620ED0) and hand IKs (0x97B400, 8, 1,300 at 0x26232D0),
// each with a count of records in use (0x2632190, 0x263218C, 0x2632188, 0x2632184, 0x2632180); each
// animation keeps a pointer to its first record. No room is checked: Armageddon's own fill them nearly
// full, and with Guerrilla's people, vehicles and player animations four ran over, spoiling what
// follows (a cutscene's prop trigger with a sound's numbers in it stopped the campaign). Each pool's
// address is in exactly two instructions, both in its reader (everything after reads the animations'
// own pointers), so the mod moves the five to bigger tables of its own (16 times or more), all or
// nothing, each place checked against the game's value first, and logs how full each is.
//
// Guerrilla's player animations for its guns (GuerrillaBuilder.exe builds them with the guns: see
// RfaFormats\Guerrilla\GuerrillaPlayerAnimations.cs): a set of their own, rfg_player, built on Darius's
// (vs_player) with the variant "confident", which nothing of Armageddon's uses. A character's set is
// chosen when it is made; the game's variant functions (Guerrilla's, still in Armageddon: a drunk or
// wounded civilian plays its variant set) switch a character to the set built on its own that has a
// variant, and back. The mod needs three things for it:
//   - the switch: when the player takes up a gun the game sets his animation group, the gun's
//     (character_instance::set_animation_group, 0x969110, thiscall: the group, a blend, ...; from
//     human::equip, 0x84DD70, with the gun just taken up at human+0x1394 -> +8 -> +0x20, its weapon
//     entry, whose +0 is its name). The mod runs it with the player's set switched first
//     (vanim_set_base_animation_set_variant, 0x98C310, cdecl: the character's animation state, the
//     character instance's +4; the variant, -1 for none) to rfg_player for one of the guns
//     mods\guerrilla_gear\player_weapons.txt names, and back to Darius's own for any other;
//   - room in the (set, animation) map: every set's states and actions are one place each in three
//     tables and a bit set the game makes while it reads the sets (0x97D6B0, from the sets' loading
//     0x979C20), from the Permanent Mempool (0x26AEE78), 6,344 places (the value at 0x1107C58, read only
//     there). The sets' reader doesn't check it: more states and actions than that spoil the memory
//     after the tables. Armageddon's own and Guerrilla's vehicles' and people's sets come close to it,
//     and rfg_player adds about 650, so the mod raises the value to 16,384 just before the sets are
//     read (a value in the game's data, not its code), if the mempool has room for it, and says how
//     many are in use after;
//   - the animations: written into mods\guerrilla_gear\animations\ (rfgpl_<Guerrilla's name>.anim_pc,
//     Armageddon's packing), loaded once and kept, as Armageddon keeps its player's: right after the
//     sets' loading (the animation library was emptied just before it, 0xD89F00 from 0x967B90, and
//     nothing empties it after), each one overlaid on its place in the library by name (as the
//     guerrilla_people mod loads its people's).
//
// Guerrilla's dropped guns. Guerrilla's NPCs carry the player's own guns (its EDF the m16 and the
// enforcer), and a gun one drops (dying, ragdolling, the AI's drop action) can be picked up as any
// other. What it carries is Guerrilla's own rule (weapon::adjust_dropped_ammo, for a gun anyone but the
// player drops): the rounds it and its owner's reserve held × its Dropped_ammo_scale (0.4 for every EDF
// gun) × the difficulty's "player ammo pickup" (Easy 2.0, Medium 1.0, Hard 0.9, Insane 0.8); a
// magazine of 0.5 to 1.5 × the scale of a full one, at least 1 round; the rest in reserve. Picking it
// up: walking over it gives its rounds as ammo if the player has that gun, else the gun itself with
// them into a free slot; with none free, the action button swaps it for the gun in hand. Armageddon
// keeps the rules (its pickup, 0x7CAE20, the items' give_item touch; its swap, 0x8BEB00; its drop,
// 0x8A2DB0, which calls the same step, 0x731A10, for an NPC's gun only) but not the numbers: its step
// keeps a full magazine and moves the scale × the owner's reserve into the gun, its pickup gives the
// firing mode's Rounds_On_Pickup instead of the gun's rounds, and its "player ammo pickup" is 1.0 at
// every difficulty. So, for Guerrilla's guns (player_weapons.txt), the mod runs Guerrilla's step in
// place of Armageddon's (0x731A10 wrapped: thiscall, the weapon) and remembers the rounds; after the
// game's pickup (0x7CAE20 wrapped: cdecl, the human, the item, two flags) or its adding the swapped-in
// gun to the inventory (0x85B3E0 wrapped: cdecl, the human, the item, a slot, three flags) it sets the
// player's magazine and reserve to them, through the game's own ammo store (0x84E8F0; its reserve
// 0x70EF40, add 0x70F4F0, take 0x70F650). Guerrilla's difficulty values come from the player's
// Guerrilla (mods\guerrilla_gear\ammo_pickup.txt), the game's difficulty from 0x10F7E98.
//
// The player turns to where he walks. Armageddon keeps Darius facing the camera: its heading
// step (0x84C6D0, cdecl, the human; once a frame for each one on foot) sets the player's turn
// (human+0x664) toward the camera unless he sprints (player+0x1830, 0x20), and its movement step
// (0x89D0C0) turns a character toward where it goes only if its character entry lacks "always face
// target" (entry+0x28A, 0x04; Armageddon added it, its player has it, Guerrilla's Mason didn't). Its
// sprint is the one time Darius turns all the way round. The player keeps Armageddon's movement and
// does the same while he walks or runs, Darius (vs_player) in every world and Mason (rfg_mason, the
// Guerrilla world's player): the mod wraps the heading step, and for the player playing one of those
// sets, on foot, moving (this frame's movement input, 0x11BE7C4 / 0x11BE7C0), not sprinting, not
// aiming (0x8BB2F0) and not attacking (the attack buttons, 0x4E4800, and 1.5 s after), it lifts the
// flag from his entry (the entries are one table, 0x21B7538, kept for the whole game) and lets no
// camera turn through, as the sprint does; aiming, attacking or standing still he faces the camera
// as before.
//
// Room for more projectiles (the user, 2026-11-06: "Yes, enlarge it"). Every projectile type
// (projectiles.xtbl: Armageddon's 88, its add-on's 3, Guerrilla's thrown and launched weapons' and its
// turrets') takes a place in one pool of 100 (0x2174F90, 0xC0 bytes a type), which its start-up
// constructor (0x82FE60, run with the game's own start-up code, after the mods start) builds in a
// buffer inside the pool object; a type past it is dropped without a word. Their names go into a
// 2 KB pool ("Proj String Pool") and their stages into a 96 KB one ("Projectile Mempool"), both blocks
// the projectile loader (0x82E9F0) takes from the Permanent Mempool once. The constructor's count is a
// one-byte value (127 at most) and its buffer a fixed part of the object, so the mod runs the
// constructor itself (its same calls: the pool's base, the pool made in a memory pool laid over a
// buffer, the memory pool dropped) with a buffer of its own for 256, and changes the loader's four
// sizes (each a 4-byte value) to 8 KB for the names and 256 KB for the stages: each checked against
// the game's first, none changed if one differs, those already changed put back if one can't be.
// After the loader it logs how many places, name bytes and stage bytes are in use.
//
// Room for more melee moves (the user, 2026-11-06: "Yes, enlarge it"). Every melee move (melee.xtbl:
// Armageddon's 22, Guerrilla's under rfg_ names) takes a place in a pool of 32 (0x21AFEF0, 0xE0 bytes a
// move) its start-up constructor (0x881BC0) builds as the projectiles' is built, in a buffer inside the
// object with a one-byte count; past it the reader stops. The mod runs that constructor too, with a
// buffer of its own for 64 (its three values checked first), and logs the use after melee.xtbl is read
// (0x876900 wrapped). The moves' names go into a string pool of their own, "Melee Combat Strings"
// (0x21B1BD8), of 512 bytes: the melee set-up (0x8777E0) takes 512 bytes from the Permanent Mempool
// (`push 200h` at 0x8777F2) and makes the pool over them (`push 200h` at 0x87783C); Armageddon's 22 names
// nearly fill it, and with Guerrilla's the game stopped ("Length of string pool exceeded", string_pool.cpp,
// the user's 19:57 run). Both values are made 4 KB, checked first, all or nothing, before the set-up runs
// (it runs once, at start-up); the use is logged after the reader and shown in F7 Limits. The vehicle
// reader's names pool is the same kind and as small for Guerrilla's 153 vehicles (the user, 2026-11-06:
// "Yes, raise both"): "Vehicle info strings" (0x14A15F8: the vehicles' names, models, display names,
// components, lights and props, about 9.3 KB of 8 KB; `push 2000h` at 0x6271DF and 0x627227); it is made
// 32 KB the same way. (Its neighbour "Vehicle interaction info strings", whose overflow stopped the user's
// 20:02 run, is raised by the guerrilla_vehicles mod.)
//
// Room for the preloaded items (the user, 2026-11-06: "Yes, raise it"). At start-up (0xA45600) the game
// allocates a block for its "item_preload" pool (0xA44A20: _aligned_malloc from the process heap, size
// pushed at 0xA47520) and makes the pool of it (0x512B30 on 0x26AF1A0, the same size pushed at
// 0xA4754E): 17 MB. Every "General (preloaded)" item's own container goes into it, then Armageddon's
// items_containers.gpeg_pc (its items' low-detail textures, 8.8 MB); with Guerrilla's weapons' and
// pickups' containers the peg no longer fit (the user's 18:44 and 19:38 runs). The mod makes both values
// 32 MB, before the game's start-up code runs: each checked against the game's first, neither changed if
// one differs, the first put back if the second can't be written. The pool's use shows in F7 Limits.
// The Permanent Mempool (0x26AEE78, what the game keeps for good from start-up) is made the same way just
// before it, 12 MB (`push 0C00000h` at 0xA47276 and 0xA472A4); Armageddon's own and the mods' raised tables
// no longer fit (the user's 21:19 run: "Failed aligned alloc ... in Permanent mempool cpu pool"), so it is
// made 24 MB the same way. Both pools' use is logged at the player's first step.
//
// Mason's sledgehammer. Guerrilla's Mason always has his hammer: it starts in his kit (slot 3) and
// that slot can't be swapped (player_can_swap_inv_item). With Guerrilla's hammer built (the weapons
// build's rfg_sledgehammer, sledge_upgrade_1, sledge_upgrade_2), the player playing Mason's set
// (rfg_mason) who has none of them is given the plain one, into slot 3 if it's free (the game's own
// add-by-inventory-item, 0x85B260, which takes the slot; give_player_item asks it for -1, the first
// free), checked every two seconds from the heading step above. And the game's swap (0x8BEB00 wrapped:
// cdecl, the human, the item on the ground, the slot or -1 for the gun in his hands) is refused for the
// player when the gun it would drop is one of Guerrilla's hammers. With Stonebreaker or Shattermaster
// bought (upgrades.cpp), that one is the hammer he is given: one he carries that is lesser is dropped
// (the game's own drop of an inventory record, 0x8A2DB0: cdecl, the human, the record; from his hands
// or his slots) and the better given into its slot.

#define RFA_MOD_NAME "guerrilla_gear"
#include "rfa_mod.h"
#include "rfa_game.h"
#include "rfa_menu.h"
#include "guerrilla_gear.h"
#include <malloc.h>
#include <algorithm>
#include <cstring>
#include <fstream>
#include <map>
#include <memory>
#include <random>
#include <string>
#include <vector>

namespace {

constexpr DWORD kInventoryItems = 0x19F8578;
constexpr DWORD kInventoryItemSize = 0x30;
constexpr DWORD kInventoryItemCount = 0x19F8570;
constexpr DWORD kGameMaxInventoryItems = 85;
constexpr DWORD kMaxInventoryItems = 127;  // the most a one-byte, signed size in the game's code can hold
// The 4-byte values in the game's code that hold the table's address (or its first item's +4).
constexpr DWORD kInventoryItemReferences[] = {0x7A782C, 0x7A7DBA, 0x7A8319, 0x7A832E, 0xF2C33D};
// The one-byte values that hold its size: the loader's check and the start-up constructor's push.
constexpr DWORD kInventoryItemLimits[] = {0x7A77F6, 0xF2C339};
alignas(16) BYTE g_inventoryItems[kMaxInventoryItems * kInventoryItemSize];
auto const BuildInventoryItem = reinterpret_cast<void*(__fastcall*)(void* item, void* unused)>(0x963420);

void MoreInventoryItems() {
    const int count = *reinterpret_cast<const int*>(kInventoryItemCount);
    if (count != 0) {
        rfa::Log("Inventory items: the game has already read %d; it keeps its %lu", count, kGameMaxInventoryItems);
        return;
    }
    for (DWORD at : kInventoryItemReferences) {
        const DWORD value = rfa::CodeValue(at);
        if (value < kInventoryItems || value >= kInventoryItems + kInventoryItemSize) {
            rfa::Log("Inventory items: the game's code at 0x%08lX isn't as expected (0x%08lX); the game keeps its %lu", at, value, kGameMaxInventoryItems);
            return;
        }
    }
    for (DWORD at : kInventoryItemLimits) {
        const BYTE size = *reinterpret_cast<const BYTE*>(at);
        if (size != kGameMaxInventoryItems) {
            rfa::Log("Inventory items: the game's size at 0x%08lX isn't %lu (it's %u); the game keeps its own", at, kGameMaxInventoryItems, size);
            return;
        }
    }
    for (DWORD i = 0; i < kMaxInventoryItems; ++i) BuildInventoryItem(g_inventoryItems + i * kInventoryItemSize, nullptr);
    const DWORD moved = reinterpret_cast<DWORD>(g_inventoryItems);
    for (DWORD at : kInventoryItemReferences) {
        const DWORD value = moved + (rfa::CodeValue(at) - kInventoryItems);
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &value, sizeof(value))) {
            rfa::Log("Inventory items: could not change the game's code at 0x%08lX (part of it already points at the new table)", at);
            return;
        }
    }
    const BYTE size = static_cast<BYTE>(kMaxInventoryItems);
    for (DWORD at : kInventoryItemLimits)
        if (!rfa::PatchCode(reinterpret_cast<void*>(at), &size, sizeof(size))) {
            rfa::Log("Inventory items: could not change the game's size at 0x%08lX", at);
            return;
        }
    rfa::Log("Inventory items: the game holds %lu (its own %lu): the table moved to 0x%08lX, %u places in its code changed",
             kMaxInventoryItems, kGameMaxInventoryItems, moved,
             static_cast<unsigned>(std::size(kInventoryItemReferences) + std::size(kInventoryItemLimits)));
}

// ---- Room for the animations' triggers (see the top)

// One of the pools the sets' reader copies each animation's anim_files.xtbl lists into: the game's
// own (a static array), its record size and room, the count of records in use (the reader adds each
// animation's to it), the two places in the reader's code that hold the pool's address (an instruction's
// 4-byte value; each pool's address is in its reader only), and our room.
struct TriggerPool {
    const char* name;
    DWORD gameBase;
    DWORD recordSize;
    DWORD gameRoom;
    DWORD count;
    DWORD references[2];
    DWORD room;
    BYTE* ours;
};
alignas(16) BYTE g_effectPool[1024 * 16];
alignas(16) BYTE g_voicePool[4096 * 8];
alignas(16) BYTE g_soundPool[16384 * 16];
alignas(16) BYTE g_propPool[4096 * 36];
alignas(16) BYTE g_ikPool[16384 * 8];
TriggerPool g_triggerPools[] = {
    // Effects (0x97B940): add ecx,261C4D0h at 0x97B99A, add edx,261C4D0h at 0x97BABC
    {"Effects", 0x261C4D0, 16, 64, 0x2632190, {0x97B99C, 0x97BABE}, 1024, g_effectPool},
    // Voice_Lines (0x97B820): lea eax,[edx*8+261C8D0h] at 0x97B881, lea edx,[ecx*8+261C8D0h] at 0x97B908
    {"Voice_Lines", 0x261C8D0, 8, 320, 0x263218C, {0x97B884, 0x97B90B}, 4096, g_voicePool},
    // Sounds (0x97B640): add edx,261D2D0h at 0x97B6A4, add eax,261D2D0h at 0x97B7E6
    {"Sounds", 0x261D2D0, 16, 960, 0x2632188, {0x97B6A6, 0x97B7E7}, 16384, g_soundPool},
    // Props (0x97ADA0): add ecx,2620ED0h at 0x97ADFC and at 0x97B3C2
    {"Props", 0x2620ED0, 36, 256, 0x2632184, {0x97ADFE, 0x97B3C4}, 4096, g_propPool},
    // IKs (0x97B400): lea eax,[edx*8+26232D0h] at 0x97B46D and at 0x97B60B
    {"IKs", 0x26232D0, 8, 1300, 0x2632180, {0x97B470, 0x97B60E}, 16384, g_ikPool},
};
bool g_triggerPoolsMoved;

void MoreTriggerRoom() {
    // All or nothing: every place checked first, and nothing read yet.
    for (const TriggerPool& pool : g_triggerPools) {
        if (*reinterpret_cast<const DWORD*>(pool.count) != 0) {
            rfa::Log("Animation triggers: the game has already read its %s; it keeps its own pools", pool.name);
            return;
        }
        for (DWORD at : pool.references)
            if (rfa::CodeValue(at) != pool.gameBase) {
                rfa::Log("Animation triggers: the game's code at 0x%08lX isn't as expected (0x%08lX, not 0x%08lX); it keeps its own pools",
                         at, rfa::CodeValue(at), pool.gameBase);
                return;
            }
    }
    unsigned changed = 0;
    for (const TriggerPool& pool : g_triggerPools)
        for (DWORD at : pool.references) {
            const DWORD moved = reinterpret_cast<DWORD>(pool.ours);
            if (!rfa::PatchCode(reinterpret_cast<void*>(at), &moved, sizeof(moved))) {
                // Put back the ones already changed, so no pool is split between two places.
                for (const TriggerPool& back : g_triggerPools)
                    for (DWORD undo : back.references)
                        if (rfa::CodeValue(undo) == reinterpret_cast<DWORD>(back.ours))
                            rfa::PatchCode(reinterpret_cast<void*>(undo), &back.gameBase, sizeof(back.gameBase));
                rfa::Log("Animation triggers: could not change the game's code at 0x%08lX; it keeps its own pools", at);
                return;
            }
            ++changed;
        }
    g_triggerPoolsMoved = true;
    rfa::Log("Animation triggers: the pools moved to the mod's own (effects %lu, voice lines %lu, sounds %lu, props %lu, IKs %lu; "
             "the game's 64, 320, 960, 256, 1300), %u places in its code changed",
             g_triggerPools[0].room, g_triggerPools[1].room, g_triggerPools[2].room, g_triggerPools[3].room, g_triggerPools[4].room, changed);
}

// After the sets are read: how full each pool is.
void LogTriggerPools() {
    for (const TriggerPool& pool : g_triggerPools) {
        const DWORD used = *reinterpret_cast<const DWORD*>(pool.count);
        const DWORD room = g_triggerPoolsMoved ? pool.room : pool.gameRoom;
        rfa::Log("Animation triggers: %s %lu of %lu%s", pool.name, used, room,
                 used > room ? ": MORE than the pool holds, the game's memory after it is spoilt (expect a crash)" : "");
    }
}

// ---- Guerrilla's player animations: the files

std::wstring GearFolder() { return rfa::RootDir() + L"\\mods\\guerrilla_gear\\"; }

// The guns that take rfg_player up (their weapons.xtbl names, one a line).
std::vector<std::string> g_playerWeapons;

void ReadPlayerWeapons() {
    std::ifstream list(GearFolder() + L"player_weapons.txt");
    for (std::string line; std::getline(list, line);) {
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        if (!line.empty() && line.size() < 64) g_playerWeapons.push_back(line);
    }
}

// ---- Guerrilla's player animations: the switch

constexpr DWORD kSetAnimationGroup = 0x969110;
constexpr size_t kSetAnimationGroupPrologue = 6;  // push ebp / mov ebp,esp / sub esp,14h
// thiscall (the character instance) with five values on the stack, which it takes off (ret 14h): the
// group, an 8-byte index alias, a blend time and a flag.
using SetAnimationGroupFn = void(__fastcall*)(BYTE* character, void* unused, int group, DWORD aliasLow, DWORD aliasHigh, float blend, int flag);
SetAnimationGroupFn g_setAnimationGroup;

auto const LocalPlayer = reinterpret_cast<BYTE*(__cdecl*)()>(0x8B92D0);
auto const SetBaseVariant = reinterpret_cast<void(__cdecl*)(void* animation, int variant)>(0x98C310);
constexpr DWORD kCharacterInstance = 0x760;  // human -> its character instance
constexpr DWORD kAnimationState = 0x4;       // character instance -> its animation state (v_animate)
constexpr DWORD kHeldItem = 0x1394;          // human -> the item in its hand (-> +8 its info -> +0x20 its weapon entry)
constexpr int kConfident = 10;               // the variant rfg_player has (Armageddon's anim_set_variants: 10 confident)
constexpr int kNoVariant = -1;

// The weapon entry of the item in a human's hand, or null.
const BYTE* HeldWeapon(const BYTE* human) {
    const BYTE* item = *reinterpret_cast<const BYTE* const*>(human + kHeldItem);
    if (!item) return nullptr;
    const BYTE* info = *reinterpret_cast<const BYTE* const*>(item + 8);
    if (!info) return nullptr;
    return *reinterpret_cast<const BYTE* const*>(info + 0x20);
}

const char* WeaponName(const BYTE* weapon) { return weapon ? *reinterpret_cast<const char* const*>(weapon) : nullptr; }

bool IsGuerrillaGun(const BYTE* weapon) {
    const char* name = WeaponName(weapon);
    if (!name) return false;
    for (const std::string& ours : g_playerWeapons)
        if (_stricmp(name, ours.c_str()) == 0) return true;
    return false;
}

int g_playerVariant = kNoVariant;  // the last one set, for the log

// rfg_player builds on Darius's set (its clips are for his skeleton), so it is taken up only by a
// player whose own set is Darius's: the animation state's +0x10 is the character's own set, a set's
// +0 its name (0x97C3BB). A player of another skeleton (Guerrilla's Mason) keeps his own set.
constexpr DWORD kBaseSet = 0x10;
const char* const kDariusSet = "vs_player";

bool PlaysDariusSet(const BYTE* animation) {
    const BYTE* set = *reinterpret_cast<const BYTE* const*>(animation + kBaseSet);
    const char* name = set ? *reinterpret_cast<const char* const*>(set) : nullptr;
    return name && _stricmp(name, kDariusSet) == 0;
}

void __fastcall SetAnimationGroup(BYTE* character, void* unused, int group, DWORD aliasLow, DWORD aliasHigh, float blend, int flag) {
    BYTE* player = LocalPlayer();
    if (character && player && *reinterpret_cast<BYTE**>(player + kCharacterInstance) == character) {
        BYTE* animation = *reinterpret_cast<BYTE**>(character + kAnimationState);
        const BYTE* weapon = HeldWeapon(player);
        const int variant = IsGuerrillaGun(weapon) ? kConfident : kNoVariant;
        if (animation && PlaysDariusSet(animation)) {
            SetBaseVariant(animation, variant);
            if (variant != g_playerVariant) {
                g_playerVariant = variant;
                const char* name = WeaponName(weapon);
                rfa::Log("Player animations: %s (%s)", variant == kConfident ? "Guerrilla's" : "Darius's own", name ? name : "no gun");
            }
        }
    }
    g_setAnimationGroup(character, unused, group, aliasLow, aliasHigh, blend, flag);
}

// ---- Guerrilla's player animations: room in the (set, animation) map, and the animations

constexpr DWORD kLoadAnimationSets = 0x979C20;
constexpr size_t kLoadAnimationSetsPrologue = 8;  // push ebp / mov ebp,esp / mov eax,11E4h
using LoadAnimationSetsFn = int(__cdecl*)(bool addOn, int which);
LoadAnimationSetsFn g_loadAnimationSets;

constexpr DWORD kMapPlaces = 0x1107C58;      // how many places the map gets (the game's data)
constexpr DWORD kGameMapPlaces = 6344, kOurMapPlaces = 16384;
constexpr DWORD kMapBytesPerPlace = 12;      // three tables of 4-byte values (and a bit each in the bit set)
constexpr DWORD kPermanentMempool = 0x26AEE78;  // 12 MB, 24 MB with MoreStartupRoom (set up at 0xA472A4)
constexpr DWORD kMempoolSize = 0x44, kMempoolUsed = 0x4C;
constexpr DWORD kMempoolMargin = 1024 * 1024;  // left for what the game takes from it after (the sets' groups take ~350 KB next)
// The groups' records the lookup reads (44 of them, 12 bytes each: +4 each name's count of sets, a
// byte each; +8 each name's first place, a 2-byte value each), and how many state and action names.
constexpr DWORD kGroupRecords = 0x261C270, kGroupRecordSize = 12;
constexpr int kGroups = 44;
constexpr DWORD kStateNames = 0x25CD744, kActionNames = 0x25CD748;

DWORD MempoolRoom() {
    const DWORD size = *reinterpret_cast<const DWORD*>(kPermanentMempool + kMempoolSize);
    const DWORD used = *reinterpret_cast<const DWORD*>(kPermanentMempool + kMempoolUsed);
    return size > used ? size - used : 0;
}

void MoreMapRoom() {
    DWORD& places = *reinterpret_cast<DWORD*>(kMapPlaces);
    if (places != kGameMapPlaces) {
        rfa::Log("Animation map: the game gives it %lu places, not its %lu; left as it is", places, kGameMapPlaces);
        return;
    }
    const DWORD more = (kOurMapPlaces - kGameMapPlaces) * kMapBytesPerPlace + (kOurMapPlaces - kGameMapPlaces) / 8 + 64;
    const DWORD room = MempoolRoom();
    if (room < more + kMempoolMargin) {
        rfa::Log("Animation map: the Permanent Mempool has %lu bytes left, too few for %lu more places; it keeps the game's %lu",
                 room, kOurMapPlaces - kGameMapPlaces, kGameMapPlaces);
        return;
    }
    places = kOurMapPlaces;
    rfa::Log("Animation map: %lu places (the game's %lu): %lu bytes more of the Permanent Mempool's %lu left", kOurMapPlaces, kGameMapPlaces, more, room);
}

// How many places the sets read take: the furthest any group's names reach.
DWORD MapUse() {
    const int names = *reinterpret_cast<const int*>(kStateNames) + *reinterpret_cast<const int*>(kActionNames);
    DWORD use = 0;
    for (int g = 0; g < kGroups; ++g) {
        const BYTE* record = reinterpret_cast<const BYTE*>(kGroupRecords + g * kGroupRecordSize);
        const BYTE* counts = *reinterpret_cast<const BYTE* const*>(record + 4);
        const WORD* firsts = *reinterpret_cast<const WORD* const*>(record + 8);
        if (!counts || !firsts) continue;
        for (int i = 0; i < names; ++i)
            if (counts[i] && static_cast<DWORD>(firsts[i]) + counts[i] > use) use = static_cast<DWORD>(firsts[i]) + counts[i];
    }
    return use;
}

// The animation library: an animation's place by its name ("x.anim"; -1 if none), a new place for a
// name, and the data overlaid on the place a name has (keep: an unload passes over it).
auto const FindAnimation = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD8C2C0);
auto const NewAnimation = reinterpret_cast<int(__cdecl*)(const char* name)>(0xD875F0);
auto const OverlayAnimation = reinterpret_cast<int(__cdecl*)(const char* name, void* data, unsigned size, bool keep)>(0xD8C0F0);
std::vector<std::unique_ptr<std::string>> g_animationNames;  // the library keeps the pointers

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

void LoadPlayerAnimations() {
    const std::wstring folder = GearFolder() + L"animations\\";
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
        // (A place that has data already, or is kept already, is passed over.)
        if (OverlayAnimation(name, data, size, true) < 0) { _aligned_free(data); ++noRoom; continue; }
        ++loaded;
        bytes += size;
    } while (FindNextFileW(search, &found));
    FindClose(search);
    rfa::Log("Player animations: %u of Guerrilla's loaded and kept (%.1f MB)", loaded, bytes / 1048576.0);
    if (noRoom) rfa::Log("Player animations: %u had no place in the animation library (it is full: is guerrilla_vehicles on?)", noRoom);
    if (failed) rfa::Log("Player animations: %u could not be read", failed);
}

int __cdecl LoadAnimationSets(bool addOn, int which) {
    if (!addOn) MoreMapRoom();
    const int result = g_loadAnimationSets(addOn, which);
    if (!addOn) {
        const DWORD use = MapUse(), places = *reinterpret_cast<const DWORD*>(kMapPlaces);
        rfa::Log("Animation map: %lu of its %lu places in use", use, places);
        if (use > places) rfa::Log("Animation map: MORE in use than it has places: the game's memory after it is spoilt (expect a crash)");
        LogTriggerPools();
        LoadPlayerAnimations();
    }
    return result;
}

void PlayerAnimations() {
    ReadPlayerWeapons();
    g_loadAnimationSets = reinterpret_cast<LoadAnimationSetsFn>(
        rfa::HookFunction(kLoadAnimationSets, kLoadAnimationSetsPrologue, reinterpret_cast<void*>(&LoadAnimationSets)));
    if (!g_loadAnimationSets)
        rfa::Log("Animation map: could not wrap the sets' loading; the map keeps its 6,344 places and Guerrilla's player animations aren't loaded");
    if (g_playerWeapons.empty()) {
        rfa::Log("Player animations: no guns take Guerrilla's up (no mods\\guerrilla_gear\\player_weapons.txt); Darius plays his own");
        return;
    }
    g_setAnimationGroup = reinterpret_cast<SetAnimationGroupFn>(
        rfa::HookFunction(kSetAnimationGroup, kSetAnimationGroupPrologue, reinterpret_cast<void*>(&SetAnimationGroup)));
    if (!g_setAnimationGroup) rfa::Log("Player animations: could not wrap the animation group's setting; Darius plays his own");
    else rfa::Log("Player animations: Guerrilla's for %u guns", static_cast<unsigned>(g_playerWeapons.size()));
}

// ---- Guerrilla's dropped guns (see the top)

constexpr DWORD kAdjustDroppedAmmo = 0x731A10;
constexpr size_t kAdjustDroppedAmmoPrologue = 6;  // push ebp / mov ebp,esp / sub esp,18h
using AdjustDroppedAmmoFn = void(__fastcall*)(BYTE* weapon, void* unused);  // thiscall, no arguments
AdjustDroppedAmmoFn g_adjustDroppedAmmo;

constexpr DWORD kGiveItem = 0x7CAE20;
constexpr size_t kGiveItemPrologue = 9;  // push ebp / mov ebp,esp / sub esp,0A8h
using GiveItemFn = bool(__cdecl*)(BYTE* human, BYTE* item, bool* ask, bool* other);
GiveItemFn g_giveItem;

constexpr DWORD kAddItem = 0x85B3E0;
constexpr size_t kAddItemPrologue = 9;  // push ebp / mov ebp,esp / sub esp,98h
using AddItemFn = BYTE*(__cdecl*)(BYTE* human, BYTE* item, int slot, bool first, bool second, bool third);
AddItemFn g_addItem;

constexpr DWORD kWeaponInfo = 0x188;          // a weapon -> its weapons.xtbl entry (whose +0 is its name)
constexpr DWORD kAdjustedOnlyIf = 0x18C;      // Armageddon's adjustment runs only while this is -1
constexpr DWORD kFiringModeSlot = 0x1C8;      // the weapon's first firing mode slot
constexpr DWORD kRoundsInMagazine = 0x2FC;    // and its rounds in the magazine
constexpr DWORD kMagazineSize = 0x13E;        // a firing mode's Magazine_Size (2 bytes)
constexpr DWORD kDroppedAmmoScale = 0x148;    // and its Dropped_Ammo_Scale
constexpr DWORD kInventoryInfo = 0x18;        // a weapon entry -> its inventory item; an inventory item -> its weapon's handle
constexpr DWORD kDifficulty = 0x10F7E98;      // the game's difficulty: 0 easy, 1 medium, 2 hard, 3 insane
auto const FiringModeInfo = reinterpret_cast<BYTE*(__fastcall*)(BYTE* slot, void* unused)>(0x42A1C0);
auto const OwnersAmmo = reinterpret_cast<void*(__fastcall*)(BYTE* weapon, void* unused)>(0x741310);
auto const HumansAmmo = reinterpret_cast<void*(__fastcall*)(BYTE* human, void* unused)>(0x84E8F0);
auto const ReserveRounds = reinterpret_cast<int(__fastcall*)(void* ammo, void* unused, BYTE* weapon, int slot)>(0x70EF40);
auto const AddRounds = reinterpret_cast<int(__fastcall*)(void* ammo, void* unused, BYTE* weapon, int slot, int rounds)>(0x70F4F0);
auto const TakeRounds = reinterpret_cast<int(__fastcall*)(void* ammo, void* unused, BYTE* weapon, int slot, int rounds)>(0x70F650);
auto const HasItem = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human, const void* inventoryInfo)>(0x85CD70);
auto const ObjectFromHandle = reinterpret_cast<BYTE*(__fastcall*)(void* world, void* unused, DWORD handle)>(0x436250);
void* const kWorld = reinterpret_cast<void*>(0x1870630);

// Guerrilla's "player ammo pickup" at each difficulty (mods\guerrilla_gear\ammo_pickup.txt, from the
// player's own Guerrilla; 1.0 where it says nothing).
float g_ammoPickup[4] = {1.0f, 1.0f, 1.0f, 1.0f};

void ReadAmmoPickup() {
    std::ifstream list(GearFolder() + L"ammo_pickup.txt");
    const char* const levels[] = {"Easy", "Medium", "Hard", "Insane"};
    std::string level;
    float value = 0;
    while (list >> level >> value)
        for (int i = 0; i < 4; ++i)
            if (_stricmp(level.c_str(), levels[i]) == 0 && value > 0) g_ammoPickup[i] = value;
}

float AmmoPickup() {
    const int level = *reinterpret_cast<const int*>(kDifficulty);
    return level >= 0 && level < 4 ? g_ammoPickup[level] : 1.0f;
}

float RandomRange(float low, float high) {
    static std::mt19937 random(std::random_device{}());
    return std::uniform_real_distribution<float>(low, high)(random);
}

// A gun an NPC dropped: what it carries, as Guerrilla gives it (the item is the weapon itself).
struct DroppedGun {
    const BYTE* info;  // its weapon entry, to tell the same object reused for another gun
    int magazine, reserve;
};
std::map<const BYTE*, DroppedGun> g_droppedGuns;

const DroppedGun* Dropped(const BYTE* item) {
    auto found = g_droppedGuns.find(item);
    if (found == g_droppedGuns.end()) return nullptr;
    if (*reinterpret_cast<const BYTE* const*>(item + kWeaponInfo) != found->second.info) {
        g_droppedGuns.erase(found);
        return nullptr;
    }
    return &found->second;
}

const char* NameOf(const BYTE* info) { return info ? *reinterpret_cast<const char* const*>(info) : "?"; }

// Guerrilla's weapon::adjust_dropped_ammo for one of its guns, in Armageddon's terms: the total the
// gun and its owner's reserve held × its Dropped_Ammo_Scale × the difficulty's player ammo pickup; a
// magazine of 0.5 to 1.5 × the scale of a full one (at least 1 round, at most a full magazine); the
// rest in reserve (as much as the ammo type holds, when it's given). Armageddon's own (any other gun)
// keeps the magazine and moves the scale × the owner's reserve into the gun.
void __fastcall AdjustDroppedAmmo(BYTE* weapon, void* unused) {
    const BYTE* info = weapon ? *reinterpret_cast<const BYTE* const*>(weapon + kWeaponInfo) : nullptr;
    BYTE* mode = info && IsGuerrillaGun(info) && *reinterpret_cast<const int*>(weapon + kAdjustedOnlyIf) == -1
                     ? FiringModeInfo(weapon + kFiringModeSlot, nullptr) : nullptr;
    if (!mode) {
        g_adjustDroppedAmmo(weapon, unused);
        return;
    }
    const int magazineSize = *reinterpret_cast<const WORD*>(mode + kMagazineSize);
    const float scale = *reinterpret_cast<const float*>(mode + kDroppedAmmoScale);
    int& magazine = *reinterpret_cast<int*>(weapon + kRoundsInMagazine);
    void* ammo = OwnersAmmo(weapon, nullptr);
    const int reserve = ammo ? ReserveRounds(ammo, nullptr, weapon, 0) : 0;
    const int total = static_cast<int>(scale * static_cast<float>(magazine + reserve) * AmmoPickup());
    int dropped = static_cast<int>(static_cast<float>(magazineSize) * (RandomRange(0.5f, 1.5f) * scale));
    dropped = (std::max)(1, (std::min)(dropped, magazineSize));
    const int rest = (std::max)(0, total - dropped);
    rfa::Log("Dropped guns: %s dropped by an NPC with %d | %d (it had %d | %d; Guerrilla's: x %.2f, difficulty x %.2f)",
             NameOf(info), dropped, rest, magazine, reserve, scale, AmmoPickup());
    magazine = dropped;
    g_droppedGuns[weapon] = {info, dropped, rest};
}

// The player's reserve of a gun's ammo type set to one value, through the game's own ammo store
// (adding is held to what the type holds).
void SetReserve(void* ammo, BYTE* weapon, int now, int wanted) {
    if (now > wanted) TakeRounds(ammo, nullptr, weapon, 0, now - wanted);
    else if (now < wanted) AddRounds(ammo, nullptr, weapon, 0, wanted - now);
}

// Walking over a gun (items_3d Touch_Script give_item). Armageddon's: a gun the player has gives him
// its Rounds_On_Pickup as ammo; one he hasn't, with a free slot (0-3), is added as a new gun of his
// with those rounds; with none, the prompt to swap. Guerrilla's (item_touch_give_item) gives a dropped
// gun's own rounds: as ammo if he has the gun, else the gun itself with them. So for one of Guerrilla's
// guns an NPC dropped, the game's pickup runs and then the rounds are made Guerrilla's.
bool __cdecl GiveItem(BYTE* human, BYTE* item, bool* ask, bool* other) {
    BYTE* player = LocalPlayer();
    const DroppedGun* found = human && human == player && item ? Dropped(item) : nullptr;
    if (!found) return g_giveItem(human, item, ask, other);
    const DroppedGun drop = *found;
    const void* inventoryInfo = *reinterpret_cast<const void* const*>(drop.info + kInventoryInfo);
    void* ammo = HumansAmmo(player, nullptr);
    const bool had = HasItem(player, inventoryInfo) != nullptr;
    const int before = ammo ? ReserveRounds(ammo, nullptr, item, 0) : 0;
    const bool result = g_giveItem(human, item, ask, other);
    BYTE* held = HasItem(player, inventoryInfo);
    if (!ammo || !held) return result;  // not taken (no free slot: the swap, below)
    const int after = ReserveRounds(ammo, nullptr, item, 0);
    if (had && after == before) return result;  // his reserve is full: nothing given, the gun stays
    if (had) {
        SetReserve(ammo, item, after, before);
        AddRounds(ammo, nullptr, item, 0, drop.magazine + drop.reserve);
    } else {
        BYTE* gun = ObjectFromHandle(kWorld, nullptr, *reinterpret_cast<const DWORD*>(held + kInventoryInfo));
        if (gun) *reinterpret_cast<int*>(gun + kRoundsInMagazine) = drop.magazine;
        SetReserve(ammo, item, after, before);
        AddRounds(ammo, nullptr, item, 0, drop.reserve);
    }
    rfa::Log("Dropped guns: %s picked up %s: %d | %d (reserve %d before, %d now)", NameOf(drop.info),
             had ? "as ammo" : "into a free slot", drop.magazine, drop.reserve, before, ReserveRounds(ammo, nullptr, item, 0));
    g_droppedGuns.erase(item);
    return result;
}

// Swapping for a gun on the ground (the action button, 0x8BEB00: the gun in hand dropped, the one on
// the ground added, itself): it comes with Guerrilla's rounds.
BYTE* __cdecl AddItem(BYTE* human, BYTE* item, int slot, bool first, bool second, bool third) {
    BYTE* player = LocalPlayer();
    const DroppedGun* found = human && human == player && item ? Dropped(item) : nullptr;
    if (!found) return g_addItem(human, item, slot, first, second, third);
    const DroppedGun drop = *found;
    void* ammo = HumansAmmo(player, nullptr);
    const int before = ammo ? ReserveRounds(ammo, nullptr, item, 0) : 0;
    BYTE* added = g_addItem(human, item, slot, first, second, third);
    if (!added || !ammo) return added;
    SetReserve(ammo, item, ReserveRounds(ammo, nullptr, item, 0), before);
    *reinterpret_cast<int*>(item + kRoundsInMagazine) = drop.magazine;
    AddRounds(ammo, nullptr, item, 0, drop.reserve);
    rfa::Log("Dropped guns: %s swapped in: %d | %d (reserve %d before, %d now)", NameOf(drop.info), drop.magazine, drop.reserve,
             before, ReserveRounds(ammo, nullptr, item, 0));
    g_droppedGuns.erase(item);
    return added;
}

void DroppedGuns() {
    ReadAmmoPickup();
    if (g_playerWeapons.empty()) return;  // no Guerrilla guns built
    g_adjustDroppedAmmo = reinterpret_cast<AdjustDroppedAmmoFn>(
        rfa::HookFunction(kAdjustDroppedAmmo, kAdjustDroppedAmmoPrologue, reinterpret_cast<void*>(&AdjustDroppedAmmo)));
    g_giveItem = reinterpret_cast<GiveItemFn>(rfa::HookFunction(kGiveItem, kGiveItemPrologue, reinterpret_cast<void*>(&GiveItem)));
    g_addItem = reinterpret_cast<AddItemFn>(rfa::HookFunction(kAddItem, kAddItemPrologue, reinterpret_cast<void*>(&AddItem)));
    if (!g_adjustDroppedAmmo || !g_giveItem || !g_addItem)
        rfa::Log("Dropped guns: could not wrap the game's %s; Guerrilla's guns an NPC drops carry Armageddon's rounds",
                 !g_adjustDroppedAmmo ? "dropped-ammo step" : !g_giveItem ? "pickup" : "inventory");
    else
        rfa::Log("Dropped guns: Guerrilla's rounds for its guns an NPC drops (player ammo pickup %.2f / %.2f / %.2f / %.2f)",
                 g_ammoPickup[0], g_ammoPickup[1], g_ammoPickup[2], g_ammoPickup[3]);
}

// ---- The player turns to where he walks (see the top)

constexpr DWORD kHeadingStep = 0x84C6D0;
constexpr size_t kHeadingStepPrologue = 9;  // push ebp / mov ebp,esp / sub esp,0A4h
using HeadingStepFn = void(__cdecl*)(BYTE* human);
HeadingStepFn g_headingStep;

constexpr DWORD kCharacterEntry = 0x4FC;     // human -> its character entry (character.xtbl)
constexpr DWORD kEntryFlags = 0x28A;         // character entry: a flags byte; 0x04 "always face target"
constexpr BYTE kAlwaysFaceTarget = 0x04;
constexpr DWORD kOnFootState = 0x720;        // human: 0 on foot, as the heading step's own branch
constexpr DWORD kHeadingOffset = 0x664;      // human: the turn the heading step asks for
constexpr DWORD kPlayerStateFlags = 0x1830;  // player: 0x20 sprinting
constexpr DWORD kSprinting = 0x20;
const char* const kMasonSet = "rfg_mason";  // (Darius's, vs_player, is kDariusSet above)

auto const IsFineAiming = reinterpret_cast<bool(__cdecl*)(const BYTE* player)>(0x8BB2F0);  // player +0x17E4 == 2
auto const ControlHeld = reinterpret_cast<bool(__cdecl*)(int control, int controller, int unused)>(0x4E4800);
const int* const kLocalController = reinterpret_cast<const int*>(0x11C0580);
const float* const kMoveInputX = reinterpret_cast<const float*>(0x11BE7C4);  // this frame's movement input
const float* const kMoveInputY = reinterpret_cast<const float*>(0x11BE7C0);
constexpr int kPrimaryAttack = 0x11, kAltAttack = 0x12, kMeleeAttack = 0x13;
constexpr ULONGLONG kFightingMs = 1500;  // after the last attack he keeps facing the camera this long

BYTE* g_liftedEntry;        // the character entry whose "always face target" the mod has lifted, if any
ULONGLONG g_lastAttack;
const char* g_walkLogged;   // the set last logged walking free

bool PlaysSet(const BYTE* human, const char* setName) {
    const BYTE* character = *reinterpret_cast<const BYTE* const*>(human + kCharacterInstance);
    const BYTE* animation = character ? *reinterpret_cast<const BYTE* const*>(character + kAnimationState) : nullptr;
    const BYTE* set = animation ? *reinterpret_cast<const BYTE* const*>(animation + kBaseSet) : nullptr;
    const char* name = set ? *reinterpret_cast<const char* const*>(set) : nullptr;
    return name && _stricmp(name, setName) == 0;
}

// Whether the player walks free: Darius or Mason, on foot, moving, not sprinting (Armageddon's own
// sprint already does this), not aiming, and not attacking (nor in the moment after).
bool WalksFree(BYTE* player) {
    if (!PlaysSet(player, kDariusSet) && !PlaysSet(player, kMasonSet)) return false;
    if (*reinterpret_cast<const int*>(player + kOnFootState) != 0) return false;
    if (*reinterpret_cast<const DWORD*>(player + kPlayerStateFlags) & kSprinting) return false;
    const int controller = *kLocalController;
    const ULONGLONG now = GetTickCount64();
    if (ControlHeld(kPrimaryAttack, controller, 0) || ControlHeld(kAltAttack, controller, 0) || ControlHeld(kMeleeAttack, controller, 0))
        g_lastAttack = now;
    if (IsFineAiming(player) || now - g_lastAttack < kFightingMs) return false;
    return (*kMoveInputX) * (*kMoveInputX) + (*kMoveInputY) * (*kMoveInputY) > 0.0001f;
}

// The camera's look-at bone. A character entry keeps its skeleton's "camera" and "camera-lookat" bones
// (+0x1E0, +0x1E4; 0x886840 looks both up by name, 0xD8A120, -1 for none). Darius's skeleton has both:
// camera-lookat is a bone of its own at his feet that his dodges, rolls and other moves carry along,
// and their "camera lookat as rotate" triggers turn the camera toward it. Mason's (Guerrilla's) has only
// "camera", on his root at his hips, so his entry holds -1, and the camera (0x4CFAD8, 0x4CFC82) asks
// the skeleton where bone -1 is (0xD8AE50) and goes there: under the ground. The mod gives such an
// entry its "camera" bone instead, so the camera turns toward his hips through the move.
constexpr DWORD kCameraBone = 0x1E0, kCameraLookatBone = 0x1E4;
bool g_lookatLogged;

void GiveCameraLookat(BYTE* entry) {
    int& lookat = *reinterpret_cast<int*>(entry + kCameraLookatBone);
    const int camera = *reinterpret_cast<const int*>(entry + kCameraBone);
    if (lookat >= 0 || camera < 0) return;
    lookat = camera;
    if (!g_lookatLogged) {
        g_lookatLogged = true;
        rfa::Log("Camera: the player's skeleton has no camera-lookat bone; his camera bone (%d) stands in for it", camera);
    }
}

void KeepHammer(BYTE* player);  // (Mason's sledgehammer, below)
void LogItemPreload();          // (room for the preloaded items, below)
void LogNameRoom();             // (room in the tables' name pools, below)
bool g_itemPreloadLogged = false;

void __cdecl HeadingStep(BYTE* human) {
    BYTE* player = LocalPlayer();
    if (!human || human != player) {
        g_headingStep(human);
        return;
    }
    if (!g_itemPreloadLogged) {  // the first step in a level: every preloaded item is in by now
        g_itemPreloadLogged = true;
        LogItemPreload();
        LogNameRoom();
    }
    upgrades::Step(player);
    backpacks::Step(player);
    KeepHammer(player);
    const bool free = WalksFree(player);
    BYTE* entry = *reinterpret_cast<BYTE**>(player + kCharacterEntry);
    if (entry) GiveCameraLookat(entry);
    // Without the flag the movement step (0x89D0C0) turns him toward where he goes, as it turns
    // Guerrilla's people (and as Guerrilla turned Mason); with it, he keeps his heading. The flag goes
    // back the moment he stops walking free (or another character is the player).
    if (g_liftedEntry && (!free || entry != g_liftedEntry)) {
        g_liftedEntry[kEntryFlags] |= kAlwaysFaceTarget;
        g_liftedEntry = nullptr;
    }
    if (free && entry && (entry[kEntryFlags] & kAlwaysFaceTarget)) {
        entry[kEntryFlags] &= static_cast<BYTE>(~kAlwaysFaceTarget);
        g_liftedEntry = entry;
    }
    g_headingStep(player);
    // The heading step turns the player to the camera unless he sprints; walking free, he doesn't either.
    if (free) {
        *reinterpret_cast<float*>(player + kHeadingOffset) = 0.0f;
        const char* who = PlaysSet(player, kMasonSet) ? "Mason" : "Darius";
        if (!g_walkLogged || std::strcmp(g_walkLogged, who) != 0) {
            g_walkLogged = who;
            rfa::Log("Movement: %s turns to where he walks and runs (aiming or attacking, to the camera)", who);
        }
    }
}

void PlayerMovement() {
    g_headingStep = reinterpret_cast<HeadingStepFn>(
        rfa::HookFunction(kHeadingStep, kHeadingStepPrologue, reinterpret_cast<void*>(&HeadingStep)));
    if (!g_headingStep) rfa::Log("Movement: could not wrap the heading step; the player faces the camera as Armageddon has it");
    else rfa::Log("Movement: Darius and Mason turn to face where they walk and run, through all 360 degrees; aiming and attacking face the camera");
}

// ---- Mason's sledgehammer (see the top)

constexpr DWORD kWeaponTable = 0x18F8048;      // the weapon entries (0xE0 bytes each, +0 the name)
constexpr DWORD kWeaponTableCount = 0x18F804C;
constexpr DWORD kWeaponEntrySize = 0xE0;
constexpr int kHammerSlot = 3;                 // Guerrilla's: the hammer's slot
constexpr int kRadialSlots = 4;
constexpr ULONGLONG kHammerCheckMs = 2000;

constexpr DWORD kSwapWeapon = 0x8BEB00;
constexpr size_t kSwapWeaponPrologue = 6;      // push ebp / mov ebp,esp / sub esp,24h
using SwapWeaponFn = void(__cdecl*)(BYTE* human, BYTE* item, int slot);
SwapWeaponFn g_swapWeapon;

// The game's add-by-inventory-item (cdecl: the human, the inventory item, rounds (0: its own), the slot
// (-1: the first free, as give_player_item asks), three flags), the record in a slot (cdecl: the human,
// the slot) and the one in his hands (cdecl: the human). A record's +0x18 is its weapon's handle.
auto const GiveByInventoryItem = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human, const void* inventoryItem, int rounds, int slot, bool, bool, bool)>(0x85B260);
auto const RecordInSlot = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human, int slot)>(0x7A8290);
auto const RecordInHands = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human)>(0x8BE980);

// Guerrilla's hammers, as the weapons build names them: the sledgehammer and its upgrades (Stonebreaker,
// Shattermaster), the first the one Mason is given.
const char* const kHammers[] = {"rfg_sledgehammer", "sledge_upgrade_1", "sledge_upgrade_2"};

ULONGLONG g_nextHammerCheck;
ULONGLONG g_lastHammerNote;

const BYTE* WeaponEntryNamed(const char* name) {
    BYTE* entries = *reinterpret_cast<BYTE* const*>(kWeaponTable);
    const int count = *reinterpret_cast<const int*>(kWeaponTableCount);
    for (int i = 0; entries && i < count; ++i) {
        const char* own = *reinterpret_cast<const char* const*>(entries + i * kWeaponEntrySize);
        if (own && _stricmp(own, name) == 0) return entries + i * kWeaponEntrySize;
    }
    return nullptr;
}

bool IsHammer(const BYTE* info) {
    const char* name = info ? *reinterpret_cast<const char* const*>(info) : nullptr;
    if (!name) return false;
    for (const char* hammer : kHammers)
        if (_stricmp(name, hammer) == 0) return true;
    return false;
}

// The weapon entry of an inventory record, or null.
const BYTE* RecordWeapon(const BYTE* record) {
    if (!record) return nullptr;
    const BYTE* weapon = ObjectFromHandle(kWorld, nullptr, *reinterpret_cast<const DWORD*>(record + kInventoryInfo));
    return weapon ? *reinterpret_cast<const BYTE* const*>(weapon + kWeaponInfo) : nullptr;
}

auto const DropRecord = reinterpret_cast<void(__cdecl*)(BYTE* human, BYTE* record)>(0x8A2DB0);

// Mason always carries his hammer (Guerrilla's start kit, character.xtbl rfg_PC: the sledgehammer in
// slot 3): every two seconds on foot, the player playing Mason's set who has none of the three is given
// the best he has bought (Guerrilla's upgrades: Stonebreaker, Shattermaster; else the plain one), into
// slot 3 if it's free, else the first free one. One he carries that is lesser than that is dropped first.
void KeepHammer(BYTE* player) {
    const ULONGLONG now = GetTickCount64();
    if (now < g_nextHammerCheck) return;
    g_nextHammerCheck = now + kHammerCheckMs;
    if (!PlaysSet(player, kMasonSet)) return;
    // The hammers in order (plain, Stonebreaker, Shattermaster), and the one he should have: the best bought.
    const std::string bought = upgrades::BestHammer();
    constexpr int kCount = static_cast<int>(sizeof(kHammers) / sizeof(kHammers[0]));
    const void* items[kCount] = {};
    int best = -1;
    for (int i = 0; i < kCount; ++i) {
        const BYTE* info = WeaponEntryNamed(kHammers[i]);
        items[i] = info ? *reinterpret_cast<const void* const*>(info + kInventoryInfo) : nullptr;
        if (items[i] && (best < 0 || _stricmp(kHammers[i], bought.c_str()) == 0)) best = i;
    }
    if (best < 0) return;  // Guerrilla's hammer isn't built
    const void* give = items[best];
    BYTE* lesser = nullptr;
    for (int i = 0; i < kCount; ++i) {
        BYTE* record = items[i] ? HasItem(player, items[i]) : nullptr;
        if (!record) continue;
        if (i >= best) return;  // he has that one, or a better
        lesser = record;
    }
    if (lesser) {
        DropRecord(player, lesser);
        rfa::Log("Hammer: dropped the lesser hammer for %s (bought)", bought.c_str());
    }
    int slot = RecordInSlot(player, kHammerSlot) ? -1 : kHammerSlot;
    for (int s = 0; slot < 0 && s < kRadialSlots; ++s)
        if (!RecordInSlot(player, s)) slot = s;
    if (slot < 0) {
        if (now - g_lastHammerNote > 60000) {
            g_lastHammerNote = now;
            rfa::Log("Hammer: Mason has no sledgehammer and no free slot for it (four guns); it waits for a free slot");
        }
        return;
    }
    const BYTE* given = GiveByInventoryItem(player, give, 0, slot, false, false, false);
    rfa::Log("Hammer: Mason %s; given %s %s, slot %d", lesser ? "had a lesser hammer" : "had no sledgehammer", given ? "him" : "nothing (the game refused)",
             kHammers[best], slot);
}

}  // namespace

// ---- What the other parts borrow (guerrilla_gear.h)

std::wstring gear::Folder() { return GearFolder(); }

const BYTE* gear::WeaponEntryNamed(const char* name) { return ::WeaponEntryNamed(name); }

bool gear::PlaysMason(const BYTE* human) { return human && PlaysSet(human, kMasonSet); }

bool gear::GiveWeapon(BYTE* player, const char* name, std::string& why) {
    const BYTE* info = name && *name ? ::WeaponEntryNamed(name) : nullptr;
    const void* inventoryItem = info ? *reinterpret_cast<const void* const*>(info + kInventoryInfo) : nullptr;
    if (!player || !inventoryItem) {
        why = "not built";
        return false;
    }
    if (HasItem(player, inventoryItem)) {
        why = "you carry it already";
        return false;
    }
    // A free slot; Mason's hammer slot last, as it is his hammer's.
    const bool mason = PlaysSet(player, kMasonSet);
    int slot = -1;
    for (int s = 0; slot < 0 && s < kRadialSlots; ++s)
        if (!(mason && s == kHammerSlot) && !RecordInSlot(player, s)) slot = s;
    if (slot < 0 && !mason) {
        why = "no free slot";
        return false;
    }
    if (slot < 0 && !RecordInSlot(player, kHammerSlot)) slot = kHammerSlot;
    if (slot < 0) {
        why = "no free slot (drop a gun)";
        return false;
    }
    if (!GiveByInventoryItem(player, inventoryItem, 0, slot, false, false, false)) {
        why = "the game refused it";
        return false;
    }
    rfa::Log("Upgrades: gave %s, slot %d", name, slot);
    return true;
}

// ---- The player's climb, for the jetpack (backpacks.cpp)
//
// Armageddon moves a human through its character controller (human+0x98, there while human+0xAC is 1;
// Guerrilla's character_controller): the game's own get and set of its velocity (0x850950 / 0x850990,
// cdecl: the human, a vector; Guerrilla's human_controller_get_vel / _set_vel, which its jetpack uses),
// and whether it stands on the ground (0x850830: the controller's +0x38 bit 2). The heading step runs
// before the controller's step each frame, so a velocity set here is the one the step starts from. One
// catch: for the player, the step puts a climb a little above the last one it worked out (controller
// +0x60) back to it, unless he jumps (0x79E77E); Guerrilla skips that while its jetpack is on, Armageddon
// has no such case, so Lift also writes the new climb there (the step writes its own after).

struct Vector3 {
    float x, y, z;
};
auto const ControllerVelocity = reinterpret_cast<void(__cdecl*)(BYTE* human, Vector3* out)>(0x850950);
auto const SetControllerVelocity = reinterpret_cast<void(__cdecl*)(BYTE* human, const Vector3* velocity)>(0x850990);
auto const OnGround = reinterpret_cast<bool(__cdecl*)(BYTE* human)>(0x850830);
constexpr DWORD kController = 0x98, kHasController = 0xAC, kLastClimb = 0x60;

bool gear::Airborne(BYTE* player) {
    return player && *reinterpret_cast<const int*>(player + kOnFootState) == 0 && player[kHasController] == 1 && !OnGround(player);
}

float gear::Climb(BYTE* player) {
    Vector3 velocity{};
    ControllerVelocity(player, &velocity);
    return velocity.y;
}

void gear::Lift(BYTE* player, float climb) {
    if (!player || player[kHasController] != 1) return;
    Vector3 velocity{};
    ControllerVelocity(player, &velocity);
    velocity.y += climb;
    SetControllerVelocity(player, &velocity);
    if (BYTE* controller = *reinterpret_cast<BYTE**>(player + kController))
        *reinterpret_cast<float*>(controller + kLastClimb) = velocity.y;
}

namespace {

// Swapping a gun on the ground for one of his (the action button, 0x8BEB00: the gun in the slot asked
// for, or in his hands, dropped): Guerrilla never lets the hammer go (player_can_swap_inv_item, RM
// 0xA111B0, refuses slot 3), so for the player a swap that would drop one of Guerrilla's hammers doesn't
// happen; with a gun in his hands it swaps that.
void __cdecl SwapWeapon(BYTE* human, BYTE* item, int slot) {
    if (human && human == LocalPlayer()) {
        BYTE* out = slot == -1 ? RecordInHands(human) : RecordInSlot(human, slot);
        if (IsHammer(RecordWeapon(out))) {
            const ULONGLONG now = GetTickCount64();
            if (now - g_lastHammerNote > 5000) {
                g_lastHammerNote = now;
                rfa::Log("Hammer: not swapped away (Guerrilla never drops it); swap with a gun in his hands");
            }
            return;
        }
    }
    g_swapWeapon(human, item, slot);
}

// ---- Room for more projectiles (see the top)

constexpr DWORD kProjectilePoolConstructor = 0x82FE60;  // the pool's start-up constructor (thiscall: the pool)
constexpr size_t kProjectilePoolConstructorPrologue = 6;  // push ebp / mov ebp,esp / sub esp,5Ch
constexpr DWORD kProjectileLoader = 0x82E9F0;            // the projectile tables' set-up and first read (cdecl, none)
constexpr size_t kProjectileLoaderPrologue = 10;          // push ebp / mov ebp,esp / cmp dword ptr [2174E70h],0
constexpr DWORD kProjectilePool = 0x2174F90;             // the projectile types (a word at +0xA how many it holds; a byte each at +0x18, 0xFF free)
constexpr DWORD kPoolHolds = 0xA, kPoolFlags = 0x18;
constexpr BYTE kPoolFree = 0xFF;
constexpr DWORD kNamePool = 0x2174ED8, kStagePool = 0x2174F38;  // the "Proj String Pool" and "Projectile Mempool" (size +0x44, used +0x4C)
constexpr DWORD kGameProjectiles = 100, kProjectiles = 256;
constexpr DWORD kProjectileTypeSize = 0xC0;              // and a link (4 bytes) and a flag (1 byte) each
constexpr DWORD kGamePoolMemory = 0x4CF9;                // the constructor's buffer, inside the pool object (+0x30)
constexpr DWORD kProjectilePoolMemory = kProjectiles * (kProjectileTypeSize + 4 + 1) + 64;  // with room for its alignment

// The constructor's own values, checked before anything changes: its buffer's size (push 4CF9h), its
// count (push 64h, a one-byte value: 127 at most) and the buffer's place in the object (add eax,30h).
constexpr DWORD kPoolMemoryValue = 0x82FE7B, kPoolCountValue = 0x82FE8F, kPoolBufferValue = 0x82FE84;

// The four sizes in the loader (each `push imm32`): the names' block and its pool, the stages' block and
// its pool; the blocks come from the Permanent Mempool once, while the game starts.
struct ProjectileRoom { const char* what; DWORD at; DWORD game; DWORD ours; };
const ProjectileRoom kProjectileRooms[] = {
    {"names' block", 0x82EA03, 0x800, 0x2000},
    {"names' pool", 0x82EA4D, 0x800, 0x2000},
    {"stages' block", 0x82EA80, 0x18000, 0x40000},
    {"stages' pool", 0x82EACB, 0x18000, 0x40000},
};

alignas(16) BYTE g_projectilePoolMemory[kProjectilePoolMemory];

auto const PoolBase = reinterpret_cast<void(__fastcall*)(BYTE* pool, void* unused)>(0x71C5D0);
auto const MempoolOver = reinterpret_cast<void(__fastcall*)(void* mempool, void* unused, void* memory, DWORD size, const void* name,
                                                             DWORD first, DWORD second)>(0x428240);
using PoolInitFn = void(__fastcall*)(BYTE* pool, void* unused, void* mempool, DWORD holds);
auto const ProjectilePoolInit = reinterpret_cast<PoolInitFn>(0x82FEB0);
auto const MempoolDone = reinterpret_cast<void(__fastcall*)(void* mempool, void* unused)>(0x427F40);
const void* const kObjectPoolName = reinterpret_cast<const void*>(0xF7101C);  // "object pool", the name the game gives it

using ProjectileLoaderFn = void(__cdecl*)();
ProjectileLoaderFn g_projectileLoader;

// What the game's start-up constructors of its fixed pools do (the projectile types' 0x82FE60, the
// melee moves' 0x881BC0): the pool's base, a memory pool laid over a buffer, the pool's set-up in it
// for so many places, the memory pool dropped. The mod's pass a buffer of their own and more places.
BYTE* BuildPool(BYTE* pool, BYTE* memory, DWORD size, PoolInitFn init, DWORD holds) {
    PoolBase(pool, nullptr);
    alignas(16) BYTE mempool[0x60];  // the game's takes 0x58 bytes of its stack
    MempoolOver(mempool, nullptr, memory, size, kObjectPoolName, 1, 0);
    init(pool, nullptr, mempool, holds);
    MempoolDone(mempool, nullptr);
    return pool;
}

// How many places such a pool holds (a word at +0xA) and how many are taken (a byte each, 0xFF while free).
int PoolHolds(DWORD pool) { return *reinterpret_cast<const WORD*>(pool + kPoolHolds); }

int PoolInUse(DWORD pool) {
    const BYTE* flags = *reinterpret_cast<BYTE* const*>(pool + kPoolFlags);
    int used = 0;
    for (int i = 0, holds = PoolHolds(pool); flags && i < holds; ++i)
        if (flags[i] != kPoolFree) ++used;
    return used;
}

// The projectile pool's constructor, with the mod's buffer and 256 places.
BYTE* __fastcall ProjectilePoolConstructor(BYTE* pool, void* unused) {
    return BuildPool(pool, g_projectilePoolMemory, sizeof(g_projectilePoolMemory), ProjectilePoolInit, kProjectiles);
}

int ProjectilesHeld() { return PoolHolds(kProjectilePool); }
int ProjectilesInUse() { return PoolInUse(kProjectilePool); }

void LogPoolUse(const char* what, DWORD pool) {
    rfa::Log("Projectiles: %s %lu of %lu bytes in use", what, *reinterpret_cast<const DWORD*>(pool + kMempoolUsed),
             *reinterpret_cast<const DWORD*>(pool + kMempoolSize));
}

// After the game has set up its projectile tables and read projectiles.xtbl (Armageddon's with
// Guerrilla's, from the guerrilla_world mod): how full they are. Add-ons' come later.
void __cdecl ProjectileLoader() {
    g_projectileLoader();
    rfa::Log("Projectiles: %d of %d places in use", ProjectilesInUse(), ProjectilesHeld());
    LogPoolUse("names:", kNamePool);
    LogPoolUse("stages:", kStagePool);
}

void MoreProjectiles() {
    if (ProjectilesHeld() != 0) {
        rfa::Log("Projectiles: the game has already built its table; it keeps its %lu", kGameProjectiles);
        return;
    }
    if (rfa::CodeValue(kPoolMemoryValue) != kGamePoolMemory || *reinterpret_cast<const BYTE*>(kPoolCountValue) != kGameProjectiles ||
        *reinterpret_cast<const BYTE*>(kPoolBufferValue) != 0x30) {
        rfa::Log("Projectiles: the game's constructor (0x%08lX) isn't as expected; the game keeps its %lu", kProjectilePoolConstructor, kGameProjectiles);
        return;
    }
    for (const ProjectileRoom& room : kProjectileRooms)
        if (rfa::CodeValue(room.at) != room.game) {
            rfa::Log("Projectiles: the game's %s size at 0x%08lX isn't 0x%lX (it's 0x%lX); the game keeps its %lu", room.what, room.at,
                     room.game, rfa::CodeValue(room.at), kGameProjectiles);
            return;
        }
    // All or nothing: the four sizes, then the constructor; any one failing puts back what changed.
    size_t changed = 0;
    for (; changed < std::size(kProjectileRooms); ++changed)
        if (!rfa::PatchCode(reinterpret_cast<void*>(kProjectileRooms[changed].at), &kProjectileRooms[changed].ours, sizeof(DWORD))) break;
    const bool hooked = changed == std::size(kProjectileRooms) &&
        rfa::HookFunction(kProjectilePoolConstructor, kProjectilePoolConstructorPrologue, reinterpret_cast<void*>(&ProjectilePoolConstructor)) != nullptr;
    if (!hooked) {
        for (size_t i = 0; i < changed; ++i) rfa::PatchCode(reinterpret_cast<void*>(kProjectileRooms[i].at), &kProjectileRooms[i].game, sizeof(DWORD));
        rfa::Log("Projectiles: could not change the game's code; it keeps its %lu", kGameProjectiles);
        return;
    }
    g_projectileLoader = reinterpret_cast<ProjectileLoaderFn>(
        rfa::HookFunction(kProjectileLoader, kProjectileLoaderPrologue, reinterpret_cast<void*>(&ProjectileLoader)));
    rfa::Log("Projectiles: the game holds %lu (its own %lu), their names %lu bytes (0x%lX), their stages %lu bytes (0x%lX)", kProjectiles,
             kGameProjectiles, kProjectileRooms[1].ours, kProjectileRooms[1].game, kProjectileRooms[3].ours, kProjectileRooms[3].game);
}

// ---- Room for more melee moves (see the top)

constexpr DWORD kMeleeConstructor = 0x881BC0;    // the melee moves' pool's start-up constructor (thiscall: the pool)
constexpr size_t kMeleeConstructorPrologue = 6;  // push ebp / mov ebp,esp / sub esp,5Ch
constexpr DWORD kMeleeLoader = 0x876900;         // melee.xtbl's reader (cdecl: two values)
constexpr size_t kMeleeLoaderPrologue = 6;       // push ebp / mov ebp,esp / sub esp,68h
constexpr DWORD kMeleePool = 0x21AFEF0;
constexpr DWORD kGameMeleeMoves = 32, kMeleeMoves = 64;
constexpr DWORD kMeleeMoveSize = 0xE0;           // and a link (4 bytes) and a flag (1 byte) each
constexpr DWORD kGameMeleeMemory = 0x1CA5;       // the constructor's buffer, inside the pool object (+0x30)
constexpr DWORD kMeleePoolMemory = kMeleeMoves * (kMeleeMoveSize + 4 + 1) + 64;
// The constructor's own values, checked first: its buffer's size (push 1CA5h), its count (push 20h, one
// byte) and the buffer's place in the object (add eax,30h).
constexpr DWORD kMeleeMemoryValue = 0x881BDB, kMeleeCountValue = 0x881BEF, kMeleeBufferValue = 0x881BE4;

alignas(16) BYTE g_meleePoolMemory[kMeleePoolMemory];
auto const MeleePoolInit = reinterpret_cast<PoolInitFn>(0x7FD7C0);

using MeleeLoaderFn = int(__cdecl*)(int first, int second);
MeleeLoaderFn g_meleeLoader;

BYTE* __fastcall MeleePoolConstructor(BYTE* pool, void* unused) {
    return BuildPool(pool, g_meleePoolMemory, sizeof(g_meleePoolMemory), MeleePoolInit, kMeleeMoves);
}

// After melee.xtbl is read (Armageddon's with Guerrilla's, from the guerrilla_world mod): how full the table is.
// ---- Room in the tables' name pools (see the top: the moves' names, and the vehicles')
//
// A string pool's set-up takes its memory from a memory pool (`push size`) and makes the string pool over it
// (`push size` again); both values are made ours, checked against the game's first, all or nothing.
struct NamePool {
    const char* what;
    DWORD memoryValue;  // the push of the bytes taken for it
    DWORD poolValue;    // the push of the pool's size
    DWORD pool;         // the string pool (size +0x44, bytes used +0x4C)
    DWORD memory;       // where the set-up keeps its memory (0 until it has run)
    DWORD game, ours;
};

const NamePool kNamePools[] = {
    // "Melee Combat Strings": melee.xtbl's moves, set up by 0x8777E0.
    {"Melee move names", 0x8777F3, 0x87783D, 0x21B1BD8, 0x21AFDE0, 0x200, 0x1000},
    // "Vehicle info strings": vehicles.xtbl's names, models, display names, components, lights and props, set
    // up by 0x6271C0 (its memory from the pool its caller hands it: the Permanent Mempool at start-up).
    {"Vehicle names", 0x6271E0, 0x627228, 0x14A15F8, 0x14A15D0, 0x2000, 0x8000},
    // ("Vehicle interaction info strings", vehicle_interactions.xtbl's, is the guerrilla_vehicles mod's.)
};

DWORD NamePoolValue(const NamePool& p, DWORD field) { return *reinterpret_cast<const DWORD*>(p.pool + field); }

void MoreNameRoom() {
    for (const NamePool& p : kNamePools) {
        if (*reinterpret_cast<const DWORD*>(p.memory) != 0) {
            rfa::Log("%s: the game has already made its pool; it keeps its %lu bytes", p.what, p.game);
            continue;
        }
        if (rfa::CodeValue(p.memoryValue) != p.game || rfa::CodeValue(p.poolValue) != p.game) {
            rfa::Log("%s: the game's code (0x%08lX, 0x%08lX) isn't as expected; it keeps its %lu bytes", p.what, p.memoryValue, p.poolValue,
                     p.game);
            continue;
        }
        if (!rfa::PatchCode(reinterpret_cast<void*>(p.memoryValue), &p.ours, sizeof(p.ours))) {
            rfa::Log("%s: could not change the game's code; it keeps its %lu bytes", p.what, p.game);
            continue;
        }
        if (!rfa::PatchCode(reinterpret_cast<void*>(p.poolValue), &p.ours, sizeof(p.ours))) {
            rfa::PatchCode(reinterpret_cast<void*>(p.memoryValue), &p.game, sizeof(p.game));
            rfa::Log("%s: could not change the game's code; it keeps its %lu bytes", p.what, p.game);
            continue;
        }
        rfa::Log("%s: %lu bytes (the game's %lu)", p.what, p.ours, p.game);
    }
}

// Each pool's use, once its tables are read (logged at the player's first step).
void LogNameRoom() {
    for (const NamePool& p : kNamePools)
        if (NamePoolValue(p, kMempoolSize)) rfa::Log("%s: %lu of %lu bytes in use", p.what, NamePoolValue(p, kMempoolUsed), NamePoolValue(p, kMempoolSize));
}

constexpr DWORD kMeleeNamesPool = 0x21B1BD8;

int __cdecl MeleeLoader(int first, int second) {
    const int result = g_meleeLoader(first, second);
    rfa::Log("Melee moves: %d of %d places in use; names %lu of %lu bytes", PoolInUse(kMeleePool), PoolHolds(kMeleePool),
             *reinterpret_cast<const DWORD*>(kMeleeNamesPool + kMempoolUsed), *reinterpret_cast<const DWORD*>(kMeleeNamesPool + kMempoolSize));
    return result;
}

// The user, 2026-11-06: "Yes, enlarge it". The constructor's count is one byte and its buffer a fixed part
// of the object (as the projectiles'), so the mod runs the constructor itself with a buffer for 64.
void MoreMeleeMoves() {
    if (PoolHolds(kMeleePool) != 0) {
        rfa::Log("Melee moves: the game has already built its table; it keeps its %lu", kGameMeleeMoves);
        return;
    }
    if (rfa::CodeValue(kMeleeMemoryValue) != kGameMeleeMemory || *reinterpret_cast<const BYTE*>(kMeleeCountValue) != kGameMeleeMoves ||
        *reinterpret_cast<const BYTE*>(kMeleeBufferValue) != 0x30) {
        rfa::Log("Melee moves: the game's constructor (0x%08lX) isn't as expected; the game keeps its %lu", kMeleeConstructor, kGameMeleeMoves);
        return;
    }
    if (!rfa::HookFunction(kMeleeConstructor, kMeleeConstructorPrologue, reinterpret_cast<void*>(&MeleePoolConstructor))) {
        rfa::Log("Melee moves: could not change the game's code; it keeps its %lu", kGameMeleeMoves);
        return;
    }
    g_meleeLoader = reinterpret_cast<MeleeLoaderFn>(rfa::HookFunction(kMeleeLoader, kMeleeLoaderPrologue, reinterpret_cast<void*>(&MeleeLoader)));
    rfa::Log("Melee moves: the game holds %lu (its own %lu)", kMeleeMoves, kGameMeleeMoves);
}

// ---- Room in the start-up's memory pools (see the top: the preloaded items, the Permanent Mempool)
//
// The game's start-up (0xA45600) makes each of these pools the same way: `push size` → 0xA44A20 (the block,
// _aligned_malloc from the process heap), then `push size` again → 0x512B30 (the pool made of it). Both values
// are made ours, before the start-up runs: each checked against the game's first, neither changed if one
// differs, the first put back if the second can't be written.
struct StartupPool {
    const char* what;
    DWORD blockValue;  // the push of the block's size
    DWORD poolValue;   // the push of the pool's size
    DWORD pool;        // the pool (size +0x44, bytes used +0x4C)
    DWORD game, ours;
    const char* holds;
};

const StartupPool kStartupPools[] = {
    {"Item preload", 0xA47521, 0xA4754F, 0x26AF1A0, 0x1100000, 32 * 1024 * 1024,
     "the preloaded items' containers, then Armageddon's items' low-detail textures"},
    // The user's 21:19 start stopped on it ("Failed aligned alloc ... in Permanent mempool cpu pool (max
    // 12.00MB, space left 3104)"): Armageddon's own (its permanent pegs 3.6 MB, the effects 3.1 MB, ...) and the
    // mods' raised tables (animation map, name pools, patrols, ...) no longer fit in 12 MB.
    {"Permanent mempool", 0xA47277, 0xA472A5, kPermanentMempool, 0xC00000, 24 * 1024 * 1024,
     "what the game keeps for good from start-up (its tables, pegs, effects; the mods' raised tables)"},
};

void MoreStartupRoom() {
    for (const StartupPool& p : kStartupPools) {
        const DWORD gameMb = p.game / (1024 * 1024), oursMb = p.ours / (1024 * 1024);
        if (*reinterpret_cast<const DWORD*>(p.pool + kMempoolSize) != 0) {
            rfa::Log("%s: the game has already made its pool; it keeps its %lu MB", p.what, gameMb);
            continue;
        }
        if (rfa::CodeValue(p.blockValue) != p.game || rfa::CodeValue(p.poolValue) != p.game) {
            rfa::Log("%s: the game's code (0x%08lX, 0x%08lX) isn't as expected; it keeps its %lu MB", p.what, p.blockValue, p.poolValue, gameMb);
            continue;
        }
        if (!rfa::PatchCode(reinterpret_cast<void*>(p.blockValue), &p.ours, sizeof(p.ours))) {
            rfa::Log("%s: could not change the game's code; it keeps its %lu MB", p.what, gameMb);
            continue;
        }
        if (!rfa::PatchCode(reinterpret_cast<void*>(p.poolValue), &p.ours, sizeof(p.ours))) {
            rfa::PatchCode(reinterpret_cast<void*>(p.blockValue), &p.game, sizeof(p.game));
            rfa::Log("%s: could not change the game's code; it keeps its %lu MB", p.what, gameMb);
            continue;
        }
        rfa::Log("%s: %lu MB (the game's %lu MB): %s", p.what, oursMb, gameMb, p.holds);
    }
}

// At the player's first step (start-up and the level's load done): each pool's use, logged once.
void LogItemPreload() {
    for (const StartupPool& p : kStartupPools) {
        const DWORD size = *reinterpret_cast<const DWORD*>(p.pool + kMempoolSize);
        const DWORD used = *reinterpret_cast<const DWORD*>(p.pool + kMempoolUsed);
        if (size) rfa::Log("%s: %lu of %lu bytes in use (%lu left)", p.what, used, size, size > used ? size - used : 0);
    }
}

bool g_upgradesRunning = false;

void MasonsHammer() {
    g_swapWeapon = reinterpret_cast<SwapWeaponFn>(rfa::HookFunction(kSwapWeapon, kSwapWeaponPrologue, reinterpret_cast<void*>(&SwapWeapon)));
    if (!g_swapWeapon) rfa::Log("Hammer: could not wrap the swap; Guerrilla's hammer can be swapped away");
    else rfa::Log("Hammer: Mason is given Guerrilla's sledgehammer (slot 3) when he has none, and never swaps it away");
}

// ---- The player's melee hits, logged (the user's Round 1, 2026-11-06: the hammer "does no damage")
//
// A diagnostic for that report, to keep or take out once its cause is known: it changes nothing in
// the game. So a test shows whether a swing lands, on what, and for how much. Armageddon picks a melee hit's
// damage in 0x878780 (cdecl: the attacker, the move's data for the attacker's animation set, the object
// hit; the damage returned): by the object's type (0x422AD0: 0 a person, 2 a building or other mover,
// 3 a vehicle) the data's figure for it (people, structures, vehicles), or 10 when there is no data.
// The wrap calls it and returns its figure unchanged; for the player's hits it logs a line, at most
// four a second. No line while swinging at someone means the hit test never got that far.
constexpr DWORD kMeleeDamage = 0x878780;
constexpr size_t kMeleeDamagePrologue = 6;  // push ebp / mov ebp,esp / sub esp,24h
using MeleeDamageFn = float(__cdecl*)(BYTE* attacker, const BYTE* data, BYTE* target);
MeleeDamageFn g_meleeDamage = nullptr;
auto const ObjectTypeOf = reinterpret_cast<int(__thiscall*)(const BYTE* object)>(0x422AD0);
DWORD g_lastMeleeLog = 0;

const char* ObjectKind(int type) {
    switch (type) {
        case 0: return "person";
        case 2: return "building or other mover";
        case 3: return "vehicle";
        default: return "object";
    }
}

float __cdecl MeleeDamage(BYTE* attacker, const BYTE* data, BYTE* target) {
    const float damage = g_meleeDamage(attacker, data, target);
    if (attacker && attacker == LocalPlayer()) {
        const DWORD now = GetTickCount();
        if (now - g_lastMeleeLog >= 250) {
            g_lastMeleeLog = now;
            const char* weapon = WeaponName(HeldWeapon(attacker));
            const int type = target ? ObjectTypeOf(target) : -1;
            rfa::Log("Melee hit: %s hit a %s (type %d) for %.0f%s", weapon ? weapon : "(nothing in hand)", ObjectKind(type), type,
                     damage, data ? "" : " (the move has no data for the player's set: the game's 10)");
        }
    }
    return damage;
}

void LogMeleeHits() {
    g_meleeDamage = reinterpret_cast<MeleeDamageFn>(rfa::HookFunction(kMeleeDamage, kMeleeDamagePrologue, reinterpret_cast<void*>(&MeleeDamage)));
    if (!g_meleeDamage) rfa::Log("Melee hits: could not wrap the damage pick; they aren't logged");
    else rfa::Log("Melee hits: the player's are logged (\"Melee hit:\" lines)");
}

}  // namespace

extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build; nothing to do");
        return;
    }
    MoreInventoryItems();
    MoreStartupRoom();
    MoreProjectiles();
    MoreMeleeMoves();
    MoreNameRoom();
    MoreTriggerRoom();
    PlayerAnimations();
    DroppedGuns();
    PlayerMovement();
    MasonsHammer();
    LogMeleeHits();
    // The upgrades' purchases happen in the heading step: without it the window stays shut.
    if (g_headingStep) {
        upgrades::Init();
        backpacks::Init();
        g_upgradesRunning = true;
    } else {
        rfa::Log("Upgrades: no heading step to buy them in; the window is off");
    }
}

extern "C" __declspec(dllexport) void __cdecl RFAMod_Menu(const RFAMenu* menu) {
    if (g_upgradesRunning) upgrades::Menu(menu);
}
