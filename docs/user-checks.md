# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–14 are played, and none is waiting. If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).

### The data format's second slice (importer 68)

- **Cadme's and Xibid's talkers on the Overworld** ([wanderers](systems/wanderers.md#talking)): four talkers named the dialogue entity twin of Cadme (in `aidyn:chunk/0/E08` and `aidyn:chunk/0/I08`) or Xibid (two in `aidyn:chunk/0/H13`), which the import merged into the entity, as the user chose. They now push and are pushed at the entity's radius (Cadme 0.45, Xibid 0.4, where 0.55 was), and notice the party at the entity's perception, 0 where 10 was. Talk to each: the conversation and their look should be as before.
- **Sense Aura, the item details and the spell lists** ([combat](systems/combat.md), [items](systems/items.md)): the element names, armor's Dexterity and Stealth lines and the school icons come from the new records; they should read as before.

### The data format's third slice (importer 69)

Each node now names its speaker, which the importer works out from the links into it ([scripts](systems/scripts.md#speakers)). Two lines are reached with different speakers in force in 1.0 and were given one by hand:

- **Cadme's "Farewell."** (script 1446, `aidyn:script/0x03B9`, node 6): it's now always Alaron's. In 1.0 it shows Alaron's portrait after his "I have no time to tell it now." and Cadme's after her own closing lines ("Go gently." and the like). Talk to her through a few of her topics and end the talk: see whether Alaron's "Farewell." reads right, or whether it should be hers.
- **"Let's see what the old man has."** (script 1470, `aidyn:script/0x03C3`, node 37, a cut-scene at a shop, with Dougal not in the party): now Alaron's, as the camera frames him. In 1.0 its portrait is Rheda's, Brenna's or Alaron's, by who spoke before. Play it with Rheda or Brenna in the party.
- **The cut-scenes' camera at the merge nodes** ([scripts](systems/scripts.md#speakers)): 138 textless nodes are reached with more than one speaker in force, every one holding only camera moves onto marks, so no op's actor depends on the speaker. All 262 cut-scene launches replayed tick for tick as before against the old import, but none has been watched since. Play one or two in the game or the editor, such as 1384 (`aidyn:script/0x0568`) and 1391 (`0x056F`): the camera should move as it did.

### The data format's fourth slice (importer 70)

- **Keelin's scene from her body** ([scripts](systems/scripts.md#the-world-it-plays-in), [wanderers](systems/wanderers.md#talking)): in Terminor's inn (C05), before she's recruited, talk to Keelin's body rather than using the script object beside her (stand so that A reaches her body: face her from the side away from the object). In 1.0 her scene crashes after "Kisses." as she walks to point 34; started from the object, it plays to its end. The port now starts it from her body as the object does (her npc's `stage`, `aidyn:mapobject/8/C05/12`), the user's fix: it should play as the object's start does in 1.0, Keelin walking off and the scene ending. The other five talkers with a stage (Edur in I08 and four more, listed in the scripts doc) should play from their bodies as from their objects too.

### The data format's fifth slice: saves (format 2)

- **A save of yours from before the overhaul** ([saves](systems/saves.md#older-formats)): run `hbht import` first (the base plugin is now at importer 71), then load a save the game wrote before it, from the System tab or with `--save`. It should load with no message, at the place it was made, the party wearing its weapons, armor and shields, the inventory and the World tab's minimap pieces as they were. Save it and load the new save: the same again. `hbht save show <save>` on the old one says `Format 1, read as 2` and should list nothing left out.

### The help and the journal notice on triggers (importer 71)

The first spell's and first potion's help and the new journal entry's notice are the base plugin's triggers now, on `spell.trained`, `item.gained` and `journal.added` ([events](systems/events.md#the-base-plugins)). They should read and come as before, once each:

- **The first potion:** in a new game, take the four Healing Potions from the chest at the start: the potion help shows on the parchment and A closes it; a second potion later shows none.
- **The first spell:** at a trainer who teaches spells, train one a member doesn't know: the spell help shows; raising it, or learning another, shows none.
- **A new journal entry:** "A new journal entry has been added." comes in trek's message box as before, as after the opening's first entry.
- **In the editor:** the record list shows a chunk's Borg9 in its Borg column (`aidyn:chunk/0/I08`, for one).

### What changed on Nov 3, 2026, by eye

Not yet looked at in the runtime or the editor. Run `hbht import` first: the base plugin is now at importer 71.

- **The half-alpha bodies** ([combat](systems/combat.md)): a Spirit Wolf in battle is drawn at half alpha, body and shadow, and fades from there to nothing after its fall, with no jump to opaque. `--battle aidyn:mapobject/0/G13/5` fights one on the Overworld. The seven Chaos lieutenants (Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz, Assim) are the same.
- **What a cut-scene shows in a hand takes the scenery's tint** ([combat-actions](systems/combat-actions.md#what-the-bodies-hold)): with `--cutscene aidyn:script/0x04F1 --time 22` (or 0x0393, 0x0495, 0x04DB), the item is as dim as the hand holding it, not bright white.
- **Environment maps look from the camera** ([rendering](systems/rendering.md#environment-maps)): the silver dish in Gwernia Castle E01 (`--set 4 --chunk E01 --at 10,10 --camera interior`) has a sheen that slides as the camera turns; the opening cinematic's door hinges look as before.
- **The editor's model view turns billboards to the camera** ([editor](systems/editor.md)): open a tree's scene or Oriana's path light (`aidyn:scene/0x374A`) and orbit it; the cards keep facing you.
- **The editor's map shows opened and destroyed containers under Flags** ([map-objects](systems/map-objects.md)): open `aidyn:chunk/0/B06` with **Flags** on, set flag 2103 on the Flags page and show the map again: container 16's chest shows opened; clear 2103 and set 2104: destroyed; **Flags** off: closed.
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

