using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Physics;

namespace HBHT.Core.Combat;

/// <summary>
/// Start Early's answer to whether one shot can hold beats that play at once (<see cref="IBeatFraming"/>), and the shot that does. It
/// tries yaws all the way round (<see cref="CameraTuning.BeatYawStep"/>) at pitches from low across the field to near enough straight down
/// (<see cref="CameraTuning.BeatPitches"/>), each framed to hold every subject on the screen, no farther than
/// <see cref="CameraTuning.BeatDistance"/>, its eye on the field and above the ground, and seeing each past the arena's collision. Bodies
/// don't count as hiding anything here: where they'll be drawn is the drawing's to say, and the composer asks of drawn ticks to come. So
/// the answer is quick, the same for the same question, and doesn't depend on what's drawn now; the director then frames what it said
/// could be held (<see cref="BattleDirector"/>), the bodies counted where it can.
/// </summary>
public sealed class BeatShots : IBeatFraming
{
    private readonly ShotFinder _finder;
    private readonly Dictionary<Key, bool> _answers = [];
    private readonly List<ShotSubject> _subjects = [];

    /// <param name="world">The arena's collision, which hides what's behind it.</param>
    /// <param name="field">The battlefield across the ground, which the eye stays on.</param>
    public BeatShots(CollisionWorld? world, (Vector2 Min, Vector2 Max)? field, CameraTuning tuning)
    {
        _finder = new ShotFinder(new SightLines(world) { BodyScale = 0, Radius = tuning.SightRadius }, tuning) { Field = field };
    }

    /// <summary>The numbers it frames by.</summary>
    public CameraTuning Tuning
    {
        get => _finder.Tuning;
        set
        {
            if (!ReferenceEquals(value, _finder.Tuning))
                _answers.Clear();
            _finder.Tuning = value;
        }
    }

    /// <summary>How many questions it was asked, and how many it had to look into (the rest were answered before).</summary>
    public int Asked { get; private set; }

    public int Looked { get; private set; }

    public bool CanHold(int drawn, IReadOnlyList<FramedBeat> beats, Vector3? turn)
    {
        Asked++;
        _subjects.Clear();
        foreach (var b in beats)
            _subjects.Add(Subject(b));
        if (turn is { } at)
            _subjects.Add(new ShotSubject(at, Tuning.TurnRadius));
        var key = Key.Of(_subjects);
        if (_answers.TryGetValue(key, out var known))
            return known;
        Looked++;
        var answer = Find(_subjects, null, null, first: true) is not null;
        if (_answers.Count > 8192)
            _answers.Clear();
        _answers[key] = answer;
        return answer;
    }

    /// <summary>A beat's subject as a shot holds it: the body it lands on, where it's drawn.</summary>
    public static ShotSubject Subject(FramedBeat b) => new(b.Subject, b.Beat.Subject.Body.ScaledRadius);

    /// <summary>
    /// The shot holding every one of <paramref name="must"/> on the screen and seeing it past the arena (and past the bodies in
    /// <paramref name="sight"/>, if given), or null if none does. Of those that do, given <paramref name="from"/>: the one turning least
    /// from it, the lowest and nearest, seeing <paramref name="also"/> best (an action's combatant beside the result it deals); else the
    /// first found.
    /// </summary>
    /// <param name="first">Take the first that holds them all: whether there is one is all that's asked.</param>
    public Shot? Find(IReadOnlyList<ShotSubject> must, float? from, IReadOnlyList<ShotSubject>? also, bool first = false, SightLines? sight = null)
    {
        if (must.Count == 0)
            return null;
        var t = Tuning;
        var lens = t.Lens;
        var steps = Math.Max(1, (int)MathF.Round(360 / MathF.Max(t.BeatYawStep, 1)));
        var start = from ?? 0;
        var middle = ShotFraming.Middle(must);
        (Shot Shot, float Score)? best = null;
        foreach (var p in t.BeatPitches)
        {
            var pitch = ShotFinder.Rad(p);
            for (var i = 0; i < steps; i++)
            {
                // Nearest the view's yaw first, either way round.
                var k = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
                var yaw = Shot.Wrap(start + MathF.Tau * k / steps);
                var distance = ShotFraming.DistanceToHold(must, middle, yaw, pitch, lens);
                if (distance > t.BeatDistance)
                    continue;
                var shot = new Shot(middle, yaw, pitch, MathF.Max(distance, t.MinDistance));
                if (!Sees(shot, must, sight))
                    continue;
                if (first)
                    return shot;
                var score = -t.TurnCost * MathF.Abs(Shot.Wrap(yaw - start)) - t.PitchCost * MathF.Abs(pitch - ShotFinder.Rad(t.BeatPitch))
                    - t.DistanceCost * shot.Distance;
                if (also is not null)
                {
                    foreach (var s in also)
                    {
                        if (!ShotFraming.Holds(shot, s with { Radius = 0 }, lens) || (sight ?? _finder.Sight).Hidden(shot.Eye, s))
                            score -= t.OcclusionPenalty * s.Weight;
                    }
                }
                if (best is not { } b || score > b.Score)
                    best = (shot, score);
            }
        }
        return best?.Shot;
    }

    /// <summary>
    /// Whether a shot holds every one of <paramref name="subjects"/> on the screen, its eye on the field and above the ground, and sees
    /// each past the arena's collision (and past the bodies in <paramref name="sight"/>, if given).
    /// </summary>
    public bool Sees(Shot shot, IReadOnlyList<ShotSubject> subjects, SightLines? sight = null)
    {
        var lens = Tuning.Lens;
        foreach (var s in subjects)
        {
            if (!ShotFraming.Holds(shot, s with { Radius = 0 }, lens))
                return false;
        }
        var eye = shot.Eye;
        if (_finder.Outside(eye) || _finder.UnderGround(eye))
            return false;
        var lines = sight ?? _finder.Sight;
        foreach (var s in subjects)
        {
            if (lines.Hidden(eye, s))
                return false;
        }
        return true;
    }

    /// <summary>A question asked before: its subjects' spheres.</summary>
    private readonly struct Key : IEquatable<Key>
    {
        private readonly ShotSubject[] _subjects;
        private readonly int _hash;

        private Key(ShotSubject[] subjects)
        {
            _subjects = subjects;
            var h = new HashCode();
            foreach (var s in subjects)
                h.Add(s);
            _hash = h.ToHashCode();
        }

        public static Key Of(List<ShotSubject> subjects) => new([.. subjects]);

        public bool Equals(Key other) => _subjects.AsSpan().SequenceEqual(other._subjects);

        public override bool Equals(object? obj) => obj is Key k && Equals(k);

        public override int GetHashCode() => _hash;
    }
}
