using System.Text.Json;
using HBHT.Core.Combat;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using CharacterParty = HBHT.Core.Characters.Party;

namespace HBHT.Core.Tests;

/// <summary>
/// Who fights (<c>BattleSetup.FillEnemies</c>, <c>0x80009374</c>) and where (<c>BattleSetup.ChooseArena</c>, <c>0x80008698</c>),
/// with the original's draws scripted so that the lists and the number of draws can both be checked.
/// </summary>
public sealed class BattleSetupTests
{
    private static readonly RecordId A = Entity(0x0300);
    private static readonly RecordId B = Entity(0x0301);
    private static readonly RecordId C = Entity(0x0302);

    private static PartyEnemy Kind(RecordId entity, int least, int most) => new(entity, least, most);

    private static BattleSetup Fight(int least, int most, params PartyEnemy[] kinds) =>
        new(BattleSource.MonsterParty, null, null, MapRecord.VoxelId(RecordId.Hex(RecordTypes.Map, 1), 0), kinds, null, false, true, null)
        {
            Size = (least, most),
        };

    /// <summary>A party of Alaron alone at <paramref name="level"/>.</summary>
    private static CharacterParty PartyOfLevel(int level = 1)
    {
        var party = ItemData.NewParty();
        party.Add(Characters.Create(Characters.Alaron(level)));
        Assert.Equal(level, party.AverageLevel);
        return party;
    }

    private static List<RecordId> Repeated(params (RecordId Entity, int Count)[] runs) =>
        [.. runs.SelectMany(r => Enumerable.Repeat(r.Entity, r.Count))];

    /// <summary>The combat record's fallback, a Skeleton.</summary>
    private static readonly RecordId Fallback = Entity(0x0263);

    private static IReadOnlyList<RecordId> Fill(BattleSetup setup, IRandom random, int partyLevel = 1, Func<RecordId, int>? level = null) =>
        setup.FillEnemies(PartyOfLevel(partyLevel), level ?? (_ => 1), random, Fallback);

    // Pass 1 (0x80008EC8): the sure ones, each kind's least, in the kinds' order. Nothing is drawn when no kind has a range.
    [Fact]
    public void TheSureOnesComeFirstInKindOrderWithNoDraws()
    {
        var setup = Fight(0, 12, Kind(A, 2, 2), Kind(B, 1, 1), Kind(C, 3, 3));

        var list = Fill(setup, new ScriptedRandom());

        Assert.Equal(Repeated((A, 2), (B, 1), (C, 3)), list);
    }

    [Fact]
    public void AKindWithNoLeastAddsNoSureOnes()
    {
        var list = Fill(Fight(0, 12, Kind(A, 0, 0), Kind(B, 2, 2)), new ScriptedRandom());

        Assert.Equal(Repeated((B, 2)), list);
    }

    // Pass 1 stops as the list reaches the party's most (n >= MaxSize), and pass 2 doesn't run: no draws.
    [Fact]
    public void TheSureOnesStopAtThePartysMostAndNothingIsDrawn()
    {
        var setup = Fight(0, 3, Kind(A, 5, 5), Kind(B, 0, 4));

        var list = Fill(setup, new ScriptedRandom());

        Assert.Equal(Repeated((A, 3)), list);
    }

    [Fact]
    public void TheSureOnesStopAtTwelve()
    {
        var setup = Fight(0, 20, Kind(A, 8, 8), Kind(B, 8, 8));

        var list = Fill(setup, new ScriptedRandom());

        Assert.Equal(Repeated((A, 8), (B, 4)), list);
    }

