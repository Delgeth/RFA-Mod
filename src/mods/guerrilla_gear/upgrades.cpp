// Guerrilla's upgrades, bought with Armageddon's salvage in an F7 window ("Guerrilla upgrades", under
// Guerrilla in the bar along the bottom of the screen).
//
// Red Faction: Guerrilla sells its upgrades at its safehouses' salvage shop (upgrades.xtbl): a ladder of
// levels for each, the first the one you start with, the others bought in order, at their cost × the
// difficulty's MENU_UPGRADE_COST_MULTIPLIER (Easy 0.8, Medium 1.0, Hard 1.1, Insane 1.2). Some are for
// sale from the start, some once a district is liberated or some guerrilla activities are done, some once
// another is bought (the grinder opens its faster and explosive upgrades). Re-Mars-tered adds its nine
// multiplayer backpacks to the shop, 250 salvage each. The weapons build (RfaFormats\Guerrilla\
// GuerrillaUpgrades.cs) writes them all to mods\guerrilla_gear\upgrades.txt, with each level's chain
// ("needs") and Guerrilla's other conditions as notes: the world has no districts to liberate or
// activities yet, so those aren't held to.
//
// Armageddon's salvage is the player's (+0x2C38): the game's own get (0x54C2B0, cdecl: the amount into
// *out, false without a player) and add (0x54C4C0, cdecl: the amount, negative to spend, and two flags,
// false and true as a purchase) are used. Armageddon's own upgrades (the Nano Forge's, a 64-bit mask on
// the player) have no room for Guerrilla's levels, so what is bought is kept by the mod, in
// mods\guerrilla_gear\upgrades_bought.txt: one list for every save (a save loaded later keeps them).
//
// The window draws on the thread that draws the game; buying, and giving a gun, happen on the game's
// own thread, from the player's heading step (guerrilla_gear.cpp), which also reads the salvage shown.
//
// What a purchase does, so far:
//   - a weapon (remote charges, grinder, arc welder, rocket launcher, proximity mines, thermobaric
//     rockets, nano rifle): its first level gives the gun (into a free slot; Take gives it again, as
//     Guerrilla's weapon locker does); its "more ammo" level isn't in effect yet;
//   - Stonebreaker and Shattermaster: Mason's hammer becomes that one (guerrilla_gear.cpp's hammer check
//     drops the lesser and gives the better into slot 3);
//   - the jetpack and the nine backpacks: owned, and one is worn (Wear; the first bought is put on), and
//     backpacks.cpp works it (its second jetpack level, Jetpack Recharge, doubles the jetpack's refill);
//   - the rest (remote charge count, the guns' own upgrades, armor, the personnel detector, radiation
//     shielding, the salvage collector, ore additive, Guerrilla Express, quantum multiplier): kept as
//     bought, not in effect yet.
#define RFA_MOD_NAME "guerrilla_gear"
#include "rfa_mod.h"
#include "rfa_menu.h"
#include "guerrilla_gear.h"
#include <cmath>
#include <deque>
#include <fstream>
#include <map>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

