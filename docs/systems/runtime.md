# The runtime (M1)

`src/Aidyn.Game` is the Godot game. It references `Aidyn.Plugins` and `Aidyn.Core`, and not `Aidyn.Rom`, so the build enforces ADR 0001. Import first (see [importer](importer.md)), then run it with `tools/godot.ps1 --path src/Aidyn.Game` (build with `dotnet build` first). It reads the plugins from the data folder, so set `AIDYN_DATA` for both if the import went elsewhere.

- **What it does so far.** It streams the 3×3 chunks around the player: each chunk's scene and see-through scene, the models its scene objects place (Expansion Pak set; each level of detail over its own distance range), actors running their Borg7 state machines, and the collision. Alaron starts where a new game does, on the ground. The core's ports of the original's player movement and camera ([movement](movement.md), [cameras](cameras.md)) move him and follow him.
- **The tick.** The player and camera are stepped once per 60 Hz tick in `_PhysicsProcess`, and Godot's physics interpolation smooths the display. Where the original acts once per frame, or loops toward a target fixed for the frame, the port matches what it does at an idealized 30 fps, a `delta` of 2 (`Aidyn.Core.Cadence`). The user chose this. The console runs at 15–25 fps, and the camera feels snappier on an overclocked emulator.
- **Collision** is the collision assets in the core's `CollisionWorld`, which the chunk streamer fills and empties. Godot physics isn't used. Toggles stay as stored until the flag engine can change them.
- **Stand-ins** still to replace: the flat sky colour and fixed light, and materials that are Godot's glTF import plus `WrapMaterials`. Objects gated on real flags are all shown until the flag engine exists (M2). Alaron's head follows his neck, but the game keeps it upright and turns it toward nearby objects.
- **Controls.**
  - WASD or the left stick moves. The stick walks under 80% tilt and runs above. On a keyboard, Shift walks. Ctrl or the left trigger sneaks (Z on the N64).
  - The original orbits the camera with R and the stick, and he can't move while it does. Here that's the gamepad's right shoulder with the left stick, dragging with the right mouse button, the right stick, or Q and E.
  - F toggles a free camera (Space and C for up and down, Shift for faster). F5 toggles the pace.
  - F3 toggles the overlay. It shows the chunk, the position in chunk coordinates, the surface underfoot, his speed over the last second, the animation request and the pace.
- **For scripts:** `-- --chunk I08 --at 16,62 --yaw 0 --pitch -15 --distance 5 --hold move_forward,4 --screenshot <absolute path>.png` starts somewhere, holds an input, saves the window and quits.
  - `--hold` takes several actions joined with `+`, as in `move_forward+walk,4`.
  - The screenshot path must be absolute, or Godot saves it relative to the project.
  - `--fly x,y,z` starts with the free camera. The run gives up after a minute.
  - Gwernia's open ground is around (30–80, 30–80) in M07, for example `--chunk M07 --at 40,55`. Elsewhere in M07 there's no ground, so he falls and is put back again and again.
- **The spawn point** is tight. A rock stands in a hole in the terrain in front of it, with collision walls and no top, and a birch's collision box is beside it. The original drops the player from a height of 10; the runtime puts him on the highest ground below the spawn instead.
