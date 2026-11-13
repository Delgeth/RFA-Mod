// Guerrilla's action nodes for the people who stand under them (piece 3a, 2026-11-13; the user: "civs seem to spend
// alot of time standing and I dont see any movement from them in town's / safe houses"). [RM, A]
//
// Guerrilla's squads under an action node use it (use_action_node_goal RM 0x67D420, its node the NPC's own): the person
// walks to the node (goto_action_node_action RM 0x68F870), is put on its spot (the type's anim_offset and
// anim_heading from the node, or from its parent with snap_to_target Parent), plays the entry clip
// (enter_action_node_action RM 0x68FEE0), holds the type's state for min..max s with its idle clips now and then
// (do_action_node_action RM 0x6523E0 / 0x68FC10: the state_action_grid, each clip its own cooldown), plays the exit
// clip (exit_action_node_action RM 0x6102C0), and the node rests for its cooldown_period before it's used again;
// danger or a fight takes it away (the AI's other goals score higher).
// Armageddon has no action nodes, so here, from mods\guerrilla_people\action_nodes.txt (the people build: each type,
// and each squad node under an action node with its spot and facing), once a frame in our worlds:
// - a living NPC spawned from one of those squad nodes (human +0x1590) is taken: its AI sits out (the walkers' yield
//   bit), the AI's go-to (0x431C20) walks it to the spot, Armageddon's teleport (0x854720, as sf_char_teleport) puts it
//   there facing the way, and the clips play through set_state with an action (0x9691D0) with the state held by the
//   state override (0x86D0F0; names by 0x97EAE0 and 0x97EB90);
// - Guerrilla's danger (the flee-to-safety belief 0x483C90, an enemy to flee 0x483B00) or a fight (its combat target,
//   0x880B70), or its team changing (a convert), gives it back to the game's AI: 30 s, or for good for a convert;
// - a spot it can't walk to (the go-to's 6, 7 or 8) gives it back for 60 s.
// 3b: the walkers (ambient.cpp) use the nodes whose type allows ambient people, as Guerrilla's use_action_node_goal
// (RM 0x67D420: an ambient person at alert green or yellow, not despawning, takes the nearest free node within 30 m,
// find_nearest_action_node RM 0x637D10, its fitness 100 above its wandering's 80): the same walk, spot and clips;
// after the exit it walks on, and the node rests for its type's cooldown_period. A walker looks again a minute later.
// Not here yet: the props (3c: ambient_prop_info), the nodes' other flags (allow_edf, allow_guerrilla, ...: the squad
// under a node is the one Guerrilla put there), and a type's other states (its first).

#include "rfa_mod.h"
#include "actions.h"
#include <climits>
#include <cmath>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace {

