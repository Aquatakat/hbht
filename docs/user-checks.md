# Checks for the user

Things only the user can settle, by playing the original in the emulator, by listening, or in full-game testing. Each says what to do and what to look for, and links to where the port's reading is written up. When one is settled, the finding goes in that doc and the item comes off this list.

## Scripts and characters

- **The lighthouse keeper's key** (Borg13 890 node 13): does the keeper animate as he says "Here... take the key"? The port has him stand still and needs A twice. [scripts](systems/scripts.md#to-check-in-the-emulator)
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: does the scene end at his line, hang, or play on? [scripts](systems/scripts.md#to-check-in-the-emulator)
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: is she there again once she's joined, and what keeps her away when the party has no free place? The data doesn't show it. [wanderers](systems/wanderers.md#joining)
- **Too many wanderers:** does the original crash or leave some parties out in the 3×3 around Overworld H13, with a new game's flags? It has room for 39 at once, and 44 parties there would get one. [wanderers](systems/wanderers.md)
- **Where the World tab's map north of Ugarit is revealed** in the original. The port doesn't reveal it when travelling through.

## Menus

- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row: where does it go? It needs a save with one equippable item at the bottom and nothing after. [ui](systems/ui.md#the-party-tabs-actions)

## Sound

- **Wrapped pitches:** music 0x05DD and 0x05DE, and sounds such as 0x0759 and 0x075F, have notes above 65,535 Hz that the original keeps in 16 bits, so they'd play far too low. The port does the same. Listen for a low growl or rumble against `aidyn audio`'s render. [audio](formats/audio.md#the-event-stream)
- **The sound volume at half:** by the code, the menu's sounds take the sound volume twice and the containers' three times. At half sound volume, are they a quarter as loud? The port plays them so. [audio](systems/audio.md#volumes)
- **Which way sounds pan:** a sound to the camera's right should come from the right speaker. Turn side on to a map sound with falloff, such as a fountain. [audio](systems/audio.md#sound-map-objects)

## Rendering

- **Point lights on Alaron:** standing near a fire indoors, such as the one in the Gwernia interiors' A01 (`--set 5 --chunk A01 --at 11,10.5` in the port), is he lit warm on the side facing it, flickering with it, more the nearer he is, out to 5, while the townsman beside him isn't? Then in EhudUnder (C11–C19, E13, E15, G11, G17), 24 lights shine from the camera's eye: are bodies there lit purple, and gold or red, on the side facing the camera, whichever way it looks? [rendering](systems/rendering.md#point-lights)
- **Particles:** a creature that emits while it stands or walks, such as a Fire Elemental, Air Elemental or Lava Hound wandering in trek, or Mago (dialogue entity 0x1413) in a scene. Compare the particles' size, colour, how fast they spawn and drift, and that they fade. The editor shows Oriana's orb with `--record aidyn:actor/0x2BD7 --drive 5`. [particles](formats/particles.md#the-simulation)
- **Texel centres:** the N64 material moves texture coordinates half a texel, as the RDP centres its texels. Do cut-out edges (trees, Oriana's path lights, foliage cards) look as the original's, with nothing shifted? [rendering](systems/rendering.md#the-n64-material)
