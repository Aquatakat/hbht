# Launch a ROM in ares with the GDB server enabled.
# Usage: tools/ares.ps1 [debug|v10|v11] [-SaveState <1-9>] [-Checkpoint "<name>"]
#
# -Checkpoint loads a named state from roms/, e.g. "Before final encounter" loads
# "roms/<rom basename> - Before final encounter.bs*". The state is copied into
# slot 9, which ares treats as scratch; the named original is never written.
param(
    [ValidateSet('debug', 'v10', 'v11')] [string]$Rom = 'debug',
    [int]$SaveState = 0,
    [string]$Checkpoint
)

$roms = @{
    debug = 'AidynDebug.z64'
    v10   = 'Aidyn Chronicles - The First Mage (U) (V1.0) [!].z64'
    v11   = 'Aidyn Chronicles - The First Mage (U) (V1.1).z64'
}

$ares = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\ares-emulator.ares_*" -Recurse -Filter ares.exe |
    Select-Object -First 1).FullName
if (-not $ares) { throw 'ares not found; install with: winget install ares-emulator.ares' }

$romPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\roms\$($roms[$Rom])")).Path   # literal: 1.0's name has [!]
$romBase = [IO.Path]::ChangeExtension($romPath, $null).TrimEnd('.')

if ($Checkpoint) {
    $state = Get-ChildItem -LiteralPath (Split-Path $romPath) |
        Where-Object { $_.Name -like "$([WildcardPattern]::Escape([IO.Path]::GetFileName($romBase))) - $Checkpoint.bs?" } |
        Select-Object -First 1
    if (-not $state) {
        $available = Get-ChildItem -LiteralPath (Split-Path $romPath) |
            Where-Object { $_.Name -like "$([WildcardPattern]::Escape([IO.Path]::GetFileName($romBase))) - *.bs?" } |
            ForEach-Object { ($_.BaseName -split ' - ', 2)[1] }
        throw "No checkpoint '$Checkpoint' for $Rom. Available: $($available -join '; ')"
    }
    Copy-Item -LiteralPath $state.FullName -Destination "$romBase.bs9" -Force
    $SaveState = 9
}

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
Write-Host "ares started on $Rom$(if ($Checkpoint) { " at '$Checkpoint'" }); GDB server on 127.0.0.1:9123"
