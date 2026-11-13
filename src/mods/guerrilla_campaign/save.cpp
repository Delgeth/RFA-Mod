// The campaign's own save, an ini file next to the mod (Windows' profile functions read and write
// it as a plain file).
#define RFA_MOD_NAME "guerrilla_campaign"
#include "rfa_mod.h"
#include "save.h"
#include "campaign.h"
#include <stdio.h>
#include <stdlib.h>

namespace campaign::save {
namespace {

const std::wstring& File() {
    static const std::wstring file = ModFolder() + L"\\campaign_save.ini";
    return file;
}

std::wstring Wide(const char* text) { return std::wstring(text, text + strlen(text)); }

}  // namespace

int Int(const char* section, const char* key, int fallback) {
    return static_cast<int>(GetPrivateProfileIntW(Wide(section).c_str(), Wide(key).c_str(), fallback, File().c_str()));
}

float Number(const char* section, const char* key, float fallback) {
    wchar_t text[64];
    GetPrivateProfileStringW(Wide(section).c_str(), Wide(key).c_str(), L"", text, 64, File().c_str());
    return text[0] ? static_cast<float>(_wtof(text)) : fallback;
}

void SetInt(const char* section, const char* key, int value) {
    CreateDirectoryW(ModFolder().c_str(), nullptr);
    WritePrivateProfileStringW(Wide(section).c_str(), Wide(key).c_str(), std::to_wstring(value).c_str(), File().c_str());
}

void SetNumber(const char* section, const char* key, float value) {
    CreateDirectoryW(ModFolder().c_str(), nullptr);
    wchar_t text[64];
    swprintf_s(text, L"%.6g", value);
    WritePrivateProfileStringW(Wide(section).c_str(), Wide(key).c_str(), text, File().c_str());
}

std::string ForObject(const char* kind, unsigned handle) {
    char name[64];
    sprintf_s(name, "%s %08X", kind, handle);
    return name;
}

}  // namespace campaign::save
