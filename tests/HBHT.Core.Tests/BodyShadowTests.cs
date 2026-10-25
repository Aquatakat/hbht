using System.Numerics;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Plugins.Assets;

namespace HBHT.Core.Tests;

public class BodyShadowTests
{
    [Fact]
    public void LiesFullSizeOnTheGroundUnderAStandingBody()
    {
        var shadow = new BodyShadow();
        Assert.True(shadow.Place(Worlds.Flat(), new Vector3(3, 0.5f, 4), 0.5f, 0x80));
        Assert.Equal(new Vector3(3, 0, 4), shadow.Position);
        Assert.Equal(Vector3.UnitY, shadow.Normal);
        Assert.Equal(1, shadow.Size);
        Assert.Equal(0.7071f, shadow.HalfWidth, 0.0001f);
        // 160 × 128 / 255, truncated.
        Assert.Equal(80, shadow.Alpha);
    }

    [Fact]
    public void ShrinksAndFadesAsTheBodyRises()
    {
        var shadow = new BodyShadow();
        // 1.5 above the ground is 1 more than standing: two thirds of full size.
        Assert.True(shadow.Place(Worlds.Flat(), new Vector3(3, 1.5f, 4), 0.5f, 0x80));
        Assert.Equal(0, shadow.Position.Y);
        Assert.Equal(2f / 3, shadow.Size, 0.0001f);
        Assert.Equal(53, shadow.Alpha);
    }

    [Fact]
    public void KeepsItsLastPlacementWithNoGroundInReach()
    {
        var world = Worlds.Flat();
        var shadow = new BodyShadow();
        shadow.Place(world, new Vector3(3, 0.5f, 4), 0.5f, 0x80);
        Assert.False(shadow.Place(world, new Vector3(5, 10, 6), 0.5f, 0x40));
        Assert.Equal(new Vector3(5, 0, 6), shadow.Position);
        Assert.Equal((1f, (byte)80), (shadow.Size, shadow.Alpha));
    }

    [Fact]
    public void LooksThroughSeeThroughTriangles()
    {
        var world = Worlds.Flat();
        world.Add(Worlds.Chunk(0), Worlds.Floor(1, 0, 10, 0, 10).Select(t => t with { Flags = CollisionFlags.SeeThrough }), Vector3.Zero);
        var shadow = new BodyShadow();
        Assert.True(shadow.Place(world, new Vector3(3, 1.5f, 4), 0.5f, 0x80));
        Assert.Equal(0, shadow.Position.Y);
    }

    [Fact]
    public void StaysOnTheGroundBesideALeaningWall()
    {
        // A bank facing −X, leaning back 0.4 in X for each unit up, just beyond his reach, and
        // listed before the ground, so the first triangle the sweep touches is the bank.
        static Vector3 Bank(float y, float z) => new(0.55f + 0.4f * y, y, z);
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(1), [
            Worlds.Triangle(Bank(-3, -10), Bank(-3, 10), Bank(3, -10)),
            Worlds.Triangle(Bank(3, -10), Bank(-3, 10), Bank(3, 10)),
        ], Vector3.Zero);
        world.Add(Worlds.Chunk(2), Worlds.Floor(0, -20, 0.55f, -20, 20), Vector3.Zero);

        var centre = new Vector3(0, 0.5f, 0);
        var from = centre + new Vector3(0, 0.05f, 0);
        Assert.True(SpherePhysics.Sweep(world, from, from - new Vector3(0, 3, 0), 0.5f, true, out _, out var first));
        Assert.True(first.X < 0, "the original's sweep lands on the bank");

