// More Ruin (wrecking crew) maps than the game's five. A part of rfa_qol.dll. The editor's New level
// adds a map to wrecking_crew_maps.xtbl; the game, as shipped, takes only five of the table's rows and
// gives them the five maps' names whatever the table says.
//
// 0x50E690 reads the table's wc_map_list rows into the map list (0x12F39E8, how many at 0x12F3A74; the
// "WC mempool" behind it holds 32): it stops at five (`cmp [12F3A74h],5` at 0x50E789), and after reading
// each row it puts the shipped values back over it by its place in the list (0x50EBF6 to 0x50EC54: its
// file name from "ruin01", "ruin08", "ruin06", "ruin07", "ruin03", its round times 60000 and 15000 ms,
// its scoreboard as its place, its damage-to-score multiplier from five constants), which for a sixth
// row would read past those five. The table's own rows for the five maps hold those very values, so:
//   - the five becomes 32 (the list's own limit, checked just before at 0x50E77B);
//   - the shipped values aren't put back (a jump over them, to 0x50EC57): every row is as the table says.
// A row the game reads from its base table starts locked until the map before it unlocks it (+0x2D,
// cleared by its unlocks_map once that map's par score is beaten); a map past the five has no map
// unlocking it, so it is opened once the table is read (0x50E690 hooked).
#define RFA_MOD_NAME "ruin_maps"
#include "rfa_mod.h"
#include "rfa_qol.h"

namespace {

constexpr DWORD kReadMaps = 0x50E690;          // void __cdecl (xml node, int dlc), prologue push ebp / mov ebp,esp / sub esp,78h
constexpr DWORD kFiveAt = 0x50E78F;            // the 5 of `cmp dword ptr [12F3A74h],5`
constexpr DWORD kShippedValues = 0x50EBF6;     // where the shipped values are put back
constexpr DWORD kMaps = 0x12F39E8, kMapCount = 0x12F3A74;
constexpr int kShipped = 5, kMost = 32;

using ReadMapsFn = void(__cdecl*)(int node, int dlc);
ReadMapsFn g_readMaps;

void __cdecl ReadMaps(int node, int dlc) {
    g_readMaps(node, dlc);
    const int count = *reinterpret_cast<volatile int*>(kMapCount);
    for (int i = kShipped; i < count && i < kMost; ++i) {
        BYTE* map = *reinterpret_cast<BYTE**>(kMaps + i * 4);
        if (map) map[0x2D] = 0;
    }
}

bool Is(DWORD address, const BYTE* bytes, size_t size) { return memcmp(reinterpret_cast<const void*>(address), bytes, size) == 0; }

}  // namespace

void ruin_maps::Init() {
    static const BYTE five[] = {0x83, 0x3D, 0x74, 0x3A, 0x2F, 0x01, 0x05, 0x7C};
    static const BYTE shipped[] = {0x8B, 0x4D, 0xC4, 0x8B, 0x15, 0x74};
    static const BYTE prologue[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x78};
    if (!Is(kFiveAt - 6, five, sizeof(five)) || !Is(kShippedValues, shipped, sizeof(shipped)) || !Is(kReadMaps, prologue, sizeof(prologue))) {
        rfa::Log("Not the build this part knows (the Ruin map list's code differs); the game keeps its five Ruin maps");
        return;
    }
    const BYTE most = kMost;
    const BYTE jump[] = {0xEB, static_cast<BYTE>(0x50EC57 - (kShippedValues + 2))};  // jmp short 0x50EC57
    if (!rfa::PatchCode(reinterpret_cast<void*>(kFiveAt), &most, 1) || !rfa::PatchCode(reinterpret_cast<void*>(kShippedValues), jump, sizeof(jump))) {
        rfa::Log("Couldn't change the Ruin map list's code");
        return;
    }
    g_readMaps = reinterpret_cast<ReadMapsFn>(rfa::HookFunction(kReadMaps, sizeof(prologue), reinterpret_cast<void*>(&ReadMaps)));
    rfa::Log(g_readMaps ? "The Ruin map list takes up to %d maps from wrecking_crew_maps.xtbl, as the table gives them"
                        : "The Ruin map list takes up to %d maps (maps past the fifth start locked: the hook failed)", kMost);
}
