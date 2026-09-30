namespace Aidyn.Core.Characters;

/// <summary>
/// A character's attributes and level (<c>CharStats_s</c>), each held four ways, as bytes that wrap
/// as the original's do:
/// <list type="bullet">
/// <item><b>Trained</b> (<c>Base</c>): what the entity started with, raised by training and levels.</item>
/// <item><b>Current</b> (<c>health</c>): trained, less damage and plus healing. Saved with the trained value.</item>
/// <item><b>Base</b> (<c>magicMod</c>): trained, plus what spells and potions raise the base by. What the
/// sheet shows as the base, and what maximum hit points count.</item>
/// <item><b>Modified</b> (<c>Modded</c>): everything: trained, damage, spells and gear. What rules read.</item>
/// </list>
/// </summary>
/// <remarks>
/// The original raises flags for the party's best stats as these change (<c>Event_flag_stat</c>),
/// but its lookup never finds them (see the hardcoded-content audit), so nothing is raised here.
/// </remarks>
public sealed class CharacterStats
{
    private readonly CharacterRules _rules;
    private readonly byte[] _trained = new byte[CharacterRules.StatCount];
    private readonly byte[] _current = new byte[CharacterRules.StatCount];
    private readonly byte[] _base = new byte[CharacterRules.StatCount];
    private readonly byte[] _modified = new byte[CharacterRules.StatCount];

    /// <summary>All four start at <paramref name="values"/> (<c>CharStats::Init</c>).</summary>
    /// <param name="values">Intelligence to Level, as bytes.</param>
    public CharacterStats(CharacterRules rules, IReadOnlyList<int> values)
    {
        if (values.Count != CharacterRules.StatCount)
            throw new ArgumentException($"There are {CharacterRules.StatCount} stats.", nameof(values));
        _rules = rules;
        for (var i = 0; i < values.Count; i++)
            _trained[i] = _current[i] = _base[i] = _modified[i] = (byte)values[i];
    }

    /// <summary>
    /// The four values as the original holds them, such as from a save or the checkpoints' RAM:
    /// trained, current, base and modified (<c>CharStats::Load</c> sets the last two from the first two).
    /// </summary>
    public CharacterStats(CharacterRules rules, IReadOnlyList<int> trained, IReadOnlyList<int> current, IReadOnlyList<int> @base,
        IReadOnlyList<int> modified)
        : this(rules, trained)
    {
        for (var i = 0; i < CharacterRules.StatCount; i++)
        {
            _current[i] = (byte)current[i];
            _base[i] = (byte)@base[i];
            _modified[i] = (byte)modified[i];
        }
    }

    /// <summary>The trained value, unread by the cap (<c>Base</c>). Training and saves use it.</summary>
    public int Trained(Stat stat) => _trained[(int)stat];

    /// <summary>The trained value less damage (<c>health</c>), unread by the cap.</summary>
    public int Current(Stat stat) => (sbyte)_current[(int)stat];

    /// <summary>The base value, capped (<c>CharStats::getBase</c>): what the sheet shows and maximum hit points count.</summary>
    public int Base(Stat stat) => Cap(_base[(int)stat], _rules[stat].BaseCap);

    /// <summary>The trained value, capped (<c>CharStats::GetBase2</c>).</summary>
    public int TrainedCapped(Stat stat) => Cap(_trained[(int)stat], _rules[stat].BaseCap);

    /// <summary>The modified value, capped and at least 0 (<c>CharStats::getModded</c>). What rules read.</summary>
    public int Modified(Stat stat) => Cap((sbyte)_modified[(int)stat], _rules[stat].Cap);

    /// <summary>The modified value, unread by the cap. It can be negative.</summary>
    public int RawModified(Stat stat) => (sbyte)_modified[(int)stat];

    /// <summary>The base value, unread by the cap.</summary>
    public int RawBase(Stat stat) => _base[(int)stat];

    /// <summary>Whether training can't raise it further (<c>CharStats::isStatCapped</c>).</summary>
    public bool IsCapped(Stat stat) => _rules[stat].BaseCap <= _trained[(int)stat];

    /// <summary>Whether damage has taken it below its trained value (<c>CharStats::someStatCheck</c>).</summary>
    public bool IsHurt(Stat stat) => (sbyte)_current[(int)stat] < _trained[(int)stat];

    /// <summary>
    /// What training it one point costs in experience: (trained + 1)² × its cost
    /// (<c>CharStats::GetTraningPrice</c>). Null for a stat that isn't trained, the level.
    /// </summary>
    public int? TrainingCost(Stat stat) =>
        _rules[stat].TrainingCost is { } cost ? (_trained[(int)stat] + 1) * (_trained[(int)stat] + 1) * cost : null;

    /// <summary>
    /// Raises the trained value, and the others with it, by <paramref name="amount"/>, up to the base
    /// cap (<c>CharStats::AddBase</c>). The cap test is unsigned, so an amount that would take it
    /// below 0 raises it to the cap instead.
    /// </summary>
    public void Train(Stat stat, int amount)
    {
        var i = (int)stat;
        var cap = _rules[stat].BaseCap;
        if ((uint)cap < (uint)(_trained[i] + amount))
            amount = cap - _trained[i];
        _trained[i] = (byte)(_trained[i] + amount);
        _base[i] = (byte)(_base[i] + amount);
        ChangeCurrent(stat, amount);
    }

    /// <summary>Damage or healing: changes the current and modified values (<c>CharStats::addModdedHealth</c>).</summary>
    public void ChangeCurrent(Stat stat, int amount)
    {
        _current[(int)stat] = (byte)(_current[(int)stat] + amount);
        _modified[(int)stat] = (byte)(_modified[(int)stat] + amount);
    }

    /// <summary>A spell or potion changing the base: changes the base and modified values (<c>CharStats::AddModdedMagic</c>).</summary>
    public void ChangeBase(Stat stat, int amount)
    {
        _base[(int)stat] = (byte)(_base[(int)stat] + amount);
        _modified[(int)stat] = (byte)(_modified[(int)stat] + amount);
    }

    /// <summary>Gear changing it: changes the modified value alone (<c>CharStats::AddModded</c>).</summary>
    public void ChangeModified(Stat stat, int amount) => _modified[(int)stat] = (byte)(_modified[(int)stat] + amount);

    // CharStats::capStatMax: below 0 reads as 0, and above the cap as the cap.
    private static int Cap(int value, int cap) => value < 0 ? 0 : Math.Min(value, cap);
}
