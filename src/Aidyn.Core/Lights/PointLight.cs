using System.Numerics;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Lights;

/// <summary>
/// A light at a point that lights the bodies and actors within its radius: a light map object while its chunk is
/// loaded, or a dynamic light (<see cref="DynamicLights"/>). The original keeps both in the same 0x6c-byte struct, a
/// light map object's in its Borg9 (<c>InitLight</c>, 0x8004ce80 in 1.0, builds a dynamic one).
/// </summary>
/// <remarks>
/// <para>
/// Its colour moves between its two once a frame, by the frame's ticks, and only in a frame where something it
/// lights is within its radius (<see cref="WorldLights"/>): a light nothing is near holds its colour. Each kind sets a
/// share of the way from the first colour to the second, and the colour is the first plus the difference times the
/// share, truncated, channel by channel (<c>PointLight.Blend</c>, 0x8004cf88).
/// </para>
/// <para>
/// The colour is worked out in place, in the light (<see cref="Working"/>), and scaled there by the distance of
/// whatever first reaches it in the frame: what it lights later in the same frame takes that colour as it stands,
/// whatever its own distance, and scales it again by the screen fade's level (<c>WorldLights.AddNear</c>, 0x8004d384).
/// </para>
/// </remarks>
public sealed class PointLight
{
    private Rgba _from, _to;

    public PointLight(Vector3 position, float radius, LightKind kind, float speed, Rgba from, Rgba to,
        bool occludable = false, bool facesCamera = false)
    {
        Position = position;
        Radius = radius;
        Kind = kind;
        Speed = speed;
        _from = from;
        _to = to;
        Occludable = occludable;
        FacesCamera = facesCamera;
        // InitLight starts a static light at its colour and the rest at nothing; they move before they light.
        Working = kind == LightKind.Static ? from : default;
    }

    /// <summary>A light map object, in its chunk at <paramref name="origin"/>.</summary>
    public static PointLight For(LightObject light, Vector3 origin) =>
        new(origin + light.Position, light.Radius, light.Kind, light.Speed, light.From, light.To, light.Occludable, light.FacesCamera);

    /// <summary>Where it is in the world.</summary>
    public Vector3 Position { get; }

    /// <summary>It lights what's nearer than this, more the nearer.</summary>
    public float Radius { get; }

    public LightKind Kind { get; }

    /// <summary>Seconds a way (alternate), a cycle (sine) or the whole way (random), at 60 ticks a second.</summary>
    public float Speed { get; }

    /// <summary>Collision between it and what it lights blocks it (header bit 0x1000).</summary>
    public bool Occludable { get; }

    /// <summary>It shines from the camera's eye rather than from where it is (header bit 0x1).</summary>
    public bool FacesCamera { get; }

    /// <summary>
    /// Its colour as it stands: moved on and scaled by distance in the frame it last lit something, and by the fade
    /// for each thing it lit then.
    /// </summary>
    public Rgba Working { get; internal set; }

    /// <summary>The frame it last moved on in (<c>+0x10</c>); 0 is before any.</summary>
    internal long Frame { get; set; }

    /// <summary>The share of the way from the first colour to the second; for a sine, the angle in degrees.</summary>
    public float Phase { get; private set; }

    /// <summary>The share a random drift is heading for.</summary>
    public float Target { get; private set; }

    /// <summary>
    /// Moves the colour on by <paramref name="ticks"/>, as the original does once a frame by its delta, into
    /// <see cref="Working"/>.
    /// </summary>
    /// <param name="random">The stream a random drift draws its next share from.</param>
    public void Step(int ticks, IRandom random)
    {
        switch (Kind)
        {
            case LightKind.Static:
                Working = _from;
                break;
            case LightKind.Alternate:
                // Each way takes speed × 30 ticks, and at the end the colours change places.
                Phase += 1 / (Speed * 30) * ticks;
                if (Phase > 1)
                    (_from, _to) = (_to, _from);
                Phase = Wrap(Phase, 1);
                Working = Blend(_from, _to, Phase);
                break;
            case LightKind.Sine:
                // A cycle takes speed × 60 ticks, from half way.
                Phase = Wrap(Phase + 360 / (Speed * 60) * ticks, 360);
                Working = Blend(_from, _to, (MathF.Sin(Phase * 0.017453f) + 1) * 0.5f);
                break;
            case LightKind.Random:
            {
                // Toward the target at 1 / (speed × 60) a tick; reaching it, a new target from 0 to 1.
                var step = 1 / (Speed * 60) * ticks;
                if (MathF.Abs(Phase - Target) < step)
                {
                    Phase = Target;
                    Target = random.Float(1);
                }
                else
                {
                    Phase += Target < Phase ? -step : step;
                }
                Working = Blend(_from, _to, Phase);
                break;
            }
        }
    }

    /// <summary>
    /// The first colour plus the difference times the share, clamped to 0–1, truncated toward zero, channel by
    /// channel (<c>PointLight.Blend</c>, 0x8004cf88).
    /// </summary>
    public static Rgba Blend(Rgba from, Rgba to, float share)
    {
        share = Math.Clamp(share, 0, 1);
        return new Rgba(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B), Channel(from.A, to.A));

        byte Channel(byte a, byte b) => (byte)(a + (int)((b - (float)a) * share));
    }

    /// <summary>Each channel times <paramref name="scale"/>, truncated (<c>PointLight.Scale</c>, 0x8004d220).</summary>
    public static Rgba Scale(Rgba colour, float scale)
    {
        return new(Times(colour.R), Times(colour.G), Times(colour.B), Times(colour.A));

        byte Times(byte c) => (byte)(uint)(c * scale);
    }

    /// <summary>Brings a phase that's run past either end back by one period (<c>PointLight.Wrap</c>, 0x8004d150).</summary>
    private static float Wrap(float x, float period)
    {
        if (x < 0)
            x += period;
        if (period < x)
            x -= period;
        return x;
    }
}
