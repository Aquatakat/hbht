using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.TestSupport;

/// <summary>Chunk records built by hand for tests that need a chunk's objects and little else (ADR 0035).</summary>
public static class TestChunk
{
    /// <summary>
    /// A chunk with these objects. Its cell comes from its ID where a base chunk's names one (<c>aidyn:chunk/8/C05</c>), else it is
    /// row 1, column 1 of map set 0; its terrain is <c>aidyn:terrain/0</c> unless given.
    /// </summary>
    public static ChunkRecord Of(RecordId id, IReadOnlyList<MapObject> objects, ObjectGrid? grid = null, RecordId? terrain = null)
    {
        var (set, row, column) = ChunkRecord.PlaceOf(id) ?? (RecordId.Decimal(RecordTypes.MapSet, 0), 1, 1);
        return new ChunkRecord(id, set, row, column, terrain ?? RecordId.Decimal(RecordTypes.Terrain, 0), objects, Grid: grid);
    }
}
