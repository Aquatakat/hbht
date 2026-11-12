# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat Turns: Start Early's deaths (Nov 11)

After your Nov 11 play ([combat](systems/combat.md#drawn-from-a-recording)): cast Fireball or Dragon Flames at a pack of rats (F09) under Start Early, and again under Original. Does every rat it kills fall, lie still a second and then fade out? The battle's last enemy stays whole as the screen fades to black, as in 1.0.

### Gwernia castle's farewells (Nov 12)

The castle's A01 holds five scenes no play reaches: Brenna's (`0x04FF`), Arturo's (`0x0501`), Rheda's (`0x0502`), Keelin's (`0x0505`) and the throne's "Farewell." (`0x0508`). Each is enabled only once flag 599 is set, and only the coronation's scene (`0x04FE`) sets 599, on the way that rolls the ending's credits, after which 1.0 goes back to the title ([what's left uncovered](systems/playthrough.md#whats-left-uncovered)). It looks like a walk round the castle after the coronation that was cut. If you remember 1.0 letting you talk to the companions after the coronation, say so, since that would mean the port ends too soon. Otherwise decide whether to leave them unplayed, as 1.0 does, or to play them, for example after the coronation and before the credits, as an option or a mod.

### Fyrsil's training (Nov 11)

Fyrsil, at the desert's Overworld Q27, offers training ("Teach me." in his talk). 1.0's training reads the wrong table for him, so he teaches what STUG02, an Ugarit trainer, does: Merchant up to 7 and the shield up to 10, for gold ([pause menu](systems/pause-menu.md#training)). The port now matches that, where it used to crash. Decide whether to keep it or have him teach something else; either is his `trainsAs` (or a sheet of his own), which a mod can change.

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).
