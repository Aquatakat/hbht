# 0005. Plugin format

Status: Accepted · Sep 26, 2026 · Open formats for every asset is superseded for sequences by [0016](0016-sequences-keep-the-originals-events.md)

## Context

Plugins are the only way content reaches the engine, including the base game ([0001](0001-importer-owns-the-rom.md)). The source format has to diff cleanly in git and be editable by hand and by tools. Loading has to stay fast.

In Bethesda's formats, the last plugin to touch a record replaces all of it. Two mods that change different fields of the same record then conflict for no reason.

The options were JSON, YAML and TOML. YAML needs a third-party parser and has implicit-typing traps (`no` parses as false). TOML is awkward for deeply nested data such as dialogue graphs.

## Decision

### Source format

Plugin source is **JSON with comments**. It is parsed with `System.Text.Json`, with comments skipped and trailing commas allowed, so there is no extra dependency. JSON Schema files are generated from the C# record types, which gives modders validation and autocomplete in ordinary editors.

A plugin is a directory, or a zip of one:

```
mymod/
  plugin.json        manifest: id (= namespace), name, version (semver), dependencies, load-after hints
  records/**/*.json  records and patches; any file layout the author likes
  assets/            asset overrides and new assets
```

### Records and patches

A record file holds new records (full objects with an ID in the plugin's own namespace) and patches to other plugins' records:

```jsonc
{
  // Patch one field of a base-game weapon; every other field is untouched.
  "patch": "aidyn:weapon/0x0712",
  "damage": 9
}
```

- **Objects merge field by field**, following JSON Merge Patch (RFC 7386). A scalar replaces the old value, a nested object merges recursively, and `null` removes a field.
- **Lists are declared in the schema.** A list whose elements have identity, such as shop stock or voxels, is keyed and merges element by element. Any other list is replaced whole. Finer list operations, such as insert-before in a dialogue, are designed in Phase 5, but the keyed-list rule holds from the start.
- The loader reports every field that more than one plugin set, with the winner. This is the conflict report.

### Assets

Asset files use open formats and are keyed by the record ID they provide or replace:

| Kind | Format |
| --- | --- |
| Meshes, scenes, animation | glTF 2.0 (`.glb`) |
| Textures and UI images | PNG |
| Audio | WAV (lossless, used by the importer), OGG (for mods) |

### Compiled cache

At load time the merged result is written to a binary cache, keyed by a hash of the plugin set, load order and plugin contents. The cache format is private and can change in any version. It is never distributed and never edited.

## Consequences

- `aidyn.base` is written as JSON source like any other plugin, so importer changes can be diffed and modders can read what they're patching.
- Hex values that JSON can't express as literals appear only in IDs, which are strings anyway.
- Field-level merging needs a schema for every record type, including which lists are keyed. That schema is part of the plugin format and is versioned with it.
- A mod author can see what changed and why, from the conflict report, without special tools.
