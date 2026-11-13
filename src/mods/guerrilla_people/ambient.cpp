// Guerrilla's walkers in its world: its ambient people spawner (amb_spawn's do_frame_spawn_npc, RM 0xB5F640) and
// their wander (pedestrian_wander_action: civilian_ped_do_action_common, RM 0x690960; civilian_pick_next_ped_node,
// build_new_waypoints_normal), which Armageddon deleted, ported onto the code Armageddon kept. A part of
// guerrilla_people.dll. GuerrillaBuilder.exe writes what it needs: mods\guerrilla_people\ambient.txt (each zone's
// walkers by day and by night, its spawn resource) and crews.txt (each resource's civilians), and the world part
// places Guerrilla's pedestrian navpoints (type 2) with their links.
//
// Spawning, as Guerrilla's: every 250 to 500 ms (500 to 1000 ms with 75 people or more in the world), in our
// worlds, where the player's zone has walkers, never with 90 people or more in the world: up to Guerrilla's cap
// (max_ped_count 0xB41180, its table 0x1335CF8 by alert level and density: green none 0, low 10, medium 20, high
// 30; yellow 0/8/10/20; orange 0/6/8/10; red 0/6/6/8; the night's density from 18:00 to 6:00, as
// game_clock_get_time_of_day has it), one at a time, on a pedestrian navpoint 20 to 100 m from the player
// (Player_spawn_region's 20 m, PED_NODE_COLLECTION_RADIUS) out of view, no more than a quarter of the cap toward
// one side of the player (max_ped_count_quad), facing along one of its links, as one of the zone's resource's
// civilians the game has streamed in (made with drop_human's create, 0x762D50). From the cap less 2 the farthest
// walker 20 m or more away starts leaving (select_despawning_npc, npc::start_active_despawn), one at a time, so
// the crowd changes. The alert level is guerrilla_campaign's (GuerrillaCampaign_Alert); green without it.
// Not ported yet: build_spawn_node_list weighs the nodes by the network's costs (here one is picked at random
// among those allowed), and the camera's own direction (pick_spawn_direction; here the player's sides).
//
// Walking, as Guerrilla's: each frame the walker's AI is asked to go to its next navpoint walking (Guerrilla's
// ai_system::pathfind_to, Armageddon's 0x431C20, as civilian_ped_do_action_common asks: walk, standing, low
// priority, no cell given), its AI otherwise sitting the frame out (the planner, perception and firing skip it,
// as for the traffic's drivers). Within 1.25 m of it (across the ground) the next is chosen: one of its links
// not among the last 8 it walked to, one ahead of it first (Guerrilla's directional choice), at random among
// those; a dead end turns it back. The walkers are NPC_MANAGER_AMBIENT (npc +0x1700 = 1), as Guerrilla's. Not
// ported yet: Guerrilla's pauses and animations at nodes (pedestrian_pause_action, pedestrian_anim_action) and its
// offset of the walk across the path. One that gets a flee belief (shots, explosions, damage near it) is given
// back to the game's AI until Guerrilla's panic (pedestrian_panicked_goal) is ported.
//
// Despawning, as Guerrilla's should_despawn_npc (0xB47700): 20 m or more away, 10 s after it was made, out of
// view for 5 s, and 20 s after it was made or beyond 120 m (MAX_PED_SPAWN_DISTANCE); a leaving one
// (can_delete_despawning_npc) once it is 20 m away and out of view for 5 s, or beyond 120 m.
#define RFA_MOD_NAME "guerrilla_people"
#include "rfa_mod.h"
#include "ambient.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace {

