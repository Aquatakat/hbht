using System.Globalization;
using System.Numerics;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;
using HBHT.Rom.Engine;
using HBHT.Rom.Import;

namespace HBHT.Rom.Rig;

/// <summary>
/// Turns the original's saves (<see cref="PakSave"/>) into the port's (<see cref="SaveGame"/>) and back, and reads and writes
/// them in a Controller Pak, so a save made in the port or by hand can be loaded in the original at any place. It needs the
/// plugins, for the entities' schools and the spell table's order, and the ROM, for the inventory's reserved and key gear
/// slots (<see cref="ItemTables"/>).
/// </summary>
public sealed class PakSaves
{
    /// <summary>The notes' game code, company and name; each slot's extension is its letter, A to P.</summary>
    public const string Game = "NAYE", Company = "78", NoteName = "CHRONICLES";

    /// <summary>The slots, as the save menu takes the first free of them.</summary>
    public const string Slots = "ABCDEFGHIJKLMNOP";

    /// <summary>The camera's eye a save notes, out along the original's facing and up from the player.</summary>
    private const float CameraBack = 5f, CameraUp = 2.3f;

    /// <summary>Opaque black, for the load menu's picture of a save made here.</summary>
    private const ushort Black = 0x0001;

    private readonly PluginSet _plugins;
    private readonly List<ushort> _reserved, _keyGear;
    private readonly Dictionary<MagicSchool, IReadOnlyList<RecordId>> _spellGroups;

