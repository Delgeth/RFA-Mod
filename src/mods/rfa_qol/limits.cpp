// A Limits window in the in-game menus (rfa_menu.h). A part of rfa_qol.dll. Open the console (the `
// key), then Limits in the bar along the bottom of the screen: how full each of the engine's fixed
// tables is, and how big it is now. Where a mod (guerrilla_vehicles) has given the game a bigger table,
// its size is read from the game's code, where the mod wrote it, and the game's own size is shown
// beside it. A table nearly full shows in red: the game drops what doesn't fit without a word (the
// 41st vehicle, the 5751st animation trigger), so this is where to look first when something
// Guerrilla's goes missing.
//
// Each line: what is counted, where the game keeps the count, and its size (a fixed number, or the
// value in the game's code that holds it):
//   vehicles           the vehicle list 0x148BD08 (+8 how many, +4 how many it holds; 40)
//   animation sets     0x2632170, size in `cmp [2632170h],8Ch` at 0x97BE87 (140)
//   bone infos         the animation library's entity bone infos, 0x3393360, size at 0xD7B613 (138)
//   animation triggers 0x34C6D68, size in `cmp [34C6D68h],1676h` at 0xD89D4F (5750)
//   library animations the animation library's entries, 0x34B2C10, size in `cmp [34B2C10h],1194h` at 0xD87682 (4500)
//   animation entries  the game's animation entries, 0x25CD738, size in `cmp [25CD738h],1080h` at 0x95DCFA (4224)
//   explosions         the explosion table, 0x1893498, size in `cmp [1893498h],84h` at 0x6E9D00 (132)
//   visual effects     the visual effects, 0x1200BF4, size in `cmp [1200BF4h],3B6h` at 0x4EE40A (950)
//   effects            the effects, 0x1200BEC, size in `cmp [1200BECh],4C9h` at 0x4EEA51 (1225)
//   inventory items    the inventory item types, 0x19F8570, size in `cmp [19F8570h],55h` at 0x7A77F0: a one-byte
//                      value at 0x7A77F6 (85; guerrilla_gear raises it to 127)
//   vehicle slots      the vehicle pool 0x1972900: taken of the slots it holds (+0x10; 0x7612F0 the free)
//   animation names    the "Anim strings" pool 0x2684EC8: bytes used (+0x4C) of its size (+0x44; 128 KB,
//                      guerrilla_people gives it 512 KB): every animation's, set's, state's and action's name
//   projectiles        the projectile types' pool 0x2174F90: how many it holds (a word at +0xA; 100,
//                      guerrilla_gear gives it 256), each place taken unless its byte (+0x18's array) is 0xFF
//   projectile names   the "Proj String Pool" 0x2174ED8 (+0x4C of +0x44; 2 KB, guerrilla_gear 8 KB)
//   projectile stages  the "Projectile Mempool" 0x2174F38 (+0x4C of +0x44; 96 KB, guerrilla_gear 256 KB)
//   melee move names   the "Melee Combat Strings" pool 0x21B1BD8 (+0x4C of +0x44; 512 bytes, guerrilla_gear 4 KB)
//   vehicle names,     the "Vehicle info strings" pool 0x14A15F8 and "Vehicle interaction info strings"
//   interaction names  0x14E6990 (+0x4C of +0x44; 8 KB each; guerrilla_gear and guerrilla_vehicles 32 KB)
//   item preload       the "item_preload" pool 0x26AF1A0 (+0x4C of +0x44; 17 MB, guerrilla_gear 32 MB): the
//                      preloaded items' containers, then Armageddon's items_containers.gpeg_pc (8.8 MB)
//   melee moves        the melee moves' pool 0x21AFEF0, a pool as the projectiles' (32, guerrilla_gear 64)
//   character names    the "Human info strings" pool 0x21DC118 (+0x4C of +0x44; 6,200 bytes, guerrilla_people 64 KB)
//   patrols            the object_patrol pool 0x20F3B10, a pool as the projectiles' (24, guerrilla_people 72)
//   people, squads,    the npc pool 0x21DCE30 (100), the object_squad pool 0x2160AF0 (100) and the
//   squad spawn nodes  object_squad_spawn_node pool 0x2108CE0 (384): the game's own sizes, as Guerrilla's
//                      (it holds 100 NPCs and 100 squads, and 256 squad spawn nodes)
#define RFA_MOD_NAME "limits"
#include "rfa_mod.h"
#include "rfa_menu.h"
#include "rfa_qol.h"

