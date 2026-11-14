// rfa_loader.dll: injected into the game by RFAModLauncher before any game code runs.
// Loads every DLL in the mods folder (alphabetically) and calls its RFAMod_Init export.
// Also writes a crash report (log lines + logs\crash-*.dmp) if the game crashes, lets files in
// overrides\ replace files inside the game's archives, and gives the game's menu scripts access
// to rfa_mods.ini. Hosts the mods' in-game menus, drawn with Dear ImGui (menus.cpp).
#define RFA_MOD_NAME "loader"
#include "rfa_mod.h"
#include "rfa_game.h"
#include "menus.h"
#include <DbgHelp.h>
#include <algorithm>
#include <cstddef>
#include <cstdio>
#include <deque>
#include <map>
#include <set>
#include <vector>

#pragma comment(linker, "/EXPORT:RFALoader_Init=_RFALoader_Init@4")

namespace {

// Catches crashes in a mod's init so they end up in the log instead of a silent exit.
bool CallModInit(void(__cdecl* init)()) {
    __try {
        init();
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// ---------------------------------------------------------------------------------------------
// Crash reports

using MiniDumpWriteDumpFn = decltype(&MiniDumpWriteDump);
MiniDumpWriteDumpFn g_writeDump;
LPTOP_LEVEL_EXCEPTION_FILTER g_gameFilter;  // the game's own handler, still called after ours
decltype(&SetUnhandledExceptionFilter) o_SetUnhandledExceptionFilter = SetUnhandledExceptionFilter;

struct CrashJob {
    EXCEPTION_POINTERS* exception;
    DWORD threadId;
    const wchar_t* dumpSuffix;  // added to the dump's name: L"" for a crash, L"-first" for a first-chance fault
};

// "module+offset" for an address, e.g. "amdxx32.dll+00DEA307".
void DescribeAddress(DWORD address, char* out, size_t size) {
    HMODULE module = nullptr;
    char path[MAX_PATH];
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCSTR>(address), &module) &&
        GetModuleFileNameA(module, path, MAX_PATH)) {
        const char* name = strrchr(path, '\\');
        snprintf(out, size, "%s+%08lX", name ? name + 1 : path, address - reinterpret_cast<DWORD>(module));
    } else {
        snprintf(out, size, "%08lX", address);
    }
}

// True if `address` points into the code of a loaded exe or DLL. Such values on the stack are
// mostly return addresses, so they show which functions led to the crash (some may be stale).
bool LooksLikeReturnAddress(DWORD address) {
    MEMORY_BASIC_INFORMATION mbi;
    const DWORD exec = PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY;
    return address >= 0x10000 && VirtualQuery(reinterpret_cast<void*>(address), &mbi, sizeof(mbi)) &&
           mbi.Type == MEM_IMAGE && (mbi.Protect & exec);
}

// True if `size` bytes from `address` can be read (committed, readable, not a guard page).
bool Readable(DWORD address, size_t size) {
    MEMORY_BASIC_INFORMATION mbi;
    const DWORD readable = PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE |
                           PAGE_EXECUTE_WRITECOPY;
    return address >= 0x10000 && VirtualQuery(reinterpret_cast<void*>(address), &mbi, sizeof(mbi)) &&
           mbi.State == MEM_COMMIT && (mbi.Protect & readable) && !(mbi.Protect & PAGE_GUARD) &&
           address + size <= reinterpret_cast<DWORD>(mbi.BaseAddress) + mbi.RegionSize;
}

// The call chain by the frame pointers, which the game's build keeps: [ebp] is the caller's ebp,
// [ebp + 4] the return address into the caller.
void LogFrames(const CONTEXT* ctx) {
    char where[MAX_PATH + 16];
    DWORD ebp = ctx->Ebp;
    for (int i = 0; i < 24 && Readable(ebp, 8); ++i) {
        const DWORD next = reinterpret_cast<const DWORD*>(ebp)[0], ret = reinterpret_cast<const DWORD*>(ebp)[1];
        if (!LooksLikeReturnAddress(ret)) break;
        DescribeAddress(ret, where, sizeof(where));
        rfa::Log("  frame %d: %s", i, where);
        if (next <= ebp) break;
        ebp = next;
    }
}

void LogCrash(const CrashJob& job) {
    const EXCEPTION_RECORD* rec = job.exception->ExceptionRecord;
    const CONTEXT* ctx = job.exception->ContextRecord;
    char where[MAX_PATH + 16];
    DescribeAddress(reinterpret_cast<DWORD>(rec->ExceptionAddress), where, sizeof(where));
    rfa::Log("CRASH: exception %08lX at %s (thread %lu)", rec->ExceptionCode, where, job.threadId);
    if (rec->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && rec->NumberParameters >= 2) {
        ULONG_PTR kind = rec->ExceptionInformation[0];
        rfa::Log("  while %s address %08lX", kind == 8 ? "executing" : kind == 1 ? "writing" : "reading",
                 static_cast<DWORD>(rec->ExceptionInformation[1]));
    }
    rfa::Log("  EAX=%08lX EBX=%08lX ECX=%08lX EDX=%08lX ESI=%08lX EDI=%08lX EBP=%08lX ESP=%08lX", ctx->Eax,
             ctx->Ebx, ctx->Ecx, ctx->Edx, ctx->Esi, ctx->Edi, ctx->Ebp, ctx->Esp);
    LogFrames(ctx);

    // Probable callers: values on the stack that look like return addresses.
    MEMORY_BASIC_INFORMATION mbi;
    if (!VirtualQuery(reinterpret_cast<void*>(ctx->Esp), &mbi, sizeof(mbi))) return;
    auto top = reinterpret_cast<const DWORD*>(ctx->Esp);
    auto end = reinterpret_cast<const DWORD*>(static_cast<BYTE*>(mbi.BaseAddress) + mbi.RegionSize);
    int found = 0;
    for (const DWORD* p = top; p < end && p < top + 4096 && found < 30; ++p) {
        if (!LooksLikeReturnAddress(*p)) continue;
        DescribeAddress(*p, where, sizeof(where));
        rfa::Log("  stack+%04X: %s", static_cast<unsigned>((p - top) * 4), where);
        ++found;
    }
}

void WriteDump(const CrashJob& job) {
    if (!g_writeDump) return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    wchar_t name[64];
    swprintf_s(name, L"\\logs\\crash-%04d%02d%02d-%02d%02d%02d%s.dmp", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute,
               t.wSecond, job.dumpSuffix);
    const std::wstring path = rfa::RootDir() + name;
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    MINIDUMP_EXCEPTION_INFORMATION info{job.threadId, job.exception, FALSE};
    auto type = static_cast<MINIDUMP_TYPE>(MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithThreadInfo |
                                           MiniDumpWithUnloadedModules);
    BOOL ok = g_writeDump(GetCurrentProcess(), GetCurrentProcessId(), file, type, &info, nullptr, nullptr);
    CloseHandle(file);
    rfa::Log("  crash dump %s: %ls", ok ? "written" : "FAILED", path.c_str());
}

// Runs on a fresh thread: after a stack overflow the crashed thread has almost no stack left.
DWORD WINAPI WriteCrashReport(LPVOID param) {
    const auto& job = *static_cast<CrashJob*>(param);
    LogCrash(job);
    WriteDump(job);
    return 0;
}

LONG WINAPI CrashFilter(EXCEPTION_POINTERS* exception) {
    static LONG reported = 0;
    if (InterlockedExchange(&reported, 1) == 0) {
        CrashJob job{exception, GetCurrentThreadId(), L""};
        if (HANDLE thread = CreateThread(nullptr, 0, WriteCrashReport, &job, 0, nullptr)) {
            WaitForSingleObject(thread, 30000);
            CloseHandle(thread);
        }
    }
    return g_gameFilter ? g_gameFilter(exception) : EXCEPTION_CONTINUE_SEARCH;
}

// Keeps our filter installed when the game sets its own; the game's still runs after ours.
LPTOP_LEVEL_EXCEPTION_FILTER WINAPI Hk_SetUnhandledExceptionFilter(LPTOP_LEVEL_EXCEPTION_FILTER filter) {
    LPTOP_LEVEL_EXCEPTION_FILTER previous = g_gameFilter;
    g_gameFilter = filter;
    return previous;
}

// First-chance access violations can be handled by the code that caused them, so these are
// only noted (a few at most) in case the crash filter never gets to run. One in the game's own
// code is reported in full (what it touched, the registers, the call chain) and the first such
// gets a dump of its own (logs\crash-*-first.dmp): the game can catch a fault in its code and
// quit, and the crash that may follow while it exits no longer shows where the fault was.
LONG CALLBACK NoteAccessViolation(EXCEPTION_POINTERS* exception) {
    static LONG noted = 0, dumped = 0;
    const EXCEPTION_RECORD* rec = exception->ExceptionRecord;
    // A stack overflow (calls that never stop calling each other) can end the game without the crash
    // filter ever running: report it here, from a fresh thread, as this one has no stack left.
    static LONG overflowed = 0;
    if (rec->ExceptionCode == EXCEPTION_STACK_OVERFLOW && InterlockedExchange(&overflowed, 1) == 0) {
        static CrashJob job;
        job = CrashJob{exception, GetCurrentThreadId(), L"-overflow"};
        if (HANDLE thread = CreateThread(nullptr, 0, WriteCrashReport, &job, 0, nullptr)) {
            WaitForSingleObject(thread, 30000);
            CloseHandle(thread);
        }
        return EXCEPTION_CONTINUE_SEARCH;
    }
    if (rec->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && InterlockedIncrement(&noted) <= 5) {
        char where[MAX_PATH + 16];
        DescribeAddress(reinterpret_cast<DWORD>(rec->ExceptionAddress), where, sizeof(where));
        rfa::Log("First-chance access violation at %s (may be handled)", where);
        HMODULE module = nullptr;
        if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                               static_cast<LPCSTR>(rec->ExceptionAddress), &module) &&
            module == GetModuleHandleW(nullptr)) {
            if (rec->NumberParameters >= 2)
                rfa::Log("  while %s address %08lX", rec->ExceptionInformation[0] == 8 ? "executing"
                                                     : rec->ExceptionInformation[0] == 1 ? "writing" : "reading",
                         static_cast<DWORD>(rec->ExceptionInformation[1]));
            const CONTEXT* ctx = exception->ContextRecord;
            rfa::Log("  EAX=%08lX EBX=%08lX ECX=%08lX EDX=%08lX ESI=%08lX EDI=%08lX EBP=%08lX ESP=%08lX", ctx->Eax,
                     ctx->Ebx, ctx->Ecx, ctx->Edx, ctx->Esi, ctx->Edi, ctx->Ebp, ctx->Esp);
            LogFrames(ctx);
            if (InterlockedExchange(&dumped, 1) == 0) {
                CrashJob job{exception, GetCurrentThreadId(), L"-first"};
                if (HANDLE thread = CreateThread(nullptr, 0, [](LPVOID param) -> DWORD {
                        WriteDump(*static_cast<CrashJob*>(param));
                        return 0;
                    }, &job, 0, nullptr)) {
                    WaitForSingleObject(thread, 30000);
                    CloseHandle(thread);
                }
            }
        }
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

void InstallCrashReporter() {
    if (!rfa::IniInt(L"Loader", L"CrashReports", 1)) return;
    if (HMODULE dbghelp = LoadLibraryW(L"dbghelp.dll"))
        g_writeDump = reinterpret_cast<MiniDumpWriteDumpFn>(GetProcAddress(dbghelp, "MiniDumpWriteDump"));
    g_gameFilter = SetUnhandledExceptionFilter(CrashFilter);
    bool hooked = rfa::HookImport(GetModuleHandleW(nullptr), "KERNEL32.dll", "SetUnhandledExceptionFilter",
                                  &Hk_SetUnhandledExceptionFilter, &o_SetUnhandledExceptionFilter);
    AddVectoredExceptionHandler(0, NoteAccessViolation);
    rfa::Log("Crash reports on (dumps go to logs\\crash-*.dmp)%s", hooked ? "" : "; could not hook the game's own handler");
}

// ---------------------------------------------------------------------------------------------
// The large-address-aware copy (the launcher's LargeAddressAware=)

// Started from the launcher's copy of its exe, the game still asks where its own exe is: WinMain
// first makes that folder the current one (0xA455B0), the DLC search looks in its build folder
// (0x519B50), and the C runtime keeps it as the program's name. So its imports of
// GetModuleFileNameA/W answer, for the game's own module, with the installed exe in the game folder,
// as when it is started from there. Other modules are answered as Windows answers.
std::wstring g_installedExe;
using GetModuleFileNameWFn = DWORD(WINAPI*)(HMODULE, LPWSTR, DWORD);
using GetModuleFileNameAFn = DWORD(WINAPI*)(HMODULE, LPSTR, DWORD);
GetModuleFileNameWFn o_GetModuleFileNameW;
GetModuleFileNameAFn o_GetModuleFileNameA;

bool IsGameModule(HMODULE module) { return module == nullptr || module == GetModuleHandleW(nullptr); }

// Copies `text` into `out` as GetModuleFileName does: cut short to fit with ERROR_INSUFFICIENT_BUFFER
// and the buffer's size returned, else the length.
template <class Char>
DWORD AnswerPath(const std::basic_string<Char>& text, Char* out, DWORD size) {
    if (size == 0) {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return 0;
    }
    const DWORD length = static_cast<DWORD>(text.size());
    const DWORD copied = length < size ? length : size - 1;
    std::copy(text.begin(), text.begin() + copied, out);
    out[copied] = 0;
    if (length >= size) {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return size;
    }
    SetLastError(ERROR_SUCCESS);
    return length;
}

DWORD WINAPI Hk_GetModuleFileNameW(HMODULE module, LPWSTR out, DWORD size) {
    if (!IsGameModule(module)) return o_GetModuleFileNameW(module, out, size);
    return AnswerPath(g_installedExe, out, size);
}

DWORD WINAPI Hk_GetModuleFileNameA(HMODULE module, LPSTR out, DWORD size) {
    if (!IsGameModule(module)) return o_GetModuleFileNameA(module, out, size);
    const int bytes = WideCharToMultiByte(CP_ACP, 0, g_installedExe.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string narrow(bytes > 0 ? bytes - 1 : 0, '\0');
    if (bytes > 1) WideCharToMultiByte(CP_ACP, 0, g_installedExe.c_str(), -1, narrow.data(), bytes, nullptr, nullptr);
    return AnswerPath(narrow, out, size);
}

void InstallInstalledExePath() {
    wchar_t running[MAX_PATH];
    GetModuleFileNameW(nullptr, running, MAX_PATH);
    g_installedExe = rfa::game::InstalledExePath();
    if (_wcsicmp(g_installedExe.c_str(), running) != 0) {
        HMODULE game = GetModuleHandleW(nullptr);
        const bool wide = rfa::HookImport(game, "KERNEL32.dll", "GetModuleFileNameW", &Hk_GetModuleFileNameW, &o_GetModuleFileNameW);
        const bool narrow = rfa::HookImport(game, "KERNEL32.dll", "GetModuleFileNameA", &Hk_GetModuleFileNameA, &o_GetModuleFileNameA);
        rfa::Log("Started from the large-address-aware copy; the game is told its program is %ls%s", g_installedExe.c_str(),
                 wide && narrow ? "" : " (COULD NOT hook its GetModuleFileName: it may look for its files next to the copy)");
    }
    MEMORYSTATUSEX memory{sizeof(memory)};
    if (GlobalMemoryStatusEx(&memory))
        rfa::Log("The game's address space: %llu MB (%s)", memory.ullTotalVirtual >> 20,
                 memory.ullTotalVirtual > (2ull << 30) ? "large address aware" : "the 2 GB of a 32-bit program not marked large address aware");
}

// ---------------------------------------------------------------------------------------------
// Lua: script overrides and settings access for the game's menu scripts

using rfa::game::lua_State;
using LoadBufferFn = int(__cdecl*)(lua_State*, const char*, size_t, const char*);
using RegisterFn = void(__cdecl*)(lua_State*);
LoadBufferFn g_loadBuffer;           // the game's luaL_loadbuffer
RegisterFn g_registerGameFunctions;  // the game's function that registers its C functions with Lua
std::vector<HMODULE> g_mods; // loaded mods, told when a setting changes from the game's menus

// File name a chunk was loaded from, or empty for chunks made from code strings.
std::string ScriptFileName(const char* chunkName) {
    if (!chunkName) return "";
    if (*chunkName == '@' || *chunkName == '=') ++chunkName;
    size_t len = strlen(chunkName);
    if (len == 0 || len > 120 || strpbrk(chunkName, "\r\n ()=")) return "";
    const char* base = (std::max)(strrchr(chunkName, '\\'), strrchr(chunkName, '/'));
    std::string name = base ? base + 1 : chunkName;
    if (name.size() < 4 || _stricmp(name.c_str() + name.size() - 4, ".lua") != 0) name += ".lua";
    return name;
}

bool ReadWholeFile(const std::wstring& path, std::string& out) {
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return false;
    out.resize(GetFileSize(f, nullptr));
    DWORD read = 0;
    bool ok = ReadFile(f, out.data(), static_cast<DWORD>(out.size()), &read, nullptr) && read == out.size();
    CloseHandle(f);
    return ok;
}

std::string Narrow(const std::wstring& text) {
    char out[MAX_PATH * 2];
    if (!WideCharToMultiByte(CP_ACP, 0, text.c_str(), -1, out, sizeof(out), nullptr, nullptr)) out[0] = '\0';
    return out;
}

// Like Narrow, but empty if some character has no equivalent in the game's (ANSI) file functions.
std::string NarrowExact(const std::wstring& text) {
    char out[MAX_PATH * 2];
    BOOL lossy = FALSE;
    if (!WideCharToMultiByte(CP_ACP, 0, text.c_str(), -1, out, sizeof(out), nullptr, &lossy) || lossy) return "";
    return out;
}

std::string Lower(std::string text) {
    std::transform(text.begin(), text.end(), text.begin(), [](unsigned char c) { return static_cast<char>(tolower(c)); });
    return text;
}

// ---------------------------------------------------------------------------------------------
// Overrides: files in overrides\ that replace files inside the game's archives, which stay untouched.
//
// overrides\ mirrors the game folder. A folder named after an archive holds replacements for files
// inside that archive:
//   overrides\build\pc\cache\interface.vpp_pc\ui_options_gameplay.lua
// replaces ui_options_gameplay.lua from <game>\build\pc\cache\interface.vpp_pc. At startup the
// loader checks that each archive exists and really contains the file, and logs every override it
// will use.
//
// Lua scripts are swapped when the game compiles them (LoadBufferHook). Every other file is swapped
// when the game looks it up by name (FindFileHook): the loader hands back the replacement as a
// loose file on disk, which the game then reads like any other.
//
// The game asks for files by name, and some names are in several archives with different contents
// (a container in each multiplayer map, for example), of which the game uses the one in the archive
// it has loaded. An override only replaces the copy from its own archive: the loader notes the size
// of that copy at startup and swaps a lookup only when the game found a copy of that size. Archives
// with identical copies (every table is in both table.vpp_pc and misc.vpp_pc) share the override.
//
// A file outside any archive's folder that the game folder doesn't have is a new file, such as a
// new map's archive:
//   overrides\build\pc\cache\rfg01.vpp_pc
// When the game looks up a file and finds it nowhere, the loader hands it the new file at that
// path (build\pc\cache\rfg01.vpp_pc), or of that name, as a loose file. The game mounts archives
// this way too: before loading a level it asks whether build\pc\cache\<level>.vpp_pc exists
// (0x6CD1A0, through 0xC92010), then opens it with cf_open (0xC91470 → 0xC914E0), both through
// FindFile; the mount itself (0xCB5280 → 0xCB53C0) reads the archive from the file it was given.

struct NewFile {
    std::wstring path;       // full path of the file, in the overrides folder
    std::string ansiPath;    // the same, for the game's file functions
    std::string relative;    // its path inside overrides\ (and in the game folder), for the log
    LONG used = 0;
};
std::map<std::string, NewFile> g_newFiles;  // lower-case path in the game folder, and file name -> the file

struct Override {
    std::wstring path;       // full path of the replacement
    std::string ansiPath;    // the same path for the game's own file functions, which take ANSI text
    std::string relative;    // path inside overrides\, for the log
    DWORD archiveSize = 0;   // size of the file it replaces, in its archive
    LONG used = 0;           // set the first time the game reads it, so the log says so only once
};
// (A deque: adding to it leaves the replacements already there where they are, so the file hooks can
// keep one they found while more are added: see RFALoader_RescanOverrides.)
std::map<std::string, std::deque<Override>> g_overrides;  // lower-case file name -> replacements
size_t g_overrideCount = 0;

// The game looks files up on several threads, and the editor can ask for overrides\ and edits\ to be
// looked through again while the game runs (RFALoader_RescanOverrides): the hooks hold this shared
// while they search g_overrides and g_newFiles (never while they call the game), a rescan alone
// while it adds to them. Nothing is ever taken out, so what a hook found stays good.
SRWLOCK g_filesLock = SRWLOCK_INIT;
struct SharedFiles {
    SharedFiles() { AcquireSRWLockShared(&g_filesLock); }
    ~SharedFiles() { ReleaseSRWLockShared(&g_filesLock); }
};
std::set<std::wstring> g_seenFiles;  // every file of overrides\ and edits\ looked at, so a rescan only looks at new ones

struct ArchiveFile {
    std::string name;
    DWORD size;
    DWORD compressedSize;
};

// The files inside a .vpp_pc archive (format version 6), with their sizes. Layout: a 2 KB header
// (file count and the size of each block at 0x154), then the directory (24 bytes per file, the
// size at +12) and then the names, each block starting on a 2 KB boundary.
bool ReadArchiveDirectory(const std::wstring& archive, std::vector<ArchiveFile>& files) {
    HANDLE f = CreateFileW(archive.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return false;
    struct Header {
        DWORD magic, version;
        BYTE unused[0x154 - 8];
        DWORD fileCount, archiveSize, directorySize, namesSize;
    } header{};
    struct Entry {
        DWORD nameOffset, zero, dataOffset, size, compressedSize, zero2;
    };
    DWORD read = 0;
    bool ok = ReadFile(f, &header, sizeof(header), &read, nullptr) && read == sizeof(header) &&
              header.magic == 0x51890ACE && header.version == 6 && header.namesSize < 64 * 1024 * 1024 &&
              header.fileCount < 1024 * 1024 && header.directorySize >= header.fileCount * sizeof(Entry);
    std::vector<Entry> directory(ok ? header.fileCount : 0);
    std::string names(ok ? header.namesSize : 0, '\0');
    if (ok) {
        const DWORD directoryBytes = header.fileCount * sizeof(Entry);
        const DWORD namesStart = 0x800 + ((header.directorySize + 0x7FF) & ~0x7FFu);
        ok = SetFilePointer(f, 0x800, nullptr, FILE_BEGIN) == 0x800 &&
             ReadFile(f, directory.data(), directoryBytes, &read, nullptr) && read == directoryBytes &&
             SetFilePointer(f, namesStart, nullptr, FILE_BEGIN) == namesStart &&
             ReadFile(f, names.data(), header.namesSize, &read, nullptr) && read == header.namesSize;
    }
    CloseHandle(f);
    if (!ok) return false;
    for (const Entry& entry : directory) {
        if (entry.nameOffset >= names.size()) return false;
        files.push_back({names.c_str() + entry.nameOffset, entry.size, entry.compressedSize});
    }
    return true;
}

// Checks one file in overrides\ and adds it to g_overrides. `relative` is its path inside overrides\.
void AddOverride(const std::wstring& root, const std::wstring& relative) {
    const std::string shown = Narrow(relative);
    const size_t slash = relative.find_last_of(L'\\');
    const std::wstring archive = slash == std::wstring::npos ? L"" : relative.substr(0, slash);
    const std::string name = Narrow(relative.substr(slash + 1));
    std::wstring gameDir = rfa::game::InstalledExePath();
    gameDir.resize(gameDir.find_last_of(L'\\'));
    if (archive.size() < 7 || _wcsicmp(archive.c_str() + archive.size() - 7, L".vpp_pc") != 0) {
        // Not in an archive's folder: a new file, if the game folder hasn't one there.
        if (GetFileAttributesW((gameDir + L"\\" + relative).c_str()) != INVALID_FILE_ATTRIBUTES) {
            rfa::Log("Override %s skipped: the game folder has this file, and whole game files can't be replaced; "
                     "put replacements in the folder of the archive they come from, "
                     "e.g. overrides\\build\\pc\\cache\\interface.vpp_pc\\%s", shown.c_str(), name.c_str());
            return;
        }
        const std::wstring path = root + L"\\" + relative;
        const std::string ansiPath = NarrowExact(path);
        if (ansiPath.empty() || ansiPath.size() >= sizeof(rfa::game::FileFindResult::name)) {
            rfa::Log("New file %s skipped: the game can't open files at this path (it must be shorter than 256 "
                     "characters, without letters outside your Windows language)", shown.c_str());
            return;
        }
        NewFile added{path, ansiPath, shown};
        g_newFiles[Lower(shown)] = added;
        g_newFiles.try_emplace(Lower(name), added);  // also by its name alone, if no other new file has it
        rfa::Log("New file: %s", shown.c_str());
        return;
    }
    std::vector<ArchiveFile> files;
    if (!ReadArchiveDirectory(gameDir + L"\\" + archive, files)) {
        rfa::Log("Override %s skipped: the game has no archive %ls", shown.c_str(), archive.c_str());
        return;
    }
    const std::string key = Lower(name);
    auto original = std::find_if(files.begin(), files.end(), [&](const ArchiveFile& f) { return Lower(f.name) == key; });
    if (original == files.end()) {
        rfa::Log("Override %s skipped: %ls has no file named %s", shown.c_str(), archive.c_str(), name.c_str());
        return;
    }
    if (auto sameName = g_overrides.find(key); sameName != g_overrides.end()) {
        for (const Override& other : sameName->second) {
            if (other.archiveSize == original->size) {
                rfa::Log("Override %s skipped: %s already overrides the same %s", shown.c_str(),
                         other.relative.c_str(), name.c_str());
                return;
            }
        }
    }
    const std::wstring path = root + L"\\" + relative;
    const std::string ansiPath = NarrowExact(path);
    if (ansiPath.empty() || ansiPath.size() >= sizeof(rfa::game::FileFindResult::name)) {
        rfa::Log("Override %s skipped: the game can't open files at this path (it must be shorter than 256 "
                 "characters, without letters outside your Windows language)", shown.c_str());
        return;
    }
    Override& added = g_overrides[key].emplace_back();
    added.path = path;
    added.ansiPath = ansiPath;
    added.relative = shown;
    added.archiveSize = original->size;
    ++g_overrideCount;
    rfa::Log("Override: %s", shown.c_str());
}

bool IsLuaScript(const std::string& name) {
    return name.size() > 4 && _stricmp(name.c_str() + name.size() - 4, ".lua") == 0;
}

// Walks overrides\ (and its subfolders). README files are notes for people, not overrides.
void FindOverrides(const std::wstring& root, const std::wstring& relative) {
    WIN32_FIND_DATAW fd;
    const std::wstring folder = relative.empty() ? root : root + L"\\" + relative;
    HANDLE find = FindFirstFileW((folder + L"\\*").c_str(), &fd);
    if (find == INVALID_HANDLE_VALUE) return;
    do {
        const std::wstring name = fd.cFileName;
        if (name == L"." || name == L"..") continue;
        const std::wstring path = relative.empty() ? name : relative + L"\\" + name;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) FindOverrides(root, path);
        else if (_wcsnicmp(name.c_str(), L"README", 6) != 0 && g_seenFiles.insert(L"overrides\\" + path).second) AddOverride(root, path);
    } while (FindNextFileW(find, &fd));
    FindClose(find);
}

// edits\ does for the archives the mod adds (new archives in overrides\, such as the Guerrilla
// world's build\pc\cache\rfg_l1.vpp_pc, which the Guerrilla builder made) what overrides\ does for
// the game's: edits\build\pc\cache\rfg_l1.vpp_pc\1h_c0312.str2_pc replaces that archive's copy of
// 1h_c0312.str2_pc. (overrides\ can't hold them: the archive itself is a file there, with the name the
// folder would have.) The editor writes its changes to those archives here, so the archives
// themselves stay as they were built. Beside each, <file>.rfabase gives the size and compressed size
// the archive's copy had when the change was made: if the archive was built again since and its copy
// is different, the change is skipped (it was made to the old one).
void AddEdit(const std::wstring& root, const std::wstring& relative) {
    const std::string shown = "edits\\" + Narrow(relative);
    const size_t slash = relative.find_last_of(L'\\');
    const std::wstring archive = slash == std::wstring::npos ? L"" : relative.substr(0, slash);
    const std::string name = Narrow(relative.substr(slash + 1));
    if (name.size() > 8 && _stricmp(name.c_str() + name.size() - 8, ".rfabase") == 0) return;  // (read with its file)
    if (archive.size() < 7 || _wcsicmp(archive.c_str() + archive.size() - 7, L".vpp_pc") != 0) {
        rfa::Log("Edit %s skipped: it isn't in an archive's folder", shown.c_str());
        return;
    }
    std::wstring gameDir = rfa::game::InstalledExePath();
    gameDir.resize(gameDir.find_last_of(L'\\'));
    if (GetFileAttributesW((gameDir + L"\\" + archive).c_str()) != INVALID_FILE_ATTRIBUTES) {
        rfa::Log("Edit %s skipped: %ls is the game's archive; its files are overridden in overrides\\", shown.c_str(), archive.c_str());
        return;
    }
    std::vector<ArchiveFile> files;
    if (!ReadArchiveDirectory(rfa::RootDir() + L"\\overrides\\" + archive, files)) {
        rfa::Log("Edit %s skipped: there is no archive overrides\\%ls", shown.c_str(), archive.c_str());
        return;
    }
    const std::string key = Lower(name);
    auto original = std::find_if(files.begin(), files.end(), [&](const ArchiveFile& f) { return Lower(f.name) == key; });
    if (original == files.end()) {
        rfa::Log("Edit %s skipped: overrides\\%ls has no file named %s", shown.c_str(), archive.c_str(), name.c_str());
        return;
    }
    const std::wstring path = root + L"\\" + relative;
    // Made to the archive as it is now? (Its copy's sizes then, in <file>.rfabase.)
    std::string base;
    if (ReadWholeFile(path + L".rfabase", base)) {
        unsigned long size = 0, compressed = 0;
        if (sscanf_s(base.c_str(), "%lu %lu", &size, &compressed) == 2 && (size != original->size || compressed != original->compressedSize)) {
            rfa::Log("Edit %s skipped: the archive was built again since this change was made (its %s is another now); "
                     "make the change again in the editor", shown.c_str(), name.c_str());
            return;
        }
    }
    if (auto sameName = g_overrides.find(key); sameName != g_overrides.end()) {
        for (const Override& other : sameName->second) {
            if (other.archiveSize == original->size) {
                rfa::Log("Edit %s skipped: %s already replaces the same %s", shown.c_str(), other.relative.c_str(), name.c_str());
                return;
            }
        }
    }
    const std::string ansiPath = NarrowExact(path);
    if (ansiPath.empty() || ansiPath.size() >= sizeof(rfa::game::FileFindResult::name)) {
        rfa::Log("Edit %s skipped: the game can't open files at this path (it must be shorter than 256 "
                 "characters, without letters outside your Windows language)", shown.c_str());
        return;
    }
    Override& added = g_overrides[key].emplace_back();
    added.path = path;
    added.ansiPath = ansiPath;
    added.relative = shown;
    added.archiveSize = original->size;
    ++g_overrideCount;
    rfa::Log("Edit: %s", shown.c_str());
}

// Walks edits\ (and its subfolders), as FindOverrides does overrides\.
void FindEdits(const std::wstring& root, const std::wstring& relative) {
    WIN32_FIND_DATAW fd;
    const std::wstring folder = relative.empty() ? root : root + L"\\" + relative;
    HANDLE find = FindFirstFileW((folder + L"\\*").c_str(), &fd);
    if (find == INVALID_HANDLE_VALUE) return;
    do {
        const std::wstring name = fd.cFileName;
        if (name == L"." || name == L"..") continue;
        const std::wstring path = relative.empty() ? name : relative + L"\\" + name;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) FindEdits(root, path);
        else if (_wcsnicmp(name.c_str(), L"README", 6) != 0 && g_seenFiles.insert(L"edits\\" + path).second) AddEdit(root, path);
    } while (FindNextFileW(find, &fd));
    FindClose(find);
}

// The game's Lua hands each run-time error of a script (the message, with the script and line) to
// one callback: 0xD93590 calls the pointer at 0x34FA99C, which 0xD935D0 sets. The game's own
// (0xB77320, set by its script start-up, 0xB77240) only keeps the last message in a buffer, so a
// menu whose script fails just stays empty. Ours writes each error to the log too, then passes it
// on. It is put in place (again, if the game has set its own since) whenever a script is compiled.
using LuaErrorCallbackFn = void(__cdecl*)(lua_State*, const char*);
auto& g_luaErrorCallback = *reinterpret_cast<LuaErrorCallbackFn*>(0x34FA99C);
auto const SetLuaErrorCallback = reinterpret_cast<void(__cdecl*)(LuaErrorCallbackFn)>(0xD935D0);
LuaErrorCallbackFn g_gameLuaErrorCallback;

void __cdecl LogLuaError(lua_State* L, const char* message) {
    RFA_LOG_FIRST(100, "Lua error: %s", message ? message : "(no message)");
    if (g_gameLuaErrorCallback) g_gameLuaErrorCallback(L, message);
}

void WatchLuaErrors() {
    if (g_luaErrorCallback == &LogLuaError) return;
    g_gameLuaErrorCallback = g_luaErrorCallback;
    SetLuaErrorCallback(&LogLuaError);
}

// Every Lua chunk the game compiles passes through here; an override with the same file name
// replaces it.
int __cdecl LoadBufferHook(lua_State* L, const char* buffer, size_t size, const char* chunkName) {
    WatchLuaErrors();
    const std::string name = ScriptFileName(chunkName);
    if (!name.empty()) {
        RFA_LOG_FIRST(60, "Lua script loaded: %s", chunkName);
        const Override* replacement = nullptr;
        {
            SharedFiles shared;
            auto found = g_overrides.find(Lower(name));
            if (found != g_overrides.end()) replacement = &found->second.front();
        }
        std::string text;
        if (replacement && ReadWholeFile(replacement->path, text)) {
            rfa::Log("Using override %s", replacement->relative.c_str());
            return g_loadBuffer(L, text.data(), text.size(), chunkName);
        }
    }
    return g_loadBuffer(L, buffer, size, chunkName);
}

using rfa::game::FileFindResult;
using FindFileFn = bool(__cdecl*)(FileFindResult*, const char*, bool);
FindFileFn g_findFile;  // the game's FindFile

// Forward declarations for FindNewFile
extern bool g_logLookups;
bool IsLevelFile(const char* name);

// A file the game found nowhere: a new file in overrides\ at that path (build\pc\cache\rfg01.vpp_pc,
// or .\build\pc\cache\..., with / or \), or of that name (also when the game asks with a full
// path), as a loose file. A request it can't answer whose name holds a new file's own name (rfg01
// in rfg01.rfgzone_pc) is logged, to show what a new map still lacks.
bool FindNewFile(FileFindResult* result, const char* name) {
    std::string key = Lower(name);
    std::replace(key.begin(), key.end(), '/', '\\');
    if (key.rfind(".\\", 0) == 0) key.erase(0, 2);
    NewFile* file = nullptr;
    {
        SharedFiles shared;
        auto found = g_newFiles.find(key);
        if (found == g_newFiles.end()) found = g_newFiles.find(key.substr(key.find_last_of('\\') + 1));
        // The console's "level <zone>" (0x58E9A0) first checks that the level is there, as a world's
        // first layer: build\pc\cache\<zone>_l0.vpp_pc (0x6CD1A0). A level the editor made (New level)
        // is one archive, <zone>.vpp_pc, as the game's Ruin and Infestation maps are (which the check
        // doesn't find either: they are started from their menus, which don't check): it answers for it.
        // The level is then loaded by its zone's name, as from the menus.
static const std::string kLayer0 = "_l0.vpp_pc";
        if (found == g_newFiles.end() && key.size() >= kLayer0.size() &&
            key.compare(key.size() - kLayer0.size(), kLayer0.size(), kLayer0) == 0) {
            std::string level = key.substr(0, key.size() - kLayer0.size()) + ".vpp_pc";
            found = g_newFiles.find(level);
            if (found == g_newFiles.end()) found = g_newFiles.find(level.substr(level.find_last_of('\\') + 1));
            // Also try the filename alone in case the request was a bare filename
            if (found == g_newFiles.end()) {
                std::string levelName = level.substr(level.find_last_of('\\') + 1);
                found = g_newFiles.find(levelName);
            }
            if (found != g_newFiles.end()) {
                rfa::Log("Level load: mapped %s -> %s (single-archive level)", name, found->second.relative.c_str());
            } else {
                rfa::Log("Level load: asked for %s (_l0.vpp_pc) but no single-archive level found for %s", name, level.c_str());
            }
        }
        if (found == g_newFiles.end()) {
            if (g_logLookups && IsLevelFile(name)) {
                rfa::Log("Level load: file not found in new files: %s", name);
            }
            for (const auto& [newKey, newFile] : g_newFiles) {
                const std::string stem = newKey.substr(newKey.find_last_of('\\') + 1, newKey.find_last_of('.') - newKey.find_last_of('\\') - 1);
                if (stem.size() >= 3 && key.find(stem) != std::string::npos) {
                    RFA_LOG_FIRST(100, "The game looked for %s and found it nowhere (no new file is called that)", name);
                    break;
                }
            }
            return false;
        }
        file = &found->second;
    }
    result->source = -1;
    result->pathType = -1;
    result->name[0] = '\0';
    result->size = 0;
    result->archiveEntry = nullptr;
    const bool ok = rfa::game::FindLooseFile(result, file->ansiPath.c_str(), 0);
    if (InterlockedExchange(&file->used, 1) == 0)
        rfa::Log(ok ? "Using new file %s (the game asked for %s)" : "New file %s could not be opened (the game asked for %s)",
                 file->relative.c_str(), name);
    return ok;
}

// Every file the game looks up by name passes through here. The game finds it first; if it found it
// in an archive and an override replaces that archive's copy (same size), the result is changed to
// point at the override instead.
// [Loader] LogLookups=1: every lookup of a level's files (zones, layers, archives, containers,
// asset lists, terrain, stitch lists, navigation, tables, time of day), with what the game found,
// goes to the log. For working out which files a level loads (up to 4,000 lines a run).
bool g_logLookups = false;

bool IsLevelFile(const char* name) {
    static const char* const kinds[] = {".rfgzone_pc", ".layer_pc", ".vpp_pc", ".str2_pc", ".asm_pc", ".cterrain_pc",
                                        ".ctmesh_pc", ".stchlst_pc", ".navdata_pc", ".precache", ".xtbl",
                                        ".gtodx", ".dtodx"};
    const size_t length = strlen(name);
    for (const char* kind : kinds) {
        const size_t size = strlen(kind);
        if (length >= size && _stricmp(name + length - size, kind) == 0) return true;
    }
    return false;
}

// Ruin (the game calls it Wrecking Crew) keeps its maps in a list: a pointer per map, read from
// wrecking_crew_maps.xtbl, one wc_map_list entry per map. The menu shows the list, and a match
// starts the map at the chosen place in it.
struct RuinWeaponList {  // the game's growable array: the weapons a map's par score unlocks, as checksums
    DWORD* items;
    int capacity;
    int count;
};

struct RuinMap {
    const char* fileName;           // +0x00 ruin01: the level's archive is build\pc\cache\ruin01.vpp_pc
    DWORD fileNameChecksum;         // +0x04 of the name, ignoring case
    const char* displayName;        // +0x08 a string id, RUIN_MAP_01
    const char* displayNameUpper;   // +0x0C
    const char* imageName;          // +0x10
    int timeOfDayHour;              // +0x14
    int timeOfDayMinute;            // +0x18
    void* resourceReservation;      // +0x1C
    int roundTimeMs;                // +0x20 total_chaos_round_time_ms
    float timeBonusTarget;          // +0x24 total_chaos_time_bonus_target
    int timeBonusTimeMs;            // +0x28 total_chaos_time_bonus_time_ms
    bool locked;                    // +0x2C is_locked
    bool lockedUntilEarned;         // +0x2D a base-game map, until the map before it unlocks it
    int dlc;                        // +0x30 the DLC the table came from, or -1
    int scoreboardIndex;            // +0x34
    int parScore;                   // +0x38 par_unlocks: the score to beat,
    int maxScoreStat;               // +0x3C
    bool unlocksExo;                // +0x40
    const char* unlocksMap;         // +0x44 the map that opens when it is beaten,
    RuinWeaponList unlocksWeapons;  // +0x48 and the weapons
    BYTE notRead54[0x14];           // +0x54 (the reader leaves these alone)
    int ambientBank;                // +0x68 Audio
    DWORD musicTrigger;             // +0x6C
    float damageToScore;            // +0x70 Damage_To_Score_Multiplier
    int nanoforgeAbility;           // +0x74
};
static_assert(offsetof(RuinMap, lockedUntilEarned) == 0x2D && offsetof(RuinMap, unlocksWeapons) == 0x48 &&
                  offsetof(RuinMap, nanoforgeAbility) == 0x74, "RuinMap must match the game's map record");

inline RuinMap** const g_ruinMaps = rfa::game::At<RuinMap**>(0x12F39E8);  // room for 35
inline int& g_ruinMapCount = *rfa::game::At<int*>(0x12F3A74);
inline const int& g_chosenRuinMap = *rfa::game::At<const int*>(0x10F857C);
inline void* const kRuinMapPool = rfa::game::At<void*>(0x12F3DA8);     // the "WC mempool": room for 32 maps
inline void* const kRuinStringPool = rfa::game::At<void*>(0x12F3C78);  // keeps the maps' names for good
inline const DWORD& kNoChecksum = *rfa::game::At<const DWORD*>(0x290A190);
inline const DWORD& kNoMusicTrigger = *rfa::game::At<const DWORD*>(0x10724B8);

// The game's table helpers, as its reader uses them. An entry is an XML element (its name at +0, its
// text at +0xC). The "required" ones log an error when the field is missing; the others don't.
struct XmlElement {
    const char* name;
    void* notUsed[2];
    const char* text;
};
inline const auto FirstEntry = rfa::game::At<void*(__cdecl*)(void* table, const char* name)>(0xC8F730);
inline const auto NextEntry = rfa::game::At<void*(__cdecl*)(void* table, void* after, const char* name)>(0xC8F780);
inline const auto EntryText = rfa::game::At<const char*(__cdecl*)(void* entry, const char* field)>(0xC8C800);
inline const auto OptionalText = rfa::game::At<const char*(__cdecl*)(void* entry, const char* field)>(0xC8C8E0);
inline const auto RequiredChild = rfa::game::At<void*(__cdecl*)(void* entry, const char* name)>(0xC8C710);
inline const auto FirstChild = rfa::game::At<void*(__cdecl*)(void* entry, const char* name)>(0xC8C7A0);
inline const auto NextChild = rfa::game::At<void*(__cdecl*)(void* entry, void* after, const char* name)>(0xC8C7C0);
inline const auto ReadInt = rfa::game::At<void(__cdecl*)(int& out, void* entry, const char* field)>(0xC8CAC0);
inline const auto ReadFloat = rfa::game::At<void(__cdecl*)(float& out, void* entry, const char* field)>(0xC8CD30);
inline const auto ReadBool = rfa::game::At<void(__cdecl*)(bool& out, void* entry, const char* field)>(0xC8CE00);
inline const auto ReadOptionalBool = rfa::game::At<bool(__cdecl*)(bool& out, void* entry, const char* field)>(0xC8CE60);
// What the reader looks names up with: "hh:mm", a spawn resource reservation, a stat, a sound
// bank (from a field), a Nano Forge ability.
inline const auto ReadTimeOfDay = rfa::game::At<void(__cdecl*)(int* hour, int* minute, const char* text)>(0x9F6970);
inline const auto FindResourceReservation = rfa::game::At<void*(__cdecl*)(const char* name)>(0x75F2F0);
inline const auto FindStat = rfa::game::At<int(__cdecl*)(const char* name)>(0x554860);
inline const auto ReadSoundBank = rfa::game::At<int(__cdecl*)(void* entry, const char* field)>(0x537A90);
inline const auto FindNanoforgeAbility = rfa::game::At<int(__cdecl*)(const char* name)>(0x71A210);
// Methods (the object in ECX), called as __fastcall with EDX unused. KeepString copies a name into
// the string pool (or finds it there, ignoring case); NameChecksum works out a name's checksum into
// `out` and returns `out`; NewRuinMap takes a record from the pool (nullptr when it is full); AddWeapon
// adds to a weapon list, growing it.
inline const auto KeepString =
    rfa::game::At<const char*(__fastcall*)(void* pool, void* unused, const char* text, int, int)>(0xC97DB0);
inline const auto NameChecksum =
    rfa::game::At<DWORD*(__fastcall*)(DWORD* out, void* unused, const char* text, DWORD seed, int maxLength)>(0xC8BEB0);
inline const auto NewRuinMap = rfa::game::At<RuinMap*(__fastcall*)(void* pool, void* unused, int)>(0x512CE0);
inline const auto AddWeapon = rfa::game::At<int(__fastcall*)(RuinWeaponList* list, void* unused, const DWORD& weapon)>(0x49C480);

const char* KeepRuinString(const char* text) { return KeepString(kRuinStringPool, nullptr, text, 0, 0); }
DWORD RuinNameChecksum(const char* text) {
    DWORD checksum;
    return *NameChecksum(&checksum, nullptr, text, 0, -1);
}

// The game's own reader (0x50E690, cdecl: the table, and the DLC it came from or -1) stops at five
// maps, and after reading each it sets these by the map's place in the list, whatever the table
// says: the five maps the game was made for, in their table's order, with that same table's values.
// So a table with a new map would start the wrong level, and no more than five could be listed.
constexpr int kRuinShippedMaps = 5, kRuinMostMaps = 32;  // 32: what the map pool holds
const char* const kShippedRuinMaps[kRuinShippedMaps] = {"ruin01", "ruin08", "ruin06", "ruin07", "ruin03"};
constexpr float kShippedDamageToScore[kRuinShippedMaps] = {0.6f, 0.85f, 3.75f, 0.56f, 0.85f};
constexpr int kShippedRoundTimeMs = 60000, kShippedTimeBonusMs = 15000;

// One wc_map_list entry, read as the game reads it.
void ReadRuinMap(RuinMap* map, void* entry, int dlc) {
    map->dlc = dlc;
    map->lockedUntilEarned = dlc == -1;
    map->fileName = nullptr;
    map->fileNameChecksum = kNoChecksum;
    if (const char* text = EntryText(entry, "file_name")) {
        map->fileName = KeepRuinString(text);
        map->fileNameChecksum = RuinNameChecksum(map->fileName);
    }
    const char* text = EntryText(entry, "display_name");
    map->displayName = text ? KeepRuinString(text) : nullptr;
    text = EntryText(entry, "display_name_uppercase");
    map->displayNameUpper = text ? KeepRuinString(text) : nullptr;
    text = EntryText(entry, "image_name");
    map->imageName = text ? KeepRuinString(text) : nullptr;
    if (const char* time = EntryText(entry, "time_of_day")) ReadTimeOfDay(&map->timeOfDayHour, &map->timeOfDayMinute, time);
    const char* reservation = OptionalText(entry, "resource_reservation");
    map->resourceReservation = reservation ? FindResourceReservation(reservation) : nullptr;
    map->locked = true;
    ReadOptionalBool(map->locked, entry, "is_locked");
    ReadInt(map->roundTimeMs, entry, "total_chaos_round_time_ms");
    ReadFloat(map->timeBonusTarget, entry, "total_chaos_time_bonus_target");
    ReadInt(map->timeBonusTimeMs, entry, "total_chaos_time_bonus_time_ms");
    ReadInt(map->scoreboardIndex, entry, "scoreboard_index");

    void* par = RequiredChild(entry, "par_unlocks");
    ReadInt(map->parScore, par, "par_score");
    map->maxScoreStat = FindStat(EntryText(par, "max_score_stat"));
    ReadBool(map->unlocksExo, par, "unlocks_exo");
    const char* unlocks = OptionalText(par, "unlocks_map");
    map->unlocksMap = unlocks ? KeepRuinString(unlocks) : nullptr;
    map->unlocksWeapons.count = 0;
    void* weapons = RequiredChild(par, "unlocks_weapons");
    for (void* weapon = FirstChild(weapons, "weapon_info"); weapon; weapon = NextChild(weapons, weapon, "weapon_info"))
        AddWeapon(&map->unlocksWeapons, nullptr, RuinNameChecksum(static_cast<XmlElement*>(weapon)->text));

    map->ambientBank = 0;
    map->musicTrigger = kNoMusicTrigger;
    if (void* audio = RequiredChild(entry, "Audio")) {
        map->ambientBank = ReadSoundBank(audio, "Ambient_Bank_Name");
        if (const char* music = OptionalText(audio, "Music_Trigger_Name")) map->musicTrigger = RuinNameChecksum(music);
    }
    ReadFloat(map->damageToScore, entry, "Damage_To_Score_Multiplier");
    const char* ability = OptionalText(entry, "Nanoforge_Ability");
    map->nanoforgeAbility = ability ? FindNanoforgeAbility(ability) : 0;
}

// The loader's reader, in the game's reader's place: up to 32 maps, each started by its own
// file_name. For the first five places it keeps what the game sets by place (the round times, the
// scoreboard and the damage-to-score multiplier), so the game's five maps are as they always were.
// A map past the five is as its table gives it, and one from the base table is open from the start:
// the game opens each base-game map from the one before it (its unlocks_map, once that map's par
// score is beaten), which the five shipped maps don't do for a new one.
void __cdecl ReadRuinMaps(void* table, int dlc) {
    if (!table) return;
    std::string leftOut;
    for (void* entry = FirstEntry(table, "wc_map_list"); entry; entry = NextEntry(table, entry, "wc_map_list")) {
        const int place = g_ruinMapCount;
        RuinMap* map = place < kRuinMostMaps ? NewRuinMap(kRuinMapPool, nullptr, 0) : nullptr;
        if (!map) {
            const char* fileName = EntryText(entry, "file_name");
            leftOut += (leftOut.empty() ? "" : ", ") + std::string(fileName ? fileName : "(no file_name)");
            continue;
        }
        ReadRuinMap(map, entry, dlc);
        if (place < kRuinShippedMaps) {
            const char* shipped = kShippedRuinMaps[place];
            if (!map->fileName) {
                map->fileName = KeepRuinString(shipped);
                map->fileNameChecksum = RuinNameChecksum(map->fileName);
            } else if (_stricmp(map->fileName, shipped) != 0) {
                rfa::Log("Ruin map %d is %s, as its table says (the game would have started %s)", place, map->fileName, shipped);
            }
            map->roundTimeMs = kShippedRoundTimeMs;
            map->timeBonusTimeMs = kShippedTimeBonusMs;
            map->scoreboardIndex = place;
            map->damageToScore = kShippedDamageToScore[place];
        } else {
            if (dlc == -1) map->lockedUntilEarned = false;
            rfa::Log("Ruin map %d: %s%s", place, map->fileName ? map->fileName : "(no name)", dlc == -1 ? ", unlocked" : "");
        }
        g_ruinMaps[place] = map;
        ++g_ruinMapCount;
    }
    if (!leftOut.empty()) rfa::Log("Ruin has room for %d maps; left out of its list: %s", kRuinMostMaps, leftOut.c_str());
}

constexpr DWORD kReadRuinMaps = 0x50E690;

// With LogLookups=1, the Ruin map list is logged too when a Ruin match begins
// (rf4_wrecking_crew_begin_match → 0x50F400): the map chosen and every map's file name.
constexpr DWORD kBeginRuinMatch = 0x50F400;
constexpr size_t kBeginRuinMatchPrologue = 9;  // push ebp / mov ebp,esp / sub esp,110h
using BeginRuinMatchFn = void(__cdecl*)();
BeginRuinMatchFn g_beginRuinMatch;

void __cdecl BeginRuinMatchHook() {
    std::string names;
    for (int i = 0; i < g_ruinMapCount && i < 35; ++i) {
        const char* name = g_ruinMaps[i] ? g_ruinMaps[i]->fileName : nullptr;
        names += (i ? ", " : "") + std::to_string(i) + " " + (name ? name : "(none)");
    }
    rfa::Log("Ruin match beginning: map %d of %d; the game's list: %s", g_chosenRuinMap, g_ruinMapCount, names.c_str());
    g_beginRuinMatch();
}

bool __cdecl FindFileHook(FileFindResult* result, const char* name, bool flag) {
    const bool found = g_findFile(result, name, flag);
if (g_logLookups && name && IsLevelFile(name)) {
        const char* sourceStr = !found                 ? "not found"
                      : result->source == 1 ? "in an archive"
                      : result->source == 2 ? "in a virtual folder"
                      : result->source == 0 ? "loose file"
                                             : "found";
        rfa::Log("Level lookup: %s -> %s (%lu bytes)", name, sourceStr, found ? result->size : 0ul);
    }
    if (!found && name) return FindNewFile(result, name);
    if (!found || result->source != 1 || !name) return found;  // not found, or not inside an archive

    Override* o = nullptr;
    {
        SharedFiles shared;
        auto overrides = g_overrides.find(Lower(name));
        if (overrides == g_overrides.end() || IsLuaScript(overrides->first)) return found;
        auto match = std::find_if(overrides->second.begin(), overrides->second.end(),
                                  [&](const Override& candidate) { return candidate.archiveSize == result->size; });
        if (match == overrides->second.end()) {
            RFA_LOG_FIRST(20, "Override %s not used here: the game loaded another archive's %s (%lu bytes)",
                          overrides->second.front().relative.c_str(), name, result->size);
            return found;
        }
        o = &*match;
    }

    // Start the result again the way the game's FindFile does, then let the game's own loose-file
    // lookup fill it in for the replacement.
    const FileFindResult gameCopy = *result;
    result->source = -1;
    result->pathType = -1;
    result->name[0] = '\0';
    result->size = 0;
    result->archiveEntry = nullptr;
    const bool ok = rfa::game::FindLooseFile(result, o->ansiPath.c_str(), 0);
    if (InterlockedExchange(&o->used, 1) == 0)
        rfa::Log(ok ? "Using override %s" : "Override %s could not be opened; the game's own file is used",
                 o->relative.c_str());
    if (!ok) *result = gameCopy;
    return true;
}

void Widen(const char* text, wchar_t* out, int size) {
    if (!MultiByteToWideChar(CP_ACP, 0, text ? text : "", -1, out, size)) out[0] = L'\0';
}

// Lua: rfa_get_setting(section, key, default) -> number from rfa_mods.ini
int __cdecl LuaGetSetting(lua_State* L) {
    using namespace rfa::game;
    const double fallback = lua_gettop(L) >= 3 ? lua_tonumber(L, 3) : 0.0;
    wchar_t section[64], key[64];
    Widen(lua_tolstring(L, 1, nullptr), section, 64);
    Widen(lua_tolstring(L, 2, nullptr), key, 64);
    lua_pushnumber(L, rfa::IniInt(section, key, static_cast<int>(fallback)));
    return 1;
}

// Lua: rfa_set_setting(section, key, value) saves to rfa_mods.ini and tells every mod about it.
int __cdecl LuaSetSetting(lua_State* L) {
    using namespace rfa::game;
    const char* section = lua_tolstring(L, 1, nullptr);
    const char* key = lua_tolstring(L, 2, nullptr);
    if (!section || !key) return 0;
    const double number = lua_tonumber(L, 3);
    const int value = static_cast<int>(number + (number < 0 ? -0.5 : 0.5));
    wchar_t sectionW[64], keyW[64];
    Widen(section, sectionW, 64);
    Widen(key, keyW, 64);
    if (rfa::IniInt(sectionW, keyW, value + 1) == value) return 0;  // unchanged
    WritePrivateProfileStringW(sectionW, keyW, std::to_wstring(value).c_str(), rfa::IniPath().c_str());
    rfa::Log("Menu changed [%s] %s=%d", section, key, value);
    using SettingChangedFn = void(__cdecl*)(const char*, const char*);
    for (HMODULE mod : g_mods)
        if (auto changed = reinterpret_cast<SettingChangedFn>(GetProcAddress(mod, "RFAMod_SettingChanged")))
            changed(section, key);
    return 0;
}

// Lua: rfa_log(text) writes to logs\rfa_mods.log, handy when editing override scripts.
int __cdecl LuaLog(lua_State* L) {
    const char* text = rfa::game::lua_tolstring(L, 1, nullptr);
    rfa::Log("lua: %s", text ? text : "(nil)");
    return 0;
}

// Runs instead of the game's function that registers its C functions with the menu Lua state:
// lets it do that, then adds ours, then each mod's: a mod that exports
//     extern "C" __declspec(dllexport) void __cdecl RFAMod_RegisterLua(rfa::game::lua_State* L);
// adds its own functions there (rfa::game::lua_register), for its interface scripts to call.
void __cdecl RegisterLuaFunctions(lua_State* L) {
    using namespace rfa::game;
    g_registerGameFunctions(L);
    lua_register(L, "rfa_get_setting", LuaGetSetting);
    lua_register(L, "rfa_set_setting", LuaSetSetting);
    lua_register(L, "rfa_log", LuaLog);
    rfa::Log("Added rfa_get_setting, rfa_set_setting and rfa_log to the game's menu Lua");
    WatchLuaErrors();
    rfa::Log("Lua script errors go to this log (\"Lua error: ...\")");
    using RegisterLuaFn = void(__cdecl*)(lua_State*);
    for (HMODULE mod : g_mods)
        if (auto registerLua = reinterpret_cast<RegisterLuaFn>(GetProcAddress(mod, "RFAMod_RegisterLua"))) {
            registerLua(L);
            wchar_t file[MAX_PATH];
            GetModuleFileNameW(mod, file, MAX_PATH);
            rfa::Log("%ls added its functions to the game's menu Lua", wcsrchr(file, L'\\') ? wcsrchr(file, L'\\') + 1 : file);
        }
}

// Looks through overrides\ and edits\ again for files added since the game started (the editor asks
// for this before it has the game load a level again or reload a table, so a zone or table saved
// for the first time while the game runs is used at once). How many new files it saw. A file
// already known is read from the disk each time the game opens it, so a changed one needs no rescan.
bool g_overridesOn = false;
extern "C" __declspec(dllexport) int __cdecl RFALoader_RescanOverrides() {
    if (!g_overridesOn) return 0;
    AcquireSRWLockExclusive(&g_filesLock);
    const size_t before = g_seenFiles.size();
    FindOverrides(rfa::RootDir() + L"\\overrides", L"");
    FindEdits(rfa::RootDir() + L"\\edits", L"");
    const int added = static_cast<int>(g_seenFiles.size() - before);
    ReleaseSRWLockExclusive(&g_filesLock);
    if (added) rfa::Log("Overrides looked through again: %d new file(s)", added);
    return added;
}

void InstallOverrides() {
    using namespace rfa::game;
    // "ScriptOverrides" is the setting's old name, from when only Lua scripts could be overridden.
    if (!rfa::IniInt(L"Loader", L"Overrides", rfa::IniInt(L"Loader", L"ScriptOverrides", 1))) {
        rfa::Log("Overrides: off ([Loader] Overrides=0)");
        return;
    }
    g_overridesOn = true;
    FindOverrides(rfa::RootDir() + L"\\overrides", L"");
    FindEdits(rfa::RootDir() + L"\\edits", L"");  // changes to the archives the mod adds (the Guerrilla world's)
    g_logLookups = rfa::IniInt(L"Loader", L"LogLookups", 0) != 0;
    if (g_logLookups) {
        rfa::Log("Logging lookups of level files ([Loader] LogLookups=1)");
        g_beginRuinMatch = reinterpret_cast<BeginRuinMatchFn>(
            rfa::HookFunction(kBeginRuinMatch, kBeginRuinMatchPrologue, reinterpret_cast<void*>(&BeginRuinMatchHook)));
        if (!g_beginRuinMatch) rfa::Log("Could not hook the Ruin match start; its map list won't be logged");
    }
    g_loadBuffer = reinterpret_cast<LoadBufferFn>(
        rfa::HookFunction(kLuaLoadBuffer, kLuaLoadBufferPrologue, reinterpret_cast<void*>(&LoadBufferHook)));
    g_findFile = reinterpret_cast<FindFileFn>(
        rfa::HookFunction(kFindFile, kFindFilePrologue, reinterpret_cast<void*>(&FindFileHook)));
    if (rfa::ReplaceFunction(kReadRuinMaps, reinterpret_cast<const void*>(&ReadRuinMaps)))
        rfa::Log("Ruin takes up to %d maps from wrecking_crew_maps.xtbl, each started by its file_name", kRuinMostMaps);
    else
        rfa::Log("Could not replace the Ruin map reader; Ruin keeps its five maps, started by their place in its table");
    const auto newFiles = std::count_if(g_newFiles.begin(), g_newFiles.end(),
                                        [](const auto& file) { return file.first.find('\\') != std::string::npos; });
    rfa::Log("Overrides: %u in overrides\\, and %u new files; Lua scripts %s, other files %s",
             static_cast<unsigned>(g_overrideCount), static_cast<unsigned>(newFiles),
             g_loadBuffer ? "on" : "FAILED", g_findFile ? "on" : "FAILED");
}

void InstallLuaFunctions() {
    using namespace rfa::game;
    g_registerGameFunctions = reinterpret_cast<RegisterFn>(rfa::HookFunction(
        kRegisterGameLuaFunctions, kRegisterGameLuaFunctionsPrologue, reinterpret_cast<void*>(&RegisterLuaFunctions)));
    if (!g_registerGameFunctions) rfa::Log("Could not hook the game's Lua registration; rfa_* functions are off");
}

// Mods that were DLLs of their own and are now parts of another. An update copied over an older
// install leaves their old DLLs in mods\; while the DLL that holds them now is there too, the old
// ones are skipped, so their hooks don't go in twice.
struct MergedMod {
    const wchar_t* oldDll;
    const wchar_t* nowIn;
};
constexpr MergedMod kMergedMods[] = {
    {L"borderless.dll", L"rfa_qol.dll"},
    {L"console.dll", L"rfa_qol.dll"},
    {L"fov.dll", L"rfa_qol.dll"},
    {L"video_settings.dll", L"rfa_qol.dll"},
};

// The DLL among `files` that now holds the mod `file` was, or nullptr.
const wchar_t* MergedInto(const std::wstring& file, const std::vector<std::wstring>& files) {
    for (const auto& merged : kMergedMods) {
        if (_wcsicmp(file.c_str(), merged.oldDll) != 0) continue;
        for (const auto& other : files)
            if (_wcsicmp(other.c_str(), merged.nowIn) == 0) return merged.nowIn;
    }
    return nullptr;
}

}  // namespace

