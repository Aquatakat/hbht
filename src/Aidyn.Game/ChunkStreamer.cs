using Aidyn.Core.Flags;
using Aidyn.Core.Physics;
using Aidyn.Core.Scenes;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Keeps the 3×3 chunks around a point loaded, as the original's zone engine does
/// (<c>ZoneDatMtx</c>): each chunk's scenery, its see-through scenery, the models its scene
/// objects place, and its collision, which goes into the <see cref="CollisionWorld"/>.
/// </summary>
public partial class ChunkStreamer : Node3D
{
    private readonly PluginSet? _plugins;
    private readonly FlagNetwork? _flags;
    private readonly Models? _models;
    private readonly MapSetRecord? _set;
    private readonly CollisionWorld? _world;
    private readonly Dictionary<(int Row, int Column), ChunkEntry> _chunks = [];
    private readonly Dictionary<(int Row, int Column), ChunkScenery> _loaded = [];
    /// <summary>Each loaded chunk's scene objects that its flags placed, by index.</summary>
    private readonly Dictionary<(int Row, int Column), HashSet<int>> _placed = [];
    private (int Row, int Column) _centre;

    public ChunkStreamer()
    {
    }

    public ChunkStreamer(PluginSet plugins, Models models, MapSetRecord set, CollisionWorld world, FlagNetwork flags)
    {
        _plugins = plugins;
        _flags = flags;
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
            _placed.Remove(cell);
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
    /// A chunk whose objects come and go is built again; its collision stays.
    /// </summary>
    public void Regate()
    {
        foreach (var (cell, placed) in _placed.ToList())
        {
            var chunk = _chunks[cell];
            if (Placed(chunk).SetEquals(placed))
                continue;
            _loaded[cell].Root.QueueFree();
            var scenery = _loaded[cell] = Scenery(chunk);
            AddChild(scenery.Root);
        }
    }

    private ChunkScenery Load(ChunkEntry chunk)
    {
        // Toggles stay as they're stored until a trigger changes them (M2's map objects).
        if (_plugins!.FindAsset(chunk.Map, "glb") is { } collision)
            _world!.Add((chunk.Row, chunk.Column), CollisionMesh.Read(File.ReadAllBytes(collision)), MapSet.Origin(chunk.Row, chunk.Column));
        return Scenery(chunk);
    }

    // Whether an object is there is decided as its chunk loads, as the original does
    // (VoxelObjects_Activate), not every frame.
    private ChunkScenery Scenery(ChunkEntry chunk)
    {
        var placed = _placed[(chunk.Row, chunk.Column)] = Placed(chunk);
        return ChunkScenery.Load(_plugins!, _models!, MapSet, chunk, o => placed.Contains(o.Index));
    }

    /// <summary>The chunk's scene objects there now: the runtime plays with the Expansion Pak, and each by its flag gate.</summary>
    private HashSet<int> Placed(ChunkEntry chunk) => [.. _plugins!.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>()
        .Where(o => o.Pak != PakVariant.Without && !(o.Visible is { } gate && _flags!.Get(gate.Flag) == gate.Invert)).Select(o => o.Index)];
}
