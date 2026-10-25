using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>Bodies other than the player's: steered to a goal or turned (<c>MoveTo</c>, <c>SetAiDest</c>, <c>SetFacing</c>), and pushed apart.</summary>
public class BodyTests
{
    private static readonly Vector3 Standing = new(0, 0.5f, 0);

    private static Body Settled(Vector3 at, float scale = 1)
    {
        var body = new Body(at, 0.5f, scale);
        body.Step(Worlds.Flat(), new SteadyMotion(0));
        return body;
    }

    [Fact]
    public void ABodyNobodySteersKeepsItsRequestAndStandsStill()
    {
        var body = Settled(Standing);
        body.Request = 4;
        var at = body.Sphere.Position;
        for (var i = 0; i < 30; i++)
            body.Step(Worlds.Flat(), new SteadyMotion(0));
        Assert.Equal(4, body.Request);
        Assert.False(body.Steered);
        Assert.Equal(at.X, body.Sphere.Position.X, 1e-4f);
        Assert.Equal(at.Z, body.Sphere.Position.Z, 1e-4f);
    }

    [Theory]
    [InlineData(true, Body.Walk)]
    [InlineData(false, Body.Run)]
    public void ItWalksOrRunsToItsGoalAndLetsGoWithinItsRadius(bool walk, int request)
    {
        var body = Settled(Standing);
        body.Heading = Vector2.UnitX;
        body.GoTo(new Vector2(0, 10), 1, walk);
        var motion = new SteadyMotion(0.05f);
        body.Step(Worlds.Flat(), motion);
        Assert.Equal(request, body.Request);
        Assert.Equal(walk ? 0.5f : 0.95f, body.Stick.Length(), 1e-5f);

        var ticks = 1;
        for (; body.Steered && ticks < 1000; ticks++)
            body.Step(Worlds.Flat(), motion);
        Assert.True(ticks < 1000);
        Assert.Equal(Body.Idle, body.Request);
        Assert.False(body.Moving);
        var left = Vector2.Distance(new Vector2(body.Sphere.Position.X, body.Sphere.Position.Z), new Vector2(0, 10));
        Assert.True(left < 1, $"{left}");
    }

    [Fact]
    public void ItTurnsTowardItsGoalTenDegreesATick()
    {
        var body = Settled(Standing);
        body.Heading = Vector2.UnitX;
        body.GoTo(new Vector2(0, 10), 1, walk: true);
        body.Step(Worlds.Flat(), new SteadyMotion(0));
        Assert.Equal(80, Degrees(body.Heading, Vector2.UnitY), 0.01f);
        for (var i = 0; i < 8; i++)
            body.Step(Worlds.Flat(), new SteadyMotion(0));
        Assert.Equal(0, Degrees(body.Heading, Vector2.UnitY), 0.01f);
    }

    [Fact]
    public void ItsAnimationsCarryItFartherAtALargerScale()
    {
        var small = Settled(Standing);
        var large = Settled(Standing, scale: 2);
        foreach (var body in new[] { small, large })
        {
            body.Heading = Vector2.UnitY;
            body.GoTo(new Vector2(0, 40), 1, walk: true);
            for (var i = 0; i < 10; i++)
                body.Step(Worlds.Flat(), new SteadyMotion(0.02f));
        }
        Assert.Equal(2 * small.Sphere.Position.Z, large.Sphere.Position.Z, 1e-3f);
    }

    [Fact]
    public void StoppingItMidRunStandsItAtItsNextStep()
    {
        var body = Settled(Standing);
        body.GoTo(new Vector2(30, 0), 1, walk: false);
        for (var i = 0; i < 30; i++)
            body.Step(Worlds.Flat(), new SteadyMotion(0.05f));
        Assert.Equal(Body.Run, body.Request);

        // As a hunter giving up does (ResetMoveQueue): the next step asks for standing, so the run's root
        // motion doesn't carry it on.
        body.Stop();
        body.Step(Worlds.Flat(), new SteadyMotion(0.05f));
        Assert.Equal(Body.Idle, body.Request);
        Assert.False(body.Steered);
        var at = body.Sphere.Position;
        for (var i = 0; i < 30; i++)
            body.Step(Worlds.Flat(), new SteadyMotion(0.05f));
        Assert.Equal(at.X, body.Sphere.Position.X, 1e-4f);
        Assert.Equal(at.Z, body.Sphere.Position.Z, 1e-4f);
    }

