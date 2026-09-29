# Playtests

The user's notes from playing the runtime and timing the original, newest last. What they led to is in the [systems](systems/) docs.

- **The user's first walk (Sep 27, 2026).** The user walked from the spawn to Gwernia (M07), and the barn, statue and castle line up with their emulator capture. Their notes, in order of how much they stood out:
  - **Vertex colour looks missing** on some models, trees especially. The unlit primitives do carry `COLOR_0`, so check whether Godot's import applies it to textured unlit materials, and whether the `WrapMaterials` swap keeps it. The N64 material should take over either way.
  - **Alaron needs his round shadow** on the ground under him (the player shadow in `ZoneEngine.cpp`; see the combat visuals in [hardcoded-content.md](hardcoded-content.md)).
  - **The camera:** the generic orbit camera with mouse control feels good, but the SM64-style one is still the target. When a wall is behind the player, the spring arm pulls the camera into his face.
  - **Movement speed** is too fast now, but the original's is too slow and feels poor. Port the original speed exactly as the fidelity default and offer a faster profile (ADR 0006's fidelity settings; roadmap Phase 6). Gravity is probably Earth's, since 1 unit is about 1 m, and there's little falling in the game.
  - **Step height is too low.** He gets stuck on low edges. `CharacterBody3D` has no step-up, so either add one or port the original's collision response, which collides with edges and corners by the triangle flags.
- **The original camera, timed by the user (Sep 27, 2026).** In the emulator, on the 1.0 ROM:
  - It lags. After running left or right and stopping, it takes 500–1000 ms to settle.
  - It always wants to be behind Alaron. Standing still, it turns slowly toward his back and decelerates as it gets there. It took 45 s to go from about 5° off head-on to 90°, then another 70 s to settle behind him. Facing it head-on, it stays put, as if it can't decide which way to turn.
  - It pulls in toward Alaron when it hits collision.
  - Holding R and moving the stick orbits it. It won't go below level with Alaron, or above about 75°.
- **Movement, timed by the user.** From the statue in Gwernia to the tree north-northeast of it along the water, Alaron runs in about 9 s and walks in about 24 s.
- **The sky at night (Sep 28, 2026).** On the beach at L08 around 21:40, the user called bodies' lighting correct for now. A detailed pass comparing screenshots across the weathers can come later. Two notes:
  - **The clouds drift against the scenery** as the camera turns. Near the arch at the left of the screen, they moved along with the camera and slid past the arch. See [rendering](systems/rendering.md): the original places sky sprites at the sine of their angle, where perspective puts the scenery at the tangent, and the sky also read the camera a tick ahead of the drawn scenery. The clouds' grid of three repeating images makes it look worse, since a lookalike can take a cloud's place after a turn.
  - **The projected clouds still stretched toward the edges of a 16:9 screen** (Sep 29, 2026). The user saw the same in the emulator, only less of it at 4:3, so it's the original's. The projection had only fixed placement across the screen, at a level look, so the sky is now projected fully as directions (see [rendering](systems/rendering.md)).
  - **The moon has no surface texture.** That's the original's: its six phase images are IA8 with every intensity nibble at F, so they're white discs shaped by alpha. The new moon's faint mottling is in its alpha.
