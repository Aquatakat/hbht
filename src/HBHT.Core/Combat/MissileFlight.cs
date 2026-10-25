using System.Numerics;
using HBHT.Plugins;

namespace HBHT.Core.Combat;

/// <summary>
/// An arrow or thrown weapon on its way (<c>Combat.CastScene</c>, <c>0x800DB0C8</c>, the spells' cast scene, which state 3 moves;
/// <c>Combat.StartCastCamera</c>): a straight line in X and Z from the attacker to the target's middle, at a speed and with an arc
/// by its weapon's muzzle number and the distance. The hit waits for it. The original's <c>CombatAttackFx.Add</c> is never called.
/// </summary>
public sealed class MissileFlight
{
    /// <param name="scene">What flies: the arrow, or the weapon's own model.</param>
    /// <param name="from">Where it starts.</param>
    /// <param name="to">Where it lands.</param>
    /// <param name="speed">The weapon's muzzle number (<see cref="Plugins.Records.Missile.Speed"/>).</param>
    /// <param name="scale">The attacker's scale, which it's drawn at.</param>
    /// <param name="turn">Degrees it's turned about the vertical.</param>
    /// <param name="spin">Degrees it turns more a tick: a flask's 8 (<c>0x800D2B44</c>).</param>
    public MissileFlight(RecordId scene, Vector3 from, Vector3 to, float speed, float scale, float turn, float spin = 0)
    {
        Spin = spin;
        Scene = scene;
        From = from;
        Position = from;
        Scale = scale;
        Turn = turn;
        var way = to - from;
        Length = way.Length();
        Way = Length > 0 ? way / Length : Vector3.Zero;
        // The speed and the arc (0x800CE37C-0x800CE3A0).
        var near = Length <= 40;
        var f = speed + (near ? Length / 40 * 4 - 2 : 2);
        PerTick = f > 10 ? 0.5f : f >= 1 ? f * 0.05f : 0.05f;
        var a = 10 - speed + (near ? Length / 40 * 8 - 4 : 4);
        Arc = a > 10 ? 7 : a >= 1 ? a * 0.7f : 0.7f;
    }

    public RecordId Scene { get; }

    public Vector3 From { get; }

    /// <summary>Where it is along its line, before the arc.</summary>
    public Vector3 Position { get; private set; }

    public Vector3 Way { get; }

    public float Length { get; }

    /// <summary>How far it goes a tick.</summary>
    public float PerTick { get; }

    /// <summary>How high its arc rises at the middle.</summary>
    public float Arc { get; }

    public float Scale { get; }

    public float Turn { get; private set; }

    /// <summary>Degrees it turns about the vertical a tick.</summary>
    public float Spin { get; }

    /// <summary>How far it has gone.</summary>
    public float Travelled { get; private set; }

    /// <summary>Whether it has reached the target.</summary>
    public bool Landed => Travelled >= Length;

    /// <summary>Where it's drawn: its place on the line, raised by the arc, sin(the share gone × π) of it.</summary>
    public Vector3 Drawn => Position + new Vector3(0, Arc * MathF.Sin(Length > 0 ? Travelled / Length * MathF.PI : 0), 0);

    /// <summary>A tick of its flight, until it lands.</summary>
    public void Tick()
    {
        if (Landed)
            return;
        Position += Way * PerTick;
        Travelled += PerTick;
        Turn += Spin;
    }
}
