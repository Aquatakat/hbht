using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.Movement;

namespace Aidyn.Core.Tests;

public class PlayerMotionTests
{
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
        var camera = new FollowCamera(player.Sphere.Position, new Vector2(0, 1));
        var motion = new SteadyMotion(0.02f);
        for (var i = 0; i < 120; i++)
        {
            camera.Step(world, player.Sphere.Position, player.Heading);
            player.Step(world, new PlayerInput(new Vector2(0, 1)), camera, motion);
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
        var camera = new FollowCamera(player.Sphere.Position, new Vector2(0, 1));
        player.Step(world, new PlayerInput(new Vector2(0, tilt)), camera, new SteadyMotion(0));
        Assert.Equal(request, player.Request);
    }

    [Fact]
    public void FasterPaceMovesFurther()
    {
        var world = Worlds.Flat();
        float Distance(float pace)
        {
            var player = new PlayerMotion(new Vector3(0, 0.5f, 0), new Vector2(0, 1)) { Pace = pace };
            var camera = new FollowCamera(player.Sphere.Position, new Vector2(0, 1));
            for (var i = 0; i < 120; i++)
                player.Step(world, new PlayerInput(new Vector2(0, 1)), camera, new SteadyMotion(0.02f));
            return player.Sphere.Position.Z;
        }
        Assert.Equal(Distance(1) * 1.5f, Distance(1.5f), 0.05f);
    }
}
