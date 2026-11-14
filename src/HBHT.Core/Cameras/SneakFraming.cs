using System.Numerics;

namespace HBHT.Core.Cameras;

/// <summary>
/// Sneaking's framing for the enhanced trek camera (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced): while he
/// sneaks with a hunter within twice its perception, the yaw's goal is on his far side from the hunter, so the hunter is in
/// the frame beyond him, with the boom a little longer and higher. Its <see cref="Weight"/> takes hold over
/// <see cref="TrekCameraTuning.SneakIn"/> and lets go over <see cref="TrekCameraTuning.SneakOut"/>, eased, and it changes
/// hunter only for one within <see cref="TrekCameraTuning.SneakSwitch"/> of the held one's distance. It replaces the trailing
/// yaw while it holds; it isn't a lean.
/// </summary>
public sealed class SneakFraming
{
    private const float Tick = 1f / 60;

    private float _rise;
    private Vector3 _hunter;

    /// <summary>The hunters about, each where it is and how far it notices (its perception), for the player at a place.</summary>
    public Func<Vector3, IEnumerable<(Vector3 At, float Perception)>> Hunters { get; set; } = _ => [];

    /// <summary>Whether he's sneaking.</summary>
    public Func<bool> Sneaking { get; set; } = () => false;

    /// <summary>How much it holds, 0 to 1, eased.</summary>
    public float Weight { get; private set; }

    /// <summary>Whether it has a hunter it frames now.</summary>
    public bool Holding { get; private set; }

    /// <summary>Where the hunter it frames (or last framed, letting go) is.</summary>
    public Vector3 Hunter => _hunter;

    /// <summary>The yaw from him to the camera's place, in radians: straight away from the hunter.</summary>
    public float Yaw(Vector3 player) => MathF.Atan2(player.Z - _hunter.Z, player.X - _hunter.X);

    /// <summary>A tick: chooses the hunter, with hysteresis, and eases the weight.</summary>
    public void Step(Vector3 player, TrekCameraTuning t)
    {
        (Vector3 At, float Distance)? best = null;
        (Vector3 At, float Distance)? held = null;
        if (Sneaking())
        {
            foreach (var (at, perception) in Hunters(player))
            {
                var distance = Flat(at - player);
                if (distance > 2 * perception || distance < 1e-3f)
                    continue;
                if (best is null || distance < best.Value.Distance)
                    best = (at, distance);
                // The held one is followed as it moves: the nearest to where it was last tick.
                if (Holding && Flat(at - _hunter) < 1 && (held is null || Flat(at - _hunter) < Flat(held.Value.At - _hunter)))
                    held = (at, distance);
            }
        }
        if (held is { } h && best is { } b && b.Distance >= t.SneakSwitch * h.Distance)
            best = h;
        Holding = best is not null;
        if (best is { } chosen)
            _hunter = chosen.At;
        var rate = Tick / MathF.Max(Holding ? t.SneakIn : t.SneakOut, 1e-3f);
        _rise = Holding ? MathF.Min(1, _rise + rate) : MathF.Max(0, _rise - rate);
        Weight = _rise * _rise * (3 - 2 * _rise);
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();
}
