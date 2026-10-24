# 0024. The options are pages by kind, in Title Case

Status: Accepted · Oct 24, 2026 · Supersedes [0013](0013-settings-in-the-options-menu.md)'s layout: one uncategorised Graphics page, in mixed case

## Context

[0013](0013-settings-in-the-options-menu.md) kept the original's options page, with its volumes and Rename Character, and turned its resolution row into Graphics, a page listing every one of the port's settings. By M4 that page had twelve rows. At 12 pixels a row from y 72, the last ran off the parchment, and more were coming: Video settings the window needs, a farther terrain distance, and the controls.

The rows were in sentence case ("Sound volume:", "Menu size"), as the original's dialogue is. The user found that it reads oddly in a menu, where the original's own labels are in Title Case.

## Decision

- **The main page links to a page for each kind of setting:** Audio, Video and Enhancements, then Rename Character in a game, set off by half a line. Controls will join them. Each page is under the same Options title, and B comes back to the row that led there.
- **Audio** holds the original's volumes with their rope bars. **Video** holds the window and how the 3D world is drawn into it. **Enhancements** holds the settings that go beyond the original's look and feel.
- **Labels and values are in Title Case** ("Sound Volume:", "Render Resolution", "Video Out"), with short words such as "and" in lower case.
- **The sky's placement is off the menu.** The projected sky never looks out of place and the original's placement does, so the projected sky is what's drawn. F6 and `--sky original` still draw the original's, for comparing.
- 0013's other decisions stand: the settings are in the game's own menu and not a launcher, they're the player's, kept in `settings.json`, and the model is shared with the title screen's menu.

## Consequences

- A new setting is a row on the page for its kind (`OptionsMenu.VideoRows`, `EnhancementRows`), not on one long list. A page past about ten rows needs splitting or scrolling.
- The old Draw distance setting is now Object Distance (Original, 2x, 3x), and Draw Distance is the terrain's (Original, Extended). `settings.json` skips keys it doesn't know, so an old file's `drawDistance` is dropped rather than failing to load.
