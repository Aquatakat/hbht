namespace Aidyn.Plugins.Records;

/// <summary>
/// An entity (<c>aidyn:entity/0x…</c>): a party member, NPC, trainer or enemy, with the character
/// sheet it starts with (<c>EntityDB::Load</c>, <c>Entity::Init</c>). A trainer teaches from its own
/// skills and spells (<c>init_skill_trainer</c>), so a recruit who also trains, such as Becan, has
/// one record for both.
/// </summary>
/// <param name="Portrait">The image beside what it says (<c>EntityDB::GetPortrait</c>).</param>
/// <param name="Category">Only <see cref="EntityCategory.Chaos"/> is tested: Charming fails on it.</param>
/// <param name="TrueNameFlag">
/// While this flag is set, the character has a True Name even if <paramref name="TrueName"/> is
/// false. It's Alaron's, set when the Roog cinematic ends (<c>CharExp::Init</c>).
/// </param>
/// <param name="Heavy">Spells that need a heavy target work on it (<c>Magic_IsHeavy</c>).</param>
/// <param name="Attributes">
/// Base attributes. Some enemies' are above what a base value can reach (<see cref="StatRecord.BaseCap"/>);
/// they read as the cap, but the excess still counts toward modified values.
/// </param>
/// <param name="Skills">Base skill levels. A skill it doesn't have can never be learnt.</param>
/// <param name="WeaponSkills">Base weapon skill levels, by weapon class. As <paramref name="Skills"/>.</param>
/// <param name="ShieldSkill">The base shield skill, or null if it can never be learnt.</param>
/// <param name="Spells">Spells it knows, at their levels, in its record's order.</param>
/// <param name="Weapons">
/// Three weapon slots, which may be empty. A party member equips the first that it can and carries
/// the rest; in combat an enemy tries the third, then the first (<c>Entity::EquipFunc0</c>–<c>2</c>).
/// </param>
/// <param name="Protection">Base protection (<c>CharExp::protection</c>). Protective spells and gear add to it.</param>
/// <param name="Damage">Base damage (<c>CharExp::damage</c>). Gear adds to it.</param>
/// <param name="Morale">How likely an enemy is to flee. 12 or more never flees.</param>
/// <param name="Resistances">What each element's damage is multiplied by: 0 is immune, 2 vulnerable.</param>
/// <param name="Experience">What beating it is worth, before the award multiplier (<see cref="ProgressionRecord.ExpAward"/>).</param>
/// <param name="LootCategory">Which loot table beaten enemies roll. Resolved with combat's rewards (2.5).</param>
/// <param name="Perception">How far it notices the party in trek, 10 or 0 (<c>EntityDB::GetPerception</c>).</param>
/// <param name="Unk21">Read with <paramref name="Perception"/> (<c>EntityDB::GetVal_21h</c>); what for isn't known.</param>
/// <param name="SoleWeapon">
/// The only weapon it can equip (<c>Entity::canEquipWeapon</c>): Niesen's Archmage Staff. Null for
/// everyone else.
/// </param>
/// <param name="Body">How it stands in the world, as a wanderer.</param>
/// <param name="Member">The flags the engine keeps for a recruitable party member. Null for everyone else, Alaron and Trahern among them.</param>
/// <param name="Sounds">Its body's footsteps and voice (the original's audio keys), or null for silence.</param>
public sealed record EntityRecord(
    RecordId Id,
    string Name,
    RecordId? Portrait,
    EntityCategory Category,
    Aspect Aspect,
    MagicSchool School,
    bool TrueName,
    bool Heavy,
    int Level,
    Attributes Attributes,
    SkillLevels Skills,
    WeaponSkillLevels WeaponSkills,
    int? ShieldSkill,
    IReadOnlyList<KnownSpell> Spells,
    IReadOnlyList<RecordId?> Weapons,
    RecordId? Armor,
    RecordId? Shield,
    int Protection,
    int Damage,
    int Morale,
    IReadOnlyList<Resistance> Resistances,
    int Experience,
    int LootCategory,
    int Perception,
    int Unk21,
    RecordId? TrueNameFlag = null,
    RecordId? SoleWeapon = null,
    EntityBody? Body = null,
    MemberFlags? Member = null,
    BodySounds? Sounds = null);

