# HBHT's own Godot export template: the pinned Godot (tools/godot-pin.ps1) built from
# its source for size, with only what the two apps use (docs/systems/releases.md#the-engine).
# Dot-sourced by tools/package.ps1 after godot-pin.ps1.
#
#   tools/godot-template/hbht.py       SCons options: optimize for size, LTO, the modules off
#   tools/godot-template/hbht.gdbuild  the build profile: the engine classes left unregistered
#
# Install-GodotTemplate returns the template for this machine's platform, built into
# .tools/godot-template/<key>/ on first use, where <key> hashes the pin, the two profiles and
# this script, so a change to any of them builds again. The release workflow caches that
# folder by the same files. Building needs Python 3 and the platform's C++ toolchain:
# Visual Studio 2019 16.11 or later with its C++ workload on Windows, and on Linux nothing
# but curl and bzip2, as the build uses Godot's own buildroot toolchain.
#
# Runs on Windows and Linux (PowerShell 7). macOS isn't built (docs/systems/releases.md#macos).

$GodotSconsVersion = '4.11.1'

# Godot's buildroot toolchain for Linux, the one the pinned release's own templates were built
# with: it links against glibc 2.28, so the template runs on the distributions the stock one
# does. (Its 2026.05 toolchain's glibc is 2.34.) GitHub publishes no digest for it; the hash is
# of the file as first downloaded.
$GodotLinuxSdk = @{
    Url    = 'https://github.com/godotengine/buildroot/releases/download/godot-2023.08.x-4/x86_64-godot-linux-gnu_sdk-buildroot.tar.bz2'
    Sha256 = 'b89f173b1f2f2f35f3090bc4efad732f1c0c1c1206e5480cfcb141977010bc94'
}

$GodotTemplateProfiles = Join-Path $PSScriptRoot 'godot-template'
$GodotBuildRoot = Join-Path $PSScriptRoot '../.tools/godot-build'

# The files the template is built from; a change to any of them builds it again.
function Get-GodotTemplateInputs {
    return @(
        (Join-Path $PSScriptRoot 'godot-pin.ps1'),
        (Join-Path $PSScriptRoot 'godot-template.ps1'),
        (Join-Path $GodotTemplateProfiles 'hbht.py'),
        (Join-Path $GodotTemplateProfiles 'hbht.gdbuild'))
}

function Get-GodotTemplateKey {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $all = foreach ($file in Get-GodotTemplateInputs) {
            # Line endings as checked out don't change the template.
            $text = (Get-Content -Raw -LiteralPath $file) -replace "`r`n", "`n"
            [Text.Encoding]::UTF8.GetBytes($text)
        }
        $hash = $sha.ComputeHash([byte[]]$all)
        return ([BitConverter]::ToString($hash) -replace '-', '').Substring(0, 16).ToLowerInvariant()
    } finally { $sha.Dispose() }
}

# The template's file name, as Godot's own templates name it.
function Get-GodotTemplateName([string]$platform) {
    switch ($platform) {
        'windows' { 'windows_release_x86_64.exe' }
        'linux'   { 'linux_release.x86_64' }
    }
}

function Get-GodotTemplatePath([string]$platform) {
    return Join-Path $PSScriptRoot "../.tools/godot-template/$(Get-GodotTemplateKey)/$(Get-GodotTemplateName $platform)"
}

# Running Godot headless, which tools/package.ps1 shares.

# A .NET export's build leaves MSBuild's worker nodes and the compiler server running
# after Godot exits, holding the handles they inherited. Without them nothing outlives
# the export, and the build is the same from a clean machine as from a warm one.
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:UseSharedCompilation = 'false'

# How long one Godot run may take before it's taken as stuck.
$script:godotTimeout = [TimeSpan]::FromMinutes(20)

