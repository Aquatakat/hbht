using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HBHT.Core.Characters;
using CharacterParty = HBHT.Core.Characters.Party;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>The random draws a wanderer's <c>WanderTick</c> takes, and the ranges it notices the player by.</summary>
public class WanderRandomTests
{
    /// <summary>The seed whose next value is <paramref name="next"/>, by inverting the generator.</summary>
    private static uint SeedFor(uint next)
    {
        const uint multiplier = 0x19660D;
        uint inverse = multiplier;
        for (var i = 0; i < 5; i++)
            inverse *= 2 - multiplier * inverse;
        return (next - 0x3C6EF35F) * inverse;
    }

    [Fact]
    public void FloatOfZeroIsZeroAndDrawsNothing()
    {
        var random = new Lcg(1234);
        Assert.Equal(0, random.Float(0));
        Assert.Equal(1234u, random.Seed);
    }

    [Fact]
    public void FloatReachesItsMaximumForTheTopValue()
    {
        var random = new Lcg(SeedFor(0xFFFFFFFF));
        Assert.Equal(7.5f, random.Float(7.5f));
    }

    [Fact]
    public void FloatIsTheNextValueAsAFractionOfTwoToTheThirtySecondTimesTheMaximum()
    {
        var half = new Lcg(SeedFor(0x80000000));
        Assert.Equal(3f, half.Float(6));

        var random = new Lcg(12345);
        var next = unchecked((uint)(12345UL * 0x19660D + 0x3C6EF35F));
        Assert.Equal((float)(next / 4294967296.0 * 2 * Math.PI), random.Float(2 * MathF.PI), 1e-5f);
        Assert.Equal(next, random.Seed);
    }

    [Fact]
    public void TheRangesAreFixedMultiplesOfTheWalkingRange()
    {
        var ranges = new NoticeRanges(8, 0.5f);
        Assert.Equal(12, ranges.Running, 1e-5f);
        Assert.Equal(16, ranges.Most, 1e-5f);
        Assert.Equal(8 * 0.6f * 0.5f, ranges.Sneaking, 1e-5f);
        Assert.Equal(8 * 0.55f * 0.5f, ranges.Other, 1e-5f);
    }

    [Theory]
    [InlineData(Body.Run, 12)]
    [InlineData(Body.Walk, 8)]
    [InlineData(Body.Sneak, 2.4f)]
    [InlineData(Body.Idle, 2.2f)]
    [InlineData(1, 2.2f)]
    [InlineData(4, 2.2f)]
    public void TheRangeForWhatThePlayerDoesIsPickedByHisRequest(int request, float range)
    {
        Assert.Equal(range, new NoticeRanges(8, 0.5f).For(request), 1e-5f);
    }

    [Fact]
    public void WithNoPartyTheSneakingAndStandingRangesAreZero()
    {
        Assert.Equal(0, NoticeRanges.SneakFor(null));
        var ranges = new NoticeRanges(8, NoticeRanges.SneakFor(null));
        Assert.Equal(0, ranges.Sneaking);
        Assert.Equal(0, ranges.Other);
        Assert.Equal(8, ranges.Walking);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 0.6f)]
    [InlineData(10, 0.2f)]
    [InlineData(15, 0.2f)]
    public void ThePartysBestStealthLeavesOneMinusEightTenthsOfItUpToTen(int stealth, float left)
    {
        var weak = TradeData.Member(0, new SkillLevels(Stealth: 0));
        var best = TradeData.Member(1, new SkillLevels(Stealth: stealth));
        var party = TradeData.PartyOf(TradeData.Catalog(), null, weak, best);
        Assert.Equal(left, NoticeRanges.SneakFor(party), 1e-5f);
    }
}

/// <summary>One wanderer's <c>WanderTick</c>: moving on, noticing the player, chasing, giving up, the ambush and facing the player.</summary>
public class WanderTickTests
{
    private static readonly RecordId MapId = RecordId.Hex(RecordTypes.Map, 0x1000);

    private const int PartyIndex = 10;

    private static RecordId Voxel(int index) => MapRecord.VoxelId(MapId, index);