namespace {

constexpr DWORD kDifficulty = 0x10F7E98;  // the game's difficulty: 0 easy, 1 medium, 2 hard, 3 insane
auto const GetSalvage = reinterpret_cast<bool(__cdecl*)(int* out)>(0x54C2B0);
auto const AddSalvage = reinterpret_cast<bool(__cdecl*)(int amount, bool first, bool second)>(0x54C4C0);

struct Level {
    int cost = 0;
    int value = 0;
    std::string name, description;
    std::string needs;  // "" or "upgrade:level": that level bought first
    std::string note;   // Guerrilla's condition, or ""
};

struct Upgrade {
    std::string name, shown, type, weapon, effect;
    std::vector<Level> levels;
    bool Backpack() const { return effect.rfind("backpack:", 0) == 0; }
    int BackpackId() const { return Backpack() ? std::atoi(effect.c_str() + 9) : -1; }
    bool Weapon() const { return effect == "weapon"; }
    std::string Hammer() const { return effect.rfind("hammer:", 0) == 0 ? effect.substr(7) : std::string(); }
};

std::vector<Upgrade> g_upgrades;  // read at start-up, then only read
float g_costScale[4] = {1.0f, 1.0f, 1.0f, 1.0f};

struct Request {
    enum Kind { kBuy, kTake, kWear } kind;
    int upgrade;  // its place in g_upgrades (kWear: the backpack, -1 none)
};

std::mutex g_lock;  // all below
std::map<std::string, int> g_bought;  // upgrade → the level bought (none: 0, its first)
int g_worn = -1;
std::deque<Request> g_requests;
std::string g_status;
int g_salvage = -1;  // as the game's thread last read it

bool g_open = false;  // the window (the drawing thread's)

std::vector<std::string> Split(const std::string& line) {
    std::vector<std::string> fields;
    std::string field;
    std::istringstream in(line);
    while (std::getline(in, field, '\t')) fields.push_back(field);
    return fields;
}

std::wstring BoughtFile() { return gear::Folder() + L"upgrades_bought.txt"; }

void ReadUpgrades() {
    std::ifstream list(gear::Folder() + L"upgrades.txt");
    std::string line;
    while (std::getline(list, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line[0] == '#') continue;
        const auto f = Split(line);
        if (f[0] == "costs" && f.size() >= 5) {
            for (int i = 0; i < 4; ++i) g_costScale[i] = static_cast<float>(std::atof(f[i + 1].c_str()));
        } else if (f[0] == "upgrade" && f.size() >= 6) {
            g_upgrades.push_back({f[1], f[2], f[3], f[4] == "-" ? "" : f[4], f[5], {}});
        } else if (f[0] == "level" && f.size() >= 7 && !g_upgrades.empty()) {
            g_upgrades.back().levels.push_back({std::atoi(f[1].c_str()), std::atoi(f[2].c_str()), f[3], f[4],
                                                f[5] == "-" ? "" : f[5], f[6] == "-" ? "" : f[6]});
        }
    }
}

void ReadBought() {
    std::ifstream list(BoughtFile());
    std::string word, name;
    while (list >> word) {
        if (word == "bought" && list >> name) {
            int level = 0;
            list >> level;
            g_bought[name] = level;
        } else if (word == "worn") {
            list >> g_worn;
        }
    }
}

// Under g_lock.
void SaveBought() {
    std::ofstream list(BoughtFile(), std::ios::trunc);
    list << "# Guerrilla's upgrades bought (guerrilla_gear's F7 window); one list for every save\n";
    for (const auto& [name, level] : g_bought)
        if (level > 0) list << "bought " << name << ' ' << level << '\n';
    list << "worn " << g_worn << '\n';
}

int BoughtLocked(const std::string& name) {
    auto found = g_bought.find(name);
    return found == g_bought.end() ? 0 : found->second;
}

const Upgrade* Find(const std::string& name) {
    for (const auto& u : g_upgrades)
        if (u.name == name) return &u;
    return nullptr;
}

int Difficulty() {
    const int level = *reinterpret_cast<const int*>(kDifficulty);
    return level < 0 || level > 3 ? 1 : level;
}

int Cost(const Level& level) { return static_cast<int>(std::floor(level.cost * g_costScale[Difficulty()] + 0.5f)); }

// Whether a level's chain is met (under g_lock); why not, if not.
bool NeedsMetLocked(const Level& level, std::string& why) {
    if (level.needs.empty()) return true;
    const size_t colon = level.needs.find(':');
    const std::string name = level.needs.substr(0, colon);
    const int wanted = colon == std::string::npos ? 1 : std::atoi(level.needs.c_str() + colon + 1);
    if (BoughtLocked(name) >= wanted) return true;
    const Upgrade* other = Find(name);
    why = "buy " + (other ? other->shown : name) + " first";
    return false;
}

// What buying this level does now.
const char* InEffect(const Upgrade& u, int level) {
    if (u.Weapon()) return level == 1 ? "gives you the gun" : "not in effect yet";
    if (!u.Hammer().empty()) return "Mason's hammer becomes this one";
    if (u.Backpack()) return u.BackpackId() == 0 ? "worn (Wear): jump held in the air climbs" : "worn (Wear): the backpack key switches it on; its ability isn't in yet";
    return "kept as bought; not in effect yet";
}

// ---- On the game's thread

void Buy(BYTE* player, int index) {
    const Upgrade& u = g_upgrades[index];
    std::string why;
    int next;
    {
        std::lock_guard<std::mutex> hold(g_lock);
        next = BoughtLocked(u.name) + 1;
        if (next >= static_cast<int>(u.levels.size())) {
            g_status = u.shown + ": all bought";
            return;
        }
        if (!NeedsMetLocked(u.levels[next], why)) {
            g_status = u.shown + ": " + why;
            return;
        }
    }
    const Level& level = u.levels[next];
    const int cost = Cost(level);
    int salvage = 0;
    if (!GetSalvage(&salvage)) {
        std::lock_guard<std::mutex> hold(g_lock);
        g_status = "No player to buy for";
        return;
    }
    if (salvage < cost) {
        std::lock_guard<std::mutex> hold(g_lock);
        g_status = u.shown + ": " + std::to_string(cost) + " salvage, you have " + std::to_string(salvage);
        return;
    }
    if (cost > 0 && !AddSalvage(-cost, false, true)) {
        std::lock_guard<std::mutex> hold(g_lock);
        g_status = "The game didn't take the salvage; nothing bought";
        return;
    }
    std::string given;
    if (u.Weapon() && next == 1 && !gear::GiveWeapon(player, u.weapon.c_str(), why)) given = " (" + why + ": Take gives it later)";
    {
        std::lock_guard<std::mutex> hold(g_lock);
        g_bought[u.name] = next;
        if (u.Backpack() && g_worn < 0) g_worn = u.BackpackId();
        SaveBought();
        g_status = "Bought " + (level.name.empty() ? u.shown : level.name) + " for " + std::to_string(cost) + " salvage" + given;
    }
    rfa::Log("Upgrades: bought %s level %d (%s) for %d salvage%s", u.name.c_str(), next, level.name.c_str(), cost, given.c_str());
}

void Take(BYTE* player, int index) {
    const Upgrade& u = g_upgrades[index];
    std::string why;
    const bool given = gear::GiveWeapon(player, u.weapon.c_str(), why);
    std::lock_guard<std::mutex> hold(g_lock);
    g_status = given ? u.shown + ": given" : u.shown + ": " + why;
}

}  // namespace

