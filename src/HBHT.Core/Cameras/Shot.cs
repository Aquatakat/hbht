using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Cameras;

/// <summary>
/// A camera shot as a point it looks at and the way and distance it looks from: the eye is <see cref="Distance"/> from
/// <see cref="Aim"/>, turned <see cref="Yaw"/> about the up axis and raised <see cref="Pitch"/> above level, both in radians. A yaw
/// of 0 puts the eye on +Z of the aim, looking toward −Z. The enhanced camera moves a shot by these four, so a swing round a subject
/// keeps its distance from it, and a turn of the view is a turn of the yaw and pitch alone (<see cref="CappedMotion"/>).
/// </summary>
public readonly record struct Shot(Vector3 Aim, float Yaw, float Pitch, float Distance)
{
    /// <summary>The way from the aim to the eye, a unit long.</summary>
    public Vector3 Back => Way(Yaw, Pitch);

    /// <summary>Where the eye is.</summary>
    public Vector3 Eye => Aim + Distance * Back;

    /// <summary>The way it looks, a unit long.</summary>
    public Vector3 Forward => -Back;

    /// <summary>The screen's right across the ground, a unit long, defined even looking straight down.</summary>
    public Vector3 Right => new(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));

    /// <summary>The screen's up.</summary>
    public Vector3 Up => Vector3.Cross(Right, Forward);

    /// <summary>
    /// The way it looks across the ground, a unit long (<see cref="FollowCamera.LookXZ"/>): what the stick is turned by, so its up is
    /// the screen's up, even in a shot from straight above.
    /// </summary>
    public Vector2 LookXZ => new(-MathF.Sin(Yaw), -MathF.Cos(Yaw));

    /// <summary>The way from an aim to an eye at a yaw and pitch.</summary>
    public static Vector3 Way(float yaw, float pitch)
    {
        var (sp, cp) = MathF.SinCos(pitch);
        var (sy, cy) = MathF.SinCos(yaw);
        return new Vector3(cp * sy, sp, cp * cy);
    }

    /// <summary>The shot from <paramref name="eye"/> at <paramref name="aim"/>; straight above or below it keeps <paramref name="yaw"/>.</summary>
    public static Shot Of(Vector3 eye, Vector3 aim, float yaw = 0)
    {
        var d = eye - aim;
        var flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        if (flat > 1e-4f)
            yaw = MathF.Atan2(d.X, d.Z);
        return new Shot(aim, yaw, MathF.Atan2(d.Y, flat), d.Length());
    }

    /// <summary>The yaw whose <see cref="LookXZ"/> is <paramref name="look"/>.</summary>
    public static float YawOf(Vector2 look) => MathF.Atan2(-look.X, -look.Y);

    /// <summary>An angle brought within a half turn either way.</summary>
    public static float Wrap(float angle)
    {
        angle %= MathF.Tau;
        return angle > MathF.PI ? angle - MathF.Tau : angle < -MathF.PI ? angle + MathF.Tau : angle;
    }
}

/// <summary>
/// What a shot is seen through: its vertical field of view in degrees, the narrowest screen's width over its height, and how much of
/// the half-width and half-height a subject must sit within (<see cref="Margin"/>), so nothing is framed against the very edge.
/// </summary>
public readonly record struct Lens(float FieldOfView, float Aspect, float Margin)
{
    /// <summary>The runtime's camera: 45° high (<c>CameraRig</c>), judged on a 4:3 screen, a subject kept within 85% of the edge.</summary>
    public static Lens Runtime { get; } = new(45, 4f / 3, 0.85f);

    /// <summary>The tangent of the half-height a subject sits within.</summary>
    public float TanV => MathF.Tan(FieldOfView * MathF.PI / 360) * Margin;

    /// <summary>The tangent of the half-width a subject sits within.</summary>
    public float TanH => TanV * Aspect;
}

/// <summary>Something a shot is to hold: a sphere, and how much it matters.</summary>
public readonly record struct ShotSubject(Vector3 At, float Radius = 0, float Weight = 1);

/// <summary>How shots hold their subjects: the distance that frames them, and where a point falls on the screen.</summary>
public static class ShotFraming
{
    /// <summary>How near a subject may come to the eye, beyond its radius.</summary>
    public const float Near = 0.5f;

    /// <summary>
    /// The least distance from <paramref name="aim"/>, looking from <paramref name="yaw"/> and <paramref name="pitch"/>, at which every
    /// subject is within the lens's margins and in front of the eye.
    /// </summary>
    public static float DistanceToHold(IEnumerable<ShotSubject> subjects, Vector3 aim, float yaw, float pitch, Lens lens)
    {
        var shot = new Shot(aim, yaw, pitch, 0);
        var (f, r, u) = (shot.Forward, shot.Right, shot.Up);
        var (tanH, tanV) = (lens.TanH, lens.TanV);
        var most = 0f;
        foreach (var s in subjects)
        {
            var q = s.At - aim;
            var depth = Vector3.Dot(q, f);
            // The eye at D back along f sees q at depth D + depth, across x and up y; each must fit its tangent there.
            var need = MathF.Max((MathF.Abs(Vector3.Dot(q, r)) + s.Radius) / tanH, (MathF.Abs(Vector3.Dot(q, u)) + s.Radius) / tanV) - depth;
            most = MathF.Max(most, MathF.Max(need, Near + s.Radius - depth));
        }
        return most;
    }

