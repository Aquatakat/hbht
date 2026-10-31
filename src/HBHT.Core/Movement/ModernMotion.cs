using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Movement;

/// <summary>
/// Modern movement, the port's, a setting (docs/systems/movement.md#modern-movement), which every body moves by under it, the
/// player's among them: the same sphere stepped every tick, which keeps to the ground and slides along walls. Ground is what faces up by <see cref="ModernRules.FloorUp"/> or more, except plain
/// collision (surfaces 1 and 2), the barriers, which is a wall unless it's nearly level. On the ground his animation's root
/// motion, its mean over a stride, carries him across it at its speed, up and down slopes alike; a wall takes away what goes
/// into it across the ground, so nothing he touches lifts him up it. He steps up onto ledges lower than his radius, squeezes up
/// to as high between walls that narrow toward the ground, and keeps to the ground down slopes and stairs a step's height at a
/// time. Off the ground he falls by gravity, with the original's falling rule, and nothing he touches bounces him.
/// </summary>
internal sealed class ModernMotion(Body body)
{
    private CollisionSphere Sphere => body.Sphere;
    private Body Body => body;
    private Vector2 Heading => body.Heading;
    private ModernRules Rules => body.Rules;
    private float Radius => body.Sphere.Radius;

    /// <inheritdoc cref="PlayerMotion.Falling"/>
    private const float Falling = PlayerMotion.Falling;

    /// <summary>How far clear of what he touches he's kept, so the next move starts outside it.</summary>
    private const float Skin = 0.005f;

    /// <summary>How many times a tick's move slides on along what it touched.</summary>
    private const int Slides = 4;

    /// <summary>How much higher each try of <see cref="Squeeze"/> lifts him.</summary>
    private const float SqueezeStep = 0.1f;

    /// <summary>The least a step up must raise him, or it's no ledge.</summary>
    private const float MinStep = 0.02f;

    /// <summary>The least an edge or corner of ground faces up toward him for him to climb over it, moving onto the ground.</summary>
    private const float LipUp = 0.05f;

    /// <summary>
    /// The least an edge or corner of ground faces up toward him for him to stand on it otherwise: his sphere's middle no more
    /// than 0.36 out past it. Further out, over a drop, he rolls off it, as 1.0's sphere is pushed off.
    /// </summary>
    private const float LipStand = 0.7f;

    /// <summary>His move across the ground this tick, which says whether he's climbing onto ground over its edge.</summary>
    private Vector3 _across;

    /// <summary>The most a tick's move climbs or drops for its distance across the ground, about 68°, as over a ledge's lip.</summary>
    private const float SteepestMove = 2.5f;

    /// <summary>How near to his sphere a trigger triangle counts as touched.</summary>
    private const float TriggerMargin = 0.02f;

    /// <summary>His animation's root motion over the last <see cref="ModernRules.Stride"/> ticks, round from <see cref="_strideNext"/>.</summary>
    private float[] _stride = [];

    private int _strideCount, _strideNext;
    private float _strideSum;

    /// <summary>
    /// Whether he's setting off: standing as of the last tick, or asked to move with his animation's root yet to move him, when he
    /// turns by <see cref="ModernRules.SetOffTurn"/>.
    /// </summary>
    private bool _settingOff = true;

    /// <summary>How far the rest of his step has carried him since the stick was let go (<see cref="ModernRules.StopReach"/>).</summary>
    private float _stopped;


    /// <summary>Whether he stood on the ground after the last Modern tick.</summary>
    private bool _onGround;

    /// <summary>The way the ground he stands on faces, under Modern movement.</summary>
    private Vector3 _groundNormal = Vector3.UnitY;

    /// <summary>The surface he stands on, under Modern movement.</summary>
    private int _groundSurface;

    /// <summary>
    /// The height his sphere was at as he last stood on the ground, which walls lift him no more than a step above: none till he
    /// first stands on it.
    /// </summary>
    private float _groundHeight = float.NegativeInfinity;

