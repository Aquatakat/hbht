using HBHT.Plugins.Assets;

namespace HBHT.Plugins.Tests;

public class WavTests
{
    [Fact]
    public void SamplesRoundTrip()
    {
        short[] samples = [0, 1, -1, short.MaxValue, short.MinValue, 1234];
        Assert.Equal(samples, Wav.Decode(Wav.Encode(samples, 22050)));
    }

    [Fact]
    public void StereoIsNotASample() =>
        Assert.Throws<InvalidDataException>(() => Wav.Decode(Wav.EncodeStereo([1, 2, 3, 4], 44100)));
}
