using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;

namespace Aidyn.Core.Combat;

/// <summary>
/// The combat camera (<c>ProcessCombatCamera</c>, <c>0x8008D8BC</c>, camera mode 4): combat's own, not trek's follow camera,
/// though it moves the same camera by its primitives. It's the interiors' leashed camera (<c>Camera::Mode1</c>,
/// <see cref="FollowCamera.StepLeashed"/>) with combat's numbers, aimed 1.05 above the body that owns it, whose turn it is.
/// Checked against checkpoints 02–04.
/// </summary>
/// <remarks>
/// <para>
/// On its first tick it's put 8 behind the current party member, away from the nearest enemy and 1.5 up, or the first
/// combatant's if an enemy's turn begins the battle (<c>CombatCamera.PlaceBehind</c>, <c>0x8008D78C</c>). Each time a
/// party member's turn takes it, only its goal is put there. Its goal is held 2 above the aim and leashed 4 to 10 from it
/// in X and Z (8 to 16 while aiming a flask or placing an area, about the battle's focus); the eye eases to it at 1/32 a
/// tick, at most 0.5, and the aim at 1/16, at most 1. Through an enemy's turn the eye goes back to where its goal was and
/// stays, at least 4 from the aim. It's kept a unit above the arena's floor (<c>KeepInArena</c>, <c>0x8008D29C</c>).
/// </para>
/// <para>
/// The original runs it once a frame, its easing looping over the frame's ticks; the port steps it every tick, which is
/// the same for the easing, and acts on the rest every tick too, as each only sets a goal or holds the eye.
/// </para>
/// </remarks>
public sealed class CombatCamera
{
    /// <summary><c>Camera::Mode1</c>'s numbers in combat.</summary>
    public static readonly LeashNumbers Numbers = new(Near: 4, Far: 10, OrbitLow: 0.5f, OrbitHigh: 8, Rise: 2, Behind: 8,
        AimRate: 16, Rate: 32, AimMost: 1, Most: 0.5f);

    /// <summary>Its numbers while aiming a flask or placing an area (states 18 and 11).</summary>
    public static readonly LeashNumbers Aiming = Numbers with { Near = 8, Far = 16 };

    /// <summary>How far behind a party member it's put, and how far up (<c>PlaceBehind</c>).</summary>
    public const float Behind = 8, BehindRise = 1.5f;

    /// <summary>How near the aim its eye stays through an enemy's turn.</summary>
    public const float EnemyTurnNear = 4;

    /// <summary>How far above the floor the eye is kept (<c>KeepInArena</c>), and the sphere it finds the floor with.</summary>
    public const float FloorClearance = 1, FloorProbe = 0.5f;

    /// <summary>How long a floor found is kept for when the probe misses: 10 frames.</summary>
    public const int FloorTicks = 10 * Cadence.FrameTicks;

    /// <summary>Whether it's yet to be put behind a party member (<c>CombatCamera.Placing</c>, set by <c>Combat.Reset</c>).</summary>
    private bool _placing = true;

    /// <summary>The body its goal was last put behind (<c>CombatCamera.LastOwner</c>).</summary>
    private Body? _lastOwner;

    private int _floorTimer;
    private float _floorY;

    /// <param name="owner">The body it starts on: the current combatant's (<c>Camera.SetEye</c> in <c>Combat.Init</c>).</param>
    public CombatCamera(Vector3 owner, Vector2 look)
    {
        Camera = new FollowCamera(owner, look) { Mode = CameraMode.Interior };
        Camera.PlaceAt(owner);
    }

    /// <summary>The camera it moves, which the zone engine and the runtime draw from.</summary>
    public FollowCamera Camera { get; }

