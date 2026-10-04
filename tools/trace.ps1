# What code does an action run? A first-hit trace of the original's functions (roadmap 0.12).
# Usage: tools/trace.ps1 -Checkpoint "<label>" [-Rom v10|debug] [-Baseline 20] [-Action 30]
#                        [-Files <text in a source file's name>] [-Range 0x...-0x...] [-Out <log>] [-Keep]
#
# ares starts at the checkpoint and gdb puts a temporary breakpoint on every function entry (or those
# placed in matching source files, or in the range). Then:
#   one beep     the baseline: stand still, or do whatever the action should be compared against
#   two beeps    the action window: do the thing in ares
#   three beeps  done
# It reports the functions first run in the action window, which the baseline never ran, in order,
# with names and source files. Setup takes about 20 s for every function; the game runs at about half
# speed until the hot functions have used up their breakpoints. 1.0's checkpoints attach; the debug
# build's don't (see the reference rig), so for the debug build, trace from a cold boot.
param(
    [string]$Checkpoint,
    [ValidateSet('v10', 'debug')] [string]$Rom = 'v10',
    [int]$Baseline = 20,
    [int]$Action = 30,
    [string]$Files,
    [string]$Range,
    [string]$Out,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$which = if ($Rom -eq 'debug') { 'debug' } else { 'us' }
if (-not (Test-Path (Join-Path $repo "out\ghidra\$which.index.json"))) { throw "No code index; run tools/ghidra.ps1 import" }
if (-not $Out) { $Out = Join-Path $repo "out\trace-$(Get-Date -Format yyyyMMdd-HHmmss).log" }

$env:TRACE_ROM = $which
$env:TRACE_OUT = $Out
$env:TRACE_PHASES = "baseline:$Baseline,action:$Action"
$env:TRACE_FILES = $Files
$env:TRACE_RANGE = $Range
if (Test-Path $Out) { Remove-Item $Out }

$gdbArgs = @{ Rom = $Rom; Source = (Join-Path $PSScriptRoot 'gdb\trace.py'); Timeout = 90 + $Baseline + $Action; Keep = $Keep }
if ($Checkpoint) { $gdbArgs.Checkpoint = $Checkpoint }
$job = Start-Job -ScriptBlock {
    param($script, $a, $envs)
    foreach ($k in $envs.Keys) { Set-Item "env:$k" $envs[$k] }
    & $script @a
} -ArgumentList (Join-Path $PSScriptRoot 'gdb.ps1'), $gdbArgs, @{
    TRACE_ROM = $env:TRACE_ROM; TRACE_OUT = $env:TRACE_OUT; TRACE_PHASES = $env:TRACE_PHASES
    TRACE_FILES = $env:TRACE_FILES; TRACE_RANGE = $env:TRACE_RANGE
}

# Show the phase markers as they're written, with beeps.
$seen = 0
while ($job.State -eq 'Running') {
    Start-Sleep -Milliseconds 300
    if (-not (Test-Path $Out)) { continue }
    $markers = @(Get-Content $Out | Where-Object { $_.StartsWith('#') })
    if ($markers.Count -le $seen) { continue }
    foreach ($m in $markers[$seen..($markers.Count - 1)]) {
        Write-Host "$(Get-Date -Format HH:mm:ss) $m"
        $beeps = switch -Wildcard ($m) { '# baseline*' { 1 } '# action*' { 2 } '# done' { 3 } default { 0 } }
        for ($i = 0; $i -lt $beeps; $i++) { [console]::Beep(880, 150); Start-Sleep -Milliseconds 100 }
    }
    $seen = $markers.Count
}
Receive-Job $job | Where-Object { $_ -match 'killed|rror|xception|not found' }
Remove-Job $job -Force
Write-Host "log: $Out"
python (Join-Path $PSScriptRoot 'gdb\trace.py') --report $Out
