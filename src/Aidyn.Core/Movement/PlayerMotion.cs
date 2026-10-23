using System.Numerics;
using Aidyn.Core.Physics;

namespace Aidyn.Core.Movement;

/// <summary>The controls that move the player for one tick.</summary>
/// <param name="Stick">The movement stick, each axis −1 to 1, up and right positive.</param>
/// <param name="Sneak">Z held: sneak instead of walking or running.</param>
/// <param name="Held">
/// Something else has the stick, as the trek camera's orbit does with R held: he stands, and neither turns nor reads it.
/// </param>
public readonly record struct MoveInput(Vector2 Stick, bool Sneak = false, bool Held = false);

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
/// <c>PlayerData.cpp</c>): the stick drives his <see cref="Body"/>, a sphere of radius 0.5 that his
/// animations move. Walking and running speeds are his animations' root motion, about 1.26 and
/// 3.6 units a second. What only the player does is here: the stick read along where the camera
/// looks, sneaking, the turn a conversation asks of him, keeping to the ground on plain collision,
/// and finding he has fallen out of the world, which trek rescues him from (<see cref="Trek.TrekLoop"/>).
/// </summary>
public sealed class PlayerMotion
{
    /// <summary>Alaron's sphere's radius.</summary>
    public const float Radius = 0.5f;

    /// <summary>Falling faster than this a tick, he has fallen out of the world (<c>ConfirmPlayerWithinZone</c>, <c>0x8000c078</c>).</summary>
    public const float FallLimit = 0.3f;

    /// <summary>Below this height, he has fallen out of the world (<c>ConfirmPlayerWithinZone</c>).</summary>
    public const float Floor = -50;

    public PlayerMotion(Vector3 position, Vector2 heading)
    {
        Body = new Body(position, Radius) { Heading = Vector2.Normalize(heading) };
    }

    /// <summary>His body, the first that <c>ProcessPlayers</c> steps.</summary>
    public Body Body { get; }

    public CollisionSphere Sphere => Body.Sphere;

    /// <summary>Whether he stood on the ground on the last step: touching something that faces up.</summary>
    public bool Grounded => Sphere.Touching && Sphere.ContactNormal.Y > 0.5f && Sphere.Velocity.Y > -0.048f;

    /// <summary>
    /// Whether he fell out of the world on the last step (<c>ConfirmPlayerWithinZone</c>): faster than <see cref="FallLimit"/> a
    /// tick, or below <see cref="Floor"/>. He's stopped, and it's trek's to put him somewhere (<see cref="Trek.TrekLoop"/>).
    /// </summary>
    public bool FellOut { get; private set; }

    /// <summary>
    /// The way he faces, in X and Z. The original keeps the opposite, <c>facing</c>, which is why
    /// the camera's goal is along it.
    /// </summary>
    public Vector2 Heading
    {
        get => Body.Heading;
        set => Body.Heading = value;
    }

    /// <summary>The animation he's asked for.</summary>
    public int Request => Body.Request;

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

    /// <summary>How many animation ticks he plays per tick (<see cref="Body.Pace"/>).</summary>
    public float Pace
    {
        get => Body.Pace;
        set => Body.Pace = value;
    }

    /// <summary>Where his feet are: the sphere's bottom.</summary>
    public Vector3 Feet => Sphere.Position - new Vector3(0, Sphere.Radius, 0);

    /// <summary>One 60 Hz tick.</summary>
    /// <param name="look">The way the camera looks across the ground, which up on the stick moves him.</param>
    public void Step(CollisionWorld world, MoveInput input, Vector2 look, IRootMotion animation)
    {
        Control(input, look);

        var height = Sphere.Position.Y;
        SpherePhysics.Step(world, Sphere);
        // On plain collision, which keeps velocity, don't slide down when there's ground just below.
        if (Sphere.Surface is 1 or 2 && SpherePhysics.Ground(world, Sphere.Position, Radius, 0.05f) && Sphere.Position.Y < height)
            Sphere.Position.Y = height;
        // And press down onto it.
        Body.Press();

        var forward = Body.Animate(animation);
        // Falling: no steering.
        if (Sphere.Velocity.Y <= -0.048f)
            Sphere.Velocity.X = Sphere.Velocity.Z = 0;
        else
            Body.Advance(forward);
        FellOut = Sphere.Velocity.Y < -FallLimit || Sphere.Position.Y < Floor;
        if (FellOut)
            Sphere.Velocity = Vector3.Zero;
    }

    /// <summary>Reads the controls (<c>Actor::Move</c>, the trek parts).</summary>
    private void Control(MoveInput input, Vector2 look)
    {
        Body.Request = Asked ?? Body.Idle;
        Asked = null;
        if (input.Held)
            return;

        var tilt = input.Stick.Length();
        if (tilt <= Body.Deadzone)
        {
            if (TurnTo is { } to)
            {
                Body.Request = Body.Idle;
                if (Body.TurnToward(to))
                    TurnTo = null;
            }
            return;
        }
        TurnTo = null;
        // Up on the stick is the way the camera looks, and right is to its right.
        var wanted = Vector2.Normalize(Facing.Rotate(new Vector2(-input.Stick.X, input.Stick.Y), look.X, look.Y));
        Body.Go(tilt, wanted, input.Sneak);
    }
}
