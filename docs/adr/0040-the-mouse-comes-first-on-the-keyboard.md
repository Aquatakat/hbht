# 0040. The mouse comes first on the keyboard, and a click in the menus takes what's under it

Status: Accepted · Nov 15, 2026 · Supersedes [0026](0026-controls-are-bound-by-situation.md)'s defaults being the bindings the runtime had, for the keyboard

## Context

[0026](0026-controls-are-bound-by-situation.md) kept the runtime's old bindings as the defaults, so the keyboard's camera was a right-button drag (Camera Hold) and mouse look waited on the Controls page. The mouse did nothing else in trek or a battle, and in the menus it only hovered rows and turned the wheel. Played on a keyboard and mouse, the game felt like a controller port.

## Decision

- **The keyboard's defaults put the mouse first.** Camera takes the mouse, so mouse look is on and the mouse is captured in trek and a battle's turn. Camera Hold has no keyboard binding, since mouse look needs no drag. The left button is A where A has no pointer to aim (Use in trek, Attack in a battle), and the right button is B everywhere (Portraits, Back, Cancel). Each is a second binding beside the key the runtime had. The controller's defaults stay the runtime's. A player's saved bindings are kept, since `settings.json` holds only what they changed.
- **In the menus and conversations a left click is a pointer, not a binding.** It takes what's under it: a row, a choice, a portrait, a tab's arrow. On nothing it does nothing, so a click on bare parchment can't accept the row the keyboard left the cursor on. A screen with nothing to aim at (a line waiting for A, a message popup, a book's page) is one target, which a click anywhere accepts. The menus' Accept has no mouse button for that reason. A view that doesn't take a click lets it go on to the game, so the right button reaches Back as a binding.
- **The wheel scrolls a scrolling view, and a click chooses in it.** Where a list scrolls (the Party tab's lists, the System tab's, the journal, a battle's held menus) the wheel moves the view and the pointer chooses. Up and down, on the keys or the stick, still step the cursor row by row, so a mouse without a wheel or a player without a mouse reaches every row.
- **The wheel does nothing in trek.** The original has nothing it would stand for, so the port doesn't invent a use for it. Nor is there a marker on what Use would reach, in trek or a battle: the user found it unneeded.
- **Invert Mouse Y and Invert Camera Y** are rows on the Controls page, the first on the keyboard's page and the second on the controller's, each off by default. Invert Camera Y turns only the controller's sources on Camera, and only as they steer the camera, never as they walk him.

## Consequences

- The menus' clicks are built a window at a time, each window saying what's clickable and where in its 320 × 240 frame, and the core taking the click as what it chooses: a row, a member, a list, a tab. Until a window has them, a left click there does nothing.
- The bindings' tests check the controller's defaults against the runtime's old bindings, and the keyboard's against this record.
- A window brought to the front by a click doesn't use what he stands by or start an attack: in trek and a battle with mouse look, a click while the mouse isn't captured only captures it.
