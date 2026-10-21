# 0020. One effect vocabulary for spells, potions and enchantments

Status: Accepted · Oct 18, 2026 · That a potion's use follows from its effects alone, and where the instant types are carried out, are superseded by [0021](0021-instant-effects-land-in-one-place.md)

## Context

[0008](0008-content-logic-layers.md) puts content logic in the first layer that can express it: records, triggers, parameterised engine features, then Lua. Spells are the largest body of logic left that the original keys by number. Its code switches on a spell's **kind**, the spell's position in the spell table (`SpellList`, `0x800E2B40`), in a dozen places: adding an effect (`Entity.AddEffect`, `0x800702F0`), undoing it (`0x800707EC`), a round's ageing (`0x80070A3C`), `ApplySpellEffect` (`0x800646E0`), the lists of dispels, charms and immobilisers, the targets each charm may take, the resistances that meet each damage spell, and the AI's value of each (`EnemyAI.SpellValue`, `0x80057498`). Potions have a switch of their own (`DrinkPotion`, `0x80064C84`), and gear reuses the spell kinds: armor's and gear's enchantments put a spell's effect on while worn, and a weapon's casts its spell on every hit. [magic](../systems/magic.md) has the whole sort.

Sorted by what the code does, the 59 spells fall into about twenty behaviours. Most are shared: ten stat spells differ only in the stat and the sign, three shields, four wards, four dispels, three immobilisers, four charms and seven damage spells. The rest have one user each: Banishing, Cheat Death, Mirror, Wraith Touch, Tap Stamina, Wind's ally face, Teleportation, Escape, Sense Aura, the moon and sun phases, and the Darkness and Light pair.

A mod should be able to make a spell, a potion or a magic sword out of the same parts the base game uses, and the AI has to judge a modded spell as it does one of the original's.

## Decision

### Records list effects, and the core implements each effect type

The way the Elder Scrolls games do it:

- A **spell** record lists its **effects**. Each effect has a **type** that the core implements in C#, configured by parameters.
- A **potion** (an item record of kind Potion) lists effects the same way, with a fixed level.
- **Gear** names a spell and a level, in the three ways Morrowind has:
  - **cast when used**: the item's spell, with charges;
  - **constant effect**: armor's and gear's enchantment, on while worn, and permanent;
  - **cast when strikes**: a weapon's enchantment, cast on the target of each hit.

There is one vocabulary of effect types for all three. It's public mod surface, with no private back door (0008). An effect is written as `{ "type": "stat", "stat": "aidyn:stat/3", "perLevel": 2 }` in the record's `effects`, a polymorphic list like a map's objects.

### The effect types

A **magnitude** is `flat + perLevel × level`, truncated. The level is the cast's **effective level**: the lower of the spell's level and the caster's Wizard, or an item's own level, or a potion's.

| Type | Parameters | Base users |
| --- | --- | --- |
| `stat` | stat, magnitude (signed) | the ten stat spells (±2 a level); Strength and Dexterity potions (+10) |
| `skill` | skill, magnitude | the Stealth spell (6 a level); Clarity, Charisma and Stealth potions (+3) |
| `protection` | magnitude, and what it leaves when it ends | Air, Spirit and Starlight Shield (2 a level); the Defence potion (15); the Acid flask (negative) |
| `ward` | school, magnitude to the resist roll | the four vs. spells (6 a level) |
| `damage` | dice base (flat or the level), elements | Dragon Flames, Earth Smite, Fireball, Lightning, Crushing Death, Whitefire, Wind against an opponent |
| `damagePerRound` | dice | Immolation, Acid Bolt (1d6), Web of Starlight (2d6) |
| `poison` | magnitude of Endurance a round | Poison (half a level) |
| `healPerRound` | magnitude, by day only | Photosynthesis, the Ring of Healing |
| `heal` | a share of maximum hit points, drawn | Healing and Curing potions |
| `immobilise` | an escape roll (× Strength), optional | Wall of Bones, Frozen Doom, Web of Starlight |
| `charm` | | Charming, Control Elem, Control Marquis, Control Zombies |
| `fieldRadius` | magnitude (signed) | Haste (a level), Stellar Gravity (less a level); Haste Amulet, Boots of Speed |
| `morale` | magnitude (signed) | Aura of Death, Solar Wrath |
| `stamina` | magnitude (signed), to whom, whether it raises the maximum | Tap Stamina's two halves, Wind on an ally, the Stamina potion |
| `weather` | combat's weather class as it ends | Wind on an ally |
| `cure` | the spells or types it removes, the margin it beats | Remove Poison, Antidote, Restore, the Stamina potion's Exhaustion |
| `dispel` | school | the four dispels |
| `partOfDay` | night or day | Darkness, Light |
| `cheatDeath` | | Cheat Death |
| `reflect` | magnitude | Mirror (3 a level) |
| `drainStat` | stats, dice; lasts the battle | Wraith Touch |
| `banish` | | Banishing |
| `flee`, `teleport` | | Escape, Teleportation |
| `senseAura`, `revealPhase` | moon or sun | Sense Aura, Detect Moon Phase and Detect Sun Phase |
| `blast` | dice, hit points or Stamina | the four flasks |
| `corrode` | a radius drawn | the Acid flask: protection off those within it |
| `lua` | reserved | none: Phase 5's |

