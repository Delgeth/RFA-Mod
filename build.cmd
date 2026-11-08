@echo off
rem Builds the launcher, loader and mods into bin\ (32-bit, matching the game), and the editor
rem into bin\editor\.
rem   build.cmd             into bin\, the copy you play
rem   build.cmd <folder>    into another folder instead, such as a staging copy of bin\ that
rem                         tools\stage.ps1 makes (stage\<name>\): its build leftovers go to
rem                         build\stage-<name>\, so several builds can run at once.
rem What the compilers say goes to build\build.log (shown if the build fails).
rem
rem The project folder is reached through a drive letter of its own (subst) while it builds: MSBuild
rem can't copy into a folder whose path has an apostrophe (C:\Users\Jo's PC\...), and Visual Studio's
rem vcvarsall.bat can stop there too. vcvarsall runs in a cmd of its own, from that drive, and the
rem settings it makes are taken from there.
setlocal
set "HERE=%~dp0"
set "STAGE=%~1"
set "SHOWN=%~dp0bin"
if not "%~1"=="" set "SHOWN=%~f1"
set "STAGENAME=%~nx1"
if not "%STAGE%"=="" set "STAGE=%~f1"
if not exist "%HERE%build" mkdir "%HERE%build"
set "LOG=%HERE%build\build.log"
echo Build %DATE% %TIME% > "%LOG%"

rem Visual Studio's C++ tools: vswhere (any edition, Build Tools too, previews too), else the usual folders.
set "VCVARS="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do if exist "%%i\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARS=%%i\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VCVARS if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -property installationPath`) do if exist "%%i\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARS=%%i\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VCVARS for /d %%v in ("%ProgramFiles%\Microsoft Visual Studio\*" "%ProgramFiles(x86)%\Microsoft Visual Studio\*") do for /d %%e in ("%%~v\*") do if exist "%%~e\VC\Auxiliary\Build\vcvarsall.bat" set "VCVARS=%%~e\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VCVARS (echo Visual Studio C++ tools not found: no vcvarsall.bat under Program Files\Microsoft Visual Studio & exit /b 1)
echo vcvarsall: %VCVARS% >> "%LOG%"

