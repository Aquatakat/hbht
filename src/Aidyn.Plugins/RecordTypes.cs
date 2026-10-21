using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>How a base-game record of a type is keyed (ADR 0004).</summary>
public enum KeyKind
{
    /// <summary>The DB record's full u16 <c>ItemID</c>, as <c>0x</c> and four uppercase hex digits: <c>aidyn:entity/0x023F</c>.</summary>
    Db,
    /// <summary>The Borg's archive index, as <c>0x</c> and four uppercase hex digits: <c>aidyn:scene/0x0A0E</c>.</summary>
    Borg,
    /// <summary>A decimal index without leading zeros: <c>aidyn:flag/47</c>; a singleton's is 0.</summary>
    Decimal,
    /// <summary>The parent's Borg key, then the decimal index within it: <c>aidyn:voxel/0x1A20/12</c>. In a mod, a name then the index.</summary>
    Nested,
}

/// <summary>What the registry knows of a record type.</summary>
/// <param name="Name">The type as IDs spell it.</param>
/// <param name="Class">The class its records read as, or null for a type with no records of its own: an asset's, or a map object's.</param>
/// <param name="Key">How its base-game keys are spelled.</param>
/// <param name="Singleton">Whether there's one record, <c>aidyn:&lt;type&gt;/0</c>, which plugins patch rather than add to.</param>
/// <param name="Views">Other classes its records read as: <see cref="DbRecord"/> reads a DB record's name, portrait and body alone.</param>
public sealed record RecordTypeInfo(string Name, Type? Class, KeyKind Key, bool Singleton = false, IReadOnlyList<Type>? Views = null)
{
    /// <summary>Whether its records read as <paramref name="type"/>: the class or a view.</summary>
    public bool Reads(Type type) => type == Class || Views?.Contains(type) == true;

    /// <summary>The one record's ID, for a singleton type.</summary>
    public RecordId SingletonId => Singleton ? RecordId.Decimal(Name, 0) : throw new InvalidOperationException($"There are many {Name} records.");
}

