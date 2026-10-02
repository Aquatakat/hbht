using Aidyn.Core.Movement;
using Aidyn.Core.Trek;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a wanderer's body: its entity's actor at its scale, standing at the body's feet and facing
/// its heading, faded by its distance from the camera's aim (<c>renderPlayers</c>), with its shadow.
/// The core steps its animations with the body, as it does the player's.
/// </summary>
public partial class WandererView : Node3D
{
    private readonly Level? _model;
    private readonly Node3D? _turn;
    private readonly ShadowDecal? _shadow;

    public WandererView(Wanderer wanderer, Aidyn.Plugins.PluginSet plugins, Models models, Texture2D? shadow)
    {
        Wanderer = wanderer;
        Name = $"Wanderer {wanderer.Party.Index}";
        if (plugins.TryGet<Aidyn.Plugins.Records.ActorRecord>(wanderer.Look.Actor) is { } actor
            && models.InstantiateActor(actor) is var (node, animator))
        {
            _turn = new Node3D { Scale = Vector3.One * wanderer.Body.Scale };
            _turn.AddChild(node);
            AddChild(_turn);
            ShadowDecal.MarkBody(node);
            _model = new Level(node, null);
            if (animator is not null)
            {
                animator.Driven = true;
                Animation = animator;
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
        Sync();
    }

    public WandererView()
    {
        Wanderer = null!;
    }

    public Wanderer Wanderer { get; }

    /// <summary>Its animations, which the core ticks as the body steps.</summary>
    public IRootMotion Animation { get; } = new Still();

    /// <summary>Moves the node to where the body is. Call after each tick.</summary>
    public void Sync()
    {
        var body = Wanderer.Body;
        Position = (body.Sphere.Position - new System.Numerics.Vector3(0, body.Sphere.Radius, 0)).ToGodot();
        if (_turn is not null)
            _turn.Rotation = new Vector3(0, Mathf.Atan2(body.Heading.X, body.Heading.Y), 0);
    }

    /// <summary>
    /// Fades the body by its distance from the camera's aim (<c>PlayerShadowAlpha</c>), and places its
    /// shadow, which shows only within <see cref="BodyShadow.OthersDistance"/> of the aim.
    /// </summary>
    /// <param name="aim">The camera's aim.</param>
    /// <param name="ownAim">
    /// The aim as the original measures a shadow from it: in the player's chunk's coordinates, against
    /// the body's position in its own chunk's. For a body in another chunk that's off by the chunks
    /// between them, so its shadow seldom shows.
    /// </param>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim, System.Numerics.Vector3 ownAim)
    {
        var sphere = Wanderer.Body.Sphere;
        var fromAim = System.Numerics.Vector3.Distance(aim, sphere.Position);
        _model?.Show(BodyShadow.BodyAlpha(BodyShadow.TrekDistance, fromAim) ?? 0);
        if (_shadow is null)
            return;
        var local = sphere.Position - Wanderer.Map.Origin;
        var alpha = BodyShadow.DrawAlpha(BodyShadow.TrekDistance, fromAim, player: false, ownAim: System.Numerics.Vector3.Distance(local, ownAim));
        if (alpha is { } a)
            _shadow.Shadow.Place(world, sphere.Position, sphere.Radius, a);
        _shadow.Sync();
        _shadow.Visible &= alpha is not null;
    }

    private sealed class Still : IRootMotion
    {
        public float Tick(int request) => 0;
    }
}