constexpr DWORD kPosition = 0x04, kHandle = 0x68, kNpcAi = 0x174C, kYield = 0x651, kHumanSpawnNode = 0x1590, kTeam = 0x13B4, kHumanAnimation = 0x760;
constexpr DWORD kNone = 0xFFFFFFFF;
void* const kWorld = reinterpret_cast<void*>(0x1870630);
using CountFn = int(__fastcall*)(void* world, void*);
using AtFn = BYTE*(__fastcall*)(void* world, void*, int index);
using WorldObjectFn = BYTE*(__fastcall*)(void* world, void*, DWORD handle);
auto const NpcCount = reinterpret_cast<CountFn>(0x424480);
auto const NpcAt = reinterpret_cast<AtFn>(0x4244D0);
auto const WorldObject = reinterpret_cast<WorldObjectFn>(0x6CB9E0);
auto const GoneOrDead = reinterpret_cast<bool(__cdecl*)(BYTE* object)>(0x42ADC0);
auto const FleeToSafetyBelief = reinterpret_cast<BYTE*(__cdecl*)(BYTE* npc)>(0x483C90);
auto const EnemyFleeBelief = reinterpret_cast<BYTE*(__cdecl*)(BYTE* npc)>(0x483B00);
auto const CombatTarget = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human)>(0x880B70);
using GoToFn = int(__fastcall*)(BYTE* ai, void*, const float* place, int movement, int stance, int priority, DWORD cellLow, DWORD cellHigh, bool flag);
auto const GoTo = reinterpret_cast<GoToFn>(0x431C20);
constexpr int kWalk = 1, kStanding = 0, kLowPriority = 0, kNoPath = 6, kStartOffMesh = 7, kDestinationOffMesh = 8;
struct V3 { float x, y, z; };
struct M3 { V3 r, u, f; };
auto const Teleport = reinterpret_cast<void(__cdecl*)(BYTE* human, const V3* place, const M3* orientation, bool flag)>(0x854720);
auto const FindState = reinterpret_cast<int(__cdecl*)(const char* name)>(0x97EAE0);
auto const FindAction = reinterpret_cast<int(__cdecl*)(const char* name)>(0x97EB90);
auto const SetStateOverride = reinterpret_cast<void(__cdecl*)(BYTE* human, int state)>(0x86D0F0);
struct IndexAlias { int index, alias; };
using SetAnimStateFn = void(__fastcall*)(BYTE* animation, void*, IndexAlias state, IndexAlias action, float blend, bool flag);
auto const SetAnimState = reinterpret_cast<SetAnimStateFn>(0x9691D0);
auto const SetStance = reinterpret_cast<void(__cdecl*)(BYTE* human, int stance)>(0x854B90);
int GameTime() { return *reinterpret_cast<const int*>(0x1121300); }

constexpr float kOnSpot = 0.8f, kBlend = 0.3f;
constexpr int kExitTime = 2500, kDangerBack = 30000, kNoWayBack = 60000, kForGood = 9999;

struct Idle { std::string action; float least = 0, most = 0; int index = -1; };
struct Type {
    std::string name, state, entry, exit;
    float rest = 0, least = 0, most = 0;
    std::vector<Idle> idles;
    int stateIndex = -2, entryIndex = -1, exitIndex = -1;  // -2: not looked up yet
};
struct Node { int type = -1; V3 spot{}; float fx = 0, fz = 1; bool taken = false; int restUntil = 0; };
std::vector<Type> g_types;
std::unordered_map<DWORD, Node> g_nodes;  // by the squad node's handle
std::vector<Node> g_pedNodes;             // the walkers' nodes
constexpr float kPedNodeReach = 30.0f;    // find_nearest_action_node's 30 m

enum Phase { kGoing, kHolding, kExiting, kResting, kGivenBack };
struct Actor {
    DWORD node = kNone;
    int pedNode = -1;   // a walker's node (an index in g_pedNodes), or -1 for a squad's person
    bool done = false;  // a walker finished (or given back): taken off once this frame's steps are over
    int team = -1;
    Phase phase = kGoing;
    int until = 0;
    std::vector<int> nextIdle;
    bool overridden = false;
};
std::unordered_map<DWORD, Actor> g_actors;  // by the person's handle
std::unordered_set<DWORD> g_converted;
int g_lastClock = 0, g_nextScan = 0, g_nextReport = 0;
int g_taken = 0, g_started = 0, g_dangers = 0, g_noWay = 0, g_pedTaken = 0;

DWORD HandleOf(const BYTE* o) { return *reinterpret_cast<const DWORD*>(o + kHandle); }
V3 Pos(const BYTE* o) { return *reinterpret_cast<const V3*>(o + kPosition); }
int Rand(float least, float most) {
    const int a = static_cast<int>(least * 1000), b = static_cast<int>(most * 1000);
    return b > a ? a + std::rand() % (b - a + 1) : a;
}

