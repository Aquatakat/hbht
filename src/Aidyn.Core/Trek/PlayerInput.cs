using System.Numerics;
using Aidyn.Core.Movement;

namespace Aidyn.Core.Trek;

/// <summary>The player's controls for one tick of trek, as the original reads its controller.</summary>
/// <param name="Stick">The movement stick, each axis −1 to 1, up and right positive.</param>
/// <param name="Sneak">Z held: sneak instead of walking or running.</param>
/// <param name="Orbit">R held: the stick turns the camera instead of moving.</param>
/// <param name="OrbitStick">What turns the camera while <paramref name="Orbit"/> is held, in stick units a frame.</param>
/// <param name="Free">
/// A second stick turns the camera, so the first still moves him while it does. The original has one stick, and
/// orbiting stops him; this is a port addition.
/// </param>
public readonly record struct PlayerInput(Vector2 Stick, bool Sneak = false, bool Orbit = false, Vector2 OrbitStick = default, bool Free = false)
{
    /// <summary>Orbiting holds him still, as in the original.</summary>
    public bool Locked => Orbit && !Free;

    /// <summary>Whether the stick is pushed past its deadzone to move him.</summary>
    public bool Pushed => Stick.Length() > Body.Deadzone;

    /// <summary>What moves him: the stick and Z, unless the orbit holds him.</summary>
    public MoveInput Move => new(Stick, Sneak, Locked);
}
