using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using CharacterParty = Aidyn.Core.Characters.Party;

namespace Aidyn.Core.Tests;

/// <summary>
/// The small rules of a battle's setup: the spawns' names and facings, a body's footprint, the Ranger's ambush roll and the
/// eight directions (<c>Combat.SpawnPosition</c>, <c>Combatant.Init</c>, <c>Party.BattleAmbushRoll</c>).
/// </summary>
public sealed class BattleRulesTests
{
    // "%s%u%c" (strings[0xDC]) with "good" or "bad", the set and 'a' + the slot.
    [Theory]
    [InlineData(true, 4, 0, "good4a")]
    [InlineData(false, 0, 3, "bad0d")]
    [InlineData(true, 2, 2, "good2c")]
    [InlineData(false, 1, 1, "bad1b")]
    public void ASpawnsNameIsTheSideTheSetAndTheLetter(bool good, int set, int letter, string expected)
    {
        Assert.Equal(expected, Battle.SpawnPoint(good, set, letter));
    }

    [Fact]
    public void FleePointsAreNamedFlee0ToFlee7()
    {
        Assert.Equal("flee0", Battle.FleePoint(0));
        Assert.Equal("flee7", Battle.FleePoint(7));
        Assert.Equal(8, Battle.MaxFleePoints);
    }

    // 0x800D27A8: pairs (dx, dz), 0 = (0, -1) north, 1 = (1, -1), 2 = (1, 0), ... 7 = (-1, -1), normalised.
    [Fact]
    public void TheEightDirectionsGoRoundFromNorthAndAreUnitVectors()
    {
        var expected = new Vector2[] { new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1) };

