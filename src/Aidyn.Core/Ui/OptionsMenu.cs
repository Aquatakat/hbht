using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>What an options row is, which says how it's drawn and what the stick and A do on it.</summary>
public enum OptionKind
{
    /// <summary>A volume, drawn as the original's rope bar under its label: the stick's left and right step it.</summary>
    Volume,

    /// <summary>A setting with named values beside its label: the stick's left and right, or A, change it.</summary>
    Choice,

    /// <summary>A row that opens something: another page, or the name entry. A opens it.</summary>
    Link,

    /// <summary>A control's binding on the Controls page: A waits for an input to bind, and the menus' Appraise clears it.</summary>
    Binding,

    /// <summary>A row that does something at once, as Reset to Defaults does: A carries it out (its <see cref="OptionRow.Set"/>).</summary>
    Command,
}

/// <summary>Where an options row that's a <see cref="OptionKind.Link"/> leads: one of the menu's pages, or out of it.</summary>
public enum OptionLink
{
    Audio,
    Video,
    Enhancements,
    Controls,
    RenameCharacter,
}

/// <summary>
/// A row of the options menu, over a setting: its label, and for a choice its values' names, with how to
/// read and change the value, as a step from 0.
/// </summary>
/// <param name="GapBefore">Whether half a line's space comes before it, setting it off from the rows above.</param>
/// <param name="Shown">For a binding, what it shows as its value.</param>
/// <param name="Control">For a control's row, its ID (<see cref="Controls.GameControl.Id"/>).</param>
/// <param name="Heading">A heading drawn above it, on a line of its own: the situation its controls are in.</param>
public sealed record OptionRow(string Label, OptionKind Kind, Func<int> Get, Action<int> Set, IReadOnlyList<string> Values,
    OptionLink? Link = null, bool GapBefore = false, Func<string>? Shown = null, string? Control = null, string? Heading = null)
{
    /// <summary>How many steps the value has: a volume's from silent to full, or a choice's values.</summary>
    public int Steps => Kind == OptionKind.Volume ? Settings.MaxVolume + 1 : Values.Count;

    public int Value => Get();

    /// <summary>The value's name, for a choice.</summary>
    public string? ValueName => Kind == OptionKind.Choice && Values.Count > 0 ? Values[Math.Clamp(Value, 0, Values.Count - 1)] : Shown?.Invoke();
}

/// <summary>
/// The screen the game is on, as the options open, which the Video page's sizes are listed from: the screen's size, the
/// part of it a window can take (less the taskbar), the window's size now, and the refresh rate.
/// </summary>
public sealed record ScreenInfo(int Width, int Height, int UsableWidth, int UsableHeight, int WindowWidth, int WindowHeight, int RefreshRate)
{
    /// <summary>A 1080p screen at 60 Hz with the window at the runtime's 1280 × 960, for tests and headless runs.</summary>
    public static readonly ScreenInfo Default = new(1920, 1080, 1920, 1040, 1280, 960, 60);
}

/// <summary>The options menu's pages.</summary>
public enum OptionsPage
{
    /// <summary>The pages' links, and in a game Rename Character.</summary>
    Main,

    /// <summary>The original's volumes, with their rope bars.</summary>
    Audio,

    /// <summary>The window, and how the 3D world is drawn into it.</summary>
    Video,

    /// <summary>The port's settings that go beyond the original's look and feel.</summary>
    Enhancements,

    /// <summary>The controls' bindings, on the keyboard and mouse or on a controller (ADR 0026).</summary>
    Controls,
}

/// <summary>
/// The options menu (<c>OptionsMenu</c>): links to its pages, the original's volumes on Audio and the port's settings on
/// Video and Enhancements, and, in a game, renaming Alaron. The pause menu's System tab opens it, and so does the
/// title screen's menu, without the rename (<paramref name="inGame"/>), as the original's title options leave
/// out its GAME rows. It changes <see cref="Settings"/> as the stick moves, so the runtime carries a setting
/// out at once.
/// </summary>
/// <remarks>
/// The port's own layout, which the user chose over the original's (ADR 0024): Title Case rather than capitals, and a
/// page for each kind of setting rather than headings.
/// </remarks>
public sealed partial class OptionsMenu
{
    /// <summary>The gap a row can have before it: half a line of the menus' lists, which are 16 apart.</summary>
    public const int Gap = 8;

    /// <summary>
    /// The rows' colour (<c>COLOR_RED1</c>), and what the cursor's row pulses toward from it over
    /// <see cref="MenuPulse.Frames"/>: dark brown, its alpha falling by 12 a step (<c>WidgetOptionsConfig::Tick</c>).
    /// </summary>
    public static readonly Rgba TextColor = PartyMenu.TextColor, HighlightColor = new(103, 70, 60, 15);

