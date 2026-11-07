# 0039. Changing the renderer restarts the game

Status: Accepted · Nov 7, 2026 · Supersedes [0038](0038-the-renderer-is-the-players-choice.md)'s taking effect as the game next starts

## Context

[0038](0038-the-renderer-is-the-players-choice.md) made the renderer the player's choice, taking effect as the game next started, with the options marking a renderer chosen but not running. A setting that waits for the player to quit and start again is a nuisance, and the port can come back to where the player was by itself: the save format holds the game, and Godot can start itself again as it quits (`OS.SetRestartOnExit`).

## Decision

- **The Video page's Renderer shows the renderer running.** A asks, in the quit box's feathered box: "Changing the renderer requires a restart, which will happen automatically.", over "Remain on" the one running and "Change to" each of the other two (`SystemMenu.AskRenderer`). The stick doesn't change it, and nothing is marked.
- **Changing it restarts the game and comes back.** From trek's pause menu, the game is saved to `restart.json` in the data folder, apart from the player's saves, and Godot starts again on the renderer chosen (`--rendering-method`) from that save (`Game.Restart`). From the title's menu, it starts again at the title, without the intro cards. Either way the options open at the Renderer, with the cursor on it.
- **It isn't offered in combat,** which can't be saved, so the row is left out of combat's options.
- **The choice is kept only once the new run has started on it.** The run that comes back (`--restarted <renderer>`) compares what Godot started with what was asked for. If they match, it keeps the renderer as the player's (`Settings.Renderer`, and `user://renderer.cfg` for the next launch) and says "Renderer changed to …". If they don't, as when Godot falls back to Compatibility, it keeps the one before and says "Renderer failed to change to … and is on …". A renderer that can't start at all leaves the next launch on the one that could. Either way it deletes `restart.json`.

## Consequences

- The player never restarts by hand to change renderer, and loses nothing a save keeps. What a save doesn't keep starts over, as after a load: the music, the falling rain, the camera's settling.
- `renderer.cfg` and `settings.json` keep the renderer as 0038 says, and `--rendering-method` still goes over them.
- A restart keeps only Godot's own options for where the project is and which GPU and drivers it used (`Renderers.GodotArgs`). The game's launch options would play again over the save, so they're dropped.
