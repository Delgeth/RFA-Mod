// The parts of guerrilla_gear.dll: guerrilla_gear.cpp (the weapons' room, animations, dropped guns,
// movement, Mason's hammer) and upgrades.cpp (Guerrilla's upgrades, bought in an F7 window).
#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <string>

struct RFAMenu;

// What guerrilla_gear.cpp lends the other parts.
namespace gear {
// The mod's folder (mods\guerrilla_gear\), ending in a backslash.
std::wstring Folder();
// The weapons.xtbl entry of that name (0xE0 bytes, +0 its name, +0x18 its inventory item), or null.
const BYTE* WeaponEntryNamed(const char* name);
// Whether the human plays Mason's animation set (rfg_mason): his slot 3 is his hammer's.
bool PlaysMason(const BYTE* human);
// Gives the player the weapon of that name into a free slot (Mason's hammer slot last); false, with
// why, when it isn't built, he has it already or has no free slot. On the game's thread.
bool GiveWeapon(BYTE* player, const char* name, std::string& why);
// The player's climb (m/s, up positive) and whether he is off the ground having left it (a jump or a
// fall); Lift adds to his climb (m/s). On the game's thread. (The jetpack's: see backpacks.cpp.)
bool Airborne(BYTE* player);
float Climb(BYTE* player);
void Lift(BYTE* player, float climb);
}  // namespace gear

namespace upgrades {
// Reads mods\guerrilla_gear\upgrades.txt (the weapons build's) and what was bought.
void Init();
// On the game's thread, from the player's heading step: the window's purchases and the salvage shown.
void Step(BYTE* player);
// Its part of the in-game menus (RFAMod_Menu).
void Menu(const RFAMenu* menu);
// The hammer Mason should carry: the best of Guerrilla's he has bought (its weapons.xtbl name), or
// empty for the plain one.
std::string BestHammer();
// The backpack worn (Guerrilla's mp_backpacks Unique_ID: 0 the jetpack, 1 Thrust ... 9 Tremor), -1 none.
int WornBackpack();
// The level of an upgrade bought (0: none).
int BoughtLevel(const char* name);
}  // namespace upgrades

namespace backpacks {
// Reads mods\guerrilla_gear\backpacks.txt (the weapons build's) and the backpack key.
void Init();
// On the game's thread, each frame from the player's heading step: the worn pack.
void Step(BYTE* player);
// The worn pack's fuel (0 to 1) and whether it is on (the jetpack: pushing), for the window.
float Fuel();
bool On();
}  // namespace backpacks
