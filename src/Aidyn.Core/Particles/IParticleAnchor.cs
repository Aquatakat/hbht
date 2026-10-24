using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Particles;

/// <summary>
/// What an emitter is attached to: an actor's scene, whose locators place it and its target. The runtime gives each
/// actor one, which reads its locators' drawn transforms, so emitters follow the actor's animation.
/// </summary>
public interface IParticleAnchor
{
    /// <summary>False once the actor is gone, which frees its emitters.</summary>
    bool Alive { get; }

    /// <summary>
    /// The alpha the actor is drawn at, 0 while it isn't drawn: beyond its draw distance, faded out or hidden. Its
    /// emitters' particles are drawn at it, and not at all at 0, though they go on stepping (<see cref="ParticleHead"/>).
    /// The original draws an actor's particles whether the actor is drawn or not; that they show and hide with it is the
    /// user's choice.
    /// </summary>
    byte Alpha => 0xFF;

    /// <summary>
    /// Where a locator is in the world, as the actor was last drawn: its scene's model's position, and its transform's
    /// matrix, whose rows give its axes. False if the scene has no such locator, or nothing is attached there.
    /// </summary>
    bool TryLocator(ParticleLocator locator, out Matrix4x4 world);
}
