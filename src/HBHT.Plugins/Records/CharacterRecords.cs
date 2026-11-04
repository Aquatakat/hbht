namespace HBHT.Plugins.Records;

/// <summary>
/// An entity (<c>aidyn:entity/0x…</c>, ADR 0032): anyone who stands in the world, talks or fights. One with a character
/// <see cref="Sheet"/> is a party member, NPC, trainer or enemy from the entity DB (<c>EntityDB::Load</c>, <c>Entity::Init</c>);
/// one without is a simple speaker, a sign, an animal or a passer-by, from the original's dialogue entity table
/// (<c>0x14xx</c>, <c>dailougEnt_info_array</c>), who talks and walks but can't fight or join the party. A trainer teaches from its
/// own skills and spells (<c>init_skill_trainer</c>), so a recruit who also trains, such as Becan, has one record for both.
/// </summary>
/// <param name="Portrait">The image beside what it says (<c>EntityDB::GetPortrait</c>).</param>
/// <param name="Perception">
/// How far it notices the party in trek, 10 or 0 (<c>EntityDB::GetPerception</c>); 10 for one from the dialogue entity table
/// (<c>Wanderers.DialoguePerception</c>).
/// </param>
/// <param name="Body">How it stands in the world, as a wanderer or a cut-scene's actor.</param>
/// <param name="Sounds">
/// Its body's footsteps and voice (the original's audio keys), or null for silence. One from the dialogue entity table has the
/// profile of the entity at its place in that table, which 1.0 borrows for it (docs/systems/audio.md).
/// </param>
/// <param name="Sheet">Its character sheet, or null for one who can't fight or join.</param>
/// <param name="Member">The flags the engine keeps for a recruitable party member. Null for everyone else, Alaron and Trahern among them.</param>
/// <param name="Combat">What's its own in combat: whose side it fights on, whether it fights at all, and the rest. Needs a sheet.</param>
/// <param name="TrainsFree">As a trainer, it charges no gold (<c>shopkeepNotOriana</c>): Oriana. Needs a sheet.</param>
public sealed record EntityRecord(
    RecordId Id,
    string Name,
    RecordId? Portrait = null,
    int Perception = 0,
    EntityBody? Body = null,
    BodySounds? Sounds = null,
    EntitySheet? Sheet = null,
    MemberFlags? Member = null,
    EntityCombat? Combat = null,
    bool TrainsFree = false)
{
    /// <summary>Its sheet, for an entity the rules need to fight, join or train: an <see cref="InvalidOperationException"/> without one.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public EntitySheet CharacterSheet => Sheet ?? throw new InvalidOperationException($"{Name} ({Id}) has no character sheet, so it can't fight or join.");
}

/// <summary>An entity's character sheet as it starts (<c>EntityDB::Load</c>, <c>Entity::Init</c>).</summary>
/// <param name="Category">Only <see cref="EntityCategory.Chaos"/> is tested: Charming fails on it.</param>
/// <param name="School">The school whose spells it can learn (<c>aidyn:school/n</c>), with the unschooled's.</param>
/// <param name="TrueNameFlag">
/// While this flag is set, the character has a True Name even if <paramref name="TrueName"/> is false. It's Alaron's, set when
/// the Roog cinematic ends (<c>CharExp::Init</c>).
/// </param>
/// <param name="Heavy">Spells that need a heavy target work on it (<c>Magic_IsHeavy</c>).</param>
/// <param name="Attributes">
/// Base attributes by stat (<c>aidyn:stat/0</c>–<c>5</c>). Some enemies' are above what a base value can reach
/// (<see cref="StatRecord.BaseCap"/>); they read as the cap, but the excess still counts toward modified values.
/// </param>
/// <param name="Skills">Base skill levels by skill (<c>aidyn:skill/n</c>). A skill left out can never be learnt.</param>
/// <param name="WeaponSkills">
/// Base weapon skill levels by weapon skill (<c>aidyn:weaponskill/n</c>), the shield's <c>aidyn:weaponskill/11</c>. As
/// <paramref name="Skills"/>.
/// </param>
/// <param name="Spells">Spells it knows, at their levels, in its record's order.</param>
/// <param name="Weapons">What it wields as a member and as an enemy, and the spare it carries.</param>
/// <param name="Protection">Base protection (<c>CharExp::protection</c>). Protective spells and gear add to it.</param>
/// <param name="Damage">Base damage (<c>CharExp::damage</c>). Gear adds to it.</param>
/// <param name="Morale">How likely an enemy is to flee. 12 or more never flees.</param>
/// <param name="Resistances">What each element's damage is multiplied by: 0 is immune, 2 vulnerable. Sense Aura shows the first two.</param>
/// <param name="Experience">What beating it is worth, before the award multiplier (<see cref="ProgressionRecord.ExpAward"/>).</param>
/// <param name="LootCategory">Which loot table beaten enemies roll.</param>
/// <param name="SoleWeapon">
/// The only weapon it can equip (<c>Entity::canEquipWeapon</c>): Niesen's Archmage Staff. Null for everyone else.
/// </param>
public sealed record EntitySheet(
    EntityCategory Category,
    Aspect Aspect,
    RecordId School,
    bool TrueName,
    bool Heavy,
    int Level,
    IReadOnlyDictionary<RecordId, int> Attributes,
    IReadOnlyDictionary<RecordId, int> Skills,
    IReadOnlyDictionary<RecordId, int> WeaponSkills,
    [property: KeyedList(nameof(KnownSpell.Spell))] IReadOnlyList<KnownSpell> Spells,
    EntityWeapons Weapons,
    RecordId? Armor,
    RecordId? Shield,
    int Protection,
    int Damage,
    int Morale,
    [property: KeyedList(nameof(Resistance.Element))] IReadOnlyList<Resistance> Resistances,
    int Experience,
    int LootCategory,
    RecordId? TrueNameFlag = null,
    RecordId? SoleWeapon = null)
{
    /// <summary>Its base value of an attribute, 0 if the sheet leaves it out.</summary>
    public int Attribute(RecordId stat) => Attributes.GetValueOrDefault(stat);

    /// <summary>Its base level in a skill or weapon skill, or null for one it can't learn.</summary>
    public int? SkillLevel(RecordId skill) =>
        (skill.Type == RecordTypes.WeaponSkill ? WeaponSkills : Skills).TryGetValue(skill, out var level) ? level : null;
}

