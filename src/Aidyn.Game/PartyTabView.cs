using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the pause menu's Party tab (<see cref="PartyMenu"/>) on a <see cref="UiScreen"/>'s canvas,
/// in one of two layouts chosen by the screen's size in its own pixels:
/// <list type="bullet">
/// <item><b>Classic</b>, the original's, in its 320 × 240 frame at the screen's centre.</item>
/// <item><b>Wide</b>, from <see cref="WideMinimum"/> up, as 16:9 at 360 lines gives: one sheet of
/// parchment across the screen with three columns, the member, the list in the main font with as
/// many rows as fit, and C-down's details, open for the cursor's row.</item>
/// </list>
/// Both place the same blocks. Each is painted on a piece of the tab's image, <c>0x00D9</c>, which
/// the classic layout draws whole and the wide one cuts out and places on its sheet, so the art is
/// the original's and the labels are its own.
/// </summary>
public partial class PartyTabView : Control
{
    /// <summary>The original's frame.</summary>
    private const int FrameWidth = 320, FrameHeight = 240;

    /// <summary>The smallest screen the wide layout fits.</summary>
    public static readonly Vector2I WideMinimum = new(560, 320);

    // The blocks, as pieces of 0x00D9 and what's drawn on them relative to each piece.

    /// <summary>The portraits' frames. DollMenuPortraits puts each portrait 2 inside its frame.</summary>
    private static readonly Rect2I PortraitPiece = new(18, 22, 134, 46);

    private static readonly Vector2I ShownPortrait = new(4, 4), NameAt = new(51, 6), IconsAt = new(4, 48);
    private static readonly Vector2I[] OtherPortraits = [new(49, 17), new(78, 17), new(107, 17)];
    private const int ShownSize = 40, OtherSize = 24;

    /// <summary>The hit points, level and gold labels and the bar's frame (WidgetHealthGold).</summary>
    private static readonly Rect2I HealthPiece = new(160, 22, 140, 58);

    private static readonly Vector2I HealthBarAt = new(60, 5), HealthBarSize = new(80, 10);
    private static readonly Vector2I HpAt = new(63, 19), MaxHpAt = new(95, 19), LevelAt = new(63, 33), GoldAt = new(63, 47);

    /// <summary>The silhouette and its slots' frames (DollEquipmentMenu::SetSlotCoords).</summary>
    private static readonly Rect2I SilhouettePiece = new(18, 72, 114, 142);

    private static readonly Vector2I[] Slots =
    [
        new(10, 47), new(10, 66), new(10, 85), new(40, 2), new(40, 24), new(30, 43), new(50, 43), new(40, 62),
        new(40, 120), new(70, 45), new(70, 66), new(70, 85), new(90, 47), new(90, 66), new(90, 85),
    ];

    private const int SlotSize = 16;

    /// <summary>
    /// The knotwork down the middle: its top, a twist of its braid, which repeats every 20 pixels, and
    /// its foot. The original's is the top, two twists and the foot.
    /// </summary>
    private static readonly Rect2I KnotTop = new(140, 98, 20, 20), KnotTwist = new(140, 118, 20, 20), KnotFoot = new(140, 158, 20, 50);

    /// <summary>The rules a list's title sits on, and plain parchment.</summary>
    private static readonly Rect2I RulesPiece = new(155, 84, 165, 16);

    private static readonly Rect2I TopEdge = new(0, 0, 320, 22), BottomEdge = new(0, 216, 236, 24), PlainPiece = new(236, 104, 60, 96);

    // DollMenuLists and WidgetTrainShop: the list's bounds; its title's centre is 70 into them and its
    // top 20 above, the arrows 15 in from each side and 13 above, and the arrow under the knotwork.
    private static readonly ScreenRect ClassicList = new(160, 101, 300, 205);
    private static readonly Vector2I SliderAt = new(126, 207);

    // WidgetItemDetail: the parchment, and its text's left and right edges and top.
    private static readonly Vector2I ClassicParchment = new(0, 58);
    private static readonly ScreenRect ClassicDetails = new(23, 70, 149, 218);
    private const int DetailsIndent = 16;

