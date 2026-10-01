using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>Hand-made weapons, armor, items and inventory rules for the item tests.</summary>
internal static class ItemData
{
    private static readonly RecordId Icon = RecordId.Decimal(RecordTypes.Image, 0);

    public static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    public static RecordId WeaponId(int n) => RecordId.Hex(RecordTypes.Weapon, 0x0700 + n);

    public static RecordId ArmorId(int n) => RecordId.Hex(RecordTypes.Armor, 0x0500 + n);

    public static RecordId ThingId(int n) => RecordId.Hex(RecordTypes.Item, 0x0900 + n);

    public static WeaponRecord NewWeapon(int n, WeaponClass weaponClass = WeaponClass.Sword, int strength = 0, GearBonuses? bonuses = null,
        bool isFixed = false) => new(WeaponId(n), $"Weapon {n}", Icon, 100, weaponClass, strength, 0, 5, 10, 0, 0, Element.None,
        bonuses ?? new GearBonuses(), null, isFixed);

    public static ArmorRecord NewArmor(int n, ArmorSlot slot = ArmorSlot.Body, int protection = 0, int defense = 0, int dexterity = 0,
        int stealth = 0, GearBonuses? bonuses = null) => new(ArmorId(n), $"Armor {n}", Icon, 100, slot, defense, protection, dexterity,
        stealth, 0, 0, bonuses ?? new GearBonuses());

    public static ItemRecord NewItem(int n, ItemKind kind = ItemKind.Misc, int protection = 0, int damage = 0, int strength = 0,
        int intelligence = 0, GearBonuses? bonuses = null, ItemTraits? traits = null, KeyItemFlags? key = null) => new(ThingId(n), $"Item {n}",
        Icon, 10, kind, 0, damage, protection, strength, intelligence, bonuses, traits, key);

    /// <summary>A bonus to a stat.</summary>
    public static GearBonuses Raises(Stat stat, int amount, Aspect aspect = Aspect.None) =>
        new(aspect, new StatBonus(RecordId.Decimal(RecordTypes.Stat, (int)stat), amount));

    /// <summary>A bonus to a skill.</summary>
    public static GearBonuses SkillGives(SkillId skill, int amount) => new(Skill: new SkillBonus(skill.Record, amount));

    public static GearBonuses CastsSpell(RecordId spell, int level = 1, int charges = 3) => new(Spell: new ItemSpell(spell, level, charges));

    public static GearBonuses Enchants(RecordId spell) => new(Enchantment: new ItemEnchantment(spell, 1));

    public static InventoryRecord Rules(int generalSlots = 8, RecordId[]? refused = null, ItemReplacement[]? replaced = null) => new(
        RecordId.Decimal(RecordTypes.Inventory, 0), 99, generalSlots, ThingId(900), ThingId(901), ThingId(902), Icon, 0.1f, 0.5f,
        refused ?? [], replaced ?? [], Flag(0));

    public static ItemCatalog CatalogWith(InventoryRecord rules, params object[] records) =>
        new(records.OfType<WeaponRecord>(), records.OfType<ArmorRecord>(), records.OfType<ItemRecord>(), rules);

    public static ItemCatalog Catalog(params object[] records) => CatalogWith(Rules(), records);

    public static FlagNetwork Flags() => new(Enumerable.Range(0, 20).Select(i => new FlagRecord(Flag(i))), Flag(9));

    public static ItemInstance Inst(WeaponRecord record) => new(Item.From(record));

    public static ItemInstance Inst(ArmorRecord record) => new(Item.From(record));

    public static ItemInstance Inst(ItemRecord record) => new(Item.From(record));

    public static Character Alaron(Func<EntityRecord, EntityRecord>? edit = null) =>
        Characters.Create(edit is null ? Characters.Alaron() : edit(Characters.Alaron()));

    /// <summary>Batteries or charges, which are null for an item without one.</summary>
    public static int?[] Levels(params int?[] values) => values;
}

public class ItemCatalogTests
{
    [Fact]
    public void ItemsAreWeaponsArmorAndOtherItemsByTheirIds()
    {
        var catalog = ItemData.Catalog(ItemData.NewWeapon(1), ItemData.NewArmor(1), ItemData.NewArmor(2, ArmorSlot.Shield), ItemData.NewItem(1, ItemKind.Ring));

        Assert.Equal(ItemKind.Weapon, catalog[ItemData.WeaponId(1)].Kind);
        Assert.Equal(ItemKind.Armor, catalog[ItemData.ArmorId(1)].Kind);
        Assert.Equal(ItemKind.Shield, catalog[ItemData.ArmorId(2)].Kind);
        Assert.Equal(ItemKind.Ring, catalog[ItemData.ThingId(1)].Kind);
        Assert.Equal(4, catalog.All.Count());
    }

    [Fact]
    public void AnItemTheCatalogLacksIsNullToTryAndAnErrorToIndex()
    {
        var catalog = ItemData.Catalog(ItemData.NewItem(1));

        Assert.Null(catalog.TryGet(ItemData.ThingId(2)));
        Assert.Throws<KeyNotFoundException>(() => catalog[ItemData.ThingId(2)]);
    }

    [Fact]
    public void AnItemReadsItsRequirementsAndProtectionFromWhicheverRecordItCameFrom()
    {
        var weapon = Item.From(ItemData.NewWeapon(1, strength: 12));
        var armor = Item.From(ItemData.NewArmor(1, protection: 4));
        var gear = Item.From(ItemData.NewItem(1, ItemKind.Helmet, protection: 2, damage: 3, strength: 9, intelligence: 8));

        Assert.Equal(12, weapon.RequiredStrength);
        Assert.Equal(4, armor.Protection);
        Assert.Equal((2, 3, 9, 8), (gear.Protection, gear.Damage, gear.RequiredStrength, gear.RequiredIntelligence));
        Assert.False(weapon.IsGear);
        Assert.False(armor.IsGear);
        Assert.True(gear.IsGear);
        Assert.False(Item.From(ItemData.NewItem(2, ItemKind.Potion)).IsGear);
    }

    [Fact]
    public void AStaminaBonusIsTheItemsBatteryAndAnyOtherIsNot()
    {
        var battery = Item.From(ItemData.NewItem(1, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Stamina, 12)));
        var strong = Item.From(ItemData.NewItem(2, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Strength, 12)));

        Assert.Equal(12, battery.BatteryCapacity);
        Assert.Equal(12, new ItemInstance(battery).Battery);
        Assert.Null(strong.BatteryCapacity);
        Assert.Null(new ItemInstance(strong).Battery);
    }

    [Fact]
    public void AnInstanceStartsWithItsSpellsChargesAndACopyKeepsItsOwnState()
    {
        var wand = Item.From(ItemData.NewItem(1, ItemKind.Wand, bonuses: ItemData.CastsSpell(Characters.Fireball.Id, charges: 7)));
        var instance = new ItemInstance(wand);
        Assert.Equal(7, instance.Charges);

        instance.Charges = 2;
        var copy = instance.Copy();
        instance.Charges = 0;

        Assert.Equal(2, copy.Charges);
        Assert.Equal(wand.Id, copy.Id);
    }
}

public class EquipmentTests
{
    private static Equipment Wearing(Character character, params ItemRecord[] gear)
    {
        foreach (var item in gear)
            Assert.Equal(EquipRefusal.None, character.Equipment.EquipGear(ItemData.Inst(item)));
        return character.Equipment;
    }

    private static ItemRecord Helmet(int n, GearBonuses? bonuses = null, int protection = 0, int damage = 0, int strength = 0, int intelligence = 0) =>
        ItemData.NewItem(n, ItemKind.Helmet, protection, damage, strength, intelligence, bonuses);

