using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>A Theatre row: a cinematic, and whether it has been seen, so its title shows and A plays it.</summary>
public sealed record TheatreRow(CinematicRecord Cinematic, bool Seen)
{
    /// <summary>What an unseen cinematic's row shows (<c>Theatre.Dashes</c>, <c>0x800d0ee0</c>).</summary>
    public const string Dashes = "----------------";

    /// <summary>The title, or the dashes.</summary>
    public string Label => Seen ? Cinematic.Title! : Dashes;
}

/// <summary>
/// The System tab's Theater page (<c>theatermenu.cpp</c>; <c>Theatre.Build</c>, <c>0x80044444</c> in 1.0): a row for
/// each cinematic the Theatre lists, its title once its done flag is set and 16 dashes until then. A on a title plays
/// it again (<c>Theatre.Play</c>, <c>0x80044aa4</c>); on the dashes it does nothing. B or Start goes back to the list.
/// </summary>
/// <remarks>
/// The rows are the DB's titles in their order, each listed by its flag (<c>Theatre.AddRows</c>, <c>0x800447e8</c>), which
/// the importer keeps on the cinematics' records, so the rows are the cinematics with a title in their order: the
/// opening, the Shadow's, Roog's and the ending. The cursor starts on the first, and wraps.
/// </remarks>
public sealed class TheatreMenu
{
    /// <summary>The rows' colour, the dashes' too, and the cursor's pulse, over 15 steps (<c>Theatre.Build</c>).</summary>
    public static readonly Rgba RowColor = new(103, 70, 60, 255), PulseFrom = new(103, 64, 64, 255), PulseTo = new(197, 174, 176, 255);

    /// <param name="isSet">Whether a flag is set, for the cinematics' done flags.</param>
    public TheatreMenu(PluginSet plugins, Func<RecordId, bool> isSet)
    {
        Items = new MenuList<TheatreRow>([.. plugins.All<CinematicRecord>(RecordTypes.Cinematic)
            .Where(c => c.Title is not null)
            .OrderBy(c => int.Parse(c.Id.Key, System.Globalization.CultureInfo.InvariantCulture))
            .Select(c => new TheatreRow(c, isSet(c.DoneFlag)))]);
    }

    public MenuList<TheatreRow> Items { get; }

    public MenuPulse Pulse { get; } = new(frames: 15);

    /// <summary>The cinematic A chose to play, which closes the menu.</summary>
    public CinematicRecord? Chosen { get; private set; }

    public void Step() => Pulse.Step();

    public void MoveCursor(int by) => Items.Move(by);

    public void Point(int row) => Items.Point(row);

    public void Accept()
    {
        if (Items.Current is { Seen: true } row)
            Chosen = row.Cinematic;
    }
}
