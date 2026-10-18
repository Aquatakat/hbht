using System.Numerics;
using Aidyn.Core.Lights;
using Aidyn.Core.Particles;
using Aidyn.Core.Rng;
using Aidyn.Core.Scenes;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>The float to byte conversion 1.0 makes everywhere: truncate toward zero, keep the low byte.</summary>
public class ColorBytesTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.999f, 0)]
    [InlineData(1f, 1)]
    [InlineData(203.9999f, 203)]
    [InlineData(254.999f, 254)]
    [InlineData(255f, 255)]
    [InlineData(255.999f, 255)]
    [InlineData(256f, 0)]
    [InlineData(300.5f, 44)]
    [InlineData(-0.5f, 0)]
    [InlineData(-1f, 255)]
    [InlineData(-1.5f, 255)]
    [InlineData(-50f, 206)]
    public void SingleTruncatesTowardZeroAndKeepsTheLowByte(float value, int expected) =>
        Assert.Equal(expected, ColorBytes.Single(value));

    [Theory]
    [InlineData(0.999, 0)]
    [InlineData(203.9999999, 203)]
    [InlineData(255.999, 255)]
    [InlineData(256.0, 0)]
    [InlineData(-0.5, 0)]
    [InlineData(-1.0, 255)]
    [InlineData(-50.9, 206)]
    public void DoubleDoesTheSame(double value, int expected) =>
        Assert.Equal(expected, ColorBytes.Double(value));

    [Fact]
    public void ParticlesTruncateTheirChannelsAndAnyThatRunNegativeWrapAsTheConsoleDoes()
    {
        // ParticleHead.Draw (0x800a9480) truncates × 255.0f and stores the low byte; it doesn't clamp.
        var head = new ParticleHead(new Lcg());

        Assert.Equal(new Rgba(0, 127, 255, 1), head.Bytes(new Vector4(0.999f / 255, 0.5f, 1, 1.5f / 255)));
        Assert.Equal(new Rgba(255, 206, 255, 0), head.Bytes(new Vector4(-1f / 255, -50f / 255, 1, -0.5f / 255)));
    }

    [Fact]
    public void AParticleIsDarkenedByTheScreenFadeBeforeItsBytesAreMade()
    {
        var head = new ParticleHead(new Lcg()) { Brightness = 0.5f };

        Assert.Equal(new Rgba(127, 63, 0, 127), head.Bytes(new Vector4(1, 0.5f, 0, 1)));
    }

    [Fact]
    public void ALightScaledByANegativeShareWrapsAsTheConsoleDoes()
    {
        // PointLight.Scale (0x8004d220); the rules never pass a negative.
        Assert.Equal(new Rgba(206, 0, 0, 156), PointLight.Scale(new Rgba(100, 0, 1, 200), -0.5f));
        Assert.Equal(new Rgba(99, 49, 24, 199), PointLight.Scale(new Rgba(100, 50, 25, 200), 0.999f));
    }

    [Fact]
    public void AnObjectInTheViewConesFadeGetsItsAlphaFromADoubleProduct()
    {
        // 40° off the look, between the cone's 37.5° and 42.5°: 255 × (cos 40° − cos 42.5°) / (cos 37.5° − cos 42.5°) = 130.78
        // (RenderVoxelScenes, 0x8000c8cc: div.s, then mul.d by 255.0). Every float share in 0–1 gives the same byte in
        // single and in double, so this is the double path's value, not a difference from the single one.
        var view = new SceneView(Vector3.Zero, Vector2.Zero, Vector2.UnitX, 0, ViewCone.Original);

        Assert.Equal(130, SceneObjectFade.ViewAlpha(new Vector2(15.320889f, 12.855753f), 5, 40, SceneObjectFlags.Solid, view));
    }

    [Fact]
    public void AnObjectBetweenTheCameraAndThePlayerTruncatesBeforeAdding0x60AndWidensInDouble()
    {
        // RenderVoxelScenes (0x8000c984): the width is (float)((double)(aim − along) × 0.35), and the alpha is
        // (int)(across / width × 159) + 0x60. With a width of 0.35f and the 0x60 added before truncating, this one is 126.
        var view = new SceneView(Vector3.Zero, Vector2.Zero, Vector2.UnitX, 10, ViewCone.Original);

        Assert.Equal(125, SceneObjectFade.ViewAlpha(new Vector2(4.53f, 0.36f), 5, 40, SceneObjectFlags.None, view));
    }
}
