using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Characters;

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

/// <summary>The stat records (<c>aidyn:stat/0</c>–<c>6</c>) by <see cref="Stat"/>.</summary>
public static class StatId
{
    public static RecordId Record(Stat stat) => RecordId.Decimal(RecordTypes.Stat, (int)stat);

    /// <summary>The stat a base record names, or null for another, such as a mod's.</summary>
    public static Stat? TryFromRecord(RecordId record) =>
        record.Type == RecordTypes.Stat && record.Namespace == RecordId.BaseNamespace && int.TryParse(record.Key, out var n) && n is >= 0 and < CharacterRules.StatCount
            ? (Stat)n : null;

    /// <summary>The stat a record names.</summary>
    public static Stat FromRecord(RecordId record) => record.Type == RecordTypes.Stat
        ? (Stat)int.Parse(record.Key)
        : throw new ArgumentException($"{record} isn't a stat.", nameof(record));
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

    public bool IsSkill => Slot < 12;

    public bool IsWeapon => Slot is >= 12 and < 23;

    public bool IsShield => Slot == 23;

    public Skill Skill => IsSkill ? (Skill)Slot : throw new InvalidOperationException($"{this} isn't a skill.");

    /// <summary>The weapon class, the weapon skill's key.</summary>
    public int Weapon => IsWeapon ? Slot - 12 : throw new InvalidOperationException($"{this} isn't a weapon skill.");

    /// <summary><c>aidyn:skill/n</c>, or <c>aidyn:weaponskill/n</c>, where the shield is 11.</summary>
    public RecordId Record => IsSkill ? RecordId.Decimal(RecordTypes.Skill, Slot) : RecordId.Decimal(RecordTypes.WeaponSkill, Slot - 12);

    public static IEnumerable<SkillId> All => Enumerable.Range(0, Count).Select(s => new SkillId(s));

    /// <summary>The weapon skill of the original's weapon class <paramref name="weaponClass"/>, <c>aidyn:weaponskill/n</c>.</summary>
    public static SkillId WeaponSkill(int weaponClass) => new(12 + weaponClass);

    /// <summary>The Thrown weapon skill, which a flask's throw trains (<c>aidyn:weaponskill/9</c>).</summary>
    public static SkillId Thrown => WeaponSkill(9);

    /// <summary>The skill a base record names, or null for another, such as a mod's.</summary>
    public static SkillId? TryFromRecord(RecordId record) => SkillSlots.Slot(record) is { } slot ? new SkillId(slot) : null;

    /// <summary>The skill a record names: <c>aidyn:skill/n</c> or <c>aidyn:weaponskill/n</c>.</summary>
    public static SkillId FromRecord(RecordId record) => record.Type switch
    {
        RecordTypes.Skill => new(int.Parse(record.Key)),
        RecordTypes.WeaponSkill => new(12 + int.Parse(record.Key)),
        _ => throw new ArgumentException($"{record} isn't a skill.", nameof(record)),
    };

    public override string ToString() => IsSkill ? Skill.ToString() : IsWeapon ? $"Weapon skill {Weapon}" : "Shield";
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

    /// <summary>What spells, potions and enchantments are made of.</summary>
    public required Magic.MagicRules Magic { get; init; }

    public StatRecord this[Stat stat] => Stats[(int)stat];

    public SkillRecord this[SkillId skill] => Skills[skill.Slot];

    public TrainingPrices Training => Progression.Training;

    /// <summary>The schools (<c>aidyn:school/n</c>) by ID; none, and no school learns every school's spells.</summary>
    public IReadOnlyDictionary<RecordId, SchoolRecord> Schools { get; init; } = new Dictionary<RecordId, SchoolRecord>();

    /// <summary>The elements (<c>aidyn:element/n</c>) by ID.</summary>
    public IReadOnlyDictionary<RecordId, ElementRecord> Elements { get; init; } = new Dictionary<RecordId, ElementRecord>();

    /// <summary>The unschooled, <c>aidyn:school/4</c>: anyone may learn its spells, and its effects can't be dispelled.</summary>
    public static readonly RecordId Unschooled = RecordId.Decimal(RecordTypes.School, 4);

    /// <summary>Whether a character of <paramref name="school"/> learns every school's spells: Chaos.</summary>
    public bool LearnsEverySchool(RecordId school) => Schools.GetValueOrDefault(school)?.LearnsEverySchool == true;

    /// <summary>The rules from the loaded plugins.</summary>
    public static CharacterRules From(PluginSet plugins) => new(
        [.. Enumerable.Range(0, StatCount).Select(s => plugins.Get<StatRecord>(StatId.Record((Stat)s)))],
        [.. SkillId.All.Select(s => plugins.Get<SkillRecord>(s.Record))],
        plugins.Singleton<ProgressionRecord>())
    {
        Magic = HBHT.Core.Magic.MagicRules.From(plugins),
        Schools = plugins.All<SchoolRecord>(RecordTypes.School).ToDictionary(s => s.Id),
        Elements = plugins.All<ElementRecord>(RecordTypes.Element).ToDictionary(e => e.Id),
    };

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

    /// <summary>
    /// What raising a stat, skill or spell from <paramref name="level"/> costs in experience: (level + 1)² × its
    /// <paramref name="cost"/> (<c>CharStats::GetTraningPrice</c>, <c>CharSkills::GetSkillXpMod</c>, <c>WidgetSpellTrain::Confirm</c>).
    /// </summary>
    public static int TrainingCost(int level, int cost) => (level + 1) * (level + 1) * cost;

    /// <summary>A trainer's gold price for raising a skill or spell from <paramref name="level"/> (<c>GetSkillTrainGoldPrice</c>).</summary>
    public int GoldPrice(int level) =>
        (byte)level != 0 ? ((byte)level + 1) * Training.GoldPerLevel : Training.FirstLevelGold;
}