An effect may say whom it's for, `opponents` or `allies`, which is how Wind is damage on an enemy and Stamina on a friend. Detecting Traps and Opening have no effects: the original can't cast them anywhere.

### The rest of a spell is the spell's

- **Targets** are the spell record's: the count and type it has now, the range, the target's requirement (True Name, small enough, an aspect), and a filter of the entities it may take. The filter is what limits a charm or Banishing to elementals or the Marquis.
- **Duration** is the record's, one for all its effects, as the original keeps one timer a spell: `perLevel` 1800 game-clock ticks for a cast, 648,000 for a potion. It can also be `battle`, lasting until the battle ends, as Wraith Touch does. A worn enchantment is always permanent. An effect with no duration happens once and leaves nothing: damage, heal, cure, dispel, flee, teleport, banish, senseAura, revealPhase, blast and corrode.
- **Element and aspect** are the spell's: its school always meets the target's resistance of that school and Magic. A Solar or Lunar spell meets those resistances, and the resist roll's aspect factor. A damage effect adds its own elements: Fire, Earth, Water or Air.

### On a character

- A character holds **active effects**: one per spell, up to 15, and one per potion, up to 7, as the original holds them. Each keeps its source, level, school, the cast's margin, its timer, and whether it's harmful.
- **Stacking:** a spell or potion already on can't go on again, and doesn't refresh. Two different spells of one type both count, as Air Shield and Spirit Shield do.
- **Ageing:** a round takes off 600, and trek takes off the clock's ticks. One at 0 ends on the next ageing, undoing what it did.
- **Dispelling** removes every active effect of the dispel's school, if the cast's margin times their number reaches the sum of their margins. Unschooled effects can't be dispelled.
- **The harmful flag** comes from the type: a negative `stat`, a charm, an immobiliser, damage, poison, a negative `morale` or `stamina`, and `drainStat`. The AI's morale and dispel scoring read it.
- **A battle's end** clears every spell effect and every potion but Stealth, then puts the worn enchantments back on. Death clears them too.

### What casting is, as a feature

The chance, the resist roll, the cost and the reagent are combat's rules, not the record's. Their numbers go in one record, `aidyn:magic/0`, so a mod can retune them. Its contents:

- the chance's weights and its tables by part of day and moon phase;
- the resist roll's weights and its aspect factors;
- the 600 a round;
- the most enemies disabled while reserves wait (2);
- the reagents' items;
- the spell-improvement roll.

### How the AI values a spell

- A spell record carries `aiValue`, the original's constant for its kind, which the importer writes.
- The type supplies the condition that makes a spell worth casting now:
  - a `cure` needs a target with something to cure;
  - a `charm` or `banish` needs a target its filter allows;
  - `partOfDay` needs the balance of aspects on the field to favour the change;
  - a `dispel` needs more of the school's harm on its side than help.
- A modded spell with no `aiValue` takes its first effect type's default, the highest of the base game's values for that type. So session 11's AI judges any spell built from these parts.

### Lua

`lua` is a reserved effect type: a plugin that names it loads with a problem reported, and the effect does nothing until Phase 5 builds the script layer. Then it calls a handler with the cast's caster, target, level and margin, on the same rules as other scripts. Nothing else needs to change for it.

## Consequences

- The importer writes every spell's effects, duration, filter and AI value as facts, checked in 1.0's code, beside the spell DB's fields. It maps the ROM's spell IDs on gear to records, as it does now.
- `Potions`' switch becomes the effect types. Whether a potion can be drunk follows from them: a heal needs missing hit points, a cure something to cure, a timed one not already on.
- The original's own oddities stay, as the user chose, and are parameters a mod can change: Spirit and Starlight Shield leave a level of Protection when they end (`leaves`), as they take off half what they put on, and Wind on an ally raises Stamina and its maximum for good (`raisesMaximum`). The Ring of Healing heals on every drawn frame in trek, as the code reads; the user checks it in play ([user-checks](../user-checks.md)).
- The effect types are a closed list in C#. A mod that needs a new behaviour uses `lua` once it exists, or asks for a type. This is the same trade-off 0008 makes for engine features.
