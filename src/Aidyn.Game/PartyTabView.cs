using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the pause menu's Party tab (<see cref="PartyMenu"/>) on a <see cref="UiScreen"/>'s canvas, as the
/// original lays it out, in its 320 × 240 frame at the screen's centre, on the tab's image, <c>0x00D9</c>. With a
/// <see cref="Barter"/>, it's the menu's barter state, the loot window or a shop (see <c>PartyTabView.Barter.cs</c>), and
/// with a <see cref="Training"/>, its training state (<c>PartyTabView.Training.cs</c>).
/// </summary>
public partial class PartyTabView : Control
{
    /// <summary>The original's frame.</summary>
    private const int FrameWidth = 320, FrameHeight = 240;

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

    /// <summary>
    /// The silhouette and its slots' frames (DollEquipmentMenu::SetSlotCoords). The shield's is level with the first
    /// scroll's, as its frame painted into <c>0x00D9</c> is and the user's captures show; at 45 it stood 2 above it.
    /// </summary>
    private static readonly Rect2I SilhouettePiece = new(18, 72, 114, 142);

    private static readonly Vector2I[] Slots =
    [
        new(10, 47), new(10, 66), new(10, 85), new(40, 2), new(40, 24), new(30, 43), new(50, 43), new(40, 62),
        new(40, 120), new(70, 47), new(70, 66), new(70, 85), new(90, 47), new(90, 66), new(90, 85),
    ];

    private const int SlotSize = 16;

    // DollMenuLists and WidgetTrainShop: the list's bounds; its title's centre is 70 into them and its
    // top 20 above, the arrows 15 in from each side and 13 above, and the arrow under the knotwork.
    private static readonly ScreenRect ClassicList = new(160, 101, 300, 205);
    private static readonly Vector2I SliderAt = new(126, 207);

    // WidgetItemDetail: the parchment, and its text's left and right edges and top.
    private static readonly Vector2I ClassicParchment = new(0, 58);
    private static readonly ScreenRect ClassicDetails = new(23, 70, 149, 218);
    private const int DetailsIndent = 16;

    // WidgetCrafting: the title centred at x 93 on the details' parchment, its list from x 23 to 163 under it,
    // 122 tall for the Alchemist's recipes and 117 for the rest. A member's row is its portrait's 25 in the original, so
    // the portraits touch and the next name overlaps the health by 2; the port leaves 3 between them, as the user asked.
    private const int CraftingTitleCentre = 93, CraftingTitleTop = 70, CraftingLeft = 23, CraftingRight = 163;
    private const int MemberRow = 28, MemberPortrait = 25;

    private readonly UiPainter _ui;
    private ScreenRect _listBounds = ClassicList;
    private ScreenRect _craftingBounds;
    private int _craftingRow = 11;
    private Vector2I _origin;

    public PartyTabView(PluginSet plugins, PartyMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "Party tab";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>The tab's state, which the runtime's menu starts afresh each time it opens.</summary>
    public PartyMenu Menu { get; set; }

    /// <summary>Whether C-down's details are showing, as they do while it's held.</summary>
    public bool DetailsOpen { get; set; }

    /// <summary>Whether combat opened the menu, which turns the arrow under the knotwork to point left only.</summary>
    public bool InCombat { get; set; }

    /// <summary>
    /// How far the widgets are faded into the parchment, from 0 to 1: the pause menu fades a tab
    /// beside the one showing, whose edge shows on a screen wider than the frame.
    /// </summary>
    public float Veil { get; set; }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Barter is { } barter)
            barter.Step();
        else if (Training is { } training)
            training.Step();
        else
            Menu.Step();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Hovering a row puts the cursor on it, and the wheel moves it.
        if (Barter is { } barter && @event is InputEventMouseMotion over && BarterRowAt(over.Position) is { } hit)
            barter.Point(hit.Pile, hit.Row);
        else if (Training is { } training && @event is InputEventMouseMotion hover && TrainerRowAt(hover.Position) is { } trainerRow)
            training.Point(trainerRow);
        else if (Barter is null && Training is null && @event is InputEventMouseMotion motion && RowAt(motion.Position) is { } row)
            Menu.Point(row);
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
            MoveCursor(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
        else
            return;
        QueueRedraw();
        AcceptEvent();
    }

