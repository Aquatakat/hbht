using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>The System tab's rows (<c>WidgetOptionsMenu::MakeScrollList</c>).</summary>
public enum SystemItem
{
    LoadGame,
    SaveGame,
    Options,
    Theater,
    Credits,
    Help,

    /// <summary>Combat's alone, while the party can flee.</summary>
    Flee,
}

/// <summary>What the System tab shows: its list, or a screen it opened.</summary>
public enum SystemPage
{
    List,
    Options,
    Saves,
    Rename,
    Theatre,
}

/// <summary>A page of the System tab as it was when the tab left it, which fades out over the next.</summary>
public sealed record SystemScreen(SystemPage Page, OptionsMenu? Options, SaveMenu? Saves, NameEntry? Rename, TheatreMenu? Theatre);

/// <summary>A page of the System tab, and what the stick, A, B and the mouse do on it.</summary>
public interface ISystemPage
{
    SystemPage Kind { get; }

    /// <summary>The stick's up and down.</summary>
    void MoveCursor(int by);

    /// <summary>The stick's left and right: steps an option, the save shown, or the name entry's key.</summary>
    void Sideways(int by);

    /// <summary>A, and how the menu ends if it closes for it, or null if it stays open.</summary>
    PauseExit? Accept();

    /// <summary>B: back a step, or false on the list, so the caller closes the menu.</summary>
    bool Back();

    /// <summary>Puts the cursor on a row, as hovering does.</summary>
    void Point(int row);
}

/// <summary>
/// The pause menu's System tab (<c>WidgetOptionsMenu</c>): a list of Load Game, Save Game, Options, Theater, Credits
/// and Help under the title "Menu", and the screens they open (<see cref="ISystemPage"/>). B on the list closes the menu;
/// in a screen, it goes back to the list.
/// </summary>
/// <remarks>
/// <para>
/// In trek every row shows but Flee; in combat (<c>OptionsMenu.OptionsMenu</c>'s branch for game mode 3) it lists only
/// Options, and Flee while the party can flee from a turn's pause, which returns <see cref="PauseExit.Flee"/>. Credits closes the menu and plays the credits (<c>pauseMenuSelectCallBack</c>'s row 9999,
/// exit code 5), which <see cref="Accept"/> returns as <see cref="PauseExit.Credits"/>. Theater opens the Theatre's page
/// (<c>OptionsMenu.ShowTheatre</c>, <c>0x8003fe34</c>), where A on a seen cinematic closes the menu and plays it
/// (<see cref="PauseExit.Theatre"/>, exit code 2). Help closes the menu and starts the help menu's script
/// (<c>pauseMenuSelectCallBack</c>, <c>0x800403ac</c> in 1.0; <see cref="PauseExit.Help"/>, exit code 3).
/// </para>
/// <para>
/// The cursor starts on the first row each time the tab is built, as the original rebuilds it at each opening.
/// </para>
/// <para>
/// Each page crossfades with the one before (<c>OptionsMenu.Step</c>, <c>0x8003f81c</c>): the new one comes in at alpha 0
/// and the old stays at 255, then each tick the new rises by 60 and the old falls by 60, 5 ticks each way. The
/// original fades the list, the options, the save and load screens and the Theatre's page so; the port fades the
/// options' pages and the name entry the same, as the user asked (Oct 17, 2026).
/// </para>
/// </remarks>
public sealed class SystemMenu
{
    /// <summary>The list's colour (<c>COLOR_RED1</c>).</summary>
    public static readonly Rgba TextColor = PartyMenu.TextColor;

    /// <summary>The rows' strings, by <see cref="SystemItem"/>: common lines, but Credits, which the code spells out.</summary>
    private static readonly int[] Lines = [160, 377, 161, 162, -1, 173, 378];

    private readonly PluginSet _plugins;
    private readonly MenuText _text;
    private readonly string _savesFolder;
    private readonly Func<string, IReadOnlyList<SaveSlot>>? _listSaves;
    private readonly Func<RecordId, bool> _isSet;
    private readonly Func<ScreenInfo> _screen;
    private ISystemPage _page;

    /// <param name="party">The party, whose Alaron the options menu renames.</param>
    /// <param name="savesFolder">Where the save and load screens find saves.</param>
    /// <param name="listSaves">Lists the saves in a folder; <see cref="SaveSlot.List"/> by default.</param>
    /// <param name="isSet">Whether a flag is set, for the Theatre's cinematics; none are, by default.</param>
    /// <param name="combat">
    /// In combat, whether the party can flee: the list is Options alone, and Flee with it (<see cref="Combat.Battle.CanFlee"/>).
    /// Null in trek.
    /// </param>
    /// <param name="screen">The screen the game is on as the options open, for the Video page; <see cref="ScreenInfo.Default"/> by default.</param>
    public SystemMenu(PluginSet plugins, Settings settings, Party party, string savesFolder, Func<string, IReadOnlyList<SaveSlot>>? listSaves = null,
        Func<RecordId, bool>? isSet = null, bool? combat = null, Func<ScreenInfo>? screen = null)
    {
        _isSet = isSet ?? (_ => false);
        _screen = screen ?? (() => ScreenInfo.Default);
        _plugins = plugins;
        _text = MenuText.From(plugins);
        Settings = settings;
        Party = party;
        _savesFolder = savesFolder;
        _listSaves = listSaves;
        Items = new MenuList<SystemItem>(combat switch
        {
            null => [.. Enum.GetValues<SystemItem>().Where(i => i != SystemItem.Flee)],
            true => [SystemItem.Options, SystemItem.Flee],
            false => [SystemItem.Options],
        });
        _page = new ListPage(this);
    }

