// The campaign's state for the other mods: the districts for guerrilla_hud's control and morale
// meters, and the whole state for guerrilla_save, so it goes with each game save.
//
// The state is the campaign's save (mods\guerrilla_campaign\campaign_save.ini: the districts' control,
// morale and liberation, the tech and progression levels, salvage, each demolitions master node's
// progress, the buildings already counted). Every change is written there as it happens, so a game
// save takes its bytes as they are, and loading one puts them back and reads them again.
#define RFA_MOD_NAME "guerrilla_campaign"
#include "rfa_mod.h"
#include "activities.h"
#include "activity_base.h"
#include "district_sources.h"
#include "liberation.h"
#include "save.h"
#include <string.h>

namespace {

// For the HUD: one district (Guerrilla's MoraleUpdate data item has these). Times are the game's
// clock in milliseconds (-1: never changed); changes counts up with each change of control or morale.
struct DistrictInfo {
    const char* name;  // Guerrilla's ("Dust"); stays valid
    int index;         // its district_index
    float control, controlMax, controlBefore;
    float morale, moraleMax, moraleBefore;
    float effectiveMorale;  // 0..1, what morale's effects read
    int liberated;
    int controlChangedAt, moraleChangedAt;
    unsigned changes;
};

}  // namespace

// The district (-1: the player's) for the HUD's meters: 1 and filled in, or 0 (none there).
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_District(int which, DistrictInfo* info) {
    using namespace campaign;
    if (!info) return 0;
    const int index = which < 0 ? liberation::PlayerDistrict() : which;
    const liberation::District* district = liberation::GetDistrict(index);
    if (!district) return 0;
    info->name = district->name.c_str();
    info->index = district->index;
    info->control = district->control;
    info->controlMax = district->controlMax;
    info->controlBefore = district->controlBefore;
    info->morale = district->morale;
    info->moraleMax = district->moraleMax;
    info->moraleBefore = district->moraleBefore;
    info->effectiveMorale = liberation::EffectiveMoralePercent(index);
    info->liberated = district->liberated ? 1 : 0;
    info->controlChangedAt = district->controlChangedAt;
    info->moraleChangedAt = district->moraleChangedAt;
    info->changes = district->changes;
    return 1;
}

// For guerrilla_save, when it makes a save of one of our worlds: the campaign's state into buffer (room
// bytes); the bytes written, or the bytes needed when buffer is null (or too small).
extern "C" __declspec(dllexport) int __cdecl GuerrillaCampaign_SaveState(unsigned char* buffer, int room) {
    const std::string bytes = campaign::save::Bytes();
    const int size = static_cast<int>(bytes.size());
    if (!buffer || room < size) return size;
    memcpy(buffer, bytes.data(), bytes.size());
    rfa::Log("Campaign state: %d bytes given to the game's save", size);
    return size;
}

// For guerrilla_save, when a save of one of our worlds is opened: the state it carried (null, 0: a fresh
// world, back to Guerrilla's starting values), read again by every system.
extern "C" __declspec(dllexport) void __cdecl GuerrillaCampaign_LoadState(const unsigned char* data, int size) {
    using namespace campaign;
    save::Replace(data, size);
    liberation::ReloadState();
    activities::ReloadProgress();
    district_sources::ReloadState();
    rfa::Log(data && size > 0 ? "Campaign state: %d bytes from the game's save" : "Campaign state: a fresh campaign (%d bytes)", size);
}
