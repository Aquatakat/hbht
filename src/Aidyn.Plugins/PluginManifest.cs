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
    /// rolls, and lists the textures each scene scrolls. 7 adds the flags, the journal and the state flags.
    /// 8 adds the dialogue scripts. 9 adds the random skill checks flag to the state flags. 10 adds
    /// every other type of map object, and the loot tables' names. 11 writes the maps a file per map set.
    /// 12 adds the fonts, the string tables and entities' portraits.
    /// 13 gives entities and spells their fields, and adds the stats, skills, weapon skills and progression.
    /// 14 gives weapons, armor, items and loot tables their fields, and adds shops, recipes and the inventory's rules.
    /// 15 gives spells their icons and descriptions, and items their descriptions.
    /// 16 gives spells their place in the spell table, which loading a save orders spellbooks by.
    /// 17 gives collision triangles the object a trigger triangle runs, or a toggle's id (<c>_TARGET</c>).
    /// 18 gives containers their reach, and maps the grid of the objects each cell lists.
    /// 19 names camera objects' flags 0x8, 0x10, 0x100 and 0x200.
    /// 20 gives entities and dialogue entities their bodies: actor, radius, scale and height.
    /// 21 gives recruitable members their joined, left and died flags, and whether their sheet is kept.
    /// 22 gives weapons, shields and six items their models.
    /// 23 adds the world map and camping. 24 adds the minimaps. 25 adds sequences and samples.
    /// </summary>
    public const int ImporterVersion = 25;
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

    /// <summary>Where saved games go (<see cref="Saves.SaveGame"/>).</summary>
    public static string Saves => Path.Combine(Root, "saves");

    /// <summary>The player's settings: volumes, graphics and fidelity (<c>Aidyn.Core.Settings</c>).</summary>
    public static string Settings => Path.Combine(Root, "settings.json");
}
