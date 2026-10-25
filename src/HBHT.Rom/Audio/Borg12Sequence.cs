
namespace HBHT.Rom.Audio;

/// <summary>
/// An instrument of a Borg12: the Borg11 it plays and its loop.
/// </summary>
/// <param name="Sample">The Borg11's archive index.</param>
/// <param name="LoopStart">The first sample of the loop. Null if it plays once.</param>
/// <param name="LoopEnd">The sample after the loop's last, where playback jumps back to <paramref name="LoopStart"/>.</param>
public readonly record struct Borg12Instrument(int Sample, int? LoopStart, int? LoopEnd);

/// <summary>
/// A Borg12: a piece of music or a sound effect, as rows of events over up to 16 tracks, played by
/// the original's own sequencer (<c>dcm.cpp</c>, a "DCM module", roadmap 1D). docs/formats/audio.md
/// has the event stream.
/// </summary>
/// <remarks>
/// After a u32 offset of the sample list and a zero u32 comes the header the sequencer reads (the
/// game passes it <c>blob + 8</c>): tracks, instruments, the event stream's length in bytes, the
/// position the stream loops back to, the instrument table's offset (0x20) and the stream's, u32
/// each. Instruments are 16 bytes: a sample pointer (0 on disk), loop start and loop end, then a
/// loop count in the last byte, 0xFF for forever or 0 for none (no other count occurs). The sample
/// list after the stream holds a Borg11 index per instrument, which <c>BorgDCMModule.Fixup</c>
/// (1.0 <c>0x8009D230</c>) loads.
/// </remarks>
public sealed class Borg12Sequence
{
    private const int HeaderSize = 0x20, InstrumentSize = 0x10;

    /// <summary>Pitch events' bases by their top two bits (1.0 <c>0x800D5284</c>).</summary>
    private static readonly int[] PitchBase = [0, 0x4000, 0xC000, 0x1C000];

    private Borg12Sequence(int tracks, Borg12Instrument[] instruments, byte[] stream, int loopPosition)
    {
        Tracks = tracks;
        Instruments = instruments;
        Stream = stream;
        LoopPosition = loopPosition;
    }

    public int Tracks { get; }
    public IReadOnlyList<Borg12Instrument> Instruments { get; }

    /// <summary>The event stream, which <c>Dcm.Step</c> reads a row at a time.</summary>
    public byte[] Stream { get; }

    /// <summary>Where in <see cref="Stream"/> a looping play goes back to. Zero in every Borg12.</summary>
    public int LoopPosition { get; }

    public static Borg12Sequence Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg12 is at least 0x{HeaderSize:X} bytes, not {data.Length}.");

        int U32(int at) => checked((int)Be.U32(data, at));
        var listOffset = U32(0);
        var tracks = U32(8);
        var count = U32(0xC);
        var streamLength = U32(0x10);
        var loop = U32(0x14);
        var tableOffset = U32(0x18);
        var streamOffset = U32(0x1C);

        if (tracks is < 1 or > 32 || count < 1)
            throw new InvalidDataException($"A Borg12 of {tracks} tracks and {count} instruments.");
        if (tableOffset != HeaderSize || streamOffset < tableOffset + count * InstrumentSize)
            throw new InvalidDataException($"The Borg12's instruments at 0x{tableOffset:X} and stream at 0x{streamOffset:X} overlap.");
        if (streamOffset + streamLength > listOffset || listOffset + count * 4 > data.Length)
            throw new InvalidDataException($"The Borg12's stream and sample list overrun its 0x{data.Length:X} bytes.");
        if (loop >= streamLength)
            throw new InvalidDataException($"The Borg12 loops to 0x{loop:X}, past its 0x{streamLength:X}-byte stream.");

        var instruments = new Borg12Instrument[count];
        for (var i = 0; i < count; i++)
        {
            var at = tableOffset + i * InstrumentSize;
            var loops = data[at + 0xF] switch
            {
                0xFF => true,
                0 => false,
                var n => throw new InvalidDataException($"Instrument {i} loops {n} times; only 0 and forever are known."),
            };
            instruments[i] = loops
                ? new Borg12Instrument(U32(listOffset + i * 4), U32(at + 4), U32(at + 8))
                : new Borg12Instrument(U32(listOffset + i * 4), null, null);
        }

        return new Borg12Sequence(tracks, instruments, data[streamOffset..(streamOffset + streamLength)], loop);
    }

    /// <summary>
    /// Every note the stream starts: the instrument and the pitch in Hz, as the original keeps it
    /// in 16 bits.
    /// </summary>
    public IEnumerable<(int Instrument, int Pitch)> Notes() =>
        Events().Where(e => (e.Flags & 0x02) != 0).Select(e => (e.Instrument, e.Pitch));

    /// <summary>Each track's first event's flags, in track order.</summary>
    public IEnumerable<byte> FirstEvents() =>
        Events().GroupBy(e => e.Track).OrderBy(g => g.Key).Select(g => g.First().Flags);

    /// <summary>
    /// Every event, rests aside, in stream order, with its track's pitch and instrument after it.
    /// Throws if the stream doesn't end on a row's end.
    /// </summary>
    private IEnumerable<(int Track, byte Flags, int Instrument, int Pitch)> Events()
    {
        var waits = new int[Tracks];
        var pitches = new int[Tracks];
        var instruments = new int[Tracks];
        var at = 0;
        while (at < Stream.Length)
        {
            for (var t = 0; t < Tracks; t++)
            {
                if (waits[t] != 0)
                {
                    waits[t]--;
                    continue;
                }
                var b = Stream[at++];
                if ((b & 0x80) != 0)
                {
                    waits[t] = (b & 0x7F) + 1;
                    continue;
                }
                if ((b & 0x40) != 0)
                {
                    int w = Stream[at] | Stream[at + 1] << 8;
                    at += 2;
                    pitches[t] = PitchBase[w >> 14] + ((w & 0x3FFF) << (w >> 14)) & 0xFFFF;
                }
                at += ((b >> 5) & 1) + ((b >> 4) & 1);
                if ((b & 0x08) != 0)
                    instruments[t] = Stream[at++];
                at += (b >> 2) & 1;
                if ((b & 0x01) != 0)
                {
                    var e = Stream[at++];
                    at += ((e >> 6) & 1) + ((e >> 5) & 1);
                }
                yield return (t, b, instruments[t], pitches[t]);
            }
        }
        if (at != Stream.Length)
            throw new InvalidDataException($"The stream overruns its {Stream.Length} bytes.");
    }
}
