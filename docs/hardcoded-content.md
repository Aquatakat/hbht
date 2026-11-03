# Hardcoded content (roadmap 1H)

The content the port's code still holds that should come out into the plugins, so a mod can change it without changing code: where the port has it, where 1.0 does, and what it needs to come out, by [ADR 0008](adr/0008-content-logic-layers.md)'s layers. What the importer already carries from 1.0's code, its tables and the switches and literals it holds as facts, is [code tables](formats/code-tables.md).

- **1:** a record, or a field on an existing one.
- **2:** a trigger on an event the core fires (`game.started`, `script.ended`, `cinematic.ended`).
- **2+:** an event or flag that has to be designed first ([events](systems/events.md#what-else-could-move-onto-triggers)).
- **3:** an engine feature configured by fields, which needs designing.
- **4:** Lua, waiting for the script host.

Nothing found needs Lua. What stays in code is engine vocabulary: the animation request numbers every actor's state machine shares, text-line indices into the string tables (the combat log's offsets into `String/Combat` among them), the menus' layouts and colours, camera, fade and pacing timers (`SpellFx`, `CombatCamera`, `MissileFlight`, `CutScene`), and the original's arithmetic quirks the port keeps.

## The protagonist and the party

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| The protagonist's entity, `aidyn:entity/0x023F`, as a constant: the name entry and Rename Character, a save slot's name, which speaker animates the player's body and whose portrait shows over choices, trek's and cut-scene actors' sounds, the player's shadow test, the editor's panels | `GameState.Alaron`, read in `GameSession`, `SystemMenu`, `SaveMenu`, `Conversation`, `DialogueBox`, `ConversationBoxView`, `TrekLoop`, `CutScene`, `CombatantView`, `CutSceneActorView` | `IDEntInd(Alaron)`; `IntroMenu.ShowNameEntry` `0x800437f8`; `Cinematic.EndRoog` `0x80054104` | 1: each use asks the new game record's `Leader` (it exists) or the party's leader. Cut-scene actors' sounds want a default body's sounds on `aidyn:sounds/0` |
| The name "Alaron": the token captions replace with the leader's name (and "ALARON" in capitals), and the default name | `CinematicPlayer.Named`; `GameSession`, `SystemMenu` | `Text.ReplaceAll` `0x800548f0` | 1: a default name on the new game record |
| The player's sphere is 0.5, Alaron's, whoever leads | `PlayerMotion.Radius`; `Reach.Alaron` (actor `0x25F9`) | `ConfirmPlayerWithinZone` `0x8000c078` | 1: the leader's entity `Body` radius. Low |
| A body of Alaron's actor, `0x25F9`, falls straight: past −0.048 a tick its animation doesn't move it and its velocity across the ground is zeroed, and it has the ground rule on plain collision, wherever it plays (the player, a combatant, a cut-scene's actor) | `Body.AlaronActor`, `Body.FallsStraight`, `Body.Falls`, `Body.KeepHeight`; `Reach.Alaron` | `Bodies.LoadActor` `0x800147E8` (`+0x70B`), `ProcessPlayers` `0x800132F4` | 1: a flag on the entity's `Body` (the rule is keyed by the actor, so a mod can't give it to another's without one). Low |
| Party slot 0 is whom a battle hangs on: its death loses; it's healed before the game over script, gets the Goblin Ambush's heal and Strength back, takes the Shadow's merge, and the Shadow walks at it | `Battle.Advance`, `StepDefeat`, `Build`, `Leave`, `StepShadowMerge`, `KeelsOver`, `ShadowGoal` | `TurnOrder.Advance` `0x8006AC4C`, `Combat.StateDefeat` `0x80022590`, `Combat.Exit` `0x80023060`, `Combat.MergeShadow` `0x800675C0`, `EnemyAI.RunStyle` `0x80059628` | 1: the battle reads the leader through the new game record; the encounter rule's `KeelOver.Member` and the Shadow's `Merges` already name him |
| A party of four | `Party.MaxSize` | `Party::AddMember` `0x80074AC4` | 1: a game setting. Low: the menus and saves are four wide |

## Combat

Most of what's left. The combat record (`aidyn:combat/0`, `CombatRecord`), `aidyn:magic/0` and the entity, weapon and spell records already carry what [code tables](formats/code-tables.md#combat) lists. [combat](systems/combat.md) has how each rule plays.

### Who and what a battle special-cases

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Effect types tested by type: Teleportation (or a self spell) only on the caster and needing a clear spot, its marker radius 1; Sense Aura at any distance; charms and immobilisers counting to `MostDisabled`; Cheat Death checked at death; immobilisers released at death; a potion's log line by heal or Stamina | `Battle.CanCast`, `CastAt`, `ConfirmSpell`, `AimMarker`, `Apply`, `CheckDeath`, `Death`, `DrinkPotion` | `Combatant.CanCast` `0x80060C38`, `Combatant.CheckDisableLimit` `0x80063A00`, `CheckDeath` `0x80065E98` | 1: properties of the effect types in [ADR 0020](adr/0020-one-effect-vocabulary.md)'s vocabulary (self only, any distance, counts as disabling, releases at death) |
| Wands and scrolls worn as gear spend a charge, and go at 0 | `Battle.PrepareItem`, `SpendItem` | `PrepareCast` `0x8006406C`, `Entity.UnequipGear` `0x8006F5C4` | 1: an item field |
| The Healer's and Troubadour's battle actions: Lay On Hands needs Stamina max(1, 11 − Healer), Heal With Herbs max(1, 5 − Healer) and a herb; Perform rolls (Troubadour × 5 + Int) × 2 on a d100 for a bonus of the margin's step (at least 1), costing 3 Stamina and 1 a round, one at a time; the bonus scales the party's casts by 1.05 + bonus × 0.01 and the enemies' by 0.95 − bonus × 0.01. Their icons, `0x0185`, `0x018C` (and a potion's `0x35E5`) | `Battle.SpellRows`, `ResolveHealer`, `StartTroubadour`, `EndRound`; `Casting.TroubadourScale`; `CombatMenu.HealerIcon`, `PerformIcon`, `PotionIcon` | `Entity.CanLayOnHands` `0x80071ED0`, `CanHealWithHerbs` `0x80071F38`, `Combatant.ResolveHealer` `0x8006591C`, `Combatant.StartTroubadour` `0x800662F4`, `TurnOrder.EndRound` `0x8006A62C`, `Combatant.TroubadourScale` `0x80062C58`, `CombatMenu.MakeEntry` `0x80088714` | 3: a skill-action feature on the skill record (its row, icon, Stamina rule, target and effect), shared with the pause menu's ([skill checks](#skill-checks-and-crafting)). The rows' words are combat `text` already |
| Sense Aura's tiers: by the cast's power, 1 leader or follower, 3 hit points, 5 level, 7 two resistances, 9 protection | `Battle.ShowAura` | `Entity.SenseAura` `0x80070070` | 1: a field on the `senseAura` effect |
| A flask's burst: the damage over the squared distance (a byte, at least 1), those not aimed at left 1 hit point; Acid's corrosion takes radius ÷ distance protection for 648,000 ticks through a fixed `ProtectionEffect` | `IMagicHost.Burst` (`Battle.Skills.cs`), `CorrosionTicks` | `Combatant.FlaskExplosion` `0x800652F8` | 1: fields of the `blast` and `corrode` effects |

### Hitting, damage and defence

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Melee chance: Str + End + base Level + weapon `Hit` + 6 × weapon skill + 2 × Warrior, less 2 × the target's Dex, its base Level and shield; +20 from behind, +10 × Thief; × 1.15 against no Stamina, +5% per half unit above, × 0.8 with none of its own, × 0.9 at night; at least 5 | `Battle.MeleeChance`, `LeastChance` | `Combatant.MeleeChance` `0x800622C4` | 3: a chance formula feature (weighted stat and skill terms, factors by condition), one design for the three |
| Ranged chance: Int + Dex + Level + `Hit` + 6 × skill + 2 × Warrior, −1 a cell past 6; less the target's Level, 2 × Dex and shield; +20 behind; × 1.2 against one that hasn't moved and × 0.8 one that has, × 1.15 no Stamina, × 0.9 own none, × (1 − 0.3 × rain), × 0.7 at night | `Battle.RangedChance` | `Combat.RangedChance` `0x80062F50` | 3: as melee |
| Throw chance: Int + Dex + Level + 6 × Thrown + 2 × Warrior + 40, less half the target's shield, its Dex and Level, and the distance past 6; +20 behind, +10 no Stamina, −20 target moved, −25 thrower moved, −30 night, −10 in fog and −20 in a storm (weather classes 1 and 2) plus the distance again | `Battle.ThrowChance` | `Combatant.ThrowChance` `0x80064EFC`, `Combat.WeatherPenalty` `0x80062D78` | 3: as melee; the weather classes named on a record (1) |
| The critical roll: a roll at or under min(Warrior, 20% of the chance) takes a second d100 off | `Casting.AdjustRoll` | `Combatant.AdjustRoll` `0x80062594` | 1: the share on `aidyn:magic/0` or the combat record |
| Damage: the weapon's dice, an extra d6 per Strength tier in melee (tiers 4, 9, 17, 24, 28, 32, 36, 39), + the margin's step + the attacker's base Level − protection, + 2 × Thief from behind, at least 1 | `Battle.Damage`, `StrengthTiers` | `Combatant.MeleeDamage` `0x80062AE4`, `StrengthTier` `0x80062008`, tiers at `0x800E0C30` | 1: the tiers on the Strength stat record, read from the ROM as a `CodeTable`; the rest 3, with the chances |
| Protection: sheet + armor + Warrior ÷ 5 + potions, then the shield's unless struck from behind; a shield's defence × shield skill + Warrior; behind is facings within 45° (dot ≥ 0.7071) | `Battle.Protection`, `ShieldDefence`, `IsBehind` | `Combatant.ProtectionVsAttack` `0x8005F71C`, `ShieldDefence` `0x8005F6B0`, `IsBehind` `0x8005FB54` | 3: with the chances |
| Aspects and elements: Lunar × 1.25 by night and × 0.75 by day, Solar the reverse; an aspect against the time costs 1 morale; Physical resistance meets Cutting and Smashing, Magic every element but None, Physical, Cutting, Smashing and Holy; which elements meet which schools and aspects | `Battle.AspectScale`, `AspectOpposed`, `ApplyResistance`; `Casting.Meets`, `SpellValues.Balance` | `0x800D2990`, `0x800D2994`; `Combatant.AspectOpposesTime` `0x800615B4`, `ResistanceApplies` `0x80061A30`, `ApplyResistance` `0x80062704`, `SpellResistanceApplies` `0x80061C5C` | 1: aspect records (the part of day each favours, its factors) and element records (what each includes, the school or aspect it meets) |
| Skill gained by use: (8 − skill)² points under skill 6, else 5, half for a miss, and a d100 at or under 10% of them raises it 1; Perform's (8 − skill)², else 8; a spell's (5 − level)² below level 4, else 3, halved for a failed cast | `Battle.Resolve`, `GainExperience`, `GainPerformance`, `PrepareKnown` | `Combatant.GainSkillExperience` `0x80062650`, `GainSmallExperience` `0x8006620C`, `AttemptCast` `0x80063F7C` | 1: the combat and magic records (`Improvement` and `MaxLevel` are already there) |

### Turns, fields and reach

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Turns a round by base Dexterity: 0 at 0, 1 below 18, 1 or 2 by turns to 24, 2 to 29, 2 or 3 to 34, then 3 | `TurnOrder.TurnsPerRound` | `Combatant.TurnsPerRound` `0x8005F4D0` | 1: a table on the Dexterity stat record |
| The field by Dexterity (0, 0, 0, 3, 7, 12, 16, 21, 26, 28, 30, 33, 36, 38, at most 13); 1 while engaged, performing or near the Shadow | `Combatant.RadiusByDexterity`, `MostRadius`, `RadiusWithEffects` | `0x800E0C20` | 1: on the Dexterity stat record, read from the ROM as a `CodeTable` |
| Reach 0.75 beyond the bodies' radii; a ranged weapon strikes from more than 2 cells and with no opponent in reach | `Combatant.Reach`, `IsWithinReach`; `Battle.Reaches`, `FreeOfOpponents` | `0x800D2810` | 1: the combat record |
| Acting needs Int, Will, Dex and Str all above 0 | `Combatant.UpdateActive` | | 1: a stat list. Low |
| A combatant's turn passes 600 ticks of the clock | `GameState.TurnTime` | `World.AdvanceTurn` `0x8007C01C` | 1. Low |

### The enemy AI

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| The style: clever at base Int 15 or more; no weapon 5; melee 0 (clever 1), with spells 6 (a boss 9); ranged 2 (clever 3), 4 if it may strike in melee, with spells 7 or 8, a boss 10 | `EnemyAI.Init`; `Battle.Style` maps the styles | `EnemyAI.Init` `0x8005BA7C`, `StyleTable` `0x800E0B80` | 1: the threshold and table on an AI record, and a style by name on the entity (`bossStyles` becoming an override); the styles stay engine (3) |
| Cell and target scores: 25 for standing, 20 flanking or behind, 50 above, up to 15 by the target's hurt, +10 against a ranged one; ±5 an ally in an area's danger; a spell's value + 10 a target past the first + its level | `Battle.ScoreAttackCells`, `AreaDanger`, `ScoreSpellAt`, `IsFlanking`, `Archer`, `FarthestCell` | `EnemyAI.ScoreAttackCells` `0x80058C7C`, `AreaDanger` `0x80058A48`, `ScoreSpellCells` `0x800581C8`, `ChooseSpellOrAttack` `0x800590B0`, `Combatant.IsFlanking` `0x8006167C` | 3: weights on an AI record |
| Morale: the entity's, −1 for its aspect against the time, −2 once a weakness hit it, −1 no Stamina, −1 under half its side able, −1 a harmful effect, +2 under half the party able, +3 its target without Stamina, +2 steady (nothing sets it), −4 the leader gone, +1 the leader's 8–10, +2 from 11; held to 2–11; 12 never flees; flees below 2d6; a reserve arriving the same | `Battle.Morale`, `CheckFlee`, `ReserveFleesOnArrival`; `EnemyAI.Fearless` | `EnemyAI.Morale` `0x80056FA8`, `MayCheckMorale` `0x8005722C`, `CheckFlee` `0x800572AC`, `ReserveFleesOnArrival` `0x80056E18` | 3: a morale table (condition, change) on an AI record |
| The leader: the first on the field unless a later one has more base Int; its morale seeds the others' | `Battle.Build`, `LeaderBonus` | `Combat.CreateEnemyCombatants` | 1 |
| Flight and approach: flee points within 1.5, reached at 2.25, eight of them; flight lasts 4 turns; approach to 2.25 | `Battle.FleePointNear`, `FleePointReach`, `MaxFleePoints`, `ApproachReach`; `EnemyAI.FlightTurns` | `0x800D2498`, `0x800D2480`, `0x800D2488` | 1: an AI record |
| Which weapon an enemy wields: its third if it has one, else its first, which is its spare; the second is never read | `Battle.ArmFromList`, `SwapWeapon`; `EnemyAI.Spare` | `0x8006E2E8`–`0x8006E434` | 1: wield and spare on the entity record |

### Setup and spawns

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| The arenas are map set 16's 28 chunks, a body made at (20, 0, 20); a script's arena above `0x1B` is 0 | `Arena.MapSet`, `Arena.Count`, `Battle.ZonePoint`; `ScriptRunner.MaxBattlefield` | `Zones.ArenaChunks` `0x800DCA74`; `dialoug_func` | 1: the combat record names the arenas' map set, and the count comes from its chunks |
| Enemy counts: trimmed by 0.3, 0.2, 0.1 or 0 by party level × 3 ÷ enemy level; 12 at most, 4 on the field (the boss flags read the same four) | `BattleSetup.LevelTrim`, `Battle.MaxEnemies`, `OnField`; `ScriptRunner.MaxEnemies` | `BattleSetup.FillEnemies` `0x80009374`, `TrimByLevel` `0x800088E4`, `0x800CD470` | 1: the combat record |
| Fleeing by where a battle comes from: a monster party's allows it, a camp ambush's doesn't, a script's does unless it's a cut-scene; the record's lists come after | `BattleSetup.WithParty`, `CampAmbush`, `Scripted` | | 1: the combat record |
| Spawns: reference points named `good` or `bad`, a set and a letter; the set 4 − the pace byte (run 3, walk 2, sneak 1, from the player's pace as noticed or talked to); reserves at set 0, then the other four; the direction drawn 1–3 or 5–7; an ambush's collision byte 2; the Ranger's roll shifting the set; Sholeh at letter d | `Battle.SpawnPoint`, `Spawn`, `SpawnSets`, `AmbushByte`, `Build`; `TrekLoop.AniByte`, `TalkByte` | `Combat.SpawnPosition` `0x8005C294`, `Combat.ChooseSpawn` `0x8005D748`; `0x8000FAD4`–`0x8000FB78`, `0x80010AA8`–`0x80010B74` | 3: an arena convention a mod's arenas follow, on the combat record; Sholeh's letter on its `ally` |
| The Ranger's ambush roll: the best Ranger with Stamina max(1, 5 − Ranger) rolls a d100 against Int × 3 + Ranger × 10 for the margin's step, paying that Stamina; with none it's 2 | `Battle.AmbushRoll`, `NoAmbushRoll` | `Party.BattleAmbushRoll` `0x80076C60` | 3: the skill-check feature |

### Rewards and ends

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Rewards: each enemy's experience and the looted items' worth to every living member whole; the gold doubled; fled enemies pay as killed; the chest's reagent one of spice, herb or gem evenly; drops past the first two give 1 | `Battle.TallyRewards`, `AddChestLoot`, `ExperienceValue` | `Combat.TallyRewards` `0x8005E2AC`, `AddChestLoot` `0x8005E0F0`, `RollChestItem` `0x8005DF38` | 1: on the combat or loot record, shared with containers' roll ([items](#items-and-shops)) |
| An entity's chest byte is loot record `0x7FF` + the byte | `Battle.ChestOf` | | 1: the importer writes the loot record's ID on the entity |
| A beaten monster party stays away 3 map loads | `Battle.RememberBeaten`, `MapObjectMemory.Short` | `MapObjectMemory.Remember` | 1: with the memory's lifetimes ([trek](#trek-and-the-world)) |

### What it draws

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| The flask's model `0x34C5`, the Troubadour's notes `0x1ABB` raised 1.0, the target marker `0x0794`, the aim marker `0x0797`, the damage digits' textures, the locators a slot's item goes to (hand 0, shield arm 2, head 3), and the bat `0x2D4A` for a body with none (it never runs) | `Battle.FlaskScene`, `NoBody`; `Combatant.NotesScene`, `NotesRaise`, `DefaultLocators`; `CombatMarkers.Scene`; `CombatNumbers.DigitTextures`; `AimMarkerView` | `Combatant.EquipVisuals` `0x80066AEC`, `LoadWeaponScene` `0x80066CF8`, `CombatStatIndicator.Frame` `0x8008A630`, `Combat.DrawTargetCounts` `0x80021448`, `0x800CDC0C` | 1: a visuals block on the combat record; until then a mod replaces the assets |

## Bespoke behaviours

ADR 0008 names six behaviours as bespoke. Brenna's Ranger check is the camp ambush's roll, Lugash and Pochanargat are records, Sholeh is the combat record's `ally` and her member flags, and Alaron's True Name is an entity field and a trigger ([code tables](formats/code-tables.md)). The two that need layer 3 are the Goblin Ambush and the Shadow, features on the combat record. [combat](systems/combat.md#the-special-battles) has how they run.

**The Goblin Ambush** (encounter flag 978) is an `EncounterRule`, but its fleeing, which is `fleeByEncounter`:

| Behaviour | Carried as | 1.0 |
| --- | --- | --- |
| No fleeing | `fleeByEncounter` | `encounter_id_check` |
| The first member healed in full as it starts | `healsLeader` | `Combat.Init` `0x80022D1C` |
| Each blow on Alaron deals 1; the second drops his Strength to 0 and plays his fall, so the battle is lost with him alive | `keelOver` (Alaron, 2 hits) | `Combatant.ShadowFightTrick` `0x80065DCC`, `Combat.ApplyHit` `0x80021930` |
| No game over: the "PlotPoison" line, locked to Alaron, the fade, the flag, the clock at 6:00 the next day | `nonLethal`, `speaker` | `TurnOrder.Defeat`, `Combat.StateDefeatWait`, `Combat.StateDefeat` |
| No victory line; boss flags and loot still come | `quietVictory` | `TurnOrder.Victory` |
| However it ends: the leader's Strength back, Alaron healed, flag 47, the party in Oriana's hut (MiscInteriors E03) at (5.9, 0.5, 3.0) | `end` (`EncounterEnd`) | `Combat.Exit` |

Still in code: the wake at 6:00 the next day (`GameState.WakeNextMorning`), the 120 frames the field holds (`Battle.SparedFrames`, `StateDefeatWait` `0x80022998`) and the line (`Battle.SparedLine`, `0x74`). Layer 1: fields of `EncounterRule` and `EncounterEnd`.

**The Shadow** (entity `0x0226`) is the combat record's `shadow` (`ShadowRule`), with the Horn of Kynon as its item:

| Behaviour | Carried as | 1.0 |
| --- | --- | --- |
| The party may flee while it's in the battle | combat `flee` | `Combat.ForbidFleeForBosses` `0x8005CCCC` |
| No one attacks it or casts at it, and it casts nothing | combat `untargetable` | `Combatant.CanAttack` `0x80060590`, `Combatant.CanCast` `0x80060C38` |
| Its aura: at each round's end, 10 − distance to each within 10 not of chaos, squared without the item | `radius` (10) | `Combat.ShadowAura` `0x80023AD0` |
| Slowing within 10, the vortex line; the AI's paths cut at 10 | `radius` | `Combat.NearShadow` `0x80023F34`, `EnemyAI.ShadowSmoothPath` |
| Without the item, the aura works at four round ends and it goes at the fifth, nothing won | `vanishRounds` (4) | `Combat.ShadowAura`, `Combat.ShadowDissipates`, states 29 and 30 |
| Merging when it touches Alaron: with the item Alaron merges and its cinematic plays; without, it vanishes; with the item it has base Dexterity 30 and walks at him | `item`, `merges`, `mergeReach` (0.5), `dexterity` (30), `cinematic` | `Combat.CheckShadowMerge`, `Combatant.Init`, `EnemyAI.RunStyle` |
| The merge's boost, then flag 566 | `boost` (`ShadowBoost`) | `Combat.MergeShadow` `0x800675C0` |

Still in code: who's spared the aura (category chaos, `Battle.ShadowAura`), and that healing and Teleportation are barred while it's in the battle (`Battle.HealingBarred`, `TeleportBarred`: potions heal nothing, the Healer is refused with Common line 437; `Entity.HealPercent` `0x800715DC`). Layer 1, fields of `ShadowRule`; or 3, a presence in a battle barring effect types, if mods want others.

## Magic

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Cheat Death heals twice the margin; a per-round heal is the margin, as a byte, by day | `Character.TakeDamage`; `EffectRules` | `Character.TakeDamage` `0x8006EB8C`, `Entity.AgeEffects` `0x80070A3C` | 1: a magnitude on the `cheatDeath` and `healPerRound` effects |
| Casting's numbers off `aidyn:magic/0`: the cost, the spell's Stamina − its level, at least 1; (level + 1) ÷ 2 targets for a by-level spell; a placed area's reach | `Casting.Cost`, `TargetCount`, `Reach` | `Entity.SpellLevel` `0x80070E68`, `0x80070EFC`, `Combatant.SpellTargetCount` `0x80060818` | 1: fields on `aidyn:magic/0` |
| A potion lands at margin 255, as the Antidote adds its Remove Poison | `Potions.Margin` | | 1: on `aidyn:magic/0` |
| Who may learn a spell: base Wizard at least its, its school none or the learner's, Chaos any; a save's spells ordered with Alaron's three extra schools | `Character.CanLearn`, `Party.ReadScroll`; `CharacterSaves.SpellOrder` | `SaveParty::LoadSpellsFromSchool`, `LoadAlaron` | 1: the schools an entity may learn, on its record |

## Skill checks and crafting

One shape runs through them all: the best member (or the living pooled) rolls a d100 against a stat and a skill weighted, and the margin goes through the step table to a step of 1–21, which a lock's, trap's, recipe's or heal's level is compared with. A skill-check feature on the skill record (3) would carry it: the stat, the weights, the mode, who counts, and the Stamina it costs. Which skill each activity uses is a field of the activity's record (1).

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| The step table, 21 breakpoints (0, 26, 46, 62, 81, 96, 111, 122, … 266) | `Party.Steps`, `SkillCheckStep` | `gSkillCheck` | 1: read from the ROM as a `CodeTable`, on the progression record |
| Each skill's party check: Alchemist Int × 3 + skill × 6; Diplomat the living's best Int × 3 + their summed skill × 10 + 50, a second roll when the first step is under 3 (passing gives 2); Healer Int × 3 + × 10; Loremaster pooled, Int × 3 + summed × 6; Mechanic (skill × 5 + Int + Dex) × 2; Merchant best Int × 3 + × 10; Ranger skill × 10 + 80; Stealth 0; Thief Int × 4 + × 10; Troubadour skill + Dex × 3; Warrior Int + × 7 + Stamina; Wizard (skill × 5 + Int) × 2 | `Party.SkillCheck`, `Pooled`, `Merchant`, `Troubadour` | `Party::SkillCheck` | 3 |
| Talkers don't remember a Troubadour check | `ScriptOps` (`Troubadour = 9`) | | 1: a skill field |
| Secret doors: the best Int × 3 + Ranger × 10 against the door's level | `Party.SecretSearch`, from `LoadedMap` | `Party::SecretLock` | 3 |
| Locks: Thief, read to 10; it opens when Thief − 1 reaches the lock's level, a level of 10 being far beyond; "a little" under 3 past, "definitely" under 6; Stamina max(10 − Thief, 1) | `Containers.PickLock`, `MapObjectUse.PickLock` | `Party::Lockpicking` | 3 |
| Traps: Mechanic, Stamina max(10 − Mechanic, 1), Int × 4 + Mechanic × 10 against the trap's level; sprung, each living member loses level × d4 Endurance, down to 1; the menu's "slightly" under 3, "moderately" under 6 | `Containers.Disarm`, `TrapDamage`; `MapObjectUse.Disarm`, `TrapMenu` | `Party::DisarmSkill`, `TrapMenu::TakeDamage`, `TrapMenu::InitMenu` | 3 |
| Harvest: (Int + Ranger × 7) × d100 ÷ 100, held to 0–1, of the source's count, at least 1 | `Containers.HarvestShare` | `Party::HarvestSkill` | 3 |
| Brewing: Int × 3 + Alchemist × 6 to reach the recipe's step; recipes offered by the modified Alchemist, the base Mechanic. Armor: (Mechanic × 5 + Int + Dex) × 2, Stamina max(5 − Mechanic, 1) on a pass | `Crafting.Offered`, `Brew`, `MakeArmor` | `Party::CraftPotion`, `Party::CraftArmor` | 3 |
| The Healer's tasks: Lay On Hands, 3 Stamina, heals Healer × 2 + 5; herbs, max(5 − Healer, 1) and a herb, Int × 4 + Healer × 11, heals 4 × step; Restore, max(15 − Healer, 0) and a herb, Int × 2 + Healer × 5, 2 × step, in the order Int, Will, Dex, End, Str, Stamina | `Crafting.LayOnHands`, `HerbHeal`, `Restore`; `PartyMenu.RestoreOrder` | `Party::PrintHeal`, `Party::HerbHeal`, `Party::HealingFunc2` | 3: the skill-action feature |
| The pause menu's skill actions: Alchemist and Mechanic list their recipes, the Healer his three tasks | `PartyMenu.OpenCrafting` | `get_crafting_menu`, `Healer.ShowLayOnHandsList` `0x800038e4` | 3: the skill-action feature, so a mod's crafting skill lists its recipes |

## Items and shops

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Loot: gold rolled then doubled; the reagent's kind chosen evenly before its chance; six drops, the first two counted, the rest one | `Containers.Roll` | `get_chest_loot` | 1: fields on the loot record, shared with a battle's |
| Container kinds: herb, spice and gem sources harvested with the inventory record's reagents; one with its own model hides it and plays nothing; sounds by kind | `Container.Usable`, `Reagent`; `MapObjectUse.Loot`, `Open`; `LoadedMap.ActivateContainer` | `loot_func`, `container_obj_check` | 1: a container kind record |
| Bartering: the living member with the best modified Merchant, paying 15 − it in Stamina or counting 0; a shopkeeper without one at 255 | `Shop.BarterFactor`, `Open` | `Party::Barter`, `InitShop` | 1: beside the inventory record's `MerchantStep` |
| Appraisal: judge (−0.0186603 b² + 2.366025 b + 50) ÷ 100, held to 0–125, b the best Int + Merchant × 6; worth price × judge × 3 ÷ the buy price, at most 6, picks Common line 384 on | `Shop.Appraise` | `Party::ApraisePrice` | 1: the inventory record |
| Selling refuses at 1 gold or less; dropping needs price × `SellShare` above 1 | `Shop.Sell`, `LootWindow.Droppable` | `WidgetInvShop::GetGoldPrice` | 1: the inventory record |
| A Stamina bonus on gear is a battery | `Item.BatteryCapacity`, `Equipment.Bonus` | `Entity::EquipStamina` | 1: a battery field the importer writes |
| Equipment: 12 gear slots, 2 rings, 3 scrolls, piles of 64, which kinds are gear and what replaces what | `Equipment.GearSlots`, `Rings`, `Scrolls`; `ItemPile.Capacity`; `Item.IsGear`; `Party.Equip` | `GEARTOTAL`, `Party::GetEquipError` | 1. Low: the kinds are engine enums |

## Trek and the world

| What | Port | 1.0 | Needs |
| --- | --- | --- | --- |
| Wanderers: a hunter notices at Perception × (1 − fog × 0.75), × 1.5 running, × 0.6 × sneak sneaking, × 0.55 × sneak otherwise, gives up past 2 × Perception, stands 300 ticks, runs to within 2 and ambushes within 7; a talker faces the player within 3; sneak is 1 − min(best Stealth ÷ 10, 1) × 0.8 | `Wanderer` (`GiveUpTicks`, `AmbushDistance`, `FaceDistance`), `NoticeRanges.SneakFor` | `Wanderer.Tick` `0x8000f6a4`, `0x8000fa38`, `0x8000fc0c`, `0x8000fc28` | 3: a wanderer settings record ([wanderers](systems/wanderers.md)) |
| A party hunts when it has no dialogue | `Wanderer` (`Hunts`) | `moveType` 1 against 0 | 1: a field of the monster party object |
| Every dialogue entity's perception is 10 | `Wanderers.DialoguePerception` | `EntityDB::GetPerception` | 1 if 1.0 reads it per entity: a dialogue entity field. Unsettled |
| Camp ambushes are Ranger's | `Camping.AmbushChance` | `Party::CampAmbushCheck` `0x80078d14` | 1: a skill on the camping record (its numbers are there) |
| Rest: 8 hours after a camp heal and 8 more for a camp scene; the heal full | `ScriptRunner` (`ScriptEnding.RestHours`), `Party.CampHeal` | `World::Lapse8Hours`, `camp_healing` (1.0, 0.5) `0x800d386c` | 1: hours on the script and camping records |
| Map objects' memory: 19 slots; random lines, kept checks and emptied containers 10 map loads, a beaten party 3, a put-out light 16 | `MapObjectMemory` (`Slots`, `Long`, `Short`, `PutOutLoads`) | `MapObjectMemory.Write` `0x80056950` | 1. Low |
| A new game's clock: noon on day 2 of week 6, 72 game ticks a tick, the wind, the first weather roll on terrain 0 | `GameClock.NewGame`, `DaySpeed`; `Weather.NewGameWind`, `NewGame` | `InitGameGlobals`, `World::init` | 1: the new game record |
| The calendar: 7-day weeks, 56-day seasons, 6 a year; the parts of the day from 6, 9, 12, 18 and 21; the moon's phases; the seasons' names from Common line 147 | `GameClock`, `CalendarDate.PartOfDay`, `GameClock.MoonPhase`, `WorldMenu.SeasonNames` | `World::GetCalendarDate`, `World::UpdateTimeOfDay` `0x8007bd3c` | 1: a calendar record. Low |
| The intro cards' order, images and holds (the Pak card 30, the rest 120), the N64 logo's scene and turn, the legal text's breaks | `IntroCards` | `Title.CardsFrame` `0x8001b614`, `Title.LoadN64Logo` `0x8001b354` | 1: the title record |
| The fly-throughs' map set, the Overworld | `TitleScreen.Overworld` | `Title.StartScenery` `0x80054d70` | 1: the title record |
| The help menu is script 1355 | `SystemMenu.HelpScript` | `gametrek.cpp` | 1: a game setting |
| The backdrops' pictures: 1–4 and 9–12 pick a Borg8 (`0x3602`–`0x3608`) and the page's text, pulse and shadow colours; only 4 narrows the text | `BackdropStyle.For` | `Backdrop.Page` `0x80047930`, `Backdrop.Widgets` `0x80046db8` | 1: a record per picture |
| Interiors are the terrains whose sky isn't drawn, for the camera | `FollowCamera.ModeFor` | `TeleportPlayer` | 1: a camera mode on the terrain record |
| Thunder's lifetimes, 240, 360, 420, 360, by place | `WeatherSounds.ThunderTicks` | `RenderSky` `0x8001d840` | 1: beside each sound on `aidyn:sounds/0` |
| A trapped container's fires, burst and lights: counts, lives, sizes, colours, the lights' radii ([map objects](systems/map-objects.md#containers)) | `Explosion`, `DynamicLights.FireLight`, `BurstLight` | `Containers.Explode` `0x8000ffe4`, `Explosion.Fire` `0x800174f8`, `Explosion.Burst` `0x80016c00` | 1: on `aidyn:explosion/0`, which holds only the texture |
| The rest of the look: rain's spread, heights and splashes beyond the weather record, the sky's colour constants and fog start | `WeatherParticles`, `SkyColors`, `SkyState.ChangeTicks` | `ProcessWeather`, `Sky::SetColors` `0x8001cee0` | 3, if mods want another look. Low |
| The journal notice, "A new journal entry has been added." | `TrekLoop` | C string `0x800ce28c` | 2+ with `journal.added` |

## Events still in code

Each is *on event, if, do*, but its event isn't fired yet ([events](systems/events.md#what-else-could-move-onto-triggers)): layer 2+.

- **`spell.learned`:** the first spell learned sets 3611 and shows its help (`TrainingMenu`, the flag on the progression record's `Training.FirstSpellFlag`).
- **`item.bought`:** the first potion bought sets 3612 and shows its help (`Trade.NoteFirstPotion`, `BarterMenu`, the flag on the inventory record's `FirstPotionFlag`).
- **`journal.added`:** the notice while 3617 holds (`TrekLoop`).
- **`camp.started`:** the camp scenes and the Campsite's script (`TrekLoop.Camp`), on the camping record.
- **`battle.ended`** with its outcome: the Shadow's cinematic after a merge and the Goblin Ambush's end (`GameSession.EndBattle`), on the combat record.
- **A conversation's end:** its shop, training or battle (`Transition.AfterScript`).
- **`cheat.entered`:** the [cheats](#cheats).

## Cheats

Not ported (`NameEntry`). In 1.0, cheats are typed as Alaron's name, in the title's name entry or Options' Rename (`Cheats::Check`), lowercased first, and take effect only while the pause menu exists, which keeps the XP and gold cheats from crashing the title. The codes are the `Cheats` string table; which runs which effect is code (`CheatCodes.cpp`). Their sounds are on `aidyn:sounds/0`.

| Code | Effect | Needs |
| --- | --- | --- |
| `!balloon`, `!bigw` | Toggle drawing heads, or weapons, at double size | 3: render options |
| `!flea` | Toggles a quarter-size Alaron who jumps with A | 3: a player option |
| `!slashing` | Sets 3673, which shows the Hockey Stick and its container in Gwernia Castle E03 | 2+: set a flag |
| `!darkside` | Sets 3675, which shows the Lightreaver and its container in Oriana's hut (MiscInteriors E03) | 2+: set a flag |
| `!crawdaddy`, `!fatboy`, `!tweety` | The player's model becomes the chaos trooper, ogre or gryphon, or Alaron again if it already is | 3: the player's model |
| `!cheater` | Once (flag 3693): 500,000 XP to each member | 2+: add XP |
| `!bingo` | Once (flag 3694): 100,000 gold | 2+: add gold |
| `Keepbusy`, `Imadoofus` | Each says to enter the other | 2+: show a message |
| `!version` | Shows the version, compile time and code size, and turns on the coordinate display | 3: a debug option |
| `!gene`, `!gotmilk` | Strings with no effect | none |

## UI chrome

The images the menus, HUD, title and intro name (`MenuImages`, `HudSprite`, `UiImages`, `TitleScreen`, `IntroCards`, `CombatGui`, `PauseScrollView`'s actor `0x0E6B`, the fallback portrait `0x0125`) need no records: mods replace them as assets under their Borg IDs ([code tables](formats/code-tables.md#assets-the-code-names)).
