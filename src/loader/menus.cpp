// In-game menus for mods (see rfa_menu.h): the loader hosts Dear ImGui over the game's picture.
//
// Where it draws: the game makes its swap chain with a DXGI factory (it imports CreateDXGIFactory),
// so the loader wraps that import and, on the factory it returns, its CreateSwapChain (slot 10 of
// the factory's methods). On the swap chain the game gets, it wraps Present (slot 8), to draw the
// menus on the game's finished picture just before it is shown, and ResizeBuffers (slot 13), to let
// go of the picture before the game resizes it. A COM object's methods are a table of pointers
// shared by every object of its kind, so each is swapped once (as an import is), keeping the old
// pointer to call on.
//
// When: while the developer console is open (0x28FF1CA), when the game has already stopped the
// player's controls and stopped holding the mouse in the middle of the screen. The game hides
// Windows' cursor, so ImGui draws its own. The game reads its own keys and mouse with DirectInput,
// so the window's messages, which ImGui reads, are passed on to the game untouched; the console
// asks RFALoader_MenusWant whether the menus have the mouse or are taking text, and leaves them.
//
// The bar along the bottom of the screen holds each mod's menus (RFAMod_Menu with RFA_MENU_BAR),
// and the mods then draw their windows (RFA_MENU_WINDOWS). Window places are kept in menus.ini.
#define RFA_MOD_NAME "menus"
#include "rfa_mod.h"
#include "rfa_menu.h"
#include "menus.h"
#include "imgui_internal.h"
#include "backends/imgui_impl_dx11.h"
#include "backends/imgui_impl_win32.h"
#include <d3d11.h>
#include <dxgi.h>
#include <string>
#include <vector>

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND window, UINT message, WPARAM w, LPARAM l);

namespace {

auto& g_consoleOpen = *reinterpret_cast<const volatile BYTE*>(0x28FF1CA);

// The methods wrapped, by their place in the object's table of methods.
constexpr int kCreateSwapChainSlot = 10;  // IDXGIFactory::CreateSwapChain
constexpr int kPresentSlot = 8;           // IDXGISwapChain::Present
constexpr int kResizeBuffersSlot = 13;    // IDXGISwapChain::ResizeBuffers

using CreateFactoryFn = HRESULT(WINAPI*)(REFIID, void**);
using CreateSwapChainFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory*, IUnknown*, DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**);
using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using ResizeBuffersFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
using MenuFn = void(__cdecl*)(const RFAMenu*);

CreateFactoryFn g_createFactory;
CreateSwapChainFn g_createSwapChain;
PresentFn g_present;
ResizeBuffersFn g_resizeBuffers;

const std::vector<HMODULE>* g_mods;
std::vector<MenuFn> g_menus;  // each mod's RFAMod_Menu, found on the first frame drawn
bool g_menusFound = false;

IDXGISwapChain* g_chain = nullptr;  // the game's
ID3D11Device* g_device = nullptr;
ID3D11DeviceContext* g_context = nullptr;
HWND g_window = nullptr;
WNDPROC g_windowProc = nullptr;
ID3D11RenderTargetView* g_target = nullptr;  // the picture, to draw on
UINT g_pictureWidth = 0, g_pictureHeight = 0;

bool g_started = false;  // ImGui is set up
bool g_failed = false;   // and couldn't be
bool g_shown = false;    // the menus were drawn this frame
std::string g_iniPath;

// Swaps slot `slot` of `object`'s methods for `replacement`, keeping the old one in `original`.
template <class Fn>
bool WrapMethod(void* object, int slot, Fn replacement, Fn* original) {
    void** methods = *static_cast<void***>(object);
    if (methods[slot] == reinterpret_cast<void*>(replacement)) return true;
    void* old = methods[slot];
    if (!rfa::WritePointer(&methods[slot], reinterpret_cast<void*>(replacement))) return false;
    *original = reinterpret_cast<Fn>(old);
    return true;
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (g_shown && ImGui::GetCurrentContext()) ImGui_ImplWin32_WndProcHandler(window, message, w, l);
    return CallWindowProcW(g_windowProc, window, message, w, l);
}

std::string Utf8(const std::wstring& text) {
    int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string out(size > 0 ? size - 1 : 0, '\0');
    if (size > 1) WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, &out[0], size, nullptr, nullptr);
    return out;
}

bool MakeTarget() {
    ID3D11Texture2D* picture = nullptr;
    if (FAILED(g_chain->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&picture))) || !picture) return false;
    D3D11_TEXTURE2D_DESC desc;
    picture->GetDesc(&desc);
    g_pictureWidth = desc.Width;
    g_pictureHeight = desc.Height;
    const HRESULT made = g_device->CreateRenderTargetView(picture, nullptr, &g_target);
    picture->Release();
    return SUCCEEDED(made);
}

