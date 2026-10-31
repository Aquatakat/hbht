using System.Numerics;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// A combatant's turn: the field's radius and who it's engaged with, the body held in its field, the stick that drives it,
/// the ring of teardrops, the grid's cells, and the moves a turn counts (<c>Combatant.UpdateRadius</c>, <c>Body.ClampToGoal</c>,
/// <c>CombatMarkers.Update</c>, <c>ArenaGrid.Vacate</c>, <c>Combatant.UpdateFromBody</c>). A turn's start and end and the round's
/// end need a whole game, which <c>Battle.Of</c> leaves out, so they're not here.
/// </summary>
public sealed class BattleTurnTests
{
    // ---- The field's radius by Dexterity ----

    // 0x800E0C20: 0,0,0,3,7,12,16,21,26,28,30,33,36,38: the first place whose byte is at least the Dexterity, 14 past the end.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(7, 4)]
    [InlineData(8, 5)]
    [InlineData(12, 5)]
    [InlineData(13, 6)]
    [InlineData(16, 6)]
    [InlineData(17, 7)]
    [InlineData(27, 9)]
    [InlineData(28, 9)]
    [InlineData(29, 10)]
    [InlineData(30, 10)]
    [InlineData(37, 13)]
    [InlineData(38, 13)]
    [InlineData(39, 14)]
    [InlineData(99, 14)]
    public void TheRadiusIsTheFirstPlaceInTheTableWhoseByteReachesTheDexterity(int dexterity, int radius)
    {
        Assert.Equal(radius, Combatant.RadiusFor(dexterity));
    }

    private static Battle Alone(Combatant fighter) => BattleFixtures.Create(1, 0, fighter);

    private static Combatant Fighter(int slot, int dexterity, float x = 0, float z = 0, bool hostile = false)
    {
        var fighter = BattleFixtures.Fighter(slot, dexterity);
        fighter.Body.Sphere.Position = new Vector3(x, 0.5f, z);
        if (hostile)
            fighter.Flags |= CombatantFlags.Hostile;
        return fighter;
    }

    // UpdateRadius: the band, then each timed effect in order; Haste adds its level.
    /// <summary>A spell's lasting effects put on a combatant's sheet at a level, as a cast that landed puts them.</summary>
    private static void Cast(Combatant on, SpellRecord spell, int level) =>
        on.Sheet.AddEffect(spell.Id, spell.Effects!, level, spell.School, 3, spell.Duration!.At(level), null, null, null);

    [Fact]
    public void AFighterWithNoEffectsHasItsDexteritysRadius()
    {
        var fighter = Fighter(0, 20);
        var battle = Alone(fighter);

        fighter.UpdateRadius(battle);

        Assert.Equal(7, fighter.Radius);
        Assert.False(fighter.Engaged);
    }

    [Fact]
    public void HasteAddsItsLevelToTheRadius()
    {
        var fighter = Fighter(0, 20);
        var battle = Alone(fighter);
        Cast(fighter, MagicData.Haste, 2);

        fighter.UpdateRadius(battle);

        Assert.Equal(9, fighter.Radius);
    }

    [Fact]
    public void StellarGravityTakesItsLevelAwayIfThatLeavesSomething()
    {
        var fighter = Fighter(0, 20);
        var battle = Alone(fighter);
        Cast(fighter, MagicData.StellarGravity, 3);

        fighter.UpdateRadius(battle);

        Assert.Equal(4, fighter.Radius);
    }

