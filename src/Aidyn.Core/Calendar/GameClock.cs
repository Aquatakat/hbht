namespace Aidyn.Core.Calendar;

/// <summary>The part of the day that content sees (<c>TerrainStruct.partOfDay</c>).</summary>
public enum PartOfDay
{
    /// <summary>From 6:00.</summary>
    Morning,
    /// <summary>From 9:00.</summary>
    Midday,
    /// <summary>From 12:00.</summary>
    Afternoon,
    /// <summary>From 18:00.</summary>
    Evening,
    /// <summary>From 21:00 to 6:00.</summary>
    Night,
}

/// <summary>A time as the calendar gives it (<c>World::GetCalendarDate</c>).</summary>
/// <param name="Season">0–5.</param>
/// <param name="Week">The week of the season, 0–7.</param>
/// <param name="DayOfWeek">0–6.</param>
public readonly record struct CalendarDate(int Season, int Week, int DayOfWeek, int Hour)
{
    /// <summary>
    /// The part of the day by the hour (<c>World::UpdateTimeOfDay</c>, <c>0x8007bd3c</c> in 1.0, which tests the hour
    /// against 6, 9, 12, 18 and 21 in both builds).
    /// </summary>
    /// <remarks>Spells that turn day to night freeze it in the original. Those aren't ported yet.</remarks>
    public PartOfDay PartOfDay => Hour switch
    {
        < 6 => PartOfDay.Night,
        < 9 => PartOfDay.Morning,
        < 12 => PartOfDay.Midday,
        < 18 => PartOfDay.Afternoon,
        < 21 => PartOfDay.Evening,
        _ => PartOfDay.Night,
    };
}

/// <summary>
/// The in-game time (<c>TerrainStruct.InGameTime</c>), in 60ths of an in-game second. In trek it
/// runs 72 times as fast as real time (<c>daySpeed</c>, <c>TickGameTime</c>), so a day lasts 20
/// minutes. It wraps at a year of six 56-day seasons.
/// </summary>
public sealed class GameClock
{
    public const int TicksPerSecond = 60;
    public const uint Hour = 60 * 60 * TicksPerSecond;
    public const uint Day = 24 * Hour;
    public const uint Week = 7 * Day;
    public const uint Season = 56 * Day;
    public const uint Year = 6 * Season;
    /// <summary>In-game ticks a tick of play (<c>World::init</c>).</summary>
    public const int DaySpeed = 72;

    public uint Time { get; set; }

    /// <summary>A new game's: noon on day 2 of week 6 of the first season (<c>InitGameGlobals</c>'s calendar).</summary>
    public static GameClock NewGame() => new() { Time = 6 * Week + 2 * Day + 12 * Hour };

    /// <summary>The fraction of the day gone, from midnight (<c>World::get_timeofDay_float</c>).</summary>
    public float TimeOfDay => (float)(Time % Day) / Day;

    public CalendarDate Date => new((int)(Time / Season), (int)(Time % Season / Week), (int)(Time % Week / Day), (int)(Time % Day / Hour));

    /// <summary>Advances by <paramref name="ticks"/> of play (<c>World::IncGameTime</c>).</summary>
    public void Step(int ticks = 1) => Time = (uint)((Time + (ulong)ticks * DaySpeed) % Year);

    /// <summary>Sets the time of day, keeping the date.</summary>
    public void SetTimeOfDay(float hours) => Time = Time - Time % Day + (uint)(hours % 24 * Hour);

    /// <summary>
    /// What's added to the time of day the sky shows (<c>world+0x28</c>), as a fraction of a day: Darkness and Light move it so the
    /// sky, the sun and the moon show midnight or noon while they last. The rules' part of the day doesn't read it. Not saved: only a
    /// battle moves it, and its end puts it back.
    /// </summary>
    public float ShownOffset { get; private set; }

    private float _offsetGoal, _offsetRate;

    /// <summary>
    /// The time of day the sky, the sun and the moon show (<c>World.TimeOfDay</c>, <c>0x8007C2C4</c>): the clock's and
    /// <see cref="ShownOffset"/>, wrapped into the day.
    /// </summary>
    public float ShownTimeOfDay
    {
        get
        {
            var t = TimeOfDay + ShownOffset;
            return t - MathF.Floor(t);
        }
    }

    /// <summary>
    /// The sky's time of day eased to <paramref name="timeOfDay"/> over <paramref name="ticks"/> (<c>World.SetTimeOfDayGoal</c>,
    /// <c>0x8007C450</c>): the offset's goal is taken from the clock now, so it then moves on with the clock.
    /// </summary>
    public void ShowTimeOfDay(float timeOfDay, float ticks)
    {
        _offsetGoal = timeOfDay - TimeOfDay;
        _offsetRate = (_offsetGoal - ShownOffset) / ticks;
    }

    /// <summary>The offset eased back to none over <paramref name="ticks"/> (<c>World.ClearTimeOffset</c>, <c>0x8007C5B8</c>).</summary>
    public void ReleaseShownTime(float ticks)
    {
        _offsetGoal = 0;
        _offsetRate = (0 - ShownOffset) / ticks;
    }

    /// <summary>The offset moved toward its goal by its rate (<c>World.StepTimeOffset</c>, <c>0x8007C3B8</c>), stopping there.</summary>
    public void StepShownOffset(int ticks = 1)
    {
        if (_offsetRate == 0)
            return;
        ShownOffset += _offsetRate * ticks;
        if (_offsetRate > 0 ? ShownOffset >= _offsetGoal : ShownOffset <= _offsetGoal)
            (ShownOffset, _offsetRate) = (_offsetGoal, 0);
    }

    /// <summary>The offset gone at once.</summary>
    public void ClearShownOffset() => (ShownOffset, _offsetGoal, _offsetRate) = (0, 0, 0);
}
