// Improvements to the developer console of the publicbeta build (opened with the ` key). A part of
// rfa_qol.dll.
//
// The mod draws the console itself, in place of the game's drawing function (0xC7B890), with the
// game's own rectangle and text functions, laid out like Quake 3's console:
//   - a dark panel behind it (console_set_bg_color still changes its colour), a lighter strip for
//     the line you type, and a coloured line along its edge;
//   - bigger text (the console's font, scaled), the line you type bigger still, and a bright caret
//     where you type;
//   - the commands you typed in amber and errors in red, so they stand out from the rest;
//   - long lines wrapped onto the next line (the game cut them at 126 characters), and the same
//     line printed again and again shown once, with how many times;
//   - a scroll bar, and a row of ^ marks while scrolled back;
//   - the build stamp just above the typed line, on the right: the game's version, Steam build,
//     internal name and link time, and RFA-Mod's version;
//   - it slides open and shut in a sixth of a second.
//
// Notify lines: while the console is shut, the last 4 lines it printed show at the top left of the
// screen for 3 seconds. The game itself keeps nothing it prints while the console is shut.
//
// History: the game keeps only its last 256 lines. The mod keeps its own copy of every line the
// console prints (10000 by default) and also writes them to logs\console.log. Scroll with the
// mouse wheel (3 lines a notch), Page Up / Page Down (a page; with Shift, a line), Ctrl+Home (the
// oldest line) and End or Ctrl+End (back to the newest). While the console is open the wheel
// doesn't reach the game, so it no longer switches weapons behind it. The mouse works on it too
// (see "The mouse" below): drag the scroll bar, or hold or click the middle button to autoscroll.
//
// What the console shows besides its own output (see "What the game keeps to itself" below):
//   - the game's hidden output, marked [game]: its debug output, which only a debugger saw, and the
//     error reports it flashes on the screen for a moment;
//   - RFA-Mod's own log (logs\rfa_mods.log) as it grows, each line marked with the module's name.
//
// Typing (see "Typing" below): Ctrl+V or Shift+Insert pastes, cleaned up into something the console
// can run; several lines are run one at a time, each with Enter. Tab completes only the command's
// name, lists the matches with what each does, and with arguments typed says what the command is.
// A % in a line you run is now taken as it is (the game read it as a format).
//
// The render debug panel (` + X): it takes the mouse only while the console is open (the game frees
// the mouse for the console). While the panel is up, the console moves to the bottom of the screen,
// below the panel's windows, and is drawn under them, so its text no longer covers them.
//
// [Console] in rfa_mods.ini: its height, text sizes, darkness, history length, the log file, the
// notify lines, the build stamp, and whether it shows the game's hidden output and RFA-Mod's log.
//
// console.ini: console settings this mod applies for you, as <console name>=<value>:
//   [Startup]   once, when the game has finished starting (right after its own gamestart.txt)
//   [Gameplay]  every time gameplay starts after loading (right after the game's own per-level
//               scripts and anything set with console_gp_enter)
// Each name is looked up in the game's console command list to see what kind it is:
//   on/off settings ("Toggles X")   1 / 0 become the console's own "on" / "off"
//   number settings ("Sets X")      the value is passed as written
//   commands                        1 / 0 become "on" / "off", anything else is passed as written,
//                                   and an empty value just runs the command
//
// The level editor's link: the game shows the editor (bin\editor) its camera and what is picked,
// every frame, through shared memory, and runs the commands the editor puts in bin\link (see "The
// level editor's link" below).
#define RFA_MOD_NAME "console"
#include "rfa_mod.h"
#include "rfa_game.h"
#include "rfa_qol.h"
#include "rfa_menu.h"
#include <algorithm>
#include <climits>
#include <ctime>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace {

using rfa::game::At;

// ---------------------------------------------------------------------------------------------
// Settings: [Console] in rfa_mods.ini

struct Settings {
    float height = 0.5f;          // how much of the screen the console covers (the game's own: 0.4)
    float textScale = 1.25f;      // the output's size, times the console font's
    float inputScale = 1.5f;      // the typed line's
    int opacity = 85;             // how dark the panel behind it is, in percent
    size_t historyLines = 10000;  // lines kept for scrolling back
    bool logToFile = true;        // also write them to logs\console.log
    bool notify = true;           // keep what it prints while shut, and show the newest lines for a moment
    bool buildStamp = true;       // the build stamp in its corner
    bool gameLog = true;          // also show the game's hidden output: its debug output and error reports
    bool modLog = true;           // also show logs\rfa_mods.log
};
Settings g_settings;

float IniFloat(const wchar_t* key, const wchar_t* fallback) {
    return static_cast<float>(_wtof(rfa::IniString(L"Console", key, fallback).c_str()));
}

void ReadSettings() {
    g_settings.height = std::clamp(rfa::IniInt(L"Console", L"Height", 50), 20, 100) / 100.0f;
    g_settings.textScale = std::clamp(IniFloat(L"TextScale", L"1.25"), 0.5f, 4.0f);
    g_settings.inputScale = std::clamp(IniFloat(L"InputScale", L"1.5"), 0.5f, 4.0f);
    g_settings.opacity = std::clamp(rfa::IniInt(L"Console", L"Opacity", 85), 0, 100);
    g_settings.historyLines = static_cast<size_t>(std::clamp(rfa::IniInt(L"Console", L"HistoryLines", 10000), 256, 200000));
    g_settings.logToFile = rfa::IniInt(L"Console", L"LogToFile", 1) != 0;
    g_settings.notify = rfa::IniInt(L"Console", L"Notify", 1) != 0;
    g_settings.buildStamp = rfa::IniInt(L"Console", L"BuildStamp", 1) != 0;
    g_settings.gameLog = rfa::IniInt(L"Console", L"GameLog", 1) != 0;
    g_settings.modLog = rfa::IniInt(L"Console", L"ModLog", 1) != 0;
}

// ---------------------------------------------------------------------------------------------
// The game's console and 2D drawing, in the publicbeta exe

struct Colour {
    BYTE r, g, b, a;
};

auto& g_consoleReady = *reinterpret_cast<BYTE*>(0x28FF1C8);    // console_init (0xC7DCD0) has run
auto& g_consoleSliding = *reinterpret_cast<BYTE*>(0x28FF1C9);  // sliding open or shut
auto& g_consoleOpen = *reinterpret_cast<BYTE*>(0x28FF1CA);
auto& g_textMode = *reinterpret_cast<BYTE*>(0x11212E0);        // output goes to the text window instead (not on PC)
auto& g_drawRectangle = *reinterpret_cast<void**>(0x28FF1DC);  // what console_init was given to draw with:
auto& g_drawText = *reinterpret_cast<void**>(0x28FF1E0);       //   0xA44640 and 0xA446C0
auto& g_screenLeft = *reinterpret_cast<int*>(0x28FF1E4);       // where text starts (0xD04A90), plus
auto& g_offsetX = *reinterpret_cast<int*>(0x11212FC);          //   console_offset_x
auto& g_screenWidth = *reinterpret_cast<int*>(0x28FF1E8);
auto& g_screenHeight = *reinterpret_cast<int*>(0x28FF1EC);
auto& g_background = *reinterpret_cast<Colour*>(0x290767C);    // console_set_bg_color sets it
auto& g_typed = *reinterpret_cast<char(*)[0x400]>(0x28FB640);     // the line being typed (up to 124 characters)
auto& g_prompted = *reinterpret_cast<char(*)[0x80]>(0x29075F8);   // "] " and that line: what Enter echoes
auto& g_caret = *reinterpret_cast<int*>(0x28FF1D0);            // where in the typed line the caret is
auto& g_caretShown = *reinterpret_cast<BYTE*>(0x28F723D);      // blinks every 300 ms
auto& g_caretTimer = *reinterpret_cast<int*>(0x28FF1F4);       // a timer of the game's: when to blink next
auto& g_scrollLine = *reinterpret_cast<int*>(0x28FBA40);       // the game's own scrolling: kept at -1 (not scrolled)
auto& g_frameSeconds = *reinterpret_cast<float*>(0x11217CC);   // how long the last frame took
auto& g_consoleFont = *reinterpret_cast<int*>(0x110AAF0);      // the font "ug-debug"

constexpr Colour kGameBackground = {16, 16, 16, 128};  // the game's own, half see-through grey
constexpr float kSlidesPerSecond = 6;                  // open or shut in a sixth of a second, as in Quake 3

// The game's timer methods (the timer in ECX, called as __fastcall with EDX unused): has it run
// out (0xC8B550), and run out in so many milliseconds from now (0xC8B4B0).
auto const TimerDone = reinterpret_cast<bool(__fastcall*)(int* timer, void* unused)>(0xC8B550);
auto const TimerStart = reinterpret_cast<void(__fastcall*)(int* timer, void* unused, int milliseconds)>(0xC8B4B0);

// 2D drawing. 0x2DD7B30 is the drawing mode the console, the debug text and the menus all use; the
// renderer takes its own form of it, which 0xD05C40 makes (as 0xD06300, the game's text call, does).
struct RenderMode {
    int value;
};
constexpr DWORD kMode2D = 0x2DD7B30;
using DrawRectangleFn = void(__cdecl*)(int x, int y, int width, int height, const Colour* colour);
auto const SetColour = At<void(__cdecl*)(const Colour*)>(0xD04E20);
auto const ToRenderMode = At<void(__cdecl*)(RenderMode*, const void* mode)>(0xD05C40);
auto const Renderer = At<void*(__cdecl*)()>(0x4FD5C0);
// How big text is at scale 1: width and height in pixels of its first `length` characters (-1 = all).
auto const MeasureText = At<void(__cdecl*)(int* width, int* height, const char* text, int length, int font)>(0xD061F0);
// The renderer's text method (the renderer in ECX), which takes a scale; the game always passes 1.
using DrawTextScaledFn = void(__fastcall*)(void* renderer, void* unused, float x, float y, const char* text, float scale,
                                           bool centred, int font, const RenderMode* mode);
auto const DrawTextScaled = reinterpret_cast<DrawTextScaledFn>(0xD18A80);

void DrawRectangle(int x, int y, int width, int height, Colour colour) {
    if (width > 0 && height > 0) reinterpret_cast<DrawRectangleFn>(g_drawRectangle)(x, y, width, height, &colour);
}

void DrawString(float x, float y, const char* text, float scale, Colour colour) {
    if (!*text || !colour.a) return;
    SetColour(&colour);
    RenderMode mode{-1};
    ToRenderMode(&mode, reinterpret_cast<const void*>(kMode2D));
    DrawTextScaled(Renderer(), nullptr, x, y, text, scale, false, g_consoleFont, &mode);
}

// The width of the first `length` characters at scale 1.
int TextWidth(const char* text, int length = -1) {
    int width = 0, height = 0;
    MeasureText(&width, &height, text, length, g_consoleFont);
    return width;
}

int FontHeight() {
    int width = 0, height = 0;
    MeasureText(&width, &height, "", -1, g_consoleFont);  // as the game measures its line height
    return height > 0 ? height : 15;
}

Colour Brighter(Colour colour, int amount, BYTE minimumAlpha) {
    auto add = [amount](BYTE value) { return static_cast<BYTE>(std::min(255, value + amount)); };
    return {add(colour.r), add(colour.g), add(colour.b), std::max(colour.a, minimumAlpha)};
}

// ---------------------------------------------------------------------------------------------
// The history: every line the console printed, and how far back the view is scrolled

enum class Kind : BYTE {
    Output,   // what the console printed
    Command,  // what you typed, as Enter echoes it
    Error,
    Warning,
    Info,     // the mod's own messages in the console (pasting, Tab): drawn as output, never taken for errors
    Hidden,   // the game's hidden output
    Mod,      // RFA-Mod's log
};

struct Line {
    std::string text;
    Kind kind;
    bool fromConsole;  // the console's own output (only these are notify lines)
    int repeats;       // printed this many times in a row
    ULONGLONG printed; // when it was last printed (GetTickCount64), for the notify lines
};

constexpr Colour kOutputColour = {225, 225, 225, 255};
constexpr Colour kCommandColour = {255, 196, 80, 255};
constexpr Colour kErrorColour = {255, 112, 96, 255};
constexpr Colour kWarningColour = {240, 205, 100, 255};
constexpr Colour kHiddenColour = {150, 172, 205, 255};
constexpr Colour kModColour = {110, 205, 185, 255};
constexpr Colour kTypedColour = {255, 255, 255, 255};
constexpr Colour kAccentColour = {90, 170, 255, 255};  // the prompt, the edge line and the scroll bar
constexpr Colour kDimColour = {160, 160, 160, 255};
constexpr Colour kCaretColour = {255, 196, 80, 255};

Colour LineColour(Kind kind) {
    switch (kind) {
    case Kind::Command: return kCommandColour;
    case Kind::Error: return kErrorColour;
    case Kind::Warning: return kWarningColour;
    case Kind::Hidden: return kHiddenColour;
    case Kind::Mod: return kModColour;
    default: return kOutputColour;
    }
}

// The game prints from other threads too (loading), so the history is only touched under this lock.
std::mutex g_historyLock;
std::deque<Line> g_history;
int g_back = 0;      // how many lines the bottom of the view is above the newest (0 = following new output)
int g_maxBack = 0;   // the most it can be: the oldest line at the top (worked out as the console is drawn)
int g_pageLines = 1; // lines on screen, for Page Up / Page Down
bool g_drawsConsole = false;  // the mod draws the console, so the keys and the wheel move its history

// The game's help lists commands indented (" name - what it does"), so those are never errors.
bool LooksLikeError(const std::string& text) {
    if (!text.empty() && text[0] == ' ') return false;
    std::string lower = text;
    for (char& c : lower) c = static_cast<char>(tolower(static_cast<unsigned char>(c)));
    for (const char* word : {"error", "unknown command", "invalid", "failed"})
        if (lower.find(word) != std::string::npos) return true;
    return false;
}

