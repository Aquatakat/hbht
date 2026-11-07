using System.Reflection;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// The rewards against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>Combat.TallyRewards</c> (<c>0x8005E2AC</c>) with
/// <c>AddChestLoot</c>, <c>RollEquipmentDrops</c>, <c>RollChestItem</c> and <c>AddBonusLoot</c>, run in the interpreter by
/// <c>tools/aivectors/formulas.py rewards</c> (<see cref="RewardsVectors"/>), and the port's <see cref="Battle.TallyRewards"/> on the same
/// state with the same draws, on its loot stream. The tests skip without the checkpoints (and the parity logs, for the vectors on their
/// states).
/// </summary>
public sealed class RewardsVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new()
    {
    };

    /// <summary>The party (<c>GameState.Party</c>), its four members' sheets first.</summary>
    private const uint PartyPointer = 0x800D9684;

    /// <summary>The setup block's bonus loot (<c>BattleSetup.BonusLoot</c>), which a log's battle is built without.</summary>
    private const uint BonusLoot = 0x800D9470;

    /// <summary>The family's own pokes: the enemy list, the bonus loot, and a member's experience.</summary>
    private static readonly Dictionary<string, FormulaState.PokeHandler> Pokes = new()
    {
        ["enemies"] = (b, a) => SetField(b.Battle, "<Enemies>k__BackingField",
            (IReadOnlyList<RecordId>)[.. a.Select(x => RecordId.Hex(RecordTypes.Entity, FormulaState.Int(x)))]),
        ["bonus"] = (b, a) =>
        {
            var id = FormulaState.Int(a[0]);
            var bonus = id >> 8 == 8 ? RecordId.Hex(RecordTypes.Loot, id) : TestRoms.ItemId((ushort)id);
            SetField(b.Battle, "<Setup>k__BackingField", b.Battle.Setup with { BonusLoot = bonus });
        },
        ["exp"] = (b, a) => SetExp(b.Game.Party.Slots[FormulaState.Int(a[0])]!, (uint)FormulaState.Int(a[1]), (uint)FormulaState.Int(a[2])),
    };

    public static TheoryData<string> Ids => [.. RewardsVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheRewardsAreTheOriginals(string id)
    {
        var v = RewardsVectors.Vectors.Single(v => v.Id == id);
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

    /// <summary>The vector's state and pokes laid over the port's battle, the port's rewards tallied, and what differs.</summary>
    private List<string> Run(FormulaVector v)
    {
        var built = FormulaState.Build(Set, v.Source);
        var party = built.Game.Party;
        // The members' experience and the bonus loot as the RAM holds them, which the overlay doesn't lay (CharExp, the sheet's +0x14: total +4, spendable +8).
        var members = built.Cp.ReadWord(PartyPointer);
        for (var i = 0; i < party.Slots.Count; i++)
        {
            var sheet = built.Cp.ReadWord(members + (uint)i * 4);
            if (sheet != 0 && party.Slots[i] is { } member)
            {
                var exp = built.Cp.ReadWord(sheet + 0x14);
                SetExp(member, built.Cp.ReadWord(exp + 4), built.Cp.ReadWord(exp + 8));
            }
        }
        if (CombatCheckpoints.Half(built.Cp, BonusLoot) is var bonus and not 0)
            Pokes["bonus"](built, [$"0x{bonus:X}"]);
        FormulaState.Poke(built, v.Pokes, Pokes);
        var battle = built.Battle;

        var draws = new FormulaState.Draws(built.Game.Random.Loot, v.Seed, v.Script);
        battle.TallyRewards();
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        var loot = battle.Loot!.Stacks.Select(s => $"{s.Item.Id.Key}*{s.Quantity}").ToList();
        var actual = new List<(string, object)>
        {
            ("gold", battle.GoldPool),
            ("experience", battle.ExperiencePool),
            ("loot", loot.Count > 0 ? string.Join(',', loot) : "-"),
            ("members", string.Join(',', party.Slots.OfType<Character>().Select(m => $"{m.TotalExp}/{m.SpendableExp}/{m.Stats.RawBase(Stat.Level)}"))),
            ("draws", draws.Count),
        };
        return FormulaState.Differences(v.Expect, actual);
    }

    private static void SetExp(Character member, uint total, uint spendable)
    {
        member.TotalExp = total;
        member.SpendableExp = spendable;
    }

    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