void LetGoOfTarget() {
    if (g_target) g_target->Release();
    g_target = nullptr;
}

bool Start() {
    if (!MakeTarget()) {
        rfa::Log("Could not reach the game's picture; no menus");
        return false;
    }
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    g_iniPath = Utf8(rfa::RootDir() + L"\\menus.ini");
    io.IniFilename = g_iniPath.c_str();
    io.LogFilename = nullptr;
    io.MouseDrawCursor = true;
    io.ConfigErrorRecoveryEnableAssert = false;  // a mod's mistake in its menus is put right, never a stop

    // Sized for the picture: as ImGui's own at 1080 lines, larger above.
    const float scale = g_pictureHeight > 1080 ? g_pictureHeight / 1080.0f : 1.0f;
    ImGui::StyleColorsDark();
    ImGuiStyle& style = ImGui::GetStyle();
    style.ScaleAllSizes(scale);
    style.FontSizeBase = 18.0f;
    style.FontScaleMain = scale;
    style.WindowRounding = 4.0f * scale;
    style.Colors[ImGuiCol_WindowBg].w = 0.92f;
    wchar_t windows[MAX_PATH];
    const UINT length = GetWindowsDirectoryW(windows, MAX_PATH);
    const std::wstring font = length && length < MAX_PATH ? std::wstring(windows) + L"\\Fonts\\segoeui.ttf" : L"";
    if (font.empty() || GetFileAttributesW(font.c_str()) == INVALID_FILE_ATTRIBUTES ||
        !io.Fonts->AddFontFromFileTTF(Utf8(font).c_str(), 18.0f))
        io.Fonts->AddFontDefault();

    if (!ImGui_ImplWin32_Init(g_window) || !ImGui_ImplDX11_Init(g_device, g_context)) {
        rfa::Log("Could not set up Dear ImGui on the game's window and device; no menus");
        return false;
    }
    g_windowProc = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(g_window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&WindowProc)));
    if (!g_windowProc) {
        rfa::Log("Could not read the game window's messages; no menus");
        return false;
    }
    rfa::Log("Dear ImGui %s set up on the game's picture (%ux%u)", IMGUI_VERSION, g_pictureWidth, g_pictureHeight);
    return true;
}

void FindMenus() {
    g_menusFound = true;
    if (!g_mods) return;
    for (HMODULE mod : *g_mods)
        if (auto menu = reinterpret_cast<MenuFn>(GetProcAddress(mod, "RFAMod_Menu"))) g_menus.push_back(menu);
    rfa::Log("%u mod(s) with menus", static_cast<unsigned>(g_menus.size()));
}

// Calls a mod's menu; a crash in it ends up in the log, and that mod's menus are off from then on.
bool CallMenu(MenuFn menu, const RFAMenu* info) {
    __try {
        menu(info);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// Calls each mod's menus. For their windows, what one leaves unfinished (a window not ended, ...) is
// put right after it, so the next starts clean; inside the bar, which is itself unfinished there,
// that is done once around the whole bar instead.
void CallMenus(int stage) {
    RFAMenu info = {};
    info.size = sizeof(info);
    info.imguiVersion = IMGUI_VERSION;
    info.context = ImGui::GetCurrentContext();
    ImGui::GetAllocatorFunctions(&info.alloc, &info.free, &info.allocData);
    info.stage = stage;
    for (MenuFn& menu : g_menus) {
        if (!menu) continue;
        ImGuiErrorRecoveryState state;
        if (stage == RFA_MENU_WINDOWS) ImGui::ErrorRecoveryStoreState(&state);
        if (!CallMenu(menu, &info)) {
            rfa::Log("A mod's menu crashed; its menus are off");
            menu = nullptr;
        }
        ImGui::SetCurrentContext(info.context);
        if (stage == RFA_MENU_WINDOWS) ImGui::ErrorRecoveryTryToRecoverState(&state);
    }
}

// The bar along the bottom of the screen, with each mod's menus.
void DrawBar() {
    ImGuiErrorRecoveryState state;
    ImGui::ErrorRecoveryStoreState(&state);
    const ImGuiViewport* view = ImGui::GetMainViewport();
    const float height = ImGui::GetFrameHeight();
    ImGui::SetNextWindowPos(ImVec2(view->Pos.x, view->Pos.y + view->Size.y - height));
    ImGui::SetNextWindowSize(ImVec2(view->Size.x, height));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0, 0));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 0.0f);
    ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, 0.0f);
    const ImGuiWindowFlags flags = ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoSavedSettings |
                                   ImGuiWindowFlags_MenuBar | ImGuiWindowFlags_NoBringToFrontOnFocus | ImGuiWindowFlags_NoFocusOnAppearing;
    if (ImGui::Begin("##RFA-Mod menus", nullptr, flags) && ImGui::BeginMenuBar()) {
        ImGui::TextDisabled("RFA-Mod");
        CallMenus(RFA_MENU_BAR);
        ImGui::EndMenuBar();
    }
    ImGui::End();
    ImGui::PopStyleVar(3);
    ImGui::ErrorRecoveryTryToRecoverState(&state);
}