/// <summary>
/// What the engine does for a recruitable party member (<c>Party::CheckFlags</c>, <c>SaveEntity::GetSlot</c>).
/// </summary>
/// <param name="Joined">Set when they join, and cleared when a script takes them out of the party.</param>
/// <param name="Left">Cleared when they join, and set when a script takes them out. Null for Brenna, who never leaves.</param>
/// <param name="Died">Set while they're in the party and dead (<c>Party::CheckDeaths</c>), with battles (2.5).</param>
/// <param name="Kept">
/// Whether their sheet is kept when they leave, so they come back as they were: experience, stats,
/// skills and spells, though not what they wore, which leaving puts in the inventory. The original
/// keeps only Abrecan's, Arturo's, Becan's, Godric's and Keelin's; the others come back new.
/// </param>
public sealed record MemberFlags(RecordId Joined, RecordId? Left, RecordId Died, bool Kept = false);

/// <summary>An entity's kind (the entity DB's groups). The decomp's enum lacks 4, which is empty.</summary>
public enum EntityCategory
{
    /// <summary>Trainers and other plain NPCs.</summary>
    Generic = 0,
    Humanoid = 1,
    Named = 2,
    Party = 3,
    /// <summary>Chaos troops and bosses. Charming can't take them (<c>CombatEntity::canControl</c>).</summary>
    Chaos = 5,
    /// <summary>Animals.</summary>
    Natural = 6,
}

/// <summary>An entity's, spell's or item's aspect.</summary>
public enum Aspect
{
    /// <summary>"Neutral". No entity has it.</summary>
    None = 0,
    Lunar = 1,
    Solar = 2,
}

public enum MagicSchool
{
    /// <summary>On an entity, every school: it can learn any spell.</summary>
    Chaos = 0,
    Elemental = 1,
    Naming = 2,
    Necromancy = 3,
    /// <summary>On an entity, "Neutral": it can learn only unschooled spells. On a spell, unschooled.</summary>
    None = 4,
    Star = 5,
}

/// <summary>Damage and resistance elements.</summary>
public enum Element
{
    None,
    Earth,
    Solar,
    /// <summary>A resistance to it also covers <see cref="Cutting"/> and <see cref="Smashing"/>.</summary>
    Physical,
    Necromancy,
    Fire,
    Lunar,
    Naming,
    Water,
    /// <summary>A resistance to it covers the magical elements.</summary>
    Magic,
    Air,
    Star,
    Elemental,
    Chaos,
    Cutting,
    Smashing,
    Holy,
}

/// <param name="Damage">What that element's damage is multiplied by, in quarters in the original.</param>
public sealed record Resistance(Element Element, float Damage);

/// <summary>The six attributes, as the original numbers them (stats 0–5).</summary>
public sealed record Attributes(int Intelligence, int Willpower, int Dexterity, int Endurance, int Strength, int Stamina);

/// <summary>Skill levels, as the original numbers them (skills 0–11). Null is a skill that can't be learnt.</summary>
public sealed record SkillLevels(
    int? Alchemist = null,
    int? Diplomat = null,
    int? Healer = null,
    int? Loremaster = null,
    int? Mechanic = null,
    int? Merchant = null,
    int? Ranger = null,
    int? Stealth = null,
    int? Thief = null,
    int? Troubadour = null,
    int? Warrior = null,
    int? Wizard = null);

/// <summary>
/// Weapon skill levels, by weapon class (weapon skills 0–10). Party members use Hafted to Thrown;
/// the rest are monsters' natural weapons. Null is one that can't be learnt.
/// </summary>
public sealed record WeaponSkillLevels(
    int? Bite = null,
    int? Breath = null,
    int? Claw = null,
    int? Hafted = null,
    int? Missile = null,
    int? Pole = null,
    int? Spit = null,
    int? Sting = null,
    int? Sword = null,
    int? Thrown = null,
    int? Tusk = null);

/// <summary>
/// The 24 skill levels as a character holds them in a row (<c>CharSkills</c>): the 12 skills, the 11 weapon
/// skills, then the shield. Null is one that can't be learnt.
/// </summary>
public static class SkillSlots
{
    public const int Count = 24;

    /// <summary>The row of levels for a record's, save's or checkpoint's three parts.</summary>
    public static int?[] ToArray(SkillLevels skills, WeaponSkillLevels weapons, int? shield) =>
        [skills.Alchemist, skills.Diplomat, skills.Healer, skills.Loremaster, skills.Mechanic, skills.Merchant, skills.Ranger,
            skills.Stealth, skills.Thief, skills.Troubadour, skills.Warrior, skills.Wizard,
            weapons.Bite, weapons.Breath, weapons.Claw, weapons.Hafted, weapons.Missile, weapons.Pole, weapons.Spit, weapons.Sting,
            weapons.Sword, weapons.Thrown, weapons.Tusk,
            shield];

