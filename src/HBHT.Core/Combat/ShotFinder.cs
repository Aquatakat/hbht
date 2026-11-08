using System.Numerics;
using HBHT.Core.Cameras;

namespace HBHT.Core.Combat;

/// <summary>
/// Finds a battle's shots for the enhanced camera (<see cref="EnhancedCamera"/>): over a character's shoulder at its target, low unless
/// only a higher one sees it; one shot holding an AI's whole turn, turned least from the shot before and the player's turn after; the
/// original's shot kept clear; and an aim with its reticle in the middle. It only looks: it holds no shot of its own, so a director
/// (Start Early's) can ask it for shots as it likes.
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
    /// The best shot holding <paramref name="subjects"/>: of <see cref="CameraTuning.Yaws"/> turned from <paramref name="behind"/> (or from
    /// <paramref name="from"/>, without one) and <see cref="CameraTuning.Pitches"/>, the one scoring best (<see cref="Score"/>). Null only
    /// for no subjects.
    /// </summary>
    /// <param name="from">The yaw the view has now, which turning from costs.</param>
    /// <param name="behind">The yaw over the character's shoulder at its target, which turning from costs too.</param>
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
                var shot = Framed(subjects, Shot.Wrap((behind ?? from) + Rad(dy)), Rad(p));
                var score = Score(shot, subjects, from, behind);
                if (best is not { } b || score > b.Item2)
                    best = (shot, score);
            }
        }
        return best;
    }

    /// <summary>
    /// How well a shot shows its subjects (<see cref="Seen"/>), less <see cref="CameraTuning.TurnCost"/> for each radian it turns the view
    /// from <paramref name="from"/> and <see cref="CameraTuning.ShoulderCost"/> for each it's turned from <paramref name="behind"/>.
    /// </summary>
    public float Score(Shot shot, IReadOnlyList<ShotSubject> subjects, float from, float? behind = null)
    {
        var t = Tuning;
        var score = Seen(shot, subjects, t.PreferredPitch) - t.TurnCost * MathF.Abs(Shot.Wrap(shot.Yaw - from));
        if (behind is { } b)
            score -= t.ShoulderCost * MathF.Abs(Shot.Wrap(shot.Yaw - b));
        return score;
    }

    /// <summary>
    /// How well a shot shows its subjects, whichever way it looks: less <see cref="CameraTuning.OcclusionPenalty"/> for each hidden or out
    /// of frame, by its weight, and less for its pitch's distance from <paramref name="pitch"/> (degrees) and for its distance. A shot from
    /// inside the ground scores nothing at all.
    /// </summary>
    public float Seen(Shot shot, IReadOnlyList<ShotSubject> subjects, float pitch)
    {
        var t = Tuning;
        if (UnderGround(shot.Eye))
            return float.NegativeInfinity;
        var score = -t.OcclusionPenalty * HiddenWeight(shot, subjects)
            - t.PitchCost * MathF.Abs(shot.Pitch - Rad(pitch))
            - t.DistanceCost * shot.Distance;
        foreach (var s in subjects)
        {
            if (!ShotFraming.Holds(shot, s with { Radius = 0 }, t.Lens))
                score -= t.OcclusionPenalty * s.Weight;
        }
        return score;
    }

    /// <summary>
    /// The one shot for an AI's turn, holding all of it (<paramref name="subjects"/>: where it stands, where it walks to and whom it acts
    /// on): of yaws all the way round (<see cref="CameraTuning.PlannedYawStep"/>) and <see cref="CameraTuning.PlannedPitches"/>, the one
    /// seeing them best that turns least from the shot now (<paramref name="from"/>) and from the next player's turn's
    /// (<paramref name="next"/>). Null only for no subjects.
    /// </summary>
    public (Shot Shot, float Score)? Planned(IReadOnlyList<ShotSubject> subjects, float from, float next)
    {
        if (subjects.Count == 0)
            return null;
        var t = Tuning;
        (Shot, float)? best = null;
        var steps = Math.Max(1, (int)MathF.Round(360 / MathF.Max(t.PlannedYawStep, 1)));
        for (var i = 0; i < steps; i++)
        {
            // From the shot now's yaw round, so with nothing to choose between them it stays.
            var yaw = Shot.Wrap(from + MathF.Tau * i / steps);
            foreach (var p in t.PlannedPitches)
            {
                var shot = Framed(subjects, yaw, Rad(p));
                var score = Seen(shot, subjects, t.PlannedPitch)
                    - t.TurnCost * MathF.Abs(Shot.Wrap(yaw - from))
                    - t.NextTurnCost * MathF.Abs(Shot.Wrap(yaw - next));
                if (best is not { } b || score > b.Item2)
                    best = (shot, score);
            }
        }
        return best;
    }

    /// <summary>
    /// An aim's shot: the reticle at <paramref name="reticle"/> in the middle of the screen, from <paramref name="yaw"/> and
    /// <paramref name="pitch"/>, <see cref="CameraTuning.AimDistance"/> away, or as far as holds <paramref name="radius"/> round it.
    /// </summary>
    public Shot Aimed(Vector3 reticle, float radius, float yaw, float pitch)
    {
        var t = Tuning;
        var around = ShotFraming.DistanceToHold([new ShotSubject(reticle, radius)], reticle, yaw, pitch, t.Lens);
        return new Shot(reticle, yaw, pitch, MathF.Max(t.AimDistance, around));
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

    /// <summary>The weight of the subjects a shot can't see, behind the arena or a body (<see cref="SightLines.Hidden"/>).</summary>
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

/// <summary>An enemy the player might be heading for.</summary>
public readonly record struct TargetCandidate(object Who, Vector3 At);

/// <summary>
/// Whom the player heads for in their turn: of the enemies in a cone about the way the character points (the way it walks, as it walks),
/// the likeliest by how near the cone's middle it is and how near the character. The target held stays while it's in a wider cone, and
/// another takes its place only once it has been clearly likelier for a while, so the stick's wobble doesn't flick the camera between two
/// bodies.
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

    /// <summary>An enemy's likelihood as the target, or null outside the cone (<see cref="CameraTuning.ConeHold"/>'s for the one held).</summary>
    public float? Likelihood(Vector3 me, Vector2 heading, TargetCandidate c, CameraTuning t)
    {
        var off = c.At - me;
        var flat = new Vector2(off.X, off.Z);
        var d = flat.Length();
        if (d < 0.1f || heading.LengthSquared() < 1e-6f)
            return null;
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(heading), flat / d), -1, 1));
        if (angle > ShotFinder.Rad(Equals(c.Who, Target) ? t.ConeHold : t.ConeAngle))
            return null;
        return 1 - MathF.Pow(angle / ShotFinder.Rad(t.ConeAngle), 2) + MathF.Exp(-d / t.NearScale);
    }

    /// <summary>A tick: picks again from <paramref name="candidates"/>. Returns whether the target changed.</summary>
    /// <param name="me">Where the character is.</param>
    /// <param name="heading">The way it points.</param>
    public bool Pick(Vector3 me, Vector2 heading, IEnumerable<TargetCandidate> candidates, CameraTuning t)
    {
        if (heading.LengthSquared() < 1e-6f)
            return false;
        object? best = null;
        var bestScore = float.NegativeInfinity;
        float? held = null;
        foreach (var c in candidates)
        {
            if (Likelihood(me, heading, c, t) is not { } score)
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
