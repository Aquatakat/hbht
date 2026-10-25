using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>A shadow drawn under text, in its colour and offset by so many pixels (<c>WidgetShadowText</c>).</summary>
public readonly record struct TextShadow(Rgba Color, int X, int Y);

/// <summary>
/// How a backdrop's picture number (the script's <see cref="DialogueRecord.Backdrop"/>, the original's
/// <c>libraryType</c>) dresses its page: the picture, the text's colour and shadow, what the cursor's choice pulses
/// between, and where the text wraps (<c>Backdrop.Page</c>, <c>0x80047930</c> in 1.0). The choices off the cursor are
/// in the text's colour, with its shadow.
/// </summary>
/// <param name="Picture">The opaque 320 × 240 image under the text.</param>
/// <param name="TextRight">Where the text wraps. The choices always wrap at <see cref="BackdropPage.Right"/>.</param>
public sealed record BackdropStyle(RecordId Picture, Rgba Text, Rgba PulseFrom, Rgba PulseTo, TextShadow? Shadow, int TextRight)
{
    private static readonly Rgba Black = new(0, 0, 0, 255);

    private static RecordId Image(int id) => RecordId.Hex(RecordTypes.Image, id);

    /// <summary>
    /// The journal's book in the defaults (<c>Backdrop.Widgets</c>), which picture 0 and every number without its own
    /// show, the help menu's 14 too: the cursor's choice pulses as a scroll menu's own does.
    /// </summary>
    public static readonly BackdropStyle Book = new(MenuImages.Book, new Rgba(103, 70, 60, 255), MenuPulse.ScrollFrom, MenuPulse.ScrollTo,
        null, BackdropPage.Right);

    /// <summary>The style for a script's picture number.</summary>
    public static BackdropStyle For(int? picture) => picture switch
    {
        1 => Book with { Picture = Image(0x3606), Text = new Rgba(54, 64, 0, 255), PulseFrom = Black, PulseTo = new Rgba(189, 99, 205, 255) },
        2 => Book with { Picture = Image(0x3603), Text = new Rgba(0, 41, 68, 255), PulseFrom = Black, PulseTo = new Rgba(173, 162, 51, 255) },
        3 => Book with { Picture = Image(0x3605) },
        // Only picture 4 narrows the text (0x80046ea0); 9 shows the same scroll at the full width.
        4 => Book with { Picture = Image(0x3608), TextRight = BackdropPage.NarrowRight },
        9 => Book with { Picture = Image(0x3608) },
        10 => Book with
        {
            Picture = Image(0x3602), Text = new Rgba(30, 26, 0, 255), PulseFrom = new Rgba(14, 0, 61, 255), PulseTo = new Rgba(254, 254, 222, 255),
            Shadow = new TextShadow(new Rgba(217, 209, 204, 177), 1, -1),
        },
        11 => Book with
        {
            Picture = Image(0x3607), Text = new Rgba(0, 0, 0, 255), PulseFrom = new Rgba(133, 13, 13, 255), PulseTo = new Rgba(226, 144, 63, 255),
            Shadow = new TextShadow(new Rgba(230, 225, 221, 177), 1, 1),
        },
        12 => Book with
        {
            Picture = Image(0x3604), Text = new Rgba(65, 27, 0, 255), PulseFrom = Black, PulseTo = new Rgba(220, 128, 30, 255),
            Shadow = new TextShadow(new Rgba(255, 242, 166, 204), 1, 1),
        },
        _ => Book,
    };
}

/// <summary>What a backdrop's page shows now: its text, and its choices, each cut to the scroll menu's bounds.</summary>
/// <param name="Highlight">The choice the cursor's on.</param>
public sealed record BackdropLayout(BackdropStyle Style, TextBlock Text, IReadOnlyList<TextBlock> Choices, int Highlight);

