// Guerrilla's demolitions master: bring one building down with the weapons given, in the time
// given, without leaving the area.
//
// A challenge is a node attached to its target building (the node's parent), a row of
// activity_demolitions_master.xtbl (weapons, time limit, par time, area, rewards, texts) and a row
// of activity.xtbl. The player starts it at the node; Guerrilla shows the briefing, fades to black,
// rebuilds the building (the node's restore list, from its script's activity_restore_object), swaps
// the player's inventory for the table's weapons and clears the area. Then, checked every 250 ms:
//   - the building is down (gone, or the game's own "destroyed" flag, which also sends the scripts'
//     building_destroyed event) → success, 7.5 s to watch it fall;
//   - the time limit runs out (with none the timer counts up) → out of time;
//   - farther than Active_Range from the building's middle: 5 s to come back (not when 5 s or less
//     are left), then out of range; farther than 85 m → out of range at once;
//   - no ammo left and the building isn't collapsing, for 2.5 s → out of ammo; the player dies → fail.
// Then the results screen, the player is warped back to where they started with their own
// inventory and vehicle. The first win gives the row's salvage; beating the par time ("PRO TIME":
// elapsed ≤ par + 999 ms) the first time gives the same again. Each node keeps whether it is done,
// discovered, par beaten, how many times, and the best time.
//
// The mod runs the challenge's own rules. Not ported yet (each logged when reached): the briefing
// and results screens (rfg_ui_demolitions, accepted at once), the fade, the building's rebuilding,
// the inventory swap, clearing the area, the out-of-ammo and death checks, the warp back, the map
// icons and the "use" prompt at the node (until then the console command rfg_dm_start <je_dm_01..16>
// starts one).
#define RFA_MOD_NAME "guerrilla_campaign"
#include "rfa_mod.h"
#include "activity_base.h"
#include "handle_kinds.h"
#include "liberation.h"
#include "save.h"
#include "script_engine.h"
#include "tables.h"
#include "world_objects.h"
#include <math.h>
#include <stdio.h>
#include <string.h>

