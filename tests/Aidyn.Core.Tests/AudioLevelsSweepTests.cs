using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Aidyn.Core.Audio;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// Sweeps the volume and pan bytes each way a sound gets them (<c>Audio.UpdateSound</c>, <c>Audio.VolumeAndPan</c>,
/// the music and cinematic volumes) over grids of distances, pans, fades and volumes, and checks a hash of them all,
/// so a change to how they are worked out keeps every result.
/// </summary>
public class AudioLevelsSweepTests
{
    private static readonly RecordId Tune = RecordId.Hex(RecordTypes.Sequence, 1);

    private static readonly Sequence Note =
        new(1, [new Instrument([.. Enumerable.Repeat((short)8000, 1_000_000)], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[1000]]);

    private static GameAudio Audio(Settings settings)
    {
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Tune, Note);
        return new GameAudio(bank, settings, 44100);
    }

    private static string Hash(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

    private static void Record(StringBuilder text, GameAudio audio, string label, bool duration)
    {
        text.Append(label);
        foreach (var (volume, pan, ms) in audio.Engine.Playing())
            text.Append(CultureInfo.InvariantCulture, $" {volume}/{pan}{(duration ? $"/{ms}" : "")}");
        text.Append('\n');
    }

    [Fact]
    public void OneShotsKeepTheirVolumeAndPanOverTheFadeAndTheSoundVolumes()
    {
        var text = new StringBuilder();
        foreach (var soundVolume in new[] { 0, 1, 7, 33, 50, 65, 99, 100 })
        foreach (var times in new[] { false, true })
        {
            var settings = new Settings { SoundVolume = soundVolume };
            var audio = Audio(settings);
            audio.Play(new OneShot(Tune, 500, 3, times));
            for (var tick = 0; tick < 110; tick++)
            {
                audio.Step();
                // The first tick of a sound is the one a map sound's duration and a one-shot's differ on.
                Record(text, audio, $"{soundVolume} {times} {tick}", duration: tick >= 5);
            }
            foreach (var now in new[] { 100, 40, 0, 12 })
            {
                settings.SoundVolume = now;
                audio.Step();
                Record(text, audio, $"{soundVolume} {times} now {now}", duration: true);
            }
        }
        Assert.Equal("F2D050D7025B68872908C4FA041D53E70305DCAFF174E7327F2E9EE39C56306A", Hash(text));
    }

    [Fact]
    public void MapSoundsKeepTheirVolumeAndPanOverDistanceFadeAndFlags()
    {
        var text = new StringBuilder();
        var flagSets = new[]
        {
            SoundFlags.None, SoundFlags.NoFalloff, SoundFlags.NoPan, SoundFlags.NoFalloff | SoundFlags.NoPan,
            SoundFlags.Music, SoundFlags.Music | SoundFlags.NoFalloff, SoundFlags.Loop, SoundFlags.Repeat,
        };
        var eyes = new[] { new Vector3(0, 5, 20), new Vector3(15, 3, -4) };
        foreach (var flags in flagSets)
        foreach (var volume in new[] { 1f, 0.37f, 0.8f })
        foreach (var (sound, music) in new[] { (100, 65), (37, 12), (0, 100) })
        foreach (var eye in eyes)
        foreach (var distance in new[] { 0f, 0.4f, 1.3f, 2.9f, 4.4f, 5f, 6.1f, 7.7f, 9.3f, 9.99f, 10f })
        foreach (var angle in new[] { 0f, 0.7f, 2.1f, 4.0f, 5.6f })
        {
            var audio = Audio(new Settings { SoundVolume = sound, MusicVolume = music });
            var sounds = new MapSounds(audio, new Lcg(), _ => true);
            var at = new SoundObject(0, Vector3.Zero, 10, PakVariant.Any, null, Tune, volume, 1, 2, flags);
            sounds.Load(LoadedMap.Activate(new MapRecord(RecordId.Hex(RecordTypes.Map, 1), [at]), Worlds.Chunk(0), ItemData.Flags(), new MapObjectMemory(), ItemData.NewParty(), new Lcg()));
            var near = new Vector3(distance * MathF.Cos(angle), 0, distance * MathF.Sin(angle));
            var far = new Vector3(40, 0, 0);
            for (var tick = 0; tick < 4; tick++)
            {
                sounds.Step(near, eye, near);
                Record(text, audio, $"{flags} {volume} {sound} {music} {eye} {distance} {angle} in {tick}", duration: true);
            }
            for (var tick = 0; tick < 60; tick++)
            {
                sounds.Step(far, eye, far);
                Record(text, audio, $"out {tick}", duration: true);
            }
        }
        Assert.Equal("F018AF93D48805ED4DCCAC885ECFC258BEA06C9047D6686BA739E1398FF3FC9A", Hash(text));
    }

    [Fact]
    public void BodiesLoudnessAndPanWrapPastTheirHearing()
    {
        var text = new StringBuilder();
        foreach (var eye in new[] { new Vector3(0, 5, 20), new Vector3(15, 3, -4), new Vector3(-3, 1, 0) })
        foreach (var aim in new[] { Vector3.Zero, new Vector3(4, 0, -2) })
        {
            for (var x = -52f; x <= 52f; x += 1.3f)
            for (var z = -52f; z <= 52f; z += 1.7f)
            {
                var (volume, pan) = AudioKeys.VolumeAndPan(new Vector3(x, 0.5f, z), eye, aim);
                text.Append(CultureInfo.InvariantCulture, $"{volume}/{pan} ");
            }
            text.Append('\n');
        }
        Assert.Equal("019933BB3A20863F2AA61C6A0D8576E3BD482D43D5BBA6BD5B16373A7BDB9C13", Hash(text));
    }

    [Fact]
    public void TheMusicAndCinematicVolumesAreTruncatedSingles()
    {
        var text = new StringBuilder();
        for (var i = -20; i <= 540; i++)
            text.Append(CultureInfo.InvariantCulture, $"{GameAudio.Volume(i / 512f)} ");
        for (var v = 0; v <= Settings.MaxVolume; v++)
            text.Append(CultureInfo.InvariantCulture, $"{GameAudio.Volume((float)v / Settings.MaxVolume)} ");
        Assert.Equal("900D5C1A304901BDC871130F258A68729C5ABBABD34483791A7FD53AE92C2729", Hash(text));
    }
}