/// <summary>
/// A backdrop's or the help menu's page (<c>Backdrop.Widgets</c> <c>0x80046db8</c>, <c>Backdrop.Node</c>
/// <c>0x80047e14</c> in 1.0): a picture over the original's whole 320 × 240 frame, a node's text on it, and the
/// choices under the text in a scroll menu (<c>WidgetScrollMenu</c>). Laid out in the frame's own pixels; the runtime
/// puts the frame at the screen's centre.
/// </summary>
/// <remarks>
/// <para>
/// The text starts at (64, 30), wrapping and cut between x 64 and <see cref="BackdropStyle.TextRight"/> and down to
/// y 205. Each node's text replaces the last, with no paging. The scroll menu is from x 75 to 256 and y 55 to 205,
/// and its choices, up to 8, are stacked from 4 below the text, 4 between them, each wrapped between 75 and 256.
/// </para>
/// <para>
/// The list slides to keep the cursor's choice inside the menu (<c>WidgetScrollMenu.Step</c>, <c>0x800afe88</c>): by
/// as much as its foot is below 205, or its top above 55, which is what keeps the choices below 55 under a short
/// text. Choices outside are cut, not hidden. The slide is kept from node to node, as the original never resets it
/// (<c>WidgetScrollMenu.Clear</c>, <c>0x800b24e4</c>), so once a one-line text has pushed the list down to 55, a
/// taller text's choices are that much more than 4 below it. Up and down wrap from the last choice to the first and
/// back, and a new node puts the cursor on its first.
/// </para>
/// <para>
/// The port fades the page in and out, as the journal does, 60 an alpha a frame, which the original doesn't: its page
/// appears and goes at once. The user chose it (Oct 15, 2026).
/// </para>
/// </remarks>
public sealed class BackdropPage(TextLayout font, BackdropStyle style)
{
    public const int TextX = 64, TextY = 30, Right = 256, NarrowRight = 240, Bottom = 205;
    public const int ChoiceX = 75, ChoiceTop = 55, ChoiceGap = 4, RowGap = 4;

    private static readonly ScreenRect Menu = new(ChoiceX, ChoiceTop, Right, Bottom);

    private ScriptPage? _page;
    /// <summary>How far the list has slid from its place (the scroll menu's y offset).</summary>
    private int _slide;

    public BackdropStyle Style { get; } = style;

    /// <summary>What the page shows, or none before the first node.</summary>
    public BackdropLayout? Layout { get; private set; }

    /// <summary>The choice the cursor is on.</summary>
    public int Highlight { get; private set; }

    /// <summary>
    /// The cursor's pulse, a step a tick, as the menus' widgets step at 60 frames a second: 38 steps a cycle, 1.6 a
    /// second, which the user timed in the original's help menu at about 48 in 30 seconds (Oct 16, 2026).
    /// </summary>
    public MenuPulse Pulse { get; } = new();

    /// <summary>The page's alpha, which fades in as it opens and out once it closes, as the journal's does (<see cref="WidgetFade"/>).</summary>
    public int Alpha { get; private set; }

    public bool Closing { get; private set; }

    /// <summary>Whether it's faded out after closing.</summary>
    public bool Closed => Closing && Alpha == 0;

    /// <summary>The cursor's choice's colour now.</summary>
    public Rgba HighlightNow => Pulse.Now(Style.PulseFrom, Style.PulseTo);

    /// <summary>Shows a node's text and choices, with the cursor on the first.</summary>
    public BackdropLayout Show(ScriptPage page)
    {
        _page = page;
        Highlight = 0;
        return Lay();
    }

    /// <summary>Moves the cursor, which wraps from the last choice to the first and back.</summary>
    public void MoveCursor(int by)
    {
        if (_page is not { Choices.Count: > 0 } page)
            return;
        var count = page.Choices.Count;
        Highlight = ((Highlight + by) % count + count) % count;
        Lay();
    }

    /// <summary>The page fades out, as the script ends.</summary>
    public void Close() => Closing = true;

    /// <summary>A 60 Hz tick: the fade, and the cursor's pulse.</summary>
    public void Step()
    {
        Alpha = Closing ? WidgetFade.Out(Alpha) : WidgetFade.In(Alpha);
        Pulse.Step();
    }

    private BackdropLayout Lay()
    {
        var page = _page!;
        var text = new TextBlock(page.Line.Text, TextX, TextY, new ScreenRect(TextX, TextY, Style.TextRight, Bottom));
        var top = TextY + font.Height(page.Line.Text, TextX, Style.TextRight) + ChoiceGap;
        var heights = page.Choices.Select(c => font.Height(c.Text, ChoiceX, Right)).ToList();
        if (heights.Count > 0)
        {
            var cursorTop = top + _slide + heights.Take(Highlight).Sum(h => h + RowGap);
            if (cursorTop + heights[Highlight] > Bottom)
                _slide -= cursorTop + heights[Highlight] - Bottom;
            else if (cursorTop < ChoiceTop)
                _slide += ChoiceTop - cursorTop;
        }
        var choices = new List<TextBlock>();
        var y = top + _slide;
        for (var i = 0; i < heights.Count; i++)
        {
            choices.Add(new TextBlock(page.Choices[i].Text, ChoiceX, y, Menu));
            y += heights[i] + RowGap;
        }
        return Layout = new BackdropLayout(Style, text, choices, Highlight);
    }
}
