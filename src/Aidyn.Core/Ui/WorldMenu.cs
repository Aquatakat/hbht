using Aidyn.Core.Calendar;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>The World tab's two choices, its only cursor.</summary>
public enum WorldChoice
{
    Camp,
    Journal,
}

/// <summary>
/// The pause menu's World tab (<c>WidgetCalendar</c>, <c>calendar_func</c> at <c>0x8004069c</c> in 1.0): the world map
/// with its pieces and the party's ring, the season's name and today on the calendar painted into the tab's image,
/// and Camp and Journal. Positions are in the 320 × 240 frame.
/// </summary>
public sealed class WorldMenu
{
    /// <summary>The season's name: common lines from here, two a season (<c>0x8004078c</c>).</summary>
    public const int SeasonNames = 147;
    /// <summary>The note in an empty journal.</summary>
    public const int NoEntries = 496;
    /// <summary>Days in each half of a season, the calendar's 28.</summary>
    public const int HalfSeason = 28;
    /// <summary>How far the map's pieces fade in a tick, and the most they reach before going to 255 (<c>0x80049428</c>).</summary>
    public const int PieceFade = 5, PieceFadeTop = 250;
    /// <summary>The ring's pulse, in ticks each way.</summary>
    public const int MarkerSteps = 10;

    public static readonly Rgba TextColor = PartyMenu.TextColor;
    /// <summary>The ring's colour at the top of its pulse, from black.</summary>
    public static readonly Rgba MarkerColor = new(165, 158, 255, 255);

    /// <summary>Today's square is 1 or 2 left of its column, by row × 7 + column (a table at <c>0x800d0b50</c>).</summary>
    private static readonly HashSet<int> OneLeft = [4, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 21, 23, 26, 28, 29];

    private readonly GameState _game;
    private readonly MenuText _text;
    private readonly TextLayout _font;
    private readonly float _campFactor;
    private readonly string _cannotCamp;
    private readonly MenuPulse _marker = new(frames: MarkerSteps, top: MarkerSteps);

    /// <param name="font">The main font, which the journal wraps its entries in.</param>
    /// <param name="place">The chunk the player is in, or none, as in a cut-scene's stage.</param>
    /// <param name="outdoors">Whether the sky is the outdoor one, which the party's ring needs.</param>
    public WorldMenu(PluginSet plugins, GameState game, TextLayout font, ChunkRef? place, bool outdoors)
    {
        _game = game;
        _text = MenuText.From(plugins);
        _font = font;
        Map = plugins.Singleton<WorldMapRecord>();
        var camping = plugins.Singleton<CampingRecord>();
        _cannotCamp = camping.CannotCamp;
        _campFactor = place is { } at && TerrainOf(plugins, at) is { } terrain ? terrain.Camp : 0;

        var date = game.Clock.Date;
        var dayOfSeason = date.Week * 7 + date.DayOfWeek;
        SeasonName = _text[SeasonNames + date.Season * 2 + (dayOfSeason >= HalfSeason ? 1 : 0)];
        // The grid starts on its fourth column.
        var cell = dayOfSeason % HalfSeason + 3;
        var (row, column) = (cell / 7, cell % 7);
        Today = (160 + 14 * column - (OneLeft.Contains(row * 7 + column) ? 1 : 2), 65 + 14 * row);

        Pieces = [.. Map.Pieces.Where(p => game.Flags.IsSet(p.Flag))];
        if (outdoors && place is { } chunk && chunk.MapSet == Map.MapSet)
        {
            Marker = ((int)Map.Origin.X + (int)((chunk.Row - 1) * Map.Step.X),
                (int)Map.Origin.Y + (int)((chunk.Column - Map.FirstColumn) * Map.Step.Y));
        }
    }

    public WorldMapRecord Map { get; }

    /// <summary>The season's name, as THE EARTH SEASON or LATE EARTH SEASON.</summary>
    public string SeasonName { get; }

    /// <summary>Today's square's top-left (<c>0x00DC</c>, drawn 17 square).</summary>
    public (int X, int Y) Today { get; }

    /// <summary>The map's pieces whose flags are set, in order.</summary>
    public IReadOnlyList<MapPiece> Pieces { get; }

    /// <summary>The pieces' alpha, which fades in once the tab is slid to, and out once it's been left.</summary>
    public int PieceAlpha { get; private set; }