    /// <summary>The three parts a row of <see cref="Count"/> levels is made of.</summary>
    public static (SkillLevels Skills, WeaponSkillLevels WeaponSkills, int? ShieldSkill) Split(IReadOnlyList<int?> levels)
    {
        if (levels.Count != Count)
            throw new ArgumentException($"There are {Count} skills, weapon skills and the shield.", nameof(levels));
        return (new SkillLevels(levels[0], levels[1], levels[2], levels[3], levels[4], levels[5], levels[6], levels[7], levels[8],
                levels[9], levels[10], levels[11]),
            new WeaponSkillLevels(levels[12], levels[13], levels[14], levels[15], levels[16], levels[17], levels[18], levels[19],
                levels[20], levels[21], levels[22]),
            levels[23]);
    }
}

/// <param name="Level">1–10. A trainer teaches up to this level.</param>
public sealed record KnownSpell(RecordId Spell, int Level);

/// <summary>
/// An attribute or the level (<c>aidyn:stat/0</c>–<c>6</c>: Intelligence, Willpower, Dexterity,
/// Endurance, Strength, Stamina and Level), as the original numbers them (<c>StatEnum</c>).
/// Scripts check them by number. Endurance, Stamina and Level together are hit points.
/// </summary>
/// <param name="Description">What the attribute screen says of it (<c>aidyn:strings/1</c>).</param>
/// <param name="BaseCap">The most its base value reads as, and the most training raises it to (<c>base_stat_cap</c>).</param>
/// <param name="Cap">The most it reads as with gear and spells (<c>modded_stat_cap</c>).</param>
/// <param name="TrainingCost">
/// Raising it from <c>n</c> to <c>n + 1</c> costs (n + 1)² × this in experience (<c>stat_multiplier</c>).
/// Null for the level, which only experience raises.
/// </param>
public sealed record StatRecord(RecordId Id, string Name, string? Description, int BaseCap, int Cap, int? TrainingCost);

/// <summary>
/// A skill (<c>aidyn:skill/0</c>–<c>11</c>, Alchemist to Wizard) or a weapon skill
/// (<c>aidyn:weaponskill/0</c>–<c>10</c> by weapon class, and <c>aidyn:weaponskill/11</c> the
/// shield), as the original numbers them (<c>SkillEnum</c>, <c>WeaponClassEnum</c>).
/// </summary>
/// <param name="Description">What the skill screen says of it (<c>aidyn:strings/3</c>). Monsters' weapon skills have none.</param>
/// <param name="TrainingCost">
/// Raising it from <c>n</c> to <c>n + 1</c> costs (n + 1)² × this in experience
/// (<c>CharSkills::GetSkillXpMod</c>), less at a trainer (<see cref="TrainingPrices.TrainerExp"/>).
/// Monsters' weapon skills cost nothing.
/// </param>
/// <param name="BaseCap">The most training raises it to, and the most its base level reads as (<c>SKILLMAXBASE</c>).</param>
/// <param name="Cap">The most it reads as with gear and spells (<c>SKILLMAXMOD</c>).</param>
/// <param name="Icon">The skill's icon (<c>GetSkillIcons</c>). Weapon skills have none.</param>
public sealed record SkillRecord(RecordId Id, string Name, string? Description, int TrainingCost, int BaseCap, int Cap, RecordId? Icon = null);

/// <summary>
/// How characters level and train (<c>aidyn:progression/0</c>, the only one).
/// </summary>
/// <param name="Levels">
/// The experience each level starts at, by level, from 0 (<c>EXP_TNL</c>). A character's level is the
/// highest whose threshold its total experience has reached, up to the level's <see cref="StatRecord.BaseCap"/>.
/// </param>
/// <param name="ExpAward">
/// What every award of experience is multiplied by, and then truncated (<c>Entity::AddExp</c>). The
/// original's 1.5 is why beating an entity gives 75 per point of its record's reward byte.
/// </param>
public sealed record ProgressionRecord(RecordId Id, IReadOnlyList<int> Levels, float ExpAward, TrainingPrices Training);