/// <summary>
/// An entity's weapons (<c>Entity::EquipFunc0</c>–<c>2</c>), by the original's three slots. Loot rolls each with its own chance
/// (<see cref="WeaponChances"/>).
/// </summary>
/// <param name="Joins">What it wields as it joins the party (the first slot); the others go to the inventory.</param>
/// <param name="Spare">A spare it carries (the second slot), which joining puts in the inventory.</param>
/// <param name="Fights">What it wields as an enemy (the third slot); without one, <paramref name="Joins"/>.</param>
public sealed record EntityWeapons(RecordId? Joins = null, RecordId? Spare = null, RecordId? Fights = null)
{
    /// <summary>The slots in the original's order, empty ones included.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<RecordId?> Slots => [Joins, Spare, Fights];

    /// <summary>What it wields as an enemy (<c>Entity::EquipFunc2</c>, then <c>EquipFunc0</c>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public RecordId? AsEnemy => Fights ?? Joins;
}

/// <summary>What's an entity's own in combat (ADR 0032), each from a list in the original's code. docs/systems/combat.md has them.</summary>
/// <param name="FightsForParty">
/// Among an enemy list's first four, it fights on the party's side instead (<c>Combat.FindSholeh</c>, <c>0x8005C6C0</c>): Sholeh.
/// </param>
/// <param name="Bystander">
/// It's dropped from an enemy list and never fights (<c>Combat.IsExcludedEnemy</c>, <c>0x8005C0C0</c>): the townsfolk, the
/// shopkeepers and trainers and the like.
/// </param>
/// <param name="Flee">
/// Whether it keeps the party from fleeing while it's on the field (<c>Combat.NoFleeEntities</c>, <c>0x800D2688</c>) or lets it
/// (the Shadow, <c>Combat.ForbidFleeForBosses</c>, <c>0x8005CCCC</c>, which compares the entity's place with <c>0xAC</c>), the last
/// on the field with a say having it; null for no say.
/// </param>
/// <param name="Rooted">
/// Its field of influence is 0, so it never moves in battle (<c>Combatant.UpdateRadius</c>, by the entity list's place in
/// <c>0x800E28EB</c>): Pochanargat.
/// </param>
/// <param name="Untargetable">
/// No one can attack it or cast at it, and it casts nothing itself: the Shadow. <c>Combatant.CanAttack</c> (<c>0x80060590</c>)
/// refuses a target whose entity is the entity list's place <c>0xAC</c>, and <c>Combatant.CanCast</c> (<c>0x80060C38</c>) a
/// caster or target in <c>Combat.ShadowSlot</c> (<c>0x800D94CC</c>).
/// </param>
/// <param name="BossFlag">
/// The flag a won battle sets with it among the enemy list's first four (<c>Combat.SetKillFlags</c>, <c>0x80067860</c>, by the
/// jump table <c>0x800D2B60</c>), however the battle started and whether it fell or not.
/// </param>
/// <param name="BossStyle">
/// Its AI takes the bosses' styles when it has spells (<c>EnemyAI.HasBossStyle</c>, <c>0x8005BA40</c>), 9 and 10 rather than 6
/// and 8.
/// </param>
public sealed record EntityCombat(
    bool FightsForParty = false,
    bool Bystander = false,
    FleeSay? Flee = null,
    bool Rooted = false,
    bool Untargetable = false,
    RecordId? BossFlag = null,
    bool BossStyle = false);

