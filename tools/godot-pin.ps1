# The pinned Godot (.NET build) and its export templates, fetched into the
# gitignored .tools/ and checked against the release's published SHA-512
# (SHA512-SUMS.txt on the release page). Dot-sourced by tools/godot.ps1 and
# tools/package.ps1, so the editor the developer runs and the one that exports
# the packages are the same. Change the version here and the Godot.NET.Sdk
# version in the Godot projects' .csproj together, with every hash below.
#
# Runs on Windows, Linux and macOS (PowerShell 7).

$GodotVersion = '4.7.2-stable'
# Godot's own name for the version, as its templates folder spells it.
$GodotTemplatesVersion = '4.7.2.stable.mono'

$GodotEditors = @{
    windows = @{
        Zip    = "Godot_v${GodotVersion}_mono_win64.zip"
        Sha512 = '79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757'
        # The console build is the same editor with its output on the terminal.
        Exe    = "Godot_v${GodotVersion}_mono_win64/Godot_v${GodotVersion}_mono_win64_console.exe"
    }
    linux = @{
        Zip    = "Godot_v${GodotVersion}_mono_linux_x86_64.zip"
        Sha512 = '1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13'
        Exe    = "Godot_v${GodotVersion}_mono_linux_x86_64/Godot_v${GodotVersion}_mono_linux.x86_64"
    }
    macos = @{
        Zip    = "Godot_v${GodotVersion}_mono_macos.universal.zip"
        Sha512 = '0862c53d7158c7a67f745e2e46f90b68cf5343cbe8b95d6d4333c469e42ca104af9c121d1746a50e5d221a99d09d82ef7016495f8e0d09255842884ed0502795'
        Exe    = 'Godot_mono.app/Contents/MacOS/Godot'
    }
}

$GodotTemplates = @{
    Tpz    = "Godot_v${GodotVersion}_mono_export_templates.tpz"
    Sha512 = 'bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a'
}

$GodotToolsRoot = Join-Path $PSScriptRoot "../.tools/godot/$GodotVersion"

function Get-HostPlatform {
    if ($IsLinux) { return 'linux' }
    if ($IsMacOS) { return 'macos' }
    return 'windows'
}

function Save-GodotRelease([string]$name, [string]$sha512, [string]$dir) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $file = Join-Path $dir $name
    $url = "https://github.com/godotengine/godot/releases/download/$GodotVersion/$name"
    Write-Host "Downloading $url"
    # The progress bar slows Invoke-WebRequest down by an order of magnitude.
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $file
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA512).Hash
    if ($actual -ne $sha512) {
        Remove-Item -LiteralPath $file
        throw "SHA-512 mismatch for $name`n  expected $sha512`n  got      $actual"
    }
    return $file
}

# The folder a self-contained editor keeps its data in (editor_data/): beside the
# executable, or beside the .app bundle on macOS, where Godot looks for ._sc_.
function Get-GodotSelfContainedDir([string]$platform) {
    if ($platform -eq 'macos') { return $GodotToolsRoot }
    return Split-Path (Join-Path $GodotToolsRoot $GodotEditors[$platform].Exe)
}

# The editor's executable for this machine, downloaded on first use.
function Install-Godot {
    $platform = Get-HostPlatform
    $editor = $GodotEditors[$platform]
    $exe = Join-Path $GodotToolsRoot $editor.Exe
    if (-not (Test-Path -LiteralPath $exe)) {
        $zip = Save-GodotRelease $editor.Zip $editor.Sha512 $GodotToolsRoot
        if ($platform -eq 'windows') {
            Expand-Archive -LiteralPath $zip -DestinationPath $GodotToolsRoot -Force
        } else {
            # unzip keeps the executable bits and the bundle's symlinks, which Expand-Archive drops.
            & unzip -q -o $zip -d $GodotToolsRoot
            if ($LASTEXITCODE -ne 0) { throw "unzip failed on $zip" }
        }
        Remove-Item -LiteralPath $zip
        # Self-contained mode keeps the editor's settings, caches and export
        # templates in .tools/, away from the user's own Godot installs.
        New-Item -ItemType File (Join-Path (Get-GodotSelfContainedDir $platform) '._sc_') -Force | Out-Null
    }
    return (Resolve-Path -LiteralPath $exe).Path
}

# The export templates the packages need, where the self-contained editor finds
# them. The .tpz holds every platform's templates (over a gigabyte); only the
# desktop release ones are kept.
function Install-GodotTemplates {
    $platform = Get-HostPlatform
    $dir = Join-Path (Get-GodotSelfContainedDir $platform) "editor_data/export_templates/$GodotTemplatesVersion"
    $wanted = @('version.txt', 'windows_release_x86_64.exe', 'linux_release.x86_64', 'macos.zip')
    if (-not ($wanted | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dir $_)) })) { return $dir }

    $tpz = Save-GodotRelease $GodotTemplates.Tpz $GodotTemplates.Sha512 $GodotToolsRoot
    New-Item -ItemType Directory -Force $dir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($tpz)
    try {
        foreach ($name in $wanted) {
            $entry = $archive.GetEntry("templates/$name")
            if (-not $entry) { throw "$($GodotTemplates.Tpz) has no templates/$name" }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $dir $name), $true)
        }
    } finally {
        $archive.Dispose()
    }
    Remove-Item -LiteralPath $tpz
    if ($platform -ne 'windows') { & chmod +x (Join-Path $dir 'linux_release.x86_64') }
    return $dir
}
