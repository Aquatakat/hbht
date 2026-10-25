# Build the release packages for the machine it runs on (docs/systems/releases.md).
# Usage: tools/package.ps1 [-Version <n>] [-OutDir <folder>]
#
#   Windows: HBHT-alpha-<n>-windows.msi and HBHT-alpha-<n>-windows-portable.zip
#   Linux:   HBHT-alpha-<n>-linux-x86_64.tar.gz
#   macOS:   HBHT-alpha-<n>-macos.dmg
#
# It fetches the pinned Godot and its export templates into .tools/ (tools/godot-pin.ps1),
# exports both Godot projects with release templates, lays them out side by side and
# writes the packages into out/packages/ (gitignored). -Version is the release number,
# which the release workflow passes; a local build without one is 0. The working tree is
# left as it was: the version written into project.godot and the files the export needs
# beside each project are put back or removed afterwards.
#
# PowerShell 7 on Windows, Linux or macOS. The workflow, .github/workflows/release.yml,
# runs this same script on each runner.
[CmdletBinding()]
param(
    [ValidateRange(0, 255)] # an MSI's major version is at most 255
    [int]$Version = 0,
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

. (Join-Path $PSScriptRoot 'godot-pin.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'out/packages' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$platform = Get-HostPlatform
$name = "HBHT-alpha-$Version"
$work = Join-Path $OutDir "work-$platform"

# The two apps: the Godot project, the executable (and PCK) name, the macOS bundle name.
$apps = @(
    @{ Project = 'HBHT.Game';   Exe = 'hbht';        Bundle = 'HBHT' }
    @{ Project = 'HBHT.Editor'; Exe = 'hbht-editor'; Bundle = 'HBHT Editor' }
)

function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') exited with $LASTEXITCODE" }
}

# Godot exits 0 from a failed .NET export, with the failure only in its output, so
# any ERROR line fails the step.
function Invoke-Godot([string]$projectDir, [string[]]$arguments, [string]$log) {
    Write-Host "godot --path $projectDir $($arguments -join ' ')"
    & $script:godot --headless --path $projectDir @arguments 2>&1 |
        ForEach-Object { "$_" } |
        Tee-Object -FilePath $log |
        Where-Object { $_.Trim() -and $_ -notmatch '^\[ *(\d+%|DONE) *\]' } |
        ForEach-Object { Write-Host "  $_" }
    $code = $LASTEXITCODE
    $errors = @(Select-String -LiteralPath $log -Pattern '^(ERROR|SCRIPT ERROR):')
    if ($code -ne 0 -or $errors.Count -gt 0) {
        throw "Godot failed (exit $code, $($errors.Count) errors); its output is in $log"
    }
}

function Copy-IfChanged([string]$from, [string]$to) {
    if ((Test-Path -LiteralPath $to) -and
        (Get-FileHash -LiteralPath $from).Hash -eq (Get-FileHash -LiteralPath $to).Hash) { return }
    Copy-Item -LiteralPath $from -Destination $to -Force
}

function Reset-Folder([string]$path) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Force $path | Out-Null
}

# Exports one project with its preset, with the version and the files the export
# needs put in place for the run and taken away after.
function Export-App($app, [string]$preset, [string]$target) {
    $dir = Join-Path $repo "src/$($app.Project)"
    $projectFile = Join-Path $dir 'project.godot'
    $original = [IO.File]::ReadAllBytes($projectFile)
    $created = @()
    try {
        # The icons the presets name. The PNG is committed (it's the project's icon);
        # the .ico and .icns are only needed here, and are gitignored.
        $branding = Join-Path $repo 'assets/branding'
        Copy-IfChanged (Join-Path $branding 'logo.png') (Join-Path $dir 'icon.png')
        foreach ($icon in @(@('hbht.ico', 'icon.ico'), @('hbht.icns', 'icon.icns'))) {
            $to = Join-Path $dir $icon[1]
            if (-not (Test-Path -LiteralPath $to)) { $created += $to }
            Copy-Item -LiteralPath (Join-Path $branding $icon[0]) -Destination $to -Force
        }
        # Godot's .NET export insists on <assembly>.sln beside the project, though it
        # publishes the .csproj; the repository's solution is HBHT.slnx at the root.
        $sln = Join-Path $dir "$($app.Project).sln"
        if (-not (Test-Path -LiteralPath $sln)) {
            $created += $sln
            Invoke-Native dotnet @('new', 'sln', '--format', 'sln', '-n', $app.Project, '-o', $dir)
            Invoke-Native dotnet @('sln', $sln, 'add', (Join-Path $dir "$($app.Project).csproj"))
        }
        # The version: Godot gives it to the Windows file and product versions and the
        # macOS bundle versions.
        $text = [Text.Encoding]::UTF8.GetString($original)
        $versioned = [regex]::Replace($text, '(?m)^config/version="[^"]*"', "config/version=`"$Version`"")
        if ($versioned -eq $text -and $text -notmatch "(?m)^config/version=`"$Version`"") {
            throw "$projectFile has no config/version line"
        }
        [IO.File]::WriteAllText($projectFile, $versioned)

        $logs = Join-Path $work 'logs'
        New-Item -ItemType Directory -Force $logs | Out-Null
        # A fresh checkout has no .godot/ cache: import first, or the export sees no resources.
        Invoke-Godot $dir @('--import') (Join-Path $logs "$($app.Exe)-import.log")
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Invoke-Godot $dir @('--export-release', $preset, $target) (Join-Path $logs "$($app.Exe)-export.log")
    } finally {
        [IO.File]::WriteAllBytes($projectFile, $original)
        foreach ($file in $created) { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
    }
}

function Assert-Exists([string[]]$paths) {
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path)) { throw "The export is missing $path" }
    }
}

