// Guerrilla's backpacks, worn by the player in Guerrilla's world: its jetpack (an upgrade in its campaign)
// and the nine Re-Mars-tered sells in its single player (Thrust, Fleetfoot, Stealth, Vision, Firepower,
// Concussion, Heal, Rhino, Tremor). Which one is worn is the upgrades' (upgrades.cpp: bought in the F7
// window, Wear); their numbers are Guerrilla's mp_backpacks.xtbl, written by the weapons build as
// mods\guerrilla_gear\backpacks.txt.
//
// Guerrilla's rules, kept:
//   - the jetpack (player_update_jetpack, RM 0xA9E430): jump held in the air, once he has left the
//     ground, while it has fuel: the climb gets Jetpack_acceleration (18 m/s²) × (1 falling, else
//     1 − min(climb, 5 m/s) / 5) each second; it burns its fuel_use_per_second (0.3) while it pushes, and
//     refills at Jetpack_charge_rate (0.1 a second, × 2 with the Jetpack Recharge upgrade,
//     upgrade_jetpack_charge_multiplier) while it doesn't (multi_object_backpack::process, RM 0x9F3E7C);
//   - the others are switched on with their own key: with fuel (uses_fuel) one switches on only with its
//     minimum_fuel_to_activate, takes its fuel_activation_cost, burns fuel_use_per_second while on and goes
//     off when it runs dry (or when the key is pressed again, if player_deactivatable), and refills at
//     fuel_regen_per_second while off.
//
// The key: [GuerrillaGear] BackpackKey= in rfa_mods.ini (B unless set): Armageddon has no backpack button,
// and its Nano Forge ability button (nano_use) stays the Nano Forge's.
//
// What each pack does while on, so far (each from its own active_abilities in Guerrilla's table):
//   Jetpack: climbs (above). Thrust: the climb to 10 m/s 180 ms after it goes on; no damage taken.
//   Fleetfoot: runs and sprints × 1.45. Firepower: bullets, melee and the hammer × 1.25, explosions × 1.25.
//   Concussion: Guerrilla's mp_shockwave_blast at his centre 1 s after it goes on. Tremor: mp_quake every
//   300 ms. Heal: 400 hit points a second to everyone within 5.5 m. Rhino: no damage taken.
// Not in yet: Stealth, Vision, Rhino's charge, Thrust's slam down, the packs' models, sounds and effects,
// and their passive values (Guerrilla's MP numbers at the top of each entry).
// The speed and damage factors are the player's own upgrade parameters (below), so walking (as against
// running) isn't faster; damage taken goes through the "Last Gasp" ones, which the pack overrides while on.
#define RFA_MOD_NAME "guerrilla_gear"
#include "rfa_mod.h"
#include "rfa_menu.h"
#include "guerrilla_gear.h"
#include <cmath>
#include <cstdlib>
#include <fstream>
#include <map>
#include <mutex>
#include <sstream>
#include <string>

