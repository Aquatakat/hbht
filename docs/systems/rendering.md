# Rendering: what's left

What the decoders found that the runtime's renderer still has to do. The runtime's own stand-ins are in [runtime](runtime.md).

## Blending

- **The N64 blends what's on screen.** Godot blends in linear light, where the same alpha darkens the screen about half as much. Found with the player's shadow, whose 31% on the N64 looked like 15%. For black over the scene, darkening by 1 − a on screen is darkening by (1 − a)^2.2 in linear light, so the shadow's decal uses that alpha. The N64 material's translucent surfaces will need the same correction, and coloured blends don't reduce to one alpha, so they need their own treatment.

## From static graphics (1A)

- **Lighting:** the viewer's lighting looks like sunny daytime in-game. Real scene lighting varies with time of day and weather. The decomp's draw code seems to ignore Borg4 lights in favour of a fixed white light, which is still unresolved.
- **Billboards:** the user doesn't recall any that visibly turn to follow the camera, except maybe a bush in later areas. This can wait until one turns up.
- **Texture filtering:** probably not worth emulating. Aidyn doesn't use the N64's three-point filtering for artistic effect, and its textures, especially the faces, have far more texels than the screen shows.

## From animation (1B)

- **Lighting**, as in 1A. The cinematics look flat without it.
- **Animated textures.** No Borg5 uses the scene-level kind, which the game would crash on. The effects the user remembers are probably the 103 Borg1s that scroll (flags `0x200` and `0x100`), which the export doesn't animate yet.
- **Some odd rendering** the user noticed in the cinematics. It isn't pinned down yet.
- **The particle simulation** (see [particles](../formats/particles.md)), and each cinematic shot's near and far planes, which the cinematic table overrides and the viewer ignores.