namespace campaign::activities {
namespace {

// One row of activity_demolitions_master.xtbl (demolitions_master_level_data).
struct Weapon {
    std::string weapon;  // weapons.xtbl name, or none for objects the activity's layer places
    std::string icon;    // Non_Weapon_Icon_Name, for those
    int ammo = -1;
};
struct Level {
    std::string name;
    std::string baseData;     // its activity.xtbl row
    std::string bodyText;     // Demo_Info_Body_Text
    std::string radioMessage;
    bool useVehicleSpawn = false;
    std::vector<Weapon> weapons;
    int timeLimit = 0;        // seconds; 0 = none, the timer counts up
    int parTime = 10;
    bool unlimitedAmmo = false;
    bool multipleTargets = false;
    std::string briefingMessage = "ACT_DEMO_MASTER_SUBTITLE";
    std::string cornerMessage = "ACT_DM_DESTROY_BUILDING";
    std::string infoTitle1, infoTitle2;
    std::string success, failAmmo, failTime, failRange, failDeath;
    float activeRange = 25.0f;
    Rewards rewards;
};

// A node's progress (dm_stats), kept in the campaign's save under its handle.
struct Progress {
    bool completed = false, discovered = false, parBeaten = false;
    int timesCompleted = 0;
    int bestTime = 0;  // ms
};

// Guerrilla's demolitions_master_states.
enum class Step { Wait, Ready, LoadingLayers, PregameBriefing, PregameAccept, PregameReject, Active, Complete, PostgameBriefing, PostgameReject, PostgameDelay };

constexpr float kHardRange = 85.0f;        // beyond this, out of range at once
constexpr float kRangeGrace = 5.0f;        // seconds to come back into the area
constexpr float kPollPeriod = 0.25f;       // do_frame_polling_period
constexpr float kSuccessDelay = 7.5f;      // watching the building fall
constexpr float kFailureDelay = 0.1f;
constexpr int kParSlackMs = 999;
constexpr float kUseDistance = 1.0f;       // the "use" prompt at a node (Guerrilla compares squared distance with 1)

std::vector<Level> g_levels;

const Level* FindLevel(const std::string& name) {
    for (const Level& level : g_levels)
        if (_stricmp(level.name.c_str(), name.c_str()) == 0) return &level;
    return nullptr;
}

void LoadLevels() {
    std::vector<tables::Element> rows;
    if (!tables::Load("activity_demolitions_master", rows)) return;
    for (const tables::Element& row : rows) {
        Level level;
        level.name = row.Text("Name");
        level.baseData = row.Text("base_activity_data");
        level.bodyText = row.Text("Demo_Info_Body_Text");
        level.radioMessage = row.Text("Radio_Message");
        level.useVehicleSpawn = row.Flag("Use_Vehicle_Spawn");
        if (const tables::Element* list = row.Child("Weapon_List"))
            for (const tables::Element& entry : list->children)
                level.weapons.push_back({entry.Text("Weapon"), entry.Text("Non_Weapon_Icon_Name"), entry.Int("Ammo", -1)});
        level.timeLimit = row.Int("Time_Limit", 0);
        level.parTime = row.Int("Par_Time", 10);
        level.unlimitedAmmo = row.Flag("Unlimited_Ammo");
        level.multipleTargets = row.Flag("Multiple_Target_Buildings");
        level.briefingMessage = row.Text("Briefing_Destroy_Message", "ACT_DEMO_MASTER_SUBTITLE");
        level.cornerMessage = row.Text("Corner_HUD_Destroy_Message", "ACT_DM_DESTROY_BUILDING");
        level.infoTitle1 = row.Text("Info_Title_1");
        level.infoTitle2 = row.Text("Info_Title_2");
        level.success = row.Text("Success_String");
        level.failAmmo = row.Text("Failure_String_Ammo");
        level.failTime = row.Text("Failure_String_Time");
        level.failRange = row.Text("Failure_String_Range");
        level.failDeath = row.Text("Failure_String_Death");
        level.activeRange = row.Number("Active_Range", 25.0f);
        if (const tables::Element* rewards = row.Child("Standard_Rewards")) {
            level.rewards.control = rewards->Number("EDF_Control_Reduction");
            level.rewards.morale = rewards->Number("Morale_Increase");
            level.rewards.salvage = rewards->Int("Salvage_Reward");
        }
        g_levels.push_back(level);
    }
    rfa::Log("activity_demolitions_master.xtbl: %u challenges", static_cast<unsigned>(g_levels.size()));
}

Progress LoadProgress(const world::Object& node) {
    // The converter gives the node's properties as Guerrilla's zone has them; the campaign's save
    // has the player's own.
    Progress progress;
    const std::string section = save::ForObject("demolitions", node.handle);
    progress.completed = save::Int(section.c_str(), "complete", node.Property("demolitions_master_complete") == "true") != 0;
    progress.discovered = save::Int(section.c_str(), "discovered", node.Property("demolitions_master_discovered") == "true") != 0;
    progress.parBeaten = save::Int(section.c_str(), "par_beaten", node.Property("demolitions_master_par_beaten") == "true") != 0;
    progress.timesCompleted = save::Int(section.c_str(), "times_completed", atoi(node.Property("demolitions_master_times_completed", "0").c_str()));
    progress.bestTime = save::Int(section.c_str(), "best_time", atoi(node.Property("demolitions_master_best_time", "0").c_str()));
    return progress;
}

void SaveProgress(unsigned node, const Progress& progress) {
    const std::string section = save::ForObject("demolitions", node);
    save::SetInt(section.c_str(), "complete", progress.completed);
    save::SetInt(section.c_str(), "discovered", progress.discovered);
    save::SetInt(section.c_str(), "par_beaten", progress.parBeaten);
    save::SetInt(section.c_str(), "times_completed", progress.timesCompleted);
    save::SetInt(section.c_str(), "best_time", progress.bestTime);
}

// The world's buildings (rfg_mover): how many, and each (Armageddon's 0x4776E0 / 0x477730 on the world).
// A building's flags (+0x150) bit 4 is game_destroyed, the test Guerrilla's challenge wins by
// (activity_demolitions_master::activity_should_end: the target's flags.game_destroyed).
void* const kWorld = reinterpret_cast<void*>(0x1870630);
using CountFn = int(__fastcall*)(void* world, void* unused);
using AtFn = BYTE*(__fastcall*)(void* world, void* unused, int index);
auto const BuildingCount = reinterpret_cast<CountFn>(0x4776E0);
auto const BuildingAt = reinterpret_cast<AtFn>(0x477730);
constexpr DWORD kBuildingFlags = 0x150, kGameDestroyed = 1u << 4;

constexpr DWORD kHandle = 0x68;  // an object's handle

bool IsBuilding(const void* object) {
    if (!object) return false;
    const int count = BuildingCount(kWorld, nullptr);
    for (int i = 0; i < count; ++i)
        if (BuildingAt(kWorld, nullptr, i) == object) return true;
    return false;
}

// The building standing at a place (within 2 m of it), or null: for a target the world converter gave
// another handle, because ours already had Guerrilla's.
BYTE* BuildingAtPlace(const Vector& place) {
    BYTE* best = nullptr;
    float bestDistance = 2.0f * 2.0f;
    const int count = BuildingCount(kWorld, nullptr);
    for (int i = 0; i < count; ++i) {
        BYTE* building = BuildingAt(kWorld, nullptr, i);
        if (!building) continue;
        const Vector at = PositionOf(building);
        const float dx = at.x - place.x, dy = at.y - place.y, dz = at.z - place.z;
        const float distance = dx * dx + dy * dy + dz * dz;
        if (distance < bestDistance) {
            bestDistance = distance;
            best = building;
        }
    }
    return best;
}

float FlatDistance(const Vector& a, const Vector& b) {
    const float dx = a.x - b.x, dz = a.z - b.z;
    return sqrtf(dx * dx + dz * dz);
}

// The game's console output (printf-style), so a player trying it sees what happens.
using ConsolePrintFn = void(__cdecl*)(const char* format, ...);
auto const ConsolePrint = reinterpret_cast<ConsolePrintFn>(0xC7C440);

void NotPorted(const char* what) {
    static std::vector<std::string> said;
    for (const std::string& line : said)
        if (line == what) return;
    said.push_back(what);
    rfa::Log("Demolitions master: %s isn't ported yet", what);
}

class Demolitions : public Activity {
public:
    Demolitions() : Activity(DemolitionsMaster, true, false) {}

