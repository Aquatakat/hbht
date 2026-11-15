# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)). The saves written before Nov 13, 2026 were lost with ares's pak; a check that names one needs it rewritten from the story pak.

## Open

- **The mouse and keyboard in play** ([ADR 0040](adr/0040-the-mouse-comes-first-on-the-keyboard.md)). Launched runs checked the menus' clicks, hovering, the title, a cut-scene's line and a held menu's click; these need a hand on the mouse, as a launched window has no focus to capture it:
  - In trek: mouse look (with Mouse on the Camera row, which a saved row without it keeps off) and Invert Mouse Y, and that the click bringing the window back to the front doesn't use what he stands by. Invert Camera Y on a controller is checked.
  - In a battle: aiming an attack or a spell, moving the mouse across steps the target and feels paced right (80 pixels a step); a bow's, thrown weapon's or flask's cursor and an area spell's follow the mouse ([combat](systems/combat-actions.md#the-port)); the wheel scrolls a long held menu ([ui](systems/ui.md#the-held-menus)).
  - Hovering a row near the foot of the Controls page holds the view, and the loot window's drop question takes a hover and a click (a launched run took the click).
  - The clicks not yet tried: a shop and the editor's window panels.
