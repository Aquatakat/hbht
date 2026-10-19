using Aidyn.Core;
using Aidyn.Core.Combat;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Import;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// A battle's rewards (M3 session 7) against the original's own: <c>Combat.TallyRewards</c> (<c>0x8005E2AC</c>) was run by session 5's
/// MIPS interpreter on checkpoint 11's RAM (the last before the ten rats' battle was won) with its own enemy list or another, the
/// random state and the bonus loot given, and each case's gold pool, experience pool, loot and members' experience are the
/// expected values here (docs/systems/combat.md "Rewards"). Checkpoint 12 is the win itself. The tests skip without the ROM and
/// the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class VictoryCheckpointTests(Na10Import fixture)
{
    private const uint CombatPointer = 0x800E0BF0;

    private static readonly RecordId Rat = RecordId.Hex(RecordTypes.Entity, 0x026E);

    private PluginSet Set => fixture.Data.Set;

    private static AresCheckpoint Combat(int number)
    {
        var tag = $" - Combat {number:00},";
        var name = Directory.Exists(TestRoms.Folder)
            ? Directory.EnumerateFiles(TestRoms.Folder).Select(Path.GetFileName).OfType<string>()
                .FirstOrDefault(n => n.Contains("(V1.0)", StringComparison.Ordinal) && n.Contains(tag, StringComparison.Ordinal) && n.EndsWith(".bs1", StringComparison.Ordinal))
            : null;
        if (name is null)
            Assert.Skip($"No Combat {number:00} checkpoint in {TestRoms.Folder}.");
        return TestRoms.Checkpoint(name);
    }

    private GameState Game(int checkpoint)
    {
        var game = GameState.Load(Set, CheckpointSave.Convert(Combat(checkpoint), Set, $"Combat {checkpoint}"));
        Assert.Empty(game.Problems);
        return game;
    }

    private static RecordId Id(int id) => DbRecordIds.For(new ItemId((ushort)id)) ?? throw new InvalidOperationException($"No record for {id:X4}.");

    /// <summary>A won battle's rewards on checkpoint 11's party, with the loot stream at <paramref name="seed"/>.</summary>
    private (Battle Battle, GameState Game) Tally(int[] enemies, uint? seed, int? bonus = null)
    {
        var game = Game(11);
        if (seed is { } s)
            game.Random.Loot.Seed = s;
        else
            game.Random.Loot.Seed = Combat(11).ReadWord(0x800D8088);
        var list = enemies.Select(Id).ToList();
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [], 3, false, true, null)
        {
            BonusLoot = bonus is { } b ? Id(b) : null,
        };
        var battle = Battle.Ending(game, setup, list);
        battle.TallyRewards();
        return (battle, game);
    }

    private static readonly int[] TenRats = [.. Enumerable.Repeat(0x026E, 10)];

    /// <summary>The original's results, from its <c>TallyRewards</c> on checkpoint 11.</summary>
    public static TheoryData<string, int[], uint?, int?, int, int, (int Id, int Count)[], (int Entity, uint Total, uint Spendable, int Level)[]> Cases => new()
    {
        { "the ten rats, at checkpoint 11's random state", TenRats, null, null, 0, 1000, [(0x0123, 4)],
            [(0x23F, 1603576, 661076, 20), (0x24C, 1342251, 790339, 19), (0x243, 1335751, 451899, 19), (0x24D, 2012732, 577732, 22)] },
        { "the ten rats, at 1", TenRats, 1, null, 0, 1000, [(0x0123, 3)],
            [(0x23F, 1603576, 661076, 20), (0x24C, 1342251, 790339, 19), (0x243, 1335751, 451899, 19), (0x24D, 2012732, 577732, 22)] },
        { "a Bandit Boss", [0x25C], 1, null, 2472, 4055, [(0x755, 1), (0x113, 7), (0x1004, 2), (0x1006, 3)],
            [(0x23F, 1608158, 665658, 20), (0x24C, 1346833, 794921, 19), (0x243, 1340333, 456481, 19), (0x24D, 2017314, 582314, 22)] },
        { "Lugash", [0x262], 1, null, 20000, 5505, [(0x50F, 1), (0x73E, 1), (0x113, 20), (0x1008, 1), (0x10E, 1)],
            [(0x23F, 1610333, 667833, 20), (0x24C, 1349008, 797096, 19), (0x243, 1342508, 458656, 19), (0x24D, 2019489, 584489, 22)] },
        { "Kitarak", [0x21D], 1, null, 2000, 4505, [(0x504, 1), (0x613, 1), (0x738, 1), (0x111, 10), (0x1010, 5), (0x10E, 7)],
            [(0x23F, 1608833, 666333, 20), (0x24C, 1347508, 795596, 19), (0x243, 1341008, 457156, 19), (0x24D, 2017989, 582989, 22)] },
        { "a rat with a bonus chest", [0x26E], 1, 0x080C, 6000, 605, [(0x123, 1), (0x112, 10), (0x1004, 3), (0x1010, 4), (0x10E, 1), (0x1008, 1)],
            [(0x23F, 1602983, 660483, 20), (0x24C, 1341658, 789746, 19), (0x243, 1335158, 451306, 19), (0x24D, 2012139, 577139, 22)] },
        { "a rat with a bonus item", [0x26E], 1, 0x1010, 0, 100, [(0x123, 1), (0x1010, 1)],
            [(0x23F, 1602226, 659726, 20), (0x24C, 1340901, 788989, 19), (0x243, 1334401, 450549, 19), (0x24D, 2011382, 576382, 22)] },
        { "two Bandit Bosses, at 7", [0x2C6, 0x2C7], 7, null, 5382, 6110, [(0x755, 1), (0x112, 13), (0x1004, 3), (0x1006, 4)],
            [(0x23F, 1611241, 668741, 20), (0x24C, 1349916, 798004, 19), (0x243, 1343416, 459564, 19), (0x24D, 2020397, 585397, 22)] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheRewardsAreTheOriginals(string what, int[] enemies, uint? seed, int? bonus, int gold, int experience,
        (int Id, int Count)[] loot, (int Entity, uint Total, uint Spendable, int Level)[] members)
    {
        var (battle, game) = Tally(enemies, seed, bonus);
        Assert.True(gold == battle.GoldPool, $"{what}: gold {battle.GoldPool}");
        Assert.Equal(experience, battle.ExperiencePool);
        Assert.Equal(loot.Select(l => (Id(l.Id), l.Count)), battle.Loot!.Stacks.Select(s => (s.Item.Id, s.Quantity)));
        Assert.Equal(members.Select(m => (Id(m.Entity), m.Total, m.Spendable, m.Level)),
            game.Party.Members.Select(m => (m.Entity, m.TotalExp, m.SpendableExp, m.Stats.Base(Core.Characters.Stat.Level))));
    }

    [Fact]
    public void TheTenRatsGiveCheckpoint12sExperienceToEveryMember()
    {
        var (battle, _) = Tally(TenRats, null);
        var won = Combat(12);
        Assert.Equal((int)won.ReadWord(won.ReadWord(CombatPointer) + 0x24), battle.ExperiencePool);
        var after = Game(12);
        Assert.Equal(after.Party.Members.Select(m => (m.TotalExp, m.SpendableExp)), Tally(TenRats, null).Game.Party.Members.Select(m => (m.TotalExp, m.SpendableExp)));
        Assert.DoesNotContain(battle.Loot!.Stacks, s => s.Item.Id != Id(0x0123));
    }

    [Fact]
    public void ADeadMemberGainsNothing()
    {
        var game = Game(11);
        var rheda = game.Party.Members.Single(m => m.Entity == Id(0x24C));
        var before = rheda.TotalExp;
        rheda.TakeDamage(1000, null);
        var battle = Battle.Ending(game, new BattleSetup(BattleSource.Wanderer, null, null, null, [], 3, false, true, null), [Rat]);
        battle.TallyRewards();
        Assert.Equal(before, rheda.TotalExp);
        Assert.Equal(1602226u, game.Party.Members.First().TotalExp);
    }

    [Fact]
    public void ABossInTheFirstFourSetsItsFlagAndOneAfterThemDoesnt()
    {
        var game = Game(11);
        var kitarak = Id(0x21D);
        var flag = RecordId.Decimal(RecordTypes.Flag, 154);
        game.Flags.TrySet(flag, false);
        var late = Battle.Ending(game, new BattleSetup(BattleSource.Script, null, null, null, [], 3, false, true, null), [Rat, Rat, Rat, Rat, kitarak]);
        late.SetKillFlags();
        Assert.False(game.Flags.IsSet(flag));
        var first = Battle.Ending(game, new BattleSetup(BattleSource.Script, null, null, null, [], 3, false, true, null), [Rat, kitarak]);
        first.SetKillFlags();
        Assert.True(game.Flags.IsSet(flag));
    }
    [Fact]
    public void TheDeadLeaveThePartyWithTheirGearInTheInventoryAndTheirJoinedFlagsSet()
    {
        // As the original's Party.AfterBattle did on checkpoint 12 with Rheda and Sholeh at 0 hit points.
        var game = Game(12);
        var (rheda, sholeh) = (Id(0x24C), Id(0x24D));
        static RecordId Of(int n) => RecordId.Decimal(RecordTypes.Flag, n);
        var worn = new List<RecordId>();
        foreach (var member in game.Party.Members.Where(m => m.Entity == rheda || m.Entity == sholeh))
        {
            worn.AddRange(new[] { member.Equipment.Weapon, member.Equipment.Armor, member.Equipment.Shield }.Concat(member.Equipment.Gear)
                .OfType<Core.Items.ItemInstance>().Select(i => i.Item.Id));
            member.TakeDamage(1000);
        }
        Assert.NotEmpty(worn);
        var before = worn.Distinct().ToDictionary(w => w, w => game.Party.Inventory.Count(w));
        Battle.Ending(game, new BattleSetup(BattleSource.Wanderer, null, null, null, [], 3, false, true, null), [Rat]).Leave();
        Assert.Equal([GameState.Alaron, Id(0x243)], game.Party.Members.Select(m => m.Entity));
        Assert.True(game.Flags.IsSet(Of(617)) && game.Flags.IsSet(Of(622)));
        Assert.True(game.Flags.IsSet(Of(91)) && game.Flags.IsSet(Of(523)));
        Assert.False(game.Flags.IsSet(Of(687)) || game.Flags.IsSet(Of(694)));
        Assert.False(game.Flags.IsSet(Of(613)));
        foreach (var item in worn.Distinct())
            Assert.Equal(before[item] + worn.Count(w => w == item), game.Party.Inventory.Count(item));
    }

    [Fact]
    public void ThePostBattleWindowSaysWhatCheckpoint13Shows()
    {
        // Checkpoint 13's widgets: "Each member earned 1500 XP.", Alaron's stats with Intelligence's price, and what he has to spend.
        var game = Game(13);
        var spoils = new BattleSpoils(0, 1000, new Core.Items.ItemPile(game.Party.Inventory.Catalog), []);
        var window = Core.Ui.PostBattleMenu.For(Set, game.Party, spoils);
        Assert.Equal("Each member earned 1500 XP.", window.Earned);
        Assert.Empty(window.Levelled);
        Assert.Equal(Core.Ui.PartyList.Stats, window.Party.List);
        Assert.Equal(["XP Cost: 5808", "XP Remaining: 661076"], window.CostLines);

        // A asks, and Yes raises Intelligence for its price.
        var alaron = game.Party.Slots[0]!;
        var intelligence = alaron.Stats.Trained(Core.Characters.Stat.Intelligence);
        window.Accept();
        Assert.Equal("Are you sure you wish to train this for 5808 XP?", window.Choice!.Text);
        window.Accept();
        Assert.Equal(intelligence + 1, alaron.Stats.Trained(Core.Characters.Stat.Intelligence));
        Assert.Equal(661076u - 5808, alaron.SpendableExp);
        Assert.False(window.Back());
    }
    [Fact]
    public void AMemberTheAwardTakesPastTheirNextLevelGainsItAndTheWindowSaysSo()
    {
        var game = Game(11);
        var alaron = game.Party.Slots[0]!;
        alaron.TotalExp = alaron.NextLevelExp!.Value - 1;
        var level = alaron.Stats.Base(Core.Characters.Stat.Level);
        var battle = Battle.Ending(game, new BattleSetup(BattleSource.Wanderer, null, null, null, [], 3, false, true, null), [Rat]);
        battle.TallyRewards();
        Assert.Equal(level + 1, alaron.Stats.Base(Core.Characters.Stat.Level));
        Assert.Equal([alaron], battle.Levelled);
        var spoils = new BattleSpoils(battle.GoldPool, battle.ExperiencePool, battle.Loot!, battle.Levelled);
        var window = Core.Ui.PostBattleMenu.For(Set, game.Party, spoils);
        Assert.Equal(["Alaron gained a level!"], window.Levelled);
        Assert.Equal("Each member earned 150 XP.", window.Earned);
    }
}
