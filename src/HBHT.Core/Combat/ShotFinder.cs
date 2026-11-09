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
    /// The battlefield across the ground, its least and most corners, or null for none. An eye beyond it by more than
    /// <see cref="CameraTuning.FieldMargin"/> sees nothing (<see cref="Outside"/>): the arena's walls and cliffs round it are scenery
    /// without collision, which the sight lines can't see through.
    /// </summary>
    public (Vector2 Min, Vector2 Max)? Field { get; set; }

    /// <summary>Whether an eye is beyond the battlefield (<see cref="Field"/>).</summary>
    public bool Outside(Vector3 eye) =>
        Field is var (min, max) && (eye.X < min.X - Tuning.FieldMargin || eye.Z < min.Y - Tuning.FieldMargin
            || eye.X > max.X + Tuning.FieldMargin || eye.Z > max.Y + Tuning.FieldMargin);

    /// <summary>
    /// A shot of the subjects from a yaw and pitch: aimed at their middle (or at <paramref name="aim"/>), as near as holds them all, within
    /// <see cref="CameraTuning.MinDistance"/> and <paramref name="most"/> (<see cref="CameraTuning.MaxDistance"/> unless given).
    /// </summary>
    public Shot Framed(IReadOnlyList<ShotSubject> subjects, float yaw, float pitch, float? most = null, Vector3? aim = null)
    {
        var t = Tuning;
        var at = aim ?? ShotFraming.Middle(subjects);
        var distance = Math.Clamp(ShotFraming.DistanceToHold(subjects, at, yaw, pitch, t.Lens), t.MinDistance, most ?? t.MaxDistance);
        return new Shot(at, yaw, pitch, distance);
    }

    /// <summary>
    /// The best shot holding <paramref name="subjects"/>: of <see cref="CameraTuning.Yaws"/> turned from <paramref name="behind"/> (or from
    /// <paramref name="from"/>, without one) and <see cref="CameraTuning.Pitches"/>, the one scoring best (<see cref="Score"/>), each
    /// framed as <see cref="Framed"/> frames it. Null only for no subjects.
    /// </summary>
    /// <param name="from">The yaw the view has now, which turning from costs.</param>
    /// <param name="behind">The yaw over the character's shoulder at its target, which turning from costs too.</param>
    /// <param name="sides">Who should be framed to the right of whom (<see cref="Sides"/>).</param>
    /// <param name="aim">What the shots aim at, rather than the subjects' middle: near the character, over the shoulder.</param>
    /// <param name="most">The farthest the eye goes from the aim.</param>
    /// <param name="yaws">The turns tried, degrees, if not <see cref="CameraTuning.Yaws"/>.</param>
    public (Shot Shot, float Score)? Best(IReadOnlyList<ShotSubject> subjects, float from, float? behind = null, IReadOnlyList<ScreenSide>? sides = null,
        Vector3? aim = null, float? most = null, IReadOnlyList<float>? yaws = null)
    {
        if (subjects.Count == 0)
            return null;
        var t = Tuning;
        (Shot, float)? best = null;
        foreach (var dy in yaws ?? t.Yaws)
        {
            foreach (var p in t.Pitches)
            {
                var shot = Framed(subjects, Shot.Wrap((behind ?? from) + Rad(dy)), Rad(p), most, aim);
                var score = Score(shot, subjects, from, behind, sides);
                if (best is not { } b || score > b.Item2)
                    best = (shot, score);
            }
        }
        return best;
    }

    /// <summary>
    /// How well a shot shows its subjects (<see cref="Seen"/>), less <see cref="CameraTuning.TurnCost"/> for each radian it turns the view
    /// from <paramref name="from"/> and <see cref="CameraTuning.ShoulderCost"/> for each it's turned from <paramref name="behind"/>, and
    /// for framing anyone on the wrong side (<see cref="Sides"/>).
    /// </summary>
    public float Score(Shot shot, IReadOnlyList<ShotSubject> subjects, float from, float? behind = null, IReadOnlyList<ScreenSide>? sides = null)
    {
        var t = Tuning;
        var score = Seen(shot, subjects, t.PreferredPitch) - t.TurnCost * MathF.Abs(Shot.Wrap(shot.Yaw - from)) - Sides(shot, sides);
        if (behind is { } b)
            score -= t.ShoulderCost * MathF.Abs(Shot.Wrap(shot.Yaw - b));
        return score;
    }

    /// <summary>
    /// How well a shot shows its subjects, whichever way it looks: less <see cref="CameraTuning.OcclusionPenalty"/> for each hidden or out
    /// of frame, by its weight, and less for its pitch's distance from <paramref name="pitch"/> (degrees) and for its distance. A shot from
    /// inside the ground scores nothing at all, and one from beyond the battlefield (<see cref="Outside"/>) as though it saw none of them.
    /// </summary>
    public float Seen(Shot shot, IReadOnlyList<ShotSubject> subjects, float pitch)
    {
        var t = Tuning;
        if (UnderGround(shot.Eye))
            return float.NegativeInfinity;
        if (Outside(shot.Eye))
            return -t.OcclusionPenalty * subjects.Sum(s => s.Weight) * 2 - t.PitchCost * MathF.Abs(shot.Pitch - Rad(pitch));
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
    public (Shot Shot, float Score)? Planned(IReadOnlyList<ShotSubject> subjects, float from, float next, IReadOnlyList<ScreenSide>? sides = null)
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
                var score = Seen(shot, subjects, t.PlannedPitch) - Sides(shot, sides)
                    - t.TurnCost * MathF.Abs(Shot.Wrap(yaw - from))
                    - t.NextTurnCost * MathF.Abs(Shot.Wrap(yaw - next));
                if (best is not { } b || score > b.Item2)
                    best = (shot, score);
            }
        }
        return best;
    }

    /// <summary>
    /// What framing anyone short of their side costs: for each of <paramref name="sides"/>, <see cref="CameraTuning.SideCost"/> times how far
    /// short of <see cref="CameraTuning.SideAcross"/> right of the one it should be right of it's framed, as a share of the lens's
    /// half-width, up to a whole one. So a shot over the shoulder goes to that side of it, and the other side costs most.
    /// </summary>
    public float Sides(Shot shot, IReadOnlyList<ScreenSide>? sides)
    {
        if (sides is null)
            return 0;
        var cost = 0f;
        foreach (var side in sides)
            cost += Tuning.SideCost * Math.Clamp(Tuning.SideAcross - Across(shot, side, Tuning.Lens), 0, 1);
        return cost;
    }

    /// <summary>
    /// How far right of <see cref="ScreenSide.Left"/> a shot frames <see cref="ScreenSide.Right"/>, as a share of the lens's half-width; 0
    /// where either is behind the eye.
    /// </summary>
    public static float Across(Shot shot, ScreenSide side, Lens lens)
    {
        var right = ShotFraming.OnScreen(shot, side.Right, lens);
        var left = ShotFraming.OnScreen(shot, side.Left, lens);
        return float.IsFinite(right.X) && float.IsFinite(left.X) ? right.X - left.X : 0;
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
    /// An area's shot as it's placed: from <paramref name="yaw"/> and <paramref name="pitch"/>, its centre in the middle of the screen, as
    /// near as holds every point of its <paramref name="ring"/>, the nearest coming down to <see cref="CameraTuning.AreaFloor"/> of the
    /// half-height (the conversation box's top), but no nearer than <see cref="CameraTuning.MinDistance"/>.
    /// </summary>
    public Shot Placed(Vector3 centre, IReadOnlyList<ShotSubject> ring, float yaw, float pitch) =>
        new(centre, yaw, pitch, MathF.Max(Tuning.MinDistance, ShotFraming.DistanceToHold(ring, centre, yaw, pitch, Tuning.Lens with { Floor = Tuning.AreaFloor })));

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
                    if (UnderGround(shot.Eye) || Outside(shot.Eye))
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
/// Two who deal with each other, and which of them a shot should frame on the right: the way the battle's vantage sees them
/// (<see cref="Vantage.Side"/>), film's rule of keeping the camera on one side of the line between them.
/// </summary>
public readonly record struct ScreenSide(Vector3 Right, Vector3 Left);

/// <summary>
/// The battle's vantage: a point off to one side of the line between the party's middle and the enemies', from whose side every shot
/// is taken, as film keeps its cameras on one side of the line of action. Two who deal with each other are framed the way round it
/// sees them (<see cref="Side"/>), so every party member is on the same side of their enemies from one shot to the next, whoever's turn
/// it is. It's placed as the battle begins, again as a reserve comes onto the field or the fight has turned round, and the shots
/// needn't be taken from it, only from its side.
/// </summary>
public static class Vantage
{
    /// <summary>How nearly in line with two the vantage may see them and still say which is on the right: a share of how far apart they are.</summary>
    private const float InLine = 0.25f;

    /// <summary>
    /// Where the vantage stands across the ground: off the middle of the line from <paramref name="party"/> to <paramref name="enemies"/>
    /// (their middles), <see cref="CameraTuning.VantageReach"/> times its length or at least <see cref="CameraTuning.VantageNearest"/>;
    /// and its side, +1 on the left of the way from the party to the enemies, −1 on its right. On the <paramref name="side"/> it had (0 for
    /// none yet) while the field has room that side for half its reach, so the camera stays on the same side of the fight as it moves
    /// about the field; else on the side with more room, where the shots' eyes have room to stand. It may stand beyond the field: it's a
    /// side, not an eye. Null where the two middles are one.
    /// </summary>
    public static (Vector2 At, int Side)? Place(Vector2 party, Vector2 enemies, (Vector2 Min, Vector2 Max)? field, int side, CameraTuning t)
    {
        var line = enemies - party;
        var length = line.Length();
        if (length < 1e-3f)
            return null;
        var way = line / length;
        // The left of the way, in x and z (Shot.Right's opposite).
        var left = new Vector2(way.Y, -way.X);
        var middle = (party + enemies) / 2;
        var reach = MathF.Max(t.VantageNearest, t.VantageReach * length);
        var (plus, minus) = (Room(middle, left, field), Room(middle, -left, field));
        if (side == 0 || (side > 0 ? plus : minus) < reach / 2)
            side = plus >= minus ? 1 : -1;
        return (middle + left * side * reach, side);
    }

    /// <summary>How far from <paramref name="from"/> along <paramref name="way"/> the field goes on: to its edge, or for ever without one.</summary>
    private static float Room(Vector2 from, Vector2 way, (Vector2 Min, Vector2 Max)? field)
    {
        if (field is not var (min, max))
            return float.MaxValue;
        var room = float.MaxValue;
        if (way.X > 1e-6f)
            room = MathF.Min(room, (max.X - from.X) / way.X);
        else if (way.X < -1e-6f)
            room = MathF.Min(room, (min.X - from.X) / way.X);
        if (way.Y > 1e-6f)
            room = MathF.Min(room, (max.Y - from.Y) / way.Y);
        else if (way.Y < -1e-6f)
            room = MathF.Min(room, (min.Y - from.Y) / way.Y);
        return MathF.Max(room, 0);
    }

    /// <summary>
    /// Which of two who deal with each other <paramref name="vantage"/> sees on the right, looking at their middle; null where it sees
    /// them nearly in line, as either side is as good.
    /// </summary>
    public static ScreenSide? Side(Vector3 a, Vector3 b, Vector2 vantage)
    {
        var look = new Vector2(a.X + b.X, a.Z + b.Z) / 2 - vantage;
        var apart = new Vector2(a.X - b.X, a.Z - b.Z);
        if (look.LengthSquared() < 1e-6f || apart.LengthSquared() < 1e-6f)
            return null;
        look = Vector2.Normalize(look);
        // The screen's right looking that way across the ground (Shot.Right), in x and z.
        var across = Vector2.Dot(apart, new Vector2(-look.Y, look.X));
        if (MathF.Abs(across) < apart.Length() * InLine)
            return null;
        return across > 0 ? new ScreenSide(a, b) : new ScreenSide(b, a);
    }
}

/// <summary>
/// Whom the player heads for in their turn. Within <see cref="CameraTuning.StrikeRange"/>, the nearest enemy in front of the character:
/// among a pack within reach, whom the camera looks at hardly matters. Further, of the enemies in a cone about the way the character points
/// (the way it walks, as it walks), within range, the likeliest by how near the cone's middle it is and how near the character. The target
/// held stays while it's in a wider cone and a longer range, or near the character as they go round it, and for a while after it leaves
/// them; another takes its place, or is taken with none held, only once it has been clearly likelier for a while, the longer the further it
/// is (<see cref="CameraTuning.TargetHold"/>). So the stick's wobble doesn't flick the camera between two bodies, or between one and none,
/// running round a pack keeps the camera on it, and a body across the field is taken only once the player heads for it.
/// </summary>
public sealed class TargetCone
{
    /// <summary>What an enemy within <see cref="CameraTuning.StrikeRange"/> is worth, less its distance: more than any further.</summary>
    private const float Strike = 100;

    private object? _challenger;
    private int _challengerTicks;
    private int _lostTicks;

    /// <summary>The target held, or null.</summary>
    public object? Target { get; private set; }

    /// <summary>Whether the target held has left its cone or its range, and is only held a while (<see cref="CameraTuning.LoseHold"/>).</summary>
    public bool Losing => _lostTicks > 0;

    /// <summary>Forgets the target.</summary>
    public void Clear()
    {
        Target = null;
        _challenger = null;
        _lostTicks = 0;
    }

    /// <summary>Holds <paramref name="who"/> as the target, whether or not it's in the cone: a turn opening on its nearest enemy.</summary>
    public void Hold(object who)
    {
        Target = who;
        _challenger = null;
        _lostTicks = 0;
    }

    /// <summary>
    /// An enemy's likelihood as the target, or null outside the cone or the range (<see cref="CameraTuning.ConeHold"/>'s and
    /// <see cref="CameraTuning.TargetRangeHold"/>'s for the one held, which within <see cref="CameraTuning.HoldNear"/> needs only that the
    /// character doesn't head away from it, <see cref="CameraTuning.HoldNearAngle"/>). Within <see cref="CameraTuning.StrikeRange"/> and
    /// not headed away from, it's by its distance alone, and likelier than any further.
    /// </summary>
    public float? Likelihood(Vector3 me, Vector2 heading, TargetCandidate c, CameraTuning t)
    {
        var off = c.At - me;
        var flat = new Vector2(off.X, off.Z);
        var d = flat.Length();
        if (d < 0.1f || heading.LengthSquared() < 1e-6f)
            return null;
        var held = Equals(c.Who, Target);
        if (d > (held ? t.TargetRangeHold : t.TargetRange))
            return null;
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(heading), flat / d), -1, 1));
        if (d <= t.StrikeRange && angle <= ShotFinder.Rad(t.HoldNearAngle))
            return Strike - d;
        var ahead = t.AngleWeight * (1 - MathF.Pow(angle / ShotFinder.Rad(t.ConeAngle), 2));
        // The one held, near: whichever way the character faces round it, as near as it is.
        if (held && d <= t.HoldNear && angle <= ShotFinder.Rad(t.HoldNearAngle))
            return MathF.Max(0, ahead) + MathF.Exp(-d / t.NearScale);
        if (angle > ShotFinder.Rad(held ? t.ConeHold : t.ConeAngle))
            return null;
        return ahead + MathF.Exp(-d / t.NearScale);
    }

    /// <summary>
    /// How many ticks in a row an enemy <paramref name="distance"/> off must be the likeliest to be taken: <see cref="CameraTuning.TargetHold"/>
    /// within <see cref="CameraTuning.TargetQuickRange"/>, rising to <see cref="CameraTuning.TargetHoldFar"/> at
    /// <see cref="CameraTuning.TargetRange"/>.
    /// </summary>
    public static int HoldTicks(float distance, CameraTuning t)
    {
        var s = Math.Clamp((distance - t.TargetQuickRange) / MathF.Max(t.TargetRange - t.TargetQuickRange, 1e-3f), 0, 1);
        return (int)MathF.Round(t.TargetHold + (t.TargetHoldFar - t.TargetHold) * s);
    }

    /// <summary>A tick: picks again from <paramref name="candidates"/>. Returns whether the target changed.</summary>
    /// <param name="me">Where the character is.</param>
    /// <param name="heading">The way it points.</param>
    /// <param name="atOnce">The likeliest is taken now, not once it has been so a while: a turn opening.</param>
    public bool Pick(Vector3 me, Vector2 heading, IEnumerable<TargetCandidate> candidates, CameraTuning t, bool atOnce = false)
    {
        if (heading.LengthSquared() < 1e-6f)
            return false;
        object? best = null;
        var (bestScore, bestDistance) = (float.NegativeInfinity, 0f);
        float? held = null;
        var present = false;
        foreach (var c in candidates)
        {
            present |= Target is not null && Equals(c.Who, Target);
            if (Likelihood(me, heading, c, t) is not { } score)
                continue;
            if (Equals(c.Who, Target))
                held = score;
            if (score > bestScore)
                (bestScore, best, bestDistance) = (score, c.Who, new Vector2(c.At.X - me.X, c.At.Z - me.Z).Length());
        }
        // Out of its cone or range, but still on the field: held a while, in case the player turns back.
        _lostTicks = Target is not null && held is null ? _lostTicks + 1 : 0;
        // Who would take its place: the likeliest, by the margin over the one held while it's in its cone, and taken once it has been so
        // long enough for how far it is. In place of one held, the same one so in a row, so the stick's wobble doesn't flick between two;
        // with none held, or the one held leaving, whoever's likeliest as there has been someone, so two taking turns don't put it off.
        var challenger = best is not null && !Equals(best, Target) && (held is not { } heldScore || bestScore > heldScore + t.TargetMargin) ? best : null;
        if (challenger is null || held is not null && !Equals(challenger, _challenger))
            _challengerTicks = 0;
        _challenger = challenger;
        if (challenger is not null && (atOnce || ++_challengerTicks >= HoldTicks(bestDistance, t)))
        {
            Target = challenger;
            (_challenger, _lostTicks) = (null, 0);
            return true;
        }
        // Let go once it has been out of them LoseHold, or has left the field.
        if (Target is not null && held is null && (atOnce || !present || _lostTicks >= t.LoseHold))
        {
            Target = null;
            _lostTicks = 0;
            return true;
        }
        return false;
    }
}
