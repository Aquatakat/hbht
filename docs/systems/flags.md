# The flag engine (roadmap 2.1)

`Aidyn.Core.Flags`: `FlagNetwork` (every flag's value and the derived flags' evaluation) and `StateFlags` (what the engine writes into flags). The flag table's format and what reads and sets each flag are in [quests](../formats/quests.md). They port the original's `ProcessGameStates` and the getters and setters in `gamestatelib.cpp` and `gameStateFunnel.cpp`, from 1.0's machine code.

## The records

The importer writes three record types:

- **Flags** (`aidyn:flag/n`): every `GameStateFunnel` entry. A plain flag has its initial value (only flag 1 starts set). A derived flag has a formula: its kind, operation, operand and inputs, as record IDs. Flags carry the labels engine code gives them, such as "Brenna is in the party" or "Time of day, bit 1 of 3".
- **Journal entries** (`aidyn:journal/n`): the text and the flag the journal lists it by. Derived flags 5556 on are "entry *i*'s flag is set", and each names the entry it adds, so a mod can add entries the same way.
- **The state flags** (`aidyn:stateflags/0`): which flags the engine writes its own state into, and the new journal entry flag (3617). It's a layer 3 feature (ADR 0008), so a mod can move them.

## The network

`FlagNetwork` holds every flag's value, by handle. A base-game flag's handle is its number, and other plugins' flags follow in load order (ADR 0004).