set "DRIVE="
for %%d in (R S T U V W X Y Z Q P O N M) do if not defined DRIVE if not exist %%d:\ set "DRIVE=%%d:"
if not defined DRIVE (echo No free drive letter to build through & exit /b 1)
subst %DRIVE% "%HERE:~0,-1%" >> "%LOG%" 2>&1
if not exist %DRIVE%\build.cmd (echo Couldn't map %DRIVE% to the project folder & exit /b 1)
set "ROOT=%DRIVE%\"
set "OUT=%ROOT%bin"
set "OBJ=%ROOT%build"
if not "%STAGE%"=="" set "OUT=%STAGE%"
if not "%STAGE%"=="" set "OBJ=%ROOT%build\stage-%STAGENAME%"
cd /d %ROOT%

rem vcvarsall x86 in a cmd of its own; its settings (set) into a file, then into this one.
set "ENVFILE=%ROOT%build\vcvars-env.txt"
cmd /d /c ""%VCVARS%" x86 >> "%LOG%" 2>&1 && set > "%ENVFILE%""
if not exist "%ENVFILE%" (echo vcvarsall failed >> "%LOG%" & goto :fail_unmapped)
for /f "usebackq tokens=1* delims==" %%a in ("%ENVFILE%") do set "%%a=%%b"
del "%ENVFILE%" >nul 2>&1
where cl >> "%LOG%" 2>&1 || (echo vcvarsall didn't give the C++ compiler cl.exe >> "%LOG%" & goto :fail_unmapped)

if not exist "%OUT%\mods" mkdir "%OUT%\mods"
if not exist "%OBJ%" mkdir "%OBJ%"
if not exist "%OUT%\rfa_mods.ini" copy /y "%ROOT%config\rfa_mods.ini" "%OUT%\rfa_mods.ini" >nul

set CFLAGS=/nologo /std:c++17 /permissive- /O2 /MT /W3 /EHsc /DUNICODE /D_UNICODE /I"%ROOT%src\common"
pushd "%OBJ%"

echo Building the launcher, loader and mods ...
cl %CFLAGS% "%ROOT%src\launcher\launcher.cpp" /Fe"%OUT%\RFAModLauncher.exe" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib >> "%LOG%" 2>&1 || goto :fail
rem Dear ImGui (src\third_party\imgui), for the in-game menus: the loader hosts it with its
rem Direct3D 11 and Win32 backends; a mod with menus is built with its core files. NDEBUG leaves out
rem its debug checks, which would stop the game with a dialog.
set "IMGUI=%ROOT%src\third_party\imgui"
set IMGUI_CORE="%IMGUI%\imgui.cpp" "%IMGUI%\imgui_draw.cpp" "%IMGUI%\imgui_tables.cpp" "%IMGUI%\imgui_widgets.cpp"
cl %CFLAGS% /DNDEBUG /I"%IMGUI%" /LD "%ROOT%src\loader\loader.cpp" "%ROOT%src\loader\menus.cpp" %IMGUI_CORE% "%IMGUI%\backends\imgui_impl_dx11.cpp" "%IMGUI%\backends\imgui_impl_win32.cpp" /Fe"%OUT%\rfa_loader.dll" /link /IMPLIB:rfa_loader.lib user32.lib >> "%LOG%" 2>&1 || goto :fail
rem rfa_qol.dll: the quality-of-life mods (borderless, console, fov, video_settings, spawner), one
rem source file each, in one DLL. The loader skips their old DLLs if an older build left them in bin\mods.
cl %CFLAGS% /DNDEBUG /I"%IMGUI%" /LD "%ROOT%src\mods\rfa_qol\*.cpp" %IMGUI_CORE% /Fe"%OUT%\mods\rfa_qol.dll" /link /IMPLIB:rfa_qol.lib user32.lib >> "%LOG%" 2>&1 || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_world\guerrilla_world.cpp" /Fe"%OUT%\mods\guerrilla_world.dll" /link /IMPLIB:guerrilla_world.lib >> "%LOG%" 2>&1 || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_hud\guerrilla_hud.cpp" /Fe"%OUT%\mods\guerrilla_hud.dll" /link /IMPLIB:guerrilla_hud.lib >> "%LOG%" 2>&1 || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_vehicles\*.cpp" /Fe"%OUT%\mods\guerrilla_vehicles.dll" /link /IMPLIB:guerrilla_vehicles.lib >> "%LOG%" 2>&1 || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_people\guerrilla_people.cpp" /Fe"%OUT%\mods\guerrilla_people.dll" /link /IMPLIB:guerrilla_people.lib >> "%LOG%" 2>&1 || goto :fail
cl %CFLAGS% /LD "%ROOT%src\mods\guerrilla_save\guerrilla_save.cpp" /Fe"%OUT%\mods\guerrilla_save.dll" /link /IMPLIB:guerrilla_save.lib >> "%LOG%" 2>&1 || goto :fail
rem guerrilla_gear.dll: Guerrilla's weapons' room, hammer and movement, and its upgrades' F7 window (ImGui).
cl %CFLAGS% /DNDEBUG /I"%IMGUI%" /LD "%ROOT%src\mods\guerrilla_gear\*.cpp" %IMGUI_CORE% /Fe"%OUT%\mods\guerrilla_gear.dll" /link /IMPLIB:guerrilla_gear.lib user32.lib >> "%LOG%" 2>&1 || goto :fail
rem guerrilla_hud's scripts, which editor\GuerrillaBuilder.exe packs with Guerrilla's HUD documents.
xcopy /i /y /q "%ROOT%src\mods\guerrilla_hud\*.lua" "%OUT%\mods\guerrilla_hud" >nul || goto :fail

popd
rem overrides\ mirrors the game folder (one folder per archive), so copy the whole tree.
xcopy /e /i /y /q "%ROOT%src\overrides" "%OUT%\overrides" >nul || goto :fail_unpopped
rem console.ini is yours to edit: only copy the template if there is none yet.
if not exist "%OUT%\console.ini" copy /y "%ROOT%config\console.ini" "%OUT%\console.ini" >nul
rem The templates again, in bin\defaults\: each time it starts, the launcher adds the settings they
rem have and your bin\rfa_mods.ini and bin\console.ini lack, keeping your values.
if not exist "%OUT%\defaults" mkdir "%OUT%\defaults"
copy /y "%ROOT%config\rfa_mods.ini" "%OUT%\defaults\rfa_mods.ini" >nul || goto :fail_unpopped
copy /y "%ROOT%config\console.ini" "%OUT%\defaults\console.ini" >nul || goto :fail_unpopped

rem The editor is C# and needs the .NET 10 SDK. Its build leftovers go to build\editor, made afresh
rem each time (they keep the path they were made through, and the drive letter can change). The
rem DOTNET_* settings turn off the SDK's usage reporting and its first-run web certificate.
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
set DOTNET_GENERATE_ASPNET_CERTIFICATE=false
rem vcvarsall (for the 32-bit C++ parts above) sets Platform=x86, which MSBuild would take for the
rem editor too and build it 32-bit, with 2 GB of memory: too little for big levels. Clear it.
set Platform=
where dotnet >nul 2>nul || (echo Skipping the editor: the .NET 10 SDK is not installed & goto :done)
echo Building the editor ...
if exist "%OBJ%\editor" rd /s /q "%OBJ%\editor"
dotnet build "%ROOT%src\editor\RfaEditor\RfaEditor.csproj" -c Release -o "%OUT%\editor" --artifacts-path "%OBJ%\editor" -v q -nologo -nodeReuse:false >> "%LOG%" 2>&1 || goto :fail_unpopped
rem GuerrillaBuilder.exe, next to the editor: makes Guerrilla's world, HUD, vehicles, weapons and people
rem usable from your copy of Guerrilla. It took the place of four programs, whose files go from bin\editor.
for %%t in (GuerrillaWorld GuerrillaHud GuerrillaArsenal GuerrillaPeople) do for %%e in (exe dll deps.json runtimeconfig.json pdb) do if exist "%OUT%\editor\%%t.%%e" del /q "%OUT%\editor\%%t.%%e"
dotnet build "%ROOT%src\editor\GuerrillaBuilder\GuerrillaBuilder.csproj" -c Release -o "%OUT%\editor" --artifacts-path "%OBJ%\editor" -v q -nologo -nodeReuse:false >> "%LOG%" 2>&1 || goto :fail_unpopped

:done
cd /d "%HERE%"
subst %DRIVE% /d
echo Build OK: %SHOWN%
exit /b 0
:fail
popd
:fail_unpopped
:fail_unmapped
cd /d "%HERE%"
subst %DRIVE% /d
type "%LOG%"
echo BUILD FAILED (build\build.log)
exit /b 1