    // The radius is 7: a level of 7 or more would leave 0 or less, so 1 is left.
    [Theory]
    [InlineData(6, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    [InlineData(50, 1)]
    public void StellarGravityLeavesOneAtLeast(int level, int radius)
    {
        var fighter = Fighter(0, 20);
        var battle = Alone(fighter);
        Cast(fighter, MagicData.StellarGravity, level);

        fighter.UpdateRadius(battle);

        // Level 6 takes 7 to 1 itself; 7 isn't less than 7, so it leaves 1 too.
        Assert.Equal(radius, fighter.Radius);
    }

    [Fact]
    public void TheEffectsAreAppliedInTheirOrder()
    {
        var fighter = Fighter(0, 20);
        var battle = Alone(fighter);

        // 7, gravity 10 is no less than 7 so 1, then Haste 3: 4.
        Cast(fighter, MagicData.StellarGravity, 10);
        Cast(fighter, MagicData.Haste, 3);
        fighter.UpdateRadius(battle);
        Assert.Equal(4, fighter.Radius);

        // 7, Haste 3: 10, gravity 10 is no less than 10 so 1.
        var other = Fighter(0, 20);
        var otherBattle = Alone(other);
        Cast(other, MagicData.Haste, 3);
        Cast(other, MagicData.StellarGravity, 10);
        other.UpdateRadius(otherBattle);
        Assert.Equal(1, other.Radius);
    }

    [Fact]
    public void OtherKindsOfEffectDoNothingToTheRadius()
    {
        var fighter = Fighter(0, 20);
        var battle = Alone(fighter);
        Cast(fighter, MagicData.AirShield, 5);

        fighter.UpdateRadius(battle);

        Assert.Equal(7, fighter.Radius);
    }

    [Fact]
    public void TheRadiusIsCappedAtThirteenAfterTheEffects()
    {
        var fighter = Fighter(0, 38);
        var battle = Alone(fighter);
        Cast(fighter, MagicData.Haste, 5);

        fighter.UpdateRadius(battle);

        Assert.Equal(Combatant.MostRadius, fighter.Radius);
        Assert.Equal(13, fighter.Radius);
    }

    [Fact]
    public void ADexterityPastTheTableTakesItsFourteenBackToThirteen()
    {
        var fighter = Fighter(0, 39);
        var battle = Alone(fighter);

        fighter.UpdateRadius(battle);

        Assert.Equal(13, fighter.Radius);
    }

    // IsWithinReach: the distance across the ground is at most each body's larger radius, summed, and 0.75. Radii of 0.7: 2.15.
    [Fact]
    public void AnOpponentWithinReachEngagesAndHoldsTheRadiusToOne()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        var foe = Fighter(1, 20, x: 2.0f, z: 0, hostile: true);
        var battle = BattleFixtures.Create(1, 1, hero, foe);

        hero.UpdateRadius(battle);

        Assert.True(hero.Engaged);
        Assert.Equal(1, hero.Radius);
        // The foe is engaged by the hero just the same.
        foe.UpdateRadius(battle);
        Assert.True(foe.Engaged);
        Assert.Equal(1, foe.Radius);
    }

    [Fact]
    public void AnOpponentJustOutOfReachDoesNotEngage()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        var foe = Fighter(1, 20, x: 2.2f, z: 0, hostile: true);
        var battle = BattleFixtures.Create(1, 1, hero, foe);

        hero.UpdateRadius(battle);

        Assert.False(hero.Engaged);
        Assert.Equal(7, hero.Radius);
    }

    [Fact]
    public void ReachCountsTheLargerOfEachBodysScaledAndPlainRadius()
    {
        // A body of radius 0.7 scaled by 2 reaches 1.4, so with the hero's 0.7 and 0.75 it reaches 2.85.
        var hero = Fighter(0, 20, x: 0, z: 0);
        var big = new Combatant(1, Characters.Create(Characters.Alaron() with { Id = Entity(0x2001), Attributes = new Attributes(15, 12, 20, 15, 15, 30) }),
            new EntityBody(RecordId.Hex(RecordTypes.Actor, 0x2D4A), 0.7f, 2, 1), new Body(new Vector3(2.8f, 0.5f, 0), 0.7f, 2),
            CombatantFlags.Active | CombatantFlags.Hostile);
        var battle = BattleFixtures.Create(1, 1, hero, big);

        Assert.True(hero.IsWithinReach(battle, big));

        big.Body.Sphere.Position = new Vector3(2.9f, 0.5f, 0);
        Assert.False(hero.IsWithinReach(battle, big));
    }

