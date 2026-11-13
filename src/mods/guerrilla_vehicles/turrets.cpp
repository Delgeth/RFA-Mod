// The gun on one of Guerrilla's vehicles while the player drives it: Guerrilla's rule. A part of
// guerrilla_vehicles.dll.
//
// Both games hand a vehicle's gun to the player who drives it when nobody else is on it: getting into the
// driver's seat of a vehicle whose first gun belongs to the first passenger's seat (the scout cars, APCs,
// staff cars and supply trucks with a ring on the roof) makes the driver its gunner (the setting
// Player_vehicle_turret_override, on in both), and a gun a driver owns aims by itself (should_autoaim:
// a vehicle's gun, not a tank's, whose gunner is a player in the driver's seat).
//
// What "by itself" means is where the games differ. Guerrilla (turret::autoaim): a walker's gun follows the
// player's aim point while it can reach it; any other gun looks for the best enemy ahead, and with none
// points straight ahead of the vehicle. The enemy is someone of an unfriendly team, alive, within 50 m, whom
// the gun can hit and who is within about 11 degrees of where the gun points now (or within 10 m and 14
// degrees); the nearest and most central wins, and the one fired at last is kept while the gun's keep-target
// timer runs. Armageddon dropped the walker test: every gun a driving player owns follows the player's aim
// point, which suits its own vehicles (the driver of its LAV or walker aims the gun with the camera).
//
// So for a vehicle that isn't one of Armageddon's own, the gun's aiming (0x830C70) is Guerrilla's again:
// the walker's rule, else the search below, which is the game's own (it still runs it for a gun nobody
// drives) with the same settings (Turret_autoaim_*, alike in both games' tweak tables). Armageddon's own
// vehicles keep Armageddon's rule. In the gunner's seat the gun follows the mouse, and the view stays on the gun,
// as Guerrilla's (see "The gunner's camera" below).
//
// RFA-Mod's own addition, not Guerrilla's: the upgrade TURRET LINK (bought with salvage like Guerrilla's
// own; guerrilla_gear sells it) hands the driver the aim: once bought, the gun follows the player's aim point
// on Guerrilla's vehicles too. See "TURRET LINK" below.
//
// The guns on Guerrilla's vehicles also get Guerrilla's hinge motors and Guerrilla's weights for their parts
// (see "The hinges' motors" and "What a part ... weighs" below): with Armageddon's the gun swung to and fro
// around where it was sent, and a turn or a bump threw it off.
//
// The log says when a vehicle's gun gets or loses its gunner, and who aims it, and for the first minute the
// player drives with a gun, four times a second, where it aims and where the gun points.
#define RFA_MOD_NAME "guerrilla_vehicles"
#include "rfa_mod.h"
#include "game.h"
#include <cstring>
#include <initializer_list>