    bool StartAt(const world::Object& node) {
        // demolitions_master_can_be_started: no activity running, waiting, alert GREEN.
        if (AnyRunning() || step_ != Step::Wait || liberation::AlertLevel() != handles::AlertGreen) return false;
        level_ = FindLevel(node.Property("demolitions_master_type"));
        if (!level_) {
            rfa::Log("Demolitions master node %08X: no challenge \"%s\"", node.handle, node.Property("demolitions_master_type").c_str());
            return false;
        }
        // The target must be in the world (a building missing would count as already down). It keeps
        // Guerrilla's handle, unless the world converter had to give it another: then it is the building
        // at its place.
        unsigned target = node.parent;
        const void* building = ObjectFromHandle(target);
        if (!IsBuilding(building) && node.hasParentPos) {
            if (BYTE* atPlace = BuildingAtPlace(node.parentPos)) {
                rfa::Log("Demolitions master %s: target %08X isn't a building here; the building at its place is %08X", level_->name.c_str(), target,
                         *reinterpret_cast<const DWORD*>(atPlace + kHandle));
                building = atPlace;
                target = *reinterpret_cast<const DWORD*>(atPlace + kHandle);
            }
        }
        if (!building) {
            rfa::Log("Demolitions master %s: its target building %08X isn't in the world; not started", level_->name.c_str(), node.parent);
            ConsolePrint("Demolitions master %s: its target building %08X isn't in the world\n", level_->name.c_str(), node.parent);
            return false;
        }
        if (!StartSpawn(node.handle, FindBaseData(level_->baseData), liberation::DistrictAt(node.pos))) return false;
        target_ = target;
        targetIsBuilding_ = IsBuilding(building);
        const Vector at = PositionOf(building);
        rfa::Log("Demolitions master %s: target %08X at (%.0f, %.0f, %.0f), %s, %.0f m from the node", level_->name.c_str(), target_, at.x, at.y, at.z,
                 targetIsBuilding_ ? "a building" : "NOT one of the world's buildings (won when it is gone)", FlatDistance(at, node.pos));
        progress_ = LoadProgress(node);
        step_ = Step::LoadingLayers;
        if (void* player = Player()) startPosition_ = PositionOf(player);
        rfa::Log("Demolitions master %s: started at node %08X, target building %08X", level_->name.c_str(), node.handle, target_);
        return true;
    }

    void Reset() override {
        Activity::Reset();
        step_ = Step::Wait;
        target_ = 0xFFFFFFFF;
        targetDown_ = success_ = false;
        StopListening();
    }

    // A new level: the game has dropped every listener with the old one (Guerrilla's
    // gem_level_shutdown), so ours is forgotten, not taken away.
    void LevelInit() override {
        listener_ = nullptr;
        Activity::LevelInit();
    }

    bool LockAlertLevel() const override { return IsActive(); }

