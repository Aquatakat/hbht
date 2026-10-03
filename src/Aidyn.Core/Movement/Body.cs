using System.Numerics;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;

namespace Aidyn.Core.Movement;

/// <summary>
/// A body other than the player's, moving through the world: a wanderer, or a cut-scene's actor (the
/// original's <c>playerData</c>, stepped by <c>ProcessPlayers</c>). It's a sphere
/// that its actor's animations move, as the player's is (<see cref="PlayerMotion"/>), steered toward a
/// goal or turned to a heading instead of by a stick (<c>Actor::SetAiDest</c>, <c>SetFacing</c>,
/// <c>MoveTo</c>).
/// </summary>
/// <remarks>
/// The original steers it with a controller of its own: <c>MoveTo</c> puts the stick toward the goal
/// through the camera's turn, and <c>Actor::Move</c> reads it back through the same turn, so the
/// camera cancels out. A body walks at half tilt and runs at 0.95, and turns 20° a frame on the
/// move as the player does, and 10° where it stands. Positions are in the world's coordinates; the original keeps each body's in its own
/// chunk's, and keeps it 0.05 inside that chunk (<see cref="Bounds"/>).
/// </remarks>
public sealed class Body
{
    /// <summary>How far a body must come nearer its goal in a tick for the stick to follow it again.</summary>
    private const float Nearer = 0.01f;

    /// <summary>
    /// How far a body turns each frame where it stands (<c>MoveTo</c>'s 0x3E32B7FE): 10°, half what
    /// it turns while walking or running.
    /// </summary>
    public const float FaceTurnPerFrame = 10 * MathF.PI / 180;

    /// <summary>How far inside its chunk's edges a body is kept.</summary>
    public const float EdgeMargin = 0.05f;

    private float _pace;
    /// <summary>The goal's distance at its nearest so far (<c>aiDestDist</c>).</summary>
    private float _nearest;
    /// <summary>Ticks it hasn't come nearer its goal (<c>aiTravelTime</c>).</summary>
    private int _stalled;

    /// <param name="position">Its sphere's centre.</param>
    /// <param name="radius">Its sphere's radius, which <paramref name="scale"/> doesn't change for the world's collision.</param>
    public Body(Vector3 position, float radius, float scale = 1)
    {
        Sphere = new CollisionSphere { Radius = radius, Gravity = new Vector3(0, -0.003f, 0), Position = position };
        Scale = scale;
    }

    public CollisionSphere Sphere { get; }

    /// <summary>
    /// How much bigger than its actor's model it's drawn, how much farther its animations move it,
    /// and how much bigger its sphere is when bodies push each other.
    /// </summary>
    public float Scale { get; }

    /// <summary>Its sphere's radius at its scale (<c>scaleRad</c>).</summary>
    public float ScaledRadius => Sphere.Radius * Scale;

    /// <summary>The way it faces, in X and Z. The original keeps the opposite, <c>facing</c>.</summary>
    public Vector2 Heading { get; set; } = Vector2.Normalize(new Vector2(-1, -1));

    /// <summary>The animation it's asked for (<c>ani_type</c>).</summary>
    public int Request { get; set; }

    /// <summary>
    /// The area of the world it's kept in, in X and Z: its chunk, less <see cref="EdgeMargin"/>. Null
    /// for none.
    /// </summary>
    public (Vector2 Min, Vector2 Max)? Bounds { get; set; }

    /// <summary>How many animation ticks it plays per tick, as <see cref="PlayerMotion.Pace"/>.</summary>
    public float Pace { get; set; } = 1;

    /// <summary>Whether something steers it (<c>ACTOR_2</c>): only then does it read its stick.</summary>
    public bool Steered { get; private set; }

    /// <summary>Whether it's heading for <see cref="Goal"/> (<c>ACTOR_CANMOVE</c>).</summary>
    public bool Moving { get; private set; }

    /// <summary>Whether it's turning to <see cref="TurnTo"/> (<c>ACTOR_CANROTATE</c>).</summary>
    public bool Turning { get; private set; }

    /// <summary>Whether it walks to its goal rather than running (<c>ACTOR_WALKONLY</c>).</summary>
    public bool Walks { get; private set; }

