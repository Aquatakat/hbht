using Aidyn.Core.Audio;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class GameAudioTests
{
    private static readonly RecordId Tune = RecordId.Hex(RecordTypes.Sequence, 1), Other = RecordId.Hex(RecordTypes.Sequence, 2);

    /// <summary>A sequence of one long note at full volume, a row every 20 ms.</summary>
    private static Sequence LongNote(int rows) =>
        new(1, [new Instrument([.. Enumerable.Repeat((short)8000, 1_000_000)], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[rows]]);

    private static GameAudio Audio(Settings? settings = null)
    {
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Tune, LongNote(1000));
        bank.Add(Other, LongNote(1000));
        return new GameAudio(bank, settings ?? new Settings(), 44100);
    }

    private static float Loudness(GameAudio audio)
    {
        var frames = new short[2 * 441];
        audio.Render(frames);
        return frames.Max(f => Math.Abs((int)f));
    }

    [Fact]
    public void MusicStartsWhenAskedForAndStopsWhenNotAsWhatsAskedForChanges()
    {
        var audio = Audio();
        audio.Music = Tune;
        audio.Step();
        Assert.Equal(Tune, audio.MusicPlaying);
        Assert.True(Loudness(audio) > 0);

        audio.Music = null;
        audio.Step();
        Assert.Null(audio.MusicPlaying);
        Loudness(audio);
        Assert.Equal(0, Loudness(audio));
    }

    [Fact]
    public void TheMusicFollowsTheMusicVolumeAtOnce()
    {
        var settings = new Settings { MusicVolume = Settings.MaxVolume };
        var audio = Audio(settings);
        audio.Music = Tune;
        audio.Step();
        Loudness(audio);
        var full = Loudness(audio);

        settings.MusicVolume = 0;
        audio.Step();
        Loudness(audio);
        Assert.True(full > 0);
        Assert.Equal(0, Loudness(audio));
    }

    [Fact]
    public void AOneShotWaitsForItsDelayAndStopsAtTheEndOfItsLifetime()
    {
        var audio = Audio();
        audio.Play(Tune, lifetime: 3, delay: 2);
        Assert.Equal(0, Loudness(audio));
        audio.Step();
        audio.Step();
        Assert.True(Loudness(audio) > 0);
        audio.Step();
        audio.Step();
        audio.Step();
        Loudness(audio);
        Assert.Equal(0, Loudness(audio));
    }

    [Fact]
    public void TheRainLoopFollowsTheWeatherAtItsLevelTimesTheSoundVolumeAndThunderIsAOneShot()
    {
        var audio = Audio(new Settings { SoundVolume = 50 });
        var sounds = new SoundsRecord(RecordId.Decimal(RecordTypes.Sounds, 0), new MenuSounds(Other, Other, Other, Other, Other, Other, Other, Other, Other, Other),
            [], Other, Other, Other, [], [], Tune, [Other, Other, Other, Other], Other, Other, []);
        var rain = new WeatherSounds(audio, sounds);
        var weather = new Core.Calendar.Weather(WeatherTests.Rules);
        weather.Force(Core.Calendar.WeatherKind.Precipitation, Core.Calendar.Precipitation.Rain, 0.5f);
        weather.Draw(skyDrawn: true);
        rain.Step(weather);
        Assert.True(rain.RainPlaying);
        // 191 at half volume: 95.
        Assert.Equal(95, WeatherSounds.RainVolume(weather.RainLoopLevel, audio.SoundVolume));

        weather.Draw(skyDrawn: false);
        rain.Step(weather);
        Assert.False(rain.RainPlaying);

        rain.Thunder(2);
        Assert.Equal(1, audio.SoundsPlaying);
        for (var i = 0; i < WeatherSounds.ThunderTicks[2]; i++)
            audio.Step();
        Assert.Equal(0, audio.SoundsPlaying);
    }

    [Fact]
    public void OnlySixteenOneShotsPlayAtOnce()
    {
        var audio = Audio();
        for (var i = 0; i < GameAudio.MaxSounds + 4; i++)
            audio.Play(Tune, lifetime: 100);
        Assert.Equal(GameAudio.MaxSounds, audio.SoundsPlaying);
    }
}
