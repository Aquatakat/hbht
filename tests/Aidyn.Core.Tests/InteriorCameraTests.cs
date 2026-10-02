using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.Physics;

namespace Aidyn.Core.Tests;

/// <summary>The interiors' camera (<c>Camera::Mode1</c>) and the camera's hold and arrival, against synthetic worlds.</summary>
public class InteriorCameraTests
{
    private static readonly Vector3 Player = new(0, 0.5f, 0);
    private static readonly Vector3 AboveHim = new(0, 1.05f, 0);

    /// <summary>An interior camera at rest: aiming above the player, 3 behind him along -X and 1.5 above the aim.</summary>
    private static FollowCamera Resting()
    {
        var camera = new FollowCamera(Player + AboveHim, Vector2.UnitX) { Mode = CameraMode.Interior };
        camera.Position = camera.PositionGoal = new Vector3(-3, Player.Y + 1.05f + 1.5f, 0);
        return camera;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private static float BearingDegrees(FollowCamera camera) =>
        MathF.Atan2(camera.Position.Z - camera.Aim.Z, camera.Position.X - camera.Aim.X) * 180 / MathF.PI;

    [Theory]
    [InlineData(10, 0.25f)]
    [InlineData(2, 0.125f)]
    public void TheAimMovesASixteenthOfTheGapAtMostAQuarter(float gap, float expected)
    {
        var camera = Resting();
        camera.Step(Worlds.Flat(), Player + new Vector3(0, 0, gap), Vector2.UnitX);
        Assert.Equal(Player.Y + 1.05f, camera.Aim.Y, 1e-5f);
        Assert.Equal(expected, camera.Aim.Z, 1e-5f);
        Assert.Equal(gap, camera.AimGoal.Z, 1e-5f);
    }

    [Theory]
    [InlineData(0.64f, 0.02f)]
    [InlineData(10f, 0.1f)]
    public void ThePositionRisesToItsGoalAThirtySecondOfTheGapAtMostATenth(float gap, float expected)
    {
        var camera = Resting();
        var goalY = camera.AimGoal.Y + 1.5f;
        camera.Position.Y = camera.PositionGoal.Y = goalY - gap;
        camera.Step(Worlds.Flat(), Player, Vector2.UnitX);
        Assert.Equal(goalY, camera.PositionGoal.Y, 1e-5f);
        Assert.Equal(goalY - gap + expected, camera.Position.Y, 1e-5f);
    }

    [Fact]
    public void TheGoalIsDraggedToStayFourFromAPlayerWalkingAwayAndNoNearerThanTwo()
    {
        var world = Worlds.Flat();
        var camera = Resting();
        var player = Player;
        for (var tick = 0; tick < 200; tick++)
        {
            // Walking +X, away from the camera, 3 units a second.
            player.X += 0.05f;
            camera.Step(world, player, Vector2.UnitX);
            Assert.InRange(Flat(camera.PositionGoal, camera.AimGoal), 2 - 1e-3f, 4 + 1e-3f);
        }
        Assert.Equal(4, Flat(camera.PositionGoal, camera.AimGoal), 1e-3f);
        Assert.Equal(player.X - 4, camera.PositionGoal.X, 1e-3f);

        // And back toward it, past where it stands: the goal is pushed on ahead to stay 2 away.
        for (var tick = 0; tick < 200; tick++)
        {
            player.X -= 0.05f;
            camera.Step(world, player, -Vector2.UnitX);
            Assert.InRange(Flat(camera.PositionGoal, camera.AimGoal), 2 - 1e-3f, 4 + 1e-3f);
            Assert.True(Flat(camera.Position, camera.Aim) >= 2 - 1e-3f);
        }
        Assert.Equal(2, Flat(camera.PositionGoal, camera.AimGoal), 1e-3f);
        Assert.Equal(player.X - 2, camera.PositionGoal.X, 1e-3f);
    }

    [Fact]
    public void ItIsNeverNearerTheAimThanTwoInXAndZ()
    {
        var world = Worlds.Flat();
        var camera = Resting();
        // Straight at the camera and on past it, fast.
        var player = Player;
        for (var tick = 0; tick < 300; tick++)
        {
            player.X -= 0.08f;
            camera.Step(world, player, -Vector2.UnitX);
            Assert.True(Flat(camera.Position, camera.Aim) >= 2 - 1e-3f, $"tick {tick}: {Flat(camera.Position, camera.Aim)}");
        }
    }

    /// <summary>
    /// A wall at X = 10 that faces -X, with the player 2 short of it and the camera between them. It has
    /// to keep 2 from the aim, which puts it on the wall, so it stops a sphere's radius short; then it
    /// finds the aim out of sight from its goal, which jumps behind the player, and it swings round.
    /// </summary>
    [Fact]
    public void ItStopsAQuarterOffAWallAndNeverPassesIt()
    {
        var world = Worlds.Flat();
        world.Add("wall", Worlds.Wall(10, 5), Vector3.Zero);
        var player = new Vector3(8, 0.5f, 0);
        var camera = new FollowCamera(player + AboveHim, Vector2.UnitX) { Mode = CameraMode.Interior };
        camera.Position = camera.PositionGoal = new Vector3(9, player.Y + 1.05f + 1.5f, 0);

        camera.Step(world, player, Vector2.UnitX);
        Assert.Equal(10 - 0.25f, camera.Position.X, 1e-3f);

        for (var tick = 1; tick < 400; tick++)
        {
            camera.Step(world, player, Vector2.UnitX);
            Assert.True(camera.Position.X <= 10 - 0.25f + 1e-4f, $"tick {tick}: X = {camera.Position.X}");
        }
        Assert.Equal(player.X - 3, camera.PositionGoal.X, 1e-3f);
    }

    /// <summary>The goal here is 0.1 off along the circle, under a degree's turn, so a degree is the limit.</summary>
    [Fact]
    public void ItTurnsRoundTheAimAtMostADegreeATickKeepingItsDistance()
    {
        var world = Worlds.Flat();
        var camera = Resting();
        // 3 along +X from the aim, its goal 0.1 round the circle: about 1.9°, of which it can turn 1°.
        camera.Position = camera.PositionGoal = camera.Aim + new Vector3(3, 1.5f, 0);
        camera.PositionGoal.Z += 0.1f;
        var before = BearingDegrees(camera);
        camera.Step(world, Player, Vector2.UnitX);
        Assert.Equal(1.06f, MathF.Abs(BearingDegrees(camera) - before), 0.1f);
        Assert.Equal(3, Flat(camera.Position, camera.Aim), 1e-3f);

        // A goal a quarter of the way round: it still turns about 1° a tick, plus what its approach adds.
        camera.Position = camera.Aim + new Vector3(3, 1.5f, 0);
        camera.PositionGoal = camera.Aim + new Vector3(0, 1.5f, 3);
        before = BearingDegrees(camera);
        camera.Step(world, Player, Vector2.UnitX);
        Assert.InRange(MathF.Abs(BearingDegrees(camera) - before), 1, 2.7f);
        Assert.InRange(Flat(camera.Position, camera.Aim), 2.9f, 3);
    }

    /// <summary>
    /// A wall at X = 3 between the aim and a goal 4 away. With the camera caught up to it and no way to see
    /// the aim from there, the goal jumps 3 behind the player, which here is the way he's facing, away.
    /// </summary>
    [Fact]
    public void ItJumpsItsGoalBehindThePlayerWhenTheAimIsOutOfSightAndNotAgainForFiveSeconds()
    {
        var world = Worlds.Flat();
        world.Add("wall", Worlds.Wall(3, 5), Vector3.Zero);
        var camera = Resting();
        var heading = Vector2.UnitX;

        // The camera has caught up with its goal, beyond the wall. Each call puts it back there.
        void Stage()
        {
            camera.Aim = camera.AimGoal = Player + AboveHim;
            camera.PositionGoal = new Vector3(4, camera.AimGoal.Y + 1.5f, 0);
            camera.Position = new Vector3(3.5f, camera.AimGoal.Y + 1.5f, 0);
        }

        Stage();
        camera.Step(world, Player, heading);
        Assert.Equal(-3, camera.PositionGoal.X, 1e-4f);
        Assert.Equal(0, camera.PositionGoal.Z, 1e-4f);
        Assert.Equal(camera.AimGoal.Y + 1.5f, camera.PositionGoal.Y, 1e-4f);

        // The same again on the next tick: it waits, and keeps the goal the leash gave it.
        Stage();
        camera.Step(world, Player, heading);
        Assert.Equal(4, camera.PositionGoal.X, 1e-4f);

        var waited = 1;
        while (true)
        {
            Stage();
            camera.Step(world, Player, heading);
            if (camera.PositionGoal.X < 0)
                break;
            Assert.InRange(++waited, 1, 400);
        }
        // It may jump again on the 300th tick after the first jump.
        Assert.Equal(299, waited);
    }

    [Fact]
    public void FollowingPutsThePitchBackOutdoorsAndZeroesItInInteriors()
    {
        var outdoors = Resting();
        outdoors.Mode = CameraMode.Outdoor;
        outdoors.Pitch = 30;
        outdoors.Follow();
        Assert.Equal(outdoors.DefaultPitch, outdoors.Pitch);

        var inside = Resting();
        inside.Pitch = 30;
        inside.Follow();
        Assert.Equal(0, inside.Pitch);
    }

    [Theory]
    [InlineData(CameraMode.Outdoor)]
    [InlineData(CameraMode.Interior)]
    public void HoldingLeavesTheCameraBeForOneStepThenItMovesAgain(CameraMode mode)
    {
        var world = Worlds.Flat();
        var camera = Resting();
        camera.Mode = mode;
        var player = Player + new Vector3(0, 0, 8);
        var (position, aim) = (camera.Position, camera.Aim);

        camera.Hold();
        Assert.True(camera.Held);
        camera.Step(world, player, Vector2.UnitX);
        Assert.False(camera.Held);
        Assert.Equal(position, camera.Position);
        Assert.Equal(aim, camera.Aim);

        camera.Step(world, player, Vector2.UnitX);
        Assert.NotEqual(aim, camera.Aim);
    }

    [Fact]
    public void ArrivingAtAPointPlacesTheCameraThereAndMovesTheAimWithThePlayer()
    {
        var camera = Resting();
        camera.Hold();
        camera.Pitch = 30;
        camera.OrbitAngle = 40;
        var from = new Vector3(5, 0, 5);
        var to = new Vector3(105, 0, 205);
        var (aim, aimGoal) = (camera.Aim, camera.AimGoal);
        var point = new Vector3(110, 3, 200);

        camera.Arrive(from, to, new Vector2(0, 1), CameraMode.Outdoor, point);

        Assert.Equal(CameraMode.Outdoor, camera.Mode);
        Assert.False(camera.Held);
        Assert.Equal(0, camera.OrbitAngle);
        Assert.Equal(camera.DefaultPitch, camera.Pitch);
        Assert.Equal(5, camera.Snap);
        Assert.Equal(point, camera.Position);
        Assert.Equal(point, camera.PositionGoal);
        Assert.Equal(aim + (to - from), camera.Aim);
        Assert.Equal(aimGoal + (to - from), camera.AimGoal);
    }

    [Fact]
    public void ArrivingInAnInteriorWithoutAPointPutsTheCameraInFrontOfThePlayer()
    {
        var camera = Resting();
        var to = new Vector3(20, 2, 30);
        var heading = new Vector2(0.6f, -0.8f);

        camera.Arrive(new Vector3(1, 0, 1), to, heading, CameraMode.Interior, null);

        Assert.Equal(CameraMode.Interior, camera.Mode);
        var expected = new Vector3(to.X + 0.6f * 4, to.Y + 3, to.Z - 0.8f * 4);
        Assert.Equal(expected, camera.Position);
        Assert.Equal(expected, camera.PositionGoal);
        Assert.Equal(to + AboveHim, camera.Aim);
        Assert.Equal(to + AboveHim, camera.AimGoal);
        Assert.Equal(5, camera.Snap);
    }

    [Fact]
    public void ArrivingOutdoorsWithoutAPointPutsTheCameraBehindThePlayer()
    {
        var camera = Resting();
        camera.Mode = CameraMode.Interior;
        camera.Pitch = 30;
        var to = new Vector3(20, 2, 30);
        var heading = Vector2.Normalize(new Vector2(3, -4));

        camera.Arrive(new Vector3(1, 0, 1), to, heading, CameraMode.Outdoor, null);

        Assert.Equal(CameraMode.Outdoor, camera.Mode);
        Assert.Equal(to + AboveHim, camera.Aim);
        Assert.Equal(heading.X, camera.LookXZ.X, 1e-5f);
        Assert.Equal(heading.Y, camera.LookXZ.Y, 1e-5f);
        var rise = MathF.Sin(camera.DefaultPitch * MathF.PI / 180) * camera.Distance;
        var expected = camera.Aim - new Vector3(heading.X, 0, heading.Y) * camera.Distance + new Vector3(0, rise, 0);
        Assert.Equal(expected.X, camera.Position.X, 1e-4f);
        Assert.Equal(expected.Y, camera.Position.Y, 1e-4f);
        Assert.Equal(expected.Z, camera.Position.Z, 1e-4f);
        Assert.Equal(camera.Position, camera.PositionGoal);
        Assert.Equal(camera.Aim, camera.AimGoal);
    }

    [Fact]
    public void OrbitingAnInteriorEasesToThreeAwayAndKeepsBetweenHalfAndTwoAndAHalfAboveTheAim()
    {
        var world = Worlds.Flat();
        var camera = Resting();
        camera.Position = camera.PositionGoal = camera.Aim + new Vector3(-6, 1.5f, 0);

        void Hold(Vector2 stick, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                camera.Orbit(Player, stick);
                camera.Step(world, Player, Vector2.UnitX);
                var above = camera.Position.Y - camera.Aim.Y;
                Assert.InRange(above, 0.5f - 1e-3f, 2.5f + 1e-3f);
            }
        }

        Hold(new Vector2(0, 1), 10 * 60);
        Assert.Equal(3, Flat(camera.Position, camera.Aim), 0.05f);
        Assert.Equal(2.5f, camera.Position.Y - camera.Aim.Y, 1e-3f);
        Hold(new Vector2(0, -1), 10 * 60);
        Assert.Equal(0.5f, camera.Position.Y - camera.Aim.Y, 1e-3f);
    }
}