void WriteToLog(const std::string& text) {
    static HANDLE file = [] {
        if (!g_settings.logToFile) return INVALID_HANDLE_VALUE;
        CreateDirectoryW((rfa::RootDir() + L"\\logs").c_str(), nullptr);
        return CreateFileW((rfa::RootDir() + L"\\logs\\console.log").c_str(), GENERIC_WRITE,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, nullptr);
    }();
    if (file == INVALID_HANDLE_VALUE) return;
    const std::string line = text + "\r\n";
    DWORD written;
    WriteFile(file, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
}

void AddLine(std::string text, Kind kind, bool fromConsole = true) {
    std::string clean;  // the font draws no tabs or other control characters
    for (char c : text) {
        if (c == '\t') clean += "    ";
        else if (static_cast<unsigned char>(c) >= 0x20) clean += c;
    }
    if ((kind == Kind::Output || kind == Kind::Hidden || kind == Kind::Mod) && LooksLikeError(clean)) kind = Kind::Error;
    std::lock_guard<std::mutex> lock(g_historyLock);
    if (!clean.empty() && !g_history.empty() && g_history.back().text == clean && g_history.back().kind == kind &&
        g_history.back().fromConsole == fromConsole) {
        ++g_history.back().repeats;  // the same line again: counted, not added
        g_history.back().printed = GetTickCount64();
        return;
    }
    WriteToLog(clean);
    g_history.push_back({std::move(clean), kind, fromConsole, 1, GetTickCount64()});
    if (g_back > 0) ++g_back;  // while scrolled back, the view stays on the same lines
    while (g_history.size() > g_settings.historyLines) g_history.pop_front();
    g_back = std::min(g_back, static_cast<int>(g_history.size()) - 1);
}

// Text the game printed: one line per '\n', as the game splits it (so "" is an empty line).
void AddOutput(const char* text) {
    const char* end = text + strlen(text);
    do {
        const char* newline = strchr(text, '\n');
        if (!newline) newline = end;
        AddLine(std::string(text, newline), Kind::Output);
        text = newline + 1;
    } while (text < end);
}

// Positive: further back. Stops at the oldest line and at the newest.
void Scroll(int lines) {
    std::lock_guard<std::mutex> lock(g_historyLock);
    g_back = std::clamp(g_back + lines, 0, g_maxBack);
}

bool ScrolledBack() {
    std::lock_guard<std::mutex> lock(g_historyLock);
    return g_back > 0;
}

// The game's console print (0xC7C310): cdecl (text, flags). It keeps lines only while the console
// is open (and not in text mode), 126 characters at most; the mod keeps the same lines, whole, and
// with notify lines on, those it prints while shut too.
constexpr DWORD kConsolePrint = 0xC7C310;
constexpr size_t kConsolePrintPrologue = 6;  // push ebp / mov ebp,esp / sub esp,1Ch
using ConsolePrintFn = void(__cdecl*)(const char*, int);
ConsolePrintFn g_consolePrint;

void __cdecl ConsolePrint(const char* text, int flags) {
    g_consolePrint(text, flags);
    if (text && !g_textMode && (g_consoleOpen || g_settings.notify)) AddOutput(text);
}

// Adds a character to the console's composed line (0xC7D140): cdecl (char). Enter sends '\n',
// which puts "] <command>" into the game's lines before the command runs.
constexpr DWORD kConsoleEcho = 0xC7D140;
constexpr size_t kConsoleEchoPrologue = 8;  // push ebp / mov ebp,esp / push ecx / movsx eax,byte ptr [ebp+8]
using ConsoleEchoFn = void(__cdecl*)(char);
ConsoleEchoFn g_consoleEcho;

void __cdecl ConsoleEcho(char c) {
    if (c == '\n') AddLine(std::string(g_prompted, strnlen(g_prompted, sizeof(g_prompted))), Kind::Command);
    g_consoleEcho(c);
}

// ---------------------------------------------------------------------------------------------
// What the game keeps to itself, and RFA-Mod's log
//
// The game's debug print (0xCA9F10: what it loads, what it couldn't), its failed checks ("<file>(<line>):
// V_ERROR: ...", 0xC7B1D0 through 0xCA9DA0) and one library's errors (0xA6B000) all end in Windows'
// OutputDebugStringA, which only a debugger sees. The mod takes the exe's import of it, so mods that
// hook those functions themselves are left alone.
//
// Its error reports (0x545D40, through 0x545BD0: "GENERAL-ERROR: ...", "ART-WARNING: ...") go into a
// queue of 164 (0x1308780, 256 bytes each, the count at 0x13130D0) that is drawn on the screen for a
// while and then emptied (0x545DD0, 0x545BB0). The mod reads the new ones each frame.
//
// logs\rfa_mods.log is what the launcher, the loader and every mod write (started afresh by the
// launcher each time); the mod reads what has been added to it ten times a second.

// Lines of hidden output: one per '\n', empty ones left out, each marked with where it came from.
void AddHidden(const char* tag, const char* text, Kind kind) {
    const char* end = text + strlen(text);
    while (text < end) {
        const char* newline = strchr(text, '\n');
        if (!newline) newline = end;
        std::string line(text, newline);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (!line.empty()) AddLine(tag + line, kind, false);
        text = newline + 1;
    }
}

using OutputDebugStringFn = void(WINAPI*)(LPCSTR);
OutputDebugStringFn g_outputDebugString;

void WINAPI GameDebugString(LPCSTR text) {
    g_outputDebugString(text);
    if (text) AddHidden("[game] ", text, Kind::Hidden);
}

constexpr int kMaxReports = 0xA4;
auto& g_reports = *reinterpret_cast<const char(*)[kMaxReports][0x100]>(0x1308780);
auto& g_reportCount = *reinterpret_cast<const int*>(0x13130D0);
int g_reportsRead = 0;

void ReadReports() {
    const int count = g_reportCount;
    if (count < 0 || count > kMaxReports) return;
    if (count < g_reportsRead) g_reportsRead = 0;  // shown and emptied since
    for (; g_reportsRead < count; ++g_reportsRead) {
        const char* report = g_reports[g_reportsRead];
        const std::string text(report, strnlen(report, sizeof(g_reports[0])));
        AddHidden("[game] ", text.c_str(), text.find("-ERROR:") != std::string::npos ? Kind::Error : Kind::Warning);
    }
}

HANDLE g_modLog = INVALID_HANDLE_VALUE;
LONGLONG g_modLogRead = 0;  // how much of it the console has
std::string g_modLogRest;   // the start of a line still being written

// "[12:34:56.789] [module] text" without the time, which the console doesn't show for its own lines.
void AddModLine(std::string line) {
    if (!line.empty() && line.back() == '\r') line.pop_back();
    if (line.size() > 15 && line[0] == '[' && line[13] == ']' && line[14] == ' ') line.erase(0, 15);
    if (!line.empty()) AddLine(line, Kind::Mod, false);
}

void ReadModLog() {
    if (g_modLog == INVALID_HANDLE_VALUE) {
        g_modLog = CreateFileW(rfa::LogPath().c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                               nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (g_modLog == INVALID_HANDLE_VALUE) return;
    }
    LARGE_INTEGER size;
    if (!GetFileSizeEx(g_modLog, &size)) return;
    if (size.QuadPart < g_modLogRead) {  // started afresh
        g_modLogRead = 0;
        g_modLogRest.clear();
    }
    while (g_modLogRead < size.QuadPart) {
        char buffer[16384];
        LARGE_INTEGER at;
        at.QuadPart = g_modLogRead;
        DWORD read = 0;
        if (!SetFilePointerEx(g_modLog, at, nullptr, FILE_BEGIN) || !ReadFile(g_modLog, buffer, sizeof(buffer), &read, nullptr) ||
            read == 0)
            return;
        g_modLogRead += read;
        g_modLogRest.append(buffer, read);
        for (size_t newline; (newline = g_modLogRest.find('\n')) != std::string::npos;) {
            AddModLine(g_modLogRest.substr(0, newline));
            g_modLogRest.erase(0, newline + 1);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Keys and the mouse wheel

// The console's key handling (0xC7C580), each frame while it is open: it takes keys from the
// game's queue (0xCADA40) until there are none. A key is its DirectInput scan code, plus 0x1000
// with Shift held, 0x2000 with Alt, 0x4000 with Ctrl and 0x8000 while ` is held.
constexpr DWORD kConsoleKeys = 0xC7C580;
constexpr size_t kConsoleKeysPrologue = 6;  // push ebp / mov ebp,esp / sub esp,60h
constexpr DWORD kNextKey = 0xCADA40;        // int(): the next key pressed, 0 when there are none
constexpr size_t kNextKeyPrologue = 8;      // push ebp / mov ebp,esp / mov al,byte ptr [0293AC0Ch]
constexpr int kShift = 0x1000, kCtrl = 0x4000, kGraveHeld = 0x8000;
constexpr int kHome = 0xC7, kPageUp = 0xC9, kEnd = 0xCF, kPageDown = 0xD1;
constexpr int kEscape = 0x01, kEnter = 0x1C, kNumpadEnter = 0x9C, kV = 0x2F, kInsert = 0xD2;
constexpr int kLinesPerNotch = 3;

// Pasting (see "Typing" below): the pasted lines still to come after the one in the typed line.
std::deque<std::string> g_pasted;
bool g_nextPastedAfterEnter = false;
void Paste();
bool TypeIn(const std::string& text, bool replaceLine);

using VoidFn = void(__cdecl*)();
using NextKeyFn = int(__cdecl*)();
VoidFn g_consoleKeys;
NextKeyFn g_nextKey;
bool g_readingConsoleKeys = false;

int PageLines() { return std::max(1, g_pageLines - 1); }

// The keys that move through the history. Home and End without Ctrl still move the caret, except
// that End first comes back from scrolling. Returns whether the key was used.
bool ScrollKey(int key) {
    switch (key) {
    case kPageUp: Scroll(PageLines()); return true;
    case kPageUp | kShift: Scroll(1); return true;
    case kPageDown: Scroll(-PageLines()); return true;
    case kPageDown | kShift: Scroll(-1); return true;
    case kHome | kCtrl: Scroll(INT_MAX / 2); return true;
    case kEnd | kCtrl: Scroll(-INT_MAX / 2); return true;
    case kEnd:
        if (!ScrolledBack()) return false;
        Scroll(-INT_MAX / 2);
        return true;
    }
    return false;
}

void __cdecl ConsoleKeys() {
    g_readingConsoleKeys = true;
    g_consoleKeys();
    g_readingConsoleKeys = false;
    g_scrollLine = -1;
}

// Also takes Ctrl+V and Shift+Insert (paste), and after Enter has run a pasted line, puts the next
// one in its place.
int __cdecl NextKey() {
    if (!g_readingConsoleKeys || !g_consoleOpen) return g_nextKey();
    if (rfa::menu::Want(RFA_MENUS_KEYBOARD) || rfa::menu::Want(RFA_MENUS_ALONE)) {  // the in-game menus': not the console's
        while (g_nextKey()) {}
        return 0;
    }
    if (g_nextPastedAfterEnter) {  // the game has run the line Enter sent
        g_nextPastedAfterEnter = false;
        if (!g_pasted.empty()) {
            TypeIn(g_pasted.front(), true);
            g_pasted.pop_front();
        }
    }
    for (;;) {
        const int key = g_nextKey();
        const int pressed = key & ~kGraveHeld;
        if (!key) return 0;
        if (pressed == (kV | kCtrl) || pressed == (kInsert | kShift)) {
            Paste();
            continue;
        }
        if (g_drawsConsole && ScrollKey(pressed)) continue;
        if (pressed == kEnter || pressed == kNumpadEnter) g_nextPastedAfterEnter = !g_pasted.empty();
        if (pressed == kEscape) g_pasted.clear();  // the game clears the line; the rest of a paste goes too
        return key;
    }
}

// ---------------------------------------------------------------------------------------------
// The render debug panel (` + X)
//
// ` + X (0x4D4F40) switches the debug display to mode 4, whose update (0x76E7C0) hands the panel
// the mouse (0x56F660); its draw (0x76EA00) runs from the world's draw (0x768BA0), before the 2D
// pass that draws the console (0x5227C0). The panel's windows are the children of its root: a
// ring through each window's `next`, from the root's `firstChild`.

constexpr DWORD kPanelDraw = 0x76EA00;
constexpr size_t kPanelDrawPrologue = 10;  // push ebp / mov ebp,esp / movzx eax,byte ptr [019742BCh]
auto& g_panelShown = *reinterpret_cast<BYTE*>(0x19742BC);

struct PanelWindow {
    void* functions;
    float left, top, right, bottom;  // on screen, in pixels
    BYTE unknown14[4];
    const PanelWindow* next;
    const PanelWindow* previous;
    BYTE unknown20[4];
    const PanelWindow* firstChild;
};
static_assert(offsetof(PanelWindow, next) == 0x18 && offsetof(PanelWindow, firstChild) == 0x24, "the game's layout");
auto& g_panelRoot = *reinterpret_cast<const PanelWindow*>(0x19750F8);

// The windows are objects in the exe's data sections; anything else isn't one.
bool InGameData(const void* pointer) {
    const auto address = reinterpret_cast<uintptr_t>(pointer);
    return address >= 0x10F4000 && address < 0x3711000 && address % 4 == 0;
}

// How far down the screen the panel's windows reach (0 if it has none).
float PanelBottom() {
    float bottom = 0;
    const PanelWindow* first = g_panelRoot.firstChild;
    const PanelWindow* window = first;
    for (int i = 0; i < 32 && InGameData(window); ++i) {
        if (window->top < window->bottom && window->bottom > 0 && window->bottom < g_screenHeight * 2.0f)
            bottom = std::max(bottom, window->bottom);
        window = window->next;
        if (window == first) break;
    }
    return bottom;
}

// ---------------------------------------------------------------------------------------------
// The build stamp: what the game and the mod are, like a development build's stamp, e.g.
//   Red Faction: Armageddon v1.01 build 1932726 RF4_pc_release_dx11 Fri Jun 30 16:42:00 2017 pc | RFA-Mod v0.4.1-beta

constexpr DWORD kGameVersionText = 0xF9A4F8;  // "v1.01", the version the pause menu shows
constexpr int kSteamBuild = 1932726;          // the publicbeta exe, the only one the mod changes

std::string g_buildStamp;

// The name the exe has in its debug information (its CodeView record: "RSDS", GUID, age, path).
std::string ExeDebugName() {
    auto base = reinterpret_cast<const BYTE*>(GetModuleHandleW(nullptr));
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + reinterpret_cast<const IMAGE_DOS_HEADER*>(base)->e_lfanew);
    const IMAGE_DATA_DIRECTORY& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    auto entry = reinterpret_cast<const IMAGE_DEBUG_DIRECTORY*>(base + directory.VirtualAddress);
    for (DWORD i = 0; directory.VirtualAddress && i < directory.Size / sizeof(IMAGE_DEBUG_DIRECTORY); ++i, ++entry) {
        if (entry->Type != IMAGE_DEBUG_TYPE_CODEVIEW || !entry->AddressOfRawData || entry->SizeOfData < 25) continue;
        auto record = reinterpret_cast<const char*>(base + entry->AddressOfRawData);
        if (memcmp(record, "RSDS", 4) != 0) continue;
        std::string name(record + 24, strnlen(record + 24, entry->SizeOfData - 24));
        name = name.substr(name.find_last_of("\\/") + 1);
        return name.substr(0, name.find_last_of('.'));
    }
    return "";
}

// When the exe was linked (its header's time stamp), as C's asctime writes a time, in UTC.
std::string ExeLinkTime() {
    const time_t linked = static_cast<time_t>(rfa::game::ExeTimestamp());
    tm utc;
    char text[64] = "";
    if (gmtime_s(&utc, &linked) == 0) strftime(text, sizeof(text), "%a %b %d %H:%M:%S %Y", &utc);
    return text;
}

// A release's version.txt (next to rfa_mods.ini) starts "RFA-Mod v0.4.1-beta, built from ...";
// without one (a build of your own), the day this mod was compiled.
std::string ModVersion() {
    FILE* file = nullptr;
    if (_wfopen_s(&file, (rfa::RootDir() + L"\\version.txt").c_str(), L"r") == 0 && file) {
        char line[256] = "";
        const bool read = fgets(line, sizeof(line), file) != nullptr;
        fclose(file);
        std::string text = read ? line : "";
        text = text.substr(0, text.find_first_of(",\r\n"));
        if (text.rfind("RFA-Mod ", 0) == 0) return text;
    }
    std::string date = __DATE__;  // "Nov  1 2026": one space is enough
    for (size_t at; (at = date.find("  ")) != std::string::npos;) date.erase(at, 1);
    return std::string("RFA-Mod dev build ") + date + " " + __TIME__;
}

void MakeBuildStamp() {
    char stamp[256];
    snprintf(stamp, sizeof(stamp), "Red Faction: Armageddon %s build %d %s %s pc | %s",
             reinterpret_cast<const char*>(kGameVersionText), kSteamBuild, ExeDebugName().c_str(), ExeLinkTime().c_str(),
             ModVersion().c_str());
    g_buildStamp = stamp;
    for (size_t at; (at = g_buildStamp.find("  ")) != std::string::npos;) g_buildStamp.erase(at, 1);  // a part left empty
}

// ---------------------------------------------------------------------------------------------
// Drawing the console

constexpr DWORD kConsoleDraw = 0xC7B890;  // void(): the game's console drawing, each frame from 0xC7D540

float g_shown = 0;  // how far it has slid open, 0 to 1
bool g_backgroundSet = false;
bool g_drawnUnderPanel = false;

constexpr Colour kStampColour = {125, 145, 170, 255};
constexpr Colour kShadowColour = {0, 0, 0, 200};
constexpr int kNotifyLines = 4;                  // as in Quake 3
constexpr ULONGLONG kNotifyMilliseconds = 3000;

// Slides it open or shut; false while it is shut.
bool Slide() {
    if (g_consoleSliding) {
        const float step = g_frameSeconds * kSlidesPerSecond;
        g_shown += g_consoleOpen ? step : -step;
        if (g_shown >= 1 || g_shown <= 0) {
            g_shown = std::clamp(g_shown, 0.0f, 1.0f);
            g_consoleSliding = 0;
        }
    } else {
        g_shown = g_consoleOpen ? 1.0f : 0.0f;
    }
    return g_shown > 0;
}

// A line as it is shown: with how many times it was printed in a row, if more than once.
std::string Shown(const Line& line) {
    return line.repeats > 1 ? line.text + "  (x" + std::to_string(line.repeats) + ")" : line.text;
}

// The longest start of `text` (at least one character) that fits in `width` at scale 1.
int CharactersThatFit(const char* text, int length, float width) {
    if (TextWidth(text, length) <= width) return length;
    int low = 1, high = length;
    while (low < high) {
        const int middle = (low + high + 1) / 2;
        if (TextWidth(text, middle) <= width) low = middle;
        else high = middle - 1;
    }
    return low;
}

// A line cut into the rows it takes up, breaking after a space where there is one.
std::vector<std::string> Wrap(const std::string& text, float width) {
    std::vector<std::string> rows;
    size_t start = 0;
    do {
        const char* rest = text.c_str() + start;
        const int left = static_cast<int>(text.size() - start);
        int fits = CharactersThatFit(rest, left, width);
        if (fits < left) {
            int space = fits - 1;
            while (space > 0 && rest[space] != ' ') --space;
            if (space >= fits / 2) fits = space + 1;
        }
        rows.emplace_back(rest, fits);
        start += fits;
    } while (start < text.size());
    return rows;
}

// Sizes and places that depend on the screen and the settings.
struct Metrics {
    float screenScale, textScale, inputScale;
    int fontHeight, rowHeight, typedHeight, padding, inputHeight, edge, left, scrollBarWidth;
    float textWidth;  // how wide output rows may be, at scale 1
};

Metrics Measure() {
    Metrics m;
    m.screenScale = std::max(1.0f, g_screenHeight / 1080.0f);  // bigger screens get bigger text
    m.textScale = g_settings.textScale * m.screenScale;
    m.inputScale = g_settings.inputScale * m.screenScale;
    m.fontHeight = FontHeight();
    m.rowHeight = static_cast<int>(m.fontHeight * m.textScale + 0.5f) + 2;
    m.typedHeight = static_cast<int>(m.fontHeight * m.inputScale + 0.5f);
    m.padding = static_cast<int>(4 * m.screenScale);
    m.inputHeight = m.typedHeight + 2 * m.padding;
    m.edge = std::max(2, static_cast<int>(2 * m.screenScale));
    m.left = g_screenLeft + g_offsetX + 2 * m.padding;
    m.scrollBarWidth = std::max(8, static_cast<int>(8 * m.screenScale));
    m.textWidth = static_cast<float>(g_screenWidth - m.left - 3 * m.scrollBarWidth) / m.textScale;
    return m;
}

// Notify lines, while the console is shut: the newest lines printed in the last 3 seconds, at
// most 4, at the top left of the screen with a shadow (there is no panel behind them).
void DrawNotify(const Metrics& m) {
    std::vector<Line> recent;
    {
        std::lock_guard<std::mutex> lock(g_historyLock);
        const ULONGLONG now = GetTickCount64();
        for (auto line = g_history.rbegin(); line != g_history.rend() && recent.size() < kNotifyLines; ++line) {
            if (now - line->printed > kNotifyMilliseconds) break;
            if (line->fromConsole) recent.push_back(*line);  // not the logs it also shows
        }
    }
    int y = 2 * m.padding;
    int rowsLeft = 2 * kNotifyLines;  // long lines wrap, but not without end
    for (auto line = recent.rbegin(); line != recent.rend(); ++line) {
        for (const std::string& row : Wrap(Shown(*line), m.textWidth)) {
            if (rowsLeft-- <= 0) return;
            DrawString(static_cast<float>(m.left + 1), static_cast<float>(y + 1), row.c_str(), m.textScale, kShadowColour);
            DrawString(static_cast<float>(m.left), static_cast<float>(y), row.c_str(), m.textScale, LineColour(line->kind));
            y += m.rowHeight;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// The mouse
//
// While the console is open the game stops keeping the mouse in the middle of the screen
// (0x4E4070), but it hides Windows' cursor (0xCB13C0, 0xCB1820), so the mod draws its own over the
// console. On the console:
//   - the scroll bar: drag its thumb, or press on its track to jump there and drag from there;
//   - the middle button: scrolls by how far the mouse is above or below where it was pressed,
//     faster the further away, as Windows' autoscroll does. Hold it while moving, or click it and
//     move; another click ends it;
//   - a click on the typed line puts the caret there.
// Clicks on the console don't reach the game (or the menu behind it); clicks elsewhere do.

auto& g_gameWindow = *reinterpret_cast<HWND*>(0x293A880);  // what 0xCB1D10 returns

// What was drawn last frame, for finding what the mouse is on.
struct Drawn {
    bool shown;
    int top, bottom;  // the console, on screen
    bool scrollBar;
    int barX, barWidth, trackTop, trackBottom, thumbTop, thumbBottom, maxBack;
    int inputTop, inputBottom, typedX, first;  // the typed line, and its first character shown
    float inputScale;
};
Drawn g_drawn = {};

bool g_dragging = false;  // the scroll bar's thumb, held
int g_grab = 0;           // where on the thumb it is held
bool g_autoScroll = false;
bool g_autoHeld = false;  // the middle button is still down since it began
POINT g_anchor = {};
ULONGLONG g_autoBegan = 0, g_autoTick = 0;
float g_autoLines = 0;    // lines scrolled but not yet whole
bool g_swallowUp[3] = {};  // a press the console took, so the game doesn't get its release either: left, middle, right

// Window pixels to the size the game draws at (the same, unless the picture is scaled).
bool ToDrawn(POINT& point) {
    RECT client;
    if (!g_gameWindow || !GetClientRect(g_gameWindow, &client) || client.right <= 0 || client.bottom <= 0) return false;
    if (point.x < 0 || point.y < 0 || point.x >= client.right || point.y >= client.bottom) return false;
    point.x = MulDiv(point.x, g_screenWidth, client.right);
    point.y = MulDiv(point.y, g_screenHeight, client.bottom);
    return true;
}

bool MouseNow(POINT& point) {
    return GetCursorPos(&point) && ScreenToClient(g_gameWindow, &point) && ToDrawn(point);
}

// Whether a button is held right now, even if let go outside the window (no message then).
// Windows reads the physical buttons here, so swapped buttons are swapped back.
bool ButtonHeld(bool left) {
    const bool swapped = GetSystemMetrics(SM_SWAPBUTTON) != 0;
    const int key = left ? (swapped ? VK_RBUTTON : VK_LBUTTON) : VK_MBUTTON;
    return (GetAsyncKeyState(key) & 0x8000) != 0;
}

bool OnConsole(const POINT& point) { return g_drawn.shown && point.y >= g_drawn.top && point.y < g_drawn.bottom; }

bool OnScrollBar(const POINT& point) {
    return g_drawn.scrollBar && point.x >= g_drawn.barX - 2 * g_drawn.barWidth && point.y >= g_drawn.trackTop &&
           point.y < g_drawn.trackBottom;
}

// Scrolls so that the thumb's top is at `thumbTop`.
void ThumbTo(int thumbTop) {
    const int thumb = g_drawn.thumbBottom - g_drawn.thumbTop;
    const int travel = g_drawn.trackBottom - g_drawn.trackTop - thumb;
    if (travel <= 0 || g_drawn.maxBack <= 0) return;
    const int fromBottom = std::clamp(g_drawn.trackBottom - thumbTop - thumb, 0, travel);
    std::lock_guard<std::mutex> lock(g_historyLock);
    g_back = std::clamp((fromBottom * g_drawn.maxBack + travel / 2) / travel, 0, g_maxBack);
}

// The caret to the character nearest `x` on the typed line.
void CaretTo(int x) {
    const char* typed = g_typed;
    const int length = static_cast<int>(strnlen(typed, sizeof(g_typed)));
    const int first = std::min(g_drawn.first, length);
    int best = first, nearest = INT_MAX;
    for (int i = first; i <= length; ++i) {
        const int at = g_drawn.typedX + static_cast<int>(TextWidth(typed + first, i - first) * g_drawn.inputScale);
        if (abs(at - x) < nearest) {
            nearest = abs(at - x);
            best = i;
        }
        if (at > x) break;
    }
    g_caret = best;
    g_caretShown = 1;  // shown straight away, as when typing
    TimerStart(&g_caretTimer, nullptr, 300);
}

// A button pressed or let go, where the mouse is on the drawn screen (null: outside the window);
// true when the console took it.
bool ConsoleButton(int button, bool down, const POINT* at) {
    if (!down) {
        const bool swallow = g_swallowUp[button];
        g_swallowUp[button] = false;
        if (button == 0) g_dragging = false;
        if (button == 1 && g_autoScroll && g_autoHeld) {
            g_autoHeld = false;  // held and moved, or held a while: it ends; a quick click keeps it going
            const bool moved = !at || abs(at->y - g_anchor.y) > 12 || abs(at->x - g_anchor.x) > 12;
            if (moved || GetTickCount64() - g_autoBegan > 350) g_autoScroll = false;
        }
        return swallow;
    }
    g_swallowUp[button] = false;
    if (g_autoScroll) {  // any press ends autoscrolling
        g_autoScroll = false;
        g_swallowUp[button] = true;
        return true;
    }
    if (!at || !OnConsole(*at)) return false;
    const POINT point = *at;
    g_swallowUp[button] = true;
    if (button == 0 && OnScrollBar(point)) {
        const bool onThumb = point.y >= g_drawn.thumbTop && point.y < g_drawn.thumbBottom;
        g_grab = onThumb ? point.y - g_drawn.thumbTop : (g_drawn.thumbBottom - g_drawn.thumbTop) / 2;
        g_dragging = true;
        ThumbTo(point.y - g_grab);
    } else if (button == 0 && point.y >= g_drawn.inputTop && point.y < g_drawn.inputBottom) {
        CaretTo(point.x);
    } else if (button == 1) {
        g_autoScroll = g_autoHeld = true;
        g_anchor = point;
        g_autoBegan = g_autoTick = GetTickCount64();
        g_autoLines = 0;
    }
    return true;
}

// Each frame while the console is open: follows a held thumb, and scrolls while autoscrolling.
void FollowMouse(const POINT* mouse, float screenScale) {
    if (g_dragging) {
        if (!ButtonHeld(true)) g_dragging = false;
        else if (mouse) ThumbTo(mouse->y - g_grab);
    }
    if (!g_autoScroll) return;
    if (g_autoHeld && !ButtonHeld(false)) ConsoleButton(1, false, mouse);
    const ULONGLONG now = GetTickCount64();
    const float seconds = std::min(0.1f, (now - g_autoTick) / 1000.0f);
    g_autoTick = now;
    if (!mouse) return;
    const float away = static_cast<float>(abs(mouse->y - g_anchor.y)) / screenScale - 10;  // still within 10 pixels
    if (away > 0) {
        const float linesPerSecond = away * 0.2f + away * away * 0.002f;  // 50 pixels: 15, 300: 240
        g_autoLines += (mouse->y < g_anchor.y ? linesPerSecond : -linesPerSecond) * seconds;
    }
    const int whole = static_cast<int>(g_autoLines);
    if (whole) {
        Scroll(whole);
        g_autoLines -= whole;
    }
}

// The console is shut: nothing held, and the game gets every click again.
void StopMouse() {
    g_dragging = g_autoScroll = g_autoHeld = false;
    for (bool& swallow : g_swallowUp) swallow = false;
    g_drawn.shown = false;
}

// An arrow, drawn a pixel row at a time: white with a black edge.
void DrawCursor(int x, int y, float screenScale) {
    const int pixel = std::max(1, static_cast<int>(screenScale + 0.5f));
    constexpr int kRows = 13;
    for (int row = -1; row <= kRows; ++row) {
        const int width = std::clamp(row, 0, kRows - 1) * 2 / 3 + 1;
        DrawRectangle(x - pixel, y + row * pixel, (width + 2) * pixel, pixel, {0, 0, 0, 255});
        if (row >= 0 && row < kRows) DrawRectangle(x, y + row * pixel, width * pixel, pixel, {255, 255, 255, 255});
    }
}

// Where the middle button was pressed: a dot with arrows up and down.
void DrawAnchor(const Metrics& m) {
    const int size = std::max(6, static_cast<int>(6 * m.screenScale));
    DrawRectangle(g_anchor.x - size / 2, g_anchor.y - size / 2, size, size, kAccentColour);
    const float half = TextWidth("^") * m.textScale / 2;
    DrawString(g_anchor.x - half, static_cast<float>(g_anchor.y - size / 2 - m.rowHeight), "^", m.textScale, kAccentColour);
    DrawString(g_anchor.x - half, static_cast<float>(g_anchor.y + size / 2), "v", m.textScale, kAccentColour);
}

// The game's handler for mouse window messages (buttons, wheel, raw input): cdecl (message,
// wParam, lParam). It turns the buttons into the game's button states (0xCB08E0) and adds wheel
// movement to a total that weapon switching reads.
constexpr DWORD kMouseMessage = 0xCDF220;
constexpr size_t kMouseMessagePrologue = 6;  // push ebp / mov ebp,esp / sub esp,0Ch

using MouseMessageFn = void(__cdecl*)(UINT, WPARAM, LPARAM);
MouseMessageFn g_mouseMessage;
int g_wheel = 0;  // wheel movement not yet turned into whole notches (high-resolution wheels)

void __cdecl MouseMessage(UINT message, WPARAM wParam, LPARAM lParam) {
    if (g_consoleOpen && rfa::menu::Want(RFA_MENUS_MOUSE)) {  // over one of the in-game menus: theirs
        if (message == WM_LBUTTONUP) g_dragging = false;
        return;
    }
    if (g_consoleOpen && g_drawsConsole) {
        if (message == WM_MOUSEWHEEL) {
            g_autoScroll = false;
            g_wheel += GET_WHEEL_DELTA_WPARAM(wParam);
            for (; g_wheel >= WHEEL_DELTA; g_wheel -= WHEEL_DELTA) Scroll(kLinesPerNotch);
            for (; g_wheel <= -WHEEL_DELTA; g_wheel += WHEEL_DELTA) Scroll(-kLinesPerNotch);
            RFA_LOG_FIRST(3, "Scrolled the console with the mouse wheel");
            return;  // the game doesn't see it
        }
        POINT point = {static_cast<short>(LOWORD(lParam)), static_cast<short>(HIWORD(lParam))};
        const bool inWindow = ToDrawn(point);
        int button = -1;
        bool down = false;
        switch (message) {
        case WM_LBUTTONDOWN: case WM_LBUTTONDBLCLK: button = 0; down = true; break;
        case WM_LBUTTONUP: button = 0; break;
        case WM_MBUTTONDOWN: case WM_MBUTTONDBLCLK: button = 1; down = true; break;
        case WM_MBUTTONUP: button = 1; break;
        case WM_RBUTTONDOWN: case WM_RBUTTONDBLCLK: button = 2; down = true; break;
        case WM_RBUTTONUP: button = 2; break;
        }
        if (button >= 0 && ConsoleButton(button, down, inWindow ? &point : nullptr)) {
            RFA_LOG_FIRST(3, "The console took a mouse click");
            return;
        }
    } else {
        g_wheel = 0;
    }
    g_mouseMessage(message, wParam, lParam);
}

void DrawConsole() {
    if (rfa::menu::Want(RFA_MENUS_ALONE)) return;  // the in-game menus were opened with their own key
    if (!g_drawRectangle || !g_drawText) return;
    if (!g_backgroundSet) {  // the mod's darker panel, unless console_set_bg_color was used already
        g_backgroundSet = true;
        if (memcmp(&g_background, &kGameBackground, sizeof(Colour)) == 0)
            g_background = {10, 12, 16, static_cast<BYTE>(g_settings.opacity * 255 / 100)};
    }
    const Metrics m = Measure();
    if (!Slide()) {
        StopMouse();
        if (g_settings.notify) DrawNotify(m);
        return;
    }
    if (TimerDone(&g_caretTimer, nullptr)) {  // the caret's blink, as the game's drawing did it
        TimerStart(&g_caretTimer, nullptr, 300);
        g_caretShown = !g_caretShown;
    }
    // The build stamp gets a thin row of its own just above the typed line, at the font's own size.
    const bool stamped = g_settings.buildStamp && !g_buildStamp.empty();
    const float stampScale = m.screenScale;
    const int stampRow = stamped ? static_cast<int>(m.fontHeight * stampScale + 0.5f) + m.padding : 0;
    const int minimum = m.inputHeight + stampRow + 4 * m.rowHeight + 2 * m.padding;
    const int width = g_screenWidth, left = m.left, rowHeight = m.rowHeight, padding = m.padding;
    const float textScale = m.textScale, inputScale = m.inputScale;

    // At the top of the screen, or at the bottom below the render debug panel while it is up.
    const bool atBottom = g_panelShown != 0;
    int height = std::max(minimum, static_cast<int>(g_screenHeight * g_settings.height));
    if (atBottom) height = std::max(minimum, std::min(height, g_screenHeight - static_cast<int>(PanelBottom()) - padding));
    height = std::min(height, g_screenHeight);
    const int top = atBottom ? g_screenHeight - static_cast<int>(height * g_shown)
                             : static_cast<int>(height * g_shown) - height;

    // Top to bottom: the output, the build stamp's row, the typed line, the edge (at the top instead
    // while at the bottom of the screen).
    const int inputTop = top + height - (atBottom ? 0 : m.edge) - m.inputHeight;
    const int stampTop = inputTop - stampRow;
    DrawRectangle(0, top, width, height, g_background);
    DrawRectangle(0, inputTop, width, m.inputHeight, Brighter(g_background, 22, 225));
    DrawRectangle(0, inputTop - 1, width, 1, Brighter(g_background, 60, 255));
    DrawRectangle(0, atBottom ? top : top + height - m.edge, width, m.edge, kAccentColour);

    // The mouse: where it is, and the scroll bar or autoscroll it moves (from last frame's layout).
    POINT mouse;
    const bool mouseIn = g_consoleOpen && MouseNow(mouse);
    FollowMouse(mouseIn ? &mouse : nullptr, m.screenScale);

    // The output, from the newest line up (or from where it is scrolled back to).
    const int outputTop = top + (atBottom ? m.edge : 0) + padding;
    const int outputBottom = stampTop - (stamped ? 0 : padding);
    const int rows = std::max(1, (outputBottom - outputTop) / rowHeight);
    std::vector<Line> lines, oldest;
    int total, back;
    {
        std::lock_guard<std::mutex> lock(g_historyLock);
        total = static_cast<int>(g_history.size());
        back = std::min(g_back, std::max(0, total - 1));
        const int bottomLine = total - 1 - back;
        for (int i = std::max(0, bottomLine - rows + 1); i <= bottomLine; ++i) lines.push_back(g_history[i]);
        for (int i = 0; i < std::min(total, rows); ++i) oldest.push_back(g_history[i]);
    }
    const int outputRows = back > 0 ? rows - 1 : rows;  // scrolled back: the last row says so
    int y = outputBottom - (rows - outputRows) * rowHeight;
    int rowsLeft = outputRows;
    for (auto line = lines.rbegin(); line != lines.rend() && rowsLeft > 0; ++line) {
        const std::vector<std::string> wrapped = Wrap(Shown(*line), m.textWidth);
        for (auto row = wrapped.rbegin(); row != wrapped.rend() && rowsLeft > 0; ++row, --rowsLeft) {
            y -= rowHeight;
            DrawString(static_cast<float>(left), static_cast<float>(y), row->c_str(), textScale, LineColour(line->kind));
        }
    }

    // How far back it can go: until the oldest line is at the top (with the row of ^ marks).
    int maxBack = 0, rowsUsed = 0;
    for (int i = 0; i < static_cast<int>(oldest.size()); ++i) {
        rowsUsed += static_cast<int>(Wrap(Shown(oldest[i]), m.textWidth).size());
        if (rowsUsed >= rows - 1) {
            maxBack = total - 1 - i;
            break;
        }
    }
    {
        std::lock_guard<std::mutex> lock(g_historyLock);
        g_maxBack = maxBack;
        g_back = std::min(g_back, maxBack);
        g_pageLines = outputRows;
    }

    if (back > 0) {  // Quake 3's row of ^ marks under the text, and how far back it is
        char where[96];
        snprintf(where, sizeof(where), "%d of %d lines back - End: newest", back, total);
        const float rowY = static_cast<float>(outputBottom - rowHeight);
        const int whereX = width - 3 * m.scrollBarWidth - static_cast<int>(TextWidth(where) * textScale);
        DrawString(static_cast<float>(whereX), rowY, where, textScale, kDimColour);
        const int step = std::max(8, static_cast<int>(TextWidth("^   ") * textScale));
        for (int x = left; x + step < whereX; x += step) DrawString(static_cast<float>(x), rowY, "^", textScale, kAccentColour);
    }

    // The scroll bar: where the view is in the history. Brighter under the mouse and while held.
    Drawn drawn = {};
    drawn.shown = true;
    drawn.top = std::max(0, top);
    drawn.bottom = top + height;
    if (maxBack > 0) {
        const int trackHeight = outputBottom - outputTop;
        const int thumb = std::max(2 * rowHeight, trackHeight * outputRows / std::max(total, 1));
        const int thumbBottom = outputBottom - (trackHeight - thumb) * std::min(back, maxBack) / maxBack;
        const int x = width - 2 * m.scrollBarWidth;
        drawn.scrollBar = true;
        drawn.barX = x;
        drawn.barWidth = m.scrollBarWidth;
        drawn.trackTop = outputTop;
        drawn.trackBottom = outputBottom;
        drawn.thumbTop = thumbBottom - thumb;
        drawn.thumbBottom = thumbBottom;
        drawn.maxBack = maxBack;
        const bool lit = g_dragging || (mouseIn && mouse.x >= x - 2 * m.scrollBarWidth && mouse.y >= outputTop &&
                                        mouse.y < outputBottom);
        DrawRectangle(x, outputTop, m.scrollBarWidth, trackHeight, Brighter(g_background, lit ? 45 : 30, 160));
        DrawRectangle(x, thumbBottom - thumb, m.scrollBarWidth, thumb, lit ? Brighter(kAccentColour, 70, 255) : kAccentColour);
    }

    if (stamped) {  // right-aligned, as far left as the text if the screen is narrow
        const int stampWidth = static_cast<int>(TextWidth(g_buildStamp.c_str()) * stampScale);
        const int x = std::max(left, width - 2 * m.scrollBarWidth - stampWidth);
        DrawString(static_cast<float>(x), static_cast<float>(stampTop + m.padding / 2), g_buildStamp.c_str(), stampScale,
                   kStampColour);
    }

    // The line being typed: "]", then the text, moved left if needed to keep the caret in view.
    const float typedY = static_cast<float>(inputTop + padding);
    const int promptWidth = static_cast<int>(TextWidth("] ") * inputScale);
    DrawString(static_cast<float>(left), typedY, "]", inputScale, kAccentColour);
    const char* typed = g_typed;
    const int length = static_cast<int>(strnlen(typed, sizeof(g_typed)));
    const int caret = std::clamp(g_caret, 0, length);
    int hintWidth = 0;  // while pasted lines wait, how many, on the right
    if (!g_pasted.empty()) {
        char hint[96];
        snprintf(hint, sizeof(hint), "%u more pasted - Enter: run this, Esc: drop them", static_cast<unsigned>(g_pasted.size()));
        hintWidth = static_cast<int>(TextWidth(hint) * textScale) + 2 * padding;
        const float hintY = inputTop + (m.inputHeight - m.fontHeight * textScale) / 2;
        DrawString(static_cast<float>(width - 2 * m.scrollBarWidth - hintWidth + 2 * padding), hintY, hint, textScale, kDimColour);
    }
    const float room = static_cast<float>(width - left - promptWidth - 3 * m.scrollBarWidth - hintWidth) / inputScale;
    int first = 0;
    while (first < caret && TextWidth(typed + first, caret - first) > room) ++first;
    const int typedX = left + promptWidth;
    const int fits = CharactersThatFit(typed + first, length - first, room);  // not past the scroll bar
    DrawString(static_cast<float>(typedX), typedY, std::string(typed + first, fits).c_str(), inputScale, kTypedColour);
    if (g_caretShown) {
        const int x = typedX + static_cast<int>(TextWidth(typed + first, caret - first) * inputScale);
        DrawRectangle(x, static_cast<int>(typedY), std::max(2, static_cast<int>(2 * inputScale)), m.typedHeight, kCaretColour);
    }
    drawn.inputTop = inputTop;
    drawn.inputBottom = inputTop + m.inputHeight;
    drawn.typedX = typedX;
    drawn.first = first;
    drawn.inputScale = inputScale;
    g_drawn = drawn;

    // The mouse on top of it all, over the console (menus and the render debug panel draw their own,
    // and so do the in-game menus while they are up).
    if (g_autoScroll) DrawAnchor(m);
    if (mouseIn && (OnConsole(mouse) || g_dragging || g_autoScroll) && !rfa::menu::Want(RFA_MENUS_SHOWN))
        DrawCursor(mouse.x, mouse.y, m.screenScale);
}

bool ConsoleCanDraw() { return g_consoleReady && !g_textMode; }  // what 0xC7D540 checks before drawing it

// In place of the game's console drawing (0xC7B890), each frame from 0xC7D540.
void __cdecl ConsoleDraw() {
    if (g_drawnUnderPanel) {  // drawn already this frame, under the panel
        g_drawnUnderPanel = false;
        return;
    }
    DrawConsole();
}

using PanelDrawFn = void(__cdecl*)();
PanelDrawFn g_panelDraw;

void __cdecl PanelDraw() {
    if (g_panelShown && ConsoleCanDraw()) {
        DrawConsole();
        g_drawnUnderPanel = true;
    }
    g_panelDraw();
}

// ---------------------------------------------------------------------------------------------
// console.ini

// The game's own console functions and data in the publicbeta exe.
constexpr DWORD kRunConsoleCommand = 0xC7BDB0;  // int(const char* format, ...): what Enter in the console does
constexpr DWORD kRunScriptFile = 0xC7E080;      // int(const char* file): runs the game's own script files
constexpr size_t kRunScriptFilePrologue = 10;   // push ebp / mov ebp,esp / push -1 / push <handler table>
constexpr DWORD kGameplayStart = 0x540840;      // void(): the game's "gameplay starts" step after loading
constexpr size_t kGameplayStartPrologue = 9;    // push ebp / mov ebp,esp / sub esp,370h
constexpr size_t kMaxCommandLength = 200;       // the game formats each command into a 256-byte buffer

auto const RunConsoleCommand = reinterpret_cast<int(__cdecl*)(const char*, ...)>(kRunConsoleCommand);

// One entry of the game's console command list (registered by 0xC7BD70).
struct ConsoleCommand {
    const char* name;
    const char* description;
    void* handler;
    int type;  // see CommandType
};
enum CommandType { kOnOff = 0, kWholeNumber = 1, kNumber = 2, kCommand = 3, kVector = 4 };

auto& g_commands = *reinterpret_cast<ConsoleCommand* (*)[0xD48]>(0x28FBA88);  // sorted by name
auto& g_commandCount = *reinterpret_cast<int*>(0x28FF1C4);

const ConsoleCommand* FindCommand(const std::string& name) {
    for (int i = 0; i < g_commandCount; ++i)
        if (g_commands[i] && _stricmp(g_commands[i]->name, name.c_str()) == 0) return g_commands[i];
    return nullptr;
}

std::string Trim(const std::string& text) {
    const size_t start = text.find_first_not_of(" \t\r\n");
    if (start == std::string::npos) return "";
    return text.substr(start, text.find_last_not_of(" \t\r\n") - start + 1);
}

// What to type after the name: 1 / 0 become on / off where the game expects on / off.
std::string Argument(const ConsoleCommand& command, const std::string& value) {
    if (command.type == kOnOff || command.type == kCommand) {
        if (value == "1") return "on";
        if (value == "0") return "off";
    }
    return value;
}

// Applies every setting in one [section] of bin\console.ini, in the order written.
void ApplySection(const char* section) {
    const std::wstring path = rfa::RootDir() + L"\\console.ini";
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"r") != 0 || !file) return;
    std::string current;
    int count = 0;
    char buffer[1024];
    while (fgets(buffer, sizeof(buffer), file)) {
        const std::string line = Trim(buffer);
        if (line.empty() || line[0] == ';' || line[0] == '#') continue;
        if (line[0] == '[') {
            current = Trim(line.substr(1, line.find(']') - 1));
            continue;
        }
        if (_stricmp(current.c_str(), section) != 0) continue;
        const size_t equals = line.find('=');
        const std::string name = Trim(line.substr(0, equals));
        const std::string value = equals == std::string::npos ? "" : Trim(line.substr(equals + 1));
        const ConsoleCommand* command = FindCommand(name);
        if (!command) {
            rfa::Log("console.ini [%s]: the console has no command named %s; skipped", section, name.c_str());
            continue;
        }
        const std::string argument = Argument(*command, value);
        const std::string typed = argument.empty() ? command->name : std::string(command->name) + " " + argument;
        if (typed.size() > kMaxCommandLength) {
            rfa::Log("console.ini [%s]: %s is longer than %u characters; skipped", section, name.c_str(),
                     static_cast<unsigned>(kMaxCommandLength));
            continue;
        }
        rfa::Log("console.ini [%s]: %s", section, typed.c_str());
        RunConsoleCommand("%s", typed.c_str());
        ++count;
    }
    fclose(file);
    if (count) rfa::Log("console.ini [%s]: applied %d setting(s)", section, count);
}

using RunScriptFileFn = int(__cdecl*)(const char*);
RunScriptFileFn g_runScriptFile;

// The game runs gamestart.txt once it has finished starting up; [Startup] follows it.
int __cdecl RunScriptFile(const char* file) {
    const int result = g_runScriptFile(file);
    if (file && _stricmp(file, "gamestart.txt") == 0) ApplySection("Startup");
    return result;
}

using GameplayStartFn = void(__cdecl*)();
GameplayStartFn g_gameplayStart;

void __cdecl GameplayStart() {
    g_gameplayStart();
    ApplySection("Gameplay");
}

// ---------------------------------------------------------------------------------------------
// Typing: pasting, Tab, and a % in what you run

constexpr size_t kTypedRoom = 124;      // the game stops typing there (the echo "] <line>" fills 0x80 bytes)
constexpr size_t kMostPastedLines = 100;
constexpr size_t kMostPasted = 65536;   // characters read from the clipboard

// Puts `text` into the typed line at the caret (or in place of the line), as much as fits, as the
// game's typing does; false if some didn't fit.
bool TypeIn(const std::string& text, bool replaceLine) {
    std::string line = replaceLine ? "" : std::string(g_typed, strnlen(g_typed, sizeof(g_typed)));
    const size_t caret = replaceLine ? 0 : std::min(static_cast<size_t>(std::max(g_caret, 0)), line.size());
    const size_t room = line.size() < kTypedRoom ? kTypedRoom - line.size() : 0;
    const std::string fits = text.substr(0, room);
    line.insert(caret, fits);
    strcpy_s(g_typed, line.c_str());
    g_caret = static_cast<int>(caret + fits.size());
    snprintf(g_prompted, sizeof(g_prompted), "] %s", g_typed);  // as the game remakes it after each key
    g_caretShown = 1;
    TimerStart(&g_caretTimer, nullptr, 300);
    return fits.size() == text.size();
}

// Pasting (Ctrl+V or Shift+Insert). The console can only type plain ASCII on one line, so what is
// pasted is cleaned up first: tabs and other spaces become a space, typographic quotes, dashes and
// the ellipsis their ASCII forms, invisible and control characters go, and anything else it can't
// type is left out (and said so). Each line is trimmed, a "]" in front (a line copied from the
// console or console.log) is dropped, and empty and comment lines (; # //) are skipped. The first
// line goes in at the caret; the others wait, and each Enter runs the line and brings up the next
// (Esc drops them), so nothing pasted runs without you pressing Enter.

// Adds what the console can type of `c` to `line`; false if it can't type it at all.
bool AppendTypeable(wchar_t c, std::string& line) {
    if (c >= 0x20 && c < 0x7F) line += static_cast<char>(c);
    else if (c == L'\t' || c == 0xA0 || (c >= 0x2000 && c <= 0x200A) || c == 0x202F || c == 0x3000) line += ' ';
    else if (c == 0x2018 || c == 0x2019 || c == 0x201A || c == 0x2032) line += '\'';
    else if (c == 0x201C || c == 0x201D || c == 0x201E || c == 0x2033) line += '"';
    else if ((c >= 0x2010 && c <= 0x2015) || c == 0x2212) line += '-';
    else if (c == 0x2026) line += "...";
    else if (c < 0x20 || c == 0x7F || (c >= 0x200B && c <= 0x200D) || c == 0x2060 || c == 0xFEFF) {}  // nothing to see
    else return false;
    return true;
}

std::wstring ClipboardText() {
    std::wstring text;
    if (!OpenClipboard(g_gameWindow)) return text;
    if (HANDLE data = GetClipboardData(CF_UNICODETEXT)) {
        if (auto characters = static_cast<const wchar_t*>(GlobalLock(data))) {
            const size_t most = std::min<size_t>(GlobalSize(data) / sizeof(wchar_t), kMostPasted);
            text.assign(characters, wcsnlen(characters, most));
            GlobalUnlock(data);
        }
    }
    CloseClipboard();
    return text;
}

std::vector<std::string> PastedLines(const std::wstring& text, int& leftOut) {
    std::vector<std::string> lines;
    std::string line;
    auto finish = [&] {
        std::string command = Trim(line);
        line.clear();
        if (!command.empty() && command[0] == ']') command = Trim(command.substr(1));
        if (command.empty() || command[0] == ';' || command[0] == '#' || command.rfind("//", 0) == 0) return;
        lines.push_back(command);
    };
    for (wchar_t c : text) {
        if (c == L'\r' || c == L'\n') finish();
        else if (!AppendTypeable(c, line)) ++leftOut;
    }
    finish();
    return lines;
}

void Paste() {
    int leftOut = 0;
    std::vector<std::string> lines = PastedLines(ClipboardText(), leftOut);
    if (leftOut > 0)
        AddLine("Pasted without " + std::to_string(leftOut) + " character(s) the console can't type", Kind::Warning);
    if (lines.empty()) {
        if (!leftOut) AddLine("Nothing to paste: the clipboard holds no text the console can run", Kind::Info);
        return;
    }
    if (lines.size() > kMostPastedLines) {
        AddLine("Pasted the first " + std::to_string(kMostPastedLines) + " of " + std::to_string(lines.size()) + " lines",
                Kind::Warning);
        lines.resize(kMostPastedLines);
    }
    bool cut = !TypeIn(lines[0], false);
    g_pasted.assign(lines.begin() + 1, lines.end());
    for (std::string& waiting : g_pasted) {
        if (waiting.size() <= kTypedRoom) continue;
        waiting.resize(kTypedRoom);
        cut = true;
    }
    if (cut)
        AddLine("A command line holds " + std::to_string(kTypedRoom) + " characters; the rest of a longer pasted line was left out",
                Kind::Warning);
    if (!g_pasted.empty())
        AddLine("Pasted " + std::to_string(lines.size()) + " lines: Enter runs each in turn, Esc drops the rest", Kind::Info);
    RFA_LOG_FIRST(3, "Pasted %u line(s) into the console", static_cast<unsigned>(lines.size()));
}

// Tab (in place of the game's 0xC7D200). The game matched the whole line against the command names,
// so with arguments typed it found nothing ("No console command found to match '<line>'"), and it
// listed matches by name alone. This completes the first word only, lists the matches with what
// each does, and with arguments typed leaves the line as it is and says what the command is.
constexpr DWORD kCompleteCommand = 0xC7D200;  // void(), from the key handling (0xC7CEF3)

void SayWhatItIs(const ConsoleCommand& command) {
    std::string line = std::string("  ") + command.name;
    if (command.description && *command.description) line += std::string(" - ") + command.description;
    AddLine(line, Kind::Info);
}

// What a setting takes (commands take whatever their own code reads).
void SayWhatItTakes(const ConsoleCommand& command) {
    const std::string name = command.name;
    switch (command.type) {
    case kOnOff: AddLine("  " + name + " on, " + name + " off, or " + name + " alone to switch it; ? " + name + " shows it", Kind::Info); break;
    case kWholeNumber: AddLine("  " + name + " <whole number>; ? " + name + " shows it", Kind::Info); break;
    case kNumber: AddLine("  " + name + " <number>; ? " + name + " shows it", Kind::Info); break;
    case kVector: AddLine("  a colour or vector setting; ? " + name + " shows it", Kind::Info); break;
    }
}

bool AsksAbout(const std::string& word) {  // the game's status (?) and help (help, man) words
    return word == "?" || _stricmp(word.c_str(), "help") == 0 || _stricmp(word.c_str(), "man") == 0;
}

void __cdecl CompleteCommand() {
    const std::string typed(g_typed, strnlen(g_typed, sizeof(g_typed)));
    // The command's name: the first word, or the next one after ?, help or man.
    size_t start = typed.find_first_not_of(' ');
    std::string lead;
    if (start != std::string::npos) {
        const size_t end = std::min(typed.find(' ', start), typed.size());
        if (end < typed.size() && AsksAbout(typed.substr(start, end - start))) {
            lead = typed.substr(start, end - start) + " ";
            start = typed.find_first_not_of(' ', end);
        }
    }
    if (start == std::string::npos) {
        AddLine("Tab completes a command's name: type how it starts first (help lists them all)", Kind::Info);
        return;
    }
    const size_t end = std::min(typed.find(' ', start), typed.size());
    const std::string word = typed.substr(start, end - start);
    std::vector<const ConsoleCommand*> matches;
    for (int i = 0; i < g_commandCount; ++i) {
        const ConsoleCommand* command = g_commands[i];
        if (command && command->name && _strnicmp(command->name, word.c_str(), word.size()) == 0) matches.push_back(command);
    }

    if (end < typed.size()) {  // arguments typed: the line stays, and what the command is
        if (const ConsoleCommand* command = FindCommand(word)) {
            SayWhatItIs(*command);
            SayWhatItTakes(*command);
            return;
        }
        AddLine("No console command is named '" + word + "'", Kind::Error);
        if (!matches.empty() && matches.size() <= 20) {
            AddLine("Commands starting with '" + word + "':", Kind::Info);
            for (const ConsoleCommand* command : matches) SayWhatItIs(*command);
        }
        return;
    }

    if (matches.empty()) {
        AddLine("No console command starts with '" + word + "'", Kind::Error);
        return;
    }
    if (matches.size() == 1) {
        TypeIn(lead + matches[0]->name + " ", true);
        return;
    }
    AddLine("Commands starting with '" + word + "' (" + std::to_string(matches.size()) + "):", Kind::Info);
    size_t common = strlen(matches[0]->name);  // as far as they all start the same
    for (const ConsoleCommand* command : matches) {
        SayWhatItIs(*command);
        size_t same = 0;
        while (same < common && command->name[same] &&
               tolower(static_cast<unsigned char>(command->name[same])) == tolower(static_cast<unsigned char>(matches[0]->name[same])))
            ++same;
        common = same;
    }
    if (common > word.size()) TypeIn(lead + std::string(matches[0]->name, common), true);
}

// The game's command runner (0xC7BDB0) is printf-style, and Enter hands it the typed line as its
// format, so a % in the line was read as a format (rubbish, or a crash). The typed line is now run
// as text; other callers' formats are written out first, into the 256 bytes the game uses.
constexpr size_t kRunConsoleCommandPrologue = 9;  // push ebp / mov ebp,esp / sub esp,128h
using RunConsoleCommandFn = int(__cdecl*)(const char* format, ...);
RunConsoleCommandFn g_runConsoleCommand;

int __cdecl RunCommandAsText(const char* format, ...) {
    if (format == g_typed) return g_runConsoleCommand("%s", format);
    char line[256];
    va_list arguments;
    va_start(arguments, format);
    vsnprintf(line, sizeof(line), format ? format : "", arguments);
    va_end(arguments);
    return g_runConsoleCommand("%s", line);
}

// ---------------------------------------------------------------------------------------------
// The level editor's link
//
// The game shows the editor what it shows, every frame, in a small block of shared memory named
// "Local\RFAModEditorLink" (see SharedView, and GameLink.cs in the editor, which reads it while
// it follows the game). It holds the object picked in the game (the console's debug target: Q in
// slew mode, or target_by_handle), where the camera is, which way it looks and its field of view.
//
// The editor sends console commands back through a file, bin\link\to_game.txt (its "Show in
// game": set_camera_pos ..., target_by_handle ...), checked ten times a second while the folder
// bin\link exists (the editor makes it). Each line is run as if typed into the console, then the
// file is deleted. A file more than 30 seconds old (sent while the game wasn't running) is
// deleted without running it. The editor can also ask where objects are now (see
// AnswerWhereWanted).

constexpr DWORD kFrameUpdate = 0x544CB0;      // void(): called once a frame by the main loop (0xA44B90)
constexpr size_t kFrameUpdatePrologue = 6;    // push ebp / mov ebp,esp / sub esp,30h
constexpr ULONGLONG kCheckEveryMs = 100;
constexpr ULONGLONG kOldCommandsSeconds = 30;

auto& g_debugTarget = *reinterpret_cast<DWORD*>(0x10F78FC);           // the picked object's handle (set by target_by_handle 0x4D4E50)
auto& g_cameraPosition = *reinterpret_cast<float(*)[3]>(0x11B9C6C);   // camera manager (0x11B9C40) +0x2C, set by set_camera_pos
auto& g_cameraRotation = *reinterpret_cast<float(*)[9]>(0x11B9C90);   // +0x50: rows right, up, forward
auto& g_cameraFov = *reinterpret_cast<float*>(0x11B9D00);             // +0xC0: the FOV the frame is drawn with, vertical degrees

// The shared memory's layout, version 1 (80 bytes). While the game writes it, `sequence` is odd;
// the editor reads `sequence`, the rest, then `sequence` again, and reads again if it changed or
// was odd, so it never sees half of one frame and half of another.
struct SharedView {
    uint32_t magic;              // 'RFAL'
    uint32_t version;            // 1
    volatile LONG sequence;      // odd while being written
    uint32_t frame;              // counts the frames written
    ULONGLONG written;           // when, as a Windows file time (UTC): the editor can tell the game is running
    uint32_t picked;             // the picked object's handle
    float position[3];           // the camera, in world coordinates
    float rotation[9];           // its rows: right, up, forward
    float fov;                   // vertical degrees
};
static_assert(sizeof(SharedView) == 80, "the editor reads this layout");
constexpr wchar_t kSharedName[] = L"Local\\RFAModEditorLink";
constexpr uint32_t kSharedMagic = 0x4C414652;  // "RFAL"

SharedView* g_shared = nullptr;
DWORD g_lastPicked = 0;
ULONGLONG g_lastCheck = 0;

std::wstring LinkFolder() { return rfa::RootDir() + L"\\link"; }

// Makes the shared memory (or opens it, if an editor still has it from an earlier run).
void ShareWithEditor() {
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(SharedView), kSharedName);
    if (!mapping) {
        rfa::Log("Editor link: could not make the shared memory (error %lu); the editor can't follow the game", GetLastError());
        return;
    }
    g_shared = static_cast<SharedView*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, sizeof(SharedView)));
    if (!g_shared) return;
    g_shared->magic = kSharedMagic;
    g_shared->version = 1;
}

// Puts this frame's view into the shared memory.
void ShowEditor() {
    if (!g_shared) return;
    InterlockedIncrement(&g_shared->sequence);  // odd: being written
    g_shared->frame++;
    FILETIME now;
    GetSystemTimeAsFileTime(&now);
    g_shared->written = (static_cast<ULONGLONG>(now.dwHighDateTime) << 32) | now.dwLowDateTime;
    g_shared->picked = g_debugTarget;
    for (int i = 0; i < 3; ++i) g_shared->position[i] = g_cameraPosition[i];
    for (int i = 0; i < 9; ++i) g_shared->rotation[i] = g_cameraRotation[i];
    g_shared->fov = g_cameraFov;
    InterlockedIncrement(&g_shared->sequence);  // even: done
    if (g_debugTarget != g_lastPicked) {
        g_lastPicked = g_debugTarget;
        rfa::Log("Editor link: picked 0x%08X", static_cast<unsigned>(g_lastPicked));
    }
}

bool RunEditorEdit(const std::string& line);  // the editor's live edits (below)

// Runs the commands the editor sent, if any, and deletes them.
void RunEditorCommands() {
    const std::wstring path = LinkFolder() + L"\\to_game.txt";
    WIN32_FILE_ATTRIBUTE_DATA info;
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &info)) return;
    FILETIME nowTime;
    GetSystemTimeAsFileTime(&nowTime);
    ULARGE_INTEGER now, written;
    now.LowPart = nowTime.dwLowDateTime;
    now.HighPart = nowTime.dwHighDateTime;
    written.LowPart = info.ftLastWriteTime.dwLowDateTime;
    written.HighPart = info.ftLastWriteTime.dwHighDateTime;
    const ULONGLONG ageSeconds = now.QuadPart > written.QuadPart ? (now.QuadPart - written.QuadPart) / 10000000 : 0;

    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"r") != 0 || !file) return;  // try again next time
    std::vector<std::string> lines;
    char buffer[1024];
    while (fgets(buffer, sizeof(buffer), file)) lines.push_back(Trim(buffer));
    fclose(file);
    DeleteFileW(path.c_str());

    if (ageSeconds > kOldCommandsSeconds) {
        rfa::Log("Editor link: skipped commands the editor sent %u seconds ago", static_cast<unsigned>(ageSeconds));
        return;
    }
    for (const std::string& line : lines) {
        if (line.empty() || line[0] == ';' || line[0] == '#') continue;
        if (RunEditorEdit(line)) continue;
        if (line.size() > kMaxCommandLength) {
            rfa::Log("Editor link: a command longer than %u characters was skipped", static_cast<unsigned>(kMaxCommandLength));
            continue;
        }
        rfa::Log("Editor link: %s", line.c_str());
        RunConsoleCommand("%s", line.c_str());
    }
}

