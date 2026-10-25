using HBHT.Core.Physics;

namespace HBHT.Core.Tests;

public sealed class SurfaceNamesTests
{
    [Fact]
    public void ThereAreTwentySixSurfacesNamedByNumber()
    {
        Assert.Equal(26, SurfaceNames.All.Count);
        Assert.Equal("Default", SurfaceNames.Of(0));
        Assert.Equal("Player and camera", SurfaceNames.Of(1));
        Assert.Equal("Wood echo", SurfaceNames.Of(10));
        Assert.Equal("Water", SurfaceNames.Of(14));
        Assert.Equal("Trans stone huge echo", SurfaceNames.Of(25));
        Assert.Equal("Surface 26", SurfaceNames.Of(26));
        Assert.Equal("Surface -1", SurfaceNames.Of(-1));
    }
}
