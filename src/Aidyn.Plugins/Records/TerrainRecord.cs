namespace Aidyn.Plugins.Records;

/// <summary>
/// A terrain type (<c>aidyn:terrain/n</c>), which each chunk carries (<see cref="ChunkEntry.Terrain"/>).
/// The chunk the player is in sets the sky, whether the party can camp, the weather, and later camp encounters.
/// </summary>
/// <param name="Sky">
/// The sky gradient, an image 12 × 37: rows 0–31 are the sky from top to bottom and rows 32–36
/// the scenery's tint and the sky, fog, cloud and sun colours, with a column every two hours from
/// midnight.
/// </param>
/// <param name="SkyDrawn">
/// Whether the gradient is drawn behind the scenery. Where it isn't, as underground, the background
/// is black and there's no weather, but the colours still come from the gradient.
/// </param>
/// <param name="Camp">
/// The camp ambush's factor (<c>camp_float_array</c>), its chance in percent with no Ranger: see
/// <see cref="CampingRecord"/>. At 0 the party can't camp here.
/// </param>
/// <param name="Precipitation">
/// What the terrain adds to the season's chance of precipitation, in percent, when the weather changes
/// (<c>0x800e3048</c> in 1.0): see <see cref="WeatherRecord"/>. −100 keeps it dry whatever the season.
/// </param>
/// <param name="PrecipitationScale">How hard rain or snow falls here: its intensity is this give or take the weather record's spread.</param>
/// <param name="Snow">Precipitation here is snow rather than rain.</param>
public sealed record TerrainRecord(RecordId Id, RecordId Sky, bool SkyDrawn, float Camp = 0, int Precipitation = 0,
    float PrecipitationScale = 0, bool Snow = false);