    HudInfo Hud() const override {
        HudInfo info;
        info.running = step_ == Step::Active || step_ == Step::PostgameDelay;
        info.mode = DemolitionsMaster;
        info.title = "ACT_DEMO_MASTER_NAME";
        info.objective = level_ ? level_->cornerMessage : "";
        info.timerVisible = info.running;
        // get_display_time: the out-of-range countdown, else the countdown, else the time so far.
        float shown = rangeTimer_ > 0.0f ? rangeTimer_ : level_ && level_->timeLimit > 0 ? timeRemaining_ : elapsed_;
        if (shown < 0.0f) shown = 0.0f;
        char text[16];
        sprintf_s(text, "%02d:%02d", static_cast<int>(shown) / 60, static_cast<int>(shown) % 60);
        info.timerText = text;
        info.status = targetDown_ ? 2 : 0;
        return info;
    }

protected:
    bool Initialize() override {
        if (!Activity::Initialize()) return false;
        if (target_ == 0xFFFFFFFF) {
            rfa::Log("Demolitions master %s: the node has no building", level_->name.c_str());
            End();
            return false;
        }
        return true;
    }

    void FrameReady(float) override {
        // Guerrilla opens the briefing (rfg_ui_demolitions, with Dan's radio line) and waits for
        // Accept; then fades to black, cancels charges and explosions in the area, and starts up.
        if (step_ == Step::LoadingLayers) {
            NotPorted("the briefing screen (accepted at once)");
            step_ = Step::PregameAccept;
        }
        if (step_ == Step::PregameAccept) {
            NotPorted("the fade to black");
            NotPorted("rebuilding the building (the node's restore list)");
            if (!BeginStartUp()) {
                step_ = Step::PregameReject;
                End();
            }
        }
    }

    bool Start() override {
        if (!Activity::Start()) return false;
        // finalize_activity_initialization: the inventory swapped for the challenge's weapons.
        for (const Weapon& weapon : level_->weapons)
            rfa::Log("Demolitions master %s: weapon %s (%s), ammo %d", level_->name.c_str(), weapon.weapon.c_str(), weapon.icon.c_str(), weapon.ammo);
        NotPorted("swapping the player's inventory for the challenge's weapons");
        NotPorted("clearing the area and having the AI ignore the player");
        timeRemaining_ = static_cast<float>(level_->timeLimit);
        elapsed_ = 0.0f;
        rangeTimer_ = 0.0f;
        pollTimer_ = 0.0f;
        // The play area's middle: the building's (Guerrilla: its bounding box's centre, or the
        // layer's DEMO_MASTER dummy).
        center_ = PositionOf(ObjectFromHandle(target_));
        listener_ = script::Listen(script::kBuildingDestroyedEvent, &BuildingDestroyed, this);
        step_ = Step::Active;
        ConsolePrint("Demolitions master %s: go! (par %d s, limit %d s, area %.0f m)\n", level_->name.c_str(), level_->parTime,
                     level_->timeLimit, level_->activeRange);
        return true;
    }

    void FrameActive(float seconds) override {
        if (step_ == Step::PostgameDelay) {
            postTimer_ -= seconds;
            if (postTimer_ <= 0.0f) Finish();
            return;
        }
        if (step_ != Step::Active) return;
        elapsed_ += seconds;
        if (level_->timeLimit > 0) {
            timeRemaining_ -= seconds;
            if (timeRemaining_ <= 0.0f) return Ending(false, OutOfTime);
        }
        if (rangeTimer_ > 0.0f) {
            rangeTimer_ -= seconds;
            if (rangeTimer_ <= 0.0f) return Ending(false, OutOfRange);
        }
        pollTimer_ -= seconds;
        if (pollTimer_ > 0.0f) return;
        pollTimer_ = kPollPeriod;
        // activity_should_end: the building down (its game_destroyed flag, the game's event, or gone), out
        // of ammo (not ported).
        const BYTE* building = static_cast<const BYTE*>(ObjectFromHandle(target_));
        if (building && targetIsBuilding_ && (*reinterpret_cast<const DWORD*>(building + kBuildingFlags) & kGameDestroyed)) targetDown_ = true;
        if (targetDown_ || !building) return Ending(true, Success);
        NotPorted("the out-of-ammo and death checks");
        void* player = Player();
        if (!player) return;
        const float distance = FlatDistance(PositionOf(player), center_);
        if (distance > kHardRange) return Ending(false, OutOfRange);
        if (distance > level_->activeRange) {
            if (rangeTimer_ <= 0.0f && !(level_->timeLimit > 0 && timeRemaining_ <= kRangeGrace)) {
                rangeTimer_ = kRangeGrace;
                ConsolePrint("Demolitions master: 5 seconds to return to the area\n");
            }
        } else {
            rangeTimer_ = 0.0f;
        }
    }

