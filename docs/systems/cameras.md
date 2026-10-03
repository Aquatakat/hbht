# Cameras

`Aidyn.Core.Cameras`. The user's timings of the original are in [playtests](../playtests.md).

## Why the original behaves as it does

These fit `Camera::Mode0` as the decomp has it, and the ROM's machine code agrees. The aim chases Alaron at 1/12 of the gap per 60 Hz tick, which settles in about 55 ticks. Standing still, `Actor::Move` calls `SetFeild70` every frame, so the goal behind him is approached at 1/144 a tick, and the camera at 1/16 of that. The turn is then proportional to the sine of the angle off his back, which stalls head-on and gives the 45:70 split. The absolute time only fits if a frame's `delta` is about 2.6 ticks: both steps loop `delta` times against a goal fixed for the frame, so the turn rate goes with `delta`. That makes the camera frame-rate dependent. It's one more reason to measure the trek `delta` distribution (ADR 0006's follow-up). With R held, `FUN_800b0fac` keeps the camera between 0 and 5 units above the aim at a distance of 5, which is 45° rather than 75°. The user agreed to trust the code.

## The follow camera (`Camera::Mode0`)

Ported (`Aidyn.Core.Cameras.FollowCamera`, from `Camera::Mode0` and the R orbit in `FUN_800b0fac`, checked against the ROM). The analysis above holds. At the 30 fps calibration, from 5° off head-on it takes 61 s to reach 90° and 80 s more to settle within 2°. The user's run, at about 2.6 ticks a frame, took 45 s and 70 s.

- The orbit tops out at 45°, as the code says. The user's estimate of 75° was off.
- `Mode0` doesn't avoid walls at all. Only the R orbit pulls in. It moves an eighth of the way a frame toward a point 0.25 *past* the wall (the ROM has the −0.25), while still easing out toward 5. The two settle about 3.1 from the player, so with a wall any closer, the camera ends up behind it.
- Interiors use `Mode1`, which does keep the camera off walls (below).
- Loading and dialogue set a quick swing (`unk80`), but the first tick after a load cancels it, because the anchor starts at the origin.

## Which mode (M2 session 4c)

The camera's mode is chosen only when the player goes through a teleporter (`TeleportPlayer`), by the chunk he arrives in: `Mode1` where its terrain doesn't draw the sky (the original's sky type 4: terrains 6, 8, 9, 11 and 13–16, the importer's `TerrainRecord.SkyDrawn` false), `Mode0` elsewhere. Walking from chunk to chunk keeps it, so Gwernia castle's courtyard (terrain 25) keeps whatever mode the door into it gave. Loading a save from the game's menu teleports too, so it chooses the same way. Loading from the title screen doesn't: the zone engine starts in `Mode0` and keeps it until the next door, so the user saw the outdoor camera inside a house until a camera object took over (Oct 2, 2026). That's a bug, and the port fixes it, outside the fidelity profile: however the game starts, the chunk chooses. `FollowCamera.Mode` holds it, and `FollowCamera.ModeFor` chooses it.

A teleport places the camera (`FollowCamera.Arrive`): first it moves with the player and stops orbiting (`ApplyZoneVelocity`, `GiveCameraToPlayer`), then it goes to the teleporter's camera point if it names one. Otherwise, in an interior it's 4 *in front of* the player and 3 up, looking back at him: the original subtracts its facing, which points behind him. Outdoors it's behind him, as a map load puts it, and swings round quickly for 5 frames (`unk80`). The user's captures (Oct 2, 2026) match the port's placement at all four doors of Gwernia's three small houses and at the inn, and show the camera in front of Alaron, looking back at him, on entering a castle corridor.

Camera objects work through a teleport's fade as well, as the original's zone engine runs on under it, so the room's camera has the camera as the screen comes up. Without them there, the interiors' camera pulled in toward Alaron for the fade's quarter second, and the room's camera eased back out after.

## The interiors' camera (`Camera::Mode1`)

Ported (`FollowCamera.Interior.cs`), with the arguments `ProcessGameCamera` passes, read from the 1.0 ROM: 2, 4, 0.5, 2.5, 1.5, 3, 16, 32, 0.25 and 0.1. The decomp's reading matches the machine code, except that the jump behind the player sweeps a sphere of 0.25, not of the decomp's distance.

