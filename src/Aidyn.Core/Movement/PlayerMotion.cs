using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.Physics;

namespace Aidyn.Core.Movement;

/// <summary>The player's controls for one tick, as the original reads its controller.</summary>
/// <param name="Stick">The movement stick, each axis −1 to 1, up and right positive.</param>
/// <param name="Sneak">Z held: sneak instead of walking or running.</param>
/// <param name="Orbit">R held: the stick turns the camera instead of moving.</param>
/// <param name="OrbitStick">What turns the camera while <paramref name="Orbit"/> is held, in stick units a frame.</param>
public readonly record struct PlayerInput(Vector2 Stick, bool Sneak = false, bool Orbit = false, Vector2 OrbitStick = default);

/// <summary>Plays an actor's animations and reports how far their root moves.</summary>
public interface IRootMotion
{
    /// <summary>
    /// Advances the animation one tick toward <paramref name="request"/> and returns how far its
    /// root moved forward (<c>Borg7Header.unk2c</c>, negated as the game does). That's nothing on
    /// a tick that enters a state or the two after it.
    /// </summary>
    float Tick(int request);
}

/// <summary>
/// The player walking about (<c>Actor::Move</c>, which combat also uses, and the player loop in
/// <c>PlayerData.cpp</c>). Alaron is a sphere of radius 0.5 that the stick turns and that his
/// animations move: walking and running speeds are his animations' root motion, about 1.26 and
/// 3.6 units a second.
/// </summary>
public sealed class PlayerMotion
{
    /// <summary>Animation requests (<c>ani_type</c>).</summary>
    public const int Idle = 0, Walk = 2, Run = 3, Sneak = 25;

    /// <summary>
    /// How far he turns toward the stick each frame: 20°. The decomp reads 2°; the ROM's
    /// constant is 0x3EB2B7FE, 20° in radians. Allies in combat turn 10°.
    /// </summary>
    public const float TurnPerFrame = 20 * MathF.PI / 180;

    private Vector3 _safe, _safer;
    private float _pace;

    public PlayerMotion(Vector3 position, Vector2 heading)
    {
        Sphere = new CollisionSphere { Radius = 0.5f, Gravity = new Vector3(0, -0.003f, 0), Position = position };
        Heading = Vector2.Normalize(heading);
        _safe = _safer = position;
    }

    public CollisionSphere Sphere { get; }

    /// <summary>
    /// The way he faces, in X and Z. The original keeps the opposite, <c>facing</c>, which is why
    /// the camera's goal is along it.
    /// </summary>
    public Vector2 Heading { get; set; }

    /// <summary>The animation he's asked for.</summary>
    public int Request { get; private set; }

    /// <summary>
    /// The heading he's turning to where he stands (<c>Actor::SetFacing</c>, as a conversation
    /// turns him to the one he talks to), or null. He turns 10° a frame, as other bodies do, and
    /// the stick cancels it.
    /// </summary>
    public Vector2? TurnTo { get; private set; }

    /// <summary>
    /// An animation asked for in place of standing, for the next tick only, as a conversation asks
    /// for his talking (request 4). Turning, or the stick, overrides it, as <c>Actor::Move</c> does.
    /// </summary>
    public int? Asked { get; set; }

    /// <summary>Turns him to face <paramref name="heading"/> where he stands (<c>Actor::SetFacing</c>).</summary>
    public void Face(Vector2 heading) =>
        TurnTo = heading.LengthSquared() > SpherePhysics.Epsilon * SpherePhysics.Epsilon ? Vector2.Normalize(heading) : null;

    /// <summary>
    /// How many animation ticks he plays per tick, which scales his speed with his steps. 1 is the
    /// original's; faster profiles play him quicker (ADR 0006's fidelity settings).
    /// </summary>
    public float Pace { get; set; } = 1;

    /// <summary>Where his feet are: the sphere's bottom.</summary>
    public Vector3 Feet => Sphere.Position - new Vector3(0, Sphere.Radius, 0);