// ---- The game's (Armageddon's publicbeta build)
// An object: +0x04 its position, +0x10 its orientation (right, up, forward), +0x68 its handle. An NPC: +0x1700 who
// manages it (1 ambient), its ai_system at +0x174C (+0x651 bit 2: yield_next_frame). A navpoint: +0xF4 its type (a
// byte: 2 pedestrian), its links from +0x9C (navpoints), how many a short at +0xE8.
constexpr DWORD kPosition = 0x04, kOrientation = 0x10, kHandle = 0x68, kManagement = 0x1700, kNpcAi = 0x174C, kYield = 0x651;
constexpr DWORD kObjectType = 0x7A, kNavpointType = 0xF4, kNavpointLinks = 0x9C, kNavpointLinkCount = 0xE8;
constexpr int kMostLinks = (kNavpointLinkCount - kNavpointLinks) / 4;
constexpr int kNavpointObject = 10;  // an object's type byte for a navpoint (the world's navpoint list, 0x43AC80 → 0x436380)
constexpr int kPedestrianNavpoint = 2, kAmbient = 1;
constexpr DWORD kNone = 0xFFFFFFFF;
void* const kWorld = reinterpret_cast<void*>(0x1870630);

using WorldCountFn = int(__fastcall*)(void* world, void*);
using WorldAtFn = BYTE*(__fastcall*)(void* world, void*, int index);
using ByHandleFn = BYTE*(__fastcall*)(void* world, void*, DWORD handle);
auto const Player = reinterpret_cast<BYTE*(__cdecl*)()>(0x8B92D0);
auto const NpcCount = reinterpret_cast<WorldCountFn>(0x424480);       // world::get_num_objects<npc>
auto const NavpointCount = reinterpret_cast<WorldCountFn>(0x43AC80);  // world::get_num_objects<navpoint>
auto const NavpointAt = reinterpret_cast<WorldAtFn>(0x43ACD0);
// world::object_from_handle, any object (0x424350, which the first build used, answers only objects of type 2,
// subtype 1: never a navpoint, so every walker's next navpoint was "gone" and it was sent to the one it stood on).
auto const ObjectByHandle = reinterpret_cast<ByHandleFn>(0x6CB9E0);
auto const HumanByHandle = reinterpret_cast<ByHandleFn>(0x436120);
auto const GoneOrDead = reinterpret_cast<bool(__cdecl*)(BYTE* object)>(0x42ADC0);  // true for null or a dead one
auto const RemoveObject = reinterpret_cast<int(__fastcall*)(void* world, void*, DWORD handle, int flag)>(0x6D56A0);
auto const HumanInfo = reinterpret_cast<BYTE*(__cdecl*)(const char* name)>(0x88D170);
auto const MakeHuman = reinterpret_cast<BYTE*(__cdecl*)(BYTE* info, const float* where, const float* orientation, void* variant)>(0x762D50);
auto const InView = reinterpret_cast<bool(__cdecl*)(const float* centre, float radius)>(0x768400);
auto const FleeToSafetyBelief = reinterpret_cast<BYTE*(__cdecl*)(BYTE* npc)>(0x483C90);
auto const EnemyFleeBelief = reinterpret_cast<BYTE*(__cdecl*)(BYTE* npc)>(0x483B00);
// ai_system::pathfind_to (thiscall on the AI; ret 1Ch): the place, how it moves (1 walk), its stance (0 standing),
// the priority (0 low), the destination's nav cell (a 64-bit handle; all ones for none), a flag. It answers
// Guerrilla's ai_pathfind_results: 0 on its way, 1 not ready yet, 2 ragdolled, 3 an animation in the way, 4 in a
// vehicle, 5 danger, 6 no path, 7 its start isn't on the mesh, 8 the destination isn't.
using GoToFn = int(__fastcall*)(BYTE* ai, void*, const float* place, int movement, int stance, int priority, DWORD cellLow, DWORD cellHigh, bool flag);
auto const GoTo = reinterpret_cast<GoToFn>(0x431C20);
constexpr int kWalk = 1, kStanding = 0, kLowPriority = 0;
constexpr int kNoPath = 6, kStartOffMesh = 7, kDestinationOffMesh = 8;
int GameTime() { return *reinterpret_cast<const int*>(0x1121300); }   // ms; it stops while the game is paused
int GameHour() { return *reinterpret_cast<const BYTE*>(0x12F76D4); }

