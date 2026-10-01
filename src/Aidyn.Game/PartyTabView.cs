using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the pause menu's Party tab (<see cref="PartyMenu"/>) on a <see cref="UiScreen"/>'s canvas.
/// The classic layout is the original's, in its 320 × 240 frame at the screen's centre; a screen with
/// room for it gets the wide layout.
/// </summary>
public partial class PartyTabView : Control
{
    /// <summary>The original's frame.</summary>
    private const int FrameWidth = 320, FrameHeight = 240;

    // DollMenuPortraits: the member shown's portrait, and the others', each 2 inside its frame.
    private static readonly Vector2I ShownPortrait = new(22, 26), NameAt = new(69, 28);
    private static readonly Vector2I[] OtherPortraits = [new(67, 39), new(96, 39), new(125, 39)];
    private const int ShownSize = 40, OtherSize = 24;

    // WidgetHealthGold.
    private static readonly Vector2I HealthBarAt = new(220, 27), HealthBarSize = new(80, 10);
    private static readonly Vector2I LevelAt = new(223, 55), GoldAt = new(223, 69), HpAt = new(223, 41), MaxHpAt = new(255, 41);

    // DollEquipmentMenu::SetSlotCoords, by EquipmentSlot.
    private static readonly Vector2I[] Slots =
    [
        new(28, 119), new(28, 138), new(28, 157), new(58, 74), new(58, 96), new(48, 115), new(68, 115), new(58, 134),
        new(58, 192), new(88, 117), new(88, 138), new(88, 157), new(108, 119), new(108, 138), new(108, 157),
    ];

    private const int SlotSize = 16;

    // DollMenuLists and WidgetTrainShop: the list's place and bounds, its title centred at 230, and the arrows.
    private static readonly ScreenRect ListBounds = new(160, 101, 300, 205);
    private const int TitleCentre = 230, TitleTop = 81;
    private static readonly Vector2I UpArrow = new(175, 88), DownArrow = new(285, 88), SliderAt = new(126, 207);

    // WidgetItemDetail: the parchment, and its text's left and right edges and top.
    private static readonly Vector2I ParchmentAt = new(0, 58);
    private const int DetailsLeft = 23, DetailsRight = 149, DetailsTop = 70, DetailsIndent = 16;

    private readonly UiPainter _ui;

    public PartyTabView(PluginSet plugins, PartyMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "Party tab";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public PartyMenu Menu { get; }

    /// <summary>Whether C-down's details are showing, as they do while it's held.</summary>
    public bool DetailsOpen { get; set; }

    /// <summary>Whether combat opened the menu, which turns the arrow under the knotwork to point left only.</summary>
    public bool InCombat { get; set; }

    /// <summary>The classic layout's frame on the screen: centred.</summary>
    public Vector2I Frame => new(((int)Size.X - FrameWidth) / 2, ((int)Size.Y - FrameHeight) / 2);

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
        var at = point - (Vector2)Frame;
        var b = ListBounds;
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
        DrawSetTransform(Frame);
        DrawClassic();
        DrawSetTransform(Vector2.Zero);
    }

    private void DrawClassic()
    {
        var shown = Menu.Shown!;
        _ui.Image(this, MenuImages.PartyTab, 0, 0);

        // The portraits, the member's name, and their aspect's and school's icons.
        var order = Menu.PortraitOrder;
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var member = Menu.Party.Slots[order[i]]!;
            if (Menu.Portrait(member.Entity) is not { } portrait)
                continue;
            var (at, size) = i == 0 ? (ShownPortrait, ShownSize) : (OtherPortraits[Math.Min(i - 1, OtherPortraits.Length - 1)], OtherSize);
            _ui.Image(this, portrait, at.X, at.Y, new Vector2I(size, size));
        }
        _ui.Text(this, _ui.Slim, shown.Name, NameAt.X, NameAt.Y, PartyMenu.TextColor);
        var iconY = ShownPortrait.Y + ShownSize + 4;
        var iconX = ShownPortrait.X;
        if (MenuImages.AspectIcon(shown.Aspect) is { } aspect)
        {
            _ui.Image(this, aspect, iconX, iconY);
            iconX += _ui.Size(aspect).X + 2;
        }
        if (MenuImages.SchoolIcon(shown.School) is { } school)
            _ui.Image(this, school, iconX, iconY);

        // Hit points, level and gold.
        _ui.ImageTo(this, MenuImages.HealthBar, HealthBarAt.X, HealthBarAt.Y, HealthBarSize, HealthBarAt.X + Menu.HealthBar(HealthBarSize.X));
        Number(shown.Hp, HpAt);
        Number(shown.MaxHp, MaxHpAt);
        Number(shown.Level, LevelAt);
        Number(Menu.Party.Gold, GoldAt);

        // The silhouette.
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            _ui.Image(this, Menu.SlotImage(slot), Slots[(int)slot].X, Slots[(int)slot].Y, new Vector2I(SlotSize, SlotSize));

        DrawList();
        _ui.Image(this, InCombat ? MenuImages.SliderLeft : MenuImages.SliderBoth, SliderAt.X, SliderAt.Y, color: PartyMenu.TextColor);