    /// <summary>One 60 Hz tick.</summary>
    public void Step(CollisionWorld world, PlayerInput input, FollowCamera camera, IRootMotion animation)
    {
        Control(input, camera);

        var height = Sphere.Position.Y;
        SpherePhysics.Step(world, Sphere);
        if (Sphere.Touching && Vector3.Distance(_safe, Sphere.Position) > 1)
        {
            _safer = _safe;
            _safe = Sphere.Position;
        }
        // On plain collision, which keeps velocity, don't slide down when there's ground just below.
        if (Sphere.Surface is 1 or 2 && SpherePhysics.Ground(world, Sphere.Position, 0.5f, 0.05f) && Sphere.Position.Y < height)
            Sphere.Position.Y = height;
        if (!Sphere.Touching)
            Sphere.Surface = 0;
        // And press down onto it: a push the original gives once a frame.
        if (Sphere.Surface is 1 or 2)
            Sphere.Velocity.Y -= Cadence.PerTick(0.03f);

        _pace += Pace;
        var forward = 0f;
        for (; _pace >= 1; _pace--)
            forward += animation.Tick(Request);
        if (Sphere.Velocity.Y <= -0.048f)
        {
            // Falling: no steering, and a long fall goes back to where he last stood.
            Sphere.Velocity.X = Sphere.Velocity.Z = 0;
            if (Sphere.Velocity.Y < -0.3f)
            {
                Sphere.Velocity = Vector3.Zero;
                Sphere.Position = _safer;
            }
        }
        else
        {
            Sphere.Position += new Vector3(Heading.X, 0, Heading.Y) * forward;
        }
    }

    /// <summary>Reads the controls (<c>Actor::Move</c>, the trek parts).</summary>
    private void Control(PlayerInput input, FollowCamera camera)
    {
        Request = Asked ?? Idle;
        Asked = null;
        if (input.Orbit)
        {
            camera.Orbit(Sphere.Position, input.OrbitStick);
            return;
        }
        camera.Follow();

        var tilt = input.Stick.Length();
        if (tilt <= 0.05f)
        {
            camera.Anchor(Sphere.Position);
            if (TurnTo is { } to)
            {
                Request = Idle;
                Heading = Turn(Heading, to, Cadence.PerTick(Body.FaceTurnPerFrame));
                if (Faces(Heading, to))
                {
                    Heading = to;
                    TurnTo = null;
                }
            }
            return;
        }
        TurnTo = null;
        Request = input.Sneak ? Sneak : tilt >= 0.8f ? Run : Walk;
        // Up on the stick is the way the camera looks, and right is to its right.
        var look = camera.LookXZ;
        var x = -input.Stick.X;
        var y = input.Stick.Y;
        var wanted = Vector2.Normalize(new Vector2(look.Y * x + look.X * y, look.Y * y - look.X * x));
        Heading = Turn(Heading, wanted, Cadence.PerTick(TurnPerFrame));
    }

    /// <summary>
    /// Whether a turn has arrived: <see cref="Turn"/> normalizes what it arrives at again, which can
    /// differ from a unit vector in its last bit.
    /// </summary>
    public static bool Faces(Vector2 heading, Vector2 to) => Vector2.DistanceSquared(heading, to) < 1e-10f;

    /// <summary>
    /// Turns <paramref name="from"/> toward <paramref name="to"/> by <paramref name="angle"/>,
    /// or all the way once they're that close (<c>some_trig_func_2</c>).
    /// </summary>
    public static Vector2 Turn(Vector2 from, Vector2 to, float angle)
    {
        from = Vector2.Normalize(from);
        to = Vector2.Normalize(to);
        var (sin, cos) = MathF.SinCos(angle);
        if (Vector2.Distance(from, to) <= sin)
            return to;
        var left = new Vector2(cos * from.X + sin * from.Y, cos * from.Y - sin * from.X);
        var right = new Vector2(cos * from.X - sin * from.Y, cos * from.Y + sin * from.X);
        return Vector2.Distance(right, to) <= Vector2.Distance(left, to) ? right : left;
    }
}
