using Aidyn.Core.Movement;

namespace Aidyn.Core.Actors;

/// <summary>
/// An actor's animations as a body plays them (<see cref="ActorMotion"/>): their root motion, how long the state playing
/// lasts, which a cut-scene's animation waits for, and its frame, which footsteps are keyed to.
/// </summary>
public interface IActorMotion : IRootMotion
{
    /// <summary>The length in ticks of the animation of the state it's in (<c>aniLength</c>).</summary>
    int StateLength { get; }

    /// <summary>The frame of the state it's in, from 1 (<see cref="ActorStateMachine.Clock"/>).</summary>
    int Frame => 1;

    /// <summary>For a body with no animations, or none yet: it stands, and its state lasts a tick.</summary>
    static readonly IActorMotion Still = new StillMotion();

    private sealed class StillMotion : IActorMotion
    {
        public int StateLength => 1;

        public float Tick(int request) => 0;
    }
}
