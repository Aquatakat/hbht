# Run the pinned Godot editor (.NET build), downloading it on first use.
# Usage: tools/godot.ps1 [<godot arguments>...]
#        tools/godot.ps1 -Path       # print the executable's path and exit
#
# Examples:
#   tools/godot.ps1 --path src/Aidyn.Inspector --editor
#   tools/godot.ps1 --path src/Aidyn.Inspector
#
# The editor is fetched from the official GitHub release, checked against the
# release's SHA-512 and unpacked into the gitignored .tools/. Change the version
# here and the Godot.NET.Sdk version in the Godot projects' .csproj together.
param(
    [switch]$Path,
    [Parameter(ValueFromRemainingArguments)] [string[]]$GodotArgs
)
$ErrorActionPreference = 'Stop'

$version = '4.7.2-stable'
$zipName = "Godot_v${version}_mono_win64.zip"
$sha512  = '79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757'

$root = Join-Path $PSScriptRoot "..\.tools\godot\$version"
# The console build is the same editor with its output on the terminal.
$exeName = "Godot_v${version}_mono_win64_console.exe"
$exe = Join-Path $root "Godot_v${version}_mono_win64\$exeName"

if (-not (Test-Path -LiteralPath $exe)) {
    New-Item -ItemType Directory -Force $root | Out-Null
    $zip = Join-Path $root $zipName
    $url = "https://github.com/godotengine/godot/releases/download/$version/$zipName"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA512).Hash
    if ($actual -ne $sha512) {
        Remove-Item -LiteralPath $zip
        throw "SHA-512 mismatch for $zipName`n  expected $sha512`n  got      $actual"
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $root -Force
    Remove-Item -LiteralPath $zip
    # Self-contained mode keeps the editor's settings and caches in .tools/,
    # away from the user's own Godot installs.
    New-Item -ItemType File (Join-Path (Split-Path $exe) '._sc_') -Force | Out-Null
}

$exe = (Resolve-Path -LiteralPath $exe).Path
if ($Path) { return $exe }
& $exe @GodotArgs
exit $LASTEXITCODE
