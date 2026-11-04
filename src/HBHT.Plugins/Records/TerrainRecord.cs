namespace HBHT.Plugins.Records;

/// <summary>
/// A terrain type (<c>aidyn:terrain/n</c>), which each chunk carries (<see cref="ChunkRecord.Terrain"/>).
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
/// <param name="Arenas">
/// Which of the Battle arenas (chunks of <c>aidyn:mapset/16</c>) a battle here takes: one of a group at random, or with two groups,
/// one of them half the time, the first if a d2 says 1, then one of its arenas (<c>BattleSetup.ChooseArena</c>, which rolls over a
/// range of the original's arena numbers that the importer turned into each group's chunks). Empty, as it is if not given, keeps the
/// last battle's arena.
/// </param>
/// <param name="Ambushes">
/// Who ambushes the party as it camps here, by the party's level: five tiers, for an average level of up to
/// <see cref="CampingRecord.AmbushLevels"/>' first, then each after, and the last for any above (the camp encounters,
/// <c>CampEncounters.Load</c>). Empty if not given: no ambush comes.
/// </param>
public sealed record TerrainRecord(RecordId Id, RecordId Sky, bool SkyDrawn, float Camp = 0, int Precipitation = 0,
    float PrecipitationScale = 0, bool Snow = false, IReadOnlyList<IReadOnlyList<RecordId>>? Arenas = null,
    IReadOnlyList<CampAmbush>? Ambushes = null)
{
    public IReadOnlyList<IReadOnlyList<RecordId>> Arenas { get; init; } = Arenas ?? [];

    public IReadOnlyList<CampAmbush> Ambushes { get; init; } = Ambushes ?? [];
}

/// <summary>A camp ambush's enemies at one level tier: each kind with how many always come and how many can.</summary>
public sealed record CampAmbush(IReadOnlyList<PartyEnemy> Enemies);

