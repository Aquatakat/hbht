using System.Numerics;
using System.Text.Json;
using HBHT.Core.Audio;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// A wanderer whose party's entity is a dialogue entity makes the sounds its record holds (docs/systems/audio.md, Bodies' sounds),
/// the profile 1.0 borrows for it, and is heard to step.
/// </summary>
public class DialogueWandererSoundTests
{
    private static readonly RecordId Talker = RecordId.Hex(RecordTypes.Entity, 0x1400), Step = RecordId.Hex(RecordTypes.Sequence, 1);
    private static readonly RecordId MapId = RecordId.Hex(RecordTypes.Map, 0x1000);

    private const int Walk = 2, Grass = 4;

    private static readonly BodySounds Profile = new(1, [new SoundKey(0, RecordIds.Request(7), [Step])]);

    private static Wanderer Load(BodySounds? sounds)
    {
        using var temp = new TempPlugins();
        var record = new DbRecord(Talker, "Amann", null, new EntityBody(RecordId.Hex(RecordTypes.Actor, 1), 0.55f, 1, 2), sounds);
        var plugins = temp.Aidyn(("dialogentities.json", JsonSerializer.Serialize(new[] { record }, PluginJson.Options)));
        var node = new WanderNodeObject(0, new Vector3(5, 0, 5), 0, null, 2, 0, 0, 1000, WanderFlags.Move, []);
        var party = new MonsterPartyObject(1, new Vector3(5, 0, 5), 20, null, [], Talker, null, 0, 0,
            MapRecord.VoxelId(MapId, 0), PartyFlags.Walks, null, null);
        var map = LoadedMap.Activate(new MapRecord(MapId, [node, party]), Worlds.Chunk(0), ItemData.Flags(), new MapObjectMemory(), ItemData.NewParty(), new Lcg());
        return Assert.Single(new Wanderers(plugins).Load(map, 20, Worlds.Flat(), new MapObjectMemory()));
    }

    [Fact]
    public void ADialogueEntitysWandererHasTheSoundsItsRecordHolds()
    {
        Assert.Equal(Profile, Load(Profile).Sounds, new BodySoundsComparer());
        Assert.Null(Load(null).Sounds);
    }

    [Fact]
    public void ADialogueEntitysWandererStepsAudibly()
    {
        var wanderer = Load(new BodySounds(1, [new SoundKey(65500, RecordIds.Request(Walk), [Step])]));
        var none = RecordId.Hex(RecordTypes.Sequence, 0);
        var surfaces = Enumerable.Range(0, 26)
            .Select(s => new FootstepSurface(null, s == Grass ? new SoundKey(65500, RecordIds.Request(Walk), [Step]) : null, null)).ToList();
        var sounds = new SoundsRecord(RecordId.Decimal(RecordTypes.Sounds, 0),
            new MenuSounds(none, none, none, none, none, none, none, none, none, none),
            [], none, none, none, [], [], none, [], none, none, [null, new BodyKind(0.5f, surfaces)]);
        var bank = new SoundBank(PluginSet.Load([]));
        bank.Add(Step, new Sequence(1, [new Instrument([.. Enumerable.Repeat((short)8000, 1_000_000)], null)], [0x7A, 0x22, 0x76, 0xFF, 0x80, 0x00, .. new byte[1000]]));
        var audio = new GameAudio(bank, 44100);
        var keys = new AudioKeys(audio, sounds, new Lcg());

        var eye = new Vector3(0, 0, 5);
        keys.Process(wanderer.Audio, wanderer.Sounds, Walk, 1, 20, Vector3.Zero, Grass, eye, Vector3.Zero);
        keys.Process(wanderer.Audio, wanderer.Sounds, Walk, 20, 20, Vector3.Zero, Grass, eye, Vector3.Zero);
        var frames = new short[2 * 441];
        audio.Render(frames);
        Assert.Contains(frames, f => f != 0);
    }

    private sealed class BodySoundsComparer : IEqualityComparer<BodySounds?>
    {
        public bool Equals(BodySounds? a, BodySounds? b) =>
            a is not null && b is not null && a.Kind == b.Kind && a.Keys.Count == b.Keys.Count
            && a.Keys.Zip(b.Keys).All(k => (k.First.Frac, k.First.Request) == (k.Second.Frac, k.Second.Request) && k.First.Sounds.SequenceEqual(k.Second.Sounds));

        public int GetHashCode(BodySounds? s) => s?.Kind ?? 0;
    }
}