void DrawMenus() {
    g_shown = false;
    if (!g_consoleOpen || g_failed) return;
    if (!g_started) {
        g_started = true;
        if (!Start()) {
            g_failed = true;
            return;
        }
    }
    if (!g_target && !MakeTarget()) return;
    if (!g_menusFound) FindMenus();

    ImGui_ImplDX11_NewFrame();
    ImGui_ImplWin32_NewFrame();
    ImGuiIO& io = ImGui::GetIO();
    if (io.DisplaySize.x > 0 && io.DisplaySize.y > 0)  // the picture may be drawn at another size than the window's
        io.DisplayFramebufferScale = ImVec2(g_pictureWidth / io.DisplaySize.x, g_pictureHeight / io.DisplaySize.y);
    ImGui::NewFrame();
    DrawBar();
    CallMenus(RFA_MENU_WINDOWS);
    ImGui::Render();

    ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT] = {};
    ID3D11DepthStencilView* depth = nullptr;
    g_context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, &depth);
    g_context->OMSetRenderTargets(1, &g_target, nullptr);
    ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    g_context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, depth);
    for (ID3D11RenderTargetView* target : targets)
        if (target) target->Release();
    if (depth) depth->Release();
    g_shown = true;
}

HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* chain, UINT sync, UINT flags) {
    if (chain == g_chain && !(flags & DXGI_PRESENT_TEST)) DrawMenus();
    return g_present(chain, sync, flags);
}

HRESULT STDMETHODCALLTYPE ResizeBuffers(IDXGISwapChain* chain, UINT count, UINT width, UINT height, DXGI_FORMAT format, UINT flags) {
    if (chain == g_chain) LetGoOfTarget();
    return g_resizeBuffers(chain, count, width, height, format, flags);
}

HRESULT STDMETHODCALLTYPE CreateSwapChain(IDXGIFactory* factory, IUnknown* device, DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** chain) {
    const HRESULT made = g_createSwapChain(factory, device, desc, chain);
    if (FAILED(made) || !chain || !*chain || !desc || !device) return made;
    if (g_chain) {  // the game made a new one (a display change): draw on that from now on
        if (desc->OutputWindow != g_window) return made;
        LetGoOfTarget();
        g_chain = *chain;
        return made;
    }
    ID3D11Device* d3d = nullptr;
    if (FAILED(device->QueryInterface(__uuidof(ID3D11Device), reinterpret_cast<void**>(&d3d))) || !d3d) return made;
    if (!WrapMethod(*chain, kPresentSlot, &Present, &g_present) ||
        !WrapMethod(*chain, kResizeBuffersSlot, &ResizeBuffers, &g_resizeBuffers)) {
        rfa::Log("Could not wrap the swap chain's Present; no menus");
        d3d->Release();
        return made;
    }
    g_device = d3d;
    g_device->GetImmediateContext(&g_context);
    g_chain = *chain;
    g_window = desc->OutputWindow;
    rfa::Log("The game's swap chain: %ux%u; menus will show while the console is open", desc->BufferDesc.Width, desc->BufferDesc.Height);
    return made;
}

HRESULT WINAPI CreateFactory(REFIID id, void** factory) {
    const HRESULT made = g_createFactory(id, factory);
    if (SUCCEEDED(made) && factory && *factory && !g_createSwapChain &&
        !WrapMethod(*factory, kCreateSwapChainSlot, &CreateSwapChain, &g_createSwapChain))
        rfa::Log("Could not wrap the DXGI factory's CreateSwapChain; no menus");
    return made;
}

}  // namespace

void InstallMenus(const std::vector<HMODULE>* mods) {
    g_mods = mods;
    if (!rfa::HookImport(GetModuleHandleW(nullptr), "dxgi.dll", "CreateDXGIFactory", &CreateFactory, &g_createFactory))
        rfa::Log("Could not hook the game's CreateDXGIFactory; no menus");
}

extern "C" __declspec(dllexport) int __cdecl RFALoader_MenusWant(int what) {
    if (!g_shown) return 0;
    const ImGuiIO& io = ImGui::GetIO();
    switch (what) {
        case RFA_MENUS_SHOWN: return 1;
        case RFA_MENUS_MOUSE: return io.WantCaptureMouse ? 1 : 0;
        case RFA_MENUS_KEYBOARD: return io.WantTextInput ? 1 : 0;
        default: return 0;
    }
}
