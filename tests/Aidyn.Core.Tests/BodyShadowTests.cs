using System.Numerics;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Plugins.Assets;

namespace Aidyn.Core.Tests;

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
        world.Add("glass", Worlds.Floor(1, 0, 10, 0, 10).Select(t => t with { Flags = CollisionFlags.SeeThrough }), Vector3.Zero);
        var shadow = new BodyShadow();
        Assert.True(shadow.Place(world, new Vector3(3, 1.5f, 4), 0.5f, 0x80));
        Assert.Equal(0, shadow.Position.Y);
    }

    [Fact]
    public void TurnsToASlope()
    {
        // Ground rising along +X at 45°.
        var world = new CollisionWorld();
        world.Add("slope", [
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
}
