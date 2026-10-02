# 0011. One follow camera, with the original's modes

Status: Proposed · Oct 2, 2026 · Supersedes the part of [0009](0009-code-organisation.md) that makes `Mode1` a sibling of `FollowCamera`

## Context

[0009](0009-code-organisation.md) names classes after what they do, not the mode that first uses them. As an example it says `FollowCamera` is `Camera::Mode0`, and that `Mode1`, "which interiors and combat use, will be its sibling."

Porting `Mode1` (M2 session 4c) showed two things that example didn't know:

- **The modes are one camera.** The original keeps a single `Camera_struct` and switches its `camera_mode`. A teleport switches the mode and keeps every field. `Mode1` then eases toward the goal that `Mode0` or the teleport left, and the R orbit (`FUN_800b0fac`) serves both modes. Camera objects (`processVoxelCamera`) write the same position and aim whichever mode is on, and take the camera from it for a tick (mode 2). Two sibling classes would have to hand all of that state across at every switch, and camera objects and cut-scenes would need to know which one is live.
- **Combat doesn't use `Mode1`.** Only trek calls `Camera::ProcessGameCamera`. Combat sets mode 4 and moves the camera itself (`processCombatCamera`).

## Decision

- `FollowCamera` is the trek camera that follows the player: the original's `Camera_struct` with its follow modes. `CameraMode.Outdoor` is `Mode0` and `CameraMode.Interior` is `Mode1`, and the camera keeps its state across a switch, as the original does. Each mode's code sits in its own file of the partial class (`FollowCamera.cs`, `FollowCamera.Interior.cs`).
- What moves that camera from outside, such as camera objects and later cut-scenes, is its own class that acts on a `FollowCamera` (`CameraMove`), named for what it does.
- Combat's camera is decided when combat is ported (2.5). It may share `FollowCamera`'s state or be a class of its own; nothing here assumes either.
- The rest of 0009 stands: the core is organised by subsystem, classes are named for what they do, and the glossary's terms apply.

## Consequences

- Teleports call `FollowCamera.Arrive`, which switches the mode and places the camera, rather than building a new camera.
- The doc comments call `FollowCamera` the trek camera and cite `Mode0` and `Mode1` where each mode's code is.
