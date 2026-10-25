using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Audio;

/// <summary>
/// The loaded chunks' sound map objects (<c>ProcessAudioBubbles</c>, 1.0 <c>0x8004ECA0</c>): each plays while the
/// player is within its radius, louder nearer what the camera looks at and panned by where it is from the camera's
/// facing (docs/systems/audio.md).
/// </summary>
/// <param name="there">Whether an object's flags let it play, checked each time it starts.</param>
public sealed class MapSounds(GameAudio audio, IRandom random, Func<MapObject, bool> there)
{
    /// <summary>The most at once (<c>Audio.Init</c>, 1.0 <c>0x8004DF08</c>): the original writes past them, the port drops the rest.</summary>
    public const int MaxSounds = 82;

    /// <summary>Ticks music from a chunk that unloaded waits for another chunk to load the same (<c>Audio.PersistMusic</c>).</summary>
    public const int PersistTicks = 2;

    private readonly List<Bubble> _bubbles = [];

    /// <summary>How many sound objects the loaded chunks have, playing or not.</summary>
    public int Count => _bubbles.Count;

    /// <summary>How many sounds are playing.</summary>
    public int Playing => _bubbles.Count(b => audio.IsPlaying(b.Handle));

    /// <summary>A chunk has loaded: its sound objects can play (<c>Audio.StartChunk</c>, 1.0 <c>0x8004E58C</c>).</summary>
    public void Load(LoadedMap map)
    {
        foreach (var o in map.Objects.OfType<SoundObject>())
        {
            if (o.Pak == PakVariant.Without || _bubbles.Count(b => !b.Orphaned) >= MaxSounds)
                continue;
            var bubble = new Bubble(map, o, map.Origin + o.Position, () => there(o));
            // Audio.FindPersistentMusic: the same music playing on from a chunk that's gone is this one's.
            if ((o.Flags & SoundFlags.Music) != 0 && _bubbles.FirstOrDefault(b => b.Orphaned && b.Sound.Sound == o.Sound) is { } held)
            {
                _bubbles.Remove(held);
                bubble.Handle = held.Handle;
                bubble.Fade = held.Fade;
                bubble.Pan = held.Pan;
                bubble.StartNeeded = held.StartNeeded;
            }
            _bubbles.Add(bubble);
        }
    }

    /// <summary>
    /// A chunk is unloading (<c>Audio.FreeOwner</c>): its sounds stop, but its music plays on for a moment, for the
    /// next chunk to load it (<c>Audio.PersistMusic</c>).
    /// </summary>
    public void Unload(LoadedMap map)
    {
        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            if (b.Map != map)
                continue;
            if ((b.Sound.Flags & SoundFlags.Music) != 0 && audio.IsPlaying(b.Handle))
            {
                b.Orphaned = true;
                b.Countdown = PersistTicks;
                continue;
            }
            audio.Stop(b.Handle);
            _bubbles.RemoveAt(i);
        }
    }

    /// <summary>
    /// Every sound stops but music, which waits for the next chunks to load it (<c>Audio.ClearSounds</c>), as trek's
    /// world goes for a cut-scene.
    /// </summary>
    public void Clear()
    {
        foreach (var map in _bubbles.Where(b => !b.Orphaned).Select(b => b.Map).Distinct().ToList())
            Unload(map);
    }

    /// <summary>A tick: each sound starts, stops, or follows the player and the camera.</summary>
    /// <param name="player">Where the player is: within a sound's radius of it, it plays.</param>
    /// <param name="eye">The camera's eye, which sounds pan from.</param>
    /// <param name="aim">What the camera looks at, which loudness is measured from (<c>Camera.Listener+0xC</c>).</param>
    public void Step(Vector3 player, Vector3 eye, Vector3 aim)
    {
        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            if (b.Orphaned)
            {
                if (--b.Countdown <= 0)
                {
                    audio.Stop(b.Handle);
                    _bubbles.RemoveAt(i);
                }
                continue;
            }
            Step(b, player, eye, aim);
        }
    }

    private void Step(Bubble b, Vector3 player, Vector3 eye, Vector3 aim)
    {
        var o = b.Sound;
        if (Vector3.Distance(player, b.Position) <= o.Radius)
        {
            var loudness = AudioLevels.Loudness(Vector3.Distance(aim, b.Position), o.Radius);
            b.Fade = (o.Flags & SoundFlags.NoFalloff) == 0 ? loudness : MathF.Min(1, b.Fade + 1f / AudioLevels.FadeTicks);
            if ((o.Flags & SoundFlags.NoPan) == 0)
                b.Pan = AudioLevels.Pan(b.Position, eye, aim, loudness);
            if (--b.Countdown <= 0)
            {
                b.Countdown = 0;
                // A repeating sound starts again after a wait: at once on coming in, as the countdown starts at 0.
                if ((o.Flags & SoundFlags.Repeat) != 0)
                {
                    audio.Stop(b.Handle);
                    b.StartNeeded = true;
                    b.Countdown = (short)(int)((o.MinWait + random.Float(o.MaxWait - o.MinWait)) * 60);
                }
            }
            Update(b);
        }
        else if (b.Fade > 0)
        {
            b.Fade = MathF.Max(0, b.Fade - 1f / AudioLevels.FadeTicks);
            Update(b);
        }
        else if (audio.IsPlaying(b.Handle))
        {
            audio.Stop(b.Handle);
            b.StartNeeded = true;
        }
    }

    /// <summary>
    /// <c>Audio.UpdateSound</c> (1.0 <c>0x8004E900</c>): keeps a playing sound up at its volume and pan, or starts one
    /// that needs starting if its flags let it (<see cref="KeptSound.Update"/>), at the music volume if it's music.
    /// </summary>
    private void Update(Bubble b) =>
        b.Update(audio, (b.Sound.Flags & SoundFlags.Music) != 0 ? audio.MusicVolume : audio.SoundVolume, b.There);

    private sealed class Bubble(LoadedMap map, SoundObject sound, Vector3 position, Func<bool> there)
        : KeptSound(sound.Sound, sound.Volume, (sound.Flags & SoundFlags.Loop) != 0)
    {
        public LoadedMap Map { get; } = map;
        public SoundObject Sound { get; } = sound;
        public Vector3 Position { get; } = position;
        public Func<bool> There { get; } = there;
        public bool Orphaned;
        public int Countdown;
    }
}
