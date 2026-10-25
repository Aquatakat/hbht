using System.Numerics;
using HBHT.Core.MapObjects;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class MapObjectPresenceTests
{
    private static Func<RecordId, bool> Set(params int[] flags) => id => flags.Any(f => Flag(f) == id);

    private static readonly RecordId Entity = RecordId.Parse("aidyn:entity/0x0001");

    private static MonsterPartyObject Party(RecordId? dialogue) =>
        new(0, Vector3.Zero, 5, PakVariant.Any, null, [], Entity, null, 1, 1, null, PartyFlags.None, dialogue, null)
        {
            Enabled = new FlagGate(Flag(10)),
            Used = new FlagWrite(Flag(11)),
        };

    private static ContainerObject Container(ContainerKind kind) =>
        new(0, Vector3.Zero, 5, PakVariant.Any, kind >= ContainerKind.Herb ? new FlagGate(Flag(12)) : null, kind,
            Flag(13), Flag(14), 0, 0, null, 0, null)
        {
            Enabled = kind >= ContainerKind.Herb ? new FlagGate(Flag(10)) : null,
        };

    [Fact]
    public void MostObjectsAreThereWhileTheirVisibleGateHolds()
    {
        var sound = new SoundObject(0, Vector3.Zero, 5, PakVariant.Any, new FlagGate(Flag(12), Invert: true),
            RecordId.Parse("aidyn:sample/0x0001"), 1, 0, 0, SoundFlags.None);
        Assert.True(MapObjectPresence.There(sound, Set()));
        Assert.False(MapObjectPresence.There(sound, Set(12)));
        Assert.True(MapObjectPresence.There(new SavePointObject(0, Vector3.Zero, 5, PakVariant.Any, null), Set()));
    }

    [Fact]
    public void AFightIsGoneOnceBeatenButAConversationStays()
    {
        Assert.False(MapObjectPresence.There(Party(null), Set()));
        Assert.True(MapObjectPresence.There(Party(null), Set(10)));
        Assert.False(MapObjectPresence.There(Party(null), Set(10, 11)));
        Assert.True(MapObjectPresence.There(Party(RecordId.Parse("aidyn:dialogue/0x0001")), Set(10, 11)));
    }

    [Fact]
    public void AChestIsGoneOnceOpenedOrDestroyed()
    {
        var chest = Container(ContainerKind.Chest);
        Assert.True(MapObjectPresence.There(chest, Set()));
        Assert.False(MapObjectPresence.There(chest, Set(13)));
        Assert.False(MapObjectPresence.There(chest, Set(14)));
    }

    [Fact]
    public void AHerbSourceNeedsBothGates()
    {
        var herb = Container(ContainerKind.Herb);
        Assert.False(MapObjectPresence.There(herb, Set(10)));
        Assert.False(MapObjectPresence.There(herb, Set(12)));
        Assert.True(MapObjectPresence.There(herb, Set(10, 12)));
        // Opening a herb source doesn't take it away; the original remembers it for a while instead.
        Assert.True(MapObjectPresence.There(herb, Set(10, 12, 13)));
    }
}
