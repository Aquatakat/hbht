using Aidyn.Core;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Physics;
using Aidyn.Core.Scenes;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Keeps the 3×3 chunks around a point loaded, as the original's zone engine does
/// (<c>ZoneDatMtx</c>): each chunk's scenery, its see-through scenery, the models its scene
/// objects place, its collision, which goes into the <see cref="CollisionWorld"/> keyed by its row
/// and column, its map objects as they stand while it's loaded (<see cref="LoadedMap"/>), and its
/// monster parties' bodies (<see cref="Wanderers"/>).
/// </summary>
public partial class ChunkStreamer : Node3D
{
    private readonly PluginSet? _plugins;
    private readonly GameState? _game;
    private readonly Models? _models;
    private readonly MapSetRecord? _set;
    private readonly CollisionWorld? _world;
    private readonly Dictionary<(int Row, int Column), ChunkEntry> _chunks = [];
    private readonly Dictionary<(int Row, int Column), ChunkScenery> _loaded = [];
    /// <summary>Each loaded chunk's map objects.</summary>
    private readonly Dictionary<(int Row, int Column), LoadedMap> _maps = [];
    private readonly Wanderers? _wanderers;
    private readonly Texture2D? _shadow;
    private readonly Dictionary<Wanderer, WandererView> _views = [];
    private (int Row, int Column) _centre;

    public ChunkStreamer()
    {
    }

    /// <param name="wanderers">
    /// The game's wanderers, which should hold none when the streamer starts. None for a cut-scene's
    /// stage, where monster parties have no bodies (<c>look_for_monsterparties</c>, <c>0x8000ef20</c>).
    /// </param>
    /// <param name="shadow">The texture of bodies' shadows.</param>
    public ChunkStreamer(PluginSet plugins, Models models, MapSetRecord set, CollisionWorld world, GameState game, Wanderers? wanderers,
        Texture2D? shadow)
    {
        _plugins = plugins;
        _wanderers = wanderers;
        _shadow = shadow;
        _game = game;
        _models = models;
        _set = set;
        _world = world;
        Name = "Chunks";
        // Actors here animate with the frame, not the physics tick, so there's nothing to interpolate.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        foreach (var chunk in set.Chunks)
            _chunks[(chunk.Row, chunk.Column)] = chunk;
    }

    public MapSetRecord MapSet => _set!;

    /// <summary>The chunk a point is in: rows run along X and columns along Z.</summary>
    public (int Row, int Column) CellAt(Vector3 position) =>
        ((int)Math.Floor(position.X / MapSet.CellSize), (int)Math.Floor(position.Z / MapSet.CellSize));

    public ChunkEntry? ChunkAt(Vector3 position) => _chunks.GetValueOrDefault(CellAt(position));

    public ChunkEntry? Find(string name) => _chunks.Values.FirstOrDefault(c => c.Name == name);

    public Vector3 Origin(ChunkEntry chunk) => MapSet.Origin(chunk.Row, chunk.Column).ToGodot();

    /// <summary>A loaded chunk's map objects, by the key its collision is under.</summary>
    public LoadedMap? Map(object key) => key is ValueTuple<int, int> cell ? _maps.GetValueOrDefault(cell) : null;

    /// <summary>The map objects of the loaded chunk a point is in.</summary>
    public LoadedMap? MapAt(Vector3 position) => _maps.GetValueOrDefault(CellAt(position));

    /// <summary>Where a loaded chunk's map is, in the map set.</summary>
    public Vector3 Origin(LoadedMap map) => map.Key is ValueTuple<int, int> cell ? MapSet.Origin(cell.Item1, cell.Item2).ToGodot() : Vector3.Zero;

    /// <summary>Loads the chunks around <paramref name="position"/> and frees the rest. Returns whether any changed.</summary>
    public bool Update(Vector3 position)
    {
        var (row, column) = _centre = CellAt(position);
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
        foreach (var cell in _loaded.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _loaded[cell].Root.QueueFree();
            _loaded.Remove(cell);
            foreach (var gone in _wanderers?.Unload(_maps[cell]) ?? [])
            {
                _views[gone].QueueFree();
                _views.Remove(gone);
            }
            _maps.Remove(cell);
            _world!.Remove(cell);
            changed = true;
        }
        foreach (var cell in wanted.Where(k => !_loaded.ContainsKey(k)))
        {
            var scenery = _loaded[cell] = Load(_chunks[cell]);
            AddChild(scenery.Root);
            changed = true;
        }
        return changed;
    }

    /// <summary>The wanderers' bodies as they're drawn.</summary>
    public IEnumerable<WandererView> Wanderers => _views.Values;

