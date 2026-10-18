using System.Numerics;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;

namespace Aidyn.Core.Movement;

/// <summary>
/// A body moving through the world (the original's <c>playerData</c>, stepped by <c>ProcessPlayers</c>): a
/// sphere that its actor's animations move. The player's is one, which <see cref="PlayerMotion"/> drives with
/// the stick; a wanderer or a cut-scene's actor is steered toward a goal or turned to a heading instead
/// (<c>Actor::SetAiDest</c>, <c>SetFacing</c>, <c>MoveTo</c>), through <see cref="Step"/>.
/// </summary>
/// <remarks>
/// The original steers a body with a controller of its own: <c>MoveTo</c> puts the stick toward the goal
/// through the camera's turn, and <c>Actor::Move</c> reads it back through the same turn, so the
/// camera cancels out. A body walks at half tilt and runs at 0.95, and turns 20° a frame on the
/// move as the player does, and 10° where it stands. Positions are in the world's coordinates; the original keeps each body's in its own
/// chunk's, and keeps it 0.05 inside that chunk (<see cref="Bounds"/>).
/// </remarks>
public sealed class Body
{
    /// <summary>
    /// Animation requests (<c>ani_type</c>). <see cref="Talk"/> is what a script's speaker is asked for as a press moves its
    /// box on (<see cref="Scripts.SpeakerTalk"/>).
    /// </summary>
    public const int Idle = 0, Walk = 2, Run = 3, Talk = 4, Sneak = 25;

    /// <summary>A body's gravity, a tick² (about 10.8 m/s²).</summary>
    public const float Gravity = 0.003f;

    /// <summary>The push down a frame onto plain collision, which keeps velocity (surfaces 1 and 2).</summary>
    private const float PressDown = 0.03f;

    /// <summary>
    /// How far a body turns toward the stick each frame (<c>Actor::Move</c>): 20°. The decomp reads 2°; the ROM's
    /// constant is 0x3EB2B7FE, 20° in radians. Allies in combat turn 10°.
    /// </summary>
    public const float TurnPerFrame = 20 * MathF.PI / 180;

    /// <summary>The stick's tilt at or below which <c>Actor::Move</c> doesn't read it.</summary>
    public const float Deadzone = 0.05f;

    /// <summary>The stick's tilt from which a body runs rather than walks.</summary>
    public const float RunTilt = 0.8f;

    /// <summary>
    /// How far from the camera's aim bodies move and animate, in trek and in cut-scenes (<c>PlayerHandler.float_0x68</c>);
    /// combat's is 128. Those farther off stand still.
    /// </summary>
    public const float StepDistance = 30;

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
        Sphere = new CollisionSphere { Radius = radius, Gravity = new Vector3(0, -Gravity, 0), Position = position };
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

    /// <summary>
    /// The field it's held in, or null for none (<c>Body.SetGoal</c>, <c>0x80015BD4</c>: the centre at <c>+0x274</c>, the
    /// radius at <c>+0x280</c>, and flag 4): a combatant's field of influence, a sphere about where it stood as its turn
    /// began (<see cref="KeepInField"/>).
    /// </summary>
    public (Vector3 Centre, float Radius)? Field { get; set; }

    /// <summary>
    /// How many animation ticks it plays per tick, which scales its speed with its steps. 1 is the
    /// original's; the brisk pace setting plays the player quicker (ADR 0018).
    /// </summary>
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
    /// The area of a chunk a body is kept in: the chunk whose coordinates start at <paramref name="origin"/>, less
    /// <see cref="EdgeMargin"/> each side.
    /// </summary>
    public static (Vector2 Min, Vector2 Max) ChunkBounds(Vector3 origin, float cellSize)
    {
        var start = new Vector2(origin.X, origin.Z);
        return (start + new Vector2(EdgeMargin), start + new Vector2(cellSize - EdgeMargin));
    }

    /// <summary>
    /// Whether it moves and animates this tick (<c>ProcessPlayers</c>): only within <paramref name="distance"/> of the
    /// camera's aim, <see cref="StepDistance"/> in trek and cut-scenes.
    /// </summary>
    public bool Near(Vector3 aim, float distance = StepDistance) => Vector3.Distance(Sphere.Position, aim) <= distance;