// Where objects are now, for the editor's "props as in the game". Clutter (general_mover: debris,
// barrels, crates, wreck pieces) is moved by physics: it falls and settles when the level starts,
// so the game shows it elsewhere than the zone file puts it.
//
// The editor writes bin\link\where_wanted.txt: the handles of the objects it wants, one per line
// (0x... hex). The game answers in bin\link\where_now.txt, a line per handle: the handle, then the
// object's position (3 numbers) and rotation rows right, up, forward (9 numbers) as it is this
// frame, or "gone" if the game has no such object now (not loaded, or broken up).

constexpr DWORD kObjectList = 0x1870630;  // the game's objects (the debug panel 0x6698C0 and target_by_handle use it)
constexpr DWORD kFindObject = 0x6CB9E0;   // object* (list, handle), a method of the list: null if none, or being removed

// The game's method takes the list in ECX and the handle on the stack, and clears the stack itself.
// __fastcall does the same with its first argument in ECX, its second in EDX (unused here) and
// the rest on the stack, so the method can be called as this.
using FindObjectFn = void*(__fastcall*)(void* list, void* unused, DWORD handle);
auto const FindObject = reinterpret_cast<FindObjectFn>(kFindObject);

// The start of an object as the game keeps it, as in Red Faction: Guerrilla: its class's function
// table, then its position, then its rotation (rows right, up, forward). The debug panel reads the
// position here for "CLOE pos" (0x66ABBC).
struct GameObject {
    void* functions;
    float position[3];
    float rotation[9];
};