    private Func<SphereCast.Hit, bool>? _ground;

    /// <summary>Whether it stood on the ground after the last tick.</summary>
    public bool OnGround => _onGround;

    /// <summary>Whether it's setting off from standing, when it turns by <see cref="ModernRules.SetOffTurn"/>.</summary>
    public bool SettingOff => _settingOff;

    /// <summary>Takes up where the original's movement left it, on the ground if it was.</summary>
    public void Begin(bool grounded)
    {
        (_onGround, _groundNormal, _groundSurface) = (grounded, Vector3.UnitY, Sphere.Surface);
        _groundHeight = grounded ? Sphere.Position.Y : float.NegativeInfinity;
    }

    /// <summary>Off the ground, as after a fall out of the world.</summary>
    public void Lift() => _onGround = false;

    /// <summary>
    /// Whether a hit is ground: a face that's ground by <see cref="Rules"/>, or an edge or corner of one below him, nearly under
    /// him (<see cref="LipStand"/>), or further out as he moves onto the ground over it (<see cref="LipUp"/>).
    /// </summary>
    private bool Ground(SphereCast.Hit hit) =>
        Rules.Ground(hit.Surface, hit.Face.Y) && (hit.OnFace || hit.Normal.Y >= LipStand
            || (hit.Normal.Y >= LipUp && Into(hit.Normal, _across)));

    /// <summary>Whether a move across the ground heads into an edge facing <paramref name="normal"/>, within 60° of straight at it.</summary>
    private static bool Into(Vector3 normal, Vector3 across)
    {
        var flat = new Vector2(normal.X, normal.Z);
        var way = new Vector2(across.X, across.Z);
        return Vector2.Dot(flat, way) < -0.5f * flat.Length() * way.Length();
    }

    /// <summary>One tick of Modern movement: across the ground, or through the air, kept in its body's bounds.</summary>
    public void Step(CollisionWorld world, IRootMotion animation)
    {
        var s = Sphere;
        // Let go of the stick, the rest of his step plays faster, and carries him no further than StopReach.
        var stopping = Body.Stopping;
        var root = Body.Animate(animation, stopping ? Rules.StopPace : 1);
        if (stopping)
        {
            root = Math.Clamp(Rules.StopReach - _stopped, 0, root);
            _stopped += root;
        }
        else
        {
            _stopped = 0;
        }
        _settingOff = root == 0 && (_settingOff || Body.Request == Body.Idle);
        var forward = Speed(root, stopping || Body.MovingStates == 1);
        var across = _across = new Vector3(Heading.X, 0, Heading.Y) * (forward * body.Scale);
        var position = s.Position;
        SphereCast.PushOut(world, ref position, Radius, s.IgnoresSeeThrough);
        s.Touching = false;
        if (_onGround)
        {
            // On the ground he has no velocity: his animation moves him across it.
            Walk(world, ref position, across);
            _onGround = Snap(world, ref position, Rules.SnapDown);
            s.Velocity = Vector3.Zero;
        }
        else
        {
            // In the air his velocity falls by gravity and slides along what he touches, and his animation moves him besides,
            // but for the original's falling rule: falling fast, he neither steers nor is moved by it.
            var velocity = s.Velocity;
            if (velocity.Y <= Falling)
                across = Vector3.Zero;
            velocity.Y -= Body.Gravity;
            Fly(world, ref position, ref velocity, across);
            if (!_onGround && velocity.Y <= 0)
                _onGround = Snap(world, ref position, Skin * 4);
            s.Velocity = _onGround ? Vector3.Zero : velocity;
        }
        // A move no sum of which is a number, as off a triangle too thin to have one, is no move.
        if (!float.IsFinite(position.X + position.Y + position.Z))
            (position, s.Velocity) = (s.Position, Vector3.Zero);
        s.Position = position;
        body.KeepInBounds();
        if (_onGround)
            (s.Touching, s.ContactNormal, s.Surface, _groundHeight) = (true, _groundNormal, _groundSurface, position.Y);
        else if (!s.Touching)
            s.Surface = 0;
        s.Trod.Clear();
        SphereCast.Touched(world, s.Position, Radius, TriggerMargin, s.Trod);
    }

