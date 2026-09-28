# Rendering

How the runtime draws scenes as the N64 does, and what's left. The runtime's other stand-ins are in [runtime](runtime.md).

## On-screen values

The N64 has no linear light. Its texels, vertex colours, tints and blends are the values it puts on screen, and the game turns the VI's gamma off (`osViSetSpecialFeatures` in `gfx.cpp`), so the emulator shows the framebuffer as it is. Godot works in linear light, where the same numbers come out differently:

- **Multiplying.** A texel times a vertex shade of 0.5 is half as bright on the N64. Treated as linear, the shade shows at about 73%. The scene export used to write vertex colours as they are into `COLOR_0`, which glTF defines as linear, so baked shading looked washed out: the birch at the start of a new game came out twice as bright as the original's.
- **Blending.** Godot blends in linear light, where the same alpha darkens the screen about half as much. For black over the scene, darkening by 1 − a on screen is darkening by (1 − a)^2.2 in linear light, so the player's shadow uses that alpha. Its 31% on the N64 looked like 15% before. The N64 material's translucent surfaces don't correct for this yet, and coloured blends don't reduce to one alpha.

The export now decodes vertex colours and untextured tints as sRGB, so they're correct glTF and a viewer that multiplies in linear light comes out almost as the N64 does (a power curve carries through a product, and sRGB is nearly one). The N64 material goes further and works in on-screen values itself.

## The N64 material

`N64Material` (in the Inspector project, linked into the runtime) replaces every material Godot's glTF import makes, in the runtime and in the Inspector's previews. It builds each from the glTF's own material, because Godot's import converts colours and samplers in ways the N64 doesn't.

- **The combiner.** `setStaticMode` sets textured draws to `TEXEL0 × SHADE`, then `× PRIMITIVE` in the second cycle, and untextured ones to `SHADE × PRIMITIVE` (combine modes `0x127E03, 0xFF0FF3FF` and `0xFFFE03, 0xFF0E79FF`, decoded by hand). Alpha is texel alpha times prim alpha, or shade alpha times prim alpha. The shader reads raw texels (no `source_color`), turns the vertex colour back into on-screen values, multiplies there, and hands Godot the linear result.
- **Shade** is the vertex colour on unlit display lists. On lit ones it's the RSP's lighting, which Godot's lights still stand in for (Lambert diffuse, no specular).
- **Prim** is the scene's tint, `SceneData.colorFloats`, times the mesh tint for untextured draws (`FUN_800a1184`). Every chunk scene and scene object gets `gMainColor` (`SetSceneColors`), which the sky sets (below). A scene object with the `Tint` flag and a non-zero tint gets the average of the two instead: 1,398 objects have it. I08, where a new game starts, has 15 of scene `0x1910` tinted a pale blue, and M07 has a birch tinted grey (88, 85, 86), which comes out at about 60% of its neighbours. Bodies get white and are lit instead (`some_player_render_sub`, `set_sun_light`), so actors' meshes are marked as bodies.
- **Fog** is per vertex, as the RSP computes it. The amount is the depth buffer's `z / w` for the original's planes, on the 0–1000 scale `gSPFogPosition` takes (0 is `z / w` of −1, 1000 is 1), between the fog's start and end. Unlit draws blend toward the fog colour in on-screen values before the pixel is written, as `G_RM_FOG_SHADE_A` does. Lit ones use Godot's `FOG` output, after its lighting, in linear light. With trek's planes of 1 and 180 and no fog in the weather, the fog runs from 996 to 1000, which is from about 105 units to the far plane. The runtime's follow camera now stops at 180, as the original's does.
- **Wrapping.** Borg2 materials clamp, repeat or mirror S and T separately (faces are often half a texture, mirrored), which a `BaseMaterial3D` can't, so the shader wraps each axis itself, as `WrapMaterials` used to for the materials that needed it.
- **Globals.** The tint, the fog colour and the fog's range are global shader parameters (`n64_tint`, `n64_fog_color`, `n64_fog`), untinted and unfogged until the runtime sets them. The Inspector leaves them so.

Measured at the spawn, against the code before the material, at the same spot and camera: the birch trunk went from 87 to 47 (0.54×) and the ground from 52 to 31, while Alaron, who is lit, didn't change. The noon tint then takes them to 41 and 27. The original's screenshot of the same birch peaks at about (72–80, 75–83, 60–68), and ours at noon at (77, 80, 66).

## The sky's colours

