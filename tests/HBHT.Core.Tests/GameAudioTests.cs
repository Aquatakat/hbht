using HBHT.Core.Audio;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class GameAudioTests
{
    private static readonly RecordId Tune = RecordId.Hex(RecordTypes.Sequence, 1), Other = RecordId.Hex(RecordTypes.Sequence, 2);

    /// <summary>A sequence of one long note at full volume, a row every 20 ms.</summary>
    private static Sequence LongNote(int rows) =>
        new(1, [new Instrument([.. Enumerable.Repeat((short)8000, 1_000_000)], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[rows]]);

    private static GameAudio Audio(SessionSettings? settings = null)
    {
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Tune, LongNote(1000));
        bank.Add(Other, LongNote(1000));
        return new GameAudio(bank, 44100) { Settings = settings ?? SessionSettings.Original };
    }

    private static float Loudness(GameAudio audio)
    {
        var frames = new short[2 * 441];
        audio.Render(frames);
        return frames.Max(f => Math.Abs((int)f));
    }

    [Fact]
    public void MusicAdvancedBeforeItStartsStartsAsFarIn()
    {
        // A saw that never repeats within the test, so where in it the music is shows.
        var saw = new Sequence(1, [new Instrument([.. Enumerable.Range(0, 200_000).Select(i => (short)(i % 3000))], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[1000]]);
        GameAudio Started(Action<GameAudio> start)
        {
            var bank = new SoundBank(PluginSet.Load([]));
            bank.Add(Tune, saw);
            var audio = new GameAudio(bank, 44100) { Settings = SessionSettings.Original, Music = Tune };
            start(audio);
            return audio;
        }
        static short[] Next(GameAudio audio)
        {
            var frames = new short[2 * 441];
            audio.Render(frames);
            return frames;
        }

        var before = Started(a => { a.AdvanceMusic(30); a.Step(); });
        var after = Started(a => { a.Step(); a.AdvanceMusic(30); });
        var not = Started(a => a.Step());
        var ahead = Next(before);
        Assert.Equal(ahead, Next(after));
        Assert.NotEqual(ahead, Next(not));
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
        var audio = Audio(SessionSettings.Original with { MusicVolume = Settings.MaxVolume });
        audio.Music = Tune;
        audio.Step();
        Loudness(audio);
        var full = Loudness(audio);

        audio.Settings = audio.Settings with { MusicVolume = 0 };
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
        var audio = Audio(SessionSettings.Original with { SoundVolume = 50 });
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
    public void TheRainLoopHushedPlaysOnUnheardUntilTheWeatherStepsItAgain()
    {
        var audio = Audio();
        var sounds = new SoundsRecord(RecordId.Decimal(RecordTypes.Sounds, 0), new MenuSounds(Other, Other, Other, Other, Other, Other, Other, Other, Other, Other),
            [], Other, Other, Other, [], [], Tune, [Other, Other, Other, Other], Other, Other, []);
        var rain = new WeatherSounds(audio, sounds);
        var weather = new Core.Calendar.Weather(WeatherTests.Rules);
        weather.Force(Core.Calendar.WeatherKind.Precipitation, Core.Calendar.Precipitation.Rain, 0.5f);
        weather.Draw(skyDrawn: true);
        rain.Step(weather);
        Loudness(audio);
        Assert.True(Loudness(audio) > 0);

        // The post-battle windows: silent, the loop still playing.
        rain.Hush();
        Loudness(audio);
        Assert.Equal(0, Loudness(audio));
        Assert.True(rain.RainPlaying);

        // Trek's next tick brings it back where it was, not started over.
        rain.Step(weather);
        Loudness(audio);
        Assert.True(Loudness(audio) > 0);
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