    private static WanderNodeObject Node(int index, Vector3 at, WanderFlags flags = WanderFlags.Move, float radius = 1, float scatter = 0,
        float chance = 0, int wait = 1000, params int[] next) =>
        new(index, at, 0, PakVariant.Any, null, radius, scatter, chance, wait, flags, [.. next.Select(Voxel)]);

    /// <summary>A party at the origin of its chunk, whose home is node 0.</summary>
    private static MonsterPartyObject PartyAt(Vector3 at, float leash, PartyFlags flags = PartyFlags.Walks, RecordId? dialogue = null) =>
        new(PartyIndex, at, leash, PakVariant.Any, null, [], RecordId.Hex(RecordTypes.Entity, 0x300), null, 0, 0, Voxel(0), flags, dialogue, null);

    private sealed record Setup(Wanderer Wanderer, LoadedMap Map, CollisionWorld World)
    {
        public Body Body => Wanderer.Body;
    }

    private static Setup Make(MonsterPartyObject party, WanderNodeObject[] nodes, Vector3 at, Vector3 origin = default, CollisionWorld? world = null)
    {
        var map = LoadedMap.Activate(new MapRecord(MapId, [.. nodes, party]), Worlds.Chunk(0), ItemData.Flags(), new MapObjectMemory(), ItemData.NewParty(), new Lcg(), origin);
        var body = new Body(at, 0.5f) { Heading = Vector2.UnitX };
        var look = new EntityBody(RecordId.Hex(RecordTypes.Actor, 1), 0.5f, 1, 2);
        var wanderer = new Wanderer(map, party, look, body, perception: 10);
        wanderer.Take((WanderNodeObject)map[0]!);
        return new Setup(wanderer, map, world ?? Worlds.Flat());
    }

    /// <summary>
    /// A hunter standing at the world's origin, facing +X, so it measures from (3, 0, 0); its home node is 5
    /// further on in Z, and it waits long at it. Its walking range is 10 in no fog.
    /// </summary>
    private static Setup Hunter(float leash = 20, CollisionWorld? world = null) =>
        Make(PartyAt(Vector3.Zero, leash), [Node(0, new Vector3(0, 0, 5))], new Vector3(0, 0.5f, 0), world: world);

    private static Setup Talker() =>
        Make(PartyAt(Vector3.Zero, 20, dialogue: RecordId.Hex(RecordTypes.Dialogue, 1)), [Node(0, new Vector3(0, 0, 5))], new Vector3(0, 0.5f, 0));

    private static bool Tick(Setup s, Vector3 player, int request = Body.Walk, float sneak = 0.5f, float fog = 0, IRandom? random = null) =>
        s.Wanderer.Tick(new WanderTick(s.World, player, request, sneak, fog, random ?? new ScriptedRandom()));

    private static Vector3 Player(float x, float y = 0.5f, float z = 0) => new(x, y, z);

