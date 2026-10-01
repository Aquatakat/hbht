# Characters (roadmap 2.2)

`Aidyn.Core.Characters`. The entity DB's layout is in [db](../formats/db.md).

Ported from `gameclasses/`: `CharStats` (`stats.cpp`), `CharSkills` (`skills.cpp`), the character sheet's hit points and experience (`entity.cpp`, `entityInfo.cpp`), the party's checks (`party.cpp`), and the training screens (`StatTrain.cpp`, `SkillTrain.cpp`, `WidgetSpellTrain.cpp`). Every function was checked against the debug build's machine code, and the price functions and tables are the same in 1.0. The decomp is wrong in the places listed [below](#checked-against-the-machine-code).

## The records

- **Entities** (`aidyn:entity/0x…`) carry the character sheet each starts with: category, aspect, school, True Name and heavy, level, attributes, skills, weapon skills and shield skill, spells at their levels, three weapon slots, armor and shield, protection, damage, morale, resistances, the experience it's worth, its loot category and its trek perception. A skill it can never learn is left out (null); the ROM's byte for it is 255. Resistances are damage multipliers (the ROM's byte in quarters), and those with no element are dropped, as combat ignores them. Alaron's has `trueNameFlag` 590 (below).
- **Spells** (`aidyn:spell/0x…`) carry the spell DB's fields. Their training cost is the ROM's byte × 50 (`TempSpell::Init`).
- **Stats** (`aidyn:stat/0`–`6`): Intelligence, Willpower, Dexterity, Endurance, Strength, Stamina and Level, with their names and descriptions from the string tables, their base caps (`base_stat_cap`), caps (`modded_stat_cap`) and training costs (`stat_multiplier`). The Level has no description or cost: only experience raises it.
- **Skills** (`aidyn:skill/0`–`11`) and **weapon skills** (`aidyn:weaponskill/0`–`10` by weapon class, and `11` the shield), with names, descriptions, training costs and the caps 10 and 15 (`SKILLMAXBASE`, `SKILLMAXMOD`). The six monster weapon classes cost nothing and have no description; the shield's name and cost (500) are literals in code.
- **Progression** (`aidyn:progression/0`): the experience each level starts at (`EXP_TNL`, levels 0–40), the award multiplier (1.5) and the training prices.

The tables are read from the code segment for each ROM version; the literals are facts from the code (see the [hardcoded-content audit](../hardcoded-content.md#characters)).

## A character sheet

`Character` is a `CharSheet`: stats, skills, experience, aspect, school, True Name, heavy, protection, damage and a spellbook. `Character.Create` builds one from an entity record (`Entity::Init`), with its experience at its level's threshold, as a joining party member's is. Equipment is in [items](items.md); spell and potion effects come with 2.5, and the sheet has the hooks they use.

**Stats** (`CharacterStats`) are held four ways, as bytes that wrap as the original's do:

| Ours | Original | What it is |
| --- | --- | --- |
| Trained | `Base` | What the entity started with, raised by training and levels. Saved. |
| Current | `health` | Trained, less damage and plus healing. Saved. |
| Base | `magicMod` | Trained, plus what spells and potions raise the base by. The sheet shows it, and maximum hit points count it. |
| Modified | `Modded` | Everything: damage, spells, potions and gear. What rules read. |

Base values read capped at the base cap, and modified ones at the cap and at least 0. Some enemies start above the base caps, such as 120 Stamina; they read as 90, but the excess counts in the modified value. Raising a stat tests the cap unsigned, so an amount that would take it below 0 raises it to the cap instead (`CharStats::AddBase`).

**Hit points** are Endurance, Stamina and Level together. The maximum is their base values summed, and the current their modified ones, up to the maximum; both are bytes. Damage (`TakeDamage`) takes the Level's points first, then Stamina's, then Endurance's. What skills and spells cost (`DrainStamina`) takes Stamina, then Endurance, but never the Level. Healing restores Endurance, then Stamina, then the Level. So a character can be at full Endurance with no Level points left, as Brenna is in the Terminor checkpoint. Camping heals a fraction of the maximum, truncated, up to what's missing.

**Skills** (`CharacterSkills`) are held as a base and a modified level, as signed bytes. A base of −1 can never be learnt, and gear doesn't change it. Base levels read capped at 10, and modified ones at 15 and at least 0; armor can take Stealth below 0. Training a skill adds to the modified level what its capped base gained, so a base already over 10 gains nothing.

**Experience.** An award is multiplied by 1.5 and truncated, and goes both to what the character can spend and to its total (`Entity::AddExp`). The level is the last whose threshold the total has reached, up to 40, and a level raises the Level stat, with its hit points. An entity's reward byte is worth 50 before the award, so 75 in all.

## Training

`Training` is the three training screens' `Confirm` and `Purchase`. A quote is the price in experience and gold, or why it's refused, with the original's order of checks.

- **After a battle**, with no trainer, a character spends experience alone on its attributes, on the skills it's above level 0 in, and on the spells it knows. A skill at 0 isn't offered, so it has to be started at a trainer.
- **At a trainer**, the character raises what the trainer teaches: the trainer's skills above 0, and spells, up to the trainer's own level of each. Experience costs 80% (in single precision, truncated), and gold (n + 1) × 40 for raising level *n*, or 200 from level 0 or for a new spell. Oriana charges no gold. Attributes aren't offered.
- **Prices in experience** are (n + 1)² × the cost for raising level *n*, for stats, skills and spells alike.
- **Spells.** A character whose Wizard can never be learnt can't learn spells. A new spell needs the character's base Wizard at least the spell's, and the same school, an unschooled spell, or a Chaos character. Spells stop at level 10. The first spell learnt sets flag 3611 and shows help; that's a trigger (2.10), so `TrainSpell` says whether the spell is new.

## The party

`Party` holds four slots, Alaron first, and the gold. Members are packed to the front when one leaves. It answers scripts' questions (`IScriptWorld`):

- **Best, worst and a member's stat** read modified values. The average level reads modified Levels, so damage lowers it.
- **The member best at a skill** is the one with the highest modified level, the later on a tie, dead or not; a member at 0, or who can't learn it, still counts. Only an empty party has none.
- **Skill checks** (`Party::SkillCheck`) give 0 to 21. Most roll d100, take away the best member's bonus and turn what's left into a step: 1 below 26, then 2 from 26, 3 from 46, 4 from 62, 5 from 81, 6 from 96, 7 from 111, and one more every 11 up to 21 from 266 (`gSkillCheck`). A high bonus there gives a low step. Loremaster, Diplomat and Troubadour work the other way: the bonus less the roll, and 0 if the roll reaches it.

| Skill | Bonus |
| --- | --- |
| Alchemist | Intelligence × 3 + Alchemist × 6 |
| Diplomat | The living members' best Intelligence × 3 + their Diplomat levels summed × 10 + 50, the other way. Below step 3 it's 2; otherwise it's rolled again, and the second counts. |
| Healer | Intelligence × 3 + Healer × 10 |
| Loremaster | The living members' best Intelligence × 3 + their Loremaster levels summed × 6, the other way |
| Mechanic | (Mechanic × 5 + Intelligence + Dexterity) × 2 |
| Merchant | Every member's best Intelligence × 3 + the best Merchant's level × 10 |
| Ranger | The best Ranger's level × 10 + 80 |
| Stealth | None: the check is a stub, and always 0 |
| Thief | Intelligence × 4 + Thief × 10 |
| Troubadour | The best Troubadour's level + Dexterity × 3, the other way; 0 if no one has the skill |
| Warrior | Intelligence + Warrior × 7 + Stamina |
| Wizard | (Wizard × 5 + Intelligence) × 2 |

Who joins and leaves with what sheet comes with M2. The original sets each recruit's joined, left and died flags (`Party::CheckFlags`, already read into the quest atlas), and keeps the sheet of each member who leaves in one of four slots, restoring it if they rejoin (`SaveEntity`).

## Checked against the machine code

The debug build's code differs from the decomp's pseudocode in these places:

- **Skill training costs.** Merchant is 750 and Ranger 1000, not 7500 and 750. The decomp's list has them shifted.
- **The Wizard check** reads the most skilled member. The pseudocode reads an unset pointer.
- **The Mechanic check** is (Mechanic × 5 + Intelligence + Dexterity) × 2 against the roll; the pseudocode's expression is garbled.
- **Cheat Death.** `Entity::DamageToLevel` looks through all 15 effects for it; the pseudocode's loop never runs. That's for combat (2.5).
- **Entity categories.** Charming fails on category 5, the Chaos troops and bosses. The decomp's enum puts Chaos at 4, which the entity DB leaves empty.

## Confirmed in the emulator

The checkpoints confirm the sheets: levels, hit points, the skills that can't be learnt and the True Name. The user confirmed the training prices by playing, on Sep 30, 2026:

- **Warrior from 3 to 4** costs 16,000 experience after a battle, and 12,800 experience and 160 gold at a trainer.
- **Merchant from 1 to 2** costs 3,000 experience after a battle, and 2,400 experience and 80 gold at a trainer: 750 × (n + 1)², not the decomp's ten times that.
- **Oriana** charges no gold for what she teaches.

## What's left

- **Effects (2.5):** spells' and potions' stat changes, their timers, Cheat Death, and what dying clears.
- **Joining and leaving (M2):** the member flags and the sheets kept for members who leave.
- **Saves (2.8):** the original saves a character's spells by school, in the spell DB's order (`SaveParty::SaveSpell`), so after loading a save the spellbook is in that order, not the order learnt. The checkpoints show it.
- **Flags for the party's best stats and skills.** The original tries to raise them whenever a stat or skill changes (`Event_flag_stat`, `event_flag_skill_`), but its lookup never finds them, so the port doesn't either (see the [audit](../hardcoded-content.md)).
