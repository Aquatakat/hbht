using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>A camera object's move of the camera (<c>processVoxelCamera</c>), against a synthetic camera.</summary>
public class CameraMoveTests
{
    private const float Radius = 0.5f;
    private const int Duration = 60;

    private static readonly Vector3 Focus = new(10, 0.5f, 10);
    private static readonly Vector3 AboveHim = new(0, 1.05f, 0);
    private static readonly Vector3 Point = new(20, 4, -6);

    /// <summary>A camera at rest 5 behind a player at <see cref="Focus"/>, which is somewhere else than <see cref="Point"/>.</summary>
    private static FollowCamera Camera() => new(Focus + AboveHim, Vector2.UnitX);

    private static CameraMove Move(CameraFlags flags, Vector3? point = null, float distance = 0, float pitch = 0) =>
        new(flags, point, new Vector3(pitch, distance, Duration));

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-3f)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    [Fact]
    public void AMoveOfTheAimOrPositionHoldsTheCameraEachStep()
    {
        var camera = Camera();
        var move = Move(CameraFlags.Pan | CameraFlags.Aim, Point);
        for (var tick = 0; tick < 5; tick++)
        {
            move.Step(camera, Focus, Radius, tick);
            Assert.True(camera.Held);
            camera.Step(Worlds.Flat(), Focus, Vector2.UnitX);
            Assert.False(camera.Held);
        }
    }

    /// <summary>
    /// The original snaps the aim onto the player as any tracking move starts, which jolts the view
    /// when an ease takes over from a camera whose aim trails him; the port only snaps on a cut.
    /// </summary>
    [Theory]
    [InlineData(CameraFlags.Sine)]
    [InlineData(CameraFlags.Pan)]
    [InlineData(CameraFlags.EaseIn)]
    [InlineData(CameraFlags.EaseOut)]
    public void AnEaseThatTracksKeepsChasingThePlayerRatherThanSnappingOntoHim(CameraFlags kind)
    {
        var camera = Camera();
        var trailing = Focus + AboveHim - new Vector3(1.4f, 0, 0);
        camera.AimAt(trailing);
        Move(kind | CameraFlags.Track | CameraFlags.Position, Point).Step(camera, Focus, Radius, 0);
        AssertNear(trailing + new Vector3(1.4f / 24, 0, 0), camera.Aim);
        Assert.False(camera.TakeCut());
    }

    [Fact]
    public void ACutTellsTheViewNotToBlendAcrossIt()
    {
        var camera = Camera();
        Move(CameraFlags.Cut | CameraFlags.Track | CameraFlags.Position, Point).Step(camera, Focus, Radius, 0);
        Assert.True(camera.TakeCut());
        Assert.False(camera.TakeCut());
    }

    [Fact]
    public void ACutThatTracksPutsTheCameraAtItsPointAndAimsAtThePlayerThenChasesHim()
    {
        var camera = Camera();
        var move = Move(CameraFlags.Cut | CameraFlags.Track | CameraFlags.Position, Point);

        move.Step(camera, Focus, Radius, 0);
        Assert.Equal(Point, camera.Position);
        Assert.Equal(Focus + AboveHim, camera.Aim);
        Assert.Equal(Focus + AboveHim, camera.AimGoal);

        // Later steps leave the position be, and the aim chases him, a 24th of the gap, at most 0.25.
        var far = Focus + new Vector3(10, 0, 0);
        move.Step(camera, far, Radius, 1);
        Assert.Equal(Point, camera.Position);
        Assert.Equal(Focus.X + AboveHim.X + 0.25f, camera.Aim.X, 1e-5f);

        var near = Focus + new Vector3(0, 0, 2.4f);
        move.Step(camera, near, Radius, 2);
        Assert.Equal(Point, camera.Position);
        var before = camera.Aim;
        move.Step(camera, near, Radius, 3);
        var gap = near.Z + AboveHim.Z - before.Z;
        Assert.Equal(before.Z + gap / 24, camera.Aim.Z, 1e-4f);
        Assert.Equal(near + AboveHim, camera.AimGoal);
    }

    [Theory]
    [InlineData(CameraFlags.Sine)]
    [InlineData(CameraFlags.EaseIn)]
    [InlineData(CameraFlags.EaseOut)]
    [InlineData(CameraFlags.Pan)]
    public void AMoveReachesItsPointOverItsDurationAlongItsCurve(CameraFlags kind)
    {
        var camera = Camera();
        var start = camera.Position;
        var move = Move(kind | CameraFlags.Position, Point);

        for (var k = 1; k <= Duration + 10; k++)
        {
            move.Step(camera, Focus, Radius, k - 1);
            var share = Share(kind, Math.Min(k, Duration));
            AssertNear(start + (Point - start) * share, camera.Position);
        }
        AssertNear(Point, camera.Position);

        // The eases' angles, and a pan's steps, put these at known fractions.
        static float Share(CameraFlags kind, int k)
        {
            var progress = (float)k / Duration;
            return kind switch
            {
                CameraFlags.Sine => (1 - MathF.Cos(MathF.PI * progress)) / 2,
                CameraFlags.EaseIn => 1 - MathF.Cos(MathF.PI / 2 * progress),
                CameraFlags.EaseOut => MathF.Sin(MathF.PI / 2 * progress),
                _ => progress,
            };
        }
    }

    [Fact]
    public void ASineIsHalfwayAtHalfItsDuration()
    {
        var camera = Camera();
        var start = camera.Position;
        var move = Move(CameraFlags.Sine | CameraFlags.Position, Point);
        for (var tick = 0; tick < Duration / 2; tick++)
            move.Step(camera, Focus, Radius, tick);
        AssertNear((start + Point) / 2, camera.Position);
    }

    [Fact]
    public void AMoveOfTheAimLeavesThePositionBe()
    {
        var camera = Camera();
        var (position, aim) = (camera.Position, camera.Aim);
        var move = Move(CameraFlags.Sine | CameraFlags.Aim, Point);
        for (var tick = 0; tick < Duration; tick++)
        {
            move.Step(camera, Focus, Radius, tick);
            Assert.Equal(position, camera.Position);
        }
        AssertNear(Point, camera.Aim);
        Assert.NotEqual(aim, camera.Aim);
    }

    [Fact]
    public void AFinishedCutOrEaseStaysDoneWhileSteppedEveryTickAndStartsAgainAfterAGap()
    {
        foreach (var kind in new[] { CameraFlags.Cut, CameraFlags.Sine })
        {
            var camera = Camera();
            var move = Move(kind | CameraFlags.Position, Point);
            var tick = 0;
            for (; tick < Duration + 1; tick++)
                move.Step(camera, Focus, Radius, tick);
            AssertNear(Point, camera.Position);

            var elsewhere = new Vector3(-3, 2, 7);
            camera.Position = elsewhere;
            move.Step(camera, Focus, Radius, tick++);
            Assert.Equal(elsewhere, camera.Position);
            move.Step(camera, Focus, Radius, tick++);
            Assert.Equal(elsewhere, camera.Position);

            // A tick missed, and it starts afresh from where the camera is: a cut jumps, an ease sets off.
            tick++;
            move.Step(camera, Focus, Radius, tick);
            if (kind == CameraFlags.Cut)
                Assert.Equal(Point, camera.Position);
            else
                AssertNear(elsewhere + (Point - elsewhere) * ((1 - MathF.Cos(MathF.PI / Duration)) / 2), camera.Position);
        }
    }

    [Theory]
    [InlineData(false, 1.05f)]
    [InlineData(true, 0.5f)]
    public void WithoutAPointTheDestinationIsJustAboveTheFocus(bool conversation, float above)
    {
        var camera = Camera();
        var focus = new Vector3(4, 3, 8);
        var move = Move(CameraFlags.Cut | CameraFlags.Position);
        move.Step(camera, focus, Radius, 0, conversation);
        AssertNear(new Vector3(4, 3 - Radius + above, 8), camera.Position);
    }

    [Theory]
    [InlineData(1f, 3f)]
    [InlineData(3.25f, 3.25f)]
    public void ADistanceSetsHowFarBehindTheCameraFollowsButNoNearerThanThreeAndDoesNotHoldIt(float distance, float expected)
    {
        var camera = Camera();
        var move = Move(CameraFlags.Distance, distance: distance);
        move.Step(camera, Focus, Radius, 0);
        Assert.Equal(expected, camera.Distance);
        Assert.False(camera.Held);
    }

    [Fact]
    public void ADistanceLastsHalfASecondOutdoors()
    {
        var world = Worlds.Flat();
        var camera = Camera();
        Move(CameraFlags.Distance, distance: 8).Step(camera, Focus, Radius, 0);
        Assert.Equal(8, camera.Distance);
        for (var i = 0; i < 29; i++)
            camera.Step(world, Focus, Vector2.UnitX);
        Assert.Equal(8, camera.Distance);
        camera.Step(world, Focus, Vector2.UnitX);
        Assert.Equal(camera.DefaultDistance, camera.Distance);
    }

    [Fact]
    public void APitchSetsTheCamerasPitchAndDoesNotHoldIt()
    {
        var camera = Camera();
        var move = Move(CameraFlags.Pitch, pitch: 30);
        move.Step(camera, Focus, Radius, 0);
        Assert.Equal(30, camera.Pitch);
        Assert.False(camera.Held);
    }

    [Fact]
    public void TheFollowCameraLeavesTheCameraBeTheTickAfterAMoveHoldsIt()
    {
        var world = Worlds.Flat();
        var camera = Camera();
        var move = Move(CameraFlags.Cut | CameraFlags.Position, Point);
        move.Step(camera, Focus, Radius, 0);
        var (position, aim) = (camera.Position, camera.Aim);

        camera.Step(world, Focus + new Vector3(0, 0, 8), Vector2.UnitX);
        Assert.Equal(position, camera.Position);
        Assert.Equal(aim, camera.Aim);

        camera.Step(world, Focus + new Vector3(0, 0, 8), Vector2.UnitX);
        Assert.NotEqual(position, camera.Position);
    }
}
