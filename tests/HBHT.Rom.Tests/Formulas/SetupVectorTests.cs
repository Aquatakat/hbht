using System.Reflection;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// A battle's setup against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>BattleSetup.FillEnemies</c>
/// (<c>0x80009374</c>), <c>BattleSetup.ChooseArena</c> (<c>0x80008698</c>), <c>Party.BattleAmbushRoll</c> (<c>0x80076C60</c>) and
/// <c>Combat.SpawnPosition</c> (<c>0x8005C294</c>), run in the interpreter by <c>tools/aivectors/formulas.py setup</c>
/// (<see cref="SetupVectors"/>), and the port's <see cref="BattleSetup.FillEnemies"/>, <see cref="BattleSetup.ChooseArena"/>,
/// <see cref="Battle.AmbushRoll"/> and <see cref="Battle.Spawn"/> on the same state with the same draws. The tests skip without the
/// checkpoints (and the parity logs, for the vectors on their states).
/// </summary>
public sealed class SetupVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
        // Twelve sure ones under a most past 12: 1.0's ChooseKinds still rolls each kind's extras, and an extra writes a thirteenth
        // entry past the list, over the setup block's bonus loot (0x800D9470); the port stops at 12 and draws nothing. No party in
        // the game has so many, so only a mod reaches it.
        ["cp05-fill-sure12-most16-seed1234"] = "1.0 writes a thirteenth enemy over the bonus loot",
        ["cp05-fill-sure12-most16-seedDEADBEEF"] = "1.0 writes a thirteenth enemy over the bonus loot",
        ["cp05-fill-sure12-most16-seed80000001"] = "1.0 writes a thirteenth enemy over the bonus loot",
        ["cp05-fill-sure12-most16-least"] = "1.0 writes a thirteenth enemy over the bonus loot",
        ["cp05-fill-sure12-most16-most"] = "1.0 writes a thirteenth enemy over the bonus loot",
    };

    private const int Fill = 0, ChooseArena = 1, Ambush = 2;

    /// <summary>The family's own pokes: the setup block's collision and animation bytes.</summary>
    private static readonly Dictionary<string, FormulaState.PokeHandler> Pokes = new()
    {
        ["block"] = (b, a) =>
        {
            var (collision, animation) = (FormulaState.Int(a[0]), FormulaState.Int(a[1]));
            typeof(Battle).GetField("<Setup>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(b.Battle, b.Battle.Setup with { CollisionByte = collision, Ambush = collision == Battle.AmbushByte, AniByte = animation });
        },
    };

    public static TheoryData<string> Ids => [.. SetupVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheSetupIsTheOriginals(string id)
    {
        var v = SetupVectors.Vectors.Single(v => v.Id == id);
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

    /// <summary>The vector's state and pokes laid over the port's battle, the port's function run, and what differs.</summary>
    private List<string> Run(FormulaVector v)
    {
        var built = FormulaState.Build(Set, v.Source);
        FormulaState.Poke(built, v.Pokes, Pokes);
        var game = built.Game;
        var random = game.Random.Combat;
        var args = v.Args[1..];

        var draws = new FormulaState.Draws(random, v.Seed, v.Script);
        var actual = new List<(string, object)>();
        switch (v.Args[0])
        {
            case Fill:
            {
                var kinds = args[2..].Chunk(3).Select(k => new PartyEnemy(RecordId.Hex(RecordTypes.Entity, k[0]), k[1], k[2])).ToList();
                var setup = new BattleSetup(BattleSource.Encounter, null, null, null, kinds, null, false, true, null) { Size = (args[0], args[1]) };
                var list = setup.FillEnemies(game.Party, e => game.Plugins.TryGet<EntityRecord>(e)?.Sheet?.Level ?? 0, random,
                    game.Plugins.Singleton<CombatRecord>().Fallback);
                actual.Add(("enemies", list.Count > 0 ? string.Join(',', list.Select(e => e.Key)) : "-"));
                break;
            }
            case ChooseArena:
            {
                var setup = new BattleSetup(BattleSource.Encounter, null, RecordId.Decimal(RecordTypes.Terrain, args[0]), null, [], null, false, true, null);
                actual.Add(("arena", setup.ChooseArena(game.Plugins, random) is { } arena ? Battlefield.Number(arena) : "none"));
                break;
            }
            case Ambush:
            {
                actual.Add(("roll", Battle.AmbushRoll(game.Party, random)));
                actual.Add(("stamina", string.Join(',', game.Party.Slots.OfType<Character>()
                    .Select(m => $"{m.Stats.Current(Stat.Stamina)}/{m.Stats.RawModified(Stat.Stamina)}"))));
                break;
            }
            default:
            {
                var (_, point, direction) = built.Battle.Spawn(args[0] != 0, args[1] != 0, args[2], args[3], random);
                var cell = Battle.CellOf(point);
                actual.Add(("x", (int)cell.X));
                actual.Add(("z", (int)cell.Y));
                actual.Add(("direction", direction));
                break;
            }
        }
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        actual.Add(("draws", draws.Count));
        return FormulaState.Differences(v.Expect, actual);
    }
}
