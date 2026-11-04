// In-game menus for mods (menus.cpp, rfa_menu.h).
#pragma once
#include <windows.h>
#include <vector>

// Sets up the menus: they are drawn once the game makes its swap chain, and call the RFAMod_Menu
// export of each of `mods` (the loaded mods, which the loader keeps).
void InstallMenus(const std::vector<HMODULE>* mods);
