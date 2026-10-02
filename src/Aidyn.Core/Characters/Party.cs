using Aidyn.Core.Rng;
using Aidyn.Plugins;

namespace Aidyn.Core.Characters;

/// <summary>
/// The party (<c>Party</c>): up to four characters in slots, Alaron first, and its gold. It answers
/// the questions scripts ask of it: skill checks, the best and worst of a stat, a member's stat.
/// </summary>
/// <remarks>
/// Who joins and leaves with what sheet comes with M2: the original sets each recruit's joined, left
/// and died flags (<c>Party::CheckFlags</c>), and keeps a sheet for each member who leaves, which it
/// restores if they rejoin (<c>SaveEntity</c>). Its items are in <c>Party.Items.cs</c>.
/// </remarks>
public sealed partial class Party
{
    public const int MaxSize = 4;

    private readonly Character?[] _slots = new Character?[MaxSize];

    /// <summary>The four slots. Members are packed to the front as others leave.</summary>
    public IReadOnlyList<Character?> Slots => _slots;

    public IEnumerable<Character> Members => _slots.OfType<Character>();

    public int Count => _slots.Count(m => m is not null);

    public uint Gold { get; set; }

    /// <summary>
    /// Puts a character in the first free slot (<c>Party::AddMember</c>), and the weapons they bring but
    /// don't wield in the inventory (<see cref="Character.Spares"/>). False if the party is full.
    /// </summary>
    public bool Add(Character member)
    {
        var slot = Array.IndexOf(_slots, null);
        if (Count == MaxSize || slot < 0)
            return false;
        _slots[slot] = member;
        foreach (var spare in member.Spares)
            Inventory?.Add(spare);
        member.Spares.Clear();
        return true;
    }

    /// <summary>Takes a member out and packs the rest to the front (<c>Party::RemoveAliveMember</c>).</summary>
    public bool Remove(RecordId entity)
    {
        var slot = Array.FindIndex(_slots, m => m?.Entity == entity);
        if (slot < 0)
            return false;
        _slots[slot] = null;
        var packed = _slots.OfType<Character>().ToArray();
        Array.Clear(_slots);
        packed.CopyTo(_slots, 0);
        return true;
    }

    /// <summary>Empties the party: its members, its gold and its inventory. Key items' flags stay as they are.</summary>
    public void Clear()
    {
        Array.Clear(_slots);
        Gold = 0;
        Inventory?.Clear();
    }

    public Character? Get(RecordId entity) => Members.FirstOrDefault(m => m.Entity == entity);

    public bool Contains(RecordId entity) => Get(entity) is not null;

    /// <summary>
    /// The members' modified Level, averaged and truncated (<c>Party::GetAvgLevel</c>). Being the
    /// modified value, damage lowers it.
    /// </summary>
    public int AverageLevel => Count == 0 ? 0 : (int)((uint)Members.Sum(m => m.Stats.Modified(Stat.Level)) / (uint)Count);

    /// <summary>The highest modified value of a stat among the members (<c>Party::BestStat</c>).</summary>
    public int BestStat(Stat stat) => Members.Select(m => m.Stats.Modified(stat)).DefaultIfEmpty().Max();

    /// <summary>The lowest modified value of a stat among the members (<c>Party::WorstStat</c>).</summary>
    public int WorstStat(Stat stat) => Members.Select(m => m.Stats.Modified(stat)).DefaultIfEmpty().Min();

    /// <summary>A member's modified value of a stat, or 0 if they aren't one (<c>Party::GetMemberStat</c>).</summary>
    public int MemberStat(RecordId entity, Stat stat) => Get(entity)?.Stats.Modified(stat) ?? 0;

    /// <summary>
    /// The member with the highest modified level in a skill, the later on a tie, dead or not, or
    /// null if the party is empty (<c>Party::GetMostSkilledMember</c>). A member at 0 still counts.
    /// </summary>
    public Character? MostSkilled(SkillId skill)
    {
        Character? best = null;
        var level = 0;
        foreach (var member in Members)
        {
            var own = member.Skills.Modified(skill);
            if (own >= level)
                (best, level) = (member, own);
        }
        return best;
    }

    /// <summary>
    /// Camping heals the living members, fully or by half (<c>Party::CampHeal</c>). The hours
    /// passing, and gear's charges, come with camping (2.7).
    /// </summary>
    public void CampHeal(bool half = false)
    {
        foreach (var member in Members.Where(m => !m.IsDead))
            member.CampHeal(half ? 0.5f : 1f);
    }