namespace gv {

namespace {

// ---- The game's turret (an object): its info at +0x98 (its name first; its flags at +0xD0, bit 6 automated),
// its gunner's handle at +0x9C, its own flags at +0xD0 (bit 0 a vehicle's, bit 7 it has a target of its own
// choosing), where it aims at +0x10C, the keep-target timer at +0x15C and whom it keeps at +0x160. Its forward
// (+0x28) is where the gun points.
constexpr DWORD kTurretInfo = 0x98, kGunner = 0x9C, kTurretFlags = 0xD0, kAimAt = 0x10C, kKeepTimer = 0x15C, kKeepTarget = 0x160;
constexpr DWORD kInfoFlags = 0xD0;
constexpr BYTE kHasOwnTarget = 0x80, kAutomated = 0x40;
constexpr DWORD kPlayerAimPoint = 0x176C;  // the player: where the crosshair is (Guerrilla's aim_pos)
constexpr DWORD kVehicleInfo = 0x3DA8;      // a vehicle: its vehicles.xtbl entry, the name first
constexpr int kWalker = 8;

constexpr DWORD kAutoaim = 0x830C70;           // turret::autoaim
constexpr size_t kAutoaimPrologue = 9;         // push ebp / mov ebp,esp / sub esp,130h
constexpr DWORD kAttachGunner = 0x836C10;      // turret::attach_new_operator
constexpr DWORD kDetachGunner = 0x8370D0;      // turret::detach_operator
constexpr size_t kGunnerPrologue = 6;          // push ebp / mov ebp,esp / sub esp,58h (50h)
using AutoaimFn = void(__fastcall*)(BYTE* turret, void* unused);
using GunnerFn = void(__fastcall*)(BYTE* turret, void* unused, BYTE* human);
AutoaimFn g_autoaim;
GunnerFn g_attachGunner, g_detachGunner;

auto const AimsByItself = reinterpret_cast<bool(__fastcall*)(BYTE* turret, void* unused)>(0x831580);        // turret::should_autoaim
auto const TurretWeapon = reinterpret_cast<BYTE*(__fastcall*)(BYTE* turret, void* unused)>(0x834600);       // turret::get_weapon_inst
auto const TurretVehicle = reinterpret_cast<BYTE*(__fastcall*)(BYTE* turret, void* unused)>(0x8345C0);      // turret::get_vehicle
auto const CanHit = reinterpret_cast<bool(__fastcall*)(BYTE* turret, void* unused, const V3* where)>(0x8364B0);  // turret::can_hit_target
auto const WeaponOwner = reinterpret_cast<DWORD(__fastcall*)(BYTE* weapon, void* unused)>(0x6C9D20);        // a handle
auto const VehicleOfHuman = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human)>(0x866960);                      // human_in_vehicle
auto const SeatOf = reinterpret_cast<int(__cdecl*)(BYTE* vehicle, BYTE* human)>(0x603F90);                 // vehicle_passenger_get_seat
auto const IsPlayer = reinterpret_cast<bool(__cdecl*)(BYTE* human)>(0x7B2600);
auto const Team = reinterpret_cast<int(__fastcall*)(BYTE* human, void* unused)>(0x862B60);
auto const Unfriendly = reinterpret_cast<bool(__cdecl*)(int team, int other)>(0x468EA0);                   // human_teams_are_unfriendly
auto const HumanCount = reinterpret_cast<int(__fastcall*)(void* world, void* unused)>(0x43A860);
auto const HumanAt = reinterpret_cast<BYTE*(__fastcall*)(void* world, void* unused, int index)>(0x43A8B0);
auto const HeadPosition = reinterpret_cast<void(__cdecl*)(BYTE* human, V3* out)>(0x868A00);                // human_get_head_pos
auto const InProtectedSeat = reinterpret_cast<bool(__cdecl*)(BYTE* human)>(0x65EB70);                      // human_in_protected_vehicle_pos

// The settings (Turret_autoaim_max_distance, _close_distance, _min_dp, _close_dp, _angle_bias) and whether
// this is a multiplayer game.
float MaxDistance() { return *reinterpret_cast<const float*>(0x218BACC); }
float CloseDistance() { return *reinterpret_cast<const float*>(0x218BAC8); }
float MinDot() { return *reinterpret_cast<const float*>(0x218BAC0); }
float CloseDot() { return *reinterpret_cast<const float*>(0x218BABC); }
float AngleBias() { return *reinterpret_cast<const float*>(0x218BAB8); }
bool Multiplayer() { return *reinterpret_cast<const BYTE*>(0x2685118) != 0; }

// Armageddon's own vehicles with guns (its vehicles.xtbl): their drivers aim with the camera.
const char* const kArmageddons[] = {
    "walker_wreck", "Mar_Flyer_2", "Mar_Combug_1", "Mar_Combug_2", "Mar_Combug_3", "TestTurret", "RF_LAV_1", "RF_LAV_2",
    "Walker_Marauder_Heavy", "Walker_Marauder_Heavy_2", "Walker_Marauder_Heavy_8a", "Walker_Marauder_Heavy_8b", "light_walker_car",
    "Walker_Marauder_Light", "Walker_Marauder_Light_m1", "Cru_Tank_1", "Cru_Tank_2", "TestFlyer", "Behemoth", "Min_Rover_1"};

const char* Name(const BYTE* record) {
    const char* name = record ? *reinterpret_cast<const char* const*>(record) : nullptr;
    return name && *name ? name : "-";
}
const char* VehicleName(const BYTE* vehicle) { return vehicle ? Name(*reinterpret_cast<const BYTE* const*>(vehicle + kVehicleInfo)) : "-"; }
const char* TurretName(const BYTE* turret) { return Name(*reinterpret_cast<const BYTE* const*>(turret + kTurretInfo)); }

bool IsArmageddons(const BYTE* vehicle) {
    const char* name = VehicleName(vehicle);
    for (const char* own : kArmageddons)
        if (_stricmp(own, name) == 0) return true;
    return false;
}

bool IsDead(BYTE* object) {  // the object's own answer (its fifth virtual function)
    using Fn = bool(__fastcall*)(BYTE* self, void* unused);
    return (*reinterpret_cast<Fn**>(object))[4](object, nullptr);
}

float Dot(const V3& a, const V3& b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
V3 Minus(const V3& a, const V3& b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }
V3 Along(const V3& from, const V3& direction, float distance) {
    return {from.x + direction.x * distance, from.y + direction.y * distance, from.z + direction.z * distance};
}

// Guerrilla's search (turret::autoaim past its walker test; Armageddon's 0x830DE5 on is the same): the best
// enemy for the gun, or straight ahead of the vehicle.
void AimAsGuerrillas(BYTE* turret, BYTE* driver, BYTE* vehicle) {
    BYTE& flags = turret[kTurretFlags];
    flags &= static_cast<BYTE>(~kHasOwnTarget);
    V3& aimAt = *reinterpret_cast<V3*>(turret + kAimAt);
    const V3 pointing = *reinterpret_cast<const V3*>(turret + 0x28);
    // From the gun's place, moved onto the vehicle's centre line.
    const V3 right = *reinterpret_cast<const V3*>(vehicle + kOrientation);
    V3 from = Pos(turret);
    from = Along(from, right, -Dot(Minus(from, Pos(vehicle)), right));

    const float range = MaxDistance();
    int* keepTimer = reinterpret_cast<int*>(turret + kKeepTimer);
    const bool keeping = TimerSet(keepTimer, nullptr) && !TimerElapsed(keepTimer, nullptr);
    const DWORD kept = *reinterpret_cast<const DWORD*>(turret + kKeepTarget);
    const int team = Team(driver, nullptr);

    BYTE* best = nullptr;       // someone in the open: aimed at the head
    BYTE* bestInside = nullptr;  // someone inside a vehicle that shields them: aimed at the vehicle
    float bestScore = 3.4e38f, bestInsideScore = 3.4e38f;
    V3 bestAt{}, bestInsideAt{};
    const int count = HumanCount(kWorld, nullptr);
    for (int i = 0; i < count; ++i) {
        BYTE* human = HumanAt(kWorld, nullptr, i);
        if (!human || human == driver || IsDead(human) || !Unfriendly(team, Team(human, nullptr))) continue;
        V3 head{};
        HeadPosition(human, &head);
        V3 to = Minus(head, from);
        if (std::fabs(to.x) > range || std::fabs(to.z) > range) continue;
        const float distance = std::sqrt(Dot(to, to));
        if (distance > range) continue;
        if (distance > 0.0001f) to = {to.x / distance, to.y / distance, to.z / distance};
        const float ahead = Dot(to, pointing);
        if (ahead < MinDot() && (distance > CloseDistance() || ahead < CloseDot())) continue;
        if (!CanHit(turret, nullptr, &head)) continue;
        if (keeping && Handle(human) == kept) {
            best = human;
            bestAt = head;
            break;
        }
        const float score = distance - AngleBias() * ahead;
        if (InProtectedSeat(human)) {
            BYTE* theirs = VehicleByHandle(kWorld, nullptr, VehicleOf(human));
            if (theirs && score < bestInsideScore) {
                bestInsideScore = score;
                bestInsideAt = Pos(theirs);
                bestInside = human;
            }
        } else if (score < bestScore) {
            bestScore = score;
            bestAt = head;
            best = human;
        }
    }
    if (best) {
        aimAt = bestAt;
        flags |= kHasOwnTarget;
        *reinterpret_cast<DWORD*>(turret + kKeepTarget) = Handle(best);
    } else if (bestInside) {
        aimAt = bestInsideAt;
        flags |= kHasOwnTarget;
    } else {
        aimAt = Along(Pos(vehicle), Forward(vehicle), range);
    }
}

// ---- TURRET LINK: RFA-Mod's own upgrade (NOT one of Guerrilla's), sold by guerrilla_gear beside Guerrilla's in
// its F7 window and the safehouses' shop (its upgrades.cpp, kOwnUpgrades). Once bought, the gun of a vehicle
// the player drives follows his aim point, which is the game's own rule, in place of Guerrilla's. Asked of
// guerrilla_gear.dll twice a second while the player drives with a gun, so it takes hold in the vehicle, at
// once; without that mod, or before it is bought, Guerrilla's rule stays.
constexpr char kAimUpgrade[] = "rfa_turret_link";
bool AimUpgradeBought() {
    using LevelFn = int(__cdecl*)(const char* name);
    static LevelFn level = nullptr;
    static ULONGLONG askedAt = 0;
    static bool bought = false;
    const ULONGLONG now = GetTickCount64();
    if (askedAt != 0 && now - askedAt < 500) return bought;
    askedAt = now;
    if (!level)
        if (HMODULE gear = GetModuleHandleW(L"guerrilla_gear.dll")) level = reinterpret_cast<LevelFn>(GetProcAddress(gear, "GuerrillaGear_UpgradeLevel"));
    const bool has = level && level(kAimUpgrade) > 0;
    if (has != bought)
        rfa::Log(has ? "Turrets: TURRET LINK is bought (RFA-Mod's own upgrade): the gun of a vehicle the player drives follows his aim point from now on"
                     : "Turrets: TURRET LINK is no longer bought: a driver's gun aims by itself again, as Guerrilla's");
    bought = has;
    return bought;
}

// The driver whose gun this is, if Guerrilla's search is what it should do now; null leaves it to the game
// (nobody drives it, one of Armageddon's own vehicles, a walker's gun that can reach the player's aim point,
// or the player has bought TURRET LINK). `rule` says which it is, for the log.
BYTE* DriverForGuerrillasSearch(BYTE* turret, BYTE*& vehicle, const char*& rule) {
    rule = "the game's own rule (it isn't a gun that aims by itself in a single-player game)";
    if (Multiplayer() || !AimsByItself(turret, nullptr)) return nullptr;
    rule = "the game's own rule (an automated gun)";
    const BYTE* info = *reinterpret_cast<const BYTE* const*>(turret + kTurretInfo);
    if (!info || (info[kInfoFlags] & kAutomated)) return nullptr;
    rule = "the game's own rule (its weapon isn't the player's)";
    BYTE* weapon = TurretWeapon(turret, nullptr);
    BYTE* owner = weapon ? HumanByHandle(kWorld, nullptr, WeaponOwner(weapon, nullptr)) : nullptr;
    BYTE* player = Player();
    if (!owner || owner != player) return nullptr;
    rule = "the game's own rule (one of Armageddon's own vehicles, or none)";
    vehicle = VehicleOfHuman(owner);
    if (!vehicle || IsArmageddons(vehicle)) return nullptr;
    rule = "the game's own rule (TURRET LINK is bought: it follows the aim point)";
    if (AimUpgradeBought()) return nullptr;
    rule = "the game's own rule (a walker's gun that can reach the aim point)";
    if (VehicleSubtype(vehicle, nullptr) == kWalker && CanHit(turret, nullptr, reinterpret_cast<const V3*>(player + kPlayerAimPoint))) return nullptr;
    rule = "Guerrilla's rule";
    return owner;
}

// ---- The log: what the gun the player drives with does, four times a second for its first minute
// A turret's physics (0x4BE0D0 gives it), in radians: its gun's pitch at +0x98, its ring's heading at +0x9C
// (measured each frame from the ring's body, in the turret's own frame) and at +0xA0 the heading its ring's
// hinge is asked to hold (the same, counted through whole turns). A human: what its aim drifts by, heading at
// +0xABC and pitch at +0xAC0 (added to a gun it's on). The muzzle is what the game measures a player's gun
// from (calculate_aim_parameters, 0x834660).
constexpr DWORD kPitch = 0x98, kHeading = 0x9C, kAskedHeading = 0xA0, kHeadingDrift = 0xABC, kPitchDrift = 0xAC0;
constexpr int kWatchEvery = 250, kWatchLines = 240;
constexpr float kDegrees = 57.29578f;
auto const TurretPhysics = reinterpret_cast<BYTE*(__fastcall*)(BYTE* turret, void* unused)>(0x4BE0D0);
auto const Muzzle = reinterpret_cast<void(__fastcall*)(BYTE* weapon, void* unused, V3* where, float* orientation, int barrel)>(0x733BD0);  // weapon::get_muzzle_transform
DWORD g_watched = kNone;  // the gun's handle
int g_watchLines, g_nextWatch, g_lastWatch;
V3 g_lastPlace;
float g_lastHeading;

struct Angles { float right, up; };  // degrees
// A direction as seen from an object: how far right of its forward, and how far up.
Angles SeenFrom(const BYTE* object, const V3& direction, V3* parts = nullptr) {
    const V3 right = *reinterpret_cast<const V3*>(object + kOrientation), up = *reinterpret_cast<const V3*>(object + kOrientation + 12);
    const float x = Dot(direction, right), y = Dot(direction, up), z = Dot(direction, Forward(object));
    if (parts) *parts = {x, y, z};
    return {std::atan2(x, z) * kDegrees, std::atan2(y, std::sqrt(x * x + z * z)) * kDegrees};
}

void Watch(BYTE* turret, const char* rule) {
    if (Handle(turret) != g_watched || g_watchLines >= kWatchLines) return;
    const int now = GameTime();
    if (now < g_nextWatch) return;
    BYTE* vehicle = TurretVehicle(turret, nullptr);
    BYTE* weapon = TurretWeapon(turret, nullptr);
    BYTE* physics = TurretPhysics(turret, nullptr);
    BYTE* player = Player();
    if (!vehicle || !weapon || !physics || !player) return;
    V3 muzzle{};
    float gun[9] = {};
    Muzzle(weapon, nullptr, &muzzle, gun, -1);
    const V3 to = Minus(*reinterpret_cast<const V3*>(turret + kAimAt), muzzle);
    const Angles aims = SeenFrom(vehicle, to), points = SeenFrom(vehicle, {gun[6], gun[7], gun[8]}), mount = SeenFrom(vehicle, Forward(turret));
    V3 out{};
    SeenFrom(vehicle, Minus(muzzle, Pos(turret)), &out);
    const V3 place = Pos(vehicle), nose = Forward(vehicle);
    const float heading = std::atan2(nose.x, nose.z) * kDegrees;
    const float seconds = g_lastWatch ? (now - g_lastWatch) / 1000.0f : 0.0f;
    float turned = heading - g_lastHeading;
    if (turned > 180.0f) turned -= 360.0f;
    if (turned < -180.0f) turned += 360.0f;
    const float speed = seconds > 0.0f ? Distance(place, g_lastPlace) / seconds : 0.0f, turning = seconds > 0.0f ? turned / seconds : 0.0f;
    g_lastWatch = now;
    g_lastPlace = place;
    g_lastHeading = heading;
    g_nextWatch = now + kWatchEvery;
    ++g_watchLines;
    rfa::Log("Turrets: watch %d, %s: it aims %.1f m from the muzzle, %+.1f right %+.1f up of the car's nose%s; the gun points %+.1f right %+.1f up; the game has "
             "its ring at %+.1f (asked for %+.1f) and its gun raised %+.1f; its mount faces %+.1f right %+.1f up; the muzzle is %+.2f right %+.2f up %+.2f ahead of "
             "the mount; the car does %.1f m/s, turning %+.0f a second; the driver's aim drift %+.2f %+.2f",
             g_watchLines, rule, std::sqrt(Dot(to, to)), aims.right, aims.up, (turret[kTurretFlags] & kHasOwnTarget) ? " (at an enemy)" : "", points.right, points.up,
             *reinterpret_cast<const float*>(physics + kHeading) * kDegrees, *reinterpret_cast<const float*>(physics + kAskedHeading) * kDegrees,
             *reinterpret_cast<const float*>(physics + kPitch) * kDegrees, mount.right, mount.up, out.x, out.y, out.z, speed, turning,
             *reinterpret_cast<const float*>(player + kHeadingDrift) * kDegrees, *reinterpret_cast<const float*>(player + kPitchDrift) * kDegrees);
}

void __fastcall Autoaim(BYTE* turret, void* unused) {
    BYTE* vehicle = nullptr;
    const char* rule = "";
    BYTE* driver = DriverForGuerrillasSearch(turret, vehicle, rule);
    if (driver) AimAsGuerrillas(turret, driver, vehicle);
    else g_autoaim(turret, unused);
    if (g_watched != kNone) Watch(turret, rule);
}

// ---- The log: a vehicle's gun getting and losing its gunner
void __fastcall AttachGunner(BYTE* turret, void* unused, BYTE* human) {
    g_attachGunner(turret, unused, human);
    BYTE* vehicle = turret ? TurretVehicle(turret, nullptr) : nullptr;
    if (!vehicle || !human) return;
    const bool player = IsPlayer(human);
    const int seat = SeatOf(vehicle, human);
    if (player && AimsByItself(turret, nullptr)) {  // the player drives with it: watch it for a minute
        g_watched = Handle(turret);
        g_watchLines = g_nextWatch = g_lastWatch = 0;
    }
    const char* aims = !AimsByItself(turret, nullptr) ? "the gunner aims it"
                       : IsArmageddons(vehicle)       ? "it follows the player's aim point (Armageddon's rule for its own vehicles)"
                       : player && AimUpgradeBought() ? "it follows the player's aim point (TURRET LINK is bought)"
                       : VehicleSubtype(vehicle, nullptr) == kWalker ? "it follows the player's aim point while it can reach it, else it aims by itself (Guerrilla's walkers)"
                                                                     : "it aims by itself: enemies ahead, else straight ahead (Guerrilla's rule for a driver)";
    // The player's lines always (a test's evidence), NPCs' only a few: traffic's gunners come and go all the time.
    if (player)
        RFA_LOG_FIRST(200, "Turrets: %s on %s has a gunner: the player in seat %d (0 the driver's); %s", TurretName(turret), VehicleName(vehicle), seat, aims);
    else
        RFA_LOG_FIRST(20, "Turrets: %s on %s has a gunner: an NPC in seat %d; %s", TurretName(turret), VehicleName(vehicle), seat, aims);
}

void __fastcall DetachGunner(BYTE* turret, void* unused, BYTE* human) {
    BYTE* vehicle = turret ? TurretVehicle(turret, nullptr) : nullptr;
    BYTE* gunner = turret ? HumanByHandle(kWorld, nullptr, *reinterpret_cast<const DWORD*>(turret + kGunner)) : nullptr;
    const bool player = gunner && IsPlayer(gunner);
    if (turret && Handle(turret) == g_watched) g_watched = kNone;
    g_detachGunner(turret, unused, human);
    if (!vehicle || !gunner) return;
    if (player)
        RFA_LOG_FIRST(200, "Turrets: %s on %s lost its gunner (the player); it goes back to its middle for 10 s, then holds where it is", TurretName(turret),
                      VehicleName(vehicle));
    else
        RFA_LOG_FIRST(20, "Turrets: %s on %s lost its gunner (an NPC)", TurretName(turret), VehicleName(vehicle));
}

// ---- The hinges' motors of a gun on one of Guerrilla's vehicles: Guerrilla's
// A turret's ring and gun each turn on a hinge whose motor pulls it to the angle the game asks for (Havok's
// position motor; the game makes both in assemble_components, 0x83AFA0, with build_pos_motor, 0x83B8A0).
// Both games give the motor the same strength (the entry's Horsepower × 745.7), stiffness (1) and damping
// (0.75), but not the same speed for making up an error: Guerrilla's 0.1 × the error and nothing constant,
// Armageddon's 1.0 × the error plus a constant 2. Armageddon's own turrets are written for that with 100 to
// 10000 horsepower; Guerrilla's ring guns have 2, and at Armageddon's speeds such a motor can't stop the gun
// where it's sent: the gun swings past, back and past again (the user's "sways left and right": 5 degrees
// either way parked, 25 to 100 once the car moves). So Guerrilla's vehicles' guns get Guerrilla's two numbers.
constexpr DWORD kAssemble = 0x83AFA0;                               // assemble_components(physics, turret, vehicle)
constexpr size_t kAssemblePrologue = 9;                             // push ebp / mov ebp,esp / sub esp,0A0h
constexpr DWORD kRingMotor = 0x50, kGunMotor = 0x78;                // a turret's physics: its two hinges' position motors
constexpr DWORD kRecoveryByError = 0x1C, kRecoveryConstant = 0x20;  // a position motor: how fast it makes up an error
constexpr float kGuerrillasRecoveryByError = 0.1f, kGuerrillasRecoveryConstant = 0.0f;
using AssembleFn = bool(__cdecl*)(BYTE* physics, BYTE* turret, BYTE* vehicle);
AssembleFn g_assemble;

// ---- What a part of a gun on one of Guerrilla's vehicles weighs: Guerrilla's
// A turret part made from an item (a top, or a middle or base that isn't one of the vehicle's own components)
// gets a body of its own. On a vehicle Guerrilla weighs that body as the turret's entry says (the part's Mass,
// at least 5 kg), whatever the item weighs by itself (create_item_component). Armageddon weighs it as the item
// (0x83CBC0, which fills in what the body is made from: an item's own mass comes first, the entry's only
// without an item). Guerrilla's ring guns name no weight for their top, so there the gun is 5 kg; with
// Armageddon's rule it was the item's 100 kg, hung half a metre off the ring's axis on a 2-horsepower hinge:
// in a hard turn or over a bump it swung the ring round or dropped its nose, and stayed there.
constexpr DWORD kPartBody = 0x83CBC0;
constexpr size_t kPartBodyPrologue = 6;  // push ebx / mov ebx,esp / sub esp,8
constexpr DWORD kBodyMass = 0x6C;        // in what a body is made from: its mass
constexpr float kLightestPart = 5.0f;
// The place and the nine numbers of the orientation go through as they came.
using PartBodyFn = void(__cdecl*)(BYTE* body, BYTE* item, void* mesh, DWORD x, DWORD y, DWORD z, DWORD r0, DWORD r1, DWORD r2, DWORD r3, DWORD r4, DWORD r5,
                                  DWORD r6, DWORD r7, DWORD r8, DWORD other, int onVehicle, float entryMass);
PartBodyFn g_partBody;
BYTE* g_assembling;  // the turret of one of Guerrilla's vehicles the game is putting together now

void __cdecl PartBody(BYTE* body, BYTE* item, void* mesh, DWORD x, DWORD y, DWORD z, DWORD r0, DWORD r1, DWORD r2, DWORD r3, DWORD r4, DWORD r5, DWORD r6,
                      DWORD r7, DWORD r8, DWORD other, int onVehicle, float entryMass) {
    g_partBody(body, item, mesh, x, y, z, r0, r1, r2, r3, r4, r5, r6, r7, r8, other, onVehicle, entryMass);
    if (!g_assembling || !body || !item || !onVehicle) return;
    float& mass = *reinterpret_cast<float*>(body + kBodyMass);
    const float items = mass, guerrillas = entryMass > kLightestPart ? entryMass : kLightestPart;
    mass = guerrillas;
    RFA_LOG_FIRST(40, "Turrets: %s: its part %s weighs %g kg as Guerrilla's (its entry's %g, at least %g), not the item's own %g", TurretName(g_assembling),
                  Name(item), guerrillas, entryMass, kLightestPart, items);
}

bool __cdecl Assemble(BYTE* physics, BYTE* turret, BYTE* vehicle) {
    const char* on = vehicle ? VehicleName(vehicle) : "-";
    const bool guerrillas = turret && on[0] != '-' && !IsArmageddons(vehicle);
    g_assembling = guerrillas ? turret : nullptr;
    const bool made = g_assemble(physics, turret, vehicle);
    g_assembling = nullptr;
    if (!made || !physics || !guerrillas) return made;
    float was[2] = {-1.0f, -1.0f};
    int set = 0;
    for (DWORD part : {kRingMotor, kGunMotor}) {
        BYTE* motor = *reinterpret_cast<BYTE* const*>(physics + part);
        if (!motor) continue;
        if (!set) {
            was[0] = *reinterpret_cast<const float*>(motor + kRecoveryByError);
            was[1] = *reinterpret_cast<const float*>(motor + kRecoveryConstant);
        }
        *reinterpret_cast<float*>(motor + kRecoveryByError) = kGuerrillasRecoveryByError;
        *reinterpret_cast<float*>(motor + kRecoveryConstant) = kGuerrillasRecoveryConstant;
        ++set;
    }
    RFA_LOG_FIRST(30, "Turrets: %s on %s: %d hinge motors make up an error as Guerrilla's (%.2g of it a second and %.2g; the game's were %.2g and %.2g)",
                  TurretName(turret), on, set, kGuerrillasRecoveryByError, kGuerrillasRecoveryConstant, was[0], was[1]);
    return made;
}

bool Opens(DWORD address, const BYTE* opening, size_t size) { return memcmp(reinterpret_cast<const void*>(address), opening, size) == 0; }

void StartHinges() {
    if (!rfa::IniInt(L"GuerrillaVehicles", L"TurretHingesAsGuerrilla", 1)) {
        rfa::Log("Turrets: the guns' hinge motors on Guerrilla's vehicles stay Armageddon's ([GuerrillaVehicles] TurretHingesAsGuerrilla=0)");
        return;
    }
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0xA0, 0x00, 0x00, 0x00};
    const BYTE partOpening[] = {0x53, 0x8B, 0xDC, 0x83, 0xEC, 0x08, 0x83, 0xE4, 0xF0};
    if (Opens(kAssemble, opening, sizeof(opening)))
        g_assemble = reinterpret_cast<AssembleFn>(rfa::HookFunction(kAssemble, kAssemblePrologue, reinterpret_cast<void*>(&Assemble)));
    rfa::Log(g_assemble ? "Turrets: the guns on Guerrilla's vehicles get Guerrilla's hinge motors (they hold the gun where it's sent)"
                        : "Turrets: the game's turret assembly isn't as expected; the guns' hinge motors and parts' weights stay Armageddon's");
    if (!g_assemble) return;
    if (Opens(kPartBody, partOpening, sizeof(partOpening)))
        g_partBody = reinterpret_cast<PartBodyFn>(rfa::HookFunction(kPartBody, kPartBodyPrologue, reinterpret_cast<void*>(&PartBody)));
    rfa::Log(g_partBody ? "Turrets: the guns' parts on Guerrilla's vehicles weigh what Guerrilla gives them (the turret's entry's mass, at least 5 kg)"
                        : "Turrets: the game's turret part bodies aren't as expected; the parts weigh what their items do, as Armageddon's");
}

// ---- The gunner's camera: Guerrilla's follows the gun
//
// Guerrilla (camera_turret_process RM 0x6DEA50): the mouse turns the gun itself (camera_turret_set_rotation →
// turret::set_turret_velocity, at the turret's own speeds and within its limits), camera_aim_turret puts the aim
// point along the muzzle, and turret_update_camera_post (RM 0x6DE030) places the camera from the gun's muzzle
// with the turret entry's offset (Default_Offset, Fine_Aim_Offset), looking where the gun points: the view never
// leaves the gun. Armageddon (its turret camera mode's frame 0x4BD350: camera_aim_turret 0x4BE320, the player's
// camera controls 0x8C1D70, then 0x4BD510) keeps the offsets but turns it round: the mouse turns the camera's
// look direction (0x11B9CA8), and while the gun runs on velocity motors (0x8C6A10) 0x4BD510 sets their speeds
// to close the gap between that look and the gun (0x83F6E0) in one frame. The camera can then look where the
// gun can't go (below its lower limit, or while it lags behind the mouse), and the view comes loose from the
// gun (the user's screenshot: the camera at Mason's head looking down at the ground, the gun pointing up).
//
// So on Guerrilla's vehicles, after 0x4BD510 has set this frame's speeds, the look direction is kept within
// kGunLead of where the gun points (as 0x4BE120 measures it: from the camera to the gun's aim point, asked
// without its smoothing, which it only moves when told). The gap still drives the gun, so the mouse aims as
// before, but the view stays on the gun, at its limits too, as Guerrilla's.
constexpr DWORD kTurretCameraPost = 0x4BD510;   // the turret camera's placing (Guerrilla's turret_update_camera_post)
constexpr size_t kTurretCameraPostPrologue = 9; // push ebp / mov ebp,esp / sub esp,1ACh
constexpr DWORD kCameraLook = 0x11B9CA8;        // the camera's look direction, which the mouse turns
constexpr DWORD kCameraMatrix = 0x11B9CB4;      // the camera's orientation (right, up, forward)
constexpr float kGunLead = 2.0f * 3.14159265f / 180.0f;  // how far the view may lead the gun (radians)
auto const MannedTurret = reinterpret_cast<BYTE*(__cdecl*)(BYTE* human)>(0x881ED0);           // human_combat_get_manned_turret
auto const GunSeenFromCamera = reinterpret_cast<bool(__cdecl*)(V3* out, BYTE* turret, bool smooth)>(0x4BE120);
auto const GunOnVelocityMotors = reinterpret_cast<bool(__cdecl*)()>(0x8C6A10);
auto const MatrixFromForward = reinterpret_cast<void(__fastcall*)(void* matrix, void* unused, const V3* forward)>(0xC9DA10);
using CameraPostFn = void(__cdecl*)();
CameraPostFn g_turretCameraPost;

float Length(const V3& v) { return std::sqrt(Dot(v, v)); }

struct CameraWatch {
    int next = 0;    // when to log next (the game's clock)
    int lines = 0;
    float most = 0;  // the widest gap since the last line (degrees)
    int held = 0;    // frames held to the gun since the last line
    int frames = 0;
} g_cameraWatch;

void __cdecl TurretCameraPost() {
    g_turretCameraPost();
    BYTE* player = Player();
    BYTE* turret = player ? MannedTurret(player) : nullptr;
    BYTE* vehicle = turret ? TurretVehicle(turret, nullptr) : nullptr;
    if (!vehicle || IsArmageddons(vehicle) || !GunOnVelocityMotors()) return;
    V3 gun{};
    if (!GunSeenFromCamera(&gun, turret, false)) return;
    V3& look = *reinterpret_cast<V3*>(kCameraLook);
    const float lookLength = Length(look), gunLength = Length(gun);
    if (lookLength < 1e-4f || gunLength < 1e-4f) return;
    const V3 l{look.x / lookLength, look.y / lookLength, look.z / lookLength}, g{gun.x / gunLength, gun.y / gunLength, gun.z / gunLength};
    float cosine = Dot(l, g);
    cosine = cosine > 1 ? 1 : (cosine < -1 ? -1 : cosine);
    const float gap = std::acos(cosine);
    CameraWatch& w = g_cameraWatch;
    ++w.frames;
    if (gap * 57.29578f > w.most) w.most = gap * 57.29578f;
    if (gap > kGunLead) {
        // The look turned from the gun's way towards its own by kGunLead (on the great circle between them).
        const V3 across{l.x - g.x * cosine, l.y - g.y * cosine, l.z - g.z * cosine};
        const float acrossLength = Length(across);
        V3 held = g;
        if (acrossLength > 1e-5f) {
            const float c = std::cos(kGunLead), s = std::sin(kGunLead);
            held = {g.x * c + across.x / acrossLength * s, g.y * c + across.y / acrossLength * s, g.z * c + across.z / acrossLength * s};
        }
        look = {held.x * lookLength, held.y * lookLength, held.z * lookLength};
        MatrixFromForward(reinterpret_cast<void*>(kCameraMatrix), nullptr, &held);
        ++w.held;
    }
    const int now = GameTime();
    if (now >= w.next && w.lines < 120) {
        if (w.next) {
            rfa::Log("Turrets: the gunner's camera on %s: the view led the gun by up to %.1f degrees in the last second; held to the gun in %d of %d frames",
                     VehicleName(vehicle), w.most, w.held, w.frames);
            ++w.lines;
        }
        w.next = now + 1000;
        w.most = 0;
        w.held = w.frames = 0;
    }
}

void StartGunnerCamera() {
    if (!rfa::IniInt(L"GuerrillaVehicles", L"GunnerCameraAsGuerrilla", 1)) {
        rfa::Log("Turrets: the gunner's camera looks where the mouse turns it, as Armageddon's ([GuerrillaVehicles] GunnerCameraAsGuerrilla=0)");
        return;
    }
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0xAC, 0x01, 0x00, 0x00};
    if (Opens(kTurretCameraPost, opening, sizeof(opening)))
        g_turretCameraPost = reinterpret_cast<CameraPostFn>(rfa::HookFunction(kTurretCameraPost, kTurretCameraPostPrologue, reinterpret_cast<void*>(&TurretCameraPost)));
    rfa::Log(g_turretCameraPost ? "Turrets: on Guerrilla's vehicles the gunner's camera stays on the gun, as Guerrilla's (it leads it by at most 2 degrees)"
                                : "Turrets: the game's turret camera isn't as expected; the gunner's camera stays Armageddon's");
}

}  // namespace

