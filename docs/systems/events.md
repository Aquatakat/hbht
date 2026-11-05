# Events and triggers (roadmap 2.10)

ADR 0008's layer 2: *on an event, if its conditions hold, do its ops*. The core fires named events where the original's engine code does something the data doesn't say, and the plugins' trigger records say what follows. The base plugin's triggers carry what the original hardcodes at those places, so a mod changes or adds to them like any record, with no hook the base game has and mods don't.

`GameEvents` and `Triggers` in `HBHT.Core.Events`, `TriggerRecord` in `HBHT.Plugins.Records`, `TriggerImporter` in `HBHT.Rom.Import`. `GameSession` fires the events and carries out what takes over.

## The records

`aidyn:trigger/<n>`:

- **`on`**: the event's name.
- **`subject`**: what the event must be about, such as the script that ended. None matches any.
- **`if`**: conditions, all of which must hold: a script's condition ops (`check.flag`, `check.member`, `check.item`, `check.skill`, `check.bestStat`, `check.worstStat`, `check.stat` with its `member`, `check.partySize`, `check.gold`), each holding when its value is in its `range`, or exactly 1 without one, as an If node's first link takes; and a trigger's own, `check.subjectKind`, 1 when the event is about an item of its `kind` (`Potion` and the rest, as an item record writes its kind), which a script node's condition refuses, having no event.
- **`do`**: ops, in order, a script's vocabulary (ADR 0034, every op in [scripts](scripts.md#the-ops)): the effects a trigger can run, which need no stage, actor or running script, and six of a trigger's own.

