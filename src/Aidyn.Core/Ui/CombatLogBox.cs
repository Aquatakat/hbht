using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>Where the battle's log box puts its panel, its text, and the portrait with its border, on the UI screen.</summary>
/// <param name="Portrait">The current combatant's portrait, or none before the first turn's line.</param>
public sealed record CombatLogBoxLayout(FeatheredPanel Panel, TextBlock Text, int BorderX, int BorderY, int PortraitX, int PortraitY, RecordId? Portrait);

/// <summary>
/// The battle's log box (<c>CombatGui.LogBoxCreate</c>, <c>0x80087A78</c>): a <c>TextBand</c>, the cinematics' caption box's
/// widget, that never drops away, with the current combatant's portrait in the conversation box's border.
/// </summary>
/// <remarks>
/// <para>
/// The original's rectangle is (0, 180) to (246, 240), with no growing: the band (<c>0x1C4</c>) runs right across the
/// frame from 180 down, the gradient (<c>0x1C3</c>) 11 tall above it, and the text, in the main font in
/// (225, 225, 225, 200), at (76, 190), wrapping at 241 and cut at 230. The border (<c>0xDF</c>, 52 square) is at
/// (20, 174) and the portrait (40 square) at (26, 180), level with the band's top; the rectangle's right keeps the text
/// off the radar. Its own lines are <see cref="Combat.CombatLog"/>'s.
/// </para>
/// <para>
/// The port floats it as the conversation box (<see cref="DialogueBox"/>), the user's choice, so the two read as one
/// panel: laid out in the original's frame at the bottom centre of the UI screen, its band from 16 to 304 and from 180 to
/// 228, inside where the overscan bars would be, feathered on every side with the conversation box's gradients, and the
/// border and portrait level with the box's top as the conversation box has them, at (20, 180) and (26, 186). The line
/// stays the original's, lowered to 190, as the box rarely holds more than one.
/// </para>
/// </remarks>
public static class CombatLogBox
{
    /// <summary>The band's top, and the text's place and bounds (the original's).</summary>
    public const int Top = 180, TextLeft = 76, TextTop = 190, TextRight = 241, TextBottom = 230;

    /// <summary>The border's and the portrait's places, the conversation box's: the original's are 6 higher, (20, 174) and (26, 180).</summary>
    public const int BorderX = 20, BorderY = 180, PortraitX = 26, PortraitY = 186;

    /// <summary>The line's colour.</summary>
    public static readonly Rgba TextColor = new(225, 225, 225, 200);

    /// <summary>The portrait's border (<c>BORG8_PortraitHighlight</c>), the conversation box's.</summary>
    public static readonly RecordId Border = HudSprite.Id(0x00DF);

    /// <summary>Lays the box out on a UI screen of the given size, in the original's frame at its bottom centre.</summary>
    public static CombatLogBoxLayout Layout(string text, RecordId? portrait, int screenWidth, int screenHeight)
    {
        var (x, y) = ((screenWidth - DialogueBox.FrameWidth) / 2, screenHeight - DialogueBox.FrameHeight);
        var band = new ScreenRect(x + DialogueBox.OverscanSide, y + Top, x + DialogueBox.FrameWidth - DialogueBox.OverscanSide,
            y + DialogueBox.FrameHeight - DialogueBox.OverscanBottom);
        return new CombatLogBoxLayout(
            new FeatheredPanel(band, DialogueBox.GradientHeight, DialogueBox.OverscanBottom, DialogueBox.OverscanSide, DialogueBox.OverscanSide),
            new TextBlock(text, x + TextLeft, y + TextTop, new ScreenRect(x + TextLeft, y + TextTop, x + TextRight, y + TextBottom)),
            x + BorderX, y + BorderY, x + PortraitX, y + PortraitY, portrait);
    }
}