    /// <summary>Moves the cursor of the barter state or of the tab.</summary>
    private void MoveCursor(int by)
    {
        if (Barter is { } barter)
            barter.MoveCursor(by);
        else if (Training is { } training)
            training.MoveCursor(by);
        else
            Menu.MoveCursor(by);
    }

    /// <summary>The list row under a point, if any: the crafting popup's while it's open.</summary>
    private int? RowAt(Vector2 point)
    {
        var at = point - (Vector2)_origin;
        if (Menu.Message is not null)
            return null;
        if (Menu.Crafting is { } crafting)
        {
            var c = _craftingBounds;
            if (at.X < c.Left || at.X >= c.Right || at.Y < c.Top || at.Y >= c.Bottom)
                return null;
            var hit = crafting.Rows.First + (int)(at.Y - c.Top) / _craftingRow;
            return hit < crafting.Rows.Rows.Count ? hit : null;
        }
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
        _origin = new Vector2I(((int)Size.X - FrameWidth) / 2, ((int)Size.Y - FrameHeight) / 2);
        DrawSetTransform(_origin);
        if (Barter is { } barter)
            DrawBarter(barter);
        else if (Training is { } training)
            DrawTraining(training);
        else
            DrawTab();
        // The tab's own image again, over its widgets, fades them into the parchment.
        if (Veil > 0)
            _ui.Image(this, MenuImages.PartyTab, 0, 0, color: new Rgba(255, 255, 255, (byte)(Veil * 255)));
        DrawSetTransform(Vector2.Zero);
        if (Barter is { } popups)
            BarterPopups(popups);
        else if (Training is { } trainingPopups)
            TrainingPopups(trainingPopups);
        else if (Menu.Message is { } message)
            Message(message);
    }

    private void DrawTab()
    {
        _ui.Image(this, MenuImages.PartyTab, 0, 0);
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        Silhouette(SilhouettePiece.Position);
        List(ClassicList, _ui.Slim);
        _ui.Image(this, InCombat ? MenuImages.SliderLeft : MenuImages.SliderBoth, SliderAt.X, SliderAt.Y, color: PartyMenu.TextColor);
        if (Menu.Crafting is { } crafting)
            Crafting(crafting);
        if (DetailsOpen && Menu.Details() is { } details)
        {
            _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
            Details(details, ClassicDetails, _ui.Slim);
        }
    }

    /// <summary>
    /// A skill's popup (<c>WidgetCrafting</c>, <c>WidgetMenuPopup</c>) on the details' parchment at its own
    /// place: its title centred above a list in the slim font, of recipes, tasks, attributes, or members
    /// with their portraits, names and health.
    /// </summary>
    private void Crafting(CraftingMenu crafting)
    {
        var at = Vector2I.Zero;
        var font = _ui.Slim;
        _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
        // The original measures the title as it builds the popup, in the main font, and draws it in the slim one.
        var titleWidth = _ui.Main.Width(crafting.Title);
        _ui.Text(this, font, crafting.Title, at.X + CraftingTitleCentre - titleWidth / 2, at.Y + CraftingTitleTop, PartyMenu.TextColor);
        var top = at.Y + CraftingTitleTop + font.LineHeight;
        var height = crafting.Task == CraftingTask.Alchemy ? 0x7A : 0x75;
        var bounds = new ScreenRect(at.X + CraftingLeft, top, at.X + CraftingRight, top + height);
        _craftingBounds = bounds;
        _craftingRow = crafting.ListsMembers ? MemberRow : font.LineHeight;
        crafting.Rows.Visible = height / _craftingRow;
        var x = bounds.Left;
        _ui.List(crafting.Rows, top, _craftingRow, PartyMenu.TextColor, Menu.HighlightNow, (row, y, color) =>
        {
            if (row.Member is { } slot && Menu.Party.Slots.ElementAtOrDefault(slot) is { } member)
            {
                // WidgetMenuHealer::ShowHealedMember: the portrait, the name 2 after it, and the health under the name.
                if (Menu.Portrait(member.Entity) is { } portrait)
                    _ui.Image(this, portrait, x, y, new Vector2I(MemberPortrait, MemberPortrait));
                _ui.Text(this, font, row.Text, x + MemberPortrait + 2, y, bounds, color);
                _ui.Text(this, font, row.Health ?? "", x + MemberPortrait, y + font.LineHeight + 5, bounds, color);
            }
            else
            {
                _ui.Text(this, font, row.Text, x, y, bounds, color);
            }
        });
    }

