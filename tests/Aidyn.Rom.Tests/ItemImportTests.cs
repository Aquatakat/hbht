using Aidyn.Plugins;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Import;
using Aidyn.Rom.Quests;
using R = Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

public class ItemTablesTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    private static IReadOnlyDictionary<ItemId, string> Names(GameDb db) =>
        db.Entities.Select(e => (e.Id, e.Name)).Concat(db.Gear.Select(g => (g.Id, g.Name)))
            .Concat(db.Weapons.Select(w => (w.Id, w.Name))).Concat(db.Armor.Select(a => (a.Id, a.Name)))
            .ToDictionary(e => e.Id, e => e.Name);

    [Theory]
    [MemberData(nameof(Versions))]
    public void PotionsArePricedByTheTableInCode(RomVersionId id)
    {
        var tables = ItemTables.Read(TestRoms.Get(id));

        Assert.Equal([250, 1000, 200, 500, 50, 300, 200, 200, 2500, 500, 500, 500, 0, 300, 300, 500, 500], tables.PotionPrices);
        Assert.Equal(ItemTables.PotionCount, tables.PotionPrices.Count);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheAlchemistsRecipesAreInTheOrderTheCodeHasThem(RomVersionId id)
    {
        var tables = ItemTables.Read(TestRoms.Get(id));

        Assert.Equal(
            [
                new PotionRecipe(0, 1, 0, 2, 1, 2, 0), new PotionRecipe(1, 3, 0, 3, 4, 6, 0), new PotionRecipe(2, 3, 3, 0, 5, 10, 0),
                new PotionRecipe(3, 2, 0, 3, 5, 10, 0), new PotionRecipe(4, 0, 3, 0, 1, 2, 0), new PotionRecipe(5, 0, 2, 0, 5, 1, 0),
                new PotionRecipe(6, 2, 3, 0, 3, 6, 0), new PotionRecipe(7, 3, 2, 0, 3, 4, 0), new PotionRecipe(9, 0, 2, 2, 2, 4, 0),
                new PotionRecipe(10, 2, 2, 0, 2, 4, 0), new PotionRecipe(13, 3, 0, 0, 3, 5, 0), new PotionRecipe(14, 2, 1, 2, 2, 4, 0),
                new PotionRecipe(15, 0, 0, 2, 2, 5, 0), new PotionRecipe(16, 2, 3, 0, 2, 4, 0), new PotionRecipe(8, 2, 0, 2, 3, 10, 0),
            ],
            tables.PotionRecipes);
        Assert.All(tables.PotionRecipes, r => Assert.Equal(0, r.Step));
    }

    /// <summary>The recipes name hides by position in the item DB and armor by position in the armor DB, not by ID.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void TheMechanicsRecipesNameHidesAndArmorByTheirPositionsInTheDb(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = ItemTables.Read(rom);
        var db = GameDb.Read(rom);

        Assert.Equal(
            [new ArmorRecipe(33, 28, 7, 5), new ArmorRecipe(34, 27, 3, 2), new ArmorRecipe(35, 29, 1, 1), new ArmorRecipe(36, 30, 5, 4), new ArmorRecipe(36, 56, 4, 3)],
            tables.ArmorRecipes);

        Assert.Equal([(0x0121, "Hellhound Hide"), (0x0122, "Darkenbat Hide"), (0x0123, "Beast Hide"), (0x0124, "Chitin Plates")],
            Enumerable.Range(33, 4).Select(i => ((int)db.Gear[i].Id.Value, db.Gear[i].Name)));
        Assert.Equal([(0x0520, "Darkenbat Hide"), (0x051F, "Hellhound Hide"), (0x051E, "Beast Hide"), (0x051C, "Scorpion scale"), (0x061D, "Scorpion Shield")],
            new[] { 27, 28, 29, 30, 56 }.Select(i => ((int)db.Armor[i].Id.Value, db.Armor[i].Name)));
    }

    /// <summary>The item icons cover 65 of the weapons, and no potion: they all share one icon.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void ItemIconsCoverGearAndNotPotions(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = ItemTables.Read(rom);
        var archive = BorgArchive.Open(rom);

        Assert.Equal(286, tables.ItemIcons.Count);
        Assert.Equal(65, tables.ItemIcons.Keys.Count(k => k.Type == DbType.Weapon));
        Assert.DoesNotContain(tables.ItemIcons.Keys, k => k.Type == DbType.Potion);
        Assert.All(tables.ItemIcons.Values, icon => Assert.Equal(BorgType.UiImage, archive.Listings[icon].Type));
        Assert.Equal(BorgType.UiImage, archive.Listings[ItemTables.DefaultIcon].Type);
        Assert.Equal(BorgType.UiImage, archive.Listings[ItemTables.PotionIcon].Type);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void SkillAspectSchoolAndSpellIconsAreByTheirNumbers(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = ItemTables.Read(rom);
        var archive = BorgArchive.Open(rom);

        Assert.Equal(Enumerable.Range(387, 12), tables.SkillIcons.Select(i => (int)i));
        Assert.Equal([0, 419, 420], tables.AspectIcons);
        Assert.Equal([425, 421, 422, 423, 0, 424], tables.SchoolIcons);
        Assert.Equal([433, 427, 430, 428, 426, 429, 431, 432, 434], tables.SpellSymbolIcons);
        Assert.All(tables.SkillIcons.Concat(tables.AspectIcons).Concat(tables.SchoolIcons).Concat(tables.SpellSymbolIcons).Where(i => i != 0),
            icon => Assert.Equal(BorgType.UiImage, archive.Listings[icon].Type));
    }

    /// <summary>The table lists 0x110E twice, so it names 79 different items in its 80 entries.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void ReservedItemsStartWithTheReagentsAndHoldAllButOneUnusedPotion(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = ItemTables.Read(rom);
        var names = Names(GameDb.Read(rom));

        Assert.Equal(80, tables.ReservedItems.Count);
        Assert.Equal([0x0111, 0x0112, 0x0113, 0x010E], tables.ReservedItems.Take(4).Select(i => (int)i.Value));
        Assert.Equal("Sapphire Gem", names[new ItemId(0x010E)]);
        var potions = Enumerable.Range(0x1000, 0x11).Select(v => new ItemId((ushort)v)).ToList();
        Assert.Equal(potions.Where(p => p.Value != 0x100C), tables.ReservedItems.Where(i => i.Type == DbType.Potion));
        Assert.Equal([new ItemId(0x110E)], tables.ReservedItems.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void KeyEquipmentIsTheThirteenUniqueItems(RomVersionId id)
    {
        var tables = ItemTables.Read(TestRoms.Get(id));

        Assert.Equal([0x0541, 0x0640, 0x0734, 0x0757, 0x0926, 0x0A28, 0x0B29, 0x0C27, 0x0D03, 0x0D00, 0x0D02, 0x1300, 0x0B04],
            tables.KeyEquipment.Select(i => (int)i.Value));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void MustTakeItemsAreTheQuestItemsTheDecompNames(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = ItemTables.Read(rom);
        var names = Names(GameDb.Read(rom));

        Assert.Equal(21, tables.MustTakeItems.Count);
        Assert.Equal(
            [0x0100, 0x0101, 0x0119, 0x0541, 0x0640, 0x0734, 0x0757, 0x0926, 0x0A28, 0x0B29, 0x0C27, 0x0D03, 0x0D02, 0x112E,
                0x1201, 0x1202, 0x1203, 0x1204, 0x1205, 0x1206, 0x1207],
            tables.MustTakeItems.Select(i => (int)i.Value));
        Assert.Equal(
            ["Letter to Txomin", "Amaranth", "Cradawgh's Body", "Irondrake Plate", "Stardrake Aegis", "Lodin's Sword", "Firedrake Fang",
                "Spiritdrake Helm", "Nightdrake Mantle", "Stormdrake Claws", "Magedrake Ring", "Horn of Kynon", "Stormbreaker", "Lighthouse Scroll",
                "Black Key", "Skull Key", "Blood Key", "Bone Key", "Lighthouse Key", "Lodin's Key", "Dragon Key"],
            tables.MustTakeItems.Select(i => names[i]));
    }
}

[Collection(ImportedPluginCollection.Name)]
public sealed class ItemImportTests(MapObjectFixture fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static RomImage Rom => TestRoms.Get(RomVersionId.Na10);

    private IReadOnlyList<R.WeaponRecord> Weapons => [.. Set.All<R.WeaponRecord>(RecordTypes.Weapon)];

    private IReadOnlyList<R.ArmorRecord> Armor => [.. Set.All<R.ArmorRecord>(RecordTypes.Armor)];

    private IReadOnlyList<R.ItemRecord> Items => [.. Set.All<R.ItemRecord>(RecordTypes.Item)];

    private R.ItemRecord Item(int id) => Set.Get<R.ItemRecord>(RecordId.Hex(RecordTypes.Item, id));

    private R.ArmorRecord ArmorPiece(int id) => Set.Get<R.ArmorRecord>(RecordId.Hex(RecordTypes.Armor, id));

    private R.WeaponRecord Weapon(int id) => Set.Get<R.WeaponRecord>(RecordId.Hex(RecordTypes.Weapon, id));

    private List<R.ShopRecord> Shops => [.. Set.All<R.ShopRecord>(RecordTypes.Shop).OrderBy(s => int.Parse(s.Id.Key))];

    private List<R.RecipeRecord> Recipes => [.. Set.All<R.RecipeRecord>(RecordTypes.Recipe).OrderBy(r => int.Parse(r.Id.Key))];

    private static RecordId Stat(int n) => RecordId.Decimal(RecordTypes.Stat, n);

    private static RecordId SkillId(int n) => RecordId.Decimal(RecordTypes.Skill, n);

    private static RecordId ItemRecordId(int id) => RecordId.Hex(RecordTypes.Item, id);

    private static RecordId ImageId(int id) => RecordId.Hex(RecordTypes.Image, id);

    [Fact]
    public void TheImportHasEveryWeaponArmorAndItemAndTheSeventeenPotions()
    {
        var db = GameDb.Read(Rom);

        Assert.Equal(105, db.Weapons.Count);
        Assert.Equal(68, db.Armor.Count);
        Assert.Equal(197, db.Gear.Count);
        Assert.Equal(105, Weapons.Count);
        Assert.Equal(68, Armor.Count);
        Assert.Equal(214, Items.Count);
        Assert.Equal(17, Items.Count(i => i.Kind == R.ItemKind.Potion));
        Assert.Equal(db.Weapons.Select(w => w.Name).Order(), Weapons.Select(w => w.Name).Order());
    }

    [Fact]
    public void TheListsAreUniqueAndSortedByKey()
    {
        var rom = Rom;
        var (weapons, armor, items) = ItemImporter.BuildItems(GameDb.Read(rom), ItemTables.Read(rom), EngineTables.Read(rom));

        foreach (var ids in new[] { weapons.Select(w => w.Id), armor.Select(a => a.Id), items.Select(i => i.Id) })
        {
            var list = ids.ToList();
            Assert.Equal(list.Count, list.Distinct().Count());
            Assert.Equal(list.OrderBy(i => i.Key, StringComparer.Ordinal), list);
        }
    }

    [Fact]
    public void ARangeIsTheRomsByteTimesFiveThirdsUpToTwoFiftyFive()
    {
        var db = GameDb.Read(Rom);

        Assert.All(db.Weapons, rom => Assert.Equal(Math.Min(rom.Range * 5 / 3, 255), Weapon(rom.Id.Value).Range));
        Assert.Contains(db.Weapons, rom => rom.Range * 5 / 3 > 255);
    }

    [Fact]
    public void OnlyTheArchmagesStaffIsFixedAndItIsNiesensSoleWeapon()
    {
        var rom = Rom;
        var db = GameDb.Read(rom);
        var staff = RecordId.Hex(RecordTypes.Weapon, 0x0735);
        var models = EntityModels.Read(rom);

        Assert.Equal([staff], Weapons.Where(w => w.Fixed).Select(w => w.Id));

        var entities = ItemImporter.WithSoleWeapons(db, CharacterImporter.BuildEntities(db, models));
        var sole = Assert.Single(entities, e => e.SoleWeapon is not null);
        Assert.Equal(RecordId.Hex(RecordTypes.Entity, 0x024A), sole.Id);
        Assert.Equal(staff, sole.SoleWeapon);
        Assert.Equal("Niesen", sole.Name);
        Assert.Equal([sole.Id], Set.All<R.EntityRecord>(RecordTypes.Entity).Where(e => e.SoleWeapon is not null).Select(e => e.Id));
    }

    [Fact]
    public void PotionsAreNamedByTheirOwnTableAndShareOneIcon()
    {
        var healing = Item(0x1004);

        Assert.Equal("Healing Potion", healing.Name);
        Assert.Equal((R.ItemKind.Potion, 50, ImageId(0x35E5)), (healing.Kind, healing.Price, healing.Icon));
        Assert.True(healing.Traits!.OwnSlot);
        Assert.All(Items.Where(i => i.Kind == R.ItemKind.Potion), p => Assert.Equal(ImageId(ItemTables.PotionIcon), p.Icon));
        Assert.Equal(ItemTables.Read(Rom).PotionPrices, Enumerable.Range(0, 17).Select(i => Item(0x1000 + i).Price));
    }

    [Fact]
    public void AWandRaisesAStatAndASkillAndNeedsIntelligence()
    {
        var wand = Item(0x0D0B);

        Assert.Equal("Wizard's Wand", wand.Name);
        Assert.Equal(new R.StatBonus(Stat(5), 20), wand.Bonuses!.Stat);
        Assert.Equal(new R.SkillBonus(SkillId(11), 1), wand.Bonuses.Skill);
        Assert.Equal(15, wand.RequiredIntelligence);
    }

    [Fact]
    public void AHelmCanCarryASpellAResistanceAndAProtection()
    {
        var helm = Item(0x0901);

        Assert.Equal("Helm of Tempests", helm.Name);
        Assert.Equal(R.ItemKind.Helmet, helm.Kind);
        Assert.Equal(new R.ItemSpell(RecordId.Hex(RecordTypes.Spell, 0x0306), 3, 25), helm.Bonuses!.Spell);
        Assert.Equal(new R.SkillBonus(SkillId(10), 2), helm.Bonuses.Skill);
        Assert.Equal(new R.Resistance(R.Element.Air, 0.5f), helm.Bonuses.Resistance);
        Assert.Equal((1, 1), (helm.Damage, helm.Protection));
    }

    [Fact]
    public void GearCanLowerAStatOrASkill()
    {
        var hat = Item(0x0900);
        var amulet = Item(0x1307);

        Assert.Equal("Kendall's Hat", hat.Name);
        Assert.Equal(new R.SkillBonus(SkillId(7), -1), hat.Bonuses!.Skill);
        Assert.Equal("Amulet of Pork", amulet.Name);
        Assert.Equal(new R.StatBonus(Stat(3), -5), amulet.Bonuses!.Stat);
        Assert.Equal(new R.Resistance(R.Element.Physical, 2f), amulet.Bonuses.Resistance);
    }

    [Fact]
    public void GearWithNoBonusesHasNone()
    {
        Assert.Contains(Items, i => i.Kind != R.ItemKind.Potion && i.Bonuses is null);
        Assert.Contains(Items, i => i.Bonuses is not null);
        Assert.All(Items.Where(i => i.Kind == R.ItemKind.Potion), p => Assert.Null(p.Bonuses));
    }

    [Fact]
    public void ChainmailIsBodyArmorThatCostsDexterityAndStealth()
    {
        var chainmail = ArmorPiece(0x0503);

        Assert.Equal("Chainmail", chainmail.Name);
        Assert.Equal((R.ArmorSlot.Body, 5, -2, -5), (chainmail.Slot, chainmail.Protection, chainmail.Dexterity, chainmail.Stealth));
    }

    [Fact]
    public void AShieldHasADefense()
    {
        var shield = ArmorPiece(0x0615);

        Assert.Equal("Tower Shield", shield.Name);
        Assert.Equal((R.ArmorSlot.Shield, 6, 5, -6), (shield.Slot, shield.Defense, shield.Protection, shield.Dexterity));
        Assert.All(Armor.Where(a => a.Slot == R.ArmorSlot.Body), a => Assert.Equal(0, a.Defense));
        Assert.All(Armor, a => Assert.Equal(a.Id.Key.StartsWith("0x06", StringComparison.Ordinal) ? R.ArmorSlot.Shield : R.ArmorSlot.Body, a.Slot));
    }

    [Fact]
    public void ArmorCanLowerAStatAndResistAnElement()
    {
        var robes = ArmorPiece(0x053D);

        Assert.Equal("Chaos Robes", robes.Name);
        Assert.Equal(new R.StatBonus(Stat(3), -2), robes.Bonuses.Stat);
        Assert.Equal(new R.Resistance(R.Element.Magic, 0.75f), robes.Bonuses.Resistance);
    }

    /// <summary>A weapon reads its spell's level from its own field, not from the bonuses block, whose byte is unread.</summary>
    [Fact]
    public void AWeaponsSpellLevelIsItsOwnFieldAndItsChargesAreTheBonusesCount()
    {
        var db = GameDb.Read(Rom);
        var withSpell = db.Weapons.Where(w => !w.Bonuses.Spell.IsNone).ToList();

        Assert.NotEmpty(withSpell);
        Assert.All(withSpell, rom =>
        {
            var spell = Weapon(rom.Id.Value).Bonuses.Spell!;
            Assert.Equal(DbRecordIds.For(rom.Bonuses.Spell), spell.Spell);
            Assert.Equal((rom.SpellLevel, rom.Bonuses.SpellCharges), (spell.Level, spell.Charges));
        });
        Assert.All(db.Weapons.Where(w => w.Bonuses.Spell.IsNone), rom => Assert.Null(Weapon(rom.Id.Value).Bonuses.Spell));
    }

    [Fact]
    public void KeyItemsSetTheFlagsTheEngineTablesGiveThemAndHaveSlotsOfTheirOwn()
    {
        var keys = EngineTables.Read(Rom).KeyItems;
        var amaranth = Item(0x0101);

        Assert.Equal(31, keys.Count);
        Assert.Equal("Amaranth", amaranth.Name);
        Assert.Equal(new R.KeyItemFlags(RecordId.Decimal(RecordTypes.Flag, keys.Single(k => k.Item.Value == 0x0101).Held), null), amaranth.KeyItem);
        Assert.Equal(new R.ItemTraits(OwnSlot: true, MustTake: true), amaranth.Traits);

        var records = Items.Where(i => i.KeyItem is not null).ToList();
        Assert.Equal(keys.Select(k => ItemRecordId(k.Item.Value)).OrderBy(i => i.Key, StringComparer.Ordinal), records.Select(i => i.Id).OrderBy(i => i.Key, StringComparer.Ordinal));
        Assert.All(records, i => Assert.True(i.Traits!.OwnSlot));
        foreach (var key in keys)
        {
            var flags = Item(key.Item.Value).KeyItem!;
            Assert.Equal(RecordId.Decimal(RecordTypes.Flag, key.Held), flags.Held);
            Assert.Equal(key.Region == 0 ? (RecordId?)null : RecordId.Decimal(RecordTypes.Flag, key.Region), flags.Region);
        }
        Assert.Contains(records, i => i.KeyItem!.Region is not null);
    }

    [Fact]
    public void ItemsHaveSlotsOfTheirOwnWhenTheCodeListsThemAndMustBeTakenWhenTheCodeSaysSo()
    {
        var rom = Rom;
        var tables = ItemTables.Read(rom);
        var db = GameDb.Read(rom);
        var existing = db.Weapons.Select(w => w.Id).Concat(db.Armor.Select(a => a.Id)).Concat(db.Gear.Select(g => g.Id))
            .Concat(Enumerable.Range(0, ItemTables.PotionCount).Select(i => ItemId.Of(DbType.Potion, (byte)i))).ToHashSet();
        // The reserved list names 0x1106 and 0x110B, which the item DB doesn't hold.
        Assert.Equal([new ItemId(0x1106), new ItemId(0x110B)], tables.ReservedItems.Concat(tables.KeyEquipment).Where(i => !existing.Contains(i)));
        var ownSlot = tables.ReservedItems.Concat(tables.KeyEquipment).Concat(EngineTables.Read(rom).KeyItems.Select(k => k.Item))
            .Where(existing.Contains).Select(i => DbRecordIds.For(i)!.Value).Distinct().OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
        var mustTake = tables.MustTakeItems.Select(i => DbRecordIds.For(i)!.Value).OrderBy(i => i.Key, StringComparer.Ordinal).ToList();

        var traits = Weapons.Select(w => (w.Id, w.Traits)).Concat(Armor.Select(a => (a.Id, a.Traits))).Concat(Items.Select(i => (i.Id, i.Traits))).ToList();
        Assert.Equal(ownSlot, traits.Where(t => t.Traits?.OwnSlot == true).Select(t => t.Id).OrderBy(i => i.Key, StringComparer.Ordinal));
        Assert.Equal(mustTake, traits.Where(t => t.Traits?.MustTake == true).Select(t => t.Id).OrderBy(i => i.Key, StringComparer.Ordinal));
        Assert.Contains(Weapons, w => w.Traits?.OwnSlot == true);
        Assert.Contains(Armor, a => a.Traits?.OwnSlot == true);
    }

    [Fact]
    public void ALootTableHasSixDropsAndThreeWeaponChances()
    {
        var db = GameDb.Read(Rom);
        var loot = Set.All<R.LootRecord>(RecordTypes.Loot).ToList();

        Assert.Equal(db.Loot.Count, loot.Count);
        Assert.All(loot, l =>
        {
            Assert.Equal(6, l.Drops.Count);
            Assert.Equal(3, l.WeaponChances.Count);
            Assert.All(l.Drops.Skip(2), d => Assert.Equal((1, 1), (d.Min, d.Max)));
        });
        Assert.Contains(loot, l => l.Drops.Take(2).Any(d => d.Max > 1));
        Assert.Contains(loot, l => l.Drops.Any(d => d.Item is null));
        Assert.Contains(loot, l => l.Drops.Any(d => d.Item is not null));
    }

    [Fact]
    public void ALootTableKeepsTheRomsNumbers()
    {
        var db = GameDb.Read(Rom);
        var loot = Set.All<R.LootRecord>(RecordTypes.Loot).ToDictionary(l => l.Id);

        Assert.All(db.Loot, rom =>
        {
            var l = loot[DbRecordIds.For(rom.Id)!.Value];
            Assert.Equal(rom.Name, l.Name);
            Assert.Equal((rom.GoldMin, rom.GoldMax, rom.ArmorChance, rom.ShieldChance), (l.GoldMin, l.GoldMax, l.ArmorChance, l.ShieldChance));
            Assert.Equal(rom.WeaponChances.Select(c => (int)c), l.WeaponChances);
            Assert.Equal((rom.ReagentChance, rom.ReagentMin, rom.ReagentMax), (l.ReagentChance, l.ReagentMin, l.ReagentMax));
            Assert.Equal(rom.Drops.Select(d => d.Item.IsNone ? (RecordId?)null : DbRecordIds.For(d.Item)), l.Drops.Select(d => d.Item));
            Assert.Equal(rom.Drops.Select(d => (int)d.Chance), l.Drops.Select(d => d.Chance));
            Assert.Equal(rom.Drops.Select(d => (int)d.Min), l.Drops.Select(d => d.Min));
        });
    }

    [Fact]
    public void ThereIsAShopForEachOneInTheRomInOrder()
    {
        var db = GameDb.Read(Rom);
        var shops = Shops;

        Assert.Equal(db.Shops.Count, shops.Count);
        Assert.Equal(Enumerable.Range(0, db.Shops.Count).Select(i => RecordId.Decimal(RecordTypes.Shop, i)), shops.Select(s => s.Id));
        Assert.Equal(db.Shops.Select(s => DbRecordIds.For(s.Shopkeeper)!.Value), shops.Select(s => s.Shopkeeper));
    }

    [Fact]
    public void AShopStocksItsPricedSlotsWithANonzeroThirdByteAndItsExtraSlots()
    {
        var db = GameDb.Read(Rom);
        var shops = Shops;

        for (var n = 0; n < db.Shops.Count; n++)
        {
            var rom = db.Shops[n];
            var priced = rom.Stock.Count(x => !x.Item.IsNone && x.Unk2 != 0);
            var extra = rom.ExtraStock.Count(x => !x.IsNone);
            Assert.Equal(priced + extra, shops[n].Stock.Count);
            Assert.All(shops[n].Stock.Skip(priced), s => Assert.Equal(1f, s.PriceMultiplier));
        }
        Assert.Contains(shops, s => s.Stock.Count > 0);
    }

    [Fact]
    public void AShopsStockIsItemsThatExistAtMultipliersTheGameHas()
    {
        var stock = Shops.SelectMany(s => s.Stock).ToList();

        Assert.NotEmpty(stock);
        Assert.All(stock, s =>
        {
            Assert.True(Set.Contains(s.Item));
            Assert.Contains(s.Item.Type, new[] { RecordTypes.Weapon, RecordTypes.Armor, RecordTypes.Item });
            Assert.Contains(s.PriceMultiplier, new[] { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f, 5f });
        });
    }

    /// <summary>The hardcoded-content audit finds shop 17, STTA05 in Talewok, as the only source of Map 7.</summary>
    [Fact]
    public void ShopSeventeenSellsMapSeven()
    {
        var map7 = Items.Single(i => i.Name == "Map 7");
        var shops = Shops;

        Assert.NotNull(map7.KeyItem);
        Assert.Contains(shops[17].Stock, s => s.Item == map7.Id);
        Assert.Single(shops, s => s.Stock.Any(x => x.Item == map7.Id));
    }

    [Fact]
    public void ThereAreTwentyRecipesPotionsFirst()
    {
        var recipes = Recipes;

        Assert.Equal(20, recipes.Count);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => RecordId.Decimal(RecordTypes.Recipe, i)), recipes.Select(r => r.Id));
        Assert.All(recipes.Take(15), r => Assert.Equal((SkillId(0), RecordTypes.Item), (r.Skill, r.Product.Type)));
        Assert.All(recipes.Skip(15), r => Assert.Equal((SkillId(4), RecordTypes.Armor), (r.Skill, r.Product.Type)));
    }

    [Fact]
    public void TheFirstRecipeBrewsFirePotionFromSpiceAndGemstones()
    {
        var recipe = Recipes[0];

        Assert.Equal(SkillId(0), recipe.Skill);
        Assert.Equal(ItemRecordId(0x1000), recipe.Product);
        Assert.Equal([new R.Ingredient(ItemRecordId(0x0111), 1), new R.Ingredient(ItemRecordId(0x0113), 2)], recipe.Ingredients);
        Assert.Equal((1, 0, (int?)2), (recipe.Level, recipe.Step, recipe.Unk5));
    }

    [Fact]
    public void IngredientsAreInSpiceHerbGemstoneOrderWithoutZeroCounts()
    {
        var tables = ItemTables.Read(Rom);
        var recipes = Recipes;

        for (var i = 0; i < tables.PotionRecipes.Count; i++)
        {
            var p = tables.PotionRecipes[i];
            var expected = new[] { (0x0111, p.Spice), (0x0112, p.Herb), (0x0113, p.Gemstone) }.Where(x => x.Item2 > 0)
                .Select(x => new R.Ingredient(ItemRecordId(x.Item1), x.Item2));
            Assert.Equal(expected, recipes[i].Ingredients);
            Assert.Equal(ItemRecordId(0x1000 + p.Potion), recipes[i].Product);
            Assert.Equal((p.Alchemist, p.Step, (int?)p.Unk5), (recipes[i].Level, recipes[i].Step, recipes[i].Unk5));
        }
    }

    [Fact]
    public void TheMechanicsRecipesMakeArmorFromAHideOnTheBaseLevel()
    {
        var recipes = Recipes;
        var hellhound = recipes[15];
        var scorpion = recipes[19];

        Assert.Equal(SkillId(4), hellhound.Skill);
        Assert.Equal(RecordId.Hex(RecordTypes.Armor, 0x051F), hellhound.Product);
        Assert.Equal([new R.Ingredient(ItemRecordId(0x0121), 1)], hellhound.Ingredients);
        Assert.Equal((5, 7, (int?)null), (hellhound.Level, hellhound.Step, hellhound.Unk5));

        Assert.Equal(RecordId.Hex(RecordTypes.Armor, 0x061D), scorpion.Product);
        Assert.Equal([new R.Ingredient(ItemRecordId(0x0124), 1)], scorpion.Ingredients);
        Assert.Equal((3, 4), (scorpion.Level, scorpion.Step));
    }

    [Fact]
    public void TheInventoryHoldsTheRulesTheCodeHas()
    {
        var db = GameDb.Read(Rom);
        var inventory = Set.Get<R.InventoryRecord>(RecordId.Decimal(RecordTypes.Inventory, 0));

        Assert.Equal((99, 128), (inventory.StackLimit, inventory.GeneralSlots));
        Assert.Equal((ItemRecordId(0x0111), ItemRecordId(0x0112), ItemRecordId(0x0113)), (inventory.Spice, inventory.Herb, inventory.Gemstone));
        Assert.Equal(ImageId(0x35DC), inventory.DefaultIcon);
        Assert.Equal((0.05f, 0.5f), (inventory.MerchantStep, inventory.SellShare));
        Assert.Equal([RecordId.Hex(RecordTypes.Weapon, db.Weapons[32].Id.Value)], inventory.Refused);
        Assert.Equal("Cyclops Club", Weapon(db.Weapons[32].Id.Value).Name);
        Assert.Equal([new R.ItemReplacement(ItemRecordId(0x100B), ItemRecordId(0x1004))], inventory.Replaced);
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 3612), inventory.FirstPotionFlag);
        Assert.Equal(["Spice", "Herb", "Gemstone"], new[] { inventory.Spice, inventory.Herb, inventory.Gemstone }.Select(i => Set.Get<R.ItemRecord>(i).Name));
        var messages = MenuTables.Read(Rom).Messages;
        Assert.Equal(new R.InventoryMessages(messages.PotionNotNow, messages.PotionNotInTrek, messages.ScrollNoSpell), inventory.Messages);
    }

    [Fact]
    public void EverySkillHasAnIcon()
    {
        var rom = Rom;
        var db = GameDb.Read(rom);
        var skills = ItemImporter.WithIcons(ItemTables.Read(rom), CharacterImporter.BuildSkills(db, CharacterTables.Read(rom)));

        Assert.Equal(12, skills.Count);
        Assert.Equal(ImageId(0x0183), skills[0].Icon);
        Assert.All(skills, s => Assert.NotNull(s.Icon));
        Assert.Equal(skills.Select(s => s.Icon), Enumerable.Range(0, 12).Select(n => Set.Get<R.SkillRecord>(SkillId(n)).Icon));
        Assert.All(Enumerable.Range(0, 12), n => Assert.Null(Set.Get<R.SkillRecord>(RecordId.Decimal(RecordTypes.WeaponSkill, n)).Icon));
    }

    [Fact]
    public void EveryRecordTheItemRecordsNameExists()
    {
        var references = new List<(string From, RecordId To)>();
        var missingImages = new List<string>();
        void Image(string from, RecordId icon)
        {
            if (Set.FindAsset(icon, "png") is null)
                missingImages.Add($"{from} has no image {icon}");
        }
        void Bonuses(string from, R.GearBonuses? b)
        {
            if (b?.Stat is not null)
                references.Add((from, b.Stat.Stat));
            if (b?.Skill is not null)
                references.Add((from, b.Skill.Skill));
            if (b?.Spell is not null)
                references.Add((from, b.Spell.Spell));
            if (b?.Enchantment is not null)
                references.Add((from, b.Enchantment.Spell));
        }

        foreach (var w in Weapons)
        {
            Image($"{w.Id}", w.Icon);
            Bonuses($"{w.Id}", w.Bonuses);
        }
        foreach (var a in Armor)
        {
            Image($"{a.Id}", a.Icon);
            Bonuses($"{a.Id}", a.Bonuses);
        }
        foreach (var i in Items)
        {
            Image($"{i.Id}", i.Icon);
            Bonuses($"{i.Id}", i.Bonuses);
            if (i.KeyItem is { } key)
            {
                references.Add(($"{i.Id}", key.Held));
                if (key.Region is { } region)
                    references.Add(($"{i.Id}", region));
            }
        }
        foreach (var l in Set.All<R.LootRecord>(RecordTypes.Loot))
            references.AddRange(l.Drops.Where(d => d.Item is not null).Select(d => ($"{l.Id}", d.Item!.Value)));
        foreach (var s in Shops)
        {
            references.Add(($"{s.Id}", s.Shopkeeper));
            references.AddRange(s.Stock.Select(x => ($"{s.Id}", x.Item)));
        }
        foreach (var r in Recipes)
        {
            references.Add(($"{r.Id}", r.Skill));
            references.Add(($"{r.Id}", r.Product));
            references.AddRange(r.Ingredients.Select(x => ($"{r.Id}", x.Item)));
        }
        var inventory = Set.Get<R.InventoryRecord>(RecordId.Decimal(RecordTypes.Inventory, 0));
        Image("the inventory", inventory.DefaultIcon);
        references.AddRange(new[] { inventory.Spice, inventory.Herb, inventory.Gemstone, inventory.FirstPotionFlag }.Select(x => ("the inventory", x)));
        references.AddRange(inventory.Refused.Select(x => ("the inventory", x)));
        references.AddRange(inventory.Replaced.SelectMany(x => new[] { ("the inventory", x.Item), ("the inventory", x.With) }));
        foreach (var s in Enumerable.Range(0, 12).Select(n => Set.Get<R.SkillRecord>(SkillId(n))))
            Image($"{s.Id}", s.Icon!.Value);
        references.AddRange(Set.All<R.EntityRecord>(RecordTypes.Entity).Where(e => e.SoleWeapon is not null).Select(e => ($"{e.Id}", e.SoleWeapon!.Value)));

        Assert.NotEmpty(references);
        Assert.Empty(missingImages);
        Assert.Empty(references.Where(r => !Set.Contains(r.To)).Select(r => $"{r.From} names {r.To}"));
    }

    [Fact]
    public void TheCatalogSummarisesTheNewRecords()
    {
        var catalog = PluginCatalog.Build(Set);

        Assert.Equal("Healing Potion", catalog.Find(ItemRecordId(0x1004))!.Summary);
        Assert.Contains("Archmage", catalog.Find(RecordId.Hex(RecordTypes.Weapon, 0x0735))!.Summary);
        Assert.Contains(catalog.ReferencesTo(RecordId.Hex(RecordTypes.Weapon, 0x0735)), r => r.From == RecordId.Hex(RecordTypes.Entity, 0x024A));
    }
}
