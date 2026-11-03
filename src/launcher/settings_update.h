// Brings your rfa_mods.ini and console.ini up to date when RFA-Mod is updated.
//
// A release ships both files twice: as the files themselves, for a new install, and as templates
// in defaults\, which an update replaces while you keep your own two files. Each time the launcher
// starts it compares your file with its template, and when the template has a setting or a section
// yours lacks, it rewrites yours from the template:
//   - the template's comments, order and new settings (with the template's values);
//   - your value for every setting you have, matched section by section, names in any case;
//   - settings of yours the template doesn't have stay in their section, after the template's own,
//     with the comment lines just above them; sections of yours it doesn't have go at the end.
// The file as it was is kept as <name>.bak, and logs\rfa_mods.log says what was added. A file that
// already has every setting is left alone, byte for byte, and a missing one is made from the
// template. The files are handled as bytes, so values in any encoding come through as they were.
#pragma once
#include "rfa_mod.h"
#include <algorithm>
#include <vector>

namespace settings {

// One line of an ini file.
struct Line {
    enum Kind { kBlank, kComment, kSection, kSetting };
    Kind kind = kBlank;
    std::string text;        // as written, without the line break
    std::string name;        // a section's name, or a setting's: before the =, or the whole line
    std::string value;       // a setting's value, after the =
    bool hasEquals = false;  // console.ini runs a command written without = with nothing after it
};

// A setting in your file, with the comment lines just above it.
struct Setting {
    std::string name, value;
    bool hasEquals = false;
    std::vector<std::string> lines;  // those comments, then the setting's own line
    bool placed = false;             // given its place in the new file
};

// A section in your file. The first one holds what comes before the first [section] line.
struct Section {
    std::string name;
    std::string header;               // its [name] line as written
    std::vector<std::string> banner;  // the comment lines just above it
    std::vector<Setting> settings;
};

// A section of the template: its lines, and where settings of yours that it doesn't have go.
struct TemplateSection {
    std::string name;
    size_t begin = 0, end = 0;  // from its [name] line (or the first line) to the next section's
    size_t keptAt = 0;          // after its last setting, before the blank line that follows
};

inline std::string Trim(const std::string& text) {
    const size_t start = text.find_first_not_of(" \t");
    if (start == std::string::npos) return "";
    return text.substr(start, text.find_last_not_of(" \t") - start + 1);
}

inline bool SameName(const std::string& a, const std::string& b) {
    return _stricmp(a.c_str(), b.c_str()) == 0;
}

// Comments start with ; (or #, which console.ini also takes).
inline std::vector<Line> Parse(const std::string& text) {
    std::vector<Line> lines;
    for (size_t at = 0; at < text.size();) {
        size_t end = text.find('\n', at);
        if (end == std::string::npos) end = text.size();
        Line line;
        line.text = text.substr(at, end - at);
        at = end + 1;
        if (!line.text.empty() && line.text.back() == '\r') line.text.pop_back();
        const std::string trimmed = Trim(line.text);
        if (trimmed.empty()) {
            line.kind = Line::kBlank;
        } else if (trimmed[0] == ';' || trimmed[0] == '#') {
            line.kind = Line::kComment;
        } else if (trimmed[0] == '[') {
            line.kind = Line::kSection;
            line.name = Trim(trimmed.substr(1, trimmed.find(']') - 1));
        } else {
            line.kind = Line::kSetting;
            const size_t equals = trimmed.find('=');
            line.hasEquals = equals != std::string::npos;
            line.name = Trim(trimmed.substr(0, equals));
            if (line.hasEquals) line.value = Trim(trimmed.substr(equals + 1));
        }
        lines.push_back(line);
    }
    return lines;
}

inline Section* FindSection(std::vector<Section>& sections, const std::string& name) {
    for (Section& section : sections)
        if (SameName(section.name, name)) return &section;
    return nullptr;
}

inline bool HasSetting(const Section* section, const std::string& name) {
    if (section)
        for (const Setting& setting : section->settings)
            if (SameName(setting.name, name)) return true;
    return false;
}

// Your first setting of that name in `section` that has no place in the new file yet.
inline Setting* Unplaced(Section* section, const std::string& name) {
    if (section)
        for (Setting& setting : section->settings)
            if (!setting.placed && SameName(setting.name, name)) return &setting;
    return nullptr;
}

// Your file's sections. A section written twice is one, as console.ini reads it.
inline std::vector<Section> YourSections(const std::vector<Line>& lines) {
    std::vector<Section> sections(1);
    size_t current = 0;
    std::vector<std::string> comments;  // since the last blank line, section or setting
    for (const Line& line : lines) {
        if (line.kind == Line::kBlank) {
            comments.clear();
        } else if (line.kind == Line::kComment) {
            comments.push_back(line.text);
        } else if (line.kind == Line::kSection) {
            if (Section* same = FindSection(sections, line.name)) {
                current = same - sections.data();
            } else {
                sections.push_back({line.name, line.text, comments, {}});
                current = sections.size() - 1;
            }
            comments.clear();
        } else {
            Setting setting{line.name, line.value, line.hasEquals, comments};
            setting.lines.push_back(line.text);
            sections[current].settings.push_back(setting);
            comments.clear();
        }
    }
    return sections;
}

inline std::vector<TemplateSection> TemplateSections(const std::vector<Line>& lines) {
    std::vector<TemplateSection> sections(1);
    for (size_t i = 0; i < lines.size(); ++i)
        if (lines[i].kind == Line::kSection) sections.push_back({lines[i].name, i});
    for (size_t s = 0; s < sections.size(); ++s) {
        TemplateSection& section = sections[s];
        section.end = s + 1 < sections.size() ? sections[s + 1].begin : lines.size();
        size_t last = section.begin;
        for (size_t i = section.begin; i < section.end; ++i)
            if (lines[i].kind == Line::kSetting) last = i;
        section.keptAt = std::min(last + 1, section.end);
        while (section.keptAt < section.end && lines[section.keptAt].kind != Line::kBlank) ++section.keptAt;
    }
    return sections;
}

// The template's line for a setting, with your value in it.
inline std::string Written(const Line& line, const Setting& yours) {
    if (!yours.hasEquals) return yours.lines.back();
    const size_t equals = line.text.find('=');
    return (equals == std::string::npos ? line.text + "=" : line.text.substr(0, equals + 1)) + yours.value;
}

// Your settings in `section` that have no place yet, as you wrote them, with their comments.
inline void AddKept(Section* section, std::vector<std::string>& out) {
    if (!section) return;
    for (Setting& setting : section->settings) {
        if (setting.placed) continue;
        out.insert(out.end(), setting.lines.begin(), setting.lines.end());
        setting.placed = true;
    }
}

// Your file rewritten from the template into `out`, and what that adds into `added` (a line for
// each section). False, with nothing written, when your file has every setting already.
inline bool Merge(const std::vector<Line>& lines, std::vector<Section>& yours, std::vector<std::string>& out,
                  std::vector<std::string>& added) {
    const std::vector<TemplateSection> sections = TemplateSections(lines);

    // Which of your settings each of the template's takes the value of, and what you don't have.
    std::vector<Setting*> yourSetting(lines.size(), nullptr);
    for (const TemplateSection& section : sections) {
        Section* mine = FindSection(yours, section.name);
        std::string missing;
        for (size_t i = section.begin; i < section.end; ++i) {
            if (lines[i].kind != Line::kSetting) continue;
            if (!HasSetting(mine, lines[i].name)) missing += (missing.empty() ? "" : ", ") + lines[i].name;
            yourSetting[i] = Unplaced(mine, lines[i].name);
            if (yourSetting[i]) yourSetting[i]->placed = true;
        }
        if (!mine) added.push_back("[" + section.name + "], a new section" + (missing.empty() ? "" : ": ") + missing);
        else if (!missing.empty()) added.push_back("[" + section.name + "] " + missing);
    }
    if (added.empty()) return false;

    for (const TemplateSection& section : sections) {
        Section* mine = FindSection(yours, section.name);
        for (size_t i = section.begin; i < section.end; ++i) {
            if (i == section.keptAt) AddKept(mine, out);
            out.push_back(yourSetting[i] ? Written(lines[i], *yourSetting[i]) : lines[i].text);
        }
        if (section.keptAt == section.end) AddKept(mine, out);
    }
    for (size_t s = 1; s < yours.size(); ++s) {
        Section& section = yours[s];
        bool inTemplate = false;
        for (const TemplateSection& other : sections) inTemplate = inTemplate || SameName(other.name, section.name);
        if (inTemplate) continue;
        if (!out.empty() && !Trim(out.back()).empty()) out.push_back("");
        out.insert(out.end(), section.banner.begin(), section.banner.end());
        out.push_back(section.header);
        AddKept(&section, out);
    }
    return true;
}

inline bool ReadText(const std::wstring& path, std::string& text) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || !file) return false;
    char buffer[4096];
    size_t read;
    while ((read = fread(buffer, 1, sizeof(buffer), file)) > 0) text.append(buffer, read);
    const bool ok = !ferror(file);
    fclose(file);
    return ok;
}

