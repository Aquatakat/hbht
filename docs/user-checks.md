# Checks for the user

Things only the user can settle, by playing the original in the emulator, by listening, or in full-game testing. Each says what to do and what to look for, and links to where the port's reading is written up. When one is settled, the finding goes in that doc and the item comes off this list.

## Scripts and characters

- **The lighthouse keeper's key** (Borg13 890 node 13): does the keeper animate as he says "Here... take the key"? The port has him stand still and needs A twice. [scripts](systems/scripts.md#to-check-in-the-emulator)
- **Dougal at the harbour reunion** (script 1040), with Dougal in the party: does the scene end at his line, hang, or play on? [scripts](systems/scripts.md#to-check-in-the-emulator)
- **Sholeh in Ugarit after she joins** (`aidyn:dialogue/0x04AD`), in full-game testing: is she there again once she's joined, and what keeps her away when the party has no free place? The data doesn't show it. [wanderers](systems/wanderers.md#joining)
- **Too many wanderers:** does the original crash or leave some parties out in the 3×3 around Overworld H13, with a new game's flags? It has room for 39 at once, and 44 parties there would get one. [wanderers](systems/wanderers.md)
- **Where the World tab's map north of Ugarit is revealed** in the original. The port doesn't reveal it when travelling through.
- **Item spells' charges:** cast a spell from a sword, armor or a ring (not a wand or scroll) several times in one battle and across battles. Does it ever run out, and does it come back with time? The code spends charges only from wands and scrolls worn as gear, and never recharges any. [magic](systems/magic.md#gear)
- **The Ring of Healing in trek:** wear it on a hurt member and walk about by day. Do their hit points climb about one a frame, many a second, or slowly or not at all? The code heals one each time trek ages the effects, once a drawn frame, and the port does it once a 30 fps frame. [magic](systems/magic.md#not-settled)

## Menus

- **The Party tab's cursor after equipping the last item** of the inventory, when no item moves up into its row: where does it go? It needs a save with one equippable item at the bottom and nothing after. [ui](systems/ui.md#the-party-tabs-actions)

## Combat

- **A reserve joining** (the rats' battle, as a rat dies and a reserve comes in at the round's end): does its small portrait slide over about a second into the dead rat's large frame, and the dead rat's, under its skull, into the small place? And while Rheda aims a spell (C-down, then a target), do the radar's dots of her targets throb and the rest dim? The port reads both from the code. [ui](systems/ui.md#the-battle-screen)

- **The target mark:** in the rats' battle, walk a member up to a rat until it's in reach, then press A to aim. How does the selected rat look: does its body pulse dark and light about once a second, or glow, or flash? The code sends a grey of \|sin\| to the body's light colour (`Body.SetTint`); the port scales its lights by it. [combat](systems/combat.md#an-attack)
- **A rat leaving the field** (the rats' battle, once two rats are dead and the rest start to flee): over its turns, does a fleeing rat run toward the arena's edge a leg a turn, saying "Giant Rat flees" each turn, and then fade out where it stands over about two seconds, its portrait marked fled? The code reads a fade (`Body.FadeOut`, 120 ticks); the port draws one. [combat](systems/combat.md#the-ai)
- **A heal's number** (Rheda's Lay On Hands or any healing over a member's head, once session 9 has them; or a regeneration at a round's end): does the blue number sink, shrink and fade in, as the code reads, or rise as damage's does? [combat](systems/combat.md#an-attack)

## Sound

- **Wrapped pitches:** music 0x05DD and 0x05DE, and sounds such as 0x0759 and 0x075F, have notes above 65,535 Hz that the original keeps in 16 bits, so they'd play far too low. The port does the same. Listen for a low growl or rumble against `aidyn audio`'s render. [audio](formats/audio.md#the-event-stream)
- **The sound volume at half:** by the code, the menu's sounds take the sound volume twice and the containers' three times. At half sound volume, are they a quarter as loud? The port plays them so. [audio](systems/audio.md#volumes)

## Rendering

- **Particles:** a creature that emits while it stands or walks, such as a Fire Elemental, Air Elemental or Lava Hound wandering in trek, or Mago (dialogue entity 0x1413) in a scene. Compare the particles' size, colour, how fast they spawn and drift, and that they fade. The editor shows Oriana's orb with `--record aidyn:actor/0x2BD7 --drive 5`. [particles](formats/particles.md#the-simulation)
- **Texel centres:** the N64 material moves texture coordinates half a texel, as the RDP centres its texels. Do cut-out edges (trees, Oriana's path lights, foliage cards) look as the original's, with nothing shifted? [rendering](systems/rendering.md#the-n64-material)
