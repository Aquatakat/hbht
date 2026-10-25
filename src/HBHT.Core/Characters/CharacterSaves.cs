using HBHT.Core.Flags;
using HBHT.Core.Items;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Characters;

/// <summary>
/// The party and its sheets as a save holds them, and back (<c>SaveParty</c>): saving and loading side by
/// side, so a field one adds the other reads. Loading rebuilds from the installed records with the rules,
/// as the original's <c>QuestData::Load</c> does, and says in <paramref name="problems"/> what no plugin has any more.
/// </summary>
public sealed class CharacterSaves(PluginSet plugins, CharacterRules rules, ItemCatalog items, FlagNetwork flags, ICollection<string> problems)
{
    /// <summary>The party as a save holds it: its members' sheets, those kept for members who left, its gold and its inventory.</summary>
    public static SaveParty Save(Party party) => new()
    {
        Gold = party.Gold,
        Members = [.. party.Members.Select(Save)],
        Kept = [.. party.Kept.Values],
        Inventory = [.. party.Inventory.Stacks.Select(s =>
            new SaveStack(s.Item.Id, s.Quantity) { Battery = s.Instance.Battery, Charges = s.Instance.Charges })],
    };

    /// <summary>
    /// Fills the party with the one a save holds (<c>QuestData::Load</c>'s party): the members in slot order,
    /// the first being Alaron, the sheets kept for those who left, the gold and the inventory.
    /// </summary>
    public void Load(Party party, SaveParty saved)
    {
        party.Gold = saved.Gold;
        for (var slot = 0; slot < saved.Members.Count; slot++)
        {
            if (Load(saved.Members[slot], slot == 0) is { } member && !party.Add(member))
                problems.Add($"{member.Name} doesn't fit in the party.");
        }
        foreach (var kept in saved.Kept)
        {
            if (plugins.TryGet<EntityRecord>(kept.Entity) is null)
                problems.Add($"{kept.Entity} was kept for rejoining, but isn't a character any plugin has.");
            else if (party.Contains(kept.Entity))
                problems.Add($"{kept.Entity} was kept for rejoining, but is in the party.");
            else
                party.Keep(kept);
        }
        foreach (var stack in saved.Inventory)
        {
            if (Instance(stack.Item, stack.Battery, stack.Charges) is not { } held)
                continue;
            if (!party.Inventory.Add(held.Item, stack.Quantity, held))
                problems.Add($"{held.Item.Name} doesn't fit in the inventory.");
        }
    }

    /// <summary>A character sheet as a save holds it (<c>SaveParty::SaveCharSheet</c>).</summary>
    public static SaveCharacter Save(Character member)
    {
        var stats = Enumerable.Range(0, CharacterRules.StatCount).Select(s => (Stat)s).ToList();
        var (skills, weaponSkills, shieldSkill) = SkillSlots.Split(
            [.. SkillId.All.Select(i => member.Skills.RawBase(i) is var level and >= 0 ? level : (int?)null)]);
        var worn = member.Equipment;
        return new SaveCharacter(member.Entity)
        {
            Name = member.Name != member.RecordName ? member.Name : null,
            TrueName = member.TrueName,
            TotalExp = member.TotalExp,
            SpendableExp = member.SpendableExp,
            Trained = SaveStats.From([.. stats.Select(member.Stats.Trained)]),
            Current = SaveStats.From([.. stats.Select(member.Stats.Current)]),
            Skills = skills,
            WeaponSkills = weaponSkills,
            ShieldSkill = shieldSkill,
            Weapon = Item(worn.Weapon),
            Armor = Item(worn.Armor),
            Shield = Item(worn.Shield),
            Gear = [.. worn.Gear.OfType<ItemInstance>().Select(g => Item(g)!)],
            Spells = [.. member.Spells.Select(s => new KnownSpell(s.Spell.Id, s.Level))],
            Potions = [.. member.PotionEffects.Select(p => new SavePotion(p.Source, p.Timer) { Power = p.Power })],
        };

        static SaveItem? Item(ItemInstance? item) => item is null ? null : new SaveItem(item.Id) { Battery = item.Battery, Charges = item.Charges };
    }

