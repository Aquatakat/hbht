using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>Text a box shows: where it starts, and the bounds it wraps at and is drawn inside.</summary>
public sealed record TextBlock(string Text, int X, int Y, ScreenRect Bounds);

/// <summary>
/// Where a conversation box puts everything on the 320 × 240 screen. The runtime draws, in order:
/// a black band across the screen from <see cref="Top"/> to <see cref="Bottom"/>, a black gradient
/// <see cref="DialogueBox.GradientHeight"/> tall above it, the portrait's border, the portrait, the
/// line and the choices.
/// </summary>
/// <param name="Speaker">Whose portrait shows: the line's speaker, or Alaron's once only the choices are up. None if the line has no speaker.</param>
/// <param name="Line">The line, or none once only the choices are up.</param>
/// <param name="Highlight">Which choice the cursor is on.</param>
public sealed record DialogueBoxLayout(
    int Top,
    int Bottom,
    RecordId? Speaker,
    int BorderX,
    int BorderY,
    int PortraitX,
    int PortraitY,
    TextBlock? Line,
    IReadOnlyList<TextBlock> Choices,
    int Highlight);

/// <summary>
/// The box a conversation shows its pages in, at the bottom of the screen with the speaker's portrait
/// on the left (<c>DiaMenuSub::InitDefault</c>, <c>DialougeClass::Setup</c>). It's for conversations
/// and the other modes that draw no backdrop; backdrops and the help menu use a full-screen image.
/// </summary>
/// <remarks>
/// The box grows upward to fit, and a line sits just above its choices. When a page's first choice
/// has the line's speaker, the line shows alone first, and A brings up the choices alone under
/// Alaron's portrait. Then the portrait and its border stay where the line put them, since the
/// original doesn't move them, but the box shrinks to the choices. The cursor's choice pulses from
/// tan to a dim brown and back, a step a frame (<c>WidgetScrollMenu::Tick</c>).
/// </remarks>
/// <param name="font">The font the box's text is in: the main font.</param>
/// <param name="borderWidth">The portrait border image's size, which the layout is built around.</param>
/// <param name="player">Alaron's entity, whose portrait shows over choices alone (<c>IDEntInd(Alaron)</c>).</param>
public sealed class DialogueBox(TextLayout font, int borderWidth, int borderHeight, RecordId player)
{
    /// <summary>Where the box's bottom part starts, before it grows to fit.</summary>
    public const int BaseTop = 180;

    public const int GradientHeight = 10;

    /// <summary>The line's colour.</summary>
    public static readonly Rgba LineColor = new(200, 200, 200, 255);

    /// <summary>The choices' colour (<c>COLOR_TAN</c>), which the cursor's choice pulses from.</summary>
    public static readonly Rgba ChoiceColor = new(200, 180, 100, 255);

    /// <summary>What the cursor's choice pulses to.</summary>
    public static readonly Rgba HighlightColor = new(50, 45, 25, 63);

    /// <summary>How many frames the pulse takes each way.</summary>
    public const int PulseFrames = 24;

    private const int Margin = 26, BorderX = 20, PortraitInset = 6, ChoiceIndent = 10, ChoiceSpacing = 4;

    private ScriptPage? _page;
    private bool _choicesAlone;
    private int _borderY = BaseTop;
    private int _pulse, _pulseSign = 1, _ticks;

    /// <summary>What the box shows now, or none before the first page.</summary>
    public DialogueBoxLayout? Layout { get; private set; }

    /// <summary>The choice the cursor is on.</summary>
    public int Highlight { get; private set; }

    /// <summary>Whether A moves the box on by itself, bringing up the choices, rather than the script.</summary>
    public bool ShowsLineBeforeChoices => _page is { Choices.Count: > 0 } page && !_choicesAlone && page.Choices[0].Speaker == page.Line.Speaker;

    /// <summary>Shows a new page, with the cursor on its first choice.</summary>
    public DialogueBoxLayout Show(ScriptPage page)
    {
        _page = page;
        _choicesAlone = false;
        Highlight = 0;
        _pulse = 0;
        _pulseSign = 1;
        return Lay();
    }

    /// <summary>Brings up the choices under a line that showed alone first.</summary>
    public DialogueBoxLayout ShowChoices()
    {
        if (!ShowsLineBeforeChoices)
            throw new InvalidOperationException("The page has no choices waiting behind its line.");
        _choicesAlone = true;
        return Lay();
    }

    /// <summary>Moves the cursor, which stops at the first and last choice.</summary>
    public void MoveCursor(int by)
    {
        if (Layout is { Choices.Count: > 0 } layout)
        {
            Highlight = Math.Clamp(Highlight + by, 0, layout.Choices.Count - 1);
            Layout = layout with { Highlight = Highlight };
        }
    }

    /// <summary>Advances the cursor's pulse by one 60 Hz tick.</summary>
    public void Step()
    {
        if (++_ticks < Cadence.FrameTicks)
            return;
        _ticks = 0;
        _pulse += _pulseSign;
        if (_pulse == 0 || _pulse >= PulseFrames - 1)
            _pulseSign = -_pulseSign;
    }

    /// <summary>
    /// The cursor's choice's colour now. Each channel moves by a whole step a frame, the difference
    /// over <see cref="PulseFrames"/> rounded toward zero.
    /// </summary>
    public Rgba HighlightNow => new(Blend(ChoiceColor.R, HighlightColor.R), Blend(ChoiceColor.G, HighlightColor.G),
        Blend(ChoiceColor.B, HighlightColor.B), Blend(ChoiceColor.A, HighlightColor.A));

    private byte Blend(byte from, byte to) => (byte)(from + (to - from) / PulseFrames * _pulse);

    private DialogueBoxLayout Lay()
    {
        var page = _page!;
        var bottom = BaseTop + borderHeight;
        var textLeft = borderWidth + Margin;
        var textRight = 320 - Margin;
        var top = bottom;
        var choices = new List<TextBlock>();

        var showChoices = page.Choices.Count > 0 && !ShowsLineBeforeChoices;
        if (showChoices)
        {
            var left = textLeft + ChoiceIndent;
            var menuBottom = BaseTop - 4 + borderHeight;
            var heights = page.Choices.Select(c => font.Height(c.Text, left, textRight)).ToList();
            top = menuBottom - (heights.Sum() + ChoiceSpacing * (heights.Count - 1));
            var y = top;
            for (var i = 0; i < heights.Count; i++)
            {
                choices.Add(new TextBlock(page.Choices[i].Text, left, y, new ScreenRect(left, top, textRight, menuBottom)));
                y += heights[i] + ChoiceSpacing;
            }
        }

        TextBlock? line = null;
        if (!_choicesAlone)
        {
            // The original works in u16s, so a line too tall for the screen wraps around and sits at the base.
            var textBottom = top - 2;
            var lineTop = textBottom - font.Height(page.Line.Text, textLeft, textRight) - 4;
            if (lineTop is < 0 or > BaseTop)
                lineTop = BaseTop;
            line = new TextBlock(page.Line.Text, textLeft, lineTop + 4, new ScreenRect(textLeft, lineTop + 4, textRight, textBottom));
            top = lineTop;
            _borderY = lineTop;
        }

        return Layout = new DialogueBoxLayout(top, bottom, _choicesAlone ? player : page.Line.Speaker,
            BorderX, _borderY, BorderX + PortraitInset, _borderY + PortraitInset, line, choices, Highlight);
    }
}
