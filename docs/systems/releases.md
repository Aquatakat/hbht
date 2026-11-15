# Releases and packages

HBHT ships as two programs, the game (`hbht`, the Godot project `src/HBHT.Game`) and the editor (`hbht-editor`, `src/HBHT.Editor`), with no game content: players import their own ROM on first run. `tools/package.ps1` builds the packages for the machine it runs on, and the release workflow (`.github/workflows/release.yml`) runs it on Windows, Linux and macOS and publishes the result as a numbered beta release.

## The packages

| Package | File | Data folder |
| --- | --- | --- |
| Windows installer | `HBHT-beta-N-windows.msi` | `%LOCALAPPDATA%\HBHT` |
| Windows portable | `HBHT-beta-N-windows-portable.zip` | beside the executables (`portable.txt`) |
| Linux | `HBHT-beta-N-linux-x86_64.tar.gz` | `~/.local/share/HBHT` |
| macOS | `HBHT-beta-N-macos.dmg` | `~/Library/Application Support/HBHT` |

On Windows and Linux both apps are in one folder, side by side, as Godot's .NET export lays each out:

```
hbht(.exe)            hbht-editor(.exe)
hbht.pck              hbht-editor.pck
data_HBHT.Game_<platform>_x86_64/     data_HBHT.Editor_<platform>_x86_64/
LICENSE   README.md   portable.txt (the portable zip only)
```

`<platform>` is `windows` or `linuxbsd`. The two exports don't collide: each executable finds the PCK with its own name, and its .NET assemblies and runtime in the data folder named after its assembly. The Windows zip has the files at its root (Explorer's Extract All makes the folder); the Linux tarball has them in a folder `HBHT-beta-N/`. The first-run code finds the other app, and `portable.txt`, by these names, so they don't change.

On macOS the disk image holds `HBHT.app` (executable `Contents/MacOS/hbht`) and `HBHT Editor.app` (`Contents/MacOS/hbht-editor`), `LICENSE`, `README.md` and a link to Applications. Each bundle has its PCK and two data folders, `data_<assembly>_macos_arm64` and `_x86_64`, in `Contents/Resources`: the export is universal, and the .NET export publishes each architecture's runtime.

Releases 1 and 2 were alphas, "HBHT alpha N", published as prereleases; from 3 on they're betas, "HBHT beta N", published as full releases. The number carries on rather than starting again, as the MSI's upgrade needs it to rise.

Every package is built with Godot's **release** templates, so `OS.IsDebugBuild()` is false in it, which is what keeps the debug keys out of a player's hands. The exports don't embed the PCK and have no console wrapper.

### The .NET runtime

Godot's .NET export publishes each project self-contained, so each app's data folder holds a whole .NET runtime of its own. Both projects import `src/Godot.Export.props`, which trims it (`PublishTrimmed`, `TrimMode` partial): the framework's assemblies lose what neither app reaches, and those nothing reaches go, while ours and GodotSharp, which aren't marked trimmable, are kept whole, so reflection over them still works. That took each Windows data folder from 85 MB to 24, and the Windows packages from 158 MB (zip) and 120 (MSI) to 99.5 and 70; an import made by the trimmed editor is byte for byte the dev build's. Trimming turns two things off by default that the apps need, and the props turn them back on:

- **Native hosting** (`_EnableConsumingManagedCodeFromNativeHosting`): Godot starts .NET through hostfxr's `load_assembly_and_get_function_pointer`, which fails without it.
- **System.Text.Json's reflection** (`JsonSerializerIsReflectionEnabledByDefault`), which reads and writes the plugins and saves.

The trim warnings are about our own assemblies' reflection, which partial trimming leaves alone, so they're suppressed (`TreatWarningsAsErrors` would fail the export on them). After publishing, the props delete what only a debugger or a crash dump uses (the DAC `mscordaccore`, `mscordbi`, `createdump`, the PDB writer `Microsoft.DiaSymReader.Native`) and HTTP/3's `msquic.dll`, which trimming turns off: about 7 MB a platform.

