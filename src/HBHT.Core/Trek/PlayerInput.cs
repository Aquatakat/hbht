using System.Numerics;
using HBHT.Core.Movement;

namespace HBHT.Core.Trek;

/// <summary>The player's controls for one tick of trek, as the original reads its controller.</summary>
/// <param name="Stick">The movement stick, each axis −1 to 1, up and right positive.</param>
/// <param name="Sneak">Z held: sneak instead of walking or running.</param>
/// <param name="Orbit">R held: the stick turns the camera instead of moving.</param>
/// <param name="OrbitStick">What turns the camera while <paramref name="Orbit"/> is held, in stick units a frame.</param>
/// <param name="Free">
/// A second stick turns the camera, so the first still moves him while it does. The original has one stick, and
/// orbiting stops him; this is a port addition.
/// </param>
/// <param name="Aim">
/// The mouse's movement this tick, which moves a battle's free cursors (a thrown weapon's and an area spell's) as far as it
/// went rather than at the stick's pace, in frames of the stick's step, up and right positive (ADR 0040).
/// </param>
/// <param name="Held">
/// R itself held (Camera Hold), which keeps A from attacking in a battle's turn. <paramref name="Orbit"/> is also true while the
/// camera turns by what needs no R: a second stick, the camera's keys, and mouse look on every tick (ADR 0040).
/// </param>
public readonly record struct PlayerInput(Vector2 Stick, bool Sneak = false, bool Orbit = false, Vector2 OrbitStick = default, bool Free = false,
    Vector2 Aim = default, bool Held = false)
{
    /// <summary>
    /// Which way and how far a battle's free cursor goes this tick, of <paramref name="step"/> a frame, turned by the camera's look:
    /// the stick's way at its fixed step a tick (<c>Combat.StickToArena</c>), else the mouse's as far as it went.
    /// </summary>
    public Vector2 CursorMove(Vector2 look, float step)
    {
        if (Stick != Vector2.Zero)
            return SafeNormalize.Of(Facing.Rotate(new Vector2(-Stick.X, Stick.Y), look.X, look.Y)) * (step / Cadence.AimFrameTicks);
        return Aim == Vector2.Zero ? Vector2.Zero : Facing.Rotate(new Vector2(-Aim.X, Aim.Y), look.X, look.Y) * step;
    }

    /// <summary>Orbiting holds him still, as in the original.</summary>
    public bool Locked => Orbit && !Free;

    /// <summary>Whether the stick is pushed past its deadzone to move him.</summary>
    public bool Pushed => Stick.Length() > Body.Deadzone;

    /// <summary>What moves him: the stick and Z, unless the orbit holds him.</summary>
    public MoveInput Move => new(Stick, Sneak, Locked);
}