    /// <summary>
    /// The party's check of a skill for scripts (<c>Party::SkillCheck</c>), 0 to 21. Each skill has its
    /// own formula, below; most roll d100 against the skill and an attribute of the member best at it
    /// and turn what's left into a step (<see cref="SkillCheckStep"/>).
    /// </summary>
    public int SkillCheck(Skill skill, IRandom random) => skill switch
    {
        Skill.Alchemist => Against(Skill.Alchemist, random, m => m.Stats.Modified(Stat.Intelligence) * 3 + m.Skills.Modified(Skill.Alchemist) * 6),
        // Rolled twice when the first passes, and the second is what counts.
        Skill.Diplomat => Pooled(Skill.Diplomat, 10, 50, random) is var first && first < 3 ? 2 : Pooled(Skill.Diplomat, 10, 50, random),
        Skill.Healer => Against(Skill.Healer, random, m => m.Stats.Modified(Stat.Intelligence) * 3 + m.Skills.Modified(Skill.Healer) * 10),
        Skill.Loremaster => Pooled(Skill.Loremaster, 6, 0, random),
        Skill.Mechanic => Against(Skill.Mechanic, random,
            m => (m.Skills.Modified(Skill.Mechanic) * 5 + m.Stats.Modified(Stat.Intelligence) + m.Stats.Modified(Stat.Dexterity)) * 2),
        Skill.Merchant => Merchant(random),
        Skill.Ranger => SkillCheckStep((short)(random.RollD(1, 100) - ((byte)(MostSkilled(Skill.Ranger)?.Skills.Modified(Skill.Ranger) ?? 0) * 10 + 80))),
        // The original's Stealth check is a stub.
        Skill.Stealth => 0,
        Skill.Thief => Against(Skill.Thief, random, m => m.Stats.Modified(Stat.Intelligence) * 4 + m.Skills.Modified(Skill.Thief) * 10),
        Skill.Troubadour => Troubadour(random),
        Skill.Warrior => Against(Skill.Warrior, random,
            m => m.Stats.Modified(Stat.Intelligence) + m.Skills.Modified(Skill.Warrior) * 7 + m.Stats.Modified(Stat.Stamina)),
        Skill.Wizard => Against(Skill.Wizard, random, m => (m.Skills.Modified(Skill.Wizard) * 5 + m.Stats.Modified(Stat.Intelligence)) * 2),
        _ => 0,
    };

    /// <summary>
    /// The party's search for a secret door (<c>Party::SecretLock</c>): the best member's Intelligence × 3
    /// + Ranger × 10, dead or not, against d100. Below it, the step for what's left; otherwise 0.
    /// </summary>
    public int SecretSearch(IRandom random)
    {
        var best = Members.Select(m => m.Stats.Modified(Stat.Intelligence) * 3 + m.Skills.Modified(Skill.Ranger) * 10).DefaultIfEmpty().Max();
        var roll = random.RollD(1, 100);
        return roll < best ? SkillCheckStep((short)(best - roll)) : 0;
    }

    /// <summary>
    /// A step from 1 to 21 for what a roll left (<c>gSkillCheck</c>): 1 below 26, then 2 from 26,
    /// 3 from 46, 4 from 62, 5 from 81, 6 from 96, 7 from 111, and one more every 11 up to 21 from 266.
    /// </summary>
    public static int SkillCheckStep(short value)
    {
        var step = 21;
        while (step > 0)
        {
            step--;
            if (value >= Steps[step])
                break;
        }
        return step + 1;
    }

    private static readonly int[] Steps = [0, 26, 46, 62, 81, 96, 111, 122, 133, 144, 155, 166, 177, 188, 199, 210, 221, 232, 243, 255, 266];

    // d100 less the bonus of the member best at the skill: a low roll and a high bonus give a low step.
    private int Against(Skill skill, IRandom random, Func<Character, int> bonus)
    {
        if (MostSkilled(skill) is not { } member)
            return 0;
        var total = (ushort)bonus(member);
        return SkillCheckStep((short)(random.RollD(1, 100) - total));
    }

    // The living members' best Intelligence × 3 and their levels in the skill summed, less d100:
    // here a high bonus gives a high step, and a roll at or over it gives 0.
    private int Pooled(Skill skill, int perLevel, int extra, IRandom random)
    {
        byte intelligence = 0, levels = 0;
        foreach (var member in Members.Where(m => !m.IsDead))
        {
            intelligence = Math.Max(intelligence, (byte)member.Stats.Modified(Stat.Intelligence));
            levels = (byte)(levels + member.Skills.Modified(skill));
        }
        var total = (ushort)(intelligence * 3 + levels * perLevel + extra);
        var roll = random.RollD(1, 100);
        return roll < total ? SkillCheckStep((short)(total - roll)) : 0;
    }

    // Every member's best Intelligence, dead or not, and the best Merchant's level.
    private int Merchant(IRandom random)
    {
        var intelligence = (byte)BestStat(Stat.Intelligence);
        var level = (byte)(MostSkilled(Skill.Merchant)?.Skills.Modified(Skill.Merchant) ?? 0);
        return SkillCheckStep((short)(random.RollD(1, 100) - (ushort)(intelligence * 3 + level * 10)));
    }

    // The best Troubadour's Dexterity × 3 and level, less d100, as with the pooled checks. None if
    // no one has the skill.
    private int Troubadour(IRandom random)
    {
        if (MostSkilled(Skill.Troubadour) is not { } member || member.Skills.Modified(Skill.Troubadour) == 0)
            return 0;
        var total = (ushort)(member.Skills.Modified(Skill.Troubadour) + member.Stats.Modified(Stat.Dexterity) * 3);
        var roll = random.RollD(1, 100);
        return roll < total ? (byte)SkillCheckStep((short)(total - roll)) : 0;
    }
}
