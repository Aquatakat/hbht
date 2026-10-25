using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// Scene assets as Godot nodes. Each glTF is parsed once (<see cref="GlbScene"/>, for its extras and actor clips, and by
/// Godot, for its nodes), and every use gets a copy, so a forest of one tree costs one parse.
/// </summary>
/// <remarks>
/// Any thread may use it, as the runtime builds chunks' scenery off the main thread and parses what's coming ahead of time
/// (<see cref="Warm"/>; docs/adr/0025-chunks-load-off-the-main-thread.md). Each asset is parsed once, by whichever thread asks
/// first, but the main thread never waits for another to finish one: Godot's renderer serves other threads' calls on the main
/// thread, between frames, so a thread parsing a scene can be waiting on the main thread, and the main thread waiting on it
/// would never go on. It parses a copy of its own instead, which happens only when both want the same asset at once. Its nodes
/// are made outside the tree, which only the main thread may add them to.
/// </remarks>
public sealed class Models(PluginSet plugins) : IDisposable
{
    private readonly Dictionary<RecordId, Template> _templates = [];
    private readonly Dictionary<RecordId, Lazy<GlbScene?>> _files = [];
    /// <summary>The main thread's own copies of templates another thread was parsing, freed with the rest.</summary>
    private readonly List<Node3D> _ownCopies = [];
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
        return Parsed(scene)?.Duplicate() as Node3D;
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

    /// <summary>The scene's parsed nodes, which are never in the tree and are only ever copied, or null without an asset.</summary>
    private Node3D? Parsed(RecordId scene)
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
            Node3D? node = null;
            try
            {
                node = Load(scene);
            }
            finally
            {
                template!.Finish(node);
            }
            return node;
        }
        if (template!.Done)
            return template.Node;
        if (System.Environment.CurrentManagedThreadId != _mainThread)
            return template.Wait();
        // Another thread is parsing it, and may be waiting on the main thread's renderer: parse a copy rather than wait.
        var own = Load(scene);
        if (own is not null)
        {
            lock (_ownCopies)
                _ownCopies.Add(own);
        }
        return own;
    }

    /// <summary>A scene's parsed nodes, once whichever thread parses them has.</summary>
    private sealed class Template
    {
        private volatile bool _done;

        public bool Done => _done;

        public Node3D? Node { get; private set; }

        public void Finish(Node3D? node)
        {
            lock (this)
            {
                Node = node;
                _done = true;
                Monitor.PulseAll(this);
            }
        }

        public Node3D? Wait()
        {
            lock (this)
            {
                while (!_done)
                    Monitor.Wait(this);
                return Node;
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
    public (Node3D Node, ActorAnimator? Animator)? InstantiateActor(ActorRecord actor, N64Material.Lighting lighting = N64Material.Lighting.Body,
        Core.Rng.IRandom? rolls = null, bool particles = true, Core.Actors.ActorMotion? motion = null)
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

    /// <summary>Frees the parsed templates, which are never in the tree. Nothing may be building from them.</summary>
    public void Dispose()
    {
        lock (_templates)
        {
            foreach (var template in _templates.Values.Where(t => t.Done))
                template.Node?.Free();
            _templates.Clear();
        }
        lock (_ownCopies)
        {
            foreach (var own in _ownCopies)
                own.Free();
            _ownCopies.Clear();
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
        // A scene with no geometry has no binary chunk, which Godot's reader warns about.
        if (!glb.HasBinaryChunk)
            return new Node3D { Name = scene.ToString().Replace(':', '_').Replace('/', '_') };
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
