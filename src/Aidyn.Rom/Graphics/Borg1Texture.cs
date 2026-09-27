using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

/// <summary><c>Borg1Data.type</c>. The order differs from Borg8's format enum.</summary>
public enum Borg1Format : ushort
{
    Rgba16 = 0,
    Ia16 = 1,
    Ci8 = 2,
    Ia8 = 3,
    I8 = 4,
    Ci4 = 5,
    Ia4 = 6,
    I4 = 7,
    Rgba32 = 8,
}

/// <summary><c>Borg1Data.flag</c>.</summary>
[Flags]
public enum Borg1Flags : ushort
{
    None = 0,
    Unknown20 = 0x20,
    /// <summary>Sets <c>G_TD_DETAIL</c>.</summary>
    Detail = 0x40,
    /// <summary>Sets <c>G_TD_SHARPEN</c>.</summary>
    Sharpen = 0x80,
    /// <summary>The game gives the texture a second, writable bitmap. Only used with <see cref="Moving"/>.</summary>
    Procedural = 0x100,
    /// <summary>The bitmap scrolls; see <see cref="Borg1Texture.Move"/>.</summary>
    Moving = 0x200,
}

/// <summary>
/// A Borg1 texture: a 0x18-byte header followed by the bitmap's mip levels and then, for the
/// colour-indexed formats, an RGBA16 palette (roadmap 1A).
/// </summary>
/// <remarks>
/// <para>
/// The header is type (u16), flags (u16), width, height, mip count and scroll (u8 each), then
/// four u32 offsets from the start of the data: a display list (always 0), the bitmap (always
/// 0x18), the palette (0 when there isn't one), and a field that's always 0.
/// </para>
/// <para>
/// The game uploads the whole bitmap with one <c>LoadBlock</c>, so it's stored in TMEM order:
/// odd rows have their words swapped (see <see cref="Texels.Decode"/>). Levels follow each other,
/// with each row padded to at least 8 bytes. Each level halves both sides, rounding an odd
/// result down to even unless it's 1.
/// </para>
/// </remarks>
public sealed class Borg1Texture
{
    public const int HeaderSize = 0x18;
    private const int MinRowBytes = 8;

    private readonly byte[] _data;

    private Borg1Texture(byte[] data, Borg1Format format, Borg1Flags flags, int width, int height, int lods,
        byte move, int paletteOffset, LevelLayout[] levels)
    {
        _data = data;
        Format = format;
        Flags = flags;
        Width = width;
        Height = height;
        Lods = lods;
        Move = move;
        PaletteOffset = paletteOffset;
        _levels = levels;
    }

    private readonly LevelLayout[] _levels;