    /// <summary>
    /// A message in off-white over a feathered black box at the screen's centre (<c>TextBox_Centered</c>):
    /// as wide as its text up to 150, and as tall as it wraps, plus 5; a crafting popup's result has 16
    /// more across and 5 more down (<c>WidgetCrafting::TextPopup</c>).
    /// </summary>
    private void Message(MenuMessage message) =>
        _ui.Message(this, (Vector2I)Size, message.Text, MenuMessage.MaxWidth, MenuMessage.Color, message.FromCrafting ? 16 : 0, message.FromCrafting ? 5 : 0);

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

    /// <summary>
    /// Where a list's columns go, from its left edge, and how far its names reach: the original's (<c>SMIItem</c>,
    /// <c>SMStatItem</c>, <c>WidgetSkillInfo</c>, <c>WidgetSpellEntry</c>).
    /// </summary>
    private static class Columns
    {
        public const int Quantity = 26, Name = 42, NameNoQuantity = 30, Value = 79, Change = 100, SkillChange = 95, Rank = 36,
            SpellName = 50, NameRight = 140;
    }

    /// <summary>
    /// The list showing, under its title, with its arrows and its rows in <paramref name="font"/>, the
    /// cursor's in <paramref name="cursor"/>, or pulsing if none is given.
    /// </summary>
    private void List(ScreenRect b, UiFont font, Rgba? cursor = null)
    {
        _listBounds = b;
        var title = Menu.List switch
        {
            PartyList.Inventory => Barter?.PartyTitle ?? MenuImages.InventoryTitle,
            PartyList.Stats => MenuImages.StatsTitle,
            PartyList.Skills => MenuImages.SkillsTitle,
            _ => MenuImages.SpellsTitle,
        };
        _ui.Image(this, title, b.Left + 70 - _ui.Size(title).X / 2, b.Top - 20);
        var x = b.Left;
        var y = b.Top;
        var highlight = cursor ?? Menu.HighlightNow;
        var color = PartyMenu.TextColor;
        var names = new ScreenRect(b.Left, b.Top, x + Columns.NameRight, b.Bottom);
        switch (Menu.List)
        {
            case PartyList.Inventory:
                Items(Menu.Inventory, b, font, highlight);
                break;
            case PartyList.Stats:
                Menu.Stats.Visible = (b.Bottom - b.Top) / 11;
                _ui.List(Menu.Stats, y, 11, color, highlight, (row, top, c) =>
                {
                    _ui.Text(this, font, row.Name, x, top, names, c);
                    _ui.Text(this, font, Num(row.Value), x + Columns.Value, top, b, color);
                    if (row.Change != 0)
                        _ui.Text(this, font, Change(row.Change), x + Columns.Change, top, b, row.Change < 0 ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.Gain);
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
                SkillRows(Menu.Skills, b, font, highlight);
                break;
            default:
                SpellRows(Menu.Spells, b, font, highlight);
                break;
        }
    }

    /// <summary>
    /// A skills list's rows (<c>WidgetSkillInfo</c>), 13 apart, with their arrows: the icon, the name 12 after
    /// it, the level, and the difference gear makes when it isn't 0.
    /// </summary>
    private void SkillRows(MenuList<SkillRow> list, ScreenRect b, UiFont font, Rgba highlight)
    {
        var x = b.Left;
        var names = new ScreenRect(b.Left, b.Top, x + Columns.NameRight, b.Bottom);
        var color = PartyMenu.TextColor;
        list.Visible = (b.Bottom - b.Top) / 13;
        _ui.List(list, b.Top, 13, color, highlight, (row, top, c) =>
        {
            if (row.Icon is { } icon)
                _ui.Image(this, icon, x, top);
            _ui.Text(this, font, row.Name, x + 12, top, names with { Left = x + 12 }, c);
            _ui.Text(this, font, Num(row.Value), x + Columns.Value, top, b, color);
            if (row.Change != 0)
                _ui.Text(this, font, Change(row.Change), x + Columns.SkillChange, top, b, row.Change < 0 ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.Gain);
        });
        _ui.Arrows(this, list, new Vector2I(b.Left + 15, b.Top - 13), new Vector2I(b.Right - 15, b.Top - 13));
    }

    /// <summary>
    /// A spells list's rows (<c>WidgetSpellEntry</c>), 13 apart, with their arrows: the aspect's, school's and
    /// spell's icons, the rank and the name.
    /// </summary>
    private void SpellRows(MenuList<SpellRow> list, ScreenRect b, UiFont font, Rgba highlight)
    {
        var x = b.Left;
        var names = new ScreenRect(b.Left, b.Top, x + Columns.NameRight, b.Bottom);
        var color = PartyMenu.TextColor;
        list.Visible = (b.Bottom - b.Top) / 13;
        _ui.List(list, b.Top, 13, color, highlight, (row, top, c) =>
        {
            foreach (var (icon, dx) in new[] { (row.Aspect, 0), (row.School, 12), (row.Symbol, 24) })
            {
                if (icon is { } i)
                    _ui.Image(this, i, x + dx, top);
            }
            _ui.Text(this, font, Num(row.Rank), x + Columns.Rank, top, b, color);
            _ui.Text(this, font, row.Name, x + Columns.SpellName, top, names with { Left = x + Columns.SpellName }, c);
        });
        _ui.Arrows(this, list, new Vector2I(b.Left + 15, b.Top - 13), new Vector2I(b.Right - 15, b.Top - 13));
    }

    /// <summary>
    /// An inventory's or a pile's rows (<c>SMIItem</c>), 13 apart, with the arrows 15 in from each side and 13
    /// above: the wearer's portrait and the item's icon at 12 square, then the count and the name.
    /// </summary>
    private void Items(MenuList<InventoryRow> list, ScreenRect b, UiFont font, Rgba cursor)
    {
        list.Visible = (b.Bottom - b.Top) / 13;
        var x = b.Left;
        _ui.List(list, b.Top, 13, PartyMenu.TextColor, cursor, (row, top, c) =>
        {
            if (row.Wearer is { } wearer && Menu.Party.Slots[wearer] is { } who && Menu.Portrait(who.Entity) is { } portrait)
                _ui.Image(this, portrait, x, top, new Vector2I(12, 12));
            _ui.Image(this, row.Item.Icon, x + 13, top, new Vector2I(12, 12));
            if (row.Quantity != 0)
                _ui.Text(this, font, Num(row.Quantity), x + Columns.Quantity, top, b, PartyMenu.TextColor);
            _ui.Text(this, font, row.Item.Name, x + (row.Quantity != 0 ? Columns.Name : Columns.NameNoQuantity), top, b, c);
        });
        _ui.Arrows(this, list, new Vector2I(b.Left + 15, b.Top - 13), new Vector2I(b.Right - 15, b.Top - 13));
    }

    /// <summary>
    /// C-down's details (<c>WidgetItemDetail</c>) in an area: a heading, the lines, and the footer, pinned where
    /// the original pins it.
    /// </summary>
    private void Details(MenuDetails details, ScreenRect area, UiFont font)
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
        var footerTop = details.FooterTop ?? (details.FooterBottom is { } bottom ? bottom - footerHeight : y);
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
