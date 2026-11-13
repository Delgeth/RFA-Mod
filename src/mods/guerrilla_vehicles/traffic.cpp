// Guerrilla's traffic in its world: its ambient vehicle spawner (amb_spawn's do_frame_spawn_vehicle and
// spawn_single_vehicle, despawn), which Armageddon deleted, ported onto the code Armageddon kept. A part of
// guerrilla_vehicles.dll. GuerrillaBuilder.exe writes what it needs: mods\guerrilla_vehicles\traffic.txt (each
// zone's vehicle group, its traffic's density by day and by night, its spawn resource; each group's
// vehicles) and mods\guerrilla_people\crews.txt (each resource's people by side, the people part's).
//
// Spawning, as Guerrilla's: every 250 to 500 ms (2 to 4 s after a spawn), in the world named in
// [GuerrillaVehicles] TrafficWorlds= (rfg), while the player's zone has traffic: up to its cap (Guerrilla's
// table at green alert: none, low 4, medium 6, high 10; the night's density from 18:00 to 6:00 by the game
// clock), a car of the zone's group on an ambient road (a road whose file flag 1 is set: Armageddon keeps it
// as bit 2 of the road's flags, +0xB4) 60 to 260 m from the player, not within 30 m of another, facing
// along the road on its right side; a model already streamed in first, else one the game is asked to
// stream (guerrilla_vehicles' spawn reserves it). Its people: 1 to 3 (as many as it has seats) of the zone's
// resource's civilians and guerrillas (the people the game streams there), made in the car's place
// (0x762D50, drop_human's) and put straight into their seats (vehicle_interaction_request_enter 0x656410,
// teleporting, as Guerrilla's do_npc_spawn_vehicle: the driver first).
//
// Driving: Armageddon has no order or goal that drives an NPC along roads to a place (drive_to_location's
// flags are read by nothing), so the mod drives its traffic's drivers itself every frame, as Guerrilla's
// ambient_veh_drive_action did: each occupant's AI told to sit this frame out (its ai_system's
// yield_next_frame, +0x651 bit 2: the planner, perception and firing skip it; death doesn't), the car's AI
// in its pathfind state at 9 m/s (AMBIENT_VEHICLE_SPEED), and ai_vehicle_travel_to_a_point_do_frame
// (0x4B18E0, Guerrilla's 0x68B1F0) toward a road destination 150 m or more ahead (the game's own
// ai_vehicle_pick_flee_destination_road, 0x4B2BD0: a flood along the roads), with roads on (it routes along
// them: the road splines' A*, lanes, the vehicle navigation at the ends), stopping 10 m short, a new
// destination within 20 m of it. A car whose driver is gone or out (shot, pulled out, the player took it)
// is handed back to the game.
//
// Reactions, as Guerrilla's: the drivers count as ambient (npc +0x1700 = 1, NPC_MANAGER_AMBIENT, which
// Armageddon never sets), so the game's own avoidance inside travel_to sets Guerrilla's ambient waits (a
// person or the player ahead, the player's car, a queue: 500 to 1500 ms; vehicle AI +0x3F1C, the blocker at
// +0x3F18); while one runs the car brakes (vehicle_set_controls_when_abandoned 0x610820) and may honk
// (civilian_horn_do_honk's rule: 20% a honk of 500 to 2000 ms, else 10 to 15 s before the next roll). A
// driver with a flee belief (ai_get_flee_to_safety_belief 0x483C90: an explosion, damage, a bullet impact,
// combat sounds within 10 s, 30 s for civilians; ai_get_enemy_flee_belief 0x483B00) drives off at its
// car's full speed (the 9 m/s cap cleared), away from the threat, stopping only for the player on foot.
// A car on fire, wrecked or flipped: its people get out (at once if it burns, else once it's nearly
// stopped: 1.5 m/s) and run (npc::start_active_despawn 0x8AAEC0, Armageddon's own "run, then vanish").
// No new car while the world has more than 90 people or 28 cars (Guerrilla's caps). Despawning, as
// Guerrilla's should_despawn_vehicle: a car more than 60 m away whose driver has been out of view 5 s,
// 20 s after it was made (10 s if it's beyond the 260 m ring); any car 280 m away; its people first.
#define RFA_MOD_NAME "guerrilla_vehicles"
#include "rfa_mod.h"
#include "crews.h"
#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

