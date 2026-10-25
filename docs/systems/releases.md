# Releases and packages

HBHT ships as two programs, the game (`hbht`, the Godot project `src/HBHT.Game`) and the editor (`hbht-editor`, `src/HBHT.Editor`), with no game content: players import their own ROM on first run. `tools/package.ps1` builds the packages for the machine it runs on, and the release workflow (`.github/workflows/release.yml`) runs it on Windows, Linux and macOS and publishes the result as a numbered prerelease.

## The packages

| Package | File | Data folder |
| --- | --- | --- |
| Windows installer | `HBHT-alpha-N-windows.msi` | `%LOCALAPPDATA%\HBHT` |
| Windows portable | `HBHT-alpha-N-windows-portable.zip` | beside the executables (`portable.txt`) |
| Linux | `HBHT-alpha-N-linux-x86_64.tar.gz` | `~/.local/share/HBHT` |
| macOS | `HBHT-alpha-N-macos.dmg` | `~/Library/Application Support/HBHT` |

On Windows and Linux both apps are in one folder, side by side, as Godot's .NET export lays each out:

```
hbht(.exe)            hbht-editor(.exe)
hbht.pck              hbht-editor.pck
data_HBHT.Game_<platform>_x86_64/     data_HBHT.Editor_<platform>_x86_64/
LICENSE   README.md   portable.txt (the portable zip only)
```

`<platform>` is `windows` or `linuxbsd`. The two exports don't collide: each executable finds the PCK with its own name, and its .NET assemblies and runtime in the data folder named after its assembly. The Windows zip has the files at its root (Explorer's Extract All makes the folder); the Linux tarball has them in a folder `HBHT-alpha-N/`. The first-run code finds the other app, and `portable.txt`, by these names, so they don't change.

On macOS the disk image holds `HBHT.app` (executable `Contents/MacOS/hbht`) and `HBHT Editor.app` (`Contents/MacOS/hbht-editor`), `LICENSE`, `README.md` and a link to Applications. Each bundle has its PCK and two data folders, `data_<assembly>_macos_arm64` and `_x86_64`, in `Contents/Resources`: the export is universal, and the .NET export publishes each architecture's runtime.

Every package is built with Godot's **release** templates, so `OS.IsDebugBuild()` is false in it, which is what keeps the debug keys out of a player's hands. The exports don't embed the PCK and have no console wrapper.

### Windows

The **MSI** (`packaging/windows/HBHT.wxs`, WiX 6.0.2 from the repository's tool manifest, `dotnet-tools.json`) installs per machine into `Program Files\HBHT`, with Start menu shortcuts "HBHT" and "HBHT Editor" and the icon in Add/Remove Programs. Its one page shows the license (WixUI_Minimal). The `UpgradeCode` is fixed and the package has a `MajorUpgrade`, so installing a newer release replaces the old one, and an older one refuses. The installer never writes the player's data folder, so uninstalling leaves the imported game, saves and settings where they are. The shortcuts' component is kept by a per-user registry value (`HKCU\Software\Aquatakat\HBHT`), which Windows Installer's checks ICE43 and ICE57 require of a shortcut; the package passes `wix msi validate`.

The **portable zip** is the same folder with `portable.txt` beside the executables, which tells HBHT to keep its data in that folder.

The executables carry the logo (`assets/branding/hbht.ico`) and version resources: product name "HBHT" or "HBHT Editor", company and copyright Aquatakat, file and product version `N.0.0.0`. Neither package is signed, so SmartScreen may warn on the first run ("More info", then "Run anyway").

### Linux

A **tar.gz** of the folder. It's what Godot exports, it keeps the executable bits, and it needs nothing to build or maintain; players unpack it anywhere and run `hbht`. The executables need the libraries any Godot 4 game does (Vulkan or OpenGL drivers, X11 or Wayland, ALSA or PulseAudio). AppImage and Flatpak were passed over for now: two programs in one AppImage need a launcher to choose between them, and Flatpak needs a manifest, a runtime and a repository to publish to, and sandboxes the paths the importer reads ROMs from.

### macOS

A **disk image** (`hdiutil`, on the macOS runner) with both apps, because a player drags apps out of a disk image into Applications as a matter of course, and the two have to stay side by side.

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
2. For each project, puts in place what its export needs and takes it away after: `config/version="N"` in `project.godot` (committed as `"0"`), which Godot gives to the Windows version resources and the macOS bundle versions; copies of `assets/branding/hbht.ico` and `hbht.icns` as `icon.ico` and `icon.icns`, which the presets name (gitignored, and excluded from the PCK); and `<assembly>.sln`, which Godot's .NET export refuses to run without, though it publishes the `.csproj` (the repository's solution is `HBHT.slnx`). `icon.png`, the project's `config/icon`, is a committed copy of `assets/branding/logo.png`, refreshed if they differ.
3. Imports and exports each project headless with its preset (`--export-release`). Godot exits 0 from a failed .NET export, so the script fails on any `ERROR:` line, and keeps Godot's output in `out/packages/work-<platform>/logs/`.
4. Lays out the folder in `out/packages/work-<platform>/` and writes the packages to `out/packages/`.

`-Version` is at most 255, an MSI's highest major version. The MSI's `ProductVersion` is `N.0.0` (Windows Installer compares only the first three fields), the executables' `N.0.0.0`.

The export presets (`export_presets.cfg` in each project, committed) hold nothing machine-specific: no export path, no signing identity. If the Godot editor's Export dialog is opened it writes them back with every option spelled out, which is harmless. A universal or arm64 macOS export requires ETC2/ASTC texture import, so both projects turn it on; neither imports any VRAM-compressed texture, so it changes nothing else.

## The release workflow

Run **Release** by hand from the repository's Actions tab (`workflow_dispatch`). It:

1. Picks the number: one more than the highest tag `v<number>`, or 1 with none. Two runs never overlap (`concurrency: release`).
2. Runs `dotnet test` on Windows. The real-data tests skip, as CI has no ROMs.
3. Runs `tools/package.ps1 -Version N` on `windows-latest`, `ubuntu-latest` and `macos-latest`, with .NET from `global.json` and `.tools/godot` cached by the hash of `tools/godot-pin.ps1`, and uploads the packages (and Godot's logs, if it fails).
4. Writes the description with `tools/release-notes.ps1`: what to download and how to open it, then one bullet per commit since the previous `v*` tag, newest first, each the first sentence of its message (cut at 280 characters) with a link to the commit, at most 100, and a link to the full comparison. The first release, with no tag before it, says it's the first alpha and links the history instead.
5. Creates the release `vN`, "HBHT alpha N", as a prerelease on the commit the workflow ran on, with the four packages attached (`gh release create`, which makes the tag).

Only the last job can write to the repository (`contents: write`); the rest read. The actions are GitHub's own, pinned to major versions.

To try the notes without a release: `tools/release-notes.ps1 -Version 2 -Previous HEAD~5 -RepositoryUrl https://github.com/Aquatakat/hbht`.

## Not done yet

- Nothing is signed with a certificate: no Authenticode on Windows, no Developer ID or notarization on macOS. Both need paid certificates and secrets in the workflow.
- Linux is x86_64 only, and Windows x86_64 only; Godot has arm64 templates for both.
- The game doesn't show its version anywhere yet; `ProjectSettings` holds it as `application/config/version`.