    /// <summary>
    /// Held where it is by something else, such as the conversation it's in (<c>ACTOR_40</c>): its
    /// wanderer doesn't move it on.
    /// </summary>
    public bool Held { get; set; }

    /// <summary>Where it's heading, in X and Z (<c>aiDest</c>).</summary>
    public Vector2 Goal { get; private set; }

    /// <summary>How near <see cref="Goal"/> it stops (<c>wanderRadius</c>).</summary>
    public float GoalRadius { get; private set; }

    /// <summary>The heading it's turning to (<c>unk26c</c>, which the original keeps reversed).</summary>
    public Vector2 TurnTo { get; private set; }

    /// <summary>Its stick, in the world's X and Z rather than the camera's: the way it wants to go, and how far tilted.</summary>
    public Vector2 Stick { get; private set; }

    /// <summary>
    /// Whether its next <see cref="Face"/> turns it at once (<c>ACTOR_1000</c>, which a cut-scene's
    /// <c>CreateActor</c> sets): the facing is written, the flag cleared, and its goal kept.
    /// </summary>
    public bool SnapNextFace { get; set; }

    /// <summary>
    /// After how many ticks of coming no nearer its goal it gives up and is put there
    /// (<c>Actor::MoveTo</c>, <c>FUN_80019b08</c>): 300 in a cut-scene, 30 in combat. Null for trek,
    /// where it keeps trying.
    /// </summary>
    public int? GiveUpTicks { get; set; }

    /// <summary>
    /// Whether the screen is fading (<c>screenFadeMode</c>): while it's steered, it's asked to stand and
    /// its stick isn't read, though its steering, and so its giving up, carries on (<c>Actor::Move</c>).
    /// </summary>
    public bool Faded { get; set; }

    /// <summary>
    /// Sets off for a point (<c>Actor::SetAiDest</c>), stopping within <paramref name="radius"/> of it,
    /// walking or running. It forgets any turn.
    /// </summary>
    public void GoTo(Vector2 goal, float radius, bool walk)
    {
        Stop();
        Steered = Moving = true;
        Walks = walk;
        Goal = goal;
        GoalRadius = radius;
        _nearest = 1000;
        _stalled = 0;
    }

    /// <summary>Turns to face a way where it stands (<c>Actor::SetFacing</c>), forgetting any goal.</summary>
    /// <param name="heading">The way to face, which needn't be a unit vector.</param>
    public void Face(Vector2 heading)
    {
        if (SnapNextFace)
        {
            SnapNextFace = false;
            Heading = Normalize(heading);
            return;
        }
        Stop();
        Steered = Turning = true;
        TurnTo = Normalize(heading);
    }

    /// <summary>
    /// Forgets its goal and turn, and lets go of its stick (<c>Actor::ResetMoveQueue</c>, and the flags
    /// <c>0x800156e8</c> clears). It's still steered until its next step, which asks for standing
    /// (request 0), as <c>MoveTo</c> and <c>Actor::Move</c> do in the original; otherwise it would go on
    /// playing the walk or run it was last asked for, and its root motion would carry it on.
    /// </summary>
    public void Stop()
    {
        Stick = default;
        Moving = Turning = Walks = false;
    }

    /// <summary>
    /// One 60 Hz tick (<c>ProcessPlayers</c>, for a body other than Alaron): its steering, its stick,
    /// the world's collision, a push down on plain collision, and its animation's step. Bodies push
    /// each other after (<see cref="Crowd"/>).
    /// </summary>
    public void Step(CollisionWorld world, IRootMotion animation)
    {
        if (Steered)
        {
            Steer(world);
            if (Faded)
                Request = PlayerMotion.Idle;
            else
                Control();
        }

        SpherePhysics.Step(world, Sphere);
        if (!Sphere.Touching)
            Sphere.Surface = 0;
        if (Sphere.Surface is 1 or 2)
            Sphere.Velocity.Y -= Cadence.PerTick(0.03f);

        _pace += Pace;
        var forward = 0f;
        for (; _pace >= 1; _pace--)
            forward += animation.Tick(Request);
        Sphere.Position += new Vector3(Heading.X, 0, Heading.Y) * (forward * Scale);

        if (Bounds is var (min, max))
        {
            Sphere.Position.X = Math.Clamp(Sphere.Position.X, min.X, max.X);
            Sphere.Position.Z = Math.Clamp(Sphere.Position.Z, min.Y, max.Y);
        }
    }

