# Scripts: the Borg13 VM (roadmap 2.4)

`Aidyn.Core.Scripts`. The format and what the importer writes are in [dialogue](../formats/dialogue.md).

`ScriptRunner` runs one script (`aidyn:dialogue/0x…`) against the flag network and the game state. It ports the walk over the nodes (`DialogNode.cpp`) and the callbacks that carry out the ops (`dialogue.cpp`, `dialogmode.cpp`). Every function was checked against the debug build's machine code, which corrected the decomp in four places (below).

## Its boundary

- **Rules** go to the flag network, and to `IScriptWorld` for the party, inventory, gold, skills, stats and experience. The party and its inventory implement it once 2.2 and 2.3 port them. Where the debug build crashes on a failed call (taking an item the party doesn't have, adding to a full party, giving experience to a non-member), retail carries on, and so does the runner.
- **Presentation** goes out as cues on `IScriptHost` (ADR 0003): cameras, spawning, facing, walking and running, animations, held items, sounds and the fade. The runtime carries them out, and tests record them.
- **The player** answers the page: `Continue` moves on from a line, `Choose` takes a choice, and `Skip` ends the script, as the original's R + A + Z + B does. While the scripted camera moves, the original ignores input, so the runtime shouldn't pass it on.
- **Waits** end with `Resume` from the runtime: an actor arriving, an animation ending or the camera stopping. `Step(ticks)` counts down `Wait`, whose operand is in ticks.
- **Map objects remember** a skill check's result and a random line's roll (the original's `VoxelChart`), through `IScriptWorld.TryRecall` and `Remember`. So talking to someone again can't reroll a check or a rumour. Troubadour checks aren't remembered. The chart itself (19 slots that expire and are saved with the game) comes with the map objects.
- **Randomness** comes from an `IRandom` stream (ADR 0006). `Lcg` is the original's generator, and `Range` is `Random::Range`.

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
- **`CheckSkill`** asks for the party's best level once per script, keeps it, and remembers it on the map object. While the state flags record's `randomSkillChecks` flag (3717, a debug option) is set, it rolls 0–21 instead, after the lookup.
- **`PlayAnimation`** waits for the animation to end. In a conversation, the original looks the actor up from the page it has just cleared (`FUN_800591e4`), so no one animates, and the script waits for the player to press A with the previous line still showing. The runner reproduces this: an `AnimateCue` with no actor, and a `Player` wait. Nine conversation nodes do it, such as the lighthouse keeper handing over his key (Borg13 890 node 13), where the player presses A twice. Cut-scenes only animate actors they've spawned.
- **`Battle`** hands over the enemies from `AddEnemy` (a thirteenth faults), the arena (above 27 becomes 0), the collision and animation bytes, whether the party can flee (not in cut-scenes), and the record's `battleFlag`. In a conversation with someone who wanders, the original fights their party instead, and a shop or training uses them as the keeper.
- **`CampHeal`** heals the party and ends the script, and eight hours pass. A camp scene (`rest`) adds eight more, so a camp heal in one would pass sixteen.
- **`AddMember`** in a conversation tells the runtime that the one talking leaves the map rather than going back to wandering.

## Faults

Where the original crashes, or reads past its nodes, the runner ends the script as `EndDialogue` would and puts the reason in `Ending.Fault`. The debug build sets `borg13End` after its crash message, so this is what it does too. The faults are: a menu or case with no text, leaving a menu or case, a line with no link and no menu to go back to, an If with no link to take, a thirteenth enemy, command 49, a random line from a script that isn't there, and a walk through 10,000 nodes without showing anything.

Playing every base-game script 40 times with random flags, party and choices finds two problems, both in the data:

- **Node 24 of script 1040** (the harbour reunion, id 2192) is Dougal's line, and it lost its link to node 25 (its siblings have one, and an op 2 naming the next node). With Dougal in the party, the original reads a node past the end of the script, and the rest of the scene doesn't play. The runner ends the scene there, setting its done flag.
- **Trahern's conversation** (936) goes round nodes 64–69 forever once flags 67 and 84 are both set. Nothing sets 84, so it can't happen.

## Checked against the machine code

The decomp's pseudocode differs from the debug build's code in four places, all in the walk:

- `dialoug_func_b_check` and `dialoug_func_c_check` read as running every op (`for (…; !done, i < 4; …)`), but the code stops at the first condition or effect, as the data expects. Ops 55, 59 and 60 aren't effects in the jump table.
- An If compares with 1 (`bne v0, 1`), not with nonzero.
- A case counts matches by each link's own range. The pseudocode reads the case node's range.
- The backdrop's choice loop tests the next choice's slot after moving on, so it shows every choice. The pseudocode reads as dropping the last.

1.0's `dialougSkillCheck` was also checked: skill 9, Troubadour, skips the map object's memory.
