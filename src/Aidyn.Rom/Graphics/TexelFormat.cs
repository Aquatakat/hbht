namespace Aidyn.Rom.Graphics;

/// <summary>The N64 texel formats the game uses: an RDP format and size pair.</summary>
public enum TexelFormat
{
    Rgba16,
    Rgba32,
    Ia16,
    Ia8,
    Ia4,
    I8,
    I4,
    Ci8,
    Ci4,
}

public static class TexelFormats
{
    public static int BitsPerTexel(this TexelFormat format) => format switch
    {
        TexelFormat.Rgba32 => 32,
        TexelFormat.Rgba16 or TexelFormat.Ia16 => 16,
        TexelFormat.Ia8 or TexelFormat.I8 or TexelFormat.Ci8 => 8,
        TexelFormat.Ia4 or TexelFormat.I4 or TexelFormat.Ci4 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Palette entries a colour-indexed format needs, or 0.</summary>
    public static int PaletteSize(this TexelFormat format) => format switch
    {
        TexelFormat.Ci8 => 256,
        TexelFormat.Ci4 => 16,
        _ => 0,
    };
}