// The editor's live edits (its Live button): lines among its commands that aren't the console's.
//   editor_move <handle> <x y z> <9 numbers>   moves and turns the object (rotation rows right, up,
//       forward) with the object's own "set position and rotation" (slot 10 of its functions, at
//       +0x28: thiscall, the position, the rotation, 1), as the mission scripts' object_set_position
//       (0x9203B0) does;
//   editor_chunk <asset list> <container>      loads a chunk the level hasn't (below);
//   editor_reload                              loads the level again and puts the player back (below);
//   editor_rescan                              has the loader look for new files in overrides\ and edits\;
//   editor_remove <handle>                     removes it (0x6D56A0, thiscall on the world: the handle, 0).
//   editor_create <handle>                     makes an object the game hasn't, from its block as a
//       zone file holds it (header, then properties), which the editor put in
//       bin\link\create_<HANDLE>.bin: as the game's own layer loader (0x6D5D30) makes a layer's
//       objects, first a stub for it (0x6D56E0, thiscall on the world: where the block's address
//       is), then the object from stub and block (0x6D5770, thiscall on the world: how many, the
//       stubs, the blocks). The block is kept: the game may point into it.
// An object the game hasn't now (not loaded, broken up, or only in the editor yet) is passed over.
using SetPlaceFn = void(__fastcall*)(void* object, void* unused, const float* position, const float* rotation, int now);
auto const RemoveObject = reinterpret_cast<int(__fastcall*)(void* world, void* unused, DWORD handle, int flag)>(0x6D56A0);