    /// <summary>The wide layout's gaps: round the sheet's columns, and between them, and how far a piece's edges fade.</summary>
    private const int WideMargin = 16, WideGap = 10, Feather = 6;

    /// <summary>The wide list's bottom margin, which holds the arrow under it.</summary>
    private const int SliderMargin = 30;

    private readonly UiPainter _ui;
    private ScreenRect _listBounds = ClassicList;
    private Vector2I _origin;

    public PartyTabView(PluginSet plugins, PartyMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "Party tab";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public PartyMenu Menu { get; }

    /// <summary>Whether C-down's details are showing, as they do while it's held. The wide layout always shows them.</summary>
    public bool DetailsOpen { get; set; }

    /// <summary>Whether combat opened the menu, which turns the arrow under the knotwork to point left only.</summary>
    public bool InCombat { get; set; }

    /// <summary>Whether the screen has room for the wide layout.</summary>
    public bool Wide => Size.X >= WideMinimum.X && Size.Y >= WideMinimum.Y;

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        Menu.Step();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Hovering a row puts the cursor on it, and the wheel moves it.
        if (@event is InputEventMouseMotion motion && RowAt(motion.Position) is { } row)
            Menu.Point(row);
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
            Menu.MoveCursor(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
        else
            return;
        QueueRedraw();
        AcceptEvent();
    }

    /// <summary>The list row under a point, if any.</summary>
    private int? RowAt(Vector2 point)
    {
        var at = point - (Vector2)_origin;
        var b = _listBounds;
        if (at.X < b.Left || at.X >= b.Right || at.Y < b.Top || at.Y >= b.Bottom)
            return null;
        var (first, count) = Menu.List switch
        {
            PartyList.Inventory => (Menu.Inventory.First, Menu.Inventory.Rows.Count),
            PartyList.Stats => (Menu.Stats.First, Menu.Stats.Rows.Count),
            PartyList.Skills => (Menu.Skills.First, Menu.Skills.Rows.Count),
            _ => (Menu.Spells.First, Menu.Spells.Rows.Count),
        };
        var row = first + (int)(at.Y - b.Top) / RowHeight(Menu.List);
        return row < count ? row : null;
    }

    /// <summary>A list's row height: 11 for stats, 13 for the rest.</summary>
    private static int RowHeight(PartyList list) => list == PartyList.Stats ? 11 : 13;

    public override void _Draw()
    {
        if (Menu.Shown is null)
            return;
        if (Wide)
        {
            _origin = Vector2I.Zero;
            DrawWide();
        }
        else
        {
            _origin = new Vector2I(((int)Size.X - FrameWidth) / 2, ((int)Size.Y - FrameHeight) / 2);
            DrawSetTransform(_origin);
            DrawClassic();
            DrawSetTransform(Vector2.Zero);
        }
    }

    private void DrawClassic()
    {
        _ui.Image(this, MenuImages.PartyTab, 0, 0);
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        Silhouette(SilhouettePiece.Position);
        List(ClassicList, _ui.Slim, Columns.Classic);
        _ui.Image(this, InCombat ? MenuImages.SliderLeft : MenuImages.SliderBoth, SliderAt.X, SliderAt.Y, color: PartyMenu.TextColor);
        if (DetailsOpen && Menu.Details() is { } details)
        {
            _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
            Details(details, ClassicDetails, _ui.Slim, classic: true);
        }
    }

    /// <summary>
    /// The wide layout: a sheet across the screen, with the member's column at the left (portraits,
    /// hit points, level and gold, the silhouette), the knotwork and the list beside it, and the
    /// details on their parchment at the right.
    /// </summary>
    private void DrawWide()
    {
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        Sheet(size);

        // The columns' widths: the member's, the knotwork's and the list's, about half the room left,
        // and the details' the rest, up to 300. Any room over centres them.
        var knotOffset = HealthPiece.Size.X + WideGap;
        var listOffset = knotOffset + KnotTop.Size.X + 4;
        var room = size.X - 2 * WideMargin - listOffset - WideGap;
        var listWidth = Math.Clamp(room * 11 / 20, 220, 300);
        var detailsWidth = Math.Min(room - listWidth + 8, 300);
        var left = WideMargin + Math.Max(0, (room - listWidth - detailsWidth + 8) / 2);
        // The member's column and the list sit a line of the slim font below the sheet's torn edge.
        var top = TopEdge.Size.Y + 2;
        var columnTop = top + _ui.Slim.LineHeight;
        Piece(PortraitPiece, left, columnTop);
        Portraits(new Vector2I(left, columnTop));
        var healthTop = columnTop + PortraitPiece.Size.Y + 14;
        Piece(HealthPiece, left, healthTop);
        Health(new Vector2I(left, healthTop), _ui.Main);
        var silhouetteTop = healthTop + HealthPiece.Size.Y + 2;
        // The silhouette is centred in the column.
        var silhouetteLeft = left + (HealthPiece.Size.X - SilhouettePiece.Size.X) / 2;
        Piece(SilhouettePiece, silhouetteLeft, silhouetteTop);
        Silhouette(new Vector2I(silhouetteLeft, silhouetteTop));

        var knotX = left + knotOffset;
        var listLeft = left + listOffset;
        var bottom = size.Y - BottomEdge.Size.Y;
        var rulesTop = columnTop + 4;
        var listTop = rulesTop + 17;
        // A deep margin under the list holds the arrow, centred on the screen: it says the tabs are either side.
        var rows = (bottom - SliderMargin - listTop) / 13 * 13;
        Rules(listLeft - 5, rulesTop, listWidth + 10);
        Knot(knotX, listTop - 3, rows + 4);
        var slider = InCombat ? MenuImages.SliderLeft : MenuImages.SliderBoth;
        _ui.Image(this, slider, (size.X - _ui.Size(slider).X) / 2, listTop + rows + (bottom - listTop - rows - _ui.Size(slider).Y) / 2,
            color: PartyMenu.TextColor);
        List(new ScreenRect(listLeft, listTop, listLeft + listWidth, listTop + rows), _ui.Main, Columns.Wide);

        var detailsLeft = listLeft + listWidth + WideGap;
        var detailsSize = new Vector2I(detailsWidth, bottom - top + 6);
        // Mirrored, so its wide torn edge is at the screen's edge rather than the list's.
        _ui.Parchment(this, detailsLeft, top - 4, detailsSize, mirror: true);
        if (Menu.Details() is { } details)
        {
            // The parchment's torn edges, scaled from the original's 23, 11, 12 and 5 of 160, left and right swapped.
            var area = new ScreenRect(detailsLeft + detailsSize.X * 11 / 160, top - 4 + detailsSize.Y * 12 / 160,
                detailsLeft + detailsSize.X - detailsSize.X * 23 / 160, top - 4 + detailsSize.Y - detailsSize.Y * 8 / 160);
            Details(details, area, _ui.Main, classic: false);
        }
    }

    /// <summary>
    /// The sheet: the tab image's torn top edge tiled across, its plain parchment stretched below it,
    /// and its torn bottom edge tiled along the foot.
    /// </summary>
    private void Sheet(Vector2I size)
    {
        var top = TopEdge.Size.Y;
        var bottom = size.Y - BottomEdge.Size.Y;
        _ui.Cut(this, MenuImages.PartyTab, PlainPiece, 0, top, new Vector2I(size.X, bottom - top));
        for (var x = 0; x < size.X; x += TopEdge.Size.X)
            _ui.Cut(this, MenuImages.PartyTab, TopEdge with { Size = new Vector2I(Math.Min(TopEdge.Size.X, size.X - x), TopEdge.Size.Y) }, x, 0);
        for (var x = 0; x < size.X; x += BottomEdge.Size.X)
            _ui.Cut(this, MenuImages.PartyTab, BottomEdge with { Size = new Vector2I(Math.Min(BottomEdge.Size.X, size.X - x), BottomEdge.Size.Y) }, x, bottom);
    }

    /// <summary>A piece of the tab's image, its edges faded into the sheet.</summary>
    private void Piece(Rect2I piece, int x, int y) => _ui.Feathered(this, MenuImages.PartyTab, piece, x, y, (Feather, Feather, Feather, Feather));

    /// <summary>The rules under a list's title, stretched along a line: its middle repeated, its ends faded.</summary>
    private void Rules(int x, int y, int width)
    {
        var end = 40;
        var middle = RulesPiece with { Position = RulesPiece.Position + new Vector2I(end, 0), Size = new Vector2I(RulesPiece.Size.X - 2 * end, RulesPiece.Size.Y) };
        _ui.Feathered(this, MenuImages.PartyTab, RulesPiece with { Size = new Vector2I(end, RulesPiece.Size.Y) }, x, y, (Feather, Feather, 0, Feather));
        for (var at = end; at < width - end; at += middle.Size.X)
        {
            var piece = middle with { Size = new Vector2I(Math.Min(middle.Size.X, width - end - at), middle.Size.Y) };
            _ui.Feathered(this, MenuImages.PartyTab, piece, x + at, y, (0, Feather, 0, Feather));
        }
        _ui.Feathered(this, MenuImages.PartyTab, RulesPiece with { Position = RulesPiece.Position + new Vector2I(RulesPiece.Size.X - end, 0), Size = new Vector2I(end, RulesPiece.Size.Y) },
            x + width - end, y, (0, Feather, Feather, Feather));
    }

    /// <summary>The knotwork, at least <paramref name="height"/> tall, with as many twists as it takes. Returns its foot.</summary>
    private int Knot(int x, int y, int height)
    {
        _ui.Feathered(this, MenuImages.PartyTab, KnotTop, x, y, (Feather, Feather, Feather, 0));
        var at = y + KnotTop.Size.Y;
        for (var twists = Math.Max(0, (height - KnotTop.Size.Y - KnotFoot.Size.Y + KnotTwist.Size.Y - 1) / KnotTwist.Size.Y); twists > 0; twists--)
        {
            _ui.Feathered(this, MenuImages.PartyTab, KnotTwist, x, at, (Feather, 0, Feather, 0));
            at += KnotTwist.Size.Y;
        }
        _ui.Feathered(this, MenuImages.PartyTab, KnotFoot, x, at, (Feather, 0, Feather, Feather));
        return at + KnotFoot.Size.Y;
    }

    /// <summary>The portraits from the member shown round, their name, and their aspect's and school's icons.</summary>
    private void Portraits(Vector2I at)
    {
        var shown = Menu.Shown!;
        var order = Menu.PortraitOrder;
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var member = Menu.Party.Slots[order[i]]!;
            if (Menu.Portrait(member.Entity) is not { } portrait)
                continue;
            var (offset, size) = i == 0 ? (ShownPortrait, ShownSize) : (OtherPortraits[Math.Min(i - 1, OtherPortraits.Length - 1)], OtherSize);
            _ui.Image(this, portrait, at.X + offset.X, at.Y + offset.Y, new Vector2I(size, size));
        }
        _ui.Text(this, _ui.Slim, shown.Name, at.X + NameAt.X, at.Y + NameAt.Y, PartyMenu.TextColor);
        var icon = at + IconsAt;
        if (MenuImages.AspectIcon(shown.Aspect) is { } aspect)
        {
            _ui.Image(this, aspect, icon.X, icon.Y);
            icon.X += _ui.Size(aspect).X + 2;
        }
        if (MenuImages.SchoolIcon(shown.School) is { } school)
            _ui.Image(this, school, icon.X, icon.Y);
    }