    /// <summary>One tick (<c>ProcessCombatCamera</c>).</summary>
    public void Step(Battle battle)
    {
        var world = battle.World;
        if (_placing)
        {
            // An enemy's turn first: the first combatant's body.
            var first = battle.Current is { Hostile: false } current ? current : battle.Combatants.FirstOrDefault();
            if (first is not null && NearestOpponent(battle, first.Body) is { } opponent)
                PlaceBehind(first.Body, opponent, now: true);
            _placing = false;
            _lastOwner = null;
        }
        if (battle.Current is not { } owner)
            return;
        var body = owner.Body;
        var enemyTurn = owner.Hostile;
        if (!enemyTurn && _lastOwner != body)
        {
            _lastOwner = body;
            if (NearestOpponent(battle, body) is { } opponent)
                PlaceBehind(body, opponent, now: false);
        }
        var numbers = Numbers;
        var target = body.Sphere.Position;
        var savedGoal = Camera.PositionGoal;
        if (battle.State is CombatState.TargetThrown or CombatState.PlaceArea)
        {
            numbers = Aiming;
            var focus = battle.Arena.Origin + new Vector3(battle.Focus.X, 0, battle.Focus.Y);
            target = new Vector3(focus.X, target.Y, focus.Z);
            enemyTurn = false;
        }
        Camera.StepLeashed(world, target, body.Heading, numbers);
        KeepInArena(world);
        if (enemyTurn)
        {
            var aim = new Vector2(Camera.Aim.X, Camera.Aim.Z);
            var goal = new Vector2(savedGoal.X, savedGoal.Z);
            var way = goal - aim;
            var length = way.Length();
            if (length < EnemyTurnNear)
            {
                way = length > 0 ? way / length : Vector2.Zero;
                savedGoal = new Vector3(aim.X + EnemyTurnNear * way.X, savedGoal.Y, aim.Y + EnemyTurnNear * way.Y);
            }
            Camera.PlaceAt(savedGoal);
            KeepInArena(world);
            Camera.Orient();
        }
    }

    /// <summary>
    /// The nearest enemy's body to <paramref name="body"/> in 3D, among the combatants that can act
    /// (<c>CombatCamera.NearestOpponent</c>, <c>0x8008D698</c>).
    /// </summary>
    private static Body? NearestOpponent(Battle battle, Body body)
    {
        Body? nearest = null;
        var best = 10000f;
        foreach (var c in battle.Combatants)
        {
            if (!c.Hostile || !c.Active)
                continue;
            var distance = Vector3.Distance(c.Body.Sphere.Position, body.Sphere.Position);
            if (distance < best)
            {
                best = distance;
                nearest = c.Body;
            }
        }
        return nearest;
    }

    /// <summary>
    /// The eye 8 behind <paramref name="body"/>, away from <paramref name="opponent"/>, and 1.5 up (<c>PlaceBehind</c>): at
    /// once, aimed at the body, or as its goal alone.
    /// </summary>
    private void PlaceBehind(Body body, Body opponent, bool now)
    {
        var at = body.Sphere.Position;
        var away = new Vector2(at.X - opponent.Sphere.Position.X, at.Z - opponent.Sphere.Position.Z);
        away = away.LengthSquared() > 0 ? Vector2.Normalize(away) : Vector2.Zero;
        var eye = new Vector3(at.X + Behind * away.X, at.Y + BehindRise, at.Z + Behind * away.Y);
        if (now)
        {
            Camera.AimAt(at);
            Camera.PlaceAt(eye);
        }
        else
        {
            Camera.PositionGoal = eye;
        }
    }

    /// <summary>
    /// Keeps the eye a unit above the arena's floor (<c>KeepInArena</c>): a sphere swept from 2 above it to 2 below finds
    /// the floor, which is kept for 10 frames when it misses.
    /// </summary>
    private void KeepInArena(CollisionWorld world)
    {
        if (_floorTimer > 0)
            _floorTimer--;
        var eye = Camera.Position;
        if (SpherePhysics.Sweep(world, eye + new Vector3(0, 2, 0), eye - new Vector3(0, 2, 0), FloorProbe, skipSeeThrough: true, out var hit, out _))
        {
            _floorTimer = FloorTicks;
            _floorY = hit.Y;
        }
        else if (_floorTimer == 0)
        {
            return;
        }
        if (eye.Y - _floorY < FloorClearance)
            Camera.Position.Y = Camera.PositionGoal.Y = _floorY + FloorClearance;
    }
}
