using HBHT.Core.Characters;
using HBHT.Core.Rng;

namespace HBHT.Core.Combat;

/// <summary>
/// A round's turns (<c>Combat.Current</c>'s <c>+0x30</c>, <c>TurnOrder</c>, <c>turn.cpp</c>): the combatants' slots in the
/// order they act, and the turn now. <see cref="Build"/> ports <c>TurnOrder.Build</c>; a turn's start and end and a round's
/// end are session 3's.
/// </summary>
public sealed class TurnOrder
{
    private readonly List<int> _turns = [];

    /// <summary>The round's turns, as combatants' slots (<c>+0x38</c>).</summary>
    public IReadOnlyList<int> Turns => _turns;

    /// <summary>The turn now (<c>+0x31</c>).</summary>
    public int Now { get; internal set; }

    /// <summary>The alternation toggle (<c>+0x34</c>): the 18–24 and 30–34 bands take their extra turn while it's set.</summary>
    public bool Toggle { get; internal set; }

    /// <summary>
    /// A combatant's turns a round by its <b>base</b> Dexterity (<c>Combatant.TurnsPerRound</c>, <c>0x8005F4D0</c>): none at 0,
    /// 1 below 18, 1 or 2 by the toggle to 24, 2 to 29, 2 or 3 by the toggle to 34, and 3 from 35. The base is capped, as
    /// the original's (<c>Stats.Base</c>, <c>0x8007B4D0</c>), at 30 in the data, so 35 isn't reached.
    /// </summary>
    public static int TurnsPerRound(int dexterity, bool toggle) => dexterity switch
    {
        <= 0 => 0,
        < 18 => 1,
        <= 24 => toggle ? 2 : 1,
        <= 29 => 2,
        <= 34 => toggle ? 3 : 2,
        _ => 3,
    };

    /// <summary>
    /// Builds the round's turns (<c>TurnOrder.Build</c>, <c>0x8006A16C</c>): of the combatants alive and in play, counting
    /// those able to act, each that can act has its turns a round. Every third turn comes first, then every second, then
    /// every first, each group in random order: a slot drawn from all of them (<c>Rng.Range</c>) until each of the group's
    /// is drawn, drawing again on one not in it. Before them, with the toggle set, one turn for each with none.
    /// </summary>
    public void Build(Battle battle, IRandom random)
    {
        var n = battle.InPlay;
        var third = new bool[n];
        var second = new bool[n];
        var first = new bool[n];
        var none = new bool[n];
        int all = 0, party = 0, enemies = 0;
        for (var i = 0; i < n; i++)
        {
            if (battle.Slots[i] is not { } c || c.Sheet.IsDead || c.Flags.HasFlag(CombatantFlags.Gone))
                continue;
            all++;
            if (i < battle.PartySize)
                party++;
            else
                enemies++;
            if (!c.Active)
                continue;
            switch (TurnsPerRound(c.Sheet.Stats.Base(Stat.Dexterity), Toggle))
            {
                case 0:
                    none[i] = Toggle;
                    break;
                case 1:
                    first[i] = true;
                    break;
                case 2:
                    second[i] = first[i] = true;
                    break;
                default:
                    third[i] = second[i] = first[i] = true;
                    break;
            }
        }
        battle.ActiveCounts = (all, party, enemies);
        _turns.Clear();
        foreach (var group in new[] { none, third, second, first })
        {
            for (var left = group.Count(t => t); left > 0;)
            {
                var i = random.Range(0, n - 1);
                if (!group[i])
                    continue;
                _turns.Add(i);
                group[i] = false;
                left--;
            }
        }
        Now = 0;
    }

    /// <summary>The combatant whose turn it is, or null with none.</summary>
    public Combatant? Current(Battle battle) => Now < _turns.Count ? battle.Slots[_turns[Now]] : null;
}