    /// <summary>
    /// His speed this tick from his animation's root motion, <paramref name="forward"/>: its mean over the last
    /// <see cref="ModernRules.Stride"/> ticks, or with none, the root's own. The root stops for the three ticks each state of the
    /// run is entered on, and pushes off at more than twice its mean a little later, which a frame of 1.0's sums but doesn't hide;
    /// the mean is the same speed, steady. It counts from the first tick the root moves in the run's loop, so he sets off as soon
    /// as the original would, and he stops as soon as his animation stops moving him. The start of a run or walk, and the rest of
    /// a step once the stick is let go, aren't steps of a loop, and move him by the root's own (<paramref name="own"/>): averaged,
    /// the start's push-off left him 0.2 behind his feet, which slid for a stride as he caught up.
    /// </summary>
    private float Speed(float forward, bool own)
    {
        var stride = Rules.Stride;
        if (stride <= 1 || own || (forward == 0 && (_strideCount == 0 || Body.Request == Body.Idle)))
        {
            (_strideCount, _strideNext, _strideSum) = (0, 0, 0);
            return forward;
        }
        if (_stride.Length != stride)
            (_stride, _strideCount, _strideNext, _strideSum) = (new float[stride], 0, 0, 0);
        if (_strideCount == stride)
            _strideSum -= _stride[_strideNext];
        else
            _strideCount++;
        _stride[_strideNext] = forward;
        _strideSum += forward;
        _strideNext = (_strideNext + 1) % stride;
        return _strideSum / _strideCount;
    }

    /// <summary>
    /// Moves him <paramref name="across"/> the ground: along its slope, onto the next ground it meets, up a ledge a wall stops
    /// him at if he can step onto it, and otherwise along the wall.
    /// </summary>
    private void Walk(CollisionWorld world, ref Vector3 position, Vector3 across)
    {
        var normal = _groundNormal;
        var stepped = false;
        var (from, wanted) = (position, across);
        Vector3? wall = null;
        var walls = 0;
        for (var i = 0; i < Slides && across.LengthSquared() > 1e-10f; i++)
        {
            var move = AlongGround(across, normal);
            if (!Cast(world, position, move, out var hit))
            {
                position += move;
                break;
            }
            position += Short(move, hit.Share);
            across *= 1 - hit.Share;
            Touch(hit);
            if (Ground(hit))
            {
                (normal, _groundSurface) = (hit.Normal, hit.Surface);
                continue;
            }
            if (!stepped)
            {
                stepped = true;
                if (StepUp(world, ref position, across, ref normal))
                    return;
            }
            if (wall is not { } first || Vector3.Dot(first, hit.Normal) < 0.9f)
                (wall, walls) = (hit.Normal, walls + 1);
            across = AlongCrease(AlongGround(across, normal), normal, hit.Normal) with { Y = 0 };
        }
        _groundNormal = normal;
        // Held between two walls, hardly moving.
        if (walls >= 2 && Vector3.Dot(position - from, wanted) < 0.25f * wanted.LengthSquared())
            Squeeze(world, ref position, wanted, Rules.StepHeight);
    }

