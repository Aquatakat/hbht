using System.Numerics;
using HBHT.Core.Physics;
using HBHT.Plugins.Assets;

namespace HBHT.Core.Tests;

public class SpherePhysicsTests
{
    private static CollisionSphere Sphere(Vector3 position) =>
        new() { Radius = 0.5f, Gravity = new Vector3(0, -0.003f, 0), Position = position };

    [Fact]
    public void RestsOnTheGround()
    {
        var world = Worlds.Flat();
        var s = Sphere(new Vector3(0, 0.5f, 0));
        for (var i = 0; i < 120; i++)
            SpherePhysics.Step(world, s);
        Assert.Equal(0.5f, s.Position.Y, 0.001f);
        Assert.True(s.Touching);
        Assert.Equal(Worlds.Grass, s.Surface);
        Assert.Equal(Vector3.Zero, s.Velocity);
    }

    [Fact]
    public void FallsAndLands()
    {
        var world = Worlds.Flat();
        var s = Sphere(new Vector3(0, 10, 0));
        for (var i = 0; i < 200; i++)
            SpherePhysics.Step(world, s);
        Assert.Equal(0.5f, s.Position.Y, 0.02f);
        Assert.True(s.Touching);
    }

    [Fact]
    public void WallsStopIt()
    {
        var world = Worlds.Flat();
        world.Add(Worlds.Chunk(1), Worlds.Wall(2, 3), Vector3.Zero);
        var s = Sphere(new Vector3(0, 0.5f, 0));
        for (var i = 0; i < 100; i++)
        {
            SpherePhysics.Step(world, s);
            s.Position.X += 0.06f;
        }
        SpherePhysics.Step(world, s);
        Assert.InRange(s.Position.X, 1.4f, 1.51f);
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.45f)]
    public void ClimbsLedgesLowerThanItsRadius(float height)
    {
        var world = Worlds.Ledge(1, height);
        var s = Sphere(new Vector3(0, 0.5f, 0));
        for (var i = 0; i < 60; i++)
        {
            SpherePhysics.Step(world, s);
            s.Position.X += 0.05f;
        }
        SpherePhysics.Step(world, s);
        Assert.True(s.Position.X > 2);
        Assert.Equal(height + 0.5f, s.Position.Y, 0.02f);
    }

    [Fact]
    public void HigherLedgesStopIt()
    {
        var world = Worlds.Ledge(1, 0.7f);
        var s = Sphere(new Vector3(0, 0.5f, 0));
        for (var i = 0; i < 60; i++)
        {
            SpherePhysics.Step(world, s);
            s.Position.X += 0.05f;
        }
        SpherePhysics.Step(world, s);
        Assert.InRange(s.Position.X, 0.4f, 0.51f);
    }

    [Fact]
    public void SweepsHitWallsFacingThem()
    {
        var world = Worlds.Flat();
        world.Add(Worlds.Chunk(1), Worlds.Wall(2, 3), Vector3.Zero);
        Assert.True(SpherePhysics.Sweep(world, new Vector3(0, 1, 0), new Vector3(4, 1, 0), 0.25f, true, out var point, out var normal));
        Assert.Equal(2, point.X, 0.001f);
        Assert.Equal(-Vector3.UnitX, normal);
        // From behind, the wall faces away.
        Assert.False(SpherePhysics.Sweep(world, new Vector3(4, 1, 0), new Vector3(0, 1, 0), 0.25f, true, out _, out _));
    }

    [Fact]
    public void CameraSweepsSkipSeeThroughWalls()
    {
        var world = Worlds.Flat();
        world.Add(Worlds.Chunk(1), Worlds.Wall(2, 3, flags: CollisionFlags.SeeThrough), Vector3.Zero);
        Assert.False(SpherePhysics.Sweep(world, new Vector3(0, 1, 0), new Vector3(4, 1, 0), 0.25f, true, out _, out _));
        Assert.True(SpherePhysics.Sweep(world, new Vector3(0, 1, 0), new Vector3(4, 1, 0), 0.25f, false, out _, out _));
    }

    /// <summary>
    /// <see cref="SpherePhysics.Blocked"/> looks only in the cells along a sweep, and answers as <see cref="SpherePhysics.Sweep"/>
    /// does over every cell of the box round it: on a clutter of triangles of every lean and size across four parts, for sweeps
    /// short and across the whole of it, along the axes, straight up and down, and standing still.
    /// </summary>
    [Fact]
    public void BlockedAnswersAsSweepDoes()
    {
        var random = new Random(17);
        float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector3 Point(float spread) => new(Next(-spread, spread), Next(-5, 15), Next(-spread, spread));
        var world = new CollisionWorld();
        for (var part = 0; part < 4; part++)
        {
            var triangles = new List<CollisionTriangle>();
            for (var i = 0; i < 150; i++)
            {
                var a = Point(80);
                var size = i % 10 == 0 ? 30 : 3;
                triangles.Add(Worlds.Triangle(a, a + new Vector3(Next(-size, size), Next(-size, size), Next(-size, size)),
                    a + new Vector3(Next(-size, size), Next(-size, size), Next(-size, size))));
            }
            world.Add(Worlds.Chunk(part), triangles, new Vector3(part * 20, 0, 0));
        }
        var hits = 0;
        for (var i = 0; i < 4000; i++)
        {
            var start = Point(100);
            var end = (i % 8) switch
            {
                0 => start with { X = start.X + Next(-200, 200) },
                1 => start with { Z = start.Z + Next(-200, 200) },
                2 => start with { Y = start.Y + Next(-20, 20) },
                3 => start,
                4 => start + new Vector3(Next(-3, 3), Next(-3, 3), Next(-3, 3)),
                _ => Point(200),
            };
            var radius = i % 3 == 0 ? 0.05f : Next(0.05f, 2);
            var swept = SpherePhysics.Sweep(world, start, end, radius, false, out _, out _);
            Assert.Equal(swept, SpherePhysics.Blocked(world, start, end, radius));
            hits += swept ? 1 : 0;
        }
        // Enough of both answers to mean something.
        Assert.InRange(hits, 400, 3600);
    }

    [Fact]
    public void PlainCollisionKeepsVelocity()
    {
        Assert.Equal(1, SpherePhysics.SurfaceKeep(1));
        Assert.Equal(1, SpherePhysics.SurfaceKeep(2));
        Assert.Equal(0, SpherePhysics.SurfaceKeep(Worlds.Grass));
    }

    [Fact]
    public void RemovedChunksStopColliding()
    {
        var world = Worlds.Flat();
        Assert.True(world.Remove(Worlds.Chunk(Worlds.OwnParts)));
        var s = Sphere(new Vector3(0, 0.5f, 0));
        for (var i = 0; i < 30; i++)
            SpherePhysics.Step(world, s);
        Assert.True(s.Position.Y < 0);
        Assert.False(s.Touching);
    }
}
