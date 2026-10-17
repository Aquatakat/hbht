using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the pause menu's World tab (<see cref="WorldMenu"/>) on a <see cref="UiScreen"/>'s canvas, in the original's
/// 320 × 240 frame at the screen's centre, on the tab's image, <c>0x00DA</c>: the map's pieces, the towns and the
/// party's ring, the season's name, today's square, Camp and Journal, and over them the refusal's message. The journal
/// is drawn over the whole menu, by <see cref="JournalView"/>.
/// </summary>
public partial class WorldTabView : Control
{
    private const int FrameWidth = 320, FrameHeight = 240;

    /// <summary>The season's name, wrapping between 160 and 260 (<c>0x800408a8</c>).</summary>
    private static readonly Vector2I SeasonName = new(160, 40);
    private const int SeasonRight = 260;
    private static readonly Vector2I Camp = new(215, 155), Journal = new(192, 174);
    private const int TodaySize = 17;

    /// <summary>
    /// The calendar's days and moons, painted into <c>0x00DA</c>, covered by plain parchment from the band above them,
    /// where the season's name is drawn by code, which the veil is drawn from so it fades them with the widgets, as
    /// <see cref="PartyTabView"/>'s does.
    /// </summary>
    private static readonly PlainImage.Patch[] PlainPatches =
    [
        new(new(155, 58, 265, 94), 155, 28), new(new(155, 94, 265, 130), 155, 28), new(new(155, 130, 265, 144), 155, 28),
    ];

    private readonly UiPainter _ui;

    public WorldTabView(PluginSet plugins, WorldMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "World tab";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _ui.Images.Plain(MenuImages.WorldTab, PlainPatches);
    }

    public WorldMenu Menu { get; set; }

    /// <summary>The main font, which the journal lays its entries out in.</summary>
    public TextLayout MainFont => _ui.Main.Layout;

    /// <summary>How far the widgets are faded into the parchment, from 0 to 1, as on <see cref="PartyTabView.Veil"/>.</summary>
    public float Veil { get; set; }

    /// <summary>
    /// Whether the tab is the one showing, or being slid to. Beside it, the party's ring isn't drawn and Camp and
    /// Journal don't pulse, though the cursor is kept.
    /// </summary>
    public bool Active { get; set; }

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
        var cursor = Active ? Menu.Pulse.Now(PartyMenu.HighlightFrom, PartyMenu.HighlightColor) : WorldMenu.TextColor;
        _ui.Image(this, MenuImages.CampTitle, Camp.X, Camp.Y, color: Menu.Cursor == WorldChoice.Camp ? cursor : WorldMenu.TextColor);
        _ui.Image(this, MenuImages.JournalTitle, Journal.X, Journal.Y, color: Menu.Cursor == WorldChoice.Journal ? cursor : WorldMenu.TextColor);
        if (Veil > 0)
            _ui.Plain(this, MenuImages.WorldTab, PlainPatches, 0, 0, new Rgba(255, 255, 255, (byte)(Veil * 255)));
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
        if (Active && Menu.Marker is { } marker)
            _ui.Image(this, Menu.Map.Marker, marker.X, marker.Y, color: Menu.MarkerNow);
    }
}
