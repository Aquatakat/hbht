# 0009. Code is organised by subsystem, and the Inspector is the one tool

Status: Accepted · Sep 28, 2026 · Keeping raw Borg browsing as a research view is superseded by [0010](0010-editor-reads-plugins.md) · Making `Mode1` a sibling of `FollowCamera` is superseded by [0011](0011-one-follow-camera.md) · Citing the decomp's names in new code is superseded by [0015](0015-the-port-no-longer-reads-the-decomp.md)

## Context

The project is written across many sessions, and none of them sees all of it at once. Where code goes and what it's called has to be predictable, or the same idea gets built twice under two names.

Two things prompted this record:

- **The first core code was named after a game mode.** The player's movement, the sphere physics and the outdoor camera went into `HBHT.Core.Trek`. The original shares all of them with combat. `gameCombat.cpp` starts the same zone engine (`InitZoneEngine(GameMode_Combat)`) and steps it each frame, `combat/control.cpp` moves the active combatant with `Actor::Move`, and `CombatCamera.cpp` drives the same `Camera` class and collision test. `gametrek.cpp` and `gameCombat.cpp` are sibling game states over shared systems, and neither extends the other.
- **There are two sets of viewers.** The three.js pages in `tools/` (scene, map and quest atlas) came first, and do some things the Inspector doesn't yet, such as driving an actor, showing a whole map set and drawing the flag graph. Each session tends to extend whichever one it opens.

## Decision

### The core is organised by subsystem

- `HBHT.Core` namespaces name **subsystems**: `Physics` (collision and the sphere physics), `Movement` (moving bodies through the world), `Cameras`, and later `Flags`, `Scripts` (the Borg13 VM), `Characters`, `Items`, `Combat` rules and so on. What the whole core shares, such as the tick `Cadence`, sits at the root.
- **Game modes are thin loops that compose subsystems**, as the original's game states do. Trek and combat get their own namespaces only for what belongs to that mode alone: in trek, wandering, stealth and starting encounters; in combat, turn order, targeting and AI. Neither mode inherits from the other.
- A class that ports one of the original's variants is named after what it does, not the mode that happens to use it first. `FollowCamera` is `Camera::Mode0`, and `Mode1`, which interiors and combat use, will be its sibling.
- **Terms follow the glossary in CLAUDE.md.** Where the decomp's name for something clashes with ours, such as `Actor` or `World`, the code uses ours and cites the decomp's in a `<c>` tag in the doc comment, as it already does for the functions it ports.

### The Inspector is the one tool, and becomes the editor

- **The Inspector grows into the editor, in the manner of Bethesda's Creation Kit.** It opens the imported plugins, the same data the runtime loads, and edits them. Edits save as a plugin's field-level patches ([0005](0005-plugin-format.md)) over `aidyn.base`, and never into `aidyn.base` itself, which only the importer writes ([0001](0001-importer-owns-the-rom.md)). Browsing raw Borgs from the ROM stays as a research view while there's still decoding to do.
- **The HTML viewers get no new features.** They get fixes only, until the Inspector does what they do. Then they're deleted. New ways to view or check data go into the Inspector.

## Consequences

- `HBHT.Core.Trek` is split into `HBHT.Core.Physics`, `HBHT.Core.Movement`, `HBHT.Core.Cameras` and the root `Cadence`. `TrekCamera` becomes `FollowCamera`, and `TrekInput` becomes `PlayerInput`.
- A session adding a system looks for its subsystem first, and adds one only if none fits.
- The HTML viewers have features the Inspector lacks, so reaching parity is a to-do in CLAUDE.md's status.
- The Inspector needs a plugin-reading path before it can edit anything. That work comes with Phase 5's editor, or earlier if a track needs to look at imported records.