    /// <summary>The hit points bar, cut to the share left, and the hit points, level and gold.</summary>
    private void Health(Vector2I at, UiFont font)
    {
        var shown = Menu.Shown!;
        var bar = at + HealthBarAt;
        _ui.ImageTo(this, MenuImages.HealthBar, bar.X, bar.Y, HealthBarSize, bar.X + Menu.HealthBar(HealthBarSize.X));
        Number(font, shown.Hp, at + HpAt);
        Number(font, shown.MaxHp, at + MaxHpAt);
        Number(font, shown.Level, at + LevelAt);
        Number(font, Menu.Party.Gold, at + GoldAt);
    }

    /// <summary>The silhouette's slots: what's worn, or each empty slot's image.</summary>
    private void Silhouette(Vector2I at)
    {
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            _ui.Image(this, Menu.SlotImage(slot), at.X + Slots[(int)slot].X, at.Y + Slots[(int)slot].Y, new Vector2I(SlotSize, SlotSize));
    }

    private void Number(UiFont font, long value, Vector2I at) =>
        _ui.Text(this, font, value.ToString(System.Globalization.CultureInfo.InvariantCulture), at.X, at.Y, PartyMenu.TextColor);

    /// <summary>Where a list's columns go, from its left edge, and how far its names reach.</summary>
    private sealed record Columns(int Quantity, int Name, int NameNoQuantity, int Value, int Change, int SkillChange, int Rank, int SpellName, int NameRight)
    {
        /// <summary>The original's (<c>SMIItem</c>, <c>SMStatItem</c>, <c>WidgetSkillInfo</c>, <c>WidgetSpellEntry</c>).</summary>
        public static readonly Columns Classic = new(26, 42, 30, 79, 100, 95, 36, 50, 140);