// ---- Guerrilla's numbers
constexpr int kCaps[4][4] = {{0, 10, 20, 30}, {0, 8, 10, 20}, {0, 6, 8, 10}, {0, 6, 6, 8}};  // [alert][density], 0x1335CF8
constexpr int kMostPeople = 90, kBusyPeople = 75;  // no spawn from 90 people in the world; spawning slows from 75
constexpr float kNearest = 20.0f, kFarthestNode = 100.0f, kFarthest = 120.0f, kReached = 1.25f, kApart = 3.0f;
constexpr int kOldWaypoints = 8;
constexpr int kYoung = 10000, kOld = 20000, kUnseen = 5000;  // ms

struct V3 { float x, y, z; };
V3 Pos(const BYTE* object) { return *reinterpret_cast<const V3*>(object + kPosition); }
DWORD Handle(const BYTE* object) { return *reinterpret_cast<const DWORD*>(object + kHandle); }
float Distance(const V3& a, const V3& b) { const float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z; return std::sqrt(dx * dx + dy * dy + dz * dz); }
float FlatDistance(const V3& a, const V3& b) { const float dx = a.x - b.x, dz = a.z - b.z; return std::sqrt(dx * dx + dz * dz); }
float Random() { return static_cast<float>(std::rand()) / RAND_MAX; }
int RandomBelow(int count) { return count <= 1 ? 0 : static_cast<int>(std::rand() / (RAND_MAX + 1.0) * count); }

// ---- What GuerrillaBuilder.exe wrote
struct Zone {
    int col, row, day, night;  // densities 0 none .. 3 high
    std::string resource, entry;
};
struct Files {
    float zoneWidth = 511.0f;
    int columnAtZero = 8, rowAtZero = 7;
    std::vector<Zone> zones;
    std::vector<std::pair<std::string, std::vector<std::string>>> civilians;  // resource → its civilians (crews.txt's "Guerilla")
};
Files g_files;
bool g_on = false;

std::vector<std::string> Split(const std::string& text, char by) {
    std::vector<std::string> parts;
    std::string part;
    std::istringstream in(text);
    while (std::getline(in, part, by))
        if (!part.empty()) parts.push_back(part);
    return parts;
}

void ReadFiles() {
    const std::wstring folder = rfa::RootDir() + L"\\mods\\guerrilla_people\\";
    std::ifstream ambient(folder + L"ambient.txt");
    for (std::string line; std::getline(ambient, line);) {
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        std::istringstream in(line);
        std::string kind;
        in >> kind;
        if (kind == "grid") in >> g_files.zoneWidth >> g_files.columnAtZero >> g_files.rowAtZero;
        else if (kind == "zone") {
            Zone zone{};
            if (in >> zone.col >> zone.row >> zone.day >> zone.night >> zone.resource) {
                in >> zone.entry;
                zone.day = std::clamp(zone.day, 0, 3);
                zone.night = std::clamp(zone.night, 0, 3);
                g_files.zones.push_back(zone);
            }
        }
    }
    std::ifstream crews(folder + L"crews.txt");
    for (std::string line; std::getline(crews, line);) {
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        if (line.empty() || line[0] == '#') continue;
        const auto fields = Split(line, '\t');
        if (fields.size() == 3 && _stricmp(fields[1].c_str(), "Guerilla") == 0) g_files.civilians.push_back({fields[0], Split(fields[2], ',')});
    }
}

const Zone* ZoneAt(const V3& where) {
    const int col = static_cast<int>(std::floor(where.x / g_files.zoneWidth)) + g_files.columnAtZero;
    const int row = g_files.rowAtZero - static_cast<int>(std::floor(where.z / g_files.zoneWidth));
    for (const Zone& zone : g_files.zones)
        if (zone.col == col && zone.row == row) return &zone;
    return nullptr;
}

const std::vector<std::string>* CiviliansOf(const std::string& resource) {
    const std::vector<std::string>* fallback = nullptr;
    for (const auto& [name, people] : g_files.civilians) {
        if (people.empty()) continue;
        if (_stricmp(name.c_str(), resource.c_str()) == 0) return &people;
        if (_stricmp(name.c_str(), "rfg_Default") == 0) fallback = &people;
    }
    return fallback;
}

