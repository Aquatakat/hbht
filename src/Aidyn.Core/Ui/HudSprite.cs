using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// An image the trek HUD draws: its top-left at (<paramref name="X"/>, <paramref name="Y"/>) on the 320 × 240 screen, in
/// quarter pixels as the RDP takes them, at <paramref name="Scale"/>, times <paramref name="Color"/> (<c>Borg8_DrawSimple</c>),
/// and cut to <paramref name="Clip"/>'s pixels, inclusive, if it has one (<c>WidgetBorg8::Render</c>).
/// </summary>
/// <param name="Size">Its size as drawn, if it's stretched to one rather than scaled.</param>
public readonly record struct HudSprite(RecordId Image, float X, float Y, float Scale, Rgba Color, (float Width, float Height)? Size = null,
    ScreenRect? Clip = null)
{
    /// <summary>Where the RDP puts a position: <c>N64BorgImageDraw</c> takes it times 4, truncated.</summary>
    public static float Quarter(float at) => (int)(at * 4) / 4f;

    /// <summary>A colour times the screen's brightness, each channel truncated, as the HUD's are.</summary>
    public static Rgba Times(Rgba color, float brightness) =>
        new(Byte(color.R * brightness), Byte(color.G * brightness), Byte(color.B * brightness), Byte(color.A * brightness));

    /// <summary>A float colour channel as the original's <c>(u8)(int)</c>: truncated, and wrapped as a byte.</summary>
    public static byte Byte(float value) => (byte)(int)value;

    internal static RecordId Id(int index) => RecordId.Hex(RecordTypes.Image, index);
}