void StartTraffic();
void ReportSpawnPools();  // guerrilla_vehicles.cpp: what the spawn pools hold, for the log

namespace {

using namespace gv;

auto const HonkHorn = reinterpret_cast<void(__cdecl*)(DWORD vehicle)>(0x5FB030);     // automobile_honk_horn (a live automobile's handle)
auto const ReleaseHorn = reinterpret_cast<void(__cdecl*)(DWORD vehicle)>(0x5FB110);  // automobile_release_horn
constexpr float kSpeed = 9.0f, kMinSpawn = 60.0f, kMaxSpawn = 260.0f, kApart = 30.0f, kDespawn = 280.0f, kReached = 20.0f, kStopShort = 10.0f;
constexpr int kCaps[4] = {0, 4, 6, 10};  // Guerrilla's at green alert (max_vehicle_count 0xB41110), by density
// The world's counts by kind (World +0x308 + kind × 12, the count at +8): people (2) and automobiles (5).
constexpr DWORD kPeopleCount = 0x1870958, kCarCount = 0x187097C;
constexpr int kMostPeople = 90, kMostCars = 28;
constexpr float kHonkChance = 0.2f, kBailSpeed = 1.5f, kNear = 60.0f;

struct Car {
    DWORD vehicle;
    std::vector<Person> people;  // the driver first
    std::vector<std::string> waiting;  // people not made yet
    V3 destination{};
    bool hasDestination = false, driving = false;
    int failures = 0;  // in a row
    int since = 0;     // the game's clock
    // On the game's clock.
    int made = 0, outOfViewSince = -1, flippedSince = -1;
    int horn = -1;  // the horn rule's timer
    bool hornDown = false, fleeing = false, bailing = false;
    V3 lastPosition{};
    int lastTime = -1;
    float speed = 0;  // m/s, from its last frame's move
};
std::vector<Car> g_cars;
struct Pending {
    BYTE parameters[0x100];  // vehicle_spawn_params (0x60 bytes and more)
    std::string resource, model;
    int since;
};
std::vector<Pending> g_pending;
int g_nextSpawn = 0;

// A model of one of traffic's vehicle groups.
BYTE* PickFromGroup(const std::string& group) {
    for (const auto& [name, vehicles] : Files().groups)
        if (_stricmp(name.c_str(), group.c_str()) == 0) return PickVehicle(vehicles);
    return nullptr;
}

void TrySpawn(const BYTE* player, const Zone& zone) {
    const bool night = [] { const int hour = GameHour(); return hour >= 18 || hour < 6; }();
    const int density = std::clamp(night ? zone.night : zone.day, 0, 3);
    if (static_cast<int>(g_cars.size() + g_pending.size()) >= kCaps[density]) return;
    // Guerrilla's caps on the whole world (do_frame_spawn_vehicle): more than 90 people or 28 cars, no new car.
    const int people = *reinterpret_cast<const int*>(kPeopleCount), cars = *reinterpret_cast<const int*>(kCarCount);
    RFA_LOG_FIRST(1, "Traffic: the world has %d people and %d cars (no new car above %d or %d)", people, cars, kMostPeople, kMostCars);
    const bool counted = people >= 0 && people < 100000 && cars >= 0 && cars < 100000;
    if (counted && (people > kMostPeople || cars > kMostCars)) {
        RFA_LOG_FIRST(5, "Traffic: no new car: the world has %d people and %d cars", people, cars);
        return;
    }
    const V3 at = Pos(player);
    for (int attempt = 0; attempt < 6; ++attempt) {
        const float angle = Random() * 6.2831853f, distance = kMinSpawn + Random() * (kMaxSpawn - kMinSpawn);
        V3 where, forward;
        if (!RoadPlace({at.x + std::cos(angle) * distance, at.y, at.z + std::sin(angle) * distance}, where, forward)) continue;
        const float fromPlayer = Distance(where, at);
        if (fromPlayer < kMinSpawn || fromPlayer > kMaxSpawn) continue;
        bool crowded = false;
        for (const Car& car : g_cars)
            if (BYTE* vehicle = VehicleByHandle(kWorld, nullptr, car.vehicle))
                if (Distance(Pos(vehicle), where) < kApart) crowded = true;
        if (crowded) continue;
        BYTE* entry = PickFromGroup(zone.group);
        if (!entry) return;
        Pending pending{};
        MakeSpawnParameters(pending.parameters, nullptr);
        *reinterpret_cast<BYTE**>(pending.parameters + 0x00) = entry;
        *reinterpret_cast<V3*>(pending.parameters + 0x04) = where;
        Orientation(forward, reinterpret_cast<float*>(pending.parameters + 0x18));
        *reinterpret_cast<int*>(pending.parameters + 0x3C) = -1;          // any variant
        *reinterpret_cast<float*>(pending.parameters + 0x40) = kSpeed;    // already moving
        *reinterpret_cast<int*>(pending.parameters + 0x44) = -1;          // no parent
        *reinterpret_cast<int*>(pending.parameters + 0x4C) = 3;           // priority, as drop_car's
        *reinterpret_cast<DWORD*>(pending.parameters + 0x58) = 0x40B;     // Guerrilla's: no save, one attempt, on a road, moving
        pending.resource = zone.resource;
        if (const char* name = *reinterpret_cast<const char* const*>(entry)) pending.model = name;
        pending.since = GameTime();
        g_pending.push_back(pending);
        return;
    }
}

void MadeCar(BYTE* vehicle, const std::string& resource, const std::string& model) {
    Car car;
    car.vehicle = Handle(vehicle);
    car.since = GameTime();
    car.made = GameTime();
    if (const auto* people = People(resource, "Guerilla")) {  // the zone's civilians and guerrillas
        const int seats = (std::max)(1, Seats(vehicle));
        const int count = (std::min)(seats, 1 + std::rand() % 3);
        for (int i = 0; i < count; ++i) car.waiting.push_back((*people)[std::rand() % people->size()]);
    }
    RFA_LOG_FIRST(100, "Traffic: %s on the road, %u aboard (%s)", model.c_str(), static_cast<unsigned>(car.waiting.size()), resource.c_str());
    g_cars.push_back(car);
}

void Board(Car& car, BYTE* vehicle) {
    for (auto name = car.waiting.begin(); name != car.waiting.end();) {
        BYTE* info = HumanInfo(name->c_str());
        float orientation[9];
        memcpy(orientation, vehicle + kOrientation, sizeof(orientation));
        BYTE* human = info ? MakeHuman(info, reinterpret_cast<const float*>(vehicle + kPosition), orientation, nullptr) : nullptr;
        if (!human) {
            if (!info) name = car.waiting.erase(name);  // not one of the game's characters
            else ++name;                                 // not streamed in yet: next time
            continue;
        }
        int enter[4] = {-1, 1, -1, 5};  // any seat, the best; teleport, minimal processing
        RequestEnter(human, vehicle, enter);
        SitOut(human);
        int& management = *reinterpret_cast<int*>(human + kManagement);
        car.people.push_back({Handle(human), management});
        management = kAmbient;  // Guerrilla's ambient traffic: the game's avoidance gives it ambient waits
        name = car.waiting.erase(name);
    }
}

void LetGoOfHorn(Car& car, BYTE* vehicle) {
    if (car.hornDown && vehicle && IsAutomobile(vehicle)) ReleaseHorn(car.vehicle);
    car.hornDown = false;
}

// Guerrilla's civilian_horn_do_honk, each frame a car waits: a roll (20%) for a honk of 500 to 2000 ms,
// else 10 to 15 s before the next roll.
void HornFrame(Car& car, BYTE* vehicle) {
    if (!IsAutomobile(vehicle)) return;
    if (TimerSet(&car.horn, nullptr) && !TimerElapsed(&car.horn, nullptr)) return;
    if (TimerSet(&car.horn, nullptr)) {  // ran out
        if (car.hornDown) {
            LetGoOfHorn(car, vehicle);
            TimerStart(&car.horn, nullptr, 10000 + std::rand() % 5001);
        } else TimerClear(&car.horn, nullptr);
        return;
    }
    if (Random() <= kHonkChance) {
        HonkHorn(car.vehicle);
        car.hornDown = true;
        TimerStart(&car.horn, nullptr, 500 + std::rand() % 1501);
        RFA_LOG_FIRST(5, "Traffic: a car honks at what's in its way");
    } else {
        TimerStart(&car.horn, nullptr, 10000 + std::rand() % 5001);
        LetGoOfHorn(car, vehicle);
    }
}

// A honk lasts its 500 to 2000 ms, waiting or not.
void HornDone(Car& car, BYTE* vehicle) {
    if (car.hornDown && TimerSet(&car.horn, nullptr) && TimerElapsed(&car.horn, nullptr)) {
        LetGoOfHorn(car, vehicle);
        TimerStart(&car.horn, nullptr, 10000 + std::rand() % 5001);
    }
}

// Unusable as Guerrilla's ai_vehicle_is_usable has it, for traffic: on fire, a wreck, or flipped (on its
// side or roof for 1.5 s).
const char* Unusable(Car& car, BYTE* vehicle, int now) {
    if (OnFire(vehicle)) return "it's on fire";
    if (*reinterpret_cast<const DWORD*>(vehicle + kVehicleFlags) & (1u << 10)) return "it's wrecked";
    const float upY = *reinterpret_cast<const float*>(vehicle + kOrientation + 16);  // the up vector's y
    if (upY >= 0.3f) { car.flippedSince = -1; return nullptr; }
    if (car.flippedSince < 0) car.flippedSince = now;
    return now - car.flippedSince >= 1500 ? "it's flipped" : nullptr;
}

// Gives the car and its people back to the game: its people as they were before (their AI no longer
// told to sit frames out once the car isn't ours).
void HandBack(Car& car, BYTE* vehicle) {
    LetGoOfHorn(car, vehicle);
    if (vehicle && car.driving) {
        OverrideSpeed(vehicle, 0);
        SetVehicleAiState(vehicle + kVehicleAi, 0);
    }
    car.driving = false;
    for (const Person& person : car.people)
        if (BYTE* human = HumanByHandle(kWorld, nullptr, person.handle)) *reinterpret_cast<int*>(human + kManagement) = person.management;
}

void Despawn(Car& car) {
    for (const Person& person : car.people) RemoveObject(kWorld, nullptr, person.handle, 0);
    RemoveObject(kWorld, nullptr, car.vehicle, 0);
}

// The car's people get out (once it's nearly stopped, at once if it burns) and run; true once they have.
bool BailOut(Car& car, BYTE* vehicle, const char* why) {
    if (!car.bailing) {
        car.bailing = true;
        RFA_LOG_FIRST(20, "Traffic: a car's people get out (%s)", why);
    }
    StopCar(vehicle);
    LetGoOfHorn(car, vehicle);
    if (!OnFire(vehicle) && car.speed > kBailSpeed) {
        for (const Person& person : car.people)
            if (BYTE* human = HumanByHandle(kWorld, nullptr, person.handle)) SitOut(human);
        return false;
    }
    HandBack(car, vehicle);
    for (const Person& person : car.people) {
        BYTE* human = HumanByHandle(kWorld, nullptr, person.handle);
        if (!human || *reinterpret_cast<const DWORD*>(human + kInVehicle) != car.vehicle) continue;
        BYTE exit[4] = {};  // vi_exit_data: nothing asked
        RequestExit(human, exit);
        StartActiveDespawn(human, nullptr, 2, vehicle);  // run from the car, then vanish (Armageddon's despawn goal)
    }
    return true;
}

// Guerrilla's should_despawn_vehicle: away more than 60 m, its driver out of view 5 s, 20 s after the car
// was made (10 s beyond the spawn ring); always 280 m away.
bool ShouldDespawn(Car& car, BYTE* vehicle, BYTE* driver, const V3& player, int now) {
    const V3 position = Pos(vehicle);
    const float away = Distance(position, player);
    if (away > kDespawn) return true;
    const V3 at = driver ? Pos(driver) : position;
    const V3 head{at.x, at.y + 0.85f, at.z};
    if (away <= kNear || InView(&head.x, 1.25f)) { car.outOfViewSince = -1; return false; }
    if (car.outOfViewSince < 0) car.outOfViewSince = now;
    const int age = now - car.made;
    if (age < 10000 || now - car.outOfViewSince < 5000) return false;
    return age >= 20000 || away > kMaxSpawn;
}

// ---- Guerrilla's aggressive despawn of cars (do_frame_despawn_auto_aggressive, RM 0xB4DDE0)
// The world holds 32 cars at once (the automobile pool, guerrilla_vehicles.cpp, Guerrilla's 32); one more is refused
// with "Ran out of slots for object type (automobile), zone (...)" (world::create_object 0x6D44E0), which
// Armageddon shows as an error. Guerrilla never gets there: every frame (do_frame_despawn RM 0xB4DE70, outside its
// timer), while the world has 30 cars or more, it takes one away: one fading out to be despawned (vehicle flags
// bit 11) that is off screen, else the farthest car from the player with nobody in it that is off screen
// (find_farthest_offscreen_auto RM 0xB4D950); world::destroy_object either way. Off screen is Guerrilla's
// vehicle::is_offscreen (RM 0xB64CA0: the car's box grown by a quarter against the view); here the game's view
// test on a sphere of 5 m round the car (0x768400), about a big truck's box grown so.
constexpr int kAggressiveCars = 30;
constexpr DWORD kCarIndices = 0x1870974;     // the automobiles' list (World +0x308 + 5 × 12): their indices (ushort), then +8 how many
constexpr DWORD kObjectTable = 0x187068C;    // World +0x5C: every object by index
constexpr DWORD kDespawnAfterFade = 1u << 11;  // vehicle flags (+0x4FB0): m_despawn_after_fade
constexpr float kOffscreenRadius = 5.0f;
auto const DestroyObject = reinterpret_cast<bool(__fastcall*)(void* world, void* unused, BYTE* object, bool flag)>(0x6D5450);

bool Offscreen(const BYTE* car) {
    V3 middle = Pos(car);
    middle.y += 1.0f;
    return !InView(&middle.x, kOffscreenRadius);
}
bool Empty(BYTE* car) {
    const int seats = Seats(car);
    for (int seat = 0; seat < seats; ++seat)
        if (SeatOccupant(car, seat)) return false;
    return true;
}
bool BeingDestroyed(const BYTE* object) { return (object[0x55] & 0x80) != 0; }

void AggressiveDespawn(BYTE* player) {
    const int count = *reinterpret_cast<const int*>(kCarCount);
    if (count < kAggressiveCars) return;
    const unsigned short* indices = *reinterpret_cast<const unsigned short* const*>(kCarIndices);
    BYTE* const* objects = *reinterpret_cast<BYTE* const* const*>(kObjectTable);
    if (!indices || !objects) return;
    BYTE* fading = nullptr;
    BYTE* farthest = nullptr;
    float farthestAway = 0.0f;
    for (int i = 0; i < count; ++i) {
        BYTE* car = objects[indices[i]];
        if (!car || BeingDestroyed(car)) continue;
        if (*reinterpret_cast<const DWORD*>(car + kVehicleFlags) & kDespawnAfterFade) {
            if (Offscreen(car)) { fading = car; break; }
            continue;
        }
        if (!Empty(car)) continue;
        const float away = Distance(Pos(car), Pos(player));
        if (away > farthestAway && Offscreen(car)) {
            farthestAway = away;
            farthest = car;
        }
    }
    BYTE* gone = fading ? fading : farthest;
    if (!gone) return;
    RFA_LOG_FIRST(40, "Cars: %d in the world (Guerrilla takes one away from 30 on, of 32); %s taken away, %.0f m from the player, off screen", count,
                  fading ? "one fading out" : "the farthest empty one", Distance(Pos(gone), Pos(player)));
    DestroyObject(kWorld, nullptr, gone, false);
}

// Every frame on the game's thread (from the spawn nodes' frame, 0x8028B0).
void TrafficFrame() {
    if (!InTrafficWorld()) {
        g_cars.clear();
        g_pending.clear();
        return;
    }
    BYTE* player = Player();
    if (!player) return;
    AggressiveDespawn(player);
    const V3 at = Pos(player);
    const int now = GameTime();  // the game's clock: a pause doesn't count
    const DWORD playerVehicle = *reinterpret_cast<const DWORD*>(player + kInVehicle);

    // The spawns waiting for their model.
    for (auto pending = g_pending.begin(); pending != g_pending.end();) {
        const int result = SpawnVehicle(pending->parameters);
        BYTE* vehicle = *reinterpret_cast<BYTE**>(pending->parameters + 0x5C);
        if (result == 0 && vehicle) {
            MadeCar(vehicle, pending->resource, pending->model);
            g_nextSpawn = now + 2000 + std::rand() % 2000;
        }
        if (result == 1 && now - pending->since < 15000) { ++pending; continue; }
        if (result != 0) RFA_LOG_FIRST(20, "Traffic: %s wasn't made (%s)", pending->model.c_str(), result == 1 ? "not streamed in within 15 s" : "the game refused it");
        pending = g_pending.erase(pending);
    }
    // The cars.
    const int clock = GameTime();
    for (auto car = g_cars.begin(); car != g_cars.end();) {
        BYTE* vehicle = VehicleByHandle(kWorld, nullptr, car->vehicle);
        if (!vehicle) {  // gone (blown up, taken away): its people, if any, back to the game
            HandBack(*car, nullptr);
            car = g_cars.erase(car);
            continue;
        }
        const V3 position = Pos(vehicle);
        if (car->lastTime >= 0 && clock > car->lastTime) car->speed = Distance(position, car->lastPosition) * 1000.0f / static_cast<float>(clock - car->lastTime);
        car->lastPosition = position;
        car->lastTime = clock;
        BYTE* driver = car->people.empty() ? nullptr : HumanByHandle(kWorld, nullptr, car->people[0].handle);
        if (playerVehicle != car->vehicle && ShouldDespawn(*car, vehicle, driver, at, clock)) {
            HandBack(*car, vehicle);
            Despawn(*car);
            car = g_cars.erase(car);
            continue;
        }
        if (!car->waiting.empty() && now - car->since < 10000) {
            Board(*car, vehicle);
            driver = car->people.empty() ? nullptr : HumanByHandle(kWorld, nullptr, car->people[0].handle);
        }
        const bool seated = driver && *reinterpret_cast<const DWORD*>(driver + kInVehicle) == car->vehicle;
        if (playerVehicle == car->vehicle || (car->driving && !car->bailing && (!driver || !Driving(driver)))) {
            HandBack(*car, vehicle);  // the player took it, or its driver is dead or out: the game's from now on
            car = g_cars.erase(car);
            continue;
        }
        // On fire, wrecked or flipped: out, and away from it.
        if (car->driving || car->bailing) {
            const char* why = car->bailing ? "" : Unusable(*car, vehicle, clock);
            if (car->bailing || why) {
                if (BailOut(*car, vehicle, why ? why : "")) { car = g_cars.erase(car); continue; }
                ++car;
                continue;
            }
        }
        for (const Person& person : car->people)
            if (BYTE* human = HumanByHandle(kWorld, nullptr, person.handle)) SitOut(human);
        if (!seated || !Driving(driver)) {
            if (now - car->since > 10000 && car->people.empty()) {  // nobody could be made for it
                RFA_LOG_FIRST(20, "Traffic: nobody of its zone's people could be made for a car; it's taken away out of sight");
                Crew empty;
                empty.vehicle = car->vehicle;
                RetireWhenUnseen(empty);
                car = g_cars.erase(car);
                continue;
            }
            if (now - car->since > 15000) {  // its people never sat down: left to the game, parked
                RFA_LOG_FIRST(20, "Traffic: a car's driver didn't take the wheel in 15 s; the car is left to the game");
                HandBack(*car, vehicle);
                car = g_cars.erase(car);
                continue;
            }
            ++car;
            continue;
        }
        BYTE* ai = vehicle + kVehicleAi;
        if (*reinterpret_cast<const int*>(ai) != 1) SetVehicleAiState(ai, 1);
        if (!car->driving) { OverrideSpeed(vehicle, kSpeed); car->driving = true; }
        HornDone(*car, vehicle);
        // Fleeing (Guerrilla's ambient_veh_flee_action): the cap off, a destination away from the threat.
        BYTE* belief = FleeToSafetyBelief(driver);
        if (!belief) belief = EnemyFleeBelief(driver);
        V3 from{};
        bool fromThreat = false;
        if (belief) {  // a belief: +0 the object's handle, +4 where it was (Guerrilla's layout)
            from = *reinterpret_cast<const V3*>(belief + 4);
            fromThreat = std::isfinite(from.x) && std::isfinite(from.y) && std::isfinite(from.z) && Distance(from, position) < 300.0f;
        }
        if (!!belief != car->fleeing) {
            car->fleeing = !!belief;
            OverrideSpeed(vehicle, car->fleeing ? 0.0f : kSpeed);  // 0 clears the override: the car's own top speed
            if (car->fleeing) {
                car->hasDestination = false;
                RFA_LOG_FIRST(20, "Traffic: a driver flees%s", fromThreat ? " from what it saw" : "");
            }
        }
        // Waiting (the game's avoidance set it): brake, maybe honk, and don't drive on this frame. A fleeing
        // car only waits for the player on foot.
        int* wait = reinterpret_cast<int*>(vehicle + kWaitTimer);
        if (TimerSet(wait, nullptr)) {
            const bool running = !TimerElapsed(wait, nullptr);
            const bool forPlayer = *reinterpret_cast<const DWORD*>(vehicle + kWaitBlocker) == Handle(player);
            if (running && (!car->fleeing || forPlayer)) {
                RFA_LOG_FIRST(5, "Traffic: a car waits for %s in its way", forPlayer ? "the player" : "something");
                StopCar(vehicle);
                HornFrame(*car, vehicle);
                ++car;
                continue;
            }
            EndWait(vehicle);
        }
        if (!car->hasDestination || Distance(position, car->destination) < kReached) {
            const V3 forward = *reinterpret_cast<const V3*>(vehicle + kOrientation + 24);
            const V3 behind{position.x - forward.x * 50, position.y, position.z - forward.z * 50};
            const V3& away = car->fleeing && fromThreat ? from : behind;
            BYTE* road = ClosestRoad(&position.x, false, 60.0f, false, false);
            car->hasDestination = road && NextRoadPlace(vehicle, road, &car->destination.x, &away.x);
        }
        if (car->hasDestination) {
            const int result = TravelTo(driver, vehicle, &car->destination.x, 0xFFFFFFFF, 0xFFFFFFFF, 1, kStopShort, 0, 0, 0, 1);
            if (result == 0) {
                RFA_LOG_FIRST(20, "Traffic: a car couldn't find its way to (%.0f, %.0f, %.0f)", car->destination.x, car->destination.y, car->destination.z);
                car->hasDestination = false;
                ++car->failures;
            }
            else {
                if (result == 1) car->hasDestination = false;
                car->failures = 0;
            }
        } else ++car->failures;
        if (car->failures > 30) {  // lost its way 30 times in a row: taken away out of sight
            RFA_LOG_FIRST(20, "Traffic: a car lost its way; it's taken away out of sight");
            HandBack(*car, vehicle);
            Crew leaving;
            leaving.vehicle = car->vehicle;
            leaving.people = car->people;
            RetireWhenUnseen(leaving);
            car = g_cars.erase(car);
            continue;
        }
        ++car;
    }
    // A new car now and then, as Guerrilla's: every 250 to 500 ms.
    if (now < g_nextSpawn) return;
    g_nextSpawn = now + 250 + std::rand() % 250;
    if (const Zone* zone = TrafficZoneAt(at)) TrySpawn(player, *zone);
}

// The spawn nodes' frame (cdecl, nothing), each frame from the gameplay update (0x4FE860).
constexpr DWORD kSpawnNodeFrame = 0x8028B0;
constexpr size_t kSpawnNodeFramePrologue = 6;  // push ebp / mov ebp,esp / sub esp,14h
using FrameFn = void(__cdecl*)();
FrameFn g_spawnNodeFrame;

void __cdecl SpawnNodeFrame() {
    g_spawnNodeFrame();
    ReportSpawnPools();
    TrafficFrame();
    gv::ConvoysFrame();
    gv::EdfFrame();
    gv::ChasesFrame();
    gv::SafehousesFrame();
}

}  // namespace

