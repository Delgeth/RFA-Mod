// The parts of rfa_qol.dll (see rfa_qol.cpp), one source file each. A part starts with its Init,
// which rfa_qol.cpp calls when the part is on, in place of the RFAMod_Init it had as a DLL of its own.
#pragma once

namespace borderless {
void Init();
}

namespace console {
void Init();
}

namespace fov {
void Init();
// A setting changed in the game's menus (RFAMod_SettingChanged).
void SettingChanged(const char* section, const char* key);
}  // namespace fov

namespace video_settings {
void Init();
}

struct RFAMenu;
namespace spawner {
void Init();
// Its part of the in-game menus (RFAMod_Menu).
void Menu(const RFAMenu* menu);
}  // namespace spawner

namespace limits {
void Init();
// Its part of the in-game menus (RFAMod_Menu).
void Menu(const RFAMenu* menu);
}  // namespace limits

namespace graphics {
void Init();
// Its part of the in-game menus (RFAMod_Menu).
void Menu(const RFAMenu* menu);
}  // namespace graphics