// Guerrilla's alert level from guerrilla_campaign (0 green .. 3 red); green if it isn't there or not playing.
int AlertLevel() {
    using AlertFn = int(__cdecl*)(float* meter);
    static AlertFn alert = nullptr;
    static bool looked = false;
    if (!looked) {
        looked = true;
        if (HMODULE campaign = GetModuleHandleW(L"guerrilla_campaign.dll")) alert = reinterpret_cast<AlertFn>(GetProcAddress(campaign, "GuerrillaCampaign_Alert"));
        rfa::Log(alert ? "Walkers: the alert level is guerrilla_campaign's" : "Walkers: guerrilla_campaign's alert level isn't there; the walkers' cap is green's");
    }
    const int level = alert ? alert(nullptr) : 0;
    return level >= 0 && level <= 3 ? level : 0;
}

// ---- Navpoints
BYTE* Navpoint(DWORD handle) {
    if (handle == kNone) return nullptr;
    BYTE* object = ObjectByHandle(kWorld, nullptr, handle);
    return object && object[kObjectType] == kNavpointObject && static_cast<signed char>(object[kNavpointType]) == kPedestrianNavpoint ? object : nullptr;
}

// A pedestrian navpoint's links to other pedestrian navpoints.
std::vector<BYTE*> Links(const BYTE* navpoint) {
    std::vector<BYTE*> links;
    const int count = std::clamp(static_cast<int>(*reinterpret_cast<const short*>(navpoint + kNavpointLinkCount)), 0, kMostLinks);
    BYTE* const* list = reinterpret_cast<BYTE* const*>(navpoint + kNavpointLinks);
    for (int i = 0; i < count; ++i)
        if (list[i] && static_cast<signed char>(list[i][kNavpointType]) == kPedestrianNavpoint) links.push_back(list[i]);
    return links;
}

// The orientation for a forward vector on the ground (right, up, forward).
void Facing(V3 forward, float* m) {
    forward.y = 0;
    const float length = std::sqrt(forward.x * forward.x + forward.z * forward.z);
    if (length < 0.001f) forward = {0, 0, 1};
    else { forward.x /= length; forward.z /= length; }
    const float values[9] = {forward.z, 0, -forward.x, 0, 1, 0, forward.x, 0, forward.z};
    memcpy(m, values, sizeof(values));
}

// ---- The walkers
struct Walker {
    DWORD human = kNone;
    DWORD target = kNone, from = kNone;  // the navpoint it walks to, and the one it came from
    std::vector<DWORD> old;               // the last navpoints it walked to (ai_pedestrian_info's old_waypoints)
    V3 goal{};
    int made = 0, outOfViewSince = -1;
    bool leaving = false;                 // started to despawn
    bool walked = true;                   // the mod walks it (false once given back to the game's AI)
    int management = 0;                   // who managed it before
    V3 lastPlace{};
    int lastMoveCheck = 0, stuck = 0;
};
std::vector<Walker> g_walkers;
int g_nextSpawn = 0, g_nextReport = 0, g_lastClock = 0;
// For the reports.
int g_made = 0, g_despawned = 0, g_died = 0, g_fled = 0, g_reachedNodes = 0, g_goToFailures = 0, g_notStreamed = 0, g_noNode = 0;
int g_results[9] = {};
// The cap last worked out, for the reports: how many may walk here now and why.
int g_cap = -1;
char g_capWhy[160] = "";

bool IsOld(const Walker& w, DWORD navpoint) { return std::find(w.old.begin(), w.old.end(), navpoint) != w.old.end(); }
void Remember(Walker& w, DWORD navpoint) {
    w.old.push_back(navpoint);
    if (static_cast<int>(w.old.size()) > kOldWaypoints) w.old.erase(w.old.begin());
}