    /// <summary>The volume bars' empty part, drawn under the full part at a quarter of its alpha.</summary>
    public static readonly Rgba BarEmpty = new(100, 100, 100, 63);

    /// <summary>The cursor's row's colour now.</summary>
    public Rgba HighlightNow => Pulse.Now(TextColor, HighlightColor);

    private readonly bool _inGame;
    private ScreenInfo _screen;

    /// <param name="screen">The screen the game is on, or <see cref="ScreenInfo.Default"/>.</param>
    public OptionsMenu(Settings settings, bool inGame, ScreenInfo? screen = null)
    {
        Settings = settings;
        _inGame = inGame;
        _screen = screen ?? ScreenInfo.Default;
        Rows = new MenuList<OptionRow>(MainRows());
    }

    public Settings Settings { get; }

    public OptionsPage Page { get; private set; }

    public MenuList<OptionRow> Rows { get; }

    /// <summary>The cursor's pulse, which turns at 20 rather than 19, 40 frames a cycle (<c>OptionsPage.Draw</c>, <c>0x800419c8</c>).</summary>
    public MenuPulse Pulse { get; } = new(top: MenuPulse.Frames);

    /// <summary>A setting changed since the menu opened, so the runtime keeps them.</summary>
    public bool Changed { get; private set; }

    /// <summary>Steps by a 60 Hz tick: the cursor's pulse, and the Controls page's prompt and message.</summary>
    public void Step()
    {
        Pulse.Step();
        StepPrompt();
    }

    public void MoveCursor(int by)
    {
        if (Prompt is null)
            Rows.Move(by);
    }

    /// <summary>A copy on the same page with the cursor on the same row, which the page fading out draws as it was.</summary>
    public OptionsMenu Copy()
    {
        var copy = new OptionsMenu(Settings, _inGame, _screen) { Device = Device };
        if (Page != OptionsPage.Main)
            copy.Open(Page);
        copy.Rows.Point(Rows.Cursor);
        return copy;
    }

    /// <summary>
    /// The window changed, by hand or as a setting was carried out: the Video page lists what the screen allows now, and shows the
    /// window as it is.
    /// </summary>
    public void Refresh(ScreenInfo screen)
    {
        _screen = screen;
        if (Page == OptionsPage.Video)
            Rows.Rows = VideoRows();
    }

    public void Point(int row)
    {
        if (Prompt is null)
            Rows.Point(row);
    }

    /// <summary>Whether the cursor is on a volume, which steps every tick the stick is held rather than once a press.</summary>
    public bool OnVolume => Rows.Current?.Kind == OptionKind.Volume;

    /// <summary>
    /// The stick's left or right: steps a volume, which stops at its ends, or a choice, which wraps round. A volume
    /// steps once a tick while the stick is held, at once and with no wait: the user timed the original's bar from
    /// empty to full at about 1.5 s, and 100 steps at 60 a second take 1.67 s.
    /// </summary>
    public void Change(int by)
    {
        if (Prompt is not null || Rows.Current is not { } row || row.Kind is OptionKind.Link or OptionKind.Binding or OptionKind.Command)
            return;
        var value = row.Kind == OptionKind.Volume
            ? Math.Clamp(row.Value + by, 0, Settings.MaxVolume)
            : ((row.Value + by) % row.Steps + row.Steps) % row.Steps;
        if (value == row.Value)
            return;
        row.Set(value);
        Changed = true;
        // The window's mode and size change what the Video page lists.
        if (Page == OptionsPage.Video)
            Rows.Rows = VideoRows();
        Settings.Notify();
    }

    /// <summary>A: changes a choice to its next value, or opens a link. Returns the link if it leads out of the menu.</summary>
    public OptionLink? Accept()
    {
        if (Prompt is not null || AcceptOnControls())
            return null;
        switch (Rows.Current)
        {
            case { Kind: OptionKind.Choice }:
                Change(1);
                return null;
            case { Link: { } link }:
                if (PageOf(link) is not { } page)
                    return link;
                Open(page);
                return null;
            default:
                return null;
        }
    }

    /// <summary>B: back from a page to the main one, and false on the main one, which the caller leaves.</summary>
    public bool Back()
    {
        // The prompt takes every input, Back's among them, as what it binds.
        if (Prompt is not null)
            return true;
        if (Page == OptionsPage.Main)
            return false;
        var from = Page;
        Open(OptionsPage.Main);
        // Back on the row that led there.
        Rows.Point(Rows.Rows.ToList().FindIndex(r => r.Link is { } link && PageOf(link) == from));
        return true;
    }

