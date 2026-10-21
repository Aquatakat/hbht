# 0022. The core is handed its settings each tick, made from the player's with the command line's over them

Status: Accepted · Oct 21, 2026 · Refines [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md)'s "a setting the core needs is passed to it like any other input", and supersedes [0013](0013-settings-in-the-options-menu.md)'s consequences on the runtime's `ApplySettings` and the command line's settings

## Context

[0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) says a setting the core needs is passed to it like any other input, but not how. By M3 the settings reached the core five ways:

- **Read live from `Settings`:** the audio's volumes (`GameAudio`).
- **Pushed on `Settings.Changed`:** the runtime's `ApplySettings` set the rain's density, the sky's placement, the player's pace, a battle's spell auras and the credits' screen height on the core's objects.
- **Hard-coded:** Oriana's path lights go out by the original's distances (`LoadedMap.PutOut`), which looked like a setting forgotten.
- **Written over by the command line:** `--draw-distance`, `--sky` and the rest were written into the player's `Settings` for the run, so the next change in the menu saved them to `settings.json`, and `--pace` was set on the player's body after `ApplySettings`, so any change of setting wiped it.
- **Set from two places:** a battle's spell auras, by `ApplySettings` and as the arena was shown.

So a headless run played by whatever the objects were left at, and when a setting took effect depended on which way it came.

## Decision

- **The core is handed what it needs of the settings each tick**, as an immutable record, `SessionSettings`: the volumes, the rain and snow's density, the sky's placement, the player's pace, the spell auras and the UI screen's lines, which the credits lay out on. `GameSession.Tick` takes it first, and the session hands it on to what plays by it (`GameSession.Apply`): the audio, trek's, a stage's and an arena's rain, the sky director, the player and a battle, which takes it as it begins. A setting changed between two ticks takes effect on the next tick, and not before. `GameSession.Start` takes the one to play by until the first tick, so the sky and the audio are built by it.
- **Nothing in the core reads `Settings`** but the options menu's model, which changes it. `SessionSettings.From` is the one mapping from the player's settings, and Brisk's pace is a constant there.
- **What only the runtime draws stays the runtime's:** the window, v-sync, anti-aliasing, the 3D resolution, the draw distance and model detail, and the UI screens' lines and filter. The runtime carries them out on `Settings.Changed`, which only the runtime hears.
- **The command line's settings are a layer** (`LaunchSettings`): the runtime draws by the player's settings with the run's over them, and makes each tick's record from the same. They're never written into the player's settings, so `settings.json` holds only the player's. Each holds for the run until the player changes that setting, in the menu or with a debug key, and then the player's holds; `--pace` gives way to the Pace setting.
- **Headless runs and tests pass a record explicitly**, `SessionSettings.Original` for the original's, the default where none is given.
- **The original's behaviour isn't a setting.** Where the port keeps the original's distances or cone whatever the settings, as for the path lights going out (`LoadedMap.PutOutDistances`, `ViewCone.Original`), the code names it so, and it isn't in the record.

## Consequences

- A new setting the core needs is a property on `Settings`, a row in `OptionsMenu`, a property on `SessionSettings` with the original's as its default and a line in `From`, and a line in `GameSession.Apply`. One only drawn is a line in the runtime's `ApplySettings`. A setting for a run is a property on `LaunchOptions` and a line in `LaunchSettings`.
- The options menu shows the player's own settings, not the run's.
- The editor runs no session, so it needs nothing; one that did would pass a record as the tests do.