Every frame, `Sky::RenderSky` sets five colours from the sky gradient of the terrain the player is in, and the scenery is multiplied by the first. `Aidyn.Core.Sky` ports it, and the runtime's `SkyView` applies it each tick.

- **The gradients** are eight Borg8s, 12 × 37 RGBA32 (`sky_borg8s`, read from the code segment). Rows 0–31 are the sky from top to bottom. Rows 32–36 are the tint, sky, fog, cloud and sun colours. There's a column every two hours from midnight, and `Sky::GenerateSky` blends between the two columns either side of the time of day, wrapping from 22:00 to midnight.
- **The tint** (`gMainColor`) is `tint row × 0.65 + 0.15`, plus a fifth of the sky colour, clamped and truncated to bytes (`Sky::SetColors`; the constants are checked against the debug ROM's machine code). The tint row is grey: 88 at midnight, 225 at 10:00 and noon. So at a new game's noon on terrain 2, the scenery is multiplied by (222, 225, 227). Lens flare darkens it while the camera faces the sun, which isn't ported yet.
- **Checked against the emulator** at the spawn at about 04:00 and 20:00. Between the user's two shots every region, from backdrop to leaves, changed by the same (0.75, 0.84, 0.84), so the time of day is one multiplier over the scenery, as ported. The clear sky predicts (0.78, 0.86, 0.87) from 04:00 to 20:00 and (0.75, 0.84, 0.84) to 20:20; the runtime's own renders changed by (0.78, 0.85, 0.86). The user thought it might be raining at 20:00. The rain sky (sky 2) at about 18:40 would fit as well, since both tints are nearly grey, so these shots don't show whether rain changes the sky. At 04:00, with the views matched, ours over the original's was even across R, G and B in each region: the warm dawn tint is right. The brightness matched at the foot of the backdrop and was about 10% dark on the ground. Upper areas showed different trees in the two views, so they don't count.
- **Which sky.** `BorgMaps::GetMapTerrain` picks a sky number by terrain in a switch, which the importer carries as a fact (checked against the debug ROM's jump table) on terrain records. Terrain types 6, 8, 9, 11 and 13–16 don't draw the gradient: the background is black, there's no weather, but the colours still come from it. Every new chunk the player enters chooses again (`Sky::SetBackgroundType` with 600), which fades to a different sky over 600 ticks. Choosing again restarts the fade, and choosing the sky that's up abandons it.
- **The clock.** A new game starts at noon on day 2 of week 6 (`InitGameGlobals`), and trek time runs 72 times as fast as real time, so a day takes 20 minutes. `--time <hours>` starts at another time, and the overlay shows it.
- **Bodies' light** comes from the same colours: an ambient of half `gSkyColor`, and the sun in `gSunColor` from the sky objects' sun position. Not ported yet.

## Normals

- **Unlit meshes have no normals that face up.** They're exported without `NORMAL`, as the N64 doesn't light them, and Godot generates its own. A decal's angle fade (`normal_fade`), which compares the decal with the surface's normal, hid the player's shadow even on level unlit ground, so those generated normals don't face up. Anything that reads unlit geometry's normals will need them exported, or the winding checked.

## What's left

- **The sky itself.** The background is still a flat placeholder. `Sky::GenerateGradient` draws rows 0–31 down the screen, offset by the camera's pitch, and fades them toward the fog colour in fog.
- **Bodies' lighting**, from the sky's colours and the sun, instead of the fixed placeholder light.
- **Translucent surfaces** blend in linear light (see above).
- **The weather's fog** (`fogTime`) thickens the fog and moves it in. There's no weather yet.
- **Lens flare**, which darkens the tint.
- **Billboards:** the user doesn't recall any that visibly turn to follow the camera, except maybe a bush in later areas. This can wait until one turns up.
- **Texture filtering:** probably not worth emulating. Aidyn doesn't use the N64's three-point filtering for artistic effect, and its textures, especially the faces, have far more texels than the screen shows.
- **Animated textures.** No Borg5 uses the scene-level kind, which the game would crash on. The effects the user remembers are probably the 103 Borg1s that scroll (flags `0x200` and `0x100`), which the export doesn't animate yet.
- **Some odd rendering** the user noticed in the cinematics. It isn't pinned down yet.
- **The particle simulation** (see [particles](../formats/particles.md)), and each cinematic shot's near and far planes, which the cinematic table overrides and the viewer ignores.
- **Borg4 lights:** the decomp's draw code seems to ignore them in favour of the lights the engine sets, which is still unresolved.
