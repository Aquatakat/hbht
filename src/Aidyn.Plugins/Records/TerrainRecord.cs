namespace Aidyn.Plugins.Records;

/// <summary>
/// A terrain type (<c>aidyn:terrain/n</c>), which each chunk carries (<see cref="ChunkEntry.Terrain"/>).
/// The chunk the player is in sets the sky, and later the weather and camp encounters.
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
public sealed record TerrainRecord(RecordId Id, RecordId Sky, bool SkyDrawn);