        /// <summary>Room for the main font, with the numbers at the right.</summary>
        public static readonly Columns Wide = new(27, 44, 32, -70, -40, -40, 38, 56, -72);
    }

    /// <summary>The list showing, under its title, with its arrows and its rows in <paramref name="font"/>.</summary>
    private void List(ScreenRect b, UiFont font, Columns columns)
    {
        _listBounds = b;
        var title = Menu.List switch
        {
            PartyList.Inventory => MenuImages.InventoryTitle,
            PartyList.Stats => MenuImages.StatsTitle,
            PartyList.Skills => MenuImages.SkillsTitle,
            _ => MenuImages.SpellsTitle,
        };
        var wide = columns != Columns.Classic;
        var titleCentre = wide ? (b.Left + b.Right) / 2 : b.Left + 70;
        _ui.Image(this, title, titleCentre - _ui.Size(title).X / 2, b.Top - 20);
        var up = new Vector2I(b.Left + 15, b.Top - 13);
        var down = new Vector2I(b.Right - 15, b.Top - 13);

        // Negative columns count back from the right.
        int X(int column) => column < 0 ? b.Right + column : b.Left + column;
        var x = b.Left;
        var y = b.Top;
        var cursor = Menu.HighlightNow;
        var color = PartyMenu.TextColor;
        var names = new ScreenRect(b.Left, b.Top, X(columns.NameRight), b.Bottom);
        switch (Menu.List)
        {
            case PartyList.Inventory:
                Menu.Inventory.Visible = (b.Bottom - b.Top) / 13;
                _ui.List(Menu.Inventory, y, 13, color, cursor, (row, top, c) =>
                {
                    if (row.Wearer is { } wearer && Menu.Party.Slots[wearer] is { } who && Menu.Portrait(who.Entity) is { } portrait)
                        _ui.Image(this, portrait, x, top, new Vector2I(12, 12));
                    _ui.Image(this, row.Item.Icon, x + 13, top, new Vector2I(12, 12));
                    if (row.Quantity != 0)
                        _ui.Text(this, font, Num(row.Quantity), x + columns.Quantity, top, b, color);
                    _ui.Text(this, font, row.Item.Name, x + (row.Quantity != 0 ? columns.Name : columns.NameNoQuantity), top, b, c);
                });
                _ui.Arrows(this, Menu.Inventory, up, down);
                break;
            case PartyList.Stats:
                Menu.Stats.Visible = (b.Bottom - b.Top) / 11;
                _ui.List(Menu.Stats, y, 11, color, cursor, (row, top, c) =>
                {
                    _ui.Text(this, font, row.Name, x, top, names, c);
                    _ui.Text(this, font, Num(row.Value), X(columns.Value), top, b, color);
                    if (row.Change != 0)
                        _ui.Text(this, font, Change(row.Change), X(columns.Change), top, b, row.Change < 0 ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.Gain);
                });
                if (Menu.Experience is { } exp)
                {
                    // Below the rows, 4 apart, the values lined up after the first label.
                    var lineY = y + Menu.Stats.Rows.Count * 11 + 4;
                    var valueX = x + font.Width(exp.RemainingLabel) + 4;
                    foreach (var (label, value) in new[] { (exp.RemainingLabel, exp.Remaining), (exp.TotalLabel, exp.Total), (exp.NextLevelLabel, exp.NextLevel) })
                    {
                        _ui.Text(this, font, label, x, lineY, b, color);
                        _ui.Text(this, font, value, valueX, lineY, b, color);
                        lineY += font.LineHeight;
                    }
                }
                break;
            case PartyList.Skills:
                Menu.Skills.Visible = (b.Bottom - b.Top) / 13;
                _ui.List(Menu.Skills, y, 13, color, cursor, (row, top, c) =>
                {
                    if (row.Icon is { } icon)
                        _ui.Image(this, icon, x, top);
                    _ui.Text(this, font, row.Name, x + 12, top, names with { Left = x + 12 }, c);
                    _ui.Text(this, font, Num(row.Value), X(columns.Value), top, b, color);
                    if (row.Change != 0)
                        _ui.Text(this, font, Change(row.Change), X(columns.SkillChange), top, b, row.Change < 0 ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.Gain);
                });
                _ui.Arrows(this, Menu.Skills, up, down);
                break;
            default:
                Menu.Spells.Visible = (b.Bottom - b.Top) / 13;
                _ui.List(Menu.Spells, y, 13, color, cursor, (row, top, c) =>
                {
                    foreach (var (icon, dx) in new[] { (row.Aspect, 0), (row.School, 12), (row.Symbol, 24) })
                    {
                        if (icon is { } i)
                            _ui.Image(this, i, x + dx, top);
                    }
                    _ui.Text(this, font, Num(row.Rank), x + columns.Rank, top, b, color);
                    _ui.Text(this, font, row.Name, x + columns.SpellName, top, wide ? b : names with { Left = x + columns.SpellName }, c);
                });
                _ui.Arrows(this, Menu.Spells, up, down);
                break;
        }
    }