    /// <summary>A wanderer's body as it's drawn.</summary>
    public WandererView? View(Wanderer wanderer) => _views.GetValueOrDefault(wanderer);

    /// <summary>The loaded chunks' billboards.</summary>
    public IEnumerable<Node3D> BillboardFacings => _loaded.Values.SelectMany(c => c.Billboards);

    /// <summary>Shows and fades the loaded chunks' scene objects for this view (<see cref="ChunkScenery.Fade"/>).</summary>
    public void Fade(in SceneView view, DrawDistance range)
    {
        foreach (var scenery in _loaded.Values)
            scenery.Fade(view, range);
    }

    /// <summary>
    /// The scenes with scrolling textures the original would draw this frame: the centre chunk's,
    /// a neighbour's when it's in the camera's view (<see cref="ChunkView"/>), and the levels of
    /// detail of those chunks' scene objects that <see cref="Fade"/> last showed.
    /// </summary>
    /// <remarks>
    /// The original also draws only the objects its collision grid lists for the player's square,
    /// or for the squares along a neighbour's edge, which isn't ported.
    /// </remarks>
    /// <param name="camera">The camera's position.</param>
    /// <param name="look">The way the camera looks.</param>
    public IEnumerable<ScrollingScene> DrawnScenes(Vector3 camera, Vector3 look)
    {
        var corner = MapSet.Origin(_centre.Row, _centre.Column).ToGodot();
        var eye = new System.Numerics.Vector2(camera.X - corner.X, camera.Z - corner.Z);
        var flat = new System.Numerics.Vector2(look.X, look.Z);
        flat = flat == default ? System.Numerics.Vector2.UnitX : System.Numerics.Vector2.Normalize(flat);
        var cell = new System.Numerics.Vector2(MapSet.CellSize);
        foreach (var ((row, column), scenery) in _loaded)
        {
            if (!ChunkView.Drawn(eye, flat, row - _centre.Row, column - _centre.Column, cell))
                continue;
            foreach (var scene in scenery.Scrolling())
                yield return scene;
        }
    }

    /// <summary>
    /// Gates the loaded chunks' scene objects again by the flags, as the original does when it
    /// reloads the map after a conversation, the pause menu or a teleport (<c>loadGameBorgScenes</c>).
    /// A chunk whose objects come and go is built again; its collision and the rest of its objects
    /// stay as they are.
    /// </summary>
    public void Regate()
    {
        foreach (var (cell, map) in _maps)
        {
            if (map.Regate(_game!.Flags))
                Rebuild(map);
        }
    }

    /// <summary>Builds a loaded chunk's scenery again, after a container's model changed or an object came or went.</summary>
    public void Rebuild(LoadedMap map)
    {
        if (map.Key is not ValueTuple<int, int> cell || !_loaded.TryGetValue(cell, out var old))
            return;
        old.Root.QueueFree();
        var scenery = _loaded[cell] = Scenery(_chunks[cell], map);
        AddChild(scenery.Root);
    }

    /// <summary>The actor a loaded chunk's scene object places, if it's placed and animated.</summary>
    public ActorAnimator? Animator(LoadedMap map, SceneObject actor) =>
        map.Key is ValueTuple<int, int> cell && _loaded.TryGetValue(cell, out var scenery) ? scenery.Find(actor.Index)?.Levels.FirstOrDefault()?.Animator : null;

    // The chunk's collision, its objects activated by the flags as the original does as it loads
    // (VoxelObjects_Activate), and its scenery.
    private ChunkScenery Load(ChunkEntry chunk)
    {
        var cell = (chunk.Row, chunk.Column);
        if (_plugins!.FindAsset(chunk.Map, "glb") is { } collision)
            _world!.Add(cell, CollisionMesh.Read(File.ReadAllBytes(collision)), MapSet.Origin(chunk.Row, chunk.Column));
        var game = _game!;
        var map = _maps[cell] = LoadedMap.Activate(_plugins.Get<MapRecord>(chunk.Map), cell, game.Flags, game.Memory, game.Party, game.Random.World,
            MapSet.Origin(chunk.Row, chunk.Column));
        foreach (var wanderer in _wanderers?.Load(map, MapSet.CellSize, _world!, game.Memory) ?? [])
        {
            var view = _views[wanderer] = new WandererView(wanderer, _plugins, _models!, _shadow);
            AddChild(view);
        }
        return Scenery(chunk, map);
    }

    // Whether a scene object is there is decided as its chunk loads, not every frame. The runtime
    // plays with the Expansion Pak.
    private ChunkScenery Scenery(ChunkEntry chunk, LoadedMap map) =>
        ChunkScenery.Load(_plugins!, _models!, MapSet, chunk, o => map.IsActive(o.Index), o => (int)map.Look(o.Index));
}
