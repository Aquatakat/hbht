namespace HBHT.Plugins;

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
    /// 26 adds the engine's sounds and the map sets' music. 27 adds the weather, on the terrains and its own record. 28 marks the lights that face the camera.
    /// 29 gives maps the neighbours they always draw, and scene objects that go out once reached (Oriana's path lights).
    /// 30 adds the scenes' particle emitters and the cinematics. 31 gives the weather record the rain sky, and how rain and snow are drawn.
    /// 32 marks the scene objects' levels of detail that are empty scenes.
    /// 33 gives the cinematics their titles and each shot its scene, length, captions and fades.
    /// 34 keeps the textures of environment-mapped display lists.
    /// 35 marks the scenes whose transforms mirror a model, and gives the cinematics their aspect ratio.
    /// 36 gives each light node its transform's scale, which tints the light. 37 adds the credits' cards.
    /// 38 adds the new game record. 39 gives each terrain the Battle arenas its battles take, and adds the combat record.
    /// 40 adds to it who never moves in battle.
    /// 41 gives spells and potions their effects (ADR 0020), and adds the magic record.
    /// 42 adds to the combat record the weapons that fly, and Spikes, which may strike in melee.
    /// 43 adds to the combat record the bosses whose AI takes the bosses' styles.
    /// 44 adds to the combat record the boss flags, the weapon classes that drop, the Goblin Ambush's rules and the game over; gives
    /// scripts what follows their end, and terrains their camp ambushes, with the ambush's level tiers on the camping record.
    /// 45 adds to the combat record the combat menus' and Sense Aura's text, which the code keeps as C strings.
    /// 46 adds to the combat record the area spell the AI aims (Fireball) and the flasks that make it spread out.
    /// 47 gives spells and potions what they show in a battle (<c>visuals</c>), and the magic record the actors a refused and a resisted
    /// cast play.
    /// 48 adds to the combat record the Shadow's rules (its aura, slowing, vanishing and merging, with the merge's boost), and to the
    /// Goblin Ambush's encounter its heal as it starts and its hold on Alaron.
    /// 49 adds the triggers (roadmap 2.10): a new game's opening and its script, the scripts' ends that lead to the game over and the
    /// cinematics, which scripts no longer carry, and what follows Roog's cinematic and the ending's.
    /// 50 gives each entity's body the share of its attack's animation at which the blow lands (<c>strike</c>).
    /// 51 gives each scene's locators a node turned by the locator model's rotation, which what a body holds hangs from, and the combat
    /// record the entities whose weapon isn't drawn and those who hold a bow in the other hand.
    /// 52 reads the last of the code's content tables (roadmap 1H): the actors combat always animates (<c>alwaysAnimated</c> on actors),
    /// the title screen's fly-throughs (<c>aidyn:title/0</c>) and the places each map set puts the player by number (<c>spawns</c>).
    /// 53 writes how each potion is used (<c>use</c>: drunk in trek, in a battle, or thrown) and which spells the AI treats as an area
    /// (<c>area</c> on the spell, where the combat record's <c>areaSpells</c> was), ADR 0021.
    /// 54 gives the combat record who can't be attacked or cast at (<c>untargetable</c>), apart from who lets the party flee.
    /// 55 turns Alaron toward Oriana where the Goblin Ambush's end leaves him: the original's kept facing is its reversed one.
    /// 56 gives each chunk the world map region flag entering it sets (<c>region</c>).
    /// 57 cuts cut-outs out from an eighth of texel alpha, as the N64's coverage does (<c>alphaCutoff</c> 0.125 for 0.5).
    /// 58 writes how a trapped container's explosion is drawn (<c>aidyn:explosion/0</c>): its particles' texture.
    /// 59 marks a lit list's vertices that the RSP loads second of a pair (<c>TEXCOORD_1</c>'s S of 1), which a body's lights don't reach.
    /// 60 moves the Roog Cave's mushroom (<c>aidyn:voxel/0x1205/12</c>) and its collision out of the passage's neck (<c>MapCorrections</c>).
    /// 61 gives each title fly-through its animation's length (<c>length</c>), and names its coordinates' origin (<c>originRow</c>, <c>originColumn</c>).
    /// </summary>
    public const int ImporterVersion = 61;

    /// <summary>
    /// What's wrong with the imported base plugin under <paramref name="pluginsDirectory"/>, worded for the player, or null if
    /// it's there and the current importer wrote it: it hasn't been imported, or it was imported by another importer, so
    /// it's to be imported again. It reads only the base plugin's manifest. The runtime, the CLI and the editor all go by this.
    /// </summary>
    public static string? Diagnose(string pluginsDirectory)
    {
        var directory = Path.Combine(pluginsDirectory, Folder);
        if (!File.Exists(Path.Combine(directory, PluginManifest.FileName)))
            return $"The game hasn't been imported. Run: hbht import <rom>\n(looked in {directory})";
        var import = Plugin.Open(directory).Manifest.Import;
        return import?.Importer == ImporterVersion
            ? null
            : $"The import is out of date (imported by importer {import?.Importer.ToString() ?? "unknown"}, now {ImporterVersion}). Run: hbht import <rom>";
    }

    /// <summary>Throws an <see cref="IOException"/> with <see cref="Diagnose"/>'s words if the base plugin isn't there and current.</summary>
    public static void Require(string pluginsDirectory)
    {
        if (Diagnose(pluginsDirectory) is { } problem)
            throw new IOException(problem);
    }
}

/// <summary>Where the importer writes and the runtime reads, outside the repository.</summary>
public static class DataPaths
{
    /// <summary><c>HBHT_DATA</c> if set, else <c>HBHT</c> in the local application data folder.</summary>
    public static string Root => Environment.GetEnvironmentVariable("HBHT_DATA") is { Length: > 0 } root
        ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HBHT");

    public static string Plugins => Path.Combine(Root, "plugins");

    public static string Base => Path.Combine(Plugins, BasePlugin.Folder);

    /// <summary>Where saved games go (<see cref="Saves.SaveGame"/>).</summary>
    public static string Saves => Path.Combine(Root, "saves");

    /// <summary>The player's settings: volumes, graphics and fidelity (<c>HBHT.Core.Settings</c>).</summary>
    public static string Settings => Path.Combine(Root, "settings.json");
}
