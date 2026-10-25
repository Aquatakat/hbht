namespace HBHT.Plugins.Tests;

/// <summary>
/// Where the data folder is (<see cref="DataPaths.Resolve"/>): <c>HBHT_DATA</c>, else a portable install's <c>data</c> beside its
/// executables, else <c>HBHT</c> in the local application data folder. The rule's inputs are handed in, so the real environment
/// plays no part.
/// </summary>
public sealed class DataPathsTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private string App => Path.Combine(_temp.Root, "app");

    private string Local => Path.Combine(_temp.Root, "local");

    public DataPathsTests() => Directory.CreateDirectory(App);

    [Fact]
    public void TheVariableWinsOverEverything()
    {
        File.WriteAllText(Path.Combine(App, DataPaths.PortableMarker), "");
        Assert.Equal(Path.Combine(_temp.Root, "mine"), DataPaths.Resolve(Path.Combine(_temp.Root, "mine"), App, Local));
    }

    [Fact]
    public void AnInstallKeepsItsDataInTheLocalApplicationDataFolder()
    {
        Assert.Equal(Path.Combine(Local, "HBHT"), DataPaths.Resolve(null, App, Local));
        Assert.Equal(Path.Combine(Local, "HBHT"), DataPaths.Resolve("", App, Local));
        // The CLI and development hand in no app folder.
        Assert.Equal(Path.Combine(Local, "HBHT"), DataPaths.Resolve(null, null, Local));
    }

    [Fact]
    public void APortableInstallKeepsItsDataBesideItsExecutables()
    {
        File.WriteAllText(Path.Combine(App, DataPaths.PortableMarker), "");
        Assert.Equal(Path.Combine(App, "data"), DataPaths.Resolve(null, App, Local));
    }

    [Fact]
    public void AnImportBesideTheExecutablesIsUsedWithoutTheMarker()
    {
        Directory.CreateDirectory(Path.Combine(App, "data", "plugins", BasePlugin.Folder));
        Assert.Equal(Path.Combine(App, "data"), DataPaths.Resolve(null, App, Local));
    }

    [Fact]
    public void AnEmptyDataFolderBesideTheExecutablesIsNotAPortableInstall()
    {
        Directory.CreateDirectory(Path.Combine(App, "data", "plugins"));
        Assert.Equal(Path.Combine(Local, "HBHT"), DataPaths.Resolve(null, App, Local));
    }

    [Fact]
    public void TheLastRomIsKeptBesideThePluginsFolder()
    {
        var plugins = Path.Combine(_temp.Root, "data", "plugins");
        Assert.Equal(Path.Combine(_temp.Root, "data", "rom.txt"), DataPaths.LastRomFor(plugins));
        Assert.Equal(Path.Combine(_temp.Root, "data", "rom.txt"), DataPaths.LastRomFor(plugins + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheManifestRecordsTheImportsOptions()
    {
        _temp.AddRaw(BasePlugin.Folder, """
            { "id": "aidyn", "name": "Base", "version": "1.0.0",
              "import": { "importer": 61, "rom": "x", "romVersion": "NA 1.0", "options": { "fixMushroom": false } } }
            """);
        Assert.True(BasePlugin.IsImported(_temp.Root));
        Assert.Equal(new ImportOptions(FixMushroom: false), BasePlugin.ImportOf(_temp.Root)?.Options);
    }

    [Fact]
    public void AnImportWithoutOptionsHadTheDefaults()
    {
        _temp.AddRaw(BasePlugin.Folder, """{ "id": "aidyn", "name": "Base", "version": "1.0.0", "import": { "importer": 60, "rom": "x", "romVersion": "NA 1.0" } }""");
        Assert.Null(BasePlugin.ImportOf(_temp.Root)?.Options);
        Assert.True((BasePlugin.ImportOf(_temp.Root)?.Options ?? ImportOptions.Default).FixMushroom);
    }

    [Fact]
    public void NoImportHasNothingToRead()
    {
        Assert.False(BasePlugin.IsImported(_temp.Root));
        Assert.Null(BasePlugin.ImportOf(_temp.Root));
    }
}
