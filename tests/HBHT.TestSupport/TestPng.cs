using System.Buffers.Binary;
using System.IO.Compression;

namespace HBHT.TestSupport;

/// <summary>
/// PNGs a test writes by hand: any colour type at 8 or 16 bits, with a row filter of its choosing, and the chunks it
/// adds. The CRCs are left 0, which the port's reader doesn't check.
/// </summary>
public static class TestPng
{
    /// <param name="colorType">0 grey, 2 RGB, 3 palette, 4 grey and alpha, 6 RGBA.</param>
    /// <param name="samples">The image's samples, row by row, as the colour type and depth lay them out.</param>
    /// <param name="filter">The filter each row is written with, 0 to 4, by its number.</param>
    /// <param name="chunks">Chunks written between the header and the data, such as <c>PLTE</c> and <c>tRNS</c>.</param>
    public static byte[] Encode(int width, int height, int colorType, byte[] samples, int depth = 8, Func<int, int>? filter = null,
        params (string Type, byte[] Data)[] chunks)
    {
        var channels = colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
        var pixelBytes = channels * depth / 8;
        var stride = width * pixelBytes;
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        (header[8], header[9]) = ((byte)depth, (byte)colorType);
        Chunk(output, "IHDR", header);
        foreach (var (type, data) in chunks)
            Chunk(output, type, data);
        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                for (var y = 0; y < height; y++)
                {
                    var kind = filter?.Invoke(y) ?? 0;
                    zlib.WriteByte((byte)kind);
                    for (var x = 0; x < stride; x++)
                    {
                        int Get(int dx, int dy) => x - dx >= 0 && y - dy >= 0 ? samples[(y - dy) * stride + x - dx] : 0;
                        int left = Get(pixelBytes, 0), above = Get(0, 1), corner = Get(pixelBytes, 1);
                        var predicted = kind switch
                        {
                            0 => 0,
                            1 => left,
                            2 => above,
                            3 => (left + above) >> 1,
                            _ => Paeth(left, above, corner),
                        };
                        zlib.WriteByte((byte)(samples[y * stride + x] - predicted));
                    }
                }
            }
            Chunk(output, "IDAT", raw.ToArray());
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static int Paeth(int left, int above, int corner)
    {
        var p = left + above - corner;
        int pa = Math.Abs(p - left), pb = Math.Abs(p - above), pc = Math.Abs(p - corner);
        return pa <= pb && pa <= pc ? left : pb <= pc ? above : corner;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(System.Text.Encoding.ASCII.GetBytes(type));
        output.Write(data);
        output.Write([0, 0, 0, 0]);
    }
}