namespace {

auto const ControlHeld = reinterpret_cast<bool(__cdecl*)(int control, int controller, int unused)>(0x4E4800);
const int* const kLocalController = reinterpret_cast<const int*>(0x11C0580);
constexpr int kJump = 0x14;  // Armageddon's control "jump"
const float* const kFrameTime = reinterpret_cast<const float*>(0x11217C8);

// Guerrilla's jetpack, from its code (Re-Mars-tered: player_update_jetpack's constants).
constexpr float kJetpackAcceleration = 18.0f;  // Jetpack_acceleration (m/s²)
constexpr float kJetpackClimbCap = 5.0f;        // the climb (m/s) the push tapers to nothing at
constexpr float kJetpackUseRate = 0.35f;        // Jetpack_use_rate (its table's jetpack.fuel_use_per_second first)
constexpr float kJetpackChargeRate = 0.1f;      // Jetpack_charge_rate
constexpr float kJetpackRechargeUpgrade = 2.0f;  // Upgrade_jetpack_increased_charge (Jetpack Recharge)
constexpr float kLongestStep = 0.1f;            // seconds: a longer gap (the game paused) counts as this

struct Pack {
    int id = -1;
    std::string name;
    std::map<std::string, std::string> values;  // Guerrilla's fields by their path
    float Number(const char* path, float otherwise) const {
        auto found = values.find(path);
        return found == values.end() ? otherwise : static_cast<float>(std::atof(found->second.c_str()));
    }
    bool Flag(const char* path) const {
        auto found = values.find(path);
        return found != values.end() && _stricmp(found->second.c_str(), "True") == 0;
    }
    bool Has(const char* path) const { return values.count(path) != 0; }
};

std::map<int, Pack> g_packs;
UINT g_key = 0;

// The worn pack's state (the game's thread).
int g_wearing = -1;
float g_fuel = 1.0f;
bool g_on = false;           // an activated pack switched on, or the jetpack pushing
bool g_keyWasDown = false;
LARGE_INTEGER g_lastStep{};

void ReadPacks() {
    std::ifstream list(gear::Folder() + L"backpacks.txt");
    std::string line;
    Pack* pack = nullptr;
    while (std::getline(list, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line[0] == '#') continue;
        const size_t tab = line.find('\t');
        if (tab == std::string::npos) continue;
        const std::string key = line.substr(0, tab), value = line.substr(tab + 1);
        if (key == "pack") {
            const size_t second = value.find('\t');
            const int id = std::atoi(value.c_str());
            pack = &g_packs[id];
            pack->id = id;
            pack->name = second == std::string::npos ? value : value.substr(second + 1);
        } else if (pack) {
            pack->values[key] = value;
        }
    }
}

UINT KeyNamed(const std::wstring& name) {
    if (name.size() >= 2 && (name[0] == L'F' || name[0] == L'f')) {
        const int n = _wtoi(name.c_str() + 1);
        if (n >= 1 && n <= 12) return VK_F1 + n - 1;
    }
    if (name.size() == 1 && iswalnum(name[0])) return towupper(name[0]);
    return 0;
}

// Whether the backpack key went down this frame: the game's window in front, the menus not typing.
bool KeyPressed() {
    bool down = false;
    if (g_key) {
        DWORD process = 0;
        GetWindowThreadProcessId(GetForegroundWindow(), &process);
        down = process == GetCurrentProcessId() && (GetAsyncKeyState(static_cast<int>(g_key)) & 0x8000) != 0 &&
               !rfa::menu::Want(RFA_MENUS_KEYBOARD) && !rfa::menu::Want(RFA_MENUS_SHOWN);
    }
    const bool pressed = down && !g_keyWasDown;
    g_keyWasDown = down;
    return pressed;
}

// This frame's time: the game's (0x11217C8, the one its movement step uses), else the clock's.
float StepSeconds() {
    LARGE_INTEGER now, frequency;
    QueryPerformanceCounter(&now);
    QueryPerformanceFrequency(&frequency);
    float seconds = g_lastStep.QuadPart ? static_cast<float>(now.QuadPart - g_lastStep.QuadPart) / static_cast<float>(frequency.QuadPart) : 0.0f;
    g_lastStep = now;
    const float game = *kFrameTime;
    if (game > 0.0f && game <= kLongestStep) return game;
    return seconds < 0.0f ? 0.0f : seconds > kLongestStep ? kLongestStep : seconds;
}

void Refill(float rate, float seconds) { g_fuel = std::fmin(1.0f, g_fuel + rate * seconds); }

// ---- The jetpack

void Jetpack(BYTE* player, const Pack& pack, float seconds) {
    const bool pushing = ControlHeld(kJump, *kLocalController, 0) && g_fuel > 0.0f && gear::Airborne(player);
    if (pushing) {
        const float use = pack.Number("jetpack.fuel_use_per_second", kJetpackUseRate);
        g_fuel = std::fmax(0.0f, g_fuel - use * seconds);
        const float climb = gear::Climb(player);
        const float taper = climb < 0.0f ? 1.0f : 1.0f - std::fmin(climb, kJetpackClimbCap) / kJetpackClimbCap;
        gear::Lift(player, kJetpackAcceleration * taper * seconds);
    } else {
        const float charge = upgrades::BoughtLevel("jetpack") >= 2 ? kJetpackRechargeUpgrade : 1.0f;
        Refill(kJetpackChargeRate * charge, seconds);
    }
    if (pushing != g_on) {
        g_on = pushing;
        if (!pushing && g_fuel <= 0.0f) rfa::Log("Backpacks: the jetpack ran out of fuel");
    }
}

// ---- What the packs' abilities use of the game's
//
// The player's upgrade parameters: 76 floats at player+0x2C60 (names at 0x1102F90; the game's getter
// 0x8CB580), which the game rebuilds from its defaults and the upgrades owned now and then (0x8CBDD0), not
// every frame. A pack scales or sets one while it is on, from the game's own value, and puts that back when
// it goes off; a value the game has rebuilt meanwhile is taken as its new own.
//   [27] Speed Mult Sprint, [28] Speed Mult Run (0x8BD870: run while human+0x724 is 2, sprint while 3);
//   [38] Bullet, [39] Explosion, [40] Melee, [44] Sledgehammer Damage Mult (applied when the player deals it);
//   [71] Last Gasp health %, [72] Last Gasp damage multiplier (the player's damage taken, 0x85E935: with [71]
//   at 1 or more, every hit's damage × [72]).
constexpr DWORD kUpgradeParameters = 0x2C60;
constexpr int kParameterCount = 76;
constexpr int kSpeedSprint = 27, kSpeedRun = 28, kBulletDamage = 38, kExplosionDamage = 39, kMeleeDamage = 40,
              kHammerDamage = 44, kLastGaspHealth = 71, kLastGaspDamage = 72;

// Health (human+0xF20 max hit points, an int; +0xF24 hit points, a float; +0x1180 what the game's
// regeneration (0x857E10) climbs back to, which must stay at least the hit points). The world's humans:
// 0x43A860 (thiscall: world) how many, 0x43A8B0 (thiscall: world, index) each; a human's position at +4.
constexpr DWORD kMaxHitPoints = 0xF20, kHitPoints = 0xF24, kRegenCeiling = 0x1180, kPosition = 0x4, kOrient = 0x10;
void* const kWorld = reinterpret_cast<void*>(0x1870630);
auto const HumanCount = reinterpret_cast<int(__fastcall*)(void* world, void* unused)>(0x43A860);
auto const HumanAt = reinterpret_cast<BYTE*(__fastcall*)(void* world, void* unused, int index)>(0x43A8B0);

// Explosions: explosions.xtbl's entry by name (0x6EAE30, cdecl; the world build has Guerrilla's), and the
// game's explosion (0x6E6930, cdecl: the entry, its owner and source object, where, its orientation, a
// direction or null, the damage's source or null, a flag, and whether in multiplayer too), as the Nano Forge's
// Impact sets one off at the body's centre (0x84DBB0, thiscall: human, out) with the human's orientation.
struct Vec3 {
    float x, y, z;
};
auto const ExplosionNamed = reinterpret_cast<const void*(__cdecl*)(const char* name)>(0x6EAE30);
auto const MakeExplosion = reinterpret_cast<bool(__cdecl*)(const void* info, BYTE* owner, BYTE* source, const Vec3* where,
                                                          const float* orient, const Vec3* direction, const void* damageSource,
                                                          char flag, bool multiplayer)>(0x6E6930);
auto const BodyCentre = reinterpret_cast<void(__fastcall*)(BYTE* human, void* unused, Vec3* out)>(0x84DBB0);

// The parameters a pack holds now, each with the game's own value.
struct Held {
    bool held = false;
    float own = 0.0f;      // the game's value
    float written = 0.0f;  // ours, as written
};
Held g_held[kParameterCount];
BYTE* g_heldPlayer = nullptr;

float* Parameters(BYTE* player) { return reinterpret_cast<float*>(player + kUpgradeParameters); }

// This frame's wishes: index → (scale, or set) the game's own value.
struct Wish {
    int index;
    bool set;
    float value;
};

// Writes this frame's wishes and puts back the parameters no longer wished for.
void HoldParameters(BYTE* player, const Wish* wishes, int count) {
    if (player != g_heldPlayer) {  // another player (a level loaded): what was held went with the old one
        for (Held& h : g_held) h = Held{};
        g_heldPlayer = player;
    }
    float* p = Parameters(player);
    bool wished[kParameterCount] = {};
    for (int i = 0; i < count; ++i) {
        const Wish& w = wishes[i];
        Held& h = g_held[w.index];
        if (!h.held || p[w.index] != h.written) h.own = p[w.index];  // first time, or the game rebuilt it
        h.held = true;
        h.written = w.set ? w.value : h.own * w.value;
        p[w.index] = h.written;
        wished[w.index] = true;
    }
    for (int i = 0; i < kParameterCount; ++i)
        if (g_held[i].held && !wished[i]) {
            if (p[i] == g_held[i].written) p[i] = g_held[i].own;
            g_held[i] = Held{};
        }
}

// ---- The packs' abilities while on

ULONGLONG g_onSince = 0;     // when the worn pack was switched on (GetTickCount64)
bool g_boosted = false;      // Thrust: its boost given since switched on
ULONGLONG g_nextKaboom = 0;  // Concussion, Tremor: when the next explosion goes off (0: none)

// Sets off one of Guerrilla's explosions (the world build's explosions.xtbl) at the player's centre.
void Kaboom(BYTE* player, const std::string& name) {
    const void* info = name.empty() ? nullptr : ExplosionNamed(name.c_str());
    if (!info) {
        rfa::Log("Backpacks: no explosion \"%s\" in explosions.xtbl (build the world again)", name.c_str());
        return;
    }
    Vec3 centre{};
    BodyCentre(player, nullptr, &centre);
    MakeExplosion(info, player, player, &centre, reinterpret_cast<const float*>(player + kOrient), nullptr, nullptr, 0, false);
}

// Heal (Unique_ID 7): health_regen_per_second (400) to everyone within health_regen_radius (5.5 m), enemies
// too while on (health_regen_heal_enemies), the player included; as the game's own regeneration, the hit
// points and what regeneration climbs back to.
void Heal(BYTE* player, const Pack& pack, float seconds) {
    const float rate = pack.Number("activatable.active_abilities.health_regen.health_regen_per_second", 400.0f);
    const float radius = pack.Number("activatable.active_abilities.health_regen.health_regen_radius", 5.5f);
    const Vec3 at = *reinterpret_cast<const Vec3*>(player + kPosition);
    const int count = HumanCount(kWorld, nullptr);
    for (int i = 0; i < count; ++i) {
        BYTE* human = HumanAt(kWorld, nullptr, i);
        if (!human) continue;
        const Vec3 pos = *reinterpret_cast<const Vec3*>(human + kPosition);
        const float dx = pos.x - at.x, dy = pos.y - at.y, dz = pos.z - at.z;
        if (dx * dx + dy * dy + dz * dz > radius * radius) continue;
        float& hp = *reinterpret_cast<float*>(human + kHitPoints);
        const float max = static_cast<float>(*reinterpret_cast<const int*>(human + kMaxHitPoints));
        if (hp <= 0.0f || hp >= max) continue;  // the dead stay dead
        hp = std::fmin(max, hp + rate * seconds);
        float& ceiling = *reinterpret_cast<float*>(human + kRegenCeiling);
        if (ceiling < hp) ceiling = hp;
    }
}

// Thrust (Unique_ID 1): Guerrilla's "jump boost": jump_boost_delay_ms (180) after it goes on, the climb
// becomes jump_boost_vertical (10 m/s) if it is less (multi_object_backpack's jump boost; its slam back down
// on landing, downthrust, isn't in yet). From the ground the step may hold him down unless he jumps too.
void Thrust(BYTE* player, const Pack& pack, ULONGLONG now) {
    if (g_boosted) return;
    const float delay = pack.Number("activatable.active_abilities.jump_boost.jump_boost_delay_ms", 180.0f);
    if (now - g_onSince < static_cast<ULONGLONG>(delay)) return;
    g_boosted = true;
    const float boost = pack.Number("activatable.active_abilities.jump_boost.jump_boost_vertical", 10.0f);
    const float climb = gear::Climb(player);
    if (climb < boost) gear::Lift(player, boost - climb);
    rfa::Log("Backpacks: Thrust boost, climb %.1f -> %.1f m/s (%s)", climb, boost, gear::Airborne(player) ? "in the air" : "from the ground");
}

// Concussion (6, Guerrilla's Shockwave) and Tremor (9): kaboom_explosion_name kaboom_delay_ms after it goes
// on (Concussion: mp_shockwave_blast after 1 s, then it is spent: its fuel_activation_cost is all of it), and
// for Tremor again every kaboom_delay_ms (300) while on (mp_quake). Guerrilla keeps its own out of the blast
// (kaboom_damages_user False); here the game's explosion decides.
bool Kabooms(BYTE* player, const Pack& pack, ULONGLONG now) {
    if (!g_nextKaboom || now < g_nextKaboom) return true;
    Kaboom(player, pack.values.count("activatable.active_abilities.kaboom.kaboom_explosion_name")
                       ? pack.values.at("activatable.active_abilities.kaboom.kaboom_explosion_name") : std::string());
    if (pack.id == 9) {
        g_nextKaboom = now + static_cast<ULONGLONG>(pack.Number("activatable.active_abilities.kaboom.kaboom_delay_ms", 300.0f));
        return true;
    }
    g_nextKaboom = 0;
    return false;  // Concussion: spent
}

// A pack's turn while on; false when it is spent.
bool WhileOn(BYTE* player, const Pack& pack, float seconds) {
    const ULONGLONG now = GetTickCount64();
    switch (pack.id) {
        case 1: Thrust(player, pack, now); return true;
        case 6:
        case 9: return Kabooms(player, pack, now);
        case 7: Heal(player, pack, seconds); return true;
        default: return true;  // Stealth, Vision, Rhino's charge: not in yet
    }
}

// The upgrade parameters a pack scales while on, from its active_abilities: movement_speed_factor (run and
// sprint), direct_damage_dealt_factor (bullets, melee, the hammer), area_damage_dealt_factor (explosions),
// damage_taken_factor (Last Gasp: every hit × it).
int Wishes(const Pack& pack, Wish* out) {
    int n = 0;
    auto factor = [&](const char* field) { return pack.Number((std::string("activatable.active_abilities.") + field).c_str(), 1.0f); };
    if (const float speed = factor("movement_speed_factor"); speed != 1.0f) {
        out[n++] = {kSpeedRun, false, speed};
        out[n++] = {kSpeedSprint, false, speed};
    }
    if (const float direct = factor("direct_damage_dealt_factor"); direct != 1.0f) {
        out[n++] = {kBulletDamage, false, direct};
        out[n++] = {kMeleeDamage, false, direct};
        out[n++] = {kHammerDamage, false, direct};
    }
    if (const float area = factor("area_damage_dealt_factor"); area != 1.0f) out[n++] = {kExplosionDamage, false, area};
    if (const float taken = factor("damage_taken_factor"); taken < 1.0f) {
        out[n++] = {kLastGaspHealth, true, 1.0f};
        out[n++] = {kLastGaspDamage, true, taken};
    }
    return n;
}

// ---- The packs switched on with their key

void Activated(BYTE* player, const Pack& pack, float seconds) {
    const bool fuel = pack.Has("activatable.uses_fuel.fuel_use_per_second") || pack.Has("activatable.uses_fuel.minimum_fuel_to_activate");
    const float use = pack.Number("activatable.uses_fuel.fuel_use_per_second", 0.0f);
    const float regen = pack.Number("activatable.uses_fuel.fuel_regen_per_second", 0.0f);
    const float cost = pack.Number("activatable.uses_fuel.fuel_activation_cost", 0.0f);
    const float minimum = pack.Number("activatable.uses_fuel.minimum_fuel_to_activate", 0.0f);
    const bool playerOff = pack.Flag("activatable.deactivatable.player_deactivatable");
    if (KeyPressed()) {
        if (!g_on) {
            if (!fuel || g_fuel + 0.0001f >= minimum) {
                g_on = true;
                g_onSince = GetTickCount64();
                g_boosted = false;
                g_nextKaboom = pack.id == 6 || pack.id == 9
                    ? g_onSince + static_cast<ULONGLONG>(pack.Number("activatable.active_abilities.kaboom.kaboom_delay_ms", 300.0f)) : 0;
                g_fuel = std::fmax(0.0f, g_fuel - cost);
                rfa::Log("Backpacks: %s on (fuel %.2f)%s", pack.name.c_str(), g_fuel,
                         pack.id == 3 || pack.id == 4 ? "; its ability isn't in yet" : pack.id == 8 ? "; no damage taken (its charge isn't in yet)" : "");
            } else {
                rfa::Log("Backpacks: %s needs %.2f fuel to switch on (has %.2f)", pack.name.c_str(), minimum, g_fuel);
            }
        } else if (playerOff) {
            g_on = false;
            rfa::Log("Backpacks: %s off (fuel %.2f)", pack.name.c_str(), g_fuel);
        }
    }
    if (g_on) {
        if (!WhileOn(player, pack, seconds)) {
            g_on = false;
            rfa::Log("Backpacks: %s spent (fuel %.2f)", pack.name.c_str(), g_fuel);
            return;
        }
        g_fuel = std::fmax(0.0f, g_fuel - use * seconds);
        if (fuel && g_fuel <= 0.0f) {
            g_on = false;
            rfa::Log("Backpacks: %s ran dry", pack.name.c_str());
        }
    } else {
        Refill(regen, seconds);
    }
}

}  // namespace

