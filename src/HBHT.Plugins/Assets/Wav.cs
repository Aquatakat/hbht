using System.Buffers.Binary;
using System.Text;

namespace HBHT.Plugins.Assets;

/// <summary>
/// Mono 16-bit PCM WAV, the format of sample assets (ADR 0005). The rate in the header is only for
/// listening to the file elsewhere: a sequence's events say what rate to play a sample at.
/// </summary>
public static class Wav
{
    public static byte[] Encode(ReadOnlySpan<short> samples, int rate)
    {
        var data = samples.Length * 2;
        var bytes = new byte[44 + data];
        var span = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF", span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + data);
        Encoding.ASCII.GetBytes("WAVEfmt ", span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], rate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data", span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], data);
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], samples[i]);
        return bytes;
    }

    /// <summary>Writes interleaved stereo 16-bit PCM, for renders.</summary>
    public static byte[] EncodeStereo(ReadOnlySpan<short> interleaved, int rate)
    {
        var bytes = Encode(interleaved, rate);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), rate * 4);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 4);
        return bytes;
    }

    /// <summary>Reads a mono 16-bit PCM WAV's samples, skipping chunks other than <c>fmt </c> and <c>data</c>.</summary>
    public static short[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV file.");
        var formatSeen = false;
        for (var at = 12; at + 8 <= bytes.Length;)
        {
            var id = bytes.Slice(at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
            if (size < 0 || at + 8 + size > bytes.Length)
                throw new InvalidDataException("A WAV chunk overruns the file.");
            var body = bytes.Slice(at + 8, size);
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || BinaryPrimitives.ReadInt16LittleEndian(body) != 1 || BinaryPrimitives.ReadInt16LittleEndian(body[2..]) != 1
                    || BinaryPrimitives.ReadInt16LittleEndian(body[14..]) != 16)
                    throw new InvalidDataException("A sample must be mono 16-bit PCM.");
                formatSeen = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!formatSeen)
                    throw new InvalidDataException("The WAV's data comes before its format.");
                var samples = new short[size / 2];
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(body[(i * 2)..]);
                return samples;
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("The WAV has no data.");
    }
}
