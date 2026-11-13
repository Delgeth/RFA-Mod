// Guerrilla's activity framework, as its code runs it.
//
// Each kind of activity is one object (Guerrilla's Activities[17], indexed by activity_modes). A
// run is a node object (a demolitions master node, a raid node, ...) plus a row of activity.xtbl
// (base_activity_data: layers, scripts, voice, music, alert caps) plus the kind's own row. Each
// frame (activity_system_do_frame) every kind takes one step of its state machine: WAIT → LOADING
// (layers, voice, objects restored) → READY (offered) → STARTING → RUNNING → SHUTDOWN → WAIT, one
// virtual function per state; the kind's own logic decides when it starts, wins and loses.
//
// Modal activities (demolitions master, house arrest, raids, riding shotgun, Guerrilla Express,
// courier, convoy, area defense, tanks a lot) run one at a time: starting one kills the others
// that aren't waiting or shutting down; after one ends, no other is offered for 180 s plus 60 s
// for each one offered in the district, at most 600 s. Free ones (mayhem, killing spree,
// continuous attack, ...) run alongside. At alert ORANGE or RED every offered modal activity is
// withdrawn. Disabling (by a mission, a script, the console, ...) kills a run that is offered or
// running. A state change tells the scripts (activity_status_change). A run in progress isn't
// saved (Guerrilla warns when saving during one); each node keeps its own progress.
//
// Not ported yet: natural spawning (only courier, convoy and activity_spawn.xtbl's raids and
// deliveries spawn by themselves), the activity layers and script groups, the voice and music,
// the vehicle and human reservations.
#define RFA_MOD_NAME "guerrilla_campaign"
#include "rfa_mod.h"
#include "activity_base.h"
#include "handle_kinds.h"
#include "liberation.h"
#include "script_words.h"
#include "tables.h"
#include "world_objects.h"
#include <stdio.h>
#include <string.h>

