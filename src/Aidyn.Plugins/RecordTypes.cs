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

    private static readonly HashSet<string> All =
    [
        Entity, Weapon, Armor, Spell, Item, Flag, Texture, Geometry, Camera, Light, Scene, Animation, Actor, Image, Map,
        Sample, Sequence, Dialogue, Voxel, MapSet,
    ];

    public static bool IsKnown(string type) => All.Contains(type);
}
