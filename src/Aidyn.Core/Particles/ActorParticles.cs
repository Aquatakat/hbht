using Aidyn.Plugins.Records;

namespace Aidyn.Core.Particles;

/// <summary>
/// An actor's emitters, which it starts as it enters a state (<c>ActorParticles.Entered</c>, <c>0x80096a38</c>): every one
/// of its scene's emitters with <see cref="ParticleEmitterFlags.ActorRequest"/> whose request is the actor's request at
/// that moment. <c>ActorStateMachine.Tick</c> calls it on every state it enters, by a branch, a restart or an
/// interruption, and making the actor calls it once (<c>0x800960e0</c>). It never checks for one already running, so a
/// looping state starts a fresh emitter every loop, and leaving the state doesn't stop them: they run out, or go with the
/// actor.
/// </summary>
public sealed class ActorParticles(ParticleHead head, EmittersRecord emitters, IParticleAnchor anchor)
{
    public IParticleAnchor Anchor => anchor;

    /// <summary>Starts the emitters for <paramref name="request"/>.</summary>
    public void Entered(int request)
    {
        foreach (var e in emitters.Emitters)
        {
            if (e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest) && e.Request == request)
                head.Start(e, anchor);
        }
    }

    /// <summary>Stops its emitters and their particles at once, as freeing the actor does.</summary>
    public void Stop() => head.Stop(anchor);
}
