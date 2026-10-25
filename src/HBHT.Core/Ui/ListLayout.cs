namespace HBHT.Core.Ui;

/// <summary>
/// Where a menu's list sits in the 320 × 240 frame and how far apart its rows are, which the original fixes for each list
/// and ADR 0014 keeps at every screen size: the bounds it draws and takes the mouse in (right and bottom exclusive), and
/// the rows' pitch. The views draw a list and test the mouse against it from one of these, and the core sizes the
/// list's <see cref="MenuList{TRow}.Visible"/> from it, so scrolling needs no view.
/// </summary>
/// <remarks>
/// The list's arrows, which show rows are hidden, are 15 in from each side and 13 above its top (<c>WidgetScrollArrows</c>).
/// </remarks>
public readonly record struct ListLayout(ScreenRect Bounds, int RowHeight)
{
    /// <summary>How far in from the bounds' sides the arrows are, and how far above its top.</summary>
    public const int ArrowInset = 15, ArrowRise = 13;

    /// <summary>How many rows fit: the bounds' height over the pitch, which the original's lists show (8 of 13, 9 of 11, 6 of 13).</summary>
    public int Visible => (Bounds.Bottom - Bounds.Top) / RowHeight;

    /// <summary>Where the arrow for rows hidden above is drawn.</summary>
    public (int X, int Y) UpArrow => (Bounds.Left + ArrowInset, Bounds.Top - ArrowRise);

    /// <summary>Where the arrow for rows hidden below is drawn.</summary>
    public (int X, int Y) DownArrow => (Bounds.Right - ArrowInset, Bounds.Top - ArrowRise);

    /// <summary>The same list moved <paramref name="by"/> across, as it slides in or out.</summary>
    public ListLayout Shifted(int by) => this with { Bounds = new ScreenRect(Bounds.Left + by, Bounds.Top, Bounds.Right + by, Bounds.Bottom) };

    /// <summary>A list of rows, showing as many as fit.</summary>
    public MenuList<TRow> List<TRow>(IReadOnlyList<TRow> rows) => new(rows, Visible);

    /// <summary>
    /// The row of <paramref name="list"/> under a point in the frame, if the point is in the bounds and the list has
    /// a row there.
    /// </summary>
    public int? RowAt<TRow>(MenuList<TRow> list, float x, float y)
    {
        if (x < Bounds.Left || x >= Bounds.Right || y < Bounds.Top || y >= Bounds.Bottom)
            return null;
        var row = list.First + (int)(y - Bounds.Top) / RowHeight;
        return row < list.Rows.Count ? row : null;
    }
}