// The next navpoint from the one reached: Guerrilla's normal choice, as read (build_new_waypoints_normal and
// _directional): a link not walked to lately, ahead of the walker first, at random; a dead end back the way it came.
BYTE* NextNavpoint(const Walker& w, const BYTE* reached, const V3& heading) {
    const auto links = Links(reached);
    if (links.empty()) return nullptr;
    const V3 at = Pos(reached);
    std::vector<BYTE*> ahead, fresh;
    for (BYTE* link : links) {
        if (IsOld(w, Handle(link))) continue;
        fresh.push_back(link);
        const V3 to = Pos(link);
        if ((to.x - at.x) * heading.x + (to.z - at.z) * heading.z > 0) ahead.push_back(link);
    }
    if (!ahead.empty()) return ahead[RandomBelow(static_cast<int>(ahead.size()))];
    if (!fresh.empty()) return fresh[RandomBelow(static_cast<int>(fresh.size()))];
    return links[RandomBelow(static_cast<int>(links.size()))];
}

// The pedestrian navpoint nearest a place within 30 m (PED_NODE_SEARCH_AREA_RADIUS), as civilian_pick_new_ped_node
// falls back to; null if none.
BYTE* NearestNavpoint(const V3& place, DWORD except) {
    BYTE* best = nullptr;
    float bestDistance = 30.0f;
    const int count = NavpointCount(kWorld, nullptr);
    for (int i = 0; i < count; ++i) {
        BYTE* navpoint = NavpointAt(kWorld, nullptr, i);
        if (!navpoint || static_cast<signed char>(navpoint[kNavpointType]) != kPedestrianNavpoint || Handle(navpoint) == except) continue;
        const float d = Distance(Pos(navpoint), place);
        if (d < bestDistance) { bestDistance = d; best = navpoint; }
    }
    return best;
}

void Head(Walker& w, BYTE* navpoint) {
    w.target = navpoint ? Handle(navpoint) : kNone;
    if (navpoint) w.goal = Pos(navpoint);
}

// Gives a walker back to the game's AI (its manager as before).
void GiveBack(Walker& w, BYTE* human) {
    w.walked = false;
    *reinterpret_cast<int*>(human + kManagement) = w.management;
}

void SitOut(BYTE* human) {
    BYTE* ai = *reinterpret_cast<BYTE**>(human + kNpcAi);
    if (ai) ai[kYield] |= 2;
}

// One frame of a walker's walk.
void Walk(Walker& w, BYTE* human, int now) {
    BYTE* ai = *reinterpret_cast<BYTE**>(human + kNpcAi);
    if (!ai) return;
    if (FleeToSafetyBelief(human) || EnemyFleeBelief(human)) {
        GiveBack(w, human);
        ++g_fled;
        RFA_LOG_FIRST(20, "Walkers: NPC 0x%08X saw danger; it's the game's AI's now (Guerrilla's panic isn't ported yet)", w.human);
        return;
    }
    SitOut(human);
    if (w.target == kNone && w.leaving) return;  // nowhere to walk: it stands until it's out of view
    const V3 at = Pos(human);
    BYTE* target = Navpoint(w.target);
    if (!target || FlatDistance(at, w.goal) < kReached) {
        BYTE* reached = target;
        V3 heading{0, 0, 0};
        if (BYTE* from = Navpoint(w.from)) { const V3 f = Pos(from); heading = {w.goal.x - f.x, 0, w.goal.z - f.z}; }
        if (reached) {
            ++g_reachedNodes;
            Remember(w, w.target);
            w.from = w.target;
        }
        BYTE* next = reached ? NextNavpoint(w, reached, heading) : NearestNavpoint(at, kNone);
        Head(w, next);
        w.stuck = 0;
        if (!next) {  // nowhere to go: it stands, and goes once out of view
            w.leaving = true;
            RFA_LOG_FIRST(10, "Walkers: NPC 0x%08X has no pedestrian navpoint to walk to at (%.1f, %.1f, %.1f); it leaves once out of view", w.human, at.x, at.y, at.z);
            return;
        }
    }
    const int result = GoTo(ai, nullptr, &w.goal.x, kWalk, kStanding, kLowPriority, kNone, kNone, false);
    if (result >= 0 && result < 9) {
        if (g_results[result]++ == 0)
            rfa::Log("Walkers: the AI's go-to first answered %d for NPC 0x%08X, to navpoint 0x%08X at (%.1f, %.1f, %.1f) from (%.1f, %.1f, %.1f)", result, w.human,
                     w.target, w.goal.x, w.goal.y, w.goal.z, at.x, at.y, at.z);
    }
    if (result == kNoPath || result == kStartOffMesh || result == kDestinationOffMesh) {
        ++g_goToFailures;
        Remember(w, w.target);  // not that one again soon
        BYTE* from = Navpoint(w.from);
        Head(w, from ? NextNavpoint(w, from, {0, 0, 0}) : NearestNavpoint(at, w.target));
        return;
    }
    // Stuck: not 0.5 m on in 4 s while on its way, then another way (three times, then it leaves).
    if (now - w.lastMoveCheck >= 4000) {
        if (Distance(at, w.lastPlace) < 0.5f && result == 0) {
            ++w.stuck;
            Remember(w, w.target);
            BYTE* from = Navpoint(w.from);
            Head(w, from ? NextNavpoint(w, from, {0, 0, 0}) : NearestNavpoint(at, w.target));
            if (w.stuck >= 3 && !w.leaving) {
                w.leaving = true;
                RFA_LOG_FIRST(20, "Walkers: NPC 0x%08X got stuck three times near (%.1f, %.1f, %.1f); it leaves once out of view", w.human, at.x, at.y, at.z);
            }
        }
        w.lastPlace = at;
        w.lastMoveCheck = now;
    }
}

