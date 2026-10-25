using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>An item of the journal's list: an entry, the blank line at either end, or the note that there are none.</summary>
/// <param name="Top">Where it starts, from the list's top.</param>
public sealed record JournalItem(string Text, int Top, int Height, bool Newest)
{
    public int Bottom => Top + Height;
}

/// <summary>
/// The journal (<c>Journal</c>, <c>InitMenu</c> at <c>0x80052228</c> in 1.0): the entries in a list that scrolls by entry,
/// over the book, which fades in and out. Positions are in the 320 × 240 frame.
/// </summary>
/// <remarks>
/// The original lists the entries whose flags are set in its table's order; the port in the order they were added
/// (<see cref="Flags.FlagNetwork.JournalOrder"/>), which the user chose.
/// </remarks>
public sealed class JournalMenu
{
    public const int Left = 64, Right = 245, Top = 30, Bottom = 220;
    /// <summary>How far the list's foot may rise from its top: the bounds' height (<c>boundY1 − boundY0</c>).</summary>
    public const int View = Bottom - Top;
    /// <summary>Between entries (<c>vSpace</c>).</summary>
    public const int Gap = 4;
    /// <summary>How far the list slides a tick toward where it's going (<c>scrollSpeed</c>).</summary>
    public const int ScrollSpeed = 5;
    /// <summary>The arrows' fade a tick, half the book's (<see cref="WidgetFade.Step"/>).</summary>
    public const int ArrowFade = 30;

    public static readonly Rgba TextColor = new(98, 70, 60, 255), NewestColor = new(130, 80, 140, 255);

    private int _target;

    /// <param name="entries">The entries' text in order, and which is the newest.</param>
    /// <param name="noEntries">What's said when there are none (common line 496).</param>
    public JournalMenu(TextLayout font, IReadOnlyList<(string Text, bool Newest)> entries, string noEntries)
    {
        var items = new List<JournalItem>();
        var y = 0;
        void Add(string text, bool newest = false)
        {
            if (items.Count > 0)
                y += Gap;
            var height = font.Height(text, Left, Right);
            items.Add(new JournalItem(text, y, height, newest));
            y += height;
        }
        Add("  ");
        foreach (var (text, newest) in entries)
            Add(text, newest);
        if (items.Count == 1)
            Add(noEntries);
        Add("  ");
        Items = items;
        ListHeight = y;

        // On the newest, or the last; at the foot if that's below the view, as it is once the list is long.
        Highlight = items.FindIndex(i => i.Newest) is var newestAt and >= 0 ? newestAt : items.Count - 1;
        if (items[Highlight].Bottom > View)
            Scroll = _target = View - ListHeight;
    }

    public IReadOnlyList<JournalItem> Items { get; }

    /// <summary>The list's height, from the first item's top to the last's foot.</summary>
    public int ListHeight { get; }

    /// <summary>The item the cursor's on, which scrolling moves from (<c>itemHighlight</c>).</summary>
    public int Highlight { get; private set; }

    /// <summary>How far the list is moved down from its place: 0 at the top, negative scrolled toward the end (<c>yCurr</c>).</summary>
    public int Scroll { get; private set; }

    /// <summary>The book's alpha, which fades in as it opens and out as it closes (<see cref="WidgetFade"/>).</summary>
    public int Alpha { get; private set; }

    public bool Closing { get; private set; }

    /// <summary>Whether it's faded out after closing.</summary>
    public bool Closed => Closing && Alpha == 0;

    /// <summary>The arrows' alphas, fading in while there's more above or below.</summary>
    public int UpArrow { get; private set; }

    public int DownArrow { get; private set; }

    /// <summary>Where an item is on the screen.</summary>
    public int ScreenTop(JournalItem item) => Top + item.Top + Scroll;

    private bool Moving => Scroll != _target;

    /// <summary>
    /// Down (<c>WSL_DownFunc</c>): the next entry whose foot is below the view comes up until its foot meets it. A press
    /// waits for the list to stop.
    /// </summary>
    public void Down()
    {
        if (Moving || Closing)
            return;
        for (var i = Highlight + 1; i < Items.Count; i++)
        {
            var foot = ScreenTop(Items[i]) + Items[i].Height;
            if (foot <= Bottom)
                continue;
            Highlight = i;
            _target = Math.Max(_target - (foot - Bottom), View - ListHeight);
            return;
        }
    }

    /// <summary>Up (<c>WSL_UpFunc</c>): the first entry back whose top is above the view comes down until its top meets it.</summary>
    public void Up()
    {
        if (Moving || Closing)
            return;
        for (var i = Highlight - 1; i >= 0; i--)
        {
            var top = ScreenTop(Items[i]);
            if (top >= Top)
                continue;
            Highlight = i;
            _target = Math.Min(_target + (Top - top), 0);
            return;
        }
    }

    /// <summary>A, B or Start: the book fades out, back to the tab.</summary>
    public void Close() => Closing = true;

    /// <summary>
    /// A 60 Hz tick: the fade, the list sliding toward where it's going, and the arrows. A direction held keeps the list
    /// moving with no wait (<c>Journal::Control</c> reads the held bits).
    /// </summary>
    public void Step(bool upHeld = false, bool downHeld = false)
    {
        Alpha = Closing ? WidgetFade.Out(Alpha) : WidgetFade.In(Alpha);
        if (Moving)
            Scroll = Scroll < _target ? Math.Min(Scroll + ScrollSpeed, _target) : Math.Max(Scroll - ScrollSpeed, _target);
        else if (upHeld)
            Up();
        else if (downHeld)
            Down();
        UpArrow = Toward(UpArrow, ScreenTop(Items[0]) < Top);
        DownArrow = Toward(DownArrow, ScreenTop(Items[^1]) + Items[^1].Height > Bottom);

        static int Toward(int alpha, bool on) => on ? WidgetFade.In(alpha, ArrowFade) : WidgetFade.Out(alpha, ArrowFade);
    }
}
