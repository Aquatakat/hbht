using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Views;

/// <summary>
/// Scene assets as Godot nodes. Each glTF is parsed once (<see cref="GlbScene"/>, for its extras and actor clips, and by
/// Godot, for its nodes), and every use gets a copy, so a forest of one tree costs one parse.
/// </summary>
/// <remarks>
/// <para>Any thread may use it, as the runtime builds chunks' scenery off the main thread and parses what's coming ahead of time
/// (<see cref="Warm"/>; docs/adr/0025-chunks-load-off-the-main-thread.md). Each asset is parsed once, by whichever thread asks
/// first, but the main thread never waits for another to finish one: Godot's renderer serves other threads' calls on the main
/// thread, between frames, so a thread parsing a scene can be waiting on the main thread, and the main thread waiting on it
/// would never go on. It parses a copy of its own instead, which happens only when both want the same asset at once. Its nodes
/// are made outside the tree, which only the main thread may add them to.</para>
/// <para>A parsed scene is kept packed, and each copy is instantiated from it. Copying its nodes (<c>Node.Duplicate</c>) read every
/// node's property list, and a mesh's asks the renderer for its instance shader parameters and their defaults, each a call that
/// waits on the main thread from another thread: a chunk's scenery built off the main thread took as many frames as it made
/// copies of meshes (runtime.md#loading-chunks). An instantiation only sets the properties packed, which waits on nothing.</para>
/// </remarks>
public sealed class Models(PluginSet plugins) : IDisposable
{
    private readonly Dictionary<RecordId, Template> _templates = [];
    private readonly Dictionary<RecordId, Lazy<GlbScene?>> _files = [];
    private readonly int _mainThread = System.Environment.CurrentManagedThreadId;

    /// <summary>The particles actors' emitters start in, if any are drawn.</summary>
    public ParticlesView? Particles { get; set; }

    /// <summary>
    /// The stream bodies' animations roll their branches on, one for every body as the original's is
    /// (<see cref="Core.Rng.RandomStreams.Animation"/>). Other actors each roll on a stream of their own.
    /// </summary>
    public Core.Rng.IRandom BodyRolls { get; init; } = new Core.Rng.Lcg(0);

    /// <summary>The scenes' scrolling textures, which each copy of a scene has its own of.</summary>
    public TextureScrolls Scrolls { get; } = new();

    /// <summary>A new copy of the scene's nodes, or null if no plugin has its asset.</summary>
    public Node3D? Instantiate(RecordId scene)
    {
        // Copies of one template may be made on several threads at once: making one only reads it.
        return Parsed(scene)?.Instantiate<Node3D>();
    }

    /// <summary>
    /// Parses a scene's asset ahead of its first use, from any thread, so that copying it later costs only the copy. An
    /// actor's head comes with it.
    /// </summary>
    public void Warm(RecordId scene)
    {
        if (scene.Type == RecordTypes.Actor)
        {
            if (plugins.TryGet<ActorRecord>(scene) is not { } actor)
                return;
            Warm(actor.Scene);
            if (actor.Head is { } head)
                Warm(head.Scene);
            return;
        }
        Parsed(scene);
    }

    /// <summary>The scene's parsed nodes, packed to be copied, or null without an asset.</summary>
    private PackedScene? Parsed(RecordId scene)
    {
        Template? template;
        bool parses;
        lock (_templates)
        {
            parses = !_templates.TryGetValue(scene, out template);
            if (parses)
                _templates[scene] = template = new Template();
        }
        if (parses)
        {
            PackedScene? packed = null;
            try
            {
                packed = Pack(Load(scene));
            }
            finally
            {
                template!.Finish(packed);
            }
            return packed;
        }
        if (template!.Done)
            return template.Scene;
        if (System.Environment.CurrentManagedThreadId != _mainThread)
            return template.Wait();
        // Another thread is parsing it, and may be waiting on the main thread's renderer: parse a copy rather than wait.
        return Pack(Load(scene));
    }

    /// <summary>Packs a scene's parsed nodes, every one of them, and frees them, as only the packed scene is copied.</summary>
    private static PackedScene? Pack(Node3D? node)
    {
        if (node is null)
            return null;
        foreach (var n in Nodes.FindAll<Node>(node))
        {
            if (n != node)
                n.Owner = node;
        }
        var packed = new PackedScene();
        var error = packed.Pack(node);
        var name = node.Name;
        node.Free();
        if (error == Error.Ok)
            return packed;
        GD.PushWarning($"Godot couldn't pack {name}: {error}.");
        return null;
    }

    /// <summary>A scene's packed nodes, once whichever thread parses them has.</summary>
    private sealed class Template
    {
        private volatile bool _done;

