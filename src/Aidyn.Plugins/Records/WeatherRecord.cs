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
/// <param name="Rain">How rain is drawn: literals in <c>ProcessWeather</c> (<c>0x8001e7c0</c>). None draws none.</param>
/// <param name="Snow">How snow is drawn.</param>
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
    PrecipitationLook? Rain = null,
    PrecipitationLook? Snow = null);

/// <summary>A value the weather draws evenly from <paramref name="Min"/> to <paramref name="Max"/>.</summary>
public sealed record WeatherRange(float Min, float Max);

/// <summary>
/// How rain or snow is drawn: the particles of two emitters, one of drops that fall and one of the splashes they make
/// as they land, which <c>ProcessWeather</c> (<c>0x8001e7c0</c> in 1.0) makes and steers by how hard it falls, I, from
/// 0 to 1. Each value that changes with I is its value at 0 plus I times its <c>PerIntensity</c>.
/// </summary>
/// <param name="Drop">The drops' texture.</param>
/// <param name="Splash">The splashes' texture.</param>
/// <param name="Color">The drops' colour, from 0 to 1, alpha last, which they fade to a quarter of over their life.</param>
/// <param name="Splashes">Whether splashes also land of themselves, about the drops: rain's do.</param>
/// <param name="Fall">How far down the drops fall a tick, on top of the wind (doubles in the code).</param>
/// <param name="Interval">A drop every this many ticks, truncated to a whole number; at 0, once a frame, and under, more.</param>
/// <param name="Life">How many ticks a drop lives, truncated.</param>
/// <param name="SplashesSooner">With <paramref name="Splashes"/>, how many ticks sooner than a drop a splash lands.</param>
public sealed record PrecipitationLook(
    RecordId Drop,
    RecordId Splash,
    WeatherColor Color,
    bool Splashes,
    double Fall,
    double FallPerIntensity,
    float Interval,
    float IntervalPerIntensity,
    float Life,
    float LifePerIntensity,
    int SplashesSooner);

/// <summary>A colour from 0 to 1 a channel, as the original keeps it in floats.</summary>
public sealed record WeatherColor(float R, float G, float B, float A);