// Guerrilla's should_despawn_npc, and can_delete_despawning_npc for a leaving one.
bool ShouldGo(const Walker& w, float away, int now) {
    const int unseen = w.outOfViewSince < 0 ? 0 : now - w.outOfViewSince;
    if (w.leaving) return (away > kNearest && unseen >= kUnseen) || away > kFarthest;
    if (away <= kNearest) return false;
    const int age = now - w.made;
    if (age < kYoung || unseen < kUnseen) return false;
    return age >= kOld || away > kFarthest;
}

// Which quarter round the player a place is in (the player's front, right, back, left).
int Side(const V3& place, const V3& player, const V3& forward) {
    const float dx = place.x - player.x, dz = place.z - player.z;
    const float ahead = dx * forward.x + dz * forward.z, right = dx * forward.z - dz * forward.x;
    if (std::fabs(ahead) >= std::fabs(right)) return ahead >= 0 ? 0 : 2;
    return right >= 0 ? 1 : 3;
}

// Guerrilla's spawn step: at the cap less 2 one starts leaving; under the cap one more.
void SpawnStep(const V3& at, const V3& forward, int now) {
    static const char* const kDensity[4] = {"none", "low", "medium", "high"};
    static const char* const kAlert[4] = {"green", "yellow", "orange", "red"};
    const Zone* zone = ZoneAt(at);
    const int hour = GameHour();
    const bool night = hour >= 18 || hour < 6;
    const int density = zone ? (night ? zone->night : zone->day) : 0;
    const int alert = AlertLevel();
    const int cap = kCaps[alert][density];
    // A line whenever the cap changes (another zone, day and night, the alert level).
    char why[160];
    if (zone)
        sprintf_s(why, "zone %02d_%02d (%s): %s by %s, %d:00, alert %s", zone->col, zone->row, zone->entry.c_str(), kDensity[density], night ? "night" : "day", hour,
                  kAlert[alert]);
    else
        sprintf_s(why, "no walkers in this zone (its ambient_spawn has none), %d:00", hour);
    static const Zone* lastZone = nullptr;
    static int lastKey = -1;
    const int key = density * 100 + alert * 10 + (night ? 1 : 0);
    if (cap != g_cap || zone != lastZone || key != lastKey)
        RFA_LOG_FIRST(60, "Walkers: the cap is now %d (%s); %d out", cap, why, static_cast<int>(g_walkers.size()));
    lastZone = zone;
    lastKey = key;
    g_cap = cap;
    strcpy_s(g_capWhy, why);
    if (!zone) return;  // no ambient_spawn with walkers here: Guerrilla's spawner does nothing either
    const int count = static_cast<int>(g_walkers.size());
    // The crowd changes: from the cap less 2, the farthest one 20 m or more away starts leaving (one at a time).
    const int nearCap = (std::max)(cap - 2, 0);
    if (nearCap <= count && std::none_of(g_walkers.begin(), g_walkers.end(), [](const Walker& w) { return w.leaving; })) {
        Walker* farthest = nullptr;
        float farthestAway = kNearest;
        for (Walker& w : g_walkers)
            if (BYTE* human = HumanByHandle(kWorld, nullptr, w.human)) {
                const float away = Distance(Pos(human), at);
                if (away > farthestAway) { farthestAway = away; farthest = &w; }
            }
        if (farthest) farthest->leaving = true;
    }
    if (count >= cap) return;
    // A quarter of the cap at most toward one side (max_ped_count_quad: a quarter, rounded up, at least 1).
    const int quarter = (std::max)(1, (cap + 3) / 4);
    int bySide[4] = {};
    std::vector<V3> places;
    for (const Walker& w : g_walkers)
        if (BYTE* human = HumanByHandle(kWorld, nullptr, w.human)) {
            places.push_back(Pos(human));
            ++bySide[Side(places.back(), at, forward)];
        }
    // The navpoints allowed: pedestrian ones 20 to 100 m away, out of view, none of the walkers within 3 m.
    std::vector<BYTE*> nodes;
    const int navpoints = NavpointCount(kWorld, nullptr);
    for (int i = 0; i < navpoints; ++i) {
        BYTE* navpoint = NavpointAt(kWorld, nullptr, i);
        if (!navpoint || static_cast<signed char>(navpoint[kNavpointType]) != kPedestrianNavpoint) continue;
        const V3 place = Pos(navpoint);
        const float away = Distance(place, at);
        if (away < kNearest || away > kFarthestNode || bySide[Side(place, at, forward)] >= quarter) continue;
        if (std::any_of(places.begin(), places.end(), [&](const V3& p) { return Distance(p, place) < kApart; })) continue;
        const V3 head{place.x, place.y + 1.0f, place.z};
        if (InView(&head.x, 1.0f)) continue;
        nodes.push_back(navpoint);
    }
    if (nodes.empty()) {
        ++g_noNode;
        RFA_LOG_FIRST(5, "Walkers: no pedestrian navpoint 20-100 m from the player out of view at (%.0f, %.0f, %.0f) (zone %02d_%02d, cap %d)", at.x, at.y, at.z,
                      zone->col, zone->row, cap);
        return;
    }
    BYTE* node = nodes[RandomBelow(static_cast<int>(nodes.size()))];
    const auto* civilians = CiviliansOf(zone->resource);
    if (!civilians) return;
    const auto links = Links(node);
    BYTE* first = links.empty() ? nullptr : links[RandomBelow(static_cast<int>(links.size()))];
    const V3 place = Pos(node);
    float orientation[9];
    Facing(first ? V3{Pos(first).x - place.x, 0, Pos(first).z - place.z} : forward, orientation);
    for (int attempt = 0; attempt < 3; ++attempt) {
        const std::string& name = (*civilians)[RandomBelow(static_cast<int>(civilians->size()))];
        BYTE* info = HumanInfo(name.c_str());
        BYTE* human = info ? MakeHuman(info, &place.x, orientation, nullptr) : nullptr;
        if (!human) {
            ++g_notStreamed;
            RFA_LOG_FIRST(10, "Walkers: %s (%s) %s", name.c_str(), zone->resource.c_str(), info ? "isn't streamed in yet" : "isn't one of the game's characters");
            continue;
        }
        Walker w;
        w.human = Handle(human);
        w.from = Handle(node);
        w.old.push_back(w.from);
        Head(w, first);
        w.made = now;
        w.lastPlace = place;
        w.lastMoveCheck = now;
        w.management = *reinterpret_cast<const int*>(human + kManagement);
        *reinterpret_cast<int*>(human + kManagement) = kAmbient;
        g_walkers.push_back(w);
        ++g_made;
        RFA_LOG_FIRST(10, "Walkers: %s walks from navpoint 0x%08X at (%.1f, %.1f, %.1f), %.0f m away, in zone %02d_%02d (%s: %s by %s, alert %s, cap %d); %d out",
                      name.c_str(), w.from, place.x, place.y, place.z, Distance(place, at), zone->col, zone->row, zone->entry.c_str(), kDensity[density],
                      night ? "night" : "day", kAlert[alert], cap, static_cast<int>(g_walkers.size()));
        return;
    }
}

}  // namespace

