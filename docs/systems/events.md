# Events and triggers (roadmap 2.10)

ADR 0008's layer 2: *on an event, if its conditions hold, do its effects*. The core fires named events where the original's engine code does something the data doesn't say, and the plugins' trigger records say what follows. The base plugin's triggers carry what the original hardcodes at those places, so a mod changes or adds to them like any record, with no hook the base game has and mods don't.

`GameEvents` and `Triggers` in `Aidyn.Core.Events`, `TriggerRecord` in `Aidyn.Plugins.Records`, `TriggerImporter` in `Aidyn.Rom.Import`. `GameSession` fires the events and carries out what takes over.

## The records

`aidyn:trigger/<n>`:

- **`on`**: the event's name.
- **`subject`**: what the event must be about, such as the script that ended. None matches any.
- **`if`**: conditions, all of which must hold: Borg13's condition ops (`CheckFlag`, `CheckMember`, `CheckItem`, `CheckSkill`, `CheckBestStat`, `CheckWorstStat`, `CheckSpeakerStat` with the member as its target, `CheckPartySize`, `CheckGold`), each holding when its value is in its `range`, or exactly 1 without one, as an If node's first link takes.
- **`do`**: effects, in order, each written with its `type`:

| Type | What | Takes over |
| --- | --- | --- |
| `op` | One of Borg13's effect ops that needs no speaker or actor, run as a node runs it: `SetFlag`, `ClearFlag`, `AddGold`, `TakeGold`, `AddItem`, `TakeItem`, `AddMember`, `RemoveMember`, and `AddExp` with the member as its target | |
| `trueName` | The party member `member` knows his True Name, which a save keeps ([saves](saves.md)) | |
| `cinematic` | The cinematic `play` plays over everything; trek comes back after it unless its own end leads elsewhere | Yes |
| `script` | The cut-scene a dialogue object (`from`, an `aidyn:voxel/…`) starts plays at once, on its stage from its reference points, with no trek before it | Yes |
| `credits` | The credits roll; with `ending`, they can't be skipped, end with "The End", and end the game | Yes |
| `gameOver` | The game is over: the original goes back to the title, and the runtime loads the last save (the user's choice for the game over, [combat](combat.md#in-and-out)) | Yes |

Conditions and ops go through the script VM's own, `ScriptOps` in `Aidyn.Core.Scripts`, so they mask and roll as a node does, and the gold's and experience's sounds go to the session's host. A condition's or op's target is also the speaker `CheckSpeakerStat` reads and the member `AddExp` gives to. The one difference is that a trigger has no map object and keeps nothing: each `CheckSkill` rolls the party's check afresh, though it still rolls 0–21 while `randomSkillChecks` is set.

An effect that takes over from trek ends the firing: the triggers after it on that event don't go off. One that can't be carried out (a cinematic no plugin has) is reported and the event's place goes on as it would without it. A type no build knows loads as a plain effect that does nothing, and `Triggers.Problems` reports it, as it does a trigger on an event the core never fires.

## The events

| Event | Subject | Fired | What the original does there |
| --- | --- | --- | --- |
| `game.started` | none | A new game, its world built, before trek's first tick (`GameSession.StartNewGame`, which the runtime calls) | The title's New Game (`Title.ModeFrame`, `0x800551c0`) loads the opening: `Cinematic.Load(0, 1, 1)` at `0x80055434` |
| `cinematic.ended` | the cinematic | A cinematic's end in the story, its done flag set (`GameSession.CinematicEnded`); not one played from the Theatre | `Cinematic.EndCase` (`0x80053c20`) switches on the kind `Cinematic.Load` was given (table `0x800D20B0`) |
| `script.ended` | the script | A cut-scene's end, its done flag set and its stage gone (`GameSession.EndCutScene`) | `CutScene.Free` (`0x8005169c`) switches on the script's ending kind, which `Script.Start` (`0x8004ff80`) looked up in the endings table (`0x800DFE1C`, `CutScene.EndingKind` `0x800dfe4c`) |

`script.ended` fires for cut-scenes alone: the table is read only by `CutScene.Free`, reached only from `CutScene.ModeFrame` (`0x8005039c`); a conversation's end (`Conversation.End`, `0x80051ccc`) never reads it, and all six of its scripts are cut-scenes.

## The base plugin's

`TriggerImporter` writes them, the endings table's from the ROM (`CodeTable.ScriptEndings`) and the rest as facts checked in 1.0's code:

| Trigger | On | If | Do | The original |
| --- | --- | --- | --- | --- |
| 0 | `game.started` | | the opening (`cinematic/0`) | `Title.ModeFrame`, above |
| 1 | `cinematic.ended` 0 | | `script` from `voxel/0x0FDB/7`: script 866 (`0x0362`, number 1004) on its stage | `Cinematic.EndOpening` (`0x8005403c`) loads Borg9 0x0FDB, takes the last dialogue object whose script is 0x0362 (`+0x28`), and calls `Script.Start` with its stage, returning mode 0xC: no trek is built first |
| 2 | `script.ended` 0x04F4 (the Rooughah scene, 1268) | `CheckFlag` 3442 | Roog's (`cinematic/2`) | `CutScene.Free`'s kind 3: `FlagNetwork.Get(0xD72)` at `0x8005179c`, then `Cinematic.Load(2, 3, 1)`; with 3442 clear, the ordinary end |
| 3 | `script.ended` 0x04FE (the last scene, 1278) | | the ending (`cinematic/3`) | Kind 1: `Cinematic.Load(3, 2, 0)` |
| 4–6 | `script.ended` 0x0512–0x0514 (the death scenes) | | `gameOver` | Kind 2: the return mode 0, the restart at the title (`Game.Restart`, `0x8001f284`) |
| 7 | `cinematic.ended` 2 | | `trueName` Alaron | `Cinematic.EndRoog` (`0x80054104`) finds the member whose id is entity DB record 153's, Alaron's, and sets his sheet's True Name bit, then returns trek (the literal 1) |
| 8 | `cinematic.ended` 3 | | `credits` with `ending` | `Cinematic.EndEnding` (`0x800540f0`) sets `Credits.Source` to 2 and returns the credits' mode 0x13; their end (`Credits.Frame`, `0x8001f648`) takes source 2 to mode 0, the title |

Every kind of the endings table takes over from trek, so a cinematic after a cut-scene starts straight from its end, with no trek built and none drawn. After Roog's, trek is built again where the player stood and fades in, as after any cut-scene; Alaron's True Name also comes back whenever his sheet is built while 590, that cinematic's done flag, is set ([characters](characters.md)).

**The Shadow's cinematic** stays on the combat record's `shadow` (`GameSession.EndBattle`): it's chosen by how a battle ended, merged, which no condition says, and the record's `shadow` holds the merge's rules with it. A `battle.ended` event with the outcome as a condition would carry it.

### Where the port differs

- **A new game builds trek's world before the opening.** The original builds it only after script 866 has ended (`InitGameTrek` runs on `TrekLoop.Frame`'s first frame, with `TrekLoop.Restoring` clear, so the player arrives at the new game's place). The port builds it where the player arrives, plays the opening over it, frees it for 866's stage and builds it again there after: the same place and the same pictures.
- **The ending's credits end the game** by `Transition.GameOver`, which in the runtime loads the last save, as after the game over: the runtime has no title screen.

## What else could move onto triggers

The [hardcoded-content](../hardcoded-content.md) audit names more layer 2 work. Each needs its event added to `GameEvents` and fired where the original does it:

- **`spell.learned` and `item.bought`:** the first spell learned (3611) and the first potion bought (3612), which show the help.
- **`journal.added`:** the new journal entry's notice (3617), which trek shows now.
- **`camp.started`:** the camp scenes and the Campsite's script (`Camp.cpp`), now the camping record's.
- **`cheat.entered`:** the cheat codes' flags, gold and experience (`CheatCodes.cpp`).
- **`battle.ended`** with its outcome: the Shadow's cinematic after a merge, and the Goblin Ambush's end, now the combat record's.
- **`chunk.entered`:** the world map regions' flags, now the chunk records'.
- **A conversation's end** as `script.ended`, and its shops, training and battles, now `Transition.AfterScript`.
