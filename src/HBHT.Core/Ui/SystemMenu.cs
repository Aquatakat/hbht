using HBHT.Core.Characters;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

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

    /// <summary>The title's alone: the name entry, then a new game (<c>IntroMenu::ProcessIntroMenu</c>).</summary>
    NewGame,

    /// <summary>
    /// The port's, last in every list: the title's quits at once, and the pause menu's asks where to go (<see cref="SystemMenu.Quitting"/>).
    /// </summary>
    Quit,
}

/// <summary>
/// The colours the System tab's pages are drawn in: the pause menu's browns on the parchment, or the title menu's whites on its
/// feathered boxes (<c>IntroMenu</c>): the same pages in the same layout.
/// </summary>
/// <param name="Text">The rows' colour: the list's, the options', the save screens' and the name entry's.</param>
/// <param name="CursorFrom">Where the list's and the save screens' cursor pulses from.</param>
/// <param name="CursorTo">And toward.</param>
/// <param name="OptionsCursor">What the options' cursor pulses toward from <paramref name="Text"/>.</param>
/// <param name="Blank">"Create New Save" and the empty load screen's line.</param>
/// <param name="Heading">The Controls page's situations.</param>
/// <param name="BarEmpty">The volume bars' empty part.</param>
/// <param name="Parchment">Whether the pages are on the System tab's parchment, or each on a feathered black box.</param>
public sealed record SystemStyle(Rgba Text, Rgba CursorFrom, Rgba CursorTo, Rgba OptionsCursor, Rgba Blank, Rgba Heading, Rgba BarEmpty,
    bool Parchment)
{
    /// <summary>The pause menu's: <c>COLOR_RED1</c> pulsing to <c>COLOR_PURPLE1</c>, and the options' to dark brown, its alpha falling.</summary>
    public static readonly SystemStyle Pause = new(PartyMenu.TextColor, PartyMenu.HighlightFrom, PartyMenu.HighlightColor,
        OptionsMenu.HighlightColor, SaveMenu.BlankColor, new Rgba(90, 55, 45, 255), OptionsMenu.BarEmpty, Parchment: true);

    /// <summary>
    /// The title's (<c>IntroMenu.MakeList</c>, <c>0x80043ae8</c>; <c>IntroMenu.ShowLoad</c>, <c>0x800439a8</c>; <c>OptionsPage.Create</c>
    /// with 0): off-white, the cursor pulsing between the scroll menu's grey (<see cref="MenuPulse.ScrollFrom"/>) and white.
    /// </summary>
    public static readonly SystemStyle Title = new(new Rgba(225, 225, 225, 255), MenuPulse.ScrollFrom, MenuPulse.ScrollTo, MenuPulse.ScrollFrom,
        new Rgba(160, 160, 160, 255), new Rgba(160, 160, 160, 255), new Rgba(100, 100, 100, 63), Parchment: false);
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
/// Quit Game is the port's, as the user asked (Oct 25, 2026): a console game needs none, but a game in a full screen does. On the
/// title it quits at once, there being nothing to lose. In the pause menu it asks, in the save screen's box (<see cref="MenuChoice"/>),
/// whether to quit to the desktop or return to the title, or to cancel, as B does too; above the choices it says that unsaved progress
/// will be lost, unless the game was saved from this opening of the menu and nothing has changed since (<see cref="PauseMenu.Unsaved"/>).
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
    private static readonly int[] Lines = [160, 377, 161, 162, -1, 173, 378, 159, -1];

    /// <summary>The quit box's choices, and what it says above them while there's progress to lose.</summary>
    public const string QuitToDesktop = "Quit to Desktop", ReturnToTitle = "Return to Title", Unsaved = "Unsaved progress will be lost.";

    /// <summary>The space between the quit box's warning and its choices.</summary>
    public const int QuitGap = 8;

    private readonly PluginSet _plugins;
    private readonly MenuText _text;
    private readonly string _savesFolder;
    private readonly Func<string, IReadOnlyList<SaveSlot>>? _listSaves;
    private readonly Func<RecordId, bool> _isSet;
    private readonly Func<ScreenInfo> _screen;
    private readonly Func<bool> _unsaved;
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
    /// <param name="title">
    /// The title's menu (<c>IntroMenu</c>): New Game, Load Game and Options, in <see cref="SystemStyle.Title"/>, its pages crossfading 20 a
    /// tick (<c>IntroMenu.Step</c>, <c>0x80042d0c</c>). Its options have no Rename Character, and its load screen loads.
    /// </param>
    /// <param name="unsaved">Whether quitting would lose progress, which the quit box warns of; always, by default.</param>
    public SystemMenu(PluginSet plugins, Settings settings, Party party, string savesFolder, Func<string, IReadOnlyList<SaveSlot>>? listSaves = null,
        Func<RecordId, bool>? isSet = null, bool? combat = null, Func<ScreenInfo>? screen = null, bool title = false, Func<bool>? unsaved = null)
    {
        Title = title;
        Style = title ? SystemStyle.Title : SystemStyle.Pause;
        _isSet = isSet ?? (_ => false);
        _screen = screen ?? (() => ScreenInfo.Default);
        _unsaved = unsaved ?? (() => true);
        _plugins = plugins;
        _text = MenuText.From(plugins);
        Settings = settings;
        Party = party;
        _savesFolder = savesFolder;
        _listSaves = listSaves;
        Items = new MenuList<SystemItem>(combat switch
        {
            null when title => [SystemItem.NewGame, SystemItem.LoadGame, SystemItem.Options, SystemItem.Quit],
            null => [.. Enum.GetValues<SystemItem>().Where(i => i is not (SystemItem.Flee or SystemItem.NewGame))],
            true => [SystemItem.Options, SystemItem.Flee, SystemItem.Quit],
            false => [SystemItem.Options, SystemItem.Quit],
        });
        _page = new ListPage(this);
    }

    public Settings Settings { get; }

    /// <summary>Whether it's the title's menu, which starts a new game or loads one.</summary>
    public bool Title { get; }

    /// <summary>The colours its pages are drawn in.</summary>
    public SystemStyle Style { get; }

    /// <summary>How much a page's alpha changes a tick as it crossfades: the pause menu's 60, the title's 20.</summary>
    public int FadeStep => Title ? 20 : WidgetFade.Step;

    /// <summary>
    /// The title's New Game, once its name is confirmed, or its Quit Game: how the menu ends (<see cref="PauseExit.NewGame"/>,
    /// <see cref="PauseExit.Quit"/>). Null until then.
    /// </summary>
    public PauseExit? Ended { get; private set; }

    /// <summary>Whether the pages are still crossfading, which the title's menu takes no presses through (<c>IntroMenu.Input</c>, <c>0x80043e44</c>).</summary>
    public bool Fading => PageAlpha < 255 || Outgoing is not null;

    public Party Party { get; }

    public MenuList<SystemItem> Items { get; }

    public MenuPulse Pulse { get; } = new();

    public SystemPage Page => _page.Kind;

    public OptionsMenu? Options { get; private set; }

    /// <summary>The window changed: the options' Video page, if it's open, lists what the screen allows now.</summary>
    public void ScreenChanged() => Options?.Refresh(_screen());

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

    /// <summary>Whether the list is showing with no box over it, which is the only time the tab can be left.</summary>
    public bool OnList => Page == SystemPage.List && Quitting is null;

    /// <summary>The quit box over the list, which takes every press until it's answered or B closes it.</summary>
    public MenuOverlay Overlay { get; } = new();

    /// <summary>Quit Game's question: where to go, and a warning if there's unsaved progress. Null while it isn't up.</summary>
    public MenuChoice? Quitting => Overlay.Choice;

    /// <summary>A row's text: common lines, but Credits, which the code spells out, and Quit Game, the port's.</summary>
    public string Label(SystemItem item) => Lines[(int)item] is var line and >= 0 ? _text[line] : item == SystemItem.Quit ? "Quit Game" : "Credits";

    /// <summary>The help menu's script, which Help starts (<c>0x054B</c>).</summary>
    public static readonly RecordId HelpScript = RecordId.Hex(RecordTypes.Dialogue, 0x054B);

    public void Step()
    {
        PageAlpha = (byte)WidgetFade.In(PageAlpha, FadeStep);
        if (Outgoing is not null)
        {
            OutgoingAlpha = (byte)WidgetFade.Out(OutgoingAlpha, FadeStep);
            if (OutgoingAlpha == 0)
                Outgoing = null;
        }
        Pulse.Step();
        Overlay.Step();
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

    /// <summary>Start: in the name entry, goes to OK or confirms; on the title's list it's A (<c>IntroMenu.OnStart</c>); elsewhere it's B.</summary>
    public bool Start()
    {
        if (Title && Page == SystemPage.List)
        {
            Accept();
            return true;
        }
        if (Page != SystemPage.Rename)
            return Back();
        Rename!.Start();
        CloseRename();
        return true;
    }

    /// <inheritdoc cref="ISystemPage.Back"/>
    public bool Back() => _page.Back();

    /// <summary>The menus' Appraise: on the options' Controls page, clears the binding under the cursor (<see cref="OptionsMenu.Clear"/>).</summary>
    public bool Clear() => Page == SystemPage.Options && Options!.Clear();

    /// <inheritdoc cref="ISystemPage.Point"/>
    public void Point(int row) => _page.Point(row);

    /// <summary>Puts the quit box's cursor on a choice, as hovering does.</summary>
    public void PointQuit(int choice) => Quitting?.Choices.Point(choice);

    /// <summary>
    /// Puts Quit Game's question up, its cursor on Quit to Desktop, warning of progress to lose if there's any: in the shop's colours,
    /// white over tan (<see cref="MenuChoice.Question"/>), which show the cursor better among three choices than the save screen's off-white.
    /// </summary>
    private void AskQuit()
    {
        var unsaved = _unsaved();
        Overlay.Ask(MenuChoice.Question(unsaved ? Unsaved : "", [QuitToDesktop, ReturnToTitle, _text[SaveMenu.Cancel]]) with
        {
            Gap = unsaved ? QuitGap : 0,
        });
    }

    /// <summary>A on the quit box: the program ends or the title shows, or Cancel takes the box down.</summary>
    private PauseExit? AnswerQuit(int choice)
    {
        Overlay.CloseChoice();
        return choice switch
        {
            0 => PauseExit.Quit,
            1 => PauseExit.ToTitle,
            _ => null,
        };
    }

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

    /// <summary>
    /// Starts the page shown fading in from nothing, as the title's list does as Start brings it up over the logo
    /// (<c>IntroMenu.ShowList</c>, <c>0x80043598</c>).
    /// </summary>
    public void FadeIn() => (Outgoing, PageAlpha, OutgoingAlpha) = (null, 0, 0);

    private void CloseRename()
    {
        if (Rename is not { Closed: true } rename)
            return;
        if (Title)
        {
            // The title's New Game: OK starts the game with the name (IntroMenu.Confirm, 0x80043cfc, state 6), and B or the cancel key
            // goes back to the list.
            if (rename.Chosen is { } chosen)
            {
                Ended = PauseExit.NewGame(chosen);
                return;
            }
            ToList();
            Rename = null;
            return;
        }
        FadeFrom();
        if (rename.Chosen is { } name && Alaron is { } alaron)
            alaron.Name = name;
        Rename = null;
        _page = new OptionsPage(this);
    }

    // The keyboard in the name entry, before any key reaches the menu's actions: a printable key types its character, Backspace
    // rubs out, Enter confirms and Escape cancels. Each is true if the name entry was up to take it.

    /// <summary>Types a character into the name entry (<see cref="NameEntry.Type"/>). False with no entry up, or a character it has no key for.</summary>
    public bool TypeName(char c)
    {
        if (Rename is not { } entry || Ended is not null || !entry.Type(c))
            return false;
        Settle();
        return true;
    }

    /// <summary>Rubs the last letter out.</summary>
    public bool RubOutName() => NameKey(entry => entry.RubOut());

    /// <summary>Confirms the name, as OK does.</summary>
    public bool ConfirmName() => NameKey(entry => entry.Confirm());

    /// <summary>Cancels the name entry.</summary>
    public bool CancelName() => NameKey(entry => entry.Back());

    private bool NameKey(Action<NameEntry> act)
    {
        if (Rename is not { } entry || Ended is not null)
            return false;
        act(entry);
        Settle();
        return true;
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

    /// <summary>The list: A opens a row's screen, or ends the menu for Credits or Help; and the quit box over it, which takes the presses while it's up.</summary>
    private sealed class ListPage(SystemMenu menu) : ISystemPage
    {
        public SystemPage Kind => SystemPage.List;

        public void MoveCursor(int by)
        {
            if (!menu.Overlay.MoveCursor(by))
                menu.Items.Move(by);
        }

        public void Sideways(int by)
        {
        }

        public PauseExit? Accept()
        {
            if (menu.Quitting is { } quitting)
                return menu.AnswerQuit(quitting.Choices.Cursor);
            var item = menu.Items.Current;
            switch (item)
            {
                case SystemItem.NewGame:
                    // The name entry on the new game's Alaron (IntroMenu.ShowNameEntry, 0x800437f8).
                    menu.FadeFrom();
                    menu.Rename = new NameEntry(menu.Alaron?.Name ?? "Alaron");
                    menu._page = new RenamePage(menu);
                    return null;
                case SystemItem.LoadGame or SystemItem.SaveGame:
                    menu.FadeFrom();
                    menu.Saves = SaveMenu.From(menu._plugins, item == SystemItem.SaveGame, menu._savesFolder, menu._listSaves);
                    menu._page = new SavesPage(menu);
                    return null;
                case SystemItem.Options:
                    menu.FadeFrom();
                    menu.Options = new OptionsMenu(menu.Settings, inGame: !menu.Title, menu._screen());
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
                case SystemItem.Quit when menu.Title:
                    return menu.Ended = PauseExit.Quit;
                case SystemItem.Quit:
                    menu.AskQuit();
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>B takes the quit box down, or on the list closes the menu.</summary>
        public bool Back()
        {
            if (menu.Quitting is null)
                return false;
            menu.Overlay.CloseChoice();
            return true;
        }

        public void Point(int row)
        {
            if (menu.Quitting is null)
                menu.Items.Point(row);
        }
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
            if (menu.Ended is not null)
                return null;
            menu.Rename!.Accept();
            menu.CloseRename();
            return menu.Ended;
        }

        public bool Back()
        {
            if (menu.Ended is not null)
                return true;
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
