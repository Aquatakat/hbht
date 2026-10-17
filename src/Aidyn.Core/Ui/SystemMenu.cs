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
}

/// <summary>What the System tab shows: its list, or a screen it opened.</summary>
public enum SystemPage
{
    List,
    Options,
    Saves,
    Rename,
}

/// <summary>
/// The pause menu's System tab (<c>WidgetOptionsMenu</c>): a list of Load Game, Save Game, Options, Theater, Credits
/// and Help under the title "Menu", and the screens they open. B on the list closes the menu; in a screen, it goes
/// back to the list.
/// </summary>
/// <remarks>
/// <para>
/// In trek every row shows; in combat the original lists only Options, and Flee when the party can flee, which
/// waits for combat (2.5). Theater and Credits aren't ported yet: the cinematics and the credits' roll wait for their
/// own systems, so they're drawn greyed and A does nothing on them. Help closes the menu and starts the help menu's
/// script (<c>pauseMenuSelectCallBack</c>, <c>0x800403ac</c> in 1.0), which the runtime does once
/// <see cref="HelpChosen"/> is set.
/// </para>
/// <para>
/// The cursor starts on the first row each time the tab is built, as the original rebuilds it at each opening.
/// </para>
/// </remarks>
public sealed class SystemMenu
{
    /// <summary>The list's colour (<c>COLOR_RED1</c>), and the greyed rows' (the resolution row's colour without the Expansion Pak).</summary>
    public static readonly Rgba TextColor = PartyMenu.TextColor, Greyed = new(96, 96, 96, 255);

    /// <summary>The rows' strings, by <see cref="SystemItem"/>: common lines, but Credits, which the code spells out.</summary>
    private static readonly int[] Lines = [160, 377, 161, 162, -1, 173];

    private readonly PluginSet _plugins;
    private readonly MenuText _text;
    private readonly string _savesFolder;
    private readonly Func<string, IReadOnlyList<SaveSlot>>? _listSaves;

    /// <param name="party">The party, whose Alaron the options menu renames.</param>
    /// <param name="savesFolder">Where the save and load screens find saves.</param>
    /// <param name="listSaves">Lists the saves in a folder; <see cref="SaveSlot.List"/> by default.</param>
    public SystemMenu(PluginSet plugins, Settings settings, Party party, string savesFolder, Func<string, IReadOnlyList<SaveSlot>>? listSaves = null)
    {
        _plugins = plugins;
        _text = MenuText.From(plugins);
        Settings = settings;
        Party = party;
        _savesFolder = savesFolder;
        _listSaves = listSaves;
        Items = new MenuList<SystemItem>(Enum.GetValues<SystemItem>());
    }

    public Settings Settings { get; }

    public Party Party { get; }

    public MenuList<SystemItem> Items { get; }

    public MenuPulse Pulse { get; } = new();

    public SystemPage Page { get; private set; }

    public OptionsMenu? Options { get; private set; }

    public SaveMenu? Saves { get; private set; }

    public NameEntry? Rename { get; private set; }

    /// <summary>Whether the cursor is on a volume, which the stick steps every tick it's held (<see cref="OptionsMenu.OnVolume"/>).</summary>
    public bool OnVolume => Page == SystemPage.Options && Options!.OnVolume;

    /// <summary>Whether the list is showing, which is the only time the tab can be left.</summary>
    public bool OnList => Page == SystemPage.List;

    /// <summary>A row's text.</summary>
    public string Label(SystemItem item) => Lines[(int)item] is var line and >= 0 ? _text[line] : "Credits";

    /// <summary>Whether a row does anything yet.</summary>
    public static bool Ported(SystemItem item) => item is SystemItem.LoadGame or SystemItem.SaveGame or SystemItem.Options or SystemItem.Help;

    /// <summary>The help menu's script, which Help starts (<c>0x054B</c>).</summary>
    public static readonly RecordId HelpScript = RecordId.Hex(RecordTypes.Dialogue, 0x054B);

    /// <summary>Whether Help was chosen, so the menu closes and the help menu starts (the menu's exit code 3).</summary>
    public bool HelpChosen { get; private set; }

    public void Step()
    {
        Pulse.Step();
        Options?.Step();
        Saves?.Step();
        Rename?.Step();
    }

    public void MoveCursor(int by)
    {
        switch (Page)
        {
            case SystemPage.List:
                Items.Move(by);
                break;
            case SystemPage.Options:
                Options!.MoveCursor(by);
                break;
            case SystemPage.Saves:
                Saves!.MoveCursor(by);
                break;
            case SystemPage.Rename:
                Rename!.Move(0, by);
                break;
        }
    }

    /// <summary>The stick's left and right: steps an option, the save shown, or the name entry's key.</summary>
    public void Sideways(int by)
    {
        switch (Page)
        {
            case SystemPage.Options:
                Options!.Change(by);
                break;
            case SystemPage.Saves:
                Saves!.Shift(by);
                break;
            case SystemPage.Rename:
                Rename!.Move(by, 0);
                break;
        }
    }

    public void Accept()
    {
        switch (Page)
        {
            case SystemPage.List:
                Open(Items.Current);
                break;
            case SystemPage.Options:
                if (Options!.Accept() == OptionLink.RenameCharacter && Alaron is { } alaron)
                {
                    Rename = new NameEntry(alaron.Name);
                    Page = SystemPage.Rename;
                }
                break;
            case SystemPage.Saves:
                Saves!.Accept();
                if (Saves.Closed)
                    ToList();
                break;
            case SystemPage.Rename:
                Rename!.Accept();
                CloseRename();
                break;
        }
    }

    /// <summary>Start: in the name entry, goes to OK or confirms; elsewhere it's B.</summary>
    public bool Start()
    {
        if (Page != SystemPage.Rename)
            return Back();
        Rename!.Start();
        CloseRename();
        return true;
    }

    /// <summary>B: back a step, or false on the list, so the caller closes the menu.</summary>
    public bool Back()
    {
        switch (Page)
        {
            case SystemPage.List:
                return false;
            case SystemPage.Options:
                if (!Options!.Back())
                    ToList();
                return true;
            case SystemPage.Saves:
                Saves!.Back();
                if (Saves.Closed)
                    ToList();
                return true;
            default:
                Rename!.Back();
                CloseRename();
                return true;
        }
    }

    /// <summary>Puts the cursor on a row, as hovering does.</summary>
    public void Point(int row)
    {
        if (Page == SystemPage.List)
            Items.Point(row);
        else if (Page == SystemPage.Options)
            Options!.Point(row);
        else if (Page == SystemPage.Saves)
            Saves!.Point(row);
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

    private void Open(SystemItem item)
    {
        switch (item)
        {
            case SystemItem.LoadGame or SystemItem.SaveGame:
                Saves = SaveMenu.From(_plugins, item == SystemItem.SaveGame, _savesFolder, _listSaves);
                Page = SystemPage.Saves;
                break;
            case SystemItem.Options:
                Options = new OptionsMenu(Settings, inGame: true);
                Page = SystemPage.Options;
                break;
            case SystemItem.Help:
                HelpChosen = true;
                break;
        }
    }

    private void CloseRename()
    {
        if (Rename is not { Closed: true } rename)
            return;
        if (rename.Chosen is { } name && Alaron is { } alaron)
            alaron.Name = name;
        Rename = null;
        Page = SystemPage.Options;
    }

    private void ToList()
    {
        Page = SystemPage.List;
        Saves = null;
        Pulse.Reset();
    }
}
