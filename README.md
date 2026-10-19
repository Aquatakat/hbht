# Hoo Boy, Here's Trouble

A PC reimplementation of Aidyn Chronicles: The First Mage for the Nintendo 64,
written in C# and Godot. No game content is included. You must bring your own
ROM.

The goal of the project is a largely console-accurate version of the game, with
a set of optional enhancements, and a comprehensive mod editor, similar to
something like OpenMW-CS.

This project is primarily written by the Claude 5.5 models. This readme is human
written.

## Accuracy

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

Some fixed bugs:
- Precipitation splashes on visible geometry, rather than on collision boxes.
- Weather continues from trek mode into cutscenes rather than reverting to clear
  and restarting.
- Some interiors hide the time of day and compass when entering through a door.
  The original shows them when you load a save inside, HBHT hides them.
- In battle, the last flask of a stack flies and bursts like the others. The
  original empties the hand as it's thrown, so it was used up for nothing.
- In battle, a weapon's line of sight runs from the attacker's head to the
  target's. The original adds the target's height to the attacker's end too and
  aims at the target's middle, so a body just in front of a target could block
  a shot over it.
- In the Shadow's battle, the Healer's skills say "The Shadow's presence
  prevents all healing." The original showed it and overwrote it at once with
  a plain failure, so the reason never showed.

The type of things not fixed:
- Rain and splash density stays pretty low by default.
- Debuffing an enemy's stats makes them immobile.

## Credits

### [AidynEditor](https://github.com/TiberGuy/AidynEditor) (MIT licensed)

Used to cross-check the database's fields, thank you.

### [Aidyn Chronicles Decompilation](https://github.com/blackgamma7/Aidyn)

At the beginning of the project, the Aidyn decomp was used as a springboard
to see how viable this project even was. We copied no code from it, but used
it as a map to the machine code in the ROM, and used its general code structure,
before checking its assertions against the machine code. I'd like to thank
blackgamma7 for their work in reverse-engineering the game.

I failed to do my due diligence about whether or not its author would object to
an AI-written project using their code this way. On October 4, 2026, I read the
author's public comments about AI-written projects and have decided to
[halt all reading of their code](docs/adr/0015-the-port-no-longer-reads-the-decomp.md)
and stop using their symbol table for future commits.

Unfortunately, for past commits, we've already opened the box and peeked inside.
I've decided to not reimplement everything we've done so far, especially because
the code as-written is mostly derived from reading the machine code. Instead,
I'd like to extend this public apology, and just add that this project exists
for the love of this very weird, janky, ambitious game - I don't mean to take
away from the decomp's work, or the art of decompilation in general.
