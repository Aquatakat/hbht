# Aidyn Chronicles Reimplementation: Roadmap

Sep 26, 2026 · Updated Nov 11, 2026

## Summary

A PC reimplementation of Aidyn Chronicles: The First Mage that runs on content imported from a user-supplied ROM, with a plugin-based mod system on top. Phase 0 laid the foundations, Phase 1 decoded the formats in parallel tracks, and everything since lands as playable vertical slices on a headless rules core.

**Quest logic is almost entirely data, not code.** The game runs on three data systems: an event-flag logic network (`GameStateFunnel`, [flags](systems/flags.md)), a dialogue and cut-scene bytecode (Borg13, [scripts](systems/scripts.md)), and map objects gated by flags (Borg9, [map-objects](systems/map-objects.md)). So extracting quests is a file-format problem. The code-lifting work is in **combat formulas, AI, and the places where engine code hardcodes content** ([hardcoded-content](hardcoded-content.md)). The game's content is already keyed records in tables and one big asset archive ([rom](formats/rom.md)), which makes a Bethesda-style mod system a natural fit.

These systems are ported from the machine code rather than parsed from data: combat (the debug build's `combatengine/combat.cpp`, `combatEntity.cpp`, `turn.cpp` and `src/gamecombat.cpp`), its AI (`combatengine/aiInfo.cpp` and `aiscores.cpp`), character and party logic (2.2, 2.3), movement and physics (M1), wanderers (M2), and the hardcoded content (1H). The debug build "02.01d-PRERELEASE" names the original's source files and some functions, which the port uses (0.9).

## Where things stand

This is the one list of what's open. A task's status is in its phase's table below, and what was found and built is in the docs. When something finishes, take it off here, mark its task and suggest what's next. When a playtest or a finished task leaves work behind, add it here. What only the user can check is in [user-checks](user-checks.md).

### Next

**M4: the prologue** is done: the user played it side by side with the emulator, what differed is fixed, and the user has played the fixes. Next, by the phases' order, is **Phase 4: content parity**, starting with [the playthrough bot](#the-playthrough-bot), which plays the game headless to a goal so one tester can check a full game.

### Left over from finished work

- **The menus' mouse:** the mouse choosing a row, and on the World tab ([pause menu](systems/pause-menu.md)); the user has set it aside for now. The menus' motion is otherwise 1.0's: their slides, fades and scroll, a battle's pause on a wide screen and the details' edge.
- **Content still in code:** the [hardcoded content](hardcoded-content.md) log, planned in [Phase 5](#phase-5-mod-system-and-tools). Among it, a mod's own skills, stats and weapon skills load and validate, but the core holds the original's 24 skill slots and 7 stats, so a mod's isn't played (ADR 0032).
- **2.10:** what else could move onto triggers, each with its event ([events](systems/events.md#what-else-could-move-onto-triggers)): the camp scenes (`camp.started`), the Shadow's cinematic and the Goblin Ambush's end (`battle.ended` with its outcome), and a conversation's end as `script.ended`. The editor has no view of the triggers of its own, and the quest atlas doesn't show what they read and set.

### Planned

- **Combat Turns: Start Early,** a setting that starts the next combatant while the last action is still being drawn ([combat](systems/combat.md#drawn-from-a-recording)). The core plays the AI's turns ahead in Original's order, and only the drawing overlaps. Four stages were done on Nov 7, 2026:
  - the battle asks the view nothing that changes its ticks;
  - it's drawn from a recording (`BattleRecorder`, `BattleFrame`, `BattleDrawing`);
  - the AI's turns overlap where they share no bodies or paths (`BattleActions`, `BattleComposer`);
  - the player's turns open early.

  The five `StartEarlyTests` battles draw up to 59% shorter than Original, and their rules and random numbers stay Original's tick for tick. The user played it: no one walks through anyone, the sounds and health bars are right, and it's faster than expected, perhaps too fast, which piece A slows a little. The rest is in three pieces. A and B touch different code and can run side by side; C needs both.
  - **A. Start Early's pacing**, in the composer. Done Nov 7, 2026 ([combat](systems/combat.md#pacing-beats-and-the-players-wait)): the five battles now draw 22–56% shorter than Original. The user played it with C's director and found it right (Nov 11).
    - **Beats.** A result is a beat: damage, a miss or dodge, a defeat, or an effect added (a buff or debuff, such as Mirror, Weakness or Haste). It runs from its strike to the end of its reaction.
      - Beats may play at once as long as one shot can hold them all. The user expects that's usually possible with shots the original never takes: a low shot across the field from one end, or a top-down one.
      - Where no shot can hold them, the later action starts later, so its beat lands once the camera is free. Its walk still starts early where it can.
      - Whether beats share a shot is C's question to answer. Until C, a stand-in answers it.
    - **A player's turn waits for the results before it.** It opens once every beat before it has played, or can stay in shot alongside the player's camera. An enemy's turn may still start early, especially its walk.
    - **Holding the player's control.** Control comes at the first tick from which the party member can't touch any enemy still walking. Work out how far the party member could have got by each tick, at its greatest travel a tick, kept inside its movement ring. That reach must never come within the enemy's radius plus a buffer scaled by the enemy's size, over the whole of the enemy's drawn walk.
      - The enemy's places are the recording's, so its speed never comes into it.
      - Aiming is held the same way. A skips the wait by fast-forwarding whatever holds it.
      - This replaces stage 4's fast-forwarding of the bodies the player's walk comes near.
  - **B. Camera: Enhanced**, a setting of its own on the Enhancements page (Original or Enhanced; it may carry trek's camera features later). It's for turns played the Original way. **Done on Nov 7, 2026**, awaiting the user's play: [cameras](systems/cameras.md#the-enhanced-battle-camera-camera-enhanced) has how each of the below is done, its numbers and what C can ask of it. Under Original its movie is pixel-identical, and with the same presses the rules play the same tick for tick under either camera. Under Start Early the row reads Enhanced, as C's director is. It's a camera in the core, not just in the drawing, because a turn's stick and its area placement are read against the camera's look (`Battle.Turns.cs:107`, `Battle.Spells.cs:420`).
    - **Reshaped from the user's first play, Nov 7, 2026.** The first version targeted party members too, framed from high and far, moved wildly fast, switched about through enemies' turns, and zoomed far out to aim with the reticle under the UI. What the user asked for instead is below, and done the same day, awaiting their play again.
    - **Reshaped again from the user's second play, Nov 8, 2026:** the walk's motion halved, the cone 20° either side, the target held a while after it leaves the cone or the range, never a cut while the player walks, the target's pack framed whole, a spell aimed at targets showing them all and some of its ring, no overshoot, and the player's own actions planned like the AI's.
    - **Reshaped a third time from the user's third play, Nov 8, 2026:**
      - no one framed under the conversation box;
      - the view quicker as the character nears its edge, and carried with them as they walk;
      - the player's turn low and near the character, never from high above, holding the pack only while it's near;
      - the target held while the character runs round its pack;
      - one vantage for the battle setting every shot's side, in place of each party member's own;
      - a spell's aim low at its targets, without the caster;
      - an arrow, a throw or a spell at someone further off shown as the loosing and then a cut to the hit.
    - **Reshaped a fourth time from the user's fourth play, Nov 8, 2026,** done the same day and awaiting their play:
      - the turn twice as quick, and turned with the character as they walk round the target, so a new shot's angle arrives with its place;
      - an area's ring framed down to the conversation box;
      - a turn opening on the enemy ahead or the nearest, held till the character moves;
      - with no enemy ahead, the view trailing the character on a leash as a third person's camera does, its aim leading them (the user's correction: a view kept still felt stiff);
      - the orbit round a pack continuous, a new framing eased onto rather than stepped to.
    - **Reshaped a fifth time from the user's fifth play, Nov 8, 2026,** done the same day and awaiting their play:
      - the camera following a point drawn smoothly after the character, so their stride doesn't jolt it;
      - turned over the shoulder at the target, not its pack's middle, within 45° of straight behind, and never turning itself round to a walker's front;
      - a fast pan into, about and back from an aim rather than a cut;
      - a spell at an ally shown landing on them however near;
      - an aim at a blow keeping the shot that already holds both;
      - an AI's walk followed.
    - **Reshaped a sixth time from the user's sixth play, Nov 8, 2026,** done the same day and awaiting their play:
      - each target of a spell that takes many shown close in its turn, the caster among them;
      - an AI's walk, and its flight, shown from near where it ends, the eye moving a little and the view turning after the walker;
      - over the shoulder, no hard turn to a new target in the pack or after one let go: the view's turn speeds up and slows under the cap, and a framing is eased onto the same way, the shot turned with it.
    - **Reshaped a seventh time from the user's seventh play, Nov 8, 2026,** done the same day and awaiting their play:
      - what the camera is trying to do drawn under F3: the bodies it holds ringed, the lens's frame, and a map from above of its eye, its goal's and the vantage;
      - in the player's turn, the goal glides after the shot found, its place, speed and acceleration continuous, and the camera rides it, rather than its caps halved for the walk and doubled near the screen's edge; the turn carried smoothed, a pack faded in, the shot coming in slowly, a target let go held half a second, and one lead from the character;
      - with no enemy ahead, the aim leading the character by how fast they go, on them standing;
      - an AI's walk's shot riding a rail laid along its way, by how far along it the walker is, smoothed, so their stride and a wander don't shake it.
    - **Reshaped an eighth time from the user's eighth play, Nov 8, 2026,** done the same day and awaiting their play:
      - over the shoulder, a view that shows the character and the target kept however far off the middle, a new framing found only once one leaves the frame or has been hidden three quarters of a second, and swung onto over two seconds along a minimum-jerk curve; the trailing shot's clearing of a hidden character swung onto too, and F3 saying why it swung;
      - among a pack within 5, the target the nearest, and the shot not orbiting it; a new target taken in a quarter of a second within 10, and in up to a second and a half across the field;
      - a turn opening more than 30° round from the shot shown cut to rather than glided onto.
    - **The player's turns:** only enemies are targets: the nearest one the character is heading for, in a narrow cone, held stickily and while the character is near it, with its pack. The shot is low over the shoulder and near the character, the character never under the conversation box. A body counts as seen while its upper half is. Among a pack within reach the target is the nearest. The shot moves and turns with the character, a view that shows both kept however off the middle, a new framing swung onto in about two seconds, and never cuts. A turn opens on one shot, held till the character moves. With no enemy ahead it trails them as a third person's camera does, its aim leading them by how fast they go; round a pack it orbits with them, never stepping, never turning hard and never round to their front, following a point drawn smoothly after them.
    - **Actions, the AI's and the player's:** never switching about. The walk and target are known once it acts, so one shot holds all of it, chosen to disrupt least from the last player turn's shot and toward the next one's. An AI's walk or flight is shown from near where it ends, turning after the walker; the player's character walking is carried along with.
    - **Sides:** one vantage for the battle, off to one side of the line between the party and the enemies, and every shot frames each party member and their enemy the way round it sees them: film's line of action. A reserve coming on, or the fight turning round, places it again.
    - **Ranged actions:** an arrow, a throw or a spell at someone further off is two shots, the loosing over the shoulder and a hard cut to the hit.
    - **Aiming:** from above as before, but tighter, with the reticle in the middle of the screen and the camera following it. A spell aimed at targets low from the caster's way, its targets all in view and some of its ring where it fits. An area spell (Fireball, which hits the party too) from straight above, its ring down to the conversation box.
    - **The player's hand on the camera:** once the player turns the camera themselves in their turn, the director lets go, and Original's camera goes on until they aim or the turn ends.
    - **Motion:** cap the camera's acceleration rather than its speed, much lower than the first version's, trail a moving goal rather than overshoot it, and cut a long way rather than swoop.
    - **Where it may go:** the arenas are ordinary world chunks; the indoor ones have no ceilings and draw a black sky. So the camera may go anywhere on the field, but not beyond it: the walls and cliffs round it are scenery without collision.
  - **C. Start Early's director.** Under Start Early the camera is always B's Enhanced camera, whatever the Camera setting says. It builds on B. **Done Nov 9, 2026**, awaiting the user's play ([cameras](systems/cameras.md#start-earlys-director)): B's camera reads a scene, the live battle's or the drawing's, and the director films the drawing with it, beats held besides, planned toward early and cut to if need be; its answer to A's shot question lets the casters and the archers overlap more (31.8% and 32.2% shorter than Original); and in the player's turn its camera is handed to the battle, so the stick reads what's drawn. The user's first play (Nov 10) brought the outcome hold (a result held a second, results of different actions a second apart), steady planning (two shots had alternated every tick), walkers kept in frame, the lead resting as a character stops, and a spell's hits on all its targets at once, in one shot (the casters 40.2% shorter than Original); done Nov 10. Their second play (Nov 11, recorded with F3 on) found the view going past its shots and coming back, which was three faults of its motion (a shot kept where the camera was while it moved, a turn's cut dropped in a hold, a glide ridden faster than the caps stop), a far walk's wide shot given up early for the walker's own result, a notch of a zoom, and a wobble as enemies walked; fixed that day, with the caster of a spell at several targets facing the middle of them. Their third play (Nov 11) found it right throughout, its numbers as they stand ([cameras](systems/cameras.md#the-users-third-play)).
    - **Beats come first.** Damage taken or an attack missed is never off camera. It frames beats that play at once, as A asks, and answers A's question of whether a shot can hold them.
    - **It plans ahead from the recording.** The beats to come are already recorded, so under the acceleration cap it can start moving early and arrive as a hit lands.
    - **The player's turn** shows the character, their targets, and any beats still playing.
    - **Aiming** works as in B, zoomed out to hold whatever is happening.
  - **The stage 5 draft** (branch `claude/start-early-s5-19a03d`) isn't to be merged as it is. B and C took its useful parts:
    - its occlusion scoring, by the arena's collision and by bodies;
    - the tuning record and `--battle-camera`;
    - F3's camera line;
    - the sounds' volume and pan by the drawn camera.
  - **The log** is hard to follow under Start Early; the user is thinking about what could replace it.
  - **After the pieces: throws and missiles feel late.** The user played stage 1 and found the ambient sounds, the music and the casting sounds right, but a throw's or a shot's timing late. Whether stage 1 moved it, or it was already late against 1.0, isn't checked. To tighten afterwards, the user's choice.

## Phases

Sizes are relative: S, M, L, XL. **Bold** tasks block other work. Phase 0 settled how the reimplementation is built, in the binding [ADRs](adr/README.md).

### Phase 0: Foundations

| Task | Size | Output | Status |
| --- | --- | --- | --- |
| **0.1 Reference rig.** An emulator with a debugger, the NA 1.0 and debug ROMs, and save states at story checkpoints. | S | [reference-rig](reference-rig.md) | Done |
| **0.2 ROM identification.** Hashing, byte-order normalisation, header parsing, and a per-version offset table. | S | `RomImage` | Done |
| **0.3 Borg archive reader.** `Borg_List` and the LZB and LZ01x decompressors. | M | Unblocked Phase 1 | Done |
| **0.4 DB table readers,** cross-checked against AidynEditor. | M | Typed records | Done |
| **0.5 Architecture decisions** as ADRs. | S | `docs/adr/` | Done |
| **0.6 Asset inspector,** which becomes the editor over imported data ([ADR 0010](adr/0010-editor-reads-plugins.md)). | M, ongoing | [editor](systems/editor.md) | Done Sep 30, 2026; grows into the mod editor (Phase 5) |

#### Reading the code without the decomp

[ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md) stops the project reading the decomp. These tasks give the rig what the decomp gave: a way to find a system's code, names for it, and the layout of what it touches. The [reference rig](reference-rig.md) says how to use each. Anything derived from the ROMs goes to the gitignored `out/`, except the symbol map, which holds only addresses and our names.

| Task | Size | Output | Status |
| --- | --- | --- | --- |
| **0.7 Code index.** Every function, its callers and callees, the data and strings it references, and the jump tables. | M | Finding code by what it touches | Done Oct 4, 2026, as an export from Ghidra (0.14) |
| **0.8 Symbol map.** `tools/symbols/`, our names for addresses, which every research run adds to. | S, then ongoing | Calls named in every disassembly | Seeded Oct 4, 2026; ongoing |
| **0.9 Source files and the two builds.** Tie the debug build's functions to its source files, and match 1.0's functions to the debug build's so names carry across. | M | `tools/builds.py` | Done Oct 4, 2026 |
| **0.10 RAM maps.** `bst.py --diff` between two checkpoints. Structures' fields go in their system's doc. | S | Where a value lives | Done Oct 4, 2026 |
| **0.11 gdb on a checkpoint.** | S–M | Watchpoints from a checkpoint | Done Oct 4, 2026: `tools/gdb.ps1`, 1.0's checkpoints only ([rig](reference-rig.md#gdb-on-a-checkpoint-toolsgdbps1-roadmap-011)) |
| **0.12 Traces.** Which functions run during an action, against standing still. | M | The code an action runs | Done Oct 4, 2026: `tools/trace.ps1` |
| **0.13 Combat captures** (the user). Checkpoints in and around battles, from an unmodified 1.0 game: on the field just before one, its first turn, before and after a melee attack, before and after a spell, an enemy's turn, fleeing, and victory with its loot and experience screens. | S | Combat's RAM, before and after each thing it does | Done Oct 18, 2026: 14 checkpoints of one battle, from the field before it to the loot |
| **0.14 A decompiler of our own.** Ghidra headless over 1.0, with the symbol map applied. | S | Readable pseudocode | Done Oct 4, 2026 |

### Phase 1: Format decoding

Each track ends with an exporter to an open format and a viewer in the editor: if it round-trips visually, it's understood.

| Track | Tasks | Size | Validation | Status |
| --- | --- | --- | --- | --- |
| **1A Static graphics** | Borg1 textures, Borg8 UI images, Borg2 via a minimal F3DEX interpreter, Borg3 cameras, Borg4 lights, Borg5 scenes | L | glTF export of a building and a character matches the emulator | Done |
| **1B Animation** | Borg6 tracks, Borg7 actors, Borg5 particles | L | The opening cinematic plays in the editor | Done Sep 27, 2026 |
| **1C Maps** | Chunk tables per map set, Borg9 collision and map objects, Expansion Pak variants | L | A map viewer shows a whole map set with its objects | Done Sep 27, 2026 |
| **1D Audio** | Borg11 PCM, Borg12 sequences and their playback, the audiokey table | M | BGM plays correctly | Done Oct 10, 2026; what isn't played yet is in [audio](systems/audio.md) |
| **1E Quest atlas** | Borg13 decoding with its branching, the flag network, flag cross-references, journal linkage | L | What sets and reads every flag, and which journal entry it maps to (`hbht flag <n>`, and the editor's Atlas tab) | Done |
| **1F Text & UI assets** | String tables, the fonts, portraits, menu images | S | The dialogue box renders in the right font | Done |
| **1G Saves** (optional) | Importing the original's Controller Pak saves | M | An original save imports | Done Oct 2026: `hbht save from-pak` ([saves](systems/saves.md#the-originals-save)). No button in the runtime or the editor, the user's choice: emulators keep their paks in their own ways. |
| **1H Hardcoded content** | Every piece of content in code rather than data, each assigned an [ADR 0008](adr/0008-content-logic-layers.md) layer | M | [code-tables](formats/code-tables.md) | Done Oct 19, 2026: every content table is read or carried; what the port's code still holds is the [hardcoded content](hardcoded-content.md) log |

### Phase 2: Rules core (headless, test-driven)

Where a task names the original's code, the file names are the debug build's.

| Task | Size | Source | Status |
| --- | --- | --- | --- |
| **2.1 Flag engine.** `ProcessGameStates`, plus the time, calendar, weather, terrain and stat flag setters. | M | `gamestatefunnel.cpp` | Done |
| **2.2 Characters.** Attributes, skills, levels, hit points, experience, training costs, modifiers, resistances. | M | `gameclasses/` | Done Sep 30, 2026 |
| **2.3 Items & inventory.** Equip rules, shops, chests, crafting. | M | `data/itemdb.cpp`, `chestdb.cpp`, `shopdb.cpp` | Done Oct 1, 2026 |
| **2.4 Borg13 VM.** Camera and actor cues go out through a host interface, so the core stays headless. | M | `dialogue.cpp` | Done |
| **2.5 Combat rules.** Turn order, field-of-influence radius, hit, damage and crit, spells (targeting, radius, reagents, battery), status effects, reserve spawns, leader and flee logic, rewards. | **XL** | `combatengine/combat.cpp`, `combatEntity.cpp`, `turn.cpp`, `src/gamecombat.cpp` | Done Oct 19, 2026, with M3 ([combat](systems/combat.md)) |
| **2.6 Enemy AI.** | L | `combatengine/aiInfo.cpp`, `aiscores.cpp` | Done Oct 19, 2026, with M3 ([combat](systems/combat-ai.md)) |
| **2.7 Time & camping.** See [camping](systems/camping.md). | S | `sundial.cpp`, `menus/campmenucontroller.cpp` | Done Oct 18, 2026 |
| **2.8 Save/load** in the engine's own format. See [saves](systems/saves.md). | S | `questdata.cpp`, `saveParty.cpp` | Done Oct 1, 2026 |
| **2.9 Differential tests.** Capture the random values the emulator consumed (ADR 0006), replay them into the core, and compare damage and turn logs. | L | | Done Nov 7, 2026: the Combat checkpoints and 13 played setups replay into the port, every formula family's vectors agree with 1.0's functions, and every difference left is the logger's cadence, a fix or a place left unchecked, by the user's choice ([combat parity](systems/combat-parity.md)) |
| **2.10 Events and triggers.** The core's named event list, and trigger records (*on event, if condition, do effects*) in Borg13's condition and effect vocabulary. Engine features that replace bespoke code (ADR 0008 layer 3) land with their system. | M | The 1H audit | Done Oct 19, 2026, with the cinematics' ([events](systems/events.md)); what else could move onto them is [above](#left-over-from-finished-work) |

### Phase 3: Godot runtime

A series of vertical slices, each one playable ([runtime](systems/runtime.md)).

| Slice | Contents | Needs | Status |
| --- | --- | --- | --- |
| **M1: Walk Gwernia** | The chunk streamer, terrain, the N64 material, collision, the player, the follow camera, the sky, draw distance | 1A, 1C | Done Sep 29, 2026 |
| **M2: Talk and trade** | Map objects, wanderers, conversations, shops, the pause menu, the journal, cut-scenes, training, camping, the HUD | 1E, 1F, 2.1–2.4 | Done Oct 4, 2026; leftovers [above](#left-over-from-finished-work) |
| **M3: Fight** | Arena loading, spawns, the turn UI, radius targeting, spell visuals and particles, the combat log, status icons, post-battle XP and loot screens | 1B, 2.5, 2.6 | Done Oct 19, 2026 ([combat](systems/combat.md#the-port)); leftovers [above](#left-over-from-finished-work) |
| **M4: The prologue** | From the opening through leaving Gwernia, end to end | Everything above | Done Oct 22, 2026, after the user's side-by-side playthrough; what it left is [above](#next) |
| **M5: Full playthrough parity** | Every region, every quest and the ending, checked against the quest atlas | Phase 4 | |

### Phase 4: Content parity

Mostly testing rather than engineering (XL).

- Walk every region against the emulator and log differences.
- Use the quest atlas as a checklist: every flag reachable, every journal entry obtainable. Every flag that's used and can change has a name ([flags](systems/flags.md#labels), Nov 10, 2026): the 1,330 the map objects write were named from them and the places of their chunks (`ChunkPlaces.tsv`, which the user went over), and the other 899 were drafted from their scripts and restyled by the user's edits, the user's pass over the story flags under way. The 110 flags play never sets go unnamed: 49 something reads but nothing sets, and 61 whose setters the static pass shows can't run. Eleven journal entries are never obtainable in the original.
- Rules parity: shops, trainers, loot tables and camp encounters for each area.
- Decide which of the original's bugs to keep or fix, one at a time and with no toggle ([ADR 0018](adr/0018-the-port-fixes-bugs-and-sets-enhancements.md)), such as the `CheckPartyGoldU16` truncation and the crash when `TakeItem` runs on an item the party doesn't have. (The spell auras' turn cycle is settled: the original's by default, each character's as the `SpellAuras` setting.)

#### The playthrough bot

`hbht playthrough`: the game played headless from a start to a goal, reporting where plan and game part ways, with saves along the way to play from by hand ([playthrough](systems/playthrough.md)). Started Nov 9, 2026, built slice by slice by subagents; a slice is done when its tests pass and it's merged into main.

| Slice | Size | Status |
| --- | --- | --- |
| Planner: actions, preconditions and effects over `QuestIndex`; the static reachability pass; steps to a goal (`QuestPlanner`) | L | Done: 7,705 actions over 4,086 of the real runner's ways; from a new game the ending and all 13 members are reachable; plans take every place as reachable until the run joins the world graph to them |
| World graph: `Reach`'s cells per map set joined by teleporters, with their gates; routes to a map object (`WorldGraph`) | M | Done: 17 map sets in about 86 s, cached; 894 teleporters, 96 gated; routes walked with the real `PlayerMotion` |
| Headless host: every `Transition` carried out as the runtime does, a new game played to trek with no input (`PlaythroughHost`) | M | Done: a new game reaches trek at I08 in 988 ticks with nobody at the controls; policies for input, pages, battles and menus |
| Pilot: a route walked by the stick, an object used, a conversation's choice picked, the step checked (`Pilot`) | L | Done: from a new game it walks through Edur's cut-scene and the Goblin Ambush to Oriana's hut, keeping clear of other cut-scenes' objects where it can |
| Battles: the party played by the combat AI, a loss replayed and won outright, deaths, levelling builds and trainers (`BattlePilot`) | L | Done: the AI plays the party, a loss is rewound and won outright (`LostFirst`), stalemates routed, deaths free the slot, experience spent by a build table through the real windows |
| The run: goals with the party member's supersedes, replanning, saves at milestones, the report, `hbht playthrough run` (`Playthrough`); the world graph behind the planner's `IPlaceReach`, with a place's closed gates (`RouteRefusal.Blockers`) turned into preconditions so plans go through the doors they need opened | M | Done: from a new game the member goal leaves Oriana's hut through flag 50 and Abrecan joins in 4 steps; the party is full in 6 |
| Collision toggles in the walk graphs, gated by their flags: 0/P24/1 (flag 480) is the only way onto the south-east island, which every plan to the ending needs, and the run stops short of it | M | Done: the nine `changeCollision` tripwires' ground joined by gated steps; a route crosses the Jundar gate with 480 set and names it without |
| A full pass from a new game to the ending, and what it finds | L | Done: the ending's credits roll in 84 steps, 4.1 million ticks and about 600 s of wall time; 273 battles (55 lost first, 31 won with a death, each played again), no deaths standing, Alaron level 19 at the end; no game-side findings |
| Story triggers on a way as gates or actions: a cut-scene a tripwire or tangible script object plays as the player walks through, which wants its task taken on (the dryads' 0x0531 wants 3647, then 1160) | M | Done: a trigger seen to play the same way twice on a step turns the player back (`StoryTriggers`); getting past it (`PassGoal`) is planned first, by closing its gates or making it take another way; the dryads are got past by offending them, which sends him to the Gorgon's cave outside the wood |
| Levelling: the party is under-levelled for the north and east (18 battles lost first, 4 deaths); grinding goals, better builds, or less wandering | M | Done: better gear from the inventory put on through the Party tab, and bought at shops near the way (`Outfitting`); a battle won with a member dead played again and won outright, as a player reloads (`DiedFirst`); the builds settled by role from the combat formulas |
| Flag coverage 0: the machinery for setting every flag the static pass reaches, over a tree of saves ([flag coverage](systems/playthrough.md#flag-coverage)): the flag ledger, a cover goal that sweeps its targets nearest first with a loss check before each step, branch jobs from the save before a step that loses targets, and a queue several processes work at once (`hbht playthrough cover init`, `work`, `status`) | M | Done: from a new game, `tools/cover.ps1` with 20 workers in one process covers 976 of the 2,230 targets in 15 minutes (905 steps, 49 jobs), starting the process again if it dies. Along the way: the bot's runs about five times faster and side by side on threads (the ending in about 2 minutes); teleporter arrivals and a player's own place no longer snap into twin-cell pockets, which stranded it in Gwernia castle; a stranded run branches from before the step; the dressing no longer swaps two swords for ever |
| Flag coverage 1: the trunk. A story run to the ending with a save before every step; each target assigned to the trunk save it's best started from, as the jobs' starts ([the trunk](systems/playthrough.md#the-trunk)) | M | Done: from a new game, `cover init --trunk` plays the trunk and assigns in about 2.5 minutes, and 20 workers then work the whole tree (193 jobs) in 7.8 minutes, covering 1,961 of the 2,230 targets with none left live, where stage 0's sweep covered 976 in 15. Along the way: a job whose game ends branches from before the step, and a sheetless npc with a way to training (Fyrsil) is no trainer |
| Flag coverage 2: the given-up targets worked down. The whole tree runs in under ten minutes, so this is runs and fixes in turn rather than one long night: the 269 a trunk tree gives up, most of them no plan where deep branches stood (their avoid lists leave them no way), the rest a branch's first step losing them again, and the dryads' fight through 0x03FA lost on its replay with Alaron at level 14 and up, ending six jobs | M | Done: 2,047 of 2,230 covered from a new game in about 40 minutes (183 given up, from 269): branches avoid the exact way that lost their targets, no deeper than 6; two retry rounds start what's given up from other trunk saves (`cover retry`); the pilot goes by trek's touch for tripwires; a backdrop's chapters aren't read round and round; a member goal that brings no one rests, and a run with nothing done for its targets in 60 steps stops |
| Flag coverage 3: party branches. The relaxation ignores the four-member cap, so a member never joined loses 2–26 targets each, about 175 in all; a branch per choice of party | M | Done: party jobs ([party jobs](systems/playthrough.md#party-jobs), `cover party`, `PartyGoal`): what needs whom by the relaxation with each member gone or kept, the planner searching with the cap and planning who leaves; 143 targets need someone from a new game, and the trunk tree already covers 124. Of its 183 given up, 12 need someone the trunk doesn't bring in; a party round makes 6 jobs for 9 of them, makes the party in 2 and covers none, so 2,047 stay covered: those 12 are kept by the story's order, its triggers and the dryads' fight, not the cap |
| Flag coverage 4: triage of what's still uncovered after 2 and 3 into bot gaps, relaxation overestimates, and findings in the game: after 2 and 3, 183, 96 of them branches with no plan from their save and about 50 lost or stranded again on a branch's first step; the dryads' fight through 0x03FA lost on its replay; the 19 that need a member, all lost to the story's order or its triggers (Godric's 73 before Erromon, Becan's at I13 and P17) | M | |
| Flag coverage 5: what the planner doesn't model: camping, deaths, traps going off, cheats, help topics, shops, and waiting for the hour (derived flags over the calendar) | L | |

### Phase 5: Mod system and tools

The base game is a plugin, so the mod format is exercised all the time; Phase 5 exposes it and builds tooling on top.

| Task | Size | Status |
| --- | --- | --- |
| Plugin loader: load order, dependencies, field-level merge, conflict report | M | Done: `PluginSet`, `hbht conflicts`, `hbht schema` |
| Flag and ID allocation for mod namespaces | S | |
| Content out of code: the [hardcoded content](hardcoded-content.md) log, from records and fields (the protagonist read from the new game record, three code tables read rather than written as literals) to features that need designing (skill checks, combat's chance and damage formulas, an AI record, aspects and elements) and the events the cheats and the first-spell help wait on | L | |
| Asset overrides (glTF, PNG, OGG) and new assets | M | |
| Scripting: a Borg13 superset for dialogue and cut-scenes, plus sandboxed Lua event handlers on the 2.10 events (ADR 0008). Mods can't load .NET assemblies. | L | |
| **Editor.** Grow the editor into a Construction Set: record editing, dialogue graph editing, map object placement, and a quest atlas that includes mod content. It shows the HUD and the World tab too, which the runtime draws and the editor doesn't yet; its map object pages use doors, triggers and teleporters; its map shows wanderers; and how it shows a cut-scene on its map, past the reference points it already draws, is to be worked out with the user | XL | |
| Cheat codes: not ported at all; typed as Alaron's name, they'd come in through the name entry as triggers on `cheat.entered`, with their accepted and rejected sounds (0x0718, 0x0721) ([hardcoded content](hardcoded-content.md)) | S | |
| Documentation and a sample mod (a new trainer, a new quest and a rebalanced spell) | M | |
| A bugfix mod: the content fixes the user chooses, as patches to the base plugin ([ADR 0029](adr/0029-content-fixes-wait-for-a-bugfix-mod.md)), for what crashed or softlocked the original, and wording the game is inconsistent in; cut-scenes' oddities, which are in most of them, are out of its scope. So far: script 1040's node 24, Dougal's, linked to node 25, so the harbour reunion plays with him in the party ([scripts](systems/scripts.md#faults)); Sense Aura's "%d Total Hitpoints" as "%d Total Hit Points", the combat record's `text.aura.hitpoints`, as the rest of the game writes it ([combat-actions](systems/combat-actions.md#spells-potions-and-skills-in-a-turn)) | S | |

### Phase 6: Enhancements (optional, post-parity)

Widescreen, higher resolution, higher frame rate, longer draw distance, rebindable controls, and quality-of-life options such as visible stealth feedback, each a setting on the options menu ([ADR 0018](adr/0018-the-port-fixes-bugs-and-sets-enhancements.md)). Some came early and are the default: the floating conversation box and a UI screen of any shape. Rebindable controls came early too, with mouse look ([ui](systems/pause-menu.md#controls)). Their faithful mode, with its overscan bars, is left for here ([ui](systems/ui.md#a-faithful-mode-later)). An overhaul of the combat AI fits here too, as an option over the original's: combat parity keeps the AI's quirks, such as its walks giving up where 1.0's do, as 1.0 plays them.

## Risk register

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Combat and AI formulas are hard to verify by eye | Balance feels "off" | Exact RNG port plus differential tests (2.9). Read constants from the machine code rather than inferring them from play. |
| Finding code without the decomp's map ([ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md)) | Research runs cost more, and a system's code can be missed | The code index, the symbol map and the debug build's source files (0.7–0.9), with watchpoints and traces on checkpoints (0.11, 0.12). |
| Content hardcoded in code that the audit misses | Mods can't reach it, or content is missing | The 1H audit and the quest atlas's reachability check in Phase 4 |
| Scope fatigue over several years | Project dies | Vertical slices so something is always playable. The editor is useful to the community on its own. |
| Legal | Takedown | Never distribute ROM-derived assets or data; plugins ship only as deltas and original assets. The project no longer reads the decomp, which has **no licence** ([ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md)), and never copied its code. The README credits it for the early research. |
