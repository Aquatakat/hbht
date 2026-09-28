using Aidyn.Plugins;
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
