# The flag engine (roadmap 2.1)

`Aidyn.Core.Flags`. The flag table's format and what reads and sets each flag are in [quests](../formats/quests.md).

Ported from `ProcessGameStates` and the getters and setters in `gamestatelib.cpp` and `gameStateFunnel.cpp`. Every function was checked against 1.0's machine code, and matches the decomp's reading of it.

## The records

The importer writes three record types:

- **Flags** (`aidyn:flag/n`): every `GameStateFunnel` entry. A plain flag has its initial value (only flag 1 starts set). A derived flag has a formula: its kind, operation, operand and inputs, as record IDs. Flags carry the labels engine code gives them, such as "Brenna is in the party" or "Time of day, bit 1 of 3".
- **Journal entries** (`aidyn:journal/n`): the text and the flag the journal lists it by. Derived flags 5556 on are "entry *i*'s flag is set", and each names the entry it adds, so a mod can add entries the same way.
- **The state flags** (`aidyn:stateflags/0`): which flags the engine writes its own state into, and the new journal entry flag (3617). It's a layer 3 feature (ADR 0008), so a mod can move them.

## The network

`FlagNetwork` holds every flag's value, by handle. A base-game flag's handle is its number, and other plugins' flags follow in load order (ADR 0004).

- **Reading.** Flag 0 always reads clear and flag 1 set. Reading a derived flag while anything has changed brings the network up to date first. Reading a plain flag doesn't. So the new journal entry flag, which the network sets, can lag until something reads a derived flag, as in the original.
- **Writing.** Only plain flags from 2 up can be set. Writes to 0, 1 and derived flags do nothing, as `setEventFlag` refuses them.
- **Bringing it up to date.** Derived flags are evaluated in order, each one seeing those before it already updated. If any changed, it starts again from the first. 13 inputs name a later derived flag, so the order matters.
- **Formulas.** The inputs are read as bits, the first the lowest, or counted. Logic flags test all (AND), any (OR), exactly one (EOR), none (NOT), or the bits equal to the operand (MSK). Value and count flags compare with the operand by =, >, < or ≠. A kind with the other kind's operation reads as clear, since the original reports it as an error. No base flag does that, and none has an unused input (0xFFFF) or more than 9 inputs.
- **The journal.** When a flag that names a journal entry turns on, the new journal entry flag is set, and the entry becomes the one the journal opens at (`newestJournal`).
- **New games and loading.** A new game starts from the plain flags' initial values and evaluates the rest. `Restore` sets every plain flag at once and evaluates, as loading a save does. Derived flags keep their old values until then, so any journal flag that turns on adds its entry again.

## What the engine writes

`StateFlags` writes numbers into runs of plain flags, lowest bit first, dropping bits that don't fit (`set_flag_array`). Every write marks the network out of date, even when nothing changes.

| Flags | What | Written |
| --- | --- | --- |
| 2299–2300 | Moon phase: 0 new, 1 crescent, 2 quarter, 3 full | Whenever time passes, which in trek is every frame |
| 2301–2303 | Part of the day: 0 morning (from 6:00), 1 midday (9:00), 2 afternoon (12:00), 3 evening (17:00), 4 night (21:00) | The same |
| 2304–2309 | Day of the season: week × 7 + day of the week | The same |
| 2310–2312 | Season, 0–5 | The same |
| 2313–2315 | Day of the week, 0–6 | The same |
| 2316–2318 | Week of the season, 0–7 | The same |
| 2319–2320 | Rain: 0 clear, 1 rain, 2 snow | The same, and when the weather is set |
| 2321–2324 | The terrain type of the player's chunk | On entering a chunk of another terrain |

- **The terrain has four flags for 28 types**, so types from 16 wrap round.
- **The party's best stats and skills are never written.** Flags 2325–2369 are laid out for the party's best Intelligence, Willpower, Endurance, Dexterity, Strength, Mechanic, Diplomat, Loremaster and Wizard, and derived flags 5287–5346 compare them with thresholds. But `Event_flag_stat` and `event_flag_skill_` look the stat up in their table with a count of 0, so the lookup always fails and they return. None of these flags is set in any checkpoint. So in the original, the secret teleporter in EhudUnder M05 (needing Mechanic above 5) and three dialogue objects in EhudUnder G03 (needing Loremaster above 4 or 5) never work. The port keeps that.
- **Spells that turn day to night** freeze the part of the day in the original (`DayNightMagic`). They aren't ported yet.

## Checked against the checkpoints

`AresCheckpoint` (`Aidyn.Rom.Rig`) reads the flags out of an ares state (see the [reference rig](../reference-rig.md)). The tests check all eight checkpoints, from 1.0 and the debug ROM:

- Restoring a checkpoint's plain flags reproduces every one of its 626 derived flags.
- The calendar, rain and terrain flags equal what `StateFlags` writes from the state's clock, rain and terrain, and the state's own part of the day and moon phase agree with them.
