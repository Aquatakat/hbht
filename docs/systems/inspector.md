# The asset inspector (roadmap 0.6)

Per [ADR 0009](../adr/0009-code-organisation.md), the Inspector is the one tool app, and the HTML viewers in `tools/` get no new features. Per [ADR 0010](../adr/0010-editor-reads-plugins.md), it becomes the editor, which reads only the imported plugins and draws them with the runtime's code. What this page describes is the raw Borg browser, which is now frozen: it gets fixes only, and it's deleted along with the HTML viewers once the editor shows what they show.

`src/Aidyn.Inspector` is a Godot app for browsing every Borg by type and index. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector` (build it first with `dotnet build`, or open it in the editor). It finds the ROMs in `roms/` (or `AIDYN_ROMS`) by hash and opens 1.0 first, or use **Open ROM…**.

- **The list** filters by type, and by index or summary text. Summaries fill in as it decodes the archive in the background, which takes a few seconds.
- **Preview:** textures with every mip level, and UI images. Models are shown alone and untextured, since scenes bind the textures. Scenes play all their animations. An animation plays in its scene, an actor plays its initial state, and a map chunk's collision shows where its first map set places it. Dialogue shows as text. Drag to turn, right-drag to pan, scroll to zoom. The 3D views are the CLI's glTF, loaded at run time with Godot's `GltfDocument`, so they match the scene viewer.
- **Godot loses texture wrap modes on import.** A `BaseMaterial3D` has one repeat switch for both axes and can't mirror, so glTF's mirrored repeat and one-axis clamp come out wrong. Faces are often half a texture mirrored across the middle (Donovan, scene 10363, lost half his face), and the knight's pauldrons (scene 9765) and the Gwernia house's door clamp or mirror too. `WrapMaterials` swaps those materials for a shader that wraps each axis itself. The runtime's N64 material will need the same.
- **Fields:** the decoded record, read by reflection, so a new decoder's fields show up with no inspector code. Lists expand in pages of 256. The Borg's `Borg_List` entry is below it.
- **Hex:** the decompressed bytes. Samples and sequences (1D) have only this so far.
- **For scripts and screenshots:** `tools/godot.ps1 --path src/Aidyn.Inspector -- --borg 10723 --tab preview --screenshot out/shot.png` opens a Borg, saves the window and quits. It gives up after a minute, so a hang can't block a script.

The decoding lives in `Aidyn.Rom.Inspection` (`BorgInspector`, `FieldTree`), which the tests check against every Borg; the Godot project is only UI. Like the CLI, it reads the ROM through `Aidyn.Rom` as a research tool. The editor won't: ADR 0010 keeps it on plugins, like the runtime.
