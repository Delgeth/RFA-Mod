// In-game menus for mods, drawn with Dear ImGui (src\third_party\imgui) over the game's picture.
// The loader (rfa_loader.dll) hosts ImGui: it draws on the game's own Direct3D 11 device just
// before each frame is shown, and feeds ImGui the game window's mouse and keys. The menus are up
// while the developer console is open (the ` key), when the game has already freed the mouse and
// stopped the player's controls: a bar along the bottom of the screen, with each mod's menus, and
// the windows they open.
//
// A mod with menus exports
//     extern "C" __declspec(dllexport) void __cdecl RFAMod_Menu(const RFAMenu* menu);
// which the loader calls twice each frame while the menus are up, on the thread that draws the
// game: first with menu->stage RFA_MENU_BAR, inside the bar (add a menu or an item:
// ImGui::BeginMenu, ImGui::MenuItem), then with RFA_MENU_WINDOWS (draw your windows: ImGui::Begin).
// Start each call with `if (!rfa::menu::Use(menu)) return;`, which points the mod's own copy of
// ImGui at the loader's (the same context and memory functions). Such a mod is built with ImGui's
// four core files (imgui.cpp, imgui_draw.cpp, imgui_tables.cpp, imgui_widgets.cpp), not its
// backends, from the same version, with src\third_party\imgui on its include path and NDEBUG
// defined (as RFA-Mod's are), so ImGui's debug checks never stop the game with a dialog.
//
// Anything a menu does to the game itself (running a console command, spawning) belongs on the
// game's own thread: note it in the menu, and do it from a hook of the game's.
#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <cstring>
#include "imgui.h"

enum RFAMenuStage {
    RFA_MENU_BAR = 0,      // inside the bar along the bottom of the screen
    RFA_MENU_WINDOWS = 1,  // the mod's own windows
};

struct RFAMenu {
    int size;                  // sizeof(RFAMenu)
    const char* imguiVersion;  // IMGUI_VERSION the loader was built with
    ImGuiContext* context;
    ImGuiMemAllocFunc alloc;
    ImGuiMemFreeFunc free;
    void* allocData;
    int stage;                 // RFAMenuStage
};

// What the loader's RFALoader_MenusWant export answers about this frame.
enum RFAMenuWant {
    RFA_MENUS_SHOWN = 0,     // the menus are up
    RFA_MENUS_MOUSE = 1,     // the mouse is over one of them (its clicks are theirs)
    RFA_MENUS_KEYBOARD = 2,  // one of them is taking typed text
};

namespace rfa::menu {

// Points this module's ImGui at the loader's. False if the loader's ImGui isn't the same version.
inline bool Use(const RFAMenu* menu) {
    if (!menu || menu->size != sizeof(RFAMenu) || !menu->context || std::strcmp(menu->imguiVersion, IMGUI_VERSION) != 0)
        return false;
    ImGui::SetAllocatorFunctions(menu->alloc, menu->free, menu->allocData);
    ImGui::SetCurrentContext(menu->context);
    return true;
}

// Whether the menus are up, have the mouse or take typed text this frame (for a mod that reads the
// mouse or keys itself and should leave them to the menus).
inline bool Want(RFAMenuWant what) {
    using WantFn = int(__cdecl*)(int);
    static WantFn want = [] {
        HMODULE loader = GetModuleHandleW(L"rfa_loader.dll");
        return loader ? reinterpret_cast<WantFn>(GetProcAddress(loader, "RFALoader_MenusWant")) : nullptr;
    }();
    return want && want(what) != 0;
}

}  // namespace rfa::menu
