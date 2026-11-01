# Checks for the user

What only the user can settle: by playing the original in the emulator, by listening, or in full-game testing. Each item says what to do and what to look for, and links to the doc that holds the port's reading. When one is settled, the finding goes in that doc and the item comes off this list.

A check far into the story needn't be played to: Claude writes a save at the place, flags and party it needs into the Controller Pak beside 1.0's ROM, and says which slot holds it ([reference rig](reference-rig.md#setting-up-a-check)).

## Open

### Combat parity (2.9)

Battles the user plays in 1.0 from written saves, logged with `tools/parity.ps1` and replayed into the port ([combat parity](systems/combat-parity.md)). Setups 1–13 are played. Not in a log yet, each wanting a setup written for it:

- the Goblin Ambush's battle and the Shadow's;
- an enemy whose flight is blocked staying on the field and acting ([combat AI](systems/combat-ai.md#not-settled));
- Sense Aura cast at level 9.

If a hold (Web of Starlight, Wall of Bones, Frozen Doom) locks 1.0 up again, save a state in ares before closing it: the RAM at the lock settles its cause ([combat](systems/combat-actions.md#where-the-port-differs)).

### The follow camera's orbit and walls

Two of the user's timings of 1.0's camera disagree with its code ([cameras](systems/cameras.md#the-follow-camera-cameramode0)):

- **How high R's orbit goes.** The code tops out at 45° once the camera has eased out to its distance, steeper only while it's nearer. The user saw about 75°. Hold R and push the stick up until it stops, standing in the open, then again with a wall just behind the camera.
- **Walls.** The code pulls the camera in only while R orbits it. The user saw the outdoor camera pull in toward Alaron when it meets collision without R. Walk with a wall or a tree behind the camera, not touching R, and note whether it comes in.

### A cut-scene that ends without the minimap growing

In M4's playthrough the user saw a cut-scene end in 1.0 without the minimap growing, where trek starting again grows it after every cut-scene, battle, backdrop and the help menu ([ui](systems/ui.md#the-trek-hud)). The port grows it after every one. Which cut-scene was it? Watching for it again in the prologue is enough.

### The cut-scenes' field of view

The port's cut-scenes show a view 10–13% wider than 1.0's in the emulator. Either ares crops its picture or the camera's field of view differs ([cameras](systems/cameras.md#cut-scenes-cameras)). A screenshot of the same shot in both, at the same window shape, settles which.

### The pause menu's tab after a load (low priority)

Does loading a save reset the tab 1.0's pause menu opens on ([pause menu](systems/pause-menu.md#opening-and-closing))? It only shows when loading from the title, as loading from the menu leaves it on the System tab. Leave the menu on the World tab, lose a battle, load from the title, then open the menu and note which tab it opens on.
