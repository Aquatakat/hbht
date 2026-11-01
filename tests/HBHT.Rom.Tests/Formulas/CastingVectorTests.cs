using System.Reflection;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Magic;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// Casting against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>Combatant.CastSpell</c> (<c>0x800648CC</c>) with
/// <c>CheckSpellAvailable</c>, <c>PrepareCast</c>, <c>CastChance</c>, <c>ResistRoll</c>, <c>ApplySpellEffect</c> and <c>SpellDamage</c>,
/// run in the interpreter by <c>tools/aivectors/formulas.py casting</c> (<see cref="CastingVectors"/>), and the port's cast at one
/// target (<c>Battle.CastAt</c>, which <see cref="Battle.Cast"/> and the turn's cast call) on the same state with the same draws. The
/// tests skip without the checkpoints (and the parity logs, for the vectors on their states).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CastingVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
        ["cp05-kind32"] = "1.0's Stamina spell lowers Stamina by 2 a level (Entity.AddEffect's case 0x20 jumps to the negating path at 0x80070560 that Exhaustion's takes); the port's raises it, as its description says",
    };

    public static TheoryData<string> Ids => [.. CastingVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void CastingIsTheOriginals(string id)
    {
        var v = CastingVectors.Vectors.Single(v => v.Id == id);
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

    /// <summary>The family's own pokes, as <c>casting.py</c>'s.</summary>
    private static readonly Dictionary<string, FormulaState.PokeHandler> Pokes = new()
    {
        // learn:slot,kind,level: the spell of that kind known at that level, and selected.
        ["learn"] = (b, a) =>
        {
            var c = FormulaState.Slot(b, a[0]);
            var spell = SpellOfKind(b, FormulaState.Int(a[1]));
            if (c.Sheet.FindSpell(spell.Id) is null)
                c.Sheet.LearnSpell(spell);
            var known = c.Sheet.FindSpell(spell.Id)!;
            known.Level = FormulaState.Int(a[2]);
            c.Spell = new SpellChoice(SpellSourceKind.Known, c.Sheet.Spells.ToList().IndexOf(known));
        },
        ["mirror"] = (b, a) => FormulaState.Slot(b, a[0]).Mirror = FormulaState.Int(a[1]),
    };

    private static SpellRecord SpellOfKind(CombatCheckpoints.Built b, int kind) =>
        b.Game.Plugins.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Position == kind);

    private static readonly MethodInfo CastAt = typeof(Battle).GetMethod("CastAt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>The vector's state and pokes laid over the port's battle, the port's cast at one target, and what differs.</summary>
    private List<string> Run(FormulaVector v)
    {
        var built = FormulaState.Build(Set, v.Source);
        FormulaState.Poke(built, v.Pokes, Pokes);
        var battle = built.Battle;
        var (a, t) = (built[v.Args[0]], built[v.Args[1]]);
        var source = a.CurrentSpell(built.Game.Rules.Magic)!;

        var draws = new FormulaState.Draws(built.Game.Random.Combat, v.Seed, v.Script);
        // As CastSpell's PrepareCast: a caster that has acted this turn casts at the margin it kept.
        var prepared = a.Flags.HasFlag(CombatantFlags.Acted);
        object[] args = [a, source, t, prepared, prepared ? a.CastMargin : -1];
        var outcome = (CastOutcome)CastAt.Invoke(battle, args)!;
        var margin = Battle.MarginAfter(outcome, (int)args[4]);
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        var spells = built.Game.Plugins.All<SpellRecord>(RecordTypes.Spell).ToDictionary(s => s.Id, s => s.Position);
        var sheet = t.Sheet;
        var actual = new List<(string, object)>
        {
            ("result", outcome.Result switch
            {
                CastResult.NotCast => -2,
                CastResult.Resisted => -1,
                CastResult.Effect => -3,
                _ => outcome.Damage,
            }),
            ("margin", (sbyte)margin),
            ("acted", a.Flags.HasFlag(CombatantFlags.Acted) ? 1 : 0),
            ("draws", draws.Count),
            ("stamina", a.Sheet.Stats.RawModified(Stat.Stamina)),
            ("level", source.Known?.Level ?? -1),
            ("effects", sheet.Effects.Count == 0 ? "-"
                : string.Join('/', sheet.Effects.Select(e => $"{spells.GetValueOrDefault(e.Source)}:{e.Power}:{e.Margin}:{e.Timer}"))),
            ("tmod", string.Join('.', Enum.GetValues<Stat>().Select(s => (byte)sheet.Stats.RawModified(s)))),
            ("tbase", string.Join('.', Enum.GetValues<Stat>().Select(s => sheet.Stats.RawBase(s)))),
            ("prot", sheet.Protection),
            ("mirror", t.Mirror),
            ("tflags", (int)(t.Flags & (CombatantFlags.Active | CombatantFlags.Hostile))),
            ("morale", t.Ai?.Morale ?? -1),
            ("aiflags", t.Ai is { } ai ? (int)ai.Flags : -1),
        };
        return FormulaState.Differences(v.Expect, actual);
    }
}
