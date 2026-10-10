using Aidyn.Rom.Audio;
using Aidyn.Rom.Borg;

namespace Aidyn.Rom.Tests;

public class AudioTests
{
    [Fact]
    public void EveryBorg11IsSignedPcmOfEightOrSixteenBits()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var samples = archive.Listings.Where(l => l.Type == BorgType.Sample)
            .Select(l => Borg11Sample.Parse(archive.Read(l.Index))).ToList();

        Assert.Equal((149, 73), (samples.Count, samples.Count(s => s.EightBit)));
        Assert.Equal((918_730, 1_528_476), (samples.Where(s => !s.EightBit).Sum(s => s.Samples.Length), samples.Where(s => s.EightBit).Sum(s => s.Samples.Length)));
        // Widened 8-bit samples keep a zero low byte. Read with the wrong sign, a wave jumps between
        // the extremes wherever it crosses zero: read as unsigned, the 8-bit samples would jump
        // hundreds of times as often as they do signed, where only noise does.
        var eightBit = samples.Where(s => s.EightBit).ToList();
        Assert.All(eightBit, s => Assert.All(s.Samples, x => Assert.Equal(0, x & 0xFF)));
        static int Jumps(IEnumerable<int> wave, int over) => wave.Zip(wave.Skip(1)).Count(p => Math.Abs(p.First - p.Second) > over);
        var signed = eightBit.Sum(s => Jumps(s.Samples.Select(x => x >> 8), 156));
        var unsigned = eightBit.Sum(s => Jumps(s.Samples.Select(x => (x >> 8) & 0xFF), 156));
        Assert.True(signed * 100 < unsigned, $"{signed} jumps signed, {unsigned} unsigned");
    }

    [Fact]
    public void EveryBorg12StreamEndsOnARowAndLoopsToTheStart()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var sequences = archive.Listings.Where(l => l.Type == BorgType.Sequence)
            .Select(l => Borg12Sequence.Parse(archive.Read(l.Index))).ToList();
        var samples = archive.Listings.Where(l => l.Type == BorgType.Sample).Select(l => l.Index).ToHashSet();

        Assert.Equal(476, sequences.Count);
        Assert.All(sequences, s => Assert.Equal(0, s.LoopPosition));
        Assert.All(sequences.SelectMany(s => s.Instruments), i => Assert.Contains(i.Sample, samples));
        Assert.Equal((1_344, 487), (sequences.Sum(s => s.Tracks), sequences.Sum(s => s.Instruments.Count(i => i.LoopStart is not null))));
        // Notes() walks every stream to its end, and every note's instrument is one the sequence has.
        Assert.Equal(29_518, sequences.Sum(s => s.Notes().Count(n => n.Instrument < s.Instruments.Count)));
        // A track's first event sets its pitch, volume, pan and instrument, so no note plays with what
        // a player slot's last sequence left. The first track's is first in the stream.
        Assert.All(sequences, s => Assert.Equal(0x78, s.Stream[0] & 0x78));
        Assert.Equal(1_344, sequences.Sum(s => s.FirstEvents().Count(b => (b & 0x78) == 0x78)));
    }
}
