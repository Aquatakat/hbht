# Combat parity (2.9)

Roadmap 2.9's differential tests: the port's [combat](combat.md) checked against 1.0's own execution. The combat checkpoints hold one battle, the party against ten rats, which pins the setup, the turn order, the rats' AI, melee and ranged at a few pairs, the casters' choices (`CasterCheckpointTests`, over the vectors `tools/aivectors` writes from 1.0's functions in its interpreter), the Fire and Sleep Gas flasks' blasts and the rewards. Past that, the campaign has two halves:

- **The flow**: battles the user plays in 1.0 from saves written for them, logged by `tools/parity.ps1` and replayed into the port. How a setup's save is made and a battle logged is the [reference rig](../reference-rig.md#combat-parity-logs-toolsparityps1)'s.
- **The formulas**: 1.0's own functions run in the interpreter on a checkpoint's or a log's RAM with the inputs varied, against the port's with the same draws ([below](#the-formula-vectors)). Melee's are done.

What the logs settled about 1.0 itself is written in the topic docs: [combat](combat.md), [combat-actions](combat-actions.md), [combat-ai](combat-ai.md) and [magic](magic.md).

## The replay

`ParityReplayTests` (`HBHT.Rom.Tests`) replays every battle under `out/parity` (or `HBHT_PARITY`), each a `battle-NN` folder that `ParityLog` reads, and skips without them. The battle is built from the RAM at `Combat.Setup` (`CombatCheckpoints`), as `BattleCheckpointTests` builds checkpoint 01's. Before each of the port's combat stream's draws, the stream is set to where the original's was before its draw of the same rank (`Lcg.BeforeDraw`). Draws whose callers are cosmetic (`CosmeticCode`: the weather, particles, audio keys, the sky and the spells' visuals) are skipped, since the port draws them on its cosmetic stream or not at all; the ring's retries, which the port draws on its cosmetic stream, are fed from the original's ring draws. The log unwinds a draw only three callers deep, so a deep ring retry's callers are all `CombatMarkers.RingPoint`, with nothing of the turn it's for.

It checks:

- **The setup** (`TheSetupDrawsAsManyAsTheOriginals`): its draws, one for one, and no `Battle.Problems`.
- **The first turn** (`TheFirstTurnIsTheOriginals`): whose it is, the turn order, and each combatant's cell (to 0.0001), direction, footprint and radius.
- **The first ring** (`TheFirstRingIsTheOriginals`): as many ring draws, and every teardrop within 0.01, turned by the rings 1.0 laid before it in the session (`RingsBefore`), as 1.0's statics drift 0.006° a ring.
- **Each action** (`EachActionIsTheOriginals`): the RAM at each turn's first `Combatant.ExecuteAction` laid over the battle, and the port's `Battle.ExecuteAction` run with the original's attacker, target and kind (the spell, potion and targets as chosen).
- **Each enemy's turn** (`EachEnemyTurnIsTheOriginals`): the port's AI playing from the RAM at its `TurnOrder.BeginTurn`, the bodies moved by their animations' roots, against the original's `Combatant.ExecuteAction`: whether it acted, whom it went for, and where it stood, to 0.5.

After an action or an enemy's turn, the next turn is checked as it begins, at the first cue after the port's `Advance` has chosen it, against the RAM dumped there: the draws' count, whose turn it is, every combatant's stats, skills, protection, weapon, death and flight, and each living enemy's AI record (kind, target, morale and flags). Bodies' places and the grid are shown, not checked. A won battle is checked at `Combat.Exit`, with the rewards' draws on the port's loot stream.

**The overlay** (`CombatCheckpoints.Overlay`, `OverlayEffects`) lays a dump's RAM over the port's battle. It must lay everything the next turn reads, and each of these was missing once:

- each combatant's effects, slot for slot with their timers, laid on without starting them, so what starting does is laid apart: Mirror's counter (`+0x28`), the sheet's protection, the battle's held part of the day (Light or Darkness's), Acid's corrosion as the protection it takes, and Wraith Touch's drained amount, which 1.0 keeps in the record's power byte (`+0x01`);
- the weapon the RAM wields, and the skills' bytes as they are (not through `CharacterSkills.Train`, which caps a base at the trainer's 10: Lugash's Wizard is 15);
- each worn item's battery (its `StatMod` at `+0xC`), which a cast draws before Stamina;
- the round's turn order, and reserves that have joined at the RAM's cells;
- the cells as 1.0's records last read them, put back after the target sets are rebuilt, since the height term of an attack's chance reads the cells (`ArenaGrid.HeightDifference`).

**The player's A in a walk** (`PressedA`): A skips an AI's walk, putting the body at its leg's end ([combat-ai](combat-ai.md#a-turn)). The logger records the press (`Combat.InputMoving` at its call of `Body.GiveUp`) and lists what it watched at `Combat.Setup`, and the replay presses A as many ticks into the walk. For logs made before it watched (setup 2's first session and earlier), the replay infers it: a walk the port's outlasts at six ticks a frame, with the original's body at the port's leg's end after it.

**Known differences** are two lists keyed by battle and dump, whose turns are logged rather than failed; one that matches fails until it comes off its list:

- **`KnownCadence`**: enemy turns that differ by 1.0's frame lengths alone. The logger slows 1.0 to frames of 3–6 ticks, where the port steps 2, so a body that sets off facing away curves wider and may give up, and a walk carries further past its arrival before 1.0 decides ([combat-ai](combat-ai.md#where-the-port-differs)). The replay doesn't play 1.0's frame lengths, the user's choice, as it's the logger that slows them. A turn goes on the list only once its difference is shown to be the frame lengths'.
- **`KnownFixes`**: actions and turns where the port fixes one of 1.0's bugs or differs by the user's choice.

## The battles logged

The party for most is Alaron, Sholeh, Dougal and Keelin, from story save A, with each setup's spells, gear and inventory; the saves are in `out/parity/saves/`. Each log is a folder of `out/parity/`.

| Setup | Log | Enemies, arena | Covered | Differs |
| --- | --- | --- | --- | --- |
| trials | `20261031-094146-trial`, `20261031-100733-trial2` | the Combat checkpoints' ten rats | backstabs; Solar Wrath on three rats; Endurance; a Strength Potion; the rats' bites; two flee checks, one a flight; a reserve's walk | |
| 1 | `20261031-105935-setup1`, `20261031-120218-setup1` | 12 Tomb Rats (Fire ×1.5), arena 27 | Fireball, Dragon Flames, Earth Smite, Whitefire, Lightning, Immolation, Acid Bolt, Tap Stamina, Haste, Darkness, Light, Stellar Gravity, Solar Wrath, Poison over rounds, Frozen Doom, Sense Aura and its scroll, the Lightning wand; Web failing its roll; a cast refused for its reagent; the Fire weakness making the morale check due; reserves refilling three slots; Wraith Touch, Wall of Bones, Charming and Aura of Death refused for their requirements; two walks skipped by A | the first session ends where 1.0 locked on a landed Web ([combat-actions](combat-actions.md#where-the-port-differs)) |
| 2 | `20261031-123809-setup2`, `20261031-134011-setup2` | 2 Chaos Spellweavers, 2 Chaos Sorcerors, arena 17 (no flee points) | Aura of Death, Wall of Bones, Dispel Necro, Light, Dispel Star; Wraith Touch, Charming, Debilitation, Lightning and Earth Smite resisted, several by Mirror's counter; vs. Star, Air Shield, Mirror, Remove Poison; the casters' buffs; bodies sliding round each other | cadence: 0018, 0096 (first), 0016 (second). `20261031-132738-setup2` ends where a party member's Wraith Touch crashed 1.0 |
| 3 | `20261031-172849-setup3` | 10 Giant Rats, arenas 2–3 | a cast refused for Stamina; Lay On Hands and Heal With Herbs; a performance; a weapon put in hand from the Party tab, and its spell; eight potions; blows from behind; the four flasks, Acid's catching a member; the rewards | fixes: 0097, 0103, 0113, 0120, Fireballs cast with a flask in hand |
| 4 | `20261031-190448-setup4` | 4 Bandit Woodsmen, arenas 6–7, at night | shots near and far, at targets moved and not; style 3 keeping its distance; a thrown Javelin; blows at night | cadence: 0006 |
| 5 | `20261031-194638-setup5` | 4 Goblin Poisoners, arenas 2–3 | darts thrown, style 3; a dart's Poison enchantment cast and resisted; the win | |
| 5b | `20261031-201521-setup5b` | the same, two members weakened | darts poisoning, and poison at the rounds' ends; Remove Poison; a member of base Dexterity 0 getting no turns | fix: 0064, the slide |
| 6 | `20261031-205206-setup6` | 4 Wraiths (Physical ×0, Magic ×0.5), arena 23 | a Wraith's Darkness; blows doing nothing; Fireball, Wind and Web halved; Wraith Touch on four members; a Restore Potion giving the drain back | cadence: 0040 |
| 7 | `20261031-223128-setup7`, `20261031-225545-setup7` | a Dust Devil and an Air Elemental, arenas 14–15 | Wind at Air ×0; Cutting under Physical ×0.25; a Holy sword through Physical ×0; Control Elem, and the charmed Air Elemental fighting for the party; Debilitation and Lightning at the party; Banishing landing; the win with only the charmed one left | cadence: 0024 |
| 7b | `20261031-224749-setup7b` | 2 Salamanders (Fire ×0), arenas 14–15 | Fireball doing nothing; a Fire Flask hurting them | |
| 8 | `20261031-230020-setup8` | Ugarit2's Skeletons (Cutting ×0.25, Smashing ×1.5), arena 24 | a Longsword, then a War Hammer put in hand from the Party tab; the Skeleton Archer's shots | fix: 0016, the slide; cadence: 0018, 0037 |
| 9 | `20261031-233707-setup9` | 4 Wolves, arenas 4–5 | eight battles fled at once: the setup block's collision and animation bytes by how they caught or were talked to (below), the ambush's spawn sets and the Ranger's roll | |
| 10 | `20261031-235130-setup10` | 12 Trolls, arenas 4–5 | a member killed and the party fleeing by the pause; Cheat Death on Alaron; his death and the game over script | fix: battle 2's 0008, the slide |
| 11 | `20261101-083418-setup11` | Kitarak and 6 Hobgoblins, arena 26 by the terrain | no Flee; Kitarak's Tap Staminas, his Warfang's blows, enchantment and battery; the Hobgoblins' Spirit Shields; the win; the kill flag | cadence: 0009; 0068, a fidget; fixes: 32 Hobgoblin turns, their own footprint |
| 12 | `20261101-092600-setup12` | Lugash, arena 18 | five battles: three lost to Wall of Bones, one fled by 5250's rule; Clumsiness; Webs that didn't hold; Holy blows; no Cheat Death on him | |
| 12b | `20261101-100932-setup12b` | Lugash, his party armed with Lightreavers | the win at `Combat.Exit`: 8257 experience, his loot, kill flag 533 | |
| 13 | `20261101-102351-setup13`, `20261101-104334-setup13` | 3 Gorgons, arena 12 (one flee point, past the grid) | Frozen Doom on the party; morale checks with the leader dead; Solar Wrath putting two to flight, each leaving at once; a body set down on a crease of the floor, drawing to the side after the turn order | cadence: 0062 |

Setup 9's setup block, as each battle began:

| Battle | How | Collision | Animation |
| --- | --- | --- | --- |
| 1 | ran at them | 2, the ambush | 3 |
| 2 | walked | 2 | 2 |
| 3, 5, 6 | sneaked | 2 | 1 |
| 4 | talked to a Wolf at its side | 1 | 0 |
| 7 | ran, then sneaked as they closed | 2 | 3 |
| 8 | sneaked | 2 | 0 |

## The known differences

**The cadence** (`KnownCadence`):

- Setup 2's 0018 and second session's 0016, setup 4's 0006 and setup 8's 0037: the first corner lies astern, and 1.0's body, turning 20° a frame at the logger's long frames, walks a wide curve away and gives up near its start; the port's turns round and walks on. At 4–6-tick frames the port's gives up too; at the second session's 0016 it curves the other way ([combat-ai](combat-ai.md#not-settled)).
- Setup 2's 0096, setup 6's 0040, setup 7's 0024, setup 8's 0018, setup 11's 0009 and setup 13's 0062: 1.0 decides two frames after a walk arrives, and the logger's frames carry its body on into (or out of) reach before it does; the port decides 2–3 ticks after, as 1.0 does at 30 fps.
- Setup 11's 0068 isn't the cadence: 1.0's Hobgoblin was mid-fidget (its actor's state 12, clock 27 of 101), which takes no run request, so its walk gave up on its cell. The replay starts every body idle; seeded in that state, the port's does as 1.0's.

**The port's fixes** (`KnownFixes`):

- Setup 3's four Fireballs, cast with a flask in hand, do their damage.
- Setup 5b's 0064, setup 8's 0016 and setup 10's 0008: 1.0's body stuck on another by the push's rounding and gave up; the port's slides on (`Crowd.Rounding`). With the slide off, each ends within 0.4 of 1.0's.
- Setup 11's 32 Hobgoblin turns: a 2-cell caster's own footprint no longer blocks its cast.

The fixes the logs found besides, which the replay doesn't list because it reads 1.0's RAM: a talk's collision byte by the cone (`TrekLoop.TalkByte`) and the animation byte as what the player was doing when last noticed (`Wanderers.NoticedAs`), both from setup 9; a 2-cell caster's footprint, setup 11; the body set-down's draws after the turn order, setup 13; corrosion off the protection byte, which wrapped to 254, setup 3; and the casts' line naming the caster, setups 7 and 7b.

## The formula vectors

1.0's own combat functions run in the MIPS interpreter (`tools/aivectors`) on a battle's RAM, with one input changed at a time and the draws fixed, and the port's same functions on the same state with the same draws. A vector is a state (a Combat checkpoint, or a parity log's dump), its pokes (typed changes: a stat, a skill, a place, a facing, a cell's height, the part of the day, an effect, a weapon in hand…), the stream's seed and any draws scripted to come up a value, and what came of it as `name=value …`, the draws' count among them. `tools/aivectors/formulas.py <family>` writes a family's table (`tests/HBHT.Rom.Tests/Formulas/<Family>Vectors.cs`); the [reference rig](../reference-rig.md#replaying-the-originals-ai-toolsaivectors) has the commands.

| Function | Varied | Done |
| --- | --- | --- |
| `MeleeChance`, `ResolveMelee`, `MeleeDamage`, `ApplyResistance` | Strength tiers; from behind, with Thief; height up and down; each part of the day; a Lunar and a Solar attacker; no Stamina, either side's; a held target; a shield, from the front and behind; Warrior's adjusted roll; the Troubadour's bonus; a weakness, a resistance, one of 0; a weapon's spell (`WeaponSpellProc`); the weapon skill's roll landing and missing | `MeleeVectorTests` |
| `RangedChance`, `ResolveRanged` | distance past 6; rain's intensity; night; a target that has moved or acted | |
| `ThrowChance`, `FlaskDamage`, `FlaskExplosion` | each flask; fog, storm and night with distance; Acid's corrosion; a thrown weapon | |
| `CastSpell` (`CheckSpellAvailable`, `PrepareCast`, `CastChance`, `ResistRoll`, `ApplySpellEffect`, `SpellDamage`) | every spell kind; each part of the day, and the moon's phases for a Lunar caster; failure; the reagent, Stamina and Field refusals; a requirement unmet; wards and aspects in the resist; Mirror's counter; the disable limit with reserves to come; the level's rise at 0 and 1; an item's spell and its charge | |
| `AddEffect`, `AgeEffects` (`EffectRules.Round`, `End`), `Dispel`, `CurePoison`, `TakeDamage` | every lasting kind over its rounds: Poison, Immolation, Acid Bolt, Web's escape, Photosynthesis by day and night, Tap Stamina, Wraith Touch, the morale spells given back; Cheat Death; death taking the effects | |
| `DrinkPotion`, `CanDrink` | every potion, each where it's refused | |
| `ResolveHealer` (`LayOnHands`, `HealWithHerbs`), `StartTroubadour` | Healer and Troubadour levels, Stamina short, a performance already going | |
| `UpdateRadius`, `TurnsPerRound`, `TurnOrder.Build` | each Dexterity threshold; Haste; Stellar Gravity; engaged; performing | |
| `FillEnemies`, `ChooseArena`, `BattleAmbushRoll` | parties of every shape, the level trim; every terrain; the Ranger's margins | |
| `TallyRewards`, `RollChestItem` | chests with gold (the doubling) and drops with worth | |
| `EnemyAI.Step`, `CheckFlee`, `Morale` | styles 1–4 as themselves; the morale spells; a weakness making the check due; the item-spell cases `CasterCheckpointTests` leaves out; `tools/aivectors/extras.py`'s vectors, which no test reads yet | |

**Melee** (`MeleeVectorTests`, 219 vectors): `IsBehind`, `MeleeChance` as `ResolveMelee` calls it, then `ResolveMelee` with the damage, the resistances and the weapon's spell. Each gives whether the blow is from behind, the chance, the d100 and the roll as Warrior turned it, the damage, the draws, the weapon skill's base after, the target's AI flags after and the attacker's cast margin after. 171 vectors are on checkpoint 05 (Alaron and rat 4, made to give chances from 5 to over 1000), varying each input of the table, the edges among them: 45° off the target's facing and 90°, a roll at Warrior's limit and one past it, a fifth of the chance under Warrior, both sides of each Strength tier, and the weapon skill's roll at its threshold and one over, on a hit and a miss. The resistances run by five weapons' elements (Cutting, Smashing, Physical, Fire, Holy) against weaknesses, resistances, immunity, Physical's and Magic's reach and two entries at once; the weapon's spell by twelve weapons, landed, missed, and with a cast margin kept from earlier in the turn. 48 more are on seven parity logs' first turns, for real entities: the Skeletons, the Wraiths, Lugash against the Lightreavers, the Air Elemental, the Tomb Rats against Firedrake Fang, Kitarak's Warfang and its spell at the party, and the bandits at night.

**All agree with 1.0**, so nothing was fixed and nothing is listed as a known difference. What the run settled about 1.0 is in [combat-actions](combat-actions.md#an-attack) and [magic](magic.md#casting): a weapon's spell spends no charge, an attacker that has acted this turn casts it at the margin it kept, the resistance entries' order, an entry of element 0 never applying, and the three holding kinds (34, 53, 57). A mutation of the port's ×1.15 fails the two vectors it should.

### Adding a family

A family is one Python module and one C# test class, which touch nothing of the others':

1. **`tools/aivectors/families/<name>.py`**: `FAMILY` (the C# class's stem), a docstring (the table's comment), `cases()` listing the vectors and `run(vector)` running one. A vector is a dict of `id`, `source` (`cp05`, or `log:<session>/<battle>:<dump>`), `pokes`, `args` (the slots and the like the test reads), `seed` and `script` (`{draw index: raw value}`; `draws.d100(r)` is a d100 coming up `r`). `run` builds a `state.State(source, pokes, seed=, script=)`, calls the function (`st.call(address, args, sp_args=)`; arguments past the fourth go on the stack from `sp+0x10`), and returns the results as `[(name, value), …]` and `st.draws.callers()`. `melee.py`'s `add(…, steer=(function, value))` fixes the first draw a function makes, once a dry run has found its index.
2. **`python tools/aivectors/formulas.py <name>`** writes `tests/HBHT.Rom.Tests/Formulas/<Family>Vectors.cs` and `out/aivectors/<name>.json`. `--only text` runs a few and prints them, writing nothing.
3. **`tests/HBHT.Rom.Tests/Formulas/<Family>VectorTests.cs`**: for each vector, `FormulaState.Build(set, v.Source)`, `FormulaState.Poke(built, v.Pokes)`, then `new FormulaState.Draws(built.Game.Random.Combat, v.Seed, v.Script)` before the port's call, and `FormulaState.Differences(v.Expect, actual)` over the same names. `MeleeVectorTests` is the pattern, with its `KnownDifferences`: a vector where the port differs on purpose is logged with its reason rather than failed, and fails once it agrees.

- **Pokes:** the shared ones are in `state.py` and `FormulaState.Pokes`, each on both sides. A family's own go in a dict of its own on both sides (`State(…, extra=)`, `FormulaState.Poke(…, extra)`), so that sibling families don't edit the shared files at once. A poke changes only what its 1.0 side writes: `weapon` sets the pointer, so the port's puts the weapon in hand whoever may wield it and puts the stats back after.
- **Draws:** every draw on the main stream goes through `Rng.Next` or `Rng.Float0To1`, which `draws.py` hooks (`Float0To1` steps the stream itself). The port's `Lcg.BeforeDraw` does the same by index. A draw's function is named by unwinding the generator's frames by their prologues, as the parity logger does.
- **Stubs:** none were needed for melee; the log lines and `sprintf` run in the interpreter. `Entity.SenseAura` opens a popup that halts the interpreter in `__osEnqueueAndYield`, so a casting family stubs it (`st.cpu.hooks[address] = lambda cpu: 0`). `PrepareCast` sets the acted flag; each vector is a fresh copy of the RAM, so nothing carries from one to the next.
- **States:** a parity log's `0002.rdram.gz` is its `Combat.StartFirstTurn`, where every slot is the setup's; a later dump works too, with the reserves that joined brought in (`CombatCheckpoints.FromLog`). A function that reads the cells reads them as the records last read the bodies (`ArenaGrid.HeightDifference`), which the overlay lays.
- **Cost:** a vector runs in a few milliseconds in the interpreter and in about 0.1 s in the test, after the import.

## What's left

- **Which way 1.0's Spellweaver curved** at setup 2's second session's 0016 ([combat-ai](combat-ai.md#not-settled)).
- **The actors' states**: the replay starts every body idle; laying each actor's state and clock from the RAM would take setup 11's 0068 off the list.
- **Positions shown but not checked:**
  - The attacker's body after a blow stands up to 0.5 from the original's (a backstab), with its footprint a cell over.
  - A reserve's walk with no action ends 4.6 from the original's (the second trial's slot 5): 1.0's body moved 0.26 from its spawn at (17, 43) and gave up after 12 frames, while the port's runs its leg toward (23, 49).
  - Reserves that join at a round's end can take other spawn points than the original's, on the same draws. In setup 1, at the end of slot 5's turn, slot 6's reserve stands at (13, 26) for the original's (24, 39), and slot 7's at (24, 39) for (19, 23). The two disagree about which point is free, likely over a body that has fled or died.
- **Not in a log**, by the user's choice: the Shadow's and the Goblin Ambush's battles, which the user played in 1.0 and the port and found alike, their outcomes being the code's rules rather than the draws' ([combat](combat.md#the-special-battles)); the Antidote drunk in battle (its cure is Remove Poison's, which passed); a spell's level rising from 0, which the pak can't hold; and setup 14, Sense Aura at level 9, whose popup was compared by eye.
- **The formula vectors** for the families after melee, [above](#the-formula-vectors).
