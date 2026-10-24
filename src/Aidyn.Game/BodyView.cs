using Aidyn.Core.Actors;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Lights;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a body that isn't the player (<c>renderPlayers</c>): its entity's actor at its scale, standing at
/// the body's feet and facing its heading, faded by its distance from the camera's aim, lit by the point lights
/// that reach it, with its shadow. The core steps its animations with the body, as it does the player's.
/// A wanderer's view and a cut-scene actor's derive from it and add what is theirs.
/// </summary>
public abstract partial class BodyView : Node3D
{
    private readonly Level? _model;
    private readonly Node3D? _turn;
    private readonly ShadowDecal? _shadow;
    private readonly Models? _models;

    /// <summary>What it holds in each attachment slot, the model drawn for it, and its billboards' facing nodes (body <c>+0x730</c>).</summary>
    private readonly (HeldModel Held, Level Level, List<Node3D> Facings)?[] _held = new (HeldModel, Level, List<Node3D>)?[3];

    /// <summary>The meshes of its actor and of what it holds, made again as what it holds changes.</summary>
    private GeometryInstance3D[]? _meshes;

    /// <summary>
    /// Makes the view of <paramref name="body"/>, which looks like <paramref name="look"/>. A derived view calls
    /// <see cref="Sync"/> when it has made what it adds.
    /// </summary>
    /// <param name="shadow">The shadow texture, or null for a body with no shadow.</param>
    protected BodyView(string name, Body body, EntityBody look, PluginSet plugins, Models models, Texture2D? shadow)
    {
        Body = body;
        Name = name;
        _models = models;
        if (plugins.TryGet<ActorRecord>(look.Actor) is { } actor && models.InstantiateActor(actor, rolls: models.BodyRolls) is var (node, animator))
        {
            _turn = new Node3D { Scale = Vector3.One * body.Scale };
            _turn.AddChild(node);
            AddChild(_turn);
            ShadowDecal.MarkBody(node);
            // Its emitters' particles show and fade with it (Level.Show).
            _model = new Level(node, null) { Animator = animator };
            if (animator is not null)
            {
                animator.Driven = true;
                Motion = animator;
            }
        }
        if (shadow is not null)
        {
            _shadow = new ShadowDecal(shadow);
            AddChild(_shadow);
            _shadow.TopLevel = true;
        }
        // It moves with the physics tick, unlike the chunks' scenery it's under.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
    }

    protected BodyView()
    {
        Body = null!;
    }

    public Body Body { get; }

    /// <summary>Its animations, which the core ticks as the body steps.</summary>
    public IActorMotion Motion { get; } = IActorMotion.Still;

    /// <summary>The actor's scene, or null for a body whose entity has no actor.</summary>
    protected Node3D? Model => _model?.Node;

    /// <summary>Moves the node to where the body is, and turns the model to its heading. Call after each tick.</summary>
    public virtual void Sync()
    {
        Position = (Body.Sphere.Position - new System.Numerics.Vector3(0, Body.Sphere.Radius, 0)).ToGodot();
        if (_turn is not null)
            _turn.Rotation = new Vector3(0, Mathf.Atan2(Body.Heading.X, Body.Heading.Y), 0);
    }

    /// <summary>
    /// Lights the body by the moon and the point lights that reach its centre this frame (<see cref="WorldLights.ForBody"/>): by
    /// default as a body that isn't one of the party's (<c>+0x70a</c>), so no light map object reaches it; combat's party members
    /// are the party's. <paramref name="tint"/> is its tint's level, which the moon's light is scaled by: 0 but in combat.
    /// </summary>
    public void Light(WorldLights lights, ChunkStreamer chunks, bool party = false, float tint = 0)
    {
        if (_model is null)
            return;
        var centre = Body.Sphere.Position;
        N64Material.SetLights(Meshes, lights.ForBody(centre, chunks.MapAt(centre), party, tint));
    }

    /// <summary>The meshes of its actor's scene and of what it holds, none for a body without an actor.</summary>
    protected IReadOnlyList<GeometryInstance3D> Meshes =>
        _meshes ??= _model is null ? [] : [.. _model.Meshes, .. _held.Where(h => h is not null).SelectMany(h => h!.Value.Level.Meshes)];

