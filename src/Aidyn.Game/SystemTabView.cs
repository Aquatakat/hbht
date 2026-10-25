using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the pause menu's System tab (<see cref="SystemMenu"/>) on a <see cref="UiScreen"/>'s canvas: its list, the
/// options with their pages, the save and load screens and the name entry, in the original's 320 × 240 frame
/// at the screen's centre, on the tab's image, <c>0x00D8</c>.
/// </summary>
public partial class SystemTabView : Control
{
    private const int FrameWidth = 320, FrameHeight = 240;

    /// <summary>The list (<c>WidgetOptionsMenu::MakeScrollList</c>): six rows 16 apart, centred, and the arrow under it.</summary>
    private const int ListPitch = 16, ListCentre = 160;

    private static readonly Vector2I ListArrow = new(170, 204);

    /// <summary>The Theatre's page (<c>Theatre.Build</c>): the title's top, and its rows', 16 apart, centred on the list's centre.</summary>
    private const int TheatreTitleTop = 50, TheatreTop = 90;

    /// <summary>
    /// The options' title's top, the Audio page's labels' left (over their bars, which are centred) and the first row's top. Their
    /// rows are the list's 16 apart, but on the Controls page.
    /// </summary>
    private const int OptionsTitleTop = 40, OptionsLeft = 84, OptionsTop = 74;

    /// <summary>
    /// The Video and Enhancements pages' labels' and values' left edges. The labels clear the System roller, whose inner edge is at
    /// about x 61, as the original's options are shifted 25 right of centre to clear it.
    /// </summary>
    private const int ChoiceLabel = 72, ChoiceValue = 204;

    // WidgetContPakData and ControllerPakSliders: the title, the screenshot stretched to 104 × 78, the portraits,
    // the arrow, the name and the time played, and the actions.
    private static readonly Vector2I SavesTitle = new(132, 24), Screenshot = new(73, 52), ScreenshotSize = new(104, 78);
    private static readonly int[] PortraitColumns = [73, 128, 183, 238];
    private const int PortraitTop = 133, PortraitSize = 40;
    private static readonly Vector2I SlotArrow = new(183, 101), SlotName = new(183, 111), SlotHours = new(183, 122), SaveActions = new(237, 180);

    /// <summary>The box a save's page is cut to as it slides, from the title's corner (<c>SaveMenu.Actions</c>, <c>0x8007e658</c>): 219 × 192.</summary>
    private static readonly Rect2I SlotPanel = new(67, 24, 219, 192);

    // InputMenu: the title's top, the name's, the grid's corner and its keys' size.
    private const int NameTitleTop = 50, NameTop = 85, KeySize = 15;
    private static readonly Vector2I Grid = new(60, 110);

    /// <summary>The key cursor and the letter being chosen pulse between greys (<c>InputMenu::Tick</c>), and the key under the cursor is black.</summary>
    private static readonly Rgba KeyUnderCursor = new(0, 0, 0, 255);

    private readonly UiPainter _ui;
    private readonly Dictionary<string, Texture2D?> _screenshots = [];
    private Vector2I _origin;

    /// <summary>Where the page being drawn has its corner: the frame's, or for the title's options, where their box is centred.</summary>
    private Vector2I _pageOrigin;