auto const MakeStub = reinterpret_cast<BYTE*(__fastcall*)(void* world, void* unused, BYTE** block)>(0x6D56E0);
auto const MakeFromStubs = reinterpret_cast<void(__fastcall*)(void* world, void* unused, int count, BYTE** stubs, BYTE** blocks)>(0x6D5770);
constexpr DWORD kStubNumber = 0x74;  // a stub's place in the lists it is made from (a short)
constexpr size_t kBlockHeader = 0x38, kBlockHandle = 4;

void MakeEditorObject(DWORD handle) {
    void* world = reinterpret_cast<void*>(kObjectList);
    wchar_t name[40];
    swprintf_s(name, L"\\create_%08X.bin", static_cast<unsigned>(handle));
    const std::wstring path = LinkFolder() + name;
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || !file) {
        rfa::Log("Editor link: no block for object 0x%08X (%ls)", static_cast<unsigned>(handle), path.c_str());
        return;
    }
    fseek(file, 0, SEEK_END);
    const long size = ftell(file);
    fseek(file, 0, SEEK_SET);
    BYTE* block = size >= static_cast<long>(kBlockHeader) && size <= 0xFFFF ? static_cast<BYTE*>(_aligned_malloc(size + 16, 16)) : nullptr;
    const bool read = block && fread(block, 1, size, file) == static_cast<size_t>(size);
    fclose(file);
    DeleteFileW(path.c_str());
    if (!read || *reinterpret_cast<DWORD*>(block + kBlockHandle) != handle) {
        rfa::Log("Editor link: the block for object 0x%08X isn't one (%ld bytes)", static_cast<unsigned>(handle), size);
        return;
    }
    if (FindObject(world, nullptr, handle)) {
        rfa::Log("Editor link: the game already has an object 0x%08X; not made again", static_cast<unsigned>(handle));
        return;
    }
    BYTE* blocks[1] = {block};
    BYTE* stubs[1] = {MakeStub(world, nullptr, &blocks[0])};
    if (!stubs[0]) {
        rfa::Log("Editor link: the game made no stub for object 0x%08X (a class it doesn't make from zones?)", static_cast<unsigned>(handle));
        return;
    }
    *reinterpret_cast<WORD*>(stubs[0] + kStubNumber) = 0;
    MakeFromStubs(world, nullptr, 1, stubs, blocks);
    rfa::Log(stubs[0] && FindObject(world, nullptr, handle) ? "Editor link: made object 0x%08X" : "Editor link: the game couldn't make object 0x%08X from its block",
             static_cast<unsigned>(handle));
}