    [Fact]
    public void FacingTurnsItWhereItStandsThenLetsGo()
    {
        var body = Settled(Standing);
        body.Heading = Vector2.UnitX;
        body.Face(new Vector2(0, -3));
        var at = body.Sphere.Position;
        var ticks = 0;
        for (; body.Steered && ticks < 100; ticks++)
            body.Step(Worlds.Flat(), new SteadyMotion(0.05f));
        // 90° at 5° a tick, which lands within rounding of it, and a tick to let go.
        Assert.Equal(19, ticks);
        Assert.Equal(-Vector2.UnitY, body.Heading);
        Assert.Equal(Body.Idle, body.Request);
        Assert.Equal(at.X, body.Sphere.Position.X, 1e-4f);
        Assert.Equal(at.Z, body.Sphere.Position.Z, 1e-4f);
    }

    [Fact]
    public void GoingSomewhereForgetsATurnAndFacingForgetsAGoal()
    {
        var body = Settled(Standing);
        body.Face(Vector2.UnitX);
        body.GoTo(new Vector2(5, 5), 1, walk: false);
        Assert.True(body.Moving);
        Assert.False(body.Turning);
        body.Face(Vector2.UnitY);
        Assert.False(body.Moving);
        Assert.True(body.Turning);
    }

    [Fact]
    public void ItsChunkKeepsItFiveHundredthsInsideItsEdges()
    {
        var body = Settled(new Vector3(9, 0.5f, 5));
        body.Bounds = (new Vector2(0.05f), new Vector2(9.95f));
        body.Heading = Vector2.UnitX;
        body.GoTo(new Vector2(30, 5), 1, walk: false);
        for (var i = 0; i < 120; i++)
            body.Step(Worlds.Flat(), new SteadyMotion(0.05f));
        Assert.Equal(9.95f, body.Sphere.Position.X, 1e-5f);
    }