    /// <summary>
    /// A character sheet from a save (<c>SaveParty::LoadCharSheet</c>): the record's sheet with the
    /// save's True Name, experience, stats and base skills, which leaves the modified values the trained and
    /// current ones, then the armor, shield, weapon and gear put on as the rules allow, which applies
    /// them. A skill the record can't learn stays so. The spellbook is put in the original's order
    /// (<see cref="SpellOrder"/>). Null if no plugin has the record.
    /// </summary>
    /// <param name="alaron">
    /// Whether it's the party's first member, whose save also holds the Elemental, Naming and
    /// Necromancy spells (<c>SaveParty::SaveAlaron</c>).
    /// </param>
    public Character? Load(SaveCharacter saved, bool alaron = false)
    {
        if (plugins.TryGet<EntityRecord>(saved.Entity) is not { } entity)
        {
            problems.Add($"{saved.Entity} isn't a character any plugin has.");
            return null;
        }
        var trained = saved.Trained.ToArray();
        var current = saved.Current.ToArray();
        var stats = new CharacterStats(rules, trained, current, trained, current);

        var recorded = Character.SkillLevels(entity);
        var levels = SkillSlots.ToArray(saved.Skills, saved.WeaponSkills, saved.ShieldSkill);
        // CharSkills::LoadOne: a skill the record can't learn stays unlearnable. The modified level starts at the
        // base, or at 0 for one that can't be learnt, as the checkpoints hold it.
        var skills = new CharacterSkills(rules, [.. levels.Select((level, i) => recorded[i] is null ? null : (int?)(sbyte)(level ?? 0))]);

        var spells = new List<LearnedSpell>();
        foreach (var known in saved.Spells)
        {
            if (plugins.TryGet<SpellRecord>(known.Spell) is { } spell)
                spells.Add(new LearnedSpell(spell, known.Level));
            else
                problems.Add($"{entity.Name} knows {known.Spell}, which no plugin has.");
        }
        var character = Character.Restore(entity, rules, stats, skills, SpellOrder(spells, entity.School, alaron), flags.IsSet);
        if (saved.Name is { Length: > 0 } name)
            character.Name = name;
        // A save from before True Names were saved leaves the record's and the flags'.
        if (saved.TrueName is { } trueName)
            character.TrueName = trueName;
        character.TotalExp = saved.TotalExp;
        character.SpendableExp = saved.SpendableExp;

        var equipment = character.Equipment;
        if (Instance(saved.Armor) is { } armor)
            equipment.EquipArmor(armor);
        Wear(saved.Shield, equipment.EquipShield);
        Wear(saved.Weapon, equipment.EquipWeapon);
        foreach (var gear in saved.Gear)
            Wear(gear, equipment.EquipGear);
        // SaveParty::LoadCharSheetEffects, after the sheet and what it wears.
        foreach (var potion in saved.Potions)
        {
            if (plugins.TryGet<ItemRecord>(potion.Potion) is { Kind: ItemKind.Potion })
                character.ApplyPotionEffect(potion.Potion, potion.Power, potion.Timer);
            else
                problems.Add($"{entity.Name} has {potion.Potion} working, which isn't a potion.");
        }
        return character;

        void Wear(SaveItem? item, Func<ItemInstance, EquipRefusal> equip)
        {
            if (Instance(item) is { } instance && equip(instance) is var refusal and not EquipRefusal.None)
                problems.Add($"{entity.Name} can't wear the {instance.Item.Name} ({refusal}), so it's lost.");
        }
    }

    /// <summary>
    /// A spellbook in the order loading a save leaves it (<c>SaveParty::LoadSpellsFromSchool</c>,
    /// <c>LoadAlaron</c>): unschooled spells, then the character's school's (Star for Chaos), then for
    /// Alaron the Elemental, Naming and Necromancy spells, each school in the spell table's order. The
    /// original drops a spell of any other school; the port keeps it, last.
    /// </summary>
    public static IReadOnlyList<LearnedSpell> SpellOrder(IEnumerable<LearnedSpell> spells, MagicSchool school, bool alaron)
    {
        List<MagicSchool> schools = [MagicSchool.None, school == MagicSchool.Chaos ? MagicSchool.Star : school];
        if (alaron)
            schools.AddRange([MagicSchool.Elemental, MagicSchool.Naming, MagicSchool.Necromancy]);
        return [.. spells.OrderBy(s => schools.IndexOf(s.Spell.School) is var rank and >= 0 ? rank : schools.Count)
            .ThenBy(s => s.Spell.Position ?? int.MaxValue)];
    }

    private ItemInstance? Instance(SaveItem? item) => item is null ? null : Instance(item.Item, item.Battery, item.Charges);

    // SaveParty::LoadItem: a new instance of the item, with the save's charges and battery. The price is the item's.
    private ItemInstance? Instance(RecordId id, int? battery, int? charges)
    {
        if (items.TryGet(id) is not { } item)
        {
            problems.Add($"{id} isn't an item any plugin has.");
            return null;
        }
        var instance = new ItemInstance(item);
        if (instance.Battery is not null && battery is { } left)
            instance.Battery = (sbyte)left;
        if (instance.Charges is not null && charges is { } count)
            instance.Charges = (byte)count;
        return instance;
    }
}