    // Pass 2 (0x800090A4): one Range(0, most - least) draw per kind with least < most, in kind order; extras are appended.
    [Fact]
    public void EachKindWithARangeDrawsOnceInKindOrderAndItsExtrasAreAppended()
    {
        var setup = Fight(0, 12, Kind(A, 1, 3), Kind(B, 0, 4), Kind(C, 2, 2));
        // A: Range(0, 2) = 2; B: Range(0, 4) = 3.
        var random = new ScriptedRandom(BattleDice.Range(0, 2, 2), BattleDice.Range(0, 4, 3));

        var list = Fill(setup, random);

        // The sure ones A, C, C, then A's two extras, then B's three.
        Assert.Equal(Repeated((A, 1), (C, 2), (A, 2), (B, 3)), list);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void AKindWhoseLeastEqualsItsMostDrawsNothing()
    {
        var setup = Fight(0, 12, Kind(A, 2, 2), Kind(B, 0, 1));
        var random = new ScriptedRandom(BattleDice.Range(0, 1, 1));

        var list = Fill(setup, random);

        Assert.Equal(Repeated((A, 2), (B, 1)), list);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void ARollOfZeroAddsNothingButStillCountsAsADraw()
    {
        var setup = Fight(0, 12, Kind(A, 1, 1), Kind(B, 0, 3));
        var random = new ScriptedRandom(BattleDice.Range(0, 3, 0));

        var list = Fill(setup, random);

        Assert.Equal(Repeated((A, 1)), list);
        Assert.Equal(0, random.Remaining);
    }

    // The extras are written before the checks (n >= MaxSize), so the party's most is reached and the later kinds draw nothing.
    [Fact]
    public void TheExtrasStopAtThePartysMostAndLaterKindsDrawNothing()
    {
        var setup = Fight(0, 3, Kind(A, 0, 5), Kind(B, 0, 5));
        var random = new ScriptedRandom(BattleDice.Range(0, 5, 5));

        var list = Fill(setup, random);

        Assert.Equal(Repeated((A, 3)), list);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void TheExtrasStopAtTwelve()
    {
        var setup = Fight(0, 20, Kind(A, 10, 10), Kind(B, 0, 6));
        var random = new ScriptedRandom(BattleDice.Range(0, 6, 6));

        var list = Fill(setup, random);

        Assert.Equal(Repeated((A, 10), (B, 2)), list);
    }

    // `if n < MinSize: return 0`, then the count of 0 becomes the fallback Skeleton.
    [Fact]
    public void ARollUnderThePartysLeastGivesTheFallbackSkeleton()
    {
        var setup = Fight(3, 5, Kind(A, 0, 1));
        var random = new ScriptedRandom(BattleDice.Range(0, 1, 1));

        var list = Fill(setup, random);

        Assert.Equal([Fallback], list);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void SureOnesUnderThePartysLeastAlsoGiveTheFallback()
    {
        var setup = Fight(3, 5, Kind(A, 1, 1));

        Assert.Equal([Fallback], Fill(setup, new ScriptedRandom()));
    }

    [Fact]
    public void ReachingTheLeastKeepsTheList()
    {
        var setup = Fight(2, 5, Kind(A, 1, 3));
        var random = new ScriptedRandom(BattleDice.Range(0, 2, 1));

        Assert.Equal(Repeated((A, 2)), Fill(setup, random));
    }

    [Fact]
    public void NoKindsAtAllGiveTheFallbackSkeleton()
    {
        Assert.Equal([Fallback], Fill(Fight(0, 12), new ScriptedRandom()));
    }

    [Fact]
    public void TheFallbackIsTheSkeletonEntity0x0263()
    {
        Assert.Equal(Entity(0x0263), Fallback);
    }

    // Pass 3 (0x800088E4): the party's average level under the enemies' trims each kind by 0.3, 0.2 or 0.1 of its count.
    [Theory]
    [InlineData(1, 4, 7)] // trunc(3 * 1 / 4) = 0: 0.3 of 10 is 3
    [InlineData(1, 3, 8)] // trunc(3 * 1 / 3) = 1: 0.2 of 10 is 2
    [InlineData(2, 3, 9)] // trunc(3 * 2 / 3) = 2: 0.1 of 10 is 1
    public void ThePartysLevelBelowTheEnemiesTrimsEachKindByAShare(int partyLevel, int enemyLevel, int kept)
    {
        var setup = Fight(0, 12, Kind(A, 10, 10));

        var list = Fill(setup, new ScriptedRandom(), partyLevel, _ => enemyLevel);

        Assert.Equal(Repeated((A, kept)), list);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public void ThePartysLevelAtOrAboveTheEnemiesTrimsNothing(int partyLevel, int enemyLevel)
    {
        var setup = Fight(0, 12, Kind(A, 10, 10));

        Assert.Equal(Repeated((A, 10)), Fill(setup, new ScriptedRandom(), partyLevel, _ => enemyLevel));
    }

    [Fact]
    public void EnemiesOfLevelZeroTrimNothing()
    {
        var setup = Fight(0, 12, Kind(A, 10, 10));

        Assert.Equal(Repeated((A, 10)), Fill(setup, new ScriptedRandom(), 1, _ => 0));
    }

    [Fact]
    public void TheTrimTakesAShareOfEachKindAndTruncatesIt()
    {
        // The sure ones stop at 12: 10 A and 2 B. Levels 4 against the party's 1 take 0.3: 10 A lose 3, 2 B lose trunc(0.6) = 0.
        var setup = Fight(0, 12, Kind(A, 10, 10), Kind(B, 5, 5), Kind(C, 3, 3));

        var list = Fill(setup, new ScriptedRandom(), 1, _ => 4);

        Assert.Equal(Repeated((A, 7), (B, 2)), list);
    }

    [Fact]
    public void TheTrimTruncatesEachKindsShare()
    {
        // 6 A lose trunc(1.8) = 1, 4 B lose trunc(1.2) = 1, 2 C lose trunc(0.6) = 0.
        var setup = Fight(0, 12, Kind(A, 6, 6), Kind(B, 4, 4), Kind(C, 2, 2));

        var list = Fill(setup, new ScriptedRandom(), 1, _ => 4);

        Assert.Equal(Repeated((A, 5), (B, 3), (C, 2)), list);
    }

    [Fact]
    public void TheTrimUsesTheAverageLevelOfTheWholeListWithIntegerDivision()
    {
        // 6 A of level 2 and 3 B of level 5: (12 + 15) / 9 = 3. Party level 2: trunc(3 * 2 / 3) = 2, so 0.1 of each kind: none removed.
        var setup = Fight(0, 12, Kind(A, 6, 6), Kind(B, 3, 3));

        var list = Fill(setup, new ScriptedRandom(), 2, e => e == A ? 2 : 5);

        Assert.Equal(Repeated((A, 6), (B, 3)), list);

        // Party level 1 against the same average of 3: trunc(1) = 1, so 0.2: A loses trunc(1.2) = 1, B trunc(0.6) = 0.
        list = Fill(setup, new ScriptedRandom(), 1, e => e == A ? 2 : 5);

        Assert.Equal(Repeated((A, 5), (B, 3)), list);
    }

    [Fact]
    public void TheTrimTakesFromTheFrontAndKeepsTheOtherKindsInTheirPlaces()
    {
        var setup = Fight(0, 12, Kind(A, 5, 10), Kind(B, 5, 5));
        var random = new ScriptedRandom(BattleDice.Range(0, 5, 2));

        var list = Fill(setup, random, 1, _ => 4);

        // The extras stop at 12: A's 2 (rolled 2) make A x5, B x5, A x2. 0.3 of 7 A is trunc(2.1) = 2, taken from the front, and
        // of 5 B is trunc(1.5) = 1, so the extras stay behind the B.
        Assert.Equal(Repeated((A, 3), (B, 4), (A, 2)), list);
    }

    // EnemyList: a script's list (Party null) as given, no draws.
    [Fact]
    public void AScriptsEnemiesAreListedAsGivenWithNoDraws()
    {
        var setup = BattleSetup.Scripted([A, B, A, C], 3, 0, 0, canFlee: true, null);
        var random = new ScriptedRandom();

        var list = setup.EnemyList(PartyOfLevel(), _ => 99, random, Fallback);

        Assert.Equal([A, B, A, C], list);
    }

    [Fact]
    public void AScriptsEnemiesAreNotTrimmedByLevel()
    {
        var setup = BattleSetup.Scripted([A, A, A, A, A, A, A, A, A, A], 0, 0, 0, canFlee: true, null);

        Assert.Equal(10, setup.EnemyList(PartyOfLevel(), _ => 50, new ScriptedRandom(), Fallback).Count);
    }

    [Fact]
    public void AScriptsEnemiesStopAtTwelve()
    {
        var setup = BattleSetup.Scripted([.. Enumerable.Repeat(A, 15)], 0, 0, 0, canFlee: true, null);

        Assert.Equal(12, setup.EnemyList(PartyOfLevel(), _ => 1, new ScriptedRandom(), Fallback).Count);
    }

    [Fact]
    public void AMonsterPartysEnemyListIsItsRolledOne()
    {
        var setup = Fight(0, 12, Kind(A, 1, 2));
        var random = new ScriptedRandom(BattleDice.Range(0, 1, 1));

        Assert.Equal(Repeated((A, 2)), setup.EnemyList(PartyOfLevel(), _ => 1, random, Fallback));
        Assert.Equal(0, random.Remaining);
    }

    // ChooseArena (0x80008698), from the terrain's record.
    private static PluginSet TerrainPlugins(TempPlugins temp, int terrain, IReadOnlyList<ArenaRange>? arenas)
    {
        var record = new TerrainRecord(RecordId.Decimal(RecordTypes.Terrain, terrain), RecordId.Decimal(RecordTypes.Sky, 0), true, Arenas: arenas);
        return temp.Aidyn(("terrain.json", JsonSerializer.Serialize(new[] { record }, PluginJson.Options)));
    }

    private static BattleSetup At(int? terrain) =>
        new(BattleSource.Wanderer, null, terrain, null, [], null, false, true, null);

    [Fact]
    public void AFixedArenaDrawsNothing()
    {
        using var temp = new TempPlugins();
        var plugins = TerrainPlugins(temp, 4, [new ArenaRange(19, 19)]);

        Assert.Equal(19, At(4).ChooseArena(plugins, new ScriptedRandom()));
    }

    [Fact]
    public void ARangeDrawsOnceInclusive()
    {
        using var temp = new TempPlugins();
        var plugins = TerrainPlugins(temp, 0, [new ArenaRange(6, 7)]);

        var low = new ScriptedRandom(BattleDice.Range(6, 7, 6));
        var high = new ScriptedRandom(BattleDice.Range(6, 7, 7));

        Assert.Equal(6, At(0).ChooseArena(plugins, low));
        Assert.Equal(7, At(0).ChooseArena(plugins, high));
        Assert.Equal(0, low.Remaining);
        Assert.Equal(0, high.Remaining);
    }

    // Terrains 20-22: RollD(1, 2) first, 1 picks the first range, then one Range.
    [Fact]
    public void TwoRangesRollAD2FirstAndOneTakesTheFirstRange()
    {
        using var temp = new TempPlugins();
        var plugins = TerrainPlugins(temp, 20, [new ArenaRange(6, 7), new ArenaRange(8, 11)]);
        var random = new ScriptedRandom(BattleDice.Range(1, 2, 1), BattleDice.Range(6, 7, 7));

        Assert.Equal(7, At(20).ChooseArena(plugins, random));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void TwoRangesAndATwoTakeTheSecondRange()
    {
        using var temp = new TempPlugins();
        var plugins = TerrainPlugins(temp, 20, [new ArenaRange(6, 7), new ArenaRange(8, 11)]);
        var random = new ScriptedRandom(BattleDice.Range(1, 2, 2), BattleDice.Range(8, 11, 10));

        Assert.Equal(10, At(20).ChooseArena(plugins, random));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void NoTerrainOrNoRecordOrNoArenasChoosesNone()
    {
        using var temp = new TempPlugins();
        var plugins = TerrainPlugins(temp, 4, []);

        Assert.Null(At(null).ChooseArena(plugins, new ScriptedRandom()));
        Assert.Null(At(5).ChooseArena(plugins, new ScriptedRandom()));
        Assert.Null(At(4).ChooseArena(plugins, new ScriptedRandom()));
    }

    [Fact]
    public void ATerrainWithArenasLeftOutChoosesNone()
    {
        using var temp = new TempPlugins();
        var plugins = TerrainPlugins(temp, 4, null);

        Assert.Null(At(4).ChooseArena(plugins, new ScriptedRandom()));
    }
}
