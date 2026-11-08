using System.Numerics;
using HBHT.Core.Cameras;

namespace HBHT.Core.Combat;

/// <summary>
/// Finds a battle's shots for the enhanced camera (<see cref="EnhancedCamera"/>): one holding a set of subjects, from a way that hides
/// none of them behind the arena or a body, turning the view as little as it can; the original's shot kept clear; and an aim's whole
/// reach. It only looks: it holds no shot of its own, so a director (Start Early's) can ask it for shots as it likes.
/// </summary>
public sealed class ShotFinder(SightLines sight, CameraTuning tuning)
{
    public SightLines Sight { get; } = sight;

    public CameraTuning Tuning { get; set; } = tuning;

    /// <summary>
    /// A shot of the subjects from a yaw and pitch: aimed at their middle, as near as holds them all, within
    /// <see cref="CameraTuning.MinDistance"/> and <paramref name="most"/> (<see cref="CameraTuning.MaxDistance"/> unless given).
    /// </summary>
    public Shot Framed(IReadOnlyList<ShotSubject> subjects, float yaw, float pitch, float? most = null)
    {
        var t = Tuning;
        var aim = ShotFraming.Middle(subjects);
        var distance = Math.Clamp(ShotFraming.DistanceToHold(subjects, aim, yaw, pitch, t.Lens), t.MinDistance, most ?? t.MaxDistance);
        return new Shot(aim, yaw, pitch, distance);
    }

    /// <summary>
    /// The best shot holding <paramref name="subjects"/>: of <see cref="CameraTuning.Yaws"/> turned from <paramref name="from"/> and
    /// <see cref="CameraTuning.Pitches"/>, the one scoring best (<see cref="Score"/>). Null only for no subjects.
    /// </summary>
    /// <param name="from">The yaw the view has now, which turning from costs.</param>
    /// <param name="behind">A yaw an eye is liked toward, if any: the character's side, away from the target.</param>
    public (Shot Shot, float Score)? Best(IReadOnlyList<ShotSubject> subjects, float from, float? behind = null)
    {
        if (subjects.Count == 0)
            return null;
        var t = Tuning;
        (Shot, float)? best = null;
        foreach (var dy in t.Yaws)
        {
            foreach (var p in t.Pitches)
            {
                var shot = Framed(subjects, Shot.Wrap(from + Rad(dy)), Rad(p));
                var score = Score(shot, subjects, from, behind);
                if (best is not { } b || score > b.Item2)
                    best = (shot, score);
            }
        }
        return best;
    }

    /// <summary>
    /// How well a shot shows its subjects: less <see cref="CameraTuning.OcclusionPenalty"/> for each hidden or out of frame, by its weight,
    /// less for each radian it turns the view from <paramref name="from"/>, for its pitch's distance from the one liked best and for its
    /// distance; more for an eye toward <paramref name="behind"/>. A shot from inside the ground scores nothing at all.
    /// </summary>
    public float Score(Shot shot, IReadOnlyList<ShotSubject> subjects, float from, float? behind = null)
    {
        var t = Tuning;
        if (UnderGround(shot.Eye))
            return float.NegativeInfinity;
        var score = -t.OcclusionPenalty * HiddenWeight(shot, subjects)
            - t.TurnCost * MathF.Abs(Shot.Wrap(shot.Yaw - from))
            - t.PitchCost * MathF.Abs(shot.Pitch - Rad(t.PreferredPitch))
            - t.DistanceCost * shot.Distance;
        if (behind is { } b)
            score += t.BehindBonus * MathF.Cos(Shot.Wrap(shot.Yaw - b));
        foreach (var s in subjects)
        {
            if (!ShotFraming.Holds(shot, s with { Radius = 0 }, t.Lens))
                score -= t.OcclusionPenalty * s.Weight;
        }
        return score;
    }

