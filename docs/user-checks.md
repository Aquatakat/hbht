# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).

### Alaron's walk in Becan's cut-scene (movement)

Script 0x0432, Becan's, starts from the dialogue object `aidyn:voxel/0x0F0B/1` in the Misc interiors' C05 and sets flag 328 as it ends. Under the original's movement the port's Alaron walks along a plain wall (surface 2) falling just fast enough (−0.072 a tick, past −0.048) that 1.0's rule for a body of his actor stops his animation moving him, so the walk gives up and puts him at its goal, 1.4 away; under Modern he walks it. Claude can write a save at the object with flag 328 clear. In 1.0, watch Alaron's walk in the cut-scene: does he walk the whole way, or stand and then jump to where he ends up? If he walks, the port's contact with that wall differs from 1.0's, and the port's walk is what to fix ([movement](systems/movement.md#the-cut-scene-comparison)).