void upgrades::Init() {
    ReadUpgrades();
    std::lock_guard<std::mutex> hold(g_lock);
    ReadBought();
    if (g_upgrades.empty()) {
        rfa::Log("Upgrades: no mods\\guerrilla_gear\\upgrades.txt (build Guerrilla's weapons with GuerrillaBuilder.exe); the window is empty");
        return;
    }
    int bought = 0;
    for (const auto& [name, level] : g_bought) bought += level > 0 ? 1 : 0;
    rfa::Log("Upgrades: %zu of Guerrilla's for sale in the F7 window (Guerrilla > Upgrades), %d bought", g_upgrades.size(), bought);
}

void upgrades::Step(BYTE* player) {
    int salvage = -1;
    GetSalvage(&salvage);
    std::deque<Request> requests;
    {
        std::lock_guard<std::mutex> hold(g_lock);
        g_salvage = salvage;
        requests.swap(g_requests);
    }
    for (const Request& r : requests) {
        if (r.kind == Request::kBuy) {
            Buy(player, r.upgrade);
        } else if (r.kind == Request::kTake) {
            Take(player, r.upgrade);
        } else {
            std::lock_guard<std::mutex> hold(g_lock);
            g_worn = r.upgrade;
            SaveBought();
        }
    }
}

std::string upgrades::BestHammer() {
    std::lock_guard<std::mutex> hold(g_lock);
    std::string best;  // the later in Guerrilla's list (Shattermaster after Stonebreaker) the better
    for (const auto& u : g_upgrades)
        if (!u.Hammer().empty() && BoughtLocked(u.name) > 0) best = u.Hammer();
    return best;
}

