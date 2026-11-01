using System.Reflection;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Items;
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
        ["reagent"] = (b, a) =>
        {
            var inventory = b.Game.Party.Inventory;
            var item = TestRoms.ItemId((ushort)FormulaState.Int(a[0]));
            while (inventory.Take(item))
            {
            }
            if (FormulaState.Int(a[1]) is > 0 and var n)
                inventory.Add(item, n);
        },
        ["select"] = (b, a) => FormulaState.Slot(b, a[0]).Spell = new SpellChoice((SpellSourceKind)FormulaState.Int(a[1]), FormulaState.Int(a[2])),
        ["charges"] = (b, a) =>
        {
            var c = FormulaState.Slot(b, a[0]);
            c.CurrentSpell(b.Game.Rules.Magic)!.Item!.Charges = FormulaState.Int(a[1]);
        },
        // gear:slot,id[,battery]: as Entity.EquipGear puts it on, a battery's charge given (1.0's stat bonus copied from the stack).
        ["gear"] = (b, a) =>
        {
            var gear = new ItemInstance(b.Game.Items.TryGet(TestRoms.ItemId((ushort)FormulaState.Int(a[1])))!);
            gear.Battery = a.Length > 2 ? FormulaState.Int(a[2]) : null;
            Assert.Equal(EquipRefusal.None, FormulaState.Slot(b, a[0]).Sheet.Equipment.EquipGear(gear));
        },
        // moon:n: the clock moved to a day of that phase (GameClock.MoonPhase), the season and the time of day kept.
        ["moon"] = (b, a) =>
        {
            var clock = b.Game.Clock;
            var day = FormulaState.Int(a[0]) switch { 3 => 0, 0 => 14, 2 => 3, _ => 10 };
            clock.Time = clock.Time / GameClock.Season * GameClock.Season + (uint)day * GameClock.Day + clock.Time % GameClock.Day;
            Assert.Equal(FormulaState.Int(a[0]), GameClock.MoonPhase(clock.Time).Phase);
        },
    };

    /// <summary>
    /// A record's power byte (<c>+1</c>) as 1.0 keeps it: the level, but Wraith Touch's the amount it drained, which the port keeps in
    /// <see cref="ActiveEffect.Amounts"/>. Its margin byte (<c>+3</c>) is read as a byte, as the port's signed one holds the same bits.
    /// </summary>
    private static int RecordPower(ActiveEffect e)
    {
        var drain = e.Effects.ToList().FindIndex(x => x is DrainStatEffect);
        return drain >= 0 ? e.Amounts[drain] : e.Power;
    }

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
            ("charges", source.Item?.Charges ?? -1),
            ("batteries", string.Join('/', a.Sheet.Equipment.Gear.Take(a.Sheet.Equipment.GearCount).Select((g, i) => (g, i))
                .Where(p => p.g?.Battery is not null).Select(p => $"{p.i}:{p.g!.Battery}")) is { Length: > 0 } b ? b : "-"),
            ("effects", sheet.Effects.Count == 0 ? "-"
                : string.Join('/', sheet.Effects.Select(e => $"{spells.GetValueOrDefault(e.Source)}:{RecordPower(e)}:{(byte)e.Margin}:{e.Timer}"))),
            ("tmod", string.Join('.', Enum.GetValues<Stat>().Select(s => (byte)sheet.Stats.RawModified(s)))),
            ("tbase", string.Join('.', Enum.GetValues<Stat>().Select(s => sheet.Stats.RawBase(s)))),
            ("prot", sheet.Protection),
            ("mirror", t.Mirror),
            ("tflags", (int)(t.Flags & (CombatantFlags.Active | CombatantFlags.Hostile))),
            ("morale", t.Ai?.Morale ?? -1),
            ("aiflags", t.Ai is { } ai ? (int)ai.Flags : -1),
            ("reagents", built.Game.Rules.Magic.ReagentItem(source.Spell.Reagent) is { } reagent ? built.Game.Party.Inventory.Count(reagent) : -1),
        };
        return FormulaState.Differences(v.Expect, actual);
    }
}
