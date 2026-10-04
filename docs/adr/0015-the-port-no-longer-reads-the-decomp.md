# 0015. The port no longer reads the decomp

Status: Accepted · Oct 4, 2026 · Supersedes part of [0003](0003-headless-rules-core.md) (porting close to the decomp's structure) and part of [0009](0009-code-organisation.md) (citing the decomp's names in new code)

## Context

Until now, the project used [blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn) as its map of the original's code. The decomp is Ghidra output cleaned up by hand. It doesn't compile and has no licence. It gave the project four things:

- **Its pseudocode:** where a system lives and roughly what it does. Research subagents read it before disassembling the machine code.
- **Its symbol tables:** a name for each address, in 1.0 and the debug build. `tools/n64dis.py` and the reference rig resolved functions through them.
- **Its headers:** struct layouts and enums.
- **Its names:** cited in `<c>` tags throughout the docs and code.

The port never copied its code, and every fact the port relies on was settled from 1.0's machine code. That's where the decomp was wrong often enough that every task found at least one error. Still, the decomp shaped how each system was found and how the port is structured, and the debt to it is large.

On Sep 22, 2026, the decomp's README gained a note saying no LLMs were used in making it. The author's public comments from around that time make it clear he would not approve of a project written by LLMs, which this one is.

## Decision

- **The project no longer reads the decomp.** No session or subagent reads its pseudocode, headers, symbol tables or history, and no tool loads them. The local clone in `ref/Aidyn` is removed and isn't cloned again.
- **What's already ported stays.** Every fact in the docs and code was settled from 1.0's machine code, data or the emulator, and deriving it again would give the same result. The behaviour of Nintendo's and H2O's game isn't the decomp's.
- **Existing citations stay, as credit.** The `<c>` names already in the code, and the docs' notes of where the decomp reads something differently, record where the work came from. Removing them would erase that credit.
- **New work names the original's code with our own names.** A function, global or struct is cited by its 1.0 address, with a name from our own symbol map. Where the ROM names something itself, as the debug build's strings do (`Entity::GetSpell()`, `./src/gamecombat.cpp`), that name is the original's and is used. New code follows the original's structure as the machine code shows it: the call graph, the debug build's source files.
- **What the project reads now:**
  - The ROMs' machine code and data: 1.0 first, and the debug build for its strings and debug features.
  - The emulator and the user's checkpoints.
  - The project's own docs.
  - [TiberGuy/AidynEditor](https://github.com/TiberGuy/AidynEditor), which is MIT-licensed.
  - Other community work only where its licence allows, after checking how its author feels about this project's use of LLMs, and with credit.
- **The rig grows what the decomp gave:** an index of the code, a symbol map of our own, maps of the structures in RAM, and captures from the emulator. These are roadmap tasks 0.7–0.13.
- **The project's README credits the decomp** for its part in the early research. It says how this project is built, that the decomp's author has no part in it, and that the project no longer reads it.

## Consequences

- Research costs more at first. A subagent starts from the project's docs and symbol map, not a named, readable file, and finds code by its strings, data references and call graph, then by the emulator. The cost falls as the symbol map grows. Each research run adds the names it settles.
- `tools/n64dis.py` resolves symbols from the project's own map, and works by address until the map has the name.
- ADR 0003's "Porting stays close to the decomp's structure" no longer holds for new code. ADR 0009's rule to cite the decomp's name where it clashes with ours applies only to citations already written.
- The roadmap's risks and sources that pointed at the decomp now point at the machine code and the debug build's own file names.
- Combat (2.5) and enemy AI (2.6), the biggest systems left, are the first to be researched this way. Fixing gdb after loading a save state matters more now. A watchpoint on a combatant's hit points finds the damage code directly.
