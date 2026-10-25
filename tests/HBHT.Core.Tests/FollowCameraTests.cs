using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Movement;
using HBHT.Core.Trek;

namespace HBHT.Core.Tests;

public class FollowCameraTests
{
    private const int TicksPerSecond = 60;

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
        var camera = new FollowCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(MathF.Sin(headOn), -MathF.Cos(headOn)));
        var motion = new SteadyMotion(0);
        double? quarter = null, settled = null;
        for (var tick = 0; tick < 300 * TicksPerSecond && settled is null; tick++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            TrekLoop.StepPlayer(world, player, new PlayerInput(Vector2.Zero), camera, motion);
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
        static double OffBehind(FollowCamera c)
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
        var camera = new FollowCamera(player.Sphere.Position, new Vector2(0, 1));
        var motion = new SteadyMotion(0.06f);
        for (var i = 0; i < 3 * TicksPerSecond; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            TrekLoop.StepPlayer(world, player, new PlayerInput(new Vector2(1, 0)), camera, motion);
        }
        var aim = player.Sphere.Position + new Vector3(0, camera.Height, 0);
        var ticks = 0;
        for (; Vector3.Distance(camera.Aim, aim) > 0.01f; ticks++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            TrekLoop.StepPlayer(world, player, new PlayerInput(Vector2.Zero), camera, motion);
        }
        // The user saw 500–1000 ms.
        Assert.InRange(ticks, 30, 60);
    }

    [Fact]
    public void OrbitingStaysBetweenLevelAndFortyFiveDegrees()
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1));
        var camera = new FollowCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(0, 1));
        var motion = new SteadyMotion(0);
        void Hold(Vector2 stick, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                camera.Step(world, player.Sphere.Position, player.Heading);
                TrekLoop.StepPlayer(world, player, new PlayerInput(Vector2.Zero, Orbit: true, OrbitStick: stick), camera, motion);
            }
        }

        Hold(new Vector2(1, 1), 10 * TicksPerSecond);
        Assert.InRange(Elevation(camera), 44.5, 45.01);
        Hold(new Vector2(0, -1), 10 * TicksPerSecond);
        Assert.InRange(Elevation(camera), -0.01, 0.5);

        static double Elevation(FollowCamera c)
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
        world.Add(Worlds.Chunk(0), [
            Worlds.Triangle(new(-2, 0, 20), new(-2, 0, -20), new(-2, 5, 20)),
            Worlds.Triangle(new(-2, 5, 20), new(-2, 0, -20), new(-2, 5, -20)),
        ], Vector3.Zero);
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0.1f), new Vector2(1, 0));
        var camera = new FollowCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(1, 0));
        for (var i = 0; i < 5 * TicksPerSecond; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            TrekLoop.StepPlayer(world, player, new PlayerInput(Vector2.Zero, Orbit: true), camera, new SteadyMotion(0));
        }
        var reach = new Vector2(camera.Position.X - camera.Aim.X, camera.Position.Z - camera.Aim.Z).Length();
        Assert.Equal(3.1f, reach, 0.1f);
    }

    /// <summary>
    /// With a second stick (<see cref="PlayerInput.Free"/>), the orbit and the walk go together: he moves, and the
    /// camera goes with him, still looking the way it was turned rather than swinging about where it started. In
    /// the original, orbiting holds him still.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrbitingWhileWalking(bool free)
    {
        var world = Worlds.Flat();
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1));
        var camera = new FollowCamera(player.Sphere.Position + new Vector3(0, 1.05f, 0), new Vector2(0, 1));
        var motion = new SteadyMotion(0.06f);
        var look = camera.LookXZ;
        for (var i = 0; i < 5 * TicksPerSecond; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            TrekLoop.StepPlayer(world, player, new PlayerInput(new Vector2(1, 0), Orbit: true, Free: free), camera, motion);
        }
        if (!free)
        {
            Assert.Equal(new Vector3(0, 0.5f, 0), player.Sphere.Position);
            return;
        }
        Assert.True(new Vector2(player.Sphere.Position.X, player.Sphere.Position.Z).Length() > 3);
        Assert.True(Vector2.Dot(camera.LookXZ, look) > 0.95f);
        var reach = new Vector2(camera.Position.X - camera.Aim.X, camera.Position.Z - camera.Aim.Z).Length();
        Assert.InRange(reach, 4, 5.5f);
    }

    [Fact]
    public void GoalRateMatchesAThirtyFpsFrame()
    {
        // Standing still, the original nudges its goal 1/144 a tick.
        Assert.Equal(74.6f, FollowCamera.GoalRate(144), 0.1f);
        Assert.Equal(8.52f, FollowCamera.GoalRate(16), 0.01f);
    }
}
