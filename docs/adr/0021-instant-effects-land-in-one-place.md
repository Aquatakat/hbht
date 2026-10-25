# 0021. Instant effects land in one place, and potions and areas are data

Status: Accepted · Oct 21, 2026 · Supersedes part of [0020](0020-one-effect-vocabulary.md): that a potion's use follows from its effects alone, and the consequence that `Potions`' switch becomes the effect types without saying where each instant type is carried out

## Context

[0020](0020-one-effect-vocabulary.md) gave spells, potions and enchantments one vocabulary of effect types, and the lasting types got one implementation (`EffectRules`: how each starts, runs each round and ends). The types that happen once didn't. A review found them handled by whoever carried them in the base game: Banishing, the dispels and the cures in the spell's cast (`Battle.Apply`), the heal in drinking (`Potions.Drink`), and the blast and corrosion in the flask's code. A plugin's spell with `heal`, or a potion with `dispel`, loaded without a problem and did nothing.

Two more pieces of the base game stayed keyed by number:

- Potions were a closed `enum Potion`, mapped to item IDs. Combat's C-up menu looped over its members, leaving out the first four (the flasks) and Stealth, so a plugin's potion never showed. Flasks were the enum's first four, wherever the code asked.
- Which spell the AI treats as an area (Fireball, kind 8 in the original's code) was a list of spell IDs on the combat record (`areaSpells`), not a property of the spell.

## Decision

### Every effect lands through one function

A spell's cast, a potion drunk in trek or in a battle, and a flask's landing all hand their effects to `EffectLanding.Land` (`HBHT.Core.Magic`), with whose they are (the record, its school, the cast's level and margin), who sent them and whom they land on. It goes through the effects in the record's order:

- An effect that happens once does now (`EffectLanding.Once`): `damage` and `banish` add the d6 over the margin that the caller rolls; `dispel`, `cure` and `heal` act on the target's sheet; `flee`, `teleport`, `senseAura` and `revealPhase` ask the battle, and do nothing in trek.
- A record with no duration has its lasting effects happen once and leave nothing, as 0020 says; the Stamina potion's is the base game's one.
- `blast` and `corrode` burst together about a cell (`IMagicHost.Burst`): where a flask landed, or for any other carrier the target's own cell. A flask's blast keeps the damage its throw rolled, as the original's `FlaskDamage` rolls every flask's dice then; anyone else's rolls its dice as it lands. Trek has nothing to burst.
- With a duration, the lasting effects go on the target as one record, and Tap Stamina's gain on the caster as another.

What stays with each carrier is what isn't an effect: a spell's chance, cost, resist roll, disable limit and lines; drinking's refusals and its turn; the flask's throw. The base game's records hold an instant type only where they did before, so the effects apply in the same order with the same draws, which the combat checkpoints confirm.

**A potion lands at margin 255.** A potion has no cast, so its dispel and cure weigh margins against the highest, as the original's Antidote adds its Remove Poison at 255. Its lasting record keeps 0, as the original's potion record has no margin. A potion's or a flask's damage dice roll as that many d6, with no margin over them.

### A potion record says how it's used

An item of kind Potion carries `use`, flags of `DrinkInTrek`, `DrinkInBattle` and `Throw`:

- the pause menu's Party tab drinks one in trek only with `DrinkInTrek`;
- C-up's menu lists every potion held with `DrinkInBattle`, in the records' order, that would do something now;
- a potion with `Throw` is a flask: thrown at the other side, taken in hand from the combat pause, drawn in the hand, called a flask by the Alchemist's messages, and never drunk.

The importer writes `Throw` on the four flasks, `DrinkInTrek` alone on Stealth (`CombatMenu.ListPotions`, `0x80088190`, skips kinds below 4 and kind 16), and both drinks on the rest. Whether drinking does anything now still follows from the effects, as 0020 has it, and a dispel now counts: something of its school on the drinker. The core has no potion enum: a combatant holds the potion item, and C-up's rows name it.

### The area is the spell's

A spell record carries `area`: the AI centres it on its opponents and scores it by whom it would catch, and a party member who knows it makes the enemies spread out by twice its casting level. The importer writes it on Fireball, checking it's at kind 8, and the combat record's `areaSpells` is gone. The flasks' danger stays the combat record's `flaskDanger`, which is about the items the party holds rather than what a flask does.

## Consequences

- A plugin can give any spell, potion or flask any effect type, and it does what it does for the base game's carriers. One that needs a battle does nothing in trek, as before.
- The base plugin is importer 53. `CombatRecord.AreaSpells` and `HBHT.Core.Items.Potion` are gone; tests name the base game's potions by `BasePotion` in `HBHT.TestSupport`.
- A dispel of the unschooled school takes nothing off, which 0020 says and the code now enforces in the one place.
- The blast's distance rule (the damage over the squared distance, kept in a byte) is the flask's, and a spell's or potion's blast uses it too. A spell that blasts at several targets bursts once for each.