void backpacks::Init() {
    ReadPacks();
    const std::wstring key = rfa::IniString(L"GuerrillaGear", L"BackpackKey", L"B");
    g_key = KeyNamed(key);
    if (g_packs.empty())
        rfa::Log("Backpacks: no mods\\guerrilla_gear\\backpacks.txt (build Guerrilla's weapons with GuerrillaBuilder.exe); none can be worn");
    else
        rfa::Log("Backpacks: %zu of Guerrilla's; the jetpack climbs with jump held in the air, the others switch on with %ls", g_packs.size(),
                 g_key ? key.c_str() : L"(no key: [GuerrillaGear] BackpackKey isn't a key)");
}

void backpacks::Step(BYTE* player) {
    const float seconds = StepSeconds();
    const int worn = upgrades::WornBackpack();
    if (worn != g_wearing) {  // a pack put on starts full, as Guerrilla's picked up
        g_wearing = worn;
        g_fuel = 1.0f;
        g_on = false;
        auto found = g_packs.find(worn);
        if (worn >= 0) rfa::Log("Backpacks: wearing %s", found != g_packs.end() ? found->second.name.c_str() : "an unknown pack");
    }
    auto found = g_packs.find(worn);
    if (found == g_packs.end()) {
        KeyPressed();  // (keeps the key's last state)
        HoldParameters(player, nullptr, 0);
        return;
    }
    if (worn == 0) Jetpack(player, found->second, seconds);
    else Activated(player, found->second, seconds);
    // The upgrade parameters the pack scales while it is on (put back the moment it isn't).
    Wish wishes[16];
    const int count = g_on && worn != 0 ? Wishes(found->second, wishes) : 0;
    HoldParameters(player, wishes, count);
}

float backpacks::Fuel() { return g_fuel; }
bool backpacks::On() { return g_on; }