namespace campaign::activities {
namespace {

Activity* g_activities[kModes];
Mode g_current = ModeNone;   // Current_activity_mode
float g_cooldown = 0.0f;     // seconds before a modal activity may be offered again
std::vector<BaseData> g_baseData;

// Guerrilla's tweaks Activity_cooldown_base_period_ms and _per_ready_ms, and its cap.
constexpr float kCooldownBase = 180.0f, kCooldownPerReady = 60.0f, kCooldownMax = 600.0f;
// Interface states fade back to none 10 s after they change (activity_do_frame, WAIT).
constexpr float kInterfaceHold = 10.0f;
// A modal run stays in SHUTDOWN at least this long (activity_base_modal::activity_clean_up_once).
constexpr float kShutdownPeriod = 1.0f;

int AlertCapFrom(const std::string& name, int fallback) {
    static const char* const kNames[] = {"green", "yellow", "orange", "red"};
    for (int i = 0; i < 4; ++i)
        if (_stricmp(name.c_str(), kNames[i]) == 0) return i;
    return fallback;
}

void LoadBaseData() {
    std::vector<tables::Element> rows;
    if (!tables::Load("activity", rows)) return;
    for (const tables::Element& row : rows) {
        BaseData data;
        data.name = row.Text("Name");
        data.controlLimit = row.Number("control_limit", 1.0f);
        if (const tables::Element* groups = row.Child("script_groups"))
            for (const tables::Element* group : groups->Children("script_group_name")) data.scriptGroups.push_back(group->text);
        if (const tables::Element* layers = row.Child("managed_layer_info"))
            for (const tables::Element& layer : layers->children) data.layers.push_back(layer.text);
        data.minAlertCap = AlertCapFrom(row.Text("min_alert_cap"), 0);
        data.maxAlertCap = AlertCapFrom(row.Text("max_alert_cap"), 3);
        if (const tables::Element* voice = row.Child("voice_info")) data.voice = voice->Text("voice_type");
        if (const tables::Element* music = row.Child("music_info")) {
            data.music = music->Text("music_intensity");
            data.specialScore = music->Text("special_score");
        }
        data.disabled = row.Flag("disabled");
        data.allowGuerrillaReinforcement = row.Flag("guerrilla_reinforcement");
        g_baseData.push_back(data);
    }
    rfa::Log("activity.xtbl: %u activities", static_cast<unsigned>(g_baseData.size()));
}

int ReadyCount() {
    int ready = 0;
    for (Activity* activity : g_activities)
        if (activity && activity->Modal() && activity->GetState() == State::Ready) ++ready;
    return ready;
}

// ---- The game's console: a command to try an activity before the game can offer it.

// A command object registered with 0xC7BD70 (thiscall: the object, name, description, type,
// handler); type 3 runs its handler, cdecl with no arguments, which reads its own arguments: with
// 0x28FF1C0 set it is being run (not asked for help), 0xC7D570(2, 0) reads a word into 0x28FF0B8.
// As the game's own "scriptx" command does (0x915890).
using RegisterCommandFn = void*(__fastcall*)(void* command, void* unused, const char* name, const char* description,
                                            int type, void (*handler)());
auto const RegisterCommand = reinterpret_cast<RegisterCommandFn>(0xC7BD70);
using ParseArgumentsFn = void(__cdecl*)(int kinds, int flags);
auto const ParseArguments = reinterpret_cast<ParseArgumentsFn>(0xC7D570);
auto& g_commandRunning = *reinterpret_cast<int*>(0x28FF1C0);
auto const g_commandWord = reinterpret_cast<const char*>(0x28FF0B8);
BYTE g_startCommand[32];

using ConsolePrintFn = void(__cdecl*)(const char* format, ...);
auto const ConsolePrint = reinterpret_cast<ConsolePrintFn>(0xC7C440);

void StartCommand() {
    if (!g_commandRunning) return;
    ParseArguments(2, 0);
    if (StartDemolitionsMaster(g_commandWord)) return;
    // Why not, in the console too: Guerrilla's demolitions_master_can_be_started (no activity running,
    // the activity's cooldown over, the alert green).
    char why[160];
    if (AnyRunning()) sprintf_s(why, "an activity is running");
    else if (g_cooldown > 0.0f) sprintf_s(why, "the activities' cooldown, %.0f s more (Guerrilla's: 180 s after one ends)", g_cooldown);
    else if (liberation::AlertLevel() != handles::AlertGreen) sprintf_s(why, "the alert level isn't green");
    else sprintf_s(why, "see logs\\rfa_mods.log");
    rfa::Log("rfg_dm_start: couldn't start \"%s\": %s", g_commandWord, why);
    ConsolePrint("rfg_dm_start: couldn't start \"%s\": %s\n", g_commandWord, why);
}

}  // namespace

// ---- Activity

bool Activity::StartSpawn(unsigned node, const BaseData* data, int district) {
    if (node == 0xFFFFFFFF || state_ != State::Wait || !CanInitialize()) return false;
    Reset();
    node_ = node;
    data_ = data;
    district_ = district;
    ChangeState(State::Loading);
    return true;
}

void Activity::Reset() {
    state_ = State::Wait;
    interface_ = Interface::None;
    node_ = 0xFFFFFFFF;
    data_ = nullptr;
    cleanedUp_ = victoryRecorded_ = active_ = false;
    shutdownTime_ = 0.0f;
}

void Activity::LevelInit() {
    disabled_ = 0;
    Reset();
    if (modal_ && g_current == mode_) g_current = ModeNone;
}

bool Activity::CanInitialize() const {
    if (disabled_ || (state_ != State::Wait && state_ != State::Loading)) return false;
    if (!modal_) return true;
    // Modal: after the cooldown; at RED only Guerrilla Express.
    if (g_cooldown > 0.0f) return false;
    return liberation::AlertLevel() < handles::AlertRed || mode_ == Delivery;
}

bool Activity::IsActive() const { return modal_ ? g_current == mode_ : active_; }

bool Activity::Initialize() {
    if (!CanInitialize()) return false;
    ChangeInterface(Interface::Ready);
    ChangeState(State::Ready);
    return true;
}

bool Activity::BeginStartUp() {
    if (state_ != State::Ready) return false;
    if (modal_)
        for (Activity* other : g_activities)
            if (other && other != this && other->modal_ && other->state_ != State::Wait && other->state_ != State::Shutdown) other->Kill();
    ChangeState(State::Starting);
    return true;
}

bool Activity::Start() {
    if (modal_) {
        if (g_current != ModeNone && g_current != mode_) return false;
        g_current = mode_;
    } else {
        active_ = true;
    }
    if (data_) liberation::SetAlertCap(data_->minAlertCap, data_->maxAlertCap);
    ChangeInterface(Interface::Running);
    ChangeState(State::Running);
    return true;
}

void Activity::End() {
    if (state_ == State::Wait || state_ == State::Shutdown) return;
    if (interface_ != Interface::Victory && interface_ != Interface::Loss) ChangeInterface(Interface::Killed);
    if (data_) liberation::SetAlertCap(handles::AlertGreen, handles::AlertRed);
    if (modal_) {
        if (g_current == mode_) g_current = ModeNone;
        StartCooldown();
    } else {
        active_ = false;
    }
    cleanedUp_ = false;
    ChangeState(State::Shutdown);
}

void Activity::CleanUpOnce() {
    cleanedUp_ = true;
    shutdownTime_ = modal_ ? kShutdownPeriod : 0.0f;
}

void Activity::FrameCleanUp(float seconds) {
    if (!cleanedUp_) CleanUpOnce();
    shutdownTime_ -= seconds;
}

bool Activity::CleanUpCompleted() const { return cleanedUp_ && shutdownTime_ <= 0.0f; }

// Back to WAIT; the scripts are told for the node that just ended, and the result stays on show.
void Activity::Complete() {
    const Interface shown = interface_;
    const float shownTime = interfaceTime_;
    const unsigned node = node_;
    Reset();
    interface_ = shown;  // (for kInterfaceHold)
    interfaceTime_ = shownTime;
    node_ = node;
    ChangeState(State::Wait);
    node_ = 0xFFFFFFFF;
}

void Activity::RecordVictory(const Rewards& rewards) {
    if (victoryRecorded_) return;
    victoryRecorded_ = true;
    ChangeInterface(Interface::Victory);
    // apply_standard_rewards: EDF control down, morale up, salvage, in the run's district.
    if (rewards.control != 0.0f) liberation::ChangeEdfControl(-rewards.control, district_);
    if (rewards.morale != 0.0f) liberation::ChangeMorale(rewards.morale, district_);
    if (rewards.salvage != 0) liberation::AddSalvage(rewards.salvage);
}

void Activity::RecordLoss() { ChangeInterface(Interface::Loss); }

void Activity::ChangeState(State state) {
    state_ = state;
    stateTime_ = 0.0f;
    if (node_ != 0xFFFFFFFF) words::PostActivityStatusChange(node_, static_cast<int>(state));
}

void Activity::ChangeInterface(Interface state) {
    interface_ = state;
    interfaceTime_ = 0.0f;
}

void Activity::Disable(DisableReason reason) {
    if (reason == Everything) return;
    disabled_ |= 1u << reason;
    if (state_ == State::Ready || state_ == State::Running) Kill();
}

void Activity::Enable(DisableReason reason) { disabled_ = reason == Everything ? 0 : disabled_ & ~(1u << reason); }

void Activity::DoFrame(float seconds) {
    stateTime_ += seconds;
    interfaceTime_ += seconds;
    if (disabled_) {
        if (state_ == State::Ready || state_ == State::Running) Kill();
        if (state_ != State::Shutdown) return;
    }
    switch (state_) {
    case State::Wait:
        FrameInactive(seconds);
        if (interface_ != Interface::None && interfaceTime_ > kInterfaceHold) ChangeInterface(Interface::None);
        break;
    case State::Loading:
        // Guerrilla waits here for the activity's layers, voice, restored objects and reserved
        // vehicles; none of those is ported yet, so the run is ready at once.
        if (!Initialize()) Kill();
        break;
    case State::Ready:
        FrameReady(seconds);
        break;
    case State::Starting:
        FrameStarting(seconds);
        if (state_ == State::Starting && StartingCompleted() && !Start()) Kill();
        break;
    case State::Running:
        FrameActive(seconds);
        break;
    case State::Shutdown:
        FrameCleanUp(seconds);
        if (CleanUpCompleted()) Complete();
        break;
    }
}

// ---- The framework

Activity* Get(Mode mode) { return mode > ModeNone && mode < kModes ? g_activities[mode] : nullptr; }
void Register(Activity* activity) { g_activities[activity->GetMode()] = activity; }
Mode CurrentMode() { return g_current; }
void SetCurrentMode(Mode mode) { g_current = mode; }

void StartCooldown() {
    const float cooldown = kCooldownBase + kCooldownPerReady * ReadyCount();
    g_cooldown = cooldown > kCooldownMax ? kCooldownMax : cooldown;
}

const BaseData* FindBaseData(const std::string& name) {
    for (const BaseData& data : g_baseData)
        if (_stricmp(data.name.c_str(), name.c_str()) == 0) return &data;
    return nullptr;
}

void Init() {
    LoadBaseData();
    Register(MakeDemolitionsMaster());
}

void Enter() {
    static bool commandAdded;
    if (!commandAdded) {
        commandAdded = true;
        RegisterCommand(g_startCommand, nullptr, "rfg_dm_start", "Starts one of Guerrilla's demolitions master challenges (je_dm_01..16)", 3,
                        &StartCommand);
    }
    g_cooldown = 0.0f;
    for (Activity* activity : g_activities)
        if (activity) activity->LevelInit();
}

void Update(float seconds) {
    if (g_cooldown > 0.0f) g_cooldown -= seconds;
    // At ORANGE or RED, offered modal activities are withdrawn when none is running (Guerrilla
    // spares Guerrilla Express).
    if (liberation::AlertLevel() >= handles::AlertOrange && g_current == ModeNone)
        for (Activity* activity : g_activities)
            if (activity && activity->Modal() && activity->GetMode() != Delivery) activity->Withdraw();
    for (Activity* activity : g_activities)
        if (activity) activity->DoFrame(seconds);
}

bool AnyRunning() {
    // Guerrilla's !activity_none(): a modal activity is current, or an offensive raid or a house
    // arrest is loading, offered or starting.
    if (g_current != ModeNone) return true;
    for (Mode mode : {RaidAttack, HouseArrest})
        if (Activity* activity = Get(mode))
            if (activity->GetState() >= State::Loading && activity->GetState() <= State::Starting) return true;
    return false;
}

bool AlertLocked() {
    for (Activity* activity : g_activities)
        if (activity && activity->LockAlertLevel()) return true;
    return false;
}

void Disable(DisableReason reason, bool free) {
    for (Activity* activity : g_activities)
        if (activity && activity->Modal() != free) activity->Disable(reason);
}

void DisableOffers(DisableReason reason) {
    for (Activity* activity : g_activities)
        if (activity && activity->Modal() && activity->GetMode() != Delivery && activity->GetMode() != DemolitionsMaster)
            activity->Disable(reason);
}

void Reinstate(DisableReason reason, bool free) {
    for (Activity* activity : g_activities)
        if (activity && activity->Modal() != free) activity->Enable(reason);
}

HudInfo Hud() {
    if (Activity* current = Get(g_current)) return current->Hud();
    return {};
}

}  // namespace campaign::activities

