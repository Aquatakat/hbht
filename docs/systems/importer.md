# The importer and the base plugin (M1)

`hbht import <rom>` turns a ROM into `aidyn.base`, the plugin the runtime loads (ADR 0001). It goes to `plugins/aidyn.base` in the data folder ([runtime](runtime.md#the-data-folder)): from the CLI, `HBHT_DATA` if set, else `%LOCALAPPDATA%\HBHT`. `--out` puts it elsewhere. It refuses the debug ROM (ADR 0007). The editor's import page runs the same importer, asking for NA 1.0 alone ([editor](editor.md#the-import-page)).

## The player's choices

`BaseImporter.Import` takes an `ImportOptions` (in `HBHT.Plugins`), `ImportOptions.Default` if none, and the manifest records it (`import.options`), so the editor and a re-import can tell what was chosen. An import made before it was recorded had the defaults.

- **`FixMushroom`** (default on): `MapCorrections` moves the Roog Cave's mushroom out of the passage's neck ([hardcoded content](../hardcoded-content.md#corrections-to-the-roms-maps)). Off, it stands where the ROM has it, with its collision. `hbht import <rom> --keep-mushroom` turns it off; the editor's import page offers it as **Fix the mushroom (recommended)** or **Don't fix the mushroom**.
- **`WeldSeams`** (default on): `SeamWelds` closes the cracks between meshes that miss by a hair, and `SceneGltf` writes the welded vertices where it puts them ([rendering](rendering.md#seams-between-meshes), [ADR 0030](../adr/0030-the-import-welds-the-seams.md)). Off, the scenes' vertices are where the ROM has them. `hbht import <rom> --keep-seams` turns it off; the editor's import page offers it as **Close the cracks in the scenery (recommended)** or **Keep the cracks**.

The mushroom's defaults left the records as importer 61 wrote them, so recording the choice didn't raise `BasePlugin.ImporterVersion`; welding by default is 62. A choice that changes the records is still the same importer: `Diagnose` goes by the version alone, and a re-import is the player's to ask for.

## The flow

`BaseImporter.Import` stages the plugin in a sibling folder (`<dir>.importing`) and moves it into place when it's done. An `ImportContext` holds what the families share: the ROM, its Borg archive and DB, each code-segment table read once on first use (`EngineTables`, `BodySoundTables`, `AudioTables` and the rest), and the writers for records and assets. An importer per family then builds its records, in this order:

- `WorldImporter`: map sets, terrains, weather, the World tab's map, camping, the new game, the title, the sky.
- `CombatImporter`: the combat record (`aidyn:combat/0`) and its `text` ([combat](combat.md), [hardcoded content](../hardcoded-content.md#bespoke-behaviours)).
- `MagicImporter`: spells' and potions' effects and visuals, and the magic record (`aidyn:magic/0`) ([magic](magic.md)). The ROM keeps no effects as data, since the original's code switches on a spell's kind, so each is a fact checked against 1.0's machine code, and the importer fails on a spell without one, a fact without a spell, or a name or entity place that differs.
- `FlagImporter`: flags, the journal, the state flags.
- `CharacterImporter`: dialogue entities' names, entities, spells, stats, skills, progression.
- `ItemImporter`: weapons, armor, items, loot, shops, recipes, inventory.
- `TextImporter`: strings, fonts ([text](../formats/text.md)).
- `DialogueImporter`: Borg13 scripts.
- `CinematicImporter`: the four cinematics, the credits.
- `TriggerImporter`: the base game's triggers, `aidyn:trigger/<n>` in `triggers.json` ([events](events.md)).
- `MapImporter`: minimaps, maps and their collision.
- `GraphicsImporter`: actors, scenes and their emitters, textures, UI images.
- `AudioImporter`: the sounds record, sequences, samples.

`BodySoundImporter` and `MinimapImporter` build the sounds and minimap records the others write.

## The base plugin

- **`plugin.json`** records the importer version (`BasePlugin.ImporterVersion`), the ROM's SHA-1 and version, and the player's choices (`options`). `BasePlugin.Diagnose`, which the runtime, the CLI and the editor all go by, refuses a base another importer version wrote, so the player imports again.
- **`records/`** holds a JSON file per family (`entities.json`, `combat.json`, `triggers.json` and so on), each record's system doc describing it. The loader reads every JSON file under `records/`, whatever the layout (ADR 0005), so the split is only for reading and diffing. Maps are a file per map set (`maps/Overworld.json` and so on).
- **`assets/aidyn/<type>/<key>`** holds every scene as a glb (`aidyn:scene/0xHHHH`, the same glTF as `hbht scenes`), with locator nodes (`locator <i>`) for what the game attaches at run time; every chunk's collision (`aidyn:map/0xHHHH`, in the chunk's own coordinates); every texture and UI image as a PNG (`aidyn:texture/0xHHHH`, full size only, and `aidyn:image/0xHHHH`, which includes the sky gradients); every Borg12's events (`sequence/0xHHHH.dcm`) and every Borg11 (`sample/0xHHHH.wav`, [audio](../formats/audio.md#in-the-base-plugin)). Scenes carry their own textures; the texture records are for what the engine draws with a texture alone, such as bodies' shadows and particles. A mod replaces an asset by putting a file at the same path in its own `assets/`. The core reads some itself, headlessly, through `HBHT.Plugins.Assets`: collision (`CollisionMesh`), scenes' triangles (`SceneMesh`), actors' timing and root motion (`ActorClips`), and images' sizes (`PluginSet.ImageSize`).
- **Record IDs** follow ADR 0004: `aidyn:<type>/<key>`, the key a DB `ItemID` or Borg index as `0xHHHH`, a decimal index, or for a map object the map's `0xHHHH` then its index. Map sets are numbered as the original's `EnumMapDatA`; a map (`aidyn:map/0xHHHH`) holds its map objects of every type but the code objects ([maps](../formats/maps.md#map-objects-in-the-base-plugin-06)).

## Conventions

- **Reading the ROM.** A reader of the ROM's bytes uses `Be` (big-endian fields, tables as arrays, lists up to a terminator, a Borg's bounds-checked slice) and `RomImage.Table` for a code-segment or DB table's range in the version's layout ([rom](../formats/rom.md#where-each-version-has-its-tables)).
- **`Rom` types and records.** A type in `HBHT.Rom` that is the ROM's form of something a plugin record also models takes a `Rom` prefix (`RomEntityRecord`, `RomCinematic` and so on), so no name in the two assemblies means two types and the importers need no `using` aliases. An enum both sides need is defined once, in `HBHT.Plugins` (the Borg9 reader parses straight into `CollisionFlags` and `CollisionRole`). Where the ROM's enum has members the record's leaves out, the importer casts one into the other, and `ParallelEnumTests` checks that the names and values still match.
- **Facts** are what the original's code holds as a switch or literal rather than a table. The importer carries them, checked against 1.0's machine code, and refuses a ROM where one doesn't fit (a sky image that isn't a UI image, a position list that doesn't match its DB). [hardcoded content](../hardcoded-content.md) maps each.
- **Corrections** to the ROM's data are the user's choice, one at a time, and keyed on the exact record, which the importer checks first: `MapCorrections` for the maps, listed in [hardcoded content](../hardcoded-content.md#corrections-to-the-roms-maps).
- **Bump `BasePlugin.ImporterVersion`** whenever what the importer writes changes, with a line in its comment saying what changed; a stale base is then refused rather than read wrongly. Branches made in parallel each bump it, so renumber on merge.
- **Deterministic.** Two imports are byte-identical, which a test checks. JSON is indented, with vectors and colours on one line, so two imports diff cleanly.

The format the importer writes in, and how plugins load and merge, is in [plugins](plugins.md).
