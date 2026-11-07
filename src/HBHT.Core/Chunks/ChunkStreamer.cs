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
/// Trek gives a chunk's npcs and encounters their bodies as it loads (<see cref="Loaded"/>); the runtime builds its
/// scenery. Given a <see cref="VisibleWorld"/>, it keeps the solid surfaces the chunks draw there too,
/// under the same keys, which rain and snow land on (<see cref="ChunkSurfaces"/>).
/// </summary>
/// <remarks>Rows run along X and columns along Z, and a chunk's coordinates start at its corner.</remarks>
public sealed class ChunkStreamer
{
    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly ChunkIndex _index;
    /// <summary>The map set's chunks as they are, read as they're first wanted (ADR 0036); null for a cell with none.</summary>
    private readonly Dictionary<ChunkKey, ChunkRecord?> _own = [];
    /// <summary>The loaded chunks as they were drawn as they loaded (<see cref="ChunkRecord.Drawn"/>).</summary>
    private readonly Dictionary<ChunkKey, ChunkRecord> _chunks = [];
    private readonly Dictionary<ChunkKey, LoadedMap> _maps = [];
    private readonly ChunkSurfaces? _surfaces;

    public ChunkStreamer(PluginSet plugins, GameState game, CollisionWorld world, MapSetRecord set, VisibleWorld? visible = null)
    {
        _plugins = plugins;
        _game = game;
        _index = ChunkIndex.For(plugins);
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
    public event Action<ChunkRecord, LoadedMap>? Loaded;

    /// <summary>A chunk is going: its collision and objects go after this.</summary>
    public event Action<ChunkRecord, LoadedMap>? Unloading;

    /// <summary>The chunk the 3×3 was last loaded around.</summary>
    public ChunkKey Centre { get; private set; }

    /// <summary>The point the 3×3 was last loaded around (<see cref="Update"/>).</summary>
    public Vector3 Focus { get; private set; }

    /// <summary>The loaded chunks and their map objects.</summary>
    public IEnumerable<(ChunkRecord Chunk, LoadedMap Map)> All => _maps.Select(m => (_chunks[m.Key], m.Value));

    /// <summary>The chunk a point is in.</summary>
    public ChunkKey CellAt(Vector3 position) =>
        new(MapSet.Id, (int)MathF.Floor(position.X / MapSet.CellSize), (int)MathF.Floor(position.Z / MapSet.CellSize));

    /// <summary>The chunk a point is in, as it was drawn if it's loaded.</summary>
    public ChunkRecord? ChunkAt(Vector3 position) => Drawn(CellAt(position));

    /// <summary>A chunk of the map set by its name, such as <c>I08</c> (<see cref="ChunkRecord.NameOf"/>), as it was drawn if it's loaded.</summary>
    public ChunkRecord? Find(string name) => ChunkRecord.CellOf(name) is { } cell ? Drawn(new ChunkKey(MapSet.Id, cell.Row, cell.Column)) : null;

    /// <summary>The map set's chunk in a cell, as it is, or null where it has none.</summary>
    public ChunkRecord? Chunk(ChunkKey cell)
    {
        if (cell.MapSet != MapSet.Id)
            return null;
        if (!_own.TryGetValue(cell, out var chunk))
            _own[cell] = chunk = _index.At(cell.MapSet, cell.Row, cell.Column) is { } id ? _plugins.TryGet<ChunkRecord>(id) : null;
        return chunk;
    }

    /// <summary>A chunk of the map set as it was drawn as it loaded, or as it is if it isn't loaded.</summary>
    private ChunkRecord? Drawn(ChunkKey cell) => _chunks.GetValueOrDefault(cell) ?? Chunk(cell);

    public Vector3 Origin(ChunkRecord chunk) => MapSet.Origin(chunk.Row, chunk.Column);

    /// <summary>A loaded chunk's map objects, by the key its collision is under.</summary>
    public LoadedMap? Map(ChunkKey key) => _maps.GetValueOrDefault(key);

    /// <summary>The map objects of the loaded chunk a point is in.</summary>
    public LoadedMap? MapAt(Vector3 position) => _maps.GetValueOrDefault(CellAt(position));

    /// <summary>The chunk a loaded map is.</summary>
    public ChunkRecord? ChunkOf(LoadedMap map) => _chunks.GetValueOrDefault(map.Key);

    /// <summary>
    /// Loads the chunks around <paramref name="position"/> and frees the rest, and loads again a chunk the flags now draw with other
    /// scenes (<see cref="ChunkRecord.Swap"/>), as the original does a cell whose scene isn't the one it was loaded with
    /// (<c>LoadGameBorgScenes</c>). Returns whether any changed.
    /// </summary>
    public bool Update(Vector3 position)
    {
        Focus = position;
        var centre = Centre = CellAt(position);
        // Kept from call to call, as it runs every frame: a set emptied lists what's added in the order it's added, as a new one does.
        var wanted = _wanted;
        wanted.Clear();
        for (var r = centre.Row - 1; r <= centre.Row + 1; r++)
        {
            for (var c = centre.Column - 1; c <= centre.Column + 1; c++)
            {
                var cell = centre with { Row = r, Column = c };
                if (Chunk(cell) is not null)
                    wanted.Add(cell);
            }
        }

        var going = _changing;
        going.Clear();
        foreach (var k in _maps.Keys)
        {
            if (!wanted.Contains(k) || Redrawn(k))
                going.Add(k);
        }
        var changed = going.Count > 0;
        foreach (var cell in going)
            Unload(cell);
        going.Clear();
        foreach (var k in wanted)
        {
            if (!_maps.ContainsKey(k))
                going.Add(k);
        }
        changed |= going.Count > 0;
        foreach (var cell in going)
            Load(Chunk(cell)!);
        return changed;
    }

    private readonly HashSet<ChunkKey> _wanted = [];
    private readonly List<ChunkKey> _changing = [];

    /// <summary>
    /// Reads what loading a chunk of the map set reads ahead of time, from any thread: the triangles of the surfaces it draws,
    /// if it keeps them. Nothing changes but when they're read, so a chunk loads the same whether it was prepared or not, and
    /// the runtime prepares the chunks the next crossing could load off the main thread
    /// (docs/adr/0025-chunks-load-off-the-main-thread.md).
    /// </summary>
    public void Prepare(ChunkRecord chunk) => _surfaces?.Warm(chunk);

    /// <summary>Frees every loaded chunk and streams another map set's, as a teleport into it does.</summary>
    public void Change(MapSetRecord set)
    {
        Clear();
        MapSet = set;
        _own.Clear();
        _chunks.Clear();
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
    /// Collision and the rest of the objects stay as they are, but for a chunk the flags now draw with other scenes, which loads
    /// again whole (<see cref="Update"/>).
    /// </summary>
    /// <returns>The maps whose scene objects came or went, but for those loaded again.</returns>
    public IReadOnlyList<LoadedMap> Regate()
    {
        foreach (var cell in _maps.Keys.Where(Redrawn).ToList())
        {
            Unload(cell);
            Load(Chunk(cell)!);
        }
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
            foreach (var o in map.PutOut(view, _game.Memory, centre.Id))
                (putOut ??= []).Add((map, o));
            if ((putOut?.Count ?? 0) > before)
                Refresh(map);
        }
        return putOut ?? [];
    }

    // The chunk's collision, and its objects activated by the flags as the original does as it loads
    // (VoxelObjects_Activate), with the scenes the flags draw it with (CheckToggleZoneScene).
    private void Load(ChunkRecord own)
    {
        var cell = ChunkKey.Of(own);
        var chunk = _chunks[cell] = own.Drawn(_game.Flags.Holds);
        var origin = Origin(chunk);
        if (chunk.Collision is { } asset && CollisionMesh.For(_plugins, asset) is { Count: > 0 } collision)
            World.Add(cell, collision, origin);
        var map = _maps[cell] = LoadedMap.Activate(chunk, cell, _game.Flags, _game.Memory, _game.Party, _game.Random.World, origin);
        Visible?.Add(cell, _surfaces!.For(chunk, map), origin);
        Loaded?.Invoke(chunk, map);
    }

    /// <summary>Whether a loaded chunk's scenes aren't the ones the flags draw it with now. Only a chunk with a swap reads its flag.</summary>
    private bool Redrawn(ChunkKey cell) => Chunk(cell) is { Swap: not null } own && own.Drawn(_game.Flags.Holds) != _chunks[cell];

    private void Unload(ChunkKey cell)
    {
        Unloading?.Invoke(_chunks[cell], _maps[cell]);
        _maps.Remove(cell);
        _chunks.Remove(cell);
        World.Remove(cell);
        Visible?.Remove(cell);
    }
}
