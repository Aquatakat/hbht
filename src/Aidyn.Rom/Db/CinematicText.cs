using Aidyn.Rom.Compression;
using Aidyn.Rom.Text;

namespace Aidyn.Rom.Db;

/// <summary>One subtitle in a cinematic.</summary>
/// <param name="Start">When the caption appears. The game doubles both times to get its runtime ticks.</param>
/// <param name="End">When the caption goes away.</param>
public sealed record Caption(short Start, short End, string Text);

/// <summary>
/// Cinematic subtitles. An index lists each cinematic's captions; each caption is its own
/// LZ01x stream holding the two times and then the encrypted text.
/// </summary>
public static class CinematicText
{
    /// <summary>Reads every cinematic's captions, in index order.</summary>
    internal static IReadOnlyList<IReadOnlyList<Caption>> Read(ReadOnlySpan<byte> index, ReadOnlySpan<byte> text)
    {
        var r = new RecordReader(index);
        var scenes = new IReadOnlyList<Caption>[r.U16Be()];
        for (var i = 0; i < scenes.Length; i++)
        {
            var captions = new Caption[r.U16Be()];
            for (var j = 0; j < captions.Length; j++)
                captions[j] = ReadCaption(text, offset: r.U16Be(), size: r.U16Be(), storedSize: r.U16Be());
            scenes[i] = captions;
        }
        return scenes;
    }

    private static Caption ReadCaption(ReadOnlySpan<byte> text, int offset, int size, int storedSize)
    {
        if (offset > text.Length - storedSize)
            throw new InvalidDataException($"Caption at 0x{offset:X}+0x{storedSize:X} is outside the cinematic text.");
        if (size < 4)
            throw new InvalidDataException($"Caption at 0x{offset:X} is too short ({size} bytes) for its times.");

        var output = new byte[size];
        var result = Lzo1x.Decompress(text.Slice(offset, storedSize), output);
        if (result.BytesWritten != size)
            throw new DecompressionException($"Caption at 0x{offset:X} decompressed to {result.BytesWritten} bytes, not {size}.");

        var r = new RecordReader(output);
        var start = r.S16Be();
        var end = r.S16Be();
        return new Caption(start, end, GameText.Decrypt(r.Bytes(r.Remaining), StringCipher.RomStringSeedA, StringCipher.RomStringSeedB));
    }
}