    public Settings Settings { get; }

    public Party Party { get; }

    public MenuList<SystemItem> Items { get; }

    public MenuPulse Pulse { get; } = new();

    public SystemPage Page => _page.Kind;

    public OptionsMenu? Options { get; private set; }

    public SaveMenu? Saves { get; private set; }

    public NameEntry? Rename { get; private set; }

    public TheatreMenu? Theatre { get; private set; }

    /// <summary>The page fading out, or null.</summary>
    public SystemScreen? Outgoing { get; private set; }

    /// <summary>The page's alpha, rising to 255 as it fades in, and the page fading out's, falling to 0 (<see cref="WidgetFade"/>).</summary>
    public byte PageAlpha { get; private set; } = 255;

    public byte OutgoingAlpha { get; private set; }

    /// <summary>Whether the cursor is on a volume, which the stick steps every tick it's held (<see cref="OptionsMenu.OnVolume"/>).</summary>
    public bool OnVolume => Page == SystemPage.Options && Options!.OnVolume;

    /// <summary>Whether the list is showing, which is the only time the tab can be left.</summary>
    public bool OnList => Page == SystemPage.List;

    /// <summary>A row's text.</summary>
    public string Label(SystemItem item) => Lines[(int)item] is var line and >= 0 ? _text[line] : "Credits";

    /// <summary>The help menu's script, which Help starts (<c>0x054B</c>).</summary>
    public static readonly RecordId HelpScript = RecordId.Hex(RecordTypes.Dialogue, 0x054B);

    public void Step()
    {
        PageAlpha = (byte)WidgetFade.In(PageAlpha);
        if (Outgoing is not null)
        {
            OutgoingAlpha = (byte)WidgetFade.Out(OutgoingAlpha);
            if (OutgoingAlpha == 0)
                Outgoing = null;
        }
        Pulse.Step();
        Options?.Step();
        Saves?.Step();
        Rename?.Step();
        Theatre?.Step();
    }

    /// <inheritdoc cref="ISystemPage.MoveCursor"/>
    public void MoveCursor(int by) => _page.MoveCursor(by);

    /// <inheritdoc cref="ISystemPage.Sideways"/>
    public void Sideways(int by) => _page.Sideways(by);

    /// <summary>A: what the page does with it, and how the menu ends if Credits, Help or a seen cinematic close it.</summary>
    public PauseExit? Accept() => _page.Accept();

    /// <summary>Start: in the name entry, goes to OK or confirms; elsewhere it's B.</summary>
    public bool Start()
    {
        if (Page != SystemPage.Rename)
            return Back();
        Rename!.Start();
        CloseRename();
        return true;
    }

    /// <inheritdoc cref="ISystemPage.Back"/>
    public bool Back() => _page.Back();

    /// <inheritdoc cref="ISystemPage.Point"/>
    public void Point(int row) => _page.Point(row);

    /// <summary>
    /// Follows a screen that finished outside the tab's own presses: the name entry confirmed or cancelled from the
    /// keyboard, or the save screen done with what the runtime did for it.
    /// </summary>
    public void Settle()
    {
        CloseRename();
        if (Saves is { Closed: true })
            ToList();
    }

    private Character? Alaron => Party.Slots.FirstOrDefault(m => m?.Entity == GameState.Alaron);

    /// <summary>
    /// Opens the Theatre's page afresh, its titles as the flags stand, as Theater does, fading in, and as the menu
    /// reopens after a cinematic the Theatre played (<c>Theatre.Reopen</c>, <c>0x800d955c</c>), at once.
    /// </summary>
    public void ShowTheatre(bool fade = true)
    {
        if (fade)
            FadeFrom();
        else
            (Outgoing, PageAlpha, OutgoingAlpha) = (null, 255, 0);
        Theatre = new TheatreMenu(_plugins, _isSet);
        _page = new TheatrePage(this);
    }

    private void CloseRename()
    {
        if (Rename is not { Closed: true } rename)
            return;
        FadeFrom();
        if (rename.Chosen is { } name && Alaron is { } alaron)
            alaron.Name = name;
        Rename = null;
        _page = new OptionsPage(this);
    }

