using HBHT.Rom.Borg;
using HBHT.Rom.Graphics;

namespace HBHT.Rom.Import;

/// <summary>
/// Where in its effect animation each spell lands (<c>SpellRecord.Strike</c>): the share of the cast state of the spell's visual actor,
/// the target's if it has one and else the caster's, at which a cast lands. In 1.0 a cast lands as the caster's own cast animation
/// ends (request 5), whoever the caster is, so the spell's visual never times it. The base game times it by the visual instead, at
/// <see cref="ReferenceCastTicks"/>, so a spell lands about when 1.0 lands it for a typical caster.
/// </summary>
internal static class SpellStrike
{
    /// <summary>
    /// The tick a cast lands at, unless its effect animation is shorter (the user's choice of reference): the mean length of the
    /// player-controlled casters' cast animations, request 5. Alaron's is 91 ticks, Sholeh's 123, Godric's 129, Brenna's 131, Keelin's 161,
    /// Dougal's 201 and Rheda's 239.
    /// </summary>
    public const int ReferenceCastTicks = 153;

    /// <summary>The request a cast asks its visual's actor for (the port's <c>Battle.CastRequest</c>).</summary>
    private const int CastRequest = 5;

    /// <summary>
    /// The strike of a spell whose effect animation is <paramref name="length"/> ticks: <see cref="ReferenceCastTicks"/> over it, or 1
    /// for an animation no longer than that. It is rounded up to three places, so that the tick it gives, <c>(int)(length × share)</c>,
    /// is the reference tick itself or the one after, never before.
    /// </summary>
    public static float Share(int length) =>
        length <= ReferenceCastTicks ? 1 : (float)(decimal.Ceiling(ReferenceCastTicks / (decimal)length * 1000) / 1000);

    /// <summary>
    /// The length in ticks of the cast state of the actor at <paramref name="borg"/>, as the port's actor machine reports it once the
    /// actor has entered it on request 5: the state the initial state's branch for request 5 leads to
    /// (<c>ActorStateMachine.Taken</c> takes a branch only for a request it names exactly, and the visual asks for 5 from its first
    /// tick), or the initial state itself for an actor with no such branch (Sense Aura's one-state actors), which the cast waits out.
    /// The state's animation's length is its Borg6's, or 1 if the animation animates another scene than the actor's, as the actor's
    /// scene's clips (<c>ActorClips</c>) hold only its own. A branch for request 5 that rolls is an error: the length is not one.
    /// </summary>
    public static int CastLength(BorgArchive archive, int borg)
    {
        var actor = Borg7Actor.Parse(archive.Read(borg));
        var scene = Borg6Animation.Parse(archive.Read(actor.States[0].Animation)).Scene;
        var branches = actor.States[actor.InitialState].Branches.Where(b => b.Request == CastRequest).ToList();
        if (branches.Count > 0 && branches[0].Chance != 100)
            throw new InvalidDataException($"The actor 0x{borg:X4}'s initial state answers request {CastRequest} with a branch that rolls.");
        var state = branches.Count > 0 ? branches[0].State : actor.InitialState;
        var animation = Borg6Animation.Parse(archive.Read(actor.States[state].Animation));
        return animation.Scene == scene ? Math.Max(1, animation.Length) : 1;
    }
}
