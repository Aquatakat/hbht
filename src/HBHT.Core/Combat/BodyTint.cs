using System.Numerics;

namespace HBHT.Core.Combat;

/// <summary>
/// A body's tint (body <c>+0x75C</c> level, <c>+0x760</c> rate, <c>+0x764</c> colour), which only combat changes: a level from 0 to 1
/// that moves by its rate a tick, mixing the body's ambient light from half the sky colour toward the colour
/// (<c>ProcessPlayers</c>, <c>0x80012B1C</c>–<c>0x80012CA4</c>). The target marks pulse it and a spell's cast glows it; the level
/// also scales the moon's light on the body (<c>Bodies.SetLights</c>, <c>0x80013798</c>). See docs/systems/rendering.md.
/// </summary>
public sealed class BodyTint
{
    /// <summary>How far the ambient has moved toward <see cref="Colour"/>, 0–1 (<c>+0x75C</c>).</summary>
    public float Level { get; private set; }

    /// <summary>What the level moves by a tick (<c>+0x760</c>), 0 once it's settled.</summary>
    public float Rate { get; private set; }

    /// <summary>The colour the ambient moves toward, 0–1 a channel (<c>+0x764</c>).</summary>
    public Vector3 Colour { get; private set; }

    /// <summary>Toward <paramref name="colour"/> over <paramref name="ticks"/> (<c>Body.SetTint</c>, <c>0x8001371C</c>): rate 1 / ticks.</summary>
    public void Set(float ticks, Vector3 colour)
    {
        Colour = colour;
        Rate = 1.0f / ticks;
    }

    /// <summary>
    /// Back to the sky's ambient over <paramref name="ticks"/> (<c>Body.ClearTint</c>, <c>0x80013774</c>): rate −1 / ticks, so 0 ticks
    /// takes it to 0 at the next step.
    /// </summary>
    public void Clear(float ticks) => Rate = -1.0f / ticks;

    /// <summary>The level moved on by its rate over <paramref name="ticks"/>, and held at 0 or 1 once it reaches either.</summary>
    public void Step(int ticks = 1)
    {
        if (Rate == 0 && Level == 0)
            return;
        Level += Rate * ticks;
        if (Level > 1)
            (Level, Rate) = (1, 0);
        if (Level < 0)
            (Level, Rate) = (0, 0);
    }

    /// <summary>
    /// The body's ambient (<c>+0x770</c>): <paramref name="sky"/> (half the sky colour, 0–1) moved toward the colour by the level, then
    /// scaled by the screen's fade and clamped. With nothing tinted it's the sky's alone, unfaded.
    /// </summary>
    public Vector3 Ambient(Vector3 sky, float fade)
    {
        if (Rate == 0 && Level == 0)
            return sky;
        return Vector3.Clamp((sky + (Colour - sky) * Level) * fade, Vector3.Zero, Vector3.One);
    }

    /// <summary>Back to none, as a body is made (<c>Player.Reset</c>).</summary>
    public void Reset() => (Level, Rate, Colour) = (0, 0, default);
}
