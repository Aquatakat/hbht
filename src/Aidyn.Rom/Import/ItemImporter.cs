using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Quests;
using Aidyn.Rom.Text;
using PluginArmor = Aidyn.Plugins.Records.ArmorRecord;
using PluginEntity = Aidyn.Plugins.Records.EntityRecord;
using PluginLoot = Aidyn.Plugins.Records.LootRecord;
using PluginLootDrop = Aidyn.Plugins.Records.LootDrop;
using PluginShop = Aidyn.Plugins.Records.ShopRecord;
using PluginWeapon = Aidyn.Plugins.Records.WeaponRecord;
using ShopStock = Aidyn.Plugins.Records.ShopStock;
using Resistance = Aidyn.Plugins.Records.Resistance;
using KeyItemFlags = Aidyn.Plugins.Records.KeyItemFlags;

namespace Aidyn.Rom.Import;

/// <summary>
/// Weapons, armor, items, loot tables, shops, recipes and the inventory's rules (roadmap 2.3), from
/// the DB tables and the tables in code (<see cref="ItemTables"/>).
/// </summary>
internal static class ItemImporter
{
    /// <summary>
    /// The Archmage Staff's position in the weapon DB: <c>Entity::canEquipWeapon</c> reads
    /// <c>weaponList[66]</c> and adds <c>0x700</c>. The weapon list is the weapon DB's order.
    /// </summary>
    internal const int ArchmageStaffPosition = 66;

    /// <summary>The Cyclops Club's position in the weapon DB, <c>weaponList[32]</c> in <c>GenericInventory::AddItem</c>.</summary>
    internal const int CyclopsClubPosition = 32;

    /// <summary>Niesen's position in the entity DB, 162: <c>Entity::canEquipWeapon</c> reads <c>entityList[162]</c>.</summary>
    internal const int NiesenDbPosition = 162;

    /// <summary>
    /// The reagents' positions in the item DB, which <c>itemID_array</c> lists in order: spice, herb and
    /// gemstone (<c>get_chest_loot</c> rolls 30 to 32; <c>Party::CraftPotion</c> reads the same three).
    /// </summary>
    internal const int SpicePosition = 30, HerbPosition = 31, GemstonePosition = 32;

    /// <summary>The Aspect potion, which every inventory turns into a Healing potion (<c>PartyInventory::AddItem</c>).</summary>
    internal static readonly ItemId AspectPotion = ItemId.Of(DbType.Potion, 11), HealingPotion = ItemId.Of(DbType.Potion, 4);

