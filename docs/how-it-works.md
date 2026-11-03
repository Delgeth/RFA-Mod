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
2. The loader loads every DLL in `mods\` (alphabetically) and calls its `RFAMod_Init` export.
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
src\mods\<name>\          one folder per mod DLL (borderless, console, fov, guerrilla_hud,
                          guerrilla_world, video_settings), and a mod's own scripts
src\common\               rfa_mod.h (logging, ini, hooking helpers), rfa_game.h (build check, Lua API)
src\overrides\            replacements for files inside the game's archives, same layout as the game
src\editor\RfaFormats\    C# library that reads and writes the game's file formats (no UI)
src\editor\RfaEditor\     the editor (C#, WPF desktop app); built into bin\editor\
src\editor\GuerrillaHud\  GuerrillaHud.exe: makes Guerrilla's HUD for guerrilla_hud; built into bin\editor\
src\editor\GuerrillaWorld\ GuerrillaWorld.exe: builds the world rfg for guerrilla_world; built into bin\editor\
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
`RFAMod_SettingChanged(section, key)` in every mod that exports it.

## The mods

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
through the Lua bridge's `rfa_get_setting` / `rfa_set_setting`, and the fov mod picks up the change
through `RFAMod_SettingChanged`. The game's slider code assumes every slider starts at 0, so the
override maps mouse clicks and drags itself.

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
  `console.dll` was compiled. It is also written to `logs\rfa_mods.log`.
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
  then draws Guerrilla's fog over what the world has drawn, before the sky: each pixel's distance
  and height from the G-buffer's depth (noted during the light pass), and the fog worked out there
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
  draws Guerrilla's own from the scene it read: the eye's brightness `L` measured on a grid of the
  scene (4.5 × its luminance) and eased toward at the district's `iris_rate`; the exposure
  `E = range / max(1 + offset × (base + amount × (L − base)), 0.1)` from the time of day's
  `tonemap_lum_range`, `tonemap_lum_offset`, `eye_adaption_base` and `eye_adaption_amount`, which
  the world builder writes; then `1 − (1 − particles) × e^(−E × (colour × (1 − cover) + bloom + sun
  shafts))` (the particles below; without them `1 − e^(−E × (colour + bloom + sun shafts))`) and the
  sRGB curve. `Exposure=` scales `E` (1 is Guerrilla's). The sky's brightness setting is a fifth under
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
  effects' three tables (`vfx.xtbl`, `effects.xtbl`, `level_objects.xtbl`, below).

The world itself is built by `editor\GuerrillaWorld.exe` (code in `src\editor\GuerrillaWorld\` and
`src\editor\RfaFormats\Guerrilla\`), from the player's own copy of Guerrilla. It writes the world
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
  entries are left as they are.
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
  (Guerrilla's are XACT cues; Armageddon uses Wwise). Each visual effect's file is converted
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

`editor\GuerrillaHud.exe` (code in `src\editor\GuerrillaHud\` and
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
- `--remove` takes all of it away again. Since that sheet table now holds the pictures of
  Armageddon's own HUD too, it must never be deleted alone.

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
- Add a `cl` line for it to `build.cmd`. Disable a mod with `<name>=0` under `[Mods]`.

## Building, packaging and releasing

`build.cmd` needs Visual Studio with the C++ desktop workload, and the .NET 10 SDK for the editor
(skipped if it's missing). It builds the launcher, loader and mods 32-bit (the game is a 32-bit
process) into `bin\`, copies `src\overrides\` into `bin\overrides\`, and copies
`config\rfa_mods.ini` and `config\console.ini` to `bin\` only if there are none there yet, and to
`bin\defaults\` every time (the templates the launcher adds new settings from to yours). It
builds the editor and the two Guerrilla tools 64-bit into `bin\editor\`. The build turns off the
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
