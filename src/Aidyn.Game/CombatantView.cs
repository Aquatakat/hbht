using Aidyn.Core.Combat;
using Aidyn.Core.Movement;
using Aidyn.Core.Particles;
using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a combatant's body in the arena (<c>renderPlayers</c> over <c>Combat.Bodies</c>): its entity's actor at its scale,
/// standing at its feet and facing its heading, faded by its distance from the camera's aim, with its shadow and what it holds. The core
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

    /// <summary>Moves the node to where the body is, and puts in its hands what it holds (<see cref="Combatant.Held"/>). Call after each tick.</summary>
    public override void Sync()
    {
        base.Sync();
        Hold(Combatant.Held);
    }

    /// <summary>Its actor's locators, which a spell's visual's emitters attach to; null for a body with no actor.</summary>
    public IParticleAnchor? Anchor => _anchor ??= Model is { } model ? ParticlesView.Anchor(model) : null;

    private IParticleAnchor? _anchor;

    /// <summary>Where its actor's locator <paramref name="index"/> is in the world as it was last drawn, or null if it has none.</summary>
    public System.Numerics.Vector3? Locator(int index) =>
        Anchor is { } anchor && anchor.TryLocator(new ParticleLocator(index, 0), out var world) ? world.Translation : null;

    /// <summary>
    /// Its body's ambient light by its tint (<c>ProcessPlayers</c>, <c>Bodies.SetLights</c>): half the sky's colour,
    /// <paramref name="halfSky"/>, moved toward the tint's colour by its level and scaled by the screen's <paramref name="fade"/>,
    /// truncated to bytes; the sky's alone while it has none. Its fog is off while a cast glows it.
    /// </summary>
    public void Tinted(System.Numerics.Vector3 halfSky, float fade)
    {
        var tint = Combatant.Tint;
        System.Numerics.Vector3? ambient = tint is { Level: 0, Rate: 0 } ? null : SkyColors.ToBytes(tint.Ambient(halfSky, fade));
        N64Material.SetBodyAmbient(Meshes, ambient?.ToGodot());
        N64Material.SetUnfogged(Meshes, Combatant.Unfogged);
    }

    /// <summary>
    /// Fades it by its distance from the camera's aim (<c>PlayerShadowAlpha</c>) and places its shadow, as a body's, over
    /// combat's distance, 128 (<c>InitZoneEngine</c> in combat's mode); a body that has fled fades out as it goes
    /// (<see cref="Combatant.Fading"/>).
    /// </summary>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim)
    {
        var opacity = Combatant.Freed ? (float)Combatant.Fading / Battle.FadeTicks : 1;
        var (_, fromAim) = Fade(aim, distance: BodyShadow.CombatDistance, opacity: opacity);
        PlaceShadow(world, BodyShadow.DrawAlpha(BodyShadow.CombatDistance, fromAim, player: Combatant.Entity == Core.GameState.Alaron));
    }
}