void ReadNodes() {
    std::ifstream file(rfa::RootDir() + L"\\mods\\guerrilla_people\\action_nodes.txt");
    if (!file) {
        rfa::Log("Actions: no mods\\guerrilla_people\\action_nodes.txt (the people built before it had them): Guerrilla's action nodes aren't used");
        return;
    }
    std::unordered_map<std::string, int> typeIndex;
    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;
        std::vector<std::string> f;
        std::stringstream in(line);
        for (std::string part; std::getline(in, part, '\t');) f.push_back(part);
        if (f[0] == "type" && f.size() >= 8) {
            Type t;
            t.name = f[1];
            t.rest = std::stof(f[2]);
            t.state = f[3];
            t.least = std::stof(f[4]);
            t.most = std::stof(f[5]);
            t.entry = f[6] == "-" ? "" : f[6];
            t.exit = f[7] == "-" ? "" : f[7];
            for (size_t k = 8; k + 2 < f.size(); k += 3) t.idles.push_back({f[k], std::stof(f[k + 1]), std::stof(f[k + 2])});
            typeIndex[t.name] = static_cast<int>(g_types.size());
            g_types.push_back(t);
        }
        else if ((f[0] == "node" || f[0] == "ped") && f.size() >= 8) {
            auto type = typeIndex.find(f[2]);
            if (type == typeIndex.end()) continue;
            Node n;
            n.type = type->second;
            n.spot = {std::stof(f[3]), std::stof(f[4]), std::stof(f[5])};
            n.fx = std::stof(f[6]);
            n.fz = std::stof(f[7]);
            if (f[0] == "ped") g_pedNodes.push_back(n);
            else g_nodes[std::stoul(f[1], nullptr, 16)] = n;
        }
    }
    rfa::Log("Actions: %d of Guerrilla's squad nodes under action nodes and %d nodes the walkers use read (%d types); their people go to them and do them, as Guerrilla's",
             static_cast<int>(g_nodes.size()), static_cast<int>(g_pedNodes.size()), static_cast<int>(g_types.size()));
}

// A type's states and clips by name, once; false when its state isn't in the game's list.
bool Resolve(Type& t) {
    if (t.stateIndex == -2) {
        t.stateIndex = FindState(t.state.c_str());
        t.entryIndex = t.entry.empty() ? -1 : FindAction(t.entry.c_str());
        t.exitIndex = t.exit.empty() ? -1 : FindAction(t.exit.c_str());
        for (Idle& idle : t.idles) idle.index = FindAction(idle.action.c_str());
        if (t.stateIndex < 0) RFA_LOG_FIRST(16, "Actions: the game has no animation state \"%s\" (type %s); its people stand", t.state.c_str(), t.name.c_str());
    }
    return t.stateIndex >= 0;
}

void Play(BYTE* human, int state, int action) {
    if (BYTE* animation = *reinterpret_cast<BYTE**>(human + kHumanAnimation)) SetAnimState(animation, nullptr, {state, -1}, {action, -1}, kBlend, false);
}

void SitOut(BYTE* human) {
    if (BYTE* ai = *reinterpret_cast<BYTE**>(human + kNpcAi)) ai[kYield] |= 2;
}

void Release(BYTE* human, Actor& a, int now, int back, const char* why) {
    if (a.overridden) {
        SetStateOverride(human, -1);
        SetStance(human, kStanding);
        a.overridden = false;
    }
    if (a.pedNode >= 0) {  // a walker: back to its walking (ambient.cpp), its node free
        g_pedNodes[a.pedNode].taken = false;
        a.done = true;
        RFA_LOG_FIRST(24, "Actions: walker 0x%08X leaves its node (%s)", HandleOf(human), why);
        return;
    }
    a.phase = kGivenBack;
    a.until = back < 0 ? INT_MAX : now + back;
    RFA_LOG_FIRST(24, "Actions: NPC 0x%08X given back to the game's AI (%s)", HandleOf(human), why);
}

