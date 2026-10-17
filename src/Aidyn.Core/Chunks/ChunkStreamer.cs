using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Chunks;

/// <summary>
/// Keeps the 3×3 chunks around a point loaded, as the original's zone engine does (<c>ZoneDatMtx</c>):
/// each chunk's collision, which goes into the <see cref="CollisionWorld"/> keyed by its row and
/// column, and its map objects as they stand while it's loaded (<see cref="LoadedMap"/>). Trek gives
/// a chunk's monster parties their bodies as it loads (<see cref="Loaded"/>); the runtime builds its
/// scenery.
/// </summary>
/// <remarks>Rows run along X and columns along Z, and a chunk's coordinates start at its corner.</remarks>
public sealed class ChunkStreamer
{
    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly Dictionary<(int Row, int Column), ChunkEntry> _chunks = [];
    private readonly Dictionary<(int Row, int Column), LoadedMap> _maps = [];

    public ChunkStreamer(PluginSet plugins, GameState game, CollisionWorld world, MapSetRecord set)
    {
        _plugins = plugins;
        _game = game;
        World = world;
        MapSet = set;
        Change(set);
    }

    public MapSetRecord MapSet { get; private set; }

    /// <summary>The loaded chunks' collision.</summary>
    public CollisionWorld World { get; }

    /// <summary>A chunk has loaded: its collision is in the world and its objects are activated.</summary>
    public event Action<ChunkEntry, LoadedMap>? Loaded;

    /// <summary>A chunk is going: its collision and objects go after this.</summary>
    public event Action<ChunkEntry, LoadedMap>? Unloading;

    /// <summary>The chunk the 3×3 was last loaded around.</summary>
    public (int Row, int Column) Centre { get; private set; }

    /// <summary>The loaded chunks and their map objects.</summary>
    public IEnumerable<(ChunkEntry Chunk, LoadedMap Map)> All => _maps.Select(m => (_chunks[m.Key], m.Value));

    /// <summary>The chunk a point is in.</summary>
    public (int Row, int Column) CellAt(Vector3 position) =>
        ((int)MathF.Floor(position.X / MapSet.CellSize), (int)MathF.Floor(position.Z / MapSet.CellSize));

    public ChunkEntry? ChunkAt(Vector3 position) => _chunks.GetValueOrDefault(CellAt(position));

    public ChunkEntry? Find(string name) => _chunks.Values.FirstOrDefault(c => c.Name == name);

    public Vector3 Origin(ChunkEntry chunk) => MapSet.Origin(chunk.Row, chunk.Column);

    /// <summary>A loaded chunk's map objects, by the key its collision is under.</summary>
    public LoadedMap? Map(object key) => key is ValueTuple<int, int> cell ? _maps.GetValueOrDefault(cell) : null;

    /// <summary>The map objects of the loaded chunk a point is in.</summary>
    public LoadedMap? MapAt(Vector3 position) => _maps.GetValueOrDefault(CellAt(position));

    /// <summary>The chunk a loaded map is.</summary>
    public ChunkEntry? ChunkOf(LoadedMap map) => map.Key is ValueTuple<int, int> cell ? _chunks.GetValueOrDefault(cell) : null;

    /// <summary>Loads the chunks around <paramref name="position"/> and frees the rest. Returns whether any changed.</summary>
    public bool Update(Vector3 position)
    {
        var (row, column) = Centre = CellAt(position);
        var wanted = new HashSet<(int, int)>();
        for (var r = row - 1; r <= row + 1; r++)
        {
            for (var c = column - 1; c <= column + 1; c++)
            {
                if (_chunks.ContainsKey((r, c)))
                    wanted.Add((r, c));
            }
        }

        var changed = false;
        foreach (var cell in _maps.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            Unload(cell);
            changed = true;
        }
        foreach (var cell in wanted.Where(k => !_maps.ContainsKey(k)).ToList())
        {
            Load(_chunks[cell]);
            changed = true;
        }
        return changed;
    }

    /// <summary>Frees every loaded chunk and streams another map set's, as a teleport into it does.</summary>
    public void Change(MapSetRecord set)
    {
        Clear();
        MapSet = set;
        _chunks.Clear();
        foreach (var chunk in set.Chunks)
            _chunks[(chunk.Row, chunk.Column)] = chunk;
    }

    /// <summary>Frees every loaded chunk.</summary>
    public void Clear()
    {
        foreach (var cell in _maps.Keys.ToList())
            Unload(cell);
    }

    /// <summary>
    /// Gates the loaded chunks' scene objects again by the flags, as the original does when it reloads
    /// the map after the pause menu, a shop, training, a battle or a teleport (<c>loadGameBorgScenes</c>).
    /// Collision and the rest of the objects stay as they are.
    /// </summary>
    /// <returns>The maps whose scene objects came or went.</returns>
    public IReadOnlyList<LoadedMap> Regate() => [.. _maps.Values.Where(m => m.Regate(_game.Flags, _game.Memory))];

    /// <summary>
    /// Puts out what goes out once reached in the loaded chunks, Oriana's path lights (<see cref="LoadedMap.PutOut"/>),
    /// remembering each under the chunk the 3×3 is loaded around, as the original does under the player's.
    /// </summary>
    /// <returns>The objects put out, with their maps.</returns>
    public IReadOnlyList<(LoadedMap Map, SceneObject Object)> PutOut(in Scenes.SceneView view)
    {
        if (!_chunks.TryGetValue(Centre, out var centre))
            return [];
        List<(LoadedMap, SceneObject)>? putOut = null;
        foreach (var map in _maps.Values)
        {
            foreach (var o in map.PutOut(view, _game.Memory, centre.Map))
                (putOut ??= []).Add((map, o));
        }
        return putOut ?? [];
    }

    // The chunk's collision, and its objects activated by the flags as the original does as it loads
    // (VoxelObjects_Activate).
    private void Load(ChunkEntry chunk)
    {
        var cell = (chunk.Row, chunk.Column);
        var origin = Origin(chunk);
        if (CollisionMesh.For(_plugins, chunk.Map) is { Count: > 0 } collision)
            World.Add(cell, collision, origin);
        var map = _maps[cell] = LoadedMap.Activate(_plugins.Get<MapRecord>(chunk.Map), cell, _game.Flags, _game.Memory, _game.Party,
            _game.Random.World, origin);
        Loaded?.Invoke(chunk, map);
    }

    private void Unload((int Row, int Column) cell)
    {
        Unloading?.Invoke(_chunks[cell], _maps[cell]);
        _maps.Remove(cell);
        World.Remove(cell);
    }
}