    /// <summary>
    /// The original's shot <paramref name="original"/> turned, raised and drawn back by <paramref name="by"/> (radians, radians, times its
    /// distance), still holding <paramref name="held"/>.
    /// </summary>
    public Shot Corrected(Shot original, (float Yaw, float Pitch, float Scale) by, IReadOnlyList<ShotSubject> held)
    {
        var yaw = original.Yaw + by.Yaw;
        var pitch = Math.Clamp(original.Pitch + by.Pitch, Rad(-30), Rad(85));
        var distance = MathF.Max(original.Distance * by.Scale, ShotFraming.DistanceToHold(held, original.Aim, yaw, pitch, Tuning.Lens));
        return new Shot(original.Aim, yaw, pitch, distance);
    }

    /// <summary>
    /// The least change to the original's shot (of <see cref="CameraTuning.EventYaws"/>, <see cref="CameraTuning.EventPitches"/> and
    /// <see cref="CameraTuning.EventScales"/>) that hides less of <paramref name="held"/> than it does, or null if none does.
    /// </summary>
    public (float Yaw, float Pitch, float Scale)? Clearing(Shot original, IReadOnlyList<ShotSubject> held)
    {
        var t = Tuning;
        var hiddenNow = HiddenWeight(original, held);
        (float, float, float)? best = null;
        var bestScore = float.NegativeInfinity;
        foreach (var dy in t.EventYaws)
        {
            foreach (var dp in t.EventPitches)
            {
                foreach (var scale in t.EventScales)
                {
                    var by = (Rad(dy), Rad(dp), scale);
                    var shot = Corrected(original, by, held);
                    if (UnderGround(shot.Eye))
                        continue;
                    var hidden = HiddenWeight(shot, held);
                    var score = -t.OcclusionPenalty * hidden - t.TurnCost * MathF.Abs(Rad(dy)) - t.PitchCost * Rad(dp) - t.DistanceCost * shot.Distance;
                    if (hidden < hiddenNow && score > bestScore)
                    {
                        bestScore = score;
                        best = by;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>The weight of the subjects a shot can't see, behind the arena or a body.</summary>
    public float HiddenWeight(Shot shot, IReadOnlyList<ShotSubject> subjects)
    {
        var eye = shot.Eye;
        var hidden = 0f;
        foreach (var s in subjects)
        {
            if (Sight.Hidden(eye, s))
                hidden += s.Weight;
        }
        return hidden;
    }

    /// <summary>Whether an eye is below the ground, or less than half the clearance above it.</summary>
    public bool UnderGround(Vector3 eye) =>
        Sight.GroundAt(eye.X, eye.Z, eye.Y + 60) is { } ground && eye.Y < ground + Tuning.FloorClearance * 0.5f;

    internal static float Rad(float degrees) => degrees * MathF.PI / 180;
}

/// <summary>A body the player might be pointing at, and whether it's on the other side.</summary>
public readonly record struct TargetCandidate(object Who, Vector3 At, bool Opponent);

/// <summary>
/// Whom the player points at in their turn: of the bodies in a cone about the way the character points, enemies and party alike, the
/// likeliest by how near the cone's middle it is and by its distance, nearer for a melee weapon and farther for a ranged one or a staff.
/// The target held stays while it's in a wider cone, and another takes its place only once it has been clearly likelier for a while, so
/// the stick's wobble doesn't flick the camera between two bodies.
/// </summary>
public sealed class TargetCone
{
    private object? _challenger;
    private int _challengerTicks;

    /// <summary>The target held, or null.</summary>
    public object? Target { get; private set; }

    /// <summary>Forgets the target.</summary>
    public void Clear()
    {
        Target = null;
        _challenger = null;
    }

    /// <summary>A body's likelihood as the target, or null outside the cone (<see cref="CameraTuning.ConeHold"/>'s for the one held).</summary>
    public float? Likelihood(Vector3 me, Vector2 heading, bool far, TargetCandidate c, CameraTuning t)
    {
        var off = c.At - me;
        var flat = new Vector2(off.X, off.Z);
        var d = flat.Length();
        if (d < 0.1f || heading.LengthSquared() < 1e-6f)
            return null;
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(heading), flat / d), -1, 1));
        if (angle > ShotFinder.Rad(Equals(c.Who, Target) ? t.ConeHold : t.ConeAngle))
            return null;
        var middle = 1 - MathF.Pow(angle / ShotFinder.Rad(t.ConeAngle), 2);
        var distance = far ? d / (d + t.FarScale) : MathF.Exp(-d / t.NearScale);
        return (c.Opponent ? 1 : t.PartyWeight) * (middle + distance);
    }

    /// <summary>A tick: picks again from <paramref name="candidates"/>. Returns whether the target changed.</summary>
    /// <param name="me">Where the character is.</param>
    /// <param name="heading">The way it points.</param>
    /// <param name="far">Whether its weapon is a ranged one or a staff.</param>
    public bool Pick(Vector3 me, Vector2 heading, bool far, IEnumerable<TargetCandidate> candidates, CameraTuning t)
    {
        if (heading.LengthSquared() < 1e-6f)
            return false;
        object? best = null;
        var bestScore = float.NegativeInfinity;
        float? held = null;
        foreach (var c in candidates)
        {
            if (Likelihood(me, heading, far, c, t) is not { } score)
                continue;
            if (Equals(c.Who, Target))
                held = score;
            if (score > bestScore)
            {
                bestScore = score;
                best = c.Who;
            }
        }
        if (held is not { } heldScore)
        {
            var changed = !Equals(Target, best);
            Target = best;
            _challenger = null;
            return changed;
        }
        if (best is null || Equals(best, Target) || bestScore <= heldScore + t.TargetMargin)
        {
            _challenger = null;
            return false;
        }
        if (!Equals(_challenger, best))
        {
            _challenger = best;
            _challengerTicks = 0;
        }
        if (++_challengerTicks < t.TargetHold)
            return false;
        Target = best;
        _challenger = null;
        return true;
    }
}

/// <summary>
/// An aim's shot (<see cref="EnhancedCamera"/>): it holds the caster and every point of its reach, from a pitch at the yaw the aim began
/// with, at most <see cref="CameraTuning.MaxAimDistance"/> away, and doesn't chase the reticle: it moves only as the reticle nears the
/// screen's edge (<see cref="CameraTuning.EdgeMargin"/>), and only as far as keeps it inside. Anywhere in the reach it held at first
/// it moves nothing.
/// </summary>
public sealed class AimFraming(IReadOnlyList<ShotSubject> reach, float yaw, float pitch)
{
    private Vector3 _shift;

    /// <summary>The caster and the points round its reach that the shot holds.</summary>
    public IReadOnlyList<ShotSubject> Reach { get; } = reach;

    public float Yaw { get; } = yaw;

    public float Pitch { get; } = pitch;

    /// <summary>The shot, followed as far as the reticle at <paramref name="reticle"/>, of radius <paramref name="radius"/>, needs.</summary>
    public Shot Step(Vector3 reticle, float radius, CameraTuning t)
    {
        var lens = t.Lens;
        var aim = ShotFraming.Middle(Reach);
        var distance = Math.Clamp(ShotFraming.DistanceToHold(Reach, aim, Yaw, Pitch, lens), t.MinDistance, t.MaxAimDistance);
        var shot = new Shot(aim + _shift, Yaw, Pitch, distance);
        // Measured on the whole screen, not the lens's margins: the reach is held within those, so it never reaches the edge.
        var screen = lens with { Margin = 1 };
        var (x, y, depth) = ShotFraming.OnScreen(shot, reticle, screen);
        if (depth <= 0 || !float.IsFinite(x))
            return shot;
        var rx = radius / (depth * screen.TanH);
        var ry = radius / (depth * screen.TanV);
        var edge = t.EdgeMargin;
        var shift = Vector3.Zero;
        if (MathF.Abs(x) + rx > edge)
            shift += shot.Right * ((MathF.Abs(x) + rx - edge) * depth * screen.TanH * MathF.Sign(x));
        if (MathF.Abs(y) + ry > edge)
        {
            // Along the ground the way the screen's up goes: a step that way lowers the reticle on screen by the sine of the pitch.
            var ahead = new Vector3(shot.LookXZ.X, 0, shot.LookXZ.Y);
            shift += ahead * ((MathF.Abs(y) + ry - edge) * depth * screen.TanV * MathF.Sign(y) / MathF.Max(MathF.Sin(Pitch), 0.2f));
        }
        _shift += shift;
        return shot with { Aim = aim + _shift };
    }
}
