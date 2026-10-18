using Aidyn.Core.Combat;
using Aidyn.Core.Movement;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a combatant's body in the arena (<c>renderPlayers</c> over <c>Combat.Bodies</c>): its entity's actor at its scale,
/// standing at its feet and facing its heading, faded by its distance from the camera's aim, with its shadow. The core
/// steps its animations as the body moves. Party members are lit as the party is (<c>+0x70a</c>), by the arena's light map
/// objects too, which the arenas have none of.
/// </summary>
public partial class CombatantView : BodyView
{
    public CombatantView(Combatant combatant, PluginSet plugins, Models models, Texture2D? shadow)
        : base($"Combatant {combatant.Slot}", combatant.Body, combatant.Look, plugins, models, shadow)
    {
        Combatant = combatant;
        Sync();
    }

    public CombatantView()
    {
        Combatant = null!;
    }

    public Combatant Combatant { get; }

    /// <summary>
    /// Fades it by its distance from the camera's aim (<c>PlayerShadowAlpha</c>) and places its shadow, as a body's, over
    /// combat's distance, 128 (<c>InitZoneEngine</c> in combat's mode).
    /// </summary>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim)
    {
        var (_, fromAim) = Fade(aim, distance: BodyShadow.CombatDistance);
        PlaceShadow(world, BodyShadow.DrawAlpha(BodyShadow.CombatDistance, fromAim, player: Combatant.Entity == Core.GameState.Alaron));
    }
}
