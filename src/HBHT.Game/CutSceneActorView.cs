using HBHT.Core;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using Godot;

namespace HBHT.Game;

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
    public CutSceneActorView(CutSceneActor actor, PluginSet plugins, Models models, Texture2D? shadow)
        : base($"Actor {actor.Entity}", actor.Body, actor.Look, plugins, models, shadow)
    {
        Actor = actor;
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
        // What it holds goes on its scene's locator 0 (AttachItemToPlayer's slot 0).
        Hold(Actor.Held is { } scene ? [new HeldModel(scene, 0)] : []);
    }

    /// <summary>
    /// Fades it by its distance from the camera's aim (<c>PlayerShadowAlpha</c>), hides it while the camera
    /// looks out of its eyes, and places its shadow: Alaron's as the player's, others' within
    /// <see cref="BodyShadow.OthersDistance"/> of the aim.
    /// </summary>
    /// <param name="distance">How far from the aim it's drawn (<see cref="Core.Scenes.ObjectDistance.Actors"/>).</param>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim, float distance)
    {
        var (_, fromAim) = Fade(aim, Actor.Hidden, distance);
        PlaceShadow(world, BodyShadow.DrawAlpha(distance, fromAim, player: Actor.Entity == GameState.Alaron));
    }
}