int upgrades::WornBackpack() {
    std::lock_guard<std::mutex> hold(g_lock);
    return g_worn;
}

int upgrades::BoughtLevel(const char* name) {
    std::lock_guard<std::mutex> hold(g_lock);
    return name ? BoughtLocked(name) : 0;
}

// ---- The window (on the thread that draws the game)

namespace {

void Ask(Request::Kind kind, int upgrade) {
    std::lock_guard<std::mutex> hold(g_lock);
    g_requests.push_back({kind, upgrade});
}

void DrawRow(int index, int salvage) {
    const Upgrade& u = g_upgrades[index];
    int bought;
    int worn;
    std::string why;
    bool needsMet = true;
    const int count = static_cast<int>(u.levels.size());
    {
        std::lock_guard<std::mutex> hold(g_lock);
        bought = BoughtLocked(u.name);
        worn = g_worn;
        if (bought + 1 < count) needsMet = NeedsMetLocked(u.levels[bought + 1], why);
    }
    ImGui::PushID(index);
    ImGui::TableNextRow();
    ImGui::TableNextColumn();
    ImGui::TextUnformatted(u.shown.c_str());
    if (ImGui::IsItemHovered() && count > 1) {
        ImGui::BeginTooltip();
        for (int i = 1; i < count; ++i) {
            const Level& l = u.levels[i];
            ImGui::Text("%s %s: %d salvage%s", i <= bought ? "[bought]" : "        ", l.name.empty() ? u.shown.c_str() : l.name.c_str(), Cost(l),
                        l.value != i ? (" (" + std::to_string(l.value) + ")").c_str() : "");
            if (!l.description.empty()) ImGui::TextDisabled("    %s", l.description.c_str());
            if (!l.note.empty()) ImGui::TextDisabled("    %s (not held to: the world has no districts or activities yet)", l.note.c_str());
            ImGui::TextDisabled("    %s", InEffect(u, i));
        }
        ImGui::EndTooltip();
    }
    ImGui::TableNextColumn();
    if (bought == 0) ImGui::TextDisabled("-");
    else ImGui::Text("%d of %d", bought, count - 1);
    const bool more = bought + 1 < count;
    const int cost = more ? Cost(u.levels[bought + 1]) : 0;
    ImGui::TableNextColumn();
    if (more) ImGui::TextUnformatted(u.levels[bought + 1].name.empty() ? u.shown.c_str() : u.levels[bought + 1].name.c_str());
    else ImGui::TextDisabled("all bought");
    ImGui::TableNextColumn();
    if (more) ImGui::Text("%d", cost);
    ImGui::TableNextColumn();
    bool first = true;
    if (more) {
        ImGui::BeginDisabled(!needsMet || salvage < cost);
        if (ImGui::SmallButton("Buy")) Ask(Request::kBuy, index);
        ImGui::EndDisabled();
        if (!needsMet && ImGui::IsItemHovered(ImGuiHoveredFlags_AllowWhenDisabled)) ImGui::SetTooltip("%s", why.c_str());
        first = false;
    }
    if (u.Weapon() && bought > 0) {
        if (!first) ImGui::SameLine();
        if (ImGui::SmallButton("Take")) Ask(Request::kTake, index);
        if (ImGui::IsItemHovered()) ImGui::SetTooltip("Gives you the gun again (Guerrilla's weapon locker), into a free slot");
        first = false;
    }
    if (u.Backpack() && bought > 0) {
        if (!first) ImGui::SameLine();
        if (ImGui::RadioButton("Wear", worn == u.BackpackId())) Ask(Request::kWear, u.BackpackId());
    }
    ImGui::PopID();
}

void DrawTable(const char* id, bool (*shows)(const Upgrade&), int salvage) {
    if (!ImGui::BeginTable(id, 5, ImGuiTableFlags_RowBg | ImGuiTableFlags_SizingStretchProp | ImGuiTableFlags_ScrollY)) return;
    ImGui::TableSetupScrollFreeze(0, 1);
    ImGui::TableSetupColumn("Upgrade", ImGuiTableColumnFlags_WidthStretch, 3.0f);
    ImGui::TableSetupColumn("Bought", ImGuiTableColumnFlags_WidthStretch, 1.0f);
    ImGui::TableSetupColumn("Next", ImGuiTableColumnFlags_WidthStretch, 3.0f);
    ImGui::TableSetupColumn("Salvage", ImGuiTableColumnFlags_WidthStretch, 1.0f);
    ImGui::TableSetupColumn("", ImGuiTableColumnFlags_WidthStretch, 1.6f);
    ImGui::TableHeadersRow();
    for (int i = 0; i < static_cast<int>(g_upgrades.size()); ++i)
        if (shows(g_upgrades[i])) DrawRow(i, salvage);
    ImGui::EndTable();
}

void DrawWindow() {
    const float em = ImGui::GetFontSize();
    ImGui::SetNextWindowSize(ImVec2(em * 40, em * 26), ImGuiCond_FirstUseEver);
    if (ImGui::Begin("Guerrilla upgrades", &g_open)) {
        int salvage;
        std::string status;
        int worn;
        {
            std::lock_guard<std::mutex> hold(g_lock);
            salvage = g_salvage;
            status = g_status;
            worn = g_worn;
        }
        if (g_upgrades.empty()) {
            ImGui::TextWrapped("No upgrades: build Guerrilla's weapons with GuerrillaBuilder.exe (it writes mods\\guerrilla_gear\\upgrades.txt).");
        } else {
            if (salvage >= 0) ImGui::Text("Salvage: %d", salvage);
            else ImGui::TextDisabled("Salvage: (in a level)");
            ImGui::SameLine();
            ImGui::TextDisabled("  costs x%.1f at this difficulty; hover an upgrade for its levels", g_costScale[Difficulty()]);
            if (!status.empty()) ImGui::TextWrapped("%s", status.c_str());
            if (ImGui::BeginTabBar("kinds")) {
                if (ImGui::BeginTabItem("Weapons")) {
                    DrawTable("weapons", [](const Upgrade& u) { return !u.Backpack() && u.type.rfind("Weapon", 0) == 0; }, salvage);
                    ImGui::EndTabItem();
                }
                if (ImGui::BeginTabItem("Mason")) {
                    DrawTable("general", [](const Upgrade& u) { return !u.Backpack() && u.type.rfind("Weapon", 0) != 0; }, salvage);
                    ImGui::EndTabItem();
                }
                if (ImGui::BeginTabItem("Backpacks")) {
                    if (ImGui::RadioButton("Wear none", worn < 0)) Ask(Request::kWear, -1);
                    if (worn >= 0) {
                        ImGui::SameLine();
                        ImGui::TextDisabled("  fuel %d%%%s; the jetpack climbs with jump held in the air, the others switch on with the backpack key",
                                            static_cast<int>(backpacks::Fuel() * 100.0f + 0.5f), backpacks::On() ? " (on)" : "");
                    }
                    DrawTable("backpacks", [](const Upgrade& u) { return u.Backpack(); }, salvage);
                    ImGui::EndTabItem();
                }
                ImGui::EndTabBar();
            }
        }
    }
    ImGui::End();
}

}  // namespace

void upgrades::Menu(const RFAMenu* menu) {
    if (!rfa::menu::Use(menu)) return;
    if (menu->stage == RFA_MENU_BAR) {
        if (ImGui::BeginMenu("Guerrilla")) {
            ImGui::MenuItem("Upgrades", nullptr, &g_open);
            ImGui::EndMenu();
        }
        return;
    }
    if (g_open) DrawWindow();
}