| Op | What | Takes over |
| --- | --- | --- |
| `flag.set`, `flag.clear`, `party.addGold`, `party.takeGold`, `party.addItem`, `party.takeItem`, `party.addMember`, `party.removeMember`, `party.addExp` | As a node's effect runs them | |
| `party.learnTrueName` | The party member `member` knows his True Name, which a save keeps ([saves](saves.md)) | |
| `cinematic.play` | The `cinematic` plays over everything; trek comes back after it unless its own end leads elsewhere | Yes |
| `script.start` | The cut-scene a script object (`from`, an `aidyn:mapobject/…`) starts plays at once, on its stage from its marks, with no trek before it | Yes |
| `credits.roll` | The credits roll; with `ending`, they can't be skipped, end with "The End", and end the game | Yes |
| `game.over` | The game is over: it goes back to the title, its intro cards first ([ui](ui.md#coming-back-to-it)) | Yes |
| `message.show` | Its `text`, where the event was fired: over the training or barter window, as the window's help on the parchment, which A, B or Start closes; in trek, in the message box. On an event fired where nothing can be shown (`game.started`, `script.ended`, `cinematic.ended`) it's passed over with a warning | |
| `lua` | Reserved for Phase 5: does nothing, and is reported | |

Conditions and ops go through the script VM's own, `ScriptOps` in `HBHT.Core.Scripts`, so they mask and roll as a node does, and the gold's and experience's sounds go to the session's host. The one difference is that a trigger has no map object and keeps nothing: each `check.skill` rolls the party's check afresh, honouring the random-checks flag. An op that needs a stage, an actor or a running script (`actor.*`, `camera.*`, `battle.*`, `script.end`, `shop.open` and the rest) is refused in a trigger by its type: `PluginSet.Validate` reports it, `Triggers.Problems` says so, and firing passes over it with a warning. A trigger's own ops are refused in a script the same way.

An op that takes over from trek ends the firing: the triggers after it on that event don't go off. The events fired in a window or in trek's notice (`spell.trained`, `item.gained`, `journal.added`, `GameEvents.InPlace`) have nothing to take over from: there such an op is passed over with a warning, the firing going on, and `Triggers.Problems` reports a trigger that has one. One that can't be carried out (a cinematic no plugin has) is reported and the event's place goes on as it would without it. An op no build knows loads as a plain op that does nothing, and `Triggers.Problems` reports it, as it does a trigger on an event the core never fires.

## The events

| Event | Subject | Fired | What the original does there |
| --- | --- | --- | --- |
| `game.started` | none | A new game, its world built, before trek's first tick (`GameSession.StartNewGame`, which the title's New Game calls, and the runtime for `--opening on`) | The title's New Game (`Title.ModeFrame`, `0x800551c0`) loads the opening: `Cinematic.Load(0, 1, 1)` at `0x80055434` |
| `cinematic.ended` | the cinematic | A cinematic's end in the story, its done flag set (`GameSession.CinematicEnded`); not one played from the Theatre | `Cinematic.EndCase` (`0x80053c20`) switches on the kind `Cinematic.Load` was given (table `0x800D20B0`) |
| `script.ended` | the script | A cut-scene's end, its done flag set and its stage gone (`GameSession.EndCutScene`) | `CutScene.Free` (`0x8005169c`) switches on the script's ending kind, which `Script.Start` (`0x8004ff80`) looked up in the endings table (`0x800DFE1C`, `CutScene.EndingKind` `0x800dfe4c`) |

| `spell.trained` | the spell | A member learns a spell new to them at a trainer (`TrainingMenu`): not raising one they know, nor reading a scroll | `WidgetSpellTrain::Purchase` sets 3611 and shows the help while 3611 is clear |
| `item.gained` | the item | The party gains a stack in a shop or loot window, bought or taken (`BarterMenu`): not a script's `party.addItem` or the Party tab's | `WidgetBarter::AFunc` (`Trade.NoteFirstPotion`): a potion, with 3612 clear, sets it and shows the help (`WidgetMenuPrompt`) |
| `journal.added` | the newest entry | Trek tells of a new entry (`TrekLoop`): the first tick with no fade, popup or conversation after the network adds one, 3617 cleared first, once however many were added ([flags](flags.md#when-the-network-comes-up-to-date)) | `NewJournalEntryPopup` (`0x80020eb0`) clears 3617 and says the literal at `0x800ce28c` |

The three in-place events are fired through a `FireEvent` that the session hands the pause menu, which hands it to the training and barter windows (`GameSession.FireInPlace`), and that the editor's windows get from their game (`EditorGame.Events`); trek's notice fires on `GameState.Triggers` itself. A window given none, as in the Core tests, fires nothing.

`script.ended` fires for cut-scenes alone: the table is read only by `CutScene.Free`, reached only from `CutScene.ModeFrame` (`0x8005039c`); a conversation's end (`Conversation.End`, `0x80051ccc`) never reads it, and all six of its scripts are cut-scenes.

## The base plugin's

`TriggerImporter` writes them, the endings table's from the ROM (`CodeTable.ScriptEndings`) and the rest as facts checked in 1.0's code:

| Trigger | On | If | Do | The original |
| --- | --- | --- | --- | --- |
| 0 | `game.started` | | `cinematic.play` the opening (`cinematic/0`) | `Title.ModeFrame`, above |
| 1 | `cinematic.ended` 0 | | `script.start` from `aidyn:mapobject/0/I08/7`: script 866 (`0x0362`, number 1004) on its stage | `Cinematic.EndOpening` (`0x8005403c`) loads Borg9 0x0FDB, takes the last script object whose script is 0x0362 (`+0x28`), and calls `Script.Start` with its stage, returning mode 0xC: no trek is built first |
| 2 | `script.ended` 0x04F4 (the Rooughah scene, 1268) | `check.flag` 3442 | `cinematic.play` Roog's (`cinematic/2`) | `CutScene.Free`'s kind 3: `FlagNetwork.Get(0xD72)` at `0x8005179c`, then `Cinematic.Load(2, 3, 1)`; with 3442 clear, the ordinary end |
| 3 | `script.ended` 0x04FE (the last scene, 1278) | | `cinematic.play` the ending (`cinematic/3`) | Kind 1: `Cinematic.Load(3, 2, 0)` |
| 4–6 | `script.ended` 0x0512–0x0514 (the death scenes) | | `game.over` | Kind 2: the return mode 0, the restart at the title (`Game.Restart`, `0x8001f284`) |
| 7 | `cinematic.ended` 2 | | `party.learnTrueName` Alaron | `Cinematic.EndRoog` (`0x80054104`) finds the member whose id is entity DB record 153's, Alaron's, and sets his sheet's True Name bit, then returns trek (the literal 1) |
| 8 | `cinematic.ended` 3 | | `credits.roll` with `ending` | `Cinematic.EndEnding` (`0x800540f0`) sets `Credits.Source` to 2 and returns the credits' mode 0x13; their end (`Credits.Frame`, `0x8001f648`) takes source 2 to mode 0, the title |
| 9 | `spell.trained` | `check.flag` 3611 in 0–0 | `flag.set` 3611, `message.show` the Common string table's line 497, "You've learned a new spell! …" | `WidgetSpellTrain::Purchase` |
| 10 | `item.gained` | `check.subjectKind` `Potion`, `check.flag` 3612 in 0–0 | `flag.set` 3612, `message.show` line 521, "You've acquired a potion! …" | `WidgetBarter::AFunc` |
| 11 | `journal.added` | | `message.show` "A new journal entry has been added.", read from the code segment (`CodeTable.JournalNotice`, `0x800ce28c` in 1.0) | `NewJournalEntryPopup` |

The user played triggers 9–11 on Nov 5, 2026: the first potion's and first spell's help each show once, and the journal notice comes in trek's message box, as before they were triggers.

Every kind of the endings table takes over from trek, so a cinematic after a cut-scene starts straight from its end, with no trek built and none drawn. After Roog's, trek is built again where the player stood and fades in, as after any cut-scene; Alaron's True Name also comes back whenever his sheet is built while 590, that cinematic's done flag, is set ([characters](characters.md)).

**The Shadow's cinematic** stays on the combat record's `shadow` (`GameSession.EndBattle`): it's chosen by how a battle ended, merged, which no condition says, and the record's `shadow` holds the merge's rules with it.

### Where the port differs

- **Script 866 is also trek's.** The script object `EndOpening` takes it from, `aidyn:mapobject/0/I08/7` (Borg9 0x0FDB's object 7) at (12.6, −17.1, 60.3) with a radius of 10, is enabled while flag 5 is clear (derived 5424), and 866 sets 5 as it ends. A new game arrives about 4 from it, so trek there before 866 has played starts it, in 1.0 as in the port: a save of a new game's flags at the arrival plays 866 in trek.
- **A new game builds trek's world before the opening.** The original builds it only after script 866 has ended (`InitGameTrek` runs on `TrekLoop.Frame`'s first frame, with `TrekLoop.Restoring` clear, so the player arrives at the new game's place). The port builds it where the player arrives, plays the opening over it, frees it for 866's stage and builds it again there after: the same place and the same pictures.
- **The ending's credits end the game** by `Transition.GameOver`, which goes back to the title, as after the game over.

## What else could move onto triggers

Each of these needs its event added to `GameEvents` and fired where the original does it ([hardcoded content](../hardcoded-content.md) logs the rest still in code):

- **`camp.started`:** the camp scenes and the Campsite's script (`Camp.cpp`), on the camping record ([camping](camping.md)).
- **`cheat.entered`:** the cheat codes' flags, gold and experience (`CheatCodes.cpp`). The cheats aren't ported at all.
- **`battle.ended`** with its outcome as a condition: the Shadow's cinematic after a merge, and the Goblin Ambush's end, both on the combat record.
- **A conversation's end** as `script.ended`, and its shops, training and battles, which `Transition.AfterScript` decides.

The world map's regions stay off triggers: which flag a chunk sets is the chunk's `region` (layer 1), and when it's set hangs on the sky, the minimap and the zone engine's mode, which no trigger condition reads, so a `chunk.entered` trigger would only restate the field ([ui](pause-menu.md#the-world-tab)).
