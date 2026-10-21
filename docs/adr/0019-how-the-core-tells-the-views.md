# 0019. How the core tells the views what changed

Status: Accepted · Oct 17, 2026

## Context

[0003](0003-headless-rules-core.md) says the core sends presentation out through a host interface and tests implement a recording host. [0012](0012-editor-holds-the-importer.md) keeps the runtime's loops thin: it draws what the core steps and carries out what it asks. As M1 and M2 grew, views came to learn about core changes in five ways:

1. **Host calls**, some carrying typed cue records:
   - `ITrekHost` and `IMapObjectHost`, with methods such as `OpenLoot`, `Teleport`, `Animate` and `ScriptEnded`.
   - `IScriptHost.Cue`, with records such as `CameraCue`, `MoveCue` and `SpawnCue` (`Scripts/ScriptCues.cs`).
2. **C# events:** `ChunkStreamer.Loaded` and `Unloading`, `Settings.Changed`, `Weather.SkyChange`, and the pause menu's `CampChosen`, `HelpChosen`, `CreditsChosen` and `TheatreChosen`.
3. **Polling by reference.** The runtime compares the popup it shows with `MapObjectUse.Popup` every tick (`Game.ShowPopup`).
4. **Getters that are read once and then clear:** `FollowCamera.TakeCut` and `PauseScroll.TakeCue`.
5. **Booleans the view polls to learn that something happened:** `SystemMenu.HelpChosen` and `CreditsChosen`.

Combat's presentation will need much more of this: attack and hit animations, damage numbers, spell visuals and the combat log. Only the first way gives tests a record of what happened in order. The last two are one-off events in disguise, and miss whatever happens between two reads.

## Decision

- **One-off presentation goes through a host.** Something that happens once and that the runtime must show or carry out is a call on the subsystem's host interface: a camera cut, an animation, a sound, opening a shop or loot window, a battle starting.
  - Where a subsystem sends many kinds of these, they're typed cue records through one method, as `IScriptHost.Cue` does.
  - Tests and the editor implement the host and record the calls.
- **Events are for lifecycle only.** C# events say that something was made or freed, or that a whole configuration changed, so views build or free their parts: chunks loading and unloading, and settings changing.
- **Continuous state is polled.** What a view draws each frame (positions, alphas, the page a menu shows, the popup up) is core state the view reads each frame. It needs no notification.
- **An ending is returned.** A mode or menu that finishes says how from its step, as `TrekStep` does and as the original's exit codes do. Whoever runs the mode acts on it.
- **New code doesn't use getters that clear when read, or booleans polled for an event.** The existing ones become host calls or returned endings when their code is next changed. The pause menu's `…Chosen` events and flags become one returned ending.

## Consequences

- Combat's presentation is built as cue records through a combat host. Its tests can assert the sequence of cues a turn produces, and the editor can replay them. Built Oct 21, 2026: `ICombatHost.Cue`, `Battle.Tick`'s returned `BattleStep`, and `CombatLog` as the one writer of the log box ([combat](../systems/combat.md#what-a-battle-tells-its-host)).
- A view never has to diff the core's state to find out that something happened, and the core never needs to know which views exist.
- Some existing code doesn't follow this yet: `Weather.SkyChange` and `FollowCamera.TakeCut`. It changes when it's next touched, not in a sweep. `PauseScroll.TakeCue` became the cue its `Step` and `Close` return, and the pause menu's events the session's own menu, which cues its sounds and the scroll's steps through `IPauseMenuHost` and returns its ending from its step (Oct 20, 2026).