# The MIT license as the RTF the installer's license page shows.
function ConvertTo-Rtf([string]$text) {
    $body = New-Object Text.StringBuilder
    foreach ($ch in $text.Replace("`r`n", "`n").ToCharArray()) {
        switch ($ch) {
            '\' { [void]$body.Append('\\') }
            '{' { [void]$body.Append('\{') }
            '}' { [void]$body.Append('\}') }
            "`n" { [void]$body.Append("\par`n") }
            default {
                if ([int]$ch -lt 128) { [void]$body.Append($ch) }
                else { [void]$body.Append("\u$([int]$ch)?") }
            }
        }
    }
    return "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18`n$body}"
}

function New-WindowsPackages {
    $stage = Join-Path $work 'HBHT'
    Reset-Folder $stage
    foreach ($app in $apps) {
        Export-App $app 'Windows Desktop' (Join-Path $stage "$($app.Exe).exe")
        Assert-Exists @(
            (Join-Path $stage "$($app.Exe).exe"),
            (Join-Path $stage "$($app.Exe).pck"),
            (Join-Path $stage "data_$($app.Project)_windows_x86_64"))
    }
    Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'README.md') $stage

    # The portable zip: the same folder with portable.txt, which keeps the data beside it.
    $zip = Join-Path $OutDir "$name-windows-portable.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $writer = New-Object IO.StreamWriter($archive.CreateEntry('portable.txt').Open())
        $writer.Write("This file makes HBHT portable: while it is here, HBHT keeps its data (the imported game, saves and settings) in the data folder beside it instead of %LOCALAPPDATA%\HBHT.`r`n")
        $writer.Dispose()
    } finally {
        $archive.Dispose()
    }
    Write-Host "Wrote $zip"

    # The MSI, with WiX from the repository's tool manifest (dotnet-tools.json).
    $wixDir = Join-Path $repo '.tools/wix'
    New-Item -ItemType Directory -Force $wixDir | Out-Null
    $rtf = Join-Path $work 'License.rtf'
    [IO.File]::WriteAllText($rtf, (ConvertTo-Rtf (Get-Content -Raw (Join-Path $repo 'LICENSE'))), [Text.Encoding]::ASCII)
    $msi = Join-Path $OutDir "$name-windows.msi"
    Push-Location $repo
    try { Invoke-Native dotnet @('tool', 'restore') } finally { Pop-Location }
    # wix keeps extensions in .wix/ under the folder it runs in: .tools/wix here.
    Push-Location $wixDir
    try {
        $wixVersion = (& dotnet wix --version).Split('+')[0]
        Invoke-Native dotnet @('wix', 'extension', 'add', "WixToolset.UI.wixext/$wixVersion")
        Invoke-Native dotnet @('wix', 'build', (Join-Path $repo 'packaging/windows/HBHT.wxs'),
            '-arch', 'x64', '-ext', 'WixToolset.UI.wixext',
            '-d', "Version=$Version.0.0",
            '-d', "AppDir=$stage",
            '-d', "LicenseRtf=$rtf",
            '-d', "Icon=$(Join-Path $repo 'assets/branding/hbht.ico')",
            '-intermediatefolder', (Join-Path $work 'wixobj'),
            '-pdb', (Join-Path $work 'HBHT.wixpdb'),
            '-o', $msi)
    } finally { Pop-Location }
    Write-Host "Wrote $msi"
}