void StartTraffic() {
    const std::wstring worlds = rfa::IniString(L"GuerrillaVehicles", L"TrafficWorlds", L"rfg");
    ReadWorlds(worlds);
    if (worlds.empty()) { rfa::Log("Traffic: off ([GuerrillaVehicles] TrafficWorlds= names no world)"); return; }
    if (Files().zones.empty()) { rfa::Log("Traffic: no mods\\guerrilla_vehicles\\traffic.txt (build the vehicles); no traffic"); return; }
    const BYTE opening[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x14};
    if (memcmp(reinterpret_cast<const void*>(kSpawnNodeFrame), opening, sizeof(opening)) != 0) {
        rfa::Log("Traffic: the game's spawn node frame isn't as expected; no traffic");
        return;
    }
    g_spawnNodeFrame = reinterpret_cast<FrameFn>(rfa::HookFunction(kSpawnNodeFrame, kSpawnNodeFramePrologue, reinterpret_cast<void*>(&SpawnNodeFrame)));
    rfa::Log(g_spawnNodeFrame ? "Traffic: Guerrilla's in %ls: %u zones, %u vehicle groups, %u resources' drivers"
                              : "Traffic: could not hook the game's frame (%ls)",
             worlds.c_str(), static_cast<unsigned>(Files().zones.size()), static_cast<unsigned>(Files().groups.size()), static_cast<unsigned>(Files().people.size()));
    if (g_spawnNodeFrame) {
        gv::StartConvoys();
        gv::StartEdf();
        gv::StartChases();
        gv::StartSafehouses();
    }
}