namespace {

int ValueAt(DWORD address) { return *reinterpret_cast<const volatile int*>(address); }
int ByteAt(DWORD address) { return *reinterpret_cast<const volatile signed char*>(address); }

struct Limit {
    const char* what;
    DWORD count;        // where the game keeps how many are taken
    DWORD size;         // where it keeps how many there may be (a value in its code, or a variable), or 0
    int fixed;          // the size, when `size` is 0
    int game;           // the game's own size
    const char* about;  // what fills it
    int width = 4;      // how many bytes the size in the game's code takes: 4, or 1 (a one-byte value in an instruction)
};

const Limit kLimits[] = {
    {"Vehicles", 0x148BD10, 0x148BD0C, 0, 40, "One for each entry of vehicles.xtbl: Armageddon's and Guerrilla's"},
    {"Animation sets", 0x2632170, 0x97BE87, 0, 140, "Set files and animated objects; each vehicle's interaction uses one"},
    {"Bone infos", 0x3393360, 0xD7B613, 0, 138, "Each vehicle keeps one; people and animated objects take one while about"},
    {"Animation triggers", 0x34C6D68, 0xD89D4F, 0, 5750, "Every animation's triggers (doors, feet, camera), from anim_files.xtbl"},
    {"Library animations", 0x34B2C10, 0xD87682, 0, 4500, "Every animation the animation library knows"},
    {"Animation entries", 0x25CD738, 0x95DCFA, 0, 4224, "The game's entries for anim_files.xtbl"},
    {"Animation names (bytes)", 0x2684EC8 + 0x4C, 0x2684EC8 + 0x44, 0, 0x20000,
     "Every animation's, set's, state's and action's name; full, the game stops while it starts"},
    {"Explosions", 0x1893498, 0x6E9D00, 0, 132, "explosions.xtbl and the DLCs'"},
    {"Visual effects", 0x1200BF4, 0x4EE40A, 0, 950, "vfx.xtbl: Armageddon's, Guerrilla's props', and a DLC level's while it's loaded"},
    {"Effects", 0x1200BEC, 0x4EEA51, 0, 1225, "effects.xtbl: Armageddon's, Guerrilla's props' and vehicles', and a DLC level's while it's loaded"},
    {"Inventory items", 0x19F8570, 0x7A77F6, 0, 85, "items_inventory.xtbl: one for each weapon you can carry (Armageddon's, Guerrilla's), and a DLC level's while it's loaded", 1},
    {"Character names (bytes)", 0x21DC118 + 0x4C, 0x21DC118 + 0x44, 0, 0x1838,
     "Every character's names, models, skeletons and props' names (character.xtbl); full, the game stops while it starts"},
    {"Projectile names (bytes)", 0x2174ED8 + 0x4C, 0x2174ED8 + 0x44, 0, 0x800, "Every projectile type's name"},
    {"Projectile stages (bytes)", 0x2174F38 + 0x4C, 0x2174F38 + 0x44, 0, 0x18000, "Every projectile type's stages, triggers and effects"},
    {"Melee move names (bytes)", 0x21B1BD8 + 0x4C, 0x21B1BD8 + 0x44, 0, 0x200, "melee.xtbl's move names: Armageddon's and Guerrilla's (rfg_)"},
    {"Vehicle names (bytes)", 0x14A15F8 + 0x4C, 0x14A15F8 + 0x44, 0, 0x2000,
     "vehicles.xtbl's names, models, display names, components, lights and props"},
    {"Vehicle interaction names (bytes)", 0x14E6990 + 0x4C, 0x14E6990 + 0x44, 0, 0x2000, "vehicle_interactions.xtbl's names"},
    {"Item preload (bytes)", 0x26AF1A0 + 0x4C, 0x26AF1A0 + 0x44, 0, 0x1100000,
     "Every preloaded item's own container (guns, clips, pickups), then Armageddon's items' low-detail textures (8.8 MB)"},
};

// The projectile types', melee moves' and the world's object pools: how many places each holds (a word at
// +0xA), and a byte for each place (an array at +0x18), 0xFF while free.
constexpr DWORD kProjectilePool = 0x2174F90, kMeleePool = 0x21AFEF0, kPoolHolds = 0xA, kPoolFlags = 0x18;
constexpr DWORD kPatrolPool = 0x20F3B10, kNpcPool = 0x21DCE30, kSquadPool = 0x2160AF0, kSquadNodePool = 0x2108CE0;

void Row(const char* what, int used, int size, int game, const char* about);

void PoolRow(const char* what, DWORD pool, int game, const char* about) {
    const int holds = *reinterpret_cast<const volatile WORD*>(pool + kPoolHolds);
    const BYTE* flags = *reinterpret_cast<BYTE* const volatile*>(pool + kPoolFlags);
    if (holds <= 0 || !flags) return;
    int used = 0;
    for (int i = 0; i < holds; ++i)
        if (flags[i] != 0xFF) ++used;
    Row(what, used, holds, game, about);
}

BYTE* const kVehiclePool = reinterpret_cast<BYTE*>(0x1972900);
constexpr DWORD kPoolSlots = 0x10;  // how many slots the pool holds (its slot list's count)
auto const PoolRoom = reinterpret_cast<int(__fastcall*)(void* pool, void* unused)>(0x7612F0);

bool g_open = false;

void Row(const char* what, int used, int size, int game, const char* about) {
    ImGui::TableNextRow();
    ImGui::TableNextColumn();
    ImGui::TextUnformatted(what);
    if (about && *about) ImGui::SetItemTooltip("%s", about);
    ImGui::TableNextColumn();
    const float full = size > 0 ? static_cast<float>(used) / size : 0;
    const ImVec4 colour = full >= 0.9f ? ImVec4(1, 0.35f, 0.3f, 1) : full >= 0.75f ? ImVec4(1, 0.8f, 0.3f, 1) : ImVec4(0.6f, 0.9f, 0.6f, 1);
    ImGui::PushStyleColor(ImGuiCol_PlotHistogram, colour);
    char text[48];
    snprintf(text, sizeof(text), "%d of %d", used, size);
    ImGui::ProgressBar(full > 1 ? 1 : full, ImVec2(-FLT_MIN, 0), text);
    ImGui::PopStyleColor();
    ImGui::TableNextColumn();
    if (game != size) ImGui::TextDisabled("raised from %d", game);
}

void DrawWindow() {
    ImGui::SetNextWindowSize(ImVec2(ImGui::GetFontSize() * 30, ImGui::GetFontSize() * 15), ImGuiCond_FirstUseEver);
    if (!ImGui::Begin("Limits", &g_open)) {
        ImGui::End();
        return;
    }
    ImGui::TextDisabled("How full the game's fixed tables are. What doesn't fit is dropped without a word.");
    if (ImGui::BeginTable("##limits", 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_SizingStretchProp)) {
        ImGui::TableSetupColumn("What", ImGuiTableColumnFlags_WidthStretch, 1.2f);
        ImGui::TableSetupColumn("Used", ImGuiTableColumnFlags_WidthStretch, 2.0f);
        ImGui::TableSetupColumn("", ImGuiTableColumnFlags_WidthStretch, 1.0f);
        for (const Limit& limit : kLimits)
            Row(limit.what, ValueAt(limit.count), !limit.size ? limit.fixed : limit.width == 1 ? ByteAt(limit.size) : ValueAt(limit.size),
                limit.game, limit.about);
        const int slots = ValueAt(reinterpret_cast<DWORD>(kVehiclePool) + kPoolSlots);
        if (slots > 0)
            Row("Vehicle slots", slots - PoolRoom(kVehiclePool, nullptr), slots, slots,
                "Room in the vehicle pool for vehicles streamed in now");
        PoolRow("Projectiles", kProjectilePool, 100,
                "projectiles.xtbl: Armageddon's, a DLC's, and Guerrilla's thrown and launched weapons' and turrets'");
        PoolRow("Melee moves", kMeleePool, 32, "melee.xtbl: Armageddon's, and Guerrilla's swings and bashes (rfg_)");
        PoolRow("Patrols", kPatrolPool, 24, "Patrol routes in the zones loaded now (Guerrilla's world places up to 29 in one 3 x 3 block)");
        PoolRow("People (NPCs)", kNpcPool, 100, "Everyone but the player: squads, people dropped in, crews (Guerrilla holds 100 too)");
        PoolRow("Squads", kSquadPool, 100, "Squads made from the loaded zones' squad spawn nodes, and those dropped in");
        PoolRow("Squad spawn nodes", kSquadNodePool, 384, "Squad spawn nodes in the zones loaded now");
        ImGui::EndTable();
    }
    ImGui::End();
}

}  // namespace

void limits::Init() { rfa::Log("Limits window ready: open the console, then Limits in the bar at the bottom"); }

void limits::Menu(const RFAMenu* menu) {
    if (!rfa::menu::Use(menu)) return;
    if (menu->stage == RFA_MENU_BAR) ImGui::MenuItem("Limits", nullptr, &g_open);
    else if (g_open) DrawWindow();
}
