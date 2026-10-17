using System.Numerics;
using Aidyn.Core.Scenes;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class SceneObjectFadeTests
{
    // Looking along +X from 10 short of the player, at the origin.
    private static readonly SceneView View = new(Vector3.Zero, new Vector2(-10, 0), Vector2.UnitX, 10, ViewCone.Original);

    [Theory]
    [InlineData(0, 255)]
    [InlineData(13.9f, 255)]
    [InlineData(14.5f, 127)]
    [InlineData(15, 0)]
    [InlineData(15.1f, 0)]
    public void ALevelFadesOutOverItsLastUnit(float distance, int alpha) =>
        Assert.Equal(alpha, SceneObjectFade.LevelAlpha(distance, 0, 15));

    [Theory]
    [InlineData(12.9f, 0)]
    [InlineData(13, 0)]
    [InlineData(13.5f, 127)]
    [InlineData(14, 255)]
    [InlineData(39.5f, 127)]
    [InlineData(40.1f, 0)]
    public void TheNextLevelFadesInBeforeThePreviousFadesOut(float distance, int alpha) =>
        Assert.Equal(alpha, SceneObjectFade.LevelAlpha(distance, 15, 40));

    [Fact]
    public void ATreeCrossesToItsFarLevelAndFadesOut()
    {
        // The common tree around Gwernia: 0x07E1 to 15, then 0x07E3 to 40, radius 40.
        var tree = Tree(new Vector3(0, 0, 0), SceneObjectFlags.Solid);
        Assert.Equal([255, 0], At(tree, 5));
        Assert.Equal([255, 127], At(tree, 13.5f));
        Assert.Equal([127, 255], At(tree, 14.5f));
        Assert.Equal([0, 255], At(tree, 20));
        Assert.Equal([0, 127], At(tree, 39.5f));
        Assert.Equal([0, 0], At(tree, 40));
    }

    [Fact]
    public void AChestIsDrawnToItsModelsDistanceFromThePlayerNotItsRadius()
    {
        // A chest's object: radius 30, and its closed model drawn to 10.
        var chest = new SceneObject(0, new Vector3(9.5f, 0, 0), 30, PakVariant.Any, Always,
            [new(RecordId.Hex(RecordTypes.Scene, 0x907), 10), new(RecordId.Hex(RecordTypes.Scene, 0x909), 0), new(RecordId.Hex(RecordTypes.Scene, 0x905), 0)],
            1, Vector3.Zero, Vector3.One, default, SceneObjectFlags.Solid);
        Span<byte> alphas = stackalloc byte[1];
        SceneObjectFade.Alphas(chest, Vector3.Zero, View, alphas);
        Assert.Equal(127, alphas[0]);
    }

    [Fact]
    public void NothingIsDrawnFromItsRadiusOut()
    {
        var o = Tree(Vector3.Zero, SceneObjectFlags.Solid) with { Radius = 20 };
        Assert.Equal([0, 255], At(o, 19.9f));
        Assert.Equal([0, 0], At(o, 20));
    }

    [Theory]
    [InlineData(37, 255)]
    [InlineData(40, 130)]
    [InlineData(43, 0)]
    public void FadesOutToTheSideOfTheLook(float degrees, int alpha)
    {
        var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180);
        var position = View.Camera + new Vector2(cos, sin) * 20;
        Assert.Equal(alpha, SceneObjectFade.ViewAlpha(position, 5, 40, SceneObjectFlags.Solid, View));
    }

    [Fact]
    public void DrawnWhenNearIgnoresTheLookWithinHalfItsRadius()
    {
        var behind = View.Camera - new Vector2(5, 0);
        Assert.Equal(255, SceneObjectFade.ViewAlpha(behind, 15, 40, SceneObjectFlags.Solid | SceneObjectFlags.DrawWhenNear, View));
        Assert.Equal(0, SceneObjectFade.ViewAlpha(behind, 25, 40, SceneObjectFlags.Solid | SceneObjectFlags.DrawWhenNear, View));
    }

    [Fact]
    public void AnObjectBetweenTheCameraAndThePlayerFadesUnlessSolid()
    {
        // Halfway to the player, the cone is 0.35 × 5 wide.
        var between = new Vector2(-5, 0);
        Assert.Equal(SceneObjectFade.BetweenAlpha, SceneObjectFade.ViewAlpha(between, 5, 40, SceneObjectFlags.None, View));
        Assert.Equal(255, SceneObjectFade.ViewAlpha(between, 5, 40, SceneObjectFlags.Solid, View));
        Assert.Equal(177, SceneObjectFade.ViewAlpha(between + new Vector2(0, 0.875f), 5, 40, SceneObjectFlags.None, View));
        Assert.Equal(255, SceneObjectFade.ViewAlpha(between + new Vector2(0, 1.75f), 5, 40, SceneObjectFlags.None, View));
        // Past the player it's in front, not between.
        Assert.Equal(255, SceneObjectFade.ViewAlpha(new Vector2(3, 0), 3, 40, SceneObjectFlags.None, View));
    }

    [Fact]
    public void AWiderScreenWidensTheConeByTheTangent()
    {
        Assert.Equal(ViewCone.Original, ViewCone.For(4f / 3));
        Assert.Equal(ViewCone.Original, ViewCone.For(1));
        var wide = ViewCone.For(16f / 9);
        Assert.Equal(MathF.Tan(ViewCone.Original.FadeFrom) * 4 / 3, MathF.Tan(wide.FadeFrom), 1e-5f);
        Assert.Equal(MathF.Tan(ViewCone.Original.Gone) * 4 / 3, MathF.Tan(wide.Gone), 1e-5f);
    }

    [Fact]
    public void AFartherDrawDistanceScalesTheRadiusAndEachLevel()
    {
        var tree = Tree(Vector3.Zero, SceneObjectFlags.Solid);
        var twice = new DrawDistance(2);
        Assert.Equal([255, 0], At(tree, 25, twice));
        Assert.Equal([0, 255], At(tree, 50, twice));
        Assert.Equal([0, 127], At(tree, 79.5f, twice));
    }

    [Fact]
    public void TheNearestDetailIsDrawnOutToTheFarthestLevelsDistance()
    {
        var tree = Tree(Vector3.Zero, SceneObjectFlags.Solid);
        var nearest = new DrawDistance(NearestDetail: true);
        Assert.Equal([255, 0], At(tree, 20, nearest));
        Assert.Equal([127, 0], At(tree, 39.5f, nearest));
    }

    [Fact]
    public void TheNearestDetailPassesOverAnEmptyLevel()
    {
        var tree = Tree(Vector3.Zero, SceneObjectFlags.Solid);
        var light = tree with { Models = [tree.Models[0] with { Empty = true }, tree.Models[1]] };
        var nearest = new DrawDistance(NearestDetail: true);
        Assert.Equal([0, 255], At(light, 2, nearest));
        Assert.Equal([0, 255], At(light, 20, nearest));
        Assert.Equal([0, 127], At(light, 39.5f, nearest));
    }

    private static readonly FlagGate Always = new(RecordId.Decimal(RecordTypes.Flag, 0), true);

    private static SceneObject Tree(Vector3 position, SceneObjectFlags flags) =>
        new(0, position, 40, PakVariant.Any, Always,
            [new(RecordId.Hex(RecordTypes.Scene, 0x7E1), 15), new(RecordId.Hex(RecordTypes.Scene, 0x7E3), 40)],
            2, Vector3.Zero, Vector3.One, default, flags);

    /// <summary>The levels' alphas with the player <paramref name="distance"/> along the look from the object, facing it.</summary>
    private static byte[] At(SceneObject o, float distance, DrawDistance? range = null)
    {
        var view = View with { Focus = o.Position - new Vector3(distance, 0, 0), Camera = new Vector2(o.Position.X - distance - 10, o.Position.Z) };
        var alphas = new byte[o.Lods];
        SceneObjectFade.Alphas(o, Vector3.Zero, view, alphas, range);
        return alphas;
    }
}
