# 0030. The import welds the seams between meshes, as an option on by default

Status: Accepted · Oct 30, 2026 · Adds to [0029](0029-content-fixes-wait-for-a-bugfix-mod.md)

## Context

The original's meshes often miss each other by a hair where they're meant to meet: between neighbouring chunks' scenery, and within a model. The console shows the background through the gap as dashes, and so does the port ([rendering](../systems/rendering.md#seams-between-meshes)). Another port draws something dark behind the gaps so the sky doesn't show. This one owns every mesh through the importer, so it can close the gaps themselves.

[0029](0029-content-fixes-wait-for-a-bugfix-mod.md) keeps the base plugin as the ROM's content, but for fixes the game needs to be finishable, and sends every other content fix to a bugfix mod of patches to records. Welding doesn't fit that. The cracks don't break the game. The fix isn't a patch to records either: it changes the scenes' glTF assets, which are made from the ROM, so a mod couldn't ship it without carrying the ROM's own geometry. It has to be made from the player's own ROM, at import.

The import is the right time anyway. The welds are worked out once, so the editor shows what the game shows, and nothing changes as the chunk streamer loads neighbours in any order. Collision is separate (Borg9), so the rules don't change.

## Decision

- **The importer welds the seams** (`SeamWelds`, [rendering](../systems/rendering.md#seams-between-meshes)) as an import option, `ImportOptions.WeldSeams`, on by default. Off, the meshes are where the ROM has them.
- **0029's rule stays for content:** records and what plays are fixed in the import only where the game would otherwise break. A change to how imported assets are drawn, made the same way from every ROM and changing nothing of the rules, can be an import option instead, as this one is.

## Consequences

- A mod that replaces a chunk's scene isn't welded to the base plugin's neighbours. The welder is a library step that takes parsed scenes, so the editor can offer it for a mod's scenes once the mod tools exist (Phase 5).
- A player who wants the original's cracks imports again with the option off: `hbht import <rom> --keep-seams`, or **Keep the cracks** on the editor's import page.
