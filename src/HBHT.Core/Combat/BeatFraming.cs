using System.Numerics;

namespace HBHT.Core.Combat;

/// <summary>
/// Whether one shot can hold beats that play at once (Start Early): the question the composer asks before it lets two actions'
/// results overlap, and before it opens a turn the player plays while an earlier result still plays. Piece C's director answers it
/// with the shots it can take; until then <see cref="SpreadFraming"/> stands in.
/// </summary>
/// <remarks>
/// The composer asks only about beats of different actions: an action's own beats (a Fireball's targets) play as the recording has
/// them. It asks on the drawn tick a beat would join others already playing, with each subject where it would be drawn then, so an
/// answer may not depend on what's drawn now; and while it searches for an action's start it asks for every delay it tries, so an
/// answer should be quick, and the same for the same question.
/// </remarks>
public interface IBeatFraming
{
    /// <summary>
    /// Whether one shot can hold every one of <paramref name="beats"/>, each playing on drawn tick <paramref name="drawn"/>, and, if
    /// <paramref name="turn"/> isn't null, the combatant of a turn the player plays standing there, whose turn's camera would have to
    /// keep them in shot as it opens.
    /// </summary>
    bool CanHold(int drawn, IReadOnlyList<FramedBeat> beats, Vector3? turn);
}

/// <summary>A beat as a shot would hold it on a drawn tick: the tick of its own it's drawn at then, and where its bodies are drawn.</summary>
/// <param name="Beat">The beat.</param>
/// <param name="Tick">The tick of the recording it's drawn at, from its strike (<see cref="Beat.From"/>) to its end.</param>
/// <param name="Subject">Where the body it lands on is drawn: its sphere's centre, as the recording had it at <paramref name="Tick"/>.</param>
/// <param name="Source">Where its action's combatant is drawn then, or null for none or one not on the field.</param>
public readonly record struct FramedBeat(Beat Beat, int Tick, Vector3 Subject, Vector3? Source);

/// <summary>
/// The stand-in for piece C's shots (Start Early): beats share a shot if their subjects, and the player's combatant if a turn is
/// asked about, all lie within <see cref="Spread"/> of one another across the ground. The sources aren't held: a ranged attacker can
/// be far from what it hits.
/// </summary>
public sealed class SpreadFraming(float spread = SpreadFraming.DefaultSpread) : IBeatFraming
{
    /// <summary>How far apart, across the ground, the stand-in's shot holds subjects by default: a guess, about a dozen bodies' widths, for C to replace.</summary>
    public const float DefaultSpread = 6;

    /// <summary>How far apart, across the ground, the bodies one shot holds may be.</summary>
    public float Spread { get; } = spread;

    public bool CanHold(int drawn, IReadOnlyList<FramedBeat> beats, Vector3? turn)
    {
        var points = new List<Vector2>(beats.Count + 1);
        foreach (var b in beats)
            points.Add(new Vector2(b.Subject.X, b.Subject.Z));
        if (turn is { } t)
            points.Add(new Vector2(t.X, t.Z));
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                if (Vector2.Distance(points[i], points[j]) > Spread)
                    return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Start Early's pacing (<see cref="BattleComposer"/>): how long a beat lasts at least and at most, and how much room the player's
/// combatant is kept from a body still walking in the drawing before its turn takes the stick.
/// </summary>
public sealed record StartEarlyPacing
{
    /// <summary>The fewest ticks a beat lasts, so a result shows long enough to be seen: half a second.</summary>
    public int BeatLeast { get; init; } = 30;

    /// <summary>The most ticks a beat lasts, however long its reaction: three seconds.</summary>
    public int BeatMost { get; init; } = 180;

    /// <summary>How many ticks a beat waits for its body's actor to leave its idle loop before it counts as having no reaction.</summary>
    public int ReactionWait { get; init; } = 12;

    /// <summary>
    /// The room kept between where the player's combatant could have got to and a body still walking in the drawing, beyond both
    /// their radii, as a multiple of the walking body's radius: the bigger the body, the wider its berth.
    /// </summary>
    public float Buffer { get; init; } = 1;

    /// <summary>
    /// The greatest travel a tick of a combatant whose actor has no run to measure, a little over the fastest tick of Alaron's run.
    /// </summary>
    public float DefaultTravel { get; init; } = 0.15f;

    /// <summary>How many drawn ticks ahead a body's walk is followed for the player's reach, at most.</summary>
    public int ReachTicks { get; init; } = 1200;
}
