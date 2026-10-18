using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Characters;

/// <summary>The attributes and the level, as the original numbers them (<c>StatEnum</c>).</summary>
public enum Stat
{
    Intelligence,
    Willpower,
    Dexterity,
    Endurance,
    Strength,
    Stamina,
    /// <summary>The level. It's a stat because its points are hit points too, and the first damage takes.</summary>
    Level,
}

/// <summary>The skills, as the original numbers them (<c>SkillEnum</c>).</summary>
public enum Skill
{
    Alchemist,
    Diplomat,
    Healer,
    Loremaster,
    Mechanic,
    Merchant,
    Ranger,
    Stealth,
    Thief,
    Troubadour,
    Warrior,
    Wizard,
}

/// <summary>
/// A skill, a weapon skill or the shield skill, by where a character holds it (<c>CharSkills</c>):
/// the twelve skills, then the eleven weapon classes, then the shield.
/// </summary>
public readonly record struct SkillId(int Slot)
{
    public const int Count = 24;

    public static SkillId Shield => new(23);

    public static implicit operator SkillId(Skill skill) => new((int)skill);

    public static implicit operator SkillId(WeaponClass weapon) => new(12 + (int)weapon);

    public bool IsSkill => Slot < 12;

    public bool IsWeapon => Slot is >= 12 and < 23;

    public bool IsShield => Slot == 23;

    public Skill Skill => IsSkill ? (Skill)Slot : throw new InvalidOperationException($"{this} isn't a skill.");

    public WeaponClass Weapon => IsWeapon ? (WeaponClass)(Slot - 12) : throw new InvalidOperationException($"{this} isn't a weapon skill.");

    /// <summary><c>aidyn:skill/n</c>, or <c>aidyn:weaponskill/n</c>, where the shield is 11.</summary>
    public RecordId Record => IsSkill ? RecordId.Decimal(RecordTypes.Skill, Slot) : RecordId.Decimal(RecordTypes.WeaponSkill, Slot - 12);

    public static IEnumerable<SkillId> All => Enumerable.Range(0, Count).Select(s => new SkillId(s));

    /// <summary>The skill a record names: <c>aidyn:skill/n</c> or <c>aidyn:weaponskill/n</c>.</summary>
    public static SkillId FromRecord(RecordId record) => record.Type switch
    {
        RecordTypes.Skill => new(int.Parse(record.Key)),
        RecordTypes.WeaponSkill => new(12 + int.Parse(record.Key)),
        _ => throw new ArgumentException($"{record} isn't a skill.", nameof(record)),
    };

    public override string ToString() => IsSkill ? Skill.ToString() : IsWeapon ? Weapon.ToString() : "Shield";
}

/// <summary>
/// The tables characters are built and trained by: the stat, skill, weapon skill and progression
/// records (<c>base_stat_cap</c>, <c>EXP_TNL</c>, <c>CharSkills::GetSkillXpMod</c> and so on).
/// </summary>
public sealed class CharacterRules
{
    public const int StatCount = 7;

    public CharacterRules(IReadOnlyList<StatRecord> stats, IReadOnlyList<SkillRecord> skills, ProgressionRecord progression)
    {
        if (stats.Count != StatCount)
            throw new ArgumentException($"There are {StatCount} stats, Intelligence to Level.", nameof(stats));
        if (skills.Count != SkillId.Count)
            throw new ArgumentException($"There are {SkillId.Count} skills, weapon skills and the shield.", nameof(skills));
        if (progression.Levels is not [0, ..])
            throw new ArgumentException("Level 0 starts at 0 experience.", nameof(progression));
        Stats = stats;
        Skills = skills;
        Progression = progression;
    }

    /// <summary>Stats 0–6 by <see cref="Stat"/>.</summary>
    public IReadOnlyList<StatRecord> Stats { get; }

    /// <summary>Skills, weapon skills and the shield by <see cref="SkillId.Slot"/>.</summary>
    public IReadOnlyList<SkillRecord> Skills { get; }

    public ProgressionRecord Progression { get; }

    public StatRecord this[Stat stat] => Stats[(int)stat];

    public SkillRecord this[SkillId skill] => Skills[skill.Slot];

    public TrainingPrices Training => Progression.Training;

    /// <summary>The rules from the loaded plugins.</summary>
    public static CharacterRules From(PluginSet plugins) => new(
        [.. Enumerable.Range(0, StatCount).Select(s => plugins.Get<StatRecord>(RecordId.Decimal(RecordTypes.Stat, s)))],
        [.. SkillId.All.Select(s => plugins.Get<SkillRecord>(s.Record))],
        plugins.Singleton<ProgressionRecord>(RecordTypes.Progression));

    /// <summary>
    /// The level <paramref name="exp"/> reaches: the last whose threshold it's reached
    /// (<c>Entity::GetLevel</c>). Past the table, the last level.
    /// </summary>
    public int LevelFor(uint exp)
    {
        var levels = Progression.Levels;
        for (var level = 0; level < levels.Count; level++)
        {
            if (exp < (uint)levels[level])
                return (ushort)(level - 1);
        }
        return levels.Count - 1;
    }

    /// <summary>The experience the level after <paramref name="exp"/>'s starts at, or null past the last (<c>Entity::GetEXPTNL</c>).</summary>
    public uint? NextLevelExp(uint exp)
    {
        var next = (ushort)(LevelFor(exp) + 1);
        return next < Progression.Levels.Count ? (uint)Progression.Levels[next] : null;
    }

    /// <summary>A trainer's gold price for raising a skill or spell from <paramref name="level"/> (<c>GetSkillTrainGoldPrice</c>).</summary>
    public int GoldPrice(int level) =>
        (byte)level != 0 ? ((byte)level + 1) * Training.GoldPerLevel : Training.FirstLevelGold;
}
