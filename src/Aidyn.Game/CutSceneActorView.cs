using Aidyn.Core;
using Aidyn.Core.Movement;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a cut-scene's actor (<c>renderPlayers</c>): its entity's actor at its scale, standing at its
/// feet and facing its heading, faded by its distance from the camera's aim, with its shadow and the
/// item it holds. The core steps its animations as the body moves.
/// </summary>
/// <remarks>
/// It's lit as a body is (<c>RenderPlayers</c>, <c>0x800140f0</c>, through <c>Bodies.SetLights</c>, <c>0x80013798</c>):
/// after the sun, the dynamic lights. No cut-scene actor is one of the party's (<c>+0x70a</c>, which only trek's
/// player and combat's party have), Alaron among them, so the stage's light map objects don't reach it.
/// </remarks>
public partial class CutSceneActorView : BodyView
{
    private readonly Models? _models;
    private readonly Node3D? _hand;
    private RecordId? _heldScene;
    private Node3D? _held;

    public CutSceneActorView(CutSceneActor actor, PluginSet plugins, Models models, Texture2D? shadow)
        : base($"Actor {actor.Entity}", actor.Body, actor.Look, plugins, models, shadow)
    {
        Actor = actor;
        _models = models;
        // What it holds goes on its scene's locator 0 (AttachItemToPlayer's slot 0).
        _hand = Model is { } model ? Nodes.FindAll<Node3D>(model).FirstOrDefault(n => n.Name == SceneNames.Locator(0)) : null;
        Sync();
    }

    public CutSceneActorView()
    {
        Actor = null!;
    }

    public CutSceneActor Actor { get; }

    /// <summary>Moves the node to where the body is, and puts what it holds in its hand. Call after each tick.</summary>
    public override void Sync()
    {
        base.Sync();
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
        var (alpha, fromAim) = Fade(aim, Actor.Hidden);
        if (_held is not null)
            _held.Visible = alpha != 0;
        PlaceShadow(world, BodyShadow.DrawAlpha(BodyShadow.TrekDistance, fromAim, player: Actor.Entity == GameState.Alaron));
    }
}