void Step(BYTE* human, Actor& a, int now) {
    Node& n = a.pedNode >= 0 ? g_pedNodes[a.pedNode] : g_nodes[a.node];
    Type& t = g_types[n.type];
    const int team = *reinterpret_cast<const int*>(human + kTeam);
    if (a.team >= 0 && team != a.team) {  // a convert: the game's (and the alert session's) for good
        if (a.phase != kGivenBack || a.until != INT_MAX) Release(human, a, now, -1, "its team changed");
        return;
    }
    const bool danger = FleeToSafetyBelief(human) || EnemyFleeBelief(human) || CombatTarget(human);
    if (a.phase == kGivenBack) {
        if (now < a.until || danger) return;
        a.phase = kGoing;
    }
    if (danger) {
        ++g_dangers;
        Release(human, a, now, kDangerBack, "danger or a fight, as Guerrilla's goals that score higher");
        return;
    }
    SitOut(human);
    BYTE* ai = *reinterpret_cast<BYTE**>(human + kNpcAi);
    switch (a.phase) {
        case kGoing: {
            const V3 at = Pos(human);
            const float dx = at.x - n.spot.x, dz = at.z - n.spot.z;
            if (dx * dx + dz * dz > kOnSpot * kOnSpot) {
                const int result = ai ? GoTo(ai, nullptr, &n.spot.x, kWalk, kStanding, kLowPriority, kNone, kNone, false) : kNoPath;
                if (result == kNoPath || result == kStartOffMesh || result == kDestinationOffMesh) {
                    ++g_noWay;
                    RFA_LOG_FIRST(16, "Actions: NPC 0x%08X can't walk to its %s spot at (%.1f, %.1f, %.1f) (the go-to's %d)", HandleOf(human), t.name.c_str(), n.spot.x,
                                  n.spot.y, n.spot.z, result);
                    Release(human, a, now, kNoWayBack, "no way to its spot");
                }
                return;
            }
            if (!Resolve(t)) {
                Release(human, a, now, -1, "its type's state isn't in the game");
                return;
            }
            const V3 f{n.fx, 0, n.fz};
            const M3 facing{{f.z, 0, -f.x}, {0, 1, 0}, f};
            Teleport(human, &n.spot, &facing, true);
            SetStance(human, kStanding);
            SetStateOverride(human, t.stateIndex);
            Play(human, t.stateIndex, t.entryIndex);
            a.overridden = true;
            a.phase = kHolding;
            a.until = t.most >= kForGood ? INT_MAX : now + Rand(t.least, t.most);
            a.nextIdle.assign(t.idles.size(), 0);
            for (size_t k = 0; k < t.idles.size(); ++k) a.nextIdle[k] = now + Rand(t.idles[k].least, t.idles[k].most);
            ++g_started;
            RFA_LOG_FIRST(24, "Actions: NPC 0x%08X does %s at (%.1f, %.1f, %.1f): %s for %s", HandleOf(human), t.name.c_str(), n.spot.x, n.spot.y, n.spot.z, t.state.c_str(),
                          t.most >= kForGood ? "good" : "a while");
            return;
        }
        case kHolding:
            for (size_t k = 0; k < t.idles.size(); ++k)
                if (now >= a.nextIdle[k]) {
                    if (t.idles[k].index >= 0) Play(human, t.stateIndex, t.idles[k].index);
                    a.nextIdle[k] = now + Rand(t.idles[k].least, t.idles[k].most);
                    break;
                }
            if (now >= a.until) {
                Play(human, t.stateIndex, t.exitIndex);
                a.phase = kExiting;
                a.until = now + kExitTime;
            }
            return;
        case kExiting:
            if (now < a.until) return;
            SetStateOverride(human, -1);
            SetStance(human, kStanding);
            a.overridden = false;
            if (a.pedNode >= 0) {  // a walker walks on; its node rests
                n.taken = false;
                n.restUntil = now + static_cast<int>(t.rest * 1000);
                a.done = true;
                return;
            }
            a.phase = kResting;
            a.until = now + static_cast<int>(t.rest * 1000);
            return;
        case kResting:
            if (now >= a.until) a.phase = kGoing;
            return;
        default: return;
    }
}

}  // namespace

void StartActions() { ReadNodes(); }

