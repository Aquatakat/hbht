using HBHT.Core.Movement;
using HBHT.Core.Trek;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws a wanderer's body: its entity's actor at its scale, standing at the body's feet and facing
/// its heading, faded by its distance from the camera's aim (<c>renderPlayers</c>), with its shadow.
/// The core steps its animations with the body, as it does the player's.
/// </summary>
public partial class WandererView : BodyView
{
    /// <param name="builder">What builds its actor's scene off the main thread, or none to build it at once.</param>
    public WandererView(Wanderer wanderer, HBHT.Plugins.PluginSet plugins, Models models, Texture2D? shadow, ChunkBuilder? builder = null)
        : base($"Wanderer {wanderer.Party.Index}", wanderer.Body, wanderer.Look, plugins, models, shadow, builder)
    {
        Wanderer = wanderer;
        Sync();
    }

    public WandererView()
    {
        Wanderer = null!;
    }

    public Wanderer Wanderer { get; }

    /// <summary>
    /// Fades the body by its distance from the camera's aim (<c>PlayerShadowAlpha</c>), and places its
    /// shadow, which shows only within <see cref="BodyShadow.OthersDistance"/> of the aim.
    /// </summary>
    /// <param name="aim">The camera's aim.</param>
    /// <param name="ownAim">
    /// The aim as the original measures a shadow from it (<see cref="BodyShadow.ChunkAim"/>): in the player's chunk's
    /// coordinates, against the body's position in its own chunk's. For a body in another chunk that's off by the
    /// chunks between them, so its shadow seldom shows.
    /// </param>
    /// <param name="distance">How far from the aim it's drawn (<see cref="Core.Scenes.ObjectDistance.Bodies"/>).</param>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim, System.Numerics.Vector3 ownAim, float distance)
    {
        var (_, fromAim) = Fade(aim, distance: distance);
        var own = BodyShadow.OwnDistance(Body.Sphere.Position, Wanderer.Map.Origin, ownAim);
        PlaceShadow(world, BodyShadow.DrawAlpha(distance, fromAim, player: false, ownAim: own));
    }
}
