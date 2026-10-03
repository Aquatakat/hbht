# Scripts: the Borg13 VM (roadmap 2.4)

`Aidyn.Core.Scripts`. The format and what the importer writes are in [dialogue](../formats/dialogue.md).

`ScriptRunner` runs one script (`aidyn:dialogue/0x…`) against the flag network and the game state. It ports the walk over the nodes (`DialogNode.cpp`) and the callbacks that carry out the ops (`dialogue.cpp`, `dialogmode.cpp`). Every function was checked against the debug build's machine code, which corrected the decomp in four places (below).

## Its boundary

- **Rules** go to the flag network, and to `IScriptWorld` for the party, inventory, gold, skills, stats and experience. The party ([characters](characters.md)) answers the skill and stat questions; its inventory comes with 2.3. Where the debug build crashes on a failed call (taking an item the party doesn't have, adding to a full party, giving experience to a non-member), retail carries on, and so does the runner.
- **Presentation** goes out as cues on `IScriptHost` (ADR 0003): cameras, spawning, facing, walking and running, animations, held items, sounds and the fade. The runtime carries them out, and tests record them.
- **The player** answers the page: `Continue` moves on from a line, `Choose` takes a choice, and `Skip` ends the script, as the debug build's B does; 1.0 has no skip. While the scripted camera moves, the original drops input (see [cut-scenes](#cut-scenes-m2-session-6)).
- **Waits** end with `Resume` from the runtime: an actor arriving, an animation ending or the camera stopping. `Step(ticks)` counts down `Wait`, whose operand is in ticks.
- **Map objects remember** a skill check's result and a random line's roll (the original's `VoxelChart`), through `IScriptWorld.TryRecall` and `Remember`. So talking to someone again can't reroll a check or a rumour. Troubadour checks aren't remembered. The chart itself, 19 memories that expire and are saved with the game, is `MapObjectMemory` (see [saves](saves.md#map-objects-memory)), and the game state answers for it.
- **Randomness** comes from an `IRandom` stream (ADR 0006). `Lcg` is the original's generator, and `Range` is `Random::Range`.
- **When a conversation ends**, the original reloads the map and gates its objects again by the flags (`loadGameBorgScenes`), so what the script changed shows at once. The runtime will need that with M2's dialogue triggers.

## How it walks

A node is **reached** when the walk arrives at it, and its setup ops run: the speaker from any op, then the settings (camera time, enemies, battle bytes, reset visited), then the cameras and actors. It's **left** when the player moves on from it, and leaving runs its first effect op. Where the walk can go on without the player, through an If or a line with no text, it does so at once.

- **Lines** show their text. Leaving one goes to its link. With no link, it goes back to the last menu or case, which is reached again: its setup and effect run again, and it's shown with the choices not yet taken.
- **Ifs** take their first link when the condition is **exactly 1**, and their second otherwise. The decomp reads "nonzero", but the code compares with 1. Every base-game condition in an If is 0 or 1, so this only matters for mods.
- **Cases** show their text and evaluate their condition. They count the links whose own range holds the value, taken or not. With two or more, the untaken matches are offered as choices. With fewer, the value is kept, and when the player moves on, the first untaken match is taken. The value is kept as 16 bits, and 65535 is the original's mark for none: a single-match case on exactly 65535 gold finds nothing and faults.
- **Menus** show their text and their untaken choices. Each choice is reached as it's added, so its setup ops run then, and a page's choices can have different speakers. Backdrops and the help menu offer taken choices again.
- **Visited** is set when a node is left, not when it's reached. So a choice that ends the script is never marked, and it stays on offer.
- **Effects that end the script** (end, shop, battle, train, camp heal) end it at once. A cut-scene, backdrop or the help menu sets its done flag however it ends, including by `Skip` or a fault.

A page is the line and the choices under it, as the original's `diags` list holds them, with room for 9. The line loses its leading spaces, as `look_for_dialouge_v_Num` trims them, but choices keep theirs. When the first choice has the same speaker as the line, a conversation shows the line alone first and then the choices under Alaron's portrait (`DialougeClass::Setup`); that's for the runtime's UI.

## Ops that need more than a flag

- **`RandomLine`** rolls 0–100, which the map object remembers, and picks a node of the other script by `roll / 100 / (1 / count)` in single precision, where the count includes its blank nodes. A blank node shows node 0's text. Twelve rumour scripts end in a blank node, so they show their first rumour about a third of the time, twice as often as each of the others. The line shows under the node that asked for it, and moving on leaves that node. The other script's text doesn't go through the player-name and escape processing the main script gets at load, but no rumour uses either.
- **`CheckSkill`** asks for the party's skill check once per script (`Party::SkillCheck`, a roll against the party's best: see [characters](characters.md#the-party)), keeps it, and remembers it on the map object. The base game only checks Diplomat, Loremaster and Troubadour. While the state flags record's `randomSkillChecks` flag (3717, a debug option) is set, it rolls 0–21 instead, after the lookup.
- **`PlayAnimation`** waits for the animation to end. In a conversation, the original looks the actor up from the page it has just cleared (`FUN_800591e4`), so no one animates, and the script waits for the player to press A with the previous line still showing. The runner reproduces this: an `AnimateCue` with no actor, and a `Player` wait. Nine conversation nodes do it, such as the lighthouse keeper handing over his key (Borg13 890 node 13), where the player presses A twice. Cut-scenes only animate actors they've spawned.
- **`Battle`** hands over the enemies from `AddEnemy` (a thirteenth faults), the arena (above 27 becomes 0), the collision and animation bytes, whether the party can flee (not in cut-scenes), and the record's `battleFlag`. In a conversation with someone who wanders, the original fights their party instead, and a shop or training uses them as the keeper.
- **`CampHeal`** heals the party and ends the script, and eight hours pass. A camp scene (`rest`) adds eight more, so a camp heal in one would pass sixteen.
- **`AddMember` and `RemoveMember`** add and take away members, with their flags and kept sheets ([characters](characters.md#joining-and-leaving-m2-session-5d)). Retail ignores whether AddMember worked: the scripts check the party's size first. AddMember working in a conversation tells the runtime that the one talking leaves the map rather than going back to wandering ([wanderers](wanderers.md#joining)).

## Faults

Where the original crashes, or reads past its nodes, the runner ends the script as `EndDialogue` would and puts the reason in `Ending.Fault`. The debug build sets `borg13End` after its crash message, so this is what it does too. The faults are: a menu or case with no text, leaving a menu or case, a line with no link and no menu to go back to, an If with no link to take, a thirteenth enemy, command 49, a random line from a script that isn't there, and a walk through 10,000 nodes without showing anything.

Playing every base-game script 40 times with random flags, party and choices finds two problems, both in the data:

- **Node 24 of script 1040** (the harbour reunion, id 2192) is Dougal's line, and it lost its link to node 25 (its siblings have one, and an op 2 naming the next node). With Dougal in the party, the original reads a node past the end of the script, and the rest of the scene doesn't play. The runner ends the scene there, setting its done flag.
- **Trahern's conversation** (936) goes round nodes 64–69 forever once flags 67 and 84 are both set. Nothing sets 84, so it can't happen.

## Cut-scenes (M2 session 6)

`CutScene`, `CutSceneActor` and `CutSceneStage` in `Aidyn.Core.Scripts`, with `ScriptCameras` in `Aidyn.Core.Cameras` and `ScreenFade` at the core's root. Read from 1.0's machine code on Oct 2, 2026 by three Sonnet subagents, with the jump tables, fades, the walk's limits and the facing snap spot-checked. The decomp's `dialogmode.cpp` and `dialogue.cpp` are a fair map; where they're wrong is noted.

### The world it plays in

- **Starting.** A dialogue object (walked into or used) or a talker whose script is a cut-scene calls `dialoug_func`, and `funcs0[1]` (`0x80051e04`) starts trek fading out, at 1/30 a tick (`FUN_80024c54`). Trek runs on under the fade, clock and wanderers too, but the player's input is dead. Once the screen has been black three frames, trek frees its world (`clear_HUD_elements`): the chunks, the wanderers and the player's body, keeping where the player stood, his facing and the camera's place.
- **The stage** is the dialogue object's staging chunk: every one of the 271 cut-scene objects has one. A talker's cut-scene stages in **the chunk the player is in** (`monsterpary_func` passes `gameVars.mapShort1/2`), with reference points from 0, and sets the party's flag B. `DialogueModeInitPrescripted` (`0x800515a8`) loads the 3×3 around it, gated by the flags as trek does, **without monster parties' bodies** (`look_for_monsterparties` returns in the dialogue state, `0x8000ef20`), and puts the camera at the stage's corner looking along X. Then the script's first nodes run, which spawn its actors and cut the camera (every cut-scene's first camera op is a cut), and the screen fades in over a second, 1/60 a tick.
- **Reference points** are looked up by id in the staging chunk's map alone (`FindReferncePoint`, `0x8000b6f0`), and nothing checks for a miss: the original reads address 0. Every point the 271 objects' scripts name is there. Nine of the 45 talkers' cut-scenes name points their party's chunk lacks, though the player may stand in a neighbour as he talks; the port notes a miss in `CutScene.Problems` and skips the op. Keelin's `0x0382` walks to point 34, which no chunk of Terminor has.
- **Each tick** (`Dialog_GameState` state 1): the actors within 30 of the camera's aim move and animate (`ProcessPlayers`), then the script's cameras; the sky, clouds and weather go on, but **the clock stops**, and map objects, trigger triangles and chunk streaming don't run (`handleZoneEngineFrame` with no player). The near and far planes are 0.1 and 125 rather than 1 and 180 (`RenderZones`, `0x8000d66c`).
- **The box** is the conversation's (`DialougeClass`), and the player's A moves it on, but presses are dropped, not kept, while the screen fades, while the script waits (a walk, a timer, an animation or the camera: each of those also frees the box, so it disappears) and while a camera move runs (the box stays up). Each press the script takes asks the speaker's actor for request 4, talking, unless the script played an animation (`funcs2[1]`, `0x80051ed4`).
- **No skip.** 1.0 has no R + A + Z + B: `cutScene_control_func` (`0x80051378`) only tests B, which toggles a flag nothing reads.
- **The end** (`0x80051f4c`): the done flag is set, and the screen fades out over a second while actors and cameras carry on, then three frames at black. Every actor is freed (`DialougFreeActors`), the stage goes, and trek starts again where the player stood (`initGameTrek`): every map object gated again and every wanderer at its home node, the camera at its old place aimed at the player and swinging round quickly, and the screen fading in at 1/30. Three scripts end the game and two lead to the cinematics (`borg_short_list`, `0x800DFE1C`: `0x0512`–`0x0514` game over, `0x04F4` Roog's true name with flag 3442, `0x04FE` the ending and credits); the runtime doesn't play those yet. A check after the end for a tangible dialogue object at the player's place, which would chain into another cut-scene, only runs where the saved place's `mapDatB` isn't −1, which ordinary play never leaves, so it isn't ported.
- **In the runtime** (`Game.CutScene.cs`), `--cutscene <id>` plays one at once, as the first dialogue object or talker that starts it would.

### Actors

- **Spawning** (`CreateActor`): a body of the speaker's entity (its actor, radius, scale and height), set down at the reference point by one sweep of its sphere from 100 above to 100 below (`Actor::CheckCollision(p, 0, 0, 0)`, `0x80011a64`), resting 0.01 above what it hits; the point's own height doesn't count. On a miss it tries four times more, a little to the side (`Body.SetDown`). It faces the original's default, (−0.71, −0.71), and is kept 0.05 inside the staging chunk. The original's flags on it: `ACTOR_1000`, whose only reader is `SetFacing` (`0x80015910`), so **its first turn snaps**, however late it comes, and keeps any goal (`Body.SnapNextFace`); and `ACTOR_400`, which keeps it out of the push loop, so actors don't push each other. Spawning one again leaves the first body standing, as the original's pointer is overwritten. Alaron is in a cut-scene only if the script spawns him.
- **Turning** (`FaceRefPoint`) to a point, 10° a frame where it stands after the first; nothing waits for it, but a turning actor counts as steered (below).
- **Walking and running** (`WalkTo`, `RunTo`): `SetAiDest` to the point's X and Z, within **1**. The script waits, and **a walk's wait ends when no actor has been steered for 30 ticks** and no camera move runs (`ProcessPlayers`, `0x800136c4`): there's no "arrived". An actor beyond 30 of the camera's aim isn't stepped, so it never walks and the wait ends 30 ticks on. An actor that comes no nearer for **300 ticks** is put at its goal and set down (`MoveTo`, `0x80015a7c`, `FUN_80019b08`; 30 in combat, never in trek): the docs once had 30 frames here, which was the wait's rule. While the screen fades, a steered actor stands, though its steering, and so its count, goes on (`Actor::Move`, `Body.Faded`).
- **Animations** (`PlayAnimation`): the speaker's actor is asked for the request (`ApplyStimulus`), which it keeps for the original's frame and the next, then stands (`FUN_80059b50` asks every actor to stand each frame unless the script animated it). A request that doesn't interrupt its state so never plays. **The wait is a timer**: after the tick it's seen, it lasts the length of the state the actor is then in (`FUN_80058ad4`, `aniLength`), counted from that tick. With no actor on the stage (its spawn missed), nothing waits.
- **Held items** (`ShowItem`, `HideItem`): the item's model (`get_item_borg5`, imported as `Model` on weapons, shields and six items: see [db](../formats/db.md)) on the speaker's scene's locator 0, with no offset, scaled with the body, drawn even while the body is hidden. An item without one, such as the two spells script `0x0434` shows, leaves what's held as it is. The goblins of the ambush (`0x0365`) hold their short swords this way.
- **The fade op** (`FadeOut`) dips: down over a second and back up over another by itself (fade mode 3), while the script carries on. No cut-scene uses it.
- **Command 53**, once near the start of every cut-scene, does nothing: it only takes its node's effect slot.

### In the port

`CutScene` takes the script, its start and its `CutSceneStage` (the staging chunk's map set, chunk, map and the loaded collision), and carries out each cue on the stage as it comes. What a wait waits for is a state of the stage, the actors steered or the cameras moving, not of the cue that started it, so scripts could share a stage and run side by side. The runtime calls `Start`, `Press` with what A does, and `Tick` with each actor's animations (`IActorMotion`: root motion and the state's length), and brings trek back once it's `Over`. Real data: every cut-scene a dialogue object or a talker starts, 305 in all, plays to its end in a new game taking the first choices; the only problems are the talkers' missing points.

## To check in the emulator

Two things the runner reads from the code that the user hasn't seen play yet (listed in [to-check](../to-check.md)):

- **The lighthouse keeper handing over his key** (Borg13 890 node 13). The runner has "Here... take the key" need A twice, with no animation, since the original looks up who animates from the page it has just cleared (`PlayAnimation`, above). If the keeper does animate, the lookup is wrong, and so are the other eight nodes that do this.
- **Dougal at the harbour reunion** (script 1040). With Dougal in the party, the runner ends the scene at his line (node 24), where the original reads past the end of the script. What the console does there, whether it ends, hangs or plays something else, decides what the runner should do.

## Checked against the machine code

The decomp's pseudocode differs from the debug build's code in four places, all in the walk:

- `dialoug_func_b_check` and `dialoug_func_c_check` read as running every op (`for (…; !done, i < 4; …)`), but the code stops at the first condition or effect, as the data expects. Ops 55, 59 and 60 aren't effects in the jump table.
- An If compares with 1 (`bne v0, 1`), not with nonzero.
- A case counts matches by each link's own range. The pseudocode reads the case node's range.
- The backdrop's choice loop tests the next choice's slot after moving on, so it shows every choice. The pseudocode reads as dropping the last.

1.0's `dialougSkillCheck` was also checked: skill 9, Troubadour, skips the map object's memory.
