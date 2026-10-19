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
/// <param name="Arenas">
/// Which of the Battle arenas (<c>aidyn:mapset/16</c>, by chunk) a battle here takes: a roll over a range, or with two,
/// one of them half the time, the first if the roll says so (<c>BattleSetup.ChooseArena</c>). Empty, or none in plugins
/// imported before it, keeps the last battle's arena.
/// </param>
/// <param name="Ambushes">
/// Who ambushes the party as it camps here, by the party's level: five tiers, for an average level of up to
/// <see cref="CampingRecord.AmbushLevels"/>' first, then each after, and the last for any above (the camp encounters,
/// <c>CampEncounters.Load</c>). Null with none, or in plugins imported before them.
/// </param>
public sealed record TerrainRecord(RecordId Id, RecordId Sky, bool SkyDrawn, float Camp = 0, int Precipitation = 0,
    float PrecipitationScale = 0, bool Snow = false, IReadOnlyList<ArenaRange>? Arenas = null, IReadOnlyList<CampAmbush>? Ambushes = null)
{
    /// <summary>The terrain a chunk carries (<see cref="ChunkEntry.Terrain"/>), if a plugin has its record.</summary>
    public static TerrainRecord? Find(PluginSet plugins, int terrain) =>
        plugins.TryGet<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, terrain));
}

/// <summary>A camp ambush's enemies at one level tier: each kind with how many always come and how many can.</summary>
public sealed record CampAmbush(IReadOnlyList<PartyEnemy> Enemies);

/// <summary>The Battle arenas from <paramref name="Min"/> to <paramref name="Max"/>, inclusive, one of which a battle rolls.</summary>
public sealed record ArenaRange(int Min, int Max);
