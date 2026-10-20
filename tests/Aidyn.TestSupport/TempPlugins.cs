using Aidyn.Plugins;

namespace Aidyn.TestSupport;

/// <summary>
/// A temporary folder of plugins a test writes by hand: each one's <c>plugin.json</c> and <c>records/*.json</c>.
/// The folder's name has a GUID in it, so tests running at once, in this project or another, never share one,
/// and it's deleted on <see cref="Dispose"/>.
/// </summary>
public sealed class TempPlugins : IDisposable
{
    /// <summary>A plugin's manifest, as <see cref="Add"/> writes it.</summary>
    public static string Manifest(string id, params string[] dependencies)
    {
        var deps = dependencies.Length == 0 ? "" : $", \"dependencies\": [{string.Join(", ", dependencies.Select(d => $"\"{d}\""))}]";
        return $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1.0.0"{{deps}} }""";
    }

    public TempPlugins() => Root = Path.Combine(Path.GetTempPath(), "aidyn-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>The folder every plugin, and anything else the test writes, goes in.</summary>
    public string Root { get; }

    /// <summary>
    /// Writes a plugin folder named <paramref name="id"/> with its manifest and these record files
    /// (<c>records/&lt;File&gt;</c>), and returns the folder.
    /// </summary>
    public string Add(string id, string[]? dependencies, params (string File, string Json)[] files)
    {
        var dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), Manifest(id, dependencies ?? []));
        foreach (var (file, json) in files)
        {
            var path = Path.Combine(dir, "records", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        return dir;
    }

    /// <summary>Writes a plugin folder named <paramref name="folder"/> with this <c>plugin.json</c> as it is, and returns the folder.</summary>
    public string AddRaw(string folder, string manifestJson)
    {
        var dir = Path.Combine(Root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), manifestJson);
        return dir;
    }

    /// <summary>Writes the base plugin, <c>aidyn</c>, with these record files and loads it alone.</summary>
    public PluginSet Aidyn(params (string File, string Json)[] files) => PluginSet.Load([Add("aidyn", null, files)]);

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