        if (DetailsOpen && Menu.Details() is { } details)
            DrawDetails(details);
    }

    private void Number(long value, Vector2I at) =>
        _ui.Text(this, _ui.Main, value.ToString(System.Globalization.CultureInfo.InvariantCulture), at.X, at.Y, PartyMenu.TextColor);

    /// <summary>The list showing, under its title, with its arrows.</summary>
    private void DrawList()
    {
        var title = Menu.List switch
        {
            PartyList.Inventory => MenuImages.InventoryTitle,
            PartyList.Stats => MenuImages.StatsTitle,
            PartyList.Skills => MenuImages.SkillsTitle,
            _ => MenuImages.SpellsTitle,
        };
        _ui.Image(this, title, TitleCentre - _ui.Size(title).X / 2, TitleTop);

        var x = ListBounds.Left;
        var y = ListBounds.Top;
        var cursor = Menu.HighlightNow;
        var color = PartyMenu.TextColor;
        var slim = _ui.Slim;
        var b = ListBounds;
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
                        _ui.Text(this, slim, Num(row.Quantity), x + 26, top, b, color);
                    _ui.Text(this, slim, row.Item.Name, x + (row.Quantity != 0 ? 42 : 30), top, b, c);
                });
                _ui.Arrows(this, Menu.Inventory, UpArrow, DownArrow);
                break;
            case PartyList.Stats:
                Menu.Stats.Visible = (b.Bottom - b.Top) / 11;
                _ui.List(Menu.Stats, y, 11, color, cursor, (row, top, c) =>
                {
                    _ui.Text(this, slim, row.Name, x, top, new ScreenRect(x, b.Top, x + 140, b.Bottom), c);
                    _ui.Text(this, slim, Num(row.Value), x + 79, top, b, color);
                    if (row.Change != 0)
                        _ui.Text(this, slim, Change(row.Change), x + 100, top, b, row.Change < 0 ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.Gain);
                });
                if (Menu.Experience is { } exp)
                {
                    // Below the rows, 4 apart, the values lined up after the first label.
                    var lineY = y + Menu.Stats.Rows.Count * 11 + 4;
                    var valueX = x + slim.Width(exp.RemainingLabel) + 4;
                    foreach (var (label, value) in new[] { (exp.RemainingLabel, exp.Remaining), (exp.TotalLabel, exp.Total), (exp.NextLevelLabel, exp.NextLevel) })
                    {
                        _ui.Text(this, slim, label, x, lineY, b, color);
                        _ui.Text(this, slim, value, valueX, lineY, b, color);
                        lineY += slim.LineHeight;
                    }
                }
                break;
            case PartyList.Skills:
                Menu.Skills.Visible = (b.Bottom - b.Top) / 13;
                _ui.List(Menu.Skills, y, 13, color, cursor, (row, top, c) =>
                {
                    if (row.Icon is { } icon)
                        _ui.Image(this, icon, x, top);
                    _ui.Text(this, slim, row.Name, x + 12, top, new ScreenRect(x + 12, b.Top, x + 140, b.Bottom), c);
                    _ui.Text(this, slim, Num(row.Value), x + 79, top, b, color);
                    if (row.Change != 0)
                        _ui.Text(this, slim, Change(row.Change), x + 95, top, b, row.Change < 0 ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.Gain);
                });
                _ui.Arrows(this, Menu.Skills, UpArrow, DownArrow);
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
                    _ui.Text(this, slim, Num(row.Rank), x + 36, top, b, color);
                    _ui.Text(this, slim, row.Name, x + 50, top, new ScreenRect(x + 50, b.Top, x + 140, b.Bottom), c);
                });
                _ui.Arrows(this, Menu.Spells, UpArrow, DownArrow);
                break;
        }
    }

    /// <summary>C-down's details on the parchment over the silhouette (<c>WidgetItemDetail</c>).</summary>
    private void DrawDetails(MenuDetails details)
    {
        _ui.Parchment(this, ParchmentAt.X, ParchmentAt.Y);
        var slim = _ui.Slim;
        var y = DetailsTop;
        if (details.Icon is { } icon)
        {
            // The icon, and the name level with its foot.
            var size = _ui.Size(icon);
            _ui.Image(this, icon, DetailsLeft, y);
            y += size.Y + 2;
            if (details.Title is { } title)
                _ui.Text(this, slim, title, DetailsLeft + size.X + 2, y - slim.LineHeight, PartyMenu.TextColor);
            y += 4;
        }
        foreach (var line in details.Lines)
            y += Line(line, y);

        var footerTop = details.FooterTop ?? (details.FooterBottom is { } bottom ? bottom - details.Footer.Count * slim.LineHeight : y);
        foreach (var line in details.Footer)
            footerTop += Line(line, footerTop);

        int Line(DetailLine line, int top)
        {
            var height = slim.LineHeight;
            var x = DetailsLeft + (line.Indent ? DetailsIndent : 0);
            foreach (var piece in line.Left)
            {
                switch (piece)
                {
                    case DetailText text when line.Wrap:
                        height = _ui.Text(this, slim, text.Text, x, top, new ScreenRect(DetailsLeft, 0, DetailsRight, FrameHeight), text.Color);
                        break;
                    case DetailText text:
                        _ui.Text(this, slim, text.Text, x, top, text.Color);
                        x += slim.Width(text.Text);
                        break;
                    case DetailImage image:
                        var size = image.Size is { } s ? new Vector2I(s, s) : _ui.Size(image.Image);
                        _ui.Image(this, image.Image, x, top, size);
                        x += size.X + 2;
                        break;
                }
            }
            var right = DetailsRight;
            foreach (var piece in line.Right.Reverse())
            {
                switch (piece)
                {
                    case DetailText text:
                        right -= slim.Width(text.Text);
                        _ui.Text(this, slim, text.Text, right, top, text.Color);
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
