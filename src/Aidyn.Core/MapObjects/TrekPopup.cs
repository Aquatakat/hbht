using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.MapObjects;

/// <summary>How a trek popup is drawn.</summary>
public enum TrekPopupKind
{
    /// <summary>
    /// A message in a box at the screen's centre (<c>TextBox_Centered</c>): as wide as its text up to
    /// <see cref="TrekPopup.Wrap"/>, wrapping there, and as tall as that plus 5, with the text 4 in.
    /// </summary>
    Message,

    /// <summary>
    /// What a trap did (<c>TrapMenu::TakeDamage</c>): a box 16 wider than its widest line, and as tall as
    /// its lines times the first line's height plus 2, unwrapped.
    /// </summary>
    Damage,

    /// <summary>
    /// A question over choices on a feathered panel at the screen's centre (<c>WidgetChoiceDia</c>): the
    /// text wrapped to <see cref="TrekPopup.Wrap"/>, then <see cref="TrekPopup.Gap"/>, then the choices.
    /// </summary>
    Choice,
}

/// <summary>
/// A popup over the world in trek, which stops it until closed: a message, which A or B closes, or a
/// choice, where the stick moves the cursor, A chooses and B closes it.
/// </summary>
public sealed class TrekPopup
{
    /// <summary>A message's off-white (<c>COLOR_OFFWHITE</c>).</summary>
    public static readonly Rgba OffWhite = MenuMessage.Color;

    /// <summary>The trap's damage, in white.</summary>
    public static readonly Rgba White = new(255, 255, 255, 255);

    /// <summary>The trap menu's pale green, its question's and its choices' (<c>TrapMenu::InitMenu</c>).</summary>
    public static readonly Rgba PaleGreen = new(225, 255, 225, 255);

    private TrekPopup(TrekPopupKind kind, string text, int wrap, Rgba color, IReadOnlyList<string> choices, int gap)
    {
        Kind = kind;
        Text = text;
        Wrap = wrap;
        Color = color;
        Choices = new MenuList<string>(choices);
        Gap = gap;
    }

    public TrekPopupKind Kind { get; }

    public string Text { get; }

    /// <summary>The widest the text wraps at.</summary>
    public int Wrap { get; }

    /// <summary>The text's colour, and a choice's.</summary>
    public Rgba Color { get; }

    /// <summary>A choice's options, with the cursor; empty for a message.</summary>
    public MenuList<string> Choices { get; }

    /// <summary>The space between a choice's question and its options.</summary>
    public int Gap { get; }

    /// <summary>A message wrapped at <paramref name="wrap"/>, 150 for most (<c>TrekTextPopup</c>).</summary>
    public static TrekPopup Message(string text, int wrap = MenuMessage.MaxWidth) => new(TrekPopupKind.Message, text, wrap, OffWhite, [], 0);

    public static TrekPopup Damage(string text) => new(TrekPopupKind.Damage, text, int.MaxValue, White, [], 0);

    public static TrekPopup Choice(string text, int width, int gap, Rgba color, IReadOnlyList<string> choices) =>
        new(TrekPopupKind.Choice, text, width, color, choices, gap);
}
