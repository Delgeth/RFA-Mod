// In-game menus for mods (menus.cpp, rfa_menu.h).
#pragma once
#include <windows.h>
#include <string>
#include <vector>

// Sets up the menus: they are drawn once the game makes its swap chain, and call the RFAMod_Menu
// export of each of `mods` (the loaded mods, which the loader keeps).
void InstallMenus(const std::vector<HMODULE>* mods);

// Typing into the main menu's text fields: while it is on, the game's window keeps its keyboard for the text (see
// menus.cpp). False if the window isn't known yet. TypingState: -1 not typing, 0 typing, 1 kept (Enter), 2 put back
// (Esc); `text` gets what is typed. A file name takes lower case letters, digits and _ only.
bool BeginTyping(const std::string& text, bool fileName, size_t most);
int TypingState(std::string* text);
void EndTyping();
