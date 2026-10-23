# Checks for the user

Things only the user can settle, by playing the original in the emulator, by listening, or in full-game testing. Each says what to do and what to look for, and links to where the port's reading is written up. When one is settled, the finding goes in that doc and the item comes off this list.

## Scripts and characters

- **The lighthouse keeper's key** (Borg13 890 node 13): does the keeper animate as he says "Here... take the key"? The port has him stand still and needs A twice. [scripts](systems/scripts.md#to-check-in-the-emulator)
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: does the scene end at his line, hang, or play on? [scripts](systems/scripts.md#to-check-in-the-emulator)
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: is she there again once she's joined, and what keeps her away when the party has no free place? The data doesn't show it. [wanderers](systems/wanderers.md#joining)
- **Too many wanderers:** does the original crash or leave some parties out in the 3×3 around Overworld H13, with a new game's flags? It has room for 39 at once, and 44 parties there would get one. [wanderers](systems/wanderers.md)
- **Item spells' charges:** cast a spell from a sword, armor or a ring (not a wand or scroll) several times in one battle and across battles. Does it ever run out, and does it come back with time? The code spends charges only from wands and scrolls worn as gear, and never recharges any. [magic](systems/magic.md#gear)
- **The Ring of Healing in trek:** wear it on a hurt member and walk about by day. Do their hit points climb about one a frame, many a second, or slowly or not at all? The code heals one each time trek ages the effects, once a drawn frame, and the port does it once a 30 fps frame. [magic](systems/magic.md#not-settled)

## Menus

- **A new game from the load menu** (the runtime, with a save from `aidyn save new` or the editor's new game): load it from the System tab's Load. Does it play the opening cinematic, then the opening's script on its stage, then trek in Overworld I08, as a plain launch does? [saves](systems/saves.md#the-format)

## Combat

- **Darkness or Light in a battle**, and **Wall of Bones, Frozen Doom or Web of Starlight** (any save with a member who knows them): does the sky swing to night or noon over a second and back as the effect ends, and does a held enemy stop its idle animation? Both are read from the code. [combat](systems/combat.md#what-spells-look-like)
- **A camp ambush on terrain 27** (the Overworld's N25, O24–O26, P24 and P25, east of the desert, with two of Abrecan, Godric and Rheda joined so that 5004 holds): camp there until it's ambushed. The original reads no enemy for that terrain, so the battle has none. Does a battle start and end at once as a win (with the post-battle window), does it never start, or does the game crash? Until it's settled, the port says the party is ambushed and trek comes back with no battle. [camping](systems/camping.md#the-ambushs-enemies)
- **The post-battle window's member:** after a battle in which you paused on someone other than Alaron's turn (Start on Brenna's turn, then back), does the post-battle window open on the member whose turn you paused on, rather than Alaron? The code's every write to the member says so, and the port does the same. [ui](systems/ui.md#after-a-battle)
- **The Shadow's battle** (a save before the merge, flag 566 clear, at either meeting): with the Horn, does the Shadow walk straight at Alaron and, reaching him, cut to Shadow Self Merge, with no post-battle window, and trek back where the battle began? Without it (drop the Horn first), does each round's end hurt whoever is near it, (10 − distance)² each, for four rounds, and then "The Shadow dissipates" hold about a second before the fade? Is anyone within 10 of it held to a field of 1, with "caught in the vortex of the Shadow"? A checkpoint inside either battle would let the port be replayed against it. [combat](systems/combat.md#the-special-battles)
- **The goblins' darts** (the Goblin Ambush, a new game): does the dart leave the goblin's hand about two fifths into its throw, and does Alaron react as it lands? [combat](systems/combat.md#an-attack)
- **A flask's blast on its target** (1.0 and 1.1, the user's comparison): throw a Fire Flask at a rat. 1.0's code takes the blast's damage from the target twice (in the blast, then as the blow lands), so a rat of 31 hit by 10 is left with 11. Does it, and does 1.1 do the same? The port keeps 1.0's. [combat](systems/combat.md#spells-potions-and-skills-in-a-turn)
- **The Troubadour's notes** (any member with Troubadour, Perform from C-down): 1.0's code puts a card of scrolling notes 1.64 across, from a unit to about 2.6 above the head's locator, the same size on every body, scrolling about 15 texels a second. Do its size, height and speed match? [combat](systems/combat.md#what-the-bodies-hold)

## Sound

- **Wrapped pitches:** music 0x05DD and 0x05DE, and sounds such as 0x0759 and 0x075F, have notes above 65,535 Hz that the original keeps in 16 bits, so they'd play far too low. The port does the same. Listen for a low growl or rumble against `aidyn audio`'s render. [audio](formats/audio.md#the-event-stream)
- **The sound volume at half:** by the code, the menu's sounds take the sound volume twice and the containers' three times. At half sound volume, are they a quarter as loud? The port plays them so. [audio](systems/audio.md#volumes)
- **The post-battle windows in the runtime**, after a battle won in the forest that gains Alaron a level (as in the prologue): are the training and loot windows silent but for the menu's own sounds, with no ambient sound or rain under them, and does the level-up jingle (0x05E8) play about a second after the scroll starts unrolling, as in the original? [ui](systems/ui.md#after-a-battle)

## Rendering

- **Particles:** a creature that emits while it stands or walks, such as a Fire Elemental, Air Elemental or Lava Hound wandering in trek, or Mago (dialogue entity 0x1413) in a scene. Compare the particles' size, colour, how fast they spawn and drift, and that they fade. The editor shows Oriana's orb with `--record aidyn:actor/0x2BD7 --drive 5`. [particles](formats/particles.md#the-simulation)
- **Bodies lit on every other vertex:** 1.0 writes the engine's lights into only one of the two copies F3DEX2 reads, so the sun, the moon and point lights reach only every other vertex of a body, the rest taking the ambient alone (importer 59). Alaron in daylight should now look as dark and as mottled as in the emulator, and a marked rat's dark side only faintly green. Is either brighter or darker than the original's? [rendering](systems/rendering.md#bodies-light)
- **A trap's explosion:** fail to disarm a trapped chest. The port draws a fire that burns about ten seconds, three small fires thrown about 1.5 away, and a burst of faint grey debris that bounces. Does it look the same, the debris as faint? [map-objects](systems/map-objects.md)
