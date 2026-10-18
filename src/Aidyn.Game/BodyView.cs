using Aidyn.Core.Actors;
using Aidyn.Core.Chunks;
using Aidyn.Core.Lights;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
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

    /// <summary>
    /// Makes the view of <paramref name="body"/>, which looks like <paramref name="look"/>. A derived view calls
    /// <see cref="Sync"/> when it has made what it adds.
    /// </summary>
    /// <param name="shadow">The shadow texture, or null for a body with no shadow.</param>
    protected BodyView(string name, Body body, EntityBody look, PluginSet plugins, Models models, Texture2D? shadow)
    {
        Body = body;
        Name = name;
        if (plugins.TryGet<ActorRecord>(look.Actor) is { } actor && models.InstantiateActor(actor, rolls: models.BodyRolls) is var (node, animator))
        {
            _turn = new Node3D { Scale = Vector3.One * body.Scale };
            _turn.AddChild(node);
            AddChild(_turn);
            ShadowDecal.MarkBody(node);
            _model = new Level(node, null);
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
    /// Lights the body by the point lights that reach its centre this frame (<see cref="WorldLights.ForBody"/>), as a
    /// body that isn't one of the party's (<c>+0x70a</c>), so no light map object reaches it.
    /// </summary>
    public void Light(WorldLights lights, ChunkStreamer chunks)
    {
        if (_model is null)
            return;
        var centre = Body.Sphere.Position;
        N64Material.SetLights(_model.Meshes, lights.ForBody(centre, chunks.MapAt(centre), party: false));
    }

    /// <summary>
    /// Fades the body by its distance from the camera's aim (<c>PlayerShadowAlpha</c>), hiding it when
    /// <paramref name="hidden"/>. Returns its alpha before that, and its distance from the aim.
    /// </summary>
    protected (byte Alpha, float FromAim) Fade(System.Numerics.Vector3 aim, bool hidden = false)
    {
        var fromAim = System.Numerics.Vector3.Distance(aim, Body.Sphere.Position);
        var alpha = BodyShadow.BodyAlpha(BodyShadow.TrekDistance, fromAim) ?? 0;
        _model?.Show(hidden ? (byte)0 : alpha);
        return (alpha, fromAim);
    }

    /// <summary>Places its shadow with the alpha <see cref="BodyShadow.DrawAlpha"/> gave, or hides it for none.</summary>
    protected void PlaceShadow(CollisionWorld world, byte? alpha) => _shadow?.Place(world, Body.Sphere, alpha);
}
