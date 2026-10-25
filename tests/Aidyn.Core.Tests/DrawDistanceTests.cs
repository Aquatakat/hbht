using Aidyn.Core.Scenes;
using Aidyn.Core.Sky;

namespace Aidyn.Core.Tests;

/// <summary>The Draw Distance setting's Extended: the planes trek's frame and its fog are drawn with over the 5×5.</summary>
public sealed class DrawDistanceTests
{
    private static readonly (float Near, float Far) Trek = (SkyDirector.TrekNear, SkyDirector.TrekFar);

    [Fact]
    public void TrekFogsFromAbout105UnitsAsTheOriginalDoes()
    {
        Assert.Equal(105, DrawDistance.Distance(Trek, SkyColors.FogStart(0)), 0);
        Assert.Equal(180, DrawDistance.Distance(Trek, 1000), 3);
        // The weather's at its thickest, 966: about 25.
        Assert.Equal(25, DrawDistance.Distance(Trek, SkyColors.FogStart(DrawDistance.ThickestFog)), 0);
    }

    [Fact]
    public void ClearOfTheWeatherTheFogAndTheCutMoveOutToTheFiveByFivesReach()
    {
        var planes = DrawDistance.Planes(Trek, 0);
        Assert.Equal(300, planes.Far, 3);
        var start = DrawDistance.Distance(planes, SkyColors.FogStart(0));
        Assert.InRange(start, 170, 180);
        // Within the 200 the 5×5 reaches from anywhere in its middle chunk.
        Assert.True(start < 200);
    }

    [Fact]
    public void ThickeningWeatherFogPullsItBackToTheOriginalsAtItsThickest()
    {
        var thickest = DrawDistance.Planes(Trek, DrawDistance.ThickestFog);
        Assert.Equal(Trek.Near, thickest.Near, 5);
        Assert.Equal(Trek.Far, thickest.Far, 3);
        Assert.Equal(Trek.Far, DrawDistance.Planes(Trek, 1).Far, 3);
        var last = float.MaxValue;
        for (var fog = 0f; fog <= DrawDistance.ThickestFog; fog += 0.05f)
        {
            var start = DrawDistance.Distance(DrawDistance.Planes(Trek, fog), SkyColors.FogStart(fog));
            Assert.True(start < last);
            // Never nearer than the original's own fog with this weather.
            Assert.True(start >= DrawDistance.Distance(Trek, SkyColors.FogStart(fog)) - 0.01f);
            last = start;
        }
    }
}