    private static CollisionWorld WalledWorld(float x, float height)
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(1), Worlds.Floor(0, -50, 60, -50, 60), Vector3.Zero);
        world.Add(Worlds.Chunk(2), Worlds.Wall(x, height), Vector3.Zero);
        return world;
    }

    /// <summary>A hunter that's noticed the player at 9 along X and is chasing him, with its ranges set.</summary>
    private static Setup Chasing(float leash)
    {
        var s = Hunter(leash);
        Assert.False(Tick(s, Player(9)));
        Assert.True(s.Wanderer.Chasing);
        return s;
    }

    // Noticing the player.

    [Fact]
    public void ItSetsItsRangesAsThePlayerComesWithinTwiceItsPerceptionFromWhereItLooks()
    {
        var s = Hunter();
        Tick(s, Player(30));
        Assert.Null(s.Wanderer.Ranges);

        // Walking range 10 less 30% for the fog: 7.
        Tick(s, Player(22), fog: 0.4f);
        var ranges = s.Wanderer.Ranges!.Value;
        Assert.Equal(7, ranges.Walking, 1e-5f);
        Assert.Equal(0.5f, ranges.Sneak);
    }

    [Fact]
    public void ItKeepsTheRangesItFirstSetUntilTheyAreCleared()
    {
        var s = Hunter();
        Tick(s, Player(22), fog: 0.4f);
        Tick(s, Player(22), fog: 0);
        Assert.Equal(7, s.Wanderer.Ranges!.Value.Walking, 1e-5f);
    }

    [Theory]
    [InlineData(Body.Walk, 12, true)]
    [InlineData(Body.Walk, 14, false)]
    [InlineData(Body.Run, 17, true)]
    [InlineData(Body.Run, 19, false)]
    public void ItNoticesAPlayerWithinTheRangeForWhatHeIsDoing(int request, float x, bool noticed)
    {
        // Measured from 3 ahead: the walking range is 10 and the running range 15.
        var s = Hunter(leash: 100);
        Tick(s, Player(x), request);
        Assert.Equal(noticed, s.Wanderer.Chasing);
    }

    [Fact]
    public void ANoticingHunterRunsForThePlayerAndStopsWithinTwoOfHim()
    {
        var s = Hunter();
        Assert.False(Tick(s, Player(9, z: 1)));
        Assert.True(s.Wanderer.Chasing);
        Assert.True(s.Body.Moving);
        Assert.False(s.Body.Walks);
        Assert.Equal(new Vector2(9, 1), s.Body.Goal);
        Assert.Equal(2, s.Body.GoalRadius);
    }

    [Fact]
    public void ACalmedHunterDoesntNoticeThePlayer()
    {
        var s = Hunter();
        s.Wanderer.Calmed = true;
        Tick(s, Player(9));
        Assert.False(s.Wanderer.Chasing);
        Assert.False(s.Body.Moving);
    }

    [Fact]
    public void AHunterOutsideItsLeashDoesntNoticeThePlayer()
    {
        var s = Hunter(leash: 20);
        s.Body.Sphere.Position.X = 25;
        Tick(s, Player(34));
        Assert.False(s.Wanderer.Chasing);
    }

    [Fact]
    public void WithNoPartyAStandingOrSneakingPlayerIsOnlyNoticedRightAtTheSpotItLooksAt()
    {
        foreach (var request in new[] { Body.Idle, Body.Sneak })
        {
            var s = Hunter();
            Tick(s, Player(9), request, sneak: 0);
            Assert.False(s.Wanderer.Chasing);

            var near = Hunter();
            near.Wanderer.Tick(new WanderTick(near.World, Player(3), request, 0, 0, new ScriptedRandom()));
            Assert.True(near.Wanderer.Chasing);
        }
    }

    [Fact]
    public void ASneakingPlayerWithGoodStealthIsNoticedOnlyWithinSixTenthsOfThePerceptionTimesWhatStealthLeaves()
    {
        // The walking range, 10, × 0.6 × 0.2.
        var s = Hunter();
        Tick(s, Player(3, z: 2), Body.Sneak, sneak: 0.2f);
        Assert.False(s.Wanderer.Chasing);

        var near = Hunter();
        Tick(near, Player(3, z: 1), Body.Sneak, sneak: 0.2f);
        Assert.True(near.Wanderer.Chasing);
    }

    // Ending a chase.

    [Fact]
    public void ABeatenOffChaserWhoseLeashIsntBrokenGoesBackToItsNodeAtOnce()
    {
        var s = Chasing(leash: 40);
        Assert.False(Tick(s, Player(30)));
        Assert.False(s.Wanderer.Chasing);
        Assert.Null(s.Wanderer.Ranges);
        Assert.True(s.Body.Moving);
        Assert.Equal(s.Wanderer.Goal, s.Body.Goal);
        Assert.Equal(new Vector2(0, 5), s.Body.Goal);
    }

    [Fact]
    public void WithBothBeyondItsLeashItGivesUpStandsStillAndAfterThreeHundredTicksGoesBack()
    {
        var s = Chasing(leash: 10);
        s.Body.Sphere.Position.X = 12.5f;
        Assert.False(Tick(s, Player(20)));
        Assert.False(s.Body.Moving);
        Assert.False(s.Wanderer.Chasing);
        Assert.True(s.Wanderer.GoesBack);
        Assert.Equal(Wanderer.GiveUpTicks, s.Wanderer.Timer);

        // The scripted stream has nothing to draw: going back picks no new node.
        for (var i = 1; i < Wanderer.GiveUpTicks; i++)
            Tick(s, Player(20));
        Assert.False(s.Body.Moving);
        Assert.True(s.Wanderer.GoesBack);

        Tick(s, Player(20));
        Assert.True(s.Body.Moving);
        Assert.False(s.Wanderer.GoesBack);
        Assert.Equal(s.Wanderer.Goal, s.Body.Goal);
        Assert.Equal(new Vector2(0, 5), s.Body.Goal);
    }

    [Fact]
    public void GoingBackAfterGivingUpDrawsNothingFromTheRandomStream()
    {
        var s = Chasing(leash: 10);
        s.Body.Sphere.Position.X = 12.5f;
        var random = new Lcg(777);
        Tick(s, Player(20), random: random);
        for (var i = 0; i < Wanderer.GiveUpTicks; i++)
            Tick(s, Player(20), random: random);
        Assert.True(s.Body.Moving);
        Assert.Equal(777u, random.Seed);
    }

    [Fact]
    public void JustPastItsLeashItKeepsChasingAfterGivingUp()
    {
        // The leash is 10; 11 is past it but not by 2, and the player is near enough to be noticed.
        var s = Chasing(leash: 10);
        s.Body.Sphere.Position.X = 11;
        Assert.False(Tick(s, Player(19)));
        Assert.True(s.Wanderer.GoesBack);
        Assert.Equal(Wanderer.GiveUpTicks, s.Wanderer.Timer);
        Assert.True(s.Wanderer.Chasing);
        Assert.Null(s.Wanderer.Ranges);
    }

    [Fact]
    public void AChaserPastItsLeashStopsChasingOnceThePlayerIsBeyondTheFurthestRange()
    {
        var s = Chasing(leash: 10);
        s.Body.Sphere.Position.X = 10.5f;
        Tick(s, Player(40));
        Assert.True(s.Wanderer.GoesBack);
        Assert.False(s.Wanderer.Chasing);
        Assert.False(s.Body.Moving);
    }

    // The ambush.

    [Fact]
    public void AChasingHunterWithinSevenOfThePlayerAndNothingBetweenAmbushesHim()
    {
        var s = Hunter();
        Assert.True(Tick(s, Player(6.99f)));
        Assert.False(s.Wanderer.Hunts);
        Assert.False(s.Body.Moving);
        Assert.True(s.Wanderer.Chasing);
    }

    [Fact]
    public void ItDoesntAmbushAPlayerSevenOrMoreAway()
    {
        var s = Hunter();
        Assert.False(Tick(s, Player(7)));
        Assert.True(s.Wanderer.Hunts);
        Assert.True(s.Body.Moving);
    }

    [Fact]
    public void AWallBetweenThemStopsTheAmbush()
    {
        var s = Hunter(world: WalledWorld(2.5f, 10));
        Assert.False(Tick(s, Player(5)));
        Assert.True(s.Wanderer.Hunts);
        Assert.True(s.Wanderer.Chasing);
        Assert.True(s.Body.Moving);

        // The same ground without the wall ambushes.
        Assert.True(Tick(Hunter(), Player(5)));
    }

    [Theory]
    [InlineData(2.9f, true)]
    [InlineData(3f, false)]
    [InlineData(5f, false)]
    public void ThreeOrMoreAboveOrBelowTheHunterStopsTheAmbush(float above, bool ambushed)
    {
        var s = Hunter();
        Assert.Equal(ambushed, Tick(s, Player(5, y: 0.5f + above)));
        Assert.Equal(ambushed, !s.Wanderer.Hunts);
    }

    [Fact]
    public void AnAmbushedHunterNoLongerHunts()
    {
        var s = Hunter();
        Assert.True(Tick(s, Player(5)));
        Assert.False(Tick(s, Player(5)));
        Assert.False(s.Body.Moving);
    }

    // Talkers.

    [Fact]
    public void ATalkerNeverChasesEvenWhereAHunterWould()
    {
        var s = Talker();
        Assert.False(Tick(s, Player(9)));
        Assert.False(s.Wanderer.Chasing);
        Assert.False(s.Wanderer.Hunts);
        Assert.False(s.Body.Moving);
    }

    [Fact]
    public void ATalkerWithinThreeOfThePlayerStandsAndTurnsToFaceHim()
    {
        var s = Talker();
        s.Body.Request = Body.Walk;
        Tick(s, Player(2, z: 2));
        Assert.Equal(Body.Idle, s.Body.Request);
        Assert.True(s.Body.Turning);
        AssertNear(Vector2.Normalize(new Vector2(2, 2)), s.Body.TurnTo);
        Assert.False(s.Wanderer.Chasing);
    }

    [Fact]
    public void ATalkerThreeOrMoreAwayDoesntTurn()
    {
        var s = Talker();
        Tick(s, Player(3));
        Assert.False(s.Body.Turning);
        Tick(s, Player(0, y: 3.5f));
        Assert.False(s.Body.Turning);
    }

    [Fact]
    public void ATalkerTurningToThePlayerDoesntMoveOn()
    {
        var s = Make(PartyAt(Vector3.Zero, 20, dialogue: RecordId.Hex(RecordTypes.Dialogue, 1)),
            [Node(0, new Vector3(0, 0, 5), wait: 0, next: 1), Node(1, new Vector3(10, 0, 0))], new Vector3(0, 0.5f, 0));
        // Its timer has run out, and the scripted stream would refuse a draw.
        Tick(s, Player(2));
        Assert.True(s.Body.Turning);
        Assert.Equal(0, s.Wanderer.Node!.Index);
    }

    // Moving on.

    private static readonly uint[] Draws = [0x20000000, 0x40000000, 0x80000000];

    private static Setup Wandering(Vector3 origin = default, PartyFlags flags = PartyFlags.Walks, int wait = 0)
    {
        WanderNodeObject[] nodes =
        [
            Node(0, new Vector3(0, 0, 5), wait: wait, chance: 0.5f, next: [1, 2]),
            Node(1, new Vector3(10, 0, 0), radius: 3, scatter: 2, wait: 7),
            Node(2, new Vector3(-10, 0, 0), WanderFlags.None, radius: 1, scatter: 4, wait: 9),
        ];
        var at = origin + new Vector3(0, 0.5f, 0);
        return Make(PartyAt(Vector3.Zero, 20, flags), nodes, at, origin);
    }

    [Fact]
    public void TheTimerDropsByOneATickAndNothingHappensWhileItsPositive()
    {
        var s = Wandering(wait: 5);
        for (var i = 4; i >= 1; i--)
        {
            Tick(s, Player(1000));
            Assert.Equal(i, s.Wanderer.Timer);
        }
        Assert.Equal(0, s.Wanderer.Node!.Index);
        Assert.False(s.Body.Moving);
    }

    [Fact]
    public void WhenTheTimerRunsOutItMovesOnToTheFirstNextNodeUnlessTheRollIsUnderTheChance()
    {
        var s = Wandering(origin: new Vector3(100, 0, 200));
        var random = new ScriptedRandom(Draws);
        Tick(s, Player(1000), random: random);

        // A roll of 0.125 is under 0.5 and takes Next[1], which is node 2: no walk, so no scatter.
        Assert.Equal(2, s.Wanderer.Node!.Index);
        Assert.Equal(9, s.Wanderer.Timer);
        Assert.Equal(2, random.Remaining);
        Assert.Equal(new Vector2(90, 200), s.Wanderer.Goal);
        Assert.False(s.Body.Moving);

        var other = Wandering(origin: new Vector3(100, 0, 200));
        Tick(other, Player(1000), random: new ScriptedRandom(0xC0000000, 0, 0));
        Assert.Equal(1, other.Wanderer.Node!.Index);
    }

    [Fact]
    public void ANodeItWalksToDrawsTheRollThenAnAngleAndALengthAndScattersTheGoal()
    {
        var s = Wandering(origin: new Vector3(100, 0, 200));
        var random = new ScriptedRandom(0xC0000000, 0x40000000, 0x80000000);
        Tick(s, Player(1000), random: random);

        var replay = new ScriptedRandom(0xC0000000, 0x40000000, 0x80000000);
        replay.Float(1);
        var angle = replay.Float(2 * MathF.PI);
        var length = replay.Float(2);
        Assert.Equal(0, random.Remaining);
        Assert.Equal(1, s.Wanderer.Node!.Index);
        Assert.Equal(7, s.Wanderer.Timer);
        AssertNear(new Vector2(110 + MathF.Sin(angle) * length, 200 + MathF.Cos(angle) * length), s.Wanderer.Goal);
        // A quarter turn, half the scatter: 1 along X.
        AssertNear(new Vector2(111, 200), s.Wanderer.Goal);
    }

    [Fact]
    public void TheScatterIsTheGeneratorsThreeDrawsInOrder()
    {
        // A seed whose first roll takes the walking node.
        uint seed = 1;
        while (new Lcg(seed).Float(1) < 0.5f)
            seed++;
        var s = Wandering();
        var random = new Lcg(seed);
        var replay = new Lcg(seed);
        replay.Float(1);
        var angle = replay.Float(2 * MathF.PI);
        var length = replay.Float(2);
        Tick(s, Player(1000), random: random);

        Assert.Equal(replay.Seed, random.Seed);
        Assert.Equal(1, s.Wanderer.Node!.Index);
        AssertNear(new Vector2(10 + MathF.Sin(angle) * length, MathF.Cos(angle) * length), s.Wanderer.Goal);
    }

    [Theory]
    [InlineData(PartyFlags.Walks, true)]
    [InlineData(PartyFlags.None, false)]
    public void ItSetsOffForItsGoalWithinTheNodesRadiusWalkingOrRunningAsThePartyDoes(PartyFlags flags, bool walks)
    {
        var s = Wandering(flags: flags);
        Tick(s, Player(1000), random: new ScriptedRandom(0xC0000000, 0, 0));
        Assert.True(s.Body.Moving);
        Assert.Equal(walks, s.Body.Walks);
        Assert.Equal(s.Wanderer.Goal, s.Body.Goal);
        Assert.Equal(3, s.Body.GoalRadius);
    }

    [Fact]
    public void ABodyHeldMovingOrTurningDoesntMoveOnWhateverItsTimer()
    {
        var held = Wandering();
        held.Body.Held = true;
        Tick(held, Player(1000));

        var moving = Wandering();
        moving.Body.GoTo(new Vector2(30, 30), 1, walk: true);
        Tick(moving, Player(1000));

        var turning = Wandering();
        turning.Body.Face(Vector2.UnitY);
        Tick(turning, Player(1000));

        // The scripted stream refuses a draw, so none of them chose.
        Assert.All(new[] { held, moving, turning }, s => Assert.Equal(0, s.Wanderer.Node!.Index));
    }

    [Fact]
    public void ANodeItTurnsToHasItFaceTheNodeInsteadOfWalking()
    {
        var s = Make(PartyAt(Vector3.Zero, 20), [Node(0, new Vector3(10, 0, 5), WanderFlags.Rotate)], new Vector3(0, 0.5f, 0));
        s.Wanderer.Resume();
        Assert.False(s.Body.Moving);
        Assert.True(s.Body.Turning);
        AssertNear(Vector2.Normalize(new Vector2(10, 5)), s.Body.TurnTo);
    }

    [Fact]
    public void ResumingForgetsBeingCalmed()
    {
        var s = Hunter();
        s.Wanderer.Calmed = true;
        s.Wanderer.Resume();
        Assert.False(s.Wanderer.Calmed);
    }
}