void StartTurrets() {
    StartHinges();
    StartGunnerCamera();
    if (!rfa::IniInt(L"GuerrillaVehicles", L"DriverGunAsGuerrilla", 1)) {
        rfa::Log("Turrets: a driver's gun follows the player's aim point on every vehicle, as Armageddon's ([GuerrillaVehicles] DriverGunAsGuerrilla=0)");
        return;
    }
    const BYTE autoaimOpening[] = {0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x30, 0x01, 0x00, 0x00};
    const BYTE attachOpening[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x58};
    const BYTE detachOpening[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x50};
    if (!Opens(kAutoaim, autoaimOpening, sizeof(autoaimOpening))) {
        rfa::Log("Turrets: the game's turret aiming isn't as expected; a driver's gun keeps Armageddon's rule");
        return;
    }
    g_autoaim = reinterpret_cast<AutoaimFn>(rfa::HookFunction(kAutoaim, kAutoaimPrologue, reinterpret_cast<void*>(&Autoaim)));
    if (!g_autoaim) {
        rfa::Log("Turrets: could not hook the turret aiming; a driver's gun keeps Armageddon's rule");
        return;
    }
    rfa::Log("Turrets: on Guerrilla's vehicles a driver's gun aims by itself as Guerrilla's (enemies ahead, else straight ahead; a walker's follows the aim point), "
             "until the player buys TURRET LINK (RFA-Mod's own upgrade): then it follows his aim point");
    if (Opens(kAttachGunner, attachOpening, sizeof(attachOpening)) && Opens(kDetachGunner, detachOpening, sizeof(detachOpening))) {
        g_attachGunner = reinterpret_cast<GunnerFn>(rfa::HookFunction(kAttachGunner, kGunnerPrologue, reinterpret_cast<void*>(&AttachGunner)));
        if (g_attachGunner) g_detachGunner = reinterpret_cast<GunnerFn>(rfa::HookFunction(kDetachGunner, kGunnerPrologue, reinterpret_cast<void*>(&DetachGunner)));
    }
    if (!g_attachGunner || !g_detachGunner) rfa::Log("Turrets: gunners coming and going aren't logged");
}

}  // namespace gv
