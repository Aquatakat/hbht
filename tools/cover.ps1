# Work a flag-coverage tree until no job is left or the hours run out, starting `hbht playthrough cover work` again
# whenever its process dies (docs/systems/playthrough.md, flag coverage). The queue hands a dead process's claimed
# jobs back on the next claim, so a crash costs only the jobs it was playing.
#
# Usage: tools/cover.ps1 -Root <dir> [-Hours 10] [-Threads 20] [-Retries 2] [-Log <file>]
#
# Start a tree first with `hbht playthrough cover init --root <dir>` (with --trunk for a trunk tree). The CLI is built in
# Release once, here; the run reads the installed plugins. Each process's output is appended to the log (<root>/work.log
# by default). In a trunk tree, once no job is left, `cover retry` gives what was given up another trunk save to start
# from, up to -Retries rounds.
param(
    [Parameter(Mandatory)][string]$Root,
    [double]$Hours = 10,
    [int]$Threads = 20,
    [int]$Retries = 2,
    [string]$Log
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$Root = [IO.Path]::GetFullPath($Root)
if (-not $Log) { $Log = Join-Path $Root 'work.log' }

dotnet build (Join-Path $repo 'src/HBHT.Cli') -c Release -v q -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'The CLI did not build.' }
$exe = Join-Path $repo 'src/HBHT.Cli/bin/Release/net10.0/hbht.exe'

$deadline = (Get-Date).AddHours($Hours)
$starts = 0
$round = 0
while ($true) {
    $left = ($deadline - (Get-Date)).TotalMinutes
    if ($left -lt 1) { Write-Host 'The hours ran out.'; break }
    $starts++
    Write-Host "$(Get-Date -Format 'HH:mm') work #$starts, $([math]::Round($left)) minutes left"
    Add-Content $Log "=== $(Get-Date -Format s) work #$starts, $([math]::Round($left)) minutes left"
    # Native output goes straight to the log, so a crash's last lines are kept.
    & $exe playthrough cover work --root $Root --threads $Threads --most-minutes ([math]::Floor($left)) *>> $Log
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        Write-Host 'No job is left.'
        $trunk = (Get-Content (Join-Path $Root 'cover.json') -Raw | ConvertFrom-Json).Trunk
        if (-not $trunk -or $round -ge $Retries) { break }
        $round++
        & $exe playthrough cover retry --root $Root
        if ($LASTEXITCODE -ne 0) { break }
        continue
    }
    if ($code -eq 3) { Write-Host 'The minutes ran out with jobs pending.'; break }
    Write-Host "The process ended with $code; starting it again."
    Add-Content $Log "=== $(Get-Date -Format s) the process ended with $code"
    if ($starts -ge 50) { throw 'Started 50 times: something is wrong.' }
}
& $exe playthrough cover status --root $Root