    public SystemTabView(PluginSet plugins, SystemMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "System tab";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public SystemMenu Menu { get; set; }

    /// <summary>The colours the pages are drawn in, and whether on the parchment or each on a box: the pause menu's or the title's.</summary>
    private SystemStyle Style => Menu.Style;

    /// <summary>How far the widgets are faded into the parchment, from 0 to 1, as on <see cref="PartyTabView.Veil"/>.</summary>
    public float Veil { get; set; }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    /// <remarks>The core's menu steps the tab (<see cref="Core.Ui.PauseMenu.Step"/>); this only draws it again.</remarks>
    public override void _PhysicsProcess(double delta) => QueueRedraw();

    /// <summary>Where the frame's corner is on the screen: the frame is centred.</summary>
    private Vector2I Origin() => new(((int)Size.X - FrameWidth) / 2, ((int)Size.Y - FrameHeight) / 2);

    public override void _GuiInput(InputEvent @event)
    {
        // The Controls page's prompt binds the wheel and the buttons too (Game.BindPrompted), so they go by.
        if (Menu.Options?.Prompt is not null)
            return;
        if (Menu.Quitting is { } quitting)
        {
            // The quit box takes the mouse as the stick: hovering points at a choice.
            if (@event is InputEventMouseMotion over && _ui.ChoiceAt(new Vector2I((int)Size.X, (int)Size.Y), quitting, over.Position) is { } choice)
                Menu.PointQuit(choice);
            else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } scroll)
                Menu.MoveCursor(scroll.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            else
                return;
            QueueRedraw();
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion motion && RowAt(motion.Position - _origin) is { } row)
            Menu.Point(row);
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
            Menu.MoveCursor(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
        else
            return;
        QueueRedraw();
        AcceptEvent();
    }

    /// <summary>The row of the list or options page under a point in the frame.</summary>
    private int? RowAt(Vector2 at)
    {
        switch (Menu.Page)
        {
            case SystemPage.List:
                var top = RowsTop();
                var row = (int)Math.Floor((at.Y - top + 2) / RowPitch);
                return at.X >= ListCentre - 70 && at.X < ListCentre + 70 && row >= 0 && row < Menu.Items.Rows.Count ? row : null;
            case SystemPage.Options when Menu.Options is { } options:
                at -= OptionsShift(options);
                var rows = OptionRows(options);
                for (var i = 0; i < rows.Count; i++)
                {
                    if (at.Y >= rows[i].Top && at.Y < rows[i].Bottom && at.X >= OptionsLeft - 40 && at.X < FrameWidth - OptionsLeft + 40)
                        return i;
                }
                return null;
            case SystemPage.Theatre when Menu.Theatre is { } theatre:
                var seat = (int)Math.Floor((at.Y - TheatreTop + 2) / ListPitch);
                return at.X >= ListCentre - 70 && at.X < ListCentre + 70 && seat >= 0 && seat < theatre.Items.Rows.Count ? seat : null;
            case SystemPage.Saves when Menu.Saves is { Choice: null } saves:
                var action = (int)Math.Floor((at.Y - SaveActions.Y) / 11);
                return at.X >= SaveActions.X && action >= 0 && action < saves.Actions.Rows.Count ? action : null;
            default:
                return null;
        }
    }

    public override void _Draw()
    {
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        _origin = _pageOrigin = Origin();
        DrawSetTransform(_origin);
        if (Style.Parchment)
            _ui.Image(this, MenuImages.SystemTab, 0, 0);
        // The page, and the one before fading out over it (OptionsMenu.Step, 0x8003f81c). The title's are each on their box, the boxes
        // drawn first by the pages' alphas (IntroMenu.Draw, 0x80042aa8).
        var showing = new SystemScreen(Menu.Page, Menu.Options, Menu.Saves, Menu.Rename, Menu.Theatre);
        if (!Style.Parchment)
        {
            Box(showing, Menu.PageAlpha);
            if (Menu.Outgoing is { } fading)
                Box(fading, Menu.OutgoingAlpha);
        }
        Page(showing, Menu.PageAlpha);
        if (Menu.Outgoing is { } outgoing)
            Page(outgoing, Menu.OutgoingAlpha);
        if (Veil > 0)
            _ui.Image(this, MenuImages.SystemTab, 0, 0, color: new Rgba(255, 255, 255, (byte)(Veil * 255)));
        DrawSetTransform(Vector2.Zero);
        if (Menu.Saves is { } saves)
            _ui.Overlay(this, size, saves.Overlay);
        _ui.Overlay(this, size, Menu.Overlay);
    }

    /// <summary>A page of the tab at an alpha, everything on it faded alike.</summary>
    private void Page(SystemScreen screen, byte alpha)
    {
        _ui.Opacity = alpha / 255f;
        switch (screen.Page)
        {
            case SystemPage.List:
                List();
                break;
            case SystemPage.Options:
                Options(screen.Options!);
                break;
            case SystemPage.Saves:
                Saves(screen.Saves!);
                break;
            case SystemPage.Rename:
                Rename(screen.Rename!);
                break;
            case SystemPage.Theatre:
                Theatre(screen.Theatre!);
                break;
        }
        _ui.Opacity = 1;
    }

    /// <summary>
    /// The title's list (<c>IntroMenu.MakeList</c>, <c>0x80043ae8</c>): the title's top, and the rows', 19 apart, the scroll menu's gap of 8
    /// under each line of 11; its box (<c>IntroMenu.ShowList</c>) reaches 66 either side of the list's centre, 48 above the rows and 68 below,
    /// for the original's three. The port's Quit Game makes four, the box reaching a row further down.
    /// </summary>
    private const int TitleListTop = 75, TitleRowsTop = 115, TitleRowPitch = 19, TitleListRows = 3;

    private ScreenRect TitleListBox => new(94, 67, 226, 183 + (Menu.Items.Rows.Count - TitleListRows) * TitleRowPitch);

    /// <summary>The name entry's box (<c>IntroMenu.ShowNameEntry</c>, <c>0x800437f8</c>): from 8 left of the grid, and from y 43 to 179.</summary>
    private static readonly ScreenRect NameBox = new(52, 43, 268, 179);

    /// <summary>The list's title's top: the pause menu's is centred down the frame with the rows, the title's at 75.</summary>
    private int ListTop() => Menu.Title ? TitleListTop
        : (FrameHeight - (24 + Menu.Items.Rows.Count * 11 + (Menu.Items.Rows.Count - 1) * 5 + 16)) / 2;

    /// <summary>The first row's top: 16 under the title in the pause menu, at 115 in the title's.</summary>
    private int RowsTop() => Menu.Title ? TitleRowsTop : ListTop() + 24 + 16;

    private int RowPitch => Menu.Title ? TitleRowPitch : ListPitch;

    private void List()
    {
        CentredImage(MenuImages.MenuTitle, ListCentre, ListTop());
        var y = RowsTop();
        var cursor = Menu.Pulse.Now(Style.CursorFrom, Style.CursorTo);
        for (var i = 0; i < Menu.Items.Rows.Count; i++)
        {
            var item = Menu.Items.Rows[i];
            var color = i == Menu.Items.Cursor ? cursor : Style.Text;
            CentredText(Menu.Label(item), ListCentre, y + i * RowPitch, color);
        }
        // The arrow to the Party tab; the title has no tabs.
        if (!Menu.Title)
            _ui.Image(this, MenuImages.SliderRight, ListArrow.X, ListArrow.Y, color: Style.Text);
    }

    /// <summary>
    /// The title's feathered black box behind a page (<c>IntroMenu_ShadowBG</c>, <c>0x80042eb0</c>), at the page's alpha: the list's, the
    /// name entry's and the save screen's where the original has them, and the options' about what each of the port's pages shows.
    /// </summary>
    private void Box(SystemScreen screen, byte alpha)
    {
        ScreenRect? band = screen.Page switch
        {
            SystemPage.List => TitleListBox,
            SystemPage.Rename => NameBox,
            SystemPage.Saves => new ScreenRect(SlotPanel.Position.X, SlotPanel.Position.Y, SlotPanel.End.X, SlotPanel.End.Y),
            SystemPage.Options when screen.Options is { } options => Shifted(OptionsBox(options), OptionsShift(options)),
            _ => null,
        };
        if (band is { } b)
            _ui.Images.DrawPanel(this, _ui.Images.Around(b), alpha / 255f);
    }

    /// <summary>
    /// The options' box, where the pause menu draws them: from 6 above the title to 8 under the last row (the foot's, on the Controls
    /// page), and 12 beyond the rows either side: the bars' width on the main and Audio pages, the labels to the widest of any row's
    /// values on Video and Enhancements, so changing one doesn't move the box, and the Controls page's whole width.
    /// </summary>
    private ScreenRect OptionsBox(OptionsMenu options)
    {
        var bottom = options.Page == OptionsPage.Controls
            ? ControlsFoot + _ui.Main.LineHeight
            : OptionRows(options).Select(r => r.Bottom).DefaultIfEmpty(OptionsTop).Max();
        var (left, right) = options.Page switch
        {
            OptionsPage.Main or OptionsPage.Audio => (OptionsLeft, OptionsLeft + _ui.Size(MenuImages.VolumeBar).X),
            OptionsPage.Controls => (ChoiceLabel, ControlsRight),
            _ => (ChoiceLabel, ChoiceValue + options.Rows.Rows.SelectMany(r => r.Values).Select(_ui.Main.Width).DefaultIfEmpty(0).Max()),
        };
        return new ScreenRect(left - 12, OptionsTitleTop - 6, right + 12, bottom + 8);
    }

    /// <summary>
    /// How far the options are moved from where the pause menu draws them: not at all on the parchment, and on the title's box so the
    /// box is centred on the screen, as the boxes are as wide and tall as each page needs.
    /// </summary>
    private Vector2I OptionsShift(OptionsMenu options)
    {
        if (Style.Parchment)
            return Vector2I.Zero;
        var box = OptionsBox(options);
        return new Vector2I((FrameWidth - box.Left - box.Right) / 2, (FrameHeight - box.Top - box.Bottom) / 2);
    }

    /// <summary>
    /// What the options' title, the main page's rows and the Controls page's headings are centred on: the frame on the parchment, the
    /// box on the title's.
    /// </summary>
    private int OptionsCentre(OptionsMenu options)
    {
        if (Style.Parchment)
            return FrameWidth / 2;
        var box = OptionsBox(options);
        return (box.Left + box.Right) / 2;
    }

    private static ScreenRect Shifted(ScreenRect r, Vector2I by) => new(r.Left + by.X, r.Top + by.Y, r.Right + by.X, r.Bottom + by.Y);

    /// <summary>
    /// The Theatre's page: "Theater" and a row a cinematic, its title or the dashes. A title is centred with the space the
    /// DB ends it with, as the original's is.
    /// </summary>
    private void Theatre(TheatreMenu theatre)
    {
        CentredImage(MenuImages.TheatreTitle, ListCentre, TheatreTitleTop);
        var cursor = theatre.Pulse.Now(TheatreMenu.PulseFrom, TheatreMenu.PulseTo);
        for (var i = 0; i < theatre.Items.Rows.Count; i++)
        {
            var row = theatre.Items.Rows[i];
            var width = _ui.Main.Width(row.Seen ? row.Label + " " : row.Label);
            _ui.Text(this, _ui.Main, row.Label, ListCentre - width / 2, TheatreTop + i * ListPitch,
                i == theatre.Items.Cursor ? cursor : TheatreMenu.RowColor);
        }
    }

    /// <summary>Whether the options' page lists settings with their values beside them, in two columns.</summary>
    private static bool HasChoices(OptionsMenu options) => options.Page is OptionsPage.Video or OptionsPage.Enhancements;

    /// <summary>
    /// Where each options row is: its label's top, and the bottom of what it draws (a volume's bar too). The rows are the list's 16
    /// apart, a volume taking two of its lines, its label's and its bar's.
    /// </summary>
    private List<(int Top, int Bottom)> OptionRows(OptionsMenu options)
    {
        if (options.Page == OptionsPage.Controls)
            return ControlPlaces(options).Rows;
        var rows = new List<(int, int)>();
        var y = OptionsTop;
        foreach (var row in options.Rows.Rows)
        {
            if (row.GapBefore)
                y += OptionsMenu.Gap;
            var volume = row.Kind == OptionKind.Volume;
            rows.Add((y, y + _ui.Main.LineHeight + (volume ? _ui.Size(MenuImages.VolumeBar).Y : 0)));
            y += volume ? 2 * ListPitch : ListPitch;
        }
        return rows;
    }

    /// <summary>
    /// The options (<c>WidgetOptionsConfig</c>), as the user laid them out, under the original's title, moved so the title's box is
    /// centred (<see cref="OptionsShift"/>).
    /// </summary>
    private void Options(OptionsMenu options)
    {
        _pageOrigin = _origin + OptionsShift(options);
        DrawSetTransform(_pageOrigin);
        OptionsContent(options);
        _pageOrigin = _origin;
        DrawSetTransform(_origin);
    }

    /// <summary>
    /// An options page: the pages' links centred, with Rename Character set off by half a line, the Audio page's volumes with their
    /// rope bars under their labels, the Video or Enhancements page, each setting with its value beside it, or the Controls page.
    /// </summary>
    private void OptionsContent(OptionsMenu options)
    {
        var centre = OptionsCentre(options);
        CentredImage(MenuImages.OptionsTitle, centre, OptionsTitleTop);
        if (options.Page == OptionsPage.Controls)
        {
            Controls(options);
            return;
        }
        var rows = OptionRows(options);
        var cursor = options.Pulse.Now(Style.Text, Style.OptionsCursor);
        var bar = _ui.Size(MenuImages.VolumeBar);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = options.Rows.Rows[i];
            var top = rows[i].Top;
            var color = i == options.Rows.Cursor ? cursor : Style.Text;
            if (HasChoices(options))
            {
                _ui.Text(this, _ui.Main, row.Label, ChoiceLabel, top, color);
                _ui.Text(this, _ui.Main, row.ValueName ?? "", ChoiceValue, top, color);
                continue;
            }
            if (row.Kind != OptionKind.Volume)
            {
                CentredText(row.Label, centre, top, color);
                continue;
            }
            _ui.Text(this, _ui.Main, row.Label, OptionsLeft, top, color);
            // The rope whole and faint, then again in the rows' colour as far as the volume reaches.
            var y = top + _ui.Main.LineHeight;
            _ui.Image(this, MenuImages.VolumeBar, OptionsLeft, y, color: Style.BarEmpty);
            _ui.ImageTo(this, MenuImages.VolumeBar, OptionsLeft, y, bar, OptionsLeft + bar.X * row.Value / Core.Settings.MaxVolume, Style.Text);
        }
    }