        Assert.Equal(8, Battle.Directions.Count);
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(1f, Battle.Directions[i].Length(), 1e-6f);
            var want = Vector2.Normalize(expected[i]);
            Assert.Equal(want.X, Battle.Directions[i].X, 1e-6f);
            Assert.Equal(want.Y, Battle.Directions[i].Y, 1e-6f);
        }
        Assert.Equal(new Vector2(0, -1), Battle.Directions[0]);
        Assert.Equal(new Vector2(1, 0), Battle.Directions[2]);
        Assert.Equal(new Vector2(0, 1), Battle.Directions[4]);
        Assert.Equal(new Vector2(-1, 0), Battle.Directions[6]);
    }

    // Combatant.Init: f = radius * scale; f += f; fp = f > 0 ? (int)(f + 0.5) : -(int)(0.5 - f); 0 becomes 1.
    [Theory]
    [InlineData(0.7f, 1f, 1)] // Alaron
    [InlineData(1.0f, 1f, 2)] // a giant rat
    [InlineData(1.5f, 1f, 3)]
    [InlineData(0.6f, 0.956f, 1)] // Rheda
    [InlineData(0.65f, 1.064f, 1)] // Sholeh
    [InlineData(0.55f, 1f, 1)] // a dialogue entity
    [InlineData(2.0f, 1f, 4)]
    [InlineData(0.25f, 1f, 1)] // 0.5 rounds up to 1
    [InlineData(0.75f, 1f, 2)] // 1.5 rounds away from zero to 2
    [InlineData(1.25f, 1f, 3)] // 2.5 rounds up to 3
    [InlineData(0.2f, 1f, 1)] // 0.4 rounds to 0, which becomes 1
    [InlineData(0f, 1f, 1)]
    [InlineData(0.6f, 2f, 2)] // the scale counts: 2.4 rounds to 2
    public void AFootprintIsTwiceTheScaledRadiusRoundedHalfAwayFromZeroAtLeastOne(float radius, float scale, int expected)
    {
        var look = new EntityBody(RecordId.Hex(RecordTypes.Actor, 0x2D4A), radius, scale, 1);

        Assert.Equal(expected, Battle.Footprint(look));
    }

    // Party.BattleAmbushRoll (0x80076C60).
    private static Character Member(int n, int ranger, int stamina = 30, int intelligence = 18) =>
        Characters.Create(Characters.Alaron() with
        {
            Id = Entity(0x3000 + n),
            Attributes = new Attributes(intelligence, 12, 15, 15, 15, stamina),
            Skills = new SkillLevels(0, 0, 0, 0, 0, 0, ranger, 0, 0, 0, 0, 0),
        });

    private static CharacterParty PartyOf(params Character[] members)
    {
        var party = ItemData.NewParty();
        foreach (var member in members)
            Assert.True(party.Add(member));
        return party;
    }

    [Fact]
    public void WithNoRangerThereIsNoRollAndTheNeutralValueIsReturned()
    {
        var party = PartyOf(Member(0, ranger: 0), Member(1, ranger: 0));

        Assert.Equal(Battle.NoAmbushRoll, Battle.AmbushRoll(party, new ScriptedRandom()));
        Assert.Equal(2, Battle.NoAmbushRoll);
    }

    [Fact]
    public void AnEmptyPartyHasNoRoll()
    {
        Assert.Equal(2, Battle.AmbushRoll(ItemData.NewParty(), new ScriptedRandom()));
    }

    // need = max(1, 5 - Ranger): a Ranger of 1 needs 4 Stamina to be the one who rolls.
    [Fact]
    public void ARangerWithoutTheStaminaForItDoesNotRoll()
    {
        var short1 = Member(0, ranger: 1, stamina: 3);
        var enough = Member(1, ranger: 1, stamina: 4);

        Assert.Equal(2, Battle.AmbushRoll(PartyOf(short1), new ScriptedRandom()));
        // The roll is made, 1, under 18 * 3 + 10 = 64.
        Assert.True(Battle.AmbushRoll(PartyOf(enough), Characters.D100(1)) > 0);
    }

    [Fact]
    public void ARollBelowTheTargetGivesTheStepOfTheMarginAndCostsStamina()
    {
        var ranger = Member(0, ranger: 3);
        var before = ranger.Stats.Modified(Stat.Stamina);

        // Target 18 * 3 + 3 * 10 = 84; a roll of 20 leaves 64, which is step 4 (62 and up).
        var step = Battle.AmbushRoll(PartyOf(ranger), Characters.D100(20));

        Assert.Equal(4, step);
        // The cost is max(1, 5 - 3) = 2.
        Assert.Equal(before - 2, ranger.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ARollOfTheTargetItselfFailsAndCostsNothing()
    {
        var ranger = Member(0, ranger: 3);
        var before = ranger.Stats.Modified(Stat.Stamina);

        Assert.Equal(0, Battle.AmbushRoll(PartyOf(ranger), Characters.D100(84)));
        Assert.Equal(before, ranger.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ARollOneUnderTheTargetIsTheFirstStep()
    {
        var ranger = Member(0, ranger: 3);

        Assert.Equal(1, Battle.AmbushRoll(PartyOf(ranger), Characters.D100(83)));
    }

    [Fact]
    public void TheCostIsAtLeastOneForAHighRanger()
    {
        var ranger = Member(0, ranger: 7);
        var before = ranger.Stats.Modified(Stat.Stamina);

        // Target 18 * 3 + 70 = 124: any roll passes.
        Battle.AmbushRoll(PartyOf(ranger), Characters.D100(100));

        Assert.Equal(before - 1, ranger.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void TheStrictlyBestRangerRollsAndAnEarlierMemberKeepsATie()
    {
        var first = Member(0, ranger: 3, intelligence: 10);
        var second = Member(1, ranger: 3, intelligence: 30);
        var firstStamina = first.Stats.Modified(Stat.Stamina);
        var secondStamina = second.Stats.Modified(Stat.Stamina);

        // The first rolls: target 10 * 3 + 30 = 60. A roll of 50 passes with 10 left.
        var step = Battle.AmbushRoll(PartyOf(first, second), Characters.D100(50));

        Assert.Equal(1, step);
        Assert.Equal(firstStamina - 2, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(secondStamina, second.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ALaterMemberWithABetterRangerRolls()
    {
        var first = Member(0, ranger: 2, intelligence: 30);
        var second = Member(1, ranger: 4, intelligence: 10);
        var firstStamina = first.Stats.Modified(Stat.Stamina);
        var secondStamina = second.Stats.Modified(Stat.Stamina);

        // The second rolls: target 10 * 3 + 40 = 70 (the first's would be 110). A roll of 75 fails.
        Assert.Equal(0, Battle.AmbushRoll(PartyOf(first, second), Characters.D100(75)));
        Assert.Equal(firstStamina, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(secondStamina, second.Stats.Modified(Stat.Stamina));
        // And a roll of 69 passes, costing the second max(1, 5 - 4) = 1.
        Assert.Equal(1, Battle.AmbushRoll(PartyOf(first, second), Characters.D100(69)));
        Assert.Equal(secondStamina - 1, second.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void TheBestRangerWithoutTheStaminaLeavesTheNextBest()
    {
        var tired = Member(0, ranger: 4, stamina: 0);
        var fit = Member(1, ranger: 2);
        var fitStamina = fit.Stats.Modified(Stat.Stamina);

        // The tired one needs 1 and has 0: the other rolls, target 18 * 3 + 20 = 74, so 74 fails and 73 passes.
        Assert.Equal(0, Battle.AmbushRoll(PartyOf(tired, fit), Characters.D100(74)));
        Assert.Equal(1, Battle.AmbushRoll(PartyOf(tired, fit), Characters.D100(73)));
        Assert.Equal(fitStamina - 3, fit.Stats.Modified(Stat.Stamina));
    }

    // Combat.SpawnPosition (0x8005C294): one draw for the direction, then the name of the point.
    private static Battle InArena(BattleSetup setup)
    {
        var points = new[] { 4, 3, 2, 1, 0 }
            .SelectMany(set => new[] { "good", "bad" }.Select(side => (set, side)))
            .SelectMany(p => "abcd".Select(letter => $"{p.side}{p.set}{letter}"))
            .Select((name, i) => (MapObject)new ReferencePointObject(i, new Vector3(10 + i, 0, 20), 0, PakVariant.Any, null, i, name, null));
        var map = new MapRecord(RecordId.Hex(RecordTypes.Map, 7), [.. points]);
        var chunk = new ChunkEntry(3, 3, null, null, map.Id, 0);
        var set = new MapSetRecord(Combat.Arena.MapSet, "Battle", 150, [chunk]);
        return BattleFixtures.Create(setup, new Combat.Arena(0, set, chunk, map));
    }

    private static BattleSetup Setup(bool ambush = false, int collision = 0, int animation = 0) =>
        new(BattleSource.Wanderer, null, null, null, [], null, ambush, true, null, collision, animation);

    private static (string Name, ReferencePointObject? Point, int Direction) Spawn(Battle battle, bool party, bool reserve = false, int roll = 2,
        int letter = 0, int draw = 2, bool enemyFacing = false)
    {
        // The party's direction and the enemies' with collision 1 or 2 are Range(1, 3); only the enemies' with 0 are Range(5, 7).
        var raw = enemyFacing ? BattleDice.Range(5, 7, draw) : BattleDice.Range(1, 3, draw);
        var random = new ScriptedRandom(raw);
        var spawn = battle.Spawn(party, reserve, letter, roll, random);
        Assert.Equal(0, random.Remaining);
        return spawn;
    }

    [Theory]
    [InlineData(0, true, "good4a")] // the party takes good, the nearest set for animation 0
    [InlineData(0, false, "bad4a")]
    [InlineData(2, true, "good2a")]
    [InlineData(2, false, "bad2a")]
    [InlineData(3, true, "good1a")]
    [InlineData(3, false, "bad1a")]
    public void ThePartyTakesGoodAndTheEnemiesBadWithTheSetFourLessTheAnimationByte(int animation, bool party, string expected)
    {
        var battle = InArena(Setup(animation: animation));

        var (name, point, _) = Spawn(battle, party, enemyFacing: !party);

        Assert.Equal(expected, name);
        Assert.NotNull(point);
        Assert.Equal(expected, point.Name);
    }

    [Fact]
    public void ReservesTakeSetZeroAndTheirOwnLetter()
    {
        var battle = InArena(Setup(animation: 2));

        Assert.Equal("bad0c", Spawn(battle, party: false, reserve: true, letter: 2, enemyFacing: true).Name);
    }

    [Fact]
    public void TheDirectionIsOneDrawOneToThreeForThePartyAndFiveToSevenForEnemiesWithCollisionZero()
    {
        var battle = InArena(Setup(collision: 0));

        Assert.Equal(3, Spawn(battle, party: true, draw: 3).Direction);
        Assert.Equal(1, Spawn(battle, party: true, draw: 1).Direction);
        Assert.Equal(5, Spawn(battle, party: false, draw: 5, enemyFacing: true).Direction);
        Assert.Equal(7, Spawn(battle, party: false, draw: 7, enemyFacing: true).Direction);
    }

    [Theory]
    [InlineData(1)] // the player came at them from outside their front
    [InlineData(2)]
    public void EnemiesFaceOneToThreeLikeThePartyWithCollisionOneOrTwo(int collision)
    {
        var battle = InArena(Setup(collision: collision));

        Assert.Equal(3, Spawn(battle, party: false, draw: 3).Direction);
        Assert.Equal(1, Spawn(battle, party: false, draw: 1).Direction);
    }

    [Fact]
    public void AScriptsCollisionOfOneIsNotAnAmbush()
    {
        var battle = InArena(Setup(collision: 1, animation: 0));

        Assert.Equal("good4a", Spawn(battle, party: true).Name);
        Assert.Equal("bad4a", Spawn(battle, party: false).Name);
    }

    // The ambush: the party takes bad and the enemies good; only the party's set moves by the Ranger's roll.
    [Theory]
    [InlineData(0, 0, "bad4")]
    [InlineData(0, 1, "bad4")] // animation 0 stays at 4
    [InlineData(0, 2, "bad4")]
    [InlineData(0, 3, "bad4")]
    [InlineData(0, 4, "bad3")]
    [InlineData(0, 21, "bad3")]
    [InlineData(2, 0, "bad4")]
    [InlineData(2, 1, "bad3")]
    [InlineData(2, 2, "bad2")]
    [InlineData(2, 3, "bad2")]
    [InlineData(2, 4, "bad1")]
    [InlineData(2, 15, "bad1")]
    [InlineData(3, 0, "bad3")] // a failed roll with animation 3 is 3, not 4
    [InlineData(3, 1, "bad2")]
    [InlineData(3, 2, "bad1")]
    [InlineData(3, 3, "bad1")]
    [InlineData(3, 4, "bad1")] // animation 3 doesn't go lower
    [InlineData(3, 21, "bad1")]
    public void InAnAmbushThePartyTakesBadAndTheRangersRollMovesItsSet(int animation, int roll, string expected)
    {
        var battle = InArena(Setup(ambush: true, animation: animation));

        Assert.Equal(expected + "a", Spawn(battle, party: true, roll: roll).Name);
    }

    [Theory]
    [InlineData(0, 0, "good4")]
    [InlineData(2, 0, "good2")]
    [InlineData(2, 5, "good2")]
    [InlineData(3, 1, "good1")]
    public void InAnAmbushTheEnemiesTakeGoodAtTheirSetWhateverTheRoll(int animation, int roll, string expected)
    {
        var battle = InArena(Setup(ambush: true, animation: animation));

        Assert.Equal(expected + "a", Spawn(battle, party: false, roll: roll).Name);
    }

    [Fact]
    public void InAnAmbushTheEnemiesReservesTakeGoodZero()
    {
        var battle = InArena(Setup(ambush: true, animation: 2));

        Assert.Equal("good0b", Spawn(battle, party: false, reserve: true, letter: 1).Name);
    }

    [Fact]
    public void ASpawnTheArenaLacksComesBackWithNoPoint()
    {
        var battle = InArena(Setup(animation: 0));

        var (name, point, _) = Spawn(battle, party: true, letter: 5);

        Assert.Equal("good4f", name);
        Assert.Null(point);
    }
}