        public bool Done => _done;

        public PackedScene? Scene { get; private set; }

        public void Finish(PackedScene? scene)
        {
            lock (this)
            {
                Scene = scene;
                _done = true;
                Monitor.PulseAll(this);
            }
        }

        public PackedScene? Wait()
        {
            lock (this)
            {
                while (!_done)
                    Monitor.Wait(this);
                return Scene;
            }
        }
    }

    /// <summary>
    /// An actor's scene with its head, if it has one, and an animator running its state machine.
    /// </summary>
    /// <param name="lighting">How it's lit: as a body, unless it's a scene object's.</param>
    /// <param name="rolls">The stream its branches roll on: a body's are <see cref="BodyRolls"/>; by default its own, from 0.</param>
    /// <param name="particles">Whether its emitters start in <see cref="Particles"/>.</param>
    /// <param name="motion">
    /// The motion its animator plays, made before it (<see cref="ActorAnimator.MotionFor"/>), in place of one rolling on
    /// <paramref name="rolls"/>; for an animated actor's (<see cref="ActorAnimator.Animated"/>).
    /// </param>
    /// <param name="recorded">Whether it draws from a battle's recording, with emitters of its own (<see cref="ActorAnimator.Recorded"/>).</param>
    public (Node3D Node, ActorAnimator? Animator)? InstantiateActor(ActorRecord actor, N64Material.Lighting lighting = N64Material.Lighting.Body,
        Core.Rng.IRandom? rolls = null, bool particles = true, Core.Actors.ActorMotion? motion = null, bool recorded = false)
    {
        if (Instantiate(actor.Scene) is not { } node)
            return null;
        if (actor.Head is { } head && Instantiate(head.Scene) is { } headNode)
        {
            // The original aligns the head with the locator but keeps it upright, and turns it
            // toward things the player could use. Parenting it follows the neck, which is close.
            var locatorName = SceneNames.Locator(head.Locator);
            if (Nodes.FindAll<Node3D>(node).FirstOrDefault(n => n.Name == locatorName) is { } locator)
                locator.AddChild(headNode);
            else
                GD.PushWarning($"{actor.Scene} has no {locatorName} for its head.");
        }
        N64Material.SetLighting(node, lighting);
        var animator = motion is not null
            ? ActorAnimator.Create(node, motion, Glb(actor.Scene)!)
            : ActorAnimator.Create(node, actor, Glb(actor.Scene)!, rolls ?? new Core.Rng.Lcg(0));
        if (animator is not null)
        {
            node.AddChild(new FrameCallback(animator.Update) { Name = "ActorAnimator" });
            animator.Recorded = recorded;
            if (particles)
                animator.Particles = Particles?.For(actor, node);
        }
        return (node, animator);
    }

    /// <summary>The asset's glTF, for what Godot's importer doesn't keep, such as node extras.</summary>
    public GlbScene? Glb(RecordId scene)
    {
        Lazy<GlbScene?>? file;
        lock (_files)
        {
            if (!_files.TryGetValue(scene, out file))
            {
                _files[scene] = file = new Lazy<GlbScene?>(
                    () => plugins.FindAsset(scene, "glb") is { } path ? GlbScene.Parse(File.ReadAllBytes(path)) : null,
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }
        }
        return file.Value;
    }

    /// <summary>Lets go of the packed templates. Nothing may be building from them.</summary>
    public void Dispose()
    {
        lock (_templates)
        {
            foreach (var template in _templates.Values.Where(t => t.Done))
                template.Scene?.Dispose();
            _templates.Clear();
        }
        lock (_files)
        {
            foreach (var file in _files.Values.Where(f => f.IsValueCreated))
                file.Value?.Dispose();
            _files.Clear();
        }
    }

    private Node3D? Load(RecordId scene)
    {
        if (Glb(scene) is not { } glb)
        {
            GD.PushWarning($"No asset for {scene}.");
            return null;
        }
        // A scene with no geometry has no binary chunk, which Godot's reader warns about: its nodes are built from the glTF, as a
        // particle-only actor's locators place its emitters.
        if (!glb.HasBinaryChunk)
            return GlbFile.LoadNodes(glb, scene.ToString().Replace(':', '_').Replace('/', '_'));
        if (GlbFile.Load(glb, out _) is not { } node)
        {
            GD.PushWarning($"Godot couldn't load {scene}'s glTF.");
            return null;
        }
        node.Name = scene.ToString().Replace(':', '_').Replace('/', '_');
        Scrolls.Add(scene, glb.Extras);
        // Billboards are turned to the camera in world space, whatever their parents do.
        foreach (var facing in Billboards.Facings(node))
            facing.TopLevel = true;
        return node;
    }
}