// ---- For the other mods (guerrilla_hud's minimap and world map).

// 1 while an activity or a mission runs (Guerrilla's !activity_none() || mission_is_active(): its
// minimap then hides the activities' starts, the safehouse and the missions' starts, and pins the
// important EDF buildings to the rim only when nothing runs). *activityMode: the current modal kind
// (Guerrilla's activity_modes: 11 demolitions master, 4 Guerrilla Express, ...; 0 none); *mission: 1
// while a mission runs (none is ported yet: 0). All 0 while the campaign's systems rest.
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_Running(int* activityMode, int* mission) {
    using namespace campaign;
    const bool playing = Playing();
    if (activityMode) *activityMode = playing ? static_cast<int>(activities::CurrentMode()) : 0;
    if (mission) *mission = 0;
    return playing && activities::AnyRunning() ? 1 : 0;
}

// 1 when the activity at a node is completed (its start icon turns grey): the node by Guerrilla's
// handle, as campaign_objects.xml has it. Only demolitions master keeps progress yet.
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_ActivityDone(unsigned node) {
    return campaign::activities::NodeDone(node) ? 1 : 0;
}

// The same by place, for a map icon that knows only where it is (map_icons.txt): the node within 2 m of
// (x, z) on the ground, or 0.
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_ActivityDoneAt(float x, float z) {
    const campaign::world::Object* node = campaign::world::Near(x, z, 2.0f);
    return node && campaign::activities::NodeDone(node->handle) ? 1 : 0;
}