    /// <summary>
    /// Starts the page shown fading out as the next fades in: as it stands, or as <paramref name="options"/>, a copy of
    /// the options from before they changed page.
    /// </summary>
    private void FadeFrom(OptionsMenu? options = null)
    {
        Outgoing = new SystemScreen(Page, options ?? (Page == SystemPage.Options ? Options?.Copy() : Options), Saves, Rename, Theatre);
        PageAlpha = 0;
        OutgoingAlpha = 255;
    }

    private void ToList()
    {
        FadeFrom();
        _page = new ListPage(this);
        Saves = null;
        Theatre = null;
        Pulse.Reset();
    }

    /// <summary>The list: A opens a row's screen, or ends the menu for Credits or Help.</summary>
    private sealed class ListPage(SystemMenu menu) : ISystemPage
    {
        public SystemPage Kind => SystemPage.List;

        public void MoveCursor(int by) => menu.Items.Move(by);

        public void Sideways(int by)
        {
        }

        public PauseExit? Accept()
        {
            var item = menu.Items.Current;
            switch (item)
            {
                case SystemItem.LoadGame or SystemItem.SaveGame:
                    menu.FadeFrom();
                    menu.Saves = SaveMenu.From(menu._plugins, item == SystemItem.SaveGame, menu._savesFolder, menu._listSaves);
                    menu._page = new SavesPage(menu);
                    return null;
                case SystemItem.Options:
                    menu.FadeFrom();
                    menu.Options = new OptionsMenu(menu.Settings, inGame: true, menu._screen());
                    menu._page = new OptionsPage(menu);
                    return null;
                case SystemItem.Theater:
                    menu.ShowTheatre();
                    return null;
                case SystemItem.Credits:
                    return PauseExit.Credits;
                case SystemItem.Help:
                    return PauseExit.Help;
                case SystemItem.Flee:
                    return PauseExit.Flee;
                default:
                    return null;
            }
        }

        public bool Back() => false;

        public void Point(int row) => menu.Items.Point(row);
    }

    /// <summary>The options, and their pages: A follows a link, and Rename Character opens the name entry.</summary>
    private sealed class OptionsPage(SystemMenu menu) : ISystemPage
    {
        public SystemPage Kind => SystemPage.Options;

        public void MoveCursor(int by) => menu.Options!.MoveCursor(by);

        public void Sideways(int by) => menu.Options!.Change(by);

        public PauseExit? Accept()
        {
            var options = menu.Options!;
            var before = options.Copy();
            if (options.Accept() == OptionLink.RenameCharacter && menu.Alaron is { } alaron)
            {
                menu.FadeFrom(before);
                menu.Rename = new NameEntry(alaron.Name);
                menu._page = new RenamePage(menu);
            }
            else if (options.Page != before.Page)
            {
                menu.FadeFrom(before);
            }
            return null;
        }

        public bool Back()
        {
            var options = menu.Options!;
            var before = options.Copy();
            if (!options.Back())
                menu.ToList();
            else
                menu.FadeFrom(before);
            return true;
        }

        public void Point(int row) => menu.Options!.Point(row);
    }

    /// <summary>The save and load screens, which go back to the list once they're done.</summary>
    private sealed class SavesPage(SystemMenu menu) : ISystemPage
    {
        public SystemPage Kind => SystemPage.Saves;

        public void MoveCursor(int by) => menu.Saves!.MoveCursor(by);

        public void Sideways(int by) => menu.Saves!.Shift(by);

        public PauseExit? Accept()
        {
            var saves = menu.Saves!;
            saves.Accept();
            if (saves.Closed)
                menu.ToList();
            return null;
        }

        public bool Back()
        {
            var saves = menu.Saves!;
            saves.Back();
            if (saves.Closed)
                menu.ToList();
            return true;
        }

        public void Point(int row) => menu.Saves!.Point(row);
    }

    /// <summary>The name entry over the options, which renames Alaron as it closes.</summary>
    private sealed class RenamePage(SystemMenu menu) : ISystemPage
    {
        public SystemPage Kind => SystemPage.Rename;

        public void MoveCursor(int by) => menu.Rename!.Move(0, by);

        public void Sideways(int by) => menu.Rename!.Move(by, 0);

        public PauseExit? Accept()
        {
            menu.Rename!.Accept();
            menu.CloseRename();
            return null;
        }

        public bool Back()
        {
            menu.Rename!.Back();
            menu.CloseRename();
            return true;
        }

        public void Point(int row)
        {
        }
    }

    /// <summary>The Theatre's page: A on a seen cinematic ends the menu to play it.</summary>
    private sealed class TheatrePage(SystemMenu menu) : ISystemPage
    {
        public SystemPage Kind => SystemPage.Theatre;

        public void MoveCursor(int by) => menu.Theatre!.MoveCursor(by);

        public void Sideways(int by)
        {
        }

        public PauseExit? Accept()
        {
            var theatre = menu.Theatre!;
            theatre.Accept();
            return theatre.Chosen is { } cinematic ? PauseExit.Theatre(cinematic) : null;
        }

        public bool Back()
        {
            menu.ToList();
            return true;
        }

        public void Point(int row) => menu.Theatre!.Point(row);
    }
}
