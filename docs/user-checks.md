# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### The lit lighthouse

In 1.0 and the port, after the scroll's cut-scene in the lighthouse's lantern room (Lighthouse A05): compare the room, lit, side by side. The lit scene bakes the room's colours far brighter and is the one chunk scene with lights of its own, a pale blue directional and a white ambient, which the port leaves out as it does for all scenery; a bluer, brighter room in 1.0 than in the port says 1.0 lights it by them ([rendering](systems/rendering.md#a-scenes-own-lights)). Outside, on the Overworld's A15, the lit scene differs from the dark only by one small quad high on the tower, so there's little to see there ([maps](formats/maps.md)). The saves, from story save D, are in 1.0's pak and `out/user-checks/lighthouse/` for the port: A, the lantern room dark with the scroll, before its cut-scene (`room-dark.json`); B, the room lit (`room-lit.json`); C and D, outside the door, dark and lit (`outside-dark.json`, `outside-lit.json`).

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).
