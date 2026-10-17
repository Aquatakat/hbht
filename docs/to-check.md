# For the user to check

Things only the user can settle, in the emulator or by reading the ROM. Each links to where the port's reading is written up. When one is settled, write what was found in that doc and take it off this list.

## In the emulator

- **The lighthouse keeper's key** (Borg13 890 node 13): whether the keeper animates as he hands it over. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: whether the scene ends at his line, hangs or plays on. See [scripts](systems/scripts.md#to-check-in-the-emulator).
- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row, when a save allows it. See [ui](systems/ui.md#the-party-tabs-actions).
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: whether she's there again once she's joined, and what keeps her away when the party has no free place, which the data doesn't show. See [wanderers](systems/wanderers.md#joining).

- **Wrapped pitches in music:** 0x05DD and 0x05DE (and SFX such as 0x0759 and 0x075F) have notes whose pitch, above 65,535 Hz, the original keeps in 16 bits, so they'd play far too low. The port does the same. Listen for a low growl or rumble in those against `aidyn audio`'s render. See [audio](formats/audio.md#the-event-stream).
- **The sound volume at half:** the menu's sounds take the sound volume twice and the containers' three times, by the machine code. Whether half sound volume makes them quarter loud is a check against the port, which now plays them so. See [audio](systems/audio.md#volumes).
- **Which way sounds pan:** a sound to the camera's right should come from the right speaker; the formula says right is positive, and the port plays it so, but nobody has listened. Any map sound with falloff, such as a fountain, turned side on. See [audio](systems/audio.md#sound-map-objects).

- **Point lights on Alaron:** standing near a fire indoors, such as the one in the Gwernia interiors' A01 (the port's `--set 5 --chunk A01 --at 11,10.5`), he should be lit warm on the side facing it and flicker with it, more the nearer he is, out to 5, while the townsman standing beside him isn't. Then the 24 lights in EhudUnder (C11–C19, E13, E15, G11, G17) that shine from the camera's eye: bodies there should be lit purple, and gold or red, on the side facing the camera, whichever way it looks. See [rendering](systems/rendering.md#point-lights).
- **Particles beside the original's:** a creature that emits while it stands or walks, such as a Fire Elemental, Air Elemental or Lava Hound wandering in trek, or Mago (dialogue entity 0x1413) in a scene: the particles' size, colour, how fast they spawn and drift, and that they fade as the port's do. The editor shows Oriana's orb with `--record aidyn:actor/0x2BD7 --drive 5`. See [particles](formats/particles.md#the-simulation).

- **The opening's door hinges and Alaron's spell**, against the user's captures `roms/theatre-gs1.png`–`gs11.png`: the hinges now look from the shot's camera, so they're shaded from the first picture and the shading turns as the camera and the doors move; the spell in shot 2 is a red disc face-on to the camera, a ring round Alaron's hands and then a wall sweeping the room. Whether both now look as the original's. See [cinematics](systems/cinematics.md#drawing).

- **Where the map area north of Ugarit unlocks:** It currently doesn't unlock while travelling through it in the port.
