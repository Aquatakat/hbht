using System.Numerics;
using Aidyn.Core.Audio;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class AudioKeysTests
{
    private static readonly RecordId Step = RecordId.Hex(RecordTypes.Sequence, 1), Ouch = RecordId.Hex(RecordTypes.Sequence, 2);

    private const int Walk = 2, Hurt = 7, Grass = 4;

    private static Sequence LongNote() =>
        new(1, [new Instrument([.. Enumerable.Repeat((short)8000, 1_000_000)], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[1000]]);

    private static SoundsRecord Sounds()
    {
        var none = RecordId.Hex(RecordTypes.Sequence, 0);
        var surfaces = Enumerable.Range(0, 26)
            .Select(s => new FootstepSurface(null, s == Grass ? new SoundKey(65500, Walk, [Step]) : null, null)).ToList();
        return new SoundsRecord(RecordId.Decimal(RecordTypes.Sounds, 0),
            new MenuSounds(none, none, none, none, none, none, none, none, none, none),
            [], none, none, none, [], [], none, [], none, none, [null, new BodyKind(0.5f, surfaces)]);
    }

    private static (AudioKeys Keys, GameAudio Audio) Make()
    {
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Step, LongNote());
        bank.Add(Ouch, LongNote());
        var audio = new GameAudio(bank, new Settings(), 44100);
        return (new AudioKeys(audio, Sounds(), new Lcg()), audio);
    }

    private static readonly BodySounds Profile = new(1, [new SoundKey(32750, Hurt, [Ouch])]);

    private static bool Heard(GameAudio audio)
    {
        var frames = new short[2 * 441];
        audio.Render(frames);
        return frames.Any(f => f != 0);
    }

    [Fact]
    public void AKeyFiresOnTheFrameItsFracReachesForTheRequestLatchedOnTheFirstFrame()
    {
        // Half of a 20-frame animation is frame 10.
        Assert.False(AudioKeys.Reached(new SoundKey(32750, Hurt, []), Hurt, 9, 20));
        Assert.True(AudioKeys.Reached(new SoundKey(32750, Hurt, []), Hurt, 10, 20));
        Assert.False(AudioKeys.Reached(new SoundKey(32750, Hurt, []), Walk, 10, 20));
        // A frac of 0 is the first frame, and the whole is the last.
        Assert.True(AudioKeys.Reached(new SoundKey(0, Hurt, []), Hurt, 1, 20));
        Assert.True(AudioKeys.Reached(new SoundKey(65500, Hurt, []), Hurt, 20, 20));

        var (keys, audio) = Make();
        var body = new BodyAudio();
        // The request changed mid-animation, so it isn't latched until the animation starts again.
        keys.Process(body, Profile, Hurt, 10, 20, Vector3.Zero, 0, new Vector3(0, 0, 5), Vector3.Zero);
        Assert.False(Heard(audio));
        keys.Process(body, Profile, Hurt, 1, 20, Vector3.Zero, 0, new Vector3(0, 0, 5), Vector3.Zero);
        keys.Process(body, Profile, Hurt, 10, 20, Vector3.Zero, 0, new Vector3(0, 0, 5), Vector3.Zero);
        Assert.True(Heard(audio));
    }

    [Fact]
    public void FootstepsGoByTheLastSurfaceWithStepsOfItsOwn()
    {
        var (keys, audio) = Make();
        var body = new BodyAudio();
        keys.Process(body, Profile, Walk, 1, 20, Vector3.Zero, Grass, new Vector3(0, 0, 5), Vector3.Zero);
        // A wall (surface 1) and leaving the ground (0) keep the grass.
        keys.Process(body, Profile, Walk, 19, 20, Vector3.Zero, 1, new Vector3(0, 0, 5), Vector3.Zero);
        Assert.Equal(Grass, body.Surface);
        Assert.False(Heard(audio));
        keys.Process(body, Profile, Walk, 20, 20, Vector3.Zero, 0, new Vector3(0, 0, 5), Vector3.Zero);
        Assert.True(Heard(audio));
    }

    [Fact]
    public void LoudnessFallsToNothingAt15AndWrapsBeyond()
    {
        Assert.Equal(255, AudioKeys.VolumeAndPan(Vector3.Zero, new Vector3(0, 0, 5), Vector3.Zero).Volume);
        Assert.Equal(127, AudioKeys.VolumeAndPan(new Vector3(0, 0, -7.5f), new Vector3(0, 0, 5), Vector3.Zero).Volume);
        Assert.Equal(0, AudioKeys.VolumeAndPan(new Vector3(0, 0, -15), new Vector3(0, 0, 5), Vector3.Zero).Volume);
        // (int)(−1/3 × 255) is −85, whose low byte is 171.
        Assert.Equal(171, AudioKeys.VolumeAndPan(new Vector3(0, 0, -20), new Vector3(0, 0, 5), Vector3.Zero).Volume);
    }
}
