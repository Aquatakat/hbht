namespace Aidyn.Plugins.Records;

/// <summary>
/// How the weather changes (<c>aidyn:weather/0</c>, the only one): literals in the original's roll and its wind
/// (<c>Weather.Roll</c>, <c>0x8007bdb0</c> in 1.0, and <c>Weather.DriftWind</c>, <c>0x8007baa0</c>). What each terrain
/// adds to the chance of precipitation, how hard it falls there and whether it's snow are on the terrain records
/// (<see cref="TerrainRecord.Precipitation"/>).
/// </summary>
/// <remarks>
/// The weather may change whenever the part of the day changes, and whenever the player enters a chunk of another
/// terrain. Each time, a d100 over <paramref name="KeepAtMost"/> clears the weather and tries for precipitation: a
/// second d100 at most the season's chance plus the terrain's brings it. A third d100 under
/// <paramref name="FallBelow"/> makes it rain (or snow), and otherwise it's fog. Every d100 is 1 to 100, so with the
/// base game's 75 and 70, a quarter of the chances change the weather, and precipitation is rain or snow 69% of the
/// time and fog 31%.
/// </remarks>
/// <param name="Seasons">Each season's chance of precipitation in percent, by season.</param>
/// <param name="KeepAtMost">A d100 at most this keeps the weather as it is.</param>
/// <param name="FallBelow">Precipitation falls (as rain or snow) on a d100 under this, and is fog otherwise.</param>
/// <param name="IntensitySpread">Rain or snow falls at the terrain's scale give or take up to this, at least 0.</param>
/// <param name="FallFog">The fog that comes with rain or snow: a share from the first to the second.</param>
/// <param name="Fog">The fog of a foggy spell of weather: a share from the first to the second.</param>
/// <param name="WindChance">
/// The chance, each frame of the clock, that the wind turns, and separately that its speed changes.
/// </param>
/// <param name="WindTurn">How far the wind's direction can move in X and Z when it turns, before it's normalised.</param>
/// <param name="WindRise">How far it can move in Y: more often down than up.</param>
/// <param name="WindGust">How far its speed can change, a tick's distance either way.</param>
/// <param name="WindMaxSpeed">Its fastest, a tick's distance.</param>
/// <param name="FallRate">How fast rain or snow sets in and dies away, a share of full a tick.</param>
/// <param name="FogRate">How fast the fog thickens and thins toward the weather's, a share a tick.</param>
/// <param name="LightningEvery">Lightning can strike once in this many frames of the sky.</param>
/// <param name="LightningShare">Then it strikes on a share from 0 to 1 under this times the storm.</param>
/// <param name="RainSky">
/// The sky gradient rain and snow bring in place of the terrain's (sky 2, a literal in <c>ProcessWeather</c>,
/// <c>0x8001e7c0</c> in 1.0), or none to keep the terrain's.
/// </param>
public sealed record WeatherRecord(
    RecordId Id,
    IReadOnlyList<int> Seasons,
    int KeepAtMost,
    int FallBelow,
    float IntensitySpread,
    WeatherRange FallFog,
    WeatherRange Fog,
    float WindChance,
    float WindTurn,
    WeatherRange WindRise,
    float WindGust,
    float WindMaxSpeed,
    float FallRate,
    float FogRate,
    int LightningEvery,
    float LightningShare,
    RecordId? RainSky = null);

/// <summary>A value the weather draws evenly from <paramref name="Min"/> to <paramref name="Max"/>.</summary>
public sealed record WeatherRange(float Min, float Max);
