@echo off
rem Builds the launcher, loader and mods into bin\ (32-bit, matching the game), and the editor
rem into bin\editor\.
rem   build.cmd             into bin\, the copy you play
rem   build.cmd <folder>    into another folder instead, such as a staging copy of bin\ that
rem                         tools\stage.ps1 makes (stage\<name>\): its build leftovers go to
rem                         build\stage-<name>\, so several builds can run at once.
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%bin"
set "OBJ=%ROOT%build"
if not "%~1"=="" set "OUT=%~f1"
if not "%~1"=="" set "OBJ=%ROOT%build\stage-%~nx1"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR (echo Visual Studio C++ tools not found & exit /b 1)
set "PATH=%PATH%;%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"
call "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" x86 >nul || exit /b 1

if not exist "%OUT%\mods" mkdir "%OUT%\mods"
if not exist "%OBJ%" mkdir "%OBJ%"
if not exist "%OUT%\rfa_mods.ini" copy /y "%ROOT%config\rfa_mods.ini" "%OUT%\rfa_mods.ini" >nul

set CFLAGS=/nologo /std:c++17 /permissive- /O2 /MT /W3 /EHsc /DUNICODE /D_UNICODE /I"%ROOT%src\common"
pushd "%OBJ%"

cl %CFLAGS% "%ROOT%src\launcher\launcher.cpp" /Fe"%OUT%\RFAModLauncher.exe" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib || goto :fail
rem Dear ImGui (src\third_party\imgui), for the in-game menus: the loader hosts it with its
rem Direct3D 11 and Win32 backends; a mod with menus is built with its core files. NDEBUG leaves out
rem its debug checks, which would stop the game with a dialog.
set "IMGUI=%ROOT%src\third_party\imgui"
set IMGUI_CORE="%IMGUI%\imgui.cpp" "%IMGUI%\imgui_draw.cpp" "%IMGUI%\imgui_tables.cpp" "%IMGUI%\imgui_widgets.cpp"
cl %CFLAGS% /DNDEBUG /I"%IMGUI%" /LD "%ROOT%src\loader\loader.cpp" "%ROOT%src\loader\menus.cpp" %IMGUI_CORE% "%IMGUI%\backends\imgui_impl_dx11.cpp" "%IMGUI%\backends\imgui_impl_win32.cpp" /Fe"%OUT%\rfa_loader.dll" /link /IMPLIB:rfa_loader.lib user32.lib || goto :fail
rem rfa_qol.dll: the quality-of-life mods (borderless, console, fov, video_settings, spawner), one
rem source file each, in one DLL. The loader skips their old DLLs if an older build left them in bin\mods.
cl %CFLAGS% /DNDEBUG /I"%IMGUI%" /LD "%ROOT%src\mods\rfa_qol\*.cpp" %IMGUI_CORE% /Fe"%OUT%\mods\rfa_qol.dll" /link /IMPLIB:rfa_qol.lib user32.lib || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_world\guerrilla_world.cpp" /Fe"%OUT%\mods\guerrilla_world.dll" /link /IMPLIB:guerrilla_world.lib || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_hud\guerrilla_hud.cpp" /Fe"%OUT%\mods\guerrilla_hud.dll" /link /IMPLIB:guerrilla_hud.lib || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_vehicles\*.cpp" /Fe"%OUT%\mods\guerrilla_vehicles.dll" /link /IMPLIB:guerrilla_vehicles.lib || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_people\guerrilla_people.cpp" /Fe"%OUT%\mods\guerrilla_people.dll" /link /IMPLIB:guerrilla_people.lib || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_save\guerrilla_save.cpp" /Fe"%OUT%\mods\guerrilla_save.dll" /link /IMPLIB:guerrilla_save.lib || goto :fail
rem guerrilla_campaign.dll: Guerrilla's alert level (and, being made, the rest of its campaign's systems).
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_campaign\*.cpp" /Fe"%OUT%\mods\guerrilla_campaign.dll" /link /IMPLIB:guerrilla_campaign.lib || goto :fail
rem guerrilla_gear.dll: Guerrilla's weapons' room, hammer and movement, and its upgrades' F7 window (ImGui).
cl %CFLAGS% /DNDEBUG /I"%IMGUI%" /LD "%ROOT%src\mods\guerrilla_gear\*.cpp" %IMGUI_CORE% /Fe"%OUT%\mods\guerrilla_gear.dll" /link /IMPLIB:guerrilla_gear.lib user32.lib || goto :fail
rem guerrilla_hud's scripts, which editor\GuerrillaBuilder.exe packs with Guerrilla's HUD documents.
xcopy /i /y /q "%ROOT%src\mods\guerrilla_hud\*.lua" "%OUT%\mods\guerrilla_hud" >nul || goto :fail

popd
rem overrides\ mirrors the game folder (one folder per archive), so copy the whole tree.
xcopy /e /i /y /q "%ROOT%src\overrides" "%OUT%\overrides" >nul || goto :fail
rem console.ini is yours to edit: only copy the template if there is none yet.
if not exist "%OUT%\console.ini" copy /y "%ROOT%config\console.ini" "%OUT%\console.ini" >nul
rem The templates again, in bin\defaults\: each time it starts, the launcher adds the settings they
rem have and your bin\rfa_mods.ini and bin\console.ini lack, keeping your values.
if not exist "%OUT%\defaults" mkdir "%OUT%\defaults"
copy /y "%ROOT%config\rfa_mods.ini" "%OUT%\defaults\rfa_mods.ini" >nul || goto :fail
copy /y "%ROOT%config\console.ini" "%OUT%\defaults\console.ini" >nul || goto :fail

rem The editor is C# and needs the .NET 10 SDK. Its build leftovers go to build\editor. The
rem DOTNET_* settings turn off the SDK's usage reporting and its first-run web certificate.
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
set DOTNET_GENERATE_ASPNET_CERTIFICATE=false
rem vcvarsall (for the 32-bit C++ parts above) sets Platform=x86, which MSBuild would take for the
rem editor too and build it 32-bit, with 2 GB of memory: too little for big levels. Clear it.
set Platform=
where dotnet >nul 2>nul || (echo Skipping the editor: the .NET 10 SDK is not installed & goto :done)
dotnet build "%ROOT%src\editor\RfaEditor\RfaEditor.csproj" -c Release -o "%OUT%\editor" --artifacts-path "%OBJ%\editor" -v q -nologo || goto :fail
rem GuerrillaBuilder.exe, next to the editor: makes Guerrilla's world, HUD, vehicles, weapons and people
rem usable from your copy of Guerrilla. It took the place of four programs, whose files go from bin\editor.
for %%t in (GuerrillaWorld GuerrillaHud GuerrillaArsenal GuerrillaPeople) do for %%e in (exe dll deps.json runtimeconfig.json pdb) do if exist "%OUT%\editor\%%t.%%e" del /q "%OUT%\editor\%%t.%%e"
dotnet build "%ROOT%src\editor\GuerrillaBuilder\GuerrillaBuilder.csproj" -c Release -o "%OUT%\editor" --artifacts-path "%OBJ%\editor" -v q -nologo || goto :fail

:done
echo Build OK: %OUT%
exit /b 0
:fail
popd
echo BUILD FAILED
exit /b 1