/// <summary>
/// What training costs (<c>WidgetSkillTrain</c>, <c>WidgetSpellTrain</c>, <c>WidgetStatTrain</c>).
/// After a battle, a character spends experience alone; at a trainer, a skill or spell costs less
/// experience, and gold as well.
/// </summary>
/// <param name="FirstLevelGold">A trainer's price for a skill or spell the character is at level 0 in, or doesn't know.</param>
/// <param name="GoldPerLevel">A trainer's price for raising level <c>n</c> is (n + 1) × this.</param>
/// <param name="TrainerExp">A trainer's experience price is the full price times this, truncated.</param>
/// <param name="FreeTrainers">Trainers who charge no gold: Oriana (<c>shopkeepNotOriana</c>).</param>
/// <param name="SpellMaxLevel">A spell can't be raised past this (<c>TempSpell::IsMaxRank</c>).</param>
/// <param name="FirstSpellFlag">Set when a character first learns a spell at a trainer, which shows help (<c>WidgetSpellTrain::Purchase</c>).</param>
public sealed record TrainingPrices(int FirstLevelGold, int GoldPerLevel, float TrainerExp, IReadOnlyList<RecordId> FreeTrainers, int SpellMaxLevel,
    RecordId? FirstSpellFlag = null);

/// <summary>A spell (<c>aidyn:spell/0x…</c>), from the spell DB (<c>SpellDB</c>, <c>TempSpell::Init</c>).</summary>
/// <param name="School">A character can learn it if its school is the same, the spell is unschooled or the character is Chaos.</param>
/// <param name="Damage">Combat reads it by spell (2.5).</param>
/// <param name="Stamina">What casting it costs (<c>Entity::SpellStaminaSubtract</c>).</param>
/// <param name="Wizard">The Wizard level it takes to learn it.</param>
/// <param name="Special">What its target must have or when it can be cast.</param>
/// <param name="Range">Its reach per level of the cast.</param>
/// <param name="Reagent">What each cast uses up.</param>
/// <param name="TrainingCost">
/// Raising it from level <c>n</c> to <c>n + 1</c> costs (n + 1)² × this in experience, and learning
/// it costs this (<c>TempSpell::GetExpPrice</c>). It's the record's byte × 50.
/// </param>
/// <param name="Unk1E">Copied to RAM as <c>aspect_flag</c>; bit 1 makes Solar resistance apply to it.</param>
/// <param name="Icon">The spell's symbol (<c>GetSpellIcon</c>), which lists show beside its school's icon.</param>
/// <param name="Description">
/// What C-down's details say of it (<c>aidyn:strings/2</c>). Four spells have none, and the original
/// can't show their details.
/// </param>
/// <param name="Cast">What casting it plays, in combat (<c>AudioKeys.PlaySpellCast</c>).</param>
/// <param name="Position">
/// Its place in the spell table (<c>SpellDB</c>), which groups spells by school. Loading a save puts a
/// spellbook in this order, school by school, as the original does (<c>SaveParty::LoadSpell</c>). A
/// spell without one goes after those with one.
/// </param>
public sealed record SpellRecord(
    RecordId Id,
    string Name,
    MagicSchool School,
    int Damage,
    int Stamina,
    SpellTargetCount TargetCount,
    SpellTargetType TargetType,
    int Wizard,
    SpellSpecial Special,
    int Range,
    Reagent Reagent,
    int TrainingCost,
    int Unk1E,
    RecordId? Icon = null,
    string? Description = null,
    int? Position = null,
    RecordId? Cast = null);

/// <summary>How many targets a spell takes.</summary>
public enum SpellTargetCount
{
    Self,
    One,
    All,
    /// <summary>Half the cast's level, rounded up.</summary>
    ByLevel,
}

/// <summary>Who a spell can target.</summary>
public enum SpellTargetType
{
    /// <summary>Only usable outside combat.</summary>
    Field,
    /// <summary>Party members, or charmed enemies.</summary>
    Party,
    Enemy,
    Area,
    All,
}

/// <summary>What a spell's target must have, or when it can be cast (<c>MagicSpecial</c>).</summary>
public enum SpellSpecial
{
    None,
    TrueName,
    Heavy,
    SolarAspect,
    LunarAspect,
    /// <summary>Can't be cast at night (<c>Entity::CheckSpellTimeOfDay</c>).</summary>
    Day,
    /// <summary>Can only be cast at night.</summary>
    Night,
}

/// <summary>The reagent a spell uses up.</summary>
public enum Reagent
{
    None,
    Spice,
    Herb,
    Gemstone,
}
