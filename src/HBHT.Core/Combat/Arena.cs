using System.Numerics;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

/// <summary>
/// One of the Battle arenas (<c>aidyn:mapset/16</c>), where a battle plays: the chunk whose place in the map set's list is
/// the arena's number (<c>Zones.ArenaChunks</c>, <c>0x800DCA74</c>), and its map, whose reference points name the spawns
/// (<c>good0a</c>–<c>bad4d</c>) and the flee points (<c>flee0</c>–<c>flee7</c>). Chunks sharing a map are the same arena.
/// </summary>
public sealed record Arena(int Number, MapSetRecord Set, ChunkEntry Chunk, MapRecord Map)
{
    /// <summary>The Battle arenas' map set.</summary>
    public static readonly RecordId MapSet = RecordId.Decimal(RecordTypes.MapSet, 16);

    /// <summary>The arenas: 0–27.</summary>
    public const int Count = 28;

    /// <summary>Where the chunk's coordinates start in the map set.</summary>
    public Vector3 Origin => Set.Origin(Chunk.Row, Chunk.Column);

    /// <summary>Where the chunk is in its map set, for what asks where the battle is.</summary>
    public ChunkRef Place => new(Set.Id, Chunk.Row, Chunk.Column);

    /// <summary>A reference point by its name, or null (<c>FindReferencePointName</c>, <c>0x8000B7C8</c>).</summary>
    public ReferencePointObject? Point(string name) =>
        Map.Objects.OfType<ReferencePointObject>().FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// The arena <paramref name="number"/>, or null if no plugin has it. The chunk is the map set's
    /// <paramref name="number"/>th, as the original's list of arena chunks is the map set's own order.
    /// </summary>
    public static Arena? For(PluginSet plugins, int number)
    {
        if (plugins.TryGet<MapSetRecord>(MapSet) is not { } set || number < 0 || number >= set.Chunks.Count
            || plugins.TryGet<MapRecord>(set.Chunks[number].Map) is not { } map)
            return null;
        return new Arena(number, set, set.Chunks[number], map);
    }
}
