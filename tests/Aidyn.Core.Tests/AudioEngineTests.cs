using Aidyn.Core.Audio;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class AudioEngineTests
{
    private const int Rate = 44100;

    /// <summary>Pitch 44,100 Hz as an event's u16: base 0x4000 plus 13,858 shifted left once.</summary>
    private static readonly byte[] PitchAtRate = [0x22, 0x76];

    /// <summary>A track's first row: pitch, volume 255, pan 0x80, instrument 0, and a note.</summary>
    private static readonly byte[] NoteAtFullVolume = [0x7A, .. PitchAtRate, 0xFF, 0x80, 0x00];

    private static Sequence OneTrack(params byte[][] rows) =>
        new(1, [new Instrument([.. Enumerable.Repeat((short)1000, 100_000)], null)], [.. rows.SelectMany(r => r)]);

    private static short[] Render(AudioEngine engine, double ms)
    {
        var frames = new short[(int)(Rate * ms / 1000) * 2];
        engine.Render(frames);
        return frames;
    }

    [Fact]
    public void ANoteAtTheOutputRatePlaysItsSampleSquaredInVolumeAndPannedByEqualPower()
    {
        var engine = new AudioEngine(Rate);
        engine.Play(OneTrack(NoteAtFullVolume, [0], [0]));
        var frames = Render(engine, 10);

        // Volume 255 is track volume 15, times 255 / 16 is 239, << 6 is 15296, squared is 0.218 of
        // full. Pan 0x80 is the synthesizer's 64 of 127, whose gains are 0.703 and 0.711.
        var (left, right) = (frames[^2], frames[^1]);
        Assert.Equal(1000 * 0.2179 * 0.7027, left, 1.0);
        Assert.Equal(1000 * 0.2179 * 0.7115, right, 1.0);
    }

    [Fact]
    public void RowsComeEvery20MsUntilATempoEventMakesThem2500OverItsValue()
    {
        Assert.Equal(3 * 20, AudioEngine.Milliseconds(OneTrack(NoteAtFullVolume, [0], [0])));

        // Effect 0x40 with tempo 100: 125000 / (100 × 50) − 1 = 24 calls between rows, so 25 ms.
        byte[] tempo = [0x7B, .. PitchAtRate, 0xFF, 0x80, 0x00, 0x40, 100];
        Assert.Equal(3 * 25, AudioEngine.Milliseconds(OneTrack(tempo, [0], [0])));
    }

    [Fact]
    public void ASequenceStopsAfterItsLastRowUnlessItLoops()
    {
        var sequence = OneTrack(NoteAtFullVolume, [0], [0]);
        var engine = new AudioEngine(Rate);
        var once = engine.Play(sequence);
        var looped = engine.Play(sequence, loop: true);

        // The last row is read 40 ms in, and the sequence stops there.
        Render(engine, 39);
        Assert.True(engine.IsPlaying(once));
        Render(engine, 2);
        Assert.False(engine.IsPlaying(once));
        Render(engine, 500);
        Assert.True(engine.IsPlaying(looped));
    }

    [Fact]
    public void ADurationStopsASequenceThatWouldLoop()
    {
        var engine = new AudioEngine(Rate);
        var handle = engine.Play(OneTrack(NoteAtFullVolume, [0], [0]), loop: true, durationMs: 100);
        Render(engine, 99);
        Assert.True(engine.IsPlaying(handle));
        Render(engine, 2);
        Assert.False(engine.IsPlaying(handle));
        Assert.All(Render(engine, 1), s => Assert.Equal(0, s));
    }

    [Fact]
    public void ANoteOffCutsTheTracksVoice()
    {
        var engine = new AudioEngine(Rate);
        engine.Play(OneTrack(NoteAtFullVolume, [0x01, 0x80], [0]), loop: true);
        Assert.NotEqual(0, Render(engine, 15)[^2]);
        Assert.All(Render(engine, 10)[^100..], s => Assert.Equal(0, s));
    }

    [Fact]
    public void ALoopingInstrumentGoesBackToItsLoopStart()
    {
        // A ramp of 100 samples that loops over its last 50 never falls below 50 once it's there.
        var ramp = new Instrument([.. Enumerable.Range(0, 100).Select(i => (short)(i * 100))], new SampleLoop(50, 100));
        var engine = new AudioEngine(Rate);
        engine.Play(new Sequence(1, [ramp], [.. NoteAtFullVolume, .. new byte[9]]));
        var frames = Render(engine, 30);
        var left = frames.Where((_, i) => i % 2 == 0).Skip(200).ToList();
        Assert.All(left, s => Assert.InRange(s, 100 * 48 * 0.2179 * 0.7027, 10_000));
    }

    [Fact]
    public void NoMoreThan32SequencesPlayAtOnce()
    {
        var engine = new AudioEngine(Rate);
        var sequence = OneTrack(NoteAtFullVolume, [0]);
        var handles = Enumerable.Range(0, 33).Select(_ => engine.Play(sequence, loop: true)).ToList();
        Assert.All(handles[..32], h => Assert.True(engine.IsPlaying(h)));
        Assert.True(handles[32].IsNone);
    }

    [Fact]
    public void ASequencesPanNarrowsItsTracksPansTowardsIt()
    {
        // A track hard left in a sequence panned hard right ends up near the right.
        var engine = new AudioEngine(Rate);
        engine.Play(OneTrack([0x7A, .. PitchAtRate, 0xFF, 0x00, 0x00], [0]), pan: 0xF0, loop: true);
        var frames = Render(engine, 10);
        Assert.True(frames[^1] > 4 * frames[^2], $"left {frames[^2]}, right {frames[^1]}");
    }
}