    /// <summary>
    /// The save and load screens: one saved game, its screenshot stretched to 104 × 78, its party's portraits, an
    /// arrow saying which ways there are more, Alaron's name and the time played, and the actions in the corner.
    /// </summary>
    private void Saves(SaveMenu saves)
    {
        // The title's is centred over its box.
        var title = saves.Saving ? MenuImages.SaveTitle : MenuImages.LoadTitle;
        if (Style.Parchment)
            _ui.Image(this, title, SavesTitle.X, SavesTitle.Y);
        else
            CentredImage(title, SlotPanel.Position.X + SlotPanel.Size.X / 2, SavesTitle.Y);
        if (saves.Slide is { } slide)
            SlidingPages(saves, slide);
        else
            SlotPage(saves, saves.Slot, 0, null);
        var cursor = saves.Pulse.Now(Style.CursorFrom, Style.CursorTo);
        for (var i = 0; i < saves.Actions.Rows.Count; i++)
        {
            var color = i == saves.Actions.Cursor && saves.Choice is null ? cursor : Style.Text;
            _ui.Text(this, _ui.Main, saves.Actions.Rows[i], SaveActions.X, SaveActions.Y + i * 11, color);
        }
    }

    /// <summary>
    /// The save sliding out and the one sliding in (<c>WidgetGroup.Next</c>, <c>WidgetGroup.Previous</c>), cut to the
    /// pages' box: the one shown moves a box's width away from the stick, and the new one in from that side to where
    /// the old one was, both on the ease-out curve and truncated to whole pixels. They're drawn with the rest of the
    /// tab, so the frame that starts a slide draws it.
    /// </summary>
    private void SlidingPages(SaveMenu saves, MenuSlide<int> slide)
    {
        var (left, width) = (SlotPanel.Position.X, SlotPanel.Size.X);
        var change = saves.SlideChange;
        SlotPage(saves, slide.From, MenuTween.Value(left, left - slide.Direction * width, change) - left, SlotPanel);
        SlotPage(saves, saves.Slot, MenuTween.Value(left + slide.Direction * width, left, change) - left, SlotPanel);
    }