    /// <summary>
    /// Lifts him between walls that hold him, as where a leaning wall narrows a passage toward the ground, to the least height up
    /// to <paramref name="most"/> where he fits through, and moves him <paramref name="across"/> there, off the ground: 1.0's
    /// sphere is lifted up the leaning wall itself, and passes the Roog Cave's mushroom so. Whether he could.
    /// </summary>
    private bool Squeeze(CollisionWorld world, ref Vector3 position, Vector3 across, float most)
    {
        for (var lift = SqueezeStep; lift <= MathF.Min(most, Rules.StepHeight) + 1e-3f; lift += SqueezeStep)
        {
            var up = new Vector3(0, lift, 0);
            if (Cast(world, position, up, out _))
                return false;
            if (Cast(world, position + up, across, out _))
                continue;
            position += up + across;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Up a ledge a wall stops him at, as 1.0's sphere is lifted over one lower than its radius: up by
    /// <see cref="ModernRules.StepHeight"/>, on <paramref name="across"/>, and down onto ground. Whether he could.
    /// </summary>
    private bool StepUp(CollisionWorld world, ref Vector3 position, Vector3 across, ref Vector3 normal)
    {
        var height = Rules.StepHeight;
        var up = new Vector3(0, height, 0);
        var lift = Cast(world, position, up, out var hit) ? Short(up, hit.Share).Y : height;
        if (lift < 0.05f)
            return false;
        var raised = position + new Vector3(0, lift, 0);
        var ahead = raised + (Cast(world, raised, across, out hit) ? Short(across, hit.Share) : across);
        if (new Vector2(ahead.X - raised.X, ahead.Z - raised.Z).LengthSquared() < 1e-8f)
            return false;
        // Down onto ground higher than he was, by no more than a step above his feet.
        var drop = new Vector3(0, -(lift + Skin * 2), 0);
        var landing = ahead;
        if (Land(world, ref landing, -drop.Y, out hit))
        {
            if (landing.Y < position.Y + MinStep || (landing - hit.Normal * Radius).Y > position.Y - Radius + height)
                return false;
            position = landing;
            (normal, _groundSurface) = (hit.Normal, hit.Surface);
            Touch(hit);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Keeps him to the ground within <paramref name="depth"/> below, moving him down onto it. Whether there's ground there: a
    /// wall below, as a steep plain face, isn't.
    /// </summary>
    private bool Snap(CollisionWorld world, ref Vector3 position, float depth)
    {
        if (!Land(world, ref position, depth, out var hit))
            return false;
        (_groundNormal, _groundSurface) = (hit.Normal, hit.Surface);
        Touch(hit);
        return true;
    }

    /// <summary>
    /// Moves him down onto ground within <paramref name="depth"/> below, if there's any: <paramref name="ground"/> is what he stands
    /// on. A wall he'd touch first doesn't stop him unless it holds him clear of the ground, so he stands in the crease where
    /// ground meets a wall, kept clear of both.
    /// </summary>
    private bool Land(CollisionWorld world, ref Vector3 position, float depth, out SphereCast.Hit ground)
    {
        var down = new Vector3(0, -depth, 0);
        if (!SphereCast.Cast(world, position, down, Radius, Sphere.IgnoresSeeThrough, _ground ??= Ground, out ground, only: _ground)
            || !Cast(world, position, down, out var first))
            return false;
        if ((ground.Share - first.Share) * depth > Skin * 2)
        {
            // Over the edge of ground with more ground a step below, as going down stairs, he stands on the edge till he's past
            // it; over a drop, he rolls off.
            if (first.OnFace || first.Normal.Y < LipUp || !Rules.Ground(first.Surface, first.Face.Y))
                return false;
            ground = first;
        }
        position += Short(down, first.Share);
        return true;
    }

    /// <summary>
    /// Moves him through the air by his <paramref name="velocity"/> and <paramref name="across"/>: landing on ground, and sliding
    /// along anything else, which takes away what goes into it without lifting him. Held between walls, he squeezes on through
    /// them as on the ground, to a step above the ground he last stood on.
    /// </summary>
    private void Fly(CollisionWorld world, ref Vector3 position, ref Vector3 velocity, Vector3 across)
    {
        var move = velocity + across;
        var from = position;
        Vector3? wall = null;
        var walls = 0;
        for (var i = 0; i < Slides && move.LengthSquared() > 1e-10f; i++)
        {
            if (!Cast(world, position, move, out var hit))
            {
                position += move;
                break;
            }
            position += Short(move, hit.Share);
            move *= 1 - hit.Share;
            Touch(hit);
            if (Ground(hit) && velocity.Y <= 0)
            {
                (_onGround, _groundNormal, _groundSurface) = (true, hit.Normal, hit.Surface);
                velocity = Vector3.Zero;
                move = AlongGround(move with { Y = 0 }, hit.Normal);
                continue;
            }
            if (wall is not { } first || Vector3.Dot(first, hit.Normal) < 0.9f)
                (wall, walls) = (hit.Normal, walls + 1);
            velocity = Off(velocity, hit.Normal);
            move = Off(move, hit.Normal);
        }
        if (!_onGround && walls >= 2 && across.LengthSquared() > 1e-10f && Vector3.Dot(position - from, across) < 0.25f * across.LengthSquared()
            && Squeeze(world, ref position, across, _groundHeight + Rules.StepHeight - position.Y))
            velocity = Vector3.Zero;
    }

    /// <summary>A move cut short where it touched, kept <see cref="Skin"/> back along it.</summary>
    private static Vector3 Short(Vector3 move, float share)
    {
        var length = move.Length();
        return length < 1e-6f ? Vector3.Zero : move * (MathF.Max(0, share * length - Skin) / length);
    }

    private bool Cast(CollisionWorld world, Vector3 from, Vector3 move, out SphereCast.Hit hit) =>
        SphereCast.Cast(world, from, move, Radius, Sphere.IgnoresSeeThrough, _ground ??= Ground, out hit);

    private void Touch(SphereCast.Hit hit) => (Sphere.Touching, Sphere.ContactNormal) = (true, hit.Normal);

    /// <summary>
    /// A move across the ground, tilted to follow ground facing <paramref name="normal"/>, its distance across kept. Up over a
    /// lip steeper than <see cref="SteepestMove"/>, it's shortened along the lip instead, and down one, it drops no steeper.
    /// </summary>
    private static Vector3 AlongGround(Vector3 across, Vector3 normal)
    {
        var distance = new Vector2(across.X, across.Z).Length();
        var rise = -(normal.X * across.X + normal.Z * across.Z) / MathF.Max(normal.Y, LipUp);
        var most = SteepestMove * distance;
        if (rise > most)
            return (across with { Y = rise }) * (most / rise);
        return across with { Y = MathF.Max(rise, -most) };
    }

    /// <summary>
    /// A move along the ground against a wall: along the crease where the ground facing <paramref name="ground"/> meets the wall, so
    /// what goes into the wall is taken away and he stays on the ground, never lifted up the wall (docs/systems/movement.md#modern-movement).
    /// </summary>
    private static Vector3 AlongCrease(Vector3 move, Vector3 ground, Vector3 wall)
    {
        if (Vector3.Dot(move, wall) >= 0)
            return move;
        var crease = Vector3.Cross(ground, wall);
        var length = crease.Length();
        if (length < 1e-4f)
            return Vector3.Zero;
        crease /= length;
        return crease * Vector3.Dot(move, crease);
    }

    /// <summary>
    /// A move across the ground against a wall: what goes into it across the ground is taken away, and nothing is added, so the
    /// wall never lifts him (docs/systems/movement.md#modern-movement).
    /// </summary>
    private static Vector3 AlongWall(Vector3 across, Vector3 normal)
    {
        var flat = new Vector3(normal.X, 0, normal.Z);
        var length = flat.Length();
        if (length < 1e-4f)
            return across;
        flat /= length;
        var into = Vector3.Dot(across, flat);
        return into < 0 ? across - flat * into : across;
    }

    /// <summary>
    /// A move or velocity through the air off what it touched: across the ground as along a wall, and its fall, if into it, along it.
    /// </summary>
    private static Vector3 Off(Vector3 move, Vector3 normal)
    {
        var across = AlongWall(move with { Y = 0 }, normal);
        var fall = new Vector3(0, move.Y, 0);
        var into = Vector3.Dot(fall, normal);
        if (into < 0)
            fall -= normal * into;
        return across + fall;
    }
}
