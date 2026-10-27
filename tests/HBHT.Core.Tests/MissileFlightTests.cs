using System.Numerics;
using HBHT.Core.Combat;
using HBHT.Plugins;

namespace HBHT.Core.Tests;

/// <summary>An arrow's, thrown weapon's or flask's flight (<c>Combat.StartCastCamera</c>, <c>Combat.StateCast</c>, <c>Combat.DrawCastScene</c>).</summary>
public class MissileFlightTests
{
    private static MissileFlight Flask(Vector3 from, Vector3 to) =>
        new(Battle.FlaskScene, from, to, Battle.FlaskMuzzle, 1, 0, Battle.FlaskSpin);

    [Theory]
    // A flask (muzzle 4) thrown 6 goes 0.13 a tick under an arc of 2.24; thrown 12, 0.16 under 3.08.
    [InlineData(6, 0.13f, 2.24f)]
    [InlineData(12, 0.16f, 3.08f)]
    public void AFlasksSpeedAndArcGoByItsLength(float length, float perTick, float arc)
    {
        var flight = Flask(Vector3.Zero, new Vector3(0, 0, length));
        Assert.Equal(perTick, flight.PerTick, 4);
        Assert.Equal(arc, flight.Arc, 4);
    }

    [Fact]
    public void ItPointsItsMinusZAlongItsLineWithItsYUp()
    {
        var flight = Flask(new Vector3(1, 2, 3), new Vector3(5, 2, 3));
        var r = flight.Rotation;
        AssertNear(new Vector3(1, 0, 0), Vector3.TransformNormal(-Vector3.UnitZ, r));
        AssertNear(Vector3.UnitY, Vector3.TransformNormal(Vector3.UnitY, r));
    }

    [Fact]
    public void AFlaskTumblesEndOverEndAboutItsSide()
    {
        var flight = Flask(Vector3.Zero, new Vector3(0, 0, -10));
        var side = Vector3.TransformNormal(Vector3.UnitX, flight.Rotation);
        for (var i = 0; i < 90 / 8 + 1; i++)
            flight.Tick();
        // 96 degrees on: the same side, and its top turned past the way it flies.
        Assert.Equal(96, flight.Turn, 3);
        AssertNear(side, Vector3.TransformNormal(Vector3.UnitX, flight.Rotation));
        var top = Vector3.TransformNormal(Vector3.UnitY, flight.Rotation);
        Assert.Equal(0, top.X, 4);
        Assert.True(MathF.Abs(top.Y) < 0.2f);
    }

    [Fact]
    public void ItsLineRunsIn3D()
    {
        var flight = Flask(new Vector3(0, 2, 0), new Vector3(3, 0, 4));
        Assert.Equal(MathF.Sqrt(29), flight.Length, 4);
        AssertNear(Vector3.Normalize(new Vector3(3, -2, 4)), Vector3.TransformNormal(-Vector3.UnitZ, flight.Rotation));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");
}
