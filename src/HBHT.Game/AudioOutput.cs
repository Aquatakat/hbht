using System.Threading;
using HBHT.Core.Audio;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Plays what the core's <see cref="GameAudio"/> mixes, through a Godot generator stream at the mixer's own rate. A
/// thread of its own keeps the stream about <see cref="Ahead"/> seconds ahead, so the game's hitches as chunks load
/// don't starve it, and a sound asked for is heard within about that long.
/// </summary>
public sealed partial class AudioOutput : AudioStreamPlayer
{
    /// <summary>How far ahead the stream is kept filled, in seconds.</summary>
    private const double Ahead = 0.06;

    /// <summary>Frames mixed at a time.</summary>
    private const int Chunk = 256;

    private readonly GameAudio _audio;
    private Thread? _thread;
    private volatile bool _running;

    public AudioOutput(GameAudio audio)
    {
        _audio = audio;
        Name = nameof(AudioOutput);
        Stream = new AudioStreamGenerator { MixRate = audio.Engine.Rate, BufferLength = 0.5f };
    }

    /// <summary>The rate the core should mix at: Godot's mixer's, so it isn't resampled again.</summary>
    public static int MixRate => (int)AudioServer.GetMixRate();

    public override void _Ready()
    {
        Play();
        var playback = (AudioStreamGeneratorPlayback)GetStreamPlayback();
        var capacity = playback.GetFramesAvailable();
        var ahead = (int)(Ahead * _audio.Engine.Rate);
        _running = true;
        _thread = new Thread(() => Fill(playback, capacity, ahead)) { IsBackground = true, Name = "Audio" };
        _thread.Start();
    }

    public override void _ExitTree()
    {
        _running = false;
        _thread?.Join();
        _thread = null;
    }

    private void Fill(AudioStreamGeneratorPlayback playback, int capacity, int ahead)
    {
        var pcm = new short[Chunk * 2];
        var frames = new Vector2[Chunk];
        while (_running)
        {
            var queued = capacity - playback.GetFramesAvailable();
            if (queued >= ahead)
            {
                Thread.Sleep(2);
                continue;
            }
            _audio.Render(pcm);
            for (var i = 0; i < Chunk; i++)
                frames[i] = new Vector2(pcm[2 * i] / 32768f, pcm[2 * i + 1] / 32768f);
            playback.PushBuffer(frames);
        }
    }
}
