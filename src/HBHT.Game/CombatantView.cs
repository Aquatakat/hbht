using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Core.Particles;
using HBHT.Core.Sky;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws a combatant's body in the arena (<c>renderPlayers</c> over <c>Combat.Bodies</c>): its entity's actor at its scale,
/// standing at its feet and facing its heading, faded by its distance from the camera's aim, with its shadow and what it holds. The core
/// owns and steps its animations as the body moves (<see cref="Combatant.Actor"/>), and the view draws it as the battle's recording has it on
/// the tick drawn (<see cref="DrawnBody"/>, ADR 0040). Party
/// members are lit as the party is (<c>+0x70a</c>), by the arena's light map objects too, which the arenas have none of.
/// </summary>
public partial class CombatantView : BodyView
{
    public CombatantView(Combatant combatant, PluginSet plugins, Models models, Texture2D? shadow)
        : base($"Combatant {combatant.Slot}", combatant.Body, combatant.Look, plugins, models, shadow, owned: combatant.Actor)
    {
        Combatant = combatant;
    }

    public CombatantView()
    {
        Combatant = null!;
    }

    public Combatant Combatant { get; }

    /// <summary>The body as the battle's recording has it on the tick drawn (ADR 0040).</summary>
    public DrawnBody Drawn { get; private set; }

    /// <summary>
    /// Moves the node to where the battle's recording has the body, poses its actor there, first starting the emitters it started in that
    /// tick, and puts in its hands what it holds (<see cref="Combatant.Held"/>). Call after each tick drawn. A body drawn from a tick other
    /// than the one after its last (<paramref name="jumped"/>, under Start Early) blends from its pose over <see cref="BattleComposer.BlendTicks"/>.
    /// </summary>
    public void Sync(DrawnBody drawn, IEnumerable<int> starts, bool jumped = false)
    {
        Drawn = drawn;
        Place(drawn.Feet, drawn.Heading, drawn.Movement);
        Follow(drawn.ActorState, drawn.ActorClock, starts, jumped ? BattleComposer.BlendTicks : 0);
        Hold([drawn.Arm, drawn.Hand, drawn.Notes]);
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
        var tint = Drawn.Tint;
        System.Numerics.Vector3? ambient = tint is { Level: 0, Rate: 0 } ? null : SkyColors.ToBytes(tint.Ambient(halfSky, fade));
        N64Material.SetBodyAmbient(Meshes, ambient?.ToGodot());
        N64Material.SetUnfogged(Meshes, Drawn.Unfogged);
    }

    /// <summary>
    /// Fades it by its distance from the camera's aim (<c>PlayerShadowAlpha</c>) and places its shadow, as a body's, over
    /// combat's distance, 128 (<c>InitZoneEngine</c> in combat's mode); a body that has died or fled fades out where it is, it and its
    /// shadow capped at its own alpha (<see cref="Combatant.Alpha"/>), what it holds faded with it.
    /// </summary>
    /// <param name="eye">The camera's place, which what it holds turns its billboards to: the Troubadour's notes.</param>
    /// <param name="scenery">The scenery's tint, in on-screen values, which what it holds is tinted by (<see cref="BodyView.TintHeld"/>).</param>
    public void Draw(Core.Physics.CollisionWorld world, System.Numerics.Vector3 aim, System.Numerics.Vector3 eye, System.Numerics.Vector3 scenery)
    {
        var alpha = Drawn.Alpha;
        var (_, fromAim) = Fade(aim, distance: BodyShadow.CombatDistance, bodyAlpha: alpha, at: Drawn.Centre);
        PlaceShadow(world, Drawn.Centre,
            BodyShadow.DrawAlpha(BodyShadow.CombatDistance, fromAim, player: Combatant.Entity == Core.GameState.Alaron, bodyAlpha: alpha));
        FaceHeld(eye.ToGodot());
        TintHeld(new Color(scenery.X, scenery.Y, scenery.Z));
    }
}
