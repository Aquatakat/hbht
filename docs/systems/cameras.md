# Cameras

`Aidyn.Core.Cameras`. The user's timings of the original are in [playtests](../playtests.md).

## Why the original behaves as it does

These fit `Camera::Mode0` as the decomp has it, and the ROM's machine code agrees. The aim chases Alaron at 1/12 of the gap per 60 Hz tick, which settles in about 55 ticks. Standing still, `Actor::Move` calls `SetFeild70` every frame, so the goal behind him is approached at 1/144 a tick, and the camera at 1/16 of that. The turn is then proportional to the sine of the angle off his back, which stalls head-on and gives the 45:70 split. The absolute time only fits if a frame's `delta` is about 2.6 ticks: both steps loop `delta` times against a goal fixed for the frame, so the turn rate goes with `delta`. That makes the camera frame-rate dependent. It's one more reason to measure the trek `delta` distribution (ADR 0006's follow-up). With R held, `FUN_800b0fac` keeps the camera between 0 and 5 units above the aim at a distance of 5, which is 45° rather than 75°. The user agreed to trust the code.

## The follow camera (`Camera::Mode0`)

Ported (`Aidyn.Core.Cameras.FollowCamera`, from `Camera::Mode0` and the R orbit in `FUN_800b0fac`, checked against the ROM). The analysis above holds. At the 30 fps calibration, from 5° off head-on it takes 61 s to reach 90° and 80 s more to settle within 2°. The user's run, at about 2.6 ticks a frame, took 45 s and 70 s.

- The orbit tops out at 45°, as the code says. The user's estimate of 75° was off.
- `Mode0` doesn't avoid walls at all. Only the R orbit pulls in. It moves an eighth of the way a frame toward a point 0.25 *past* the wall (the ROM has the −0.25), while still easing out toward 5. The two settle about 3.1 from the player, so with a wall any closer, the camera ends up behind it.
- Interiors use `Mode1`, which does push the camera off walls. It isn't ported yet.
- Loading and dialogue set a quick swing (`unk80`), but the first tick after a load cancels it, because the anchor starts at the origin.
