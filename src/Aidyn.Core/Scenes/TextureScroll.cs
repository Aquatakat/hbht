namespace Aidyn.Core.Scenes;

/// <summary>
/// A texture that scrolls, such as water or fire (Borg1 flag 0x200). The game shifts its bitmap
/// sideways, wrapping, each time a scene loads it for drawing (<c>FUN_8009d3dc</c>,
/// <c>moveBitmap16</c>, <c>moveBitmap32</c>, checked against the debug ROM's machine code).
/// </summary>
/// <remarks>
/// <para>
/// The Borg1's scroll byte controls it, and the game writes to it as a counter. With a low nibble
/// of 0, each load shifts the image by the high nibble. Otherwise the high nibble counts loads up to
/// the low nibble, and on the load that finds them equal, it shifts by that many texels and starts
/// counting again. So 0x02 shifts 2 texels every third load, and 0x10 one texel every load.
/// </para>
/// <para>
/// A shift moves the image toward +S: after it, texel <c>s</c> holds what texel <c>s − n</c> held.
/// The game shifts the base level only, and none of the textures that scroll have mip levels. It
/// shifts 16-bit texels for RGBA16 and IA16 and 32-bit words for the rest, which is only right for
/// RGBA32, and those three formats are the only ones that scroll.
/// </para>
/// <para>
/// There's no check for a new frame. A texture that several scenes draw is loaded once by each,
/// and once per <c>LoadTexture</c> of it in each scene's instructions, so it scrolls faster the
/// more of them are drawn.
/// </para>
/// </remarks>
public sealed class TextureScroll(int width, byte move)
{
    /// <summary>The texture's width in texels.</summary>
    public int Width { get; } = width;

    /// <summary>The scroll byte, <c>Borg1Data.move</c>, whose high nibble counts loads.</summary>
    public byte Move { get; private set; } = move;

    /// <summary>
    /// How far the image has moved toward +S, 0 to <see cref="Width"/> − 1 texels: texel <c>s</c>
    /// shows what the stored image has at <c>s − Offset</c>.
    /// </summary>
    public int Offset { get; private set; }

    /// <summary>One load of the texture by a scene.</summary>
    public void Load()
    {
        int high = Move >> 4, low = Move & 0xF;
        if (low == 0 && high != 0)
        {
            Shift(high);
        }
        else if (high == low)
        {
            Shift(high);
            Move = (byte)low;
        }
        else
        {
            Move = (byte)(Move + 0x10);
        }
    }

    private void Shift(int texels) => Offset = (Offset + texels) % Width;
}
