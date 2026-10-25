using System.Numerics;
using HBHT.Plugins.Records;

namespace HBHT.Core.Particles;

/// <summary>
/// An actor's emitters, which it starts as it enters a state (<c>ActorParticles.Entered</c>, <c>0x80096a38</c>): every one
/// of its scene's emitters with <see cref="ParticleEmitterFlags.ActorRequest"/> whose request is the actor's request at
/// that moment. <c>ActorStateMachine.Tick</c> calls it on every state it enters, by a branch, a restart or an
/// interruption, and making the actor calls it once (<c>0x800960e0</c>). It never checks for one already running, so a
/// looping state starts a fresh emitter every loop, and leaving the state doesn't stop them: they run out, or go with the
/// actor.
/// </summary>
/// <remarks>
/// Its particles are drawn at the actor's <see cref="Alpha"/>, which its view sets as it fades the actor, so they show and
/// hide with it, the user's choice (<see cref="IParticleAnchor.Alpha"/>).
/// </remarks>
public sealed class ActorParticles(ParticleHead head, EmittersRecord emitters, IParticleAnchor anchor)
{
    private readonly Shown _anchor = new(anchor);

    /// <summary>What its emitters are attached to: the actor's locators, at <see cref="Alpha"/>.</summary>
    public IParticleAnchor Anchor => _anchor;

    /// <summary>The alpha the actor is drawn at, 0 while it isn't, which its particles are drawn at.</summary>
    public byte Alpha
    {
        get => _anchor.Alpha;
        set => _anchor.Alpha = value;
    }

    /// <summary>Starts the emitters for <paramref name="request"/>.</summary>
    public void Entered(int request)
    {
        foreach (var e in emitters.Emitters)
        {
            if (e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest) && e.Request == request)
                head.Start(e, _anchor);
        }
    }

    /// <summary>Stops its emitters and their particles at once, as freeing the actor does.</summary>
    public void Stop() => head.Stop(_anchor);

    /// <summary>The actor's anchor, at the alpha the actor is drawn at.</summary>
    private sealed class Shown(IParticleAnchor inner) : IParticleAnchor
    {
        public bool Alive => inner.Alive;

        public byte Alpha { get; set; } = 0xFF;

        public bool TryLocator(ParticleLocator locator, out Matrix4x4 world) => inner.TryLocator(locator, out world);
    }
}
