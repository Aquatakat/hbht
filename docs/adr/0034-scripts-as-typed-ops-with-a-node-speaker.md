# 0034. Scripts as typed ops with a node speaker

Status: Accepted · Nov 3, 2026

## Context

A Borg13 came into the plugin as a `dialogue` record: 892 of them in one 6.7 MB file, each a graph of nodes whose ops were `{command, value?, target?}` with the original's command numbers. The glossary already called a Borg13 a *script*, but the record type, the class, the map object and the enums said `dialogue`.

The ops were the original's, one int or one record ID each:

- **Actor ops acted on "the speaker",** the entity the latest `SetSpeaker` named. So `SetSpeaker` sat in 15,882 of the 26,684 nodes, over half of all ops, and an op's actor could only be read by walking the graph back to the latest one. Two actors couldn't move in one node.
- **An op needing two parameters had to be split** into ops that set state: `CameraTime`, then `CameraPanTo`.
- **Only the first condition and the first effect of a node run** (`DialogEvalCallback`, `dialoug_func_c_check`). Four nodes have a second effect that 1.0 never runs; 212 nodes' only effect is command 53, which does nothing.
- **The numbers said nothing to a modder:** `CameraAngleTo` on a point pans at a steady speed while on an actor it eases, and `CameraPanTo` the other way round (1.0's jump table at `0x800d1d80`).
- `rest` was written on every script though 9 have it, and `blankNodes`, written on 153, mattered only to the 13 RandomLine ops that show a line of one of 12 of them.

Trigger records (2.10) reused the vocabulary as `{command, value?, target?}` too, and their effects were polymorphic on `type` with an `op` effect holding a Borg13 op.

## Decision

- **`dialogue` becomes `script`:** the record type and its IDs (`aidyn:dialogue/K` → `aidyn:script/K`, the same Borg key), `ScriptRecord`, the map object `script` (its script in `plays`), a monster party's `script`, the enums and the docs. "Lua" stays the word for Lua, and a plugin's Lua files go in `lua/`.
- **One file per script** in the base plugin, `records/scripts/<key>.json`, with a `label`: the original's number and the first speaker's name. Mods may lay their records out any way, as before (ADR 0005).
- **A node names its `speaker`,** who says its line and whose portrait shows. `SetSpeaker` is gone. The importer resolves each node's speaker as 1.0's walk leaves it: the node's own `SetSpeaker`, else the speaker of the nodes that link to it; a menu's choice without one takes the choice before it, since 1.0 sets each choice up in turn as the menu shows. A node whose script names no speaker on any way to it has none, which draws the fallback portrait as before. Where the ways to a line disagree (two lines in 1.0's scripts), the importer has a hand choice, which [scripts](../systems/scripts.md) lists; elsewhere it takes the lowest-numbered node that links to it. Only nodes that show a line carry a speaker. A random line shows under the speaker of the node that asks for it.
- **Ops are typed, with named parameters,** polymorphic on `op` as effects are on `type` (ADR 0020): `{ "op": "actor.walkTo", "actor": "aidyn:entity/0x023F", "mark": 12 }`. [scripts](../systems/scripts.md) lists every op with its parameters and the original's command number, which stays only in the importer and the docs. The camera ops say what the camera does: `camera.cutTo` and `camera.moveTo` move it, `camera.lookAt` and `camera.turnTo` turn it, at once or over time, and `turnTo` is eased unless it's `steady`. An op no build knows loads as a plain op that does nothing, and validation reports it.
- **A node has `setup` (a list), one `condition` and one `effect`,** which is what 1.0 runs. Setup ops run in their order, and the importer writes the ones that set something up for later (the camera's time, the battle's enemies and bytes, the choices reset) first, as 1.0 runs them in a pass of their own. The importer leaves out the four dead second effects and command 53.
- **Actor ops name their actor:** `actor.spawn`, `actor.face`, `actor.walkTo`, `actor.runTo`, `actor.animate`, `actor.hold`, `actor.putAway`, `camera.follow`, and the camera ops on an actor. So do `shop.open` (its `keeper`), `training.open` (its `trainer`), `party.addExp` and `check.stat` (their `member`). The importer fills them from the speaker in force. **A camera op on a mark names no actor:** 1.0 reads the speaker's actor for it only to crash without one, and the port's camera doesn't use it.
- **`wait`** on `actor.walkTo`, `actor.runTo` and `actor.animate`, which wait as 1.0's do unless it's `false`. Camera moves take an optional `ticks`; `camera.time` stays, setting the time of the moves after it, which 1.0 keeps across nodes. The runner still runs a node's effect and then waits for it; parallel ops, and a camera move that waits, can come later in the same shape.
- **Marks:** a reference point's `id` is its `mark`, and the script object's `refPointBase` is `marks`, the number a script's marks count from. It stays a start parameter (`ScriptStart`), since talkers start some scripts at 0 that script objects start at 128 and up. A script names marks, not map objects.
- **Script-level fields:** `rest` is written only where true. `blankNodes` goes: `script.randomLine` takes a `firstLineWeight`, how many chances the target's first line has, 2 for the 13 ops whose target ended with a blank node, which 1.0 counted and showed as node 0. `doneFlag`, `battleFlag`, `backdrop` and `actors` stay.
- **One vocabulary for scripts and triggers.** A trigger record's `if` holds condition ops (with an optional `range`, else they hold at exactly 1) and its `do` holds ops. The trigger-only actions are ops too: `cinematic.play`, `script.start`, `credits.roll`, `game.over`, `party.learnTrueName`. Each op type says where it can run (a node's setup, its condition, its effect, a trigger), and validation refuses the rest by type: the ops that need a stage, an actor or a running script can't be a trigger's, and the trigger-only ones can't be a node's. A `lua` op is reserved in both, as effects reserve `lua`: it loads and does nothing until Phase 5, and loading reports it.

## Consequences

- A script reads as a modder meets it: who speaks each line, what each op does to whom, with no number to look up. A mod's op can move two actors in a node, though the runner still waits on one.
- The speaker is the node's, not a field of the runner that any op left behind. What the runner reported as the speaker at the end is the line's, and a shop's or training's keeper is the op's. Where 1.0 showed the speaker another way would have left it, on a way through the graph the importer didn't pick, the port shows the importer's; [scripts](../systems/scripts.md) lists where that can happen.
- Saves name no script and no node: what a map object remembers of a conversation (its random line's roll, its skill checks) is keyed by the map object, and a running script isn't saved. So a save needs nothing for this change.
- Lazy loading of the script files comes with chunks (slice 4).
