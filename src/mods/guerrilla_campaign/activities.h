// Guerrilla's activities ("guerrilla actions"): the framework (activities.cpp) and its kinds
// (demolitions_master.cpp first).
#pragma once
#include "campaign.h"
#include <string>
#include <vector>

namespace campaign::activities {

// Guerrilla's activity_modes.
enum Mode : int {
    ModeNone = 0,
    VehicleRail,  // riding shotgun
    MissionVrail,
    TanksALot,
    Delivery,  // Guerrilla Express
    KillingSpree,
    Courier,
    Mayhem,
    RaidDefend,
    RaidAttack,
    HouseArrest,
    DemolitionsMaster,
    Assassin,
    Convoy,
    ContinuousAttack,
    CarBomb,
    AreaDefense,
    kModes,
};

// Guerrilla's base_activity_states (the same values as handles::ActivityState).
enum class State { Wait, Loading, Ready, Starting, Running, Shutdown };
// activity_interface_states: what the HUD shows of it.
enum class Interface { None, Ready, Running, Victory, Loss, Killed };
// activity_disable_reason (Everything clears them all).
enum DisableReason { ByActivity, ByMission, ByScript, ByConsole, BySatellite, ByOther, Everything };
// gamelog_complete_reasons: why a run ended.
enum EndReason { Failed, Success, PlayerDied, Cancelled, OutOfTime, VehicleAbandoned, VehicleDestroyed, Escaped, OutOfRange, OutOfAmmo, HostagesKilled, BuildingDestroyed, GoalNotReached };

// activity_standard_rewards.
struct Rewards {
    float control = 0.0f;  // EDF_Control_Reduction
    float morale = 0.0f;   // Morale_Increase
    int salvage = 0;       // Salvage_Reward
};

// One row of activity.xtbl (base_activity_data): what a run of an activity loads and holds.
struct BaseData {
    std::string name;
    float controlLimit = 1.0f;  // offered only while the district's EDF control is at most this
    std::vector<std::string> scriptGroups;
    std::vector<std::string> layers;
    int minAlertCap = 0, maxAlertCap = 3;  // green, red
    std::string voice;          // voice_info/voice_type
    std::string music;          // music_info/music_intensity
    std::string specialScore;   // music_info/special_score
    bool disabled = false;
    bool allowGuerrillaReinforcement = false;
};
const BaseData* FindBaseData(const std::string& name);

void Init();
void Enter();
void Update(float seconds);

// For the script words.
bool AnyRunning();                               // activity_is_active: a modal activity is current or starting
bool AlertLocked();                              // an activity holds the alert still
void Disable(DisableReason reason, bool free);   // suspend the modal (free: the free) activities
void DisableOffers(DisableReason reason);        // every modal one but Guerrilla Express and demolitions master
void Reinstate(DisableReason reason, bool free);

// What the HUD reads (Guerrilla's MissionInfo, MissionStatus, HudTimerInfo and DemolitionsInfo).
struct HudInfo {
    bool running = false;
    Mode mode = ModeNone;
    std::string title;         // e.g. "DEMOLITIONS MASTER"
    std::string objective;     // e.g. "DESTROY THE BUILDING"
    std::string timerText;     // "mm:ss"
    bool timerVisible = false;
    int status = 0;            // the kind's own (demolitions: 1 out of ammo while collapsing, 2 destroyed)
};
HudInfo Hud();

}  // namespace campaign::activities
