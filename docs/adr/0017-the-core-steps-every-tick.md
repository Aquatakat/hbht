# 0017. The core steps every tick, and per-frame behaviour is matched to a frame each system names

Status: Accepted · Oct 17, 2026 · Supersedes the Time section of [0006](0006-tick-model-and-rng.md)

## Context

[0006](0006-tick-model-and-rng.md) planned for the runtime to drive the core with `Step(ticks)` at a fixed cadence. The cadence was to be a fidelity setting. Its default would step at the original's typical `delta`, measured in the rig first, and a smooth profile would step one tick at a time.

Building M1 and M2 went another way:

- **The runtime steps the core once per 60 Hz tick**, in `_PhysicsProcess`. Godot's physics interpolation, and the particle view's own, smooth what's drawn between ticks, so the display runs at any frame rate.
- **Most of the original doesn't care about the frame.** It loops over a frame's ticks, or scales by `delta`, so stepping one tick at a time gives the same result.
- **The rest acts once a frame**, or loops toward a target that only moves once a frame (`AdjustAim`). That part runs faster at a higher frame rate. For example, the camera turns behind the player about twice as fast per second at 30 fps as at 60. Stepping it every tick would play it as the original at 60 fps, which the console never reached in trek.
- **No one frame rate matches the console everywhere.** It runs at 15–25 fps: about 15 by the fire outside Gwernia and on the beach at L08, faster indoors. The user compared each case with the emulator and chose a frame for it: 30 fps for most, 20 for scrolling textures and rain, and 60 for the pause menu, whose frames are light.
- Nobody has wanted the frame to be a player's choice, so it isn't a setting.

## Decision

- **The core steps one tick at a time.** The runtime calls each system's step once per 60 Hz tick, and so do the tests. Systems that the original advances by `delta`, such as the clock, the clouds, lights and script waits, may take a tick count, and are given 1.
- **The display is smoothed, not the rules.** The frame rate Godot draws at never changes what the core does.
- **Per-frame behaviour is matched to an idealized frame**, a fixed number of ticks. Where the original acts once a frame, or loops toward a target fixed for the frame, the port does what the original does at that frame rate. The frames are named constants in `Aidyn.Core.Cadence`, each with its reason:
  - `FrameTicks = 2` (30 fps) is the default.
  - `TextureFrameTicks = 3` and `PrecipitationFrameTicks = 3` (20 fps) are the console's average, for scrolling textures and for rain and snow spawning.
  - `MenuFrameTicks = 1` (60 fps) is the pause menu's, which matched the original's timing in the emulator.
- **Per-frame code goes through `Cadence`.** It either counts ticks to its frame and acts once, or spreads the frame's work over its ticks with `Cadence.PerTick`, `CompoundPerTick` or `FrameShare`. The system's doc says which frame it uses.
- **A new per-frame system takes `FrameTicks`.** It takes another frame only when the user chooses one after comparing with the emulator. That frame becomes a new constant in `Cadence`, with its reason.
- **The frame is not a setting.** A faster pace than the original's is an enhancement with a setting of its own ([0018](0018-the-port-fixes-bugs-and-sets-enhancements.md)), as `BriskPace` is.

0006's Randomness and Differential tests sections stand.

## Consequences

- The core and its tests run the same steps as the runtime, at any frame rate, and the same inputs always give the same result.
- Behaviour that the original ties to its varying frame rate, such as a per-frame chance roll or a catch-up toward a target, happens at a fixed rate that matches the original at the chosen frame. It doesn't match the original at every moment, and isn't meant to.
- 0006's follow-up to measure `delta` is no longer needed to choose a default. It can still help choose combat's frame, which M3 settles the same way.
- `Step(ticks)` is no longer the core's single entry point. Each system has its own step, and the game mode calls them in the original's order.