function New-LinuxPackage {
    $stage = Join-Path $work $name
    Reset-Folder $stage
    foreach ($app in $apps) {
        Export-App $app 'Linux' (Join-Path $stage $app.Exe)
        Assert-Exists @(
            (Join-Path $stage $app.Exe),
            (Join-Path $stage "$($app.Exe).pck"),
            (Join-Path $stage "data_$($app.Project)_linuxbsd_x86_64"))
        Invoke-Native chmod @('+x', (Join-Path $stage $app.Exe))
    }
    Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'README.md') $stage
    $tar = Join-Path $OutDir "$name-linux-x86_64.tar.gz"
    Invoke-Native tar @('-czf', $tar, '-C', $work, $name)
    Write-Host "Wrote $tar"
}

function New-MacPackage {
    $stage = Join-Path $work $name
    Reset-Folder $stage
    foreach ($app in $apps) {
        $zip = Join-Path $work "$($app.Exe)-macos.zip"
        Export-App $app 'macOS' $zip
        $unzipped = Join-Path $work "$($app.Exe)-macos"
        Reset-Folder $unzipped
        Invoke-Native ditto @('-x', '-k', $zip, $unzipped)
        $exported = @(Get-ChildItem -LiteralPath $unzipped -Directory -Filter '*.app')
        if ($exported.Count -ne 1) { throw "Expected one .app in $zip" }
        $bundle = Join-Path $stage "$($app.Bundle).app"
        Move-Item -LiteralPath $exported[0].FullName -Destination $bundle

        # Godot names the executable and the PCK after the project ("HBHT Editor");
        # the packages name them hbht and hbht-editor, as on Windows and Linux. The
        # engine finds the PCK by the executable's name, in Contents/Resources.
        $contents = Join-Path $bundle 'Contents'
        $binary = @(Get-ChildItem -LiteralPath (Join-Path $contents 'MacOS') -File)
        if ($binary.Count -ne 1) { throw "Expected one executable in $contents/MacOS" }
        $pck = Join-Path $contents "Resources/$($binary[0].BaseName).pck"
        Assert-Exists @($pck,
            (Join-Path $contents "Resources/data_$($app.Project)_macos_arm64"),
            (Join-Path $contents "Resources/data_$($app.Project)_macos_x86_64"))
        if ($binary[0].Name -cne $app.Exe) {
            Move-Item -LiteralPath $binary[0].FullName -Destination (Join-Path $contents "MacOS/$($app.Exe).tmp")
            Move-Item -LiteralPath (Join-Path $contents "MacOS/$($app.Exe).tmp") -Destination (Join-Path $contents "MacOS/$($app.Exe)")
            Move-Item -LiteralPath $pck -Destination (Join-Path $contents "Resources/$($app.Exe).pck.tmp")
            Move-Item -LiteralPath (Join-Path $contents "Resources/$($app.Exe).pck.tmp") -Destination (Join-Path $contents "Resources/$($app.Exe).pck")
            Invoke-Native plutil @('-replace', 'CFBundleExecutable', '-string', $app.Exe, (Join-Path $contents 'Info.plist'))
        }
        # Renaming broke Godot's ad-hoc signature. Sign again, ad hoc: arm64 Macs run
        # nothing unsigned. There's no Developer ID, so Gatekeeper still asks (releases.md).
        Invoke-Native codesign @('--force', '--deep', '--sign', '-', $bundle)
        Invoke-Native codesign @('--verify', '--deep', '--verbose=2', $bundle)
    }
    Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'README.md') $stage
    # A link to /Applications, so the disk image's window says where the apps go.
    Invoke-Native ln @('-s', '/Applications', (Join-Path $stage 'Applications'))
    $dmg = Join-Path $OutDir "$name-macos.dmg"
    if (Test-Path -LiteralPath $dmg) { Remove-Item -LiteralPath $dmg }
    # hdiutil now and then fails with "Resource busy" on a busy machine; a retry clears it.
    foreach ($attempt in 1..3) {
        & hdiutil create -volname "HBHT alpha $Version" -srcfolder $stage -ov -format UDZO $dmg
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq 3) { throw "hdiutil create exited with $LASTEXITCODE" }
        Start-Sleep -Seconds 5
    }
    Write-Host "Wrote $dmg"
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Reset-Folder $work
$script:godot = Install-Godot
$templates = Install-GodotTemplates
Write-Host "Godot $GodotVersion at $script:godot, templates in $templates"

switch ($platform) {
    'windows' { New-WindowsPackages }
    'linux'   { New-LinuxPackage }
    'macos'   { New-MacPackage }
}