// Called by the launcher on a remote thread while the game's main thread is parked.
// Returns the number of mods loaded.
extern "C" DWORD WINAPI RFALoader_Init(LPVOID) {
    wchar_t exe[MAX_PATH];
    GetModuleFileNameW(nullptr, exe, MAX_PATH);
    rfa::Log("Loader running in %ls (pid %lu)", exe, GetCurrentProcessId());
    rfa::Log("Game build: %s", rfa::game::BuildDescription());
    InstallInstalledExePath();
    InstallCrashReporter();
    if (rfa::game::IsSupportedBuild()) {
        InstallOverrides();
        InstallLuaFunctions();
        InstallMenus(&g_mods);
    }

    const std::wstring dir = rfa::RootDir() + L"\\mods\\";
    std::vector<std::wstring> files;
    WIN32_FIND_DATAW fd;
    HANDLE find = FindFirstFileW((dir + L"*.dll").c_str(), &fd);
    if (find != INVALID_HANDLE_VALUE) {
        do {
            if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) files.push_back(fd.cFileName);
        } while (FindNextFileW(find, &fd));
        FindClose(find);
    }
    std::sort(files.begin(), files.end());

    DWORD loaded = 0;
    for (const auto& file : files) {
        const std::wstring name = file.substr(0, file.size() - 4);
        if (const wchar_t* nowIn = MergedInto(file, files)) {
            rfa::Log("Skipping %ls: an old copy of a mod that is now part of %ls (you can delete it)",
                     file.c_str(), nowIn);
            continue;
        }
        if (!rfa::IniInt(L"Mods", name.c_str(), 1)) {
            rfa::Log("Skipping %ls (disabled in [Mods])", file.c_str());
            continue;
        }
        HMODULE mod = LoadLibraryW((dir + file).c_str());
        if (!mod) {
            rfa::Log("FAILED to load %ls (error %lu)", file.c_str(), GetLastError());
            continue;
        }
        auto init = reinterpret_cast<void(__cdecl*)()>(GetProcAddress(mod, "RFAMod_Init"));
        if (init && !CallModInit(init)) {
            rfa::Log("%ls crashed in RFAMod_Init", file.c_str());
            continue;
        }
        rfa::Log("Loaded %ls", file.c_str());
        g_mods.push_back(mod);
        ++loaded;
    }
    return loaded;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