    /// <summary>
    /// Puts in its actor's hands what it holds, slot by slot (<c>RenderPlayers</c>, <c>0x80013DCC</c>): each model on its locator's
    /// attachment node, turned by the locator model's rotation and moved and scaled with the transform it's on as the animation
    /// poses it, lit and faded as the body. A locator the actor doesn't have draws nothing, as the original's
    /// <c>Scene.HasLocator</c> skips it. Each held scene has its own copy of its scrolling textures, which <see cref="ScrollHeld"/>
    /// moves on, and its billboards, which <see cref="FaceHeld"/> turns to the camera, as the Troubadour's notes need. Call after each tick.
    /// </summary>
    protected void Hold(IReadOnlyList<HeldModel?> held)
    {
        for (var slot = 0; slot < _held.Length; slot++)
        {
            var want = slot < held.Count ? held[slot] : null;
            if (_held[slot]?.Held != want)
            {
                if (_held[slot] is { } old)
                    old.Level.Node.QueueFree();
                _held[slot] = null;
                _meshes = null;
                if (want is { } h && Attachment(h.Locator) is { } parent && _models!.Instantiate(h.Scene) is { } item)
                {
                    N64Material.SetLighting(item, N64Material.Lighting.Body);
                    ShadowDecal.MarkBody(item);
                    parent.AddChild(item);
                    var level = new Level(item, _models.Scrolls.Attach(h.Scene, item));
                    level.Show(_model!.Alpha);
                    _held[slot] = (h, level, Billboards.Facings(item));
                }
            }
            // Only the Troubadour's notes are raised, in the world's Y, after they're placed on their locator.
            if (_held[slot] is ({ Raise: not 0 } raised, var shown, _) && shown.Node.IsInsideTree() && shown.Node.GetParent() is Node3D on)
                shown.Node.GlobalPosition = on.GlobalPosition + new Vector3(0, raised.Raise, 0);
        }
    }

    /// <summary>
    /// Turns the billboards of what it holds to face <paramref name="eye"/>, as <c>Scene.Draw</c> draws an attachment as it does any scene
    /// (<c>BorgAnimDrawSceneRaw</c>): at the billboard's place, sized by its own scale alone, as <c>RenderPlayers</c> leaves the scene's
    /// scalar at 1 and the body's scale only moves where it's placed. Call after <see cref="Hold"/>.
    /// </summary>
    public void FaceHeld(Vector3 eye)
    {
        foreach (var held in _held)
        {
            if (held is { Facings.Count: > 0 } h && h.Level.Node.IsInsideTree())
                Billboards.Face(h.Facings, eye, sceneScale: Vector3.One);
        }
    }

    /// <summary>One draw of what it holds, which moves its scrolling textures on (<c>Scene.LoadTextureOp</c>), while it's shown.</summary>
    public void ScrollHeld()
    {
        foreach (var held in _held)
        {
            if (held is { } h && h.Level.Node.Visible)
                h.Level.Scrolling?.Draw();
        }
    }

    /// <summary>
    /// The prim colour of what it holds (<c>RenderPlayers</c>' <c>Scene.SetTint</c>, <c>0x80014254</c>): the scenery's tint, in on-screen
    /// values, where the body's own scene is white. The screen's fade, which the original multiplies in too, is the runtime's overlay.
    /// </summary>
    public void TintHeld(Color scenery)
    {
        foreach (var held in _held)
        {
            if (held is { } h)
                N64Material.SetSceneTint(h.Level.Node, scenery);
        }
    }

    /// <summary>
    /// The node what's held at locator <paramref name="locator"/> hangs from: the body's actor's own, not its head's, whose scene hangs
    /// from another locator. A plugin from before importer 51 has only the locator's node, which leaves out the locator model's rotation.
    /// </summary>
    private Node3D? Attachment(int locator)
    {
        if (Model is not { } model)
            return null;
        var nodes = Nodes.FindAll<Node3D>(model);
        return Own(SceneNames.Attachment(locator)) ?? Own(SceneNames.Locator(locator));

        Node3D? Own(string name) => nodes.FirstOrDefault(n => n.Name == name && LocatorsAbove(n, model) <= 1);
    }

    /// <summary>How many locators <paramref name="node"/> is or is under, up to <paramref name="root"/>: more than one is in a scene hung from another.</summary>
    private static int LocatorsAbove(Node node, Node root)
    {
        var count = 0;
        for (var n = node; n is not null && n != root; n = n.GetParent())
        {
            if (SceneNames.TryLocator(n.Name.ToString(), out _))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Fades the body by its distance from the camera's aim (<c>PlayerShadowAlpha</c>), over trek's distance or
    /// <paramref name="distance"/>, capped at its own alpha, <paramref name="bodyAlpha"/> (<c>+0x70C</c>), and hiding it when
    /// <paramref name="hidden"/>. Returns that alpha, and its distance from the aim.
    /// </summary>
    protected (byte Alpha, float FromAim) Fade(System.Numerics.Vector3 aim, bool hidden = false, float distance = BodyShadow.TrekDistance, byte bodyAlpha = 0xFF)
    {
        var fromAim = System.Numerics.Vector3.Distance(aim, Body.Sphere.Position);
        var alpha = BodyShadow.BodyAlpha(distance, fromAim, bodyAlpha) ?? 0;
        var shown = hidden ? (byte)0 : alpha;
        _model?.Show(shown);
        foreach (var held in _held)
            held?.Level.Show(shown);
        return (alpha, fromAim);
    }

    /// <summary>Places its shadow with the alpha <see cref="BodyShadow.DrawAlpha"/> gave, or hides it for none.</summary>
    protected void PlaceShadow(CollisionWorld world, byte? alpha) => _shadow?.Place(world, Body.Sphere, alpha);
}
