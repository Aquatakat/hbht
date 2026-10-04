# Run gdb commands against the original in ares, from a checkpoint or a cold boot (roadmap 0.11).
# Usage: tools/gdb.ps1 [-Rom v10|v11|debug] [-Checkpoint "<label>"] [-Commands '<gdb command>', ...]
#                      [-Source <gdb script, .py or .gdb>] [-Boot <seconds>] [-Timeout <seconds>] [-Keep]
#
# Example: what writes the in-game clock (TerrainStruct.InGameTime, the pointer at 0x800E303C + 0x20)?
#   tools/gdb.ps1 -Checkpoint "Before Rooughah" -Commands 'set $t = *(unsigned int*)0x800e303c',
#       'watch *(unsigned int*)($t+0x20)', 'continue', 'p/x $pc'
#
# ares starts through tools/ares.ps1 with its GDB server on, gdb attaches once the game has run for
# -Boot seconds, runs the commands in batch mode and detaches, and ares is closed afterwards unless
# -Keep. gdb is killed after -Timeout seconds, for a watchpoint that never fires. Hardware
# watchpoints and breakpoints work. The debug build's checkpoints don't attach (see the reference
# rig); 1.0's do, and so does a cold boot of either.
param(
    [ValidateSet('debug', 'v10', 'v11')] [string]$Rom = 'v10',
    [string]$Checkpoint,
    [string[]]$Commands = @(),
    [string]$Source,
    [int]$Boot = 12,
    [int]$Timeout = 60,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$gdb = 'C:\msys64\ucrt64\bin\gdb-multiarch.exe'
if (-not (Test-Path $gdb)) { throw "gdb-multiarch not found at $gdb (MSYS2 UCRT64)" }

Get-Process ares -ErrorAction SilentlyContinue | Stop-Process -Force
if ($Checkpoint) { & (Join-Path $PSScriptRoot 'ares.ps1') $Rom -Checkpoint $Checkpoint } else { & (Join-Path $PSScriptRoot 'ares.ps1') $Rom }
Start-Sleep -Seconds $Boot

$gdbArgs = @('-batch', '-ex', '"set architecture mips:4000"', '-ex', '"set endian big"', '-ex', '"set remotetimeout 5"',
    '-ex', '"target remote 127.0.0.1:9123"')
foreach ($c in $Commands) { $gdbArgs += @('-ex', "`"$c`"") }
if ($Source) { $gdbArgs += @('-x', "`"$((Resolve-Path -LiteralPath $Source).Path)`"") }
$gdbArgs += @('-ex', 'detach')
$log = Join-Path ([IO.Path]::GetTempPath()) "aidyn-gdb-$PID.log"
try {
    # An empty stdin: without a console (a background job), gdb otherwise blocks before it starts.
    Set-Content "$log.in" '' -NoNewline
    $p = Start-Process $gdb -ArgumentList $gdbArgs -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError "$log.err" -RedirectStandardInput "$log.in"
    if (-not $p.WaitForExit($Timeout * 1000)) { $p.Kill(); Write-Warning "gdb killed after $Timeout s" }
    Get-Content $log, "$log.err" -ErrorAction SilentlyContinue |
        Where-Object { $_ -and $_ -notmatch 'OS ABI|default mips|ambiguous with reported|No executable has been specified|determining executable automatically' }
} finally {
    Remove-Item $log, "$log.err", "$log.in" -ErrorAction SilentlyContinue
    if (-not $Keep) { Get-Process ares -ErrorAction SilentlyContinue | Stop-Process -Force }
}
