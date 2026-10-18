using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>Text a box shows: where it starts, and the bounds it wraps at and is drawn inside.</summary>
public sealed record TextBlock(string Text, int X, int Y, ScreenRect Bounds);

/// <summary>
/// Where a conversation box puts everything on the UI screen. The runtime draws, in order: the
/// panel, the portrait's border, the portrait, the line and the choices.
/// </summary>
/// <param name="Speaker">Whose portrait shows: the line's speaker, or Alaron's once only the choices are up. None if the line has no speaker.</param>
/// <param name="Line">The line, or none once only the choices are up.</param>
/// <param name="Highlight">Which choice the cursor is on.</param>
public sealed record DialogueBoxLayout(
    FeatheredPanel Panel,
    RecordId? Speaker,
    int BorderX,
    int BorderY,
    int PortraitX,
    int PortraitY,
    TextBlock? Line,
    IReadOnlyList<TextBlock> Choices,
    int Highlight)
{
    /// <summary>The top of the panel's band.</summary>
    public int Top => Panel.Band.Top;

    /// <summary>The bottom of the panel's band.</summary>
    public int Bottom => Panel.Band.Bottom;
}

/// <summary>What A did on a page of a script (<see cref="DialogueBox.Accept"/>).</summary>
public enum DialogueAccept
{
    /// <summary>It brought up the choices behind a line that showed alone first. The script hasn't moved.</summary>
    ShowedChoices,
    /// <summary>The script took a choice, so there's a new page, or none.</summary>
    Chose,
    /// <summary>The script moved on from a line.</summary>
    Continued,
}

/// <summary>
/// The box a conversation shows its pages in, at the bottom of the screen with the speaker's portrait
/// on the left (<c>DiaMenuSub::InitDefault</c>, <c>DialougeClass::Setup</c>). It's for conversations
/// and the other modes that draw no backdrop; backdrops and the help menu use a full-screen image.
/// </summary>
/// <remarks>
/// <para>
/// The original lays the box out on its 320 × 240 screen, and so does this, in a frame of that size
/// at the bottom centre of the UI screen, so its text wraps as authored whatever the screen's size.
/// The original draws a translucent band right across the screen, with a gradient above it, and
/// the black bars it draws over the screen's edges for overscan cut it off (<c>Graphics::EndList</c>).
/// The port draws a floating panel instead, the band inside those bars with feathering where they
/// were, built from the original's own popup pieces. On a 4:3 screen it looks as the original's does
/// inside its bars; on a wider one it has margins either side.
/// </para>
/// <para>
/// The box grows upward to fit, and a line sits just above its choices. When a page's first choice
/// has the line's speaker, the line shows alone first, and A brings up the choices alone under
/// Alaron's portrait. Then the portrait and its border stay where the line put them, since the
/// original doesn't move them, but the box shrinks to the choices. The cursor's choice pulses from
/// tan to a dim brown and back, a step a frame (<c>WidgetScrollMenu::Tick</c>).
/// </para>
/// </remarks>
/// <param name="font">The font the box's text is in: the main font.</param>
/// <param name="borderWidth">The portrait border image's size, which the layout is built around.</param>
/// <param name="player">Alaron's entity, whose portrait shows over choices alone (<c>IDEntInd(Alaron)</c>).</param>
public sealed class DialogueBox(TextLayout font, int borderWidth, int borderHeight, RecordId player)
{
    /// <summary>Where the box's bottom part starts, before it grows to fit.</summary>
    public const int BaseTop = 180;

    /// <summary>The original's screen, which the box is laid out in.</summary>
    public const int FrameWidth = 320, FrameHeight = 240;

    /// <summary>How much of the frame's edges the original covers with black bars: 16 pixels either side and 12 at the bottom (1.0 and 1.1).</summary>
    public const int OverscanSide = 16, OverscanBottom = 12;

    /// <summary>How tall the gradient above the band is: its image, 16 tall, squeezed (<c>DiaMenuSub</c>).</summary>
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
    private int _screenWidth = FrameWidth, _screenHeight = FrameHeight;
    private bool _choicesAlone;
    private int _borderY = BaseTop;
    private readonly MenuPulse _pulse = new(Cadence.FrameTicks, PulseFrames);

    /// <summary>What the box shows now, or none before the first page.</summary>
    public DialogueBoxLayout? Layout { get; private set; }

    /// <summary>The choice the cursor is on.</summary>
    public int Highlight { get; private set; }