    /// <summary>
    /// One 60 Hz tick of a body the stick doesn't drive (<c>ProcessPlayers</c>, for a body other than Alaron): its
    /// steering, its stick, the world's collision, a push down on plain collision, and its animation's step. Bodies
    /// push each other after (<see cref="Crowd"/>).
    /// </summary>
    public void Step(CollisionWorld world, IRootMotion animation)
    {
        if (Steered)
        {
            Steer(world);
            if (Faded)
                Request = Idle;
            else
                Control();
        }

        SpherePhysics.Step(world, Sphere);
        Press();
        Advance(Animate(animation));
        KeepInField();
    }

    /// <summary>
    /// The stick of a body the player drives that isn't trek's player, a party member's in its turn in combat
    /// (<c>Body.ApplyInput</c>, <c>0x80014FEC</c>, trek's own stick handler): within the deadzone it stands; past it, up
    /// on the stick is the way the camera looks, and it walks, runs from 0.8, or sneaks with Z held, turning 10° a tick.
    /// <see cref="Step"/> moves it after.
    /// </summary>
    /// <param name="look">The way the camera looks across the ground.</param>
    public void Drive(MoveInput input, Vector2 look)
    {
        Request = Idle;
        if (input.Held)
            return;
        var tilt = input.Stick.Length();
        if (tilt <= Deadzone)
            return;
        Go(tilt, Vector2.Normalize(Facing.Rotate(new Vector2(-input.Stick.X, input.Stick.Y), look.X, look.Y)), input.Sneak);
    }

    /// <summary>
    /// Holds it in its <see cref="Field"/> once it has moved (<c>Body.ClampToGoal</c>, <c>0x80014518</c>, from
    /// <c>ProcessPlayers</c> after the world's collision and its animation's step, before bodies push each other): the
    /// distance from the centre is in 3D, and at the radius or past it, its X and Z are put back on the sphere along the
    /// way it's off, its height, velocity and heading left as they are. So on a slope it reaches a little less far
    /// across the ground, and at the edge it slides round as its stick pushes. A radius of 0 pins it to the centre.
    /// </summary>
    internal void KeepInField()
    {
        if (Field is not var (centre, radius))
            return;
        var off = Sphere.Position - centre;
        var length = off.Length();
        if (radius > length)
            return;
        if (length > 0)
            off /= length;
        Sphere.Position.X = centre.X + off.X * radius;
        Sphere.Position.Z = centre.Z + off.Z * radius;
    }

    /// <summary>
    /// After the world's collision (<c>ProcessPlayers</c>): off the ground it has no surface, and on plain collision,
    /// which keeps velocity, it's pushed down onto it, as the original does once a frame.
    /// </summary>
    internal void Press()
    {
        if (!Sphere.Touching)
            Sphere.Surface = 0;
        if (Sphere.Surface is 1 or 2)
            Sphere.Velocity.Y -= Cadence.PerTick(PressDown);
    }

    /// <summary>
    /// Plays its animation <see cref="Pace"/> ticks toward <see cref="Request"/>, carrying the fraction to the next
    /// tick, and returns how far their root moved it forward.
    /// </summary>
    internal float Animate(IRootMotion animation)
    {
        _pace += Pace;
        var forward = 0f;
        for (; _pace >= 1; _pace--)
            forward += animation.Tick(Request);
        return forward;
    }

    /// <summary>Moves it <paramref name="forward"/> along its heading, at its scale, and keeps it in its <see cref="Bounds"/>.</summary>
    internal void Advance(float forward)
    {
        Sphere.Position += new Vector3(Heading.X, 0, Heading.Y) * (forward * Scale);
        if (Bounds is var (min, max))
        {
            Sphere.Position.X = Math.Clamp(Sphere.Position.X, min.X, max.X);
            Sphere.Position.Z = Math.Clamp(Sphere.Position.Z, min.Y, max.Y);
        }
    }

    /// <summary>
    /// Turns it toward <paramref name="to"/> where it stands, <see cref="FaceTurnPerFrame"/> a frame
    /// (<c>Actor::SetFacing</c>'s turn), and lands on it once there. Returns whether it has.
    /// </summary>
    internal bool TurnToward(Vector2 to)
    {
        Heading = Facing.Turn(Heading, to, Cadence.PerTick(FaceTurnPerFrame));
        if (!Facing.Arrived(Heading, to))
            return false;
        Heading = to;
        return true;
    }