// editor_reload: the level is loaded again (the console's reload_level), and the player then put
// back where they stood (warp x y z), so a change that needs the level loaded again (new models,
// the ground, level geometry) doesn't also send you back to the level's start. The player (0x8B92D0)
// is gone or another while the level loads; once there is one again that has stayed put for a
// second, the level is taken to be up.
auto const ThePlayer = reinterpret_cast<BYTE*(__cdecl*)()>(0x8B92D0);
struct ReloadReturn {
    bool waiting = false;
    float position[3]{};
    BYTE* playerThen = nullptr;
    bool sawLoading = false;
    int checks = 0, steady = 0;
    float last[3]{};
} g_reloadReturn;

void StartEditorReload() {
    BYTE* player = ThePlayer();
    g_reloadReturn = ReloadReturn{};
    if (player) {
        memcpy(g_reloadReturn.position, player + 4, sizeof(g_reloadReturn.position));
        g_reloadReturn.playerThen = player;
        g_reloadReturn.waiting = true;
    }
    rfa::Log("Editor link: loading the level again%s", player ? "; the player goes back to where they stand" : "");
    RunConsoleCommand("reload_level");
}

// Ten times a second while a reload's return is waiting.
void ContinueEditorReload() {
    ReloadReturn& r = g_reloadReturn;
    if (!r.waiting) return;
    if (++r.checks > 1200) {  // two minutes: given up
        r.waiting = false;
        return;
    }
    BYTE* player = ThePlayer();
    if (!player || player != r.playerThen) r.sawLoading = true;
    if (!player || (!r.sawLoading && r.checks < 50)) {  // (no sign of loading after 5 seconds: it was quick)
        r.steady = 0;
        return;
    }
    float now[3];
    memcpy(now, player + 4, sizeof(now));
    r.steady = memcmp(now, r.last, sizeof(now)) == 0 || r.steady == 0 ? r.steady + 1 : 1;
    memcpy(r.last, now, sizeof(now));
    if (r.steady < 10) return;
    r.waiting = false;
    rfa::Log("Editor link: the level is up again; the player goes back to %.1f, %.1f, %.1f", r.position[0], r.position[1], r.position[2]);
    RunConsoleCommand("warp %.3f %.3f %.3f", r.position[0], r.position[1], r.position[2]);
}

// editor_chunk <asset list> <container>: loads a chunk the level hasn't, so objects of it can be made
// at once (Live). The editor put a container holding the chunk's model and textures, and an asset
// list naming just that container, in overrides\live\ (new files, found by the loader once it has
// looked again: editor_rescan comes first). The asset list is read as the chunk system reads
// chunk_containers.asm_pc at the game's start (0x90A3A0, with the chunk system's pools, see 0x6AA9F0),
// which registers the container with the streaming system; then it is found by name (0x90A290) and
// loaded at once, as the game loads its preload chunks (0x908FC0: true once loaded). Loading the
// container's model makes the chunk known to the game (the chunk primitive type does that), so
// objects of it can be made. The result goes to link\chunk_<container>.txt for the editor.
auto const ReadAssetList = reinterpret_cast<bool(__cdecl*)(const char* name, void* tempPool, void* pool, int, int, int)>(0x90A3A0);
auto const FindContainer = reinterpret_cast<void*(__cdecl*)(const char* name)>(0x90A290);
auto const LoadContainerNow = reinterpret_cast<bool(__cdecl*)(void* container)>(0x908FC0);
constexpr DWORD kChunkTempPool = 0x26AF908, kChunkPool = 0x26AEE78;

// Each step on its own, guarded: a fault in the game's code is logged with the step, not a crash
// (unless another mod's exception handler gets to it first).
int GuardedReadAssetList(const char* name) {
    __try {
        return ReadAssetList(name, reinterpret_cast<void*>(kChunkTempPool), reinterpret_cast<void*>(kChunkPool), 0, 0, 0) ? 1 : 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -static_cast<int>(GetExceptionCode() & 0x7FFFFFFF);
    }
}
void* GuardedFindContainer(const char* name, bool* faulted) {
    __try {
        return FindContainer(name);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true;
        return nullptr;
    }
}
int GuardedLoadContainer(void* container) {
    __try {
        return LoadContainerNow(container) ? 1 : 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -static_cast<int>(GetExceptionCode() & 0x7FFFFFFF);
    }
}

void LoadEditorChunk(const std::string& assetList, const std::string& container) {
    bool faulted = false;
    rfa::Log("Editor link: chunk container %s: looking it up", container.c_str());
    void* found = GuardedFindContainer(container.c_str(), &faulted);
    if (!found && !faulted) {
        rfa::Log("Editor link: chunk container %s: reading the asset list %s", container.c_str(), assetList.c_str());
        const int read = GuardedReadAssetList(assetList.c_str());
        if (read < 0) rfa::Log("Editor link: reading the asset list FAULTED (exception %08X)", static_cast<unsigned>(-read) | 0x80000000u);
        found = read < 0 ? nullptr : GuardedFindContainer(container.c_str(), &faulted);
        if (!found) rfa::Log("Editor link: the asset list %s was %s, but has no container %s", assetList.c_str(), read > 0 ? "read" : "not read", container.c_str());
    }
    int load = 0;
    if (found) {
        rfa::Log("Editor link: chunk container %s: loading it (container record %p)", container.c_str(), found);
        load = GuardedLoadContainer(found);
        if (load < 0) rfa::Log("Editor link: loading the container FAULTED (exception %08X)", static_cast<unsigned>(-load) | 0x80000000u);
    }
    const bool loaded = load > 0;
    rfa::Log("Editor link: chunk container %s %s", container.c_str(), loaded ? "loaded" : "NOT loaded");
    const std::wstring answer = LinkFolder() + L"\\chunk_" + std::wstring(container.begin(), container.end()) + L".txt";
    FILE* file = nullptr;
    if (_wfopen_s(&file, answer.c_str(), L"w") == 0 && file) {
        fputs(loaded ? "loaded" : "failed", file);
        fclose(file);
    }
}

// Level geometry (stitch pieces), live. A zone's stitch list stays in memory as it was read (the
// loader, 0x6B8710, only turns its offsets into addresses); its terrain record (0x6BBA90, by the
// zone's .cterrain_pc name) has the terrain data at +0 (the zone object it was made for at +0xA8)
// and the list's header at +4: its first 9 words are each list's pieces, the next 9 their counts,
// and +0x94 the combined collision's parts (0 when each piece has its own body). A piece is 100 bytes:
// name +0, position +4, rotation +0x10 (3 rows), flags +0x34, its collision body +0x38 (when it has
// its own), its draw instances +0x3C (one per sub-chunk, +0x40 of them).
// When the level is made (0x6BBAC0) each piece gets its draw instances (0x76F280: an
// rl_stitch_mesh_instance per sub-chunk, placed with the instance's set position 0x602710 and set
// rotation 0x602740 and put in the scene, 0x768460) and, unless the level has one combined
// collision for all its pieces, a collision body of its own (0x6BC270, from the piece's chunk
// 0x6AC740 and the zone; the body told its piece, 0x776240). The same calls move, take away and
// make pieces now:
//   moved: its draw instances placed again, and its body (havok set position and rotation, 0x7765C0;
//     a body handle is the body). With a combined collision the piece has no body: one is made for
//     it where it now is (the combined one still has it where it was, until the level loads again);
//   deleted: its draw instances let go (0x76F8B0), its body put far below the world;
//   put back (undo): made again as the level made it;
//   new (a piece added in the editor): a piece of our own, outside the level's lists, made the
//     same way. Its chunk has to be loaded (the editor loads it first if the level hasn't).
auto const TerrainRecord = reinterpret_cast<BYTE*(__cdecl*)(const char* cterrainName)>(0x6BBA90);
auto const ChunkByName = reinterpret_cast<BYTE*(__cdecl*)(const char* name)>(0x6AC740);
auto const MakeStitchDraw = reinterpret_cast<void(__cdecl*)(BYTE* piece)>(0x76F280);
auto const FreeStitchDraw = reinterpret_cast<void(__cdecl*)(BYTE* piece)>(0x76F8B0);
auto const MakeStitchBody = reinterpret_cast<DWORD(__cdecl*)(BYTE* chunk, BYTE* zone, BYTE* piece)>(0x6BC270);
auto const BodyOf = reinterpret_cast<BYTE*(__cdecl*)(DWORD handle)>(0x778440);
auto const SetBodyUserData = reinterpret_cast<void(__cdecl*)(BYTE* body, int what, void* data)>(0x776240);
auto const SetBodyPlace = reinterpret_cast<void(__cdecl*)(BYTE* body, const float* position, const float* rotation)>(0x7765C0);
auto const SetDrawPosition = reinterpret_cast<void(__thiscall*)(void* instance, const float* position)>(0x602710);
auto const SetDrawRotation = reinterpret_cast<void(__thiscall*)(void* instance, const float* rotation)>(0x602740);
auto const UpdateInScene = reinterpret_cast<void(__cdecl*)(void* instance)>(0x768460);
auto const FreeBody = reinterpret_cast<void(__cdecl*)(DWORD handle, bool, bool, bool)>(0x7755C0);  // as the level frees its pieces' (handle, 0, 1, 1)
constexpr int kPieceSize = 100, kPiecePlace = 4, kPieceFlags = 0x34, kPieceBody = 0x38, kPieceDraws = 0x3C, kPieceDrawCount = 0x40;
constexpr int kChunkSubChunks = 0x158;
constexpr DWORD kNoBody = 0xFFFFFFFF;

// What we did to a piece beyond its own data: a body made for it (the level's collision being
// combined, or a new piece), and whether it is taken away now.
struct LivePiece {
    BYTE* record = nullptr;  // the terrain record it is of
    DWORD body = kNoBody;
    bool hidden = false;
};
std::vector<std::pair<BYTE*, LivePiece>> g_livePieces;  // by piece
struct SpawnedPiece {
    DWORD id = 0;
    BYTE* piece = nullptr;
    BYTE* record = nullptr;
    std::string name;
};
std::vector<SpawnedPiece> g_spawnedPieces;

LivePiece& LivePieceOf(BYTE* piece, BYTE* record = nullptr) {
    for (auto& [at, live] : g_livePieces)
        if (at == piece) return live;
    g_livePieces.push_back({piece, LivePiece{record}});
    return g_livePieces.back().second;
}

bool HasDraws(BYTE* piece) {
    void** draws = *reinterpret_cast<void***>(piece + kPieceDraws);
    const int count = *reinterpret_cast<int*>(piece + kPieceDrawCount);
    for (int i = 0; draws && i < count; i++)
        if (draws[i]) return true;
    return false;
}

// The piece's draw instances where its data puts it now.
void PlaceDraws(BYTE* piece) {
    void** draws = *reinterpret_cast<void***>(piece + kPieceDraws);
    const int count = *reinterpret_cast<int*>(piece + kPieceDrawCount);
    const float* place = reinterpret_cast<float*>(piece + kPiecePlace);
    for (int i = 0; draws && i < count; i++) {
        if (!draws[i]) continue;
        SetDrawPosition(draws[i], place);
        SetDrawRotation(draws[i], place + 3);
        UpdateInScene(draws[i]);
    }
}

// The body the piece collides with as we can move it: its own, else one we made for it (made now
// if `make`; the chunk's collision model is needed).
DWORD BodyFor(BYTE* piece, BYTE* zone, bool ownBody, bool make) {
    if (ownBody) {
        const DWORD own = *reinterpret_cast<DWORD*>(piece + kPieceBody);
        if (own != kNoBody && own != 0) return own;
    }
    LivePiece& live = LivePieceOf(piece);
    if (live.body != kNoBody || !make || !zone) return live.body;
    BYTE* chunk = ChunkByName(*reinterpret_cast<const char**>(piece));
    if (!chunk) return kNoBody;
    live.body = MakeStitchBody(chunk, zone, piece);
    if (BYTE* body = live.body != kNoBody ? BodyOf(live.body) : nullptr) SetBodyUserData(body, 0xE, piece);
    return live.body;
}

