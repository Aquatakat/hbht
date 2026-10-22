using Aidyn.Core;
using Aidyn.Core.Chunks;
using Aidyn.Core.Lights;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Scenes;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the chunks the core's <see cref="ChunkStreamer"/> has loaded: each chunk's scenery, its
/// see-through scenery and the models its scene objects place (<see cref="ChunkScenery"/>), and the
/// bodies of its monster parties that have them (<see cref="WandererView"/>).
/// </summary>
public partial class ChunkStreamerView : Node3D
{
    private readonly PluginSet? _plugins;
    private readonly Models? _models;
    private readonly ChunkStreamer? _chunks;
    private readonly Wanderers? _wanderers;
    private readonly Texture2D? _shadow;
    private readonly Dictionary<LoadedMap, ChunkScenery> _loaded = [];
    private readonly Dictionary<Wanderer, WandererView> _views = [];

    public ChunkStreamerView()
    {
    }

    /// <param name="wanderers">
    /// The wanderers whose bodies to draw. None for a cut-scene's stage, where monster parties have no
    /// bodies (<c>look_for_monsterparties</c>, <c>0x8000ef20</c>).
    /// </param>
    /// <param name="shadow">The texture of bodies' shadows.</param>
    public ChunkStreamerView(PluginSet plugins, Models models, ChunkStreamer chunks, Wanderers? wanderers, Texture2D? shadow)
    {
        _plugins = plugins;
        _models = models;
        _chunks = chunks;
        _wanderers = wanderers;
        _shadow = shadow;
        Name = "Chunks";
        // Actors here animate with the frame, not the physics tick, so there's nothing to interpolate.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        foreach (var (chunk, map) in chunks.All)
            Add(chunk, map);
        chunks.Loaded += Add;
        chunks.Unloading += Remove;
    }

    public ChunkStreamer Chunks => _chunks!;

    public override void _ExitTree()
    {
        if (_chunks is null)
            return;
        _chunks.Loaded -= Add;
        _chunks.Unloading -= Remove;
    }

    public Vector3 Origin(ChunkEntry chunk) => Chunks.Origin(chunk).ToGodot();

    /// <summary>The wanderers' bodies as they're drawn.</summary>
    public IEnumerable<WandererView> Wanderers => _views.Values;

    /// <summary>A wanderer's body as it's drawn.</summary>
    public WandererView? View(Wanderer wanderer) => _views.GetValueOrDefault(wanderer);

    /// <summary>
    /// Gives each wanderer with a body a view, and takes away the views of those gone: their chunk
    /// unloaded, or their party joined the player's.
    /// </summary>
    public void SyncWanderers()
    {
        var all = _wanderers?.All ?? [];
        foreach (var gone in _views.Keys.Where(w => !all.Contains(w)).ToList())
        {
            _views[gone].QueueFree();
            _views.Remove(gone);
        }
        foreach (var wanderer in all.Where(w => !_views.ContainsKey(w)))
        {
            var view = _views[wanderer] = new WandererView(wanderer, _plugins!, _models!, _shadow);
            AddChild(view);
        }
    }

    /// <summary>
    /// Lights the loaded chunks' actors that are drawn and aren't fullbright by the point lights that reach them this
    /// frame (<see cref="WorldLights.ForActor"/>), each from where its map places it.
    /// </summary>
    public void LightActors(WorldLights lights)
    {
        foreach (var (map, scenery) in _loaded)
        {
            foreach (var placed in scenery.Objects)
            {
                if (placed.Object.Flags.HasFlag(SceneObjectFlags.Fullbright))
                    continue;
                foreach (var level in placed.Levels)
                {
                    if (level is { Animator: not null, Node.Visible: true })
                        N64Material.SetLights(level.Meshes, lights.ForActor(map.Origin + placed.Object.Position, map));
                }
            }
        }
    }

    /// <summary>Lights the wanderers' bodies by the dynamic lights that reach them: they aren't the party's, so map lights don't.</summary>
    public void LightWanderers(WorldLights lights, ChunkStreamer chunks)
    {
        foreach (var view in _views.Values)
            view.Light(lights, chunks);
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
    /// Takes a scene object trek put out out of the scenery, one of Oriana's path lights (<see cref="ITrekHost.PutOut"/>),
    /// leaving the rest of its chunk as it is.
    /// </summary>
    public void PutOut(LoadedMap map, SceneObject o)
    {
        if (_loaded.TryGetValue(map, out var scenery))
            scenery.Remove(o.Index);
    }

    /// <summary>
    /// The scenes with scrolling textures the original would draw this frame: the centre chunk's,
    /// a neighbour's when it's in the camera's view or the centre's map has it drawn whatever the view
    /// (<see cref="ChunkView"/>), and the levels of
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
        var centre = Chunks.Centre;
        var set = Chunks.MapSet;
        var corner = set.Origin(centre.Row, centre.Column).ToGodot();
        var eye = new System.Numerics.Vector2(camera.X - corner.X, camera.Z - corner.Z);
        var flat = new System.Numerics.Vector2(look.X, look.Z);
        flat = SafeNormalize.Of(flat, System.Numerics.Vector2.UnitX);
        var cell = new System.Numerics.Vector2(set.CellSize);
        var forced = _loaded.Keys.FirstOrDefault(m => m.Key == centre)?.Record.DrawnNeighbours ?? Neighbours.None;
        foreach (var (map, scenery) in _loaded)
        {
            if (!ChunkView.Drawn(eye, flat, map.Key.Row - centre.Row, map.Key.Column - centre.Column, cell, forced))
                continue;
            foreach (var scene in scenery.Scrolling())
                yield return scene;
        }
    }

    /// <summary>Builds a loaded chunk's scenery again, after a container's model changed or an object came or went.</summary>
    public void Rebuild(LoadedMap map)
    {
        if (!_loaded.TryGetValue(map, out var old) || Chunks.ChunkOf(map) is not { } chunk)
            return;
        old.Root.QueueFree();
        var scenery = _loaded[map] = Scenery(chunk, map);
        AddChild(scenery.Root);
    }

    /// <summary>The actor a loaded chunk's scene object places, if it's placed and animated.</summary>
    public ActorAnimator? Animator(LoadedMap map, SceneObject actor) =>
        _loaded.TryGetValue(map, out var scenery) ? scenery.Find(actor.Index)?.Levels.FirstOrDefault()?.Animator : null;

    private void Add(ChunkEntry chunk, LoadedMap map)
    {
        var scenery = _loaded[map] = Scenery(chunk, map);
        AddChild(scenery.Root);
        SyncWanderers();
    }

    private void Remove(ChunkEntry chunk, LoadedMap map)
    {
        if (_loaded.Remove(map, out var scenery))
            scenery.Root.QueueFree();
        SyncWanderers();
    }

    // Whether a scene object is there is decided as its chunk loads, not every frame. The runtime
    // plays with the Expansion Pak.
    private ChunkScenery Scenery(ChunkEntry chunk, LoadedMap map) =>
        ChunkScenery.Load(_plugins!, _models!, Chunks.MapSet, chunk, o => map.IsActive(o.Index), o => (int)map.Look(o.Index));
}
