using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// Melee's formulas against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>Combatant.MeleeChance</c> (<c>0x800622C4</c>),
/// <c>ResolveMelee</c> (<c>0x800628A4</c>) with <c>MeleeDamage</c>, <c>ApplyResistance</c> and <c>WeaponSpellProc</c>, run in the
/// interpreter by <c>tools/aivectors/formulas.py melee</c> (<see cref="MeleeVectors"/>), and the port's <see cref="Battle.MeleeChance"/>
/// and <see cref="Battle.ResolveMelee"/> on the same state with the same draws. The tests skip without the checkpoints (and the parity
/// logs, for the vectors on their states).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MeleeVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
    };

    public static TheoryData<string> Ids => [.. MeleeVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheMeleeFormulasAreTheOriginals(string id)
    {
        var v = MeleeVectors.Vectors.Single(v => v.Id == id);
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
        FormulaState.Poke(built, v.Pokes);
        var battle = built.Battle;
        var (a, t) = (built[v.Args[0]], built[v.Args[1]]);
        var behind = Battle.IsBehind(a, t);
        var chance = battle.MeleeChance(a, t, behind);

        var draws = new FormulaState.Draws(built.Game.Random.Combat, v.Seed, v.Script);
        var weapon = Battle.WeaponOf(a)!;
        var outcome = battle.ResolveMelee(a, t);
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
            ("margin", a.CastMargin),
        };
        return FormulaState.Differences(v.Expect, actual);
    }
}
