using Aidyn.Core.Calendar;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Characters;

/// <summary>A spell a character knows, at its level (<c>SpellInstance</c> in a <c>SpellBook</c>).</summary>
public sealed class LearnedSpell(SpellRecord spell, int level)
{
    public SpellRecord Spell { get; } = spell;

    /// <summary>1–10, as a byte.</summary>
    public int Level { get; internal set; } = (byte)level;
}

/// <summary>Whether a character can learn a spell (<see cref="Character.CanLearn"/>).</summary>
public enum SpellLearnability
{
    Learnable,
    /// <summary>The character's base Wizard is below the spell's.</summary>
    WizardTooLow,
    /// <summary>The spell is of another school, and the character isn't Chaos.</summary>
    WrongSchool,
}

/// <summary>
/// A character sheet (<c>CharSheet</c>): a party member's, or an enemy's in combat. It holds the
/// stats, skills, experience and spells, and the rules for hit points, damage, healing and levels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hit points</b> are Endurance, Stamina and Level together: the maximum counts their base values
/// (<c>Entity::getHPMax</c>), and the current their modified ones, up to the maximum
/// (<c>getHPCurrent</c>). Both are bytes. Damage takes the Level's points first, then Stamina's, then
/// Endurance's (<c>Entity::DamageToLevel</c>, <c>DecreaseHP</c>); healing restores Endurance first,
/// then Stamina, then Level (<c>Entity::addHP</c>). Skills and spells spend Stamina's points.
/// </para>
/// <para>
/// What it has equipped is its <see cref="Equipment"/>, which changes the sheet as items go on and off.
/// Spells', potions' and worn enchantments' effects last a while (<see cref="Effects"/>, <see cref="PotionEffects"/>,
/// ADR 0020).
/// </para>
/// </remarks>
public sealed partial class Character
{
    private readonly List<LearnedSpell> _spells;

    private Character(EntityRecord record, Func<RecordId, bool>? flags, CharacterRules rules, CharacterStats stats,
        CharacterSkills skills, List<LearnedSpell> spells)
    {
        Record = record;
        Name = RecordName = record.Name;
        Rules = rules;
        Stats = stats;
        Skills = skills;
        _spells = spells;
        TrueName = record.TrueName || (record.TrueNameFlag is { } flag && flags?.Invoke(flag) == true);
        Protection = (byte)record.Protection;
        Damage = (byte)record.Damage;
        Equipment = new Equipment(this);
    }

    /// <summary>The record the sheet started from, whose traits that don't change it reads.</summary>
    public EntityRecord Record { get; }

    public RecordId Entity => Record.Id;

    /// <summary>
    /// The character's name: the record's, but Alaron's can be changed in the options menu (<c>InputMenu</c>),
    /// and his save holds it (<c>SaveParty::SaveAlaron</c>).
    /// </summary>
    public string Name { get; set; }

    /// <summary>The record's name, which <see cref="Name"/> starts as.</summary>
    public string RecordName { get; }

    public CharacterRules Rules { get; }

    public CharacterStats Stats { get; }

    public CharacterSkills Skills { get; }

    public Equipment Equipment { get; }

    /// <summary>The only weapon it can wield (<see cref="EntityRecord.SoleWeapon"/>): Niesen's staff.</summary>
    public RecordId? SoleWeapon => Record.SoleWeapon;

    /// <summary>
    /// Weapons its entity brings that it doesn't wield. Joining the party puts them in the inventory
    /// (<c>Entity::EquipFunc0</c>).
    /// </summary>
    public List<Item> Spares { get; } = [];

    public Aspect Aspect => Record.Aspect;

    /// <summary>The school it learns spells from. Chaos learns any.</summary>
    public MagicSchool School => Record.School;

    /// <summary>Whether its True Name is known: its record's, or its flag being set (Alaron's, 590) when the sheet was made.</summary>
    public bool TrueName { get; set; }

    public bool Heavy => Record.Heavy;

    /// <summary>Protection from spells and gear, over the entity's base (<c>CharExp::protection</c>), as a byte.</summary>
    public int Protection { get; set; }

    /// <summary>Damage from gear, over the entity's base (<c>CharExp::damage</c>), as a byte.</summary>
    public int Damage { get; set; }

    /// <summary>All the experience it's gained, which sets its level (<c>CharExp::total</c>).</summary>
    public uint TotalExp { get; set; }

