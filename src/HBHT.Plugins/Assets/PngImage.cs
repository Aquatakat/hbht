using System.Buffers.Binary;
using System.IO.Compression;

namespace HBHT.Plugins.Assets;

/// <summary>
/// A PNG asset's pixels, decoded without Godot, for the core to read an image's colours as rules: the sky's gradients
/// (<c>HBHT.Core.Sky.SkyGradient</c>). .NET has no portable decoder.
/// </summary>
/// <remarks>
/// It reads what the importer writes (8-bit RGBA, unfiltered) and what an image editor saves for a mod: 8- or 16-bit
/// grey, grey and alpha, RGB and RGBA, and 8-bit palettes, with any of the five row filters and a <c>tRNS</c> chunk.
/// It doesn't read interlaced images, or palettes and greys of fewer than 8 bits; those come back null.
/// </remarks>
public static class PngImage
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The image in a PNG file as 8-bit RGBA, row by row, or null if it isn't a PNG this reads.</summary>
    public static (int Width, int Height, byte[] Rgba)? Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>The image in a PNG's bytes as 8-bit RGBA, row by row, or null if it isn't a PNG this reads.</summary>
    public static (int Width, int Height, byte[] Rgba)? Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
            return null;
        int width = 0, height = 0, depth = 0, colorType = -1;
        byte[]? palette = null, transparency = null;
        using var compressed = new MemoryStream();
        for (var at = Signature.Length; at + 12 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (length < 0 || at + 12 + length > png.Length)
                return null;
            var type = png.Slice(at + 4, 4);
            var data = png.Slice(at + 8, length);
            at += 12 + length;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (length < 13 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    return null;
                (width, height, depth, colorType) = (BinaryPrimitives.ReadInt32BigEndian(data), BinaryPrimitives.ReadInt32BigEndian(data[4..]), data[8], data[9]);
            }
            else if (type.SequenceEqual("PLTE"u8))
                palette = data.ToArray();
            else if (type.SequenceEqual("tRNS"u8))
                transparency = data.ToArray();
            else if (type.SequenceEqual("IDAT"u8))
                compressed.Write(data);
            else if (type.SequenceEqual("IEND"u8))
                break;
        }
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (width <= 0 || height <= 0 || channels == 0 || depth is not (8 or 16) || colorType == 3 && (depth != 8 || palette is null))
            return null;

        var bytesPerSample = depth / 8;
        var pixelBytes = channels * bytesPerSample;
        var stride = width * pixelBytes;
        var raw = new byte[(long)(stride + 1) * height];
        compressed.Position = 0;
        try
        {
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            if (zlib.ReadAtLeast(raw, raw.Length, throwOnEndOfStream: false) < raw.Length)
                return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var rows = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            if (filter > 4)
                return null;
            var line = raw.AsSpan(y * (stride + 1) + 1, stride);
            var row = rows.AsSpan(y * stride, stride);
            ReadOnlySpan<byte> up = y > 0 ? rows.AsSpan((y - 1) * stride, stride) : new byte[stride];
            for (var x = 0; x < stride; x++)
            {
                int left = x >= pixelBytes ? row[x - pixelBytes] : 0, above = up[x], corner = x >= pixelBytes ? up[x - pixelBytes] : 0;
                row[x] = (byte)(line[x] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) >> 1,
                    _ => Paeth(left, above, corner),
                });
            }
        }

        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            // A sample's top byte; a 16-bit one's whole value for the transparent colour.
            byte Sample(int channel) => rows[i * pixelBytes + channel * bytesPerSample];
            int Whole(int channel) => bytesPerSample == 1 ? Sample(channel)
                : rows[i * pixelBytes + channel * 2] << 8 | rows[i * pixelBytes + channel * 2 + 1];
            byte r, g, b, a = 255;
            switch (colorType)
            {
                case 0:
                    r = g = b = Sample(0);
                    if (transparency is { Length: >= 2 } && Whole(0) == BinaryPrimitives.ReadUInt16BigEndian(transparency))
                        a = 0;
                    break;
                case 2:
                    (r, g, b) = (Sample(0), Sample(1), Sample(2));
                    if (transparency is { Length: >= 6 } && Whole(0) == BinaryPrimitives.ReadUInt16BigEndian(transparency)
                        && Whole(1) == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2))
                        && Whole(2) == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)))
                        a = 0;
                    break;
                case 3:
                    var index = rows[i];
                    if (index * 3 + 2 >= palette!.Length)
                        return null;
                    (r, g, b) = (palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]);
                    if (transparency is not null && index < transparency.Length)
                        a = transparency[index];
                    break;
                case 4:
                    (r, a) = (Sample(0), Sample(1));
                    g = b = r;
                    break;
                default:
                    (r, g, b, a) = (Sample(0), Sample(1), Sample(2), Sample(3));
                    break;
            }
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (r, g, b, a);
        }
        return (width, height, rgba);
    }

    private static int Paeth(int left, int above, int corner)
    {
        var p = left + above - corner;
        int pa = Math.Abs(p - left), pb = Math.Abs(p - above), pc = Math.Abs(p - corner);
        return pa <= pb && pa <= pc ? left : pb <= pc ? above : corner;
    }
}