void PlaceBody(DWORD handle, const float* place) {
    if (BYTE* body = handle != kNoBody ? BodyOf(handle) : nullptr) SetBodyPlace(body, place, place + 3);
}

// Puts a piece where `numbers` (position, 3 rotation rows) say, its drawing and its collision; makes
// its drawing again if it was taken away. 1 if done, else a reason (see the callers).
int GuardedPlacePiece(BYTE* piece, BYTE* record, BYTE* zone, bool ownBody, const float* numbers) {
    __try {
        memcpy(piece + kPiecePlace, numbers, 12 * sizeof(float));
        LivePiece& live = LivePieceOf(piece, record);
        if (!HasDraws(piece)) {
            if (!ChunkByName(*reinterpret_cast<const char**>(piece))) return -6;
            MakeStitchDraw(piece);  // made where the piece is
        } else {
            PlaceDraws(piece);
        }
        live.hidden = false;
        PlaceBody(BodyFor(piece, zone, ownBody, true), numbers);
        return HasDraws(piece) ? 1 : -7;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -5;
    }
}

int GuardedHidePiece(BYTE* piece, bool ownBody) {
    __try {
        FreeStitchDraw(piece);
        LivePieceOf(piece).hidden = true;
        // Its collision: far below the world (a body can't be taken out of the world safely here).
        float away[12];
        memcpy(away, piece + kPiecePlace, sizeof(away));
        away[1] = -20000.0f;
        PlaceBody(BodyFor(piece, nullptr, ownBody, false), away);
        return 1;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -5;
    }
}

// The level's own piece (list, index) of the zone whose .cterrain_pc is named, checked by its name;
// with its list's header and the zone object. A reason (below 0) if there's none.
int GuardedFindPiece(const char* cterrain, int list, int index, const char* name, BYTE** piece, BYTE** record, BYTE** header, BYTE** zone) {
    __try {
        *record = TerrainRecord(cterrain);
        if (!*record) return -1;
        *header = *reinterpret_cast<BYTE**>(*record + 4);
        BYTE* data = *reinterpret_cast<BYTE**>(*record);
        *zone = data ? *reinterpret_cast<BYTE**>(data + 0xA8) : nullptr;
        if (!*header || list < 0 || list >= 9) return -2;
        BYTE* pieces = *reinterpret_cast<BYTE**>(*header + list * 4);
        const int count = *reinterpret_cast<int*>(*header + 0x24 + list * 4);
        if (!pieces || index < 0 || index >= count) return -3;
        *piece = pieces + index * kPieceSize;
        const char* pieceName = *reinterpret_cast<const char**>(*piece);
        if (!pieceName || _stricmp(pieceName, name) != 0) return -4;
        return 1;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -5;
    }
}

const char* const kPieceWhy[] = {"", "the game has no terrain record of that name", "no such list", "no such piece",
                                 "the piece there has another name (the stitch list changed since the level loaded)",
                                 "the game's code faulted", "its chunk isn't loaded in the game", "the game made no drawing for it"};

bool ReadNumbers(const char* rest, float* numbers, int count) {
    for (int i = 0; i < count; i++) {
        char* next = nullptr;
        numbers[i] = strtof(rest, &next);
        if (next == rest) return false;
        rest = next;
    }
    return true;
}

bool CombinedCollision(BYTE* header) {
    __try {
        return *reinterpret_cast<void**>(header + 0x94) != nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return true;
    }
}

// editor_stitch <zone>.cterrain_pc <list> <index> <piece name> x y z <9 rotation numbers>: the level's
// own piece moved (or put back). editor_stitch_hide <zone>.cterrain_pc <list> <index> <piece name>: taken away.
void EditorStitchPiece(const std::string& line, bool hide) {
    const size_t skip = hide ? 19 : 14;
    char cterrain[96] = {}, name[96] = {};
    int list = -1, index = -1, used = 0;
    if (sscanf_s(line.c_str() + skip, "%95s %d %d %95s%n", cterrain, static_cast<unsigned>(sizeof(cterrain)), &list, &index, name,
                 static_cast<unsigned>(sizeof(name)), &used) != 4) {
        rfa::Log("Editor link: %s needs a terrain name, list, index and piece name%s", hide ? "editor_stitch_hide" : "editor_stitch",
                 hide ? "" : ", and 12 numbers");
        return;
    }
    float numbers[12];
    if (!hide && !ReadNumbers(line.c_str() + skip + used, numbers, 12)) return;
    BYTE *piece = nullptr, *record = nullptr, *header = nullptr, *zone = nullptr;
    int result = GuardedFindPiece(cterrain, list, index, name, &piece, &record, &header, &zone);
    if (result > 0) {
        const bool ownBody = !CombinedCollision(header);
        result = hide ? GuardedHidePiece(piece, ownBody) : GuardedPlacePiece(piece, record, zone, ownBody, numbers);
    }
    static int done = 0, problems = 0;
    if (result > 0) {
        if (++done <= 5) rfa::Log("Editor link: level geometry piece %s (%s list %d #%d) %s", name, cterrain, list, index, hide ? "taken away" : "moved");
    } else if (++problems <= 10) {
        rfa::Log("Editor link: level geometry piece %s (%s list %d #%d) not %s: %s", name, cterrain, list, index, hide ? "taken away" : "moved",
                 kPieceWhy[-result]);
    }
}

void AnswerEditor(const std::string& name, const char* answer) {
    const std::wstring path = LinkFolder() + L"\\" + std::wstring(name.begin(), name.end()) + L".txt";
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"w") == 0 && file) {
        fputs(answer, file);
        fclose(file);
    }
}

// A new piece of our own: its data (as a stitch list's), an array for its draw instances, its name
// kept. Never freed (its body knows it).
BYTE* NewPiece(const char* name, int flags) {
    BYTE* piece = static_cast<BYTE*>(calloc(1, kPieceSize));
    char* kept = _strdup(name);
    void** draws = static_cast<void**>(calloc(8, sizeof(void*)));
    if (!piece || !kept || !draws) return nullptr;
    *reinterpret_cast<char**>(piece) = kept;
    piece[kPieceFlags] = static_cast<BYTE>(flags);
    *reinterpret_cast<DWORD*>(piece + kPieceBody) = kNoBody;
    *reinterpret_cast<void***>(piece + kPieceDraws) = draws;
    return piece;
}

int GuardedSubChunks(const char* name) {
    __try {
        BYTE* chunk = ChunkByName(name);
        return chunk ? *reinterpret_cast<int*>(chunk + kChunkSubChunks) : -1;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -1;
    }
}

int GuardedZoneOf(const char* cterrain, BYTE** record, BYTE** zone) {
    __try {
        *record = TerrainRecord(cterrain);
        if (!*record) return -1;
        BYTE* data = *reinterpret_cast<BYTE**>(*record);
        *zone = data ? *reinterpret_cast<BYTE**>(data + 0xA8) : nullptr;
        return *zone ? 1 : -1;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -5;
    }
}

// editor_stitch_add <zone>.cterrain_pc <id> <piece name> <flags> x y z <9 rotation numbers>: a piece
// added in the editor made (or, made already, moved). The answer (link\stitch_<id>.txt: made, moved,
// nochunk or failed) tells the editor whether to fall back on a reload.
void EditorAddStitchPiece(const std::string& line) {
    char cterrain[96] = {}, name[96] = {};
    unsigned id = 0;
    int flags = 4, used = 0;
    if (sscanf_s(line.c_str() + 18, "%95s %x %95s %d%n", cterrain, static_cast<unsigned>(sizeof(cterrain)), &id, name,
                 static_cast<unsigned>(sizeof(name)), &flags, &used) != 4) {
        rfa::Log("Editor link: editor_stitch_add needs a terrain name, id, piece name, flags and 12 numbers");
        return;
    }
    float numbers[12];
    if (!ReadNumbers(line.c_str() + 18 + used, numbers, 12)) return;
    char answerName[32];
    sprintf_s(answerName, "stitch_%08X", id);
    BYTE *record = nullptr, *zone = nullptr;
    if (GuardedZoneOf(cterrain, &record, &zone) <= 0) {
        rfa::Log("Editor link: new level geometry piece %s: the game has no zone for %s", name, cterrain);
        AnswerEditor(answerName, "failed");
        return;
    }
    SpawnedPiece* spawned = nullptr;
    for (SpawnedPiece& s : g_spawnedPieces)
        if (s.id == id && s.record == record && _stricmp(s.name.c_str(), name) == 0) spawned = &s;
    const bool made = spawned != nullptr;
    if (!spawned) {
        const int subChunks = GuardedSubChunks(name);
        if (subChunks < 0) {
            rfa::Log("Editor link: new level geometry piece %s: its chunk isn't loaded in the game", name);
            AnswerEditor(answerName, "nochunk");
            return;
        }
        if (subChunks > 1) {  // (the game doesn't make those as stitch pieces either)
            rfa::Log("Editor link: new level geometry piece %s: its chunk has %d sub-chunks; stitch pieces have one", name, subChunks);
            AnswerEditor(answerName, "failed");
            return;
        }
        BYTE* piece = NewPiece(name, flags);
        if (!piece) {
            AnswerEditor(answerName, "failed");
            return;
        }
        *reinterpret_cast<int*>(piece + kPieceDrawCount) = subChunks;
        g_spawnedPieces.push_back({id, piece, record, name});
        spawned = &g_spawnedPieces.back();
    }
    // Pieces of our own always have a body of their own (made for them, as for a combined collision).
    const int result = GuardedPlacePiece(spawned->piece, record, zone, false, numbers);
    static int count = 0;
    if (result > 0 && (!made && ++count <= 10)) rfa::Log("Editor link: made level geometry piece %s (0x%08X)", name, id);
    if (result <= 0) rfa::Log("Editor link: new level geometry piece %s not %s: %s", name, made ? "moved" : "made", kPieceWhy[-result]);
    AnswerEditor(answerName, result > 0 ? (made ? "moved" : "made") : result == -6 ? "nochunk" : "failed");
}

// editor_stitch_drop <id>: a piece added in the editor (made by editor_stitch_add) deleted there.
void EditorDropStitchPiece(const std::string& line) {
    const DWORD id = static_cast<DWORD>(strtoul(line.c_str() + 19, nullptr, 16));
    for (SpawnedPiece& s : g_spawnedPieces) {
        if (s.id != id || LivePieceOf(s.piece).hidden) continue;
        if (GuardedHidePiece(s.piece, false) > 0) rfa::Log("Editor link: level geometry piece %s (0x%08X) taken away", s.name.c_str(), static_cast<unsigned>(id));
    }
}

// The game takes a zone's terrain and level geometry away (0x6BC900, the terrain record: when the level
// unloads or loads again): first the pieces we made and the bodies we made for its pieces go, while
// the chunks they are made of are still there.
constexpr DWORD kTerrainTakeDown = 0x6BC900;
constexpr size_t kTerrainTakeDownPrologue = 6;  // push ebp / mov ebp,esp / sub esp,34h
using TerrainTakeDownFn = void(__cdecl*)(BYTE* record);
TerrainTakeDownFn g_terrainTakeDown;

void GuardedFreePiece(BYTE* piece, DWORD body, bool draws) {
    __try {
        if (draws) FreeStitchDraw(piece);
        if (body != kNoBody) FreeBody(body, false, true, true);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        rfa::Log("Editor link: letting go of a level geometry piece made live faulted");
    }
}

void __cdecl TerrainTakeDown(BYTE* record) {
    int freed = 0;
    for (auto it = g_spawnedPieces.begin(); it != g_spawnedPieces.end();) {
        if (it->record != record) {
            ++it;
            continue;
        }
        DWORD body = kNoBody;
        for (auto& [at, live] : g_livePieces)
            if (at == it->piece) std::swap(body, live.body);  // (not freed again below)
        GuardedFreePiece(it->piece, body, true);
        freed++;
        it = g_spawnedPieces.erase(it);
    }
    for (auto it = g_livePieces.begin(); it != g_livePieces.end();) {
        if (it->second.record != record) {
            ++it;
            continue;
        }
        if (it->second.body != kNoBody) GuardedFreePiece(it->first, it->second.body, false);
        it = g_livePieces.erase(it);
    }
    if (freed) rfa::Log("Editor link: %d level geometry pieces made live let go with their zone", freed);
    g_terrainTakeDown(record);
}

bool RunEditorEdit(const std::string& line) {
    if (line.rfind("editor_stitch ", 0) == 0 || line.rfind("editor_stitch_hide ", 0) == 0) {
        EditorStitchPiece(line, line.rfind("editor_stitch_hide ", 0) == 0);
        return true;
    }
    if (line.rfind("editor_stitch_add ", 0) == 0) {
        EditorAddStitchPiece(line);
        return true;
    }
    if (line.rfind("editor_stitch_drop ", 0) == 0) {
        EditorDropStitchPiece(line);
        return true;
    }
    if (line.rfind("editor_chunk ", 0) == 0) {
        char assetList[128] = {}, container[128] = {};
        if (sscanf_s(line.c_str() + 13, "%127s %127s", assetList, static_cast<unsigned>(sizeof(assetList)), container,
                     static_cast<unsigned>(sizeof(container))) == 2)
            LoadEditorChunk(assetList, container);
        return true;
    }
    if (line == "editor_reload") {
        StartEditorReload();
        return true;
    }
    if (line == "editor_rescan") {
        // The loader looks through overrides\ and edits\ again, for files the editor saved for the
        // first time since the game started (before it has the game reload a level or a table).
        using RescanFn = int(__cdecl*)();
        if (auto rescan = reinterpret_cast<RescanFn>(GetProcAddress(GetModuleHandleW(L"rfa_loader.dll"), "RFALoader_RescanOverrides")))
            rescan();
        else rfa::Log("Editor link: this loader can't look through the overrides again (an older rfa_loader.dll)");
        return true;
    }
    if (line.rfind("editor_create ", 0) == 0) {
        MakeEditorObject(static_cast<DWORD>(strtoul(line.c_str() + 14, nullptr, 0)));
        return true;
    }
    const bool move = line.rfind("editor_move ", 0) == 0, remove = line.rfind("editor_remove ", 0) == 0;
    if (!move && !remove) return false;
    static int moved = 0, removed = 0, missing = 0;
    const char* text = line.c_str() + (move ? 12 : 14);
    char* rest = nullptr;
    const DWORD handle = static_cast<DWORD>(strtoul(text, &rest, 0));
    void* object = FindObject(reinterpret_cast<void*>(kObjectList), nullptr, handle);
    if (!object) {
        if (++missing <= 10) rfa::Log("Editor link: the game has no object 0x%08X now (%s skipped)", static_cast<unsigned>(handle), move ? "move" : "removal");
        return true;
    }
    if (remove) {
        RemoveObject(reinterpret_cast<void*>(kObjectList), nullptr, handle, 0);
        if (++removed <= 10) rfa::Log("Editor link: removed object 0x%08X", static_cast<unsigned>(handle));
        return true;
    }
    float numbers[12];
    for (float& number : numbers) {
        char* next = nullptr;
        number = strtof(rest, &next);
        if (next == rest) {
            rfa::Log("Editor link: editor_move needs a handle and 12 numbers");
            return true;
        }
        rest = next;
    }
    const auto setPlace = reinterpret_cast<SetPlaceFn>((*static_cast<void***>(object))[10]);
    setPlace(object, nullptr, numbers, numbers + 3, 1);
    if (++moved <= 5) rfa::Log("Editor link: moved object 0x%08X to %.2f, %.2f, %.2f", static_cast<unsigned>(handle), numbers[0], numbers[1], numbers[2]);
    return true;
}