    void End() override {
        StopListening();
        Activity::End();
    }

private:
    static int __cdecl BuildingDestroyed(const script::GameEvent* event, void* notify) {
        // The listener's data (notify +8) is the challenge.
        auto self = *reinterpret_cast<Demolitions* const*>(static_cast<BYTE*>(notify) + 8);
        if (self && event && event->values[0] == self->target_) self->targetDown_ = true;
        return script::kEventOk;
    }

    void StopListening() {
        script::StopListening(listener_);
        listener_ = nullptr;
    }

    // begin_ending_sequence: the result, then a moment before the end.
    void Ending(bool success, EndReason reason) {
        success_ = success;
        rangeTimer_ = 0.0f;
        postTimer_ = success ? kSuccessDelay : kFailureDelay;
        step_ = Step::PostgameDelay;
        const char* text = success ? level_->success.c_str()
                           : reason == OutOfTime ? level_->failTime.c_str()
                           : reason == OutOfRange ? level_->failRange.c_str()
                           : reason == OutOfAmmo ? level_->failAmmo.c_str()
                                                 : level_->failDeath.c_str();
        rfa::Log("Demolitions master %s: %s after %.1f s", level_->name.c_str(), text, elapsed_);
        ConsolePrint("Demolitions master %s: %s (%.1f s)\n", level_->name.c_str(), text, elapsed_);
    }

    // do_activity_end, then (after the results screen) the warp back and the victory or loss.
    void Finish() {
        const unsigned node = node_;
        const int elapsedMs = static_cast<int>(elapsed_ * 1000.0f);
        int salvage = 0;
        if (success_) {
            // get_salvage_received, then record_victory.
            if (!progress_.completed) salvage += level_->rewards.salvage;
            if (!progress_.parBeaten && elapsedMs <= level_->parTime * 1000 + kParSlackMs) {
                progress_.parBeaten = true;
                salvage += level_->rewards.salvage;
            }
            if (!progress_.completed || elapsedMs < progress_.bestTime) progress_.bestTime = elapsedMs;
            progress_.completed = true;
            ++progress_.timesCompleted;
            SaveProgress(node, progress_);
        }
        End();
        NotPorted("the results screen");
        NotPorted("warping the player back and giving back their inventory and vehicle");
        if (success_) {
            Rewards rewards = level_->rewards;
            rewards.salvage = salvage;
            RecordVictory(rewards);
            ConsolePrint("Demolitions master %s: salvage %d, best %.1f s\n", level_->name.c_str(), salvage, progress_.bestTime / 1000.0f);
        } else {
            RecordLoss();
        }
        step_ = Step::Complete;
    }

    void FrameInactive(float) override {
        // Guerrilla shows every node on the map while nothing runs, and offers the challenge at
        // the node ("use"). Until the prompt is ported, the console says which one is here.
        void* player = Player();
        if (!player) return;
        const Vector here = PositionOf(player);
        for (const world::Object* node : world::OfClass("object_demolitions_master_node")) {
            const float dx = node->pos.x - here.x, dy = node->pos.y - here.y, dz = node->pos.z - here.z;
            const bool atNode = dx * dx + dy * dy + dz * dz < kUseDistance * kUseDistance * 4.0f;
            if (atNode && nearNode_ != node->handle)
                ConsolePrint("Demolitions master here: rfg_dm_start %s\n", node->Property("demolitions_master_type").c_str());
            if (atNode) {
                nearNode_ = node->handle;
                return;
            }
        }
        nearNode_ = 0xFFFFFFFF;
    }

    const Level* level_ = nullptr;
    Step step_ = Step::Wait;
    unsigned target_ = 0xFFFFFFFF;
    Progress progress_;
    Vector startPosition_{};
    Vector center_{};
    float timeRemaining_ = 0.0f, elapsed_ = 0.0f, rangeTimer_ = 0.0f, postTimer_ = 0.0f, pollTimer_ = 0.0f;
    bool targetDown_ = false, success_ = false, targetIsBuilding_ = false;
    void* listener_ = nullptr;
    unsigned nearNode_ = 0xFFFFFFFF;
};

Demolitions* g_demolitions;

}  // namespace

Activity* MakeDemolitionsMaster() {
    LoadLevels();
    world::Load();
    g_demolitions = new Demolitions();
    return g_demolitions;
}

bool StartDemolitionsMaster(const char* level) {
    for (const world::Object* node : world::OfClass("object_demolitions_master_node"))
        if (_stricmp(node->Property("demolitions_master_type").c_str(), level) == 0) return g_demolitions->StartAt(*node);
    rfa::Log("No demolitions master node of \"%s\" in campaign_objects.xml", level);
    return false;
}

}  // namespace campaign::activities
