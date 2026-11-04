# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).

### Alaron's walk in Becan's cut-scene (movement)

Script 0x0432, Becan's, starts from the dialogue object `aidyn:voxel/0x0F0B/1` in the Misc interiors' C05 and sets flag 328 as it ends. Under the original's movement the port's Alaron walks along a plain wall (surface 2) falling just fast enough (−0.072 a tick, past −0.048) that 1.0's rule for a body of his actor stops his animation moving him, so the walk gives up and puts him at its goal, 1.4 away; under Modern he walks it. Claude can write a save at the object with flag 328 clear. In 1.0, watch Alaron's walk in the cut-scene: does he walk the whole way, or stand and then jump to where he ends up? If he walks, the port's contact with that wall differs from 1.0's, and the port's walk is what to fix ([movement](systems/movement.md#the-cut-scene-comparison)).

### The data format's second slice (importer 68)

- **Cadme's and Xibid's talkers on the Overworld** ([wanderers](systems/wanderers.md#talking)): four talkers named the dialogue entity twin of Cadme (`aidyn:map/0x0F47`, `0x0FDB`) or Xibid (two in `aidyn:map/0x0FC5`), which the import merged into the entity, as the user chose. They now push and are pushed at the entity's radius (Cadme 0.45, Xibid 0.4, where 0.55 was), and notice the party at the entity's perception, 0 where 10 was. Talk to each: the conversation and their look should be as before.
- **Sense Aura, the item details and the spell lists** ([combat](systems/combat.md), [items](systems/items.md)): the element names, armor's Dexterity and Stealth lines and the school icons come from the new records; they should read as before.

### What changed on Nov 3, 2026, by eye

Not yet looked at in the runtime or the editor. Run `hbht import` first: the base plugin is at importer 66.

- **The half-alpha bodies** ([combat](systems/combat.md)): a Spirit Wolf in battle is drawn at half alpha, body and shadow, and fades from there to nothing after its fall, with no jump to opaque. `--battle aidyn:voxel/0x0F9B/5` fights one on the Overworld. The seven Chaos lieutenants (Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz, Assim) are the same.
- **What a cut-scene shows in a hand takes the scenery's tint** ([combat-actions](systems/combat-actions.md#what-the-bodies-hold)): with `--cutscene aidyn:dialogue/0x04F1 --time 22` (or 0x0393, 0x0495, 0x04DB), the item is as dim as the hand holding it, not bright white.
- **Environment maps look from the camera** ([rendering](systems/rendering.md#environment-maps)): the silver dish in Gwernia Castle E01 (`--set 4 --chunk E01 --at 10,10 --camera interior`) has a sheen that slides as the camera turns; the opening cinematic's door hinges look as before.
- **The editor's model view turns billboards to the camera** ([editor](systems/editor.md)): open a tree's scene or Oriana's path light (`aidyn:scene/0x374A`) and orbit it; the cards keep facing you.
- **The editor's map shows opened and destroyed containers under Flags** ([map-objects](systems/map-objects.md)): open `aidyn:map/0x0ECF` with **Flags** on, set flag 2103 on the Flags page and show the map again: container 16's chest shows opened; clear 2103 and set 2104: destroyed; **Flags** off: closed.
- **Alaron's animations for doors and chests** ([map-objects](systems/map-objects.md#the-players-animation-for-a-use)): standing still at any door, press A: he leans and reaches as it fades out, and comes out of the fade standing. At a locked chest (the Overworld's J09 has three) he crouches and reaches once while the lock's popup is up, then stands. A chest that opens the loot window shows almost none of it, as in 1.0.
- **The view past the loaded chunks** ([rendering](systems/rendering.md#draw-distance)): under both Draw Distances, walk the Overworld's coasts and look out to sea and across bays, for holes in the sea, a cell's sea of another shade beside its neighbour's, or land that floats or pops in close. Claude looked from M22 over L23 (the old hole), from L20 across the bay to J19, from I19 toward K20, from Namer's Isle east and from C08 to Erromon's peaks. Under Original, J19 from L20 is J19 itself now, with its scaffolding. From I19 toward K20 under Original, the fogged land at the edge of the view is K20's cliffs cut by the far plane, in steps, where the original shows I19's copy of it smooth; and there's a hairline seam and a pale wedge out there in the fog, which may be where a piece was cut. Under Extended a fogged shelf of land now sits on the horizon from L20 looking north, the chunks the 5 × 5 shows. In the editor, the map shows the Overworld's new chunks of sea.
- **Where the cut changed the view** ([rendering](systems/rendering.md#draw-distance)): the 64 Overworld chunk scenes importer 66 cut, by area. Each drew art two or more cells out, which now draws from the real chunk there or from that cell's fill (cells marked * are chunks the import made). Stand in the first chunks named, under both Draw Distances, and look toward the rest, for art missing, doubled, or arriving late:
  - **Port Saiid's sea** (B15): its sea went into the fills of rows −1 to C, columns 13–17*. Down the coast, C15–I15 each moved a strip into C16*–I16*, and J14, K16, K17 and L15 show I14–L16.
  - **Namer's Isle and Chaos Isle** (B22, C22, E22, F22, G23): their sea went into the fills of rows −1 to I, columns 19–25 (almost all *). Extended is off on the isles, so they keep the 3 × 3 and the chunks they show; from the mainland under Extended (G19–I21) the sea is the new chunks.
  - **H19's sea** to the north-west, F17*–I17* and F18*–H20*: near Terminor's gate at I19; J17 and J18 moved a little into H17*, I17* and H18*.
  - **The bay round L20** (I20, J18–J20, K19, L20, M20, M21, M23, N20, N21): the old L23 hole. M21's sea is in L21*–M24*; J19 and K19 moved theirs into K20*–K22*; J20 into J21*, J22*; I20 into I21*. From L20, M23, N20 and N21, the shows reach J18–L20 across the bay.
  - **Erromon's peaks** (D09, E08): moved into D07 and E07*.
  - **West of Jundar** (E13, F13, F14): into D13*, E12* and F12*.
  - **Gwernia and the prologue's forest** (M07, L08, N07, N08, I08): M07's west edge into M06*, L08's beach into L07*, N07 into O07*, N08 into O09*, O10* and P08*–P10*; I08 shows J09.
  - **The south coast** (S16, T16, T18, S18–S21, P17, N16, O16, U26, U28): S21's sea is in R22*–U23*; S18–S20 moved theirs into T19*–U20*; T16 into T15*, U15*, U16*. The rest are slivers: S15*, P18*, N15*, O15*, U25*, U29*, and the Goblin Camp's B03* from B04.
- **Dialogue entities' footsteps** ([audio](systems/audio.md#bodies-sounds)): Amann's patrol in the Misc interiors' E01, or Damek's at Overworld Q16, is heard stepping. The dialogue entities are entities with no sheet now (ADR 0032), with the same sounds.