void StartAmbient() {
    if (rfa::IniInt(L"GuerrillaPeople", L"Walkers", 1) == 0) {
        rfa::Log("Walkers: off ([GuerrillaPeople] Walkers=0)");
        return;
    }
    ReadFiles();
    if (g_files.zones.empty()) {
        rfa::Log("Walkers: no mods\\guerrilla_people\\ambient.txt (build the people); no one walks");
        return;
    }
    size_t civilians = 0;
    for (const auto& [resource, people] : g_files.civilians) civilians += people.size();
    rfa::Log("Walkers: Guerrilla's ped spawner: %u zones where people walk, %u resources' civilians (%u names) from crews.txt",
             static_cast<unsigned>(g_files.zones.size()), static_cast<unsigned>(g_files.civilians.size()), static_cast<unsigned>(civilians));
    g_on = true;
}

void AmbientFrame(bool ours) {
    if (!g_on) return;
    if (!ours) {
        g_walkers.clear();  // the level's people are gone with it
        return;
    }
    BYTE* player = Player();
    if (!player) return;
    const int now = GameTime();
    if (now < g_lastClock) {  // the clock started over (a new level): what we knew is gone
        g_walkers.clear();
        g_nextSpawn = g_nextReport = 0;
    }
    g_lastClock = now;
    const V3 at = Pos(player);
    for (auto w = g_walkers.begin(); w != g_walkers.end();) {
        BYTE* human = HumanByHandle(kWorld, nullptr, w->human);
        if (!human || GoneOrDead(human)) {
            ++g_died;
            w = g_walkers.erase(w);
            continue;
        }
        const V3 place = Pos(human);
        const V3 head{place.x, place.y + 1.0f, place.z};
        if (InView(&head.x, 1.0f)) w->outOfViewSince = -1;
        else if (w->outOfViewSince < 0) w->outOfViewSince = now;
        if (ShouldGo(*w, Distance(place, at), now)) {
            RemoveObject(kWorld, nullptr, w->human, 0);
            ++g_despawned;
            w = g_walkers.erase(w);
            continue;
        }
        if (w->walked) Walk(*w, human, now);
        ++w;
    }
    if (now >= g_nextReport && (g_made > 0 || ZoneAt(at))) {
        g_nextReport = now + 30000;
        if (g_made > 0 || g_noNode > 0)
            rfa::Log("Walkers: %d out of a cap of %d (%s), %d people in the world (no spawning from %d); %d made, %d gone out of view, %d died or were taken, "
                     "%d fled to the game's AI; %d navpoints reached, %d go-tos without a way; %d not streamed in, %d times no navpoint to spawn on",
                     static_cast<int>(g_walkers.size()), g_cap < 0 ? 0 : g_cap, g_capWhy[0] ? g_capWhy : "not worked out yet", NpcCount(kWorld, nullptr), kMostPeople,
                     g_made, g_despawned, g_died, g_fled, g_reachedNodes, g_goToFailures, g_notStreamed, g_noNode);
    }
    if (now < g_nextSpawn && g_nextSpawn - now <= 1000) return;
    const int people = NpcCount(kWorld, nullptr);
    g_nextSpawn = people >= kBusyPeople ? now + 500 + std::rand() % 501 : now + 250 + std::rand() % 251;
    if (people >= kMostPeople) {
        RFA_LOG_FIRST(5, "Walkers: no new walker: the world has %d people (Guerrilla's spawner stops at %d)", people, kMostPeople);
        return;
    }
    SpawnStep(at, *reinterpret_cast<const V3*>(player + kOrientation + 24), now);
}
