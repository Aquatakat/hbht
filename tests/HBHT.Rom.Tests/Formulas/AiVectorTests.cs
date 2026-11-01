using System.Globalization;
using System.Numerics;
using System.Reflection;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// Morale and the AI's turn against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>EnemyAI.Morale</c>
/// (<c>0x80056FA8</c>), <c>MayCheckMorale</c> (<c>0x8005722C</c>), <c>CheckFlee</c> (<c>0x800572AC</c>),
/// <c>ReserveFleesOnArrival</c> (<c>0x80056E18</c>), a turn as <c>TurnOrder.BeginTurn</c> begins it with <c>EnemyAI.Step</c>
/// (<c>0x8005B314</c>) for styles 0–4 and a flight, and <c>EnemyAI.FleeAdvance</c> (<c>0x8005B434</c>), run in the interpreter by
/// <c>tools/aivectors/formulas.py ai</c> (<see cref="AiVectors"/>), and the port's same functions on the same state with the same
/// draws. The tests skip without the checkpoints (and the parity logs, for the vectors on their states).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class AiVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
    };

    /// <summary>The operations, the vector's first argument (<c>ai.py</c>'s).</summary>
    private enum Op { Morale, MayCheckMorale, CheckFlee, ReserveFleesOnArrival, Turn, FleeAdvance }

    public static TheoryData<string> Ids => [.. AiVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void MoraleAndTheTurnAreTheOriginals(string id)
    {
        var v = AiVectors.Vectors.Single(v => v.Id == id);
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

    /// <summary>The family's own pokes, as <c>ai.py</c>'s <c>POKES</c> make them to the RAM.</summary>
    private static readonly Dictionary<string, FormulaState.PokeHandler> Pokes = new()
    {
        ["sortseed"] = (b, a) => b.Game.Random.Sort.Seed = (uint)long.Parse(a[0], CultureInfo.InvariantCulture),
        ["morale"] = (b, a) => Ai(b, a[0]).Morale = (sbyte)(byte)(FormulaState.Int(a[1]) & 0xFF),
        ["aiflags"] = (b, a) => Ai(b, a[0]).Flags = (AiFlags)FormulaState.Int(a[1]),
        ["target"] = (b, a) => Ai(b, a[0]).Target = (sbyte)(byte)(FormulaState.Int(a[1]) & 0xFF),
        ["style"] = (b, a) => Ai(b, a[0]).Style = FormulaState.Int(a[1]),
        ["kind"] = (b, a) => (Ai(b, a[0]).Kind, Ai(b, a[0]).Wanted) = ((AiKind)FormulaState.Int(a[1]), (AiKind)FormulaState.Int(a[2])),
        ["flight"] = (b, a) => Ai(b, a[0]).FlightLeft = FormulaState.Int(a[1]),
        ["cflags"] = (b, a) => FormulaState.Slot(b, a[0]).Flags = (CombatantFlags)FormulaState.Int(a[1]),
        ["action"] = (b, a) => FormulaState.Slot(b, a[0]).Action = (CombatAction)FormulaState.Int(a[1]),
        ["radius"] = (b, a) => FormulaState.Slot(b, a[0]).Radius = FormulaState.Int(a[1]),
        ["spell"] = (b, a) => FormulaState.Slot(b, a[0]).Spell = FormulaState.Int(a[1]) == 0 ? SpellChoice.None
            : new SpellChoice((SpellSourceKind)FormulaState.Int(a[1]), FormulaState.Int(a[2])),
        ["counts"] = (b, a) => b.Battle.ActiveCounts = (FormulaState.Int(a[0]), FormulaState.Int(a[1]), FormulaState.Int(a[2])),
        ["leader"] = (b, a) => b.Battle.LeaderMorale = FormulaState.Int(a[0]),
        ["leadergone"] = (b, a) => Poke(b.Battle, nameof(Battle.LeaderGone), FormulaState.Int(a[0]) != 0),
        ["nofleepoints"] = (b, a) => Poke(b.Battle, nameof(Battle.HasFleePoints), false),
        ["origin"] = (b, a) => Poke(b.Battle, nameof(Battle.FleeOrigin), new Vector2(FormulaState.Float(a[0]), FormulaState.Float(a[1]))),
        ["body"] = (b, a) =>
        {
            var body = FormulaState.Slot(b, a[0]).Body;
            body.Sphere.Position = body.Sphere.Position with { X = b.Battle.Origin.X + FormulaState.Float(a[1]), Z = b.Battle.Origin.Z + FormulaState.Float(a[2]) };
        },
    };

    private static EnemyAI Ai(Built built, string slot) => FormulaState.Slot(built, slot).Ai!;

    private static readonly MethodInfo FleeAdvance = typeof(Battle).GetMethod("FleeAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>The vector's state and pokes laid over the port's battle, the port's function run, and what differs.</summary>
    private List<string> Run(FormulaVector v)
    {
        var built = FormulaState.Build(Set, v.Source);
        // The target sets' cursors and the action's target as the RAM has them, which a turn's rebuilding keeps where it can.
        var battle = built.Battle;
        Poke(battle.TargetsA, nameof(TargetSet.Cursor), (int)built.Cp.ReadByte(built.Struct + 0x4E64 + 0xA6));
        Poke(battle.TargetsB, nameof(TargetSet.Cursor), (int)built.Cp.ReadByte(built.Struct + 0x4F0C + 0xA6));
        Poke(battle, nameof(Battle.Target), (int)(sbyte)built.Cp.ReadByte(built.Struct + 0x14));
        FormulaState.Poke(built, v.Pokes, Pokes);
        var (op, who, arg) = ((Op)v.Args[0], v.Args[1], v.Args[2]);
        var draws = new FormulaState.Draws(built.Game.Random.Combat, v.Seed, v.Script);
        var actual = new List<(string, object)>();
        if (op == Op.ReserveFleesOnArrival)
        {
            var flees = battle.ReserveFleesOnArrival(Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, who))!);
            actual.Add(("flees", flees ? 1 : 0));
            actual.Add(("draws", draws.Count));
            output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
            return FormulaState.Differences(v.Expect, actual);
        }

        var c = battle.Slots[who]!;
        var ai = c.Ai!;
        switch (op)
        {
            case Op.Morale:
                actual.Add(("morale", battle.Morale(c, arg != 0)));
                break;
            case Op.MayCheckMorale:
                actual.Add(("may", battle.MayCheckMorale(c) ? 1 : 0));
                break;
            case Op.CheckFlee:
                battle.CheckFlee(c, arg != 0);
                break;
            case Op.Turn:
                // The target sets as the combatant's own to start: a fleeing one's turn keeps those it has.
                battle.RebuildTargets(c);
                BeginTurn(battle, c, arg);
                battle.StepAI(c);
                break;
            case Op.FleeAdvance:
                // As state 6 calls it, with the AI's state to go on to.
                battle.Current = c;
                Poke(battle, nameof(Battle.State), CombatState.Facing);
                Poke(battle, nameof(Battle.NextState), CombatState.AI);
                FleeAdvance.Invoke(battle, [ai]);
                break;
        }
        if (op is Op.Morale or Op.CheckFlee or Op.Turn)
            actual.AddRange([("aiflags", (int)ai.Flags), ("leader", battle.LeaderMorale)]);
        if (op is Op.CheckFlee or Op.Turn or Op.FleeAdvance)
            actual.Add(("flight", ai.Flags.HasFlag(AiFlags.Fleeing) || op == Op.FleeAdvance ? ai.FlightLeft : 0));
        if (op is Op.Turn or Op.FleeAdvance)
        {
            actual.AddRange([("state", (int)battle.State), ("next", (int)battle.NextState), ("gone", c.Gone ? 1 : 0),
                ("x", ((double)c.Cell.X).ToString("F2", CultureInfo.InvariantCulture)),
                ("z", ((double)c.Cell.Y).ToString("F2", CultureInfo.InvariantCulture))]);
        }
        if (op == Op.Turn)
        {
            var path = ai.PathLength < battle.Grid.Diagonal ? string.Join('/', battle.PathOf(ai).Select(p => $"{p.X},{p.Z}")) : "";
            actual.AddRange([("target", ai.Target), ("kind", (int)ai.Kind), ("wanted", (int)ai.Wanted), ("path", path.Length > 0 ? path : "-"),
                ("acttarget", battle.Target), ("selA", Mask(battle.TargetsA)), ("selB", Mask(battle.TargetsB)),
                ("sort", built.Game.Random.Sort.Seed)]);
        }
        actual.Add(("draws", draws.Count));
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        return FormulaState.Differences(v.Expect, actual);

        static int Mask(TargetSet set) => set.SelectedSlots.Sum(s => 1 << s);
    }

    /// <summary>
    /// <c>TurnOrder.BeginTurn</c>'s part the AI reads, as <c>ai.py</c>'s <c>begin_turn</c>: the combatant current, its check to flee
    /// (<paramref name="asked"/>, −1 none, 2 as the battle asks: any but the leader while it lives), its body read and its start kept,
    /// the focus on it, the AI's state as the state and the next, and its target sets unless it flees.
    /// </summary>
    private static void BeginTurn(Battle battle, Combatant c, int asked)
    {
        battle.Current = c;
        if (asked == 2)
            asked = battle.LeaderGone || c.Slot != battle.LeaderSlot ? 1 : 0;
        if (asked >= 0 && c.Ai!.Flags.HasFlag(AiFlags.Plays))
            battle.CheckFlee(c, asked != 0);
        c.SyncFromBody(battle);
        battle.FocusAt(c.Cell);
        c.SaveStartPosition();
        Poke(battle, nameof(Battle.State), CombatState.AI);
        Poke(battle, nameof(Battle.NextState), CombatState.AI);
        Poke(battle, nameof(Battle.Stuck), 0);
        if (!c.Fleeing)
            battle.RebuildTargets(c);
    }
}