    /// <summary>What it has left to spend on training (<c>CharExp::spending</c>).</summary>
    public uint SpendableExp { get; set; }

    /// <summary>Its spellbook, in the order learnt.</summary>
    public IReadOnlyList<LearnedSpell> Spells => _spells;

    /// <summary>
    /// A character as <paramref name="entity"/>'s record starts it (<c>Entity::Init</c>): its stats,
    /// skills, spells and experience at its level's threshold, as a joining party member's. With
    /// <paramref name="items"/>, it wears its armor, carries its shield if it has the skill, and wields
    /// the first of its weapons it can; the rest it brings as <see cref="Spares"/>, if it wields one
    /// (<c>Entity::EquipFunc0</c>, a party member's way). A weapon it can't wield, while it wields none,
    /// is lost.
    /// </summary>
    /// <param name="spells">The spell records its spells name.</param>
    /// <param name="flags">Whether a flag is set, for <see cref="EntityRecord.TrueNameFlag"/>.</param>
    public static Character Create(EntityRecord entity, CharacterRules rules, Func<RecordId, SpellRecord> spells,
        Func<RecordId, bool>? flags = null, ItemCatalog? items = null)
    {
        var a = entity.Attributes;
        var stats = new CharacterStats(rules, [a.Intelligence, a.Willpower, a.Dexterity, a.Endurance, a.Strength, a.Stamina, entity.Level]);
        var skills = new CharacterSkills(rules, SkillLevels(entity));
        var character = new Character(entity, flags, rules, stats, skills,
            [.. entity.Spells.Select(s => new LearnedSpell(spells(s.Spell), s.Level))]);
        if (items is not null)
        {
            if (entity.Armor is { } armor)
                character.Equipment.EquipArmor(new ItemInstance(items[armor]));
            if (entity.Shield is { } shield)
                character.Equipment.EquipShield(new ItemInstance(items[shield]));
        }
        // Entity::EquipFunc0, as a party member joins. Enemies' sheets start at 0, which nothing reads.
        character.TotalExp = (uint)rules.Progression.Levels[Math.Min(stats.Base(Stat.Level), rules.Progression.Levels.Count - 1)];
        if (items is not null)
        {
            foreach (var weapon in entity.Weapons.OfType<RecordId>().Select(w => items[w]))
            {
                if (character.Equipment.Weapon is null)
                    character.Equipment.EquipWeapon(new ItemInstance(weapon));
                else
                    character.Spares.Add(weapon);
            }
        }
        return character;
    }

    /// <summary>
    /// A character from the sheet's parts, such as a save or the checkpoints' RAM holds, with the
    /// traits that don't change from <paramref name="entity"/>'s record, which is the one installed now, whatever
    /// the sheet was made from. The caller sets the rest.
    /// </summary>
    /// <param name="flags">Whether a flag is set, for <see cref="EntityRecord.TrueNameFlag"/>. Without it, only the record's True Name counts.</param>
    public static Character Restore(EntityRecord entity, CharacterRules rules, CharacterStats stats, CharacterSkills skills,
        IEnumerable<LearnedSpell> spells, Func<RecordId, bool>? flags = null) =>
        new(entity, flags, rules, stats, skills, [.. spells]);

    /// <summary>An entity's skill levels by <see cref="SkillId.Slot"/>.</summary>
    public static int?[] SkillLevels(EntityRecord entity) => SkillSlots.ToArray(entity.Skills, entity.WeaponSkills, entity.ShieldSkill);

    /// <summary>Its level, from its base Level stat.</summary>
    public int Level => Stats.Base(Stat.Level);

    /// <summary>Base Endurance, Stamina and Level, as a byte (<c>Entity::getHPMax</c>).</summary>
    public int MaxHp => (byte)(Stats.Base(Stat.Endurance) + Stats.Base(Stat.Stamina) + Stats.Base(Stat.Level));

    /// <summary>Modified Endurance, Stamina and Level, as a byte, up to <see cref="MaxHp"/> (<c>Entity::getHPCurrent</c>).</summary>
    public int Hp => Math.Min((byte)(Stats.Modified(Stat.Endurance) + Stats.Modified(Stat.Stamina) + Stats.Modified(Stat.Level)), MaxHp);

    public bool IsDead => Hp == 0;

