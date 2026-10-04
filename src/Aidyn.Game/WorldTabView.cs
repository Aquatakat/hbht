using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the pause menu's World tab (<see cref="WorldMenu"/>) on a <see cref="UiScreen"/>'s canvas, in the original's
/// 320 × 240 frame at the screen's centre, on the tab's image, <c>0x00DA</c>: the map's pieces, the towns and the
/// party's ring, the season's name, today's square, Camp and Journal, and over them the journal's book
/// (<see cref="JournalMenu"/>) and the refusal's message.
/// </summary>
public partial class WorldTabView : Control
{
    private const int FrameWidth = 320, FrameHeight = 240;

    /// <summary>The season's name, wrapping between 160 and 260 (<c>0x800408a8</c>).</summary>
    private static readonly Vector2I SeasonName = new(160, 40);
    private const int SeasonRight = 260;
    private static readonly Vector2I Camp = new(215, 155), Journal = new(192, 174);
    private const int TodaySize = 17;

    // The journal: its arrows, and the strips of the book redrawn over the text at its edges, by how much of the
    // book's alpha (0x80052a90 on).
    private static readonly Vector2I BookUp = new(247, 30), BookDown = new(247, 184);
    private const int FadeLeft = 62, FadeRight = 245;
    private static readonly (int Top, int Bottom, float Share)[] EdgeFades =
    [
        (28, 33, 0.75f), (33, 35, 0.5f), (35, 37, 0.25f), (37, 40, 0.125f),
        (210, 213, 0.125f), (213, 215, 0.25f), (215, 217, 0.5f), (217, 220, 0.75f),
    ];

    private readonly UiPainter _ui;

    public WorldTabView(PluginSet plugins, WorldMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "World tab";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public WorldMenu Menu { get; set; }

    /// <summary>The main font, which the journal lays its entries out in.</summary>
    public TextLayout MainFont => _ui.Main.Layout;

    /// <summary>How far the widgets are faded into the parchment, from 0 to 1, as on <see cref="PartyTabView.Veil"/>.</summary>
    public float Veil { get; set; }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _Draw()
    {
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        var origin = new Vector2I((size.X - FrameWidth) / 2, (size.Y - FrameHeight) / 2);
        DrawSetTransform(origin);
        _ui.Image(this, MenuImages.WorldTab, 0, 0);
        Map();
        _ui.Text(this, _ui.Main, Menu.SeasonName, SeasonName.X, SeasonName.Y,
            new ScreenRect(SeasonName.X, SeasonName.Y, SeasonRight, FrameHeight), WorldMenu.TextColor);
        _ui.Image(this, MenuImages.Today, Menu.Today.X, Menu.Today.Y, new Vector2I(TodaySize, TodaySize));
        var cursor = Menu.Pulse.Now(PartyMenu.HighlightFrom, PartyMenu.HighlightColor);
        _ui.Image(this, MenuImages.CampTitle, Camp.X, Camp.Y, color: Menu.Cursor == WorldChoice.Camp ? cursor : WorldMenu.TextColor);
        _ui.Image(this, MenuImages.JournalTitle, Journal.X, Journal.Y, color: Menu.Cursor == WorldChoice.Journal ? cursor : WorldMenu.TextColor);
        if (Veil > 0)
            _ui.Image(this, MenuImages.WorldTab, 0, 0, color: new Rgba(255, 255, 255, (byte)(Veil * 255)));
        if (Menu.Journal is { } journal)
            Book(journal);
        DrawSetTransform(Vector2.Zero);
        if (Menu.Message is { } message)
            _ui.Message(this, size, message.Text, MenuMessage.MaxWidth, MenuMessage.Color);
    }

    /// <summary>The pieces found, tinted as the tab's text and fading in; the towns' names; and the party's ring.</summary>
    private void Map()
    {
        var tint = WorldMenu.TextColor;
        var pieces = new Rgba(tint.R, tint.G, tint.B, (byte)Menu.PieceAlpha);
        foreach (var piece in Menu.Pieces)
            _ui.Image(this, piece.Image, piece.X, piece.Y, color: pieces);
        foreach (var town in Menu.Map.Towns)
            _ui.Image(this, town.Image, town.X, town.Y);
        if (Menu.Marker is { } marker)
            _ui.Image(this, Menu.Map.Marker, marker.X, marker.Y, color: Menu.MarkerNow);
    }

    /// <summary>
    /// The journal: the book at its alpha, the entries clipped to the list's bounds, the book's edges redrawn over
    /// them so they fade into the page, and the arrows, which show only once the book is opaque.
    /// </summary>
    private void Book(JournalMenu journal)
    {
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
