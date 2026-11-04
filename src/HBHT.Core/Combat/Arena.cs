using System.Numerics;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

/// <summary>
/// One of the Battle arenas (<c>aidyn:mapset/16</c>), where a battle plays: a chunk, whose reference points name the spawns
/// (<c>good0a</c>–<c>bad4d</c>) and the flee points (<c>flee0</c>–<c>flee7</c>). The original numbers its 28 arenas by the order of the map
/// set's chunks (<c>Zones.ArenaChunks</c>, <c>0x800DCA74</c>), which records name by their chunks instead (ADR 0035).
/// </summary>
public sealed record Arena(MapSetRecord Set, ChunkRecord Chunk)
{
    /// <summary>The Battle arenas' map set.</summary>
    public static readonly RecordId MapSet = RecordId.Decimal(RecordTypes.MapSet, 16);

    /// <summary>Where the chunk's coordinates start in the map set.</summary>
    public Vector3 Origin => Set.Origin(Chunk.Row, Chunk.Column);

    /// <summary>The chunk, for what asks where the battle is.</summary>
    public RecordId Place => Chunk.Id;

    /// <summary>A reference point by its name, or null (<c>FindReferencePointName</c>, <c>0x8000B7C8</c>).</summary>
    public ReferencePointObject? Point(string name) =>
        Chunk.Objects.OfType<ReferencePointObject>().FirstOrDefault(p => p.Name == name);

    /// <summary>The arena in a chunk, or null if no plugin has the chunk or its map set.</summary>
    public static Arena? For(PluginSet plugins, RecordId chunk) =>
        plugins.TryGet<ChunkRecord>(chunk) is { } record && plugins.TryGet<MapSetRecord>(record.MapSet) is { } set ? new Arena(set, record) : null;
}
