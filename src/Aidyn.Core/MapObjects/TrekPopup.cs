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
/// choice, where the stick moves the cursor, A chooses and B closes it. It wraps the menus' own models
/// (<see cref="MenuMessage"/>, <see cref="MenuChoice"/>), which the painter draws alike.
/// </summary>
/// <remarks>
/// It differs from the menus' <see cref="MenuOverlay"/> on purpose. Trek has one popup at a time, so there's no stack,
/// and no fade (the original's popups fade, which the port leaves out); <see cref="MapObjectUse"/> closes it and runs
/// what was waiting on it. Its choice pulses a trek frame at a time (<see cref="Cadence.FrameTicks"/>), where the
/// menus' widgets step every tick, and a choice's question and options share one colour.
/// </remarks>
public sealed class TrekPopup
{
    /// <summary>A message's off-white (<c>COLOR_OFFWHITE</c>).</summary>
    public static readonly Rgba OffWhite = MenuMessage.Color;

    /// <summary>The trap's damage, in white.</summary>
    public static readonly Rgba White = MenuMessage.White;

    /// <summary>The trap menu's pale green, its question's and its choices' (<c>TrapMenu::InitMenu</c>).</summary>
    public static readonly Rgba PaleGreen = new(225, 255, 225, 255);

    /// <summary>The cursor's option pulses between a scroll menu's own colours, a step a trek frame.</summary>
    private readonly MenuPulse _pulse = new(Cadence.FrameTicks);

    private readonly MenuList<string> _noChoices = new([]);

    private TrekPopup(TrekPopupKind kind, MenuMessage? body, MenuChoice? question, Rgba color)
    {
        Kind = kind;
        Body = body;
        Question = question;
        Color = color;
    }

    public TrekPopupKind Kind { get; }

    /// <summary>What a message or the trap's damage says, wrapped as its <see cref="MenuMessage.Wrap"/> says; null for a choice.</summary>
    public MenuMessage? Body { get; }

    /// <summary>A question over choices, its cursor in <see cref="Choices"/>; null for a message.</summary>
    public MenuChoice? Question { get; }

    public string Text => (Body?.Text ?? Question?.Text)!;

    /// <summary>The widest the text wraps at.</summary>
    public int Wrap => Body?.Wrap ?? Question!.Width;

    /// <summary>The text's colour, and a choice's.</summary>
    public Rgba Color { get; }

    /// <summary>A choice's options, with the cursor; empty for a message.</summary>
    public MenuList<string> Choices => Question?.Choices ?? _noChoices;

    /// <summary>The space between a choice's question and its options.</summary>
    public int Gap => Question?.Gap ?? 0;

    /// <summary>The colour the cursor's option has now.</summary>
    public Rgba Highlight => _pulse.Now(MenuPulse.ScrollFrom, MenuPulse.ScrollTo);

    /// <summary>Steps the cursor's pulse by a 60 Hz tick.</summary>
    public void Step() => _pulse.Step();

    /// <summary>A message wrapped at <paramref name="wrap"/>, 150 for most (<c>TrekTextPopup</c>).</summary>
    public static TrekPopup Message(string text, int wrap = MenuMessage.MaxWidth) =>
        new(TrekPopupKind.Message, new MenuMessage(text, Wrap: wrap), null, OffWhite);

    public static TrekPopup Damage(string text) => new(TrekPopupKind.Damage, new MenuMessage(text, Wrap: int.MaxValue), null, White);

    public static TrekPopup Choice(string text, int width, int gap, Rgba color, IReadOnlyList<string> choices) =>
        new(TrekPopupKind.Choice, null, new MenuChoice(text, width, gap, color, color, new MenuList<string>(choices)), color);
}
