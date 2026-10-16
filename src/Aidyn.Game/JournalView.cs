using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the journal (<see cref="JournalMenu"/>) on a <see cref="UiScreen"/>'s canvas, over the pause menu's tabs and
/// scroll: the book, <c>0x0182</c>, in the original's 320 × 240 frame at the screen's centre, its entries, and its
/// arrows. The menu fades out under it as it fades in, and back as it fades out, so the darkened world shows beside
/// it on a screen wider than the frame, rather than the scroll's rollers over it.
/// </summary>
public partial class JournalView : Control
{
    private const int FrameWidth = 320, FrameHeight = 240;

    // The arrows, and the strips of the book redrawn over the text at its edges, by how much of the book's alpha
    // (0x80052a90 on).
    private static readonly Vector2I BookUp = new(247, 30), BookDown = new(247, 184);
    private const int FadeLeft = 62, FadeRight = 245;
    private static readonly (int Top, int Bottom, float Share)[] EdgeFades =
    [
        (28, 33, 0.75f), (33, 35, 0.5f), (35, 37, 0.25f), (37, 40, 0.125f),
        (210, 213, 0.125f), (213, 215, 0.25f), (215, 217, 0.5f), (217, 220, 0.75f),
    ];

    private readonly UiPainter _ui;

    public JournalView(PluginSet plugins)
    {
        _ui = new UiPainter(plugins);
        Name = "Journal";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The journal showing, or null.</summary>
    public JournalMenu? Journal { get; set; }

    /// <summary>How far the book has faded in, from 0 to 1.</summary>
    public float Shown => Journal is { } journal ? journal.Alpha / 255f : 0;

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    /// <summary>
    /// The book at its alpha, the entries clipped to the list's bounds, the book's edges redrawn over them so they fade
    /// into the page, and the arrows, which show only once the book is opaque.
    /// </summary>
    public override void _Draw()
    {
        if (Journal is not { } journal)
            return;
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        DrawSetTransform(new Vector2I((size.X - FrameWidth) / 2, (size.Y - FrameHeight) / 2));
        var alpha = journal.Alpha / 255f;
        _ui.Image(this, MenuImages.Book, 0, 0, color: new Rgba(255, 255, 255, (byte)journal.Alpha));
        var bounds = new ScreenRect(JournalMenu.Left, JournalMenu.Top, JournalMenu.Right, JournalMenu.Bottom);
        foreach (var item in journal.Items)
        {
            var top = journal.ScreenTop(item);
            if (top + item.Height <= JournalMenu.Top || top >= JournalMenu.Bottom)
                continue;
            var color = item.Newest ? JournalMenu.NewestColor : JournalMenu.TextColor;
            _ui.Text(this, _ui.Main, item.Text, JournalMenu.Left, top, bounds, color with { A = (byte)(color.A * alpha) });
        }
        foreach (var (top, bottom, share) in EdgeFades)
        {
            _ui.Cut(this, MenuImages.Book, new Rect2I(FadeLeft, top, FadeRight - FadeLeft, bottom - top), FadeLeft, top,
                color: new Rgba(255, 255, 255, (byte)(journal.Alpha * share)));
        }
        if (journal.Alpha < 255)
            return;
        var arrow = WorldMenu.TextColor;
        _ui.Image(this, MenuImages.BookUp, BookUp.X, BookUp.Y, color: arrow with { A = (byte)journal.UpArrow });
        _ui.Image(this, MenuImages.BookDown, BookDown.X, BookDown.Y, color: arrow with { A = (byte)journal.DownArrow });
    }
}
