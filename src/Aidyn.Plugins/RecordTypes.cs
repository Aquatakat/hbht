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

    /// <summary>A map set's minimap in trek, by the map set's number, such as <c>aidyn:minimap/4</c> for Gwernia castle's.</summary>
    public const string Minimap = "minimap";

    /// <summary>How camping goes: the ambush, the heal and the camp scenes. There's one, <c>aidyn:camping/0</c>.</summary>
    public const string Camping = "camping";

    /// <summary>What combat keeps of content: who fights with the party, who never fights, and the flee rules. There's one, <c>aidyn:combat/0</c>.</summary>
    public const string Combat = "combat";

    /// <summary>Casting's numbers: the chance, the resist roll, a round's ageing and the rest (ADR 0020). There's one, <c>aidyn:magic/0</c>.</summary>
    public const string Magic = "magic";

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

    /// <summary>The sounds engine code plays: the menus', the scripts', the containers' and so on. There's one, <c>aidyn:sounds/0</c>.</summary>
    public const string Sounds = "sounds";

    /// <summary>How the weather changes. There's one, <c>aidyn:weather/0</c>.</summary>
    public const string Weather = "weather";

    /// <summary>A scene's particle emitters, keyed by the scene's archive index, as <c>aidyn:emitters/0x2BCB</c> for Oriana's.</summary>
    public const string Emitters = "emitters";

    /// <summary>One of the four cinematics, keyed by its decimal place in the cinematic table: 0 is the opening.</summary>
    public const string Cinematic = "cinematic";

    /// <summary>The credits' cards. There's one, <c>aidyn:credits/0</c>.</summary>
    public const string Credits = "credits";

    /// <summary>How a new game starts: where the player arrives and who leads. There's one, <c>aidyn:newgame/0</c>.</summary>
    public const string NewGame = "newgame";

    /// <summary>The title screen: the fly-throughs behind its menu. There's one, <c>aidyn:title/0</c>.</summary>
    public const string Title = "title";

    /// <summary>A trigger: on an event, if its conditions hold, its effects (roadmap 2.10), keyed by a decimal number.</summary>
    public const string Trigger = "trigger";

    private static readonly HashSet<string> All =
    [
        Entity, Weapon, Armor, Spell, Item, DialogEntity, Loot, Flag, Texture, Geometry, Camera, Light, Scene, Animation, Actor, Image, Map,
        Sample, Sequence, Dialogue, Voxel, MapSet, Terrain, Sky, Journal, StateFlags, WorldMap, Minimap, Camping, Font, Strings, Stat, Skill, WeaponSkill,
        Progression, Shop, Recipe, Inventory, Sounds, Weather, Emitters, Cinematic, Credits, NewGame, Title, Combat, Magic, Trigger,
    ];

    public static bool IsKnown(string type) => All.Contains(type);

    /// <summary>
    /// The class a type's records read as, which says which of their lists are keyed (<see cref="KeyedListAttribute"/>), or null
    /// for a type with no records of its own, such as an asset's or a map object's.
    /// </summary>
    public static Type? ClassOf(string type) => type switch
    {
        Entity => typeof(Records.EntityRecord),
        Weapon => typeof(Records.WeaponRecord),
        Armor => typeof(Records.ArmorRecord),
        Spell => typeof(Records.SpellRecord),
        Item => typeof(Records.ItemRecord),
        DialogEntity => typeof(Records.DbRecord),
        Loot => typeof(Records.LootRecord),
        Flag => typeof(Records.FlagRecord),
        Actor => typeof(Records.ActorRecord),
        Map => typeof(Records.MapRecord),
        Sequence => typeof(Records.SequenceRecord),
        Dialogue => typeof(Records.DialogueRecord),
        MapSet => typeof(Records.MapSetRecord),
        Terrain => typeof(Records.TerrainRecord),
        Journal => typeof(Records.JournalRecord),
        StateFlags => typeof(Records.StateFlagsRecord),
        WorldMap => typeof(Records.WorldMapRecord),
        Minimap => typeof(Records.MinimapRecord),
        Camping => typeof(Records.CampingRecord),
        Combat => typeof(Records.CombatRecord),
        Magic => typeof(Records.MagicRecord),
        Sky => typeof(Records.SkyRecord),
        Font => typeof(Records.FontRecord),
        Strings => typeof(Records.StringsRecord),
        Stat => typeof(Records.StatRecord),
        Skill or WeaponSkill => typeof(Records.SkillRecord),
        Progression => typeof(Records.ProgressionRecord),
        Shop => typeof(Records.ShopRecord),
        Recipe => typeof(Records.RecipeRecord),
        Inventory => typeof(Records.InventoryRecord),
        Sounds => typeof(Records.SoundsRecord),
        Weather => typeof(Records.WeatherRecord),
        Emitters => typeof(Records.EmittersRecord),
        Cinematic => typeof(Records.CinematicRecord),
        Credits => typeof(Records.CreditsRecord),
        NewGame => typeof(Records.NewGameRecord),
        Title => typeof(Records.TitleRecord),
        Trigger => typeof(Records.TriggerRecord),
        _ => null,
    };
}