    public Borg1Format Format { get; }
    public Borg1Flags Flags { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>The number of mip levels after the base level.</summary>
    public int Lods { get; }

    /// <summary>
    /// Scroll control for <see cref="Borg1Flags.Moving"/> textures, which the game shifts
    /// sideways in place. Per the decomp (<c>FUN_8009d3dc</c>, unconfirmed): with a low nibble of
    /// 0, it shifts by the high nibble every frame. Otherwise the high nibble counts frames up to
    /// the low nibble, then it shifts by that many texels and restarts. Still textures hold 0x10.
    /// </summary>
    public byte Move { get; }

    public int PaletteOffset { get; }
    public int LevelCount => _levels.Length;
    public TexelFormat TexelFormat => ToTexelFormat(Format);

    /// <summary>The size of the level-<paramref name="level"/> image.</summary>
    public (int Width, int Height) LevelSize(int level) => (_levels[level].Width, _levels[level].Height);

    /// <summary>Parses and validates a decompressed Borg1. Doesn't decode any texels.</summary>
    public static Borg1Texture Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg1 is at least 0x{HeaderSize:X} bytes, not {data.Length}.");

        var h = data.AsSpan();
        var format = (Borg1Format)BinaryPrimitives.ReadUInt16BigEndian(h);
        var flags = (Borg1Flags)BinaryPrimitives.ReadUInt16BigEndian(h[2..]);
        int width = h[4], height = h[5], lods = h[6];
        var move = h[7];
        var dlist = BinaryPrimitives.ReadUInt32BigEndian(h[8..]);
        var bitmap = BinaryPrimitives.ReadUInt32BigEndian(h[0xC..]);
        var palette = BinaryPrimitives.ReadUInt32BigEndian(h[0x10..]);

        if (!Enum.IsDefined(format))
            throw new InvalidDataException($"Unknown Borg1 format {(ushort)format}.");
        if (dlist != 0)
            throw new InvalidDataException("Borg1 textures with their own display list aren't supported (none are known).");
        if (bitmap != HeaderSize)
            throw new InvalidDataException($"The bitmap is at 0x{bitmap:X}, not straight after the header.");
        if (width == 0 || height == 0)
            throw new InvalidDataException($"Empty {width}x{height} texture.");

        var texelFormat = ToTexelFormat(format);
        var levels = new LevelLayout[lods + 1];
        var offset = HeaderSize;
        int w = width, ht = height;
        for (var i = 0; i < levels.Length; i++)
        {
            var stride = Math.Max(MinRowBytes, w * texelFormat.BitsPerTexel() / 8);
            levels[i] = new LevelLayout(w, ht, offset, stride);
            offset += stride * ht;
            w = Half(w);
            ht = Half(ht);
            if (i < levels.Length - 1 && (w == 0 || ht == 0))
                throw new InvalidDataException($"A {width}x{height} texture can't have {lods} mip levels.");
        }

        var paletteSize = texelFormat.PaletteSize();
        if (paletteSize == 0 ? palette != 0 : palette != offset)
            throw new InvalidDataException($"The palette is at 0x{palette:X}; expected 0x{(paletteSize == 0 ? 0 : offset):X}.");
        var end = offset + paletteSize * 2;
        if (data.Length < end || data.Length - end >= 8)
            throw new InvalidDataException($"A {width}x{height} {format} with {lods} mips is 0x{end:X} bytes, but the Borg is 0x{data.Length:X}.");

        return new Borg1Texture(data, format, flags, width, height, lods, move, (int)palette, levels);
    }

    /// <summary>Decodes one mip level; 0 is the full-size image.</summary>
    public RgbaImage Decode(int level = 0) => Decode(level, tmemInterleaved: true);

    /// <param name="tmemInterleaved">False only so tests can check that skipping the de-interleave is worse.</param>
    internal RgbaImage Decode(int level, bool tmemInterleaved)
    {
        var l = _levels[level];
        var palette = PaletteOffset == 0
            ? []
            : Texels.ReadPalette(_data.AsSpan(PaletteOffset), TexelFormat.PaletteSize());
        return Texels.Decode(TexelFormat, _data.AsSpan(l.Offset, l.Stride * l.Height), l.Width, l.Height, l.Stride,
            palette, tmemInterleaved);
    }

    public static TexelFormat ToTexelFormat(Borg1Format format) => format switch
    {
        Borg1Format.Rgba16 => TexelFormat.Rgba16,
        Borg1Format.Ia16 => TexelFormat.Ia16,
        Borg1Format.Ci8 => TexelFormat.Ci8,
        Borg1Format.Ia8 => TexelFormat.Ia8,
        Borg1Format.I8 => TexelFormat.I8,
        Borg1Format.Ci4 => TexelFormat.Ci4,
        Borg1Format.Ia4 => TexelFormat.Ia4,
        Borg1Format.I4 => TexelFormat.I4,
        Borg1Format.Rgba32 => TexelFormat.Rgba32,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>The game's mip halving: an odd result other than 1 rounds down to even.</summary>
    private static int Half(int x)
    {
        var r = x >> 1;
        return r != 1 && (r & 1) == 1 ? r - 1 : r;
    }

    private readonly record struct LevelLayout(int Width, int Height, int Offset, int Stride);
}
