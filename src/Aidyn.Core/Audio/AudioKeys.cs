using System.Numerics;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Audio;

/// <summary>What a body's sounds keep between ticks: the request it latched, the surface last walked on and its two slots.</summary>
public sealed class BodyAudio
{
    /// <summary>The request its animation started with, latched on the animation's first frame (body <c>+0x16</c>).</summary>
    public int Request { get; internal set; }

    /// <summary>The collision surface last touched that has footsteps of its own, 3 or more (body <c>+0x102</c>).</summary>
    public int Surface { get; internal set; }

    internal readonly SoundHandle[] Slots = new SoundHandle[2];
    internal int Slot;
}

/// <summary>
/// Bodies' footsteps and voices (<c>audiokey.cpp</c>, <c>AudioKeys.ProcessPlayer</c>, 1.0 <c>0x80053560</c>): each tick,
/// after a body's animation has stepped, the key that its request and frame reach plays, heard within 15 of the camera's
/// eye (docs/systems/audio.md).
/// </summary>
public sealed class AudioKeys(GameAudio audio, SoundsRecord? sounds, IRandom random)
{
    /// <summary>How far from what the camera looks at a body is heard, before the loudness wraps (<c>Audio.VolumeAndPan</c>'s 15).</summary>
    public const float Hearing = 15;

    /// <summary>How far from what the camera looks at bodies are stepped at all (<c>Players+0x68</c>).</summary>
    public const float Reach = 50;

    /// <summary>The frac that's an animation's whole length (<c>0x800D205C</c>).</summary>
    public const float WholeFrac = 65500;

    /// <summary>The camera's eye, which sounds pan from, set before bodies step.</summary>
    public Vector3 Eye { get; set; }

    /// <summary>What the camera looks at, which bodies are heard from (<c>Camera.Listener+0xC</c>).</summary>
    public Vector3 Aim { get; set; }

    /// <summary>
    /// A body's animation that plays its sounds as it steps: each step of the animation is a tick of
    /// <c>ProcessPlayers</c>' loop.
    /// </summary>
    public IRootMotion Sounding(IRootMotion animation, BodyAudio body, BodySounds? profile, CollisionSphere sphere) =>
        animation is IActorMotion actor ? Sounding(actor, body, profile, sphere) : animation;

    /// <inheritdoc cref="Sounding(IRootMotion, BodyAudio, BodySounds?, CollisionSphere)"/>
    public IActorMotion Sounding(IActorMotion animation, BodyAudio body, BodySounds? profile, CollisionSphere sphere) =>
        new SoundingMotion(this, animation, body, profile, sphere);

    /// <summary>
    /// A body's tick, after its animation stepped (<c>ProcessPlayers</c>, <c>AudioKeys.ProcessPlayer</c>).
    /// </summary>
    /// <param name="body">What the body's sounds keep.</param>
    /// <param name="profile">Its entity's sounds, or null for none.</param>
    /// <param name="request">The request its animation was stepped toward.</param>
    /// <param name="frame">Its animation's frame after the step, from 1 (<see cref="Actors.ActorStateMachine.Clock"/>).</param>
    /// <param name="length">Its animation's length in frames.</param>
    /// <param name="surface">The surface its sphere touches, 0 if none.</param>
    public void Process(BodyAudio body, BodySounds? profile, int request, int frame, int length, Vector3 position, int surface,
        Vector3 eye, Vector3 aim)
    {
        if (frame == 1)
            body.Request = request;
        if (surface >= 3)
            body.Surface = surface;
        if (Vector3.Distance(position, aim) > Reach)
            return;
        var (volume, pan) = VolumeAndPan(position, eye, aim);
        if (volume == 0 || profile is not { Kind: not 0 } || sounds is null)
            return;

        if (sounds.Kinds.ElementAtOrDefault(profile.Kind) is { } kind && kind.Surfaces.ElementAtOrDefault(body.Surface) is { } steps)
        {
            var stepVolume = (byte)(int)(volume * (double)kind.Loudness);
            if (stepVolume != 0)
            {
                foreach (var key in steps.Keys)
                {
                    if (Reached(key, body.Request, frame, length))
                        Play(body, key.Sounds, stepVolume, pan);
                }
            }
        }

        foreach (var key in profile.Keys)
        {
            // A key with no sounds of its own plays the weapon's, which is only in combat.
            if (key.Sounds.Count == 0 || !Reached(key, body.Request, frame, length))
                continue;
            Play(body, key.Sounds, volume, pan);
            break;
        }
    }

    private sealed class SoundingMotion(AudioKeys keys, IActorMotion animation, BodyAudio body, BodySounds? profile, CollisionSphere sphere)
        : IActorMotion
    {
        public int StateLength => animation.StateLength;

        public int Frame => animation.Frame;

        public float Tick(int request)
        {
            var forward = animation.Tick(request);
            keys.Process(body, profile, request, animation.Frame, animation.StateLength, sphere.Position,
                sphere.Touching ? sphere.Surface : 0, keys.Eye, keys.Aim);
            return forward;
        }
    }

    /// <summary>Whether a key fires: its request's animation at its frame, at least the first.</summary>
    public static bool Reached(SoundKey key, int request, int frame, int length)
    {
        if (key.Request != request)
            return false;
        var target = (ushort)(int)(key.Frac / WholeFrac * length);
        return frame == (target == 0 ? 1 : target);
    }

    /// <summary>
    /// A body's loudness and pan (<c>Audio.VolumeAndPan</c>, 1.0 <c>0x8004EB90</c>), as bytes. Neither is clamped: past
    /// 15 from what the camera looks at, the loudness's int wraps to its low byte, as the original's does.
    /// </summary>
    public static (byte Volume, byte Pan) VolumeAndPan(Vector3 position, Vector3 eye, Vector3 aim)
    {
        var loudness = AudioLevels.Loudness(Vector3.Distance(position, aim), Hearing);
        return (AudioLevels.LoudnessByte(loudness), AudioLevels.WrappedPanByte(AudioLevels.Pan(position, eye, aim, loudness)));
    }

    /// <summary>
    /// <c>AllocPlayerAudio</c> (1.0 <c>0x80012610</c>): one of the sounds at random, in the slot after the last, stopping
    /// what played there, once, at the sound volume, where the body is now.
    /// </summary>
    private void Play(BodyAudio body, IReadOnlyList<RecordId> choices, byte volume, byte pan)
    {
        var sequence = choices[(int)random.Below((uint)choices.Count)];
        var scaled = (byte)(int)(volume * audio.SoundVolume);
        body.Slot ^= 1;
        audio.Stop(body.Slots[body.Slot]);
        body.Slots[body.Slot] = scaled == 0 ? default : audio.Start(sequence, scaled, pan, loop: false);
    }
}
