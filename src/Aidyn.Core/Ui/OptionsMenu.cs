using Aidyn.Core.Sky;
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
}

/// <summary>Where an options row that's a <see cref="OptionKind.Link"/> leads.</summary>
public enum OptionLink
{
    Graphics,
    RenameCharacter,
}

/// <summary>
/// A row of the options menu, over a setting: its label, and for a choice its values' names, with how to
/// read and change the value, as a step from 0.
/// </summary>
/// <param name="GapBefore">Whether half a line's space comes before it, setting it off from the rows above.</param>
public sealed record OptionRow(string Label, OptionKind Kind, Func<int> Get, Action<int> Set, IReadOnlyList<string> Values,
    OptionLink? Link = null, bool GapBefore = false)
{
    /// <summary>How many steps the value has: a volume's from silent to full, or a choice's values.</summary>
    public int Steps => Kind == OptionKind.Volume ? Settings.MaxVolume + 1 : Values.Count;

    public int Value => Get();

    /// <summary>The value's name, for a choice.</summary>
    public string? ValueName => Kind == OptionKind.Choice && Values.Count > 0 ? Values[Math.Clamp(Value, 0, Values.Count - 1)] : null;
}

/// <summary>The options menu's pages.</summary>
public enum OptionsPage
{
    /// <summary>The original's: the volumes, then Graphics and, in a game, Rename Character.</summary>
    Main,

    /// <summary>The port's graphics and fidelity settings, which the original's menu has no room for.</summary>
    Graphics,
}

/// <summary>
/// The options menu (<c>OptionsMenu</c>): the original's volumes, the port's graphics and fidelity settings on
/// a page of their own, and, in a game, renaming Alaron. The pause menu's System tab opens it, and so will the
/// title screen's menu, without the rename (<paramref name="inGame"/>), as the original's title options leave
/// out its GAME rows. It changes <see cref="Settings"/> as the stick moves, so the runtime carries a setting
/// out at once.
/// </summary>
/// <remarks>
/// The port's own layout, which the user chose over the original's (Oct 3, 2026): mixed case rather than
/// capitals, and no headings, with the graphics settings a page off it rather than one row.
/// </remarks>
public sealed class OptionsMenu
{
    /// <summary>The gap a row can have before it: half a line of the main font.</summary>
    public const int Gap = 5;

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

    public OptionsMenu(Settings settings, bool inGame)
    {
        Settings = settings;
        _inGame = inGame;
        Rows = new MenuList<OptionRow>(MainRows());
    }

    public Settings Settings { get; }

    public OptionsPage Page { get; private set; }

    public MenuList<OptionRow> Rows { get; }

    /// <summary>The cursor's pulse, which turns at 20 rather than 19, 40 frames a cycle (<c>OptionsPage.Draw</c>, <c>0x800419c8</c>).</summary>
    public MenuPulse Pulse { get; } = new(top: MenuPulse.Frames);

    /// <summary>A setting changed since the menu opened, so the runtime keeps them.</summary>
    public bool Changed { get; private set; }

    /// <summary>Steps by a 60 Hz tick: the cursor's pulse.</summary>
    public void Step() => Pulse.Step();

    public void MoveCursor(int by) => Rows.Move(by);

    /// <summary>A copy on the same page with the cursor on the same row, which the page fading out draws as it was.</summary>
    public OptionsMenu Copy()
    {
        var copy = new OptionsMenu(Settings, _inGame);
        if (Page != OptionsPage.Main)
            copy.Open(Page);
        copy.Rows.Point(Rows.Cursor);
        return copy;
    }

    public void Point(int row) => Rows.Point(row);

    /// <summary>Whether the cursor is on a volume, which steps every tick the stick is held rather than once a press.</summary>
    public bool OnVolume => Rows.Current?.Kind == OptionKind.Volume;

    /// <summary>
    /// The stick's left or right: steps a volume, which stops at its ends, or a choice, which wraps round. A volume
    /// steps once a tick while the stick is held, at once and with no wait: the user timed the original's bar from
    /// empty to full at about 1.5 s, and 100 steps at 60 a second take 1.67 s.
    /// </summary>
    public void Change(int by)
    {
        if (Rows.Current is not { } row || row.Kind == OptionKind.Link)
            return;
        var value = row.Kind == OptionKind.Volume
            ? Math.Clamp(row.Value + by, 0, Settings.MaxVolume)
            : ((row.Value + by) % row.Steps + row.Steps) % row.Steps;
        if (value == row.Value)
            return;
        row.Set(value);
        Changed = true;
        Settings.Notify();
    }

    /// <summary>A: changes a choice to its next value, or opens a link. Returns the link if it leads out of the menu.</summary>
    public OptionLink? Accept()
    {
        switch (Rows.Current)
        {
            case { Kind: OptionKind.Choice }:
                Change(1);
                return null;
            case { Link: OptionLink.Graphics }:
                Open(OptionsPage.Graphics);
                return null;
            case { Link: { } link }:
                return link;
            default:
                return null;
        }
    }

