using System.Numerics;
using System.Text.Json;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins.Saves;

/// <summary>
/// A saved game, in the port's own format (roadmap 2.8): JSON, like plugin source, with records
/// named by their IDs, so a save can be read, diffed and edited by hand. It holds what the original's
/// save holds (<c>SaveDatStruct</c>, <c>QuestData::Save</c>), and the rules rebuild the rest on loading,
/// as the original's do: see <c>Aidyn.Core.GameState</c>.
/// </summary>
/// <remarks>
/// Not kept yet: spell effects (2.5), the weather, the minimap's fog, the screenshot, and the
/// party's three movement timers, which nothing reads.
/// </remarks>
public sealed record SaveGame
{
    /// <summary>The format's version, raised when it changes in a way older readers can't follow.</summary>
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>A name for it, such as the checkpoint it was made from.</summary>
    public string? Label { get; init; }

    /// <summary>The plugins it was saved with, in load order, so loading can say what's changed.</summary>
    public IReadOnlyList<SavedPlugin> Plugins { get; init; } = [];

    /// <summary>Time played, in 60 Hz ticks (<c>TerrainStruct.PlayTime</c>).</summary>
    public uint PlayTime { get; init; }

    /// <summary>The in-game clock (<c>TerrainStruct.InGameTime</c>), in 60ths of an in-game second.</summary>
    public uint Time { get; init; }

    /// <summary>Where the player is. None starts where a new game does.</summary>
    public SavePlace? Place { get; init; }

    /// <summary>
    /// The plain flags that are set, but for 1, which always is. Derived flags are evaluated from
    /// them on loading.
    /// </summary>
    public IReadOnlyList<RecordId> Flags { get; init; } = [];

    /// <summary>
    /// The journal's entries in the order they were added, which the original doesn't keep. An entry whose flag is
    /// set and isn't listed comes before those listed.
    /// </summary>
    public IReadOnlyList<RecordId> Journal { get; init; } = [];

    public SaveParty Party { get; init; } = new();

    /// <summary>What map objects remember (the original's <c>VoxelChart</c>), in the order it was remembered.</summary>
    public IReadOnlyList<SaveMemory> Memories { get; init; } = [];

    /// <summary>The random streams' states (ADR 0006). None seeds them as a new game does.</summary>
    public SaveRandom? Random { get; init; }
}

public sealed record SavedPlugin(string Id, string Version);

/// <summary>Where the player stands (<c>mapFloatDat</c>).</summary>
/// <param name="MapSet">The map set (<c>mapDatA</c>).</param>
/// <param name="Chunk">The chunk by name, such as <c>I08</c> (<c>MapShort1</c> and <c>MapShort2</c>).</param>
/// <param name="Position">The centre of the player's sphere, in the chunk's coordinates (<c>playerVec3</c>).</param>
/// <param name="Heading">
/// The way the player faces, in X and Z. The original keeps the opposite (<c>playerVec2</c>, <c>facing</c>).
/// </param>
public sealed record SavePlace(RecordId MapSet, string Chunk, Vector3 Position, Vector2 Heading);

public sealed record SaveParty
{
    public uint Gold { get; init; }

    /// <summary>The members in slot order, Alaron first.</summary>
    public IReadOnlyList<SaveCharacter> Members { get; init; } = [];

    /// <summary>
    /// The sheets kept for members who left, which they rejoin with (<c>SaveEntity</c>'s slots 4 to 8).
    /// The original keeps a copy for those in the party too, which the port has no need of.
    /// </summary>
    public IReadOnlyList<SaveCharacter> Kept { get; init; } = [];

    /// <summary>The inventory's stacks, in the order they were gained.</summary>
    public IReadOnlyList<SaveStack> Inventory { get; init; } = [];
}

/// <summary>
/// A character sheet as the original saves one (<c>SaveParty::SaveCharSheet</c>): experience, the
/// trained and current stats, base skill levels, what's worn and the spells. Loading starts from
/// the entity's record and puts what's worn on, which gives the modified values. The aspect, school,
/// True Name, protection and damage come from the record and the flags.
/// </summary>
public sealed record SaveCharacter(RecordId Entity)
{
    /// <summary>
    /// A name the player gave the character in place of the record's: the original lets Alaron be renamed, and
    /// saves his name (<c>SaveParty::SaveAlaron</c>). None keeps the record's.
    /// </summary>
    public string? Name { get; init; }