    /// <summary>
    /// Takes Stamina's points, then Endurance's for what's left (<c>Entity::DecreaseHP</c>). What
    /// skills cost, and the rest of damage once the Level's points are gone.
    /// </summary>
    public void DrainStamina(int amount)
    {
        amount = (short)amount;
        var stamina = Stats.Modified(Stat.Stamina);
        if (stamina >= amount)
        {
            Stats.ChangeCurrent(Stat.Stamina, -amount);
            return;
        }
        amount = (short)(amount - stamina);
        Stats.ChangeCurrent(Stat.Stamina, -stamina);
        if (amount <= 0)
            return;
        var endurance = Stats.Modified(Stat.Endurance);
        Stats.ChangeCurrent(Stat.Endurance, endurance < amount ? -endurance : -amount);
    }

    /// <summary>
    /// Heals <paramref name="amount"/> points: Endurance up to its base first, then Stamina, then the
    /// Level (<c>Entity::addHP</c>).
    /// </summary>
    public void Heal(int amount)
    {
        var left = (uint)(ushort)amount;
        foreach (var stat in (ReadOnlySpan<Stat>)[Stat.Endurance, Stat.Stamina, Stat.Level])
        {
            if (left == 0)
                return;
            if (Stats.Modified(stat) >= Stats.Base(stat))
                continue;
            var gap = (uint)(byte)(Stats.Base(stat) - Stats.Modified(stat));
            if (left < gap)
            {
                Stats.ChangeCurrent(stat, (sbyte)left);
                left = 0;
            }
            else
            {
                Stats.ChangeCurrent(stat, (int)gap);
                left = (ushort)(left - gap);
            }
        }
    }

    /// <summary>
    /// Restores Stamina to its base, or to 80% of it (<c>Entity::addStamina</c>), if it's below.
    /// </summary>
    public void RestoreStamina(bool toEightyPercent = false)
    {
        var target = toEightyPercent ? (int)(Stats.Base(Stat.Stamina) * 0.8f) : Stats.Base(Stat.Stamina);
        var gain = (sbyte)(target - Stats.Modified(Stat.Stamina));
        if (gain > 0)
            Stats.ChangeCurrent(Stat.Stamina, gain);
    }

    /// <summary>
    /// Camping heals <paramref name="fraction"/> of the maximum hit points, truncated, up to what's
    /// missing, and refills the worn items' batteries by the same fraction (<c>Entity::CampHeal</c>).
    /// The party ages its effects beside it.
    /// </summary>
    public void CampHeal(float fraction)
    {
        var max = (ushort)MaxHp;
        var amount = (ushort)(max * fraction);
        var missing = (ushort)(max - Hp);
        Heal(Math.Min(amount, missing));
        Equipment.RechargeBatteries(fraction);
    }

    /// <summary>
    /// Gains experience, times the award multiplier and truncated, to spend and toward levels, and
    /// takes any levels it reaches (<c>Entity::AddExp</c>). A level raises the Level stat, and its
    /// hit points with it.
    /// </summary>
    public void AddExp(int amount)
    {
        var gained = (int)(amount * Rules.Progression.ExpAward);
        SpendableExp += (uint)gained;
        TotalExp += (uint)gained;
        Stats.Train(Stat.Level, Rules.LevelFor(TotalExp) - Stats.Base(Stat.Level));
    }

    /// <summary>The experience its next level starts at, or null at the last (<c>Entity::GetEXPTNL</c>).</summary>
    public uint? NextLevelExp => Rules.NextLevelExp(TotalExp);

    /// <summary>
    /// Whether the spell is within reach of a character who can learn Wizard, as reading a scroll and a trainer
    /// both check: its base Wizard skill at least the spell's, then its school the spell's, the spell's being none or the
    /// character Chaos, who learns any.
    /// </summary>
    public SpellLearnability CanLearn(SpellRecord spell)
    {
        if (Skills.Base(Skill.Wizard) < spell.Wizard)
            return SpellLearnability.WizardTooLow;
        if (School != MagicSchool.Chaos && spell.School != MagicSchool.None && School != spell.School)
            return SpellLearnability.WrongSchool;
        return SpellLearnability.Learnable;
    }

    internal void LearnSpell(SpellRecord spell, int level = 1) => _spells.Add(new LearnedSpell(spell, level));

    public LearnedSpell? FindSpell(RecordId spell) => _spells.FirstOrDefault(s => s.Spell.Id == spell);
}
