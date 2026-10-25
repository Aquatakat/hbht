namespace HBHT.Core.Characters;

/// <summary>
/// A character's skills, weapon skills and shield skill (<c>CharSkills</c>), each held as a base
/// level and a modified one, as signed bytes. A base of −1 is a skill the character can never
/// learn; gear and spells change the modified level.
/// </summary>
/// <remarks>
/// The original raises flags for the party's best skills as these change (<c>event_flag_skill_</c>),
/// but its lookup never finds them (see the hardcoded-content audit), so nothing is raised here.
/// </remarks>
public sealed class CharacterSkills
{
    /// <summary>A base level that can never be raised.</summary>
    public const int CantLearn = -1;

    private readonly CharacterRules _rules;
    private readonly sbyte[] _base = new sbyte[SkillId.Count];
    private readonly sbyte[] _modified = new sbyte[SkillId.Count];

    /// <summary>
    /// Starts at <paramref name="levels"/>, with the modified levels at least 0 (<c>CharSkills::Init</c>).
    /// </summary>
    /// <param name="levels">By <see cref="SkillId.Slot"/>, null for one that can't be learnt.</param>
    public CharacterSkills(CharacterRules rules, IReadOnlyList<int?> levels)
    {
        if (levels.Count != SkillId.Count)
            throw new ArgumentException($"There are {SkillId.Count} skills, weapon skills and the shield.", nameof(levels));
        _rules = rules;
        for (var i = 0; i < SkillId.Count; i++)
        {
            _base[i] = (sbyte)(levels[i] ?? CantLearn);
            _modified[i] = Math.Max(_base[i], (sbyte)0);
        }
    }

    /// <summary>The levels as the original holds them, such as from the checkpoints' RAM.</summary>
    public CharacterSkills(CharacterRules rules, IReadOnlyList<int> @base, IReadOnlyList<int> modified)
        : this(rules, [.. @base.Select(b => (int?)b)])
    {
        for (var i = 0; i < SkillId.Count; i++)
            _modified[i] = (sbyte)modified[i];
    }

    /// <summary>The base level, unread by the cap. −1 can't be learnt.</summary>
    public int RawBase(SkillId skill) => _base[skill.Slot];

    /// <summary>The modified level, unread by the cap. It can be negative.</summary>
    public int RawModified(SkillId skill) => _modified[skill.Slot];

    /// <summary>
    /// The base level, down to the base cap, or −1 if it can't be learnt
    /// (<c>CharSkills::capSkillBaseMax</c>). What the skill screen shows and trainers teach up to.
    /// </summary>
    public int Base(SkillId skill) => Math.Min(_base[skill.Slot], _rules[skill].BaseCap);

    /// <summary>The modified level, capped and at least 0 (<c>CharSkills::getModdedSkill</c>). What rules read.</summary>
    public int Modified(SkillId skill) => Math.Clamp((int)_modified[skill.Slot], 0, _rules[skill].Cap);

    public bool CanLearn(SkillId skill) => _base[skill.Slot] != CantLearn;

    /// <summary>The skills, weapon skills and shield it's above level 0 in, in the skill screen's order.</summary>
    public IEnumerable<SkillId> Learnt => SkillId.All.Where(s => Base(s) > 0);

    /// <summary>Whether training can't raise it further (<c>CharSkills::isSkillCapped</c>).</summary>
    public bool IsCapped(SkillId skill) => _rules[skill].BaseCap <= _base[skill.Slot];

    /// <summary>
    /// What training it one level costs in full experience: (base + 1)² × its cost
    /// (<c>CharSkills::GetSkillXpMod</c>, <c>GetWeaponXpMod</c>, <c>GetShieldXpPrice</c>).
    /// </summary>
    public int TrainingCost(SkillId skill) => CharacterRules.TrainingCost(_base[skill.Slot], _rules[skill].TrainingCost);

    /// <summary>
    /// Raises the base level by <paramref name="amount"/>, up to the base cap, and the modified level
    /// by what the base's capped reading gained (<c>AddToSkill</c>). One that can't be learnt stays so.
    /// </summary>
    public void Train(SkillId skill, int amount)
    {
        var i = skill.Slot;
        var cap = _rules[skill].BaseCap;
        var before = (sbyte)Math.Min((int)_base[i], cap);
        if (before == CantLearn)
            return;
        if (_base[i] + (sbyte)amount > cap)
            amount = (sbyte)(cap - _base[i]);
        _base[i] = (sbyte)(_base[i] + (sbyte)amount);
        var after = (sbyte)Math.Min((int)_base[i], cap);
        Modify(skill, (sbyte)(after - before));
    }

    /// <summary>
    /// Gear, a potion or a spell changing the modified level; nothing for one that can't be learnt
    /// (<c>AddToModdedSkill</c>).
    /// </summary>
    public void Modify(SkillId skill, int amount)
    {
        if (_base[skill.Slot] >= 0)
            _modified[skill.Slot] = (sbyte)(_modified[skill.Slot] + (sbyte)amount);
    }
}