/// <summary>
/// The fixed registry of record types (ADR 0004): each type's name, class, key shape and whether there's one of it
/// (<see cref="Info"/>). Borg assets are named by what they are, and one archive index space is shared by all of them, so
/// <c>aidyn:scene/0x0A0E</c> is Borg 0x0A0E. Scenes, textures, UI images, samples and animations are assets alone, with
/// no record and no class; a map object is nested in its map's record.
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

    private static readonly Type[] DbViews = [typeof(DbRecord)];

    /// <summary>Every type, in the registry's order: the DB records, the flags, the Borgs, then the rest as they were added.</summary>
    public static IReadOnlyList<RecordTypeInfo> Registry { get; } =
    [
        new(Entity, typeof(EntityRecord), KeyKind.Db, Views: DbViews),
        new(Weapon, typeof(WeaponRecord), KeyKind.Db, Views: DbViews),
        new(Armor, typeof(ArmorRecord), KeyKind.Db, Views: DbViews),
        new(Spell, typeof(SpellRecord), KeyKind.Db, Views: DbViews),
        new(Item, typeof(ItemRecord), KeyKind.Db, Views: DbViews),
        new(DialogEntity, typeof(DbRecord), KeyKind.Db),
        new(Loot, typeof(LootRecord), KeyKind.Db, Views: DbViews),
        new(Flag, typeof(FlagRecord), KeyKind.Decimal),
        new(Texture, null, KeyKind.Borg),
        new(Scene, null, KeyKind.Borg),
        new(Animation, null, KeyKind.Borg),
        new(Actor, typeof(ActorRecord), KeyKind.Borg),
        new(Image, null, KeyKind.Borg),
        new(Map, typeof(MapRecord), KeyKind.Borg),
        new(Sample, null, KeyKind.Borg),
        new(Sequence, typeof(SequenceRecord), KeyKind.Borg),
        new(Dialogue, typeof(DialogueRecord), KeyKind.Borg),
        new(Voxel, null, KeyKind.Nested),
        new(MapSet, typeof(MapSetRecord), KeyKind.Decimal),
        new(Terrain, typeof(TerrainRecord), KeyKind.Decimal),
        new(Sky, typeof(SkyRecord), KeyKind.Decimal, Singleton: true),
        new(Journal, typeof(JournalRecord), KeyKind.Decimal),
        new(StateFlags, typeof(StateFlagsRecord), KeyKind.Decimal, Singleton: true),
        new(WorldMap, typeof(WorldMapRecord), KeyKind.Decimal, Singleton: true),
        new(Minimap, typeof(MinimapRecord), KeyKind.Decimal),
        new(Camping, typeof(CampingRecord), KeyKind.Decimal, Singleton: true),
        new(Font, typeof(FontRecord), KeyKind.Decimal),
        new(Strings, typeof(StringsRecord), KeyKind.Decimal),
        new(Stat, typeof(StatRecord), KeyKind.Decimal),
        new(Skill, typeof(SkillRecord), KeyKind.Decimal),
        new(WeaponSkill, typeof(SkillRecord), KeyKind.Decimal),
        new(Progression, typeof(ProgressionRecord), KeyKind.Decimal, Singleton: true),
        new(Shop, typeof(ShopRecord), KeyKind.Decimal),
        new(Recipe, typeof(RecipeRecord), KeyKind.Decimal),
        new(Inventory, typeof(InventoryRecord), KeyKind.Decimal, Singleton: true),
        new(Sounds, typeof(SoundsRecord), KeyKind.Decimal, Singleton: true),
        new(Weather, typeof(WeatherRecord), KeyKind.Decimal, Singleton: true),
        new(Emitters, typeof(EmittersRecord), KeyKind.Borg),
        new(Cinematic, typeof(CinematicRecord), KeyKind.Decimal),
        new(Credits, typeof(CreditsRecord), KeyKind.Decimal, Singleton: true),
        new(NewGame, typeof(NewGameRecord), KeyKind.Decimal, Singleton: true),
        new(Title, typeof(TitleRecord), KeyKind.Decimal, Singleton: true),
        new(Combat, typeof(CombatRecord), KeyKind.Decimal, Singleton: true),
        new(Magic, typeof(MagicRecord), KeyKind.Decimal, Singleton: true),
        new(Trigger, typeof(TriggerRecord), KeyKind.Decimal),
    ];

    private static readonly Dictionary<string, RecordTypeInfo> ByName = Registry.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public static bool IsKnown(string type) => ByName.ContainsKey(type);

    /// <summary>Every record type's name, in the registry's order.</summary>
    public static IEnumerable<string> Names => Registry.Select(t => t.Name);

    /// <summary>What the registry knows of <paramref name="type"/>, or an <see cref="ArgumentException"/> for a name that isn't a type.</summary>
    public static RecordTypeInfo Info(string type) =>
        ByName.GetValueOrDefault(type) ?? throw new ArgumentException($"\"{type}\" isn't a record type.", nameof(type));

    /// <summary><see cref="Info"/>, or null for a name that isn't a type.</summary>
    public static RecordTypeInfo? TryInfo(string type) => ByName.GetValueOrDefault(type);

    /// <summary>
    /// The class a type's records read as, which says which of their lists are keyed (<see cref="KeyedListAttribute"/>), or null
    /// for a type with no records of its own, such as an asset's or a map object's.
    /// </summary>
    public static Type? ClassOf(string type) => Info(type).Class;

    /// <summary>
    /// The one type whose records read as <typeparamref name="T"/> as their class, such as <see cref="RecordTypes.Entity"/> for
    /// <see cref="EntityRecord"/>, or an <see cref="ArgumentException"/> where there's none or more than one
    /// (<see cref="SkillRecord"/> is a skill's and a weapon skill's; <see cref="DbRecord"/> is a dialogue entity's, and a view of the rest).
    /// </summary>
    public static RecordTypeInfo Of<T>() where T : class
    {
        var found = Registry.Where(t => t.Class == typeof(T)).ToList();
        return found.Count == 1
            ? found[0]
            : throw new ArgumentException(found.Count == 0 ? $"No record type reads as {typeof(T).Name}." : $"{typeof(T).Name} is the class of {string.Join(" and ", found.Select(t => t.Name))}: say which.");
    }

    /// <summary>The one singleton type whose record reads as <typeparamref name="T"/>, such as <see cref="RecordTypes.Combat"/> for <see cref="CombatRecord"/>.</summary>
    public static RecordTypeInfo SingletonOf<T>() where T : class =>
        Registry.SingleOrDefault(t => t.Singleton && t.Class == typeof(T))
            ?? throw new ArgumentException($"No singleton record type reads as {typeof(T).Name}.");
}