    /// <summary>The page a link opens, or null for one that leads out of the menu.</summary>
    private static OptionsPage? PageOf(OptionLink link) => link switch
    {
        OptionLink.Audio => OptionsPage.Audio,
        OptionLink.Video => OptionsPage.Video,
        OptionLink.Enhancements => OptionsPage.Enhancements,
        OptionLink.Controls => OptionsPage.Controls,
        _ => null,
    };

    private void Open(OptionsPage page)
    {
        Page = page;
        Rows.Rows = page switch
        {
            OptionsPage.Audio => AudioRows(),
            OptionsPage.Video => VideoRows(),
            OptionsPage.Enhancements => EnhancementRows(),
            OptionsPage.Controls => ControlRows(),
            _ => MainRows(),
        };
        Rows.Point(0);
        Pulse.Reset();
    }

    private List<OptionRow> MainRows()
    {
        List<OptionRow> rows =
        [
            Link("Audio", OptionLink.Audio),
            Link("Video", OptionLink.Video),
            Link("Enhancements", OptionLink.Enhancements),
            Link("Controls", OptionLink.Controls),
        ];
        if (_inGame)
            rows.Add(Link("Rename Character", OptionLink.RenameCharacter, gapBefore: true));
        return rows;
    }

    private static OptionRow Link(string label, OptionLink link, bool gapBefore = false) =>
        new(label, OptionKind.Link, () => 0, _ => { }, [], link, gapBefore);

    private List<OptionRow> AudioRows()
    {
        var s = Settings;
        return
        [
            new("Sound Volume", OptionKind.Volume, () => s.SoundVolume, v => s.SoundVolume = v, []),
            new("Music Volume", OptionKind.Volume, () => s.MusicVolume, v => s.MusicVolume = v, []),
        ];
    }

    /// <summary>
    /// The Video page's rows. Window Size lists the sizes that fit the screen and Maximized, and in fullscreen only the screen's own;
    /// Render Resolution lists the line counts under the window's or screen's height at its shape, then its own and twice it.
    /// </summary>
    private List<OptionRow> VideoRows()
    {
        var s = Settings;
        var output = Output();
        var renders = RenderResolutions(output.Height);
        return
        [
            Choice("Display", ["Windowed", "Fullscreen"], () => s.Fullscreen ? 1 : 0, v => s.Fullscreen = v == 1),
            s.Fullscreen ? Choice("Window Size", [Size(output)], () => 0, _ => { }) : WindowSizeRow(Restored()),
            Choice("Render Resolution", [.. renders.Select(r => Size(Render(output, r)))],
                // A line count not listed, or at or past the output's own, draws at the output's own.
                () => renders.IndexOf((s.Supersampled ? 0 : s.WorldLines, s.Supersampled)) is var i and >= 0 ? i : renders.Count - 2,
                v => (s.WorldLines, s.Supersampled) = renders[v]),
            Choice("Frame Rate Limit", [.. FrameRates.Select(r => r switch { Settings.DisplayRate => "Display", 0 => "Unlimited", _ => $"{r} FPS" })],
                () => Array.IndexOf(FrameRates, s.FrameRateLimit) is var i and >= 0 ? i : 0, v => s.FrameRateLimit = FrameRates[v]),
            Choice("V-Sync", ["On", "Off"], () => s.VSync ? 0 : 1, v => s.VSync = v == 0),
            Choice("Anti-Aliasing", ["Off", "2x", "4x", "8x"], () => Array.IndexOf(Samples, s.Antialiasing) is var i and >= 0 ? i : 0,
                v => s.Antialiasing = Samples[v]),
        ];
    }

    /// <summary>
    /// What the 3D world is scaled to: the screen in fullscreen, the window as it is when it's maximized, else the window at its
    /// setting's size.
    /// </summary>
    private (int Width, int Height) Output() => Settings switch
    {
        { Fullscreen: true } => (_screen.Width, _screen.Height),
        { Maximized: true } => (_screen.WindowWidth, _screen.WindowHeight),
        _ => Restored(),
    };

    /// <summary>The window's size when it isn't maximized: its setting's, or as it opened.</summary>
    private (int Width, int Height) Restored() => Settings is { WindowWidth: > 0, WindowHeight: > 0 } s
        ? (s.WindowWidth, s.WindowHeight)
        : (_screen.WindowWidth, _screen.WindowHeight);

