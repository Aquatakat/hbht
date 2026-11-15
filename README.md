# Hoo Boy, Here's Trouble

A PC port of the engine that runs Aidyn Chronicles: The First Mage for the
Nintendo 64, written in C# and Godot.

The goal of the project is a largely console-accurate version of the game, with
a set of optional enhancements, and a comprehensive mod editor, similar to
something like OpenMW-CS.

No game content is included. You must bring your own copy of the US 1.0 ROM.

This project is primarily written by the Claude 5.5 models, directed and
playtested by me. This readme is human written. Not affiliated with H2O
Entertainment or THQ, Aidyn Chronicles is their trademark.

## Getting Started

1. Download the latest release
2. Run `hbht` or `hbht.exe`
3. On first run, select your ROM, and decide whether or not you want to fix the
   mushroom (you know the one)
4. Play

## Accuracy

The primary goal is a reasonable representation of the game, down to some of its
weirder quirks, but this is a game limited by its hardware and rushed out the
door, so some bugs and limitations are corrected at the expense of perfect N64
accuracy.

Examples of some fixed bugs:
- Precipitation splashes on visible geometry, rather than on collision boxes.
- Weather continues from trek mode into cut-scenes rather than reverting to clear
  and restarting.
- The time of day and compass are meant to be hidden inside some interiors. The
  original shows them when you load a save inside, HBHT hides them.
- Flasks show the correct amount of damage dealt.
- Damage spells work when the caster is holding a flask.
- Some larger enemy spellcasters (Hobgoblins) cast their spells in situations
  where the original would cause them to freeze depending on the position of
  their targets.

The type of things not fixed:
- Debuffing an enemy's stats makes them immobile.
- The Ring of Healing restoring 1 HP per frame outside of combat. No other
  lingering spell effect works this way - the code path used for this seems
  meant to be used solely for combat rounds. It works like the original, for
  now, but if anyone has any input on this I'd appreciate it.

Differences from the original:
- Weather, animated textures, and camera movement are tied to frame rate in the
  original. HBHT moves them at a fixed rate at speeds meant to maintain their
  look and feel.
- Some combat AI movement that may fail out and skip their turn in the original
  due to low frame rate may succeed in HBHT. This is fairly rare, and doesn't
  have a large impact on the feel of combat. The AI is still janky, gets stuck,
  skips their turns, etc.
- N64 games use a unique form of texture filtering that samples three points
  rather than four. It causes smooth 45 degree slants top left to bottom right.
  I didn't mimic this because Aidyn's artwork doesn't seem to take advantage of
  it artistically. Cut out edges like trees look a little rounder.

## Enhancements

- High resolution, including rendering higher than screen resolution
- High frame rate, no upper limit
- Extended draw distance
- Higher rain and snow density
- Three size settings for the HUD and menus
- [Enhanced camera movement](https://www.youtube.com/watch?v=LUJc9yhlceA),
  including [a new camera for non-cut-scene dialogue](https://www.youtube.com/watch?v=-li307gXxTM)
- Faster movement speed
- Smoother movement
- [Faster combat](https://www.youtube.com/watch?v=W-fbM2U1Sy8): the next turn
  starts as soon as it can, allowing multiple enemies to move at the same time
- Status auras appear more often in combat
- Rebindable controls

Each of these is optional, except for the frame rate. You can limit it to 30,
but there's no going back to the 15-20 from the console.

These enhancements are planned:

- An overhaul of battle AI - smarter (or dumber!) decision-making based on
  stats, and sensible pathfinding.
- Replaceable menus at native resolution.

## Status

Trek mode has been tested from a new game through defeating Pochangarat by hand
and all combat encounters and side quests have been completed through automated
testing. The editor hasn't been meaningfully started, though there is an
inspector and a map/dialogue viewer.

| System | Status |
| --- | --- |
| Trek | ✅ Complete, tested in a variety of situations. There may be some movement issues in some areas with the smoother movement enhancement. |
| Quest progression | ✅ A full playthrough revealed no quest progression or flag-related bugs. Automated testing confirmed all flags are reachable (chests, combat encounters, cut-scenes, side quests). Some content-related bugs are left unfixed, leaving them for the mod editor (talking to Keelin in Port Saiid with Dougal in your party softlocks the original, HBHT ends the conversation). |
| Cut-scenes | ✅ All story cut-scenes play through with no game-breaking issues. Some edge cases involving differences between HBHT and the original cause some cut-scenes to play out better (Niesen no longer faces the wrong way during the Fire Lord cut-scene) and some to play out worse (Shamsuk sometimes stands half a unit off in the cut-scene prior to his fight). |
| Combat | ✅ Combat has been compared against the ROM in a variety of scenarios, and simulations were conducted to confirm the damage and stat formulas match exactly. Automated testing has completed every combat encounter that sets a defeated flag (the vast majority). |
| Menu | ✅ Complete |
| Editor | ⏳ An inspector is available for in-game maps, characters, scenes, dialogue. Editing hasn't been started yet. An audit of hardcoded content is required, followed by a finalization of the plugin format. |

## Why C# and Godot

This project is not a decomp, it's a reimplementation of the engine in a
different language. For the most part, all of the game's systems are C#, with
Godot being a thin display layer on top.

What this gets us:
- C# is a pretty easy language that imposes a lot of structure, which makes it
  simpler for humans and AI to reason about.
- Adding our own features and changing the engine becomes simpler.
- The engine is much more stable. A decomp might immediately fix the most common
  crashes in the original just by PCs having more memory, but HBHT fixes the
  weird ones that were noticed while rebuilding each system one by one. Camping
  in the corner of the desert, talking to Keelin in Terminor, etc.

What we lose:
- Accuracy doesn't come for free, a significant amount of testing is required
  to match the original, and it's an endless process. The game is huge, and
  while I'm confident the game can be enjoyed end to end right now, there's
  always going to be some little thing I missed.
- Performance is worse. On a regular PC with a graphics card, you'll get
  hundreds of frames per second, no problem. On integrated graphics, it may
  struggle to maintain 60 FPS.

## Credits

### [AidynEditor](https://github.com/TiberGuy/AidynEditor) (MIT licensed)

Used to cross-check the database's fields, thank you.

### [Aidyn Chronicles Decompilation](https://github.com/blackgamma7/Aidyn)

At the beginning of the project, the Aidyn decomp was used as a springboard
to see how viable this project even was. We copied no code from it, but mirrored
some of its file/class structure, and used it as a map to the machine code in
the ROM before checking its assertions against the machine code. I'd like to
thank blackgamma7 for their work in reverse-engineering the game.

I failed to do my due diligence about whether or not its author would object to
an AI-written project using their work this way. On October 4, 2026, I read the
author's public comments about AI-written projects and decided to
[halt all reading of their code](docs/adr/0015-the-port-no-longer-reads-the-decomp.md)
and stop using their symbol table for future commits.

Unfortunately, for past commits, we've already opened the box and peeked inside.
I've decided to not reimplement everything we had done so far, especially
because the code as-written is mostly derived from reading the machine code.
Instead, I'd like to extend this public apology, and just add that this project
exists for the love of this very weird, janky, ambitious game - I don't mean to
take away from the decomp's work, or the art of decompilation in general.

## Also Check Out

### [Aidyn ReChronicled](https://github.com/spitzdano/AidynReChronicled)

A static recomp of the game by Spitzdano. It takes the original game and adds
modern conveniences on top as well as upscaled artwork.

## License

This software is [MIT licensed](LICENSE). This license covers this repository's
code and docs. Any imported game content remains the property of its rights
holders.
