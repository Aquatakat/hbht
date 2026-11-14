# Work a flag-coverage tree until no job is left or the hours run out, starting `hbht playthrough cover work` again
# whenever its process dies (docs/systems/playthrough.md, flag coverage). The queue hands a dead process's claimed
# jobs back on the next claim, so a crash costs only the jobs it was playing.
#
# Usage: tools/cover.ps1 -Root <dir> [-Hours 10] [-Threads 20] [-Retries 2] [-PartyRounds 1] [-Log <file>]
#
# Start a tree first with `hbht playthrough cover init --root <dir>` (with --trunk for a trunk tree). The CLI is built in
# Release once, here; the run reads the installed plugins. Each process's output is appended to the log (<root>/work.log
# by default). In a trunk tree, once no job is left, `cover retry` gives what was given up another trunk save to start
# from, up to -Retries rounds; then `cover party` gives what still needs someone in the party a party job, up to
# -PartyRounds rounds. A round whose command fails (any exit but 0, jobs made, and 4, nothing to make) has its output
# in the log, and is tried once more after `work` has run again (which hands back any dead process's claims); failing
# twice, it's skipped, and the script says so in its output and the log.
param(
    [Parameter(Mandatory)][string]$Root,
    [double]$Hours = 10,
    [int]$Threads = 20,
    [int]$Retries = 2,
    [int]$PartyRounds = 1,
    [string]$Log
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$Root = [IO.Path]::GetFullPath($Root)
if (-not $Log) { $Log = Join-Path $Root 'work.log' }

dotnet build (Join-Path $repo 'src/HBHT.Cli') -c Release -v q -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'The CLI did not build.' }
$exe = Join-Path $repo 'src/HBHT.Cli/bin/Release/net10.0/hbht.exe'

# Runs a round's command (retry or party), its output to the screen and the log: its exit code.
function Invoke-Round([string]$What, [string[]]$Arguments) {
    Add-Content $Log "=== $(Get-Date -Format s) cover $What"
    $ErrorActionPreference = 'Continue'
    & $exe playthrough cover $What @Arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $Log -Append | Out-Host
    $code = $LASTEXITCODE
    Add-Content $Log "=== $(Get-Date -Format s) cover $What exited with $code"
    return $code
}

$deadline = (Get-Date).AddHours($Hours)
$starts = 0
$round = 0
$party = 0
$failed = @{}
$skipped = @()
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
        if (-not $trunk) { break }
        if ($round -lt $Retries) {
            $name = "retry round $($round + 1)"
            $made = Invoke-Round 'retry' @('--root', $Root)
            if ($made -eq 0) { $round++; continue }
            if ($made -eq 4) { $round = $Retries }
            elseif (-not $failed[$name]) {
                $failed[$name] = $true
                Write-Host "The $name failed (exit $made; its output is in $Log): trying it once more after work."
                continue
            }
            else {
                Write-Host "THE $($name.ToUpper()) WAS SKIPPED: cover retry failed twice (exit $made; its output is in $Log)."
                Add-Content $Log "=== the $name was skipped: cover retry failed twice"
                $skipped += $name
                $round = $Retries
            }
        }
        if ($party -lt $PartyRounds) {
            $name = "party round $($party + 1)"
            $made = Invoke-Round 'party' @('--root', $Root, '--threads', "$Threads")
            if ($made -eq 0) { $party++; continue }
            if ($made -ne 4) {
                if (-not $failed[$name]) {
                    $failed[$name] = $true
                    Write-Host "The $name failed (exit $made; its output is in $Log): trying it once more after work."
                    continue
                }
                Write-Host "THE $($name.ToUpper()) WAS SKIPPED: cover party failed twice (exit $made; its output is in $Log)."
                Add-Content $Log "=== the $name was skipped: cover party failed twice"
                $skipped += $name
            }
        }
        break
    }
    if ($code -eq 3) { Write-Host 'The minutes ran out with jobs pending.'; break }
    Write-Host "The process ended with $code; starting it again."
    Add-Content $Log "=== $(Get-Date -Format s) the process ended with $code"
    if ($starts -ge 50) { throw 'Started 50 times: something is wrong.' }
}
& $exe playthrough cover status --root $Root
if ($skipped.Count -gt 0) { Write-Host "Skipped: $($skipped -join ', '). The tree is short of them." }
