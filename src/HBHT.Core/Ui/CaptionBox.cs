using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>Where a cinematic's caption box puts its panel and its text on the UI screen.</summary>
public sealed record CaptionBoxLayout(FeatheredPanel Panel, TextBlock Text);

/// <summary>
/// The box a cinematic's captions show in, across the foot of the screen (<c>TextBand.Create</c>, <c>0x80055800</c>, as
/// <c>Cinematic.CaptionsCreate</c>, <c>0x800545d0</c>, makes it): a <c>TextBand</c> with its grow flag set, the widget the
/// battle's log box is too (<see cref="CombatLogBox"/>).
/// </summary>
/// <remarks>
/// <para>
/// The original's rectangle is (0, 200) to (300, 230) in its 320 × 240 frame, its text 25 in and 5 down in the main font,
/// in (225, 225, 225, 200), left aligned and wrapping at 295, 11 pixels a line. It grows upward to fit
/// (<c>TextBand.SetText</c>, <c>0x80055a80</c>): when the text and 5 above and below it are taller than 30, the top is
/// 230 less that, so two lines put it at 198 and four at 176. Over the band is the conversation box's gradient,
/// <c>0x1C3</c>, 11 tall. The band is drawn from x 0, under the overscan bar, and the bottom bar hides it below 228, so it
/// touches the screen's bottom and left; but <c>SetText</c> lays it out again to the rectangle's right, 300, where the
/// constructor had 320, so it stops 4 short of the right bar. The port takes that for a slip and runs it to the right as
/// well, as the user chose (Oct 17, 2026).
/// </para>
/// <para>
/// The port lays it out in the original's frame at the bottom centre of the UI screen, as the conversation box is, so the
/// text wraps as authored at any size. Like that box it floats rather than running under bars it doesn't draw: the band
/// is inside where the side bars would be, feathered beside it and above it with the popups' pieces, but it runs down
/// to the screen's bottom, which it's anchored to, with no feathering there. The user chose this shape (Oct 17, 2026).
/// The battle's log box is the same widget, floated as the conversation box instead.
/// </para>
/// </remarks>
public static class CaptionBox
{
    /// <summary>The original's rectangle, before it grows, and where its text starts inside it.</summary>
    public const int Top = 200, Bottom = 230, TextLeft = 25, TextRight = 295, Padding = 5;

    /// <summary>How tall the gradient over the band is.</summary>
    public const int GradientHeight = 11;

    /// <summary>The captions' colour.</summary>
    public static readonly Rgba TextColor = new(225, 225, 225, 200);

    /// <summary>
    /// Lays a caption out on a UI screen of the given size, in the original's frame at its bottom centre, with the band
    /// from inside the side bars down to the screen's foot.
    /// </summary>
    public static CaptionBoxLayout Layout(TextLayout font, string text, int screenWidth, int screenHeight)
    {
        var need = 2 * Padding + font.Height(text, TextLeft, TextRight);
        var top = need > Bottom - Top ? Bottom - need : Top;
        var (x, y) = ((screenWidth - DialogueBox.FrameWidth) / 2, screenHeight - DialogueBox.FrameHeight);
        var band = new ScreenRect(x + DialogueBox.OverscanSide, y + top, x + DialogueBox.FrameWidth - DialogueBox.OverscanSide, screenHeight);
        var textTop = y + top + Padding;
        return new CaptionBoxLayout(
            new FeatheredPanel(band, GradientHeight, 0, DialogueBox.OverscanSide, DialogueBox.OverscanSide),
            new TextBlock(text, x + TextLeft, textTop, new ScreenRect(x + TextLeft, textTop, x + TextRight, y + Bottom - Padding)));
    }
}
