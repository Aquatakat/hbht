namespace Aidyn.Plugins.Tests;

/// <summary>The order installed plugins load in, and the check that the base plugin was imported by the current importer.</summary>
public sealed class PluginLoadOrderTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Manifest(string id, string[]? dependencies = null, string[]? loadAfter = null, string? import = null)
    {
        static string List(string[] ids) => "[" + string.Join(", ", ids.Select(i => $"\"{i}\"")) + "]";
        var extra = (dependencies is null ? "" : $", \"dependencies\": {List(dependencies)}") +
            (loadAfter is null ? "" : $", \"loadAfter\": {List(loadAfter)}") +
            (import is null ? "" : $", \"import\": {import}");
        return $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1.0.0"{{extra}} }""";
    }

    private void Base(int? importer = null) => _temp.AddRaw(BasePlugin.Folder, Manifest("aidyn",
        import: importer is { } n ? $$"""{ "importer": {{n}}, "rom": "abc", "romVersion": "NA 1.0" }""" : null));

    private void Mod(string id, string[]? dependencies = null, string[]? loadAfter = null) =>
        _temp.AddRaw(id, Manifest(id, dependencies, loadAfter));

    private string[] Order() => [.. PluginSet.LoadInstalled(_temp.Root).Plugins.Select(p => p.Manifest.Id)];

    [Fact]
    public void WithoutConstraintsPluginsLoadByIdAfterTheBase()
    {
        Base();
        Mod("zeta");
        Mod("alpha");
        Mod("mid");

        Assert.Equal(["aidyn", "alpha", "mid", "zeta"], Order());
    }

    [Fact]
    public void ADependencyThatSortsLaterStillLoadsFirst()
    {
        Base();
        Mod("alpha", dependencies: ["zeta"]);
        Mod("zeta", dependencies: ["aidyn"]);

        Assert.Equal(["aidyn", "zeta", "alpha"], Order());
    }

    [Fact]
    public void LoadAfterOrdersWhenThePluginIsThere()
    {
        Base();
        Mod("alpha", loadAfter: ["zeta"]);
        Mod("zeta");
        Mod("beta");

        Assert.Equal(["aidyn", "beta", "zeta", "alpha"], Order());
    }

    [Fact]
    public void ALoadAfterThatIsntInstalledIsNotAnError()
    {
        Base();
        Mod("alpha", loadAfter: ["missing", "aidyn"]);

        Assert.Equal(["aidyn", "alpha"], Order());
    }

    [Fact]
    public void AMissingDependencyIsStillAnError()
    {
        Base();
        Mod("alpha", dependencies: ["missing"]);

        var error = Assert.Throws<InvalidDataException>(Order);
        Assert.Contains("alpha needs missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACycleIsAnErrorNamingThePluginsInIt()
    {
        Base();
        Mod("one", dependencies: ["two"]);
        Mod("two", loadAfter: ["three"]);
        Mod("three", dependencies: ["one"]);
        Mod("after_the_cycle", dependencies: ["one"]);
        Mod("fine");

        var error = Assert.Throws<InvalidDataException>(Order);
        Assert.Contains("one, three, two", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("after_the_cycle", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderMustBeNamedForItsPluginsId()
    {
        Base();
        _temp.AddRaw("some_folder", Manifest("other_id"));

        var error = Assert.Throws<InvalidDataException>(Order);
        Assert.Contains("some_folder", error.Message, StringComparison.Ordinal);
        Assert.Contains("other_id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANotImportedGameIsDiagnosedWithTheFolderItLookedIn()
    {
        var diagnosis = BasePlugin.Diagnose(_temp.Root);

        Assert.NotNull(diagnosis);
        Assert.Contains("hasn't been imported", diagnosis, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_temp.Root, BasePlugin.Folder), diagnosis, StringComparison.Ordinal);
        Assert.Throws<IOException>(() => PluginSet.LoadInstalledChecked(_temp.Root));
    }

    [Fact]
    public void AnOldImportIsDiagnosedWithBothVersions()
    {
        Base(importer: BasePlugin.ImporterVersion - 1);

        var diagnosis = BasePlugin.Diagnose(_temp.Root);

        Assert.Equal(
            $"The import is out of date (imported by importer {BasePlugin.ImporterVersion - 1}, now {BasePlugin.ImporterVersion}). Run: aidyn import <rom>",
            diagnosis);
        var error = Assert.Throws<IOException>(() => PluginSet.LoadInstalledChecked(_temp.Root));
        Assert.Equal(diagnosis, error.Message);
    }

    [Fact]
    public void ABaseWithNoImportInfoIsOutOfDate()
    {
        Base();

        Assert.Contains("imported by importer unknown", BasePlugin.Diagnose(_temp.Root), StringComparison.Ordinal);
    }

    [Fact]
    public void ACurrentImportPassesAndLoads()
    {
        Base(importer: BasePlugin.ImporterVersion);
        Mod("alpha", dependencies: ["aidyn"]);

        Assert.Null(BasePlugin.Diagnose(_temp.Root));
        Assert.Equal(["aidyn", "alpha"], PluginSet.LoadInstalledChecked(_temp.Root).Plugins.Select(p => p.Manifest.Id));
    }
}
