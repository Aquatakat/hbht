using System.Collections.Concurrent;
using Aidyn.Core;
using Aidyn.Core.Chunks;
using Aidyn.Core.Lights;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Scenes;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the chunks the core's <see cref="ChunkStreamer"/> has loaded: each chunk's scenery, its
/// see-through scenery and the models its scene objects place (<see cref="ChunkScenery"/>), and the
/// bodies of its monster parties that have them (<see cref="WandererView"/>). Trek's also draws the scenery of the
/// Overworld's 5×5 under the Draw Distance setting's Extended (<see cref="SceneryRing"/>).
/// </summary>
/// <remarks>
/// Trek's builds a chunk's scenery off the main thread where it can't be seen arriving (<see cref="ChunkBuilder"/>,
/// docs/adr/0025-chunks-load-off-the-main-thread.md): a chunk the 3×3 loads at least <see cref="NearBuild"/> from where it was
/// loaded about, as a crossing loads them 100 away, and every chunk of the ring, then adds it to the tree on the main
/// thread a frame or a few later, one a frame. What the game asks of the scenery at once, the actors a trigger animates
/// and a light put out, is in the chunk the player is in, which is built at once. A chunk going from the 3×3 into the ring
/// or back keeps its old scenery drawn until the new is built, so nothing goes missing as it changes hands. A wanderer's
/// view is made at once, with the motion the core steps its body by (<see cref="ITrekHost.Motion"/>), rolling on the game's
/// stream as it always has, and its actor's scene is built off the main thread and drawn when it comes (<see cref="BodyView"/>).
/// </remarks>
public partial class ChunkStreamerView : Node3D
{
    /// <summary>
    /// How near to where the 3×3 was loaded about a chunk is built at once, rather than off the main thread: three quarters
    /// of a chunk, so a crossing's chunks, 100 away, are built off it, and a teleport's, around a point inside its middle
    /// chunk, mostly at once.
    /// </summary>
    public const float NearBuild = 0.75f;

    /// <summary>
    /// How near to where the 3×3 was loaded about a wanderer's body is built at once: twice the 20 bodies are drawn to from
    /// the camera's aim (<see cref="BodyShadow.TrekDistance"/>).
    /// </summary>
    public const float NearBody = 2 * BodyShadow.TrekDistance;

    private readonly PluginSet? _plugins;
    private readonly Models? _models;
    private readonly ChunkStreamer? _chunks;
    private readonly Wanderers? _wanderers;
    private readonly Texture2D? _shadow;
    private readonly SceneryRing? _ring;
    private readonly ChunkBuilder? _builder;
    private readonly Dictionary<LoadedMap, ChunkScenery> _loaded = [];
    private readonly Dictionary<LoadedMap, ChunkScenery> _ringLoaded = [];
    private readonly Dictionary<LoadedMap, Building> _building = [];
    private readonly Dictionary<ChunkKey, ChunkScenery> _standIns = [];
    private readonly ConcurrentQueue<Building> _built = new();
    private readonly Dictionary<Wanderer, WandererView> _views = [];
    private (RecordId Set, ChunkKey Centre, bool Ring)? _readAround;

    public ChunkStreamerView()
    {
    }

    /// <param name="wanderers">
    /// The wanderers whose bodies to draw. None for a cut-scene's stage, where monster parties have no
    /// bodies (<c>look_for_monsterparties</c>, <c>0x8000ef20</c>).
    /// </param>
    /// <param name="shadow">The texture of bodies' shadows.</param>
    /// <param name="ring">Trek's scenery beyond the 3×3, under Draw Distance's Extended.</param>
    /// <param name="builder">What builds scenery off the main thread; with none, everything is built at once.</param>
    public ChunkStreamerView(PluginSet plugins, Models models, ChunkStreamer chunks, Wanderers? wanderers, Texture2D? shadow,
        SceneryRing? ring = null, ChunkBuilder? builder = null)
    {
        _plugins = plugins;
        _models = models;
        _chunks = chunks;
        _wanderers = wanderers;
        _shadow = shadow;
        _ring = ring;
        _builder = builder;
        Name = "Chunks";
        // Actors here animate with the frame, not the physics tick, so there's nothing to interpolate.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        foreach (var (chunk, map) in chunks.All)
            Show(map, Scenery(chunk, map, ring: false), ring: false);
        SyncWanderers();
        chunks.Loaded += Add;
        chunks.Unloading += Remove;
        if (ring is not null)
        {
            foreach (var (chunk, map) in ring.All)
                AddRing(chunk, map);
            ring.Loaded += AddRing;
            ring.Unloading += RemoveRing;
        }
    }

