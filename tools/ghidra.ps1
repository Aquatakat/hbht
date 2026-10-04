# Run Ghidra headless over the original's code (roadmap 0.14). Everything it writes is in the gitignored out/ghidra.
# Usage: tools/ghidra.ps1 import [us|debug|all]    # extract the code segment, import and analyse it, then export
#        tools/ghidra.ps1 export [us|debug|all]    # write <rom>.index.json, builds.json and <rom>.c, with tools/symbols applied
#        tools/ghidra.ps1 run <us|debug> <Script.java> [<script args>...]   # run a script from tools/ghidra
#
# Run export after changing the symbol map. tools/n64dis.py reads the index (--callers, --refs, ...)
# and the pseudocode (--c).
#
# Ghidra lives in tools/ghidra_<version>_PUBLIC (gitignored), or GHIDRA_HOME. It needs JDK 21.
# The code segment sits at ROM 0x1000 and loads at 0x80000400; it runs to where the boot code's BSS
# clear starts, which SetupN64.java maps along with the rest of RDRAM and the hardware registers.
param(
    [Parameter(Mandatory)] [ValidateSet('import', 'export', 'run')] [string]$Command,
    [string]$Rom = 'all',
    [Parameter(ValueFromRemainingArguments)] [string[]]$Rest
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot
$home_ = if ($env:GHIDRA_HOME) { $env:GHIDRA_HOME } else {
    (Get-ChildItem $PSScriptRoot -Directory -Filter 'ghidra_*_PUBLIC' | Sort-Object Name | Select-Object -Last 1).FullName
}
if (-not $home_) { throw 'Ghidra not found: unpack a release into tools/ghidra_<version>_PUBLIC or set GHIDRA_HOME' }
$headless = Join-Path $home_ 'support\analyzeHeadless.bat'
$romsDir = if ($env:AIDYN_ROMS) { $env:AIDYN_ROMS } else { Join-Path $repo 'roms' }
$out = Join-Path $repo 'out\ghidra'
$scripts = Join-Path $PSScriptRoot 'ghidra'
New-Item -ItemType Directory -Force $out | Out-Null

$romFiles = @{ us = '*(V1.0)*.z64'; debug = 'AidynDebug.z64' }

function Import-Rom([string]$which) {
    $path = Get-ChildItem -LiteralPath $romsDir -Filter $romFiles[$which] | Select-Object -First 1
    if (-not $path) { throw "No $which ROM in $romsDir" }
    $rom = [IO.File]::ReadAllBytes($path.FullName)
    # The entry point's lui/addiu pair at 0x80000408 loads the BSS start, where the loaded segment ends.
    $hi = [int]$rom[0x100A] -shl 8 -bor $rom[0x100B]
    $lo = [int16]([int]$rom[0x100E] -shl 8 -bor $rom[0x100F])
    $length = ($hi -shl 16) + $lo - 0x80000400
    $bin = Join-Path $out "$which.bin"
    [IO.File]::WriteAllBytes($bin, $rom[0x1000..(0x1000 + $length - 1)])
    Write-Host ("{0}: code segment 0x80000400-0x{1:X8}" -f $which, (0x80000400 + $length))
    & $headless $out aidyn -import $bin -overwrite -loader BinaryLoader -loader-baseAddr 0x80000400 `
        -loader-blockName code -processor MIPS:BE:64:64-32addr -cspec o32 -scriptPath $scripts `
        -preScript SetupN64.java -postScript FindPointedFunctions.java -postScript Probe.java *> (Join-Path $out "import-$which.log")
    Select-String -Path (Join-Path $out "import-$which.log") -Pattern '\.java> ' | ForEach-Object { $_.Line -replace '^.*?\.java> ', "  " -replace ' \(GhidraScript\)\s*$', '' }
}

function Invoke-Scripts([string]$which, [string]$log, [string[]]$scriptNames) {
    $posts = $scriptNames | ForEach-Object { '-postScript', $_ }
    & $headless $out aidyn -process "$which.bin" -noanalysis -scriptPath $scripts @posts *> (Join-Path $out $log)
    Select-String -Path (Join-Path $out $log) -Pattern '\.java> ' | ForEach-Object { $_.Line -replace '^.*?\.java> ', "  " -replace ' \(GhidraScript\)\s*$', '' }
}

# The index first, then the builds' match and source files (tools/builds.py), which carry each build's
# names to the other, then the names and the pseudocode.
function Export-Roms([string[]]$which) {
    foreach ($w in $which) { Invoke-Scripts $w "index-$w.log" ExportIndex.java }
    if ((Test-Path (Join-Path $out 'us.index.json')) -and (Test-Path (Join-Path $out 'debug.index.json'))) {
        python (Join-Path $PSScriptRoot 'builds.py') | ForEach-Object { "  $_" }
    }
    foreach ($w in $which) { Invoke-Scripts $w "export-$w.log" ApplySymbols.java, DecompileAll.java }
}

$roms = if ($Rom -eq 'all') { 'us', 'debug' } else { $Rom }
switch ($Command) {
    'import' { foreach ($which in $roms) { Import-Rom $which }; Export-Roms $roms }
    'export' { Export-Roms $roms }
    'run' {
        # The script's println lines carry its name; a multi-line one carries it on its first line only.
        $mine = $false
        & $headless $out aidyn -process "$Rom.bin" -noanalysis -scriptPath $scripts -postScript @Rest 2>&1 | ForEach-Object {
            $line = "$_"
            if ($line -match '\.java> ') { $mine = $true; $line -replace '^.*?\.java> ', '' -replace ' \(GhidraScript\)\s*$', '' }
            elseif ($line -match '^(INFO|WARN|ERROR|DEBUG) ') { $mine = $false }
            elseif ($mine) { $line -replace ' \(GhidraScript\)\s*$', '' }
        }
    }
}
