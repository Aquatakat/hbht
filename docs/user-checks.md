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

- **A save of yours from before the overhaul** ([saves](systems/saves.md#older-formats)): run `hbht import` first (the base plugin is now at importer 73), then load a save the game wrote before it, from the System tab or with `--save`. It should load with no message, at the place it was made, the party wearing its weapons, armor and shields, the inventory and the World tab's minimap pieces as they were. Save it and load the new save: the same again. `hbht save show <save>` on the old one says `Format 1, read as 2` and should list nothing left out.

### The open sea as squares (importer 73)

Run `hbht import` first. Each Overworld cell that's open sea is a square of it now, and other chunks' sea is cut at its edge ([rendering](systems/rendering.md#draw-distance)). The user looked over it in the editor on Nov 5, and found it right but for a lighter patch in S20: R20's shallows, whose sandy tint doesn't fade as its neighbours' do. Importer 73 cuts away the part that lay on the open sea; the shallows' tint is 1.0's own, and the user left it for a mod. In the runtime, look again from where the sea was a mess on Nov 4, under both Draw Distances: out from L20, Namer's Isle (B22), Port Saiid (B15) and I19 for seams and two textures; round Terminor from G19 and from its gate, at dawn or dusk too, for the sea's far edge in the fog, which under Extended now fades into the sky behind it rather than into a band of fog colour; and along the south coast from R20, R21 and S16, where the shallows keep the original's pieces.