    /// <summary>A shop slot's price multiplier, by its byte (<c>InitShop</c>, checked in the debug build).</summary>
    internal static readonly float[] PriceMultipliers = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f, 5f];

    /// <summary>A weapon's range byte is multiplied by this over 3 and capped (<c>WeaponDB::Load</c>).</summary>
    private const int RangeTimesThree = 5;

    /// <summary>A resistance's byte is a damage multiplier in quarters (<c>ItemDB::LoadItem</c>).</summary>
    private const float ResistanceStep = 0.25f;

    private const int StackLimit = 99, GeneralSlots = 128;

    /// <summary>What <c>Party::Barter</c> multiplies the Merchant difference by, and the share of the price selling pays.</summary>
    private const float MerchantStep = 0.05f, SellShare = 0.5f;

    /// <summary>Set the first time the party gains a potion in a shop or loot (<c>WidgetBarter::AFunc</c>).</summary>
    private const int FirstPotionFlag = 3612;

    /// <summary>The Alchemist's and Mechanic's skill numbers.</summary>
    private const int Alchemist = 0, Mechanic = 4;

    /// <summary>Writes the weapons (with their sounds), armor, items, loot tables, shops, recipes and the inventory's rules.</summary>
    public static void Import(ImportContext ctx)
    {
        var db = ctx.Db;
        ctx.Report("Items");
        var (weapons, armor, items) = BuildItems(db, ctx.Items, ctx.Engine, ctx.Menus);
        ctx.WriteRecords("weapons.json", BodySoundImporter.WithSounds(db, ctx.BodySounds, weapons));
        ctx.WriteRecords("armor.json", armor);
        ctx.WriteRecords("items.json", items);
        ctx.WriteRecords("loot.json", BuildLoot(db));
        ctx.WriteRecords("shops.json", BuildShops(db));
        ctx.WriteRecords("recipes.json", BuildRecipes(db, ctx.Items));
        ctx.WriteRecords("inventory.json", new[] { BuildInventory(db, ctx.Menus) });
    }

    /// <summary>
    /// The text of the line a description table gives <paramref name="id"/>, as the strings record holds it,
    /// or null when the table has none for it.
    /// </summary>
    internal static string? Description(GameDb db, RomStringTable table, IReadOnlyDictionary<ItemId, int> lines, ItemId id) =>
        lines.TryGetValue(id, out var line) ? DialogueText.Unescape(db.Strings[table][line]) : null;

    /// <param name="menu">Where items' and potions' descriptions come from. Without it, none have one.</param>
    public static (List<PluginWeapon> Weapons, List<PluginArmor> Armor, List<ItemRecord> Items) BuildItems(
        GameDb db, ItemTables tables, EngineTables engine, MenuTables? menu = null)
    {
        var reserved = tables.ReservedItems.Concat(tables.KeyEquipment).Concat(engine.KeyItems.Select(k => k.Item)).ToHashSet();
        var mustTake = tables.MustTakeItems.ToHashSet();
        var keyItems = engine.KeyItems.ToDictionary(k => k.Item);
        var staff = db.Weapons[ArchmageStaffPosition].Id;

        ItemTraits? Traits(ItemId id) =>
            reserved.Contains(id) || mustTake.Contains(id) ? new ItemTraits(reserved.Contains(id), mustTake.Contains(id)) : null;
        RecordId Icon(ItemId id) => Image(tables.ItemIcons.TryGetValue(id, out var icon) ? icon : ItemTables.DefaultIcon);

        RecordId? Model(IReadOnlyDictionary<int, ushort> models, int position) =>
            models.TryGetValue(position, out var scene) ? Scene(scene) : null;

        var weapons = db.Weapons.Select((w, i) => new PluginWeapon(
            DbRecordIds.Required(w.Id), w.Name, Icon(w.Id), w.Price, w.Class, w.RequiredStrength, w.Hit, w.Damage,
            Math.Min(w.Range * RangeTimesThree / 3, 255), w.Animation, w.ExpModifier, w.DamageType,
            Bonuses(w.Bonuses, w.SpellLevel), Traits(w.Id), Fixed: w.Id == staff,
            Model: Model(tables.WeaponModels, i)));

        var armor = db.Armor.Select((a, i) => new PluginArmor(
            DbRecordIds.Required(a.Id), a.Name, Icon(a.Id), a.Price, a.Slot == 0 ? ArmorSlot.Body : ArmorSlot.Shield, a.Defense, a.Protection,
            a.Dexterity, a.Stealth, a.Unk1D, a.ExpBonus, Bonuses(a.Bonuses, a.Bonuses.SpellLevel), Traits(a.Id),
            Model(ItemTables.ShieldModels, i)));

        var gear = db.Gear.Select((g, i) =>
        {
            var bonuses = Bonuses(g.Bonuses, g.Bonuses.SpellLevel);
            return new ItemRecord(DbRecordIds.Required(g.Id), g.Name, Icon(g.Id), g.Price, (ItemKind)g.Id.Type, g.ExpMultiplier, g.Damage, g.Protection,
                g.RequiredStrength, g.RequiredIntelligence, bonuses == new GearBonuses() ? null : bonuses, Traits(g.Id),
                keyItems.TryGetValue(g.Id, out var key)
                    ? new KeyItemFlags(DbRecordIds.Flag(key.Held), key.Region == 0 ? null : DbRecordIds.Flag(key.Region))
                    : null,
                menu is null ? null : Description(db, RomStringTable.Items, menu.ItemDescriptions, g.Id),
                Model(tables.ItemModels, i));
        });

        // Potions aren't in the item DB: potion n is named by line n of their own string table, and
        // priced by the table in code (ItemInstance::InitPotion).
        var names = db.Strings[RomStringTable.PotionNames];
        var potions = Enumerable.Range(0, ItemTables.PotionCount).Select(i =>
        {
            var id = ItemId.Of(DbType.Potion, (byte)i);
            var (effects, duration) = MagicImporter.Potion(i);
            return new ItemRecord(DbRecordIds.Required(id), names[i], Image(ItemTables.PotionIcon), tables.PotionPrices[i], ItemKind.Potion,
                Traits: Traits(id), Description: menu is null ? null : Description(db, RomStringTable.PotionDetails, menu.PotionDescriptions, id),
                Effects: effects, Duration: duration);
        });

        return (Sorted(weapons, w => w.Id), Sorted(armor, a => a.Id), Sorted(gear.Concat(potions), i => i.Id));
    }

    /// <summary>Niesen can equip only the Archmage Staff (<c>Entity::canEquipWeapon</c>).</summary>
    public static List<PluginEntity> WithSoleWeapons(GameDb db, List<PluginEntity> entities)
    {
        var niesen = DbRecordIds.Required(db.Entities[NiesenDbPosition].Id);
        var staff = DbRecordIds.Required(db.Weapons[ArchmageStaffPosition].Id);
        return [.. entities.Select(e => e.Id == niesen ? e with { SoleWeapon = staff } : e)];
    }

    /// <summary>Each skill's icon (<c>GetSkillIcons</c>).</summary>
    public static List<SkillRecord> WithIcons(ItemTables tables, List<SkillRecord> skills) =>
        [.. skills.Select(s => int.Parse(s.Id.Key) is var n && n < tables.SkillIcons.Count ? s with { Icon = Image(tables.SkillIcons[n]) } : s)];

    public static List<PluginLoot> BuildLoot(GameDb db) => Sorted(db.Loot.Select(l => new PluginLoot(
        DbRecordIds.Required(l.Id), l.Name, l.GoldMin, l.GoldMax, l.ArmorChance, l.ShieldChance, [.. l.WeaponChances.Select(c => (int)c)],
        l.ReagentChance, l.ReagentMin, l.ReagentMax,
        [.. l.Drops.Select(d => new PluginLootDrop(d.Item.IsNone ? null : DbRecordIds.Required(d.Item), d.Chance, d.Min, d.Max))])), l => l.Id);

    /// <summary>
    /// Each shop's stock (<c>InitShop</c>): a priced slot is stocked only when its third byte is
    /// nonzero, and the three extra slots are stocked at the base price.
    /// </summary>
    public static List<PluginShop> BuildShops(GameDb db) => [.. db.Shops.Select((s, i) => new PluginShop(
        RecordId.Decimal(RecordTypes.Shop, i), DbRecordIds.Required(s.Shopkeeper),
        [
            .. s.Stock.Where(x => !x.Item.IsNone && x.Unk2 != 0).Select(x => new ShopStock(DbRecordIds.Required(x.Item), PriceMultipliers[x.PriceMultiplier])),
            .. s.ExtraStock.Where(x => !x.IsNone).Select(x => new ShopStock(DbRecordIds.Required(x))),
        ]))];

    /// <summary>The Alchemist's potions, then the Mechanic's armor, each in the original's order.</summary>
    public static List<RecipeRecord> BuildRecipes(GameDb db, ItemTables tables)
    {
        var (spice, herb, gemstone) = Reagents(db);
        var recipes = tables.PotionRecipes.Select(p => (
            Skill: Alchemist,
            Product: DbRecordIds.Required(ItemId.Of(DbType.Potion, (byte)p.Potion)),
            Ingredients: new[] { new Ingredient(spice, p.Spice), new Ingredient(herb, p.Herb), new Ingredient(gemstone, p.Gemstone) }
                .Where(x => x.Count > 0).ToList(),
            Level: p.Alchemist, p.Step, Unk5: (int?)p.Unk5))
            .Concat(tables.ArmorRecipes.Select(a => (
                Skill: Mechanic,
                Product: DbRecordIds.Required(db.Armor[a.Armor].Id),
                Ingredients: new List<Ingredient> { new(DbRecordIds.Required(db.Gear[a.Hide].Id), 1) },
                Level: a.Mechanic, a.Step, Unk5: (int?)null)));
        return [.. recipes.Select((r, i) => new RecipeRecord(RecordId.Decimal(RecordTypes.Recipe, i),
            RecordId.Decimal(RecordTypes.Skill, r.Skill), r.Product, r.Ingredients, r.Level, r.Step, r.Unk5))];
    }

    public static InventoryRecord BuildInventory(GameDb db, MenuTables menuTables)
    {
        var m = menuTables.Messages;
        var (spice, herb, gemstone) = Reagents(db);
        return new InventoryRecord(RecordId.Decimal(RecordTypes.Inventory, 0), StackLimit, GeneralSlots, spice, herb, gemstone,
            Image(ItemTables.DefaultIcon), MerchantStep, SellShare,
            [DbRecordIds.Required(db.Weapons[CyclopsClubPosition].Id)],
            [new ItemReplacement(DbRecordIds.Required(AspectPotion), DbRecordIds.Required(HealingPotion))],
            DbRecordIds.Flag(FirstPotionFlag), new InventoryMessages(m.PotionNotNow, m.PotionNotInTrek, m.ScrollNoSpell));
    }

    private static (RecordId Spice, RecordId Herb, RecordId Gemstone) Reagents(GameDb db) =>
        (DbRecordIds.Required(db.Gear[SpicePosition].Id), DbRecordIds.Required(db.Gear[HerbPosition].Id), DbRecordIds.Required(db.Gear[GemstonePosition].Id));

    /// <param name="spellLevel">A weapon's is its own field; armor's and gear's is in the bonuses.</param>
    private static GearBonuses Bonuses(EquipmentBonuses b, int spellLevel) => new(
        b.Aspect,
        b.Stat == 0xFF ? null
            : b.Stat < 6 ? new StatBonus(RecordId.Decimal(RecordTypes.Stat, b.Stat), b.StatBonus)
            : throw new InvalidDataException($"Gear raises stat {b.Stat}."),
        b.Skill == 0xFF ? null : new SkillBonus(Skill(b.Skill), b.SkillBonus),
        b.Spell.IsNone ? null : new ItemSpell(DbRecordIds.Required(b.Spell), spellLevel, b.SpellCharges),
        b.Magic.IsNone ? null : new ItemEnchantment(DbRecordIds.Required(b.Magic), b.MagicLevel),
        b.Resist == Element.None ? null : new Resistance(b.Resist, b.ResistAmount * ResistanceStep));

    /// <summary>
    /// The code indexes a character's 24 bytes of skills with a gear's skill byte: the 12 skills, the
    /// 11 weapon skills, then the shield (<c>CharSkills::ModdedSkillAdd</c>).
    /// </summary>
    private static RecordId Skill(int n) => n switch
    {
        < 12 => RecordId.Decimal(RecordTypes.Skill, n),
        < 24 => RecordId.Decimal(RecordTypes.WeaponSkill, n - 12),
        _ => throw new InvalidDataException($"Gear raises skill {n}."),
    };

    private static RecordId Scene(int borg5) => RecordId.Hex(RecordTypes.Scene, borg5);

    private static RecordId Image(int borg8) => RecordId.Hex(RecordTypes.Image, borg8);

    private static List<T> Sorted<T>(IEnumerable<T> records, Func<T, RecordId> id) => [.. records.OrderBy(r => id(r).Key, StringComparer.Ordinal)];
}
