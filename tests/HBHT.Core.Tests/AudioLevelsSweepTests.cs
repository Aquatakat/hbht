using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using HBHT.Core.Audio;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

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

    private static GameAudio Audio(SessionSettings settings)
    {
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Tune, Note);
        return new GameAudio(bank, 44100) { Settings = settings };
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
        {
            var audio = Audio(SessionSettings.Original with { SoundVolume = soundVolume });
            audio.Play(new OneShot(Tune, 500, 3));
            for (var tick = 0; tick < 110; tick++)
            {
                audio.Step();
                // The first tick of a sound is the one a map sound's duration and a one-shot's differ on.
                Record(text, audio, $"{soundVolume} {tick}", duration: tick >= 5);
            }
            foreach (var now in new[] { 100, 40, 0, 12 })
            {
                audio.Settings = audio.Settings with { SoundVolume = now };
                audio.Step();
                Record(text, audio, $"{soundVolume} now {now}", duration: true);
            }
        }
        Assert.Equal("B22A875CFB1A747C480F33621D2BF75407189462F0EFE57D3155B5EF2E3E44BE", Hash(text));
    }

    [Fact]
    public void OneShotsTakeTheSoundVolumeOnce()
    {
        // The original's take it twice, and the containers', thunder's and gold's three times as they start; the
        // port's, the user's fix, take it once, as a map's sounds and bodies' do.
        var audio = Audio(SessionSettings.Original with { SoundVolume = 50 });
        audio.Play(new OneShot(Tune, 500));
        audio.Step();
        Assert.Equal(127, audio.Engine.Playing().Single().Volume);
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
            var audio = Audio(SessionSettings.Original with { SoundVolume = sound, MusicVolume = music });
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
    public void BodiesLoudnessFallsAndPanHoldsItsSidePastTheirHearing()
    {
        // The original's both wrap past 15 (Audio.VolumeAndPan); the port's, the user's fix, never do: the loudness only
        // falls with distance, to nothing from 30, and the pan stays on the body's side of the camera.
        foreach (var eye in new[] { new Vector3(0, 5, 20), new Vector3(15, 3, -4), new Vector3(-3, 1, 0) })
        foreach (var aim in new[] { Vector3.Zero, new Vector3(4, 0, -2) })
        {
            var facing = Vector2.Normalize(new Vector2(aim.X - eye.X, aim.Z - eye.Z));
            for (var x = -52f; x <= 52f; x += 1.3f)
            for (var z = -52f; z <= 52f; z += 1.7f)
            {
                var at = new Vector3(x, 0.5f, z);
                var distance = Vector3.Distance(at, aim);
                var (volume, pan) = AudioKeys.VolumeAndPan(at, eye, aim);
                Assert.Equal(distance >= AudioKeys.Hearing ? 0 : (int)((1 - distance / AudioKeys.Hearing) * 255.0), volume);
                var to = new Vector2(x - eye.X, z - eye.Z);
                var side = facing.X * to.Y - facing.Y * to.X;
                if (MathF.Abs(side) > 0.01f && distance > 1)
                    Assert.True(side > 0 ? pan >= 127 : pan <= 128, $"{at} from {eye}: {pan}");
            }
        }
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