/// <summary>Wanderers of a loaded chunk, from a plugin of one entity: <c>Wanderers.Tick</c> over them.</summary>
public sealed class WanderersFixture : IDisposable
{
    private readonly TempPlugins _temp = new();

    public static readonly RecordId Entity = RecordId.Hex(RecordTypes.Entity, 0x300);

    public WanderersFixture()
    {
        // One entity, as both its sheet (for its perception) and its body.
        var entity = (JsonObject)JsonSerializer.SerializeToNode(Characters.Alaron() with { Id = Entity, Perception = 10 }, PluginJson.Options)!;
        entity["body"] = JsonSerializer.SerializeToNode(new EntityBody(RecordId.Hex(RecordTypes.Actor, 1), 0.5f, 1, 2), PluginJson.Options);
        Plugins = _temp.Aidyn(("entities.json", new JsonArray(entity).ToJsonString()));
    }

    public PluginSet Plugins { get; }

    public void Dispose() => _temp.Dispose();
}

public class WanderersTickTests(WanderersFixture fixture) : IClassFixture<WanderersFixture>
{
    private static readonly RecordId MapId = RecordId.Hex(RecordTypes.Map, 0x1000);

    [Fact]
    public void TickWeighsEveryWandererAndReturnsTheHuntersThatAmbush()
    {
        var node = new WanderNodeObject(0, new Vector3(5, 0, 5), 0, PakVariant.Any, null, 2, 0, 0, 1000, WanderFlags.Move, []);
        var party = new MonsterPartyObject(1, new Vector3(5, 0, 5), 20, PakVariant.Any, null, [], WanderersFixture.Entity, null, 0, 0,
            MapRecord.VoxelId(MapId, 0), PartyFlags.Walks, null, null);
        var map = LoadedMap.Activate(new MapRecord(MapId, [node, party]), Worlds.Chunk(0), ItemData.Flags(), new MapObjectMemory(), ItemData.NewParty(), new Lcg());
        var world = Worlds.Flat();
        var wanderers = new Wanderers(fixture.Plugins);
        var added = wanderers.Load(map, 20, world, new MapObjectMemory());
        var wanderer = Assert.Single(added);
        Assert.Equal(10, wanderer.Perception);
        var at = wanderer.Body.Sphere.Position;

        // Far off: nothing noticed.
        Assert.Empty(wanderers.Tick(world, at + new Vector3(500, 0, 0), Body.Walk, null, new ScriptedRandom()));
        Assert.False(wanderer.Chasing);

        // 7.5 away, the way it faces, which it notices from where it looks but is too far to ambush.
        var facing = new Vector3(-1, 0, -1) / MathF.Sqrt(2);
        Assert.Empty(wanderers.Tick(world, at + facing * 7.5f, Body.Walk, null, new ScriptedRandom()));
        Assert.True(wanderer.Chasing);
        Assert.True(wanderer.Body.Moving);

        // Within 7: the ambush.
        var ambushes = wanderers.Tick(world, at + facing * 4, Body.Walk, null, new ScriptedRandom());
        Assert.Equal([wanderer], ambushes);
        Assert.False(wanderer.Hunts);
    }

