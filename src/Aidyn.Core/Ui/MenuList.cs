namespace Aidyn.Core.Ui;

/// <summary>
/// A list with a cursor that scrolls to keep it in view (<c>WidgetFastScrollMenu</c>): the cursor
/// wraps from the last row to the first and back, and the list scrolls only as far as it must.
/// </summary>
/// <remarks>
/// How many rows show is the layout's: the original's lists are bounded at 104 pixels, eight rows
/// of 13 or nine of 11, and a taller layout shows more. The cursor pulses between the rows' colour
/// and <see cref="PartyMenu.HighlightColor"/>, as the conversation box's choices do.
/// </remarks>
public sealed class MenuList<TRow>
{
    private IReadOnlyList<TRow> _rows = [];
    private int _visible = int.MaxValue;

    public MenuList(IReadOnlyList<TRow> rows) => Rows = rows;

    public IReadOnlyList<TRow> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            Cursor = Math.Clamp(Cursor, 0, Math.Max(0, value.Count - 1));
            Scroll();
        }
    }

    /// <summary>The row the cursor is on. 0 in an empty list.</summary>
    public int Cursor { get; private set; }

    /// <summary>The first row in view.</summary>
    public int First { get; private set; }

    /// <summary>How many rows show at once.</summary>
    public int Visible
    {
        get => _visible;
        set
        {
            _visible = Math.Max(1, value);
            Scroll();
        }
    }

    public TRow? Current => _rows.Count == 0 ? default : _rows[Cursor];

    /// <summary>Whether rows are hidden above, or below.</summary>
    public bool MoreAbove => First > 0;

    public bool MoreBelow => First + _visible < _rows.Count;

    /// <summary>Moves the cursor by a row, wrapping round.</summary>
    public void Move(int by)
    {
        if (_rows.Count == 0)
            return;
        Cursor = ((Cursor + by) % _rows.Count + _rows.Count) % _rows.Count;
        Scroll();
    }

    /// <summary>Puts the cursor on a row, as hovering does.</summary>
    public void Point(int row)
    {
        if (_rows.Count == 0)
            return;
        Cursor = Math.Clamp(row, 0, _rows.Count - 1);
        Scroll();
    }

    /// <summary>
    /// Replaces the rows, keeping the cursor and the scroll as a member change does: past the end, the
    /// cursor goes to the last row, or with <paramref name="resetPastEnd"/>, back to the top.
    /// </summary>
    public void Replace(IReadOnlyList<TRow> rows, bool resetPastEnd)
    {
        if (resetPastEnd && Cursor >= rows.Count)
            (Cursor, First) = (0, 0);
        Rows = rows;
    }

    private void Scroll()
    {
        if (Cursor < First)
            First = Cursor;
        else if (Cursor >= First + _visible)
            First = Cursor - _visible + 1;
        First = Math.Clamp(First, 0, Math.Max(0, _rows.Count - _visible));
    }
}
