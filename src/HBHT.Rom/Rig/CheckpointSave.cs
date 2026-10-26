using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;
using HBHT.Rom.Import;

namespace HBHT.Rom.Rig;

/// <summary>
/// Turns a checkpoint into a save in the port's format (<see cref="SaveGame"/>), so the runtime and
/// the editor can start from a real party where the user stood. It needs the plugins only to name
/// map objects, whose record IDs come from the chunk's map.
/// </summary>
public static class CheckpointSave
{
    /// <summary>
    /// A checkpoint as a save: its play time, clock and place, the plain flags set (2 to 5000), the
    /// party's sheets as the original saves them (experience, trained and current stats, base skills,
    /// what's worn, the spellbook), its gold and inventory, and what map objects remember. The random
    /// streams are left to start as a new game's, since the original's saves don't keep its seed.
    /// </summary>
    /// <param name="label">The save's label, such as the checkpoint's name.</param>
    /// <param name="problems">Gets what couldn't be converted, such as a memory of a kind the port doesn't know.</param>
    public static SaveGame Convert(AresCheckpoint checkpoint, PluginSet plugins, string? label = null, ICollection<string>? problems = null)
    {
        problems ??= [];
        var place = checkpoint.Place;
        var mapSet = RecordId.Decimal(RecordTypes.MapSet, place.MapSet);
        var chunk = plugins.TryGet<MapSetRecord>(mapSet)?.Chunk(place.Row, place.Column);
        if (chunk is null)
            problems.Add($"{mapSet} has no chunk {place.Row},{place.Column}, where the player is.");

        var flags = checkpoint.Flags;
        return new SaveGame
        {
            Label = label,
            Plugins = [.. plugins.Plugins.Select(p => new SavedPlugin(p.Manifest.Id, p.Manifest.Version))],
            PlayTime = checkpoint.PlayTime,
            Time = checkpoint.Time,
            Place = new SavePlace(mapSet, chunk?.Name ?? new ChunkEntry(place.Row, place.Column, null, null, default, 0).Name,
                place.Position, -place.Facing),
            // The first 5001 flags are plain bits, of which 0 and 1 are constants.
            Flags = [.. Enumerable.Range(2, BitFlags - 2).Where(n => flags[n]).Select(n => DbRecordIds.Flag(n))],
            Party = new SaveParty
            {
                Gold = checkpoint.Gold,
                Members = [.. checkpoint.Party.Select(Member)],
                // The game keeps a copy for kept members in the party too; the port keeps only those who left.
                Kept = [.. checkpoint.Kept.Where(k => checkpoint.Party.All(m => m.Id != k.Id)).Select(Member)],
                Inventory = [.. checkpoint.Inventory.Select(s => new SaveStack(DbRecordIds.Item(s.Item.Id), s.Quantity)
                {
                    Battery = Battery(s.Item),
                    Charges = s.Item.Charges,
                })],
            },
            Memories = [.. checkpoint.Memories.Select(m => Memory(m, plugins, problems)).OfType<SaveMemory>()],
            Weather = Weather(checkpoint.Weather),
        };
    }

    /// <summary>The weather, in the port's names, which are the original's numbers.</summary>
    private static SaveWeather Weather(CheckpointWeather w)
    {
        string[] kinds = ["Clear", "Fog", "Precipitation"], falls = ["None", "Rain", "Snow"];
        static string Name(string[] names, int value) => value < names.Length ? names[value] : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new SaveWeather(Name(kinds, w.Kind), Name(falls, w.Precipitation), w.Intensity, w.Fog, w.Storm, w.Wind, Name(falls, w.Falling),
            w.FallIntensity, w.FogTime);
    }

    /// <summary>The flags that are plain bits, 0 to 5000.</summary>
    private const int BitFlags = 5001;

    private static SaveCharacter Member(CheckpointCharacter sheet)
    {
        // The console's slots: the 12 skills, the 11 weapon skills, then the shield. -1 can't be learnt.
        var (skills, weaponSkills, shieldSkill) = SkillSlots.Split([.. sheet.SkillBase.Select(level => level == -1 ? (int?)null : level)]);
        return new SaveCharacter(RecordId.Hex(RecordTypes.Entity, sheet.Id))
        {
            TrueName = (sheet.Flags & 1) != 0,
            TotalExp = sheet.TotalExp,
            SpendableExp = sheet.SpendableExp,
            Trained = SaveStats.From(sheet.Trained),
            Current = SaveStats.From(sheet.Current),
            Skills = skills,
            WeaponSkills = weaponSkills,
            ShieldSkill = shieldSkill,
            Weapon = Worn(sheet.Weapon),
            Armor = Worn(sheet.Armor),
            Shield = Worn(sheet.Shield),
            Gear = [.. sheet.Gear.OfType<CheckpointItem>().Select(g => Worn(g)!)],
            Spells = [.. sheet.Spells.Select(s => new KnownSpell(RecordId.Hex(RecordTypes.Spell, s.Spell), s.Level))],
        };

        static SaveItem? Worn(CheckpointItem? item) => item is null ? null : new SaveItem(DbRecordIds.Item(item.Id))
        {
            Battery = Battery(item),
            Charges = item.Charges,
        };
    }

    /// <summary>What's left in a battery: the Stamina (stat 5) its <c>StatMod</c> holds.</summary>
    private static int? Battery(CheckpointItem item) => item.StatMod is { Stat: 5, Amount: var left } ? left : null;

    private const int FirstSkillCheck = 0x49, LastSkillCheck = 0x54;

    internal static SaveMemory? Memory(CheckpointMemory memory, PluginSet plugins, ICollection<string> problems)
    {
        var mapSet = RecordId.Decimal(RecordTypes.MapSet, memory.MapSet);
        var where = $"{mapSet} chunk {memory.Row},{memory.Column} object {memory.Index}";
        var chunk = plugins.TryGet<MapSetRecord>(mapSet)?.Chunk(memory.Row, memory.Column);
        if (chunk is null)
        {
            problems.Add($"{where} remembers something, but the map set has no such chunk.");
            return null;
        }
        var mapObject = MapRecord.VoxelId(chunk.Map, memory.Index);
        switch (memory.Type)
        {
            case 9:
                return new SaveMemory(mapObject, "RandomLine", (int)MathF.Round(BitConverter.Int32BitsToSingle((int)memory.Value) * 100), memory.Lifetime);
            case >= FirstSkillCheck and <= LastSkillCheck:
                return new SaveMemory(mapObject, "SkillCheck", (int)memory.Value, memory.Lifetime) { Skill = memory.Type - FirstSkillCheck };
            case 1:
                return new SaveMemory(mapObject, "Opened", (int)memory.Value, memory.Lifetime);
            case 5:
                return new SaveMemory(mapObject, "Beaten", (int)memory.Value, memory.Lifetime);
            case 0:
                return new SaveMemory(mapObject, "PutOut", (int)memory.Value, memory.Lifetime);
            default:
                problems.Add($"{where} remembers type {memory.Type}, which the port doesn't know.");
                return null;
        }
    }

    /// <summary>The label in a checkpoint's file name, after its last " - ", or the whole name.</summary>
    public static string Label(string path)
    {
        var file = Path.GetFileNameWithoutExtension(path);
        var dash = file.LastIndexOf(" - ", StringComparison.Ordinal);
        return dash < 0 ? file : file[(dash + 3)..];
    }
}
