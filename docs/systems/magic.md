# Magic: spells, potions and enchantments (2.5, M3 session 8)

`Aidyn.Core.Magic`, with the effects on a character in `Character.Effects.cs` and casting in a battle in `Battle.Magic.cs`. [ADR 0020](../adr/0020-one-effect-vocabulary.md) makes spells, potions and enchantments data: a record lists **effects**, each of a type the core implements, configured by parameters. This doc says what the original does, from 1.0's machine code and the combat checkpoints, and how the port does it. What the player sees is in [engine-behaviour](../engine-behaviour.md#magic). Names are in the symbol map, under `Character.`, `EffectRules.`, `ActiveEffect.`, `Casting.`, `Potions.`, and coined ones under `Entity.`, `Spell.`, `EffectRecord.`, `PotionRecord.` and `Combatant.`.

## The records

- **A spell** (`aidyn:spell/0x03…`) has the spell DB's fields and, from importer 40, its `effects`, its `duration` (1800 game-clock ticks a level for those that last, the battle for Wraith Touch, none for the rest), its `aspect` and `placed` (the ROM's `unk1E` bits 1, 2 and 0x10, [db](../formats/db.md)), who resists it, the `filter` of entities it may take, and its `aiValue`. The importer writes these as facts, checked in 1.0's code (`MagicImporter`).
- **A potion** (`aidyn:item/0x10nn`) has its effects and duration: 648,000 ticks (`0x9E340`, 3 hours of game time) for the stat, skill and Defence potions.
- **Gear** names a spell and a level, as before: an item's `spell` is cast when used, with charges; an armor's or gear's `enchantment` is on while worn; a weapon's `enchantment` is cast at the target of each hit.
- **The magic record** (`aidyn:magic/0`) holds casting's numbers. The chance's factors, the improvement factor and the resist roll's aspect factors are read from the code segment (`CodeTable.CastingFactors`, `0x800D29CC`, the same in all three ROMs).

## A spell's kind

