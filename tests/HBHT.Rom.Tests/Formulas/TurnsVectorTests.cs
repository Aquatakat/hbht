using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// The turns' formulas against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>Combatant.UpdateRadius</c>
/// (<c>0x8005F29C</c>) on each living combatant, <c>Combatant.TurnsPerRound</c> (<c>0x8005F4D0</c>) and <c>TurnOrder.Build</c>
/// (<c>0x8006A16C</c>), run in the interpreter by <c>tools/aivectors/formulas.py turns</c> (<see cref="TurnsVectors"/>), and the port's
/// <see cref="Combatant.UpdateRadius"/>, <see cref="TurnOrder.TurnsPerRound"/> and <see cref="TurnOrder.Build"/> on the same state with
/// the same draws. The tests skip without the checkpoints (and the parity logs, for the vectors on their states).
/// </summary>
public sealed class TurnsVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
    };

    /// <summary>The family's own pokes: the turn order's toggle, and a combatant's flags set and cleared.</summary>
    private static readonly Dictionary<string, FormulaState.PokeHandler> Pokes = new()
    {
        ["toggle"] = (b, a) => b.Battle.Order.Toggle = FormulaState.Int(a[0]) != 0,
        ["flags"] = (b, a) =>
        {
            var c = FormulaState.Slot(b, a[0]);
            c.Flags = (c.Flags | (CombatantFlags)FormulaState.Int(a[1])) & ~(CombatantFlags)FormulaState.Int(a[2]);
        },
    };

    public static TheoryData<string> Ids => [.. TurnsVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheTurnsAreTheOriginals(string id)
    {
        var v = TurnsVectors.Vectors.Single(v => v.Id == id);
        var found = Run(v);
        output.WriteLine($"original: {v.Expect}");
        if (KnownDifferences.TryGetValue(id, out var why))
        {
            Assert.True(found.Count > 0, $"{id} is the original's now: take it off the known differences");
            output.WriteLine($"a known difference, {why}:\n  {string.Join("\n  ", found)}");
            return;
        }
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    /// <summary>The vector's state and pokes laid over the port's battle, the port's functions run, and what differs.</summary>
    private List<string> Run(FormulaVector v)
    {
        var built = FormulaState.Build(Set, v.Source);
        FormulaState.Poke(built, v.Pokes, Pokes);
        var battle = built.Battle;
        var slots = Enumerable.Range(0, battle.InPlay)
            .Where(i => battle.Slots[i] is { } c && !c.Sheet.IsDead && !c.Flags.HasFlag(CombatantFlags.Gone)).ToList();

        var draws = new FormulaState.Draws(built.Game.Random.Combat, v.Seed, v.Script);
        foreach (var i in slots)
            built[i].UpdateRadius(battle);
        var radius = slots.Select(i => built[i].Radius).ToList();
        var flags = slots.Select(i => (int)(built[i].Flags & (CombatantFlags.Engaged | CombatantFlags.Performing))).ToList();
        var turns = slots.Select(i => TurnOrder.TurnsPerRound(built[i].Sheet.Stats.Base(Stat.Dexterity), battle.Order.Toggle)).ToList();
        battle.Order.Build(battle, built.Game.Random.Combat);
        var order = battle.Order.Turns;
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        var actual = new List<(string, object)>
        {
            ("slots", string.Join(',', slots)),
            ("radius", string.Join(',', radius)),
            ("flags", string.Join(',', flags)),
            ("turns", string.Join(',', turns)),
            ("troubadour", battle.TroubadourBonus),
            ("order", string.Join(',', order)),
            ("active", battle.ActiveCounts.All),
            ("party", battle.ActiveCounts.Party),
            ("enemies", battle.ActiveCounts.Enemies),
            ("current", order.Count > 0 ? order[0] : -1),
            ("draws", draws.Count),
        };
        return FormulaState.Differences(v.Expect, actual);
    }
}
