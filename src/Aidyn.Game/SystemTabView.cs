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

    /// <summary>The options' title's top, the rows' left (under the bar, which is centred) and the first row's top.</summary>
    private const int OptionsTitleTop = 40, OptionsLeft = 84, OptionsTop = 74, OptionsSpacing = 4;

    /// <summary>
    /// The Video and Enhancements pages' labels' and values' left edges, their first row's top and their rows' pitch. The labels
    /// clear the System roller, whose inner edge is at about x 61, as the original's options are shifted 25 right of centre to clear it.
    /// </summary>
    private const int ChoiceLabel = 72, ChoiceValue = 204, ChoiceTop = 72, ChoicePitch = 12;

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

    public SystemTabView(PluginSet plugins, SystemMenu menu)
    {
        _ui = new UiPainter(plugins);
        Menu = menu;
        Name = "System tab";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public SystemMenu Menu { get; set; }

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
                var top = ListTop() + 24 + 16;
                var row = (int)Math.Floor((at.Y - top + 2) / ListPitch);
                return at.X >= ListCentre - 70 && at.X < ListCentre + 70 && row >= 0 && row < Menu.Items.Rows.Count ? row : null;
            case SystemPage.Options when Menu.Options is { } options:
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
        _origin = Origin();
        DrawSetTransform(_origin);
        _ui.Image(this, MenuImages.SystemTab, 0, 0);
        // The page, and the one before fading out over it (OptionsMenu.Step, 0x8003f81c).
        Page(new SystemScreen(Menu.Page, Menu.Options, Menu.Saves, Menu.Rename, Menu.Theatre), Menu.PageAlpha);
        if (Menu.Outgoing is { } outgoing)
            Page(outgoing, Menu.OutgoingAlpha);
        if (Veil > 0)
            _ui.Image(this, MenuImages.SystemTab, 0, 0, color: new Rgba(255, 255, 255, (byte)(Veil * 255)));
        DrawSetTransform(Vector2.Zero);
        if (Menu.Saves is { } saves)
            _ui.Overlay(this, size, saves.Overlay);
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

    /// <summary>The title and the list are centred together down the frame.</summary>
    private int ListTop() => (FrameHeight - (24 + Menu.Items.Rows.Count * 11 + (Menu.Items.Rows.Count - 1) * 5 + 16)) / 2;

    private void List()
    {
        var top = ListTop();
        CentredImage(MenuImages.MenuTitle, ListCentre, top);
        var y = top + 24 + 16;
        var cursor = Menu.Pulse.Now(PartyMenu.HighlightFrom, PartyMenu.HighlightColor);
        for (var i = 0; i < Menu.Items.Rows.Count; i++)
        {
            var item = Menu.Items.Rows[i];
            var color = i == Menu.Items.Cursor ? cursor : SystemMenu.TextColor;
            CentredText(Menu.Label(item), ListCentre, y + i * ListPitch, color);
        }
        _ui.Image(this, MenuImages.SliderRight, ListArrow.X, ListArrow.Y, color: SystemMenu.TextColor);
    }

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

    /// <summary>Where each options row is: its label's top, and the bottom of what it draws (a volume's bar too).</summary>
    private List<(int Top, int Bottom)> OptionRows(OptionsMenu options)
    {
        var rows = new List<(int, int)>();
        var choices = HasChoices(options);
        var y = choices ? ChoiceTop : OptionsTop;
        foreach (var row in options.Rows.Rows)
        {
            if (row.GapBefore)
                y += OptionsMenu.Gap;
            var height = _ui.Main.LineHeight + (row.Kind == OptionKind.Volume ? _ui.Size(MenuImages.VolumeBar).Y : 0);
            rows.Add((y, y + height));
            y += choices ? ChoicePitch : height + OptionsSpacing;
        }
        return rows;
    }

    /// <summary>
    /// The options (<c>WidgetOptionsConfig</c>), as the user laid them out, under the original's title: the pages' links and Rename
    /// Character set off by half a line, the Audio page's volumes with their rope bars under their labels, or the Video or
    /// Enhancements page, each setting with its value beside it.
    /// </summary>
    private void Options(OptionsMenu options)
    {
        CentredImage(MenuImages.OptionsTitle, FrameWidth / 2, OptionsTitleTop);
        var rows = OptionRows(options);
        var cursor = options.HighlightNow;
        var bar = _ui.Size(MenuImages.VolumeBar);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = options.Rows.Rows[i];
            var top = rows[i].Top;
            var color = i == options.Rows.Cursor ? cursor : OptionsMenu.TextColor;
            if (HasChoices(options))
            {
                _ui.Text(this, _ui.Main, row.Label, ChoiceLabel, top, color);
                _ui.Text(this, _ui.Main, row.ValueName ?? "", ChoiceValue, top, color);
                continue;
            }
            _ui.Text(this, _ui.Main, row.Label, OptionsLeft, top, color);
            if (row.Kind == OptionKind.Volume)
            {
                // The rope whole and faint, then again in the rows' colour as far as the volume reaches.
                var y = top + _ui.Main.LineHeight;
                _ui.Image(this, MenuImages.VolumeBar, OptionsLeft, y, color: OptionsMenu.BarEmpty);
                _ui.ImageTo(this, MenuImages.VolumeBar, OptionsLeft, y, bar, OptionsLeft + bar.X * row.Value / Core.Settings.MaxVolume,
                    OptionsMenu.TextColor);
            }
        }
    }

    /// <summary>
    /// The save and load screens: one saved game, its screenshot stretched to 104 × 78, its party's portraits, an
    /// arrow saying which ways there are more, Alaron's name and the time played, and the actions in the corner.
    /// </summary>
    private void Saves(SaveMenu saves)
    {
        _ui.Image(this, saves.Saving ? MenuImages.SaveTitle : MenuImages.LoadTitle, SavesTitle.X, SavesTitle.Y);
        if (saves.Slide is { } slide)
            SlidingPages(saves, slide);
        else
            SlotPage(saves, saves.Slot, 0, null);
        var cursor = saves.Pulse.Now(PartyMenu.HighlightFrom, PartyMenu.HighlightColor);
        for (var i = 0; i < saves.Actions.Rows.Count; i++)
        {
            var color = i == saves.Actions.Cursor && saves.Choice is null ? cursor : SaveMenu.TextColor;
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
            _ui.Image(this, a, SlotArrow.X + dx, SlotArrow.Y, color: SaveMenu.TextColor, clip: clip);
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
                SaveMenu.TextColor, clip);
            _ui.Text(this, _ui.Main, saves.Hours(slot), SlotHours.X + dx, SlotHours.Y, SaveMenu.TextColor, clip);
        }
        else
        {
            _ui.Text(this, _ui.Main, saves.BlankOf(index) ?? "", SlotName.X + dx, SlotName.Y,
                new ScreenRect(SlotName.X + dx, 0, FrameWidth - 12 + dx, FrameHeight), SaveMenu.BlankColor, clip);
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
            _ui.Text(this, _ui.Main, c, x, NameTop, i == entry.Slot ? chosen : SystemMenu.TextColor);
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
                k == entry.Key ? KeyUnderCursor : SystemMenu.TextColor);
        }
    }

    private void CentredImage(Plugins.RecordId image, int centre, int top) => _ui.Image(this, image, centre - _ui.Size(image).X / 2, top);

    private void CentredText(string text, int centre, int top, Rgba color) =>
        _ui.Text(this, _ui.Main, text, centre - _ui.Main.Width(text) / 2, top, color);
}
