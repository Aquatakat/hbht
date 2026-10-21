namespace Aidyn.Core.Tests;

public class RoundingTests
{
    /// <summary>Halves round away from zero, and a negative wraps to a byte.</summary>
    [Theory]
    [InlineData(2.5f, 3)]
    [InlineData(2.4f, 2)]
    [InlineData(0f, 0)]
    [InlineData(-0.4f, 0)]
    [InlineData(-0.5f, 255)]
    [InlineData(-2.5f, 253)]
    [InlineData(-2.6f, 253)]
    public void HalvesRoundAwayFromZero(float value, int expected)
    {
        Assert.Equal((byte)expected, Rounding.HalfAwayToByte(value));
        Assert.Equal((byte)expected, Rounding.HalfAwayToByte((double)value));
    }

    [Fact]
    public void DegreesAreRadians()
    {
        Assert.Equal(MathF.PI / 2, Degrees.ToRadians(90), 1e-6f);
        Assert.Equal(-MathF.PI, Degrees.ToRadians(-180), 1e-6f);
    }
}
