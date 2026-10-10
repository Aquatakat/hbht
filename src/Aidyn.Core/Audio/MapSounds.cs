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

    /// <summary>Ticks a sound takes to fade in, without falloff, or out (<c>delta / 90</c> a frame).</summary>
    public const int FadeTicks = 90;

    /// <summary>Milliseconds a sound plays for when it starts, until it's next kept up (<c>Audio.UpdateSound</c>).</summary>
    public const int FirstDurationMs = 2000;

    /// <summary>Ticks music from a chunk that unloaded waits for another chunk to load the same (<c>Audio.PersistMusic</c>).</summary>
    public const int PersistTicks = 2;

    private readonly List<Bubble> _bubbles = [];

    /// <summary>How many sounds are playing.</summary>
    public int Playing => _bubbles.Count(b => audio.IsPlaying(b.Handle));

    /// <summary>A chunk has loaded: its sound objects can play (<c>Audio.StartChunk</c>, 1.0 <c>0x8004E58C</c>).</summary>
    public void Load(LoadedMap map)
    {
        foreach (var o in map.Objects.OfType<SoundObject>())
        {
            if (o.Pak == PakVariant.Without || _bubbles.Count(b => !b.Orphaned) >= MaxSounds)
                continue;
            var bubble = new Bubble(map, o, map.Origin + o.Position);
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
            var loudness = 1 - Vector3.Distance(aim, b.Position) / o.Radius;
            b.Fade = (o.Flags & SoundFlags.NoFalloff) == 0 ? loudness : MathF.Min(1, b.Fade + 1f / FadeTicks);
            if ((o.Flags & SoundFlags.NoPan) == 0)
                b.Pan = Pan(b.Position, eye, aim, loudness);
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
            b.Fade = MathF.Max(0, b.Fade - 1f / FadeTicks);
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
    /// that needs starting if its flags let it. One that loops is started again whenever it has ended.
    /// </summary>
    private void Update(Bubble b)
    {
        var o = b.Sound;
        var level = (o.Flags & SoundFlags.Music) != 0 ? audio.MusicVolume : audio.SoundVolume;
        var volume = unchecked((byte)(int)(Math.Clamp((int)(b.Fade * o.Volume * 255.0), 0, 255) * level));
        var pan = (byte)Math.Clamp((int)((b.Pan + 1f) * 0.5f * 255.0), 0, 255);
        if (audio.IsPlaying(b.Handle))
        {
            audio.SetDuration(b.Handle, -1);
            audio.Set(b.Handle, volume, pan);
            return;
        }
        if (!b.StartNeeded || !there(o))
            return;
        var loop = (o.Flags & SoundFlags.Loop) != 0;
        b.Handle = audio.Start(o.Sound, volume, pan, loop, FirstDurationMs);
        b.StartNeeded = loop;
    }

    /// <summary>
    /// A sound's pan (<c>Audio.Pan</c>, 1.0 <c>0x8004EB1C</c>): the sine of the angle across the ground from the
    /// camera's facing to it, positive to the right, the less the louder it is. Not clamped.
    /// </summary>
    public static float Pan(Vector3 position, Vector3 eye, Vector3 aim, float loudness)
    {
        var facing = new Vector2(aim.X - eye.X, aim.Z - eye.Z);
        var to = new Vector2(position.X - eye.X, position.Z - eye.Z);
        if (facing.LengthSquared() == 0 || to.LengthSquared() == 0)
            return 0;
        facing = Vector2.Normalize(facing);
        to = Vector2.Normalize(to);
        return (facing.X * to.Y - facing.Y * to.X) * (1 - loudness);
    }

    private sealed class Bubble(LoadedMap map, SoundObject sound, Vector3 position)
    {
        public LoadedMap Map { get; } = map;
        public SoundObject Sound { get; } = sound;
        public Vector3 Position { get; } = position;
        public SoundHandle Handle;
        public float Fade, Pan;
        public bool StartNeeded = true, Orphaned;
        public int Countdown;
    }
}
