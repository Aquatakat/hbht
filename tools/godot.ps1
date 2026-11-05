# Run the pinned Godot editor (.NET build), downloading it on first use.
# Usage: tools/godot.ps1 [<godot arguments>...]
#        tools/godot.ps1 -ExePath    # print the executable's path and exit
#
# Examples:
#   tools/godot.ps1 --path src/HBHT.Editor --editor
#   tools/godot.ps1 --path src/HBHT.Editor
#
# The version and its hashes are in tools/godot-pin.ps1, which fetches the
# editor from the official GitHub release, checks it against the release's
# SHA-512 and unpacks it into the gitignored .tools/.
#
# There's no param block on purpose: PowerShell would bind Godot's own options
# to it (it read --path as a -Path switch), so every argument goes to Godot as is.
$printPath = $args.Count -eq 1 -and $args[0] -eq '-ExePath'
# Typed at a PowerShell prompt, or run as `powershell tools/godot.ps1 ...`, an argument such as 16,62 reaches
# the script as an array, which would go to Godot as separate arguments: join it back with its commas.
$godotArgs = @($args | ForEach-Object { if ($_ -is [array]) { $_ -join ',' } else { $_ } })
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'godot-pin.ps1')
$exe = Install-Godot
if ($printPath) { return $exe }
& $exe @godotArgs
exit $LASTEXITCODE