    /// <summary>The party's ring's top-left, or none away from the Overworld or indoors.</summary>
    public (int X, int Y)? Marker { get; }

    /// <summary>The ring's colour now, pulsing from black.</summary>
    public Rgba MarkerNow => _marker.Now(new Rgba(0, 0, 0, 255), MarkerColor);

    public WorldChoice Cursor { get; private set; }

    /// <summary>Camp's and Journal's pulse, which turns at 20 rather than 19, 40 frames a cycle (<c>WorldMenu.TabStep</c>, <c>0x80040dc4</c>).</summary>
    public MenuPulse Pulse { get; } = new(top: MenuPulse.Frames);

    /// <summary>The popups over the tab: what's said, such as that the party can't camp here.</summary>
    public MenuOverlay Overlay { get; } = new();

    /// <summary>What's said, such as that the party can't camp here; A, B or Start closes it.</summary>
    public MenuMessage? Message => Overlay.Message;

    public JournalMenu? Journal { get; private set; }

    /// <summary>Up or down: Camp or Journal. Left and right do nothing.</summary>
    public void MoveCursor(int by)
    {
        if (Message is not null || Journal is not null)
            return;
        var to = by < 0 ? WorldChoice.Camp : WorldChoice.Journal;
        if (to != Cursor)
            Pulse.Reset();
        Cursor = to;
    }

    /// <summary>
    /// A (<c>campMenuController</c>): Camp camps where the terrain allows it, and otherwise says it can't; Journal opens
    /// the journal, showing the newest entry once. Returns <see cref="PauseExit.Camp"/> where the party can camp, as the menu
    /// closes and it camps, and otherwise null.
    /// </summary>
    public PauseExit? Accept()
    {
        if (Overlay.CloseMessage())
            return null;
        if (Journal is not null)
        {
            Journal.Close();
            return null;
        }
        if (Cursor == WorldChoice.Camp)
        {
            if (_campFactor > 0)
                return PauseExit.Camp;
            Overlay.Say(_cannotCamp);
            return null;
        }
        Journal = OpenJournal();
        return null;
    }

    /// <summary>B or Start: closes the message or the journal, and otherwise says the menu should close.</summary>
    public bool Back()
    {
        if (Overlay.CloseMessage())
            return true;
        if (Journal is not null)
        {
            Journal.Close();
            return true;
        }
        return false;
    }

    private JournalMenu OpenJournal()
    {
        var flags = _game.Flags;
        var newest = flags.NewestJournalEntry;
        flags.NewestJournalEntry = null;
        var entries = _game.Plugins.All<JournalRecord>(RecordTypes.Journal).ToDictionary(j => j.Id);
        var shown = flags.JournalOrder.Where(entries.ContainsKey).Select(id => entries[id]).Where(e => flags.IsSet(e.Flag));
        return new JournalMenu(_font, [.. shown.Select(e => (e.Text, e.Id == newest))], _text[NoEntries]);
    }

    /// <summary>
    /// A 60 Hz tick. The pieces and the ring move on only while the tabs aren't sliding: the pieces fade in on the tab,
    /// as the original's do each time it's slid to (<c>bigAssMenu_RFunc</c> starts them from 0), and out at the same
    /// rate once it's been left, so they don't vanish as it's slid to again. The port shows the tabs either side.
    /// </summary>
    /// <param name="showing">Whether the tab is the one showing, or being slid to.</param>
    public void Step(bool showing, bool sliding, bool upHeld = false, bool downHeld = false)
    {
        Pulse.Step();
        Overlay.Step();
        if (Journal is { } journal)
        {
            journal.Step(upHeld, downHeld);
            if (journal.Closed)
                Journal = null;
        }
        if (sliding)
            return;
        if (!showing)
        {
            PieceAlpha = Math.Max(PieceAlpha - PieceFade, 0);
            return;
        }
        PieceAlpha = PieceAlpha >= PieceFadeTop ? 255 : PieceAlpha + PieceFade;
        _marker.Step();
    }

    private static TerrainRecord? TerrainOf(PluginSet plugins, ChunkRef at) =>
        plugins.TryGet<MapSetRecord>(at.MapSet)?.Chunk(at.Row, at.Column) is { } chunk
            ? TerrainRecord.Find(plugins, chunk.Terrain)
            : null;
}