void AnswerWhereWanted() {
    const std::wstring wanted = LinkFolder() + L"\\where_wanted.txt";
    FILE* file = nullptr;
    if (_wfopen_s(&file, wanted.c_str(), L"r") != 0 || !file) return;
    std::vector<DWORD> handles;
    char buffer[64];
    while (fgets(buffer, sizeof(buffer), file)) {
        const std::string line = Trim(buffer);
        if (!line.empty()) handles.push_back(static_cast<DWORD>(strtoul(line.c_str(), nullptr, 0)));
    }
    fclose(file);
    DeleteFileW(wanted.c_str());

    // Written under another name, then renamed, so the editor never reads half of it.
    const std::wstring answer = LinkFolder() + L"\\where_now.txt", partial = answer + L".tmp";
    if (_wfopen_s(&file, partial.c_str(), L"w") != 0 || !file) return;
    int found = 0;
    for (DWORD handle : handles) {
        auto* object = static_cast<const GameObject*>(FindObject(reinterpret_cast<void*>(kObjectList), nullptr, handle));
        if (!object) {
            fprintf(file, "0x%08X gone\n", static_cast<unsigned>(handle));
            continue;
        }
        ++found;
        fprintf(file, "0x%08X", static_cast<unsigned>(handle));
        for (float value : object->position) fprintf(file, " %.4f", value);
        for (float value : object->rotation) fprintf(file, " %.5f", value);
        fprintf(file, "\n");
    }
    fclose(file);
    MoveFileExW(partial.c_str(), answer.c_str(), MOVEFILE_REPLACE_EXISTING);
    static bool told = false;
    if (!told) {
        told = true;
        rfa::Log("Editor link: the editor asked where %u objects are (%d found); it asks again while it shows props as in the game",
                 static_cast<unsigned>(handles.size()), found);
    }
}

// The game's light, for calibrating the editor's "game light". The editor writes
// bin\link\lighting_wanted.txt; the game answers in bin\link\lighting_now.txt, a line each:
//   time <t>                     the time of day the game lights by (0x10F8898; the .dtodx curves' time)
//   tod_enabled <0/1>            whether the time of day is in use (0x12FD6D4)
//   tod_weights <a> <b> <c> <d>  how much each of the four loaded time-of-day sets counts
//   exposure <min> <max>         the tone-mapping stage's exposure this frame (rl_hdr's Exposure_min, _max)
//   desired_brightness <x>       and the brightness it adapts towards (Desired_brightness)
//   world_ambient, world_back_ambient <r g b>   the world's ambient from above and below
//                                (0x1870630, 0x187063C), which the time of day sets (0x768520)
//   render_ambient, render_back_ambient <r g b a>   what the renderer's lighting manager then
//                                holds (its +0x12C and +0x13C, set by 0x769BD0 and 0x769C00)
//   render_fog <11 numbers>      the block the lighting manager is handed each frame (0x1973924, its +0x100)
//   then each time-of-day curve below, as the game works it out now: every set's curve at the
//   time of day, by the game's own functions, times the set's weight, added up. Colours are
//   red, green, blue and a brightness, as in the files.
// The time-of-day sets are the level's .dtodx files (world_mission_01.dtodx, ...): curves over
// the day for the ambient, fog, sky and tone mapping, read by 0x558EE0 into a set (curve offsets below).

auto& g_timeOfDay = *reinterpret_cast<float*>(0x10F8898);
auto& g_worldAmbient = *reinterpret_cast<float(*)[6]>(0x1870630);  // from above (3 numbers), then from below (3)
auto& g_lighting = *reinterpret_cast<const BYTE**>(0x19737E0);     // the renderer's lighting manager (fed each frame by 0x768DF0)
auto& g_renderFog = *reinterpret_cast<float(*)[11]>(0x1973924);     // the block it is handed each frame (its +0x100)
auto& g_todEnabled = *reinterpret_cast<BYTE*>(0x12FD6D4);
auto& g_todSets = *reinterpret_cast<BYTE* (*)[4]>(0x12FD6B4);   // the sets d, c, b, a (0x12FD6C0 is the first)
auto& g_todWeights = *reinterpret_cast<float(*)[4]>(0x12FD6C4);  // their weights, in the same order

// The game's curve functions, methods of a curve (in ECX), called as __fastcall (EDX unused):
// a colour curve's value at a time into `out` (0x531B90, as tod_ambient's handler 0x52BE90 uses it),
// and a number curve's (0x531CB0, as tod_fog_density's 0x52C9F0 does).
using ColourAtFn = float*(__fastcall*)(void* curve, void* unused, float* out, float time);
using NumberAtFn = float(__fastcall*)(void* curve, void* unused, float time);
auto const ColourAt = reinterpret_cast<ColourAtFn>(0x531B90);
auto const NumberAt = reinterpret_cast<NumberAtFn>(0x531CB0);

struct TodCurve {
    const char* name;
    size_t offset;  // in a set
    bool colour;
};
constexpr TodCurve kTodCurves[] = {
    {"ambient_color", 0x2080, true},     {"back_ambient_color", 0x23C0, true}, {"tod_light_color", 0x1D40, true},
    {"fog_color", 0x2700, true},         {"particle_ambient_color", 0x2A40, true},
    {"fog_density_new", 0x32A8, false},  {"fog_density_offset", 0x339C, false}, {"fog_atmosphere_scale", 0x31B4, false},
    {"fog_ground", 0x30C0, false},       {"fog_camera_min", 0x6E04, false},    {"fog_camera_max", 0x6EF8, false},
    {"fog_skybox_scale", 0x70E0, false}, {"fog_skybox_offset", 0x6FEC, false},
};

// The renderer's tone-mapping step (rl_hdr), a method called once a frame: the renderer in ECX.
// It hands the shader the renderer's exposure: +0xAD8 Desired_brightness, +0xAE0 Exposure_min,
// +0xAE4 Exposure_max (looked up by name at 0xD22720). Hooked only to learn where the renderer is.
constexpr DWORD kToneMapStep = 0xD22C60;
constexpr size_t kToneMapStepPrologue = 6;  // push ebp / mov ebp,esp / sub esp,50h
using ToneMapStepFn = void(__fastcall*)(void* renderer, void* unused);
ToneMapStepFn g_toneMapStep;
const BYTE* g_renderer = nullptr;

void __fastcall ToneMapStep(void* renderer, void* unused) {
    g_renderer = static_cast<const BYTE*>(renderer);
    g_toneMapStep(renderer, unused);
}

float RendererFloat(size_t offset) { return g_renderer ? *reinterpret_cast<const float*>(g_renderer + offset) : -1.0f; }

void AnswerLightingWanted() {
    const std::wstring wanted = LinkFolder() + L"\\lighting_wanted.txt";
    if (GetFileAttributesW(wanted.c_str()) == INVALID_FILE_ATTRIBUTES) return;
    DeleteFileW(wanted.c_str());
    const std::wstring answer = LinkFolder() + L"\\lighting_now.txt", partial = answer + L".tmp";
    FILE* file = nullptr;
    if (_wfopen_s(&file, partial.c_str(), L"w") != 0 || !file) return;
    fprintf(file, "time %.4f\n", g_timeOfDay);
    fprintf(file, "tod_enabled %d\n", g_todEnabled ? 1 : 0);
    fprintf(file, "tod_weights %.4f %.4f %.4f %.4f\n", g_todWeights[3], g_todWeights[2], g_todWeights[1], g_todWeights[0]);
    fprintf(file, "exposure %.4f %.4f\n", RendererFloat(0xAE0), RendererFloat(0xAE4));
    fprintf(file, "desired_brightness %.4f\n", RendererFloat(0xAD8));
    fprintf(file, "world_ambient %.5f %.5f %.5f\n", g_worldAmbient[0], g_worldAmbient[1], g_worldAmbient[2]);
    fprintf(file, "world_back_ambient %.5f %.5f %.5f\n", g_worldAmbient[3], g_worldAmbient[4], g_worldAmbient[5]);
    if (g_lighting) {
        const float* ambient = reinterpret_cast<const float*>(g_lighting + 0x12C);
        const float* back = reinterpret_cast<const float*>(g_lighting + 0x13C);
        fprintf(file, "render_ambient %.5f %.5f %.5f %.5f\n", ambient[0], ambient[1], ambient[2], ambient[3]);
        fprintf(file, "render_back_ambient %.5f %.5f %.5f %.5f\n", back[0], back[1], back[2], back[3]);
    }
    fprintf(file, "render_fog");
    for (float value : g_renderFog) fprintf(file, " %.5f", value);
    fprintf(file, "\n");
    for (const TodCurve& curve : kTodCurves) {
        float total[4] = {0, 0, 0, 0};
        for (int set = 0; set < 4; ++set) {
            if (!g_todSets[set] || g_todWeights[set] == 0) continue;
            void* inSet = g_todSets[set] + curve.offset;
            if (curve.colour) {
                float value[4] = {0, 0, 0, 0};
                ColourAt(inSet, nullptr, value, g_timeOfDay);
                for (int i = 0; i < 4; ++i) total[i] += value[i] * g_todWeights[set];
            } else {
                total[0] += NumberAt(inSet, nullptr, g_timeOfDay) * g_todWeights[set];
            }
        }
        if (curve.colour) fprintf(file, "%s %.5f %.5f %.5f %.5f\n", curve.name, total[0], total[1], total[2], total[3]);
        else fprintf(file, "%s %.6f\n", curve.name, total[0]);
    }
    fclose(file);
    MoveFileExW(partial.c_str(), answer.c_str(), MOVEFILE_REPLACE_EXISTING);
    rfa::Log("Editor link: told the editor the game's light (time %.2f, exposure %.2f to %.2f)", g_timeOfDay,
             RendererFloat(0xAE0), RendererFloat(0xAE4));
}

using FrameUpdateFn = void(__cdecl*)();
FrameUpdateFn g_frameUpdate;

void __cdecl FrameUpdate() {
    g_frameUpdate();
    ShowEditor();
    if (g_drawsConsole && g_settings.gameLog) ReadReports();
    // The editor's commands are looked for every frame (Live sends them as you drag), the rest ten times a second.
    static int linkFolderChecks = 0;
    static bool linkFolderThere = false;
    if (linkFolderChecks-- <= 0) {
        linkFolderThere = GetFileAttributesW(LinkFolder().c_str()) != INVALID_FILE_ATTRIBUTES;
        linkFolderChecks = 60;
    }
    if (linkFolderThere) RunEditorCommands();
    const ULONGLONG now = GetTickCount64();
    if (now - g_lastCheck < kCheckEveryMs) return;
    g_lastCheck = now;
    if (g_drawsConsole && g_settings.modLog) ReadModLog();
    if (!linkFolderThere) return;  // the editor's link isn't set up
    ContinueEditorReload();
    AnswerWhereWanted();
    AnswerLightingWanted();
}

}  // namespace

void console::Init() {
    if (!rfa::game::IsSupportedBuild()) {
        rfa::Log("Not the publicbeta build (it has no developer console); nothing to do");
        return;
    }
    ReadSettings();
    MakeBuildStamp();
    rfa::Log("Build stamp: %s", g_buildStamp.c_str());
    g_consolePrint =reinterpret_cast<ConsolePrintFn>(
        rfa::HookFunction(kConsolePrint, kConsolePrintPrologue, reinterpret_cast<void*>(&ConsolePrint)));
    g_consoleEcho = reinterpret_cast<ConsoleEchoFn>(
        rfa::HookFunction(kConsoleEcho, kConsoleEchoPrologue, reinterpret_cast<void*>(&ConsoleEcho)));
    g_drawsConsole = g_consolePrint && g_consoleEcho && rfa::ReplaceFunction(kConsoleDraw, reinterpret_cast<void*>(&ConsoleDraw));
    if (g_drawsConsole) {
        rfa::Log("The mod draws the console: %d%% of the screen, text x%.2f, typed line x%.2f, %u lines kept%s",
                 static_cast<int>(g_settings.height * 100 + 0.5f), g_settings.textScale, g_settings.inputScale,
                 static_cast<unsigned>(g_settings.historyLines), g_settings.logToFile ? ", also in logs\\console.log" : "");
        g_panelDraw = reinterpret_cast<PanelDrawFn>(
            rfa::HookFunction(kPanelDraw, kPanelDrawPrologue, reinterpret_cast<void*>(&PanelDraw)));
        if (!g_panelDraw) rfa::Log("Could not hook the render debug panel's drawing; the console stays on top of it");
    } else {
        rfa::Log("Could not hook the console's output or drawing; the game draws it as before");
    }

    g_consoleKeys = reinterpret_cast<VoidFn>(
        rfa::HookFunction(kConsoleKeys, kConsoleKeysPrologue, reinterpret_cast<void*>(&ConsoleKeys)));
    g_nextKey = reinterpret_cast<NextKeyFn>(
        rfa::HookFunction(kNextKey, kNextKeyPrologue, reinterpret_cast<void*>(&NextKey)));
    g_mouseMessage = reinterpret_cast<MouseMessageFn>(
        rfa::HookFunction(kMouseMessage, kMouseMessagePrologue, reinterpret_cast<void*>(&MouseMessage)));
    rfa::Log(g_consoleKeys && g_nextKey && g_mouseMessage
                 ? "The mouse wheel, Page Up / Page Down, Ctrl+Home and End scroll the console, and Ctrl+V pastes (open it with the ` key)"
                 : "Could not hook the game's keys or mouse handler; console scrolling and pasting may be off");
    g_runConsoleCommand = reinterpret_cast<RunConsoleCommandFn>(
        rfa::HookFunction(kRunConsoleCommand, kRunConsoleCommandPrologue, reinterpret_cast<void*>(&RunCommandAsText)));
    if (!g_runConsoleCommand) rfa::Log("Could not hook the console's command runner; a %% in a typed line is still read as a format");
    if (g_drawsConsole) {
        if (rfa::ReplaceFunction(kCompleteCommand, reinterpret_cast<void*>(&CompleteCommand)))
            rfa::Log("Tab completes the first word of a console line and lists what each match does");
        else
            rfa::Log("Could not replace the console's Tab completion; it is the game's own");
    }

    if (g_drawsConsole && g_settings.gameLog) {
        rfa::Log(rfa::HookImport(GetModuleHandleW(nullptr), "KERNEL32.dll", "OutputDebugStringA", &GameDebugString,
                                 &g_outputDebugString)
                     ? "The console also shows the game's debug output and error reports, marked [game]"
                     : "Could not take the game's debug output; the console shows its error reports only");
    }
    if (g_drawsConsole && g_settings.modLog) rfa::Log("The console also shows logs\\rfa_mods.log as it grows");

    g_runScriptFile = reinterpret_cast<RunScriptFileFn>(
        rfa::HookFunction(kRunScriptFile, kRunScriptFilePrologue, reinterpret_cast<void*>(&RunScriptFile)));
    g_terrainTakeDown = reinterpret_cast<TerrainTakeDownFn>(
        rfa::HookFunction(kTerrainTakeDown, kTerrainTakeDownPrologue, reinterpret_cast<void*>(&TerrainTakeDown)));
    if (!g_terrainTakeDown) rfa::Log("Could not hook the game's terrain take-down; level geometry made live may outlive its level");
    g_gameplayStart = reinterpret_cast<GameplayStartFn>(
        rfa::HookFunction(kGameplayStart, kGameplayStartPrologue, reinterpret_cast<void*>(&GameplayStart)));
    rfa::Log(g_runScriptFile && g_gameplayStart
                 ? "console.ini: [Startup] applies at startup, [Gameplay] every time gameplay starts"
                 : "Could not hook the game's startup / gameplay points; console.ini is off");

    ShareWithEditor();
    g_frameUpdate = reinterpret_cast<FrameUpdateFn>(
        rfa::HookFunction(kFrameUpdate, kFrameUpdatePrologue, reinterpret_cast<void*>(&FrameUpdate)));
    rfa::Log(g_frameUpdate ? "Editor link: the camera is shared every frame, and bin\\link is watched for the editor's commands"
                           : "Could not hook the game's frame update; the editor link is off");
    g_toneMapStep = reinterpret_cast<ToneMapStepFn>(
        rfa::HookFunction(kToneMapStep, kToneMapStepPrologue, reinterpret_cast<void*>(&ToneMapStep)));
    if (!g_toneMapStep) rfa::Log("Could not hook the renderer's tone mapping; the editor won't be told the game's exposure");
}