    [Fact]
    public void AnAllyWithinReachDoesNotEngage()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        var friend = Fighter(1, 20, x: 1.0f, z: 0);
        var battle = BattleFixtures.Create(2, 0, hero, friend);

        hero.UpdateRadius(battle);

        Assert.False(hero.Engaged);
        Assert.Equal(7, hero.Radius);
    }

    [Fact]
    public void ADeadOrUnableOpponentDoesNotEngage()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        var dead = BattleFixtures.Fighter(1, 20, dead: true);
        dead.Flags |= CombatantFlags.Hostile;
        dead.Body.Sphere.Position = new Vector3(1, 0.5f, 0);
        var held = BattleFixtures.Fighter(2, 20, active: false);
        held.Flags |= CombatantFlags.Hostile;
        held.Body.Sphere.Position = new Vector3(1, 0.5f, 0);
        var battle = BattleFixtures.Create(1, 2, hero, dead, held);

        hero.UpdateRadius(battle);

        Assert.False(hero.Engaged);
        Assert.Equal(7, hero.Radius);
    }

    [Fact]
    public void WalkingAwayClearsTheEngagement()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        var foe = Fighter(1, 20, x: 2.0f, z: 0, hostile: true);
        var battle = BattleFixtures.Create(1, 1, hero, foe);
        hero.UpdateRadius(battle);
        Assert.True(hero.Engaged);

        foe.Body.Sphere.Position = new Vector3(10, 0.5f, 0);
        hero.UpdateRadius(battle);

        Assert.False(hero.Engaged);
        Assert.Equal(7, hero.Radius);
    }

    [Fact]
    public void EngagementStopsAPerformance()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        hero.Flags |= CombatantFlags.Performing;
        var foe = Fighter(1, 20, x: 2.0f, z: 0, hostile: true);
        var battle = BattleFixtures.Create(1, 1, hero, foe);

        hero.UpdateRadius(battle);

        Assert.True(hero.Engaged);
        Assert.False(hero.Performing);
        Assert.Equal(1, hero.Radius);
    }

    [Fact]
    public void PerformingAloneHoldsTheRadiusToOne()
    {
        var hero = Fighter(0, 20);
        hero.Flags |= CombatantFlags.Performing;
        var battle = Alone(hero);

        hero.UpdateRadius(battle);

        Assert.True(hero.Performing);
        Assert.False(hero.Engaged);
        Assert.Equal(1, hero.Radius);
    }

    [Fact]
    public void PerformingDoesNotRaiseASmallerRadius()
    {
        // Dexterity 0 has a radius of 0; the hold is "at most 1".
        var hero = Fighter(0, 0);
        hero.Flags |= CombatantFlags.Performing;
        var battle = Alone(hero);

        hero.UpdateRadius(battle);

        Assert.Equal(0, hero.Radius);
    }

    [Fact]
    public void NoOneIsRootedInABattleOfHandMadeCombatants()
    {
        var hero = Fighter(0, 20);
        var battle = Alone(hero);

        Assert.False(battle.Rooted(hero.Entity));
        // (A rooted entity's radius of 0 needs the battle's own list, which only a started battle fills.)
    }

    [Fact]
    public void PlaceFieldCentresTheBodysFieldOnItsCellAtTheBodysHeight()
    {
        var hero = Fighter(0, 20, x: 5, z: 6);
        var battle = Alone(hero);
        hero.UpdateRadius(battle);
        hero.SyncFromBody(battle);

        hero.PlaceField(battle);

        Assert.Equal((new Vector3(5, 0.5f, 6), 7f), hero.Body.Field);
    }

    // ---- A body held in its field ----

    private static Body BodyAt(Vector3 at, (Vector3, float)? field)
    {
        var body = new Body(at, 0.5f) { Field = field };
        return body;
    }

    [Fact]
    public void ABodyInsideItsFieldIsLeftAlone()
    {
        var body = BodyAt(new Vector3(1, 0.5f, 2), (Vector3.Zero, 5));
        body.Sphere.Velocity = new Vector3(1, 2, 3);

        body.KeepInField();

        Assert.Equal(new Vector3(1, 0.5f, 2), body.Sphere.Position);
        Assert.Equal(new Vector3(1, 2, 3), body.Sphere.Velocity);
    }

    [Fact]
    public void ABodyPastTheRadiusIsPutBackOnTheSphereAlongItsOffsetKeepingItsHeightAndVelocity()
    {
        // 3D distance sqrt(36 + 4 + 64) = sqrt(104); X and Z go back to radius / distance of their offsets, Y stays.
        var body = BodyAt(new Vector3(6, 2, 8), (Vector3.Zero, 5));
        body.Sphere.Velocity = new Vector3(1, 2, 3);
        var length = MathF.Sqrt(104);

        body.KeepInField();

        Assert.Equal(6 / length * 5, body.Sphere.Position.X, 1e-5f);
        Assert.Equal(2f, body.Sphere.Position.Y);
        Assert.Equal(8 / length * 5, body.Sphere.Position.Z, 1e-5f);
        Assert.Equal(new Vector3(1, 2, 3), body.Sphere.Velocity);
    }

    [Fact]
    public void TheFieldIsMeasuredFromItsCentreNotTheOrigin()
    {
        var body = BodyAt(new Vector3(30, 1, 10), (new Vector3(10, 1, 10), 4));

        body.KeepInField();

        Assert.Equal(14f, body.Sphere.Position.X, 1e-5f);
        Assert.Equal(10f, body.Sphere.Position.Z, 1e-5f);
        Assert.Equal(1f, body.Sphere.Position.Y);
    }

    [Fact]
    public void ABodyAtTheRadiusIsOnItAlready()
    {
        var body = BodyAt(new Vector3(3, 0, 4), (Vector3.Zero, 5));

        body.KeepInField();

        Assert.Equal(3f, body.Sphere.Position.X, 1e-5f);
        Assert.Equal(4f, body.Sphere.Position.Z, 1e-5f);
    }

    [Fact]
    public void ABodyHighAboveTheCentreReachesLessFarAcrossTheGround()
    {
        // 3D distance sqrt(9 + 16 + 16) of 6.4 against a radius of 5.
        var body = BodyAt(new Vector3(3, 4, 4), (Vector3.Zero, 5));
        var length = MathF.Sqrt(41);

        body.KeepInField();

        Assert.Equal(3 / length * 5, body.Sphere.Position.X, 1e-5f);
        Assert.Equal(4 / length * 5, body.Sphere.Position.Z, 1e-5f);
        Assert.Equal(4f, body.Sphere.Position.Y);
    }

    [Fact]
    public void ARadiusOfZeroPinsXAndZToTheCentre()
    {
        var body = BodyAt(new Vector3(6, 2, 8), (new Vector3(1, 0, 2), 0));

        body.KeepInField();

        Assert.Equal(1f, body.Sphere.Position.X);
        Assert.Equal(2f, body.Sphere.Position.Z);
        Assert.Equal(2f, body.Sphere.Position.Y);
    }

    [Fact]
    public void ABodyAtTheCentreOfAZeroFieldStays()
    {
        var body = BodyAt(new Vector3(1, 0, 2), (new Vector3(1, 0, 2), 0));

        body.KeepInField();

        Assert.Equal(new Vector3(1, 0, 2), body.Sphere.Position);
    }

    [Fact]
    public void ABodyWithNoFieldIsNeverHeld()
    {
        var body = BodyAt(new Vector3(600, 2, 800), null);

        body.KeepInField();

        Assert.Equal(new Vector3(600, 2, 800), body.Sphere.Position);
    }

    [Fact]
    public void StepHoldsABodyInItsFieldAfterItMoves()
    {
        var body = BodyAt(new Vector3(0, 0.5f, 0), (Vector3.Zero, 2));
        body.Heading = Vector2.UnitX;
        body.Request = Body.Walk;

        // An animation that carries it 10 units forward along its heading a tick, over the frame it's moved once in.
        var world = Worlds.Flat();
        body.Step(world, new SteadyMotion(10));
        body.Step(world, new SteadyMotion(10));

        Assert.Equal(2f, body.Sphere.Position.X, 0.05f);
    }

    // ---- The stick that drives a body in a turn ----

    private static Body Driven() => new(Vector3.Zero, 0.5f);

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.04f, 0f)]
    [InlineData(0.05f, 0f)]
    public void WithinTheDeadzoneTheBodyStands(float x, float y)
    {
        var body = Driven();
        body.Request = Body.Run;

        body.Drive(new MoveInput(new Vector2(x, y)), new Vector2(0, 1));

        Assert.Equal(Body.Idle, body.Request);
    }

    [Theory]
    [InlineData(0.06f, Body.Walk)]
    [InlineData(0.5f, Body.Walk)]
    [InlineData(0.79f, Body.Walk)]
    [InlineData(0.8f, Body.Run)]
    [InlineData(0.95f, Body.Run)]
    [InlineData(1f, Body.Run)]
    public void PastTheDeadzoneItWalksUnderPointEightAndRunsFromIt(float tilt, int request)
    {
        var body = Driven();

        body.Drive(new MoveInput(new Vector2(0, tilt)), new Vector2(0, 1));

        Assert.Equal(request, body.Request);
    }

    [Fact]
    public void SneakingIsItsOwnRequestAtAnyTiltPastTheDeadzone()
    {
        var body = Driven();

        body.Drive(new MoveInput(new Vector2(0, 0.3f), Sneak: true), new Vector2(0, 1));
        Assert.Equal(Body.Sneak, body.Request);
        Assert.Equal(25, body.Request);

        body.Drive(new MoveInput(new Vector2(0, 0.95f), Sneak: true), new Vector2(0, 1));
        Assert.Equal(Body.Sneak, body.Request);

        body.Drive(new MoveInput(new Vector2(0, 0.01f), Sneak: true), new Vector2(0, 1));
        Assert.Equal(Body.Idle, body.Request);
    }

    [Fact]
    public void ADrivenBodyTurnsTowardTheStick()
    {
        var body = Driven();
        var before = body.Heading;

        body.Drive(new MoveInput(new Vector2(0, 0.5f)), new Vector2(0, 1));

        // Up on the stick is the way the camera looks, (0, 1), and it turns 20 degrees toward it.
        Assert.NotEqual(before, body.Heading);
        Assert.True(Vector2.Dot(body.Heading, new Vector2(0, 1)) > Vector2.Dot(before, new Vector2(0, 1)));
    }

    [Fact]
    public void AHeldBodyStandsAndDoesNotTurn()
    {
        var body = Driven();
        body.Request = Body.Run;
        var before = body.Heading;

        body.Drive(new MoveInput(new Vector2(0, 1), Held: true), new Vector2(0, 1));

        Assert.Equal(Body.Idle, body.Request);
        Assert.Equal(before, body.Heading);
    }

    // ---- The ring of teardrops ----

    // CombatMarkers.CountFor (0x8008580C): 6 up to 3, 16 up to 7, 20 above.
    [Theory]
    [InlineData(0f, 6)]
    [InlineData(3.0f, 6)]
    [InlineData(3.0001f, 16)]
    [InlineData(7.0f, 16)]
    [InlineData(7.0001f, 20)]
    [InlineData(40f, 20)]
    public void TheRingHasSixSixteenOrTwentyTeardrops(float radius, int count)
    {
        Assert.Equal(count, CombatMarkers.CountFor(radius));
    }

    private static Combatant Walker(int dexterity, float scale = 1, bool controlled = false, float turnX = 10, float turnZ = 20)
    {
        var entity = Characters.Alaron() with { Id = Entity(0x2000), Attributes = new Attributes(15, 12, dexterity, 15, 15, 30) };
        var look = new EntityBody(RecordId.Hex(RecordTypes.Actor, 0x2D4A), 1, scale, 1);
        var fighter = new Combatant(0, Characters.Create(entity), look, new Body(new Vector3(turnX, 0.5f, turnZ), 1, scale), CombatantFlags.Active)
        {
            Controlled = controlled,
        };
        fighter.TurnStart = new Vector2(turnX, turnZ);
        return fighter;
    }

    [Fact]
    public void TheRingIsCentredOnTheTurnStartWithItsFirstTeardropDueZAndTheNextTowardMinusX()
    {
        // Dexterity 20 has a radius of 7; the body's scaled radius is 1; the ring is 8 across, with 20 teardrops.
        var walker = Walker(20);
        var battle = Alone(walker);
        var ring = battle.Ring;

        ring.Update(battle, walker);

        Assert.Equal(7, walker.Radius);
        Assert.Equal(8f, ring.RingRadius, 1e-5f);
        Assert.Equal(20, ring.Count);
        Assert.Equal(20, ring.Points.Length);
        Assert.Equal(10f, ring.Points[0].X, 1e-4f);
        Assert.Equal(28f, ring.Points[0].Z, 1e-4f);
        // 18 degrees round (0.017453 a degree) toward -X.
        var step = 18 * 0.017453f;
        Assert.Equal(10 - MathF.Sin(step) * 8, ring.Points[1].X, 1e-4f);
        Assert.Equal(20 + MathF.Cos(step) * 8, ring.Points[1].Z, 1e-4f);
        Assert.True(ring.Points[1].X < ring.Points[0].X);
    }

    [Fact]
    public void EveryTeardropStandsAtTheRingsRadiusFromTheTurnStart()
    {
        var walker = Walker(20);
        var battle = Alone(walker);

        battle.Ring.Update(battle, walker);

        foreach (var point in battle.Ring.Points)
            Assert.Equal(8f, Vector2.Distance(new Vector2(point.X, point.Z), new Vector2(10, 20)), 1e-3f);
    }

    [Fact]
    public void WithNoGroundBelowEachTeardropKeepsTheBodysHeight()
    {
        // The world is empty, so every sweep misses, and a miss with no neighbour's height to average is the body's.
        var walker = Walker(20);
        walker.Body.Sphere.Position = new Vector3(10, 3.25f, 20);
        var battle = Alone(walker);

        battle.Ring.Update(battle, walker);

        Assert.Equal(20, battle.Ring.Count);
        foreach (var point in battle.Ring.Points)
            Assert.Equal(3.25f, point.Y, 1e-5f);
    }

    [Fact]
    public void TheRingIsCentredOnWhereTheTurnBeganNotWhereTheBodyIs()
    {
        var walker = Walker(20, turnX: 10, turnZ: 20);
        walker.Body.Sphere.Position = new Vector3(14, 0.5f, 22);
        var battle = Alone(walker);

        battle.Ring.Update(battle, walker);

        Assert.Equal(10f, battle.Ring.Points[0].X, 1e-4f);
        Assert.Equal(28f, battle.Ring.Points[0].Z, 1e-4f);
    }

    // Radius = the field's + the body's scaled radius, + the scaled radius again when the AI plays it.
    [Theory]
    [InlineData(0, 1f, false, 1.0f, 6)] // 0 + 1
    [InlineData(0, 1.7f, false, 1.7f, 6)] // 0 + 1.7
    [InlineData(0, 1.7f, true, 3.4f, 16)] // twice 1.7 crosses 3
    [InlineData(1, 1f, false, 4.0f, 16)] // 3 + 1
    [InlineData(4, 1.7f, false, 5.7f, 16)] // 4 + 1.7
    [InlineData(4, 1.7f, true, 7.4f, 20)] // twice 1.7 crosses 7
    [InlineData(20, 1f, false, 8.0f, 20)]
    public void TheRingsSizeFollowsTheFieldAndTheBodyAndTheAIsScaledRadiusAgain(int dexterity, float scale, bool controlled, float radius, int count)
    {
        var walker = Walker(dexterity, scale, controlled);
        var battle = Alone(walker);

        battle.Ring.Update(battle, walker);

        Assert.Equal(radius, battle.Ring.RingRadius, 1e-4f);
        Assert.Equal(count, battle.Ring.Count);
    }

    [Fact]
    public void LayingTheRingOutWorksTheRadiusOutFirst()
    {
        var walker = Walker(20);
        var battle = Alone(walker);
        Cast(walker, MagicData.Haste, 2);

        battle.Ring.Update(battle, walker);

        Assert.Equal(9, walker.Radius);
        Assert.Equal(10f, battle.Ring.RingRadius, 1e-5f);
    }

    // ---- The grid's cells ----

    // Combatant.CellX: rounded half away from zero, as a signed byte.
    [Theory]
    [InlineData(2.5f, 3)]
    [InlineData(2.4f, 2)]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 1)]
    [InlineData(-0.5f, -1)]
    [InlineData(-0.4f, 0)]
    [InlineData(-2.5f, -3)]
    [InlineData(-2.6f, -3)]
    [InlineData(41.5f, 42)]
    public void ACellIsTheCoordinateRoundedHalfAwayFromZero(float coordinate, int cell)
    {
        Assert.Equal(cell, ArenaGrid.CellOf(coordinate));
    }

    private static ArenaGrid Grid(int width, int depth)
    {
        var grid = new ArenaGrid();
        grid.Init(new MapRecord(RecordId.Hex(RecordTypes.Map, 1), [], new ObjectGrid(Vector2.Zero, new Vector2(1, 1), width, depth, [])));
        return grid;
    }

    [Fact]
    public void VacateFreesWhatOccupyTook()
    {
        var grid = Grid(6, 6);
        grid.Occupy(1, 2, 2);
        Assert.Equal(ArenaGrid.Taken, grid[1, 2]);
        Assert.Equal(ArenaGrid.Taken, grid[2, 3]);

        grid.Vacate(1, 2, 2);

        for (var x = 0; x < 6; x++)
            for (var z = 0; z < 6; z++)
                Assert.Equal(0, grid[x, z]);
    }

    [Fact]
    public void VacateFreesOnlyItsFootprint()
    {
        var grid = Grid(6, 6);
        grid.Occupy(0, 0, 3);

        grid.Vacate(1, 1, 1);

        Assert.Equal(0, grid[1, 1]);
        Assert.Equal(ArenaGrid.Taken, grid[0, 0]);
        Assert.Equal(ArenaGrid.Taken, grid[2, 2]);
        Assert.Equal(ArenaGrid.Taken, grid[1, 2]);
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(6, 0, 1)]
    [InlineData(0, 6, 1)]
    [InlineData(5, 5, 2)] // a footprint reaching past the last cell
    [InlineData(4, 0, 3)]
    public void VacateDoesNothingIfAnyCellOfTheFootprintIsOutside(int x, int z, int footprint)
    {
        var grid = Grid(6, 6);
        for (var i = 0; i < 6; i++)
            grid.Occupy(i, i, 1);
        grid.Occupy(5, 0, 1);

        grid.Vacate(x, z, footprint);

        for (var i = 0; i < 6; i++)
            Assert.Equal(ArenaGrid.Taken, grid[i, i]);
        Assert.Equal(ArenaGrid.Taken, grid[5, 0]);
    }

    [Fact]
    public void VacateReachesTheLastCell()
    {
        var grid = Grid(6, 6);
        grid.Occupy(5, 5, 1);

        grid.Vacate(5, 5, 1);

        Assert.Equal(0, grid[5, 5]);
    }

    [Fact]
    public void SyncingABodyMovesItsCellsOnTheGrid()
    {
        var hero = Fighter(0, 20, x: 3, z: 3);
        var battle = Alone(hero);
        battle.Grid.Init(new MapRecord(RecordId.Hex(RecordTypes.Map, 1), [], new ObjectGrid(Vector2.Zero, new Vector2(1, 1), 10, 10, [])));
        hero.Footprint = 2;

        hero.SyncFromBody(battle);
        Assert.Equal(ArenaGrid.Taken, battle.Grid[3, 3]);
        Assert.Equal(ArenaGrid.Taken, battle.Grid[4, 4]);

        hero.Body.Sphere.Position = new Vector3(7, 0.5f, 6);
        hero.SyncFromBody(battle);

        Assert.Equal(0, battle.Grid[3, 3]);
        Assert.Equal(0, battle.Grid[4, 4]);
        Assert.Equal(ArenaGrid.Taken, battle.Grid[7, 6]);
        Assert.Equal(ArenaGrid.Taken, battle.Grid[8, 7]);
    }

    // ---- Counting moves ----

    [Fact]
    public void EachCallWhereTheBodyMovedCountsAMoveToThreeAndTheFourthSetsMoved()
    {
        var hero = Fighter(0, 20, x: 0, z: 0);
        var battle = Alone(hero);
        // Its facing is read for the first time, so the first call is a change.
        hero.UpdateFromBody(battle);
        Assert.Equal(1, hero.Moves);

        hero.Body.Sphere.Position = new Vector3(1, 0.5f, 0);
        hero.UpdateFromBody(battle);
        Assert.Equal(2, hero.Moves);
        Assert.False(hero.Flags.HasFlag(CombatantFlags.Moved));

        hero.Body.Sphere.Position = new Vector3(2, 0.5f, 0);
        hero.UpdateFromBody(battle);
        Assert.Equal(3, hero.Moves);
        Assert.False(hero.Flags.HasFlag(CombatantFlags.Moved));

        hero.Body.Sphere.Position = new Vector3(3, 0.5f, 0);
        hero.UpdateFromBody(battle);
        Assert.Equal(3, hero.Moves);
        Assert.True(hero.Flags.HasFlag(CombatantFlags.Moved));
    }

    [Fact]
    public void ACallWithNoChangeCountsNothing()
    {
        var hero = Fighter(0, 20, x: 2, z: 2);
        var battle = Alone(hero);
        hero.UpdateFromBody(battle);
        var moves = hero.Moves;

        hero.UpdateFromBody(battle);
        hero.UpdateFromBody(battle);

        Assert.Equal(moves, hero.Moves);
        Assert.False(hero.Flags.HasFlag(CombatantFlags.Moved));
    }

    [Fact]
    public void TurningWhereItStandsCountsAMoveToo()
    {
        var hero = Fighter(0, 20, x: 2, z: 2);
        var battle = Alone(hero);
        hero.UpdateFromBody(battle);
        Assert.Equal(1, hero.Moves);

        hero.Body.Heading = Vector2.UnitX;
        hero.UpdateFromBody(battle);

        Assert.Equal(2, hero.Moves);
        Assert.Equal(Vector2.UnitX, hero.Facing);
    }

    [Fact]
    public void ReadingTheBodyPutsTheAimsAndTheCellWhereItIs()
    {
        var hero = Fighter(0, 20, x: 3, z: 4);
        var battle = Alone(hero);

        hero.UpdateFromBody(battle);

        Assert.Equal(new Vector2(3, 4), hero.Cell);
        Assert.Equal((new Vector2(3, 4), new Vector2(3, 4), new Vector2(3, 4)), (battle.ThrownAim, battle.AreaAim, battle.AiAreaCentre));
    }
}