/// <summary>What an entity on the field says of the party's fleeing.</summary>
public enum FleeSay
{
    /// <summary>The party can't flee.</summary>
    Forbids,
    /// <summary>The party can flee.</summary>
    Allows,
}

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

/// <param name="Damage">What that element's damage is multiplied by, in quarters in the original.</param>
/// <param name="Element">The element (<c>aidyn:element/n</c>).</param>
public sealed record Resistance(RecordId Element, float Damage);

/// <summary>The six attributes, as the original numbers them (stats 0–5), as a save holds them.</summary>
public sealed record Attributes(int Intelligence, int Willpower, int Dexterity, int Endurance, int Strength, int Stamina);

/// <summary>Skill levels, as the original numbers them (skills 0–11), as a save holds them. Null is a skill that can't be learnt.</summary>
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

    /// <summary>The row of levels a sheet starts with: the base's skills and weapon skills by their numbers. A mod's own aren't in the row.</summary>
    public static int?[] ToArray(EntitySheet sheet)
    {
        var row = new int?[Count];
        foreach (var (skill, level) in sheet.Skills.Concat(sheet.WeaponSkills))
        {
            if (Slot(skill) is { } slot)
                row[slot] = level;
        }
        return row;
    }

    /// <summary>The slot a base skill (<c>aidyn:skill/0</c>–<c>11</c>) or weapon skill (<c>aidyn:weaponskill/0</c>–<c>11</c>) is held in, or null for another.</summary>
    public static int? Slot(RecordId skill) =>
        skill.Namespace != RecordId.BaseNamespace || !int.TryParse(skill.Key, out var n) ? null
            : skill.Type == RecordTypes.Skill && n is >= 0 and < 12 ? n
            : skill.Type == RecordTypes.WeaponSkill && n is >= 0 and < 12 ? 12 + n
            : null;

    /// <summary>The skill held in <paramref name="slot"/>.</summary>
    public static RecordId Id(int slot) =>
        slot < 12 ? RecordId.Decimal(RecordTypes.Skill, slot) : RecordId.Decimal(RecordTypes.WeaponSkill, slot - 12);

    /// <summary>A sheet's skills and weapon skills from a row of <see cref="Count"/> levels, leaving out those that can't be learnt.</summary>
    public static (IReadOnlyDictionary<RecordId, int> Skills, IReadOnlyDictionary<RecordId, int> WeaponSkills) Maps(IReadOnlyList<int?> levels)
    {
        if (levels.Count != Count)
            throw new ArgumentException($"There are {Count} skills, weapon skills and the shield.", nameof(levels));
        var skills = new Dictionary<RecordId, int>();
        var weapons = new Dictionary<RecordId, int>();
        for (var slot = 0; slot < Count; slot++)
        {
            if (levels[slot] is { } level)
                (slot < 12 ? skills : weapons)[Id(slot)] = level;
        }
        return (skills, weapons);
    }

    /// <summary>The row of levels for a save's or checkpoint's three parts.</summary>
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
/// <param name="Drops">
/// A weapon skill's: a beaten enemy's weapons of it can drop as loot (<c>Combat.DropWeaponClasses</c>, <c>0x800D26C8</c>). Natural
/// weapons, bites and claws and the like, never do. Skills leave it out.
/// </param>
public sealed record SkillRecord(RecordId Id, string Name, string? Description, int TrainingCost, int BaseCap, int Cap, RecordId? Icon = null,
    bool Drops = false);

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
/// <param name="SpellMaxLevel">A spell can't be raised past this (<c>TempSpell::IsMaxRank</c>).</param>
public sealed record TrainingPrices(int FirstLevelGold, int GoldPerLevel, float TrainerExp, int SpellMaxLevel);

