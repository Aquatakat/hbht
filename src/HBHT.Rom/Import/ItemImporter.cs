using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Quests;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

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

    /// <summary>
    /// The request each weapon animation byte asks of the attacker (<c>Combat.AnimationByWeaponClass</c>, <c>0x800D27E0</c>, byte pairs
    /// <c>00 10</c>, <c>01 11</c>, <c>02 0D</c>, <c>03 0C</c>, <c>04 0F</c>, <c>05 0E</c> ended by <c>FF</c>, which
    /// <c>Combatant.AnimationForWeapon</c>, <c>0x8005FEE4</c>, walks), and 18 for any other.
    /// </summary>
    internal static readonly IReadOnlyList<int> AttackRequests = [16, 17, 13, 12, 15, 14];

    /// <summary>The attack request for a weapon animation byte not in <see cref="AttackRequests"/>.</summary>
    internal const int OtherAttackRequest = 18;

    /// <summary>The request a weapon's animation byte asks of the attacker's body.</summary>
    internal static RecordId AttackRequest(int animation) =>
        Requests.Id(animation >= 0 && animation < AttackRequests.Count ? AttackRequests[animation] : OtherAttackRequest);

    /// <summary>Writes the items (with their weapons' sounds), loot tables, shops, recipes and the inventory's rules.</summary>
    public static void Import(ImportContext ctx)
    {
        var db = ctx.Db;
        ctx.Report("Items");
        var items = BuildItems(db, ctx.Items, ctx.Engine, ctx.Menus, CombatImporter.ItemFacts(ctx));
        items = BodySoundImporter.WithSounds(db, ctx.BodySounds, items);
        ctx.WriteRecords("items.json", MagicImporter.WithPotionVisuals(items, ctx.SpellVisuals, ctx.Archive));
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

    /// <summary>
    /// Every item (ADR 0031): the weapon DB's, the armor DB's, the item DB's and the potions, sorted by ID. A weapon's facts from
    /// combat's tables (<paramref name="facts"/>) and the inventory's own go on the items they're about.
    /// </summary>
    /// <param name="menu">Where items' and potions' descriptions come from. Without it, none have one.</param>
    /// <param name="facts">Combat's facts about items. Without them, no weapon flies or strikes in melee and no flask spreads the AI.</param>
    public static List<ItemRecord> BuildItems(GameDb db, ItemTables tables, EngineTables engine, MenuTables? menu = null, ItemFacts? facts = null)
    {
        var reserved = tables.ReservedItems.Concat(tables.KeyEquipment).Concat(engine.KeyItems.Select(k => k.Item)).ToHashSet();
        var mustTake = tables.MustTakeItems.ToHashSet();
        var keyItems = engine.KeyItems.ToDictionary(k => k.Item);
        var staff = db.Weapons[ArchmageStaffPosition].Id;
        var club = DbRecordIds.Required(db.Weapons[CyclopsClubPosition].Id);
        var aspect = DbRecordIds.Required(AspectPotion);
        var kept = DbRecordIds.Required(ItemId.Of(DbType.Potion, (byte)MagicImporter.StealthPotion));

        ItemTraits? Traits(ItemId id) =>
            reserved.Contains(id) || mustTake.Contains(id) ? new ItemTraits(reserved.Contains(id), mustTake.Contains(id)) : null;
        RecordId Icon(ItemId id) => Image(tables.ItemIcons.TryGetValue(id, out var icon) ? icon : ItemTables.DefaultIcon);

        RecordId? Model(IReadOnlyDictionary<int, ushort> models, int position) =>
            models.TryGetValue(position, out var scene) ? Scene(scene) : null;

        var weapons = db.Weapons.Select((w, i) =>
        {
            var id = DbRecordIds.Required(w.Id);
            return new ItemRecord(id, w.Name, Icon(w.Id), w.Price, ItemKind.Weapon,
                Weapon: new WeaponStats(Numbering.WeaponSkill(w.Class), w.Damage, Math.Min(w.Range * RangeTimesThree / 3, 255), w.Hit,
                    AttackRequest(w.Animation), Numbering.Element(w.DamageType), facts?.Missiles.GetValueOrDefault(id),
                    facts?.StrikesInMelee.Contains(id) == true),
                Requirements: Requirements((Strength, w.RequiredStrength)),
                Bonuses: Bonuses(w.Bonuses, w.SpellLevel), LootExperience: w.ExpModifier, Traits: Traits(w.Id), Fixed: w.Id == staff,
                Model: Model(tables.WeaponModels, i), Refused: id == club);
        });

        // Armor's Dexterity is a modified-only add, as a stat bonus is (Stats.AddModdedOnly, 0x8007B498), and its Stealth a skill
        // bonus that the inventory's rule lets count unlearnt.
        var armor = db.Armor.Select((a, i) => new ItemRecord(DbRecordIds.Required(a.Id), a.Name, Icon(a.Id), a.Price,
            a.Slot == 0 ? ItemKind.Body : ItemKind.Shield,
            Bonuses: Bonuses(a.Bonuses, a.Bonuses.SpellLevel, dexterity: a.Dexterity, stealth: a.Stealth),
            LootExperience: a.ExpBonus, Protection: a.Protection, Defense: a.Defense, Traits: Traits(a.Id),
            Model: Model(ItemTables.ShieldModels, i)));

        var gear = db.Gear.Select((g, i) => new ItemRecord(DbRecordIds.Required(g.Id), g.Name, Icon(g.Id), g.Price, (ItemKind)g.Id.Type,
            Requirements: Requirements((Strength, g.RequiredStrength), (Intelligence, g.RequiredIntelligence)),
            Bonuses: Bonuses(g.Bonuses, g.Bonuses.SpellLevel), LootExperience: g.ExpMultiplier, Damage: g.Damage, Protection: g.Protection,
            Traits: Traits(g.Id),
            KeyItem: keyItems.TryGetValue(g.Id, out var key)
                ? new KeyItemFlags(DbRecordIds.Flag(key.Held), key.Region == 0 ? null : DbRecordIds.Flag(key.Region))
                : null,
            Description: menu is null ? null : Description(db, RomStringTable.Items, menu.ItemDescriptions, g.Id),
            Model: Model(tables.ItemModels, i)));

        // Potions aren't in the item DB: potion n is named by line n of their own string table, and
        // priced by the table in code (ItemInstance::InitPotion).
        var names = db.Strings[RomStringTable.PotionNames];
        var potions = Enumerable.Range(0, ItemTables.PotionCount).Select(i =>
        {
            var id = ItemId.Of(DbType.Potion, (byte)i);
            var record = DbRecordIds.Required(id);
            var (effects, duration) = MagicImporter.Potion(i);
            return new ItemRecord(record, names[i], Image(ItemTables.PotionIcon), tables.PotionPrices[i], ItemKind.Potion,
                Traits: Traits(id), Description: menu is null ? null : Description(db, RomStringTable.PotionDetails, menu.PotionDescriptions, id),
                Effects: effects, Duration: duration, Use: MagicImporter.PotionUse(i),
                AiSpread: facts?.AiSpread.GetValueOrDefault(record) ?? 0,
                Becomes: record == aspect ? DbRecordIds.Required(HealingPotion) : null, KeptAfterBattle: record == kept);
        });

        return Sorted(weapons.Concat(armor).Concat(gear).Concat(potions), i => i.Id);
    }

    private static readonly RecordId Strength = RecordId.Decimal(RecordTypes.Stat, 4), Intelligence = RecordId.Decimal(RecordTypes.Stat, 0),
        Dexterity = RecordId.Decimal(RecordTypes.Stat, 2), Stealth = RecordId.Decimal(RecordTypes.Skill, 7);

    /// <summary>A requirements map of the stats whose least value isn't 0, or null for none.</summary>
    private static Dictionary<RecordId, int>? Requirements(params (RecordId Stat, int Least)[] requirements) =>
        requirements.Any(r => r.Least != 0) ? requirements.Where(r => r.Least != 0).ToDictionary(r => r.Stat, r => r.Least) : null;

    /// <summary>Niesen can equip only the Archmage Staff (<c>Entity::canEquipWeapon</c>).</summary>
    public static List<EntityRecord> WithSoleWeapons(GameDb db, List<EntityRecord> entities)
    {
        var niesen = DbRecordIds.Required(db.Entities[NiesenDbPosition].Id);
        var staff = DbRecordIds.Required(db.Weapons[ArchmageStaffPosition].Id);
        return [.. entities.Select(e => e.Id == niesen && e.Sheet is { } sheet ? e with { Sheet = sheet with { SoleWeapon = staff } } : e)];
    }

    /// <summary>Each skill's icon (<c>GetSkillIcons</c>).</summary>
    public static List<SkillRecord> WithIcons(ItemTables tables, List<SkillRecord> skills) =>
        [.. skills.Select(s => int.Parse(s.Id.Key) is var n && n < tables.SkillIcons.Count ? s with { Icon = Image(tables.SkillIcons[n]) } : s)];

    public static List<LootRecord> BuildLoot(GameDb db) => Sorted(db.Loot.Select(l => new LootRecord(
        DbRecordIds.Required(l.Id), l.Name, l.GoldMin, l.GoldMax, l.ArmorChance, l.ShieldChance,
        l.WeaponChances.Count == 3 ? new WeaponChances(l.WeaponChances[0], l.WeaponChances[1], l.WeaponChances[2])
            : throw new InvalidDataException($"Loot table {l.Id} has {l.WeaponChances.Count} weapon chances."),
        l.ReagentChance, l.ReagentMin, l.ReagentMax,
        [.. l.Drops.Select((d, i) => (d, i)).Where(x => Drop(l.Id, x.d)).Select(x => new LootDrop(DbRecordIds.Required(x.d.Item), x.d.Chance,
            // 1.0 rolls a count for its first two drops alone (Combat.RollChestItem, 0x8005DF38; get_chest_loot); the rest give one.
            x.i < 2 && (x.d.Min, x.d.Max) != (1, 1) ? x.d.Min : null, x.i < 2 && (x.d.Min, x.d.Max) != (1, 1) ? x.d.Max : null))])), l => l.Id);

    /// <summary>
    /// Whether a ROM drop is written: not an empty one, which never rolls, its chance being 0 in every table. An empty one with a
    /// chance would draw from the random numbers, which the records couldn't say, so it's an error.
    /// </summary>
    private static bool Drop(ItemId table, RomLootDrop d) =>
        !d.Item.IsNone ? true
            : d.Chance == 0 ? false
            : throw new InvalidDataException($"Loot table {table} has an empty drop with a chance of {d.Chance}.");

    /// <summary>
    /// Each shop's stock (<c>InitShop</c>): a priced slot is stocked only when its third byte is
    /// nonzero, and the three extra slots are stocked at the base price.
    /// </summary>
    public static List<ShopRecord> BuildShops(GameDb db) => [.. db.Shops.Select((s, i) => new ShopRecord(
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
            Level: p.Alchemist, p.Step))
            .Concat(tables.ArmorRecipes.Select(a => (
                Skill: Mechanic,
                Product: DbRecordIds.Required(db.Armor[a.Armor].Id),
                Ingredients: new List<Ingredient> { new(DbRecordIds.Required(db.Gear[a.Hide].Id), 1) },
                Level: a.Mechanic, a.Step)));
        return [.. recipes.Select((r, i) => new RecipeRecord(RecordId.Decimal(RecordTypes.Recipe, i),
            RecordId.Decimal(RecordTypes.Skill, r.Skill), r.Product, r.Ingredients, r.Level, r.Step))];
    }

    public static InventoryRecord BuildInventory(GameDb db, MenuTables menuTables)
    {
        var m = menuTables.Messages;
        var (spice, herb, gemstone) = Reagents(db);
        return new InventoryRecord(RecordId.Decimal(RecordTypes.Inventory, 0), StackLimit, GeneralSlots, spice, herb, gemstone,
            Image(ItemTables.DefaultIcon), MerchantStep, SellShare, [ItemKind.Body, ItemKind.Shield],
            DbRecordIds.Flag(FirstPotionFlag), new InventoryMessages(m.PotionNotNow, m.PotionNotInTrek, m.ScrollNoSpell));
    }

    internal static (RecordId Spice, RecordId Herb, RecordId Gemstone) Reagents(GameDb db) =>
        (DbRecordIds.Required(db.Gear[SpicePosition].Id), DbRecordIds.Required(db.Gear[HerbPosition].Id), DbRecordIds.Required(db.Gear[GemstonePosition].Id));

    /// <param name="spellLevel">A weapon's is its own field; armor's and gear's is in the bonuses.</param>
    /// <param name="dexterity">Armor's Dexterity penalty, which goes with the stat bonus.</param>
    /// <param name="stealth">Armor's Stealth, which goes with the skill bonus.</param>
    /// <returns>Null for none.</returns>
    private static ItemBonuses? Bonuses(EquipmentBonuses b, int spellLevel, int dexterity = 0, int stealth = 0)
    {
        var stats = new Dictionary<RecordId, int>();
        if (b.Stat != 0xFF)
            stats[b.Stat < 6 ? RecordId.Decimal(RecordTypes.Stat, b.Stat) : throw new InvalidDataException($"Gear raises stat {b.Stat}.")] = b.StatBonus;
        if (dexterity != 0)
            stats[Dexterity] = stats.GetValueOrDefault(Dexterity) + dexterity;
        var skills = new Dictionary<RecordId, int>();
        if (b.Skill != 0xFF)
            skills[Skill(b.Skill)] = b.SkillBonus;
        if (stealth != 0)
            skills[Stealth] = skills.GetValueOrDefault(Stealth) + stealth;
        var resistances = b.Resist == Element.None ? null
            : new Dictionary<RecordId, float> { [Numbering.Element(b.Resist)!.Value] = b.ResistAmount * ResistanceStep };
        var bonuses = new ItemBonuses(
            b.Aspect,
            stats.Count == 0 ? null : stats,
            skills.Count == 0 ? null : skills,
            b.Spell.IsNone ? null : new ItemSpell(DbRecordIds.Required(b.Spell), spellLevel, b.SpellCharges),
            b.Magic.IsNone ? null : new ItemEnchantment(DbRecordIds.Required(b.Magic), b.MagicLevel),
            resistances);
        return bonuses == new ItemBonuses() ? null : bonuses;
    }

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
