# Checks for the user

Things only the user can settle, by playing the original in the emulator, by listening, or in full-game testing. Each says what to do and what to look for, and links to where the port's reading is written up. When one is settled, the finding goes in that doc and the item comes off this list. A check far into the story needn't be played to: a save can be written into the pak beside the ROM, at the place with the flags and party it needs (`hbht save to-pak`, [saves](systems/saves.md#the-originals-save)).

## Setting up a check

The user needn't play to a check: a save is written into the Controller Pak beside 1.0's ROM, at the place, flags and party the check needs, and the user loads it from the title in `tools/ares.ps1 v10`. Every save so far has loaded. The format and the commands are in [saves](systems/saves.md#making-and-checking-saves).

1. **Start from a save of the port's.** `hbht save from-pak <rom> <slot> --pak "roms/<1.0 rom basename> - story saves.pak" --out <save>` takes one of the four story saves (A before the final encounter, B before Shamsuk's Tower, C before Rooughah, D in Terminor, each with the [checkpoint](reference-rig.md#checkpoint-library) captured at the same place); `hbht save from <checkpoint>` or `hbht save new` do too. Pick the one nearest the check in the story, and say which flags were changed to get there.
2. **Find what the check needs.** The map object that starts the script or holds the place: search the installed base plugin's `records/maps/*.json` for the script's ID (its stage, position, radius and enabling flag), and `records/mapsets.json` for the chunk that places the map. `hbht flag <n>` says what sets, clears and reads a flag, and what a derived one is made of. `hbht dialogue <rom> <number>` dumps the script, for the path to the node that matters.
3. **Edit it** with the scripts in `tools/saves/`, each run as `dotnet run tools/saves/<script>.cs -- …`:
   - `party.cs <in> <out> --remove <entity hex,…> --add <entity hex,…>` changes the party by the scripts' own leave and join, so the party's flags follow.
   - `edit.cs <in> <out> --at <map set> <chunk> <x> <z> [<y>] --face <x> <z> --set <n,…> --clear <n,…> --give <item ID> <n> --label <text>` stands the player on the floor under a point of the chunk (swept down on the port's collision; give `y` indoors, or it lands on the roof), facing another point, sets or clears plain flags, and adds items to the inventory.
   - `sheet.cs <in> <out> --gold <n> --hour <h[:mm]> --days <n> --member <name> --stats <stat>=<n>,… --current <stat>=<n>,… --skills <skill>=<n>,… --spells <spell>=<level>,… --forget <spell,…|all> --weapon|--armor|--shield <item|none> --gear <item,…|none> --exp <total>[/<spendable>]` edits the party's sheets, the gold and the clock, each `--member`'s options until the next. Items and spells go by name or ID. It refuses what the pak's sheet can't hold (Stamina to 255, Endurance and Level to 63, the other stats to 31, skills to 14, spell levels to 15) and a spell the original's load would drop: only unschooled ones and the member's school's (Star for Chaos), and Alaron's Elemental, Naming and Necromancy too. It prints the party as the save loads.
   - `flags.cs <save> <n,…>` prints flags, derived ones included, as the save loads: check the object's enabling flag holds, and that what should be hidden is.
4. **Write it**: `hbht save to-pak <rom> <save> A`. Close ares first, since it writes its pak back as it closes. Slot A is the checks'; the command keeps the pak as it was first (`<pak>.<time>.bak`), and the story saves stay untouched in `- story saves.pak`, which the tests read.
5. **For the runtime**, the same JSON loads with `tools/godot.ps1 --path src/HBHT.Game -- --save <path>`. Keep it in the gitignored `out/` and give the user the path: what Claude writes under `%LOCALAPPDATA%` lands in the Claude app's private copy, not the data folder the user's runtime reads. The saves made for the checks so far are in `out/user-checks/`.
6. **When the user reports back,** the finding goes in the system's doc and the item comes off this list. The port's fixes follow [ADR 0018](adr/0018-the-port-fixes-bugs-and-sets-enhancements.md) and, for the game's data, [ADR 0029](adr/0029-content-fixes-wait-for-a-bugfix-mod.md).

## Open

### Combat parity (2.9)

Combat plays well, but the original's execution has checked it in one battle alone: the Combat checkpoints' ten rats, with Rheda's Endurance its only spell ([combat](systems/combat.md#the-port)). That pins the setup, turn order, the rats' AI, melee and ranged at the checkpoints' few pairs, the casters' choices (`CasterVectors`), the Fire and Sleep Gas flasks' blasts and the rewards. Nothing has been run against the original for:

- the party's spells but Endurance: damage, placed areas, holds and charms with the disable limit, Mirror, the wards and dispels, Day and Night, Cheat Death, Banishing, Sense Aura, Tap Stamina, Wraith Touch, the morale spells, healing, failures and resists;
- the attack's modifiers past those pairs: night, the aspects, Stamina, height, a held target, Thief, the Troubadour, rain, distance, a moved target; resistances and weaknesses; a weapon's spell; thrown weapons; the Inferno and Acid flasks;
- potions drunk, the Healer, the Troubadour, a skill trained by its roll, the effects' rounds (Poison, Immolation, Web's escape, Photosynthesis) and deaths from them;
- the AI's styles 1–4 as themselves rather than a caster's fallback, the morale spells' flight, defeat, the party fleeing, and every boss but the rewards' tally.

The campaign has two halves. **The formulas** run in the interpreter with no play: 1.0's own functions on a checkpoint's RAM with the inputs varied, against the port's with the same draws, as `tools/aivectors` does for the casters. **The flow** comes from battles the user plays from saves written for them, logged as below, and replayed into the port.

#### How a battle is captured and replayed

1. **The save.** Each setup below is a save of the port's, edited (the party's members with `party.cs`; their stats, skills, spells and gear and the clock with `sheet.cs`; the inventory and the place beside the monster party with `edit.cs`) and written into the pak beside 1.0's ROM, as in [Setting up a check](#setting-up-a-check). A setup can be fought as often as it's loaded.
2. **The log.** `tools/parity.ps1 -Label <setup>` boots 1.0 under gdb with the pak, and logs every battle until ares is closed, one beep as it begins and two as it ends ([the rig](reference-rig.md#combat-parity-logs-toolsparityps1)): every step of the stream from `Combat.Setup` to `Combat.Exit` with the functions that drew it, the rules' entry points with the current combatant, and RAM at the setup, at each turn's start and at the end. The battle plays at about 60% speed and stops for 2 s as each turn begins, while trek runs at full speed.
3. **Save states** aren't needed, as the RAM at each turn's start stands in for them. One on the field before the battle is still worth keeping, to fight it again without walking there.
4. **The replay.** The port builds the battle from the RAM at `Combat.Setup`, as `BattleCheckpointTests` does from checkpoint 01, and plays it turn by turn: before each of its combat stream's draws, the stream set to where the original's was before its draw of the same rank, the cosmetic ones skipped; the player's turns from the log (where the body stood, the action, the spell or item, the targets), the AI's its own. It compares each turn's start with that turn's RAM (hit points, Stamina, stats, effects and their timers, the log's lines, places) and reports the first difference.
5. **Then the runtime.** The user plays the same setups in the port for feel, side by side, as in M4: not draw for draw, as the port's combat stream has no cosmetic draws and the bodies go where they're steered.

The replay so far is `ParityReplayTests`, over every battle in `out/parity`. It builds the battle from the RAM at `Combat.Setup` on the original's draws (`Lcg.BeforeDraw`), and checks four things:

- **The setup:** its draws, one for one.
- **The first turn:** its order, directions, footprints and radii.
- **Each turn's first action**, carried out from the RAM at its `Combatant.ExecuteAction`. The RAM is laid over the battle (`CombatCheckpoints.Overlay`), with the spell, potion and targets as chosen, and the port's `Battle.ExecuteAction` runs with the original's attacker, target and kind.
- **Each enemy's turn**, played by the port's AI from the RAM at its `TurnOrder.BeginTurn`, the bodies moved by their animations' roots. It's checked against the original's `Combatant.ExecuteAction`: whether it acted, whom it went for, and where it stood, to 0.5.

After an action or an enemy's turn, the next turn is checked as it begins, at the first cue after the port's `Advance` has chosen it, where the original's RAM was dumped. The check covers the draws' count, whose turn it is, and every combatant's stats, skills, protection, weapon, death and flight, and each living enemy's AI record (kind, target, morale and flags). Reserves that have joined are brought in at the RAM's cells first, and the overlay lays the round's turn order too.

Both trials and setup 1's first session pass. The trials' actions are:

- Brenna's and Sholeh's backstabs;
- Solar Wrath on three rats;
- Endurance on Rheda and Alaron;
- a Strength Potion;
- the rats' bites, each going for Brenna from within 0.1 of the original's cell;
- two flee attempts, one ending in a flight;
- a reserve's walk.

Setup 1's first session (`out/parity/20261031-105935-setup1`) adds:

- Fireball, Dragon Flames, Earth Smite, Whitefire, Lightning, Immolation (twice), Acid Bolt, Tap Stamina, Haste, Darkness and Sense Aura;
- Web of Starlight on two rats;
- a Stamina Potion;
- the Fire weakness raising the rats' morale check;
- a round's end with three slots refilled by reserves.

1.0 locked up on Sholeh's third Web, with two rats already held and reserves to come. Frames kept running, and nothing else in combat moved. The log ends there.

Still to do:

- **Effects' records and timers**, which the overlay doesn't copy.
- **Positions, shown but not checked:**
  - The attacker's body after a blow stands up to 0.5 from the original's (Brenna's backstab), with its footprint a cell over.
  - A reserve's walk with no action ends 4.6 from the original's (the second trial's slot 5).
  - Reserves that join at a round's end can take other spawn points than the original's, on the same draws. In setup 1, at the end of slot 5's turn, slot 6's reserve stands at (13, 26) for the original's (24, 39), and slot 7's at (24, 39) for (19, 23). The two disagree about which point is free, likely over a body that has fled or died.

#### The formula vectors (no play)

Run on the Combat checkpoints' RAM and on each setup's first state, with every input that matters varied:

| Function | Varied |
| --- | --- |
| `MeleeChance`, `ResolveMelee`, `MeleeDamage`, `ApplyResistance` | Strength tiers; from behind, with Thief; height up and down; each part of the day; a Lunar and a Solar attacker; no Stamina, either side's; a held target; a shield, from the front and behind; Warrior's adjusted roll; the Troubadour's bonus; a weakness, a resistance, one of 0; a weapon's spell (`WeaponSpellProc`); the weapon skill's roll landing and missing |
| `RangedChance`, `ResolveRanged` | distance past 6; rain's intensity; night; a target that has moved or acted |
| `ThrowChance`, `FlaskDamage`, `FlaskExplosion` | each flask; fog, storm and night with distance; Acid's corrosion; a thrown weapon |
| `CastSpell` (`CheckSpellAvailable`, `PrepareCast`, `CastChance`, `ResistRoll`, `ApplySpellEffect`, `SpellDamage`) | every spell kind; each part of the day, and the moon's phases for a Lunar caster; failure; the reagent, Stamina and Field refusals; a requirement unmet; wards and aspects in the resist; Mirror's counter; the disable limit with reserves to come; the level's rise at 0 and 1; an item's spell and its charge |
| `AddEffect`, `AgeEffects` (`EffectRules.Round`, `End`), `Dispel`, `CurePoison`, `TakeDamage` | every lasting kind over its rounds: Poison, Immolation, Acid Bolt, Web's escape, Photosynthesis by day and night, Tap Stamina, Wraith Touch, the morale spells given back; Cheat Death; death taking the effects |
| `DrinkPotion`, `CanDrink` | every potion, each where it's refused |
| `ResolveHealer` (`LayOnHands`, `HealWithHerbs`), `StartTroubadour` | Healer and Troubadour levels, Stamina short, a performance already going |
| `UpdateRadius`, `TurnsPerRound`, `TurnOrder.Build` | each Dexterity threshold; Haste; Stellar Gravity; engaged; performing |
| `FillEnemies`, `ChooseArena`, `BattleAmbushRoll` | parties of every shape, the level trim; every terrain; the Ranger's margins |
| `TallyRewards`, `RollChestItem` | chests with gold (the doubling) and drops with worth |
| `EnemyAI.Step`, `CheckFlee`, `Morale` | styles 1–4 as themselves; the morale spells; a weakness making the check due; the item-spell cases `CasterCheckpointTests` leaves out; `extras.py`'s vectors, which no test reads yet |

#### The setups (the user plays)

The party for most: Alaron, Sholeh (Star, Healer 10, Ranger 6, Long Bow), Dougal (Elemental, Troubadour 10, Thrown 10) and Keelin (Thief 8, Missile 7), with the spells each setup needs taught within their schools (Alaron's, Chaos, takes any, so he carries the Necromancy and Naming ones) and its flasks, potions, reagents, scrolls and a wand in the inventory, at a level the battle lasts at. In order of what the checks lack most:

| # | Monster party | Arena | Enemies | Scenarios |
| --- | --- | --- | --- | --- |
| 1 | MiscInteriors E05 #1 (24, 64) | 27 | 12 Tomb Rats, weak to fire ×1.5, reserves | The party's spells: Fireball placed to catch a member; Dragon Flames by level; Lightning; Earth Smite; Whitefire; Immolation and Acid Bolt over rounds; Web of Starlight and Frozen Doom, with the disable limit while reserves are to come and Web's escape; Stellar Gravity and Haste; Solar Wrath and Aura of Death putting them to flight; Poison to a death; Tap Stamina; Wraith Touch and Wall of Bones against their True Name requirement; Darkness then Light; Sense Aura at levels 1, 5 and 9; a cast failed, one refused for its reagent and one for Stamina; a scroll and a wand; a level-0 spell |
| 2 | ChaosIsle A07 #8 (27, 27) | 17, no flee points | 2 Chaos Spellweavers, 2 Chaos Sorcerors | Enemy casters: Starlight Shield, Mirror, Wall of Bones, Web, Darkness, Clumsiness, Crushing Death, Whitefire and Lightning at the party; the party's spells into Mirror; Dispel Star on their shields; the wards (vs. Star); enemies that can't flee |
| 3 | Overworld M08 #2 (26, 85) | 2–3 | 10 Giant Rats, reserves | The skills and items: Lay On Hands and Heal With Herbs; Dougal performing for two rounds, then acting; a weapon changed and a flask taken in hand from the Party tab; Fire, Inferno, Acid and Sleep Gas flasks, one landing near a member; each potion drinkable; blows from behind with Keelin and from higher ground; a turn skipped; the win's loot and experience |
| 4 | Overworld F14 #2 (29, 85), at night | 6–7 | 4 Bandit Woodsmen, long bows, style 3 | Ranged: Sholeh's and Keelin's shots at targets near and far, moved and not; the Woodsmen keeping their distance; Dougal's thrown Javelin; melee at night; rain if the weather brings it |
| 5 | Overworld E09 #5 (98, 53) | 2–3 | 4 Goblin Poisoners, darts, style 3 | Thrown attacks at the party; Antidote and Remove Poison if the darts poison |
| 6 | EhudUnder E03 #4 (6, 14) | 23 | 4 Wraiths, Physical ×0, Magic ×0.5 | Blows doing nothing; spells halved; Wraith Touch and Darkness at the party; Restore after Wraith Touch |
| 7 | Overworld N26 #2 (87, 70) and #3 (63, 33) | 14–15 | a Dust Devil and an Air Elemental; 2 Salamanders | Banishing and Control Elemental on the elementals; Wind at the immune; the Fire Flask at the Salamanders |
| 8 | Ugarit2 A07 #3 (5, 2) | 24 | Skeletons, Cutting ×0.25, Smashing ×1.5 | The same member with a sword, then a hafted weapon |
| 9 | Overworld A04 #10 (23, 78) | 4–5 | 4 Wolves, which roam | Noticed while running, walking and sneaking (the ambush, its animation byte and the Ranger's roll); talked to from in front and from behind (`Talk`'s collision byte and cone, [combat](systems/combat.md#not-settled)) |
| 10 | Overworld N24 #0 (87, 14) | 4–5 | 12 Trolls | A party too weak: a member dying mid-battle (the dead leave, stripped, after it), Cheat Death on Alaron, the party fleeing by the pause, and Alaron's death, the game over |
| 11 | GoblinCamp C01 #3 (3, 3), talked to | 26 | Kitarak and 6 Hobgoblins | A boss: no fleeing, his weapon's spell, the kill flag, the Hobgoblins' Tap Stamina and Spirit Shield; the arena by the terrain rather than the script's |
| 12 | Barrows G03 #11 (21, 13), talked to | 18 | Lugash | His Cheat Death; fleeing allowed by 5250 |
| 13 | MiscInteriors A09 #1 (38, 49) | 12–13, one flee point | 3 Gorgons | Frozen Doom at the party; a fleeing Gorgon cornered ([combat](systems/combat.md#not-settled)) |

The saves are in `out/parity/saves/`. `base.json` is story save A with the party above: Rheda and Brenna left and Dougal and Keelin joined (`party.cs`), with Sholeh's Long Bow and the skills (`sheet.cs`). Each setup is built on it:

- **Setup 1, `setup1.json`, in the pak's slot A.** The party stands 8 west of the Tomb Rats, facing them. The spells:
  - Alaron: Acid Bolt, Aura of Death, Haste, Tap Stamina, Wraith Touch, Wall of Bones and Darkness at 5, and Sense Aura at 1.
  - Sholeh: Whitefire, Web of Starlight, Frozen Doom, Stellar Gravity, Solar Wrath and Light at 5. She wears the Sense Aura (level 5) and Charming scrolls.
  - Dougal: Fireball, Dragon Flames and Earth Smite at 5.
  - Keelin: Lightning and Immolation at 4, Poison at 3 and Dragon Flames at 1. Keelin wears the Lightning wand and starts with 6 Stamina, so a cast gets refused for Stamina. The low Stamina leaves Keelin 38 of 72 HP.

  The party has 2 Herbs, so the third of Frozen Doom, Wall of Bones and Sense Aura gets refused for its reagent. The pak can't hold a spell at level 0, so the rise is tried at level 1 alone. Sense Aura is cast at 1 and 5, not 9.
- **Setup 2, `setup2.json`, in slot B.** Flag 3078 is cleared, so party #8 is back. The other five parties of A07 stay beaten. The party stands 7 from them, facing them. On top of the story's spells:
  - Alaron: Dispel Necro and Light at 5.
  - Dougal: Lightning and Earth Smite at 5.
  - Keelin: vs. Star at 5 and Lightning at 4.

  Sholeh's Dispel Star and Dougal's Mirror are the story's.

Slots C and D still hold copies of story saves C and D.

Every log also settles the frame's delta in combat, and the first one where in a turn an attack's draws fall, both in [combat](systems/combat.md#not-settled). Each party above is always enabled but Kitarak's (while 154 is clear) and Lugash's (5250); others can stand in from the base plugin's monster parties.
