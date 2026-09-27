# 0006. Tick model and RNG

Status: Accepted · Sep 27, 2026

## Context

### How the original keeps time

Game time is counted in 60 Hz ticks (`TICKSPERSECOND`, `world.h`). The game does not run one logic step per tick. It runs **once per rendered frame**, with an integer `delta`:

- At the start of each frame, the game drains the controller queue. The number of queued 60 Hz polls becomes `delta`.
- `DeltaCap` (`Controller.h`) clamps `delta` to **1–6**. At the game's usual frame rate, a frame covers several ticks.
- Every game state computes `delta` this way: trek (`gametrek.cpp`), combat (`gameCombat.cpp`), dialogue, pause, and credits.
- Systems consume `delta` in different ways. Timers subtract it (wander nodes: `timer -= delta` in `WanderTick`), `PlayTime` adds it, and animation scales by it (`Borg7_TickAnimation`). Some systems act once per frame, whatever the delta.

So the original is variable-step with integer steps. How a system behaves can depend on frame rate. For example, a per-frame chance roll fires more often at a higher frame rate.

### How the original draws random numbers

`random.cpp` is a 32-bit linear congruential generator:

- `seed = seed × 0x19660D + 0x3C6EF35F` (mod 2³²)
- An integer in `[0, n)` is `(n × next) >> 32`. `Range(a, b)` and `RollD(dice, sides)` are built on it.
- A float in `[0, 1]` is `next × 2⁻³²`, in single precision.

**One global stream (`gGlobals.rngSeed`, the first field of `gGlobals`) serves almost everything.** Its users include combat, loot, weather rolls, and wander paths. It also serves purely cosmetic effects that draw every frame: wind jitter (`world.cpp`), thunder (`sky.cpp`), combat marker placement, and SFX variant choice. Particles and quicksort have their own streams, seeded from the clock.

`appProc_init` seeds the global stream to `0x3DBB6CD` at boot. The seed isn't in the save file. This was checked against the rig: the live seed in all eight checkpoint states is between 412 and 11,513 generator steps from `0x3DBB6CD`, despite 7–18 hours of play time on the saves. Random values would almost never be that close. So every session starts from the same seed, and the address (`0x800D8088` on 1.0, `0x800E68A8` on debug) is confirmed.

### What that means for parity

Cosmetic effects draw from the gameplay stream every frame, so the stream's position depends on how many frames were rendered. That varies with scene load and emulator timing. Exact stream parity with a live session is impossible in general, and chasing it would couple the core to rendering.

## Decision

### Time

- The core's unit of time is the **60 Hz tick**. The core's main entry point is `Step(ticks)`, and ported code receives `ticks` exactly where the original receives `delta`.
- The runtime drives the core on a **fixed cadence**, decoupled from rendering. Godot renders at any frame rate and interpolates between core states.
- The **cadence is a fidelity setting**. The default steps at the original's typical delta, so per-frame behaviour happens as often as it did on hardware. That delta is measured in the rig first (see below). A smooth profile steps one tick at a time.
- Anything that happens **once per step** rather than per tick gets a comment where it is ported and an entry in a list. These are the systems whose behaviour changes with cadence.

### Randomness

- Port the generator exactly, including the integer-range and float conversions. Confirm the float conversion against the emulator, because the decomp's signedness there is unclear.
- The core takes randomness through an `IRandom` interface. Nothing calls a global generator.
- The reimplementation uses **separate named streams**, all using the original generator: `combat`, `loot`, `world` (weather and camp ambushes), `wander`, and `cosmetic`. Cosmetic effects can no longer shift combat and loot results. Stream states live in the save.
- New games seed the streams from `0x3DBB6CD`, to stay faithful to the original. Loaded games restore the saved stream states.

### Differential tests

Tests compare **formulas, not stream positions**. The emulator captures the exact 32-bit values each rules call consumed, and the test feeds those same values to the core through a scripted `IRandom`:

1. Set a gdb write watchpoint on the seed word (`0x800D8088` or `0x800E68A8`). This catches every advance, including the ones inlined in `GetFloat0To1`.
2. Log the new seed and the caller's return address for each hit, and map the caller to a function with the symbol files.
3. Replay the log. Rules calls consume their values in order, and cosmetic calls are skipped.

## Consequences

- Battles and loot are testable against the emulator even though the live stream order can't be reproduced.
- Combat outcomes stop depending on frame rate and effects. This is a deliberate difference from the original, which a player can't observe.
- The rig needs two follow-ups: measure the distribution of `delta` in trek and in combat (a gdb breakpoint on the frame loop, or a sampled `bst.py` read), and get the gdb watchpoint working with checkpoint states, which is currently blocked by the known state-loading issue in the [rig doc](../reference-rig.md).
- `bst.py` can report "RNG steps since boot" for any state. It's a cheap sanity check for future captures.