    public ChunkStreamer Chunks => _chunks!;

    /// <summary>The ring's chunks as drawn, and those still building.</summary>
    public int RingCount => _ringLoaded.Count;

    /// <summary>How many chunks' scenery is building off the main thread.</summary>
    public int BuildingCount => _building.Count;

    public override void _ExitTree()
    {
        if (_chunks is null)
            return;
        _chunks.Loaded -= Add;
        _chunks.Unloading -= Remove;
        if (_ring is not null)
        {
            _ring.Loaded -= AddRing;
            _ring.Unloading -= RemoveRing;
        }
    }

    /// <summary>Frees what was built off the main thread and never added, its wanderers' bodies' too, once the builder has stopped.</summary>
    public void Discard()
    {
        _building.Clear();
        while (_built.TryDequeue(out var built))
            built.Scenery?.Root.Free();
        foreach (var view in _views.Values)
            view.AbandonBuilt();
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
            _views[gone].AbandonBuilt();
            _views[gone].QueueFree();
            _views.Remove(gone);
        }
        foreach (var wanderer in all.Where(w => !_views.ContainsKey(w)))
        {
            // Off the main thread only where it can't be seen arriving: bodies are drawn within 20 of the camera's aim.
            var near = System.Numerics.Vector3.Distance(wanderer.Body.Sphere.Position, Chunks.Focus) < NearBody;
            var view = _views[wanderer] = new WandererView(wanderer, _plugins!, _models!, _shadow, near ? null : _builder);
            AddChild(view);
        }
    }

    /// <summary>
    /// Lights the loaded chunks' actors that are drawn and aren't fullbright by the point lights that reach them this
    /// frame (<see cref="WorldLights.ForActor"/>), each from where its map places it. The ring's have no lights of their own.
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

    /// <summary>Every chunk's scenery drawn: the 3×3's, the ring's, and what stands in while a chunk's builds.</summary>
    private IEnumerable<ChunkScenery> Drawn => _loaded.Values.Concat(_ringLoaded.Values).Concat(_standIns.Values);

    /// <summary>The drawn chunks' billboards.</summary>
    public IEnumerable<Node3D> BillboardFacings => Drawn.SelectMany(c => c.Billboards);

    /// <summary>Shows and fades the drawn chunks' scene objects for this view (<see cref="ChunkScenery.Fade"/>).</summary>
    public void Fade(in SceneView view, ObjectDistance range)
    {
        foreach (var scenery in Drawn)
            scenery.Fade(view, range);
    }

    /// <summary>
    /// Takes a scene object trek put out out of the scenery, one of Oriana's path lights (<see cref="ITrekHost.PutOut"/>),
    /// leaving the rest of its chunk as it is. A chunk still building leaves it out as it's added (<see cref="Flush"/>).
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
    /// detail of those chunks' scene objects that <see cref="Fade"/> last showed. The ring's, which the original doesn't
    /// have, are drawn every frame.
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
        foreach (var scenery in _ringLoaded.Values)
        {
            foreach (var scene in scenery.Scrolling())
                yield return scene;
        }
    }

    /// <summary>Builds a loaded chunk's scenery again, after a container's model changed or an object came or went.</summary>
    public void Rebuild(LoadedMap map)
    {
        if (Chunks.ChunkOf(map) is not { } chunk)
            return;
        if (_building.Remove(map))
        {
            // Still building as it was: it builds again as it is now.
            Queue(chunk, map, ring: false);
            return;
        }
        if (!_loaded.TryGetValue(map, out var old))
            return;
        old.Root.QueueFree();
        _loaded.Remove(map);
        Show(map, Scenery(chunk, map, ring: false), ring: false);
    }

    /// <summary>The actor a loaded chunk's scene object places, if it's placed, animated and built.</summary>
    public ActorAnimator? Animator(LoadedMap map, SceneObject actor) =>
        _loaded.TryGetValue(map, out var scenery) ? scenery.Find(actor.Index)?.Levels.FirstOrDefault()?.Animator : null;

    /// <summary>
    /// Adds what's been built off the main thread to the tree, one chunk a frame, so that what drawing a new chunk first
    /// costs is spread over frames. Leaves out a scene object put out since its build began.
    /// </summary>
    public void Flush()
    {
        foreach (var view in _views.Values)
            view.TakeBuilt();
        while (_built.TryDequeue(out var built))
        {
            if (_building.GetValueOrDefault(built.Map) != built)
            {
                // Gone, or building again, since.
                built.Scenery?.Root.Free();
                continue;
            }
            _building.Remove(built.Map);
            var scenery = built.Scenery ?? Scenery(built.Chunk, built.Map, built.Ring);
            foreach (var placed in scenery.Objects.Where(p => !built.Map.IsActive(p.Object.Index)).ToList())
                scenery.Remove(placed.Object.Index);
            Show(built.Map, scenery, built.Ring);
            return;
        }
    }

    /// <summary>
    /// Lets go of the scenery that stood in for chunks whose new scenery isn't coming: a chunk the 3×3 let go of that the ring
    /// didn't take. Once a frame, after the 3×3 and the ring have moved.
    /// </summary>
    public void Settle()
    {
        if (_standIns.Count == 0)
            return;
        foreach (var key in _standIns.Keys.Where(k => !_building.Keys.Any(m => m.Key == k)).ToList())
        {
            _standIns[key].Root.QueueFree();
            _standIns.Remove(key);
        }
    }

    /// <summary>
    /// Reads ahead, off the main thread, what the next crossing could load: the surfaces and assets of the chunks two from the
    /// centre, which come into the 3×3, and under the ring three from it, which come into the ring, and the actors of their
    /// monster parties' bodies. Each time the centre moves; what was still to read about the old one is dropped.
    /// </summary>
    public void ReadAhead()
    {
        if (_builder is null || !Chunks.All.Any())
            return;
        var ring = _ring?.Active ?? false;
        var around = (Chunks.MapSet.Id, Chunks.Centre, ring);
        if (_readAround == around)
            return;
        _readAround = around;
        _builder.DropReads();
        var centre = Chunks.Centre;
        var set = Chunks.MapSet;
        var plugins = _plugins!;
        var models = _models!;
        var chunks = Chunks;
        var reach = ring ? SceneryRing.Reach + 1 : 2;
        for (var r = centre.Row - reach; r <= centre.Row + reach; r++)
        {
            for (var c = centre.Column - reach; c <= centre.Column + reach; c++)
            {
                var distance = Math.Max(Math.Abs(r - centre.Row), Math.Abs(c - centre.Column));
                if (distance < 2 || set.Chunk(r, c) is not { } chunk)
                    continue;
                var inner = distance == 2;
                _builder.ReadAhead(() =>
                {
                    if (inner)
                        chunks.Prepare(chunk);
                    foreach (var asset in ChunkScenery.Assets(plugins, chunk))
                        models.Warm(asset);
                    if (inner && plugins.TryGet<MapRecord>(chunk.Map) is { } map)
                    {
                        foreach (var party in map.Objects.OfType<MonsterPartyObject>())
                        {
                            if (plugins.TryGet<DbRecord>(party.Entity)?.Body is { } look)
                                models.Warm(look.Actor);
                        }
                    }
                });
            }
        }
    }

    private void Add(ChunkEntry chunk, LoadedMap map)
    {
        if (_builder is null || Near(chunk))
            Show(map, Scenery(chunk, map, ring: false), ring: false);
        else
            Queue(chunk, map, ring: false);
        SyncWanderers();
    }

    private void Remove(ChunkEntry chunk, LoadedMap map)
    {
        _building.Remove(map);
        if (_loaded.Remove(map, out var scenery))
        {
            // The ring may take it in this frame: it's drawn until the ring's is built, or let go of (Settle).
            if (_ring?.Active == true && _builder is not null)
                StandIn(map.Key, scenery);
            else
                scenery.Root.QueueFree();
        }
        SyncWanderers();
    }

    private void AddRing(ChunkEntry chunk, LoadedMap map)
    {
        if (_builder is null)
            Show(map, Scenery(chunk, map, ring: true), ring: true);
        else
            Queue(chunk, map, ring: true);
    }

    private void RemoveRing(ChunkEntry chunk, LoadedMap map)
    {
        _building.Remove(map);
        if (!_ringLoaded.Remove(map, out var scenery))
            return;
        // Into the 3×3 or gated again, it's drawn until its new scenery is built, unless that's drawn already; out of the 5×5,
        // it's let go of once the ring has moved (Settle).
        if (_loaded.Keys.Any(m => m.Key == map.Key))
            scenery.Root.QueueFree();
        else
            StandIn(map.Key, scenery);
    }

    /// <summary>Whether a chunk is near enough to where the 3×3 was loaded about to be built at once.</summary>
    private bool Near(ChunkEntry chunk)
    {
        var size = Chunks.MapSet.CellSize;
        var corner = Chunks.Origin(chunk);
        var focus = Chunks.Focus;
        var dx = Math.Max(Math.Max(corner.X - focus.X, focus.X - (corner.X + size)), 0);
        var dz = Math.Max(Math.Max(corner.Z - focus.Z, focus.Z - (corner.Z + size)), 0);
        return MathF.Sqrt(dx * dx + dz * dz) < NearBuild * size;
    }

    /// <summary>Builds a chunk's scenery off the main thread, by its objects as they stand now.</summary>
    private void Queue(ChunkEntry chunk, LoadedMap map, bool ring)
    {
        var shown = map.Record.Objects.OfType<SceneObject>().ToDictionary(o => o.Index, o => (Active: map.IsActive(o.Index), Look: (int)map.Look(o.Index)));
        var building = _building[map] = new Building(chunk, map, ring);
        var plugins = _plugins!;
        var models = _models!;
        var set = Chunks.MapSet;
        _builder!.Build(() =>
        {
            try
            {
                building.Scenery = ChunkScenery.Load(plugins, models, set, chunk, o => shown[o.Index].Active, o => shown[o.Index].Look,
                    particles: !ring);
            }
            finally
            {
                // Built or not: without scenery, the main thread builds it.
                _built.Enqueue(building);
            }
        });
    }

    private void Show(LoadedMap map, ChunkScenery scenery, bool ring)
    {
        (ring ? _ringLoaded : _loaded)[map] = scenery;
        AddChild(scenery.Root);
        if (_standIns.Remove(map.Key, out var standIn))
            standIn.Root.QueueFree();
    }

    private void StandIn(ChunkKey key, ChunkScenery scenery)
    {
        if (_standIns.Remove(key, out var older))
            older.Root.QueueFree();
        _standIns[key] = scenery;
    }

    // Whether a scene object is there is decided as its chunk loads, not every frame. The runtime
    // plays with the Expansion Pak. The ring's actors start no emitters: they share the particles' pool with
    // what's near, and are drawn only within their levels of detail, nearer than the ring.
    private ChunkScenery Scenery(ChunkEntry chunk, LoadedMap map, bool ring) =>
        ChunkScenery.Load(_plugins!, _models!, Chunks.MapSet, chunk, o => map.IsActive(o.Index), o => (int)map.Look(o.Index), particles: !ring);

    /// <summary>A chunk's scenery building off the main thread, and once built, what it came to (null if it failed).</summary>
    private sealed class Building(ChunkEntry chunk, LoadedMap map, bool ring)
    {
        public ChunkEntry Chunk { get; } = chunk;
        public LoadedMap Map { get; } = map;
        public bool Ring { get; } = ring;
        public ChunkScenery? Scenery { get; set; }
    }
}
