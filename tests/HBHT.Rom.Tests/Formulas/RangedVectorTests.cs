using System.Numerics;
using System.Reflection;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// Ranged attacks' and flasks' formulas against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>Combat.RangedChance</c>
/// (<c>0x80062F50</c>) and <c>Combatant.ResolveRanged</c> (<c>0x80063324</c>) for a shot, <c>Combatant.ThrowFlask</c>
/// (<c>0x80065600</c>) with <c>ThrowChance</c>, <c>Combat.WeatherPenalty</c> and <c>FlaskDamage</c>, then <c>FlaskExplosion</c>
/// (<c>0x800652F8</c>) for a throw, run in the interpreter by <c>tools/aivectors/formulas.py ranged</c> (<see cref="RangedVectors"/>),
/// and the port's on the same state with the same draws. The tests skip without the checkpoints (and the parity logs, for the vectors
/// on their states).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class RangedVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
    };

    /// <summary>The family's own pokes, as <c>families/ranged.py</c>'s.</summary>
    private static readonly Dictionary<string, FormulaState.PokeHandler> Pokes = new()
    {
        ["weather"] = (b, a) => b.Game.Weather!.Kind = (WeatherKind)FormulaState.Int(a[0]),
        ["rain"] = (b, a) => b.Game.Weather!.Intensity = FormulaState.Float(a[0]),
        ["moves"] = (b, a) => FormulaState.Slot(b, a[0]).Moves = FormulaState.Int(a[1]),
        ["moved"] = (b, a) => FormulaState.Slot(b, a[0]).Flags |= CombatantFlags.Moved,
        // A flask in hand (item 0x1000 + the kind), two of it in the party's inventory so the throw leaves one, as the RAM's poke does.
        ["flask"] = (b, a) =>
        {
            var flask = b.Game.Items[RecordId.Hex(RecordTypes.Item, 0x1000 + FormulaState.Int(a[1]))];
            b.Game.Party.Inventory.Add(flask, 2);
            FormulaState.Slot(b, a[0]).HoldPotion(flask, b.Battle);
        },
        ["protection"] = (b, a) => FormulaState.Slot(b, a[0]).Sheet.Protection = FormulaState.Int(a[1]),
    };

    private static readonly MethodInfo ThrowFlask = typeof(Battle).GetMethod("ThrowFlask", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static TheoryData<string> Ids => [.. RangedVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheRangedFormulasAreTheOriginals(string id)
    {
        var v = RangedVectors.Vectors.Single(v => v.Id == id);
        var found = Run(v);
        output.WriteLine($"original: {v.Expect}\n  draws: {v.Draws}");
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
        return v.Args.Length > 2 ? Throw(built, v) : Shot(built, v);
    }

    /// <summary>
    /// A flask thrown (<c>ThrowFlask</c>, with the chance as it asks for it) and on a hit its blast (<c>FlaskExplosion</c>, as
    /// <c>Combat.EndCast</c> calls it), a second time for two throws; then every combatant's hit points, Stamina, protection (with no
    /// shield, <c>TotalProtection</c>'s) and damage byte.
    /// </summary>
    private List<string> Throw(CombatCheckpoints.Built built, FormulaVector v)
    {
        var battle = built.Battle;
        var (a, t) = (built[v.Args[0]], built[v.Args[1]]);
        var distance = (byte)(float)Vector2.Distance(a.Cell, t.Cell);
        var chance = battle.ThrowChance(a, t, distance);

        var stream = built.Game.Random.Combat;
        var draws = new FormulaState.Draws(stream, v.Seed, v.Script);
        // Each draw's raw value: the stream's seed as the next draw begins, or at the end.
        var values = new List<uint>();
        var counting = stream.BeforeDraw!;
        stream.BeforeDraw = s =>
        {
            if (draws.Count > 0)
                values.Add(s.Seed);
            counting(s);
        };
        var cells = new List<(int, int)>();
        var (firstDamage, throwDraws, skill) = (0, 0, 0);
        for (var n = 0; n < (v.Args[2] == 2 ? 2 : 1); n++)
        {
            battle.BlastCell = (0xFF, 0xFF);
            a.Damage = 0;
            ThrowFlask.Invoke(battle, [a, t]);
            if (n == 0)
                (firstDamage, throwDraws, skill) = (a.Damage, draws.Count, a.Sheet.Skills.RawBase(SkillId.Thrown));
            if (battle.BlastCell == (0xFF, 0xFF))
            {
                // Missed, and burst about the last flask's cell all the same, as Combat.EndCast does with more of the flask in hand.
                if (v.Args[2] == 3)
                {
                    battle.BlastCell = (v.Args[3], v.Args[4]);
                    battle.FlaskExplosion(a, t, a.Damage);
                }
                break;
            }
            cells.Add(battle.BlastCell);
            var damage = a.Damage;
            if (n == 0)
            {
                foreach (var c in battle.Combatants)
                    c.Damage = 0;
            }
            battle.FlaskExplosion(a, t, damage);
        }
        values.Add(stream.Seed);
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");

        var roll = (int)(100UL * values[0] >> 32) + 1;
        var adjusted = draws.Drawn.Count > 1 && draws.Drawn[1] == "Casting.AdjustRoll" ? roll - ((int)(100UL * values[1] >> 32) + 1) : roll;
        var hit = cells.Count > 0;
        var actual = new List<(string, object)>
        {
            ("distance", distance),
            ("chance", chance),
            ("roll", roll),
            ("adjusted", adjusted),
            ("hit", hit ? 1 : 0),
            ("damage", firstDamage),
            ("cell", hit ? $"{cells[0].Item1},{cells[0].Item2}" : "-"),
            ("skill", skill),
            ("throwdraws", throwDraws),
            ("draws", draws.Count),
            ("active", $"{battle.ActiveCounts.All},{battle.ActiveCounts.Party},{battle.ActiveCounts.Enemies}"),
        };
        foreach (var c in battle.Slots.OfType<Combatant>())
            actual.Add(($"s{c.Slot}", $"{c.Sheet.Hp}/{c.Sheet.Stats.Modified(Stat.Stamina)}/{Battle.Protection(c.Sheet, behind: true)}/{c.Damage}"));
        return FormulaState.Differences(v.Expect, actual);
    }

    private List<string> Shot(CombatCheckpoints.Built built, FormulaVector v)
    {
        var battle = built.Battle;
        var (a, t) = (built[v.Args[0]], built[v.Args[1]]);
        var behind = Battle.IsBehind(a, t);
        var chance = battle.RangedChance(a, t, behind);

        var draws = new FormulaState.Draws(built.Game.Random.Combat, v.Seed, v.Script);
        var weapon = Battle.WeaponOf(a)!;
        var outcome = battle.ResolveRanged(a, t);
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        var actual = new List<(string, object)>
        {
            ("behind", behind ? 1 : 0),
            ("chance", chance),
            ("roll", outcome.Roll),
            ("adjusted", outcome.Adjusted),
            ("damage", outcome.Damage),
            ("draws", draws.Count),
            ("skill", a.Sheet.Skills.RawBase(SkillId.FromRecord(weapon.Skill))),
            ("aiflags", (int)(t.Ai?.Flags ?? 0)),
        };
        return FormulaState.Differences(v.Expect, actual);
    }
}
