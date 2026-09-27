using Aidyn.Rom.Borg;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class ParticleTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void ParticleTexturesAreTheFirstBorg1s(RomVersionId id)
    {
        var slots = ParticleTextures.Read(TestRoms.Get(id));

        Assert.Equal([3, 0, 1, null, 2, null, 4, 5, 6, 7, 8, 9, 10, 11, null, null, 12], slots);
    }

    /// <summary>Emitters on their own scene's locators export as nodes under the locator's transform.</summary>
    [Fact]
    public void EmittersOnOwnLocatorsExportAsNodes()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        int placed = 0, listed = 0;
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Scene))
        {
            var scene = Borg5Scene.Parse(archive.Read(listing.Index));
            if (scene.ParticleEmitters.Count == 0)
                continue;
            var glb = SceneGltf.Export(scene, archive.Read, listing.Index);
            var json = System.Text.Json.JsonDocument.Parse(glb.AsMemory(20, BitConverter.ToInt32(glb, 12))).RootElement;
            foreach (var e in json.GetProperty("scenes")[0].GetProperty("extras").GetProperty("particleEmitters").EnumerateArray())
            {
                listed++;
                placed += e.TryGetProperty("node", out _) ? 1 : 0;
            }
        }

        Assert.Equal((218, 183), (listed, placed));
    }

    [Fact]
    public void EmittersUseKnownFieldsTexturesAndLocators()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var slots = ParticleTextures.Read(rom);
        var flags = Borg5EmitterFlags.None;
        var requests = new SortedSet<int>();
        int unknown40 = 0, started = 0;
        var missing = new List<int>();
        int own = 0, attached = 0;
        int emitters = 0;
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Scene))
        {
            var scene = Borg5Scene.Parse(archive.Read(listing.Index));
            if (scene.ParticleEmitters.Count == 0)
                continue;
            var sceneLocators = scene.Models.Select(m => Borg2Geometry.Parse(archive.Read(m)).Unknown24 & 0xFF).ToHashSet();
            foreach (var e in scene.ParticleEmitters)
            {
                emitters++;
                flags |= e.Flags;
                Assert.NotNull(slots[e.Texture]);
                unknown40 += e.Unknown40 != 0 ? 1 : 0;
                if (e.Flags.HasFlag(Borg5EmitterFlags.ActorRequest))
                {
                    started++;
                    requests.Add(e.Request);
                }
                if (e.Locator.Scene != 0)
                    attached++;
                else if (sceneLocators.Contains((uint)e.Locator.Index))
                    own++;
                else
                    missing.Add(listing.Index);
            }
        }

        Assert.Equal(218, emitters);
        // The data never moves an emitter or gives it a second acceleration.
        Assert.Equal(Borg5EmitterFlags.BounceAtZero | Borg5EmitterFlags.ExactTargetJitter | Borg5EmitterFlags.ExactSpawnJitter |
            Borg5EmitterFlags.LocatorNormal | Borg5EmitterFlags.ActorRequest | Borg5EmitterFlags.Unknown40 |
            Borg5EmitterFlags.AccelerationChanges | Borg5EmitterFlags.Target | Borg5EmitterFlags.Fade |
            Borg5EmitterFlags.Scales, flags);
        Assert.Equal(20, unknown40);
        // Two emitters lack the flag that lets an actor start them, and nothing else does.
        Assert.Equal(216, started);
        Assert.Equal([0, 2, 5, 7, 8, 9, 13, 16, 17, 18], requests);
        // Most sit on a locator of their own scene; the rest on a scene attached at run time, such
        // as the actor casting a spell. Scene 10015's names a locator it doesn't have, which would
        // crash the game if the emitter started.
        Assert.Equal((183, 34), (own, attached));
        Assert.Equal([10015], missing);
    }
}
