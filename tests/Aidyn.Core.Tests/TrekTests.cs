using System.Numerics;
using Aidyn.Core.Trek;

namespace Aidyn.Core.Tests;

public class TrekTests
{
    private const int TicksPerSecond = 60;

    [Fact]
    public void TurnsTwentyDegreesAFrame()
    {
        var heading = new Vector2(0, 1);
        var ticks = 0;
        while (Vector2.Distance(heading, new Vector2(0, -1)) > 1e-4f)
        {
            heading = PlayerMotion.Turn(heading, new Vector2(0, -1), Cadence.PerTick(PlayerMotion.TurnPerFrame));
            ticks++;
        }
        // 180° at 10° a tick.
        Assert.Equal(18, ticks);
    }

    [Fact]
    public void WalksWhereTheCameraLooks()
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(1, 0));
        var camera = new TrekCamera(player.Sphere.Position, new Vector2(0, 1));
        var motion = new SteadyMotion(0.02f);
        for (var i = 0; i < 120; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            player.Step(world, new TrekInput(new Vector2(0, 1)), camera, motion);
        }
        Assert.Equal(PlayerMotion.Run, player.Request);
        Assert.True(player.Sphere.Position.Z > 2, $"Only got to {player.Sphere.Position}.");
        Assert.Equal(0.5f, player.Sphere.Position.Y, 0.01f);
        Assert.Equal(0, player.Heading.X, 0.05f);
    }

    [Theory]
    [InlineData(0.5f, PlayerMotion.Walk)]
    [InlineData(0.79f, PlayerMotion.Walk)]
    [InlineData(0.8f, PlayerMotion.Run)]
    [InlineData(0.04f, PlayerMotion.Idle)]
    public void TiltPicksTheGait(float tilt, int request)
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1));
        var camera = new TrekCamera(player.Sphere.Position, new Vector2(0, 1));
        player.Step(world, new TrekInput(new Vector2(0, tilt)), camera, new SteadyMotion(0));
        Assert.Equal(request, player.Request);
    }

    [Fact]
    public void FasterPaceMovesFurther()
    {
        var world = Worlds.Flat();
        float Distance(float pace)
        {
            var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1)) { Pace = pace };
            var camera = new TrekCamera(player.Sphere.Position, new Vector2(0, 1));
            for (var i = 0; i < 120; i++)
                player.Step(world, new TrekInput(new Vector2(0, 1)), camera, new SteadyMotion(0.02f));
            return player.Sphere.Position.Z;
        }
        Assert.Equal(Distance(1) * 1.5f, Distance(1.5f), 0.05f);
    }

    /// <summary>
    /// The user timed the original, standing still: from about 5° off facing the camera, 45 s to
    /// swing round 90°, then 70 s more to settle behind. That was at roughly 2.6 ticks a frame;
    /// matched to 30 fps it's slower, but the shape is the same: the second leg takes about half
    /// as long again as the first.
    /// </summary>
    [Fact]
    public void SwingsBehindAStandingPlayerSlowly()
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1));
        var headOn = 5 * MathF.PI / 180;
        var camera = new TrekCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(MathF.Sin(headOn), -MathF.Cos(headOn)));
        var motion = new SteadyMotion(0);
        double? quarter = null, settled = null;
        for (var tick = 0; tick < 300 * TicksPerSecond && settled is null; tick++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            player.Step(world, new TrekInput(Vector2.Zero), camera, motion);
            var off = OffBehind(camera);
            if (quarter is null && off <= 90)
                quarter = tick / (double)TicksPerSecond;
            if (off <= 2)
                settled = tick / (double)TicksPerSecond;
        }
        Assert.NotNull(quarter);
        Assert.NotNull(settled);
        Assert.InRange(quarter!.Value, 45, 75);
        Assert.InRange((settled!.Value - quarter.Value) / quarter.Value, 1.2, 1.9);

        // Degrees between the camera's side of the player and his back.
        static double OffBehind(TrekCamera c)
        {
            var side = Vector2.Normalize(new Vector2(c.Position.X - c.Aim.X, c.Position.Z - c.Aim.Z));
            return Math.Acos(Math.Clamp(Vector2.Dot(side, new Vector2(0, -1)), -1, 1)) * 180 / Math.PI;
        }
    }

    [Fact]
    public void SettlesSoonAfterThePlayerStops()
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1));
        var camera = new TrekCamera(player.Sphere.Position, new Vector2(0, 1));
        var motion = new SteadyMotion(0.06f);
        for (var i = 0; i < 3 * TicksPerSecond; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            player.Step(world, new TrekInput(new Vector2(1, 0)), camera, motion);
        }
        var aim = player.Sphere.Position + new Vector3(0, camera.Height, 0);
        var ticks = 0;
        for (; Vector3.Distance(camera.Aim, aim) > 0.01f; ticks++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            player.Step(world, new TrekInput(Vector2.Zero), camera, motion);
        }
        // The user saw 500–1000 ms.
        Assert.InRange(ticks, 30, 60);
    }

    [Fact]
    public void OrbitingStaysBetweenLevelAndFortyFiveDegrees()
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1));
        var camera = new TrekCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(0, 1));
        var motion = new SteadyMotion(0);
        void Hold(Vector2 stick, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                camera.Step(world, player.Sphere.Position, player.Heading);
                player.Step(world, new TrekInput(Vector2.Zero, Orbit: true, OrbitStick: stick), camera, motion);
            }
        }

        Hold(new Vector2(1, 1), 10 * TicksPerSecond);
        Assert.InRange(Elevation(camera), 44.5, 45.01);
        Hold(new Vector2(0, -1), 10 * TicksPerSecond);
        Assert.InRange(Elevation(camera), -0.01, 0.5);

        static double Elevation(TrekCamera c)
        {
            var flat = new Vector2(c.Position.X - c.Aim.X, c.Position.Z - c.Aim.Z).Length();
            return Math.Atan2(c.Position.Y - c.Aim.Y, flat) * 180 / Math.PI;
        }
    }

    /// <summary>
    /// Orbiting into a wall, the original pulls in an eighth of the way a frame toward a point
    /// 0.25 past the wall, while still easing out toward 5. The two settle about 3.1 away, so
    /// with the wall 2 away, the camera ends up behind it. The ROM has the -0.25.
    /// </summary>
    [Fact]
    public void OrbitingPullsInFromWalls()
    {
        var world = Worlds.Flat();
        // A wall at X = -2, facing the player.
        world.Add("wall", [
            Worlds.Triangle(new(-2, 0, 20), new(-2, 0, -20), new(-2, 5, 20)),
            Worlds.Triangle(new(-2, 5, 20), new(-2, 0, -20), new(-2, 5, -20)),
        ], Vector3.Zero);
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0.1f), new Vector2(1, 0));
        var camera = new TrekCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(1, 0));
        for (var i = 0; i < 5 * TicksPerSecond; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            player.Step(world, new TrekInput(Vector2.Zero, Orbit: true), camera, new SteadyMotion(0));
        }
        var reach = new Vector2(camera.Position.X - camera.Aim.X, camera.Position.Z - camera.Aim.Z).Length();
        Assert.Equal(3.1f, reach, 0.1f);
    }

    [Fact]
    public void GoalRateMatchesAThirtyFpsFrame()
    {
        // Standing still, the original nudges its goal 1/144 a tick.
        Assert.Equal(74.6f, TrekCamera.GoalRate(144), 0.1f);
        Assert.Equal(8.52f, TrekCamera.GoalRate(16), 0.01f);
    }
}