inline bool WriteText(const std::wstring& path, const std::string& text) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"wb") != 0 || !file) return false;
    const bool ok = fwrite(text.data(), 1, text.size(), file) == text.size();
    return fclose(file) == 0 && ok;
}

// Takes a UTF-8 byte order mark off the front, if there is one (Notepad can save with one).
// False for UTF-16, which isn't read.
inline bool TakeOffMark(std::string& text, bool& hadMark) {
    if (text.size() >= 2 && ((text[0] == '\xFF' && text[1] == '\xFE') || (text[0] == '\xFE' && text[1] == '\xFF')))
        return false;
    hadMark = text.compare(0, 3, "\xEF\xBB\xBF") == 0;
    if (hadMark) text.erase(0, 3);
    return true;
}

// Adds the settings the template in <folder>\defaults\ has to <folder>\<name>, as described at the top.
inline void Update(const std::wstring& folder, const wchar_t* name) {
    const std::wstring path = folder + L"\\" + name;
    const std::wstring templatePath = folder + L"\\defaults\\" + name;
    std::string templateText, yourText;
    if (!ReadText(templatePath, templateText)) {
        rfa::Log("There is no defaults\\%ls, so %ls was not checked for new settings", name, name);
        return;
    }
    if (GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) {
        if (CopyFileW(templatePath.c_str(), path.c_str(), TRUE)) rfa::Log("%ls was missing: made it from defaults\\%ls", name, name);
        else rfa::Log("ERROR: %ls is missing and could not be made from defaults\\%ls (error %lu)", name, name, GetLastError());
        return;
    }
    bool yourMark = false, templateMark = false;
    if (!ReadText(path, yourText)) {
        rfa::Log("ERROR: could not read %ls to check it for new settings", name);
        return;
    }
    if (!TakeOffMark(yourText, yourMark) || !TakeOffMark(templateText, templateMark)) {
        rfa::Log("%ls is saved as UTF-16, so it was not checked for new settings", name);
        return;
    }

    std::vector<Section> yours = YourSections(Parse(yourText));
    std::vector<std::string> lines, added;
    if (!Merge(Parse(templateText), yours, lines, added)) return;
    std::string text = yourMark ? "\xEF\xBB\xBF" : "";
    for (const std::string& line : lines) text += line + "\r\n";

    // Write the new file next to yours first, so yours is only replaced once all of it is there.
    const std::wstring fresh = path + L".new", backup = path + L".bak";
    if (!WriteText(fresh, text) || !CopyFileW(path.c_str(), backup.c_str(), FALSE) ||
        !MoveFileExW(fresh.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        rfa::Log("ERROR: could not add the new settings to %ls (error %lu); it is as it was", name, GetLastError());
        DeleteFileW(fresh.c_str());
        return;
    }
    rfa::Log("%ls: added this version's new settings from defaults\\%ls, keeping your values (the file as it "
             "was is %ls.bak):", name, name, name);
    for (const std::string& line : added) rfa::Log("  %s", line.c_str());
}

}  // namespace settings
