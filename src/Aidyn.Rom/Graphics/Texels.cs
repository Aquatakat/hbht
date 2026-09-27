using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

/// <summary>A decoded image: 8-bit RGBA, rows top to bottom, no padding.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    public RgbaImage(int width, int height) : this(width, height, new byte[width * height * 4])
    {
    }
}

/// <summary>Decodes N64 texels to RGBA the way the RDP expands them.</summary>
public static class Texels
{
    /// <summary>
    /// Decodes one image.
    /// </summary>
    /// <param name="rowStride">Bytes from one row to the next.</param>
    /// <param name="palette">RGBA16 entries, for the colour-indexed formats.</param>
    /// <param name="tmemInterleaved">
    /// The data is in TMEM order, as <c>LoadBlock</c> copies it without reordering. On odd rows,
    /// TMEM swaps each pair of 32-bit words (each pair of 64-bit words for RGBA32), so the
    /// swap has to be undone here.
    /// </param>
    public static RgbaImage Decode(
        TexelFormat format, ReadOnlySpan<byte> data, int width, int height, int rowStride,
        ReadOnlySpan<ushort> palette = default, bool tmemInterleaved = false)
    {
        if (palette.Length < format.PaletteSize())
            throw new ArgumentException($"{format} needs {format.PaletteSize()} palette entries.", nameof(palette));
        var rowBytes = (width * format.BitsPerTexel() + 7) / 8;
        // A swapped read can land anywhere in the stride, so TMEM-ordered data needs whole rows.
        var needed = tmemInterleaved ? rowStride * height : rowStride * (height - 1) + rowBytes;
        if (rowStride < rowBytes || data.Length < needed)
            throw new ArgumentException($"{width}x{height} {format} doesn't fit in {data.Length} bytes at stride {rowStride}.");
        var swap = format == TexelFormat.Rgba32 ? 8 : 4;
        if (tmemInterleaved && height > 1 && rowStride % (swap * 2) != 0)
            throw new ArgumentException($"A TMEM-ordered {format} row stride must be a multiple of {swap * 2}.");

        var image = new RgbaImage(width, height);
        var dst = image.Pixels.AsSpan();
        for (var y = 0; y < height; y++)
        {
            var row = data.Slice(y * rowStride, Math.Min(rowStride, data.Length - y * rowStride));
            var xor = tmemInterleaved && (y & 1) == 1 ? swap : 0;
            for (var x = 0; x < width; x++)
                DecodeTexel(format, row, x, xor, palette, dst.Slice((y * width + x) * 4, 4));
        }
        return image;
    }

    private static void DecodeTexel(
        TexelFormat format, ReadOnlySpan<byte> row, int x, int xor, ReadOnlySpan<ushort> palette, Span<byte> rgba)
    {
        switch (format)
        {
            case TexelFormat.Rgba16:
                Rgba16(BinaryPrimitives.ReadUInt16BigEndian(row[((x * 2) ^ xor)..]), rgba);
                break;
            case TexelFormat.Rgba32:
                row.Slice((x * 4) ^ xor, 4).CopyTo(rgba);
                break;
            case TexelFormat.Ia16:
            {
                var at = (x * 2) ^ xor;
                Gray(row[at], row[at + 1], rgba);
                break;
            }
            case TexelFormat.Ia8:
            {
                var b = row[x ^ xor];
                Gray((byte)((b >> 4) * 0x11), (byte)((b & 0xF) * 0x11), rgba);
                break;
            }
            case TexelFormat.Ia4:
            {
                var n = Nibble(row, x, xor);
                var i = (n >> 1) & 7;
                Gray((byte)((i << 5) | (i << 2) | (i >> 1)), (byte)((n & 1) * 0xFF), rgba);
                break;
            }
            case TexelFormat.I8:
            {
                var i = row[x ^ xor];
                Gray(i, i, rgba);
                break;
            }
            case TexelFormat.I4:
            {
                var i = (byte)(Nibble(row, x, xor) * 0x11);
                Gray(i, i, rgba);
                break;
            }
            case TexelFormat.Ci8:
                Rgba16(palette[row[x ^ xor]], rgba);
                break;
            case TexelFormat.Ci4:
                Rgba16(palette[Nibble(row, x, xor)], rgba);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>The high nibble is the left texel.</summary>
    private static int Nibble(ReadOnlySpan<byte> row, int x, int xor)
    {
        var b = row[(x >> 1) ^ xor];
        return (x & 1) == 0 ? b >> 4 : b & 0xF;
    }

    private static void Rgba16(ushort v, Span<byte> rgba)
    {
        rgba[0] = Expand5(v >> 11);
        rgba[1] = Expand5(v >> 6);
        rgba[2] = Expand5(v >> 1);
        rgba[3] = (byte)((v & 1) * 0xFF);
    }

    private static byte Expand5(int v)
    {
        v &= 0x1F;
        return (byte)((v << 3) | (v >> 2));
    }

    /// <summary>I and IA texels: the RDP copies intensity to all three colour channels.</summary>
    private static void Gray(byte i, byte a, Span<byte> rgba)
    {
        rgba[0] = rgba[1] = rgba[2] = i;
        rgba[3] = a;
    }

    /// <summary>Reads a big-endian RGBA16 palette.</summary>
    public static ushort[] ReadPalette(ReadOnlySpan<byte> data, int count)
    {
        var palette = new ushort[count];
        for (var i = 0; i < count; i++)
            palette[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(i * 2)..]);
        return palette;
    }
}
