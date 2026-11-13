// Guerrilla's keys (see keys.h): their settings in rfa_mods.ini [GuerrillaKeys], and their F7 window, Guerrilla > Keys.
//
// Guerrilla's PC version let its own actions be bound in its controls menu; Armageddon's controls menu knows only
// Armageddon's. Until Guerrilla's are added there, this window lists them: click a key, then press the new one (a
// letter, a digit, Tab, Enter, Space or F1-F12; Esc keeps the old one, Delete leaves the action without a key). Each change is
// written to rfa_mods.ini at once and used at once. Before [GuerrillaKeys] the map's key was [GuerrillaHud] MapKey and
// the backpack's [GuerrillaGear] BackpackKey: those are still read where [GuerrillaKeys] has no setting.
#include "rfa_mod.h"
#include "rfa_menu.h"
#include "keys.h"
#include <cstdio>
#include <cstdlib>
#include <cwchar>
#include <cwctype>
#include <string>

namespace {

struct KeyAction {
    const wchar_t* name;             // its setting under [GuerrillaKeys]
    const char* shown;               // its name in the window
    const char* also;                // the keys that do the same and can't be changed ("" for none)
    const wchar_t* defaultKey;
    const wchar_t* oldSection;       // where it was set before [GuerrillaKeys] (null for nowhere)
    const wchar_t* oldName;
    int code = 0;
};

KeyAction g_actions[keys::Count] = {
    {L"Map", "World map", "", L"Tab", L"GuerrillaHud", L"MapKey"},
    {L"Backpack", "Backpack on / off", "", L"B", L"GuerrillaGear", L"BackpackKey"},
    {L"MapZoomIn", "World map: zoom in", "Page Up, +, the mouse wheel", L"E", nullptr, nullptr},
    {L"MapZoomOut", "World map: zoom out", "Page Down, -, the mouse wheel", L"Q", nullptr, nullptr},
    {L"MapCentre", "World map: centre on you", "", L"X", nullptr, nullptr},
    {L"Up", "Up (the map's pan, the safehouse screens' lists)", "the up arrow, the mouse wheel in the lists", L"W", nullptr, nullptr},
    {L"Down", "Down (the map's pan, the safehouse screens' lists)", "the down arrow, the mouse wheel in the lists", L"S", nullptr, nullptr},
    {L"Left", "Left (the map's pan)", "the left arrow", L"A", nullptr, nullptr},
    {L"Right", "Right (the map's pan)", "the right arrow", L"D", nullptr, nullptr},
    {L"TipContinue", "Close a tool tip that paused the game", "", L"Enter", nullptr, nullptr},
};
constexpr wchar_t kSection[] = L"GuerrillaKeys";

// guerrilla_vehicles' keys for driving ([GuerrillaVehicles]): shown here, set there. That mod puts them into
// Armageddon's own key map as the game starts (by the game's key names: W, Spacebar, Mouse3, ...), so a change there
// takes effect at the next start.
struct VehicleKeySetting {
    const wchar_t* name;
    const char* shown;
    const wchar_t* defaultKey;  // that mod's (Guerrilla's PC defaults)
};
const VehicleKeySetting kVehicleKeys[] = {
    {L"Accelerate", "Accelerate", L"W"},
    {L"Brake", "Brake / reverse", L"S"},
    {L"Handbrake", "Handbrake", L"Spacebar"},
    {L"Exit", "Get out", L"E"},
    {L"ChangeSeat", "Change seat", L"Mouse3"},
};

int g_version = 0;
bool g_read = false;         // the settings read (keys::Read, or at the first use)
bool g_windowOpen = false;
int g_capturing = -1;        // the action waiting for its new key, -1 for none
bool g_heldAtCapture[256];   // the keys already down as it started waiting (not taken as the new key)
std::string g_status;

// A key's virtual-key code from its name: a letter or digit, Tab, Enter, Space, or F1-F12; 0 for anything else (or none).
int CodeOf(const std::wstring& name) {
    if (name.size() == 1 && iswalnum(name[0])) return towupper(name[0]);
    if (_wcsicmp(name.c_str(), L"Tab") == 0) return VK_TAB;
    if (_wcsicmp(name.c_str(), L"Enter") == 0) return VK_RETURN;
    if (_wcsicmp(name.c_str(), L"Space") == 0) return VK_SPACE;
    if (name.size() >= 2 && name.size() <= 3 && (name[0] == L'F' || name[0] == L'f')) {
        const int number = _wtoi(name.c_str() + 1);
        if (number >= 1 && number <= 12) return VK_F1 + number - 1;
    }
    return 0;
}

std::wstring NameOf(int code) {
    if ((code >= 'A' && code <= 'Z') || (code >= '0' && code <= '9')) return std::wstring(1, static_cast<wchar_t>(code));
    if (code == VK_TAB) return L"Tab";
    if (code == VK_RETURN) return L"Enter";
    if (code == VK_SPACE) return L"Space";
    if (code >= VK_F1 && code <= VK_F12) return L"F" + std::to_wstring(code - VK_F1 + 1);
    return L"";
}

// The settings' names and keys are plain ASCII.
std::string Narrow(const std::wstring& text) {
    std::string narrow;
    for (wchar_t c : text) narrow += static_cast<char>(c);
    return narrow;
}

bool HasSetting(const wchar_t* section, const wchar_t* name) {
    wchar_t text[64];
    GetPrivateProfileStringW(section, name, L"\x1", text, 64, rfa::IniPath().c_str());
    return !(text[0] == L'\x1' && text[1] == L'\0');
}

// The key that opens the F7 menus ([Loader] MenuKey): it can't be one of these too.
int MenuKey() {
    const std::wstring name = rfa::IniString(L"Loader", L"MenuKey", L"F7");
    return CodeOf(name);
}

void Set(int action, int code) {
    KeyAction& a = g_actions[action];
    a.code = code;
    WritePrivateProfileStringW(kSection, a.name, NameOf(code).c_str(), rfa::IniPath().c_str());
    ++g_version;
    rfa::Log("Guerrilla's keys: %s is %s now ([GuerrillaKeys] %s=%s)", a.shown, code ? Narrow(NameOf(code)).c_str() : "no key",
             Narrow(a.name).c_str(), Narrow(NameOf(code)).c_str());
}

// The other action with the same key, or -1.
int SharedWith(int action) {
    const int code = g_actions[action].code;
    for (int i = 0; code && i < keys::Count; ++i)
        if (i != action && g_actions[i].code == code) return i;
    return -1;
}

// While an action waits for its new key: the first key that goes down (not one already held as it started).
void Capture() {
    if (g_capturing < 0) return;
    if (GetAsyncKeyState(VK_ESCAPE) & 0x8000) {
        g_capturing = -1;
        g_status = "Kept as it was.";
        return;
    }
    if (GetAsyncKeyState(VK_DELETE) & 0x8000) {
        Set(g_capturing, 0);
        g_capturing = -1;
        g_status.clear();
        return;
    }
    const int menuKey = MenuKey();
    for (int code = 1; code < 256; ++code) {
        const bool down = (GetAsyncKeyState(code) & 0x8000) != 0;
        if (!down) {
            g_heldAtCapture[code] = false;
            continue;
        }
        if (g_heldAtCapture[code] || NameOf(code).empty()) continue;
        if (code == menuKey) {
            g_status = "That key opens these menus ([Loader] MenuKey); pick another.";
            g_heldAtCapture[code] = true;
            continue;
        }
        Set(g_capturing, code);
        g_capturing = -1;
        g_status.clear();
        return;
    }
}

void Window() {
    ImGui::SetNextWindowSize(ImVec2(720, 0), ImGuiCond_FirstUseEver);
    if (!ImGui::Begin("Guerrilla's keys", &g_windowOpen)) {
        ImGui::End();
        return;
    }
    if (!g_windowOpen) g_capturing = -1;
    Capture();
    ImGui::TextWrapped("The keys of Guerrilla's own actions in the Guerrilla world. Click a key, then press the new one: "
                       "a letter, a digit, Tab, Enter, Space or F1-F12 (Esc keeps the old one, Delete leaves it without a key). "
                       "They're saved in rfa_mods.ini under [GuerrillaKeys] and work at once.");
    ImGui::Spacing();
    if (ImGui::BeginTable("keys", 4, ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerH | ImGuiTableFlags_SizingStretchProp)) {
        ImGui::TableSetupColumn("Action", ImGuiTableColumnFlags_WidthStretch, 3.0f);
        ImGui::TableSetupColumn("Key", ImGuiTableColumnFlags_WidthFixed, 110.0f);
        ImGui::TableSetupColumn("", ImGuiTableColumnFlags_WidthFixed, 70.0f);
        ImGui::TableSetupColumn("Also", ImGuiTableColumnFlags_WidthStretch, 2.0f);
        ImGui::TableHeadersRow();
        for (int i = 0; i < keys::Count; ++i) {
            KeyAction& a = g_actions[i];
            ImGui::PushID(i);
            ImGui::TableNextRow();
            ImGui::TableNextColumn();
            ImGui::TextUnformatted(a.shown);
            const int shared = SharedWith(i);
            if (shared >= 0) ImGui::TextColored(ImVec4(1.0f, 0.6f, 0.2f, 1.0f), "  the same key as: %s", g_actions[shared].shown);
            ImGui::TableNextColumn();
            const std::string label = g_capturing == i ? "press a key..." : (a.code ? Narrow(NameOf(a.code)) : "(none)");
            if (ImGui::Button(label.c_str(), ImVec2(-1, 0)) && g_capturing != i) {
                g_capturing = i;
                g_status.clear();
                for (int code = 0; code < 256; ++code) g_heldAtCapture[code] = (GetAsyncKeyState(code) & 0x8000) != 0;
            }
            ImGui::TableNextColumn();
            const int defaultCode = CodeOf(a.defaultKey);
            if (a.code != defaultCode && ImGui::SmallButton("Default")) {
                g_capturing = -1;
                Set(i, defaultCode);
            }
            ImGui::TableNextColumn();
            ImGui::TextDisabled("%s", a.also);
            ImGui::PopID();
        }
        ImGui::EndTable();
    }
    if (!g_status.empty()) ImGui::TextColored(ImVec4(1.0f, 0.8f, 0.3f, 1.0f), "%s", g_status.c_str());
    ImGui::Spacing();
    ImGui::SeparatorText("Vehicles");
    ImGui::TextWrapped("Driving keys are guerrilla_vehicles' and go into Armageddon's own controls as the game starts: set them "
                       "in rfa_mods.ini under [GuerrillaVehicles] (by the game's key names: W, Spacebar, Shift, Mouse1-Mouse7, ...); "
                       "a change takes effect at the next start.");
    // Read again every few seconds while the window is open (the file may be edited beside the game).
    static std::wstring vehicleKeys[sizeof(kVehicleKeys) / sizeof(kVehicleKeys[0])];
    static ULONGLONG readAt = 0;
    if (readAt == 0 || GetTickCount64() - readAt > 2000) {
        readAt = GetTickCount64();
        for (size_t i = 0; i < sizeof(kVehicleKeys) / sizeof(kVehicleKeys[0]); ++i)
            vehicleKeys[i] = rfa::IniString(L"GuerrillaVehicles", kVehicleKeys[i].name, kVehicleKeys[i].defaultKey);
    }
    if (ImGui::BeginTable("vehicle keys", 2, ImGuiTableFlags_RowBg | ImGuiTableFlags_SizingStretchProp)) {
        for (size_t i = 0; i < sizeof(kVehicleKeys) / sizeof(kVehicleKeys[0]); ++i) {
            const VehicleKeySetting& v = kVehicleKeys[i];
            ImGui::TableNextRow();
            ImGui::TableNextColumn();
            ImGui::TextUnformatted(v.shown);
            ImGui::TableNextColumn();
            const std::wstring& key = vehicleKeys[i];
            ImGui::TextDisabled("%s  ([GuerrillaVehicles] %s)", key.empty() ? "(none)" : Narrow(key).c_str(), Narrow(v.name).c_str());
        }
        ImGui::EndTable();
    }
    ImGui::End();
}

}  // namespace

