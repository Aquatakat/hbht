namespace Aidyn.Plugins;

/// <summary>
/// A plugin's <c>plugin.json</c> (ADR 0005). Its ID is also the namespace its records live in.
/// </summary>
/// <param name="Dependencies">Plugins that must be loaded, and load, before this one.</param>
/// <param name="LoadAfter">Plugins this one loads after when they're present.</param>
/// <param name="Import">Set on the base plugin only: what the importer made it from.</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? LoadAfter = null,
    ImportInfo? Import = null)
{
    public const string FileName = "plugin.json";
}

/// <summary>
/// Where <c>aidyn.base</c> came from (ADR 0001). The runtime re-imports when the importer's
/// <see cref="BasePlugin.ImporterVersion"/> or the ROM changes.
/// </summary>
/// <param name="Rom">The ROM's SHA-1.</param>
public sealed record ImportInfo(int Importer, string Rom, string RomVersion);

/// <summary>The base game's plugin, which the importer writes from the user's ROM.</summary>
public static class BasePlugin
{
    /// <summary>The folder it's imported to, under <see cref="DataPaths.Plugins"/>.</summary>
    public const string Folder = "aidyn.base";

    /// <summary>
    /// Raised whenever the importer's output changes, so existing imports are redone. 1 was the
    /// first slice for M1: map sets, chunks, scene objects, actors, and scene and collision assets.
    /// 2 adds locator nodes to scenes and Alaron's head. 3 adds every texture as PNG. 4 writes
    /// vertex colours and tints as linear, and adds terrains and every UI image as PNG.
    /// 5 adds the sky record. 6 gives scenes' billboards a node to turn to the camera and a card that
    /// rolls, and lists the textures each scene scrolls.
    /// </summary>
    public const int ImporterVersion = 6;
}

/// <summary>Where the importer writes and the runtime reads, outside the repository.</summary>
public static class DataPaths
{
    /// <summary><c>AIDYN_DATA</c> if set, else <c>Aidyn</c> in the local application data folder.</summary>
    public static string Root => Environment.GetEnvironmentVariable("AIDYN_DATA") is { Length: > 0 } root
        ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aidyn");

    public static string Plugins => Path.Combine(Root, "plugins");

    public static string Base => Path.Combine(Plugins, BasePlugin.Folder);
}
