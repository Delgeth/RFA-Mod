# How RFA-Mod works

This page is for people working on RFA-Mod's code: how the mod gets into the game, what each mod
changes inside it, and how the editor is put together. To install and use the mod, see the
[README](../README.md).

Every game address here is in the Steam **publicbeta** exe, `RedFactionArmageddon_DX11.exe` build
1932726 (July 2017). It is an internal "Release" build that still has the developer console. On
any other build the mods that touch game code check `rfa::game::IsSupportedBuild()`, log that it
isn't the publicbeta build, and leave the game alone.

## Contents

- [Getting into the game](#getting-into-the-game)
- [Project layout](#project-layout)
- [The loader](#the-loader)
- [The mods](#the-mods)
- [The Guerrilla builder](#the-guerrilla-builder)
- [The editor](#the-editor)
- [Writing a mod](#writing-a-mod)
- [Building, packaging and releasing](#building-packaging-and-releasing)
- [Checking the game files are untouched](#checking-the-game-files-are-untouched)

## Getting into the game

1. `RFAModLauncher.exe` starts `RedFactionArmageddon_DX11.exe -packfiles` suspended, holds it at
   its very first instruction and loads `rfa_loader.dll` into it. Keep `-packfiles`: the
   official launcher (`rf4_launcher.exe`) passes it, and without it the engine expects loose dev
   files instead of the shipped `.vpp_pc` archives, and its first frames fail with
   `DXGI_ERROR_DEVICE_REMOVED`.
2. The loader loads every DLL in `mods\` (alphabetically, by file name) and calls its
   `RFAMod_Init` export.
3. The game is released. Mods hook Windows functions the game imports, and send some of the
   game's own functions to plain C++ replacements in the mod DLLs.

The game folder is `GameDir=` from `rfa_mods.ini`, or the launcher's own folder when the mod was
copied into the game folder, or (with Steam's launch options `"<launcher>" %command%`) the folder
of the exe Steam passes on the command line. The launcher sets `SteamAppId` / `SteamGameId` (55110)
when they aren't set, since without an app id the game asks Steam to restart it, which would drop
the mods. It never looks anything up in Steam or the registry. With `WaitForExit=1` it waits for
the game to exit, which keeps Steam's playtime and overlay right.

**4 GB of memory** (`LargeAddressAware=1`, the default): the game is a 32-bit program, and Windows
gives one 4 GB of memory to work in (on 64-bit Windows) only if its exe is marked "large address
aware", reading that mark as the process starts. The game's exe isn't marked (Guerrilla's are, in
both the Steam Edition and Re-Mars-tered). So the launcher keeps a copy of the exe in
`large_address\` in the mod folder with only that mark set, made again whenever the game's own exe
changes (the copy carries its last-write time), and starts the game from the copy, in the game
folder, with the game folder at the head of its `PATH` for the DLLs that sit beside the exe
(`steam_api.dll`, `binkw32.dll`). The game asks where its own exe is: `WinMain` first makes that
folder the current one, and the DLC search looks in its `build` folder. So the loader, before any
game code runs, answers the game's `GetModuleFileNameA/W` for its own module with the installed
exe (named by the launcher in `RFA_GAME_EXE`), and the game works from its own folder as usual. The
log says how much address space the game got. `LargeAddressAware=0` starts the game's own exe.

Nothing is tied to one PC: the launcher, loader, mods and editor find everything from where their
own files are and from `GameDir=`. `rfa_mods.ini` and `console.ini` in `bin\` are your own
settings; the templates others start from are in `config\`.

**Settings an update brings.** A release holds `rfa_mods.ini` and `console.ini` twice: as the files
themselves, for a new install, and in `defaults\`, which an update replaces while the player keeps
their own two files. Each time the launcher starts, before anything reads them, it compares each
file with its template (`src\launcher\settings_update.h`). When the template has a setting or a
section the file lacks, the file is rewritten from the template:
- the template's comments, order and new settings, with the template's values;
- the player's value for every setting they have, matched section by section, names in any case;
- settings of theirs the template doesn't have stay in their section, after the template's own,
  with the comment lines just above them; sections it doesn't have go at the end.

The file as it was is kept as `rfa_mods.ini.bak` (`console.ini.bak`), and the log names what was
added. A file that has every setting already is left alone, byte for byte, and a missing one is
made from the template. The files are handled as bytes, so values in any encoding (and a UTF-8
mark) come through as they were. A setting deleted from a file comes back with the template's value
the next time: in `rfa_mods.ini` that is the mod's own default anyway, but in `console.ini` it is
applied again, so change a command's value there rather than deleting it.

## Project layout

```
src\launcher\             RFAModLauncher.exe: updates your settings files, starts the game, injects the loader
src\loader\               rfa_loader.dll: loads mods, crash reports, overrides, Lua bridge
src\mods\<name>\          one folder per mod DLL (guerrilla_gear, guerrilla_hud, guerrilla_people,
                          guerrilla_vehicles, guerrilla_world, rfa_qol), and a mod's own scripts; rfa_qol\ has a source file for each of
                          its parts (borderless, console, fov, video_settings)
src\common\               rfa_mod.h (logging, ini, hooking helpers), rfa_game.h (build check, Lua API)
src\overrides\            replacements for files inside the game's archives, same layout as the game
src\editor\RfaFormats\    C# library that reads and writes the game's file formats (no UI)
src\editor\RfaEditor\     the editor (C#, WPF desktop app); built into bin\editor\
src\editor\GuerrillaBuilder\ GuerrillaBuilder.exe: makes Guerrilla's world, HUD, vehicles, weapons and people
                          usable, from your copy of Guerrilla; built into bin\editor\
config\rfa_mods.ini       the settings template (to bin\ when missing, always to bin\defaults\)
config\console.ini        console settings template (to bin\ when missing, always to bin\defaults\)
docs\editor-guide.md      how to use the editor
docs\how-it-works.md      this page
docs\editor-roadmap.md    what the editor still lacks, by Volition's own tools (CLOE, ...)
docs\renderer-plan.md     the plan for drawing levels with Direct3D 11, to look like the game
docs\releases\            each release's notes (the GitHub release text)
tools\game-integrity.ps1  proves the game folder is untouched
tools\package.ps1         makes a clean copy to share (dist\), without your own settings
reference\build-*\        each Steam build's file list, for tools\game-integrity.ps1
bin\                      the built mod (what you install); build\ holds compiler leftovers
dist\                     made by tools\package.ps1: the mod and editor as others get them
```

## The loader

Settings are in `rfa_mods.ini`. Everything the launcher, loader and mods do is written to
`logs\rfa_mods.log` (the launcher keeps the previous run as `rfa_mods.prev.log`). A mod is skipped
when its name is set to `0` under `[Mods]`.

**Merged mods**: `borderless.dll`, `console.dll`, `fov.dll` and `video_settings.dll` are now the
parts of `rfa_qol.dll` (see [rfa_qol](#rfa_qol)). Copying an update over an older install leaves
the old DLLs in `mods\`, so while `rfa_qol.dll` is there the loader skips them (`kMergedMods` in
`loader.cpp`) and logs that they can be deleted; otherwise every hook would go in twice.

**Crash reports** (`CrashReports=1` under `[Loader]`): if the game crashes, `logs\rfa_mods.log`
ends with a `CRASH:` block: the exception, the module+offset it happened in, the registers, and
likely callers found on the stack. A minidump (`logs\crash-<date>-<time>.dmp`) is written next to
it for deeper analysis.

**Overrides** (`Overrides=1`): `overrides\` mirrors the game folder, with a folder for each
archive, so `overrides\build\pc\cache\interface.vpp_pc\ui_options_gameplay.lua` replaces that
script from `<game>\build\pc\cache\interface.vpp_pc`. The archives themselves are untouched.
- At startup the loader checks that every override names a real file in a real archive and logs
  each one (or why it was skipped).
- Any file directly inside an archive can be overridden: Lua scripts are swapped when the game
  compiles them (`luaL_loadbuffer`, `0xD95730`), every other file when the game looks it up by
  name (`FindFile`, `0xC90960`), which then reads the replacement as a loose file.
- An override replaces only its own archive's copy (recognised by size; identical copies in other
  archives share it).
- Files inside `.str2_pc` containers can't be overridden one by one; the editor rebuilds the
  container instead.
- Inside an override, our changes are marked `[RFA]`; the rest is the game's original.
- See [src/overrides/README.md](../src/overrides/README.md) for the details.

**New files**: a file in `overrides\` outside any archive's folder, where the game folder has no
file (a new map's archive, `overrides\build\pc\cache\<map>.vpp_pc`), is handed to the game when it
looks for that path or name and finds nothing. The game mounts a level's archive the same way: it
finds the archive (`0x6CD1A0`, through `0xC92010`), then opens it with `cf_open` (`0xC91470` →
`0xC914E0`), both through `FindFile`; the mount itself (`0xCB5280` → `0xCB53C0`) reads the archive
from the file it was given.

**Ruin maps**: a new Ruin map is listed in an override of `wrecking_crew_maps.xtbl`. The game
itself starts Ruin maps by their place in that table (it reads at most five), whatever their
`file_name` says, so the loader puts each map's `file_name` back after the game reads the table
(`0x50E690`).

**LogLookups=1** under `[Loader]` logs every lookup of a level's files (zones, layers, archives,
containers, terrain), to see what a level loads. It also logs the Ruin map list when a Ruin match
begins (`0x50F400`).

**Lua bridge**: menu scripts can call `rfa_get_setting(section, key, default)`,
`rfa_set_setting(section, key, value)` and `rfa_log(text)`. After a change the loader calls
`RFAMod_SettingChanged(section, key)` in every mod that exports it. A mod can add Lua functions
of its own for its interface scripts: the loader calls its `RFAMod_RegisterLua(lua_State*)`
export, if it has one, right after adding its own (`rfa::game::lua_register` in `rfa_game.h`).

**In-game menus** (`menus.cpp`): the loader hosts [Dear ImGui](https://github.com/ocornut/imgui)
(`src\third_party\imgui`, built with `NDEBUG` so its checks never stop the game) on the game's own
Direct3D 11 device. The game makes its swap chain with a DXGI factory, so the loader wraps the
`CreateDXGIFactory` import and, on the factory, `CreateSwapChain` (slot 10 of its methods); on the
swap chain, `Present` (8), where it draws the menus on the finished picture, and `ResizeBuffers`
(13). It reads the game window's messages for ImGui by subclassing it. The menus are up while the
console is open (`0x28FF1CA`), when the game has already stopped the player's controls and freed the
mouse; ImGui draws its own cursor, as the game hides Windows'. The game reads its own input with
DirectInput, so the window's messages go on to it untouched, and the console asks
`RFALoader_MenusWant` whether the menus have the mouse or are taking text, and then leaves those
clicks and keys alone (it also hands the game's mouse messages on, so they don't reach the game
either). A bar along the bottom of the screen holds each mod's menus: every frame the loader calls
the `RFAMod_Menu(const RFAMenu*)` export of each mod that has one, first inside the bar, then for its
windows ([rfa_menu.h](../src/common/rfa_menu.h) has the rules for writing one). Whatever a mod leaves
unfinished is put right after it, and a crash in one turns that mod's menus off. Window places are
kept in `menus.ini`.

## The mods

### rfa_qol

`rfa_qol.dll` holds the quality-of-life mods below, `borderless`, `console`, `fov`,
`video_settings` and `spawner`, one source file each in `src\mods\rfa_qol\`. The first four were
DLLs of their own until v0.4.9-beta and work as they did then:
- each has its own settings section in `rfa_mods.ini`, and its own switch under `[Mods]`
  (`borderless=`, `console=`, `fov=`, `video_settings=`, and now `spawner=`), the names the loader
  used to read for their DLLs, so old settings files need no change. `rfa_qol.cpp`'s `RFAMod_Init`
  reads them and calls the `Init` of each part that is on (`rfa_qol.h`), in the order the DLLs
  loaded in. A crash in one part's `Init` is logged, and the others still start.
  `[Mods] rfa_qol=0` turns them all off;
- each logs under its own name. `RFA_MOD_NAME` is set per source file: `rfa::Log` sits in an
  unnamed namespace in `rfa_mod.h`, so every file has its own copy with its own name;
- `RFAMod_SettingChanged` is passed on to `fov`, the only part that listens, and `RFAMod_Menu` (the
  in-game menus) to `spawner`.

Its name sorts before `vehicle_spawner.dll`, a community mod that hooks the game's frame update
(`0x544CB0`), which `console` hooks too. `rfa::HookFunction` copies the function's first bytes as
they are, so it has to go first: the other hook, placed after it, chains on.

### spawner

**Spawn** in the in-game menus (see "In-game menus" under the loader): four windows, each with
ImGui's text filter. The menus are drawn on the thread that draws the game, so what a window asks
for is only noted there, and carried out on the game's thread from a hook of `drop_car`'s per-frame
step (`0x619360`, from the gameplay update).

- **Vehicles**: the game's own vehicle list (`0x148BD08`: entries of 0x880 bytes, `+0` the table
  name, `+4` the `Display_Name` that `drop_car` takes); Spawn or a double-click drops one (below).
- **Items**: the item table `drop_item` looks names up in (`0x7CA730`: `[0x1BB1D44]` its entries,
  `[0x1BB1D3C]` how many, 0x168 bytes each, the name first); Drop runs `drop_item <name>`.
- **Weapons**: the weapon table (`[0x18F8048]`, `[0x18F804C]`, 0xE0 bytes each: `+0` the name,
  `+0x18` its inventory item, whose name `give_player_item` takes). Turret and walker weapons share
  `generic_inventory_item`, which would only give the first of them, so those are greyed out.
- **Spawned**: the vehicle the game's create (`0x762A40`) makes inside `drop_car`'s step for one of
  our spawns is kept by its handle (object `+0x68`), eight at most, the oldest removed when a ninth
  comes. Remove takes one away by its handle (`0x6D56A0`, thiscall on the world `0x1870630`), so one
  already gone is passed over. It also shows the vehicle pool's free slots (`0x7612F0` on
  `0x1972900`, of the count at `+0x10`).

A spawned walker used to crash the game a few seconds later: every frame its per-human debug update
(`0x636440`, from `0x638CC0`) casts each human with `0x5FB5C0`, which returns 0 for a walker, and
reads the cast's `+0x1C`. The update only draws debug overlays, so the spawner leaves it out for an
object the cast turns down.

A vehicle has to have streamed in (`0x762290`) before the game makes one. Where a level's spawn
resource is in force, the game makes only the vehicles that resource holds and loads no other, so
`drop_car` alone waits its 3 seconds (`0x10FCA14`) and gives up. The resource is in force when the
pools' state `0x19734CC` isn't 0: on Wrecking Crew levels, which the Guerrilla world is made from,
and in multiplayer. So the spawner first adds the vehicle to that resource, the way the game adds a
resource's own vehicles when it starts (`0x761DC0`: category 8, owner 3, the vehicle; it makes room
only by letting go of a reservation nothing uses). It waits up to 30 seconds for the vehicle to
stream in, then runs `drop_car <display name>`. Where no resource is in force (the campaign),
`drop_car` loads the vehicle itself. Clearing the state instead makes the game dump every
reservation, freeing what the vehicles already in the world use, and the game crashes soon after.
The game makes no vehicle while you're in one (the player, `0x8B92D0()`, `+0x1160`: the vehicle's
handle, -1 on foot), so the window says so. This way of loading a vehicle, the item and weapon
lists, the eight tracked vehicles, the walkers' guard and the first spawner are Venom_Intellect's.

### borderless

When the game is set to Windowed, its window loses its frame and covers the whole monitor. While
the game is in its own exclusive fullscreen mode, the mod leaves it alone. Everything goes through
the game's import table (`USER32.dll`: `CreateWindowExW`, `SetWindowLong*`, `SetWindowPos`,
`ShowWindow`, `ClipCursor`, `SetCursorPos`, `GetForegroundWindow`, `IsIconic`, `GetAsyncKeyState`,
`RegisterRawInputDevices`, `SetWindowsHookExA`, ...; and `XInputGetState`). DXGI/D3D COM vtables are
deliberately not patched: the Steam overlay patches them too, and when the game rebuilt its swap
chain the two hooks ended up calling each other until the stack overflowed.

With `IgnoreFocusLoss=1` the game is never told it was alt-tabbed away from: focus messages are
hidden from it, its own low-level keyboard hook doesn't see Alt+Tab and friends, and the mod frees
the cursor (no clipping, no recentring) and mutes keyboard and controller input instead. It also
asks the game to open its pause menu, as if you had pressed the pause key (the request flag
`0x135B3F1`, whose own setter is `0x582760`; the HUD opens the menu on its next update unless a
cutscene or another menu is up, `0x57B774`). So switching away always pauses the game, as it does
when the game sees the switch itself.

The game normally blocks the Windows key system-wide while it runs; with `AllowWindowsKey=1` it
works in the game too. With `Enabled=0` every hook stays installed but only logs.

### fov

Every frame the active camera mode hands its camera to the game's "finalise the camera FOV"
function (`0x4D32D0`), which copies the camera's FOV (45 degrees vertical unless something like
aiming changes it) into the camera manager as the FOV the frame is drawn with. On 4:3 displays it
multiplies by 1.125 first (the console's `Camera_43_fov_modifier`). The mod replaces that function
with a few lines of C++ that multiply by FOV / 45 instead. Cutscene cameras don't use it, so
cutscenes keep their intended framing.

The vehicle and exo-suit camera mode (`0x4C5370`) doesn't use that function: it copies its
camera's FOV into the camera manager itself. With `Vehicles=1` the mod wraps that camera mode and
scales the FOV right after it runs. Both cap the result at 170: the engine has no limit of its
own, and at 180 or more the picture turns inside out.

**The FOV slider in Options → Gameplay** is an override of the game's own script,
[ui_options_gameplay.lua](../src/overrides/build/pc/cache/interface.vpp_pc/ui_options_gameplay.lua)
(every line we added or changed is marked `[RFA]`). It reads and saves `[FOV] VerticalFov=`
through the Lua bridge's `rfa_get_setting` / `rfa_set_setting`, and `fov` picks up the change
through `RFAMod_SettingChanged`. The game's slider code assumes every slider starts at 0, so the
override maps mouse clicks and drags itself.

**Options → Experimentals** is the same Gameplay screen with a list of RFA-Mod's own settings in
place of the game's. The main and pause menus (overrides of `ui_main_menu_pc.lua` and
`ui_pause_menu_pc.lua`, single player only) add EXPERIMENTALS to their Options lists; picking it
sets the global `Rfa_options_page` to `"experimentals"` and opens `ui_options_gameplay`, whose
script then shows its `Rfa_experimentals` list and leaves the game's own gameplay settings (and
Reset Profile) out. The menus' Lua refuses to write or read a global its scripts didn't declare as
they loaded (`system_lib.lua`), so the page is kept with `rawset(_UGGlobals, ...)` and read with
the game's own `_GetAnyGlobalSilent`. Each entry names its `rfa_mods.ini` section, key and default: a toggle
saves 1 or 0 (it reads as enabled for anything but 0), a slider its whole number. The Lua bridge
writes only whole numbers, so tuning values such as `Exposure=1.2` stay in the ini, and a setting
isn't rewritten unless you change it in the menu. The first entries are the Guerrilla world's live
switches (sun's shadows, sun shafts, fog, blowing dust, Guerrilla's particles and tone mapping,
colour correction, sky brightness, clock speed), which change as you pick them.

### video_settings

The game saves its video options (resolution, Windowed, quality settings) through its own
`ReadSetting`/`WriteSetting` functions (`0xCC6090`/`0xCC60C0`; their only callers are the video
settings load, `0x5A7E40`, and save, `0x5A7C50`), which store them under `HKEY_LOCAL_MACHINE`.
Windows refuses that without administrator rights, so every save failed and the game started at
1280x720 fullscreen each time. Both Steam builds have this bug. The mod replaces those two
functions with versions that use `video_settings.cfg` in the mod folder, a plain text file. The
game's registry code never runs for these settings, and nothing in the mod calls the registry.

### console

- *Drawing*: the mod replaces the game's console drawing (`0xC7B890`, called each frame from
  `0xC7D540`) with its own, made of the same calls: the rectangle and text functions the console
  was set up with (`0xA44640`, and the renderer's text method `0xD18A80`, which takes a scale the
  game always passes as 1), in the console's font (`ug-debug`, `0x110AAF0`). It draws a dark panel
  (the console's background colour, `0x290767C`, which `console_set_bg_color` sets; the mod only
  darkens the game's default 50% grey), a lighter strip with the typed line (`0x28FB640`) and a
  caret at its position (`0x28FF1D0`, blinking on the game's own 300 ms timer), a scroll bar, and
  the output lines, wrapped to the screen's width. The layout follows Quake 3's console
  (`code/client/cl_console.c` in id Software's GPL source; nothing of it is copied): a line along
  the edge, the version along the bottom right, a row of `^` marks while scrolled back, and a slide
  that takes a sixth of a second (the game's took about half a second, at 1000 pixels a second).
  Text sizes are `[Console]` `TextScale` and `InputScale`, times the screen's height ÷ 1080 on
  screens taller than that.
- *Build stamp*: a thin row just above the typed line, made once at startup from the exe itself: the
  game's version text (`v1.01`, as the pause menu shows it, at `0xF9A4F8`), the Steam build the
  publicbeta exe is (1932726), the exe's name in its debug information (`RF4_pc_release_dx11`, from
  its CodeView record) and its link time (the header's time stamp, in UTC, as C's `asctime` writes
  it); then RFA-Mod's version from a release's `version.txt`, or, in a build of your own, the day
  `console.cpp` was compiled. It is also written to `logs\rfa_mods.log`.
- *Notify lines*: with `Notify=1` the mod also keeps what the console prints while it is shut (the
  game keeps nothing then), and shows the newest lines printed in the last 3 seconds, 4 at most, at
  the top left with a shadow, as Quake 3 does.
- *History*: the game keeps the last 256 lines in a ring buffer (`0x28FF1F8`), each cut at 126
  characters, and only while the console is open. The mod wraps its print function (`0xC7C310`)
  and, when the game keeps a line, keeps it too, whole, in a list of its own (`HistoryLines`,
  10000 by default), and also writes it to `logs\console.log`. The echo of a typed command,
  `] <command>`, comes from `0xC7D140`, which the mod also wraps, so it can draw commands in their
  own colour. Lines with "error", "unknown command", "invalid" or "failed" in them are drawn red.
  The same line printed again straight after itself is counted rather than added, and shown once
  with `(x<count>)`.
- *The game's hidden output* (`GameLog=1`): the game's debug print (`0xCA9F10`), its failed checks
  (`<file>(<line>): V_ERROR: ...`, `0xC7B1D0` through `0xCA9DA0`) and one library's errors end in
  Windows' `OutputDebugStringA`, which only a debugger sees. The mod points the exe's import of it
  at its own function, which passes each call on and adds its lines to the console, marked `[game]`
  and drawn blue-grey; mods that hook those game functions themselves aren't disturbed. The error
  reports the game flashes on the screen (`0x545D40` → `0x545BD0`: `GENERAL-ERROR: ...`,
  `ART-WARNING: ...`) wait in a queue of 164 (`0x1308780`, 256 bytes each, the count at
  `0x13130D0`) until shown for a while and emptied; the mod reads the new ones each frame and adds
  them, `[game]` too, errors red and warnings yellow. Neither shows in the notify lines.
- *RFA-Mod's log* (`ModLog=1`): `logs\rfa_mods.log`, which the launcher starts afresh and the loader
  and every mod append to, is read ten times a second as it grows; each new line is added without
  its time, starting with the module's name (`[guerrilla_world] ...`), drawn teal.
- *Pasting*: Ctrl+V or Shift+Insert, taken out of the key queue like the scrolling keys. The
  clipboard's text (Unicode) is made into what the console can type: printable ASCII as it is, tabs
  and other spaces a space, typographic quotes, dashes and the ellipsis their ASCII forms, control
  and invisible characters dropped, anything else left out (and counted in a warning). Each line is
  trimmed, a leading `]` (a line copied from the console or `console.log`) dropped, and empty and
  comment lines (`;`, `#`, `//`) skipped. The first line goes in at the caret, as far as the typed
  line's 124 characters allow; the rest wait, shown on the right of the typed line. When Enter has
  run a line (the next time the key queue is read), the next waiting line is put in its place; Esc
  drops them. So a paste never runs anything until you press Enter.
- *Running a line*: Enter hands the typed line to the game's command runner (`0xC7BDB0`, which is
  printf-style) as its format, so a `%` in it was read as a format. The mod wraps the runner: the
  typed line is run as text, and other callers' formats are written out first (into the 256 bytes
  the game uses, no further).
- *Tab* (in place of the game's `0xC7D200`): the game matched the whole typed line against the
  command names, so with arguments typed it found nothing, and it listed matches by name alone. The
  mod's completes the first word: one match is completed with a space after it; several are listed
  with their descriptions (from the command list at `0x28FBA88`, as `help` prints them), and the
  line is lengthened as far as they all agree. With arguments typed the line stays as it is, and
  the console says what the command is (its description, and for a setting what it takes and that
  `? <name>` shows its value, the game's own status query).
- *Scrolling*: while the console is open the wheel scrolls its output, 3 lines per notch, and
  doesn't reach the game, so it no longer switches weapons behind the console (the mod wraps the
  game's mouse message handler, `0xCDF220`). The console's key handling (`0xC7C580`) reads keys
  from the game's queue (`0xCADA40`; a key is its scan code, plus `0x1000` with Shift, `0x2000`
  Alt, `0x4000` Ctrl, `0x8000` while `` ` `` is held). While it runs, the mod takes Page Up / Page
  Down (a page; with Shift, a line), Ctrl+Home and Ctrl+End out of the queue first, and End while
  scrolled back; everything else, Home and End included, goes on to the game.
- *The mouse*: while the console is open the game stops putting the cursor back in the middle of
  the screen (`0x4E4070`), but it hides Windows' cursor (`ShowCursor`), so the mod draws an arrow of
  its own over the console, where Windows says the cursor is (scaled from the window to the size
  the game draws at). The game's mouse message handler (`0xCDF220`, which the mod already wraps for
  the wheel) gets the button messages; the mod takes the presses on the console (and their
  releases), so neither the game nor a menu behind the console sees them. A press on the scroll
  bar drags it (on the track, the thumb first jumps there); a middle press starts autoscrolling,
  `0.2 × d + 0.002 × d²` lines a second for `d` pixels from where it was pressed (beyond the first
  10), ending when the button is let go after a move, or with the next press after a quick click;
  a press on the typed line moves the caret (`0x28FF1D0`). The thumb also lets go when the button
  is found up (released outside the window sends no message).
- *The render debug panel* (`` ` `` + X, debug display mode 4): the panel reads the mouse's
  position on screen, which the game keeps locked in the middle while you play and frees only
  while the console is open (`0x4E4070`), so it is used with the console open. The panel is drawn
  (`0x76EA00`) during the world's draw, before the 2D pass that draws the console (`0x5227C0`),
  so the console used to cover it. While the panel is up (`0x19742BC`) the mod draws the console
  just before the panel instead, so the panel is on top, and moves the console to the bottom of
  the screen, below the lowest of the panel's windows (the children of its root, `0x19750F8`, each
  with its rectangle on screen).
- *`console.ini`*: `[Startup]` is applied once when the game has finished starting (right after
  the game's own `gamestart.txt`); `[Gameplay]` every time gameplay starts after loading a level,
  save or checkpoint (right after the game's own per-level scripts, `0x540840`). The mod looks each
  name up in the game's command list to see what it is: on/off settings take `1` / `0` (sent as
  the console's `on` / `off`), number settings take the number, commands take their argument (`1`
  / `0` also mean on / off) or nothing to just run them. Because values say what a setting should
  be, applying them again after every load is safe. Each one is logged, and unknown names are
  skipped with a message. The template is `config\console.ini`, copied only when `bin\console.ini`
  doesn't exist, so your edits are kept; the launcher adds settings new in the template to yours
  (see [Getting into the game](#getting-into-the-game)).
- *The level editor's link*: every frame (from the game's frame update, `0x544CB0`) the mod puts
  what the game shows into a small block of shared memory the editor reads while it follows the
  game (the object picked in the game, the camera's position, direction and field of view). While
  the folder `link\` exists (the level editor makes it), it also runs the console commands the
  editor puts in `link\to_game.txt`, checked ten times a second.

### guerrilla_world

What a world ported from Red Faction: Guerrilla needs that Armageddon doesn't do itself (see
`[GuerrillaWorld]` in the ini). Everything below happens only in the worlds named by `Worlds=`.

- **The sky**: a dome coloured by the time of day's nine sky colours (four bands up from the
  horizon on the west side, four on the east, and the zenith), worked out the way Guerrilla's sky
  shader does it. Armageddon reads those colours into every time-of-day set but never draws such a
  dome for a world; its scene renderer's sky step (`0xD528E0`) only draws the unlockables viewer's
  background. The mod wraps that step and draws the sky after it: one triangle over the whole
  screen at the far plane, behind whatever the world has drawn, with the game's own Direct3D device
  (noted when the game makes it) and its own small shader, compiled when first needed. Everything
  it changes on the device is put back. It draws nothing in Armageddon's own worlds.
  `Brightness=` sets how bright the sky is; the mod reads it again whenever `rfa_mods.ini` is
  saved, so it can be tuned while playing.
- **The clouds** (`Clouds=`) follow Guerrilla's cloud shader: Guerrilla's cloud model (an overhead
  dome and a ring along the horizon), two textures of two cloud layers each, the layers'
  strengths, light colours, rim light and drift speeds from the time of day. In front of them, a
  ring of far mountains: Guerrilla's backdrop, tinted by the time of day's mountain and haze
  colours as its backdrop shader does. The model and textures (the backdrop's too) are files in
  `mods\guerrilla_world\`, made from your own copy of Guerrilla when the ported world is built
  (they are never shipped); without them the sky is drawn alone.
- **The clock**: Armageddon still has Guerrilla's game clock, and the time of day still follows
  it, but the game sets its speed to 0 at start-up, so the day never moves. The mod runs it at
  `ClockSpeed=` game seconds a real second (48 by default: a day in half an hour, also read again
  when `rfa_mods.ini` is saved) and gives the clock its old speed back in any other world.
- **The view distance**: the game keeps a world's zones loaded only within a radius of the player,
  and each zone's far-away ground comes with it; Armageddon's radius covers little more than the
  zones next to you (its levels are walled canyons), so in an open world the ground stops not far
  off. The game streams zones and their high-detail sub-zones on two grids, each tracking only
  about 35 containers at once. The mod wraps the game's radius setter (`0x6B3100`) and widens the zones' radius to `StreamRadius=`
  metres (500 by default, the 3 × 3 zones around you as in Guerrilla; at most just under two
  zones, 5 × 5; also read again when `rfa_mods.ini` is saved) and puts the game's own back
  elsewhere.
- **The light**: Armageddon lights surfaces only with lamps (it draws them into a light buffer
  that each surface is then multiplied by); the time of day's ambient light never reaches the
  ground, so a ported world without lamps is dark. The mod adds light into that buffer right after
  the game's light pass (`Light=`; the pass is `0xEF8BB0`): the sky's light, the time of day's
  ambient from above and below blended by which way each surface faces (times `Ambient=`), and the
  sun's light along its path over the day, by night the moon's, from the time of day's light
  colour (times `Sun=`). Both are read again when `rfa_mods.ini` is saved. The clouds are lit from
  the same direction.
- **The sun's shadows** (`SunShadows=`): Armageddon's renderer has no light that shines from one
  direction over the whole map, only lamps, but it renders a lamp's shadow map, and it keeps a lamp
  for a sun's shadow: the "follow player light" (`lights.xtbl`'s `FollowPlayerLight`, a spot lamp
  with an orthographic shadow map only), which an environment box's `follow_player_light` switches
  on. The game makes it and puts it over the camera every frame, shining straight down (`0x8B8C30`,
  `0x7E8A80`); its outdoor maps' lightning lamps borrow its shadow map. In our worlds the mod
  switches it on (choosing its template as such a box does), puts it along the sun's direction
  instead, aimed back at the camera, and has it drawn rather than its map only, with next to no
  light of its own. The light pass renders its shadow map into the shared map and draws it
  (`0xEF67D0`); right after, while the map and the lamp's constants (its shadow projection) are
  still bound, the mod draws the sun's light through them, with the game's own shadow test: the
  pixel's place, moved out a little along its normal (more the more it slants from the sun, with only
  a texel or two of depth held back, so a surface doesn't shadow itself through the map's wide texels
  yet a thin overhang such as a roof's edge still shadows what's under it), taken into the map and
  compared with it over a square three texels across, each texel counted by how much of it the
  square covers, so the edge slides smoothly from texel to texel. The map's size is the game's `ShadowDetail` video setting (4096 across by default). The
  sky's light is added after the pass as before.
- **The sun's shine**: a surface's shine comes from the light buffer's alpha, which only lamps
  write, so Guerrilla's specular maps showed nothing. The mod's sun writes it as Armageddon's
  lamps do: the sun reflected off the surface toward the eye, to the power of the surface's
  sharpness (kept with its normal), times how much it faces the sun. Guerrilla's sharpness is a
  half-way-vector exponent and Armageddon's a reflection one (× 5.12), about a quarter of it for
  the same highlight, so the world builder writes Guerrilla's `Specular_Power` / 20.48. The ground's
  shader ignores the shine.
- **The fog**: Armageddon's fog is each environment box's, by distance only; it keeps the time of
  day's fog curves but never draws with them. Guerrilla's is height fog, thicker low down and far
  away. The world builder writes Guerrilla's fog curves into each district's set (colour, density,
  fall-off with height, ground, offset) and moves the zones' boxes' own fog out of reach. The mod
  then draws Guerrilla's fog over what the world has drawn, as the particles begin (Armageddon's sky
  step draws into a multisampled layer of its own, laid over the scene only where nothing was drawn,
  so fog drawn there would show on the sky alone): each pixel's distance and height from the
  G-buffer's depth (noted as the sun's lamp is drawn in the light pass), and the fog worked out there
  as Guerrilla's shaders do per vertex, `1 − 2^−(term × distance × density − offset)` with
  `term = (1 − e^−k·dy)/(k·dy)`. As in Guerrilla's world, the fog's ground follows the camera (131 m
  below it), so the density at the camera stays the same. `Fog=` scales it, live. Guerrilla fogs its
  sky the same way, and so does the mod: the dome, the clouds and the far mountains each stand in for
  a surface stretched across to Guerrilla's far clip (1,000 m) and up to the time of day's fog scale,
  raised by its fog offset (`fog_skybox_`, `fog_clouds_` and `fog_matte_` offset and scale), less the
  camera's height; the sky's colour is mixed toward the fog's there. That is what turns the sky near
  the horizon into the district's haze. The clouds also get the time of day's ambient light, as
  Guerrilla's cloud shader adds it.
- **The tone mapping** (`ToneMapping=`): Armageddon turns its picture into what's shown with a
  fixed exposure held between its time of day's limits, a curve that glares on bright light, and
  its colour grading. In our worlds the mod holds Armageddon's exposure at 1 (wrapping the call
  that sets its constants, `0xC45130`) and, after Armageddon's last pass (`0xC46C10` / `0xC471C0`),
  draws Guerrilla's own from the scene it read. Guerrilla's scene holds the picture ÷ 8 (its lights,
  sky and fog colours as the time of day gives them) and every pass that reads it takes it × 8; ours
  holds the same values, so the mod's passes take it × 8 too. The eye's brightness `L` is measured on
  a grid of the scene (36 × its luminance) and eased toward at the district's `iris_rate`; the exposure
  `E = range / max(1 + offset × (base + amount × (L − base)), 0.1)` from the time of day's
  `tonemap_lum_range`, `tonemap_lum_offset`, `eye_adaption_base` and `eye_adaption_amount`, which
  the world builder writes; then `1 − (1 − particles) × e^(−E × (8 × colour × (1 − cover) + bloom +
  8 × sun shafts))` (the particles below; the bloom is made from the scene × 8) and the sRGB curve. `Exposure=` scales `E` (1 is Guerrilla's). The sky's brightness setting is a fifth under
  it, since Guerrilla's sky goes through the same exposure.
- **The particles** (`Particles=`, with the tone mapping): Guerrilla draws its particles into a
  buffer of their own (their colour, and in its alpha how much they cover) and lays it over the picture
  only in its last tone mapping pass, as above: particles keep the colour they're drawn in, dim only
  what's behind them, and never feed the bloom or the eye's brightness. Armageddon draws them into the
  lit scene before its bloom, where Guerrilla's colours burn out (an EDF screen's blue glow added to
  its lit screen went white and bloomed). Armageddon draws them all in one phase (`0xC35FC0`: the
  half-res ones, laid in at the end of `0xC35DD0`, then the full-resolution ones, `0xC35040`), into
  the scene with premultiplied blending, all four channels written, before a last step for the heat
  distortion (`0xC35CF0`). In our worlds the mod copies the scene aside and clears it as the phase
  starts, so the phase leaves exactly Guerrilla's particle buffer in it; before the last step it
  copies that into a layer of its own and puts the scene back, so the distortion, the bloom and the
  eye see no particles; the tone mapping lays the layer over the picture. `Particles=0` leaves them
  to Armageddon.
- **The particles' fog** (`ParticleFog=`): Guerrilla fogs every particle with the same height fog
  (its particle vertex shaders), mixes it toward the colour a surface lost in the fog shows on the
  screen (`1 − e^(−8 × E × the fog colour)`, worked out once a frame), and fades it out by the same
  amount, so a particle deep in the haze is gone. Armageddon's particle shaders fog only by
  distance, toward the boxes' fog colour, and never fade. In our worlds the mod wraps the particle
  renderer's draw of each effect (`0xC25030`): it works out Guerrilla's fog at the effect's
  particles (the mean over up to 32 of them), draws the effect with Armageddon's fog set to give
  every vertex that amount toward Guerrilla's colour, and its opacity times one less it; coronas
  (`0xC33820`) the same. The heat haze is left alone.
- **The dust** (`Dust=`): Guerrilla blows dust round the camera with a system of its own, run every
  frame, which Armageddon has dropped (its start-up function is empty) while keeping what it rests on:
  the weather's wind (`0x6FB620`: speed in mph and direction; started halfway between the zone's
  `wind_min_speed` and `wind_max_speed`, 50 and 80 in all of Guerrilla's zones), the effects' wind and
  their expression variables (set for every effect by `0xB63140`), the effect player (`0x4EB460`) and
  the world's raycast (`0x7993C0`). In our worlds the mod runs Guerrilla's after the game's effects
  update (`0x4F8E10`). Each frame it casts one of 26 rays round the camera, 50 m each, and counts its
  direction blocked if it hits the world within 20 m. Guerrilla's one ray of the world is two in
  Armageddon: one filter (`[0x1975F00]`) sees the terrain but goes through buildings, another
  (`[0x1975EF0]`) sees the buildings, so a direction is cast with the second when the first misses.
  The share of the sky blocked, each direction weighed as Guerrilla weighs them (up the most), dims
  the effects' variable `wind_los`: the wind's speed in m/s times the share left open, so none
  indoors. Weather.xtbl's camera effect, `env_dust_camera_a` (the world builder adds it to every
  zone), plays at the camera, turned as the effects' wind blows and moved upwind by up to a quarter
  of its radius as the wind nears its top speed. Its expressions fade its haze, dust and grit in as
  `wind_los` grows from 0 to 45. Guerrilla's gusts along the ground round the player (each ground
  material's own) and its fading of the dust right beside buildings aren't there yet.
- **The wind**: Armageddon blows its wind from its environment boxes. Each frame the world update
  blends the boxes' settings at the camera (`0x51BD30`) and applies them (`0x51D960`), and their
  weather part (on at +0x150: the wind's speed and direction, a weather value and the weather camera
  effects) is on even with no box about, with a wind of 0 mph. Guerrilla has no such boxes; its
  weather wanders the wind itself (`weather_wind_update`): every 20 s a new speed at most 40 mph
  from the present one, within the zone's `wind_min_speed` and `wind_max_speed`, reached over 10 s.
  In our worlds the mod leaves the boxes' weather out (it clears +0x150 in the blend before
  `0x51D960` applies it) and runs Guerrilla's wind before the effects update, setting it with
  `0x6FB650`; it starts again whenever the game starts its wind (`0x6FB390`, also the `wind`
  command), and stays still while the game's wind is off.
- **Particles' opacity scale**: Guerrilla's effects can fade an emitter's particles by a variable
  (`clerp_opacity_scale`: the camera dust's haze layers by `wind_los`, each from its own threshold),
  and its renderer multiplies the emitter's colour keys' opacity by that scale each frame. Armageddon
  keeps the function but not the multiplying (none of its own effects use it). So for the effects the
  world builder adds, the mod works each emitter's scale out from its expressions on `wind_los` each
  frame, as Guerrilla does, and gives its colour keys their opacity times it. Expressions on each
  object's own variables (a vehicle's speed, an engine's smoke) aren't handled yet.
- **The renderer's warnings**: when one of the renderer's pools runs out (GPU visibility queries,
  decals, render calls, ...) it sets a bit in a word (the renderer at `0x11BA7F0`, +0x60, then +4)
  and shows a "RENDER_LIB WARNINGS" box with a line a bit, two of them with no text. The box only
  flashes on the screen, so the mod names each bit in the log the first time it appears in our
  worlds.
- **The sun** (`SunShafts=`): Armageddon draws no sun in our worlds. Guerrilla's sun is an orbital of
  the world's global time of day (`terr01.gtodx`, which the world builder writes as `rfg.gtodx`): a
  texture, a size, a colour and an opacity, each a curve over the day, along the sun's path (the same
  path the mod lights the world from). The mod draws it at the sky step, before the clouds, as
  Guerrilla's skybox does: a square facing the camera one unit toward the sun, as many units across
  as twice its scale ÷ 100 (about half a degree), its texture's alpha times the colour (times its
  brightness, 32) and the opacity, added to the sky. With the tone mapping it draws Guerrilla's sun
  shafts, from Guerrilla's own shaders, at half the picture's size: the sky round the sun (where the
  G-buffer's depth is at the far plane) under a round fall-off, a small Gaussian blur, then 16
  samples from each pixel toward the sun, nearer ones counting more, weighted by the distance from
  the sun across the screen; added to the picture with the bloom. The settings are Re-Mars-tered's
  starting values (sun size 300, radius 5, fall-off curve −4, 1.6, −0.02); where Guerrilla lays a
  texture of its own round the sun, a smooth disc stands in.
- **The colour correction**: Guerrilla multiplies its whole picture by its time of day's tint
  (`color_correct_tint`, divided by its brightest channel); its light is pink and the tint bluish,
  so its colours need both. Armageddon doesn't colour its picture by that tint, so the mod
  multiplies the light it adds, the sky, the clouds and the far mountains by it
  (`ColourCorrection=`).
- **The zone size**: the ported world keeps Guerrilla's 511 m zones (`ZoneSize=`), so the game's
  16 × 16 grid of zones is Guerrilla's whole 8 km map. The game already knows that size (it gives
  it to worlds whose names start `mpl_`, left over from Guerrilla's multiplayer maps); the mod
  gives it to the worlds in `Worlds=` (wrapping `0x6C0D60`).
- **The physics world**: Armageddon's physics world reaches only 2295 m from the middle (nine of
  its own 255 m zones; the constants `0xFB5DC0` and `0xFB5DC4`), and anything beyond it drops out
  of the physics, so at start-up the mod widens it to nine of ours (4599 m). That is for every
  world, since the game makes it only once.
- **The precache**: when a level opens, the game mounts `<area>_precache.vpp_pc`, the area being
  the one its start zone's grid place has in `precache.xtbl` (`bio`, `ice`, ... or `world`),
  read once at start-up. The mod wraps that lookup (`0x6B4710`) and answers the world's own name
  in our worlds, so `rfg` mounts `rfg_precache.vpp_pc`.
- **The gameplay properties**: each destructible object names an entry of
  `gameplay_properties.xtbl` (its shadows, map icon, smoke when destroyed, anchoring, importance,
  team, salvage). The game reads that table once while it starts, from its own copy of
  `table.vpp_pc` in memory, which the loader's overrides don't reach (`0xC8E760` asks the copy,
  `0xC8FCD0`, before opening a table by name), and keeps the names in a 24 KB pool its own nearly
  fill (`0xC97AF0`, "Gameplay Property Names"; a full pool stops the game). When the world builder
  has written `mods\guerrilla_world\gameplay_properties.xtbl`, the mod hands the game that table
  at the copy's lookup and gives the names 64 KB of its own. Without the file, or with the mod
  off, the game keeps its own table, and a name it doesn't have is its Default entry. The draw
  distances (`lod_properties.xtbl`, read the same way) are handed over likewise, and so are the
  effects' three tables (`vfx.xtbl`, `effects.xtbl`, `level_objects.xtbl`, below), the vehicles'
  two (`vehicles.xtbl`, `vehicle_interactions.xtbl`) and the animation lists their animation sets
  need (`anim_set_filenames.xtbl`, `anim_files.xtbl`, `anim_states.xtbl`, `anim_actions.xtbl`), all
  written by the builder's vehicles (see [guerrilla_vehicles](#guerrilla_vehicles)).

The world itself is built by `editor\GuerrillaBuilder.exe` (see
[The Guerrilla builder](#the-guerrilla-builder); the code is in `src\editor\RfaFormats\Guerrilla\`), from the player's own copy of Guerrilla. It writes the world
`rfg` into `overrides\build\pc\cache\` and the sky's files into `mods\guerrilla_world\`. What it
converts:
- **The ground** of each of Guerrilla's 102 zones, sampled whole, with its own textures and cliff
  material, and far-away meshes. Guerrilla cuts its
  roads, and the ground around its rocks (skirts), out of the ground and lays them in as separate
  meshes in the same sub-zone files (`TerrainSubzone.Paving`); their heights are sampled with the
  ground's. The roads (road decals) are laid into our sub-zones as Armageddon lays its own
  (`GuerrillaRoads`: the mesh and Guerrilla's road textures, with a material written from code on
  Armageddon's road shader, `TerrainMaterials.Road`, checked against all 13 of the game's own road
  materials, and Guerrilla's values), and the ground is left out under them
  (`GuerrillaGround.RoadHoles`). Where a rock closes the ground
  itself (a cap on a pillar, a cover on a slope), the ground is laid half a metre under the rock's
  surface.
- **The terrain files**, written from code part by part as the game reads them (`TerrainFile`; each
  part checked against the game's own files): the ground's collision height field (Havok 2010,
  `TerrainHeightField.Section`), the ground's, cliffs' and far meshes' materials (`TerrainMaterials`:
  Guerrilla's textures, scales and tints, its cliff textures), the far-away meshes (`FarGround`),
  Guerrilla's ground effect map (sand, stone, gravel...) and its **invisible barriers**, the collision
  shapes nothing draws that keep the player in an area (`TerrainBarriers`: Guerrilla's Havok 5.5
  shapes written as 2010, as the chunks' are). Nothing of another level's terrain file is used.
- **The rocks** (Guerrilla's stitch pieces). A piece is the same kind of file as a destructible
  object's chunk file, so it is converted the same way (`GuerrillaChunks`, below): its own mesh,
  its material on Armageddon's shaders, and its collision mesh under a Havok MOPP tree written from
  nothing in Armageddon's layout (`PieceCollision`; checked against the game's own 930 stitch pieces
  of that layout). Each zone's stitch list places them, with a collision tree of ours over the
  zone's pieces. A zone without rocks gets an
  empty stitch list, with no tree, which the game's loader skips (Armageddon's own levels put a
  floor piece in such zones; Guerrilla has none).
- **The navigation meshes** the AI walks and drives on. Armageddon's `.navdata_pc` is Guerrilla's
  terrain pathfinding moved into a file of its own, so each of Guerrilla's 41 playable zones keeps
  its two meshes, the people's and the vehicles' (`NavigationFile`, `GuerrillaNavigation`: the
  vertices, the triangular cells with their neighbours and flags, the edges that meet the next
  zones, and Havok's tree over the cells), with what Armageddon adds made from the mesh: each
  cell's island (a piece of the mesh joined to nothing else) and the pools the game fills while it
  runs. When a level loads, the game checks that within each island of the people's mesh the
  cells no mission has switched off are one piece ("Isolated sub-islands found!"), so the people's
  islands are numbered that way. The scenery zones around the map get a file with no navigation,
  as in Guerrilla. Buildings bring navpoints of their own, which the game makes as each streams in
  (navpoint stream `0x7F08A0`); a transition point, joining a building's points to the ground's
  mesh, looks for the cell under it (`0x5B9AA0`), skipping cells a mission has switched off.
  Guerrilla switches off the ground's cells round many of its buildings, so some of these points
  stand on no cell the game takes ("Invalid transition navpoint ... can not find nav cell").
  Guerrilla's own loader then links such a point to the nearest open point of the mesh; Armageddon's
  reports it instead. In our worlds guerrilla_world gives the point the nearest open cell, asking the
  game's lookup again on rings round it (every half metre, out to 10 m), which joins it where
  Guerrilla's link would; the log names each one.
- **The destructible objects** (Guerrilla's `rfg_mover`s and `general_mover`s). Each chunk file
  (`.cchk_pc`, Guerrilla's version 55) is rewritten as Armageddon's version 62 (`GuerrillaChunks`):
  the mesh block, the materials moved onto Armageddon's shaders (`GuerrillaMaterials`), the
  destruction data, the Havok 5.5 collision written as 2010 (`ChunkCollision`), the object ids.
  The zone objects are written as Volition's tools wrote Armageddon's own movers (the same
  properties in the same order, Armageddon's own settings at the schema's defaults), with
  Guerrilla's placement, its links between pieces (kept in the objects in Guerrilla, in the zone's
  extra block in Armageddon), ground anchors, script names (`display_name`), how much of a building
  must fall for it to count as destroyed, and the header flag that keeps a free-standing building
  among the physics' fixed objects (`GuerrillaMovers`). Each chunk file goes into the container of
  the sub-zone its object stands in, as Armageddon keeps them.
- **The gameplay properties** of those objects (`GuerrillaGameplay`): Armageddon's
  `gameplay_properties.xtbl` still names most of Guerrilla's entries, but with its own settings
  (it took the shadows off nearly all and keeps many props from anchoring), and lacks 121 that
  Guerrilla's world uses. So the builder writes Armageddon's table with Guerrilla's settings added
  for the guerrilla_world mod (above): each entry the objects name is put in Armageddon's fields
  (Guerrilla's flags but two Armageddon has no bit for), and an object names an Armageddon entry
  with the same settings where there is one, else a new `rfg_` entry (139). Armageddon's own
  entries are left as they are. The two games' flags are the same bits up to `no_cover`, but for
  one name: Guerrilla's `force_dynamic` (bit 3: a loose prop, knocked about whole) is Armageddon's
  `prevent_anchor` (Armageddon's own conversion has it on 68 of the 97 shared entries with
  `force_dynamic` and on none of the others). Written under that name, barrels, crates and chairs
  fly when hit, where without it the game anchored them to the ground and they swung on the spot
  or broke off.
- **Their draw distances** (`GuerrillaLod`): Guerrilla switches an object off at the distance of
  its gameplay entry's LOD class (25, 50, 100, 150 or 200 m; most have none, and are always
  drawn). Armageddon's classes of the same names fade out much nearer, so the builder adds
  Guerrilla's to Armageddon's `lod_properties.xtbl` (`rfg_1_clutter_extra_small` ... at
  Guerrilla's distances), and each object names its class. Guerrilla switches an object off at
  once; ours fade out over half a second there, so they don't pop.
- **Their effects** (what an object shows when it breaks, is hit or stands burning). An object names
  its entry of `level_objects.xtbl` (its Props), which names effects of `effects.xtbl` (death,
  impact, custom and script point effects), each naming a visual effect of `vfx.xtbl`, which names
  its file and how it is loaded: the ones the world's objects use are loaded with the level, from
  its files. Armageddon's `level_objects.xtbl` still names most of Guerrilla's entries, with its own
  settings and effects. So the builder (`GuerrillaEffectTables`) adds Guerrilla's level objects,
  effects and visual effects the objects use to Armageddon's three tables under `rfg_` names, for
  the mod to hand the game, and each object names its `rfg_` level object. Sounds are left out
  (Guerrilla's are XACT cues; Armageddon uses Wwise). The explosions an object dies in
  (`explosions.xtbl`) are added the same way, Guerrilla's settings and effects in Armageddon's
  fields (its `Impulse` as `Mover_Impulse`, `Structural_Damage` as `StructuralDamage`, each effect
  an `Effect` with its name; how it damages buildings taken from Armageddon's explosion of the same
  name). Armageddon's own explosion of a name plays effects it loads only with its own levels, so
  in our world it went off unseen. The game keeps at most 132 explosions, Armageddon's and its
  DLCs' first, which left room for two, so the mod gives it a table of 256 (`0x1888D88`, 0x144 bytes
  each: eight addresses and one size changed after checking them), and the world builder adds 12 of
  Guerrilla's explosions. Guerrilla's vehicles' effects (engine fire and smoke, a wreck's fire, a
  broken-off part's smoke, exhaust, sparks on a hit or a scrape) and their explosions are added too,
  as effects played everywhere in the world (packed with the world's own, as its dust is), for
  `GuerrillaArsenal` to name in the vehicles: Armageddon's effects and explosions of the same
  names are its own (its fire and sparks, a purple car explosion, exhaust it loads only with its own
  levels, a vehicle explosion with a fifth of the damage and ten times the share for the player's
  own car).
  Visual effects had the same problem: the game keeps 950 (`0x121FDE8`, 0xB4 bytes each) and
  Armageddon's take 904, so only the 46 most placed of Guerrilla's 99 fitted. The mod moves that
  table to 2,048 (13 addresses and 2 sizes, its new entries built with the game's own constructor),
  and the world builder adds them all. The effects (`effects.xtbl`) too: the game keeps 1,225
  (`0x1200C40`, 0x68 bytes each) and Armageddon's take 1,112, so with the objects' and the vehicles'
  they didn't fit. The game finds an effect by name, and a loaded effect file, through two lookups of
  1,225 names built into their objects (`0x124BBE0`, `0x11E9EF8`), whose methods reach their arrays by
  fixed offsets. Only those two objects use that class, so the mod replaces its six methods
  (contains, find, insert, insert with a copied name, remove, clear) with its own, which keep 4,096
  names for each, the same way the game does (names in any case, the first of a name kept), and moves
  the effects to a table of 4,096 (7 addresses and 2 sizes). Each visual effect's file is converted
  (`GuerrillaEffects`: Guerrilla's effect format, versions 21 to 26, written as Armageddon's
  version 36: its emitters, lights, meshes and coronas reworked, each emitter's material given the
  values Armageddon keeps after it (taken from Guerrilla's emitter: self-illumination, ambient and
  diffuse colours and the rest), each emitter's flags where Armageddon reads them (whether its
  particles grow through their middle sizes, lengths and colours, as Guerrilla's file says) with the
  size and colour variances Guerrilla's loader turns on for every emitter, its meshes' materials on
  Armageddon's shaders, and the fixes Guerrilla's loader makes to its older files), with its
  textures (`GuerrillaTextures`: found in Guerrilla's effect archives, flip-book tiles kept), into
  the container of each zone whose objects show it, and a group of its own is named after each file
  (`rfg_<name>.cefct_pc`) in Armageddon's effects asset list (an override of `effects.vpp_pc`'s
  `vfx_containers.asm_pc`, the player's own with these added), where Armageddon lists its own
  effects: the game makes an effect only when the streaming system knows a group by its visual's
  file name, and that list is read once and kept (in the world's own list, read again at every level
  load, the groups broke a second load). The game keeps at most 950 visual effects, of
  which Armageddon's own take 904, so the 46 shown by the most placed objects are added (over 92%
  of what the world's objects show) and the other 41 wait for more room.
- **The precache**: every file more than one container would hold (the ground's texture layers,
  roads, rocks, chunk files and their textures) goes once into `rfg_precache.vpp_pc`, and each
  zone's and sub-zone's precache group in the asset list (`0p_cXXYY`, `1p_cXXYY`) names the ones
  it needs, as Guerrilla's world and Armageddon's campaign keep their shared files. The game loads
  such a file once while any cell that needs it is loaded, so the zones around you take about a
  third of the memory they did. The game picks a level's precache by the area of its start zone's
  grid place, which knows only the campaign's areas, so `guerrilla_world` makes it pick the
  world's own (below).
- **The zone files**, made from the game's own schema (`rfg_zonex_format.txt` in `misc.vpp_pc`,
  which Volition's level editor used: each object's properties at their defaults, then what the
  world needs): each zone's marker, an environment box over the whole zone (its view distance, fog,
  bloom and sky set, everything else off), a player start (Guerrilla's own where the zone has one,
  else the zone's middle) and the destructible objects. They go into `zonescript_rfg.vpp_pc`, stored
  compressed file by file as the game's own zone archive is: the game reads that archive whole into
  a 20 MB pool when a level loads.
- **The districts and their time of day**: Guerrilla gives each of its eight districts (Parker,
  Dust, Badlands, Oasis, Eos, Manufacturing, End Game, Tutorial) its own time of day and blends
  those of the zone you are in and its neighbours by where you stand. Armageddon still works the
  same way: at start-up it reads the territories in `territories.xtbl` (each district with its
  zones), and a level loads `<world>_<district>.dtodx` for each district of the territory named
  like the world (nine sets at most). So the builder adds Guerrilla's districts to Armageddon's
  table as the territory `rfg` (`GuerrillaTerritory`; an override in
  `overrides\build\pc\cache\table.vpp_pc\`) and writes a set for each district and the default
  (`GuerrillaTimeOfDay`). Both games keep a set as the same 68 curves in the same file format, and
  give a curve a set leaves empty the same default, so each set is the district's own, written
  from code curve by curve. Only the ambient light is converted, because Armageddon caps its
  brightness at `ldr_max` where Guerrilla doesn't: each key's brightness goes into its colour, and
  `ldr_min`/`ldr_max` are 0 and 3 in every set, as Guerrilla's districts that set them have them.
  The districts are named by two
  letters (`Eo` for Eos, `rfg_eo.dtodx`, ...): Armageddon keeps district names in 32 bytes, and
  Guerrilla's full names stopped the game at start-up ("Length of string pool exceeded").
- **Room for the world's textures**: the game names every texture it loads by a slot in its
  texture name table (`misc.vpp_pc\bitmaps_pc`). Its own textures' names come with the table;
  any other texture takes one of 200 spare slots, never given back while the game runs. When
  they're gone, textures draw as the game's stand-in (a grid) or white. The Guerrilla world has
  about 2,000 textures of its own, so the second level loaded ran out. The builder writes the
  game's table again with that many more empty slots (`GuerrillaBitmaps`; an override in
  `overrides\build\pc\cache\misc.vpp_pc\`); the game makes room for their names from the slot
  count.

### guerrilla_hud

Red Faction: Guerrilla's HUD in the worlds ported from Guerrilla (`[GuerrillaHud] Worlds=`),
Armageddon's own everywhere else. Both games build their HUD from the same kind of interface
documents (elements driven by a Lua script).

- Armageddon loads its HUD once at start-up and keeps it through every world. The mod wraps the
  HUD's data update (`0x57BC30`) and, when a listed world is entered, loads Guerrilla's documents
  (`Documents=`) the way the game loads its own (`0x563990`), and unloads them when another world
  is.
- Guerrilla's documents and pictures load in Armageddon as they are, but their scripts read game
  data Armageddon doesn't have, so each document gets a script of our own (`mods\guerrilla_hud\`,
  from `src\mods\guerrilla_hud\`) that drives Guerrilla's elements from Armageddon's data
  (`health_hud`) and fades out the parts of Armageddon's HUD it replaces while it is loaded.
- The mod puts each document in the interface's draw list after loading it, as the console's
  `vint_doc_load` does (a document in no draw list is loaded but never drawn), and keeps it there
  only while the HUD itself runs (the interface's draw pass, `0x564FF0`, is wrapped too), so
  nothing of it stays on loading screens or in the menus.

`editor\GuerrillaBuilder.exe` (its HUD; code in
`src\editor\RfaFormats\Interface\`) makes the documents from the player's own copy of Guerrilla:
- It renames Guerrilla's pictures (`rfg_...`, since both games use some of the same names for
  different pictures), gathers the textures each document needs into one texture file, packs each
  document with its script into containers as Armageddon packs its own, and adds them to the
  game's lists of documents and containers (`interface.vpp_pc\vint_doc_containers.asm_pc` and
  `misc.vpp_pc\rfg.vint_proj`), all in `overrides\`.
- Guerrilla's picture sheets (where each picture sits on its texture) are added to a copy of the
  game's sheet table under a new name, `guerrilla_hud_sheets.xtbl`, which the interface project
  names in place of the game's. The game reads the tables it needs at start-up from
  `table.vpp_pc` held in memory, past the overrides, so its own `bitmap_sheets.xtbl` can't be
  overridden, but a table under a new name is looked up by name.
- A picture that is a whole texture (the blood) gets a sheet of its own there too, holding just
  that picture, since the game finds pictures reliably only through sheets.
- Taking the HUD away in the builder takes all of it away again. Since that sheet table now holds the pictures of
  Armageddon's own HUD too, it must never be deleted alone.

**Guerrilla's map** (`Map=1`): its minimap (`rfg_ui_hud`) and its world map (`rfg_ui_fullscreen`,
opened and closed with `MapKey=`).
- Guerrilla drew its maps in its documents' `map` elements with game code of its own, which
  Armageddon kept only for its compass. So the builder makes those elements plain pictures
  (the minimap's is its frame: the border, its shading and the player's arrow), and our scripts
  build the map from Guerrilla's own pictures with the interface's elements, created at load
  (`vint_object_create`).
- The minimap: a clip box and a square turned 45° used as a mask (`mask`) cut Guerrilla's octagon
  (its mask picture's shape); inside, a group turned against the camera holds Guerrilla's world
  map picture (`terr01_map`, 1024 × 1024, a zone every 85⅓ pixels) and, over it, the zones' own
  pictures near the player (`terr01_<col>_<row>_map`, 512 × 512, a metre a pixel). It reaches
  Guerrilla's 40 m from the player to the frame's edge, plus half the vehicle's speed while
  driving. The district's name above it comes from Guerrilla's districts (`territories.xtbl`) and
  their names in its strings (`territories_us.le_strings`), written by the builder to
  `mods\guerrilla_hud\districts.txt`.
- The zones' own pictures (13 MB in all) come as Guerrilla keeps them, each in a container of its
  own: a document per zone that holds nothing but that texture (`rfg_ui_map_<col>_<row>`, 128 KB);
  the mod loads a zone's while the player is within 250 m of it, and unloads it 20 s after.
- **The interface's limits**, raised by the mod in plain C++ before the game starts: the project's
  documents (a fixed array of 128 records, `0x133A5F8`, made by `0x56A7C0`; the reader's cap at
  `0x566BC6`) to 256 (Armageddon's 115 and ours 107); documents open at once (a pool of 10 records,
  `0x566850`) to 40; the slots documents are loaded into (45, `0x28C3F48`; every document inside
  another takes one too, and with none free a menu page waits forever) to 120, by moving the array
  (its 14 references and its one-byte counts); the interface's lists of document handles (16 each,
  `0x133A1A0`) to 256; the pools every element comes from (one per type: bitmaps 1,300, tweens 820,
  ...; an element that doesn't fit is left out, and a menu page missing its parts can crash) to
  three times as many, with their memory pool "vint_perm" from 640 KB to 4 MB; and the interface's
  memory (`0xA45600`: 10 MB) to 32 MB, which the game's own menus and loading screens share with
  Guerrilla's map.
- Script errors in the interface's Lua (which the game otherwise only keeps, so a broken menu just
  stays empty) are written to `logs\rfa_mods.log` by the loader ("Lua error: ...").
- What the scripts need each frame, the mod gives them through Lua functions of its own, added
  with the loader's `RFAMod_RegisterLua` export: `rfg_map_view()` (where the player is on
  Guerrilla's map picture, which way the camera looks, the speed, the zone, the district, whether
  the world map is open) and `rfg_map_tiles()` (the zones near the player whose pictures are in).
  The player's position is the local player object's (`0x2271490`, `+4`), the camera's direction
  the camera manager's (`0x11B9C40`, `+0x50`); the world keeps Guerrilla's coordinates (zone
  column `8 + x / 511`, row `8 − z / 511`).

**Guerrilla's reticles and ammo** (`Reticles=1`): its document `rfg_ui_hud_reticles`, run by
Guerrilla's own script for it.
- Guerrilla's script draws everything from one data item its game filled each frame, `WeaponFire`
  (22 fields: the weapon, its reticle picture, the rounds in the magazine and the rest, whether
  it's aimed at a friend or an enemy, a hit this frame, the heat, ...). Armageddon has no such
  item. So the mod adds Lua functions that make a data item (`rfg_dataitem_make`, through the
  engine's own `0xC58910`, which holds up to 128 items) and set its fields (`rfg_dataitem_set`,
  through `0xC576E0`, which tells the subscribers), and our part of the script makes `WeaponFire`
  and fills it from Armageddon's own items for its HUD (`reticule_hud`, `primary_weapon_hud`).
- The builder builds the document's script from the player's copy of Guerrilla: our shims
  first (`rfg_guerrilla_shims.lua`: what Guerrilla's scripts call that Armageddon's interface Lua
  hasn't got, such as `string.find`), then Guerrilla's script with its `_init` and `_cleanup`
  renamed and the pictures it names renamed `rfg_...`, then ours, which calls them.
- Two differences between the games' interface Lua are bridged in Guerrilla's script there:
  Armageddon's `vint_set_property` / `vint_get_property` take and give a `tint` as 0–255 where
  Guerrilla's scripts use 0–1, so their calls go through the shims' `rfg_vint_set_property` /
  `rfg_vint_get_property`; and their calls of the document clock (`vint_get_time_index()`,
  `vint_set_time_index(t)`), which name no document and so use whichever document's script is
  running, name their own.
- Armageddon's reticle, ammo ring and weapon meter are faded out while it's loaded; its Nano
  Forge meter, its magnet gun's marks and its reload and wait prompts stay.

**Guerrilla's weapon select** (`WeaponSelect=1`): its document `rfg_ui_hud_weapon_sel`, run by
Guerrilla's own script the same way, in place of Armageddon's weapon selector (which sits at the
bottom left, over the minimap).
- Guerrilla's script reads two items its game filled: `WeaponSlotInfo` (each of the four slots'
  ammo, picture and name) and `WeaponShowInfo` (open or not, the chosen slot, the one before).
  Armageddon has the same four slots: its item `weapon_radial_hud` says when its selector is open
  and which slot is chosen (with the weapon's name), and its data responder
  `wpn_radial_populate` gives each slot's picture and ammo, counted as Guerrilla counts it (-1
  unlimited, -2 none). Our part makes Guerrilla's two items and fills them from those, Armageddon's
  slot *n* as Guerrilla's plate *n*, and fades Armageddon's `weapon_selector_grp` while loaded.
- Armageddon closes its selector about 0.7 s after a key picks a weapon, before Guerrilla's has
  finished coming up (the weapon's name fades in after about 0.8 s), so Guerrilla's stays up 1.5 s
  longer. How long Guerrilla's own game kept it up isn't known; that time is ours.

### guerrilla_vehicles

What Red Faction: Guerrilla's vehicles need from Armageddon's engine that it doesn't do itself
(`[GuerrillaVehicles]` in the ini). So far: the keys for driving, getting out and changing seats,
room for the vehicles themselves, their animation sets and their animations' triggers, their own
skeletons, and their dents.

- **Driving keys**: Armageddon still has Guerrilla's driving code for the player. Its gameplay
  controls (`0x4DBBD0`) read the accelerator and brake as button values (actions 62 `veh_accelerate`
  and 63 `veh_brake`, through `0x4E1280`) and the handbrake as a held button (64 `veh_handbrake`),
  and pass them to the car, with steering from the `veh_steer` axis (A/D). But its PC controls give
  those three actions no key (it has no car-driving scheme on PC, only on foot, exo, walker/tank and
  flyer), so a car's throttle stays at 0. The mod wraps the function that builds a control set's key
  map from its bindings (`0x4E86E0`) and gives each of the three a key first, if it has none:
  Guerrilla's own PC defaults, W, S and Space, or the keys named in the ini (by the names the
  console's `controls_show_key_bindings` prints). The bindings are 20 bytes an action from
  `0x11BEBA0`, the keyboard key at +8 as a DirectInput scancode, 0 for none, the mouse button at
  +0xC (0 to 6, −1 for none).
- **Getting out and changing seats**: in a vehicle the game's use button is the vehicle action
  (`0x4E1B70` picks it: `of_action` on foot, `exo_action` in an exo, 86 `veh_action` in a vehicle,
  `veh_flyer_action` in a flyer), which gets the player out through the vehicle's interaction; 90
  `veh_change_seat` moves them to another seat. Armageddon's PC defaults (six tables from
  `0xF7F078`: action, key, mouse button) give neither one a key, only a controller button for the
  vehicle action (its own walkers and tank are left by script), so the player got into a car with E
  and couldn't get out. The mod gives them Guerrilla's: E (as on foot) and the third mouse button.
- **Room for animation sets**: the game keeps every animation set in one table of 140
  (`MAX_ANIM_SETS`, 20 bytes a set from `0x26321B8`, the count at `0x2632170`), and Armageddon fills
  125 itself (its 50 set files and one set for each of its 75 animated objects), so Guerrilla's
  vehicles' 24 don't fit: one too many stops the game at start-up ("Num_anim_sets <
  MAX_ANIM_SETS") and it crashes. No one function holds the table: the game's code reaches it by
  its address, written into 34 instructions in 20 functions (the set loaders, `anim_set_find`, the
  people's animation code, which turns a set's address back into its number by subtracting the
  table's start), and its size into five (the four loaders' checks, the start-up constructor). So
  the mod gives the game a table of 255 of its own and changes those 39 values to it, each checked
  against the game's first, none changed if one differs. Mods start before the game's own start-up
  code, so the game's constructor builds the new table and the old one is never used. Without the
  mod, guerrilla_world doesn't hand over Guerrilla's set list (the game keeps its own).
- **Room for the vehicles**: the game keeps every vehicle the vehicle table describes in one list of
  40 (`0x148BD08`: its entries, how many it holds, how many it has; 0x880 bytes an entry, kept right
  after it), which its start-up constructor (`0x6270C0`) builds and which can't grow. The table's
  reader drops every vehicle after the 40th without a word, and Armageddon has 24, so until
  v0.5.0-beta only Guerrilla's first 16 (the Maverick, the APCs and three scouts) were in the game.
  The game reaches the entries only through the list, so the mod builds it itself in place of the
  constructor: 255 entries of its own, each built by the game's own constructor for one (`0x627100`).
- **Room for their bone infos**: each vehicle in the list also takes, for as long as the game runs,
  one of the animation library's 138 entity bone infos (0x240 bytes each from `0x323BD50`), which the
  people and animated objects take theirs from while they're about. With all of them taken the game
  writes through a null pointer. Four small functions use that table (add, remove, clear, find),
  each with its address and its size in its code, so the mod moves it as it does the animation sets:
  to a table of 512, those four addresses and five sizes changed after checking them.
- **Room for the animations' triggers**: an animation's triggers (a door starting to open, a foot
  landing, the camera moving: each `anim_files.xtbl` entry's `<Triggers>`) are kept in one pool of
  5,750 for every animation (8 bytes each from `0x34BB9B8`, how many taken at `0x34C6D68`), which the
  animation library fills as it reads the table (`0xD89D40`). Once it's full, every animation after
  gets none, without a word. Armageddon's and Guerrilla's together come to about 7,000, so the cars
  after the APCs had no "door start" and getting in stopped at the door. Only that one function
  reaches the pool, so the mod gives it one of 16,384: its address and its size changed after
  checking them. At start-up the log also says, for a few door animations, how many triggers each
  got ("Door check:").
- **Room for the animations themselves**: the game keeps an entry for each `anim_files.xtbl` entry
  (4,224 of 0x34 bytes from `0x2597D30`) and the animation library one for each animation it knows
  (4,500 of 32 bytes from `0x34C7570`, with their names in a lookup of 9,000 places from `0x34B2D18`).
  Armageddon's and Guerrilla's vehicles' come to about 3,820 of each, and Guerrilla's people and
  walkers would pass them. Only their own functions reach these tables, so the mod moves them the
  same way: the entries to 8,192 (15 addresses and 7 sizes), the library to 8,192 and its lookup to
  16,384 (17 addresses, 10 sizes and the reset's two byte counts). The lookup moves first, and the
  library only if it did: a lookup with no empty place would leave the game looking for one for ever.
- **Vehicles' own skeletons**: walkers, flyers and turret trucks have a skeleton of their own
  (`Rig` in their table entry). The game loads every skeleton its archives list while it starts
  (`0x973460`: each through `0x973550`, by name), and when it reads the vehicle table it takes each
  vehicle's from those (`0x62F1C0`) without checking: one that isn't loaded crashes it. Guerrilla's
  six (`walker_civilian`, `walker_min`, `walker_flamer`, `edf_flyer`, `flatbed`, `fueltanker`, none
  of them Armageddon's) are new files the builder writes into `overrides\build\pc\cache\`,
  in no archive's list, with their names in `mods\guerrilla_vehicles\rigs.txt`; the mod wraps the
  start-up loading and has the game load each of those by name after its own. The tool leaves out a
  vehicle whose skeleton is in neither game.
- **Dents**: Guerrilla's cars crumple where they're hit, by a morph target in the vehicle's file (46
  of its 74 models have one). Armageddon kept all of Guerrilla's code for it: the vehicle loader
  reads the morph target (`0x622110`, which wants exactly Guerrilla's version, 2) and makes the
  vehicle's dent container, and the impact code dents it, but only while
  `vehicle_deformation_enabled` is on (a bool at `0x142A484`, also a console toggle), which Guerrilla
  starts on and Armageddon off (none of its own vehicles has a morph target). The mod turns it on
  while the game starts, before any vehicle is loaded, after checking the three instructions that
  read it (`Dents=0` leaves it off).
- **Doors**: getting in, the "Open Door" step opens the door and records it among the vehicle's
  open doors, which the game closes 2.5 seconds later once nobody stands where it was opened from (4
  at most), Guerrilla's code as it was. A door recorded open is never opened again, so a door that has
  shut while its record still says open was climbed through (seen on Guerrilla's taxi). The mod wraps
  the door-open step (`0x654AA0`): a door that is shut but still recorded open has its record cleared
  first, so the game opens it again. And a door's opening is measured as Guerrilla measures it: the
  game works it out from the door's turn against the vehicle (`0x6134A0`), whose axis and angle a
  shared math function gives; for a shut door the turn is next to none, which Guerrilla's version of
  that function calls no turn and Armageddon's a half turn, so at some headings a shut door read
  fully open, the game never moved it, and you climbed through it. The mod replaces the measurement
  with Guerrilla's (the shared function stays Armageddon's for everything else). `DoorLog=1` (off
  unless set) logs every door opening, closing and change of state.

`editor\GuerrillaBuilder.exe` (its vehicles; code in
`src\editor\RfaFormats\Guerrilla\GuerrillaArsenal.cs`, `GuerrillaVehicles.cs`,
`GuerrillaAnimationSets.cs`, `GuerrillaAnimations.cs`) makes the vehicles
from the player's own copy of Guerrilla (every one that converts, or `--vehicles <names>`;
taking the vehicles away in the builder takes everything away). Tracked vehicles wait for their tread splines, and walkers for
their skeletons and blended movement (they are built but can't be got into: a vehicle whose
interaction can't be ported gets none, not Armageddon's `Dummy`, with which the player gets in and
can't drive or get out):
- **The model** (`.ccar_pc`, Guerrilla's version 27) is written as Armageddon's version 30. Both are
  one structure (the vehicle's parts, lights, tag lists, mesh, materials, collision and MOPP trees,
  with offsets from the file's start); Armageddon only added fields: 0x90 bytes and two arrays in
  the header, three arrays in each part (240 → 264 bytes), a word in each light (76 → 80). The mesh
  block (4 or 6 → 9), the materials (onto Armageddon's shaders) and the collision (Havok 5.5 → 2010)
  are converted as for the world's destructible objects, and every offset is moved to where its
  target lands. Checked on the civilian walker, which both games ship: every part, light, tag list
  and section of ours matches Armageddon's own conversion. Guerrilla's dents (morph targets, which
  none of Armageddon's vehicles has) are carried over as they are (their vertices are in the g-file,
  which stays as it is). Tracked vehicles' tread splines aren't carried over yet.
- **Its container**, named as Guerrilla's (`col_taxi_1.ccar_pc.str2_pc`, a new file in
  `overrides\build\pc\cache\`) with the model and its textures, and an entry for it in an override
  of `vehicles.vpp_pc\vehicle_containers.asm_pc` (the player's own list with ours added).
- **Its table entries**: Guerrilla's own `vehicles.xtbl` and `vehicle_interactions.xtbl` entries,
  added to Armageddon's tables, which guerrilla_world hands the game at start-up. A field
  Armageddon's table hasn't is left out, unless Armageddon's vehicle reader still reads it:
  Armageddon's reader is Guerrilla's with fields added, but its table's description lost some
  handling values, and without them a car takes the defaults (Guerrilla's taxi turns with a yaw
  torque of 1.4, the default 1; the APCs reverse at once, the default waits half a second; AI
  drivers' own torque, spin damping and top speed). A field it requires that Guerrilla's entry hasn't gets the
  default Armageddon's table gives it (the game's table reader stops on a missing one). A field the
  two games measure differently is converted: the vehicle's price (Guerrilla's in millions,
  Armageddon's in whole units) and a flyer's turn and tilt rates (revolutions a second, degrees a
  second); a decimal left in a field Armageddon reads as a whole number stops its reader too. Its
  effects and explosion are Guerrilla's own (its entry's, else Guerrilla's vehicle defaults', as
  Guerrilla's reader takes them), under the `rfg_` names the world builder adds them by: build the
  world first. Left out for now: the sounds (Guerrilla's are XACT cues), lights (Armageddon lights
  a vehicle through a map of its own), turrets.
- **Getting in and out**: the interaction is Guerrilla's whole: its waypoints, seats and every
  transition between them with its steps (open the door, climb in; quick, diving and side exits;
  driver to passenger; crawling out of an upturned car). Both games name the same steps, actions
  and animation states (`vehicle enter primary 1` ending in `driving forward` is what seats the
  player behind the wheel); Guerrilla's `Door` is Armageddon's `Door_1`. The doors' sound cues are
  left out until Guerrilla's audio is ported.
- **Its animation set** (such as Guerrilla's `Vehicle_Taxi`: which animation plays for each step), with
  every animation it names, from the vehicle's container or Guerrilla's `anims.vpp_pc`, as Guerrilla
  has it, packed in our container (as Armageddon packs its walkers' player animations: asset list
  type 16). In Guerrilla's world the player is Mason and the people are Guerrilla's own, on its
  people's skeletons (all share its player's 52 bones, in the same order; some add their own after
  them: Mason's camera, EDF shoulder pads, coats), so the animations stay theirs. Both games'
  animations are the same format (version 10), but Guerrilla packs its rotation keys differently
  (each run's header 5 bytes, each key 4: an explicit w; Armageddon's 4 and 3, w worked out from the
  rest), so it has a reader of its own (`Animation.ReadGuerrilla`), and `AnimationWriter` writes
  Armageddon's; scale keys and weight curves as they are. (Until Mason became the player they were
  moved onto Darius's skeleton; a Guerrilla vehicle in Armageddon's own levels now plays Mason's
  animations on Darius.) The game reads its animation lists and
  sets while it starts, and guerrilla_world hands it Armageddon's with Guerrilla's added: the set's
  listing (`anim_set_filenames.xtbl`; the set's own table is a new file in
  `overrides\build\pc\cache\`, which the game then reads by name), each animation's triggers and
  flags (`anim_files.xtbl`: the door opens between its animation's `door start` and `door end`
  frames, so without them a transition naming a door stops at it), and the state and action names
  Armageddon's tables haven't (`driver look back`, which Guerrilla's code names itself). A name
  Armageddon already uses gets the prefix `rfg_`. With guerrilla_vehicles the game holds 255 sets
  (see above), and Armageddon fills 125: the sets in its listed files and one per object with
  animations of its own (`anim_rig_objects.xtbl`). The tool counts them from the player's files and
  ports only as many of Guerrilla's sets as fit, in the order of Guerrilla's vehicle table; a
  vehicle whose set is left out gets no interaction.
  The four lists are shared with the people (below): each part starts from the lists
  already in `mods\guerrilla_world\` and replaces only its own entries, which it records in its own
  folder (`mods\guerrilla_vehicles\anim_entries.txt`, `mods\guerrilla_people\anim_entries.txt`: a
  line a set table or animation), so either can be run again without taking the other's away.

### guerrilla_people

What Red Faction: Guerrilla's people need from Armageddon's engine that it doesn't do itself
(`[GuerrillaPeople]` in the ini). Armageddon's character code is Guerrilla's with fields added: its
character table reader still reads Guerrilla's fields, and it takes each character's bones from that
character's own skeleton. So Guerrilla's people keep their own meshes, skeletons and animations; the
mod gives them what the engine lacks around them:

- **Their skeletons**: the game loads every skeleton its archives list while it starts, and takes
  each character's from those when it reads the character table (`0x8860D0`). Guerrilla's people's
  (`greene.rig_pc`, ...) are new files, in no archive's list, named in
  `mods\guerrilla_people\rigs.txt`; the mod has the game load each of them by name (`0x973550`) just
  before it reads the table.
- **The world's spawn resource**: where a level's spawn resource is in force (the pools' state
  `0x19734CC` isn't 0: Wrecking Crew levels, and the Guerrilla world), the game makes only the people
  the resource holds and loads no other: its human create (`0x762D50`, which the console's
  `drop_human` uses) gives up on a character that isn't loaded or on its way. The Guerrilla world's
  zones name its own resource, `rfg_Default`, which lists one person for each container the builder
  made (the game drops a whole resource that lists two characters of one container; the others come
  with their container), so the game loads them itself, and the mod writes in the log which of `mods\guerrilla_people\people.txt` are
  loaded, on their way or not, 5 and 30 seconds after the world starts. A name the resource table
  hasn't, or none (from a zone, the mission, a cutscene or a script), gives the table's fallback,
  Armageddon's "Default" (its aliens, soldiers and Marauders, whose effects run the effect pools out
  in the Guerrilla world); so in the worlds `Worlds=` names the mod gives the game `rfg_Default`
  wherever it would load that one (it hooks where a level's resource is set, `0x7628B0`, and where
  one is applied at once, `0x7625A0`), and logs who asked for it.
- **Their animations**: Guerrilla kept its people's animations in its animations archive, which its
  game loads whole while it starts and keeps for good (each overlaid on its place in the animation
  library with "keep" set, which an unload passes over). They are many characters' at once (every
  set builds on Male), and an animation packed in a character's own container belongs to whichever
  container loaded first and goes when that one goes. So, as Guerrilla, they are loaded once and kept:
  The builder writes them into `mods\guerrilla_people\animations\`, and once Armageddon has
  loaded its own archive (`0x95E180`) the mod reads each one and overlays it the same way (its place
  by name, `0xD8C2C0`, or a new one, `0xD875F0`; then `0xD8C0F0` with "keep" set). The animation
  library holds 8,192 with guerrilla_vehicles (Armageddon's own 4,500 were nearly all taken).
- **Room for the skeletons**: every skeleton the game loads (its own, and Guerrilla's vehicles' and
  people's) goes into one memory pool, "rigs" (`0x26AFB48`), which the game's start-up makes 576 KB;
  Armageddon's and Guerrilla's filled it, and one more skeleton stops the game while it starts ("Failed
  aligned alloc ... in rigs pool"). When the game sets that pool up (its memory pools' init,
  `0x4280C0`) the mod gives it 2 MB of its own instead; the pool is empty then, and no code is changed.
  Always, as the vehicles' skeletons need the room too; the log says how full it is.
- **Room for the animations' names**: the game keeps every animation's name (each entry of
  `anim_files.xtbl`, and the sets', states' and actions' names) in one string pool, "Anim strings"
  (`0x2684EC8`), which a start-up initializer (`0xF4A7E0`) gives 128 KB; Armageddon's, Guerrilla's
  vehicles' and people's names together don't fit, and the game stops while it starts ("Length of
  string pool exceeded"). The pool is one of the game's memory pools (its memory at +0x40, its size
  at +0x44, how much is used at +0x4C), so the mod lets the initializer run and then gives it 512 KB of
  its own, with what is in it already; no code is changed. This happens whatever `Enabled=` says, since
  the animation lists hold the people's animations either way. The people's animations also need the
  room two other mods give the game's tables: guerrilla_vehicles (the animation library and its entries)
  and guerrilla_gear (the map of every set's states and actions); the mod writes a warning in the log at
  start-up if either isn't loaded.
- **Guerrilla's sides**: both games number the teams the same (0 Guerrilla's Red Faction, the
  player's; 1 its EDF, Armageddon's Legion; 2 civilians; 3 Marauders; Armageddon adds 4, its
  cultists) and keep how each team takes each other in a 5 × 5 table (`0x21DC8D0`), which a level's
  start resets to Armageddon's (`0x8A0350`). Armageddon's make the Marauders the player's friends;
  Guerrilla's make the EDF and the Marauders hostile to the player's side, civilians friendly to it
  and unfriendly to the EDF, and the Marauders hostile to everyone. In the worlds `Worlds=` names the
  mod writes Guerrilla's after each reset, and again whenever they differ (through the game's own
  setter, `0x8A03A0`); the cultists keep Armageddon's.
- **Guerrilla's player**: the game picks the player's character once per level load (`0x8B77B0`):
  multiplayer's choice, else a DLC uniform chosen in Armageddon's menu, else the start zone's spawn
  resource's `Player_Override` (in the Guerrilla world Mason, whom the builder makes), else Darius. In
  the worlds `Worlds=` names the mod sets the chosen uniform aside while the game picks (and puts it
  back), so the player there is always Mason; Armageddon's own levels keep the uniform.

`editor\GuerrillaBuilder.exe` (its people; code in
`src\editor\RfaFormats\Guerrilla\GuerrillaPeople.cs`, `GuerrillaCharacterMeshes.cs`) makes the people
from the player's own copy of Guerrilla (`--characters <names>`, Guerrilla's `character.xtbl` names;
by default the EDF soldiers whose bodies are whole: the trooper, tough trooper, mercenary, the three
lieutenants, the elite and the two heavy elites; taking the people away in the builder takes everything away). For each:
- **The model** (`.ccmesh_pc`, Guerrilla's version 3) is written as Armageddon's version 7: the
  header gains a submesh count for each detail level and a visibility id for each submesh, the hit
  spheres and cylinders a word each (and their radius and height go from inches to metres), the mesh
  block goes to version 9 with one bone palette that keeps the skeleton's own numbers (Guerrilla's
  people have at most 64 bones, a palette's room), and the materials go onto Guerrilla's own
  shaders' skinned rewrites (`gw_*_c`: the clothes' BRDF shaders, the faces' skin and eyes, and the
  standard shaders, opaque or alpha-tested (eyelashes), as Re-Mars-tered draws them; the tool names
  any material none of them can draw yet). The vertex and index buffers stay as they are (both games use vertex format 3 for
  characters). Checked against Armageddon's own three-detail-level characters, whose layout is the
  one Guerrilla's convert to.
- **Its container**, named as the character (`EDF_Trooper.str2_pc` in `overrides\build\pc\cache\`)
  with the model and its textures (each as a single-texture file under the world's name for it), and
  an entry for it in an override of `humans.vpp_pc\character_containers.asm_pc` (Armageddon's own list
  with ours added; type 19, as Armageddon's people). Characters whose containers are the same in
  Guerrilla, file for file (the trooper, mercenary and lieutenants are all `greene`), share the first
  one's (`Shared_Container`, as Armageddon's own do; and Guerrilla's own sharing, its riot-shield
  elite's).
- **Its animation set**, Guerrilla's own (its Male set as `rfg_Male`, with the sets it builds on),
  every animation it names as `rfgp_<name>` (Armageddon has animations of many of the same names, for
  its own skeleton, and the vehicles' `rfg_` ones are moved onto Armageddon's player), re-packed in
  Armageddon's key packing with the tracks of the 52 bones every Guerrilla person's skeleton shares in
  the same order (the bones after them are each skeleton's own: the EDF's shoulder pads, coats), each
  a file in `mods\guerrilla_people\animations\` that the mod loads (above). A state naming one of Guerrilla's blend trees (a walk blended by the ground's slope, which Armageddon
  has no reader for) plays the tree's level animation for now; the faces' animations wait for the
  heads.
- **Its table entry**: Guerrilla's own `character.xtbl` fields, added to Armageddon's table
  (`mods\guerrilla_world\character.xtbl`, which guerrilla_world hands the game at start-up), its
  `Anim_Set` as Armageddon's `Animation_Sets`, a field Armageddon's table hasn't left out, a field it
  requires filled from its description's default or, where Guerrilla has nothing for it, named by
  the tool (the model type and audio switch by the character's side), and its inventory Guerrilla's own
  items where the builder has ported them (the trooper's assault rifle; its grenades not yet).
  Left out for now, each with its own port to come: the voices, the props and heads not made yet
  (below), and the height.
- **Its ragdoll**, Guerrilla's: Guerrilla builds its people's ragdoll in code (the bones it finds by name in
  the character's own skeleton, each part's size and weight, each joint's limits and axes, the bodies'
  damping, friction and bounce), and Armageddon's ragdoll is the same code with those numbers moved into a
  table a character names (`ragdoll_type`). So the tool writes Guerrilla's numbers as such a table
  (`mods\guerrilla_world\ragdoll_rfg_human.xtbl`, `RfaFormats\Guerrilla\GuerrillaRagdoll.cs`), which
  guerrilla_world hands the game at start-up, and every Guerrilla person names it: the bones it names are
  the same in all their skeletons. Without a ragdoll Armageddon hides a dead body at once.
- **Guerrilla's player, Mason** (`rfg_PC`), made the player of the Guerrilla world: his own model and
  skeleton, the skeleton with the tags Armageddon's player code looks for that Guerrilla's hasn't
  (`muzzle_flash0` and `l-nanoforge` on his left forearm, where the Nano Forge fires from and its
  effects sit; `l-spotlight`, the player's light, on his chest; placed as on Darius, carried over by
  the turn between the two forearms), in a player container (type 20, as Armageddon's player suits);
  Guerrilla's own fields (his hit points, speed, footsteps) and Armageddon's player's where Guerrilla
  has none (the player controller, container and blend tree, always face target and IK hands to
  weapon, the hit indicators, the Nano Forge's light); his animation set the weapons' build's
  `rfg_mason` (Guerrilla's player animations on his skeleton, and Armageddon's player moves carried
  onto it); his own spine bend and arm limbs (Armageddon's `spinebend.xtbl` and `ik_limbs.xtbl` name
  Darius's bones: entries with his added, which guerrilla_world hands the game); and the Guerrilla
  world's spawn resource's `Player_Override`. Armageddon's player's always-on Nano Forge light (a cyan
  lamp at his left forearm, from Darius's `Persistent_Effects`) comes with those fields;
  `[GuerrillaPeople] MasonNanoLight=0` (or the F7 menu's Graphics window) takes it off Mason, as
  Guerrilla's Mason has none: guerrilla_people takes it out of his character's effects as the game
  keeps them, and it shows the next time the game makes him.
- **Props** (Mason's coat and armour, later the Marauders' masks and bags): each of Guerrilla's props is
  an item (`items_3d.xtbl`) whose model is a character mesh with its own skeleton and the flag
  `inherit_bone_transforms`: the game copies the wearer's bones onto the prop's skeleton by name, so
  the coat follows Mason's body (its tails and scarf ride stiffly until Guerrilla's cloth is ported).
  Armageddon kept that code and uses it for its own coats. For each prop a character names whose mesh
  is in its own container, the tool converts the mesh and packs it, with its textures, into the
  character's container, copies the prop's skeleton (loaded with the people's), and adds the item,
  Guerrilla's entry fitted to Armageddon's table, to the items table the weapons build wrote (its
  own entries recorded in `mods\guerrilla_people\prop_items.txt`, taken out again on a rebuild or
  `--remove`). The character's entry keeps only the props made: a listed item whose mesh isn't
  packed would stop the game.
- **Heads** (Mason's face): Guerrilla draws a person's head as a base head (an item's character mesh:
  Mason's is `base_hires_head`) dressed with the face's own materials (`mason_head.mat_pc` in his
  container) on the face's own five-bone skeleton (`mason_head.rig_pc`: spine to eyes), the face's shape
  a morph of the base head where it has one. Armageddon kept that code but drops the morph and needs the
  base heads as preloaded items, so the tool makes each head that has no morph a prop instead: the base
  head's mesh (from Guerrilla's `items.vpp_pc`) converted with the face's materials in place of its own,
  packed with its textures into the character's container as `rfg_<face>` (`rfg_mason_head`), the face's
  skeleton loaded with the people's, and an item of that name (the base head's entry, with
  `inherit_bone_transforms`) worn as a `head_prop` prop at 100%, so his face follows his neck, head and
  eyes. His hair is part of his body's model. The faces Guerrilla shapes with a morph (Braun's, May's)
  wait for their morph to be baked into a mesh of their own.
- **The skin and eyes** are drawn by Guerrilla's own shaders, rewritten from Re-Mars-tered's (`rfg-skin`:
  the light under the skin from its unscattered, melanin and hemoglobin colours, a wrapped light by the
  specular map's blue, a glancing colour, three highlights; `rfg-eye`: the sun's direction laid flat,
  the skin's colours by its own curves, and a small glint that fades out from 1 to 6 m).
- **The squads**: Guerrilla's `squads.xtbl` gives a squad's team and size, and each member's character
  and weapon come from its team's progression when it spawns (`squad_progression.xtbl`: the EDF's,
  at the level the campaign is at); Armageddon's names each member (a leader and a member list, each a
  character and a loadout). So each of Guerrilla's squads is written into Armageddon's table with its
  members picked from its team's progression at the campaign's first level, as Guerrilla's weights
  spread them, among the characters made and the loadouts whose weapon is ported (Guerrilla's
  `squad_loadout.xtbl` entries, added to Armageddon's under their own names: `rfg_` where Armageddon has
  one; their grenades not yet). Both tables (`mods\guerrilla_world\squads.xtbl`, `squad_loadout.xtbl`)
  are handed to the game by guerrilla_world.

The world build places Guerrilla's patrols whose squads those tables will hold
(`RfaFormats\Guerrilla\GuerrillaPopulation.cs`). It works the squads out itself from Guerrilla's own
tables for the people the builder makes (its default ones, and any its last run made), so the
world can be built before the people; a squad that isn't written after all (its weapon not ported) the
game leaves out, with a warning. The zones name the Guerrilla world's own spawn resource, which the
world builder writes holding only the player if the people aren't built yet. For each patrol:
each `object_patrol`, the squad spawn node hung under it
(its orders Patrol, as Armageddon's own patrols' squads) and every navpoint its route reaches, handles
and links kept. Armageddon's own squad spawn and patrol code does the rest: the squad comes down when
its zone streams in and walks the route. Armageddon holds 24 patrols at once (Guerrilla 72), so the
builder keeps every 3 × 3 block of zones within that for now.

### guerrilla_gear

What Red Faction: Guerrilla's player gear needs from Armageddon's engine that it doesn't do itself.
So far:

- **Room for more inventory items.** Every weapon a player can carry has an inventory item of its
  own (`items_inventory.xtbl`; the game gives a weapon by its inventory item). The game keeps them in
  one table of 85 (`0x19F8578`, 0x30 bytes an item, filled while it starts by `0x7A7710`, which
  stops the game past 85: "Too many inventory item types"). Armageddon's own are 75, and its Path to
  War add-on's 4 more while one of its levels is loaded, so Guerrilla's weapons don't fit. The table
  is reached by its address in five instructions (the loader, the lookups by name and by name hash,
  the start-up constructor) and its size is in two (the loader's check, `cmp dword ptr
  [19F8570h],55h` at `0x7A77F0`, and the constructor's `push 55h` at `0xF2C338`): one-byte, signed
  values, so 127 is the most the table can grow to without changing the instructions' form. The mod
  moves it to a table of 127 of its own, every value checked against the game's first and none
  changed if one differs; the game's start-up constructor builds the new table's entries.
- **Guerrilla's player animations for its guns.** A character has one animation set; the game asks
  it for a state (standing, walking, aiming) or an action (firing, reloading) in the animation group
  of the gun held, and a set that hasn't one asks the set it builds on, then the group's parent group.
  Darius's set (`vs_player`) is also the one Armageddon's people build on, and Guerrilla's group names
  are Armageddon's (Enforcer and Railgun hold Armageddon's own guns too), so Guerrilla's animations are
  a set of their own, `rfg_player`, built on `vs_player` with the variant `confident` (Armageddon's
  variants are how a drunk or wounded civilian plays another set; nothing of Armageddon's uses this
  one). When the player takes up a gun the game sets his animation group
  (`character_instance::set_animation_group`, `0x969110`, from `human::equip`, `0x84DD70`); the mod
  first switches his set with the game's own variant setter (`0x98C310`): to `rfg_player` for one of
  the guns `mods\guerrilla_gear\player_weapons.txt` names, back to Darius's for any other. What
  `rfg_player` hasn't (dodging, free running, the Nano Forge) stays Darius's.
- **Room for every set's states and actions.** Each is one place in a map the game makes while it
  reads the sets (`0x97D6B0`), 6,344 places (a value in the game's data at `0x1107C58`), and its reader
  doesn't check: more spoil the memory after it. Armageddon's own sets and Guerrilla's vehicles' and
  people's come close, `rfg_player` adds about 650, so the mod raises the value to 16,384 just before
  the sets are read (`0x979C20`), if the Permanent Mempool it comes from (12 MB) has room, and logs how
  many are in use.
- **Guerrilla's dropped guns.** Guerrilla's NPCs carry the player's own guns, and one an NPC drops can
  be picked up as any other; what it carries is Guerrilla's own rule (`weapon::adjust_dropped_ammo`):
  the rounds it and its owner held × its `Dropped_ammo_scale` (0.4 for the EDF's) × the difficulty's
  "player ammo pickup" (Easy 2.0, Medium 1.0, Hard 0.9, Insane 0.8), a magazine of 0.5 to 1.5 × the
  scale of a full one (at least 1 round), the rest in reserve. Armageddon keeps the pickup rules (a gun
  you have gives ammo, else a free slot, else the action button swaps it for the one in hand) but not
  the numbers: its drop step (`0x731A10`) keeps a full magazine, its pickup gives the firing mode's
  `Rounds_On_Pickup`, and its ammo-pickup difficulty is 1.0 throughout. For Guerrilla's guns the mod
  runs Guerrilla's step in place of Armageddon's and, after the game's pickup (`0x7CAE20`, the items'
  `give_item` touch) or its adding a swapped-in gun (`0x85B3E0`), sets the player's magazine and
  reserve to those rounds through the game's own ammo store. Guerrilla's difficulty values come from
  your Guerrilla (`mods\guerrilla_gear\ammo_pickup.txt`, written with the weapons).
- **The animations**, `mods\guerrilla_gear\animations\` (`rfgpl_<Guerrilla's name>.anim_pc`): loaded
  once right after the sets and kept, each overlaid on its place in the animation library by name, as
  `guerrilla_people` loads its people's.
- **The player turns to where he walks.** Armageddon keeps its player facing the camera: its heading
  step (`0x84C6D0`, once a frame for each human on foot) turns the player toward the camera unless he
  sprints, and its movement step (`0x89D0C0`) turns a character toward where it goes only if its
  character entry lacks "always face target" (an Armageddon flag its player has). The mod wraps the
  heading step: for the player playing Darius's set (`vs_player`, in every level) or Mason's
  (`rfg_mason`), on foot, moving, not sprinting, not aiming (`0x8BB2F0`) and not attacking (the attack
  buttons, and 1.5 seconds after), it lifts that flag from his entry and lets no camera turn through,
  as the sprint does; the flag goes back as soon as he stops. Aiming, attacking or standing still, he
  faces the camera as before.
- **Room for the animations' triggers.** While the game reads its animation sets it copies each
  `anim_files.xtbl` entry's lists into five fixed pools, a record each, and never checks their room:
  effects 64, voice lines 320, sounds 960, props 256 and hand IKs 1,300. Armageddon's own nearly fill
  them, and Guerrilla's people, vehicles and player animations ran four of them over, spoiling the game's
  memory after them (the campaign stopped after a cutscene). Each pool's address is in two instructions,
  both in its reader, so the mod moves the five to bigger tables of its own (1,024, 4,096, 16,384,
  4,096 and 16,384), all or nothing, each place checked against the game's value first, and logs how
  full each is once the sets are read. `editor\GuerrillaBuilder.exe` refuses to write an
  `anim_files.xtbl` that would hold more than that (`AnimTriggerPools.cs`).
- **The camera's look-at bone.** A character entry keeps its skeleton's "camera" and "camera-lookat"
  bones (`+0x1E0`, `+0x1E4`, found by name when the character table is read, -1 for none). Darius's
  dodges, rolls and other moves turn the camera toward his camera-lookat bone, which they move. Mason's
  skeleton gets the same bone from the builder (below); should a player's skeleton still have none, the
  mod gives its entry the camera bone instead, so the camera (`0x4CFAD8`) never asks for bone -1.

`editor\GuerrillaBuilder.exe` (its weapons, `--weapons <names>`; code in `src\editor\RfaFormats\Guerrilla\GuerrillaWeapons.cs`,
`GuerrillaStaticMeshes.cs`, `GuerrillaTableFit.cs`) makes Guerrilla's weapons from the player's own copy of
Guerrilla (Guerrilla's `weapons.xtbl` names: `m16`, `enforcer`, `autoblaster`, `gauss_rifle`,
`rail_driver` by default; taking the weapons away in the builder takes them, and the people who carry them, away). For each:
- **Its item** (the model in the hand and on the ground, `items_3d.xtbl`), Guerrilla's own entry: the
  static mesh (`.csmesh_pc` version 4) written as Armageddon's version 6 (the header gains one word;
  the mesh block goes to version 9; the materials onto Guerrilla's shaders' rewrites, as the world's),
  in a container named as the item with its low-detail textures (from Guerrilla's
  `items_containers.cpeg_pc`), and its 512 × 512 textures with its high-detail material file
  (`.mat_pc` version 1 → 2) in `<item>_high`, which Armageddon loads for an item flagged
  `high_res_map` by that name. Its props (the magazine) likewise. Both containers are listed in an
  override of `items.vpp_pc\items_containers.asm_pc` (Armageddon's own list with ours added).
- **Its inventory item**, Guerrilla's, named as Guerrilla shows it (its strings file), and **an ammo
  type of its own** (`ammo.xtbl`): Guerrilla's ammo is each weapon's, Armageddon's shared by weapons
  of one type. Guerrilla's `Max_Rounds` counts the loaded magazine, Armageddon's `Max_Reserve` doesn't.
- **Its weapon entry**: Guerrilla's one flat entry written as Armageddon's one firing mode (Armageddon's
  own assault rifle is Guerrilla's, moved so, with the same numbers): the shot (a bullet, or a
  penetrating one with Guerrilla's flag), its trigger, magazine, spreads, ranges, damage at near and
  far range (a building takes the shot's damage, as in Guerrilla), refire and reload delays, recoil,
  muzzle flash, smoke and tracer (Guerrilla's own effects, which the world builder adds: build the
  world first), explosion, camera shake, and the flags both games have. A weapon firing several shots
  a round (the autoblaster) throws each into a cone round the aimed one in Guerrilla; Armageddon has no
  such cone, so the cone is added to each spread for now. The rail driver's scope sees through walls
  as in Guerrilla (Armageddon's `xray zoom`). Every entry carries an `_Editor` category: Armageddon's
  weapon reader requires one (it skips multiplayer override copies by it).
- **What it names that both games have under one name but differently** (the NPCs' firing pattern
  and aim drift, camera shakes): Guerrilla's under an `rfg_` name; one both have alike is used as it is.
- Names: Guerrilla's, or with `rfg_` before them where Armageddon has one of that name (`rfg_m16`,
  `rfg_enforcer`, `rfg_rail_driver`); a fixed Unique_ID of ours from 141 (saved games keep weapons by it).
- **The player's animations for them** (`GuerrillaPlayerAnimations.cs`): the set `rfg_player` with a
  group for each group the guns use (AK, Enforcer, Railgun so far). Each is Guerrilla's group as its
  game plays it, its own entries then those of the groups it builds on in Guerrilla's
  `anim_groups.xtbl` (the enforcer's are mostly the assault rifle's), cut to the names Armageddon's
  player asks for (its blend tree's and Darius's set's): the name's own entry, or where Guerrilla has
  none one that plays the same (Armageddon's `fire` is Guerrilla's `stand fire`, its upgraded reloads
  the plain ones, its turning on the spot the standing pose). Guerrilla blends its walking and running
  by speed in a table Armageddon can't read; Armageddon's player tree blends walking, running and
  sprinting itself, so each name takes that blend's animation for its speed. Where Armageddon has the
  animation itself on Darius (its developers moved many of Guerrilla's player animations over, under
  Guerrilla's names), its own is used by name; each other is moved from Mason's skeleton onto Darius's
  (`PlayerRetarget.cs`, learnt from those same pairs: Darius's root only moves with the hips and never
  turns, his pelvis turns as Mason's root), with Guerrilla's `anim_files.xtbl` entry (when a reload hands
  over the ammo, the hand's IK, the magazine moved between gun and hand), less what Armageddon's tables
  haven't (`Use_Shortest_Rotation`) and the sounds. The set's file goes in `overrides\build\pc\cache\`
  and into the shared `anim_set_filenames.xtbl` and `anim_files.xtbl` in `mods\guerrilla_world\`
  (the vehicles' and people's tools keep each other's entries; this one's are recorded in
  `mods\guerrilla_gear\anim_entries.txt`).
- **A set for a player on Mason's own skeleton**, `rfg_mason`, built with it: every group of Guerrilla's
  player (18), each made the same way, its animations as Guerrilla has them (no skeleton to move them
  onto: `rfgm_<Guerrilla's name>`, in the same folder), and no set under it (Darius's animations can't
  play on Mason's bones). A character with Mason's skeleton names it in its `Animation_Sets`
  (`rfg_mason.xtbl`). Armageddon's own moves come with it (dodging, free running, the Nano Forge, its
  own guns): every group and name of Darius's set Mason's hasn't, with Darius's animation moved onto
  Mason's skeleton (`rfgmd_<its name>`, `PlayerRetarget.cs`), or Guerrilla's own where Darius's is one
  of Guerrilla's. How each of Mason's bones follows Darius's is learnt each build from the player
  animations Armageddon's developers moved from Guerrilla onto Darius themselves (Armageddon's
  `anims.vpp_pc` has them under Guerrilla's names): a source bone and one constant turn for each, fitted
  on those pairs (Mason's root is his hips; Darius's root never turns). Moved back, those pairs land
  within half a degree of Guerrilla's originals at the spine, head, feet and hands. Armageddon's
  mission-only moves (its cutscene and mission props) aren't in its preloaded animations and are left out.
  Mason's skeleton gets Darius's camera-lookat bone after his own (`PlayerRetarget.WithCameraLookat`,
  applied by the people's build and checked by this one), and Armageddon's moves keep Darius's track for
  it, so the camera follows a dodge or roll as it does Darius's. Jumping is Armageddon's too: its
  controller lifts the player and its jump clips keep him on the ground, while Guerrilla's carry their
  own small hop in the character's movement, which held Mason to it; every jump, fall and landing name
  of Mason's groups takes Darius's clip (moved onto him), through the game's group parents.
  Neither game has its player walk with nothing in his hands (Mason always carries his sledgehammer,
  Darius his), so every name Mason's hammer group has and his Default group (what the game plays with no
  weapon) lacks goes into Default: empty-handed he moves as with the hammer.
The seven tables (`weapons`, `items_3d`, `items_inventory`, `ammo`, `weapon_firing_patterns`,
`aim_drift`, `camera_shake`) are Armageddon's with Guerrilla's entries added, in `mods\guerrilla_world\`,
which guerrilla_world hands the game at start-up. Not yet: the sounds (Guerrilla's are XACT cues), the
melee moves, the bullets' impact effects, the HUD icons, Guerrilla's facial animations for the
player, and the weapons modelled as characters (pistol, shotgun, sniper rifle).

## The Guerrilla builder

`editor\GuerrillaBuilder.exe` (code in `src\editor\GuerrillaBuilder\`) makes everything of
Guerrilla's that the guerrilla_* mods use, from the player's own copy of Guerrilla, into the mod
folder (`overrides\` and `mods\`); nothing it makes is ever shipped. The converting is
`RfaFormats`' (`Guerrilla\GuerrillaWorld.cs`, `Interface\GuerrillaHud.cs`,
`Guerrilla\GuerrillaArsenal.cs`, `GuerrillaWeapons.cs`, `GuerrillaPeople.cs`); the builder finds
the folders and runs them in order:
- `Setup.cs`: the mod folder (the one above `editor\`, with `rfa_mods.ini`), Armageddon (`GameDir`,
  or the mod folder when the mod is in the game folder) and Guerrilla (`GuerrillaDir`, or else the
  Steam Edition in the Steam library Armageddon is in, or another that library's
  `libraryfolders.vdf` names: only files are read).
- `Parts.cs`: the five parts in their order (world, HUD, vehicles, weapons, people); what each needs
  built first (the vehicles and weapons name the world's effect and explosion tables, the people
  carry the weapons), which a build adds when they aren't built; and what goes with a part taken
  away (everything with the world, the people with the weapons), last built first. It records each
  part in `mods\guerrilla_world\builder.ini` once it's finished (when, by which release from
  `version.txt`, with which options): a part that is there but not recorded (left half-made, or
  built by an older RFA-Mod), built by another release, or a world of another format (`world.txt`)
  shows as needing building again. The four programs it took the place of (GuerrillaWorld,
  GuerrillaHud, GuerrillaArsenal and GuerrillaPeople) are deleted from `editor\` when it starts.
- `MainWindow.xaml`: the window, in the editor's look (the editor's `Theme.xaml` and
  `DarkTitleBar.cs`, linked in): the two folders, a row a part with how it stands (ticked when it
  isn't built or needs building again), Build and Remove. The work runs on another thread; its
  lines also go to `logs\guerrilla_builder.log`, with the details of an error.
- `CommandLine.cs`: given a command it runs without a window: `build [<parts>] [<options>]`,
  `remove [<parts>]`, `status` and `help`. The options are the parts' own (`--zones`, `--props`,
  `--props-in`, `--vehicles`, `--weapons`, `--characters`) and `--guerrilla <folder>`; one given
  without naming parts builds only its part. Being a window program, it writes to the console it
  was started from, or to the pipe or file its output goes to; a command prompt waits for it only
  with `start /wait`, PowerShell only when its output is piped.

## The editor

The code is in `src\editor\`: `RfaFormats` reads and writes the formats (no UI, reusable by other
tools) and `RfaEditor` is the editor:
- `MainWindow`: the layout and explorer.
- `Documents\`: the tabs (file preview, table editor, level editor).
- `Level\`: the level view's camera, gizmo, scene, grid, kinds of objects, inspector parts, level
  geometry and the ground's painter.
- `Render\`: the Direct3D 11 view.
- `Theme.xaml`: the dark look of every control (with `Theme.cs` for code).
- `Icons.cs`: the icons (characters of the Segoe MDL2 Assets font that comes with Windows) and
  the kinds of files.
- `NumberField.cs`: the draggable number box.

The editor's builds name no folder of the PC they were built on: `src\editor\Directory.Build.props`
writes `/_/` in place of the project's folder in every path the compiler stores (source files and
`.pdb`), and `tools\package.ps1` checks for that. The editor is built 64-bit; a 32-bit one runs
out of memory on big levels.

**Tables**: the game's tables describe their own fields (the `<TableDescription>` Volition's own
table editor used), and the table editor is built from that description rather than from
anything hard-coded. Saving writes back only what changed; the rest is written back byte for
byte, in the game's own formatting.

**Containers**: most textures and zones are inside `.str2_pc` containers, which the game streams
in whole. To change one, the editor rebuilds the container into `overrides\`, compressed the way
Volition's tools did, and updates the container's entries in the asset lists (`.asm_pc`) that
tell the game how big it is, including the group entries (such as `1_c0812`) that the game sets
memory aside by. Many zones are in the game twice, identically (in a `zonescript_*.vpp_pc` archive
and in their level's container); the game may read either, so the editor saves every copy.

**The zone schema**: the game still has the schema of Volition's level editor (CLOE),
`rfg_zonex_format.txt` in `misc.vpp_pc`: every class of zone object (69, with `edit_obj` the base
they share) and its properties' types, defaults, choices (some read from a table, such as
`squads.xtbl`) and markers (editor-only, read-only, hidden). `RfaFormats\Zones\ZoneSchema.cs`
reads it from the player's copy of the game (it is never shipped). Its names hash as the zones' do,
and it was checked against every zone and layer in the game: it names all 42 classes the zones use
and 269 of their 294 properties (the rest were added by Volition's tools: `oP`, a navpoint's data,
links, destruction data), every value has the type and size the schema gives, and an object made
from it has the same properties as the game's own objects of its class. The inspector edits each
property by its type, and Add makes new objects from it.

**Saving zones**: the game's object table (the zone's relation table) is built again on saving, as
the game's tools do. Deleting an object also removes its destruction links to the buildings next
to it (the game's table of which pieces hold onto which, renumbered as the game's own files number
it). A copied object is a whole breakable object, with everything attached and the destruction
links holding the pieces together, each with a handle of its own.

The add-objects freeze (found in the game on 2026-10-31): with objects added to `world_08_03` the
game froze, stuck in its breakable objects' destruction links. The zone had grown, but a group
entry of the asset list still gave its old size, so the end of the zone ran into other memory.
Fixed (saving again mends a list saved before): a new game with four copied tankers in
`world_08_03` then loaded and ran, with all four copies in place.

**Speed**: the level view draws objects in batches (every object of one kind with one texture, in
one 64 m square of the map, joined into one mesh), the level geometry and the zones around joined
per texture, and each model's parts joined per texture. A zone is a few hundred pieces for WPF to
draw each frame instead of over ten thousand (`world_08_02`: 523, was 10,201). A click still picks
the one object under the mouse (found by the triangle hit), and an object that moves is taken out
of its batch and drawn on its own. The log says how long each part of a zone took to load.

**Level geometry and foliage**: for the first mission's `world_08_02`, the stitch list places 865
pieces including the Terraformer, its platform and the ring walls. Foliage (in 11 zones) is scattered exactly
as the game does it and set down on what is below; pieces with no surface in their 1 m cube are left
out, as the game does: 336 of 495 are drawn in `world_08_02`.

**Direct3D view**: draws the level with Direct3D 11 on the graphics card instead of WPF, the first
step of [renderer-plan.md](renderer-plan.md). It shows the same things from the same camera, worked out for
every pixel, and only draws again when something changed (a frame takes the editor 2–3 ms). It uses the Vortice
libraries (MIT; see [third-party-notices.txt](third-party-notices.txt)).

**Game light** uses the game's own formulas, read from its shaders: lamps (spot, omni, tube and
ambient, with their colours, fades and cones), the environment box's ambient light, fog and
exposure, the game's tone curve, and materials (normal, specular and glow maps, colour multiplier,
alpha and cut-outs, the Legion's alien skin shader). The game gives shadows to 561 lamps; the
editor casts them as the game does.

**Handles**: handles and positions in the editor are the game's own, so a building picked in the
game (`0x820300F9`, `str_sur_edf_qm_01` at 90.4, -4.7, 1306.6) is the same object at the same place
in the editor.

What the editor still lacks, measured against Volition's own tools, is in
[editor-roadmap.md](editor-roadmap.md).

## Writing a mod

Create `src\mods\<name>\<name>.cpp`, define `RFA_MOD_NAME`, include `rfa_mod.h`, and export:

```cpp
extern "C" __declspec(dllexport) void __cdecl RFAMod_Init() { /* install hooks */ }
```

- `rfa_mod.h` provides `rfa::Log`, `rfa::IniInt`/`IniString`, `rfa::HookImport` (import table),
  `rfa::ReplaceFunction` (send a game function to a C++ function with the same signature) and
  `rfa::HookFunction` (the same, keeping a pointer to call the original).
- `rfa_game.h` has `rfa::game::IsSupportedBuild()` (check it before touching game code) and the
  game's Lua API.
- Write hooks in plain C++: match the game function's calling convention (`__cdecl`, or
  `__fastcall` for `this`-in-`ecx` methods without stack arguments) instead of writing assembly.
- Two `HookFunction`s on one address don't chain, so check no other mod hooks it first.
- Export `RFAMod_SettingChanged(section, key)` to hear about settings changed from the game's
  menus.
- Export `RFAMod_RegisterLua(lua_State*)` to give the interface's scripts functions of your own
  (`rfa::game::lua_register`; push results with `lua_pushnumber`, `lua_pushstring`,
  `lua_pushboolean`).
- Export `RFAMod_Menu(const RFAMenu*)` to add menus and windows to the in-game menus, drawn with
  Dear ImGui (`rfa_menu.h`: start with `rfa::menu::Use(menu)`, and build ImGui's four core files
  with the mod, with `NDEBUG`, as `build.cmd` does for `rfa_qol`).
- Add a `cl` line for it to `build.cmd`. Disable a mod with `<name>=0` under `[Mods]`.
- One DLL can hold several mods, each in a source file of its own with its own `RFA_MOD_NAME`, as
  `rfa_qol` does: its `<name>.cpp` exports `RFAMod_Init` and starts the parts.

## Building, packaging and releasing

`build.cmd` needs Visual Studio with the C++ desktop workload, and the .NET 10 SDK for the editor
(skipped if it's missing). It builds the launcher, loader and mods 32-bit (the game is a 32-bit
process) into `bin\`, copies `src\overrides\` into `bin\overrides\`, and copies
`config\rfa_mods.ini` and `config\console.ini` to `bin\` only if there are none there yet, and to
`bin\defaults\` every time (the templates the launcher adds new settings from to yours). It
builds the editor and the Guerrilla builder 64-bit into `bin\editor\`. The build turns off the
.NET SDK's usage reporting and its first-run web certificate.

`tools\package.ps1` makes the copy to give others, in `dist\RFA-Mod\` and as
`dist\RFA-Mod-<date>.zip`:
- It holds the launcher, the loader, the mods (and their own scripts, from `src\mods\`; never
  files made from your copy of Guerrilla), the mod's own overrides (from `src\overrides\`, not the
  files you edited), the editor (without its `.pdb` debugging files), the README with the documents
  it links to (named one by one: `docs\` also holds private notes) and the licence, with the
  settings templates from `config\` (so `GameDir=` is empty), twice: as `rfa_mods.ini` and
  `console.ini`, and in `defaults\`.
- Your own `rfa_mods.ini`, `console.ini`, `video_settings.cfg`, `logs\`, `link\` and edited
  overrides stay out.
- Every file it packs is checked for anything naming your PC (your user folder or name, this
  project's folder, your game folder), and nothing is packaged if one does.
- `-Build` runs `build.cmd` first; `-NoEditor` leaves the editor out; `-Version v0.1.0-beta` makes
  a release: the zip is named `RFA-Mod-v0.1.0-beta.zip` and its `version.txt` says which commit it
  was built from.

**Releases** on GitHub are built by GitHub itself (`.github\workflows\release.yml`): pushing a tag
that starts with `v` builds that commit on a clean Windows machine with `build.cmd`, packs it with
`tools\package.ps1 -Version <tag>` and publishes the zip as a release, a pre-release when the tag
has a `-` in it (`v0.1.0-beta`). Its text comes from `docs\releases\<tag>.md`. To release:

```
git tag -a v0.3.0-beta -m "RFA-Mod v0.3.0-beta"
git push origin v0.3.0-beta
```

The notes' updating paragraph, from the first release with `defaults\` on:

> **Updating from vX:** copy the new files over your old ones, but keep your own `rfa_mods.ini`
> and `console.ini`. The launcher adds this version's new settings to them the next time it
> starts, keeping your values: (name each new setting and its section, or say there are none).

## Checking the game files are untouched

```
powershell -ExecutionPolicy Bypass -File tools\game-integrity.ps1 -Mode Verify
```

This compares every file in the game folder with the SHA-256 snapshot of the publicbeta install,
`reference\build-1932726-publicbeta\game_baseline.csv`. `reference\build-1291145\game_baseline.csv`
is the default branch, taken before the first modded launch. Files the beta writes itself
(`console_commands.txt`, `rfg_last_level_pc.txt`, `Load_Successful.txt`) are listed as `GAME`, not
counted as changes.
