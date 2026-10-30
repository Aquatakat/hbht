using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Movement;

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

    /// <summary>
    /// Falling faster than this a tick, he can't steer and his animation doesn't move him (<c>ProcessPlayers</c>, <c>0x800132f4</c>:
    /// <c>0x800cdb60</c>, −0.048, for a body of Alaron's actor, <c>+0x70B</c>).
    /// </summary>
    public const float Falling = -0.048f;

    /// <summary>
    /// The ticks he may be falling by his velocity without dropping before he steers again (<see cref="Pinned"/>), the port's:
    /// about a quarter of a second, counted a frame at a time.
    /// </summary>
    public const int PinnedTicks = 15;

    /// <summary>Less than this lower than where <see cref="Pinned"/>'s count began, he hasn't dropped.</summary>
    private const float PinnedDrop = 0.05f;

    /// <summary>
    /// The ticks of the frames in a row he has been falling by his velocity (<see cref="Falling"/>) at a frame's end, yet stands,
    /// after each frame's collision, no more than <see cref="PinnedDrop"/> lower than as the count began. From
    /// <see cref="PinnedTicks"/> he steers and his animation moves him again, the port's, as the user chose to rescue rather than
    /// leave him stuck: against a leaning plain wall the ground rule, or a wall facing it, can hold him up while plain collision's
    /// push keeps his velocity falling, and 1.0 leaves him there with no way out. A real fall drops him at least 0.048 a tick, so
    /// it never counts past a frame.
    /// </summary>
    public int Pinned { get; private set; }

    /// <summary>His height as <see cref="Pinned"/>'s count began.</summary>
    private float _pinnedFrom;

    /// <summary>
    /// Counts the ticks to the end of a 30 fps frame (<see cref="Cadence.FrameTicks"/>), on which <c>ProcessPlayers</c>, once a
    /// frame, moves and collides him.
    /// </summary>
    private readonly FrameGate _frame = new(Cadence.FrameTicks);

    /// <summary>How far his animation's root has moved him forward over the frame so far, which moves him as it ends.</summary>
    private float _frameForward;

    /// <summary>The fraction of a tick of gravity and velocity carried to the next frame, at a <see cref="Pace"/> other than 1.</summary>
    private float _physicsTicks;

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

    /// <summary>
    /// One 60 Hz tick. The stick and his animation are read every tick, and the rest is <c>ProcessPlayers</c>'s once a frame, on
    /// the tick that ends a 30 fps frame, in its order: the frame's ticks of gravity and velocity, then one collision
    /// (<c>ProcessCollisionSphere</c>, <c>0x80012e84</c>), the ground rule, plain collision's push, the falling rule, and last the
    /// frame's root motion (<c>0x80013330</c>), which the next frame's collision clips. Against plain collision, which keeps his
    /// velocity, a frame's fall carries him back down a steep face as it would in 1.0; collided every tick, he walked up faces of
    /// normal Y 0.5 or so and out of the world (docs/systems/movement.md).
    /// </summary>
    /// <param name="look">The way the camera looks across the ground, which up on the stick moves him.</param>
    public void Step(CollisionWorld world, MoveInput input, Vector2 look, IRootMotion animation)
    {
        Control(input, look);
        _frameForward += Body.Animate(animation);
        if (!_frame.Tick())
            return;
        var forward = _frameForward;
        _frameForward = 0;

        // At a faster pace his gravity and velocity run as fast as his steps, the port's, the user's choice: the frame's fall
        // still balances his climb up a steep plain face where it does at the original's pace.
        _physicsTicks += Cadence.FrameTicks * Pace;
        var ticks = Math.Max(1, (int)_physicsTicks);
        _physicsTicks = Math.Max(0, _physicsTicks - ticks);
        var height = Sphere.Position.Y;
        SpherePhysics.Step(world, Sphere, ticks);
        // On plain collision, which keeps velocity, don't slide down when there's ground just below.
        if (Sphere.Surface is 1 or 2 && SpherePhysics.Ground(world, Sphere.Position, Radius, 0.05f) && Sphere.Position.Y < height)
            Sphere.Position.Y = height;
        // And press down onto it.
        Body.Press();

        // Falling: no steering, unless he has been held where he is for a while, falling by his velocity alone.
        var falling = Sphere.Velocity.Y <= Falling;
        if (!falling)
            Pinned = 0;
        else if (Pinned == 0)
            (Pinned, _pinnedFrom) = (Cadence.FrameTicks, height);
        else
            Pinned = Sphere.Position.Y > _pinnedFrom - PinnedDrop ? Pinned + Cadence.FrameTicks : 0;
        if (falling && Pinned < PinnedTicks)
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
