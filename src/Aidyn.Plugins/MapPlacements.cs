using System.Runtime.CompilerServices;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>A chunk of a map set, where a map is placed.</summary>
public sealed record MapPlace(MapSetRecord Set, ChunkEntry Chunk)
{
    /// <summary>As the editor and the quest report name it, such as <c>Overworld I08</c>.</summary>
    public override string ToString() => $"{Set.Label} {Chunk.Name}";
}

/// <summary>
/// Where each map sits: the chunks of the map sets that name it. Every base map is placed, most by one chunk
/// and a few (the battle arenas, and one overworld map) by several.
/// </summary>
public sealed class MapPlacements
{
    private static readonly ConditionalWeakTable<PluginSet, MapPlacements> Cache = [];

    private readonly Dictionary<RecordId, List<MapPlace>> _places = [];

    private MapPlacements(PluginSet plugins)
    {
        foreach (var set in plugins.All<MapSetRecord>(RecordTypes.MapSet))
        {
            foreach (var chunk in set.Chunks)
            {
                if (!_places.TryGetValue(chunk.Map, out var list))
                    _places[chunk.Map] = list = [];
                list.Add(new MapPlace(set, chunk));
            }
        }
    }

    /// <summary>The placements of a plugin set's maps, read once. A set doesn't change after it loads.</summary>
    public static MapPlacements For(PluginSet plugins) => Cache.GetValue(plugins, p => new MapPlacements(p));

    /// <summary>Every chunk that places <paramref name="map"/>: by map set ID, then in the set's chunk order. None for a map no set places.</summary>
    public IReadOnlyList<MapPlace> All(RecordId map) => _places.GetValueOrDefault(map) ?? [];

    /// <summary>The first chunk that places <paramref name="map"/>, or null if none does.</summary>
    public MapPlace? First(RecordId map) => _places.GetValueOrDefault(map)?[0];
}
