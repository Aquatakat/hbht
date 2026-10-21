using System.Numerics;
using Aidyn.Core.Audio;
using Aidyn.Core.Chunks;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using CharacterParty = Aidyn.Core.Characters.Party;

namespace Aidyn.Core.Tests;

public class MapSoundsTests
{
    private static readonly RecordId Tune = RecordId.Hex(RecordTypes.Sequence, 1), Song = RecordId.Hex(RecordTypes.Sequence, 2);

    /// <summary>A long note at full volume.</summary>
    private static Sequence LongNote() =>
        new(1, [new Instrument([.. Enumerable.Repeat((short)8000, 1_000_000)], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[1000]]);

    private static GameAudio Audio()
    {
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Tune, LongNote());
        bank.Add(Song, LongNote());
        return new GameAudio(bank, 44100);
    }

    private static SoundObject Sound(int index, SoundFlags flags, RecordId? sequence = null, float minWait = 0, float maxWait = 0) =>
        new(index, new Vector3(0, 0, 0), 10, PakVariant.Any, null, sequence ?? Tune, 1, minWait, maxWait, flags);

    private static LoadedMap Map(ChunkKey key, params SoundObject[] sounds) =>
        LoadedMap.Activate(new MapRecord(RecordId.Hex(RecordTypes.Map, 1), sounds), key, ItemData.Flags(), new MapObjectMemory(), ItemData.NewParty(), new Lcg());

    private static readonly Vector3 Eye = new(0, 5, 20);

    [Fact]
    public void ASoundPlaysWithinItsRadiusOfThePlayerAndStopsOutsideIt()
    {
        var audio = Audio();
        var sounds = new MapSounds(audio, new Lcg(), _ => true);
        sounds.Load(Map(Worlds.Chunk(0), Sound(0, SoundFlags.None)));
        sounds.Step(new Vector3(20, 0, 0), Eye, new Vector3(20, 0, 0));
        Assert.Equal(0, sounds.Playing);

        sounds.Step(new Vector3(5, 0, 0), Eye, new Vector3(5, 0, 0));
        Assert.Equal(1, sounds.Playing);

        // Its fade was its loudness, half at 5 of 10, which falls 1/90 a tick before it stops.
        for (var i = 0; i < 40; i++)
            sounds.Step(new Vector3(20, 0, 0), Eye, new Vector3(20, 0, 0));
        Assert.Equal(1, sounds.Playing);
        for (var i = 0; i < 10; i++)
            sounds.Step(new Vector3(20, 0, 0), Eye, new Vector3(20, 0, 0));
        Assert.Equal(0, sounds.Playing);
    }

    [Fact]
    public void ASoundWhoseFlagsDontHoldWaitsUntilTheyDo()
    {
        var audio = Audio();
        var there = false;
        var sounds = new MapSounds(audio, new Lcg(), _ => there);
        sounds.Load(Map(Worlds.Chunk(0), Sound(0, SoundFlags.NoFalloff)));
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(0, sounds.Playing);
        there = true;
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(1, sounds.Playing);
    }

    [Fact]
    public void ARepeatingSoundStartsAtOnceAndAgainAfterItsWait()
    {
        var audio = Audio();
        var sounds = new MapSounds(audio, new Lcg(), _ => true);
        sounds.Load(Map(Worlds.Chunk(0), Sound(0, SoundFlags.Repeat, minWait: 1, maxWait: 1)));
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(1, sounds.Playing);
        // A second's wait, in ticks; then it's cut and started again.
        for (var i = 0; i < 60; i++)
            sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(1, sounds.Playing);
    }

    [Fact]
    public void MusicPlaysOnIntoTheNextChunkThatHasTheSame()
    {
        var audio = Audio();
        var sounds = new MapSounds(audio, new Lcg(), _ => true);
        var first = Map(Worlds.Chunk(0), Sound(0, SoundFlags.Music | SoundFlags.Loop | SoundFlags.NoFalloff, Song));
        sounds.Load(first);
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(1, sounds.Playing);

        sounds.Unload(first);
        sounds.Load(Map(Worlds.Chunk(1), Sound(0, SoundFlags.Music | SoundFlags.Loop | SoundFlags.NoFalloff, Song)));
        for (var i = 0; i < 5; i++)
            sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(1, sounds.Playing);
    }

    [Fact]
    public void MusicNoChunkTakesUpStopsAfterTwoTicks()
    {
        var audio = Audio();
        var sounds = new MapSounds(audio, new Lcg(), _ => true);
        var first = Map(Worlds.Chunk(0), Sound(0, SoundFlags.Music | SoundFlags.Loop | SoundFlags.NoFalloff, Song));
        sounds.Load(first);
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        sounds.Unload(first);
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(1, sounds.Playing);
        sounds.Step(Vector3.Zero, Eye, Vector3.Zero);
        Assert.Equal(0, sounds.Playing);
    }

    [Fact]
    public void ASoundToTheCamerasRightPansRight()
    {
        // Looking along −Z, +X is to the right.
        Assert.True(AudioLevels.Pan(new Vector3(5, 0, 0), new Vector3(0, 0, 10), Vector3.Zero, 0) > 0);
        Assert.True(AudioLevels.Pan(new Vector3(-5, 0, 0), new Vector3(0, 0, 10), Vector3.Zero, 0) < 0);
    }
}