// ---- For guerrilla_hud: the running activity's screens, its answer, the use prompt, the minimap's rings.

namespace {

// What GuerrillaCampaign_Activity fills (C, for the HUD). The texts are Guerrilla's string tags (or names),
// valid until the next call.
struct ActivityInfo {
    int mode;                // Guerrilla's activity_modes: 11 demolitions master; 0 none
    int screen;              // 0 none, 1 the briefing (accept or cancel), 2 running (the corner panel), 3 the results (close)
    const char* title;       // ACT_DEMO_MASTER_NAME
    const char* subtitle;    // the briefing's (Briefing_Destroy_Message)
    const char* body;        // the briefing's text (Demo_Info_Body_Text)
    const char* objective;   // the corner panel's (Corner_HUD_Destroy_Message)
    const char* timer;       // "mm:ss": the 5 s back into the area, else the time left, else the time so far
    const char* result;      // the results: the row's Success_String / Failure_String_Time / _Range / _Ammo / _Death
    const char* failure;     // the results, lost: MENU_YOU_DIED, ACTIVITY_FAIL_OUT_OF_TIME, _LEFT_AREA, _OUT_OF_AMMO
    const char* weapon[4];   // the briefing's weapons (weapons.xtbl names, or a layer prop's icon); null past the last
    int ammo[4];             // their rounds (-1: none shown)
    int timeLimit, parTime;  // seconds (limit 0: none)
    int bestMs, elapsedMs;   // the best time (0: none yet), this run's
    int status;              // running: 0, 1 unstable (out of ammo while collapsing), 2 destroyed
    int success, proTime;    // the results: won; done within the par time
    int outOfRange;          // running: the 5 s back into the area are counting
    int salvage;             // the results: salvage given
};

// A ring on the minimap's ground (CIRCLE_FILLED_OUTLINE).
struct MapRing {
    float x, z, radius;
    unsigned char r, g, b, a;
    int inverted;  // shade the outside
};

campaign::activities::Screen g_shown;  // what the last call gave (the texts point into it)

}  // namespace

extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_Activity(ActivityInfo* info) {
    using namespace campaign;
    if (!info) return 0;
    memset(info, 0, sizeof(*info));
    g_shown = {};
    if (!Playing() || !activities::DemolitionsScreen(g_shown) || g_shown.screen == 0) return 0;
    info->mode = static_cast<int>(g_shown.mode);
    info->screen = g_shown.screen;
    info->title = g_shown.title.c_str();
    info->subtitle = g_shown.subtitle.c_str();
    info->body = g_shown.body.c_str();
    info->objective = g_shown.objective.c_str();
    info->timer = g_shown.timer.c_str();
    info->result = g_shown.result.c_str();
    info->failure = g_shown.failure.c_str();
    for (size_t i = 0; i < 4; ++i) {
        info->weapon[i] = i < g_shown.weapons.size() ? g_shown.weapons[i].c_str() : nullptr;
        info->ammo[i] = i < g_shown.ammo.size() ? g_shown.ammo[i] : -1;
    }
    info->timeLimit = g_shown.timeLimit;
    info->parTime = g_shown.parTime;
    info->bestMs = g_shown.bestMs;
    info->elapsedMs = g_shown.elapsedMs;
    info->status = g_shown.status;
    info->success = g_shown.success;
    info->proTime = g_shown.proTime;
    info->outOfRange = g_shown.outOfRange;
    info->salvage = g_shown.salvage;
    return 1;
}

// The HUD's answer: the briefing, 1 accept or 0 cancel; the results, anything closes them.
extern "C" __declspec(dllexport) void __cdecl GuerrillaCampaign_ActivityChoose(int accept) { campaign::activities::DemolitionsChoose(accept); }

// The use prompt at the start of one of Guerrilla's activities: its icon (Guerrilla's use_object_icon; 1 the
// default hand), 0 none. The use key itself the campaign reads.
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_UsePrompt() {
    return campaign::Playing() ? campaign::activities::DemolitionsUsePrompt() : 0;
}

// The rings on the minimap's ground now (render_regions): demolitions master's play area, green (50, 200, 50, 80),
// red (200, 50, 50, 255) while the 5 s back into it are counting. How many were written.
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_MapRings(MapRing* rings, int most) {
    using namespace campaign;
    if (!rings || most <= 0 || !Playing()) return 0;
    activities::Screen screen;
    if (!activities::DemolitionsScreen(screen) || screen.screen != 2) return 0;
    MapRing& ring = rings[0];
    ring = {screen.center.x, screen.center.z, screen.range, 50, 200, 50, 80, 0};
    if (screen.outOfRange) ring = {screen.center.x, screen.center.z, screen.range, 200, 50, 50, 255, 0};
    return 1;
}