    [Fact]
    public void WanderersStepAsFarFromTheAimAsTheirReach()
    {
        var node = new WanderNodeObject(0, new Vector3(5, 0, 5), 0, PakVariant.Any, null, 2, 0, 0, 1000, WanderFlags.Move, []);
        var party = new MonsterPartyObject(1, new Vector3(5, 0, 5), 20, PakVariant.Any, null, [], WanderersFixture.Entity, null, 0, 0,
            MapRecord.VoxelId(MapId, 0), PartyFlags.Walks, null, null);
        var map = LoadedMap.Activate(new MapRecord(MapId, [node, party]), Worlds.Chunk(0), ItemData.Flags(), new MapObjectMemory(), ItemData.NewParty(), new Lcg());
        var world = Worlds.Flat();
        var wanderers = new Wanderers(fixture.Plugins);
        var wanderer = Assert.Single(wanderers.Load(map, 100, world, new MapObjectMemory()));
        wanderer.Body.GoTo(new Vector2(15, 5), 0.5f, walk: true);
        var player = new CollisionSphere { Position = new Vector3(500, 0.5f, 500) };
        var motion = new SteadyMotion(0.02f);
        var aim = wanderer.Body.Sphere.Position + new Vector3(0, 0, 60);
        void Step() => wanderers.Step(world, player, player.Position, null, aim, _ => motion);

        // 60 from the aim: the original's 30 leaves it where it stands.
        Assert.Equal(Body.StepDistance, wanderers.Reach);
        var at = wanderer.Body.Sphere.Position;
        for (var i = 0; i < 30; i++)
            Step();
        Assert.Equal(at, wanderer.Body.Sphere.Position);

        // Object Distance's 2× and 3× step it out to 100; Original keeps the 30.
        Assert.Equal([30f, 100f, 100f], new[] { 1f, 2f, 3f }.Select(Wanderers.ReachFor));
        wanderers.Reach = Wanderers.ReachFor(2);
        for (var i = 0; i < 30; i++)
            Step();
        Assert.True(wanderer.Body.Sphere.Position.X > at.X + 0.1f, $"{wanderer.Body.Sphere.Position}");
    }
}