    /// <summary>B: back from the graphics page to the main one, and false on the main one, which the caller leaves.</summary>
    public bool Back()
    {
        if (Page == OptionsPage.Main)
            return false;
        Open(OptionsPage.Main);
        // Back on the row that led there.
        Rows.Point(Rows.Rows.ToList().FindIndex(r => r.Link == OptionLink.Graphics));
        return true;
    }

    private void Open(OptionsPage page)
    {
        Page = page;
        Rows.Rows = page == OptionsPage.Main ? MainRows() : GraphicsRows();
        Rows.Point(0);
        Pulse.Reset();
    }

    private List<OptionRow> MainRows()
    {
        var s = Settings;
        List<OptionRow> rows =
        [
            new("Sound volume:", OptionKind.Volume, () => s.SoundVolume, v => s.SoundVolume = v, []),
            new("Music volume:", OptionKind.Volume, () => s.MusicVolume, v => s.MusicVolume = v, []),
            Link("Graphics", OptionLink.Graphics),
        ];
        if (_inGame)
            rows.Add(Link("Rename Character", OptionLink.RenameCharacter));
        return rows;
    }

    private static OptionRow Link(string label, OptionLink link) => new(label, OptionKind.Link, () => 0, _ => { }, [], link, GapBefore: true);

    /// <summary>The graphics page's rows, each a setting with its values' names.</summary>
    private List<OptionRow> GraphicsRows()
    {
        var s = Settings;
        return
        [
            Choice("Display", ["Windowed", "Fullscreen"], () => s.Fullscreen ? 1 : 0, v => s.Fullscreen = v == 1),
            Choice("V-sync", ["On", "Off"], () => s.VSync ? 0 : 1, v => s.VSync = v == 0),
            Choice("3D resolution", ["Full", "480 lines", "240 lines"], () => Array.IndexOf(WorldLines, s.WorldLines) is var i and >= 0 ? i : 0,
                v => s.WorldLines = WorldLines[v]),
            Choice("Anti-aliasing", ["Off", "2x", "4x", "8x"], () => Array.IndexOf(Samples, s.Antialiasing) is var i and >= 0 ? i : 0,
                v => s.Antialiasing = Samples[v]),
            Choice("Draw distance", ["Original", "Farther", "Farthest"], () => NearestIndex(DrawDistances, s.DrawDistance),
                v => s.DrawDistance = DrawDistances[v]),
            Choice("Model detail", ["Original", "Highest"], () => s.NearestDetail ? 1 : 0, v => s.NearestDetail = v == 1),
            Choice("Rain and snow", ["Original", "2x", "4x", "8x"], () => NearestIndex(PrecipitationDensities, s.PrecipitationDensity),
                v => s.PrecipitationDensity = PrecipitationDensities[v]),
            Choice("Sky", ["Projected", "Original"], () => s.SkyPlacement == SkyPlacement.Original ? 1 : 0,
                v => s.SkyPlacement = v == 1 ? SkyPlacement.Original : SkyPlacement.Projected),
            Choice("Menu size", ["Original", "Smaller", "Smallest"], () => Array.IndexOf(UiLines, s.UiLines) is var i and >= 0 ? i : 0,
                v => s.UiLines = UiLines[v]),
            Choice("Menu filter", ["Video out", "Sharp", "Smooth"], () => Array.IndexOf(Filters, s.UiFilter) is var i and >= 0 ? i : 0,
                v => s.UiFilter = Filters[v]),
            Choice("Pace", ["Original", "Brisk"], () => s.BriskPace ? 1 : 0, v => s.BriskPace = v == 1),
            Choice("Spell auras", ["Original", "Each character"], () => s.SpellAuras == Combat.AuraTurns.EachCharacter ? 1 : 0,
                v => s.SpellAuras = v == 1 ? Combat.AuraTurns.EachCharacter : Combat.AuraTurns.Battle),
        ];
    }

    private static OptionRow Choice(string label, IReadOnlyList<string> values, Func<int> get, Action<int> set) =>
        new(label, OptionKind.Choice, get, set, values);

    // The values the graphics page's rows step through.
    private static readonly int[] WorldLines = [0, 480, 240], Samples = [0, 2, 4, 8], UiLines = [240, 360, 480];
    private static readonly float[] DrawDistances = [1, 1.2f, 1.4f];

    /// <summary>The rain and snow's densities, up to <see cref="Particles.WeatherParticles.MaxDensity"/>.</summary>
    private static readonly float[] PrecipitationDensities = [1, 2, 4, 8];
    private static readonly UiFilter[] Filters = [UiFilter.VideoOut, UiFilter.Pixels, UiFilter.Smooth];

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
