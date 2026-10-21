# 0013. Graphics and fidelity settings live in the game's own options menu

Status: Accepted · Oct 3, 2026 · Its consequences on the runtime's `ApplySettings` and the command line's settings are superseded by [0022](0022-the-core-is-handed-its-settings-each-tick.md)

## Context

Ports like this one usually put their graphics and fidelity settings in a launcher outside the game. The roadmap's Phase 6 keeps every enhancement behind a fidelity profile, and the runtime had gathered several such settings with nowhere to change them but the command line and debug keys: the UI screen's height and filter, the draw distance, the sky's placement and the pace.

The original's options menu has room for two volumes, a resolution toggle that needs the Expansion Pak, and Rename Character. Its layout is all capitals under headings.

## Decision

- **The settings are in the options menu**, which the pause menu's System tab opens, and later the title screen's menu. They aren't in a launcher. The engine's settings are part of the engine, so the menu should feel like part of the game.
- **The original's menu keeps its volumes, with their rope bars, and Rename Character.** Its resolution row becomes **Graphics**, which opens a page of the port's settings under the same Options title, so no new header art is needed. The page lists every setting with its value, uncategorised. The user chose this layout, and mixed case over the original's capitals.
- **The settings are the player's, not the game's.** They're kept in `settings.json` in the data folder, not in saves. The original writes its volumes and resolution into every save, so loading a game set them back. The port doesn't.
- **The options menu's model is shared.** `OptionsMenu` in the core builds its rows over `Settings` and takes whether it's in a game, which adds Rename Character. The title screen's menu will open the same model without it, as the original's title options leave out its GAME rows.

## Consequences

- A new setting is a property on `Settings`, a row in `OptionsMenu`'s graphics page, and a line in the runtime's `ApplySettings`.
- Command-line options such as `--ui-lines` override a setting for one run. They're written to the file only if something in the menu then changes.
- The fidelity profile at the end of the roadmap can be a set of these settings.
