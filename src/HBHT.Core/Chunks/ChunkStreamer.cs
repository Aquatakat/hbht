using System.Numerics;
using HBHT.Core.MapObjects;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Core.Chunks;

/// <summary>
/// Keeps the 3×3 chunks around a point loaded, as the original's zone engine does (<c>ZoneDatMtx</c>):
/// each chunk's collision, which goes into the <see cref="CollisionWorld"/> under its <see cref="ChunkKey"/>
/// (its map set, row and column), and its map objects as they stand while it's loaded (<see cref="LoadedMap"/>).
/// Trek gives a chunk's monster parties their bodies as it loads (<see cref="Loaded"/>); the runtime builds its
/// scenery. Given a <see cref="VisibleWorld"/>, it keeps the solid surfaces the chunks draw there too,
/// under the same keys, which rain and snow land on (<see cref="ChunkSurfaces"/>).
/// </summary>
/// <remarks>Rows run along X and columns along Z, and a chunk's coordinates start at its corner.</remarks>
public sealed class ChunkStreamer
{
    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly Dictionary<ChunkKey, ChunkEntry> _chunks = [];
    private readonly Dictionary<ChunkKey, LoadedMap> _maps = [];
    private readonly ChunkSurfaces? _surfaces;

    public ChunkStreamer(PluginSet plugins, GameState game, CollisionWorld world, MapSetRecord set, VisibleWorld? visible = null)
    {
        _plugins = plugins;
        _game = game;
        World = world;
        Visible = visible;
        if (visible is not null)
            _surfaces = new ChunkSurfaces(plugins);
        MapSet = set;
        Change(set);
    }

    public MapSetRecord MapSet { get; private set; }

    /// <summary>The loaded chunks' collision.</summary>
    public CollisionWorld World { get; }

    /// <summary>The solid surfaces the loaded chunks draw, if it keeps them.</summary>
    public VisibleWorld? Visible { get; }

    /// <summary>A chunk has loaded: its collision is in the world and its objects are activated.</summary>
    public event Action<ChunkEntry, LoadedMap>? Loaded;

    /// <summary>A chunk is going: its collision and objects go after this.</summary>
    public event Action<ChunkEntry, LoadedMap>? Unloading;

    /// <summary>The chunk the 3×3 was last loaded around.</summary>
    public ChunkKey Centre { get; private set; }

    /// <summary>The point the 3×3 was last loaded around (<see cref="Update"/>).</summary>
    public Vector3 Focus { get; private set; }

    /// <summary>The loaded chunks and their map objects.</summary>
    public IEnumerable<(ChunkEntry Chunk, LoadedMap Map)> All => _maps.Select(m => (_chunks[m.Key], m.Value));

    /// <summary>The chunk a point is in.</summary>
    public ChunkKey CellAt(Vector3 position) =>
        new(MapSet.Id, (int)MathF.Floor(position.X / MapSet.CellSize), (int)MathF.Floor(position.Z / MapSet.CellSize));

    public ChunkEntry? ChunkAt(Vector3 position) => _chunks.GetValueOrDefault(CellAt(position));

    public ChunkEntry? Find(string name) => _chunks.Values.FirstOrDefault(c => c.Name == name);

    public Vector3 Origin(ChunkEntry chunk) => MapSet.Origin(chunk.Row, chunk.Column);

    /// <summary>A loaded chunk's map objects, by the key its collision is under.</summary>
    public LoadedMap? Map(ChunkKey key) => _maps.GetValueOrDefault(key);

    /// <summary>The map objects of the loaded chunk a point is in.</summary>
    public LoadedMap? MapAt(Vector3 position) => _maps.GetValueOrDefault(CellAt(position));

    /// <summary>The chunk a loaded map is.</summary>
    public ChunkEntry? ChunkOf(LoadedMap map) => _chunks.GetValueOrDefault(map.Key);

    /// <summary>Loads the chunks around <paramref name="position"/> and frees the rest. Returns whether any changed.</summary>
    public bool Update(Vector3 position)
    {
        Focus = position;
        var centre = Centre = CellAt(position);
        var wanted = new HashSet<ChunkKey>();
        for (var r = centre.Row - 1; r <= centre.Row + 1; r++)
        {
            for (var c = centre.Column - 1; c <= centre.Column + 1; c++)
            {
                var cell = centre with { Row = r, Column = c };
                if (_chunks.ContainsKey(cell))
                    wanted.Add(cell);
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

    /// <summary>
    /// Reads what loading a chunk of the map set reads ahead of time, from any thread: the triangles of the surfaces it draws,
    /// if it keeps them. Nothing changes but when they're read, so a chunk loads the same whether it was prepared or not, and
    /// the runtime prepares the chunks the next crossing could load off the main thread
    /// (docs/adr/0025-chunks-load-off-the-main-thread.md).
    /// </summary>
    public void Prepare(ChunkEntry chunk) => _surfaces?.Warm(chunk);

    /// <summary>Frees every loaded chunk and streams another map set's, as a teleport into it does.</summary>
    public void Change(MapSetRecord set)
    {
        Clear();
        MapSet = set;
        _chunks.Clear();
        foreach (var chunk in set.Chunks)
            _chunks[ChunkKey.Of(set, chunk)] = chunk;
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
    public IReadOnlyList<LoadedMap> Regate()
    {
        IReadOnlyList<LoadedMap> changed = [.. _maps.Values.Where(m => m.Regate(_game.Flags, _game.Memory))];
        foreach (var map in changed)
            Refresh(map);
        return changed;
    }

    /// <summary>
    /// Takes a loaded chunk's drawn surfaces again (<see cref="Visible"/>), after a scene object came or went or a
    /// container's model changed.
    /// </summary>
    public void Refresh(LoadedMap map)
    {
        if (Visible is not null && _maps.GetValueOrDefault(map.Key) == map)
            Visible.Add(map.Key, _surfaces!.For(_chunks[map.Key], map), Origin(_chunks[map.Key]));
    }

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
            var before = putOut?.Count ?? 0;
            foreach (var o in map.PutOut(view, _game.Memory, centre.Map))
                (putOut ??= []).Add((map, o));
            if ((putOut?.Count ?? 0) > before)
                Refresh(map);
        }
        return putOut ?? [];
    }

    // The chunk's collision, and its objects activated by the flags as the original does as it loads
    // (VoxelObjects_Activate).
    private void Load(ChunkEntry chunk)
    {
        var cell = ChunkKey.Of(MapSet, chunk);
        var origin = Origin(chunk);
        if (CollisionMesh.For(_plugins, chunk.Map) is { Count: > 0 } collision)
            World.Add(cell, collision, origin);
        var map = _maps[cell] = LoadedMap.Activate(_plugins.Get<MapRecord>(chunk.Map), cell, _game.Flags, _game.Memory, _game.Party,
            _game.Random.World, origin);
        Visible?.Add(cell, _surfaces!.For(chunk, map), origin);
        Loaded?.Invoke(chunk, map);
    }

    private void Unload(ChunkKey cell)
    {
        Unloading?.Invoke(_chunks[cell], _maps[cell]);
        _maps.Remove(cell);
        World.Remove(cell);
        Visible?.Remove(cell);
    }
}