    /// <summary>
    /// The stick read past its <see cref="Deadzone"/> (<c>Actor::Move</c>): it sneaks if asked, and otherwise walks
    /// under <see cref="RunTilt"/> and runs from it, turning toward <paramref name="way"/> <see cref="TurnPerFrame"/>
    /// a frame.
    /// </summary>
    /// <param name="tilt">How far the stick is tilted, 0 to 1.</param>
    /// <param name="way">The way the stick points, in the world's X and Z.</param>
    internal void Go(float tilt, Vector2 way, bool sneak = false)
    {
        Request = sneak ? Sneak : tilt >= RunTilt ? Run : Walk;
        Heading = Facing.Turn(Heading, way, Cadence.PerTick(TurnPerFrame));
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
            if (TurnToward(TurnTo))
                Turning = false;
        }
        // MoveTo drops ACTOR_2 with nothing left to do, after this step's Actor::Move.
        if (!busy)
            Steered = false;
    }

    /// <summary>
    /// Puts it at its goal, set down there, facing the way it was turning to if it was, and stops it
    /// (<c>0x80015c28</c>, the decomp's <c>FUN_80019b08</c>). The original's set-down tries again to the side on a
    /// miss; the port's doesn't here, having no random stream to draw from.
    /// </summary>
    private void GiveUp(CollisionWorld world)
    {
        Sphere.Position = SetDown(world, new Vector3(Goal.X, Sphere.Position.Y, Goal.Y), Sphere.Radius, null).Position;
        Sphere.Velocity = Vector3.Zero;
        if (Turning)
            Heading = TurnTo;
        Stop();
    }

    /// <summary>The most sweeps from on high a set-down makes: the first, and four tries to the side.</summary>
    private const int SetDownTries = 5;

    /// <summary>
    /// Where a sphere at <paramref name="at"/> is set down on the ground (<c>Actor::CheckCollision</c>,
    /// <c>0x80011a64</c>, checked in 1.0's code). With <paramref name="near"/>, it first sweeps from 1 above that
    /// height to 1 below; otherwise, or missing, from a height of 100 to −100, whatever its own. It skips see-through
    /// triangles and rests on the first that faces it in the grid's order, 0.01 above it. Each sweep from on high
    /// that misses moves it a little to the side, by up to 0.25 each way with two draws from
    /// <paramref name="random"/>, and it tries again: four times, or three after a miss near the height. With no
    /// <paramref name="random"/> it doesn't. The original's callers pass one of two shapes: near a height
    /// (<c>(body, y, 1, 0)</c>, a teleport's arrival and a wanderer's spawn), or not (<c>(body, 0, 0, 0)</c>, a
    /// cut-scene's spawn, a body giving up its goal, combat's placing).
    /// </summary>
    /// <param name="near">The height to sweep about first (the original's mode 1), or null for none.</param>
    /// <returns>
    /// Where it rests and whether it found ground. Finding none, it's where its tries to the side left it, at its own
    /// height, as the original leaves the body.
    /// </returns>
    public static (Vector3 Position, bool Grounded) SetDown(CollisionWorld world, Vector3 at, float radius, IRandom? random, float? near = null)
    {
        var tries = 0;
        if (near is { } y)
        {
            if (Sweep(at with { Y = y + 1 }, at with { Y = y - 1 }) is { } close)
                return (close, true);
            tries = 1;
        }
        for (; ; tries++)
        {
            if (Sweep(at with { Y = 100 }, at with { Y = -100 }) is { } ground)
                return (ground, true);
            if (tries >= SetDownTries - 1 || random is null)
                return (at, false);
            var angle = random.Float(2 * MathF.PI);
            var reach = random.Float(0.25f);
            at.X += (MathF.Sin(angle) + 1) * 0.5f * reach;
            at.Z += (MathF.Cos(angle) + 1) * 0.5f * reach;
        }

        Vector3? Sweep(Vector3 from, Vector3 to) =>
            SpherePhysics.Sweep(world, from, to, radius, skipSeeThrough: true, out var hit, out var normal)
                ? new Vector3(hit.X + normal.X * radius, hit.Y + 0.01f + normal.Y * radius, hit.Z + normal.Z * radius)
                : null;
    }

    /// <summary>Reads the stick (<c>Actor::Move</c>, the trek parts): it walks under 80% tilt and runs above.</summary>
    private void Control()
    {
        Request = Idle;
        var tilt = Stick.Length();
        if (tilt <= Deadzone)
            return;
        Go(tilt, Stick);
    }

    private static Vector2 Normalize(Vector2 v) => v.LengthSquared() > SpherePhysics.Epsilon * SpherePhysics.Epsilon ? Vector2.Normalize(v) : Vector2.UnitY;
}