    /// <summary>A save's page, its arrow, screenshot, portraits, name and hours, or its blank, moved dx across and cut to a clip.</summary>
    private void SlotPage(SaveMenu saves, int index, int dx, Rect2I? clip)
    {
        var arrow = (index > 0, index < saves.Slots.Count - 1) switch
        {
            (true, true) => MenuImages.SliderBoth,
            (true, false) => MenuImages.SliderLeft,
            (false, true) => MenuImages.SliderRight,
            _ => (Plugins.RecordId?)null,
        };
        if (arrow is { } a)
            _ui.Image(this, a, SlotArrow.X + dx, SlotArrow.Y, color: Style.Text, clip: clip);
        if (saves.Slots.ElementAtOrDefault(index) is { } slot)
        {
            if (ScreenshotOf(slot) is { } shot)
                _ui.Texture(this, shot, new Rect2(Screenshot + new Vector2I(dx, 0), ScreenshotSize), clip);
            var portraits = saves.Portraits(slot);
            for (var i = 0; i < Math.Min(portraits.Count, PortraitColumns.Length); i++)
                _ui.Image(this, portraits[i], PortraitColumns[i] + dx, PortraitTop, new Vector2I(PortraitSize, PortraitSize), clip: clip);
            // A checkpoint's label can be longer than a name: in the slim font if the main one runs off the parchment.
            var name = saves.Name(slot);
            _ui.Text(this, _ui.Main.Width(name) <= FrameWidth - 8 - SlotName.X ? _ui.Main : _ui.Slim, name, SlotName.X + dx, SlotName.Y,
                Style.Text, clip);
            _ui.Text(this, _ui.Main, saves.Hours(slot), SlotHours.X + dx, SlotHours.Y, Style.Text, clip);
        }
        else
        {
            _ui.Text(this, _ui.Main, saves.BlankOf(index) ?? "", SlotName.X + dx, SlotName.Y,
                new ScreenRect(SlotName.X + dx, 0, FrameWidth - 12 + dx, FrameHeight), Style.Blank, clip);
        }
    }