/// <summary>A spell (<c>aidyn:spell/0x…</c>), from the spell DB (<c>SpellDB</c>, <c>TempSpell::Init</c>).</summary>
/// <param name="School">A character can learn it if its school is the same, the spell is unschooled or the character is Chaos.</param>
/// <param name="Stamina">What casting it costs (<c>Entity::SpellStaminaSubtract</c>).</param>
/// <param name="Targeting">Whom it takes, how many, and how its area is placed.</param>
/// <param name="Wizard">The Wizard level it takes to learn it.</param>
/// <param name="Special">What its target must have or when it can be cast.</param>
/// <param name="Range">Its reach per level of the cast.</param>
/// <param name="Reagent">
/// The item each cast uses up, or null for none: in the base, the inventory's spice, herb or gemstone
/// (<see cref="InventoryRecord.Spice"/>; the ROM's byte names one of the three, by the table at <c>0x800E29CC</c>).
/// </param>
/// <param name="TrainingCost">
/// Raising it from level <c>n</c> to <c>n + 1</c> costs (n + 1)² × this in experience, and learning
/// it costs this (<c>TempSpell::GetExpPrice</c>). It's the record's byte × 50.
/// </param>
/// <param name="Icon">The spell's symbol (<c>GetSpellIcon</c>), which lists show beside its school's icon.</param>
/// <param name="Description">
/// What C-down's details say of it (<c>aidyn:strings/2</c>). Four spells have none, and the original
/// can't show their details.
/// </param>
/// <param name="Position">
/// Its place in the spell table (<c>SpellDB</c>), which groups spells by school. Loading a save puts a
/// spellbook in this order, school by school, as the original does (<c>SaveParty::LoadSpell</c>). A
/// spell without one goes after those with one. It's the original's kind of the spell, which its code keys by.
/// </param>
/// <param name="Effects">What a cast does to each target (ADR 0020).</param>
/// <param name="Duration">How long its effects last on a target; none, and they happen once.</param>
/// <param name="Aspect">Solar or Lunar: it meets those resistances, and the resist roll's aspect factor.</param>
/// <param name="Resisted">Who resists it.</param>
/// <param name="NotResistedBy">Entities that never resist it: the Marquis, Wall of Bones.</param>
/// <param name="Filter">Who it may be cast at, beyond its side and its special.</param>
/// <param name="Appeal">
/// The base of the enemies' AI's score for casting it (<c>EnemyAI.SpellValue</c>): 0 and the AI never picks it. With none, the
/// highest of the spells whose first effect is of the same type.
/// </param>
/// <param name="Presentation">What casting it shows and plays in a battle; with none, nothing.</param>
/// <param name="Strike">
/// The share of its effect's animation at which it lands, the target reacting and its number shown: the cast state of the actor
/// its visual plays at the target, or with none at the caster (a refused cast's or a resisted one's, when it's that), at the frame
/// <c>(int)(length × this)</c>; with no actor, as the visual starts; never before the action's 30 ticks. The original has no such
/// field: a cast lands as its caster's cast animation (request 5) ends, whatever its visual does (<c>ProcessPlayers</c> tests a
/// strike's frame only for the attacks, 12 to 18). The port ties it to the effect, the user's choice, and the importer gives each
/// spell the share that lands it about when 1.0 lands it for a typical caster (docs/systems/combat-actions.md).
/// </param>
public sealed record SpellRecord(
    RecordId Id,
    string Name,
    RecordId School,
    int Stamina,
    SpellTargeting Targeting,
    int Wizard,
    SpellSpecial Special,
    int Range,
    RecordId? Reagent,
    int TrainingCost,
    RecordId? Icon = null,
    string? Description = null,
    int? Position = null,
    IReadOnlyList<Effect>? Effects = null,
    EffectDuration? Duration = null,
    Aspect Aspect = Aspect.None,
    Resisted Resisted = Resisted.Never,
    IReadOnlyList<RecordId>? NotResistedBy = null,
    TargetFilter? Filter = null,
    int? Appeal = null,
    MagicPresentation? Presentation = null,
    float Strike = SpellRecord.DefaultStrike)
{
    /// <summary>A spell's strike with none given: its effect's end.</summary>
    public const float DefaultStrike = 1;
}

/// <summary>Whom a spell takes, and how its area is placed.</summary>
/// <param name="Type">Which side it takes.</param>
/// <param name="Count">How many it takes.</param>
/// <param name="Placed">
/// The caster places its area, and its range is about that point (state 11): the ROM's <c>unk1E</c> bit 0x10, which <c>CanCast</c>,
/// <c>BeginSpellTargeting</c> and <c>IsResistable</c> read. Fireball, Teleportation and the four dispels.
/// </param>
/// <param name="Blast">
/// The AI centres its placed area on its opponents and scores it by whom it would catch, and a party member who knows it makes the
/// enemies spread out by twice the casting level (<c>EnemyAI.AreaCentre</c>, <c>0x80058008</c>, <c>EnemyAI.AimAreaSpell</c>,
/// <c>0x8005974C</c>, and <c>Combat.CreatePartyCombatants</c>, <c>0x8005C7C8</c>, all by spell kind 8): Fireball alone.
/// </param>
public sealed record SpellTargeting(
    SpellTargetType Type,
    SpellTargetCount Count,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Placed = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Blast = false);

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