The code keys a spell by its **kind**, its place in the spell table (`SpellList`, `0x800E2B40`; the record's `position`): `GetIdIndex` turns an ID into it, and the effect records, the AI's values and the special-case lists are all by kind. There are 59, IDs `0x0300`–`0x033F` with five gaps. The ROM's item records hold spell IDs; the item tables in RAM hold kinds. The port keys by record ID throughout, so modded spells need no kind.

## What each spell does

Sorted by what the code does with it (`Entity.AddEffect` `0x800702F0`, `Combatant.ApplySpellEffect` `0x800646E0`, `EffectRules.Round` `0x80070A3C`, `EffectRules.End` `0x800707EC`). "L" is the cast's effective level; a record is a timed effect, L × 1800 ticks.

| Type | Spells (kind) | What the code does |
| --- | --- | --- |
| `stat` | Strength (10), Endurance (19), Brilliance (13), Stamina (32), Dexterity (51) +2L; Debilitation (5), Weakness (22), Stupidity (14), Exhaustion (31), Clumsiness (52) −2L | base and modified (`Character.AddStat` `0x800723E8`: not on the dead; Endurance, and the Level while Endurance is 0, left at 1 at the least) |
| `protection` | Air Shield (3), Spirit Shield (35), Starlight Shield (48), 2L | the sheet's protection byte; Spirit and Starlight Shield take off L as they end |
| `ward` | vs. Elemental, Naming, Necromancy, Star (38–41), 6L | added to the resist roll against that school (`0x80061F3C`, the first such record by slot) |
| `damage` | Dragon Flames (6, Fire), Earth Smite (7, Earth), Fireball (8, Fire, L), Lightning (9, Water), Crushing Death (28), Whitefire (58), Wind at an opponent (11, Air) | (margin + the damage byte, or L for Fireball) d6, times the resistances that meet it |
| `damagePerRound` | Immolation (0), Acid Bolt (24) 1d6; Web of Starlight (57) 2d6 | each round, if no more than the target's hit points |
| `poison` | Poison (36) | each round, L >> 1 of Endurance's current value, no more than it has, not if hit points are below it |
| `healPerRound` | Photosynthesis (46); the Ring of Healing | heals the cast's margin each round, not when the clock says night (`World+1`) |
| `immobilise` | Wall of Bones (34), Frozen Doom (53), Web of Starlight (57) | clears the combatant's active flag; the Shadow is immune; Web is escaped each round on a d100 under twice Strength |
| `charm` | Control Elem (4), Charming (16), Control Marquis (17), Control Zombies (27) | toggles the hostile flag; as it ends, an enemy goes back to the enemies and a member to the party |
| `fieldRadius` | Haste (30) +L; Stellar Gravity (56) −L | `Combatant.UpdateRadius` (`0x8005F29C`), record by record: Gravity takes L off, leaving 1 if that's as much or more |
| `morale` | Aura of Death (25), Solar Wrath (47), −L | the AI record's morale, no more than it has, and a morale check due; given back as it ends |
| `stamina` | Tap Stamina (33): the target −2L, the caster +2L up to its base (a record on each); Wind on an ally +2L to Stamina and its maximum (`Entity.AddStaminaEffect`, `0x80072158`, never given back) | once |
| `weather` | Wind on an ally | as it ends, combat's weather class to 1 (`World+4`) |
| `cure` | Remove Poison (2): Poison records cast with a lower margin than this cast's | once (`Entity.CurePoison`, `0x80071A6C`) |
| `dispel` | Dispel Elemental, Naming, Necro, Star (42–45) | every record of the school, if the margin × their number reaches the sum of their margins (`Entity.Dispel`, `0x80071CD8`); unschooled ones never |
| `partOfDay` | Darkness (29) night, Light (54) day | the other's records come off everyone, and the battle's part of the day is held (`Entity.SetLight`, `0x80071BDC`) |
| `cheatDeath` | Cheat Death (23) | a blow that kills heals twice the margin instead, and the record ends (`Character.TakeDamage`, `0x8006EB8C`) |
| `reflect` | Mirror (37), 3L | the combatant's counter (`+0x28`), which the resist roll spends; it isn't taken back as Mirror ends |
| `drainStat` | Wraith Touch (26) | Intelligence, Willpower, Dexterity or Strength (`0x800D34B0`, by 1d4) less 2d6, base and modified, for the battle; the record's margin byte holds the stat |
| `banish` | Banishing (15) | (margin + the target's hit points) d6 |
| `flee`, `teleport` | Escape (1), Teleportation (12) | the targets flee if the battle allows; the caster moves to the placed point if it's free, not in the Shadow's battle |
| `senseAura`, `revealPhase` | Sense Aura (21), Detect Moon Phase (49), Detect Sun Phase (50) | the details popup; the HUD's moon or sun |
| none | Detecting Traps (18), Opening (20), and Stealth (55, Stealth +6L) | target type Field: "Spell Unavailable"; nothing casts them anywhere |

No spell both deals damage and leaves a record: the damage byte of Immolation, Acid Bolt, Wraith Touch, Frozen Doom and Web only routes the original's code (`Spell.AddsEffect`, `0x8007AAB4`). Whether a damage spell is Fire, Earth, Water or Air isn't a field: `Combatant.SpellResistanceApplies` (`0x80061C5C`) names the kinds, which the importer writes as the `damage` effect's elements.

## Casting

`Combatant.CastSpell` (`0x800648CC`), once per target of the selection (`Combatant.CastCurrentSpell`, `0x80065C08`). The port's is `Battle.Cast(caster, source, targets)`.

1. **Available** (`0x800635B8`): a Field spell is "Spell Unavailable"; the target must meet the spell's special (`Casting.Requirement`): a True Name, the heavy flag set (which the party, the rats and ordinary humanoids have and the 32 big creatures haven't, so "heavy" means small enough), or an aspect. Day and Night specials test the battle's part of the day; no spell has one.
2. **The level** (`Casting.Level`): a known spell's level, no higher than the caster's modified Wizard; an item's spell its own.
3. **Prepared,** once (`PrepareCast`, `0x8006406C`, skipped while the acted flag is up):
   - a known spell (`AttemptCast`, `0x80063F7C`): the reagent out of the party's inventory, unless the caster is from the enemy list or the AI plays it (`0x80063BFC`, "Missing spell component"); the cost, max(1, the spell's Stamina − L), from the worn items' batteries and then Stamina (`Equipment.PayStamina`, "Not enough ST", the reagent kept); then the roll;
   - an item's spell: a charge, which only a wand or a scroll worn as gear spends (`0x80070E0C`); no reagent, Stamina or roll: the margin is a d100's step.
