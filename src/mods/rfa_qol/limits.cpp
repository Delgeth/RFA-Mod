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
//   vehicle slots      the vehicle pool 0x1972900: taken of the slots it holds (+0x10; 0x7612F0 the free)
#define RFA_MOD_NAME "limits"
#include "rfa_mod.h"
#include "rfa_menu.h"
#include "rfa_qol.h"

namespace {

int ValueAt(DWORD address) { return *reinterpret_cast<const volatile int*>(address); }

struct Limit {
    const char* what;
    DWORD count;        // where the game keeps how many are taken
    DWORD size;         // where it keeps how many there may be (a value in its code, or a variable), or 0
    int fixed;          // the size, when `size` is 0
    int game;           // the game's own size
    const char* about;  // what fills it
};

const Limit kLimits[] = {
    {"Vehicles", 0x148BD10, 0x148BD0C, 0, 40, "One for each entry of vehicles.xtbl: Armageddon's and Guerrilla's"},
    {"Animation sets", 0x2632170, 0x97BE87, 0, 140, "Set files and animated objects; each vehicle's interaction uses one"},
    {"Bone infos", 0x3393360, 0xD7B613, 0, 138, "Each vehicle keeps one; people and animated objects take one while about"},
    {"Animation triggers", 0x34C6D68, 0xD89D4F, 0, 5750, "Every animation's triggers (doors, feet, camera), from anim_files.xtbl"},
    {"Library animations", 0x34B2C10, 0xD87682, 0, 4500, "Every animation the animation library knows"},
    {"Animation entries", 0x25CD738, 0x95DCFA, 0, 4224, "The game's entries for anim_files.xtbl"},
    {"Explosions", 0x1893498, 0x6E9D00, 0, 132, "explosions.xtbl and the DLCs'"},
    {"Visual effects", 0x1200BF4, 0x4EE40A, 0, 950, "vfx.xtbl: Armageddon's, Guerrilla's props', and a DLC level's while it's loaded"},
};

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
            Row(limit.what, ValueAt(limit.count), limit.size ? ValueAt(limit.size) : limit.fixed, limit.game, limit.about);
        const int slots = ValueAt(reinterpret_cast<DWORD>(kVehiclePool) + kPoolSlots);
        if (slots > 0)
            Row("Vehicle slots", slots - PoolRoom(kVehiclePool, nullptr), slots, slots,
                "Room in the vehicle pool for vehicles streamed in now");
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
