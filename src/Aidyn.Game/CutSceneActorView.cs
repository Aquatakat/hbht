using Aidyn.Core;
using Aidyn.Core.Movement;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a cut-scene's actor (<c>renderPlayers</c>): its entity's actor at its scale, standing at its
/// feet and facing its heading, faded by its distance from the camera's aim, with its shadow and the
/// item it holds. The core steps its animations as the body moves.
/// </summary>
public partial class CutSceneActorView : Node3D
{
    private readonly Models? _models;
    private readonly Level? _model;
    private readonly Node3D? _turn;
    private readonly Node3D? _hand;
    private readonly ShadowDecal? _shadow;
    private RecordId? _heldScene;
    private Node3D? _held;

    public CutSceneActorView(CutSceneActor actor, PluginSet plugins, Models models, Texture2D? shadow)
    {
        Actor = actor;
        _models = models;
        Name = $"Actor {actor.Entity}";
        if (plugins.TryGet<ActorRecord>(actor.Look.Actor) is { } record && models.InstantiateActor(record) is var (node, animator))
        {
            _turn = new Node3D { Scale = Vector3.One * actor.Body.Scale };
            _turn.AddChild(node);
            AddChild(_turn);
            ShadowDecal.MarkBody(node);
            _model = new Level(node, null);
            // What it holds goes on its scene's locator 0 (AttachItemToPlayer's slot 0).
            _hand = Nodes.FindAll<Node3D>(node).FirstOrDefault(n => n.Name == "locator 0");
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
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
        Sync();
    }

    public CutSceneActorView()
    {
        Actor = null!;
    }

    public CutSceneActor Actor { get; }

    /// <summary>Its animations, which the core ticks as the body steps.</summary>
    public IActorMotion Motion { get; } = new Still();

    /// <summary>Moves the node to where the body is, and puts what it holds in its hand. Call after each tick.</summary>
    public void Sync()
    {
        var body = Actor.Body;
        Position = (body.Sphere.Position - new System.Numerics.Vector3(0, body.Sphere.Radius, 0)).ToGodot();
        if (_turn is not null)
            _turn.Rotation = new Vector3(0, Mathf.Atan2(body.Heading.X, body.Heading.Y), 0);
        if (_heldScene != Actor.Held)
        {
            _held?.QueueFree();
            _held = null;
            _heldScene = Actor.Held;
            if (Actor.Held is { } scene && _hand is not null && _models!.Instantiate(scene) is { } item)
            {
                N64Material.SetLighting(item, N64Material.Lighting.Body);
                ShadowDecal.MarkBody(item);
                _hand.AddChild(item);
                _held = item;
            }
        }
    }

    /// <summary>
    /// Fades it by its distance from the camera's aim (<c>PlayerShadowAlpha</c>), hides it while the camera
    /// looks out of its eyes, and places its shadow: Alaron's as the player's, others' within
    /// <see cref="BodyShadow.OthersDistance"/> of the aim.
    /// </summary>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim)
    {
        var sphere = Actor.Body.Sphere;
        var fromAim = System.Numerics.Vector3.Distance(aim, sphere.Position);
        var alpha = BodyShadow.BodyAlpha(BodyShadow.TrekDistance, fromAim) ?? 0;
        _model?.Show(Actor.Hidden ? (byte)0 : alpha);
        if (_held is not null)
            _held.Visible = alpha != 0;
        if (_shadow is null)
            return;
        var shadow = BodyShadow.DrawAlpha(BodyShadow.TrekDistance, fromAim, player: Actor.Entity == GameState.Alaron);
        if (shadow is { } a)
            _shadow.Shadow.Place(world, sphere.Position, sphere.Radius, a);
        _shadow.Sync();
        _shadow.Visible &= shadow is not null;
    }

    private sealed class Still : IActorMotion
    {
        public int StateLength => 1;

        public float Tick(int request) => 0;
    }
}
