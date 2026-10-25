namespace HBHT.Rom.Engine;

/// <summary>
/// The weather's tables in the code segment, which its roll reads (<c>Weather.Roll</c>, <c>0x8007bdb0</c> in 1.0):
/// each season's chance of precipitation, what each terrain type adds to it, and how hard it falls on each.
/// Identical in all three ROMs.
/// </summary>
/// <param name="Seasons">The chance in percent for each of the six seasons (u8s; the table's last two bytes are padding).</param>
/// <param name="Terrains">What each terrain type adds, in percent (s8s): −100 keeps it dry.</param>
/// <param name="Scales">How hard rain or snow falls on each terrain type.</param>
public sealed record WeatherTables(IReadOnlyList<int> Seasons, IReadOnlyList<int> Terrains, IReadOnlyList<float> Scales)
{
    /// <summary>The seasons in a year (<c>World::GetCalendarDate</c>).</summary>
    public const int SeasonCount = 6;

    public static WeatherTables Read(RomImage rom)
    {
        var seasons = rom.Table(CodeTable.SeasonPrecipitation).ToArray();
        var terrains = rom.Table(CodeTable.TerrainPrecipitation).ToArray();
        return new WeatherTables(
            [.. seasons.Take(SeasonCount).Select(b => (int)b)],
            [.. terrains.Select(b => (int)(sbyte)b)],
            Be.F32s(rom.Table(CodeTable.TerrainPrecipitationScale)));
    }
}