    /// <summary>
    /// The window sizes that fit the screen, with the window's own among them if it was sized by hand, then Maximized. A size restores
    /// a maximized window to it; the runtime sets the row to whatever the player makes the window by hand, maximized or not.
    /// </summary>
    private OptionRow WindowSizeRow((int Width, int Height) window)
    {
        var s = Settings;
        List<(int Width, int Height)> sizes = [.. WindowSizes.Where(w => w.Width <= _screen.UsableWidth && w.Height <= _screen.UsableHeight)];
        if (!sizes.Contains(window))
            sizes.Add(window);
        sizes.Sort((a, b) => (a.Width * a.Height).CompareTo(b.Width * b.Height));
        return Choice("Window Size", [.. sizes.Select(Size), "Maximized"], () => s.Maximized ? sizes.Count : sizes.IndexOf(window), v =>
        {
            if (v == sizes.Count)
                s.Maximized = true;
            else
                (s.Maximized, s.WindowWidth, s.WindowHeight) = (false, sizes[v].Width, sizes[v].Height);
        });
    }

    /// <summary>The line counts under <paramref name="height"/>, then its own (0) and twice it.</summary>
    private static List<(int Lines, bool Supersampled)> RenderResolutions(int height) =>
        [.. RenderLines.Where(l => l < height).Select(l => (l, false)), (0, false), (0, true)];

    /// <summary>The size the 3D world is drawn at for a render resolution, at the output's shape.</summary>
    private static (int Width, int Height) Render((int Width, int Height) output, (int Lines, bool Supersampled) render) => render switch
    {
        { Supersampled: true } => (output.Width * 2, output.Height * 2),
        { Lines: 0 } => output,
        _ => ((int)Math.Round((double)render.Lines * output.Width / output.Height), render.Lines),
    };

    private static string Size((int Width, int Height) size) => $"{size.Width}x{size.Height}";

    private List<OptionRow> EnhancementRows()
    {
        var s = Settings;
        return
        [
            Choice("Draw Distance", ["Original", "Extended"], () => s.ExtendedDrawDistance ? 1 : 0, v => s.ExtendedDrawDistance = v == 1),
            Choice("Object Distance", ["Original", "2x", "3x"], () => NearestIndex(ObjectDistances, s.ObjectDistance),
                v => s.ObjectDistance = ObjectDistances[v]),
            Choice("Model Detail", ["Original", "Highest"], () => s.NearestDetail ? 1 : 0, v => s.NearestDetail = v == 1),
            Choice("Rain and Snow", ["Original", "2x", "4x", "8x"], () => NearestIndex(PrecipitationDensities, s.PrecipitationDensity),
                v => s.PrecipitationDensity = PrecipitationDensities[v]),
            Choice("Menu Size", ["Original", "Smaller", "Smallest"], () => Array.IndexOf(UiLines, s.UiLines) is var i and >= 0 ? i : 0,
                v => s.UiLines = UiLines[v]),
            Choice("Menu Filter", ["Sharp", "Smooth", "Video Out"], () => Array.IndexOf(Filters, s.UiFilter) is var i and >= 0 ? i : 0,
                v => s.UiFilter = Filters[v]),
            Choice("Pace", ["Original", "Brisk"], () => s.BriskPace ? 1 : 0, v => s.BriskPace = v == 1),
            Choice("Status Auras", ["Original", "More Often"], () => s.SpellAuras == Combat.AuraTurns.EachCharacter ? 1 : 0,
                v => s.SpellAuras = v == 1 ? Combat.AuraTurns.EachCharacter : Combat.AuraTurns.Battle),
        ];
    }

    private static OptionRow Choice(string label, IReadOnlyList<string> values, Func<int> get, Action<int> set) =>
        new(label, OptionKind.Choice, get, set, values);

    // The values the pages' rows step through.
    private static readonly int[] Samples = [0, 2, 4, 8], UiLines = [240, 360, 480];

    /// <summary>The render resolutions' line counts under the window's own: the original's 240 and 480, then the usual video modes.</summary>
    private static readonly int[] RenderLines = [240, 480, 720, 1080, 1440, 2160];

    private static readonly int[] FrameRates = [Settings.DisplayRate, 30, 60, 90, 120, 144, 0];

    /// <summary>The window sizes offered, 4:3 and 16:9, of those that fit the screen.</summary>
    private static readonly (int Width, int Height)[] WindowSizes =
        [(640, 480), (960, 720), (1280, 720), (1280, 960), (1600, 900), (1600, 1200), (1920, 1080), (2560, 1440), (2880, 2160), (3840, 2160)];
    private static readonly float[] ObjectDistances = [1, 2, 3];

    /// <summary>The rain and snow's densities, up to <see cref="Particles.WeatherParticles.MaxDensity"/>.</summary>
    private static readonly float[] PrecipitationDensities = [1, 2, 4, 8];
    private static readonly UiFilter[] Filters = [UiFilter.Pixels, UiFilter.Smooth, UiFilter.VideoOut];

    private static int NearestIndex(float[] values, float value)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value))
                best = i;
        }
        return best;
    }
}
