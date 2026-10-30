# Characters (roadmap 2.2)

`HBHT.Core.Characters`: `Character` (the sheet, with its effects in `Character.Effects.cs`), `CharacterStats` and `CharacterSkills`, `CharacterRules` (the records' numbers: caps, prices, experience), `Training`, `Party` (the slots, the gold, the checks; its items in `Party.Items.cs`, [items](items.md)) and `CharacterSaves` (a sheet to and from a save, [saves](saves.md)). The entity DB's layout is in [db](../formats/db.md).

They port the original's `CharStats` (`stats.cpp`), `CharSkills` (`skills.cpp`), the sheet's hit points and experience (`entity.cpp`, `entityInfo.cpp`), the party's checks (`party.cpp`) and the training screens (`StatTrain.cpp`, `SkillTrain.cpp`, `WidgetSpellTrain.cpp`). The price functions and tables are the same in 1.0 and the debug build.

## The records

- **Entities** (`aidyn:entity/0x…`) carry the character sheet each starts with: category, aspect, school, True Name and heavy, level, attributes, skills, weapon skills and shield skill, spells at their levels, three weapon slots, armor and shield, protection, damage, morale, resistances, the experience it's worth, its loot category and its trek perception. A skill it can never learn is left out (null); the ROM's byte for it is 255. Resistances are damage multipliers (the ROM's byte in quarters), and those with no element are dropped, as combat ignores them. Alaron's has `trueNameFlag` 590 (below).
- **Spells** (`aidyn:spell/0x…`) carry the spell DB's fields. Their training cost is the ROM's byte × 50 (`TempSpell::Init`).
- **Stats** (`aidyn:stat/0`–`6`): Intelligence, Willpower, Dexterity, Endurance, Strength, Stamina and Level, with their names and descriptions from the string tables, their base caps (`base_stat_cap`), caps (`modded_stat_cap`) and training costs (`stat_multiplier`). The Level has no description or cost: only experience raises it.
- **Skills** (`aidyn:skill/0`–`11`) and **weapon skills** (`aidyn:weaponskill/0`–`10` by weapon class, and `11` the shield), with names, descriptions, training costs and the caps 10 and 15 (`SKILLMAXBASE`, `SKILLMAXMOD`). Merchant costs 750 and Ranger 1000. The six monster weapon classes cost nothing and have no description; the shield's name and cost (500) are literals in code.
- **Progression** (`aidyn:progression/0`): the experience each level starts at (`EXP_TNL`, levels 0–40), the award multiplier (1.5), the training prices and the flag the first spell learnt at a trainer sets (3611).

The tables are read from the code segment for each ROM version; the literals are facts from the code (see the [hardcoded-content audit](../hardcoded-content.md#characters)).

## A character sheet

`Character` is the original's `CharSheet`: stats, skills, experience, aspect, school, True Name, heavy, protection, damage and a spellbook. `Character.Create` builds one from an entity record (`Entity::Init`), with its experience at its level's threshold, as a joining party member's is. The sheet keeps that record and reads its aspect, school, heavy and sole weapon from it, so a loaded sheet takes the record now installed, not the one it was saved under; its True Name is the save's, or for a save without it the record's or its flag's. Skill levels are held as a row of 24 (`SkillSlots`: the skills, the weapon skills, the shield), which the records, saves and checkpoints split into their three parts. Whether a character can learn a spell, for a scroll and a trainer alike, is `Character.CanLearn`. Equipment is in [items](items.md); the effects of spells, potions and worn enchantments, up to 15 spells' and seven potions', and how they age, are in [magic](magic.md#effects-on-a-character).

**Stats** (`CharacterStats`) are held four ways, as bytes that wrap as the original's do:

| Ours | Original | What it is |
| --- | --- | --- |
| Trained | `Base` | What the entity started with, raised by training and levels. Saved. |
| Current | `health` | Trained, less damage and plus healing. Saved. |
| Base | `magicMod` | Trained, plus what spells and potions raise the base by. The sheet shows it, and maximum hit points count it. |
| Modified | `Modded` | Everything: damage, spells, potions and gear. What rules read. |

Base values read capped at the base cap, and modified ones at the cap and at least 0. Some enemies start above the base caps, such as 120 Stamina; they read as 90, but the excess counts in the modified value. Raising a stat tests the cap unsigned, so an amount that would take it below 0 raises it to the cap instead (`CharStats::AddBase`). The original also tries to write the party's best stats and skills into flags whenever one changes, and never manages ([flags](flags.md#what-the-engine-writes)).

**Hit points** are Endurance, Stamina and Level together. The maximum is their base values summed, and the current their modified ones, up to the maximum; both are bytes. Damage (`TakeDamage`) takes the Level's points first, then Stamina's, then Endurance's. What skills and spells cost (`DrainStamina`) takes Stamina, then Endurance, but never the Level. Healing restores Endurance, then Stamina, then the Level. So a character can be at full Endurance with no Level points left. Camping heals a fraction of the maximum, truncated, up to what's missing.

**Skills** (`CharacterSkills`) are held as a base and a modified level, as signed bytes. A base of −1 can never be learnt, and gear doesn't change it. Base levels read capped at 10, and modified ones at 15 and at least 0; armor can take Stealth below 0. Training a skill adds to the modified level what its capped base gained, so a base already over 10 gains nothing.

**Experience.** An award is multiplied by 1.5 and truncated, and goes both to what the character can spend and to its total (`Entity::AddExp`). The level is the last whose threshold the total has reached, up to 40, and a level raises the Level stat, with its hit points. An entity's reward byte is worth 50 before the award, so 75 in all.

## Training

`Training` is the three training screens' `Confirm` and `Purchase`. A quote is the price in experience and gold, or why it's refused, with the original's order of checks.

- **After a battle**, with no trainer, a character spends experience alone on its attributes, on the skills it's above level 0 in, and on the spells it knows. A skill at 0 isn't offered, so it has to be started at a trainer.
- **At a trainer**, the character raises what the trainer teaches: the trainer's skills above 0, and spells, up to the trainer's own level of each. Experience costs 80% (in single precision, truncated), and gold (n + 1) × 40 for raising level *n*, or 200 from level 0 or for a new spell. Oriana charges no gold. Attributes aren't offered.
- **Prices in experience** are (n + 1)² × the cost for raising level *n*, for stats, skills and spells alike.
- **The window** at a trainer is in [ui](ui.md#training), with what its lines show.
- **Spells.** A character whose Wizard can never be learnt can't learn spells. A new spell needs the character's base Wizard at least the spell's, and the same school, an unschooled spell, or a Chaos character. Spells stop at level 10. The first spell learnt sets flag 3611 and shows help, which the training window does; `TrainSpell` says whether the spell is new.

## The party

`Party` holds four slots, Alaron first, and the gold. Members are packed to the front when one leaves. It answers scripts' questions (`IScriptWorld`):

- **Best, worst and a member's stat** read modified values. The average level reads modified Levels, so damage lowers it.
- **The member best at a skill** is the one with the highest modified level, the later on a tie, dead or not; a member at 0, or who can't learn it, still counts. Only an empty party has none.
- **Skill checks** (`Party::SkillCheck`) give 0 to 21. The Wizard check reads the most skilled member. Most roll d100, take away the best member's bonus and turn what's left into a step: 1 below 26, then 2 from 26, 3 from 46, 4 from 62, 5 from 81, 6 from 96, 7 from 111, and one more every 11 up to 21 from 266 (`gSkillCheck`). A high bonus there gives a low step. Loremaster, Diplomat and Troubadour work the other way: the bonus less the roll, and 0 if the roll reaches it.

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

## Joining and leaving

Scripts add and take away members (Borg13 ops 47 and 48), and `GameState.AddMember` and `RemoveMember` do it as the original does. Nothing else does but a battle's end: the original has no menu to dismiss a member, and a battle's dead are taken out after it (below). From 1.0's code:

- **Each recruitable member's record has their flags** (`MemberFlags`, from `Party::CheckFlags`'s tables at `0x800D36C4` and `0x800D3718`): joined, left and died. Alaron and Trahern have none.

| Member | Joined | Left | Died | Kept |
| --- | --- | --- | --- | --- |
| Farris | 455 | 456 | 450 | |
| Abrecan | 89 | 685 | 615 | yes |
| Arturo | 247 | 689 | 614 | yes |
| Baird | 380 | 690 | 623 | |
| Becan | 375 | 697 | 618 | yes |
| Brenna | 92 | none | 613 | |
| Donovan | 382 | 691 | 620 | |
| Dougal | 574 | 695 | 582 | |
| Godric | 90 | 686 | 616 | yes |
| Keelin | 265 | 688 | 619 | yes |
| Niesen | 412 | 692 | 448 | |
| Rheda | 91 | 687 | 617 | |
| Sholeh | 523 | 694 | 622 | |

- **Joining** (`Party::AddMember`, `0x80074AC4`) fails only when the party has four. Otherwise:
  1. The member goes in the first free slot (slot 0 if it's empty), as their record starts them (`Entity::Init` mode 0). The weapons they bring but don't wield go into the inventory.
  2. Their joined flag is set and their left flag cleared.
  3. If a sheet was kept when they left, it replaces the new one (`SaveEntity::CopyMember`, `Copy`): experience, all four stat arrays, skills, the spellbook and what it wears. The record's gear is discarded, but the spare weapons are already in the inventory, so they come again each time.
- **Someone already in the party.** The original doesn't check for this, and adds a second sheet. The port deliberately doesn't add them again, but still says they joined, so the one talking leaves the map as in the original. Only Sholeh's first meeting, at Overworld S26 on the road to Ugarit (`aidyn:dialogue/0x04AD`), can re-add someone, since her joining doesn't hide her there; reaching Ugarit does ([wanderers](wanderers.md#joining)).
- **Leaving** (`Party::RemoveAliveMember`, `0x80074DCC`):
  1. Everything they wear goes into the inventory (`StripMember`): the gear, the weapon, the armor and the shield. The Archmage Staff stays, and so does what the inventory has no room for.
  2. Their sheet is kept, if they're one of the five.
  3. Their joined flag is cleared and their left flag set.
  4. The rest move up. Removing someone not in the party does nothing.

  No script removes Alaron, and the code wouldn't stop it.
- **Kept sheets.** The original keeps sheets in nine slots (`gSaveEntity`).
  - Slots 0 to 3 mirror the party.
  - Slots 4 to 8 are fixed: Abrecan, Arturo, Becan, Godric and Keelin (`SaveEntity::GetSlot`, a jump table at `0x800CDD88`). Everyone else comes back new each time.
  - It copies the party into them (`BenchParty`) on saving and on someone leaving. So a kept sheet is the member as they left, stripped, with no potions working.
  - A member who dies in battle has theirs erased (`Party.EraseKeptSheetsOf`, `0x80017F84`).

  The port keeps only those who left (`Party.Kept`), and takes a sheet out when its member rejoins; the mirror changes nothing anyone sees. The importer marks the five (`MemberFlags.Kept`).
- **A full party is the scripts' business.** Retail ignores whether AddMember worked; the debug build crashes with "Couldn't add %d to the party". 20 of the 29 joining scripts check the party's size, which counts Alaron. The other nine, Gwernia's, are gated so they can't come up with the party full: their map objects need fewer than two of Abrecan, Godric and Rheda (5004). Brenna's need two.
- **The full party's prompt is a script template**, which eleven joining cut-scenes share, and Niesen's (`0x0472`) is alike.
  - After "no room", it asks each member in turn (`CheckMember`). One who would leave says why, and a menu offers to leave (a goodbye, RemoveMember, and back to the join) or stay.
  - Brenna and Niesen never leave.
  - Rheda won't until flag 458 (Beat Shamsuk): "I haven't found a cure for Niesen yet". Dougal's script doesn't gate her.
  - Farris and Dougal aren't asked.

  The VM runs all of it, so nothing in the engine is needed.

## Deaths

- **The died flags** (`Party.SyncMemberFlags`, `0x80074A4C`, the decomp's `Party::CheckDeaths`) are set for each dead member and cleared for each living one, at each party member's death in battle and as a battle ends (`GameState.WriteDeathFlags`). Sholeh's, 622, is also set at her death whatever her side, the combat record's ally ([combat](combat.md#deaths)).
- **The dead leave the party** as every battle ends, won, lost or fled (`Party.AfterBattle`, `0x80074EE8`; `GameState.BringOutYourDead`): each, in order, has any kept sheet of theirs erased and what they wear put in the inventory, as a member leaving does, and the rest move up. Their joined flag stays set and their left flag clear, so the story still counts them as having joined. The port keeps only the sheets of those who left, so a member in the party has none to erase.
- **The award** after a battle won is the whole experience pool to each living member (`Character.AddExp`, × 1.5): a dead member gains nothing ([combat](combat.md#a-turn)).