    public uint TotalExp { get; init; }

    public uint SpendableExp { get; init; }

    /// <summary>The stats as trained, the level among them (<c>CharStats_s.Base</c>).</summary>
    public required SaveStats Trained { get; init; }

    /// <summary>Trained less damage (<c>CharStats_s.health</c>).</summary>
    public required SaveStats Current { get; init; }

    /// <summary>Base skill levels. One the entity can never learn stays so, whatever this says.</summary>
    public SkillLevels Skills { get; init; } = new();

    public WeaponSkillLevels WeaponSkills { get; init; } = new();

    public int? ShieldSkill { get; init; }

    public SaveItem? Weapon { get; init; }

    public SaveItem? Armor { get; init; }

    public SaveItem? Shield { get; init; }

    /// <summary>The gear worn. Loading puts each on in the first free slot, as the original does.</summary>
    public IReadOnlyList<SaveItem> Gear { get; init; } = [];

    /// <summary>The spellbook. Loading puts it in the original's order: see <c>GameState</c>.</summary>
    public IReadOnlyList<KnownSpell> Spells { get; init; } = [];

    /// <summary>The potions working on the character, which loading applies again (<c>SaveParty::SavePotionEffect</c>).</summary>
    public IReadOnlyList<SavePotion> Potions { get; init; } = [];
}

/// <summary>A potion working on a character.</summary>
/// <param name="Potion">The potion's item record.</param>
/// <param name="Timer">What's left, in the game clock's ticks.</param>
public sealed record SavePotion(RecordId Potion, uint Timer)
{
    /// <summary>A byte the original keeps, which nothing reads.</summary>
    public int Power { get; init; }
}

/// <summary>Intelligence to Level (stats 0–6), as bytes.</summary>
public sealed record SaveStats(int Intelligence, int Willpower, int Dexterity, int Endurance, int Strength, int Stamina, int Level)
{
    public static SaveStats From(IReadOnlyList<int> values) =>
        new(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);

    public int[] ToArray() => [Intelligence, Willpower, Dexterity, Endurance, Strength, Stamina, Level];
}

/// <summary>An item as one is held: what's left in its battery and its spell's charges, if it has them.</summary>
public sealed record SaveItem(RecordId Item)
{
    public int? Battery { get; init; }

    public int? Charges { get; init; }
}

/// <summary>A stack in the inventory, which shares one battery and one spell's charges.</summary>
public sealed record SaveStack(RecordId Item, int Quantity)
{
    public int? Battery { get; init; }

    public int? Charges { get; init; }
}

/// <summary>A memory a map object holds (<c>VoxelChartEntry</c>).</summary>
/// <param name="Kind">
/// <c>RandomLine</c>, <c>SkillCheck</c>, <c>Opened</c> or <c>Beaten</c>, as <c>Aidyn.Core.MapObjects.MemoryKind</c> names them.
/// </param>
/// <param name="Skill">For a skill check, the skill (0–11).</param>
/// <param name="Value">A random line's roll (0–100) or a skill check's step.</param>
/// <param name="Lifetime">Map loads it lasts.</param>
public sealed record SaveMemory(RecordId MapObject, string Kind, int Value, int Lifetime)
{
    public int? Skill { get; init; }
}

/// <summary>The states of the random streams (ADR 0006).</summary>
public sealed record SaveRandom(uint Combat, uint Loot, uint World, uint Wander, uint Cosmetic);

/// <summary>Reads and writes saves.</summary>
public static class SaveFile
{
    public const string Extension = ".json";

    public static SaveGame Read(string path) => Parse(File.ReadAllText(path));

    public static SaveGame Parse(string json)
    {
        var save = JsonSerializer.Deserialize<SaveGame>(json, PluginJson.Options) ?? throw new JsonException("The save is empty.");
        if (save.Format > SaveGame.CurrentFormat)
            throw new InvalidDataException($"The save's format is {save.Format}, newer than this version reads ({SaveGame.CurrentFormat}).");
        return save;
    }

    public static string Serialize(SaveGame save) => JsonSerializer.Serialize(save, PluginJson.Options);

    /// <summary>Writes a save, making its folder if need be.</summary>
    public static void Write(string path, SaveGame save)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, Serialize(save) + "\n");
    }
}
