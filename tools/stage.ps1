# Builds your work into a staging copy of bin\ of your own, so several people (or Claude sessions) can
# build and check at the same time without touching bin\ or waiting for each other. bin\, the copy the
# game is played from, is changed only by tools\install-bin.ps1, once, when it's time to test.
#
#   powershell -ExecutionPolicy Bypass -File tools\stage.ps1 -Name <name> [-Parts <parts>] [-NoMirror]
#
#   1. Mirrors bin\ into stage\<name>\: everything but logs\ (the first time all of it, about 2 GB;
#      after that only what changed). So the staging copy starts from what bin\ has now.
#   2. Builds the launcher, loader, mods, editor and Guerrilla builder into it (build.cmd stage\<name>).
#   3. Runs the Guerrilla builder there: -Parts world, hud, vehicles, weapons, people (a comma list)
#      builds those parts over what bin\ already has, then its table check; without -Parts, only the
#      check. The builder's log is stage\<name>\logs\guerrilla_builder.log.
#   -NoMirror skips step 1, to build again on the staging copy as it is.
#
# The game is never started from a staging copy: it's for building and checking. Exit code 0 when the
# build and the builder both pass.
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Name,
    [string]$Parts = '',
    [switch]$NoMirror
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin'
$stage = Join-Path $root "stage\$Name"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8  # the builder writes UTF-8 when its output is piped

if (-not $NoMirror) {
    if (-not (Test-Path (Join-Path $bin 'rfa_mods.ini'))) { throw "bin\ has no build yet: run build.cmd and the Guerrilla builder there first." }
    if (Test-Path (Join-Path $bin '.installing')) { throw "bin\ is being installed into (tools\install-bin.ps1): try again when that's done." }
    Write-Host "Mirroring bin\ into stage\$Name\ ..."
    # /MIR: the same files as bin\ (newer or changed ones copied, others removed); /XD logs keeps the
    # staging copy's own logs\ and leaves bin\logs\ out.
    robocopy $bin $stage /MIR /XD (Join-Path $bin 'logs') /XF .installing /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying bin\ into stage\$Name\ failed (robocopy exit code $LASTEXITCODE)." }
}
elseif (-not (Test-Path $stage)) { throw "stage\$Name\ doesn't exist yet: run without -NoMirror first." }

Write-Host "Building into stage\$Name\ ..."
& (Join-Path $root 'build.cmd') $stage
if ($LASTEXITCODE -ne 0) { Write-Host "stage\$Name\: the build failed (above)."; exit 1 }

$builder = Join-Path $stage 'editor\GuerrillaBuilder.exe'
$words = @($Parts -split '[,\s]+' | Where-Object { $_ })
$builderArgs = @(if ($words.Count -gt 0) { @('build') + $words } else { 'check' })
Write-Host "Guerrilla builder in stage\$Name\: $($builderArgs -join ' ')"
# A window program: piped, PowerShell waits for it and gets its output.
& $builder @builderArgs | Out-Host
$code = $LASTEXITCODE
Write-Host ""
if ($code -eq 0) { Write-Host "stage\$Name\: built, and the builder passed ($($builderArgs -join ' '))." }
else { Write-Host "stage\$Name\: the builder FAILED ($($builderArgs -join ' '), exit code $code); its log is stage\$Name\logs\guerrilla_builder.log." }
exit $code
