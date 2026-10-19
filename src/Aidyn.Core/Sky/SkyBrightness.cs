namespace Aidyn.Core.Sky;

/// <summary>
/// The sky's slower fade (<c>Sky.Brightness</c>, <c>0x800D9518</c>), which multiplies all five of its colours and its backdrop
/// (<see cref="SkyColors.At"/>'s <c>dim</c>): a spell's cast in a battle dims it to half and back (<c>Sky.FadeBrightness</c>,
/// <c>0x8001DE88</c>, which <c>RenderSky</c> steps). 1 while nothing dims it.
/// </summary>
public sealed class SkyBrightness
{
    /// <summary>The brightness now (<c>0x800D9518</c>).</summary>
    public float Value { get; private set; } = 1;

    /// <summary>Where it's going (<c>0x800D951C</c>).</summary>
    public float Goal { get; private set; } = 1;

    private float _step;

    /// <summary>Toward <paramref name="goal"/> over <paramref name="ticks"/>: a step of (goal − now) / ticks a tick.</summary>
    public void FadeTo(float goal, float ticks)
    {
        Goal = goal;
        _step = (goal - Value) / ticks;
    }

    /// <summary>A tick of the fade, which stops at its goal.</summary>
    public void Step(int ticks = 1)
    {
        if (_step == 0)
            return;
        Value += _step * ticks;
        if (_step > 0 ? Value >= Goal : Value <= Goal)
            (Value, _step) = (Goal, 0);
    }
}
