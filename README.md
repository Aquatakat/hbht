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

This project is not a decomp, it's essentially a clean-room reimplementation of
the engine in a different language, with the ROM checked generally to answer
specific questions as to how certain systems function. This gains us a lot of
flexibility and the ability to expand the game in the future, but loses flawless
accuracy and requires significantly more testing.

The primary goal is a reasonable representation of the game, down to some of its
weirder quirks, but this is a game limited by its hardware and rushed out the
door, so some bugs and limitations are corrected at the expense of perfect N64
accuracy.

Timing is a big one. A lot of effects in the game (weather, animated textures,
some RNG) are directly tied to framerate, so the speed could vary. On a modern
computer, this isn't as much of an issue, but in order to get the feel roughly
the same, some reasonable defaults were chosen: weather and animated textures
go as fast as if the original were running at 20 FPS, camera movement is a
little faster at 30 FPS.

Examples of some fixed bugs:
- Precipitation splashes on visible geometry, rather than on collision boxes.
- Weather continues from trek mode into cutscenes rather than reverting to clear
  and restarting.
- The time of day and compass are meant to be hidden inside some interiors. The
  original shows them when you load a save inside, HBHT hides them.

The type of things not fixed:
- Rain and splash density stays pretty low by default.
- Debuffing an enemy's stats makes them immobile.

## Enhancements

- High resolution, including rendering higher than screen resolution
- High frame rate, no upper limit
- Extended draw distance
- Higher rain and snow density
- Three size settings for the HUD and menus
- Faster movement speed
- Status auras appear more often in battle
- Rebindable controls

Each of these is optional, except for the frame rate. You can limit it to 30,
but there's no going back to the 15-20 from the console.

## Status

Trek mode has been tested from a new game through defeating Pochanargarat. More
combat testing is required. The editor hasn't been meaningfully started, though
there is an inspector and a map/dialogue viewer.

| System | Status |
| --- | --- |
| Trek | ✅ Complete, tested in a variety of situations. There's probably some odd edge cases left. |
| Quest progression | ✅ A full playthrough revealed no quest progression or flag-related bugs. The side quests (Gwernia's Inn, Elisheva's Tomb, etc.) have not been tested yet. Some content-related bugs are left unfixed, leaving them for the mod editor (talking to Keelin in Terminor from certain angles loads the wrong set of camera positions). |
| Cut-scenes | ⚠️ All story cut-scenes play through with no game-breaking issues. Some edge cases involving differences between HBHT and the original cause some cut-scenes to play out better (Niesen no longer faces the wrong way during the Fire Lord cutscene) and some to play out worse (Shamsuk sometimes doesn't stand in the correct spot in the scene prior to his fight). A full test of all cut-scenes is required. |
| Combat | ⚠️ Combat has been fully implemented, but only basic combat has been thoroughly tested (Alaron vs rats and bats in the intro). A full test of all spell types in various scenarios is required. |
| Menu | ✅ Complete |
| Editor | ⏳ An inspector is available for in-game maps, characters, scenes, dialogue. Editing hasn't been started yet. Some systemic changes need to happen first - custom fights like the Goblin ambush, that whole weird thing with Sholeh, and the Shadow should be reimplemented as scripts rather than being hardcoded. |

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

## License

This software is [MIT licensed](LICENSE). This license covers this repository's
code and docs. Any imported game content remains the property of its rights
holders.
