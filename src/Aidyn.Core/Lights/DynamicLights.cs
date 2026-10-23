using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Lights;

/// <summary>
/// Lights the game adds for a while, which light bodies and actors as light map objects do (the original's manager at
/// 0x800d8900 in 1.0, <c>AllocDynamicLight</c> 0x8004db1c, <c>FreeDynamicLight</c> 0x8004dbe0,
/// <c>DynamicLights.Step</c> 0x8004dc74).
/// </summary>
/// <remarks>
/// <para>
/// There are 16 slots. A new light goes in the slot numbered by how many are lit, not in a free one: the original
/// keeps a list of the slots it frees but never reads it. So when a light goes out before one in a later slot, the
/// next new light takes that later slot's place while it's still lit. The port does as the original does.
/// </para>
/// <para>
/// In 1.0 only a trapped container's explosion adds any (<see cref="Explosion"/>): spells, torches and scripts add
/// none. Combat's spells will come through here too if they turn out to.
/// </para>
/// </remarks>
public sealed class DynamicLights
{
    public const int Slots = 16;

    private readonly PointLight?[] _lights = new PointLight?[Slots];
    private readonly int[] _life = new int[Slots];

    /// <summary>How many are lit, which is also the slot the next goes in.</summary>
    public int Count { get; private set; }

    /// <summary>The lights that are lit, in slot order.</summary>
    public IEnumerable<PointLight> Lit => _lights.OfType<PointLight>();

    /// <summary>
    /// Adds a light for <paramref name="ticks"/> ticks, or for good with 0, or does nothing when all 16 are lit
    /// (the original stops on an assert, "Out of Dynamic Lights").
    /// </summary>
    public PointLight? Add(int ticks, Vector3 position, float radius, LightKind kind, float speed, Rgba from, Rgba to)
    {
        if (Count >= Slots)
            return null;
        var light = new PointLight(position, radius, kind, speed, from, to);
        _lights[Count] = light;
        _life[Count] = ticks;
        Count++;
        return light;
    }

    /// <summary>Counts each light's life down by a frame's <paramref name="ticks"/>, and puts out those it ends.</summary>
    public void Step(int ticks)
    {
        for (var i = 0; i < Slots; i++)
        {
            if (_lights[i] is null || _life[i] <= 0)
                continue;
            _life[i] -= ticks;
            if (_life[i] <= 0)
            {
                _lights[i] = null;
                Count--;
            }
        }
    }

    /// <summary>Whether a light is still lit in its slot: its life hasn't run out, and no new light has taken its place.</summary>
    public bool IsLit(PointLight light) => Array.IndexOf(_lights, light) >= 0;

    public void Clear()
    {
        Array.Clear(_lights);
        Count = 0;
    }

    /// <summary>
    /// A trapped container going off (<c>Containers.Explode</c>, 0x8000ffe4) where there are no particles to go with its
    /// lights: four fires, each a light drifting at random between a dark red and yellow, within 5, the first for 600 ticks
    /// and the rest for 60 (<see cref="FireLight"/>), then a burst within 10 that fades from orange to nothing over its 120
    /// ticks (<see cref="BurstLight"/>), all where the container is. With particles, <see cref="Particles.Explosion"/>
    /// adds the same lights, each with its emitter, and the flying fires' follow them.
    /// </summary>
    public void Explosion(Vector3 at)
    {
        FireLight(at, 600);
        for (var i = 0; i < 3; i++)
            FireLight(at, 60);
        BurstLight(at);
    }

    /// <summary>A fire's light (<c>Explosion.Fire</c>, 0x800174f8): drifting at random (speed 0.75) between dark red and yellow, within 5.</summary>
    public PointLight? FireLight(Vector3 at, int ticks) =>
        Add(ticks, at, 5, LightKind.Random, 0.75f, new Rgba(0x60, 0, 0, 0), new Rgba(0xFF, 0xFF, 0, 0));

    /// <summary>A burst's light (<c>Explosion.Burst</c>, 0x80016c00): orange fading to nothing (alternate, speed 4) over 120 ticks, within 10.</summary>
    public PointLight? BurstLight(Vector3 at) => Add(120, at, 10, LightKind.Alternate, 4, new Rgba(0xFF, 0x80, 0, 0), default);
}