    /// <summary>
    /// C-down's details (<c>WidgetItemDetail</c>) in an area: a heading, the lines, and the footer,
    /// which the classic layout pins where the original does and the wide one at the area's foot.
    /// </summary>
    private void Details(MenuDetails details, ScreenRect area, UiFont font, bool classic)
    {
        var y = area.Top;
        if (details.Icon is { } icon)
        {
            // The icon, and the name level with its foot.
            var size = _ui.Size(icon);
            _ui.Image(this, icon, area.Left, y);
            y += size.Y + 2;
            if (details.Title is { } title)
                _ui.Text(this, font, title, area.Left + size.X + 2, y - font.LineHeight, PartyMenu.TextColor);
            y += 4;
        }
        foreach (var line in details.Lines)
            y += Line(line, y);

        var footerHeight = details.Footer.Count * font.LineHeight;
        var footerTop = classic
            ? details.FooterTop ?? (details.FooterBottom is { } bottom ? bottom - footerHeight : y)
            : Math.Max(y, area.Bottom - footerHeight);
        foreach (var line in details.Footer)
            footerTop += Line(line, footerTop);

        int Line(DetailLine line, int top)
        {
            var height = font.LineHeight;
            var x = area.Left + (line.Indent ? DetailsIndent : 0);
            foreach (var piece in line.Left)
            {
                switch (piece)
                {
                    case DetailText text when line.Wrap:
                        height = _ui.Text(this, font, text.Text, x, top, new ScreenRect(area.Left, 0, area.Right, (int)Size.Y), text.Color);
                        break;
                    case DetailText text:
                        _ui.Text(this, font, text.Text, x, top, text.Color);
                        x += font.Width(text.Text);
                        break;
                    case DetailImage image:
                        var size = image.Size is { } s ? new Vector2I(s, s) : _ui.Size(image.Image);
                        _ui.Image(this, image.Image, x, top, size);
                        x += size.X + 2;
                        break;
                }
            }
            var right = area.Right;
            foreach (var piece in line.Right.Reverse())
            {
                switch (piece)
                {
                    case DetailText text:
                        right -= font.Width(text.Text);
                        _ui.Text(this, font, text.Text, right, top, text.Color);
                        break;
                    case DetailImage image:
                        // An aspect's icon sits 2 higher, and its line is as tall as it is, plus 1.
                        var size = _ui.Size(image.Image);
                        right -= size.X;
                        _ui.Image(this, image.Image, right, top - 2);
                        height = size.Y + 1;
                        break;
                }
            }
            return height;
        }
    }

    private static string Num(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Change(int change) => change < 0 ? $"({Num(change)})" : $"(+{Num(change)})";
}