    public PakSaves(PluginSet plugins, RomImage rom)
    {
        _plugins = plugins;
        var tables = ItemTables.Read(rom);
        _reserved = [.. tables.ReservedItems.Select(i => i.Value)];
        _keyGear = [.. tables.KeyEquipment.Select(i => i.Value)];
        // The spell table groups the spells by school, in the order of their places (SaveFile's WriteSchoolSpells).
        _spellGroups = plugins.All<SpellRecord>(RecordTypes.Spell).Where(s => s.Position is not null)
            .GroupBy(s => s.School)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RecordId>)[.. g.OrderBy(s => s.Position).Select(s => s.Id)]);
    }

    /// <summary>An entity's school; one no plugin has is taken as the commonest, None.</summary>
    public MagicSchool SchoolOf(int entity) =>
        _plugins.TryGet<EntityRecord>(RecordId.Hex(RecordTypes.Entity, entity))?.School ?? MagicSchool.None;

    public PakSave Decode(ReadOnlySpan<byte> note) => PakSave.Decode(note, SchoolOf);

    public byte[] Encode(PakSave save) => save.Encode(SchoolOf);

    /// <summary>The pak's saves by slot, as the load menu lists them: the game's notes, whatever their name.</summary>
    public IReadOnlyList<(char Slot, PakSave Save)> Read(ControllerPak pak) =>
    [
        .. pak.Notes.Where(n => n.Game == Game && n.Company == Company)
            .Select(n => (FontText.Decode(n.Extension) is [var slot] ? slot : '?', Decode(pak.ReadNote(n)))),
    ];

    /// <summary>Writes a save into a slot, replacing the save there.</summary>
    public void Write(ControllerPak pak, char slot, PakSave save)
    {
        if (!Slots.Contains(slot, StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"A slot is one of {Slots}.");
        pak.WriteNote(Game, Company, FontText.Encode(NoteName, 16), FontText.Encode(slot.ToString(), 4), Encode(save));
    }

    /// <summary>The original's save as the port's, as <see cref="CheckpointSave"/> turns a checkpoint into one.</summary>
    /// <param name="problems">Gets what couldn't be converted.</param>
    public SaveGame ToSave(PakSave pak, string? label = null, ICollection<string>? problems = null)
    {
        problems ??= [];
        var place = pak.Place;
        var mapSet = RecordId.Decimal(RecordTypes.MapSet, place.MapSet);
        var chunk = _plugins.TryGet<MapSetRecord>(mapSet)?.Chunk(place.Row, place.Column);
        if (chunk is null)
            problems.Add($"{mapSet} has no chunk {place.Row},{place.Column}, where the player is.");

        var members = pak.Party.OfType<PakSheet>().ToList();
        var inventory = new List<SaveStack>();
        foreach (var stack in pak.Inventory.Where(s => s.Quantity > 0 && s.Item is not null))
            inventory.Add(Stack(stack.Item!, stack.Quantity));
        for (var i = 0; i < _reserved.Count; i++)
        {
            // A reserved slot keeps only the quantity, and loading gives the item its record's charges and battery.
            if (pak.Reserved[i] > 0)
                inventory.Add(Stack(new PakItem(_reserved[i], Bonuses(DbRecordIds.Item(_reserved[i]))?.Spell?.Charges ?? 0, 0), pak.Reserved[i]));
        }
        // Key items aren't saved: loading gives each whose flag is set (LoadKeyItems).
        inventory.AddRange(KeyItems().Where(k => pak.Flags.Contains(k.Flag)).Select(k => new SaveStack(k.Item, 1)));
        inventory.AddRange(pak.KeyGear.OfType<PakItem>().Select(g => Stack(g, 1)));

        return new SaveGame
        {
            Label = label,
            Plugins = [.. _plugins.Plugins.Select(p => new SavedPlugin(p.Manifest.Id, p.Manifest.Version))],
            PlayTime = pak.PlayTime,
            Time = pak.Clock,
            Place = new SavePlace(mapSet, chunk?.Name ?? new ChunkEntry(place.Row, place.Column, null, null, default, 0).Name,
                place.Position, -place.Facing),
            Flags = [.. pak.Flags.Where(f => f >= 2).Order().Select(DbRecordIds.Flag)],
            Party = new SaveParty
            {
                Gold = pak.Gold,
                Members = [.. members.Select((m, i) => Character(m, i == 0 ? pak : null, problems))],
                Kept = [.. pak.Kept.OfType<PakSheet>().Where(k => members.All(m => m.Entity != k.Entity)).Select(k => Character(k, null, problems))],
                Inventory = inventory,
            },
            Memories = [.. pak.Chart.Used.Select(m => CheckpointSave.Memory(m, _plugins, problems)).OfType<SaveMemory>()],
            Minimaps = SavedMinimaps(pak.Minimaps),
        };
    }

    /// <summary>
    /// The port's save as the original's. What the port's save doesn't hold takes what a fresh save would: a black picture, the
    /// volumes' defaults, the movement timers at 0, and the camera behind the player. Its potions aren't written, as the original
    /// never writes any, and the original's sheets have no room for a spell of a school that isn't the character's.
    /// </summary>
    /// <param name="problems">Gets what couldn't be written.</param>
    public PakSave FromSave(SaveGame save, ICollection<string>? problems = null)
    {
        problems ??= [];
        var place = save.Place ?? NewGamePlace(problems);
        var members = save.Party.Members;
        if (members.Count is 0 or > PakSave.PartySheets || Number(members[0].Entity) != Alaron)
            throw new InvalidDataException("The original's save has Alaron first, and at most four in the party.");

        var mapSet = _plugins.TryGet<MapSetRecord>(place.MapSet) ?? throw new InvalidDataException($"No plugin has {place.MapSet}.");
        var chunk = mapSet.Chunks.FirstOrDefault(c => c.Name == place.Chunk)
            ?? throw new InvalidDataException($"{place.MapSet} has no chunk {place.Chunk}.");
        var facing = -place.Heading;

        var flags = new HashSet<int> { 1 };
        foreach (var flag in save.Flags)
        {
            if (Number(flag) is var n and < PakSave.FlagBits)
                flags.Add(n);
            else
                problems.Add($"{flag} isn't a plain flag, which is all the original saves.");
        }

        var alaron = members[0];
        var name = alaron.Name ?? _plugins.TryGet<EntityRecord>(alaron.Entity)?.Name ?? "Alaron";
        var party = new PakSheet?[PakSave.PartySheets];
        for (var i = 0; i < members.Count; i++)
            party[i] = Sheet(members[i], true, problems);
        var kept = PakSave.KeptEntities.Select(e =>
            members.Concat(save.Party.Kept).FirstOrDefault(c => Number(c.Entity) == e) is { } c ? Sheet(c, false, problems) : null).ToArray();

        var general = new List<PakStack>();
        var reserved = new int[PakSave.ReservedSlots];
        var keyGear = new PakItem?[PakSave.KeyGearSlots];
        var keyItems = KeyItems().ToDictionary(k => k.Item, k => k.Flag);
        foreach (var stack in save.Party.Inventory)
        {
            var id = Number(stack.Item);
            if (keyItems.TryGetValue(stack.Item, out var held))
                flags.Add(held);
            else if (_reserved.IndexOf((ushort)id) is var r and >= 0)
                reserved[r] = Math.Min(reserved[r] + stack.Quantity, 255);
            else if (_keyGear.IndexOf((ushort)id) is var g and >= 0)
                keyGear[g] = Item(new SaveItem(stack.Item) { Battery = stack.Battery, Charges = stack.Charges });
            else if (general.Count == PakSave.GeneralStacks)
                problems.Add($"{stack.Item} doesn't fit: the inventory has {PakSave.GeneralStacks} general slots.");
            else
            {
                if (stack.Quantity > 255)
                    problems.Add($"{stack.Item}'s stack of {stack.Quantity} is saved as 255.");
                general.Add(new PakStack(Math.Min(stack.Quantity, 255), Item(new SaveItem(stack.Item) { Battery = stack.Battery, Charges = stack.Charges })));
            }
        }
        while (general.Count < PakSave.GeneralStacks)
            general.Add(new PakStack(0, null));

        var thumbnail = new ushort[PakSave.ThumbnailWidth * PakSave.ThumbnailHeight];
        Array.Fill(thumbnail, Black);
        return new PakSave
        {
            PlayTime = save.PlayTime,
            Portraits = [.. Enumerable.Range(0, 4).Select(i => i < members.Count ? (ushort)Number(members[i].Entity) : (ushort)0)],
            Name = name,
            Place = new PakPlace(place.Position, facing, place.Position + new Vector3(facing.X, 0, facing.Y) * CameraBack + new Vector3(0, CameraUp, 0),
                chunk.Row, chunk.Column, Number(place.MapSet)),
            Flags = flags,
            Thumbnail = thumbnail,
            Minimaps = MinimapBits(save.Minimaps, problems),
            Clock = save.Time,
            Gold = save.Party.Gold,
            Party = party,
            AlaronName = name,
            AlaronSpells = [.. new[] { MagicSchool.Elemental, MagicSchool.Naming, MagicSchool.Necromancy }.SelectMany(s => Levels(alaron, s, Group(s).Count))],
            Kept = kept,
            Inventory = general,
            Reserved = reserved,
            KeyGear = keyGear,
            Weather = save.Weather is { } w
                ? new PakWeather((int)(w.Intensity * 1000), (int)(w.Fog * 1000), (int)(w.Storm * 1000), Array.IndexOf(Falls, w.Precipitation) is var p and >= 0 ? p : 0)
                : new PakWeather(0, 0, 0, 0),
            Chart = PakChart.Of([.. save.Memories.Select(m => Memory(m, problems)).OfType<CheckpointMemory>()]),
        };
    }

    private const int Alaron = 0x023F;

    /// <summary>
    /// Where a new game arrives (<see cref="NewGameRecord"/>), for a save without a place: the original's saves always have one,
    /// and loading puts the player there in trek, skipping the opening cinematic: trek there starts the opening's script, whose dialogue
    /// object holds the arrival (docs/systems/events.md).
    /// </summary>
    private SavePlace NewGamePlace(ICollection<string> problems)
    {
        var start = _plugins.TrySingleton<NewGameRecord>()
            ?? throw new InvalidDataException("The save has no place, and the plugins no new game record to take one from.");
        var chunk = _plugins.Get<MapSetRecord>(start.Chunk.MapSet).Chunk(start.Chunk.Row, start.Chunk.Column)
            ?? throw new InvalidDataException($"{start.Chunk.MapSet} has no chunk {start.Chunk.Row},{start.Chunk.Column}, where a new game arrives.");
        problems.Add($"The save has no place, so it's written where a new game arrives, {chunk.Name}: loading it skips the opening cinematic, and trek there plays the opening's script.");
        return new SavePlace(start.Chunk.MapSet, chunk.Name, new Vector3(start.Position.X, start.Height, start.Position.Y), new Vector2(0, 1));
    }

    private static readonly string[] Falls = ["None", "Rain", "Snow"];

    /// <summary>The number a record's key gives: hex for DB and archive keys, decimal for the rest.</summary>
    private static int Number(RecordId id)
    {
        var key = id.Key.Split('/')[0];
        return key.StartsWith("0x", StringComparison.Ordinal)
            ? int.Parse(key.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(key, CultureInfo.InvariantCulture);
    }

    /// <summary>The key items, each with the flag set while the party has it.</summary>
    private IEnumerable<(RecordId Item, int Flag)> KeyItems() =>
        _plugins.All<ItemRecord>(RecordTypes.Item).Where(i => i.KeyItem is not null).Select(i => (i.Id, Number(i.KeyItem!.Held)));

    private IReadOnlyList<RecordId> Group(MagicSchool school) => _spellGroups.GetValueOrDefault(school) ?? [];

    /// <summary>A sheet's school block's group: Chaos keeps the Star spells there.</summary>
    private MagicSchool BlockSchool(int entity) => SchoolOf(entity) switch
    {
        MagicSchool.Chaos => MagicSchool.Star,
        var s => s,
    };

    /// <summary>A gear record's bonuses, whichever kind of item it is.</summary>
    private GearBonuses? Bonuses(RecordId item) => item.Type switch
    {
        RecordTypes.Weapon => _plugins.TryGet<WeaponRecord>(item)?.Bonuses,
        RecordTypes.Armor => _plugins.TryGet<ArmorRecord>(item)?.Bonuses,
        _ => _plugins.TryGet<ItemRecord>(item)?.Bonuses,
    };

    private const int StaminaStatNumber = 5;

    private static readonly RecordId StaminaStat = RecordId.Decimal(RecordTypes.Stat, StaminaStatNumber);

    /// <summary>
    /// An item as the save holds one: its spell's charges, and the stat mod of an item with one, a battery's with the Stamina
    /// left in it (<c>WriteItem</c> writes the instance's <c>StatMod</c>).
    /// </summary>
    private PakItem Item(SaveItem item)
    {
        var bonuses = Bonuses(item.Item);
        var charges = item.Charges ?? bonuses?.Spell?.Charges ?? 0;
        var statMod = bonuses?.Stat is { Stat: var stat, Amount: var amount }
            ? PakItem.MakeStatMod(Number(stat), stat == StaminaStat ? item.Battery ?? amount : amount)
            : 0;
        return new PakItem(Number(item.Item), Math.Clamp(charges, 0, 31), statMod);
    }

    private SaveItem Worn(PakItem item)
    {
        var id = DbRecordIds.Item((ushort)item.Id);
        var bonuses = Bonuses(id);
        return new SaveItem(id)
        {
            Battery = item.Stat is (StaminaStatNumber, var left) ? left
                : bonuses?.Stat is { Stat: var stat, Amount: var amount } && stat == StaminaStat ? amount : null,
            Charges = bonuses?.Spell is null ? null : item.Charges,
        };
    }

    private SaveStack Stack(PakItem item, int quantity)
    {
        var worn = Worn(item);
        return new SaveStack(worn.Item, quantity) { Battery = worn.Battery, Charges = worn.Charges };
    }

    private PakSheet Sheet(SaveCharacter c, bool party, ICollection<string> problems)
    {
        var entity = Number(c.Entity);
        var levels = SkillSlots.ToArray(c.Skills, c.WeaponSkills, c.ShieldSkill);
        var gear = c.Gear.Select(Item).Cast<PakItem?>().ToList();
        if (gear.Count > 12)
            problems.Add($"{c.Entity} wears {gear.Count} gear; the save keeps 12.");
        while (gear.Count < 12)
            gear.Add(null);

        var block = BlockSchool(entity);
        var kept = new HashSet<RecordId>(Group(MagicSchool.None).Concat(Group(block)));
        if (entity == Alaron)
            kept.UnionWith(new[] { MagicSchool.Elemental, MagicSchool.Naming, MagicSchool.Necromancy }.SelectMany(Group));
        foreach (var spell in c.Spells.Where(s => !kept.Contains(s.Spell)))
            problems.Add($"{c.Entity}'s {spell.Spell} isn't saved: the original keeps only the spells of the character's school and unschooled ones.");
        if (party && c.Potions.Count > 0)
            problems.Add($"{c.Entity}'s potions aren't saved, as the original never saves any.");
        foreach (var (stats, which) in new[] { (c.Trained, "trained"), (c.Current, "current") })
        {
            var values = stats.ToArray();
            foreach (var (stat, bits) in PakSheet.StatOrder.Where(s => values[s.Stat] >> s.Bits != 0))
                problems.Add($"{c.Entity}'s {which} {StatNames[stat]} of {values[stat]} is saved as {values[stat] & ((1 << bits) - 1)}, in the original's {bits} bits.");
        }

        return new PakSheet(entity, c.TotalExp, c.SpendableExp, c.Trained.ToArray(), c.Current.ToArray(),
            [.. PakSheet.SavedSkills.Select(s => levels[s] ?? 0xF)],
            c.Armor is { } a ? Item(a) : null, c.Shield is { } s ? Item(s) : null, c.Weapon is { } w ? Item(w) : null, gear,
            Levels(c, MagicSchool.None, PakSave.UnschooledSlots), Levels(c, block, PakSave.SchooledSlots),
            party ? new PakEffect?[15] : [], party ? new PakPotion?[7] : []);
    }

    private static readonly string[] StatNames = ["Intelligence", "Willpower", "Dexterity", "Endurance", "Strength", "Stamina", "Level"];

    /// <summary>A character's levels of a school's spells by their place in its group, padded with zeroes to <paramref name="slots"/>.</summary>
    private int[] Levels(SaveCharacter c, MagicSchool school, int slots)
    {
        var group = Group(school);
        var levels = new int[slots];
        for (var i = 0; i < Math.Min(group.Count, slots); i++)
            levels[i] = c.Spells.FirstOrDefault(s => s.Spell == group[i])?.Level ?? 0;
        return levels;
    }

    private SaveCharacter Character(PakSheet sheet, PakSave? alaron, ICollection<string> problems)
    {
        var (skills, weaponSkills, shieldSkill) = SkillSlots.Split([.. Enumerable.Range(0, SkillSlots.Count).Select(slot =>
            Array.IndexOf([.. PakSheet.SavedSkills], slot) is var i and >= 0 && sheet.Skills[i] != 0xF ? sheet.Skills[i] : (int?)null)]);
        var spells = new List<KnownSpell>();
        void Known(MagicSchool school, IReadOnlyList<int> levels)
        {
            var group = Group(school);
            for (var i = 0; i < levels.Count; i++)
            {
                if (levels[i] == 0)
                    continue;
                if (i < group.Count)
                    spells.Add(new KnownSpell(group[i], levels[i]));
                else
                    problems.Add($"0x{sheet.Entity:X4} knows a spell at slot {i} of the {school} spells, which has none.");
            }
        }
        Known(MagicSchool.None, sheet.Unschooled);
        Known(BlockSchool(sheet.Entity), sheet.Schooled);
        if (alaron is not null)
        {
            var at = 0;
            foreach (var school in new[] { MagicSchool.Elemental, MagicSchool.Naming, MagicSchool.Necromancy })
            {
                var count = Group(school).Count;
                Known(school, [.. alaron.AlaronSpells.Skip(at).Take(count)]);
                at += count;
            }
        }
        if (sheet.Effects.Any(e => e is not null) || sheet.Potions.Any(p => p is not null))
            problems.Add($"0x{sheet.Entity:X4}'s spell effects and potions aren't read.");

        var id = RecordId.Hex(RecordTypes.Entity, sheet.Entity);
        var recordName = _plugins.TryGet<EntityRecord>(id)?.Name;
        return new SaveCharacter(id)
        {
            Name = alaron is not null && alaron.AlaronName != recordName ? alaron.AlaronName : null,
            TotalExp = sheet.TotalExp,
            SpendableExp = sheet.SpendableExp,
            Trained = SaveStats.From(sheet.Trained),
            Current = SaveStats.From(sheet.Current),
            Skills = skills,
            WeaponSkills = weaponSkills,
            ShieldSkill = shieldSkill,
            Weapon = sheet.Weapon is { } w ? Worn(w) : null,
            Armor = sheet.Armor is { } a ? Worn(a) : null,
            Shield = sheet.Shield is { } s ? Worn(s) : null,
            Gear = [.. sheet.Gear.OfType<PakItem>().Select(Worn)],
            Spells = spells,
        };
    }

    /// <summary>The minimaps, in the order the save numbers their pieces (<c>CodeTable.MinimapSections</c>).</summary>
    private IEnumerable<MinimapRecord> Minimaps() =>
        MinimapImporter.MapSets.Select(m => _plugins.TryGet<MinimapRecord>(RecordId.Decimal(RecordTypes.Minimap, m))).OfType<MinimapRecord>();

    /// <summary>The pieces seen, by minimap, for those not as they start, as <c>MinimapSeen.Save</c> lists them.</summary>
    private List<SaveMinimap> SavedMinimaps(IReadOnlySet<int> bits)
    {
        var saved = new List<SaveMinimap>();
        var at = 0;
        foreach (var minimap in Minimaps())
        {
            var seen = Enumerable.Range(0, minimap.Sections.Count).Where(i => bits.Contains(at + i)).ToList();
            if (!seen.SequenceEqual(Enumerable.Range(0, minimap.Sections.Count).Where(i => minimap.Sections[i].Seen)))
                saved.Add(new SaveMinimap(minimap.Id, seen));
            at += minimap.Sections.Count;
        }
        return [.. saved.OrderBy(m => m.Minimap.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>Every piece seen: a minimap the save doesn't list as it starts, as loading sets each piece from its bit.</summary>
    private HashSet<int> MinimapBits(IReadOnlyList<SaveMinimap> saved, ICollection<string> problems)
    {
        var bits = new HashSet<int>();
        var at = 0;
        var listed = saved.ToDictionary(m => m.Minimap, m => m.Seen);
        foreach (var minimap in Minimaps())
        {
            var seen = listed.Remove(minimap.Id, out var s) ? s : [.. Enumerable.Range(0, minimap.Sections.Count).Where(i => minimap.Sections[i].Seen)];
            bits.UnionWith(seen.Where(i => i < minimap.Sections.Count).Select(i => at + i));
            at += minimap.Sections.Count;
        }
        foreach (var other in listed.Keys)
            problems.Add($"{other} isn't one of the original's minimaps.");
        return bits;
    }

    private const int FirstSkillCheck = 0x49;

    /// <summary>A memory as the chart holds one, at the first chunk that places its map.</summary>
    private CheckpointMemory? Memory(SaveMemory memory, ICollection<string> problems)
    {
        var map = MapRecord.MapOf(memory.MapObject);
        if (MapPlacements.For(_plugins).First(map) is not { } place)
        {
            problems.Add($"{memory.MapObject} is in a map no map set places, so its memory isn't saved.");
            return null;
        }
        var index = int.Parse(memory.MapObject.Key.Split('/')[^1], CultureInfo.InvariantCulture);
        (int Type, uint Value)? kind = memory.Kind switch
        {
            "RandomLine" => (9, (uint)BitConverter.SingleToInt32Bits(memory.Value / 100f)),
            "SkillCheck" => (FirstSkillCheck + (memory.Skill ?? 0), (uint)memory.Value),
            "Opened" => (1, (uint)memory.Value),
            "Beaten" => (5, (uint)memory.Value),
            "PutOut" => (0, (uint)memory.Value),
            _ => null,
        };
        if (kind is not { } k)
        {
            problems.Add($"{memory.MapObject} remembers {memory.Kind}, which the original doesn't.");
            return null;
        }
        return new CheckpointMemory(index, memory.Lifetime, Number(place.Set.Id), place.Chunk.Row, place.Chunk.Column, k.Type, k.Value);
    }
}
