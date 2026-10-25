# 0003. Game rules live in a headless C# library

Status: Accepted · Sep 27, 2026 · Porting close to the decomp's structure is superseded by [0015](0015-the-port-no-longer-reads-the-decomp.md), and the fidelity toggle by [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md)

## Context

Combat formulas, AI, flags and dialogue scripts are the parts that must match the original. They are also the hardest to check by eye. Testing them needs fast, repeatable runs with no rendering. Eventually they get compared against emulator captures ([0006](0006-tick-model-and-rng.md)).

## Decision

All game rules live in `HBHT.Core`, a plain .NET library with no Godot reference. It covers the event-flag network, the Borg13 script VM, character stats, inventory, shops, combat, AI, time and camping, and the RNG.

- **State is plain data.** The whole game state is a serialisable object graph. A save file is a snapshot of it.
- **Presentation goes through a host interface.** The core never draws, plays sound or reads input. Camera cues, actor cues, sounds, fades and UI prompts go out as calls on an `IHost` interface. Player choices come back in as commands. Godot implements the host, and tests implement a recording host.
- **No hidden inputs.** The core never reads the wall clock, Godot's RNG or any global state. Time advances only through explicit ticks, and randomness comes only from injected streams ([0006](0006-tick-model-and-rng.md)).
- **Match the original's numeric types.** Where the original uses 32-bit floats or narrow integers, the core does too, including overflow and truncation behaviour. Where an original bug is kept, it sits behind a fidelity toggle.
- **The boundary is enforced by the build.** `HBHT.Core` references only `HBHT.Plugins` and the base class library.

## Consequences

- Rules get unit tests from the start, and differential tests later (roadmap 2.9).
- Porting stays close to the decomp's structure, which keeps the two easy to compare. The core is written from an understanding of the decomp, not copied from it. The decomp has no licence.
- The host interface has to be designed with care. Borg13 mixes rules effects (set a flag, add an item) with presentation cues (pan the camera, walk an actor) in one script. The VM runs both, and it waits on the host where the original waits on an animation.
- Godot-side code stays thin. If Godot code needs a rules answer, that's a sign the logic belongs in the core.