    [Fact]
    public void AStatBonusRaisesTheModifiedValueAndNotTheBaseAndTakingItOffUndoesIt()
    {
        var alaron = ItemData.Alaron();
        var equipment = Wearing(alaron, Helmet(1, ItemData.Raises(Stat.Strength, 3)));

        Assert.Equal(18, alaron.Stats.RawModified(Stat.Strength));
        Assert.Equal(15, alaron.Stats.RawBase(Stat.Strength));

        equipment.RemoveGear(0);
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Strength));
        Assert.Equal(15, alaron.Stats.RawBase(Stat.Strength));
    }

    [Fact]
    public void AnEnduranceBonusRaisesTheBaseTooSoItRaisesMaximumHitPoints()
    {
        var alaron = ItemData.Alaron();
        var equipment = Wearing(alaron, Helmet(1, ItemData.Raises(Stat.Endurance, 5)));

        Assert.Equal(20, alaron.Stats.RawBase(Stat.Endurance));
        Assert.Equal(20, alaron.Stats.RawModified(Stat.Endurance));
        Assert.Equal(20 + 30 + 1, alaron.MaxHp);

        equipment.RemoveGear(0);
        Assert.Equal(15, alaron.Stats.RawBase(Stat.Endurance));
        Assert.Equal(15 + 30 + 1, alaron.MaxHp);
    }

    [Fact]
    public void AStaminaBonusIsNotAppliedToTheSheetButIsTheItemsBattery()
    {
        var alaron = ItemData.Alaron();
        var amulet = ItemData.Inst(ItemData.NewItem(1, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Stamina, 10)));

        alaron.Equipment.EquipGear(amulet);

        Assert.Equal(30, alaron.Stats.RawModified(Stat.Stamina));
        Assert.Equal(30, alaron.Stats.RawBase(Stat.Stamina));
        Assert.Equal(10, amulet.Battery);
        Assert.Equal(10, amulet.Item.BatteryCapacity);
        Assert.Equal(new ItemInstance(amulet.Item).Battery, amulet.Battery);
    }

    [Fact]
    public void ANegativeEnduranceBonusThatWouldReachZeroLeavesItAtOne()
    {
        var alaron = ItemData.Alaron(e => e with { Attributes = new Attributes(18, 12, 15, 4, 15, 30) });

        Wearing(alaron, Helmet(1, ItemData.Raises(Stat.Endurance, -5)));

        Assert.Equal(1, alaron.Stats.RawModified(Stat.Endurance));
        // The base drops by 3, not 5.
        Assert.Equal(1, alaron.Stats.RawBase(Stat.Endurance));
    }

    [Fact]
    public void ALevelBonusIsBoundedAtOneOnlyWhileModifiedEnduranceIsZero()
    {
        var spent = ItemData.Alaron(e => e with { Level = 3, Attributes = new Attributes(18, 12, 15, 0, 15, 30) });
        Wearing(spent, Helmet(1, ItemData.Raises(Stat.Level, -3)));
        Assert.Equal(1, spent.Stats.RawModified(Stat.Level));

        var healthy = ItemData.Alaron(e => e with { Level = 3 });
        Wearing(healthy, Helmet(1, ItemData.Raises(Stat.Level, -4)));
        Assert.Equal(-1, healthy.Stats.RawModified(Stat.Level));
    }

    [Fact]
    public void TakingOffAnEnduranceBonusIsBoundedTheOtherWayAndTheBaseDriftsUp()
    {
        var alaron = ItemData.Alaron();
        var equipment = Wearing(alaron, Helmet(1, ItemData.Raises(Stat.Endurance, 5)));
        // Damage takes modified Endurance from 20 to 3 while the base stays at 20.
        alaron.Stats.ChangeCurrent(Stat.Endurance, -17);
        Assert.Equal(3, alaron.Stats.RawModified(Stat.Endurance));

        equipment.RemoveGear(0);

        // Only 2 comes off, leaving it at 1, so the base is 18 where it started at 15.
        Assert.Equal(1, alaron.Stats.RawModified(Stat.Endurance));
        Assert.Equal(18, alaron.Stats.RawBase(Stat.Endurance));
    }

    [Fact]
    public void ASkillBonusAppliesWhileTheBaseLevelIsAboveZeroAndTakingItOffUndoesIt()
    {
        var alaron = ItemData.Alaron();
        var equipment = Wearing(alaron, Helmet(1, ItemData.SkillGives(Skill.Warrior, 2)));

        Assert.Equal(3, alaron.Skills.RawBase(Skill.Warrior));
        Assert.Equal(5, alaron.Skills.RawModified(Skill.Warrior));

        equipment.RemoveGear(0);
        Assert.Equal(3, alaron.Skills.RawModified(Skill.Warrior));
    }

    [Fact]
    public void ASkillBonusDoesNothingWhenTheBaseLevelIsZeroEitherOnOrOff()
    {
        var alaron = ItemData.Alaron();
        var equipment = Wearing(alaron, Helmet(1, ItemData.SkillGives(Skill.Alchemist, 2)));

        Assert.Equal(0, alaron.Skills.RawBase(Skill.Alchemist));
        Assert.Equal(0, alaron.Skills.RawModified(Skill.Alchemist));

        equipment.RemoveGear(0);
        Assert.Equal(0, alaron.Skills.RawModified(Skill.Alchemist));
    }

    [Fact]
    public void ASkillBonusLeavesASkillThatCantBeLearntAlone()
    {
        var alaron = ItemData.Alaron(e => e with { Skills = new SkillLevels(Warrior: 3) });
        var equipment = Wearing(alaron, Helmet(1, ItemData.SkillGives(Skill.Thief, 2)));

        Assert.Equal(CharacterSkills.CantLearn, alaron.Skills.Base(Skill.Thief));
        Assert.Equal(0, alaron.Skills.RawModified(Skill.Thief));

        equipment.RemoveGear(0);
        Assert.Equal(0, alaron.Skills.RawModified(Skill.Thief));
    }

    [Fact]
    public void ABodyArmorAndAShieldAddTheirDexterityAndStealthAndTakingThemOffUndoesIt()
    {
        var alaron = ItemData.Alaron();
        var equipment = alaron.Equipment;

        equipment.EquipArmor(ItemData.Inst(ItemData.NewArmor(1, dexterity: -2, stealth: -1)));
        Assert.Equal(EquipRefusal.None, equipment.EquipShield(ItemData.Inst(ItemData.NewArmor(2, ArmorSlot.Shield, dexterity: -1, stealth: -2))));

        Assert.Equal(12, alaron.Stats.RawModified(Stat.Dexterity));
        Assert.Equal(15, alaron.Stats.RawBase(Stat.Dexterity));
        // Stealth is 1: the penalties take it to -2, which reads as 0.
        Assert.Equal(-2, alaron.Skills.RawModified(Skill.Stealth));
        Assert.Equal(0, alaron.Skills.Modified(Skill.Stealth));

        equipment.RemoveArmor();
        equipment.RemoveShield();
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Dexterity));
        Assert.Equal(1, alaron.Skills.RawModified(Skill.Stealth));
    }

    [Fact]
    public void ArmorsStealthAppliesWhateverTheLevelButNotToASkillThatCantBeLearnt()
    {
        var untrained = ItemData.Alaron(e => e with { Skills = new SkillLevels(Stealth: 0) });
        untrained.Equipment.EquipArmor(ItemData.Inst(ItemData.NewArmor(1, stealth: -1)));
        Assert.Equal(-1, untrained.Skills.RawModified(Skill.Stealth));
        untrained.Equipment.RemoveArmor();
        Assert.Equal(0, untrained.Skills.RawModified(Skill.Stealth));

        var unable = ItemData.Alaron(e => e with { Skills = new SkillLevels(Warrior: 3) });
        unable.Equipment.EquipArmor(ItemData.Inst(ItemData.NewArmor(1, stealth: -1)));
        Assert.Equal(0, unable.Skills.RawModified(Skill.Stealth));
    }

    [Fact]
    public void ArmorProtectionIsTheArmorsPlusTheShieldsAndShieldDefenseTheShieldsAlone()
    {
        var equipment = ItemData.Alaron().Equipment;
        equipment.EquipArmor(ItemData.Inst(ItemData.NewArmor(1, protection: 5)));
        Assert.Equal(5, equipment.ArmorProtection);
        Assert.Equal(0, equipment.ShieldDefense);

        equipment.EquipShield(ItemData.Inst(ItemData.NewArmor(2, ArmorSlot.Shield, protection: 2, defense: 3)));
        Assert.Equal(7, equipment.ArmorProtection);
        Assert.Equal(3, equipment.ShieldDefense);

        equipment.RemoveArmor();
        Assert.Equal(2, equipment.ArmorProtection);
    }

    [Fact]
    public void GearAddsItsProtectionAndDamageToTheSheetAndTakingItOffRemovesThem()
    {
        var alaron = ItemData.Alaron();
        var startingDamage = alaron.Damage;
        var equipment = Wearing(alaron, Helmet(1, protection: 3, damage: 4), Helmet(2, protection: 1));

        Assert.Equal(4, alaron.Protection);
        Assert.Equal(startingDamage + 4, alaron.Damage);

        equipment.RemoveGear(0);
        Assert.Equal(1, alaron.Protection);
        Assert.Equal(startingDamage, alaron.Damage);
    }

    [Fact]
    public void AWeaponNeedsBaseStrengthBeforeAnythingElse()
    {
        var equipment = ItemData.Alaron().Equipment;
        // The pole needs Strength 20 and Alaron has 15 and no Pole skill: Strength is checked first.
        var pole = Item.From(ItemData.NewWeapon(1, WeaponClass.Pole, strength: 20));

        Assert.Equal(EquipRefusal.NeedsStrength, equipment.CanWield(pole));
    }

    [Fact]
    public void AWeaponNeedsItsClassSkillAboveZero()
    {
        var equipment = ItemData.Alaron().Equipment;

        Assert.Equal(EquipRefusal.CantUse, equipment.CanWield(Item.From(ItemData.NewWeapon(1, WeaponClass.Pole))));
        Assert.Equal(EquipRefusal.CantUse, equipment.CanWield(Item.From(ItemData.NewWeapon(2, WeaponClass.Claw))));
        Assert.Equal(EquipRefusal.None, equipment.CanWield(Item.From(ItemData.NewWeapon(3, WeaponClass.Sword, strength: 15))));
    }

    [Fact]
    public void ACharacterWithASoleWeaponWieldsOnlyThatWeapon()
    {
        var staff = ItemData.NewWeapon(1, WeaponClass.Hafted);
        var niesen = ItemData.Alaron(e => e with { SoleWeapon = staff.Id });

        Assert.Equal(EquipRefusal.None, niesen.Equipment.CanWield(Item.From(staff)));
        Assert.Equal(EquipRefusal.CantUse, niesen.Equipment.CanWield(Item.From(ItemData.NewWeapon(2, WeaponClass.Sword))));
    }

    [Fact]
    public void ANonWeaponCantBeWielded()
    {
        var equipment = ItemData.Alaron().Equipment;

        Assert.Equal(EquipRefusal.CantUse, equipment.CanWield(Item.From(ItemData.NewItem(1))));
    }

    [Fact]
    public void WieldingAWeaponAppliesItsBonusesAndRemovingItUndoesThem()
    {
        var alaron = ItemData.Alaron();
        var sword = ItemData.Inst(ItemData.NewWeapon(1, bonuses: ItemData.Raises(Stat.Willpower, 4)));

        Assert.Equal(EquipRefusal.None, alaron.Equipment.EquipWeapon(sword));
        Assert.Equal(16, alaron.Stats.RawModified(Stat.Willpower));
        Assert.Same(sword, alaron.Equipment.RemoveWeapon());
        Assert.Equal(12, alaron.Stats.RawModified(Stat.Willpower));
    }

    [Fact]
    public void AShieldNeedsTheShieldSkillAboveZero()
    {
        var skilled = ItemData.Alaron().Equipment;
        var unskilled = ItemData.Alaron(e => e with { ShieldSkill = 0 }).Equipment;
        var unable = ItemData.Alaron(e => e with { ShieldSkill = null }).Equipment;
        var shield = ItemData.NewArmor(1, ArmorSlot.Shield);

        Assert.True(skilled.CanCarryShield);
        Assert.False(unskilled.CanCarryShield);
        Assert.False(unable.CanCarryShield);

        Assert.Equal(EquipRefusal.None, skilled.EquipShield(ItemData.Inst(shield)));
        Assert.Equal(EquipRefusal.CantUse, unskilled.EquipShield(ItemData.Inst(shield)));
        Assert.Null(unskilled.Shield);
    }

    [Fact]
    public void GearNeedsAFreeSlotThenBaseStrengthThenBaseIntelligence()
    {
        var equipment = ItemData.Alaron().Equipment;
        var weak = Item.From(Helmet(100, strength: 20, intelligence: 30));
        var dull = Item.From(Helmet(101, intelligence: 30));

        Assert.Equal(EquipRefusal.NeedsStrength, equipment.CanWear(weak));
        Assert.Equal(EquipRefusal.NeedsIntelligence, equipment.CanWear(dull));

        for (var i = 0; i < Equipment.GearSlots; i++)
            Assert.Equal(EquipRefusal.None, equipment.EquipGear(ItemData.Inst(Helmet(i))));

        Assert.Equal(EquipRefusal.NoRoom, equipment.CanWear(weak));
        Assert.Equal(EquipRefusal.NoRoom, equipment.EquipGear(ItemData.Inst(Helmet(200))));
        Assert.Equal(12, equipment.GearCount);
    }

    [Fact]
    public void GearFillsTheFirstFreeSlotAndARemovedOnesGapIsFilledNext()
    {
        var equipment = ItemData.Alaron().Equipment;
        var a = ItemData.Inst(Helmet(1));
        var b = ItemData.Inst(Helmet(2));
        var c = ItemData.Inst(Helmet(3));

        equipment.EquipGear(a);
        equipment.EquipGear(b);
        Assert.Same(a, equipment.Gear[0]);
        Assert.Same(b, equipment.Gear[1]);
        Assert.Equal(2, equipment.GearCount);

        equipment.RemoveGear(0);
        Assert.Null(equipment.Gear[0]);
        Assert.Equal(1, equipment.GearCount);

        equipment.EquipGear(c);
        Assert.Same(c, equipment.Gear[0]);
        Assert.Same(b, equipment.Gear[1]);
        Assert.Equal(2, equipment.GearCount);
    }

    [Fact]
    public void FindLooksForGearOnlyInTheFirstGearCountSlotsSoItMissesGearPastAGap()
    {
        var equipment = ItemData.Alaron().Equipment;
        var a = Helmet(1);
        var b = Helmet(2);
        equipment.EquipGear(ItemData.Inst(a));
        equipment.EquipGear(ItemData.Inst(b));
        Assert.NotNull(equipment.Find(b.Id));

        equipment.RemoveGear(0);

        // The second helmet is still worn, in slot 1, but only slot 0 is looked at now.
        Assert.NotNull(equipment.Gear[1]);
        Assert.Null(equipment.Find(b.Id));
    }

    [Fact]
    public void FindFindsTheWeaponArmorAndShieldInTheirOwnSlots()
    {
        var equipment = ItemData.Alaron().Equipment;
        var sword = ItemData.NewWeapon(1);
        var armor = ItemData.NewArmor(1);
        var shield = ItemData.NewArmor(2, ArmorSlot.Shield);
        equipment.EquipWeapon(ItemData.Inst(sword));
        equipment.EquipArmor(ItemData.Inst(armor));
        equipment.EquipShield(ItemData.Inst(shield));

        Assert.Equal(sword.Id, equipment.Find(sword.Id)?.Id);
        Assert.Equal(armor.Id, equipment.Find(armor.Id)?.Id);
        Assert.Equal(shield.Id, equipment.Find(shield.Id)?.Id);
        Assert.Null(equipment.Find(ItemData.WeaponId(9)));
    }

    // Weapon battery 5, armor 3, shield 2 and one piece of gear 4: 14 in all, with Stamina 30 behind them.
    private static Character Charged(out ItemInstance weapon, out ItemInstance armor, out ItemInstance shield, out ItemInstance gear)
    {
        var alaron = ItemData.Alaron();
        weapon = ItemData.Inst(ItemData.NewWeapon(1, bonuses: ItemData.Raises(Stat.Stamina, 5)));
        armor = ItemData.Inst(ItemData.NewArmor(1, bonuses: ItemData.Raises(Stat.Stamina, 3)));
        shield = ItemData.Inst(ItemData.NewArmor(2, ArmorSlot.Shield, bonuses: ItemData.Raises(Stat.Stamina, 2)));
        gear = ItemData.Inst(Helmet(1, ItemData.Raises(Stat.Stamina, 4)));
        alaron.Equipment.EquipWeapon(weapon);
        alaron.Equipment.EquipArmor(armor);
        alaron.Equipment.EquipShield(shield);
        alaron.Equipment.EquipGear(gear);
        return alaron;
    }

    [Fact]
    public void PayingStaminaDrawsOnTheBatteriesInOrderWeaponArmorShieldGearBeforeStamina()
    {
        var alaron = Charged(out var weapon, out var armor, out var shield, out var gear);

        Assert.True(alaron.Equipment.PayStamina(12));

        Assert.Equal(ItemData.Levels(0, 0, 0, 2), ItemData.Levels(weapon.Battery, armor.Battery, shield.Battery, gear.Battery));
        Assert.Equal(30, alaron.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ABatteryPartlyUsedKeepsTheRest()
    {
        var alaron = Charged(out var weapon, out var armor, out _, out _);

        Assert.True(alaron.Equipment.PayStamina(2));

        Assert.Equal(3, weapon.Battery);
        Assert.Equal(3, armor.Battery);
    }

    [Fact]
    public void WhatTheBatteriesCantPayComesFromStamina()
    {
        var alaron = Charged(out var weapon, out var armor, out var shield, out var gear);

        Assert.True(alaron.Equipment.PayStamina(20));

        Assert.Equal(ItemData.Levels(0, 0, 0, 0), ItemData.Levels(weapon.Battery, armor.Battery, shield.Battery, gear.Battery));
        Assert.Equal(30 - 6, alaron.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ACostBatteriesAndStaminaTogetherCantCoverIsRefusedAndChangesNothing()
    {
        var alaron = Charged(out var weapon, out var armor, out var shield, out var gear);

        Assert.False(alaron.Equipment.PayStamina(14 + 30 + 1));

        Assert.Equal(ItemData.Levels(5, 3, 2, 4), ItemData.Levels(weapon.Battery, armor.Battery, shield.Battery, gear.Battery));
        Assert.Equal(30, alaron.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ACostBatteriesAndStaminaJustCoverIsPaidInFull()
    {
        var alaron = Charged(out var weapon, out var armor, out var shield, out var gear);

        Assert.True(alaron.Equipment.PayStamina(14 + 30));

        Assert.Equal(ItemData.Levels(0, 0, 0, 0), ItemData.Levels(weapon.Battery, armor.Battery, shield.Battery, gear.Battery));
        Assert.Equal(0, alaron.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void RechargingBatteriesAddsTheTruncatedFractionOfEachCapacityUpToFull()
    {
        var alaron = Charged(out var weapon, out var armor, out _, out _);
        alaron.Equipment.PayStamina(8);
        Assert.Equal(ItemData.Levels(0, 0), ItemData.Levels(weapon.Battery, armor.Battery));

        alaron.Equipment.RechargeBatteries(0.5f);
        // Half of 5 is 2, and half of 3 is 1.
        Assert.Equal(ItemData.Levels(2, 1), ItemData.Levels(weapon.Battery, armor.Battery));

        alaron.Equipment.RechargeBatteries(1f);
        Assert.Equal(ItemData.Levels(5, 3), ItemData.Levels(weapon.Battery, armor.Battery));
    }

    [Fact]
    public void CampingRechargesTheBatteriesByTheSameFractionItHeals()
    {
        var alaron = Charged(out var weapon, out _, out _, out var gear);
        alaron.Equipment.PayStamina(9);
        Assert.Equal(ItemData.Levels(0, 4), ItemData.Levels(weapon.Battery, gear.Battery));

        alaron.CampHeal(0.5f);

        Assert.Equal(2, weapon.Battery);
        // The gear's was full already.
        Assert.Equal(4, gear.Battery);
    }

    [Fact]
    public void EnchantmentsAreThoseOfArmorShieldAndGearButNotTheWeapon()
    {
        var alaron = ItemData.Alaron();
        var equipment = alaron.Equipment;
        var spells = Enumerable.Range(0, 4).Select(i => RecordId.Hex(RecordTypes.Spell, 0x0300 + i)).ToArray();
        equipment.EquipWeapon(ItemData.Inst(ItemData.NewWeapon(1, bonuses: ItemData.Enchants(spells[0]))));
        equipment.EquipArmor(ItemData.Inst(ItemData.NewArmor(1, bonuses: ItemData.Enchants(spells[1]))));
        equipment.EquipShield(ItemData.Inst(ItemData.NewArmor(2, ArmorSlot.Shield, bonuses: ItemData.Enchants(spells[2]))));
        equipment.EquipGear(ItemData.Inst(Helmet(1, ItemData.Enchants(spells[3]))));

        Assert.Equal(spells[1..], equipment.Enchantments.Select(e => e.Spell));
    }

    [Fact]
    public void SkillBonusSumsTheWornItemsBonusesToThatSkill()
    {
        var alaron = ItemData.Alaron();
        var equipment = alaron.Equipment;
        equipment.EquipWeapon(ItemData.Inst(ItemData.NewWeapon(1, bonuses: ItemData.SkillGives(Skill.Warrior, 1))));
        equipment.EquipArmor(ItemData.Inst(ItemData.NewArmor(1, bonuses: ItemData.SkillGives(Skill.Warrior, 1))));
        equipment.EquipGear(ItemData.Inst(Helmet(1, ItemData.SkillGives(Skill.Warrior, 2))));
        equipment.EquipGear(ItemData.Inst(Helmet(2, ItemData.SkillGives(Skill.Thief, 5))));

        Assert.Equal(4, equipment.SkillBonus(Skill.Warrior));
        Assert.Equal(5, equipment.SkillBonus(Skill.Thief));
        Assert.Equal(0, equipment.SkillBonus(Skill.Healer));
    }
}

public class SkillTrainingGearTests
{
    private static Character Student()
    {
        var alaron = ItemData.Alaron();
        alaron.SpendableExp = 100_000;
        return alaron;
    }

    [Fact]
    public void RaisingASkillFromZeroToOneAppliesTheBonusesAlreadyWornInAdditionToTheLevel()
    {
        var alaron = Student();
        alaron.Equipment.EquipGear(ItemData.Inst(ItemData.NewItem(1, ItemKind.Helmet, bonuses: ItemData.SkillGives(Skill.Alchemist, 2))));
        Assert.Equal(0, alaron.Skills.RawModified(Skill.Alchemist));
        var gold = 0u;

        var quote = Training.TrainSkill(alaron, Skill.Alchemist, null, ref gold);

        Assert.True(quote.Allowed);
        Assert.Equal(1, alaron.Skills.Base(Skill.Alchemist));
        Assert.Equal(1 + 2, alaron.Skills.RawModified(Skill.Alchemist));
    }

    [Fact]
    public void RaisingASkillFromOneToTwoDoesNotApplyTheBonusAgain()
    {
        var alaron = Student();
        alaron.Equipment.EquipGear(ItemData.Inst(ItemData.NewItem(1, ItemKind.Helmet, bonuses: ItemData.SkillGives(Skill.Alchemist, 2))));
        var gold = 0u;
        Training.TrainSkill(alaron, Skill.Alchemist, null, ref gold);

        var quote = Training.TrainSkill(alaron, Skill.Alchemist, null, ref gold);

        Assert.True(quote.Allowed);
        Assert.Equal(2, alaron.Skills.Base(Skill.Alchemist));
        Assert.Equal(4, alaron.Skills.RawModified(Skill.Alchemist));
    }

    [Fact]
    public void WeaponSkillsDontGetTheGearBonusWhenTheyGoFromZeroToOne()
    {
        var alaron = Student();
        alaron.Equipment.EquipGear(ItemData.Inst(ItemData.NewItem(1, ItemKind.Helmet, bonuses: ItemData.SkillGives(WeaponClass.Pole, 2))));
        var gold = 0u;

        var quote = Training.TrainSkill(alaron, WeaponClass.Pole, null, ref gold);

        Assert.True(quote.Allowed);
        Assert.Equal(1, alaron.Skills.Base(WeaponClass.Pole));
        Assert.Equal(1, alaron.Skills.RawModified(WeaponClass.Pole));
    }
}

public class CharacterItemTests
{
    private static readonly WeaponRecord Sword = ItemData.NewWeapon(1, WeaponClass.Sword, strength: 10);
    private static readonly WeaponRecord Axe = ItemData.NewWeapon(2, WeaponClass.Hafted, strength: 10);
    private static readonly WeaponRecord Pole = ItemData.NewWeapon(3, WeaponClass.Pole);
    private static readonly ArmorRecord Mail = ItemData.NewArmor(1, protection: 5);
    private static readonly ArmorRecord Buckler = ItemData.NewArmor(2, ArmorSlot.Shield);
    private static readonly ItemCatalog Catalog = ItemData.Catalog(Sword, Axe, Pole, Mail, Buckler);

    private static Character Create(Func<EntityRecord, EntityRecord> edit) =>
        Character.Create(edit(Characters.Alaron()), Characters.Rules, _ => Characters.Fireball, null, Catalog);

    [Fact]
    public void ACharacterWearsItsEntitysArmorAndCarriesItsShieldWhenItHasTheSkill()
    {
        var alaron = Create(e => e with { Armor = Mail.Id, Shield = Buckler.Id });

        Assert.Equal(Mail.Id, alaron.Equipment.Armor?.Id);
        Assert.Equal(Buckler.Id, alaron.Equipment.Shield?.Id);
    }

    [Fact]
    public void ACharacterWithoutTheShieldSkillDoesntCarryItsShield()
    {
        var alaron = Create(e => e with { Armor = Mail.Id, Shield = Buckler.Id, ShieldSkill = 0 });

        Assert.Null(alaron.Equipment.Shield);
        Assert.Equal(Mail.Id, alaron.Equipment.Armor?.Id);
    }

    [Fact]
    public void ACharacterWieldsTheFirstWeaponItCanAndBringsTheOthersAsSpares()
    {
        var alaron = Create(e => e with { Weapons = [Pole.Id, Sword.Id, Axe.Id] });

        Assert.Equal(Sword.Id, alaron.Equipment.Weapon?.Id);
        // The pole it couldn't wield came before any it could, and is lost.
        Assert.Equal(new[] { Axe.Id }, alaron.Spares.Select(s => s.Id));
    }

    [Fact]
    public void WeaponsAfterTheOneWieldedAreSparesWhetherOrNotItCouldWieldThem()
    {
        var alaron = Create(e => e with { Weapons = [Sword.Id, Axe.Id, Pole.Id] });

        Assert.Equal(Sword.Id, alaron.Equipment.Weapon?.Id);
        Assert.Equal(new[] { Axe.Id, Pole.Id }, alaron.Spares.Select(s => s.Id));
    }

    [Fact]
    public void AWeaponACharacterCantWieldWhileItWieldsNoneIsLost()
    {
        var alaron = Create(e => e with { Weapons = [Pole.Id, null, null] });

        Assert.Null(alaron.Equipment.Weapon);
        Assert.Empty(alaron.Spares);
    }

    [Fact]
    public void JoiningThePartyMovesTheSparesToTheInventory()
    {
        var alaron = Create(e => e with { Weapons = [Sword.Id, Axe.Id, Pole.Id] });
        var party = new CharacterParty(Catalog);

        Assert.True(party.Add(alaron));

        Assert.Empty(alaron.Spares);
        Assert.Equal(1, party.Inventory!.Count(Axe.Id));
        Assert.Equal(1, party.Inventory.Count(Pole.Id));
        Assert.Equal(0, party.Inventory.Count(Sword.Id));
    }
}

public class PartyInventoryTests
{
    private static readonly ItemRecord A = ItemData.NewItem(1);
    private static readonly ItemRecord B = ItemData.NewItem(2);
    private static readonly ItemRecord C = ItemData.NewItem(3);
    private static readonly ItemRecord Herb = ItemData.NewItem(4, traits: new ItemTraits(OwnSlot: true));

    [Fact]
    public void AddingAnItemThePartyHasAddsToItsStackUpToTheLimit()
    {
        var inventory = new PartyInventory(ItemData.Catalog(A));

        inventory.Add(A.Id, 60);
        inventory.Add(A.Id, 30);
        Assert.Equal(90, inventory.Count(A.Id));

        inventory.Add(A.Id, 30);
        Assert.Equal(99, inventory.Count(A.Id));
        Assert.Single(inventory.Stacks);
    }

    [Fact]
    public void ABrandNewStackTakesTheQuantityAsGivenWithoutTheCap()
    {
        var inventory = new PartyInventory(ItemData.Catalog(A));

        Assert.True(inventory.Add(A.Id, 150));

        Assert.Equal(150, inventory.Count(A.Id));
    }

    [Fact]
    public void TakingReducesTheStackAndAZeroStackIsGone()
    {
        var inventory = new PartyInventory(ItemData.Catalog(A));
        inventory.Add(A.Id, 5);

        Assert.True(inventory.Take(A.Id, 2));
        Assert.Equal(3, inventory.Count(A.Id));

        Assert.True(inventory.Take(A.Id, 3));
        Assert.False(inventory.Has(A.Id));
        Assert.Empty(inventory.Stacks);
    }

    [Fact]
    public void TakingMoreThanThereAreTakesTheStack()
    {
        var inventory = new PartyInventory(ItemData.Catalog(A));
        inventory.Add(A.Id, 2);

        Assert.True(inventory.Take(A.Id, 10));

        Assert.False(inventory.Has(A.Id));
    }

    [Fact]
    public void TakingAnItemThePartyDoesntHaveReturnsFalse()
    {
        var inventory = new PartyInventory(ItemData.Catalog(A));

        Assert.False(inventory.Take(A.Id));
    }

    [Fact]
    public void ANewStackNeedsAGeneralSlotUnlessTheItemHasASlotOfItsOwn()
    {
        var inventory = new PartyInventory(ItemData.CatalogWith(ItemData.Rules(generalSlots: 2), A, B, C, Herb));

        Assert.True(inventory.Add(A.Id));
        Assert.True(inventory.Add(B.Id));
        Assert.Equal(2, inventory.GeneralSlotsUsed);

        Assert.False(inventory.Add(C.Id));
        Assert.False(inventory.Has(C.Id));
        Assert.True(inventory.Add(Herb.Id));
        Assert.Equal(2, inventory.GeneralSlotsUsed);
        // An item the party has already needs no new slot.
        Assert.True(inventory.Add(A.Id, 3));
        Assert.Equal(4, inventory.Count(A.Id));
    }

    [Fact]
    public void AKeyItemSetsItsRegionAndHeldFlagsWhenFirstGained()
    {
        var flags = ItemData.Flags();
        var key = ItemData.NewItem(5, ItemKind.Key, key: new KeyItemFlags(ItemData.Flag(10), ItemData.Flag(11)));
        var inventory = new PartyInventory(ItemData.Catalog(key), flags);
        Assert.False(flags.Get(ItemData.Flag(10)));

        inventory.Add(key.Id);

        Assert.True(flags.Get(ItemData.Flag(10)));
        Assert.True(flags.Get(ItemData.Flag(11)));
    }

    [Fact]
    public void AKeyItemClearsItsHeldFlagButNotItsRegionFlagWhenTheLastIsTaken()
    {
        var flags = ItemData.Flags();
        var key = ItemData.NewItem(5, ItemKind.Key, key: new KeyItemFlags(ItemData.Flag(10), ItemData.Flag(11)));
        var inventory = new PartyInventory(ItemData.Catalog(key), flags);
        inventory.Add(key.Id, 2);

        inventory.Take(key.Id);
        Assert.True(flags.Get(ItemData.Flag(10)));

        inventory.Take(key.Id);
        Assert.False(flags.Get(ItemData.Flag(10)));
        Assert.True(flags.Get(ItemData.Flag(11)));
    }

    [Fact]
    public void AKeyItemWithoutARegionSetsOnlyItsHeldFlag()
    {
        var flags = ItemData.Flags();
        var key = ItemData.NewItem(5, ItemKind.Key, key: new KeyItemFlags(ItemData.Flag(10)));
        var inventory = new PartyInventory(ItemData.Catalog(key), flags);

        inventory.Add(key.Id);

        Assert.True(flags.Get(ItemData.Flag(10)));
        Assert.False(flags.Get(ItemData.Flag(11)));
    }

    [Fact]
    public void AnItemListedAsReplacedIsAddedAsItsReplacementInstead()
    {
        var aspect = ItemData.NewItem(6, ItemKind.Potion);
        var healing = ItemData.NewItem(7, ItemKind.Potion);
        var rules = ItemData.Rules(replaced: [new ItemReplacement(aspect.Id, healing.Id)]);
        var inventory = new PartyInventory(ItemData.CatalogWith(rules, aspect, healing));

        Assert.True(inventory.Add(aspect.Id, 2));

        Assert.False(inventory.Has(aspect.Id));
        Assert.Equal(2, inventory.Count(healing.Id));
    }
}

public class ItemPileTests
{
    private static readonly ItemRecord Club = ItemData.NewItem(1);
    private static readonly ItemRecord AspectPotion = ItemData.NewItem(2, ItemKind.Potion);
    private static readonly ItemRecord Healing = ItemData.NewItem(3, ItemKind.Potion);
    private static readonly ItemRecord Quest = ItemData.NewItem(4, traits: new ItemTraits(MustTake: true));
    private static readonly ItemRecord Plain = ItemData.NewItem(5);

    private static ItemPile Pile(params ItemRecord[] extra) => new(ItemData.CatalogWith(
        ItemData.Rules(refused: [Club.Id], replaced: [new ItemReplacement(AspectPotion.Id, Healing.Id)]),
        [Club, AspectPotion, Healing, Quest, Plain, .. extra]));

    [Fact]
    public void APileRefusesTheItemsTheRulesRefuse()
    {
        var pile = Pile();

        Assert.False(pile.Add(Club.Id));

        Assert.Empty(pile.Stacks);
    }

    [Fact]
    public void APileReplacesAnItemLikeTheInventoryDoes()
    {
        var pile = Pile();

        Assert.True(pile.Add(AspectPotion.Id));

        Assert.Null(pile.Find(AspectPotion.Id));
        Assert.Equal(1, pile.Find(Healing.Id)?.Quantity);
    }

    [Fact]
    public void APileHoldsAtMostSixtyFourStacks()
    {
        var many = Enumerable.Range(100, 65).Select(i => ItemData.NewItem(i)).ToArray();
        var pile = Pile(many);

        foreach (var item in many.Take(64))
            Assert.True(pile.Add(item.Id));
        Assert.Equal(64, pile.Stacks.Count);

        Assert.False(pile.Add(many[64].Id));
        // More of one it holds still fits.
        Assert.True(pile.Add(many[0].Id, 2));
        Assert.Equal(3, pile.Find(many[0].Id)?.Quantity);
    }

    [Fact]
    public void APileCapsAStackAtTheLimitAndAZeroStackIsGone()
    {
        var pile = Pile();
        pile.Add(Plain.Id, 90);
        pile.Add(Plain.Id, 20);
        Assert.Equal(99, pile.Find(Plain.Id)?.Quantity);

        Assert.True(pile.Take(Plain.Id, 99));
        Assert.Empty(pile.Stacks);
        Assert.False(pile.Take(Plain.Id));
    }

    [Fact]
    public void APileCantBeLeftWhileItHoldsAnItemThatMustBeTaken()
    {
        var pile = Pile();
        pile.Add(Plain.Id);
        Assert.True(pile.CanLeave);

        pile.Add(Quest.Id);
        Assert.False(pile.CanLeave);

        pile.Take(Quest.Id);
        Assert.True(pile.CanLeave);
    }
}

public class PartyItemTests
{
    private static readonly WeaponRecord Sword = ItemData.NewWeapon(1, WeaponClass.Sword, strength: 10);
    private static readonly WeaponRecord Blade = ItemData.NewWeapon(2, WeaponClass.Sword, strength: 10);
    private static readonly WeaponRecord Staff = ItemData.NewWeapon(3, WeaponClass.Hafted, isFixed: true);
    private static readonly ArmorRecord Mail = ItemData.NewArmor(1, protection: 5, dexterity: -2);
    private static readonly ArmorRecord Plate = ItemData.NewArmor(2, protection: 9);
    private static readonly ArmorRecord Buckler = ItemData.NewArmor(3, ArmorSlot.Shield, defense: 2);
    private static readonly ArmorRecord Tower = ItemData.NewArmor(4, ArmorSlot.Shield, defense: 6);
    private static readonly ItemRecord Cap = ItemData.NewItem(1, ItemKind.Helmet, protection: 1, bonuses: ItemData.Raises(Stat.Strength, 2));
    private static readonly ItemRecord Helm = ItemData.NewItem(2, ItemKind.Helmet, protection: 3);
    private static readonly ItemRecord Ring1 = ItemData.NewItem(3, ItemKind.Ring);
    private static readonly ItemRecord Ring2 = ItemData.NewItem(4, ItemKind.Ring);
    private static readonly ItemRecord Ring3 = ItemData.NewItem(5, ItemKind.Ring);
    private static readonly RecordId Spark = RecordId.Hex(RecordTypes.Spell, 0x0401);
    private static readonly ItemRecord[] Scrolls = [.. Enumerable.Range(10, 4).Select(i =>
        ItemData.NewItem(i, ItemKind.Scroll, bonuses: ItemData.CastsSpell(Spark)))];
    private static readonly ItemRecord Blank = ItemData.NewItem(20, ItemKind.Scroll);
    private static readonly ItemRecord Potion = ItemData.NewItem(21, ItemKind.Potion);
    private static readonly ItemRecord Hide = ItemData.NewItem(22);
    private static readonly ItemRecord LunarGem = ItemData.NewItem(23, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Willpower, 1, Aspect.Lunar));
    private static readonly ItemRecord SolarGem = ItemData.NewItem(24, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Willpower, 1, Aspect.Solar));
    private static readonly ItemRecord Heavy = ItemData.NewItem(25, ItemKind.Belt, strength: 20);
    private static readonly ItemRecord Charm = ItemData.NewItem(26, ItemKind.Amulet,
        bonuses: ItemData.Raises(Stat.Stamina, 10) with { Spell = new ItemSpell(Spark, 1, 5) });
    private static readonly ItemRecord Filler = ItemData.NewItem(27);

    private static readonly object[] All =
    [
        Sword, Blade, Staff, Mail, Plate, Buckler, Tower, Cap, Helm, Ring1, Ring2, Ring3, .. Scrolls, Blank, Potion, Hide, LunarGem,
        SolarGem, Heavy, Charm, Filler,
    ];

    private static (CharacterParty Party, Character Alaron) Setup(int generalSlots = 20, Func<EntityRecord, EntityRecord>? edit = null)
    {
        var party = new CharacterParty(ItemData.CatalogWith(ItemData.Rules(generalSlots), All));
        var alaron = ItemData.Alaron(edit);
        party.Add(alaron);
        return (party, alaron);
    }

    private static ItemInstance Worn(Character character, int slot = 0) => character.Equipment.Gear[slot]!;

    [Fact]
    public void EquippingSomethingNotInTheInventoryIsRefused()
    {
        var (party, alaron) = Setup();

        Assert.Equal(EquipRefusal.CantUse, party.Equip(alaron, Helm.Id));
    }

    [Fact]
    public void AnItemOfAnotherAspectCantBeEquippedButNeutralAndOwnAspectCan()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(LunarGem.Id);
        party.Inventory.Add(SolarGem.Id);
        party.Inventory.Add(Cap.Id);

        Assert.Equal(EquipRefusal.WrongAspect, party.Equip(alaron, LunarGem.Id));
        Assert.True(party.Inventory.Has(LunarGem.Id));
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, SolarGem.Id));
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Cap.Id));
    }

    [Fact]
    public void EquippingArmorTakesOneFromTheStackAndPutsTheOldArmorBack()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Mail.Id);

        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Mail.Id));
        Assert.Equal(Mail.Id, alaron.Equipment.Armor?.Id);
        Assert.False(party.Inventory.Has(Mail.Id));
        Assert.Equal(13, alaron.Stats.RawModified(Stat.Dexterity));

        party.Inventory.Add(Plate.Id);
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Plate.Id));
        Assert.Equal(Plate.Id, alaron.Equipment.Armor?.Id);
        Assert.Equal(1, party.Inventory.Count(Mail.Id));
        Assert.False(party.Inventory.Has(Plate.Id));
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Dexterity));
    }

    [Fact]
    public void EquippingAShieldSwapsLikeArmorButNeedsTheShieldSkill()
    {
        var (party, alaron) = Setup();
        var unskilled = ItemData.Alaron(e => e with { ShieldSkill = 0 });
        party.Add(unskilled);
        party.Inventory!.Add(Buckler.Id, 2);
        party.Inventory.Add(Tower.Id);

        Assert.Equal(EquipRefusal.CantUse, party.Equip(unskilled, Buckler.Id));
        Assert.Equal(2, party.Inventory.Count(Buckler.Id));

        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Buckler.Id));
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Tower.Id));
        Assert.Equal(Tower.Id, alaron.Equipment.Shield?.Id);
        Assert.Equal(2, party.Inventory.Count(Buckler.Id));
    }

    [Fact]
    public void EquippingAWeaponSwapsItWithTheOneWielded()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Sword.Id);
        party.Inventory.Add(Blade.Id);

        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Sword.Id));
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Blade.Id));

        Assert.Equal(Blade.Id, alaron.Equipment.Weapon?.Id);
        Assert.Equal(1, party.Inventory.Count(Sword.Id));
        Assert.False(party.Inventory.Has(Blade.Id));
    }

    [Fact]
    public void AWeaponTheCharacterCantWieldIsRefusedWithTheReason()
    {
        var (party, weak) = Setup(edit: e => e with { Attributes = new Attributes(18, 12, 15, 15, 5, 30) });
        party.Inventory!.Add(Sword.Id);

        Assert.Equal(EquipRefusal.NeedsStrength, party.Equip(weak, Sword.Id));
        Assert.True(party.Inventory.Has(Sword.Id));
        Assert.Null(weak.Equipment.Weapon);
    }

    [Fact]
    public void AFixedWeaponCantBeReplacedAndNothingChanges()
    {
        var (party, alaron) = Setup();
        alaron.Equipment.EquipWeapon(ItemData.Inst(Staff));
        party.Inventory!.Add(Sword.Id);

        Assert.Equal(EquipRefusal.Fixed, party.Equip(alaron, Sword.Id));

        Assert.Equal(Staff.Id, alaron.Equipment.Weapon?.Id);
        Assert.Equal(1, party.Inventory.Count(Sword.Id));
    }

    [Fact]
    public void GearOfAKindAlreadyWornReplacesItAndTheOldGoesBackToTheInventory()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Cap.Id);
        party.Inventory.Add(Helm.Id);
        party.Equip(alaron, Cap.Id);
        Assert.Equal(17, alaron.Stats.RawModified(Stat.Strength));

        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Helm.Id));

        Assert.Equal(1, alaron.Equipment.GearCount);
        Assert.Equal(Helm.Id, Worn(alaron).Id);
        Assert.Equal(1, party.Inventory.Count(Cap.Id));
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Strength));
        Assert.Equal(3, alaron.Protection);
    }

    [Fact]
    public void GearOfADifferentKindIsWornAlongside()
    {
        var (party, alaron) = Setup(edit: e => e with { Attributes = new Attributes(18, 12, 15, 15, 25, 30) });
        party.Inventory!.Add(Cap.Id);
        party.Inventory.Add(Heavy.Id);

        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Cap.Id));
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Heavy.Id));

        Assert.Equal(2, alaron.Equipment.GearCount);
    }

    [Fact]
    public void GearTheCharacterIsTooWeakForIsRefusedAndStaysInTheInventory()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Heavy.Id);

        Assert.Equal(EquipRefusal.NeedsStrength, party.Equip(alaron, Heavy.Id));

        Assert.True(party.Inventory.Has(Heavy.Id));
        Assert.Equal(0, alaron.Equipment.GearCount);
    }

    [Fact]
    public void AMemberWearsTwoRingsAndTheThirdIsRefusedNotSwapped()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Ring1.Id);
        party.Inventory.Add(Ring2.Id);
        party.Inventory.Add(Ring3.Id);

        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Ring1.Id));
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Ring2.Id));
        Assert.Equal(EquipRefusal.NoRoom, party.Equip(alaron, Ring3.Id));

        Assert.Equal(2, alaron.Equipment.GearCount);
        Assert.True(party.Inventory.Has(Ring3.Id));
        Assert.False(party.Inventory.Has(Ring1.Id));
    }

    [Fact]
    public void AMemberWearsThreeScrollsWithASpellAndTheFourthIsRefused()
    {
        var (party, alaron) = Setup();
        foreach (var scroll in Scrolls)
            party.Inventory!.Add(scroll.Id);

        Assert.All(Scrolls.Take(3), s => Assert.Equal(EquipRefusal.None, party.Equip(alaron, s.Id)));
        Assert.Equal(EquipRefusal.NoRoom, party.Equip(alaron, Scrolls[3].Id));

        Assert.Equal(3, alaron.Equipment.GearCount);
        Assert.True(party.Inventory!.Has(Scrolls[3].Id));
    }

    [Fact]
    public void AScrollWithoutASpellPotionsAndOtherItemsCantBeEquipped()
    {
        var (party, alaron) = Setup();
        foreach (var item in new[] { Blank, Potion, Hide })
            party.Inventory!.Add(item.Id);

        Assert.Equal(EquipRefusal.CantUse, party.Equip(alaron, Blank.Id));
        Assert.Equal(EquipRefusal.CantUse, party.Equip(alaron, Potion.Id));
        Assert.Equal(EquipRefusal.CantUse, party.Equip(alaron, Hide.Id));

        Assert.Equal(3, party.Inventory!.Stacks.Count);
        Assert.Equal(0, alaron.Equipment.GearCount);
    }

    [Fact]
    public void WhenTheInventoryCantTakeTheOldItemBackTheNewOneGoesBackAndTheOldStaysWorn()
    {
        var (party, alaron) = Setup(generalSlots: 2);
        party.Inventory!.Add(Cap.Id);
        party.Equip(alaron, Cap.Id);
        // Two general slots, both used, with the helm's stack staying after one is taken from it.
        party.Inventory.Add(Helm.Id, 2);
        party.Inventory.Add(Filler.Id);

        Assert.Equal(EquipRefusal.NoRoom, party.Equip(alaron, Helm.Id));

        Assert.Equal(Cap.Id, Worn(alaron).Id);
        Assert.Equal(2, party.Inventory.Count(Helm.Id));
        Assert.False(party.Inventory.Has(Cap.Id));
    }

    [Fact]
    public void TheEquippedItemTakesTheStacksBatteryAndChargesAsACopy()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Charm.Id, 2);
        var stack = party.Inventory.Find(Charm.Id)!;
        stack.Instance.Battery = 4;
        stack.Instance.Charges = 2;

        party.Equip(alaron, Charm.Id);
        var worn = Worn(alaron);
        Assert.Equal(4, worn.Battery);
        Assert.Equal(2, worn.Charges);

        worn.Battery = 1;
        Assert.Equal(4, stack.Instance.Battery);
    }

    [Fact]
    public void UnequippingPutsTheItemBackWithItsBatteryAndChargesIntoANewStack()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Charm.Id);
        party.Equip(alaron, Charm.Id);
        var worn = Worn(alaron);
        worn.Battery = 1;
        worn.Charges = 0;

        Assert.True(party.Unequip(alaron, worn));

        Assert.Equal(0, alaron.Equipment.GearCount);
        var stack = party.Inventory.Find(Charm.Id)!;
        Assert.Equal(1, stack.Quantity);
        Assert.Equal(1, stack.Instance.Battery);
        Assert.Equal(0, stack.Instance.Charges);
    }

    [Fact]
    public void UnequippingIntoAStackThatAlreadyExistsMakesTheStackTakeTheItemsBatteryAndCharges()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Charm.Id, 2);
        party.Equip(alaron, Charm.Id);
        var worn = Worn(alaron);
        worn.Battery = 1;
        worn.Charges = 0;

        Assert.True(party.Unequip(alaron, worn));

        var stack = party.Inventory.Find(Charm.Id)!;
        Assert.Equal(2, stack.Quantity);
        Assert.Equal(1, stack.Instance.Battery);
        Assert.Equal(0, stack.Instance.Charges);
    }

    [Fact]
    public void UnequippingAWeaponArmorAndShieldReturnsThemToTheInventoryAndUndoesTheirEffects()
    {
        var (party, alaron) = Setup();
        foreach (var id in new[] { Sword.Id, Mail.Id, Buckler.Id })
        {
            party.Inventory!.Add(id);
            Assert.Equal(EquipRefusal.None, party.Equip(alaron, id));
        }
        Assert.Equal(13, alaron.Stats.RawModified(Stat.Dexterity));

        Assert.True(party.Unequip(alaron, alaron.Equipment.Weapon!));
        Assert.True(party.Unequip(alaron, alaron.Equipment.Armor!));
        Assert.True(party.Unequip(alaron, alaron.Equipment.Shield!));

        Assert.Empty(alaron.Equipment.All);
        Assert.Equal(3, party.Inventory!.Stacks.Count);
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Dexterity));
    }

    [Fact]
    public void AFixedWeaponCantBeUnequipped()
    {
        var (party, alaron) = Setup();
        var staff = ItemData.Inst(Staff);
        alaron.Equipment.EquipWeapon(staff);

        Assert.False(party.Unequip(alaron, staff));

        Assert.Same(staff, alaron.Equipment.Weapon);
        Assert.False(party.Inventory!.Has(Staff.Id));
    }

    [Fact]
    public void ThePartyHasWhatItCarriesAndWhatAMemberWears()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Hide.Id);
        alaron.Equipment.EquipGear(ItemData.Inst(Cap));
        alaron.Equipment.EquipArmor(ItemData.Inst(Mail));

        Assert.True(party.HasItem(Hide.Id));
        Assert.True(party.HasItem(Cap.Id));
        Assert.True(party.HasItem(Mail.Id));
        Assert.False(party.HasItem(Plate.Id));
    }

    [Fact]
    public void TakingAnItemTakesFromTheInventoryBeforeAnythingWorn()
    {
        var (party, alaron) = Setup();
        party.Inventory!.Add(Cap.Id);
        alaron.Equipment.EquipGear(ItemData.Inst(Cap));

        Assert.True(party.TakeItem(Cap.Id));

        Assert.False(party.Inventory.Has(Cap.Id));
        Assert.Equal(1, alaron.Equipment.GearCount);
        Assert.Equal(17, alaron.Stats.RawModified(Stat.Strength));
    }

    [Fact]
    public void TakingAnItemOnlyWornDestroysItFromTheFirstMemberWearingItAndUndoesItsEffects()
    {
        var (party, alaron) = Setup();
        var second = ItemData.Alaron(e => e with { Id = RecordId.Hex(RecordTypes.Entity, 0x0240) });
        party.Add(second);
        alaron.Equipment.EquipGear(ItemData.Inst(Cap));
        second.Equipment.EquipGear(ItemData.Inst(Cap));

        Assert.True(party.TakeItem(Cap.Id));

        Assert.Equal(0, alaron.Equipment.GearCount);
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Strength));
        Assert.Equal(0, alaron.Protection);
        Assert.Equal(1, second.Equipment.GearCount);
        Assert.Equal(17, second.Stats.RawModified(Stat.Strength));
        Assert.False(party.Inventory!.Has(Cap.Id));
    }

    [Fact]
    public void TakingAWornWeaponArmorOrShieldRemovesItAndAnItemNobodyHasReturnsFalse()
    {
        var (party, alaron) = Setup();
        alaron.Equipment.EquipWeapon(ItemData.Inst(Sword));
        alaron.Equipment.EquipArmor(ItemData.Inst(Mail));
        alaron.Equipment.EquipShield(ItemData.Inst(Buckler));

        Assert.True(party.TakeItem(Sword.Id));
        Assert.True(party.TakeItem(Mail.Id));
        Assert.True(party.TakeItem(Buckler.Id));

        Assert.Empty(alaron.Equipment.All);
        Assert.Equal(15, alaron.Stats.RawModified(Stat.Dexterity));
        Assert.False(party.TakeItem(Sword.Id));
    }

    private static readonly SpellRecord Elemental1 = Characters.Spell(0x0401, MagicSchool.Elemental, wizard: 1, cost: 100);
    private static readonly SpellRecord Hard = Characters.Spell(0x0402, MagicSchool.Elemental, wizard: 5, cost: 100);
    private static readonly SpellRecord Unschooled = Characters.Spell(0x0403, MagicSchool.None, wizard: 1, cost: 100);

    private static SpellRecord Lookup(RecordId id) => new[] { Elemental1, Hard, Unschooled, Characters.Fireball }.Single(s => s.Id == id);

    private static (CharacterParty Party, RecordId Scroll) WithScroll(SpellRecord spell, int quantity = 1)
    {
        var scroll = ItemData.NewItem(50, ItemKind.Scroll, bonuses: ItemData.CastsSpell(spell.Id));
        var party = new CharacterParty(ItemData.Catalog(scroll));
        party.Inventory!.Add(scroll.Id, quantity);
        return (party, scroll.Id);
    }

    [Fact]
    public void ReadingAScrollLearnsItsSpellAtLevelOneAndUsesUpOneScroll()
    {
        var (party, scroll) = WithScroll(Elemental1, quantity: 2);
        var alaron = ItemData.Alaron();

        Assert.Equal(ScrollResult.Learned, party.ReadScroll(alaron, scroll, Lookup));

        Assert.Equal(1, alaron.FindSpell(Elemental1.Id)?.Level);
        Assert.Equal(1, party.Inventory!.Count(scroll));
    }

    [Fact]
    public void OnlyACharacterWhoCanLearnWizardCanReadScrollsAndThatIsCheckedFirst()
    {
        var (party, scroll) = WithScroll(Elemental1);
        var layman = ItemData.Alaron(e => e with { Skills = new SkillLevels(Warrior: 3) });

        Assert.Equal(ScrollResult.NotAWizard, party.ReadScroll(layman, scroll, Lookup));
        Assert.Equal(ScrollResult.NotAWizard, party.ReadScroll(layman, ItemData.ThingId(99), Lookup));
        Assert.Equal(1, party.Inventory!.Count(scroll));
    }

    [Fact]
    public void AScrollWithoutASpellOrOneNotCarriedHasNoSpell()
    {
        var blank = ItemData.NewItem(51, ItemKind.Scroll);
        var party = new CharacterParty(ItemData.Catalog(blank, ItemData.NewItem(50, ItemKind.Scroll, bonuses: ItemData.CastsSpell(Elemental1.Id))));
        party.Inventory!.Add(blank.Id);
        var alaron = ItemData.Alaron();

        Assert.Equal(ScrollResult.NoSpell, party.ReadScroll(alaron, blank.Id, Lookup));
        Assert.Equal(ScrollResult.NoSpell, party.ReadScroll(alaron, ItemData.ThingId(50), Lookup));
        Assert.Equal(1, party.Inventory.Count(blank.Id));
    }

    [Fact]
    public void ASpellAlreadyKnownIsntLearntAgainOrTheScrollUsed()
    {
        var (party, scroll) = WithScroll(Characters.Fireball);
        var alaron = Characters.Create(Characters.Alaron(spells: [new KnownSpell(Characters.Fireball.Id, 2)]));

        Assert.Equal(ScrollResult.AlreadyKnown, party.ReadScroll(alaron, scroll, Lookup));

        Assert.Equal(2, alaron.FindSpell(Characters.Fireball.Id)?.Level);
        Assert.Equal(1, party.Inventory!.Count(scroll));
    }

    [Fact]
    public void ASpellNeedsBaseWizardAtLeastItsOwnEvenBeforeTheSchoolIsChecked()
    {
        var (party, scroll) = WithScroll(Hard);
        var naming = ItemData.Alaron(e => e with { School = MagicSchool.Naming });

        Assert.Equal(ScrollResult.NeedsWizard, party.ReadScroll(naming, scroll, Lookup));
        Assert.Null(naming.FindSpell(Hard.Id));
    }

    [Fact]
    public void ASpellOfAnotherSchoolIsRefusedUnlessTheCharacterIsChaosOrTheSpellIsUnschooled()
    {
        var naming = ItemData.Alaron(e => e with { School = MagicSchool.Naming });
        var (party, scroll) = WithScroll(Elemental1, quantity: 2);
        Assert.Equal(ScrollResult.WrongSchool, party.ReadScroll(naming, scroll, Lookup));
        Assert.Equal(2, party.Inventory!.Count(scroll));

        var chaos = ItemData.Alaron();
        Assert.Equal(ScrollResult.Learned, party.ReadScroll(chaos, scroll, Lookup));

        var elemental = ItemData.Alaron(e => e with { School = MagicSchool.Elemental });
        Assert.Equal(ScrollResult.Learned, party.ReadScroll(elemental, scroll, Lookup));

        var (unschooledParty, unschooledScroll) = WithScroll(Unschooled);
        Assert.Equal(ScrollResult.Learned, unschooledParty.ReadScroll(naming, unschooledScroll, Lookup));
    }
}
