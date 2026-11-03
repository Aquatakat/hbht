# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).

### Alaron's walk in Becan's cut-scene (movement)

Script 0x0432, Becan's, starts from the dialogue object `aidyn:voxel/0x0F0B/1` in the Misc interiors' C05 and sets flag 328 as it ends. Under the original's movement the port's Alaron walks along a plain wall (surface 2) falling just fast enough (−0.072 a tick, past −0.048) that 1.0's rule for a body of his actor stops his animation moving him, so the walk gives up and puts him at its goal, 1.4 away; under Modern he walks it. Claude can write a save at the object with flag 328 clear. In 1.0, watch Alaron's walk in the cut-scene: does he walk the whole way, or stand and then jump to where he ends up? If he walks, the port's contact with that wall differs from 1.0's, and the port's walk is what to fix ([movement](systems/movement.md#the-cut-scene-comparison)).

### What changed on Nov 3, 2026, by eye

Not yet looked at in the runtime or the editor. Run `hbht import` first: the base plugin is at importer 65.

- **The half-alpha bodies** ([combat](systems/combat.md)): a Spirit Wolf in battle is drawn at half alpha, body and shadow, and fades from there to nothing after its fall, with no jump to opaque. `--battle aidyn:voxel/0x0F9B/5` fights one on the Overworld. The seven Chaos lieutenants (Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz, Assim) are the same.
- **What a cut-scene shows in a hand takes the scenery's tint** ([combat-actions](systems/combat-actions.md#what-the-bodies-hold)): with `--cutscene aidyn:dialogue/0x04F1 --time 22` (or 0x0393, 0x0495, 0x04DB), the item is as dim as the hand holding it, not bright white.
- **Environment maps look from the camera** ([rendering](systems/rendering.md#environment-maps)): the silver dish in Gwernia Castle E01 (`--set 4 --chunk E01 --at 10,10 --camera interior`) has a sheen that slides as the camera turns; the opening cinematic's door hinges look as before.
- **The editor's model view turns billboards to the camera** ([editor](systems/editor.md)): open a tree's scene or Oriana's path light (`aidyn:scene/0x374A`) and orbit it; the cards keep facing you.
- **The editor's map shows opened and destroyed containers under Flags** ([map-objects](systems/map-objects.md)): open `aidyn:map/0x0ECF` with **Flags** on, set flag 2103 on the Flags page and show the map again: container 16's chest shows opened; clear 2103 and set 2104: destroyed; **Flags** off: closed.
- **Dialogue entities' footsteps** ([audio](systems/audio.md#bodies-sounds)): Amann's patrol in the Misc interiors' E01, or Damek's at Overworld Q16, is heard stepping. (How dialogue entities get their sounds may change: folding them into entities is being looked at.)

