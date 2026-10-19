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

- **The target mark:** in the rats' battle, walk a member up to a rat until it's in reach, then press A to aim. How does the selected rat look: does its body pulse dark and light about once a second, still lit by the sun? The code sets the body's ambient light to a grey of \|sin\| (`Body.SetTint`), and the port does the same. [combat](systems/combat.md#an-attack)
- **A spell's look, captured** (the rats' battle from Combat 01): Rheda casts Endurance at herself and Alaron, then Weakness at a rat on a later turn; a short video, or screenshots every half second, saved in `roms/`. The port draws Endurance's shell around Rheda for each cast, the sky dimming to half for about three seconds, Alaron glowing white for his (Rheda's own glow is undone at once, as checkpoint 08's tints show), Weakness's sparks over the rat's head, and later each lasting shell or ring pulsing over its owner's head in turn, nothing for five seconds after a cast. Do sizes, colours and timing match? [combat](systems/combat.md#what-spells-look-like)
- **Darkness or Light in a battle**, and **Wall of Bones, Frozen Doom or Web of Starlight** (any save with a member who knows them): does the sky swing to night or noon over a second and back as the effect ends, and does a held enemy stop its idle animation? Both are read from the code. [combat](systems/combat.md#what-spells-look-like)
- **A rat leaving the field** (the rats' battle, once two rats are dead and the rest start to flee): over its turns, does a fleeing rat run toward the arena's edge a leg a turn, saying "Giant Rat flees" each turn, and then fade out where it stands over about two seconds, its portrait marked fled? The code reads a fade (`Body.FadeOut`, 120 ticks); the port draws one. [combat](systems/combat.md#the-ai)
- **A heal's number** (Rheda's Lay On Hands or any healing over a member's head, once session 9 has them; or a regeneration at a round's end): does the blue number sink, shrink and fade in, as the code reads, or rise as damage's does? [combat](systems/combat.md#an-attack)

- **A camp ambush on terrain 27** (the Overworld's N25, O24–O26, P24 and P25, east of the desert, with two of Abrecan, Godric and Rheda joined so that 5004 holds): camp there until it's ambushed. The original reads no enemy for that terrain. Does a battle start and end at once as a win (with the post-battle window), does it never start, or does the game crash? The port, for now, says the party is ambushed and goes on with no battle. [camping](systems/camping.md#the-ambushs-enemies)
- **The post-battle window's member:** after a battle in which you paused on someone other than Alaron's turn (Start on Brenna's turn, then back), which member does the post-battle window open on: Alaron, or the one whose turn you paused on? The code opens it on the last member a menu showed. The port opens it on the first. [ui](systems/ui.md#after-a-battle)
- **C-down's and C-up's menus, captured** (the rats' battle, on Rheda's or Alaron's turn): hold C-down and take a screenshot while it's open, then the same for C-up, saved in `roms/` as `combat-cdown.png` and `combat-cup.png`. The port draws them from the code (a list 120 wide, centred, the rows' icons and colours, the cursor pulsing), but the rows' heights and the font's spacing need a capture to match, as the battle screen was matched to `combat.png`. [ui](systems/ui.md#the-held-menus)
- **Detect Moon Phase or Detect Sun Phase cast in a battle**: does anything show, does the game go on as normal, or does it crash or misbehave? The code writes to trek's sundial, which combat has freed. The port does nothing. [magic](systems/magic.md#what-each-spell-does)
- **A flask's blast on its target** (1.0 and 1.1, the user's comparison): throw a Fire Flask at a rat. 1.0's code takes the blast's damage from the target twice (in the blast, then as the blow lands), so a rat of 31 hit by 10 is left with 11. Does it, and does 1.1 do the same? The port keeps 1.0's. [combat](systems/combat.md#spells-potions-and-skills-in-a-turn)

## Sound

- **Wrapped pitches:** music 0x05DD and 0x05DE, and sounds such as 0x0759 and 0x075F, have notes above 65,535 Hz that the original keeps in 16 bits, so they'd play far too low. The port does the same. Listen for a low growl or rumble against `aidyn audio`'s render. [audio](formats/audio.md#the-event-stream)
- **The sound volume at half:** by the code, the menu's sounds take the sound volume twice and the containers' three times. At half sound volume, are they a quarter as loud? The port plays them so. [audio](systems/audio.md#volumes)

## Rendering

- **Particles:** a creature that emits while it stands or walks, such as a Fire Elemental, Air Elemental or Lava Hound wandering in trek, or Mago (dialogue entity 0x1413) in a scene. Compare the particles' size, colour, how fast they spawn and drift, and that they fade. The editor shows Oriana's orb with `--record aidyn:actor/0x2BD7 --drive 5`. [particles](formats/particles.md#the-simulation)
- **Texel centres:** the N64 material moves texture coordinates half a texel, as the RDP centres its texels. Do cut-out edges (trees, Oriana's path lights, foliage cards) look as the original's, with nothing shifted? [rendering](systems/rendering.md#the-n64-material)