# Quotes an argument for a Windows command line, as Start-Process doesn't.
function ConvertTo-Argument([string]$value) {
    if ($value -and $value -notmatch '[\s"]') { return $value }
    return '"' + ($value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

# Godot's output goes to files rather than a pipe: a process the export leaves behind
# that holds the pipe keeps it from ending, and the step waits forever (it did on
# GitHub's Windows runners, the export finished). Godot exits 0 from a failed .NET
# export, with the failure only in its output, so any ERROR line fails the step.
function Invoke-Godot([string]$godot, [string]$projectDir, [string[]]$arguments, [string]$log) {
    Write-Host "godot --path $projectDir $($arguments -join ' ')"
    $all = @('--headless', '--path', $projectDir) + $arguments
    $errLog = "$log.stderr"
    $process = Start-Process -FilePath $godot -ArgumentList (($all | ForEach-Object { ConvertTo-Argument $_ }) -join ' ') `
        -RedirectStandardOutput $log -RedirectStandardError $errLog -NoNewWindow -PassThru
    # The handle, so the exit code is still there once it has exited.
    $null = $process.Handle
    $stuck = $false
    if (-not $process.WaitForExit([int]$godotTimeout.TotalMilliseconds)) {
        $process.Kill($true)
        $stuck = $true
    }
    foreach ($file in @($log, $errLog)) {
        Get-Content -LiteralPath $file -ErrorAction SilentlyContinue |
            Where-Object { $_.Trim() -and $_ -notmatch '^\[ *(\d+%|DONE) *\]' } |
            ForEach-Object { Write-Host "  $_" }
    }
    if ($stuck) { throw "Godot was still running after $($godotTimeout.TotalMinutes) minutes; its output is in $log" }
    $code = $process.ExitCode
    $errors = @(Select-String -LiteralPath $log, $errLog -Pattern '^(ERROR|SCRIPT ERROR):')
    if ($code -ne 0 -or $errors.Count -gt 0) {
        throw "Godot failed (exit $code, $($errors.Count) errors); its output is in $log"
    }
}

# The presets with the named one's release template set to a file.
function Set-PresetTemplate([string]$presets, [string]$preset, [string]$path) {
    $lines = $presets -split "`r?`n"
    $index = $null
    $section = $null
    $set = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\[preset\.(\d+)\]$') { $section = "preset.$($Matches[1])"; continue }
        if ($lines[$i] -match '^\[(preset\.\d+\.options)\]$') { $section = $Matches[1]; continue }
        if ($section -match '^preset\.\d+$' -and $lines[$i] -eq "name=`"$preset`"") { $index = $section }
        if ($index -and $section -eq "$index.options" -and $lines[$i] -match '^custom_template/release=') {
            $lines[$i] = 'custom_template/release="' + $path.Replace('\', '/') + '"'
            $set = $true
        }
    }
    if (-not $set) { throw "export_presets.cfg has no custom_template/release for the preset $preset" }
    return $lines -join "`n"
}

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    Write-Host "$exe $($arguments -join ' ')"
    # To the host, not the pipeline, where it would join the caller's return value.
    & $exe @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$exe exited with $LASTEXITCODE" }
}

function Save-Checked([string]$url, [string]$file, [string]$algorithm, [string]$hash) {
    Write-Host "Downloading $url"
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $file
    $actual = (Get-FileHash -LiteralPath $file -Algorithm $algorithm).Hash
    if ($actual -ne $hash) {
        Remove-Item -LiteralPath $file
        throw "$algorithm mismatch for $url`n  expected $hash`n  got      $actual"
    }
}

# The pinned source, unpacked into .tools/godot-build/, with SCons in a virtual environment.
function Install-GodotSource {
    New-Item -ItemType Directory -Force $GodotBuildRoot | Out-Null
    $src = Join-Path $GodotBuildRoot "godot-$GodotVersion"
    if (-not (Test-Path -LiteralPath (Join-Path $src 'SConstruct'))) {
        $tar = Save-GodotRelease $GodotSource.Tar $GodotSource.Sha512 $GodotBuildRoot
        # Windows' own tar (bsdtar) reads .xz; Git's GNU tar, often first on the PATH, takes C: for a host.
        $tarExe = if ($IsWindows) { Join-Path $env:SystemRoot 'System32/tar.exe' } else { 'tar' }
        Invoke-Checked $tarExe @('-xf', $tar, '-C', $GodotBuildRoot)
        Remove-Item -LiteralPath $tar
    }
    $venv = Join-Path $GodotBuildRoot 'venv'
    $scons = if ($IsWindows) { Join-Path $venv 'Scripts/scons.exe' } else { Join-Path $venv 'bin/scons' }
    if (-not (Test-Path -LiteralPath $scons)) {
        $python = if ($IsWindows) { 'python' } else { 'python3' }
        Invoke-Checked $python @('-m', 'venv', $venv)
        $pip = if ($IsWindows) { Join-Path $venv 'Scripts/pip.exe' } else { Join-Path $venv 'bin/pip' }
        Invoke-Checked $pip @('install', '--disable-pip-version-check', "scons==$GodotSconsVersion")
    }
    return @{ Source = (Resolve-Path -LiteralPath $src).Path; Scons = $scons; Python = (Split-Path $scons) }
}

# The prebuilt libraries Godot's official builds link (ANGLE, AccessKit), fetched by Godot's
# own scripts from Godot's own releases, into a folder of the build's.
function Install-GodotDeps($tools, [string[]]$scripts) {
    $deps = Join-Path $GodotBuildRoot 'deps'
    $python = Join-Path $tools.Python $(if ($IsWindows) { 'python.exe' } else { 'python' })
    # The scripts install into $LOCALAPPDATA/Godot/build_deps when it's set, else bin/build_deps.
    $saved = $env:LOCALAPPDATA
    $env:LOCALAPPDATA = $deps
    Push-Location $tools.Source
    try {
        foreach ($script in $scripts) {
            Invoke-Checked $python @((Join-Path 'misc/scripts' $script))
        }
    } finally {
        Pop-Location
        $env:LOCALAPPDATA = $saved
    }
    return (Resolve-Path -LiteralPath (Join-Path $deps 'Godot/build_deps')).Path
}

function Invoke-Scons($tools, [string[]]$arguments) {
    $all = @(
        "profile=$(Join-Path $GodotTemplateProfiles 'hbht.py')",
        "build_profile=$(Join-Path $GodotTemplateProfiles 'hbht.gdbuild')",
        "-j$([Environment]::ProcessorCount)"
    ) + $arguments
    Push-Location $tools.Source
    try { Invoke-Checked $tools.Scons $all } finally { Pop-Location }
}

# Godot's buildroot toolchain on the PATH, for this process and the builds it starts.
function Use-GodotLinuxSdk {
    $sdk = Join-Path $GodotBuildRoot 'x86_64-godot-linux-gnu_sdk-buildroot'
    if (-not (Test-Path -LiteralPath (Join-Path $sdk 'bin/gcc'))) {
        $tar = Join-Path $GodotBuildRoot 'linux-sdk.tar.bz2'
        Save-Checked $GodotLinuxSdk.Url $tar 'SHA256' $GodotLinuxSdk.Sha256
        Invoke-Checked tar @('-xjf', $tar, '-C', $GodotBuildRoot)
        Remove-Item -LiteralPath $tar
        Push-Location $sdk
        try { Invoke-Checked bash @('./relocate-sdk.sh') } finally { Pop-Location }
    }
    $env:PATH = "$(Join-Path $sdk 'bin')$([IO.Path]::PathSeparator)$env:PATH"
}

function Build-GodotTemplate([string]$platform, [string]$out) {
    $tools = Install-GodotSource
    $bin = Join-Path $tools.Source 'bin'
    switch ($platform) {
        'windows' {
            $deps = Install-GodotDeps $tools @('install_angle.py', 'install_accesskit.py')
            # d3d12 is a platform default, which only the command line overrides.
            Invoke-Scons $tools @('platform=windows', 'arch=x86_64', 'd3d12=no',
                "angle_libs=$(Join-Path $deps 'angle')", "accesskit_sdk_path=$(Join-Path $deps 'accesskit')")
            Copy-Item -LiteralPath (Join-Path $bin 'godot.windows.template_release.x86_64.mono.exe') -Destination $out
        }
        'linux' {
            $deps = Install-GodotDeps $tools @('install_accesskit.py')
            Use-GodotLinuxSdk
            Invoke-Scons $tools @('platform=linuxbsd', 'arch=x86_64', "accesskit_sdk_path=$(Join-Path $deps 'accesskit')")
            Copy-Item -LiteralPath (Join-Path $bin 'godot.linuxbsd.template_release.x86_64.mono') -Destination $out
            Invoke-Checked chmod @('+x', $out)
        }
        default { throw "HBHT's template isn't built for $platform (docs/systems/releases.md#macos)" }
    }
}

# Whether GodotSharp binds to a template: exports the bind check (tools/godot-template/bindcheck)
# with it and runs it headless. GodotSharp looks up all of a class's methods the first time the
# class is used, so a template missing one method of a class the apps use fails every use of it.
function Test-GodotTemplate([string]$template) {
    $platform = Get-HostPlatform
    $godot = Install-Godot
    $null = Install-GodotTemplates
    $project = (Resolve-Path -LiteralPath (Join-Path $GodotTemplateProfiles 'bindcheck')).Path
    $out = Join-Path $GodotBuildRoot "bindcheck-$platform"
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null
    $out = (Resolve-Path -LiteralPath $out).Path
    $preset, $target, $exe = switch ($platform) {
        'windows' { 'Windows Desktop', (Join-Path $out 'bindcheck.exe'), (Join-Path $out 'bindcheck.exe') }
        'linux'   { 'Linux', (Join-Path $out 'bindcheck.x86_64'), (Join-Path $out 'bindcheck.x86_64') }
    }
    $presetsFile = Join-Path $project 'export_presets.cfg'
    $original = [IO.File]::ReadAllBytes($presetsFile)
    try {
        [IO.File]::WriteAllText($presetsFile, (Set-PresetTemplate ([IO.File]::ReadAllText($presetsFile)) $preset $template))
        Invoke-Godot $godot $project @('--import') (Join-Path $out 'import.log')
        Invoke-Godot $godot $project @('--export-release', $preset, $target) (Join-Path $out 'export.log')
    } finally {
        [IO.File]::WriteAllBytes($presetsFile, $original)
    }
    if (-not $IsWindows) { Invoke-Checked chmod @('+x', $exe) }
    $log = Join-Path $out 'run.log'
    $process = Start-Process -FilePath $exe -ArgumentList '--headless' -RedirectStandardOutput $log `
        -RedirectStandardError "$log.stderr" -NoNewWindow -PassThru
    $null = $process.Handle
    if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw "The bind check was still running after two minutes" }
    Get-Content -LiteralPath $log, "$log.stderr" | Where-Object { $_ -match '^BindCheck(:| failed)' } | ForEach-Object { Write-Host $_ }
    if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -Pattern '^BindCheck: \d+ classes bound, 0 failed' -Quiet)) {
        throw "GodotSharp doesn't bind to the template $template (exit $($process.ExitCode)); the run's output is in $log"
    }
    # GodotSharp's classes the template doesn't have, which the exports mustn't use (Assert-GodotClassesInTemplate).
    $missing = (Select-String -LiteralPath $log -Pattern '^BindCheck missing: (.*)$').Matches[0].Groups[1].Value
    [IO.File]::WriteAllLines((Get-GodotMissingClassesPath $template), [string[]]($missing -split ' ' | Where-Object { $_ }))
}

# The list of GodotSharp's classes a template lacks, which the bind check writes beside it.
function Get-GodotMissingClassesPath([string]$template) {
    return Join-Path (Split-Path $template) 'missing-classes.txt'
}

# The classes of GodotSharp's (by their C# names) that an export's own assemblies, HBHT.*.dll
# in its data folder, refer to.
function Get-GodotClassesUsed([string]$dataFolder) {
    $used = [Collections.Generic.HashSet[string]]::new()
    foreach ($dll in Get-ChildItem -LiteralPath $dataFolder -Filter 'HBHT.*.dll' -File) {
        $stream = [IO.File]::OpenRead($dll.FullName)
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $md = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            foreach ($handle in $md.TypeReferences) {
                $type = $md.GetTypeReference($handle)
                if ($type.ResolutionScope.Kind -ne [Reflection.Metadata.HandleKind]::AssemblyReference) { continue }
                $assembly = $md.GetAssemblyReference([Reflection.Metadata.AssemblyReferenceHandle]$type.ResolutionScope)
                if ($md.GetString($assembly.Name) -eq 'GodotSharp' -and $md.GetString($type.Namespace) -eq 'Godot') {
                    [void]$used.Add($md.GetString($type.Name))
                }
            }
        } finally {
            $pe.Dispose()
            $stream.Dispose()
        }
    }
    return $used
}

# Fails if an export's assemblies use a class the template lacks: one the build profile leaves
# out or a disabled module holds. The bind check only binds the classes the template has.
function Assert-GodotClassesInTemplate([string]$dataFolder, [string]$template) {
    $listFile = Get-GodotMissingClassesPath $template
    if (-not (Test-Path -LiteralPath $listFile)) { throw "$template has no list of the classes it lacks ($listFile)" }
    $missing = [Collections.Generic.HashSet[string]]::new([string[]](Get-Content -LiteralPath $listFile))
    $used = Get-GodotClassesUsed $dataFolder
    if ($used.Count -eq 0) { throw "Found no use of GodotSharp in $dataFolder" }
    $wanted = @($used | Where-Object { $missing.Contains($_) } | Sort-Object)
    if ($wanted.Count -gt 0) {
        throw "The export uses classes HBHT's Godot template leaves out: $($wanted -join ', '). Take them out of tools/godot-template/hbht.gdbuild, or turn their module back on in hbht.py."
    }
    Write-Host "The export uses $($used.Count) of GodotSharp's classes, all in the template"
}

# This platform's template, built and checked on first use.
function Install-GodotTemplate {
    $platform = Get-HostPlatform
    $path = Get-GodotTemplatePath $platform
    if (Test-Path -LiteralPath $path) { return (Resolve-Path -LiteralPath $path).Path }
    # Built beside its place under its own name, which the export needs, and moved in once checked.
    $building = Join-Path (Split-Path $path) 'building'
    if (Test-Path -LiteralPath $building) { Remove-Item -LiteralPath $building -Recurse -Force }
    New-Item -ItemType Directory -Force $building | Out-Null
    $built = Join-Path ([IO.Path]::GetFullPath($building)) (Split-Path -Leaf $path)
    Build-GodotTemplate $platform $built
    Test-GodotTemplate $built
    Move-Item -LiteralPath $built -Destination $path -Force
    Move-Item -LiteralPath (Get-GodotMissingClassesPath $built) -Destination (Get-GodotMissingClassesPath $path) -Force
    Remove-Item -LiteralPath $building -Recurse -Force
    return (Resolve-Path -LiteralPath $path).Path
}