void ActionsFrame(bool ours) {
    if (g_nodes.empty()) return;
    const int now = GameTime();
    if (!ours || now < g_lastClock) {  // another world, or the level started over
        g_actors.clear();
        g_lastClock = now;
        return;
    }
    g_lastClock = now;
    // Who is about: those spawned from a squad node under an action node, taken; the gone let go.
    if (now >= g_nextScan) {
        g_nextScan = now + 1000;
        std::unordered_set<DWORD> about;
        const int people = NpcCount(kWorld, nullptr);
        for (int i = 0; i < people; ++i) {
            BYTE* human = NpcAt(kWorld, nullptr, i);
            if (!human || GoneOrDead(human)) continue;
            const DWORD node = *reinterpret_cast<const DWORD*>(human + kHumanSpawnNode);
            if (node == kNone || !g_nodes.count(node)) continue;
            const DWORD handle = HandleOf(human);
            about.insert(handle);
            if (g_actors.count(handle)) continue;
            Actor a;
            a.node = node;
            a.team = *reinterpret_cast<const int*>(human + kTeam);
            g_actors[handle] = a;
            ++g_taken;
            RFA_LOG_FIRST(16, "Actions: NPC 0x%08X (spawned from squad node 0x%08X) goes to its %s spot", handle, node, g_types[g_nodes[node].type].name.c_str());
        }
        for (auto it = g_actors.begin(); it != g_actors.end();) it = it->second.pedNode >= 0 || about.count(it->first) ? std::next(it) : g_actors.erase(it);
    }
    for (auto& [handle, a] : g_actors) {
        BYTE* human = WorldObject(kWorld, nullptr, handle);
        if (human && !GoneOrDead(human)) Step(human, a, now);
        else if (a.pedNode >= 0) {  // a walker gone (out of view, or dead): its node free
            g_pedNodes[a.pedNode].taken = false;
            a.done = true;
        }
    }
    for (auto it = g_actors.begin(); it != g_actors.end();) it = it->second.done ? g_actors.erase(it) : std::next(it);
    if (now >= g_nextReport) {
        g_nextReport = now + 60000;
        if (g_taken > 0 || g_pedTaken > 0)
            rfa::Log("Actions: %d people at Guerrilla's action nodes now; %d taken, %d walkers took a node, %d actions started, %d handed back for danger or a fight, "
                     "%d with no way to their spot",
                     static_cast<int>(g_actors.size()), g_taken, g_pedTaken, g_started, g_dangers, g_noWay);
    }
}

bool ActionsWalkerBusy(unsigned long human) {
    auto it = g_actors.find(human);
    return it != g_actors.end() && it->second.pedNode >= 0 && !it->second.done;
}

bool ActionsWalkerStart(unsigned long human, const float* at) {
    if (g_pedNodes.empty() || g_actors.count(human)) return false;
    const int now = GameTime();
    int nearest = -1;
    float best = kPedNodeReach * kPedNodeReach;
    for (int i = 0; i < static_cast<int>(g_pedNodes.size()); ++i) {
        const Node& n = g_pedNodes[i];
        if (n.taken || now < n.restUntil) continue;
        const float dx = n.spot.x - at[0], dy = n.spot.y - at[1], dz = n.spot.z - at[2];
        const float d = dx * dx + dy * dy + dz * dz;
        if (d < best) best = d, nearest = i;
    }
    if (nearest < 0) return false;
    g_pedNodes[nearest].taken = true;
    Actor a;
    a.pedNode = nearest;
    const BYTE* person = WorldObject(kWorld, nullptr, human);
    a.team = person ? *reinterpret_cast<const int*>(person + kTeam) : -1;  // a convert leaves its node
    g_actors[human] = a;
    ++g_pedTaken;
    RFA_LOG_FIRST(24, "Actions: walker 0x%08lX goes to a %s node %.0f m away, as Guerrilla's use_action_node_goal", human,
                  g_types[g_pedNodes[nearest].type].name.c_str(), std::sqrt(best));
    return true;
}
void ActionsWalkerStop(unsigned long human) {
    auto it = g_actors.find(human);
    if (it == g_actors.end() || it->second.pedNode < 0) return;
    Actor& a = it->second;
    if (BYTE* person = WorldObject(kWorld, nullptr, human); person && a.overridden) {
        SetStateOverride(person, -1);
        SetStance(person, kStanding);
    }
    g_pedNodes[a.pedNode].taken = false;
    g_actors.erase(it);
}
