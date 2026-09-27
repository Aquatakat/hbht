# Launch a ROM in ares with the GDB server enabled.
# Usage: tools/ares.ps1 [debug|v10|v11] [-SaveState <1-9>]
param(
    [ValidateSet('debug', 'v10', 'v11')] [string]$Rom = 'debug',
    [int]$SaveState = 0
)

$roms = @{
    debug = 'AidynDebug.z64'
    v10   = 'Aidyn Chronicles - The First Mage (U) (V1.0) [!].z64'
    v11   = 'Aidyn Chronicles - The First Mage (U) (V1.1).z64'
}

$ares = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\ares-emulator.ares_*" -Recurse -Filter ares.exe |
    Select-Object -First 1).FullName
if (-not $ares) { throw 'ares not found; install with: winget install ares-emulator.ares' }

$romPath = Join-Path $PSScriptRoot "..\roms\$($roms[$Rom])" | Resolve-Path

# Overrides apply to this run only; the user's settings.bml is left alone.
# Defocus=Allow keeps emulation running while gdb (or anything else) has focus.
$aresArgs = @(
    '--system "Nintendo 64"'
    '--setting DebugServer/Enabled=true'
    '--setting DebugServer/UseIPv4=true'
    '--setting Input/Defocus=Allow'
    '--no-file-prompt'
)
if ($SaveState) { $aresArgs += "--save-state $SaveState" }
$aresArgs += "`"$romPath`""

Start-Process $ares -ArgumentList $aresArgs
Write-Host "ares started on $Rom; GDB server on 127.0.0.1:9123"
