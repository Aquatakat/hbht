# 0027. Cut-scenes and conversations take the menus' controls

Status: Accepted · Oct 25, 2026 · Supersedes [0026](0026-controls-are-bound-by-situation.md)'s cut-scenes and conversations as a situation with controls of their own

## Context

[0026](0026-controls-are-bound-by-situation.md) listed cut-scenes and conversations as a fourth situation on the Controls page, with four controls of their own: Choose, Continue, Close and Skip. Each had the same defaults as one of the menus' (Move Cursor, Accept, Back, Close Menu), and they do the same things to the game. The page listed eight rows that a player would have to rebind in pairs to keep the menus and conversations alike, and a binding changed on one side but not the other would leave the page saying one thing while a conversation did another.

## Decision

- **Cut-scenes and conversations are worked by the menus' controls.** Move Cursor moves a conversation's choices, Accept continues it, Back closes it and Close Menu skips a cut-scene; so do they for trek's popups, a backdrop's or the help menu's page, a cinematic and the credits. Those four controls are live in both situations (`Situation.Menus | Situation.Talking`), and the Talking situation has no controls of its own, so the page lists no heading for it.
- **`Situation.Talking` stays** as what a mode is (`Situations.Of`), so conflicts are still checked within it, and the menus' other controls (Details, Appraise, the members and the tabs) don't reach a conversation.
- **The essential controls** are trek's Menu, the menus' Move Cursor, Accept and Back, and combat's Pause. A conversation's Continue was essential; it's the menus' Accept now, which already is.

## Consequences

- A player's saved `talk_*` bindings are dropped as unknown controls when `settings.json` is read; the menus' bindings, which had the same defaults, take their place.
- `--press` presses the menus' actions in a conversation: `menu_accept` and `menu_down`, not `talk_continue` and `choose_down`.