    /// <summary>Two floors, the lower listed first, as the original's grid might list them.</summary>
    private static CollisionWorld TwoFloors(CollisionFlags upper = CollisionFlags.None)
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(2), [.. Worlds.Floor(0, -5, 5, -5, 5),
            Worlds.Triangle(new(-5, 3, -5), new(-5, 3, 5), new(5, 3, -5), flags: upper),
            Worlds.Triangle(new(5, 3, -5), new(-5, 3, 5), new(5, 3, 5), flags: upper)], Vector3.Zero);
        return world;
    }

    [Fact]
    public void SettingDownFromOnHighTakesTheFirstFloorTheGridListsNotTheNearest()
    {
        var (at, grounded) = Body.SetDown(TwoFloors(), new Vector3(1, 6, 1), 0.5f, null);
        Assert.True(grounded);
        Assert.Equal(0.51f, at.Y, 1e-5f);
    }

    [Fact]
    public void SettingDownNearAHeightLooksWithinOneOfItFirst()
    {
        var (at, grounded) = Body.SetDown(TwoFloors(), new Vector3(1, 6, 1), 0.5f, null, near: 3.2f);
        Assert.True(grounded);
        Assert.Equal(3.51f, at.Y, 1e-5f);
    }

    [Fact]
    public void SettingDownSkipsSeeThroughTriangles()
    {
        var (at, _) = Body.SetDown(TwoFloors(CollisionFlags.SeeThrough), new Vector3(1, 6, 1), 0.5f, null, near: 3.2f);
        Assert.Equal(0.51f, at.Y, 1e-5f);
    }

    [Fact]
    public void SettingDownFromOnHighSweepsFromAHundredToMinusAHundredWhateverTheBodysHeight()
    {
        var (at, grounded) = Body.SetDown(Worlds.Flat(), new Vector3(0, 150, 0), 0.5f, null);
        Assert.True(grounded);
        Assert.Equal(0.51f, at.Y, 1e-5f);

        var high = new CollisionWorld();
        high.Add(Worlds.Chunk(3), Worlds.Floor(120, -5, 5, -5, 5), Vector3.Zero);
        Assert.False(Body.SetDown(high, new Vector3(0, 125, 0), 0.5f, null).Grounded);
    }

    /// <summary>
    /// Missing, it tries four times more a little to the side, or three after a miss near a height, two draws each;
    /// the moves add up, and it stays where they took it.
    /// </summary>
    [Theory]
    [InlineData(null, 4)]
    [InlineData(2f, 3)]
    public void SettingDownOverNothingTriesToTheSideAndStaysWhereTheTriesTookIt(float? near, int tries)
    {
        // Each draw half its range: a half turn and 0.125 out, which moves it 0.0625 along X.
        var random = new ScriptedRandom([.. Enumerable.Repeat(0x8000_0000u, 2 * tries)]);
        var (at, grounded) = Body.SetDown(new CollisionWorld(), new Vector3(0, 7, 0), 0.5f, random, near);
        Assert.False(grounded);
        Assert.Equal(0, random.Remaining);
        Assert.Equal(0.0625f * tries, at.X, 1e-5f);
        Assert.Equal(7, at.Y);
        Assert.Equal(0, at.Z, 1e-5f);
    }

    private static CrowdMember Member(Vector3 at, float radius = 0.5f, float scale = 1, ChunkKey? chunk = null) =>
        new(new CollisionSphere { Radius = radius, Position = at }, scale, chunk ?? Worlds.Chunk(0));

    [Fact]
    public void ABodyIsPushedStraightAwayAcrossTheGroundUntilItTouchesTheOther()
    {
        var body = new CollisionSphere { Radius = 0.5f, Position = new(0, 0.5f, 0) };
        var other = new CollisionSphere { Radius = 0.5f, Position = new(0.6f, 1.5f, 0) };
        Assert.True(SpherePhysics.PushApart(body, 1, other, 1, out var distance));
        // The distance is the one before the push.
        Assert.Equal(0.6f, distance, 1e-5f);
        Assert.Equal(-0.4f, body.Position.X, 1e-5f);
        Assert.Equal(0.5f, body.Position.Y);
        Assert.Equal(new Vector3(0.6f, 1.5f, 0), other.Position);
    }

    [Fact]
    public void ScaleWidensBothSpheres()
    {
        var body = new CollisionSphere { Radius = 0.5f, Position = new(0, 0.5f, 0) };
        var other = new CollisionSphere { Radius = 0.5f, Position = new(1.5f, 0.5f, 0) };
        Assert.True(SpherePhysics.PushApart(body, 1.5f, other, 2, out _));
        Assert.Equal(1.5f - 1.75f, body.Position.X, 1e-5f);
    }

    [Fact]
    public void AMissMeasuresTheOthersRadiusAtThisBodysScale()
    {
        var body = new CollisionSphere { Radius = 0.5f, Position = Vector3.Zero };
        var other = new CollisionSphere { Radius = 0.5f, Position = new(5, 0, 0) };
        Assert.False(SpherePhysics.PushApart(body, 1, other, 2, out var distance));
        Assert.Equal(5 - 1 + 0.5f, distance, 1e-5f);
    }

    [Fact]
    public void TheCrowdReportsTheNearestOtherBeforeAnyPush()
    {
        var bodies = new CrowdMember?[] { Member(new(0, 0.5f, 0)), Member(new(0.6f, 1.5f, 0)), Member(new(4, 0.5f, 0)) };
        var (nearest, distance) = Crowd.Push(bodies, 0, new Vector3(0, 0.5f, 0));
        Assert.Equal(1, nearest);
        Assert.Equal(0.6f, distance, 1e-5f);
    }

    [Fact]
    public void BodiesInAnotherChunkOrMoreThanThreeApartInHeightDontPush()
    {
        var bodies = new CrowdMember?[]
        {
            Member(new(0, 0.5f, 0)),
            Member(new(0.5f, 0.5f, 0), chunk: Worlds.Chunk(1)),
            Member(new(0, 3.6f, 0.5f)),
        };
        var (nearest, _) = Crowd.Push(bodies, 0, Vector3.Zero);
        Assert.Equal(new Vector3(0, 0.5f, 0), bodies[0]!.Value.Sphere.Position);
        Assert.Equal(-1, nearest);
    }

    [Fact]
    public void ASecondPushPutsTheBodyBackWhereItBeganTheTick()
    {
        var start = new Vector3(-3, 0.5f, 0);
        var bodies = new CrowdMember?[] { Member(new(0, 0.5f, 0)), Member(new(0.6f, 0.5f, 0)), Member(new(-0.6f, 0.5f, 0)) };
        Crowd.Push(bodies, 0, start);
        Assert.Equal(start, bodies[0]!.Value.Sphere.Position);
    }

    [Fact]
    public void AfterAPushTheTurnStartsAgainFromTheSecondBodySoTheFirstIsntWeighedAgain()
    {
        // Body 1 is pushed out of the first body, to x = 0.4. The turn starts again from body 1,
        // itself, and body 2 is out of reach, so the first isn't weighed again and it stays.
        var start = new Vector3(5, 0.5f, 5);
        var bodies = new CrowdMember?[] { Member(new(-0.6f, 0.5f, 0)), Member(new(0, 0.5f, 0)), Member(new(-1, 0.5f, 3)) };
        Crowd.Push(bodies, 1, start);
        Assert.Equal(0.4f, bodies[1]!.Value.Sphere.Position.X, 1e-5f);
    }
    /// <summary>A state machine as a body's animation, moving nothing.</summary>
    private sealed class MachineMotion(ActorStateMachine machine) : IActorMotion
    {
        public ActorStateMachine Machine => machine;

        public int StateLength => machine.Length(machine.State);

        public int Frame => machine.Clock;

        public bool Entered { get; private set; }

        public float Tick(int request)
        {
            machine.Request = request;
            Entered = machine.Tick();
            return 0;
        }
    }

    /// <summary>Idle (0, 6 ticks) repeats, and dying interrupts it (1, 4 ticks), which goes back to idle for any request, as the Fire Lord's does.</summary>
    private static MachineMotion Mortal() => new(new ActorStateMachine(new ActorRecord(RecordId.Parse("aidyn:actor/0x2FBA"),
        RecordId.Parse("aidyn:scene/0x2FB0"), 2, 0,
        [
            new(RecordId.Parse("aidyn:animation/0x2FB4"), [new(Body.Dying, 100, true, 1), new(ActorStateMachine.AnyRequest, 100, false, 0)]),
            new(RecordId.Parse("aidyn:animation/0x2FB9"), [new(ActorStateMachine.AnyRequest, 100, false, 0)]),
        ]), state => state == 0 ? 6 : 4, new Lcg(0)));

    [Fact]
    public void ABodyAskedToDieHoldsItsDeathsLastFrameWhateverItsAskedForAfter()
    {
        // ProcessPlayers (0x80013200): once its actor has entered a state for 9 and 9 has gone, the body's +0x77C is set, and the
        // animation plays to the end of the state and stops there, where its state machine would go back to idle.
        var body = Settled(Standing);
        var motion = Mortal();
        body.Request = Body.Dying;
        body.Step(Worlds.Flat(), body.Mortal(motion));
        Assert.Equal((1, 1, Body.Dying), (motion.Machine.State, motion.Machine.Clock, body.Playing));
        for (var i = 0; i < 30; i++)
        {
            body.Request = i == 10 ? Body.Talk : Body.Idle;
            body.Step(Worlds.Flat(), body.Mortal(motion));
        }
        Assert.True(body.DeathHeld);
        Assert.Equal((1, 4), (motion.Machine.State, motion.Machine.Clock));
    }

    [Fact]
    public void ABodyStillAskedToDieStopsAtItsEndButOneOnlyAskedNowPlaysOn()
    {
        var body = Settled(Standing);
        var motion = Mortal();
        // Asked at idle's end, the request doesn't hold idle: the death it leads to plays, and holds while it's still asked for.
        for (var i = 0; i < 5; i++)
            body.Step(Worlds.Flat(), body.Mortal(motion));
        Assert.Equal((0, 6), (motion.Machine.State, motion.Machine.Clock));
        body.Request = Body.Dying;
        for (var i = 0; i < 20; i++)
            body.Step(Worlds.Flat(), body.Mortal(motion));
        Assert.Equal((1, 4), (motion.Machine.State, motion.Machine.Clock));
        Assert.False(body.DeathHeld);
    }

    [Fact]
    public void ABodyTurningWhereItStandsKeepsADeathItsAskedFor()
    {
        // Body.ApplyInput (0x80014FEC) asks a steered body to stand only if it isn't dying, and its stick is still while it turns.
        var body = Settled(Standing);
        body.Face(new Vector2(1, 0));
        body.Request = Body.Dying;
        body.Step(Worlds.Flat(), new SteadyMotion(0));
        Assert.True(body.Turning);
        Assert.Equal(Body.Dying, body.Request);
        body.Faded = true;
        body.Step(Worlds.Flat(), new SteadyMotion(0));
        Assert.Equal(Body.Dying, body.Request);
        body.Request = Body.Talk;
        body.Step(Worlds.Flat(), new SteadyMotion(0));
        Assert.Equal(Body.Idle, body.Request);
    }
}