4. **The roll** (`Casting.Chance`, `0x80063CA0`): (modified Intelligence × 4 + L × 6 + base Level) × a factor, the moon's phase's for a Lunar caster (0.5, 0.75, 1.25, 1.5), the battle's part of the day's for anyone else (0.75, 1.25, 1.25, 0.75, 0.75 from morning to night), truncated, times the Troubadour's scale, at least 5. A d100 at or under the modified Wizard, and under a fifth of the chance, has a second d100 taken off it (`Casting.AdjustRoll`, `0x80062594`). Under the chance succeeds, with the margin the step of what's left (`SkillCheckStep`); otherwise the reagent comes back, "fails to cast the spell", and the next target tries again with another. Cast or not, a spell at level 0 or 1 may rise a level: a d100 at or under (5 − L)² × 0.1, halved on a failure (`ResolveCastRoll`, `0x80063DF4`).
5. **Resisted** (`Casting.Resist`, `0x80064298`), by everyone for a spell at enemies or a placed area, by the other side for Wind, by no one for the rest, and never by the Marquis for Wall of Bones: (modified Willpower × 3 + modified Endurance + base Level) plus its ward, × 0.75 for a Solar or Lunar spell when the two share an aspect or 1.25 when they don't. A d100 over it doesn't resist; otherwise its step comes off the margin, and a step of the margin or more resists, as does any while Mirror's counter lasts, which spends the difference.
6. **What it does** (`Combatant.ApplySpellEffect`): Banishing, a dispel, or the disable limit; then the effects for the target's side, the lasting ones as a record. **The disable limit** (`0x80063A00`): while reserves are still to come, the party's side can't charm or hold an enemy while more than two in the enemies' slots are charmed or held. The blocked cast still does damage ([below](#the-originals-quirks-the-port-keeps)).
7. **Damage:** (margin + the dice) d6, times every resistance of the target's and its worn items' that meets the spell (`Combatant.SpellDamage`, `0x800649E4`): at least 1, unless a resistance is 0. `Battle.Cast` returns it in the outcome; the state that shows the numbers deals it.

**Targets** (`Casting.TargetCount`, `0x80060818`): one, every one the spell may take, or (L + 1) >> 1. `Casting.CanTarget` (`0x80060988`) wants the target alive and in play, on the side the spell's type wants, not under the spell already, and allowed by the filter. The reach is L × the range byte, or L from the placed point (`Casting.Reach`).

**A weapon's spell** (`Combatant.WeaponSpellProc`, `0x800627D0`; `Battle.Strike`) is cast after every melee and ranged hit when it may take the target: an item's spell at the enchantment's level, its damage added to the hit's.

## Effects on a character

**The record** (`EffectRecord`, 0x18 bytes, 15 at `entity+0x34`; `ActiveEffect`): `+0` kind, `+1` power (L), `+2` the spell's school, `+3` the margin after the resist, `+4` u32 harmful, `+8` u32 timer (`0xFFFFFFFF` never runs down), `+0x14` the visual's handle. Checked in 08: Rheda's Endurance is `13 04 02 04 | 00000000 | 00001C20`. **Potions'** (`PotionRecord`, 0xC bytes, 7 at `entity+0x38`): kind, power, u32 timer, the visual. The port holds both in slot arrays (`Character.Effects`, `PotionEffects`), as their order is the RNG's.

- **Adding** (`Character.AddEffect`): the first free slot. A spell already on, or one with no slot free, adds nothing and doesn't refresh, though the cast still costs; targeting rules it out first. A potion isn't checked: drinking checks.
- **Ageing** (`Character.AgeEffects`, `0x80071444`): the potions, then the spells (`EffectRules.Round`). A combat round takes 600 off each timer (`TurnOrder.EndRound` for every combatant in play; `Battle.AgeEffects`); trek takes the clock's 72 a tick of play (`TrekLoop.TickClock`, `GameState.PassTime`). A timer at 0 ends on the next ageing, so a cast of L lasts 3L rounds and its per-round effects run 3L times. A dead character's don't age. Checked: Alaron's Endurance reads 7200, 6600, 6000, 4800, 3000 over checkpoints 08–12.
- **Ending** (`Character.RemoveEffect`, `0x800724B8`): each effect undone (`EffectRules.End`).
- **Death** (`Character.TakeDamage`): a Cheat Death on the dead heals twice its margin (`& 0x7FFF`, shifted) and ends; otherwise, in a battle, every spell's record comes off, and every potion's, and if that brought the character back its Endurance, Stamina and Level are put to 0 (`0x80071E28`). Taking a stat back does nothing to the dead, so a buff on the dead stays raised ([below](#the-originals-quirks-the-port-keeps)).
- **A battle's end** (`Character.EndBattleEffects`, `Entity.ClearEffects` `0x80071830`): the potions but Stealth's (`MagicRecord.KeptAfterBattle`), every spell record, then the armor's, shield's and gear's enchantments back on.
- **Readers:** `Combatant.RadiusWithEffects` (Haste and Gravity), `Combatant.IsImmobilised` (`0x80062250`: also an attack's chance takes no Dexterity or shield from a held target, and `Combatant.UpdateActive` `0x8006615C` keeps a held one, or one with Intelligence, Willpower, Dexterity or Strength at 0, inactive), `Casting.Ward`, `Combatant.Mirror`, `Character.HasCheatDeath`, `EnemyAI.Morale` (−1 for any harmful record) and the AI's values.

## Potions and flasks

**Drinking** (`Potions.Drink`; trek's `0x8006FE34`, combat's `Combatant.DrinkPotion` `0x80064C84`) does the potion's effects in order and uses one up. Whether it can (`Potions.CanDrink`, `0x8006FBE8`) follows from the effects: a lasting one if it isn't working already; otherwise a heal below full health, a cure with something to cure, or Stamina below its base. A flask, or a potion with no effects (Aspect, Jumping), can't be drunk in trek.

| Potion | Effects |
| --- | --- |
| Healing, Curing | heal 15–25% or 40–60% of the maximum (none in the Shadow's battle) |
| Stamina | cure Exhaustion, then Stamina to its base |
| Antidote | cure Poison (the original adds Remove Poison at margin 255) |
| Restore | cure Wraith Touch, Debilitation, Weakness, Exhaustion, Stupidity, Clumsiness (`0x800D34B4`) |
| Strength, Dexterity | the stat +10 for 648,000 ticks |
| Clarity, Charisma, Stealth | Loremaster, Diplomat or Stealth +3 |
| Defence | protection +15 (the original adds it in `Combatant.TotalProtection`) |
| Fire, Inferno, Acid flasks | blast 4d6, 8d6, 2d6 (`FlaskDamage`, `0x80065270`, which rolls all three and takes one) |
| Sleep Gas | blast of 15 from Stamina: no sleep and no duration |
| Acid | and corrosion: a 2d6 radius, each within it −(radius ÷ squared distance) protection until the battle ends |

**The blast** (`FlaskExplosion`, `0x800652F8`): each living combatant within a squared distance of the damage takes the damage ÷ the squared distance (at least 1 distance), left at 1 at the least if it isn't the target; friendly fire happens. **The throw's chance** (`ThrowChance`, `0x80064EFC`) is in the research for session 9.

## Gear

- **Worn enchantments** go on with the armor, shield or gear (`Character.PutOnEnchantment`, from `Equipment`), never running down, and come off with it (the first of the spell's that never runs down). A dispel can take one off until the battle's end puts it back. Only three exist: the Haste Amulet (Haste 1), the Boots of Speed (Haste 5) and the Ring of Healing (Photosynthesis 1, margin 1).
- **Charges** go only from wands and scrolls worn as gear; the spells of weapons, armor, shields and other gear never run out. Nothing recharges them (`Party.RechargeItems`, `0x80078D5C`, is empty). Batteries are [items](items.md#equipment)'.
- **Resistances** on the body armor, shield, weapon and the 12 gear slots count with the character's own (`Equipment.Resistances`).

## The AI's values

`SpellValues.Value` (`EnemyAI.SpellValue`, `0x80057498`): the record's `aiValue`, or with none the highest the loaded spells give its first effect's type; and 0 when its type finds nothing to do: a `cure` with no candidate under what it cures (`0x8005B734`), a `charm` or `banish` whose filter allows no candidate (`0x8005B60C`, `0x8005B6AC`), `partOfDay` unless it changes the part of the day and the balance favours it (+1 for each combatant the light now hinders on the caster's side, or doesn't on the other's, −1 for the rest; `0x800615B4`), a `dispel` unless the school's timed records harm its side or help the other's more than not. The values range from 0 (Escape, Teleportation, the moon and sun, the morale spells) to 85 (Darkness, Light).

## The original's quirks the port keeps

The user chose to keep these, which look like bugs, as the original has them:

- **Spirit and Starlight Shield leave protection behind.** They add 2 a level but take off 1 a level as they end (`0x800707EC`), so each cast leaves a level of protection on the character until a load rebuilds the sheet. It's a parameter a mod can change (`ProtectionEffect.Leaves`).
- **Wind on an ally raises Stamina and its maximum for good.** It adds 2 a level to Stamina's base and modified values (`Entity.AddStaminaEffect`, `0x80072158`), and nothing takes it back, so maximum hit points rise with each cast until a load. It's a parameter too (`StaminaEffect.RaisesMaximum`).
- **A blocked charm or hold still hurts.** When the disable limit stops a charm or an immobilise, `ApplySpellEffect` logs that the target resists but returns 0, not −1, so `CastSpell` still rolls (margin) d6 of damage at the target (`0x800646E0`, `0x800648CC`). `Battle.Cast` returns that damage.
- **The dead keep their buffs.** `Entity.SubStat` (`0x80072450`) does nothing to a dead character, so when death takes a spell's or potion's effects off, a raised stat stays raised. Endurance's and Stamina's raise the maximum hit points too, until a load.

## Where the port differs

- The Defence potion and the Acid flask's corrosion change the sheet's protection, as the shields do, where the original adds them when it works out the total (`Combatant.TotalProtection`); the totals differ only when the sum is below 0.
- One record can hold several effects; the original keys one per kind. The base game's records hold one each but Web of Starlight's (immobilise and 2d6) and Tap Stamina's.
- Trek's ageing runs every tick, and what the effects do each round once a 30 fps frame (`Cadence.FrameTicks`), as the original does once a drawn frame.
- The battle's held part of the day is the battle's (`Battle.PartOfDay`); the original's lock (`World+5`) also stops the weather rolling, which the port's battle doesn't yet do.
- The "cannot cast" messages (`0x800E2C00`) aren't imported: `Battle.Cast` returns the refusal, and session 9 shows it.

## What's left

- **Session 9:** C-down's and C-up's menus over `Battle.Cast` and `Potions.Drink`, target set B over `Casting.CanTarget` and `TargetCount`, the placed area (state 11), the flasks' throw and blast, the Troubadour's bonus (`Battle.TroubadourBonus`), the Healer's two, the refusals' messages, and Sense Aura's popup and the phases on the HUD (`Battle.AuraShown`, `PhasesShown`).
- **Session 3:** `Battle.AgeEffects` at a round's end, `Combatant.RadiusWithEffects`, and the turns an immobilised combatant loses.
- **Session 5:** dealing a cast's damage, `Battle.Strike` after a hit, death from an effect's damage, and `Combatant.IsImmobilised` in the attack's chance.
- **Session 10:** the visuals (`CombatSpellFx`, `ProcessSpellVisuals`) and the handle each record keeps.
- **Session 11:** `SpellValues` in the AI's scoring, and `Combatant.Morale` from the entity's.

## Not settled

- Whether the acted flag is up when a weapon's spell is cast, which decides whether it rolls its own margin (the port rolls one).
- Whether a worn Ring of Healing really heals every frame in trek ([user-checks](../user-checks.md)).
- What Wind's weather class 1 does in combat: weather.md calls it fog, and Wind's description says it clears fog.
- The `unk1E` bits 0x20, 0x40 and 0x80, which nothing in combat reads.