        var shadow = new BodyShadow();
        Assert.True(shadow.Place(world, centre, 0.5f, 0x80));
        Assert.Equal(Vector3.UnitY, shadow.Normal);
        Assert.Equal(0, shadow.Position.Y, 0.0001f);
    }

    [Fact]
    public void StaysOnTheGroundWhenPressedAgainstAWall()
    {
        // He's walked into a leaning wall, which touches his sphere just as the ground does.
        static Vector3 Bank(float y, float z) => new(0.5f / 0.928f + 0.4f * y, y, z);
        var world = Worlds.Flat();
        world.Add(Worlds.Chunk(1), [
            Worlds.Triangle(Bank(-3, -10), Bank(-3, 10), Bank(3, -10)),
            Worlds.Triangle(Bank(3, -10), Bank(-3, 10), Bank(3, 10)),
        ], Vector3.Zero);
        var shadow = new BodyShadow();
        Assert.True(shadow.Place(world, new Vector3(0, 0.5f, 0), 0.5f, 0x80));
        Assert.Equal(Vector3.UnitY, shadow.Normal);
    }

    [Fact]
    public void TurnsToASlope()
    {
        // Ground rising along +X at 45°.
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(3), [
            Worlds.Triangle(new(-10, -10, -10), new(-10, -10, 10), new(10, 10, -10)),
            Worlds.Triangle(new(10, 10, -10), new(-10, -10, 10), new(10, 10, 10)),
        ], Vector3.Zero);
        var shadow = new BodyShadow();
        Assert.True(shadow.Place(world, new Vector3(0, 0.8f, 0), 0.5f, 0x80));
        var up = Vector3.Normalize(new Vector3(-1, 1, 0));
        Assert.Equal(up.X, shadow.Normal.X, 0.0001f);
        Assert.Equal(up.Y, shadow.Normal.Y, 0.0001f);

        var (s, t) = shadow.Axes();
        Assert.Equal(0, Vector3.Dot(s, shadow.Normal), 0.0001f);
        Assert.Equal(0, Vector3.Dot(t, shadow.Normal), 0.0001f);
        Assert.Equal(0, Vector3.Dot(s, t), 0.0001f);
    }

    [Fact]
    public void OnLevelGroundTheTextureRunsAlongZAndMinusX()
    {
        var shadow = new BodyShadow { Normal = Vector3.UnitY };
        var (s, t) = shadow.Axes();
        Assert.Equal(1, s.Z, 0.0001f);
        Assert.Equal(-1, t.X, 0.0001f);
    }

    /// <remarks>
    /// Over the last 2 units the fade starts from 255, not the 128 it's drawn with nearer, so a
    /// shadow darkens before it fades. The original does this too.
    /// </remarks>
    [Theory]
    [InlineData(1, true, 0x80)]
    [InlineData(18.5f, true, 191)]
    [InlineData(20, true, null)]
    [InlineData(4, false, 76)]
    [InlineData(10, false, null)]
    public void FadesWithDistanceFromTheAim(float fromAim, bool player, int? expected)
    {
        Assert.Equal((byte?)expected, BodyShadow.DrawAlpha(BodyShadow.TrekDistance, fromAim, player));
    }

    [Fact]
    public void ADyingBodysShadowFadesWithIt() =>
        Assert.Equal((byte?)0x30, BodyShadow.DrawAlpha(BodyShadow.TrekDistance, 1, true, 0x30));

    /// <remarks>Bodies fade by the same 2 units as their shadows, from full instead of 128.</remarks>
    [Theory]
    [InlineData(1, 0xFF)]
    [InlineData(17.9f, 0xFF)]
    [InlineData(18.5f, 191)]
    [InlineData(19, 127)]
    [InlineData(19.99f, 1)]
    [InlineData(20, null)]
    [InlineData(25, null)]
    public void BodiesFadeOverTheLastTwoUnits(float fromAim, int? expected) =>
        Assert.Equal((byte?)expected, BodyShadow.BodyAlpha(BodyShadow.TrekDistance, fromAim));

    [Fact]
    public void ABodyAndItsShadowFadeTogetherInTheLastTwoUnits()
    {
        for (var d = 18.01f; d < 20; d += 0.07f)
            Assert.Equal(BodyShadow.BodyAlpha(BodyShadow.TrekDistance, d), BodyShadow.DrawAlpha(BodyShadow.TrekDistance, d, player: true));
    }

    [Fact]
    public void ADyingBodyFadesAtMostToItsOwnAlpha() =>
        Assert.Equal((byte?)0x30, BodyShadow.BodyAlpha(BodyShadow.TrekDistance, 1, 0x30));

    [Fact]
    public void TheAimIsMeasuredInThePlayersChunkWhenHeStandsInOne()
    {
        var aim = new Vector3(110, 5, 230);
        Assert.Equal(new Vector3(10, 5, 30), BodyShadow.ChunkAim(aim, new Vector3(100, 0, 200)));
        Assert.Equal(aim, BodyShadow.ChunkAim(aim, null));
    }

    /// <remarks>
    /// In the player's chunk the measure is the true distance, and in the next chunk along, 100 units off, it
    /// is off by that much, so the body's shadow (within 10 of the aim) doesn't show.
    /// </remarks>
    [Fact]
    public void ABodyInAnotherChunkIsMeasuredFromItsOwnChunksCoordinates()
    {
        var playerChunk = new Vector3(0, 0, 0);
        var neighbour = new Vector3(100, 0, 0);
        var aim = new Vector3(95, 0, 50);
        var own = BodyShadow.ChunkAim(aim, playerChunk);
        var position = new Vector3(103, 0, 50);

        Assert.Equal(8, Vector3.Distance(position, aim));
        Assert.Equal(8, BodyShadow.OwnDistance(position, playerChunk, own), 0.0001f);
        Assert.Equal(92, BodyShadow.OwnDistance(position, neighbour, own), 0.0001f);
        Assert.NotNull(BodyShadow.DrawAlpha(BodyShadow.TrekDistance, 8, player: false, ownAim: BodyShadow.OwnDistance(position, playerChunk, own)));
        Assert.Null(BodyShadow.DrawAlpha(BodyShadow.TrekDistance, 8, player: false, ownAim: BodyShadow.OwnDistance(position, neighbour, own)));
    }
}
