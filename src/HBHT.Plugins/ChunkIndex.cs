using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HBHT.Plugins.Records;

namespace HBHT.Plugins;

/// <summary>A chunk's cell: its map set, row and column.</summary>
public readonly record struct ChunkCell(RecordId MapSet, int Row, int Column)
{
    /// <summary>As the editor and the quest report name it, such as <c>aidyn:mapset/0 I08</c>.</summary>
    public override string ToString() => $"{MapSet} {ChunkRecord.NameOf(Row, Column)}";
}

/// <summary>A chunk with its map set, as the editor and the quest report name it: <c>Overworld I08</c>.</summary>
public sealed record ChunkPlace(MapSetRecord Set, ChunkRecord Chunk)
{
    public override string ToString() => $"{Set.Label} {Chunk.Name}";

    /// <summary>The chunk's place, or null if no plugin has the chunk or its map set.</summary>
    public static ChunkPlace? Of(PluginSet plugins, RecordId chunk) =>
        plugins.TryGet<ChunkRecord>(chunk) is { } record && plugins.TryGet<MapSetRecord>(record.MapSet) is { } set ? new(set, record) : null;
}

/// <summary>
/// Which chunk is in each cell of each map set (ADR 0035): a map set doesn't list its chunks, each chunk says where it is. The base
/// plugin's chunks are found from their IDs alone, so finding a cell's chunk reads no file (ADR 0036); a mod's are read for their
/// <c>mapSet</c>, <c>row</c> and <c>column</c>. Where two chunks claim a cell, the earlier plugin's holds it and the later one's claim
/// is a validation problem (<see cref="Clashes"/>).
/// </summary>
public sealed class ChunkIndex
{
    private static readonly ConditionalWeakTable<PluginSet, ChunkIndex> Cache = [];

    private readonly Dictionary<ChunkCell, RecordId> _cells = [];
    private readonly Dictionary<RecordId, ChunkCell> _places = [];
    private readonly Dictionary<RecordId, List<RecordId>> _sets = [];
    private readonly List<(RecordId Chunk, ChunkCell Cell)> _clashes = [];

    private ChunkIndex(PluginSet plugins)
    {
        var claims = new List<(RecordId Id, ChunkCell Cell, int Order)>();
        foreach (var id in plugins.IdsOf(RecordTypes.Chunk))
        {
            if (CellOf(plugins, id) is { } cell)
                claims.Add((id, cell, plugins.DefinedBy(id) is { } by ? plugins.LoadIndex(by) : int.MaxValue));
        }
        foreach (var (id, cell, _) in claims.OrderBy(c => c.Order).ThenBy(c => c.Id.ToString(), StringComparer.Ordinal))
        {
            if (!_cells.TryAdd(cell, id))
            {
                _clashes.Add((id, cell));
                continue;
            }
            _places[id] = cell;
            if (!_sets.TryGetValue(cell.MapSet, out var list))
                _sets[cell.MapSet] = list = [];
            list.Add(id);
        }
        foreach (var list in _sets.Values)
            list.Sort((a, b) => (_places[a].Row, _places[a].Column).CompareTo((_places[b].Row, _places[b].Column)));
    }

    /// <summary>The index of a plugin set's chunks, made once. A set doesn't change after it loads.</summary>
    public static ChunkIndex For(PluginSet plugins) => Cache.GetValue(plugins, p => new ChunkIndex(p));

    /// <summary>The chunk in a cell, or null where the map set has none.</summary>
    public RecordId? At(RecordId mapSet, int row, int column) => _cells.TryGetValue(new(mapSet, row, column), out var id) ? id : null;

    /// <summary>The chunk in a cell, or null.</summary>
    public RecordId? At(ChunkCell cell) => _cells.TryGetValue(cell, out var id) ? id : null;

    /// <summary>A chunk's cell, or null for a chunk no plugin has or whose claim lost.</summary>
    public ChunkCell? CellOf(RecordId chunk) => _places.TryGetValue(chunk, out var cell) ? cell : null;

    /// <summary>The chunks of a map set, by row then column.</summary>
    public IReadOnlyList<RecordId> In(RecordId mapSet) => _sets.GetValueOrDefault(mapSet) ?? [];

    /// <summary>Every chunk that holds a cell.</summary>
    public IEnumerable<RecordId> All => _places.Keys;

    /// <summary>The chunks whose claim on a cell lost to an earlier plugin's, with the chunk that holds it.</summary>
    public IEnumerable<(RecordId Chunk, RecordId Holder)> Clashes => _clashes.Select(c => (c.Chunk, _cells[c.Cell]));

    /// <summary>
    /// Where a chunk says it is: a base chunk's ID names its cell (ADR 0035, which validation holds its fields to), a mod's chunk
    /// says it in its fields.
    /// </summary>
    private static ChunkCell? CellOf(PluginSet plugins, RecordId id)
    {
        if (ChunkRecord.PlaceOf(id) is { } place)
            return new ChunkCell(place.MapSet, place.Row, place.Column);
        if (plugins.GetJson(id) is not { } json)
            return null;
        return json["mapSet"] is JsonValue set && set.TryGetValue(out string? text) && RecordId.TryParse(text, out var mapSet)
            && json["row"] is JsonValue row && row.TryGetValue(out int r) && json["column"] is JsonValue column && column.TryGetValue(out int c)
            ? new ChunkCell(mapSet, r, c)
            : null;
    }
}

/// <summary>Finding chunks by their cell (<see cref="ChunkIndex"/>).</summary>
public static class ChunkLookup
{
    /// <summary>The chunk in a cell, read, or null where the map set has none.</summary>
    public static ChunkRecord? ChunkAt(this PluginSet plugins, RecordId mapSet, int row, int column) =>
        ChunkIndex.For(plugins).At(mapSet, row, column) is { } id ? plugins.TryGet<ChunkRecord>(id) : null;

    /// <summary>The chunks of a map set, read, by row then column.</summary>
    public static IEnumerable<ChunkRecord> ChunksIn(this PluginSet plugins, RecordId mapSet) =>
        ChunkIndex.For(plugins).In(mapSet).Select(plugins.Get<ChunkRecord>);
}
