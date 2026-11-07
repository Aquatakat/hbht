# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).

### The data format's fourth slice (importer 70)

- **Keelin's scene from her body** ([scripts](systems/scripts.md#the-world-it-plays-in), [wanderers](systems/wanderers.md#talking)): in Terminor's inn (C05), before she's recruited, talk to Keelin's body rather than using the script object beside her (stand so that A reaches her body: face her from the side away from the object). In 1.0 her scene crashes after "Kisses." as she walks to point 34; started from the object, it plays to its end. The port now starts it from her body as the object does (her npc's `stage`, `aidyn:mapobject/8/C05/12`), the user's fix: it should play as the object's start does in 1.0, Keelin walking off and the scene ending. The other five talkers with a stage (Edur in I08 and four more, listed in the scripts doc) should play from their bodies as from their objects too.

### The data format's fifth slice: saves (format 2)

- **A save of yours from before the overhaul** ([saves](systems/saves.md#older-formats)): run `hbht import` first (the base plugin is now at importer 81), then load a save the game wrote before it, from the System tab or with `--save`. It should load with no message, at the place it was made, the party wearing its weapons, armor and shields, the inventory and the World tab's minimap pieces as they were. Save it and load the new save: the same again. `hbht save show <save>` on the old one says `Format 1, read as 2` and should list nothing left out.
