# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### The fog's edge under Draw Distance's Extended

The fog now comes in to the edge of the chunks drawn and moves back as a new row comes in ([rendering](systems/rendering.md#draw-distance)). Under Extended, walk and run across a few chunks on the mainland, and along the coast where the sea ends short of the fog, as from S18 looking east: no row of chunks should pop up in the middle distance, and the sea's end should fade into the sky. See whether the fog moving back after a crossing (twice his walk over his own speed) looks right, and whether the fog ever comes in so near that it looks worse than before, as at a corner of the land or after a teleport. The user found the south of Terminor right on Nov 5, 2026; since, the edge is measured from Alaron rather than the camera, so turning the camera shouldn't change the fog on distant hills, and coming back from a battle the fog should move out smoothly over a second or so rather than in steps. At J16 by the water, looking south toward the bridge, the fog drew a curtain down the bridge's rocks that swept across them as Alaron walked; the edge is now rounded over about 11° either way, so it should come and go gradually.

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

- **A save of yours from before the overhaul** ([saves](systems/saves.md#older-formats)): run `hbht import` first (the base plugin is now at importer 80), then load a save the game wrote before it, from the System tab or with `--save`. It should load with no message, at the place it was made, the party wearing its weapons, armor and shields, the inventory and the World tab's minimap pieces as they were. Save it and load the new save: the same again. `hbht save show <save>` on the old one says `Format 1, read as 2` and should list nothing left out.

### The sea's edges meet, and the islands are whole (importer 80)

Run `hbht import` first. The skirts under the squares' and fills' edges are gone; instead a square's edge takes the points and heights of the water beside it, and a fill's sea a real chunk's ([rendering](systems/rendering.md#draw-distance)). Go close to where a square meets the shallows' see-through water (G18, off the south coast) and look for a wall under the water or a line of sky at the edge; then out from K20 toward J20, and round Terminor from G19, G20 and H21, where H20's own water stands 0.77 above the squares and they now rise to meet it. The user found the east shore right on Nov 5 with importer 76. Importer 77 leaves Namer's Isle and Chaos Isle uncut, as 1.0 draws them: look round both from on them for the dark triangles, which should be gone. With importer 77 the user still found a seam on Namer's Isle, the line where A22's and B22's sea sheets meet, running north from the island; importer 78 welds it, and Chaos Isle's where E22 meets D22. The line that was left, the user found in the editor, was the sky under the shallows' edge, half a cell out round Namer's Isle, where the seabed ends 0.05 to 0.2 under the line the shallows turn to open sea on and nothing lay under it. Importer 79 hid it with a floor of sea under the open sea; importer 80 closes it instead, raising the seabed's rim onto that line, and the floor goes. Look out from Namer's Isle, from on it, across the shallows' edge, and in the editor from low over the water on both sides of it: no line of sky, and no trace of one. Terminor's cliffs are done: the user found the sky showing past G19's sea, where 1.0 draws nothing either, and left it.