    /// <summary>Whether A moves the box on by itself, bringing up the choices, rather than the script.</summary>
    public bool ShowsLineBeforeChoices => _page is { Choices.Count: > 0 } page && !_choicesAlone && page.Choices[0].Speaker == page.Line.Speaker;

    /// <summary>Sets the UI screen's size, in its pixels, and lays the page out again.</summary>
    public void Resize(int width, int height)
    {
        (_screenWidth, _screenHeight) = (width, height);
        if (_page is not null)
            Lay();
    }

    /// <summary>Where the original's screen is on the UI screen: at the bottom, in the middle.</summary>
    public (int X, int Y) Frame => ((_screenWidth - FrameWidth) / 2, _screenHeight - FrameHeight);

    /// <summary>Shows a new page, with the cursor on its first choice.</summary>
    public DialogueBoxLayout Show(ScriptPage page)
    {
        _page = page;
        _choicesAlone = false;
        Highlight = 0;
        _pulse.Restart();
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

    /// <summary>
    /// Whether A takes one of the page's choices rather than moving the script on: the page has choices and the script
    /// isn't waiting for A, which a random line or an animation with no actor does with only its line up.
    /// </summary>
    public static bool PicksChoice(ScriptRunner runner) => runner.Page is { Choices.Count: > 0 } && runner.Wait != ScriptWait.Player;

    /// <summary>
    /// A on the page the script shows: brings up the choices behind a line that showed alone first, else takes the
    /// cursor's choice (<see cref="PicksChoice"/>) or moves the script on.
    /// </summary>
    /// <param name="runner">The script, showing a page.</param>
    /// <param name="press">What moves the script on, as a mode takes the press: a conversation's or a cut-scene's, which ask the speaker to talk. None runs it as it is.</param>
    public DialogueAccept Accept(ScriptRunner runner, Action<Action>? press = null)
    {
        if (!ShowsLineBeforeChoices)
            return Advance(runner, Highlight, press);
        (press ?? Run)(() => ShowChoices());
        return DialogueAccept.ShowedChoices;
    }

    /// <summary>
    /// A on a page that has no line to show alone first, as a backdrop's or the help menu's: takes the choice
    /// <paramref name="choice"/> if the page has choices to take (<see cref="PicksChoice"/>), else moves the script on.
    /// </summary>
    /// <param name="choice">Which choice to take: where the cursor is, or for the help menu's B, the last.</param>
    /// <param name="press">As in <see cref="Accept"/>.</param>
    public static DialogueAccept Advance(ScriptRunner runner, int choice, Action<Action>? press = null)
    {
        press ??= Run;
        if (!PicksChoice(runner))
        {
            press(runner.Continue);
            return DialogueAccept.Continued;
        }
        press(() => runner.Choose(choice));
        return DialogueAccept.Chose;
    }

    private static void Run(Action advance) => advance();

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
    public void Step() => _pulse.Step();

    /// <summary>
    /// The cursor's choice's colour now. Each channel moves by a whole step a frame, the difference
    /// over <see cref="PulseFrames"/> rounded toward zero.
    /// </summary>
    public Rgba HighlightNow => _pulse.Now(ChoiceColor, HighlightColor);

    private DialogueBoxLayout Lay()
    {
        var page = _page!;
        var bottom = BaseTop + borderHeight;
        var textLeft = borderWidth + Margin;
        var textRight = FrameWidth - Margin;
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

        // The band ends where the original's bars cut it, and the feathering fills where they were.
        var panel = new FeatheredPanel(new ScreenRect(OverscanSide, top, FrameWidth - OverscanSide, Math.Min(bottom, FrameHeight - OverscanBottom)),
            GradientHeight, OverscanBottom, OverscanSide, OverscanSide);
        var (frameX, frameY) = Frame;
        return Layout = new DialogueBoxLayout(panel.Offset(frameX, frameY), _choicesAlone ? player : page.Line.Speaker,
            BorderX + frameX, _borderY + frameY, BorderX + PortraitInset + frameX, _borderY + PortraitInset + frameY,
            line is null ? null : Shift(line, frameX, frameY), [.. choices.Select(c => Shift(c, frameX, frameY))], Highlight);
    }

    private static TextBlock Shift(TextBlock block, int x, int y) => block with
    {
        X = block.X + x,
        Y = block.Y + y,
        Bounds = new ScreenRect(block.Bounds.Left + x, block.Bounds.Top + y, block.Bounds.Right + x, block.Bounds.Bottom + y),
    };
}