void keys::Read() {
    g_read = true;
    std::string summary;
    for (KeyAction& a : g_actions) {
        std::wstring name;
        if (HasSetting(kSection, a.name)) name = rfa::IniString(kSection, a.name);
        else if (a.oldSection && HasSetting(a.oldSection, a.oldName)) name = rfa::IniString(a.oldSection, a.oldName);
        else name = a.defaultKey;
        a.code = CodeOf(name);
        if (!name.empty() && !a.code)
            rfa::Log("Guerrilla's keys: [GuerrillaKeys] %s=%s isn't a key this mod knows (a letter, a digit, Tab, Enter, Space, F1-F12); %s has none",
                     Narrow(a.name).c_str(), Narrow(name).c_str(), a.shown);
        summary += (summary.empty() ? "" : ", ") + Narrow(a.name) + " " + (a.code ? Narrow(NameOf(a.code)) : "none");
    }
    rfa::Log("Guerrilla's keys ([GuerrillaKeys], F7 Guerrilla > Keys): %s", summary.c_str());
}

int keys::Key(Action action) {
    if (!g_read) Read();
    return action >= 0 && action < Count ? g_actions[action].code : 0;
}

int keys::Version() { return g_version; }

// The F7 menus (see rfa_menu.h): Guerrilla > Keys, beside guerrilla_gear's Guerrilla > Upgrades (ImGui adds to a
// menu of the same name).
extern "C" __declspec(dllexport) void __cdecl RFAMod_Menu(const RFAMenu* menu) {
    if (!rfa::menu::Use(menu)) return;
    if (menu->stage == RFA_MENU_BAR) {
        if (ImGui::BeginMenu("Guerrilla")) {
            ImGui::MenuItem("Keys", nullptr, &g_windowOpen);
            ImGui::EndMenu();
        }
    } else if (menu->stage == RFA_MENU_WINDOWS && g_windowOpen) {
        if (!g_read) keys::Read();
        Window();
    }
}

// For other mods (see keys.h): a number that goes up with each change made in the window.
extern "C" __declspec(dllexport) int __cdecl GuerrillaHud_KeysVersion() { return g_version; }

// For other mods: the virtual-key code of one of these by its setting's name ("Backpack", "Map", ...); 0 for none or
// a name not among them.
extern "C" __declspec(dllexport) int __cdecl GuerrillaHud_Key(const char* name) {
    if (!g_read) keys::Read();
    for (const KeyAction& a : g_actions)
        if (name && _stricmp(Narrow(a.name).c_str(), name) == 0) return a.code;
    return 0;
}
