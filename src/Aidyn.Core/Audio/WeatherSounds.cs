using Aidyn.Core.Calendar;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Audio;

/// <summary>
/// The weather's sounds: the rain loop (<c>ProcessWeather</c>, <c>0x8001e7c0</c> in 1.0, on <c>Weather.Data</c> +
/// 0x18) and the thunder as lightning flashes (<c>RenderSky</c>, <c>0x8001d840</c>). See docs/systems/weather.md.
/// </summary>
/// <remarks>
/// The rain loop isn't a one-shot: <c>ProcessWeather</c> plays the sounds record's <see cref="SoundsRecord.Rain"/>
/// straight on the sequencer (<c>AudioEngine.Play</c>), looped and centred at volume 0, and sets its volume every frame
/// from the rain's drawn intensity (<c>AudioEngine.SetVolume</c>). The thunder goes through <c>Audio.PlaySound</c>
/// with the sound volume as its gain, as the containers' sounds do.
/// </remarks>
public sealed class WeatherSounds(GameAudio audio, SoundsRecord sounds)
{
    /// <summary>Each thunder sound's lifetime in ticks, by its place in <see cref="SoundsRecord.Thunder"/> (literals in <c>RenderSky</c>).</summary>
    public static readonly int[] ThunderTicks = [240, 360, 420, 360];

    /// <summary>The pan the rain loop plays at: the middle, as <c>ProcessWeather</c> passes it (0x80).</summary>
    public const byte Centre = 0x80;

    private SoundHandle _rain;
    private int _starts;

    /// <summary>Whether the rain loop is playing.</summary>
    public bool RainPlaying => audio.IsPlaying(_rain);

    /// <summary>
    /// The rain loop's volume: <see cref="Weather.RainLoopLevel"/> times the sound volume, truncated to a byte
    /// (<c>ProcessWeather</c>).
    /// </summary>
    public static byte RainVolume(byte level, float soundVolume) => unchecked((byte)(int)(level * soundVolume));

    /// <summary>
    /// A tick of what's drawn, after <see cref="Weather.Draw"/>: the rain loop starts, keeps up its volume and stops
    /// as the weather has it, and thunder plays if lightning flashed.
    /// </summary>
    public void Step(Weather weather)
    {
        if (!weather.RainLoop)
        {
            StopRain();
        }
        else
        {
            if (weather.RainLoopStarts != _starts)
            {
                StopRain();
                _starts = weather.RainLoopStarts;
                if (sounds is not null)
                    _rain = audio.Start(sounds.Rain, 0, Centre, loop: true);
            }
            audio.Set(_rain, RainVolume(weather.RainLoopLevel, audio.SoundVolume), Centre);
        }
    }

    /// <summary>Plays the thunder that goes with a flash of lightning (<see cref="Weather.Thunder"/>), one of four.</summary>
    public void Thunder(int which)
    {
        if (sounds is not null && which >= 0 && which < sounds.Thunder.Count)
            audio.Play(new OneShot(sounds.Thunder[which], ThunderTicks[Math.Min(which, ThunderTicks.Length - 1)], TimesSoundVolume: true));
    }

    /// <summary>Stops the rain loop (<c>Weather.StopRain</c>, <c>0x8001e770</c>).</summary>
    public void StopRain()
    {
        audio.Stop(_rain);
        _rain = default;
    }

    /// <summary>
    /// Silences the rain loop without stopping it, until the next <see cref="Step"/> sets its volume again: under the post-battle
    /// windows, which the original's are silent through, as <c>Combat.Exit</c>'s <c>FreeZoneEngine</c> stops the loop
    /// (<c>Weather.Free</c>, <c>0x8001e030</c>). The port carries the loop through trek's rebuilds (docs/systems/weather.md), so it
    /// plays on, unheard, and trek's next tick brings it back where it was.
    /// </summary>
    public void Hush() => audio.Set(_rain, 0, Centre);
}
