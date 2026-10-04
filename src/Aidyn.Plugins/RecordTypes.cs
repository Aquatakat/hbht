namespace Aidyn.Plugins;

/// <summary>
/// The fixed registry of record types (ADR 0004). Borg assets are named by what they are, and one
/// archive index space is shared by all of them, so <c>aidyn:scene/0x0A0E</c> is Borg 0x0A0E.
/// </summary>
public static class RecordTypes
{
    // DB records, keyed by ItemID.
    public const string Entity = "entity";
    public const string Weapon = "weapon";
    public const string Armor = "armor";
    public const string Spell = "spell";
    public const string Item = "item";
    /// <summary>A <c>DialogEntity</c>: a sign, animal or other simple speaker.</summary>
    public const string DialogEntity = "dialogentity";
    /// <summary>A loot table (<c>ChestDB</c>), which containers and monster parties roll.</summary>
    public const string Loot = "loot";

    // Event flags, keyed by their decimal index.
    public const string Flag = "flag";

    // Borg assets, keyed by archive index.
    public const string Texture = "texture";
    public const string Geometry = "geometry";
    public const string Camera = "camera";
    public const string Light = "light";
    public const string Scene = "scene";
    public const string Animation = "animation";
    public const string Actor = "actor";
    public const string Image = "image";
    /// <summary>A Borg9: one chunk's collision and map objects.</summary>
    public const string Map = "map";
    public const string Sample = "sample";
    public const string Sequence = "sequence";
    public const string Dialogue = "dialogue";

    /// <summary>A map object, nested in its <see cref="Map"/>: <c>aidyn:voxel/0x1A20/12</c>.</summary>
    public const string Voxel = "voxel";

    /// <summary>A set of chunks loaded as one world, keyed by the original's set number.</summary>
    public const string MapSet = "mapset";

    /// <summary>A terrain type, which chunks carry, keyed by its decimal number.</summary>
    public const string Terrain = "terrain";

    /// <summary>A journal entry, keyed by its decimal position in the journal.</summary>
    public const string Journal = "journal";

    /// <summary>Which flags the engine writes its own state into, such as the time of day. There's one, <c>aidyn:stateflags/0</c>.</summary>
    public const string StateFlags = "stateflags";

    /// <summary>The World tab's map: its pieces, towns and the party's ring. There's one, <c>aidyn:worldmap/0</c>.</summary>
    public const string WorldMap = "worldmap";

    /// <summary>How camping goes: the ambush, the heal and the camp scenes. There's one, <c>aidyn:camping/0</c>.</summary>
    public const string Camping = "camping";

    /// <summary>What the outdoor sky draws: its sun, moon, stars, clouds and lens flare. There's one, <c>aidyn:sky/0</c>.</summary>
    public const string Sky = "sky";

    /// <summary>A font: its sheet, its glyphs' widths and which character draws which, keyed by its decimal number.</summary>
    public const string Font = "font";

    /// <summary>A string table, keyed by its decimal position in the ROM.</summary>
    public const string Strings = "strings";

    /// <summary>An attribute or the level, keyed by the original's decimal stat number.</summary>
    public const string Stat = "stat";

    /// <summary>A skill, keyed by the original's decimal skill number.</summary>
    public const string Skill = "skill";

    /// <summary>A weapon skill, keyed by the original's decimal weapon class; 11 is the shield.</summary>
    public const string WeaponSkill = "weaponskill";

    /// <summary>How characters level and train. There's one, <c>aidyn:progression/0</c>.</summary>
    public const string Progression = "progression";

    /// <summary>A shop, keyed by its decimal position in the original's shop DB.</summary>
    public const string Shop = "shop";

    /// <summary>A potion or piece of armor a skill makes, keyed by a decimal number: potions first.</summary>
    public const string Recipe = "recipe";

    /// <summary>How the party carries, buys and finds items. There's one, <c>aidyn:inventory/0</c>.</summary>
    public const string Inventory = "inventory";

    private static readonly HashSet<string> All =
    [
        Entity, Weapon, Armor, Spell, Item, DialogEntity, Loot, Flag, Texture, Geometry, Camera, Light, Scene, Animation, Actor, Image, Map,
        Sample, Sequence, Dialogue, Voxel, MapSet, Terrain, Sky, Journal, StateFlags, WorldMap, Camping, Font, Strings, Stat, Skill, WeaponSkill,
        Progression, Shop, Recipe, Inventory,
    ];

    public static bool IsKnown(string type) => All.Contains(type);
}