    /// <summary>
    /// Points the stick at the goal, and turns (<c>Actor::MoveTo</c>). The stick follows the goal only
    /// while the body comes nearer than it's been; otherwise it keeps the last way, so a body that
    /// starts facing away walks a short curve. In trek, a body that can't get nearer keeps trying.
    /// Once within its radius, it stops; with nothing left to do, it lets go of its stick.
    /// </summary>
    private void Steer(CollisionWorld world)
    {
        var busy = false;
        if (!Moving)
        {
            Stick = default;
        }
        else
        {
            busy = true;
            var toGoal = Goal - new Vector2(Sphere.Position.X, Sphere.Position.Z);
            var distance = toGoal.Length();
            if (_nearest - distance > Nearer)
            {
                _nearest = distance;
                Stick = Normalize(toGoal) * (Walks ? 0.5f : 0.95f);
            }
            else if (GiveUpTicks is { } most && ++_stalled >= most)
            {
                GiveUp(world);
                return;
            }
            if (distance < GoalRadius)
                Moving = false;
        }
        if (Turning)
        {
            busy = true;
            Heading = PlayerMotion.Turn(Heading, TurnTo, Cadence.PerTick(FaceTurnPerFrame));
            if (PlayerMotion.Faces(Heading, TurnTo))
            {
                Heading = TurnTo;
                Turning = false;
            }
        }
        // MoveTo drops ACTOR_2 with nothing left to do, after this step's Actor::Move.
        if (!busy)
            Steered = false;
    }

    /// <summary>
    /// Puts it at its goal, set down there, facing the way it was turning to if it was, and stops it
    /// (<c>FUN_80019b08</c>).
    /// </summary>
    private void GiveUp(CollisionWorld world)
    {
        Sphere.Position = SetDown(world, new Vector3(Goal.X, Sphere.Position.Y, Goal.Y), Sphere.Radius, null);
        Sphere.Velocity = Vector3.Zero;
        if (Turning)
            Heading = TurnTo;
        Stop();
    }

    /// <summary>
    /// Where a sphere is set down at a point (<c>Actor::CheckCollision(p, 0, 0, 0)</c>): swept from 100
    /// above to 100 below, it rests on what it hits, 0.01 higher; the point's own height doesn't count.
    /// On a miss, it tries again up to four times a little to the side, by up to 0.25 each way, with two
    /// draws each from <paramref name="random"/>; with none, or nothing found, it stays where it was.
    /// </summary>
    public static Vector3 SetDown(CollisionWorld world, Vector3 at, float radius, IRandom? random)
    {
        var point = at;
        for (var tries = 0; tries < 5; tries++)
        {
            if (SpherePhysics.Sweep(world, point with { Y = at.Y + 100 }, point with { Y = at.Y - 100 }, radius, false, out var hit, out var normal))
                return new Vector3(hit.X + normal.X * radius, hit.Y + 0.01f + normal.Y * radius, hit.Z + normal.Z * radius);
            if (random is null)
                break;
            var angle = random.Float(2 * MathF.PI);
            var reach = random.Float(0.25f);
            point = at + new Vector3((MathF.Sin(angle) + 1) * 0.5f * reach, 0, (MathF.Cos(angle) + 1) * 0.5f * reach);
        }
        return at;
    }

    /// <summary>Reads the stick (<c>Actor::Move</c>, the trek parts): it walks under 80% tilt and runs above.</summary>
    private void Control()
    {
        Request = PlayerMotion.Idle;
        var tilt = Stick.Length();
        if (tilt <= 0.05f)
            return;
        Request = tilt >= 0.8f ? PlayerMotion.Run : PlayerMotion.Walk;
        Heading = PlayerMotion.Turn(Heading, Stick, Cadence.PerTick(PlayerMotion.TurnPerFrame));
    }

    private static Vector2 Normalize(Vector2 v) => v.LengthSquared() > SpherePhysics.Epsilon * SpherePhysics.Epsilon ? Vector2.Normalize(v) : Vector2.UnitY;
}