What's left is mostly the engine, HBHT's own template ([the engine](#the-engine)), and the two executables are the same but for their icon and version resources. On Linux they're byte for byte the same, and so are most of the two data folders, so the script hard-links each of the editor's files to the game's where they match (`Join-SameFiles`), which tar keeps: the tarball holds them once and unpacks them once. The macOS disk image is ULMO (LZMA), which opens from macOS 10.15, the oldest the presets allow, and packs smaller than UDZO's zlib.

### The engine

Both executables are HBHT's own build of the pinned Godot's .NET release template, made from its source for size: `tools/godot-template.ps1` builds it the first time it's wanted, with the SCons options in `tools/godot-template/hbht.py` and the build profile `tools/godot-template/hbht.gdbuild`, and keeps it in `.tools/godot-template/<key>/`, where the key hashes the pin, the two profiles and the script, so changing any of them builds it again. On Windows it's 44.5 MB against the stock template's 109.5, and the packages went from 99.5 MB (zip) and 70.4 (MSI) to 54.8 and 36.0. On Linux it's 33.2 MB against 73.7, and the tarball went from 42.4 MB to 26.2 (built and packaged in an Ubuntu container, as the runner will). The macOS build hasn't run yet ([not done yet](#not-done-yet)); the stock universal template is 170.6 MB. What it changes:

- **`optimize=size` and full LTO**, under `production=yes`, without debug symbols.
- **The modules neither app uses** are off: GDScript, Jolt and Godot's own physics, navigation, OpenXR and the other XR, CSG and GridMap, the audio codecs (Ogg, Vorbis, MP3, Theora, interactive music), the image formats bar PNG, WebP and SVG, the network ones (mbedTLS, ENet, WebSocket, WebRTC, UPnP, multiplayer), Embree's raycast, regex, noise, visual shaders, ZIP and the texture compressors. Kept: mono, glTF (actors and chunks are glTF), FreeType and the advanced text server, SVG (the default theme's icons), WebP (the icon's import), glslang (Vulkan's shaders), meshoptimizer, bcdec, and astcenc, which ANGLE's library links against.
- **The build profile** leaves 260 classes unregistered, so the linker drops them: the 2D nodes, the 3D nodes nothing makes (particles, GI, fog, occluders, paths, sprites, the skeleton modifiers and IK), AnimationTree and its nodes, the audio effects, the tile sets, and a few controls (RichTextLabel, CodeEdit, ColorPicker, MenuBar and some others). It keeps everything either app names in its code and the classes Godot makes itself for those: the glTF loader's Skeleton3D, BoneAttachment3D and lights, Tree's TextEdit, FileDialog's MenuButton, the scroll bars. Godot's Engine Compilation Configuration editor can't see this: both apps make their nodes in C#, not in scenes. The list came from the types the three assemblies refer to in GodotSharp (System.Reflection.Metadata over `HBHT.Game`, `HBHT.Views` and `HBHT.Editor`) and the engine's own `memnew` of the candidates; it saved 5 MB.
- **No Direct3D 12 and no WinRT.** Godot uses Vulkan on Windows and falls back to Direct3D 12 only where Vulkan fails; without it the fallback is Compatibility, which the game supports (it draws no decals, which the shadows are, [rendering](rendering.md#the-renderers)). D3D12 with its Mesa NIR cost 9.5 MB. WinRT is HDR output, the emoji picker and text-to-speech, which neither app uses, and needs a newer Windows SDK than Visual Studio 2019's.
- **ANGLE and AccessKit** stay, as in Godot's own builds, fetched by Godot's scripts from Godot's releases: ANGLE for Compatibility on the GPUs Godot sends to it, AccessKit for screen readers over the editor's controls. SDL stays for gamepads.

**What can't go:** GodotSharp looks up every method of a class in the class's static constructor, the first time the class is used, so a template missing one method fails the whole class. Godot's `disable_physics_3d`, `disable_navigation_3d` and `disable_xr` (and their 2D twins) take methods off classes the apps use (`Viewport.set_physics_object_picking`, `MeshInstance3D.create_trimesh_collision`, `RenderingServer.viewport_set_use_xr`), and the editor failed at its first `Viewport`. So only the modules behind them go, and the servers are Godot's dummies; both projects say so (`physics/2d/physics_engine` and `physics/3d/physics_engine` are `"Dummy"`), which keeps Godot from warning at every start.

**The checks.** Each template is checked as it's built (`Test-GodotTemplate`): the bind check, `tools/godot-template/bindcheck`, a .NET project exported with the new template and run headless, runs the static constructor of every GodotSharp class the template registers (524) and fails on any method it can't bind. On macOS its preset carries the apps' `codesign/entitlements`, without which the system kills it at launch (exit 137). It also lists the classes the template lacks, by their C# names, in `missing-classes.txt` beside it, and `tools/package.ps1` fails an export whose own assemblies refer to any of them (`Assert-GodotClassesInTemplate`), so an app that starts using a class the profile or a module took out stops the package rather than a player's game. To use one, take it out of `hbht.gdbuild`, or turn its module back on in `hbht.py`.

Checked on Windows against the stock template, both exported from the same tree: an import by the new editor (`--rom … --import 1`, into a scratch `HBHT_DATA`) is byte for byte the installed one and the CLI's (6,944 files); trek, a battle and the editor's map-set and actor pages draw; and deterministic movies match the stock template's frame for frame, all 180 of a battle under Forward+ and all 120 under Compatibility. A cut-scene's (script 0x0365) differ in 83 of 240 frames by one level in 255 on a few thousand pixels at most, which the stock template doesn't against itself: Godot's Windows templates are built with MinGW GCC 15 and HBHT's with MSVC, whose floating point rounds otherwise.

The other variants built on Windows: the `disable_*` options instead of the modules, 46.6 MB (41.6 with the profile), which GodotSharp can't bind; the fallback text server instead of the advanced one, 2 MB less, which shapes no right-to-left or complex scripts in the editor's file paths and fields; Direct3D 12, 9.5 MB more.

On Linux, in the container, the template bound as on Windows, both apps exported with it, and the game (a battle) and the editor ran headless, logging what the stock template's did (one shader error of the headless renderer).

Building it needs Python 3 and the platform's toolchain: Visual Studio 2019 16.11 or later with C++ on Windows, Xcode on macOS; on Linux, Godot's buildroot toolchain, which the script fetches: the 2023.08 one, which Godot's 4.7.2 templates were built with, so the template needs glibc 2.28 as the stock one does (Godot's 2026.05 toolchain needs 2.34). On macOS it builds arm64 and x86_64, links MoltenVK in (`use_volk=no`, as Godot's own macOS template does) from the Vulkan SDK Godot's script installs, and joins them with `lipo` into the bundle Godot's `macos.zip` holds. On the user's 32-thread machine a Windows build takes 2 to 4 minutes. `tools/package.ps1 -StockTemplate` exports with Godot's stock template instead, on a machine without the toolchain.

### Windows

The **MSI** (`packaging/windows/HBHT.wxs`, WiX 6.0.2 from the repository's tool manifest, `dotnet-tools.json`) installs per machine into `Program Files\HBHT`, with Start menu shortcuts "HBHT" and "HBHT Editor" and the icon in Add/Remove Programs. Its one page shows the license (WixUI_Minimal). The `UpgradeCode` is fixed and the package has a `MajorUpgrade`, so installing a newer release replaces the old one, and an older one refuses. The installer never writes the player's data folder, so uninstalling leaves the imported game, saves and settings where they are. The shortcuts' component is kept by a per-user registry value (`HKCU\Software\Aquatakat\HBHT`), which Windows Installer's checks ICE43 and ICE57 require of a shortcut; the package passes `wix msi validate`.

The **portable zip** is the same folder with `portable.txt` beside the executables, which tells HBHT to keep its data in that folder.

The executables carry the logo (`assets/branding/hbht.ico`) and version resources: product name "HBHT" or "HBHT Editor", company and copyright Aquatakat, file and product version `N.0.0.0`. Neither package is signed, so SmartScreen may warn on the first run ("More info", then "Run anyway").

### Linux

A **tar.gz** of the folder, which keeps the executable bits; players unpack it anywhere and run `hbht`. The executables need the libraries any Godot 4 game does (Vulkan or OpenGL drivers, X11 or Wayland, ALSA or PulseAudio). Not AppImage, as two programs in one need a launcher to choose between them, nor Flatpak, which needs a manifest, a runtime and a repository, and sandboxes the paths the importer reads ROMs from.

### macOS

A **disk image** (`hdiutil`, on the macOS runner) with both apps, which the player drags to Applications together: the two have to stay side by side.

Godot names a bundle's executable and PCK after the project (`HBHT Editor`), so the script renames them to `hbht` and `hbht-editor`, with `CFBundleExecutable`, and signs each bundle again ad hoc (`codesign --sign -`), since the rename breaks Godot's signature and Apple silicon runs nothing unsigned. Godot looks for the PCK in `Contents/Resources` by the executable's name, so the two renames go together. Bundle identifiers are `io.github.aquatakat.hbht` and `io.github.aquatakat.hbht.editor`.

There's no Developer ID, so the apps aren't notarized and Gatekeeper stops them the first time. A player:

1. Drags both apps from the disk image to Applications (or any one folder, together).
2. Opens HBHT. macOS says it can't check it for malicious software; choose Done.
3. In System Settings, Privacy & Security, chooses **Open Anyway** beside the message about HBHT, and confirms with their password. Then the same for HBHT Editor.

Or, in Terminal, clears the quarantine once: `xattr -dr com.apple.quarantine /Applications/HBHT.app "/Applications/HBHT Editor.app"`.

Running the apps straight from the disk image or the Downloads folder works, but while they are quarantined macOS runs each from a random read-only copy (App Translocation), where the game can't find the editor beside it. Moving them clears that.

## Building locally

```powershell
tools/package.ps1 -Version 7     # the packages for this machine, as release 7
tools/package.ps1                # as release 0
```

The script:

1. Fetches the pinned Godot editor and the .NET export templates into `.tools/godot/` (`tools/godot-pin.ps1`, which `tools/godot.ps1` shares), each checked against the SHA-512 in the release's `SHA512-SUMS.txt`. The templates come as one 1.2 GB `.tpz` of every platform; only the four desktop release files are kept, in the self-contained editor's `editor_data/export_templates/4.7.2.stable.mono/`.
2. Builds HBHT's template for this platform, if `.tools/godot-template/` doesn't have it yet, and checks it ([the engine](#the-engine)): the source tarball from the same release, checked against the same sums, is unpacked into `.tools/godot-build/`, with SCons in a virtual environment there. `-StockTemplate` skips this and exports with the stock template; `-Template <file>` exports with that one.
3. For each project, puts in place what its export needs and takes it away after: `config/version="N"` in `project.godot` (committed as `"0"`), which Godot gives to the Windows version resources and the macOS bundle versions; copies of `assets/branding/hbht.ico` and `hbht.icns` as `icon.ico` and `icon.icns`, which the presets name (gitignored, and excluded from the PCK); and `<assembly>.sln`, which Godot's .NET export refuses to run without, though it publishes the `.csproj` (the repository's solution is `HBHT.slnx`). `icon.png`, the project's `config/icon`, is a committed copy of `assets/branding/logo.png`, refreshed if they differ. With HBHT's template, the preset's `custom_template/release` names it for the run.
4. Imports and exports each project headless with its preset (`--export-release`). Godot exits 0 from a failed .NET export, so the script fails on any `ERROR:` line, and keeps Godot's output in `out/packages/work-<platform>/logs/`. It then fails if the export's assemblies use a class the template lacks.
5. Lays out the folder in `out/packages/work-<platform>/` and writes the packages to `out/packages/`.

`-Version` is at most 255, an MSI's highest major version. The MSI's `ProductVersion` is `N.0.0` (Windows Installer compares only the first three fields), the executables' `N.0.0.0`.

The export presets (`export_presets.cfg` in each project, committed) hold nothing machine-specific: no export path, no signing identity. If the Godot editor's Export dialog is opened it writes them back with every option spelled out, which is harmless. A universal or arm64 macOS export requires ETC2/ASTC texture import, so both projects turn it on; neither imports any VRAM-compressed texture, so it changes nothing else.

## The release workflow

Run **Release** by hand from the repository's Actions tab (`workflow_dispatch`). It:

1. Picks the number: one more than the highest tag `v<number>`, or 1 with none. Two runs never overlap (`concurrency: release`).
2. Runs `dotnet test` on Windows. The real-data tests skip, as CI has no ROMs.
3. Builds HBHT's template on each runner with **Godot template** (`.github/workflows/godot-template.yml`), unless the Actions cache has it: the cache key hashes the pin, the two profiles and `tools/godot-template.ps1`, as the script's own key does. A build takes up to four hours before it's given up on; macOS builds twice. Run **Godot template** by hand to build ahead of a release after changing any of them. GitHub drops a cache entry nobody has read for a week, so a release more than a week after the last builds the templates again.
4. Runs `tools/package.ps1 -Version N` on `windows-latest`, `ubuntu-latest` and `macos-latest`, with .NET from `global.json`, `.tools/godot` cached by the hash of `tools/godot-pin.ps1` and the template restored from the cache (the job fails without it), and uploads the packages (and Godot's logs, if it fails).
5. Writes the description with `tools/release-notes.ps1`: what to download and how to open it, then one bullet per commit since the previous `v*` tag, newest first, each the first sentence of its message (cut at 280 characters) with a link to the commit, at most 100, and a link to the full comparison. The first release, with no tag before it, says so and links the history instead.
6. Creates the release `vN`, "HBHT beta N", on the commit the workflow ran on, with the four packages attached (`gh release create`, which makes the tag).

Only the last job can write to the repository (`contents: write`); the rest read. The actions are GitHub's own, pinned to major versions.

To try the notes without a release: `tools/release-notes.ps1 -Version 2 -Previous HEAD~5 -RepositoryUrl https://github.com/Aquatakat/hbht`.

## Not done yet

- HBHT's template has been built and checked on Windows, and on Linux in a container ([the engine](#the-engine)), but the macOS build (two architectures, MoltenVK from the Vulkan SDK, `lipo`, the bundle) has never run: nothing here runs macOS. Its first run on GitHub built both architectures, the universal binary and the bundle, and exported the bind check, which macOS then killed at launch (exit 137, no output): the bind check's macOS preset lacked the entitlements the apps' presets give .NET's JIT (`allow_jit_code_execution`, `allow_unsigned_executable_memory`), and an ad-hoc signed, hardened app that JITs without them is killed by the system. It has them now; run **Godot template** again before the next release, and measure the disk image.
- Merging the game and the editor into one Godot project, one executable started as either, would ship the engine and the runtime once, but they're separate projects by design ([ADR 0022](../adr/0022-views-library.md)).
- Nothing is signed with a certificate: no Authenticode on Windows, no Developer ID or notarization on macOS. Both need paid certificates and secrets in the workflow.
- Linux is x86_64 only, and Windows x86_64 only; Godot has arm64 templates for both.
- The game doesn't show its version anywhere yet; `ProjectSettings` holds it as `application/config/version`.