- **Reading.** Flag 0 always reads clear and flag 1 set. Reading a derived flag while anything has changed brings the network up to date first. Reading a plain flag doesn't. So the new journal entry flag, which the network sets, can lag until something reads a derived flag, as in the original ([below](#when-the-network-comes-up-to-date)). A map object's write is followed by such a read, so it brings the network up to date at once (`TrySetAndProcess`, [map objects](map-objects.md#map-objects-write-flags)).
- **Writing.** Only plain flags from 2 up can be set. Writes to 0, 1 and derived flags do nothing, as `setEventFlag` refuses them.
- **Unknown flags.** The rules read and write flags by record through `IsSet`, `Holds` (a map object's gate) and `TrySet`: a flag no loaded plugin has reads clear and its writes are ignored, since a mod can remove a flag another plugin's records still name, which the base game's never do. `Get(RecordId)` and `Set(RecordId, bool)` throw instead, for where an unknown flag is a mistake, as in tests.
- **Bringing it up to date.** Derived flags are evaluated in order, each one seeing those before it already updated. If any changed, it starts again from the first. 13 inputs name a later derived flag, so the order matters.
- **Formulas.** The inputs are read as bits, the first the lowest, or counted. Logic flags test all (AND), any (OR), exactly one (EOR), none (NOT), or the bits equal to the operand (MSK). Value and count flags compare with the operand by =, >, < or ≠. A kind with the other kind's operation reads as clear, since the original reports it as an error. No base flag does that, and none has an unused input (0xFFFF) or more than 9 inputs.
- **The journal.** When a flag that names a journal entry turns on, the new journal entry flag is set, and the entry becomes the one the journal opens at (`newestJournal`). In 1.0 that's `GameStateFunnel.Write` (`0x800cb33c`), which only `ProcessGameStates` (`0x800cb034`) calls for a derived flag: a write that turns on a flag above 5555 sets 3617 (`+0x710e` in the table) and stores the entry's index, the flag less 5556 (`FlagNetwork.SetNewestJournal`, `0x80020ea4`, into `0x800db05c`). The port also lists the entry in `JournalOrder`, the order the journal shows them in and a save keeps ([saves](saves.md#the-format)); one whose flag clears keeps its place. The journal lists an entry by its own flag, set or not in `JournalOrder` ([ui](ui.md#the-journal)).
- **New games and loading.** A new game starts from the plain flags' initial values and evaluates the rest. `Restore` sets every plain flag at once and evaluates, as loading a save does. Derived flags keep their old values until then, so any journal flag that turns on adds its entry again.

## When the network comes up to date

1.0 has no timer for it: `ProcessGameStates` runs only when a derived flag is read while the dirty halfword is set (`GameStateFunnel.Read`, `0x800cb3ac`, whose one caller is `FlagNetwork.Get`, `0x8001fd5c`), or when a game is loaded or begun (`GameStateFunnel.MarkAndProcess`, `0x800cb31c`). Trek writes the calendar's flags every frame, so the network is out of date nearly always, but nothing brings it up to date every frame. A journal entry's notice (`TrekLoop.JournalEntryNotice`, `0x80020eb0`) reads 3617, a plain flag, each frame with no fade, nothing queued, nothing busy and trek in its state 3, so **the notice comes on the first such frame after the network next comes up to date**, not after the flag is set.

`FlagNetwork.Get` has 40 callers, none through a pointer. These are the ones in trek and its scripts that can read a derived flag, which the port reads at the same places through `FlagNetwork.Get`, `IsSet` or `Holds`:

- **A map object's write** is followed by a read of 5626 ([map objects](map-objects.md#map-objects-write-flags)): a trigger, a Used flag, a container's opened or exploded flag, a secret door, a talker's flag B. A door's picked and disarmed flags aren't.
- **Loading a chunk** reads each object's gate (`LoadedMap.Activate`, `0x80017af8`, through `MapObjects.Gate`, `0x800101e0`, and `VoxelObjects.GateTable`, `0x800da458`): flag C for most types, a monster party's A and B ([map objects](map-objects.md)). That's every chunk of the 3×3 when trek starts again after a cut-scene, a backdrop, the help menu or a battle (`InitGameTrek`), when the pause menu closes, on a teleport, and each new chunk on crossing a border. `LoadedMap.Activate` ports it. 1.0 also reads 5017 whenever Overworld A15 or Lighthouse A05 loads, to swap them to the lit lighthouse (`CheckToggleZoneScene`, `0x8000aa14`); the port doesn't swap them yet, so doesn't read it.
- **Each tick,** the A gate of each active tangible teleporter, dialogue or trigger the player is within (`ProcessVoxelObjects`, `0x80083838`, through `MapObjects.Enabled`, `0x80010214`), but not while a script runs (`MapObjectUse.Touch`). So standing in the dialogue object that started a cut-scene, gated by a derived flag as most are, brings it up to date on trek's first tick back.
- **A map sound starting** reads its gate (`Audio.UpdateSound`, `0x8004e900`; `MapSounds`).
- **A script's `CheckFlag`** (`Dialogue.Condition`, `0x8004f680`, op 0x18). `SetFlag` and `ClearFlag` only write (`Dialogue.Effect`, `0x8004f7b8`, ops 0x20 and 0x21), and so do the done flag and the engine's writes for items and members ([scripts](scripts.md)).
- **A battle won** reads the monster party's A gate after setting its encounter flag (`Combat.StateVictory`, `0x80022450`). The bosses' flags are set before it, as the battle is won, and again after it (`Combat.SetKillFlags`, `0x80067860`) ([combat](combat.md)). Where that gate is plain, as Kitarak's (154 itself) is, nothing in the battle brings the network up to date.
- **The journal** reads every entry's own flag as it opens (`Journal.AddEntry`, `0x8005256c`), and entry 32's is derived 5017 ([ui](ui.md#the-journal)).
- Camping (`Camping.Ambushed`, `TrekLoop.CampScene`) and the end of a cut-scene, whose chain check reads dialogue objects' A gates (`CutScene.Free`'s `MapObjects.Touches`).

The minimap's and the world map's flags, and the B gates `RenderVoxelScenes` reads as an actor's model loads, are all plain in the data, so they never do.

So after a cut-scene, a backdrop or a battle, an entry's notice comes as trek fades in where loading the 3×3 or the dialogue the player stands in reads a derived flag, and otherwise waits. A conversation or a sign stays in trek and loads nothing: its entries wait for the next derived read, unless the script checks a derived flag after setting them. What the wait ends on is whatever reads one first: a chunk border, a tangible object, the chest's or the door's write, opening the journal, or leaving.

Over the 71 entries (`aidyn flag` and a script over the quest index, Oct 23, 2026): 8 come at once from a map object's write, 27 as trek comes back after a cut-scene or battle, 4 either way, and 1 by a `CheckFlag` later in its conversation. 18 can wait: 14 set in conversations, Kitarak's (14) and Shamsuk's (57) by their battles' boss flags, the letter in the cave under Port Saiid (35, a backdrop) and the arrival at Cradawgh's Isle (38, a cut-scene code starts). 11 are never set, the merge's (68) is set by `Combat.MergeShadow` (`0x800675c0`), and the lighthouse's (32) is derived 5017, over 286, which the keeper's cut-scene sets where its dialogue's gate is derived. The port's timing is the same for every way: it reads the same flags at the same places.

`GameSessionTests.Journal.cs` plays four: the Wizard's School's entry the tick after the first step in (JundarInteriors A01's trigger, 1.1 inside the arrival point); the letter's, waiting in the cave until the door out loads the Overworld; Kitarak's, waiting in his room after the battle (no gate there is derived) until the chest's write or the journal brings it up to date; and Yeraza's dinner's, as trek fades in in Erromon A11, whose scene objects are gated by derived 5515.

## What the engine writes

`StateFlags` writes numbers into runs of plain flags, lowest bit first, dropping bits that don't fit (`set_flag_array`). Every write marks the network out of date, even when nothing changes.

| Flags | What | Written |
| --- | --- | --- |
| 2299–2300 | Moon phase: 0 new, 1 crescent, 2 quarter, 3 full | Whenever time passes, which in trek is every frame |
| 2301–2303 | Part of the day: 0 morning (from 6:00), 1 midday (9:00), 2 afternoon (12:00), 3 evening (18:00), 4 night (21:00) | The same |
| 2304–2309 | Day of the season: week × 7 + day of the week | The same |
| 2310–2312 | Season, 0–5 | The same |
| 2313–2315 | Day of the week, 0–6 | The same |
| 2316–2318 | Week of the season, 0–7 | The same |
| 2319–2320 | Rain: 0 clear, 1 rain, 2 snow: what the weather has falling, or setting in, not what's drawn as it dies away ([weather](weather.md)). Fog is 0. | The same (`World.Update`, `0x8007bf5c`, through `StateFlags.SetRain`, `0x8001fe9c`) |
| 2321–2324 | The terrain type of the player's chunk | On entering a chunk of another terrain |

- **The terrain has four flags for 28 types**, so types from 16 wrap round.
- **The party's best stats and skills are never written.** Flags 2325–2369 are laid out for the party's best Intelligence, Willpower, Endurance, Dexterity, Strength, Mechanic, Diplomat, Loremaster and Wizard, and derived flags 5287–5346 compare them with thresholds. But `Event_flag_stat` and `event_flag_skill_`, which the original calls whenever a stat or skill changes, look the stat up in their table with a count of 0, so the lookup always fails and they return. None of these flags is set in any checkpoint. So in the original, the secret teleporter in EhudUnder M05 (needing Mechanic above 5) and three dialogue objects in EhudUnder G03 (needing Loremaster above 4 or 5) never work. The port keeps that.
- **Darkness and Light** hold the part of the day in a battle, the original's lock at `World+5` ([weather](weather.md)). The port's hold is the battle's (`Battle.PartOfDay`, [magic](magic.md#where-the-port-differs)), and flags 2301–2303 go by the clock's hour alone. No checkpoint has the lock up.

## Checked against the checkpoints

`AresCheckpoint` (`Aidyn.Rom.Rig`) reads the flags out of an ares state ([reference rig](../reference-rig.md)). Over the eight story checkpoints, from 1.0 and the debug ROM, restoring the plain flags reproduces every derived flag, and the calendar, rain and terrain flags equal what `StateFlags` writes from the state's clock, rain and terrain.