- It hangs back on a leash rather than swinging behind. Its goal is 1.5 above the aim's goal, and only dragged along to stay 2 to 4 from it in X and Z. So walking away, it follows 4 behind; walking toward it, it backs away 2 ahead.
- It eases toward its goal at 1/32 of the way a tick, but no more than 0.1 (6 a second), and turns round the aim toward it by a degree a tick. It aims 1.05 above the player, chasing that at 1/16 a tick, at most 0.25.
- It's never nearer than 2 in X and Z, and it can't pass through a wall: the sphere swept from where it was stops 0.25 off it.
- When it has caught up with its goal and the aim is out of sight from there, its goal jumps to 3 behind the player, or to the wall in the way, and it doesn't do that again for 5 seconds.
- R orbits as outdoors, at a distance easing to 3, between 0.5 and 2.5 above the aim. Letting go zeroes the pitch, which only the orbit uses here.
- Every part loops over a frame's ticks or acts on a goal that only it moves, so stepping it each tick matches the original at any frame rate.
- The user confirmed it in the emulator and the port alike (Oct 2, 2026): it sticks just off the walls and never wanders past one on its own. Running round a corner, it snaps back behind Alaron. R's orbit can push it through a wall, and letting go, it turns until it's back inside, with the same snap.

## Camera objects (`processVoxelCamera`)

Ported (`CameraMove`), checked against the debug ROM's machine code. A camera object takes the camera while the player is in its reach ([map-objects](map-objects.md#camera-objects) has how he reaches it). While it moves the camera or its aim, the follow camera does nothing the next tick (`camera_mode` 2, then `RevertMode`), and when it lets go, the follow camera carries on from where the camera is, in its own mode.

- **Its first tick** starts the move from wherever the camera is now, toward its reference point (or, without one, the player's sphere's bottom plus 1.05, or 0.5 in a conversation). A cut that tracks aims straight at the player then. The original does that for every tracking move, which jolts the view when a pan or an ease takes over (below); the port doesn't.
- **The moves,** each over its duration (the values' Z) in ticks: a cut (0x1) jumps there at once; a pan (0x2) goes at a steady speed; a sine (0x4) eases in and out, (1 − cos) / 2 over half a turn; 0x8 eases in, 1 − cos over a quarter turn (the decomp's precedence reads it as length − cos, but the machine code multiplies); 0x10 eases out, sin over a quarter turn. Flag 0x4000 moves the aim and 0x8000 the camera; no object does both. Each writes the camera or its aim, not their goals.
- **Tracking** (0x2000) chases 1.05 above the player at 1/24 of the way a tick, at most 0.25. Running, at 0.06 a tick, that leaves the aim about 1.4 behind him, so he runs ahead of the middle of the screen and the camera catches up when he stops, as the user saw in the original (Oct 2, 2026). In the original, a camera taking over snaps the aim onto him: a jump of 4° to 8° in one tick at 60 Hz, which the user saw jolt the view, less at the console's frame rate. They chose to treat it as a bug, so the port snaps the aim only on a cut, where the view jumps anyway, and the runtime doesn't blend a cut across frames.
- **A finished move stays done** while the player stays in reach. The original marks it to start again, but the timestamp it writes at the end of the same call overwrites the mark. Out of reach for a tick, it starts afresh next time.
- **Flags 0x100 and 0x200,** on the 28 objects that don't move the camera, set how far behind the outdoor camera follows (the values' Y, at least 3) for the next 30 ticks, and its pitch (X), anchoring it at the player. The interiors' camera reads neither.
- Cut-scenes' cameras are the same moves (`AllocScriptCamera`), aimed at an actor, so session 6 builds them from `CameraMove`.
- In a conversation the original counts the cameras holding it, and while any does, R doesn't orbit. That comes with session 5.

## The conversation camera (5c)

From 1.0's machine code (Oct 2, 2026, a Sonnet subagent; the constants spot-checked). Talking to a wanderer, both bodies are flagged `ACTOR_800`, and the player's camera focus is cleared (`ClearPlayerCamFocus`). Each frame, `ProcessPlayers` (`0x80012854`–`0x80012968`) gathers the flagged bodies and, in trek with no focus, frames them (`CreateFocusFromMultiple`, `0x800a77b4`):

- **The focus** is the midpoint of the two bodies farthest from their centroid: with two, the midpoint between them. It's anchored there (`SetFeild70`) and the camera aims at it as at the player.
- **The way it looks:** with h from the farthest body to the focus, the facing passed is (−|h.z|, |h.x|), normalised, in the original's reversed facing (`0x800a79cc`–`7a08`, checked), so the camera sits that way from the focus: always toward −X and +Z, whoever stands where. That's across the line between the two when it runs from −X−Z to +X+Z, and along it, looking past one at the other, when it runs from −X+Z to +X−Z.
- **The distance** is (|h| + 4) / tan 33.5°, at least 5 (`0x800cdb14`): 33.5° is half of the 75° field of view (`0x800cdb10`) less 8. The decomp has the tangent upside down. Two bodies 1.6 apart put it about 7.2 away.
- It's stepped as the follow camera with that distance (`unk5c`) for the frame, then the distance goes back to the default, and `unk80` is 1, a quick swing each frame.
- At the end the camera is given back to the player (`GiveCameraToPlayer`) with a quick swing of 5 frames (`unk80` 5), and its pitch back to the default.
