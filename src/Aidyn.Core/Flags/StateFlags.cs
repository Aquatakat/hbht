using Aidyn.Core.Calendar;
using Aidyn.Core.Sky;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Flags;

/// <summary>
/// Writes the engine's state into the flags that content reads it from (<see cref="StateFlagsRecord"/>):
/// the calendar (<c>SetFlagArray_on_Time</c>), the rain (<c>set_weather_flags</c>) and the terrain
/// (<c>set_terrain_flags</c>).
/// </summary>
/// <remarks>
/// The original also has setters for the party's best stats and skills, but they never find their
/// table entries, so those flags stay clear. They aren't ported.
/// </remarks>
public sealed class StateFlags(FlagNetwork flags, StateFlagsRecord record)
{
    private readonly int[] _moonPhase = Handles(flags, record.MoonPhase);
    private readonly int[] _timeOfDay = Handles(flags, record.TimeOfDay);
    private readonly int[] _dayOfSeason = Handles(flags, record.DayOfSeason);
    private readonly int[] _season = Handles(flags, record.Season);
    private readonly int[] _dayOfWeek = Handles(flags, record.DayOfWeek);
    private readonly int[] _week = Handles(flags, record.Week);
    private readonly int[] _rain = Handles(flags, record.Rain);
    private readonly int[] _terrain = Handles(flags, record.Terrain);

    public FlagNetwork Flags => flags;

    /// <summary>
    /// The date, the part of the day and the moon's phase (<c>SetFlagArray_on_Time</c>). The original
    /// writes these whenever time passes, which in trek is every frame (<c>World::SeveralTimeFuncs</c>).
    /// </summary>
    public void SetTime(CalendarDate date, PartOfDay partOfDay, int moonPhase)
    {
        flags.SetBits(_timeOfDay, (int)partOfDay);
        flags.SetBits(_dayOfWeek, date.DayOfWeek);
        flags.SetBits(_week, date.Week);
        flags.SetBits(_season, date.Season);
        flags.SetBits(_moonPhase, moonPhase);
        flags.SetBits(_dayOfSeason, date.Week * 7 + date.DayOfWeek);
    }

    /// <summary>The clock's time, with the part of the day and the moon's phase it gives.</summary>
    public void SetTime(GameClock clock)
    {
        var date = clock.Date;
        SetTime(date, date.PartOfDay, SkyObjects.MoonPhase(clock.Time).Phase);
    }

    /// <summary>0 clear, 1 rain, 2 snow (<c>set_weather_flags</c>).</summary>
    public void SetRain(int precipitation) => flags.SetBits(_rain, precipitation);

    /// <summary>The terrain type of the chunk the player is in (<c>set_terrain_flags</c>).</summary>
    public void SetTerrain(int terrain) => flags.SetBits(_terrain, terrain);

    private static int[] Handles(FlagNetwork flags, IReadOnlyList<Plugins.RecordId> ids)
    {
        var handles = ids.Select(flags.Handle).ToArray();
        if (handles.Any(flags.IsDerived))
            throw new InvalidDataException($"The state flags name a derived flag among {string.Join(", ", ids)}.");
        return handles;
    }
}
