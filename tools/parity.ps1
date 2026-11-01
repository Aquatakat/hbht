# Log battles in the original for combat parity (roadmap 2.9, docs/systems/combat-parity.md).
# Usage: tools/parity.ps1 [-Label <setup>] [-Checkpoint "<label>"] [-Hours 4] [-Out <folder>]
#
# ares boots 1.0 cold (or at a checkpoint), with the Controller Pak beside the ROM, and gdb logs every
# battle until ares is closed: one beep as a battle begins, two as it ends. Each battle's log and RAM
# dumps go to out/parity/<time>-<label>/battle-NN (tools/gdb/parity.py says what's in them). A battle
# plays at about 60% speed, and pauses for about 2 s as each turn begins. Trek runs at full speed.
param(
    [string]$Label = 'run',
    [string]$Checkpoint,
    [double]$Hours = 4,
    [string]$Out
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
if (-not (Test-Path (Join-Path $repo 'out\ghidra\us.index.json'))) { throw 'No code index; run tools/ghidra.ps1 import' }
if (-not $Out) { $Out = Join-Path $repo "out\parity\$(Get-Date -Format yyyyMMdd-HHmmss)-$Label" }
New-Item -ItemType Directory -Force $Out | Out-Null
$progress = Join-Path $Out 'progress.txt'

$gdbArgs = @{ Rom = 'v10'; Source = (Join-Path $PSScriptRoot 'gdb\parity.py'); Boot = 4; Timeout = [int]($Hours * 3600) }
if ($Checkpoint) { $gdbArgs.Checkpoint = $Checkpoint }
$job = Start-Job -ScriptBlock {
    param($script, $a, $out)
    $env:PARITY_OUT = $out
    & $script @a
} -ArgumentList (Join-Path $PSScriptRoot 'gdb.ps1'), $gdbArgs, $Out

# Show the progress as it's written, with beeps.
$seen = 0
while ($job.State -eq 'Running') {
    Start-Sleep -Milliseconds 300
    if (-not (Test-Path $progress)) { continue }
    $lines = @(Get-Content $progress)
    if ($lines.Count -le $seen) { continue }
    foreach ($m in $lines[$seen..($lines.Count - 1)]) {
        Write-Host "$(Get-Date -Format HH:mm:ss) $m"
        $beeps = switch -Wildcard ($m) { '*begins' { 1 } '*ends' { 2 } default { 0 } }
        for ($i = 0; $i -lt $beeps; $i++) { [console]::Beep(880, 150); Start-Sleep -Milliseconds 100 }
    }
    $seen = $lines.Count
}
Receive-Job $job | Where-Object { $_ -match 'killed|rror|xception|not found|Traceback|File "' }
Remove-Job $job -Force
Write-Host "logs: $Out"
Get-ChildItem $Out -Directory | ForEach-Object { Write-Host "  $($_.Name): $((Get-Content (Join-Path $_.FullName 'log.jsonl')).Count) records" }
