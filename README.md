# Hoo Boy, Here's Trouble

A PC reimplementation of Aidyn Chronicles: The First Mage for the Nintendo 64,
written in C# and Godot. No game content is included. You must bring your own
ROM.

The goal of the project is a largely console-accurate version of the game, with
a set of optional enhancements, and a comprehensive mod editor, similar to
something like OpenMW-CS.

This project is made possible by the advances in frontier AI and is primarily
written by the Claude 5.5 models. This readme is human written.

## Accuracy

The primary goal is a reasonable representation of the game, down to some of its
weirder quirks. As we progress through the codebase, some bugs are just bugs
though, and those we can correct.

As an example: Negative stats causing enemies to become immobile is probably not
intended but it's a core part of how battles function, so we'll keep it. The
time of day being visible when you load a save indoors but not when you enter
via a door is just a bug, so we'll change it.

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
