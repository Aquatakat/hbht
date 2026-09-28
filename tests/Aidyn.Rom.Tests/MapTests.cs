using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Tests;

public class MapTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void ChunkListsNameScenesAndCollision(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var archive = BorgArchive.Open(rom);
        var chunks = MapChunks.Read(rom);

        Assert.Equal(543, chunks.Count);
        Assert.Equal([172, 19, 21, 4, 42, 11, 25, 28, 34, 16, 5, 26, 63, 36, 7, 6, 28],
            Enum.GetValues<MapSet>().Select(s => chunks.Count(c => c.Set == s)));
        Assert.All(chunks, c =>
        {
            Assert.Equal(BorgType.Scene, archive.Listings[c.Scene].Type);
            Assert.True(c.AltScene == 0 || archive.Listings[c.AltScene].Type == BorgType.Scene);
            Assert.Equal(BorgType.MapChunk, archive.Listings[c.Collision].Type);
        });
        Assert.Equal("Overworld A04", chunks[0].ToString());
    }

    [Fact]
    public void EveryBorg9sMapObjectsParse()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var db = GameDb.Read(rom);
        var chunks = MapChunks.Read(rom).Select(c => (c.Set, c.Row, c.Column)).ToHashSet();
        var flagCount = db.GameStateFunnel.Flags.Count;

        var objects = archive.Listings.Where(l => l.Type == BorgType.MapChunk)
            .SelectMany(l => Borg9Voxels.Read(archive.Read(l.Index))).ToList();

        Assert.Equal([8447, 371, 777, 1038, 2062, 1085, 9329, 899, 246, 364, 302, 3, 1168],
            Enum.GetValues<VoxelType>().Select(t => objects.Count(o => o.Type == t)));
        Assert.All(objects, o =>
        {
            Assert.InRange(o.FlagA, 0, flagCount - 1);
            Assert.InRange(o.FlagB, 0, flagCount - 1);
            Assert.InRange(o.FlagC, 0, flagCount - 1);
        });

        var ignoredWrites = new List<int>();
        foreach (var o in objects)
        {
            switch (o.Data)
            {
                case DialogueData d:
                    Assert.Equal(BorgType.Dialogue, archive.Listings[d.Script].Type);
                    break;
                case MonsterPartyData m:
                    Assert.True(m.Dialogue == 0 || archive.Listings[m.Dialogue].Type == BorgType.Dialogue);
                    Assert.All(m.Enemies, e => Assert.Equal(DbType.Entity, e.Enemy.Type));
                    break;
                case TeleporterData t:
                    Assert.Contains(t.Destination, chunks);
                    Assert.All([t.LockFlag, t.UnlockFlag, t.TrapFlag, t.DisarmFlag, t.SecretFlag],
                        f => Assert.InRange(f, 0, flagCount - 1));
                    break;
                case ContainerData c:
                    Assert.True(c.Loot.IsNone || c.Loot.Type == DbType.Loot);
                    Assert.InRange(c.OpenFlag, 0, flagCount - 1);
                    Assert.InRange(c.ExplodeFlag, 0, flagCount - 1);
                    break;
                case TriggerData { Kind: TriggerKind.SetFlag } t:
                    Assert.InRange(t.A, 0, flagCount - 1);
                    if (t.A < 2 || t.A >= db.GameStateFunnel.BitFlagCount)
                        ignoredWrites.Add(t.A);
                    break;
            }
        }
        // The game only writes plain bit flags, so two triggers do nothing: one names derived flag
        // 5495 and one names flag 0.
        Assert.Equal([5495, 0], ignoredWrites);
    }

    [Fact]
    public void ObjectsNameAssetsAndObjectsThatExist()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var shapes = new List<string>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.MapChunk))
        {
            var objects = Borg9Voxels.Read(archive.Read(listing.Index));
            var refPoints = objects.Select(o => o.Data).OfType<ReferencePointData>().Select(r => r.Id).ToHashSet();
            foreach (var o in objects)
            {
                switch (o.Data)
                {
                    case SceneData s:
                        var type = s.SceneFlags.HasFlag(SceneObjectFlags.Actor) ? BorgType.Actor : BorgType.Scene;
                        Assert.All(s.Models, m => Assert.Equal(type, archive.Listings[m.Borg].Type));
                        shapes.Add($"{s.Lods}/{s.Models.Count} {type}");
                        break;
                    case AudioData a:
                        Assert.Equal(BorgType.Sequence, archive.Listings[a.Sequence].Type);
                        break;
                    case WanderNodeData w:
                        Assert.All(w.Next, n => Assert.Equal(VoxelType.WanderNode, objects[n].Type));
                        break;
                    case CameraData c:
                        Assert.True(c.RefPoint == 0 || refPoints.Contains(c.RefPoint), $"Camera {o.Index} in {listing.Index}");
                        break;
                    case LightData l:
                        Assert.InRange(l.Kind, LightKind.Static, LightKind.Random);
                        break;
                }
                if (o.Type is VoxelType.Code or VoxelType.SavePoint)
                    Assert.Null(o.Data);
            }
        }

        // Levels of detail, a container's model with its opened and destroyed ones, or an actor.
        Assert.Equal(new Dictionary<string, int>
        {
            ["1/1 Scene"] = 6629, ["2/2 Scene"] = 1284, ["1/3 Scene"] = 371, ["1/1 Actor"] = 163,
        }, shapes.CountBy(s => s).ToDictionary());
    }
}
