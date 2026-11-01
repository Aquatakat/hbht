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
- **Each enemy's turn**, played by the port's AI from the RAM at its `TurnOrder.BeginTurn`, the bodies moved by their animations' roots. It's checked against the original's `Combatant.ExecuteAction`: whether it acted, whom it went for, and where it stood, to 0.5. Where the player pressed A in the walk, which puts the body at its leg's end at once ([combat](systems/combat.md#the-ai)), the replay presses it too (`PressedA`). The logger now records the press (`Combat.InputMoving` at its call of `Body.GiveUp`), and lists what it watched at `Combat.Setup`. For logs made before, which list nothing, the replay infers it: the walk the port's outlasts at six ticks a frame, with the original's body at the port's leg's end after it.

After an action or an enemy's turn, the next turn is checked as it begins, at the first cue after the port's `Advance` has chosen it, where the original's RAM was dumped. The check covers the draws' count, whose turn it is, and every combatant's stats, skills, protection, weapon, death and flight, and each living enemy's AI record (kind, target, morale and flags). Reserves that have joined are brought in at the RAM's cells first, and the overlay lays the round's turn order too.

Both trials and both of setup 1's sessions pass, and every party action of setup 2's but one. The overlay lays each combatant's effects on from the RAM, slot for slot, with their timers (`CombatCheckpoints.OverlayEffects`). The trials' actions are:

- Brenna's and Sholeh's backstabs;
- Solar Wrath on three rats;
- Endurance on Rheda and Alaron;
- a Strength Potion;
- the rats' bites, each going for Brenna from within 0.1 of the original's cell;
- two flee attempts, one ending in a flight;
- a reserve's walk.

Setup 1's first session (`out/parity/20261031-105935-setup1`) adds:

- Fireball, Dragon Flames, Earth Smite, Whitefire, Lightning, Immolation (twice), Acid Bolt, Tap Stamina, Haste, Darkness and Sense Aura;
- Web of Starlight failing its cast roll twice;
- a Stamina Potion;
- the Fire weakness raising the rats' morale check;
- a round's end with three slots refilled by reserves.

1.0 locked up on Sholeh's third Web, the first to land. Frames kept running, and nothing else in combat moved. The log ends there. The likely cause is a hold landing on a one-tick frame ([combat](systems/combat.md#where-the-port-differs), which the port doesn't reproduce). If a hold locks again, save a state in ares before closing it, so the RAM at the lock can settle it.

The second session (`out/parity/20261031-120218-setup1`) adds:

- Frozen Doom landing twice without a lock;
- Stellar Gravity, Solar Wrath, Light, and Poison over rounds;
- the Sense Aura scroll and the Lightning wand;
- Immolation burning at the rounds' ends;
- a cast refused for its reagent.

Its actions all pass. Wraith Touch, Wall of Bones and Charming can't be cast at Tomb Rats, which have no True Name, nor Aura of Death, which needs a Solar target, and 1.0 refuses all four as the port does. They move to setup 2, whose casters are Solar and have True Names.

Its enemies' turns pass too. Two of its rats' walks were the player's A, not the AI's:
- At the turn dumped as 0037, slot 6's walk ended in 15 frames, where the port's takes about 30. The original's rat went for Alaron, its leg cut at its field's edge at (33, 20), and stood within 0.14 of it without an attack. The port's, walking on, brushed past Keelin and bit him from where it arrived.
- At 0051, slot 4's walk ended in 11 frames, where the port's takes about 20. At `Combat.ExecuteAction` its body stood at exactly (30, 12), Sholeh's cell and its leg's end, and it bit her from there. At 0041 the same rat on the same path stuck against her and bit from where it stopped, as the port's does.

With A pressed where the walks ended, the port's rats end within 0.1 of the original's bodies.

Setup 2's first session (`out/parity/20261031-123809-setup2`) was logged before the logger recorded A. Every party action passes:
- Aura of Death, Wall of Bones, Dispel Necro, Light and Dispel Star;
- Wraith Touch, Charming, Debilitation, Lightning and Earth Smite resisted, several by Mirror's counter;
- vs. Star, Air Shield, Mirror and Remove Poison.

The overlay copies Mirror's counter (`+0x28`) and the sheet's protection, which the effects laid on without starting don't add. The AI record's check compares its eight action slots (`+0x0A`), which a caster fills.

The casters' turns pass but three walks:
- **Clumsiness at 0074 and 0081** was the overlay's. Alaron's Light at 0061 holds the battle at Midday (`World.LockPartOfDay`, world `+2`). The overlay laid the effect on without the hold, so the turn's clock put Morning back. The Solar Spellweavers' chance then took ×0.75 for ×1.25, a cast margin of 4 for 1.0's 9, and the resists went the other way. The landed records show it: Alaron's Clumsiness keeps margin 7, and Sholeh's 1. The overlay now lays the hold too.
- **The double cast at 0094** was the replay's. Slot 4 had two turns in a row, which the play-out ran as one. It now ends at the turn's change.
- **The walks at 0018, 0092 and 0096 aren't A.** The port plans 1.0's paths, and none of 1.0's bodies ended at its leg's end:
  - At 0092 and 0096, the port's caster stuck against another caster's body, at the same place to the bit, because its 2-tick frame repeated after the push put it back. 1.0's varying frames let its caster slide on. The port now lets the re-weigh's bare touch go and slides on too ([combat](systems/combat.md#the-ai)), the user's choice, and 0092 passes.
  - At 0096, the port's caster then decides 0.3 short of where 1.0's did, as 1.0's longer frames carry a walk further past its arrival, and Alaron is out of its reach: 5.98 to 1.0's 5.70.
  - At 0018, 1.0's Sorceror turned 20° a frame at the logger's slow frames, walked a wide curve away from its first corner and gave up. With frames of 4–6 ticks, the port's does the same.

  The last two are the cadence ([combat](systems/combat.md#where-the-port-differs)). The replay doesn't play 1.0's frame lengths, the user's choice, as it's the logger that slows them: it lists the cadence turns (`KnownCadence`) and logs their differences rather than failing them. The port's body runs on through the rest after a walk, as 1.0's does at 30 fps. Under the logger, 1.0's frames use the run up before it acts ([combat](systems/combat.md#the-ai), setup 6's 0040).

Setup 2's second session (`out/parity/20261031-134011-setup2`), with the logger recording A, passes every action, and every enemy turn but one, without A pressed:
- At 0016, the Spellweaver plans 1.0's path to (11, 17), but 1.0's curved away from its first corner, dead astern, and gave up near its start, casting Mirror on itself 4.6 from where the port's does. It's a known cadence turn in the replay. At 4–6-tick frames the port's gives up near its start too, but curving the other way. Which way 1.0's first frames carry it isn't settled: its `Facing.Turn`, run in the interpreter, turns as the port's does.
- At 0020, the Sorceror's walk had ended 0.14 short of 1.0's, against slot 5's body, and its Starlight Shield missed slot 5 by 0.003 of its reach. With the stuck body sliding on, it reaches 1.0's place and covers all four.

Setup 3's session (`out/parity/20261031-172849-setup3`) covered these, and the battle was won:
- a cast refused for Stamina;
- Lay On Hands and Heal With Herbs;
- Dougal performing;
- Alaron's Warfang put in hand from the Party tab, and its spell;
- the Stamina, Strength, Dexterity, Clarity, Defence, Healing, Charisma and Curing potions;
- Keelin's blows from behind;
- the four flasks, Acid's catching Keelin;
- the rewards.

Every enemy turn passes. So does every action but Dougal's four Fireballs. 1.0 cast them for no damage, as he still had a flask in hand ([combat](systems/combat.md#spells-potions-and-skills-in-a-turn)). The port fixes that, the user's choice, and the replay logs the four as its fix.

Three of the differences were the replay's own:
- The overlay now puts on the weapon the RAM wields and lays the skills, so the Party tab's change and the potions' bonuses carry through.
- A won battle is checked at `Combat.Exit`, with the rewards' draws on the port's loot stream.
- Acid's corrosion record is laid as the protection it takes.

One was the port's: Acid's corrosion wrapped a rat's protection byte to 254 ([magic](systems/magic.md#potions-and-flasks)).

What the session showed of 1.0's menus:
- C-up's menu leaves out a potion that can't be drunk, such as Healing at full health. It never lists Stealth, which can't be drunk in a battle.
- Acid's corrosion takes protection and isn't a poison, so the Antidote had nothing to cure here. Setup 5's darts can poison.
- Restore had nothing to undo either. Setup 6's Wraith Touch can drain a stat.

Setup 4's first session (`out/parity/20261031-185317-setup4`) crawled at a frame every three seconds. The night sky draws for its stars every frame, a logger stop each ([the rig](reference-rig.md#combat-parity-logs-toolsparityps1)), and the logger now skips those draws.

The second session (`out/parity/20261031-190448-setup4`) ran at night with no rain, and the battle wasn't finished. Every action passes:
- Sholeh's and Keelin's shots at near and far Woodsmen, moved and not;
- the Woodsmen's eight shots, style 3 keeping its distance;
- Dougal's Javelin;
- Alaron's two blows at night.

Every Woodsman's turn passes but one. At 0006, the Woodsman's first corner was dead astern. 1.0's walked 0.9 away in 6 frames of 5–6 ticks and gave up. At 2-tick frames the port's turns round and walks on 3.5 further, and at 5-tick frames it gives up within 0.37 of 1.0's. It's a known cadence turn in the replay, like setup 2's second session at 0016.

Setup 5's session (`out/parity/20261031-194638-setup5`) was won, and every action and Poisoner turn passes:
- the Poisoners' darts thrown at the party, style 3;
- Dougal's and Alaron's blows;
- the party's shots.

One dart in 13 hit, Keelin's at 0021. Its Poison enchantment (`0x0332`, level 3) was cast at him, through `Combatant.WeaponSpellProc`'s cast roll and resist, and didn't land, in 1.0 and the replay alike. The Antidote is missing from C-up's menu for a member who isn't poisoned.

One difference was the replay's own, at 0061. Rebuilding the target sets tested reach, which read Alaron's body into his cell, a cell over from where 1.0's record had last read it. The blow's height difference, which reads the cell (`ArenaGrid.HeightDifference`, `0x80067AD4`), went from −1 to 0. That raised the margin across a `SkillCheckStep`, so the blow did 42 for 1.0's 41. The replay now puts the cells back after the rebuild.

Setup 5b (`setup5b.json`, slot A) weakens two members for the poison checks. Keelin's current Dexterity is 4 and Willpower 2, and Dougal's are 4 (0 modified) and 4. A dart's chance takes off twice its target's Dexterity, and the lowered stats give Restore something to restore.

Setup 5b's session (`out/parity/20261031-201521-setup5b`) was won, and every action passes. It covered:
- the darts landing and poisoning, with poison's damage at the rounds' ends;
- Remove Poison curing it.

Every Poisoner turn passes but one, 0064. The Poisoner made for a cell 0.87 from Keelin, its target. 1.0's body stuck on hers by the push's rounding after 0.24 and gave up. The port's slides on round her, the user's choice in setup 2 (`Crowd.Rounding`), and with the slide off it ends within 0.35 of 1.0's. The replay logs it with the port's fixes (`KnownFixes`).

What the session showed:
- **Dougal had no turns.** Taking his base Dexterity to 0 (4, less his armor's 4) left him out of every round. 1.0's `TurnsPerRound` (`0x8005F4D0`) gives none at 0, and the port's matches. So no member could drink the Antidote while poisoned. The Antidote goes unchecked, the user's call: its cure is the same `cure` effect as Remove Poison's, which the session checked.
- **Restore wasn't offered in the battle**, the user saw. 1.0's Healer in a battle lays on hands or heals with herbs (`ResolveHealer`), and its Restore is the pause menu's Healer popup (`MakeHealerMenu`, `0x800034A0`, `Healer.ShowRestoreMembers`). In setup 6 the user found it doesn't open in a battle's pause, and the port now keeps it shut too ([ui](systems/ui.md)).

Setup 6's session (`out/parity/20261031-205206-setup6`), against the Wraiths, ended unfinished. It covered:
- a Wraith's Darkness;
- blows that did nothing;
- Fireball, Wind and Web at the Wraiths, halved;
- Wraith Touch on four members;
- a Restore Potion giving back the drained Dexterity.

Light wasn't cast, as base Sholeh doesn't know it. The Healer's popup didn't open in the battle's pause. Every action passes once the overlay lays Wraith Touch's drained amount: 1.0 keeps it in the record's power byte (`+0x01`), which `Entity.UndoEffect`'s kind `0x1A` gives back.

One Wraith turn differs, 0040. 1.0's Wraith turned to face for 3 frames after its walk, its body running on 0.63, and cast Wraith Touch at Dougal from (17.01, 23.13). The port's took 2 ticks to turn and decided at (17.04, 23.76), out of reach, then rested while its body ran on. It's the cadence. 1.0 decides two frames after a walk arrives, and the logger's 6-tick frames had carried its body 0.63 on by then ([combat](systems/combat.md#the-ai)). At 30 fps that's about 4 ticks, the port's 2–3. It's a known cadence turn in the replay.

Setup 7's session (`out/parity/20261031-223128-setup7`), against the Dust Devil and the Air Elemental, was won. Every action passes:
- Alaron's Wind at the Air Elemental, for nothing (Air ×0);
- Keelin's Short Sword at the Dust Devil, Cutting taking Physical's ×0.25;
- Alaron's Lodin's Sword at the Air Elemental, which hurt it: the sword is Holy, which its Physical ×0 doesn't cover;
- Dougal's Control Elem landing on the Air Elemental, which then fought the Dust Devil for the party;
- the Dust Devil's Debilitation and Lightning, and Dougal's Fireballs;
- the win with the charmed Air Elemental the only enemy left.

1.0's line for Alaron's Wind read "Air Elemental casts Wind", the user saw. A cast that does no damage logs through `Combatant.LogResisted` (`0x8006382C`), which takes the caster for the casts line and the target for the resists line. `CastCurrentSpell` passes the target as both (`0x80065C84`), so the target is named as the caster. The port's line names the caster.

Setup 7b's session (`out/parity/20261031-224749-setup7b`), against the two Salamanders (Fire ×0), passes every action and every Salamander turn:
- Dougal's Fireball did nothing, and 1.0's line read "Salamander casts Fireball", the target named as the caster again;
- his Fire Flask hurt the Salamander. 1.0's blast reads no resistance (`FlaskExplosion`, [magic](systems/magic.md#potions-and-flasks)), so a flask has no element, and the port's is the same.

The port's line names the caster, a fix ([combat](systems/combat.md#where-the-port-differs)).

Banishing wasn't reached in that session. Debilitation at 10 takes 20 from Strength, and Alaron, at 9, had no more turns: `Combatant.UpdateActive` (`0x8006615C`) makes a combatant with no Intelligence, Willpower, Dexterity or Strength left inactive, in the port as in 1.0. Setup 7's second session (`out/parity/20261031-225545-setup7`) has Alaron's Banishing landing on the Air Elemental, which passes, its 115 draws one for one. Its second battle, against the Dust Devil, was closed at its first turn. That turn's check found 1.0's Dust Devil at 31.999998 for its spawn's 32: setting its body down on the floor moved it by float noise, and its cell is read back from the body. The first turn's cells are now checked to 0.0001.

Every enemy turn passes but one. At 0024 the charmed Air Elemental's walk ended 1.17 short of 1.0's and didn't strike the Dust Devil. At the logger's 4-tick frames the port's ends within 0.01 of 1.0's and strikes, and its walk at 0021 ends at 1.0's too. It's a known cadence turn in the replay.

Setup 8's session (`out/parity/20261031-230020-setup8`), against Ugarit2's Skeletons (Cutting ×0.25, Smashing ×1.5), passes every action. Those include:
- Dougal's three Longsword blows at a Skeleton (0008, 0024, 0033);
- after the swap from the Party tab, his two War Hammer blows at the Giant Skeleton (0043, 0055);
- Keelin's blow;
- the Skeletons' and Giant Skeleton's blows, and the Skeleton Archer's three shots.

Every enemy turn passes but three, none of them the port's rules:
- **0016** is the slide. The Skeleton slid on round Keelin and struck from 0.59 past 1.0's place. With the slide off it strikes from within 0.38 of it. It's with the port's fixes (`KnownFixes`).
- **0018** is the cadence. The Giant Skeleton's walk ended 0.58 off 1.0's, out of Dougal's reach. At 3-tick frames it strikes him as 1.0's does.
- **0037** is the cadence. The Skeleton Archer, having shot Dougal, faced 162° from its leg to (33, 8) and swung wide through +Z as it turned, coming no nearer. 1.0's body gave up after 11 frames in state 5, at (32.28, 8.86). The port's turns round at 2-tick frames and walks on. At 5-tick frames it gives up too, and shoots from within 0.27 of 1.0's. The deltas logged at the entry points were 2–3 ticks, but each of the walk's frames stopped the logger for two draws, so they likely ran longer.

Setup 9's session (`out/parity/20261031-233707-setup9`) has eight battles with the Wolves, each fled at once. The setup block's bytes, as each battle began:

| Battle | How | Collision | Animation |
| --- | --- | --- | --- |
| 1 | ran at them | 2, the ambush | 3 |
| 2 | walked | 2 | 2 |
| 3, 5, 6 | sneaked | 2 | 1 |
| 4 | talked to a Wolf at its side, with a Stealth Potion | 1 | 0 |
| 7 | ran, then sneaked as they closed | 2 | 3 |
| 8 | sneaked | 2 | 0 |

Every battle's setup draws and first turn pass, the ambushes' spawn sets and the Ranger's roll among them. The user found talking from in front or behind hard to reach: the Wolves keep moving, and a sneak near them was noticed and ambushed four times.

Two differences were the port's:
- **The talk.** 1.0's collision byte is 0 within 60° of the party's heading (`0x80010AA8`–`0x80010B74`, [wanderers](systems/wanderers.md#talking)), so a talk at the side is 1, and the Wolves start facing away. The port passed 0 for every talk, so its Wolves faced the party, 6–7 for 1.0's 2–3. The port now tests the cone (`TrekLoop.TalkByte`). The replay, which had passed 0 for every battle but an ambush, reads 1.0's byte.
- **The animation byte.** 1.0 writes it each frame a hunter's notice holds, not as it catches him. Battle 7, run at and then sneaking, was caught running. Battle 8's sneak was last noticed as Alaron stood. The port took his request as the Wolf caught him, and now keeps what he was doing when last noticed (`Wanderers.NoticedAs`). The replay reads the byte from RAM, so it didn't show this.

Setup 10's session (`out/parity/20261031-235130-setup10`), against the Trolls, played as written. Every action passes:
- In the first battle, a Troll's blow killed Keelin, and the party fled by the pause.
- In the second, Alaron cast Cheat Death on himself and the Trolls' three blows at him passed through it. The last took the battle to state 13 (`Combat.StateDefeat`) and on to the game over script (21, `Combat.StateLeaveToScript`).

Every enemy turn passes but one, 0008 in the second battle. 1.0's Troll stuck on the Troll in slot 3, the two exactly touching (3.40, their radii), 2.98 from Alaron. It gave up there without a blow, though Alaron was within its reach of 3.15. The port's slides on round it and strikes him. With the slide off it stops within 0.07 of 1.0's and doesn't strike. It's with the port's fixes (`KnownFixes`), the slide being the user's choice in setup 2.

The second battle's first ring was turned 0.054° from the port's. 1.0 keeps the way round its rings from ring to ring ([combat](systems/combat.md#the-arenas)), and each ring falls 0.006° short of a turn: 9 rings in the first battle make 0.054°. The ring's check now allows for the rings laid before it in the session.

Setup 11's session (`out/parity/20261101-083418-setup11`), against Kitarak and his Hobgoblins in arena 26, was won. The pause menu had no Flee, as 0x800D9481 is clear for a boss. The journal's "We killed the goblin king Kitarak" came only once the party left the room, where the port tells it as trek fades in, the user's choice ([flags](systems/flags.md#when-the-network-comes-up-to-date)). Every action passes:
- Kitarak's 17 Tap Staminas and his Warfang blows, whose enchantment is Tap Stamina 3;
- the Hobgoblins' Spirit Shields and Tap Staminas;
- the party's blows, shots and Healing Potions;
- the win.

Kitarak never cast Crushing Death in the log. His Tap Stamina scores 55 + 5 wherever it reaches anyone, and Crushing Death 13 + 2 ([combat](systems/combat.md#the-ai)).

One difference was the replay's own. Warfang has a 15-point Stamina battery, which a cast draws before Stamina, and the overlay laid its charges but not the battery. Kitarak's casts at 0209 and 0219, with the battery spent, took his own Stamina in 1.0 and the full battery in the port. The overlay now lays each worn item's battery (its `StatMod` at `+0xC`).

Every enemy turn passes but two, neither of them the port's rules:
- **0009** is the cadence. The Hobgoblin's walk at Alaron ended 1.05 off 1.0's, out of reach, and it rested. At the logger's 4-tick frames the port's strikes him from within 0.08 of 1.0's. It's a known cadence turn in the replay.
- **0068** is the animation. The newcomer Hobgoblin in slot 6 cast Spirit Shield on itself. 1.0's AI chose (19, 10), as the port's does, with the same scores and path, run in 1.0's interpreter on that RAM. But its actor was mid-fidget, which takes no run request, so its body never moved, and the stuck timer gave up after 30 ticks on its cell, (19, 12) ([combat](systems/combat.md#the-ai)). The replay starts every body idle, so the port's walked to (19, 11.14). Seeded in the fidget's state and clock, it acts from (19, 12) as 1.0's does. It's logged with the known cadence turns until the replay lays each actor's state from the RAM.

The user saw the three Hobgoblins that joined once the first three fell cast their buffs and sometimes Tap Stamina, and then neither close in nor strike. From dump 0100 on they only rested, in 1.0 and the port alike. 1.0's AI scores Tap Stamina from their own cells with their 2-cell footprints lifted off the grid, then builds its targets with them on, which blocks the cast toward the party, at +X and +Z. So they rested every turn ([combat](systems/combat.md#the-ai)). The port fixes it, the user's choice: they cast what scored. The replay lists the 32 Hobgoblin turns that now differ with its fixes.

Setup 12's session (`out/parity/20261101-092600-setup12`) has five battles against Lugash, in arena 18. Battles 1–3 were lost: his Wall of Bones held Alaron and Sholeh, the only two who could hurt him, through Alaron's vs. Necromancy. Battle 4 fled at once, as 5250's rule allows (`Combat.ApplyFleeRuleByFlag`, after `Combat.Setup`'s dump, which reads 0). Battle 5 can be ignored. Every action and every Lugash turn passes:
- his Wall of Bones on all four members and Clumsiness on Keelin;
- Sholeh's four Webs of Starlight at him, none of which held;
- Alaron's blows with Lodin's Sword, Holy;
- Fireball, Debilitation, Wind, the shots and Keelin's blow, which did nothing.

One difference was the replay's own. The overlay laid each skill through `CharacterSkills.Train`, which holds a base at the trainer's cap. Lugash's record has Wizard 15, over it, so a Train of 0 took his base to 10. The overlay now lays the skills' bytes as they are. The rules read the base through the cap.

No Cheat Death was on Lugash in 1.0's RAM: his effect records were empty at every turn.

Setup 12b's session (`out/parity/20261101-100932-setup12b`), with Dougal and Keelin wielding Lightreavers, won. Every action and Lugash turn passes, and so does the win at `Combat.Exit`: 8257 experience, a Restore Potion, the Staff of Lugash, Royal Platemail, a Sapphire Gem and 20 Spice. The kill flag 533 was set ("The lich is dead."), and 1.0 showed its journal entry at once on the return to trek, as the port's does as trek fades in.

Setup 13's session (`out/parity/20261101-102351-setup13`), against the three Gorgons in arena 12, ended with the leader (slot 4) and slot 5 dead and slot 6 at about a quarter. Every action passes, the Gorgons' Frozen Dooms on the party among them, with no lock. Every Gorgon turn passes but one:
- **0062** is the cadence. The player's A put the Gorgon at its leg's end, (19, 27), 2.25 from Sholeh for a reach of 2.14. Through the 4 frames before it decided, 3 ticks each, 1.0's body ran on 0.34 toward her and struck from 1.78. The port's decides 2 ticks after the walk and rests. It's a known cadence turn in the replay, like setup 6's 0040.

None fled. From 0025 on, slot 6 was asked every turn: the leader gone, it was the only one able. Its morale came to about 8: 11, −1 for Lunar by day, −4 for the leader gone and +2 for the dead leader's 11, which `Combat+0x11` keeps. So it fled only on a 2d6 roll over 8, about 28%. On six of its eight checks it was engaged, so a flight couldn't start. Neither of the other two rolled over. The port's checks are the same, draw for draw. A cornered flight is still unchecked ([combat](systems/combat.md#not-settled)). Sholeh's Solar Wrath on the two that aren't the leader would bring one: it makes a check due and takes 8 from their morale.

Still to do:

- **Setup 2's walks,** above: which way 1.0's Spellweaver curved at the second's 0016.
- **Positions, shown but not checked:**
  - The attacker's body after a blow stands up to 0.5 from the original's (Brenna's backstab), with its footprint a cell over.
  - A reserve's walk with no action ends 4.6 from the original's (the second trial's slot 5). The original's body moved 0.26 from where it spawned at (17, 43) and gave up after 12 frames, about the 30 ticks of coming no nearer, while the port's runs its leg toward (23, 49).
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
| 1 | MiscInteriors E05 #1 (24, 64) | 27 | 12 Tomb Rats, weak to fire ×1.5, reserves | The party's spells: Fireball placed to catch a member; Dragon Flames by level; Lightning; Earth Smite; Whitefire; Immolation and Acid Bolt over rounds; Web of Starlight and Frozen Doom, with the disable limit while reserves are to come and Web's escape; Stellar Gravity and Haste; Solar Wrath putting them to flight; Poison to a death; Tap Stamina; Darkness then Light; Sense Aura at levels 1, 5 and 9; a cast failed, one refused for its reagent and one for Stamina; a scroll and a wand; a level-0 spell |
| 2 | ChaosIsle A07 #8 (27, 27) | 17, no flee points | 2 Chaos Spellweavers, 2 Chaos Sorcerors | Enemy casters: Starlight Shield, Mirror, Wall of Bones, Web, Darkness, Clumsiness, Crushing Death, Whitefire and Lightning at the party; the party's spells into Mirror; Dispel Star on their shields; Wall of Bones, Charming and Aura of Death, which need a True Name or a Solar target; the wards (vs. Star); enemies that can't flee |
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

- **Setup 1, `setup1.json`, once in the pak's slot A.** The party stands 8 west of the Tomb Rats, facing them. The spells:
  - Alaron: Acid Bolt, Aura of Death, Haste, Tap Stamina, Wraith Touch, Wall of Bones and Darkness at 5, and Sense Aura at 1.
  - Sholeh: Whitefire, Web of Starlight, Frozen Doom, Stellar Gravity, Solar Wrath and Light at 5. She wears the Sense Aura (level 5) and Charming scrolls.
  - Dougal: Fireball, Dragon Flames and Earth Smite at 5.
  - Keelin: Lightning and Immolation at 4, Poison at 3 and Dragon Flames at 1. Keelin wears the Lightning wand and starts with 6 Stamina, so a cast gets refused for Stamina. The low Stamina leaves Keelin 38 of 72 HP.

  The party has 2 Herbs, so the third of Frozen Doom, Wall of Bones and Sense Aura gets refused for its reagent. The pak can't hold a spell at level 0, so the rise is tried at level 1 alone. Sense Aura is cast at 1 and 5, not 9.
- **Setup 2, `setup2.json`, once in slot B.** Flag 3078 is cleared, so party #8 is back. The other five parties of A07 stay beaten. The party stands 7 from them, facing them. On top of the story's spells:
  - Alaron: Dispel Necro, Light, Aura of Death and Wall of Bones at 5. Wraith Touch was taken off him after it crashed 1.0 on landing (`out/parity/20261031-132738-setup2`): a party caster's crashes 1.0, which play can't reach ([combat](systems/combat.md#what-spells-look-like)).
  - Dougal: Lightning and Earth Smite at 5.
  - Keelin: vs. Star at 5 and Lightning at 4.

  Sholeh wears the Charming scroll. Her Dispel Star and Dougal's Mirror are the story's.

- **Setup 3, `setup3.json`, once in slot C, now 8's.** The party stands 8 west of the Giant Rats, facing them, 22 from M08's wolves. On top of `base.json`:
  - Hurt, for the Healer and the potions: Alaron at 102 of 117 HP and Dougal at 108 of 122, their Level's points spent (`--current Level`). Keelin is at 35 of 72 with 3 Stamina, so a spell can be refused for Stamina before a Stamina Potion is drunk. Setups 1 and 2 never had a Stamina refusal.
  - Flasks: Fire ×3, Inferno, Sleep Gas and Acid ×2. Dougal (Thrown 10) throws them.
  - The potions the inventory lacked: Antidote, Dexterity, Clarity, Charisma and Defense ×2. Healing, Stamina, Curing, Restore, Strength and Stealth were there already.
  - Dougal's Fireball 6 is from the story. It's there to place a blast that catches a member, which setup 1 didn't do.
- **Setup 4, `setup4.json`, in slot D.** At 22:00, Night. The party stands on the slope 11.4 north-east of the Woodsmen (party #2, `0x025B`, radius 10), facing them, 24 from the Ogre (#0, radius 20) and 17 from the Wyverns (#1, radius 10). Keelin wears the Short Bow, and Dougal has 4 Javelins.
- **Setup 5, `setup5.json`, once in slot A, now 5b's.** The party stands 11 west of the Goblin Poisoners (Overworld E09 #5, `0x0284`, level 5, radius 10), facing them, 37 from the Cave Bears (#4). The inventory has 3 Antidotes on top of the base's potions, and Keelin knows Remove Poison 3 from the story.
- **Setup 6, `setup6.json`, once in slot B, now 7b's.** The party stands 8 south of the four Wraiths (EhudUnder E03 #4, `0x0265`, level 20, radius 10), facing them, in the only open floor round them. A teleporter (#3) is 2 beyond the Wraiths. Restore is Sholeh's Healer 10 with the party's Herbs, or one of the 3 Restore Potions.
- **Setup 7, `setup7.json`, once in slot A, now 9's.** The party stands 8.4 from the Dust Devil and Air Elemental (Overworld N26 #2, radius 20), facing them, 28 from the Giant Scorpions (#1, radius 20). Both elementals are level 20, Air ×0 and Morale 12; the Dust Devil is Physical ×0.25 and casts Debilitation, Wind and Lightning at 10, and the Air Elemental is Physical ×0 and has a True Name. On top of `base.json`, Alaron knows Banishing 10 (Naming, a Gemstone, range 1) and Dougal Control Elem 10 (Elemental, a Spice, range 2), and the inventory has 3 Fire Flasks. Alaron's Wind 10 and Keelin's Wind 3 are the story's, as are the 99 Gemstones and Spices.
- **Setup 7b, `setup7b.json`, once in slot B, now 10's.** The same party, 7.9 from the two Salamanders (N26 #3, level 17, Fire ×0, radius 20), facing them, 46 from the Scorpions. Neither battle depends on how the other went. Dougal throws the Fire Flasks, and his Fireball 6 is the story's.
- **Setup 8, `setup8.json`, in slot C, once 3's.** Flag 1240 is cleared, so Ugarit2 A07's #3 is back (8–12 of Skeleton Archers, Giant Skeletons and two kinds of Skeleton, all Cutting ×0.25 and Smashing ×1.5). #2, 11.6 along the corridor, stays beaten (1241). The party stands at (12.5, 3.0), 8 from #3, facing it. Dougal wields a Longsword (Cutting, damage 3, hit 45) for the story's Enchanted Blade, which is Magic, and the War Hammer in the inventory is Smashing, damage 3, hit 60: his Sword and Hafted are both 10.

- **Setup 9, `setup9.json`, in slot A.** `base.json` unchanged but for the place: on A04's plateau (height 20), 20 north of the four Wolves (#10, level 2, perception 10, radius 20), facing them. Teleporter #5 is 3.6 west of their home, so a walk past it leaves the chunk. The Hobgoblins (#13) and Goblins (#11) are 20 below, too low to ambush (|Δy| < 3). With 458 set, the Sergeant's party (#14) is gone and the Chaos Slayer's (#15, radius 50) is 61 off. Alaron's Stealth 10 is the party's best, which makes the sneaking and standing ranges 0.2 of their full: about 1.2 from the point 3 ahead of a Wolf. Sholeh's Ranger 6 is the party's best, for the ambush's roll.
- **Setup 10, `setup10.json`, in slot B.** The party stands 10 east of the 12 Trolls (N24 #0, level 15, Strength 40, radius 8), facing them, 37 from O24's Cyclops (#2, radius 15). The only floor near the Trolls runs east from them. Alaron knows Cheat Death 5 (a Gemstone, 5 Stamina) and has 10 Stamina, at 48 of 117 HP. Keelin has 2 Stamina, at 34 of 72.
- **Setup 11, `setup11.json`, in slot A.** `base.json` with 154 (Kitarak beaten), 1210 (his party talked to) and 151 (his conversation's done flag) cleared, so Kitarak's party (GoblinCamp C01 #3, `0x021D`, level 10, with 6 Hobgoblins, level 5) is back. 3122 is set, so the Chaos Major's party (#4, enabled by 458), 6 from Kitarak's home, is gone. The party stands at (13, 6) inside the room's door (teleporter #1), 10 from Kitarak, facing him. His conversation (`0x03CF`) is six lines and a battle. He wields Warfang (Magic) and knows Tap Stamina 5, Spirit Shield 4 and Crushing Death 2. The Hobgoblins have Great Axes, Tap Stamina 5 and Spirit Shield 4. Both are Naming ×1.25.
- **Setup 12, `setup12.json`, in slot B.** `base.json` with 533 (Lugash beaten) and 548 (Fate recovered) cleared, so 5250 holds: its inputs 532, 533, 548 and 4279, as bits, are 8. That enables the dialogue object #9 (`0x04D1`, tangible, radius 10) and Lugash's party #11. The party stands at the stair's foot (`stairbottom`, (8.6, 14.6)), 15.7 from the dialogue, facing Lugash. With 548 clear, the script runs Lugash's speech to a menu, whose "Give me the horn." sets 532 and starts the battle, in arena 18. Lugash (level 25, Lunar) is Physical ×0 and Magic ×0. He knows Whitefire, Darkness, Wall of Bones and Crushing Death at 15, and Clumsiness at 10. Alaron's Lodin's Sword is Holy. The script's 532 takes 5250 down, so each try needs the save loaded again.
- **Setup 12b, `setup12b.json`, in slot B, once 12's.** Setup 12 with Dougal and Keelin each wielding a Lightreaver, a Holy sword with no Strength required (damage 10, hit 100, Physical ×0 for its wearer), so three members can hurt Lugash.
- **Setup 13, `setup13.json`, in slot C.** `base.json` but for the place: (52, 49), at the east mouth of the Gorgons' hollow, 15 from their home (MiscInteriors A09 #1, `0x0217`, radius 20), facing it. The three Gorgons are level 10, Lunar, Physical ×0.5 and morale 11, with Frozen Doom 10. Arenas 12–13 have one flee point. The leader isn't asked its morale while it lives, so a flight comes from Sholeh's Solar Wrath 8, or from the others falling.

Setups 5 and 6 replaced 1 and 2 in slots A and B, then 7, 7b and 8 replaced 5b, 6 and 3 in A, B and C, 9 and 10 replaced 7 and 7b in A and B, 11, 12 and 13 replaced 9, 10 and 8 in A, B and C, and 12b replaced 12 in B. Slot D holds 4. To fight an earlier one again, write its save back with `hbht save to-pak`.

Every log also settles the frame's delta in combat, and the first one where in a turn an attack's draws fall, both in [combat](systems/combat.md#not-settled). Each party above is always enabled but Kitarak's (while 154 is clear) and Lugash's (5250); others can stand in from the base plugin's monster parties.
