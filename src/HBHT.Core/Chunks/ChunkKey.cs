using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Chunks;

/// <summary>
/// A loaded chunk's identity: its map set, row and column. The chunk streamer puts a chunk's collision
/// (<see cref="Physics.CollisionWorld"/>) and drawn surfaces (<see cref="Physics.VisibleWorld"/>) in under it and keys
/// its map objects (<see cref="MapObjects.LoadedMap.Key"/>) by it, and a trigger triangle names its map by it. Two map
/// sets' I08 are different keys, so a stage's or an arena's chunks can't be taken for trek's.
/// </summary>
/// <param name="MapSet">The map set's record, <c>aidyn:mapset/&lt;n&gt;</c>.</param>
/// <param name="Row">As in <see cref="ChunkEntry.Row"/>.</param>
/// <param name="Column">As in <see cref="ChunkEntry.Column"/>.</param>
public readonly record struct ChunkKey(RecordId MapSet, int Row, int Column)
{
    /// <summary>A chunk of a map set.</summary>
    public static ChunkKey Of(MapSetRecord set, ChunkEntry chunk) => new(set.Id, chunk.Row, chunk.Column);

    public override string ToString() => $"{MapSet} {Row},{Column}";
}
