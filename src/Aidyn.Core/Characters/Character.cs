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
/// Spell and potion effects come with combat (2.5); the sheet has the hooks they change:
/// <see cref="CharacterStats.ChangeModified"/>, <see cref="CharacterStats.ChangeBase"/> and
/// <see cref="CharacterSkills.Modify"/>.
/// </para>
/// </remarks>
public sealed class Character
{
    private readonly List<LearnedSpell> _spells;

    private Character(RecordId entity, string name, CharacterRules rules, CharacterStats stats, CharacterSkills skills,
        List<LearnedSpell> spells)
    {
        Entity = entity;
        Name = name;
        Rules = rules;
        Stats = stats;
        Skills = skills;
        _spells = spells;
        Equipment = new Equipment(this);
    }

    public RecordId Entity { get; }

    public string Name { get; }

    public CharacterRules Rules { get; }

    public CharacterStats Stats { get; }

    public CharacterSkills Skills { get; }

    public Equipment Equipment { get; }

    /// <summary>The only weapon it can wield (<see cref="EntityRecord.SoleWeapon"/>): Niesen's staff.</summary>
    public RecordId? SoleWeapon { get; init; }

    /// <summary>
    /// Weapons its entity brings that it doesn't wield. Joining the party puts them in the inventory
    /// (<c>Entity::EquipFunc0</c>).
    /// </summary>
    public List<Item> Spares { get; } = [];

    public Aspect Aspect { get; init; }

    /// <summary>The school it learns spells from. Chaos learns any.</summary>
    public MagicSchool School { get; init; }

    public bool TrueName { get; set; }

    public bool Heavy { get; init; }

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
        var character = new Character(entity.Id, entity.Name, rules, stats, skills,
            [.. entity.Spells.Select(s => new LearnedSpell(spells(s.Spell), s.Level))])
        {
            Aspect = entity.Aspect,
            School = entity.School,
            TrueName = entity.TrueName || (entity.TrueNameFlag is { } flag && flags?.Invoke(flag) == true),
            Heavy = entity.Heavy,
            Protection = (byte)entity.Protection,
            Damage = (byte)entity.Damage,
            SoleWeapon = entity.SoleWeapon,
        };
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
    /// traits that don't change from <paramref name="entity"/>'s record. The caller sets the rest.
    /// </summary>
    public static Character Restore(EntityRecord entity, CharacterRules rules, CharacterStats stats, CharacterSkills skills,
        IEnumerable<LearnedSpell> spells) => new(entity.Id, entity.Name, rules, stats, skills, [.. spells])
    {
        Aspect = entity.Aspect,
        School = entity.School,
        TrueName = entity.TrueName,
        Heavy = entity.Heavy,
        Protection = (byte)entity.Protection,
        Damage = (byte)entity.Damage,
        SoleWeapon = entity.SoleWeapon,
    };

    /// <summary>An entity's skill levels by <see cref="SkillId.Slot"/>.</summary>
    public static int?[] SkillLevels(EntityRecord entity)
    {
        var s = entity.Skills;
        var w = entity.WeaponSkills;
        return [s.Alchemist, s.Diplomat, s.Healer, s.Loremaster, s.Mechanic, s.Merchant, s.Ranger, s.Stealth, s.Thief, s.Troubadour,
            s.Warrior, s.Wizard, w.Bite, w.Breath, w.Claw, w.Hafted, w.Missile, w.Pole, w.Spit, w.Sting, w.Sword, w.Thrown, w.Tusk,
            entity.ShieldSkill];
    }

    /// <summary>Its level, from its base Level stat.</summary>
    public int Level => Stats.Base(Stat.Level);

    /// <summary>Base Endurance, Stamina and Level, as a byte (<c>Entity::getHPMax</c>).</summary>
    public int MaxHp => (byte)(Stats.Base(Stat.Endurance) + Stats.Base(Stat.Stamina) + Stats.Base(Stat.Level));

    /// <summary>Modified Endurance, Stamina and Level, as a byte, up to <see cref="MaxHp"/> (<c>Entity::getHPCurrent</c>).</summary>
    public int Hp => Math.Min((byte)(Stats.Modified(Stat.Endurance) + Stats.Modified(Stat.Stamina) + Stats.Modified(Stat.Level)), MaxHp);

    public bool IsDead => Hp == 0;

    /// <summary>
    /// Takes damage: the Level's points first, then as <see cref="DrainStamina"/> (<c>Entity::DamageToLevel</c>).
    /// </summary>
    /// <remarks>
    /// Combat's parts come with it (2.5): a hit ends a Troubadour's performance, and one that kills
    /// is undone by Cheat Death, or else clears the character's spell and potion effects.
    /// </remarks>
    public void TakeDamage(int amount)
    {
        amount = (short)amount;
        var level = Stats.Modified(Stat.Level);
        if (level < amount)
        {
            Stats.ChangeCurrent(Stat.Level, -level);
            DrainStamina((short)(amount - level));
        }
        else
        {
            Stats.ChangeCurrent(Stat.Level, -amount);
        }
    }

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
    /// The time passing for effects comes with them (2.5).
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

    internal void LearnSpell(SpellRecord spell) => _spells.Add(new LearnedSpell(spell, 1));

    public LearnedSpell? FindSpell(RecordId spell) => _spells.FirstOrDefault(s => s.Spell.Id == spell);
}
