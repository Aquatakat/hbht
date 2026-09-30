using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

/// <summary><c>Borg8Data.format</c>. The order differs from Borg1's type enum.</summary>
public enum Borg8Format : ushort
{
    Rgba32 = 1,
    Rgba16 = 2,
    Ia16 = 3,
    Ci8 = 4,
    Ia8 = 5,
    I8 = 6,
    Ci4 = 7,
    Ia4 = 8,
    I4 = 9,
}

/// <summary>
/// A Borg8 UI image: a 0x10-byte header, then a linear bitmap and, for the colour-indexed
/// formats, an RGBA16 palette (roadmap 1A).
/// </summary>
/// <remarks>
/// The header is format, width, height and an always-zero field (u16 each), then the palette's
/// and the bitmap's offsets from the start of the data (u32 each). The game draws these with
/// <c>LoadTile</c>, which does its own TMEM swizzling, so unlike Borg1 the rows are stored
/// in plain order. The palette offset is ignored unless the format is colour-indexed.
/// </remarks>
public sealed class Borg8Image
{
    public const int HeaderSize = 0x10;

    private readonly byte[] _data;

    private Borg8Image(byte[] data, Borg8Format format, int width, int height, int paletteOffset, int bitmapOffset)
    {
        _data = data;
        Format = format;
        Width = width;
        Height = height;
        PaletteOffset = paletteOffset;
        BitmapOffset = bitmapOffset;
    }

    public Borg8Format Format { get; }
    public int Width { get; }
    public int Height { get; }
    public int PaletteOffset { get; }
    public int BitmapOffset { get; }
    public TexelFormat TexelFormat => ToTexelFormat(Format);
    private int RowBytes => (Width * TexelFormat.BitsPerTexel() + 7) / 8;

    /// <summary>Parses and validates a decompressed Borg8. Doesn't decode any texels.</summary>
    public static Borg8Image Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg8 is at least 0x{HeaderSize:X} bytes, not {data.Length}.");

        var h = data.AsSpan();
        var format = (Borg8Format)BinaryPrimitives.ReadUInt16BigEndian(h);
        int width = BinaryPrimitives.ReadUInt16BigEndian(h[2..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(h[4..]);
        var palette = BinaryPrimitives.ReadUInt32BigEndian(h[8..]);
        var bitmap = BinaryPrimitives.ReadUInt32BigEndian(h[0xC..]);

        if (!Enum.IsDefined(format))
            throw new InvalidDataException($"Unknown Borg8 format {(ushort)format}.");
        if (width == 0 || height == 0)
            throw new InvalidDataException($"Empty {width}x{height} image.");

        var texelFormat = ToTexelFormat(format);
        var paletteBytes = texelFormat.PaletteSize() * 2;
        var bitmapEnd = bitmap + (long)(width * texelFormat.BitsPerTexel() + 7) / 8 * height;
        if (bitmap < HeaderSize || bitmapEnd > data.Length)
            throw new InvalidDataException($"The {width}x{height} {format} bitmap at 0x{bitmap:X} overruns the 0x{data.Length:X}-byte Borg.");
        if (paletteBytes > 0 && (palette < HeaderSize || palette + paletteBytes > data.Length))
            throw new InvalidDataException($"The palette at 0x{palette:X} overruns the 0x{data.Length:X}-byte Borg.");

        return new Borg8Image(data, format, width, height, paletteBytes > 0 ? (int)palette : 0, (int)bitmap);
    }

    /// <summary>The bitmap's bytes, row after row, as the game reads them.</summary>
    public ReadOnlySpan<byte> Bitmap => _data.AsSpan(BitmapOffset, RowBytes * Height);

    public RgbaImage Decode()
    {
        var palette = PaletteOffset == 0
            ? []
            : Texels.ReadPalette(_data.AsSpan(PaletteOffset), TexelFormat.PaletteSize());
        return Texels.Decode(TexelFormat, _data.AsSpan(BitmapOffset), Width, Height, RowBytes, palette);
    }

    public static TexelFormat ToTexelFormat(Borg8Format format) => format switch
    {
        Borg8Format.Rgba32 => TexelFormat.Rgba32,
        Borg8Format.Rgba16 => TexelFormat.Rgba16,
        Borg8Format.Ia16 => TexelFormat.Ia16,
        Borg8Format.Ci8 => TexelFormat.Ci8,
        Borg8Format.Ia8 => TexelFormat.Ia8,
        Borg8Format.I8 => TexelFormat.I8,
        Borg8Format.Ci4 => TexelFormat.Ci4,
        Borg8Format.Ia4 => TexelFormat.Ia4,
        Borg8Format.I4 => TexelFormat.I4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
