namespace Aidyn.Core.Calendar;

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

    /// <summary>Advances by <paramref name="ticks"/> of play (<c>World::IncGameTime</c>).</summary>
    public void Step(int ticks = 1) => Time = (uint)((Time + (ulong)ticks * DaySpeed) % Year);

    /// <summary>Sets the time of day, keeping the date.</summary>
    public void SetTimeOfDay(float hours) => Time = Time - Time % Day + (uint)(hours % 24 * Hour);
}
