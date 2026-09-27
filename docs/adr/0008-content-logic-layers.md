# 0008. Content logic: records, triggers, features, scripts

Status: Accepted · Sep 27, 2026

## Context

[0001](0001-importer-owns-the-rom.md) says content that the original hardcodes has to become records. Some of it is behaviour, though, not just values. The mod system needs an answer for logic that no table can express.

The decomp has about 310 references to named event flags. They fall into three groups:

1. **Tables written as code: the large majority.** Examples:
   - `WorldMap.cpp` (180 references): map piece flag, image and position.
   - `PartyInventory.cpp` (49): which key items and maps set which "has" flags.
   - `bossflag.cpp`: boss entity to "killed" flag.
   - `party.cpp`: party join, leave and death flags.
   - The cinematic flag lists and the Theatre menu.
   - `dialogmode.cpp`: dialogue script to encounter.
   - `ZoneEngine.cpp`: the lit-lighthouse scene swap.
2. **Engine reactions keyed to a flag.** The first-spell and first-potion prompts, the new-journal-entry popup, and camp dialogue checks. Each follows the pattern *when an engine event happens, if a condition holds, do something*.
3. **Bespoke logic: about half a dozen behaviours.**
   - The Goblin Ambush is the largest. Across four combat files, Alaron keels over after two hits, damage prints as 1, and the victory text is suppressed. Afterwards the party is healed, a flag is set, and Alaron is moved to the bed in Oriana's hut.
   - The others are Brenna's Ranger check in Gwernia, the Lugash fight, shadow merge, Sholeh's death, and Alaron's True Name.

Mods will need logic of the third kind too. That's what games usually embed Lua for.

## Decision

Content logic uses four layers. Each piece of logic goes in the **first layer that can express it**.

1. **Records.** Everything table-shaped. The importer emits these from a built-in table of facts taken from the decomp, alongside what it reads from the ROM.
2. **Triggers.** Declarative rules of the form *on event, if condition, do effects*:
   - The core publishes named events, such as `combat.ended`, `item.acquired`, `spell.learned`, `journal.added` and `camp.started`.
   - Conditions and effects reuse Borg13's vocabulary. Conditions check a flag, item, party member, skill or gold. Effects set a flag, add an item, show a prompt, start a dialogue, or teleport.
   - Triggers are records, so plugins add and patch them like anything else.
3. **Parameterised engine features.** When bespoke code hides a reusable idea, it becomes a C# feature configured by data. For example, the Goblin Ambush becomes encounter fields such as `nonLethal`, `keelOverAfterHits`, `suppressVictoryText` and `onVictory`.
4. **Scripts in Lua**, for what is truly one-off.

### No private back door

The base game gets no hooks that mods don't. Every event, trigger effect, feature field and script function that the base game uses is part of the public mod surface. If base content needs a new hook, the hook is added to that public surface. This is the same principle as the base game being a plugin.

### Script constraints

Scripts run **inside the headless core** ([0003](0003-headless-rules-core.md)), so they have to keep it deterministic and saveable:

- **Sandboxed.** Scripts can't touch files, the network, the clock, the OS or .NET reflection. Randomness comes only from the core's streams ([0006](0006-tick-model-and-rng.md)).
- **Handlers run to completion.** A script is an event handler, or a function that a trigger or Borg13 calls. It can't wait. Anything that has to wait, such as a cut-scene, an actor walk or a dialogue, starts a Borg13 sequence, and Borg13 state saves cleanly. Lua coroutines can't be serialised, so none survives past the handler that created it.
- **State lives in declared records.** Scripts keep persistent state only in flags and variables declared with IDs ([0004](0004-record-ids.md)). The state saves with the game, and the quest atlas can see what each script reads and sets. A script declares the IDs it touches, so the atlas doesn't have to guess from its source.
- **Budgeted.** Each handler gets an instruction budget. Going over the budget aborts the handler and reports the plugin, rather than freezing the game.

### Language and runtime

- **Lua**, because it's the established language for game modding (OpenMW made the same choice) and it's small and easy to sandbox.
- It runs on a **pure-.NET interpreter**, so there are no native libraries to ship for each platform. The specific interpreter is chosen when the script layer is built. It must support sandboxing and instruction budgets.
- **Mods can't load .NET assemblies.** Modern .NET can't sandbox them, so a mod could do anything on the player's machine.

## Consequences

- Most of the hardcoded-content audit (roadmap 1H) becomes records and triggers. For each item, the audit also records which layer it belongs in.
- The core's event list and the trigger vocabulary are designed in Phase 2 alongside the flag engine and the Borg13 VM, because the base game needs them. The Lua layer can wait until Phase 5, as long as it hooks into the same events.
- Features in layer 3 give mods capabilities the original only had as one-offs, such as non-lethal and tutorial fights.
- Scripts can't express long-running behaviour directly, so some mod ideas need a Borg13 sequence plus a handler. That's the price of saves working anywhere a script could have run.
- Base-game content should rarely if ever need Lua. If the audit finds that it does, the case gets a second look first, to see whether a layer-3 feature fits better.
