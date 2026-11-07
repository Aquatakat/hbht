# 0038. The renderer is the player's choice

Status: Accepted · Nov 7, 2026 · Taking effect as the game next starts is superseded by [0039](0039-changing-the-renderer-restarts-the-game.md)

## Context

Godot has three renderers. Forward+, which the runtime has always run, clusters lights over Vulkan or Direct3D 12 and can draw everything Godot can: global illumination, screen-space effects, volumetric fog, hundreds of lights. Mobile draws each object once with a few lights of its own, over the same APIs, and skips what Forward+ spends on clustering and a depth prepass. Compatibility is OpenGL 3.3, for old or weak graphics, and what Godot falls back to on its own where Vulkan and Direct3D 12 aren't there (`rendering/rendering_device/fallback_to_opengl3`).

The original's look needs none of Forward+'s features: the N64 material lights per vertex from the sky's globals, and Godot's lights don't touch it. So at the Overworld's busiest spot the user's machine drew 365 fps under Forward+ and 445 under Mobile, and their Intel UHD 770 at 1080p 37 and 61 ([runtime](../systems/runtime.md#measuring-it)). But a mod that remodels and relights the game would want Forward+'s features, and the user wants both open: the port fast on modest hardware, and room for that mod.

## Decision

- **The renderer is a setting,** the Video page's Renderer (`Settings.Renderer`): Forward+, Mobile or Compatibility. Forward+ stays the default.
- **It takes effect as the game next starts.** Godot picks its renderer before any of the game's code runs. The runtime writes the choice to `user://renderer.cfg` (`Renderers.Choose`), which the project reads over its own settings as it starts (`application/config/project_settings_override`). The Video page marks a renderer other than the one running with an asterisk. A run's `--rendering-method` still goes over it.
- **The original's look is the same under all three.** What the runtime draws must work under each, or fall back to a likeness where one can't draw it: Compatibility has no decals, so bodies' shadows are the original's flat square there (`ShadowView`). Where a renderer handles colour differently, the runtime follows it: Compatibility hands the 3D image to the screen as it is, so the colour correction that undoes Forward+'s and Mobile's encoding is left off there (`OnScreenOutput`).
- **What only Forward+ can draw is a mod's to use,** and a mod that needs it says so; a shader can ask which renderer it runs under (`CURRENT_RENDERER`) and do without.

## Consequences

- A player on weak graphics can trade Forward+'s unused features for speed, and gets the same picture.
- Each change to how the runtime draws is checked under Compatibility and Mobile as well as Forward+ (the comparison scenes under [runtime](../systems/runtime.md#measuring-it)).
- The choice lives in two places, `settings.json` in the data folder and `renderer.cfg` in Godot's user folder, which follows it each run. A data folder of its own (`HBHT_DATA`) doesn't keep a renderer of its own.
- A player stuck with a renderer that won't start can delete `renderer.cfg`, or start with `--rendering-method`.