    /// <summary>
    /// Where <paramref name="point"/> falls in <paramref name="shot"/>: across and up as parts of the lens's margins (within ±1 is held),
    /// and how far in front of the eye it is.
    /// </summary>
    public static (float X, float Y, float Depth) OnScreen(Shot shot, Vector3 point, Lens lens)
    {
        var v = point - shot.Eye;
        var depth = Vector3.Dot(v, shot.Forward);
        if (depth < 1e-3f)
            return (float.PositiveInfinity, float.PositiveInfinity, depth);
        return (Vector3.Dot(v, shot.Right) / (depth * lens.TanH), Vector3.Dot(v, shot.Up) / (depth * lens.TanV), depth);
    }

    /// <summary>Whether a subject is wholly within the lens's margins.</summary>
    public static bool Holds(Shot shot, ShotSubject s, Lens lens)
    {
        var v = s.At - shot.Eye;
        var depth = Vector3.Dot(v, shot.Forward);
        if (depth < Near + s.Radius)
            return false;
        return MathF.Abs(Vector3.Dot(v, shot.Right)) + s.Radius <= depth * lens.TanH * 1.001f
            && MathF.Abs(Vector3.Dot(v, shot.Up)) + s.Radius <= depth * lens.TanV * 1.001f;
    }

    /// <summary>The middle of the box round the subjects' spheres.</summary>
    public static Vector3 Middle(IReadOnlyList<ShotSubject> subjects)
    {
        if (subjects.Count == 0)
            return Vector3.Zero;
        var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var s in subjects)
        {
            min = Vector3.Min(min, s.At - new Vector3(s.Radius));
            max = Vector3.Max(max, s.At + new Vector3(s.Radius));
        }
        return (min + max) / 2;
    }
}

/// <summary>
/// Whether something stands between an eye and a subject: the world's collision, see-through triangles passed over, or a body drawn
/// across the sight line, taken as <see cref="BodyScale"/> times its radius for limbs and wings. And where the ground is under a point,
/// so a shot isn't taken from inside a hill.
/// </summary>
public sealed class SightLines(CollisionWorld? world)
{
    /// <summary>The bodies that may stand in the way, their spheres where they're drawn.</summary>
    public List<ShotSubject> Bodies { get; } = [];

    /// <summary>The sphere a sight line is swept with.</summary>
    public float Radius { get; set; } = 0.1f;

    /// <summary>How much bigger than its sphere a body hides what's behind it; 0 for bodies hiding nothing.</summary>
    public float BodyScale { get; set; } = 1.2f;

    /// <summary>
    /// Whether the world's collision or a body hides <paramref name="s"/> from <paramref name="eye"/>: its upper half, the sight line to
    /// its middle or to its top. Its lower half hidden, behind a rise or another's shoulder, it's still seen.
    /// </summary>
    public bool Hidden(Vector3 eye, ShotSubject s) =>
        HiddenByBody(eye, s) || HiddenByWorld(eye, s)
        || s.Radius > 0 && (HiddenByBody(eye, s, Top(s)) || HiddenByWorld(eye, s, Top(s)));

    /// <summary>The top of a subject's sphere, a little inside it.</summary>
    private static Vector3 Top(ShotSubject s) => s.At + new Vector3(0, s.Radius * 0.9f, 0);

    /// <summary>Whether another body is drawn across the sight line to <paramref name="s"/>.</summary>
    public bool HiddenByBody(Vector3 eye, ShotSubject s) => HiddenByBody(eye, s, End(eye, s));

    /// <summary>Whether another body is drawn across the sight line to <paramref name="end"/>, a point of <paramref name="s"/>.</summary>
    private bool HiddenByBody(Vector3 eye, ShotSubject s, Vector3 end)
    {
        if (BodyScale <= 0)
            return false;
        var line = end - eye;
        var reach = line.LengthSquared();
        if (reach < 1e-6f)
            return false;
        foreach (var b in Bodies)
        {
            // The subject itself, or one standing where it does, doesn't hide it.
            if (Vector3.Distance(b.At, s.At) <= s.Radius + 0.05f)
                continue;
            var along = Math.Clamp(Vector3.Dot(b.At - eye, line) / reach, 0, 1);
            var r = b.Radius * BodyScale;
            if (along > 0 && Vector3.DistanceSquared(eye + line * along, b.At) < r * r)
                return true;
        }
        return false;
    }

    /// <summary>Whether a sight line swept to just short of <paramref name="s"/> meets the world's collision facing it.</summary>
    public bool HiddenByWorld(Vector3 eye, ShotSubject s) => HiddenByWorld(eye, s, End(eye, s));

    private bool HiddenByWorld(Vector3 eye, ShotSubject s, Vector3 end) =>
        world is not null && (eye - s.At).Length() > s.Radius + Radius && SpherePhysics.Blocked(world, eye, end, Radius, skipSeeThrough: true);

    /// <summary>Where a sight line to a subject ends: just inside its sphere's near side.</summary>
    private static Vector3 End(Vector3 eye, ShotSubject s)
    {
        var way = s.At - eye;
        var length = way.Length();
        return length > 1e-4f ? s.At - way / length * (s.Radius * 0.9f) : s.At;
    }

    /// <summary>
    /// The ground's height under (<paramref name="x"/>, <paramref name="z"/>): the first floor a sphere swept down from
    /// <paramref name="top"/> meets, or null if there's none within 200 below it.
    /// </summary>
    public float? GroundAt(float x, float z, float top)
    {
        if (world is null)
            return null;
        return SpherePhysics.SweepNearest(world, new Vector3(x, top, z), new Vector3(x, top - 200, z), 0.25f, skipSeeThrough: true, minUp: 0.3f,
            out var point, out _) ? point.Y : null;
    }
}
