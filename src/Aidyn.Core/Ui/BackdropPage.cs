using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>A shadow drawn under text, in its colour and offset by so many pixels (<c>WidgetShadowText</c>).</summary>
public readonly record struct TextShadow(Rgba Color, int X, int Y);

/// <summary>
/// How a backdrop's picture number (the script's <see cref="DialogueRecord.Backdrop"/>, the original's
/// <c>libraryType</c>) dresses its page: the picture, the text's colour and shadow, what the cursor's choice pulses
/// between, and where the text wraps (<c>Backdrop.Page</c>, <c>0x80047930</c> in 1.0).
/// </summary>
/// <param name="Picture">The opaque 320 × 240 image under the text.</param>
/// <param name="Right">Where the text and the choices wrap.</param>
/// <param name="Choice">The choices' colour off the cursor.</param>
public sealed record BackdropStyle(RecordId Picture, Rgba Text, Rgba Choice, Rgba PulseFrom, Rgba PulseTo, TextShadow? Shadow, int Right)
{
    private static RecordId Image(int id) => RecordId.Hex(RecordTypes.Image, id);

    /// <summary>The journal's book, which picture 0 and any number without its own show, the help menu's too.</summary>
    public static readonly BackdropStyle Book = new(MenuImages.Book, new Rgba(103, 70, 60, 255), new Rgba(103, 70, 60, 255), MenuPulse.ScrollFrom, MenuPulse.ScrollTo,
        null, BackdropPage.Right);

    /// <summary>The style for a script's picture number.</summary>
    public static BackdropStyle For(int? picture) => picture switch
    {
        1 => Book with { Picture = Image(0x3606), Text = new Rgba(54, 64, 0, 255), PulseTo = new Rgba(189, 99, 205, 255) },
        2 => Book with { Picture = Image(0x3603), Text = new Rgba(0, 41, 68, 255), PulseTo = new Rgba(173, 162, 51, 255) },
        3 => Book with { Picture = Image(0x3605) },
        4 or 9 => Book with { Picture = Image(0x3608), Right = BackdropPage.NarrowRight },
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
            Picture = Image(0x3604), Text = new Rgba(65, 27, 0, 255), PulseTo = new Rgba(220, 128, 30, 255),
            Shadow = new TextShadow(new Rgba(255, 242, 166, 204), 1, 1),
        },
        _ => Book,
    };
}

/// <summary>What a backdrop's page shows now: its text, and the choices in the scroll menu's window.</summary>
/// <param name="Choices">The choices showing, the first of them <paramref name="First"/>.</param>
/// <param name="Highlight">The choice the cursor's on, counted from the page's first.</param>
public sealed record BackdropLayout(BackdropStyle Style, TextBlock Text, IReadOnlyList<TextBlock> Choices, int First, int Highlight);

/// <summary>
/// A backdrop's or the help menu's page (<c>Backdrop.Widgets</c> <c>0x80046db8</c>, <c>Backdrop.Node</c>
/// <c>0x80047e14</c> in 1.0): a picture over the original's whole 320 × 240 frame, a node's text on it, and the
/// choices under the text in a scroll menu (<c>WidgetScrollMenu</c>). Laid out in the frame's own pixels; the runtime
/// puts the frame at the screen's centre.
/// </summary>
/// <remarks>
/// <para>
/// The text starts at (64, 30), wrapping and cut between x 64 and <see cref="BackdropStyle.Right"/> and down to
/// y 205. Each node's text replaces the last, with no paging. The choices are 4 below the text, and never above
/// y 55, from x 75, 4 between them, up to 8 showing.
/// </para>
/// <para>
/// The port fades the page in and out, as the journal does, 60 an alpha a frame, which the original doesn't: its page
/// appears and goes at once. The user chose it (Oct 15, 2026).
/// </para>
/// </remarks>
public sealed class BackdropPage(TextLayout font, BackdropStyle style)
{
    public const int TextX = 64, TextY = 30, Right = 256, NarrowRight = 240, Bottom = 205;
    public const int ChoiceX = 75, ChoiceTop = 55, ChoiceGap = 4, RowGap = 4, Rows = 8;

    /// <summary>How much the page's alpha changes a frame as it fades in or out: the journal's.</summary>
    public const int Fade = JournalMenu.Fade;

    private ScriptPage? _page;
    private int _first;

    public BackdropStyle Style { get; } = style;

    /// <summary>What the page shows, or none before the first node.</summary>
    public BackdropLayout? Layout { get; private set; }

    /// <summary>The choice the cursor is on.</summary>
    public int Highlight { get; private set; }

    public MenuPulse Pulse { get; } = new();

    /// <summary>The page's alpha, which fades in as it opens and out once it closes.</summary>
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
        _first = 0;
        Pulse.Reset();
        return Lay();
    }

    /// <summary>Moves the cursor, which stops at the first and last choice; the window follows it.</summary>
    public void MoveCursor(int by)
    {
        if (_page is not { Choices.Count: > 0 } page)
            return;
        Highlight = Math.Clamp(Highlight + by, 0, page.Choices.Count - 1);
        if (Highlight < _first)
            _first = Highlight;
        else if (Highlight >= _first + Rows)
            _first = Highlight - Rows + 1;
        Lay();
    }

    /// <summary>The page fades out, as the script ends.</summary>
    public void Close() => Closing = true;

    /// <summary>A 60 Hz tick: the fade, and the cursor's pulse.</summary>
    public void Step()
    {
        Alpha = Closing ? (Alpha < Fade + 1 ? 0 : Alpha - Fade) : Alpha < 255 - Fade ? Alpha + Fade : 255;
        Pulse.Step();
    }

    private BackdropLayout Lay()
    {
        var page = _page!;
        var text = new TextBlock(page.Line.Text, TextX, TextY, new ScreenRect(TextX, TextY, Style.Right, Bottom));
        var height = page.Line.Text.Length == 0 ? 0 : font.Height(page.Line.Text, TextX, Style.Right);
        var top = Math.Max(TextY + height + ChoiceGap, ChoiceTop);
        var choices = new List<TextBlock>();
        var y = top;
        foreach (var choice in page.Choices.Skip(_first).Take(Rows))
        {
            choices.Add(new TextBlock(choice.Text, ChoiceX, y, new ScreenRect(ChoiceX, top, Style.Right, Bottom)));
            y += font.Height(choice.Text, ChoiceX, Style.Right) + RowGap;
        }
        return Layout = new BackdropLayout(Style, text, choices, _first, Highlight);
    }
}