    /// <summary>A save's screenshot, loaded once.</summary>
    private Texture2D? ScreenshotOf(SaveSlot slot)
    {
        var key = slot.Path + slot.Written.Ticks;
        if (_screenshots.TryGetValue(key, out var texture))
            return texture;
        if (slot.Screenshot is { } png)
            texture = Png.Texture(png);
        return _screenshots[key] = texture;
    }

    /// <summary>
    /// The name entry (<c>InputMenu</c>): its title, the name centred under it with the letter being chosen pulsing,
    /// and the grid of keys, 13 a row, with the cursor's frame on the key.
    /// </summary>
    private void Rename(NameEntry entry)
    {
        CentredImage(MenuImages.EnterNameTitle, FrameWidth / 2, NameTitleTop);
        var name = entry.Name;
        var x = FrameWidth / 2 - _ui.Main.Width(name) / 2;
        var chosen = entry.Pulse.Now(NameEntry.PulseFrom, NameEntry.PulseTo);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i].ToString();
            _ui.Text(this, _ui.Main, c, x, NameTop, i == entry.Slot ? chosen : Style.Text);
            x += _ui.Main.Width(c);
        }
        // The next letter's place, which the font's underscore (a diamond) can't show.
        if (entry.Slot >= name.Length)
            DrawRect(new Rect2(x + 1, NameTop + _ui.Main.LineHeight - 2, 6, 1), _ui.Tint(chosen));
        for (var k = 0; k < NameEntry.Keys.Length; k++)
        {
            var cell = Grid + new Vector2I(k % NameEntry.Columns * KeySize, k / NameEntry.Columns * KeySize);
            var key = NameEntry.Keys[k].ToString();
            if (k == entry.Key)
                _ui.Image(this, MenuImages.KeyCursor, cell.X, cell.Y, color: chosen);
            _ui.Text(this, _ui.Main, key, cell.X + (KeySize - _ui.Main.Width(key)) / 2, cell.Y + (KeySize - _ui.Main.LineHeight) / 2 + 1,
                k == entry.Key ? KeyUnderCursor : Style.Text);
        }
    }

    private void CentredImage(Plugins.RecordId image, int centre, int top) => _ui.Image(this, image, centre - _ui.Size(image).X / 2, top);

    private void CentredText(string text, int centre, int top, Rgba color) =>
        _ui.Text(this, _ui.Main, text, centre - _ui.Main.Width(text) / 2, top, color);
}
