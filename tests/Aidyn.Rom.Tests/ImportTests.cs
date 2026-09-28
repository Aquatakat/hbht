using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Import;

namespace Aidyn.Rom.Tests;

public sealed class ImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aidyn-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheBasePluginLoadsAndItsReferencesResolve()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var dir = Path.Combine(_root, BasePlugin.Folder);
        BaseImporter.Import(rom, dir);

        var set = PluginSet.Load([dir]);
        var import = set.Plugins[0].Manifest.Import!;
        Assert.Equal((BasePlugin.ImporterVersion, rom.Sha1), (import.Importer, import.Rom));

        var mapSets = set.All<MapSetRecord>(RecordTypes.MapSet).ToList();
        Assert.Equal(17, mapSets.Count);
        Assert.Equal(543, mapSets.Sum(s => s.Chunks.Count));
        var overworld = set.Get<MapSetRecord>(RecordId.Parse("aidyn:mapset/0"));
        Assert.Equal(("Overworld", 100f), (overworld.Label, overworld.CellSize));
        Assert.Contains(overworld.Chunks, c => c.Name == "I08");

        var sceneObjects = 0;
        foreach (var chunk in mapSets.SelectMany(s => s.Chunks))
        {
            foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
                Assert.NotNull(set.FindAsset(scene, "glb"));
            Assert.NotNull(set.FindAsset(chunk.Map, "glb"));
            foreach (var o in set.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>())
            {
                sceneObjects++;
                foreach (var model in o.Models)
                {
                    if (model.Model.Type == RecordTypes.Actor)
                        Assert.NotNull(set.FindAsset(set.Get<ActorRecord>(model.Model).Scene, "glb"));
                    else
                        Assert.NotNull(set.FindAsset(model.Model, "glb"));
                }
            }
        }
        Assert.True(sceneObjects > 7000, $"{sceneObjects} scene objects");

        // A chunk's collision reads back as its Borg9's triangles, in the chunk's own coordinates.
        var archive = Borg.BorgArchive.Open(rom);
        var i08 = overworld.Chunks.Single(c => c.Name == "I08");
        var borg9 = Maps.Borg9Map.Parse(archive.Read(Convert.ToInt32(i08.Map.Key, 16)));
        var collision = CollisionMesh.Read(File.ReadAllBytes(set.FindAsset(i08.Map, "glb")!));
        Assert.Equal(borg9.Triangles.Count, collision.Count);
        for (var t = 0; t < collision.Count; t++)
        {
            var (expected, actual) = (borg9.Triangles[t], collision[t]);
            Assert.Equal((borg9.Vertices[expected.A], borg9.Vertices[expected.B], borg9.Vertices[expected.C]), (actual.A, actual.B, actual.C));
            Assert.Equal((int)expected.Surface, actual.Surface);
            Assert.Equal((int)expected.Flags, (int)actual.Flags);
        }
        Assert.Contains(collision, t => t.Flags.HasFlag(CollisionFlags.SeeThrough));

        // Every texture is there as PNG, such as the one bodies' shadows use (BORG1_PlayerShadow).
        var textures = archive.Listings.Where(l => l.Type == Borg.BorgType.Texture).ToList();
        Assert.All(textures, l => Assert.NotNull(set.FindAsset(RecordId.Hex(RecordTypes.Texture, l.Index), "png")));
        var shadow = File.ReadAllBytes(set.FindAsset(RecordId.Parse("aidyn:texture/0x0003"), "png")!);
        Assert.Equal(Graphics.Png.Encode(Graphics.Borg1Texture.Parse(archive.Read(3)).Decode()), shadow);
    }

    [Fact]
    public void ImportsAreDeterministic()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        BaseImporter.Import(rom, first);
        BaseImporter.Import(rom, second);

        var files = Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(first, f)).Order().ToList();
        Assert.Equal(files, Directory.EnumerateFiles(second, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(second, f)).Order());
        foreach (var file in files)
            Assert.True(File.ReadAllBytes(Path.Combine(first, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, file))), file);
    }

    [Fact]
    public void ResearchRomsAreRefused()
    {
        var rom = TestRoms.Get(RomVersionId.Debug);
        Assert.Throws<UnsupportedRomException>(() => BaseImporter.Import(rom, Path.Combine(_root, "debug")));
    }
}
