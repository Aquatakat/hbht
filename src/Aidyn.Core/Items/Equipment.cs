using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>Why a character can't equip an item, or <see cref="None"/>.</summary>
/// <remarks>The original shows one message for <see cref="CantUse"/> and <see cref="NoRoom"/> (error 1).</remarks>
public enum EquipRefusal
{
    None,
    /// <summary>
    /// Not something worn, a weapon whose skill is at 0, a shield without the shield skill, Niesen and
    /// any weapon but his staff, a scroll without a spell, or none in the inventory.
    /// </summary>
    CantUse,
    /// <summary>Its aspect isn't the character's (error 2).</summary>
    WrongAspect,
    /// <summary>The character's base Strength is below what it needs (error 5).</summary>
    NeedsStrength,
    /// <summary>The character's base Intelligence is below what it needs (error 6).</summary>
    NeedsIntelligence,
    /// <summary>
    /// All 12 gear slots are full, two rings or three scrolls are worn already, or the inventory can't
    /// take back what it replaces.
    /// </summary>
    NoRoom,
    /// <summary>What it would replace can't be taken off: the Archmage Staff.</summary>
    Fixed,
}

/// <summary>
/// What a character has equipped (<c>CharSheet</c>'s weapon, armor and <c>CharGear</c>): a weapon,
/// body armor, a shield, and 12 slots of worn gear. Equipping applies what each does to the sheet, and
/// taking it off undoes it (<c>Entity::ApplyEquipment</c>, <c>FUN_80078874</c>).
/// </summary>
/// <remarks>
/// <para>
/// A stat bonus changes the modified value, or for Endurance the base value too, so it raises maximum
/// hit points. A bonus that would take Endurance to 0 or below leaves it at 1, as does one to the Level
/// while Endurance is at 0; taking one off does the same the other way, so the base value can drift
/// (<c>FUN_8007b6bc</c>, <c>FUN_8007b760</c>). A Stamina bonus isn't applied: it's the item's battery.
/// </para>
/// <para>
/// A skill bonus applies only while the character's base level in it is above 0; raising a skill from 0
/// to 1 at a trainer applies the bonuses already worn (<see cref="SkillBonus"/>). Armor's Dexterity
/// and Stealth apply whatever the levels.
/// </para>
/// <para>
/// Enchantments are on while armor or gear is worn; a weapon's isn't (it's cast as it strikes). Applying
/// them is combat's (2.5), which reads <see cref="Enchantments"/>.
/// </para>
/// </remarks>
public sealed class Equipment
{
    /// <summary>How many pieces of gear a character can wear (<c>GEARTOTAL</c>).</summary>
    public const int GearSlots = 12;

    /// <summary>How many rings and scrolls can be worn (<c>itemtype_ring</c>, <c>itemtype_scroll</c>).</summary>
    public const int Rings = 2, Scrolls = 3;

    private readonly Character _owner;
    private readonly ItemInstance?[] _gear = new ItemInstance?[GearSlots];

    internal Equipment(Character owner) => _owner = owner;

    public ItemInstance? Weapon { get; private set; }

    /// <summary>Body armor.</summary>
    public ItemInstance? Armor { get; private set; }

    public ItemInstance? Shield { get; private set; }

    /// <summary>The 12 gear slots. One taken off leaves a gap, which the next piece fills.</summary>
    public IReadOnlyList<ItemInstance?> Gear => _gear;

    /// <summary>
    /// How many pieces of gear are worn (<c>CharGear::usedItems</c>). Some of the original's loops stop
    /// at this many slots, so they miss gear past a gap (<see cref="Find"/>).
    /// </summary>
    public int GearCount { get; private set; }

    /// <summary>The weapon, armor, shield and gear, in that order.</summary>
    public IEnumerable<ItemInstance> All =>
        new[] { Weapon, Armor, Shield }.Concat(_gear).OfType<ItemInstance>();

    /// <summary>Body armor's and the shield's protection (<c>Entity::GetArmorProtect</c>). Gear's is in <see cref="Character.Protection"/>.</summary>
    public int ArmorProtection => (Armor?.Item.Armor?.Protection ?? 0) + (Shield?.Item.Armor?.Protection ?? 0);

    /// <summary>The shield's defense (<c>Entity::GetShieldDefence</c>), which combat reads (2.5).</summary>
    public int ShieldDefense => Shield?.Item.Armor?.Defense ?? 0;

    /// <summary>The spells on the character while armor and gear are worn (<c>Entity::ApplyEquipEffect</c>).</summary>
    public IEnumerable<ItemEnchantment> Enchantments =>
        new[] { Armor, Shield }.Concat(_gear).OfType<ItemInstance>().Select(i => i.Item.Bonuses.Enchantment).OfType<ItemEnchantment>();

    /// <summary>The resistances what's worn gives, which combat reads (2.5).</summary>
    public IEnumerable<Resistance> Resistances => All.Select(i => i.Item.Bonuses.Resistance).OfType<Resistance>();

    /// <summary>
    /// The equipped item with this ID, or null (<c>Entity::HasItemEquipped</c>). Weapons, armor and
    /// shields are looked for in their own slots, and gear only in the first <see cref="GearCount"/> slots.
    /// </summary>
    public ItemInstance? Find(RecordId id)
    {
        var slot = id.Type switch
        {
            RecordTypes.Weapon => Weapon,
            RecordTypes.Armor => Armor?.Id == id ? Armor : Shield,
            _ => null,
        };
        if (id.Type is RecordTypes.Weapon or RecordTypes.Armor)
            return slot?.Id == id ? slot : null;
        return _gear.Take(GearCount).FirstOrDefault(g => g?.Id == id);
    }

    /// <summary>
    /// Whether the character can wield a weapon (<c>Entity::canEquipWeapon</c>): base Strength at least
    /// its need, its weapon skill above 0, and for an entity with a sole weapon, that weapon.
    /// </summary>
    public EquipRefusal CanWield(Item weapon)
    {
        if (weapon.Weapon is not { } record)
            return EquipRefusal.CantUse;
        if (_owner.Stats.Base(Stat.Strength) < record.RequiredStrength)
            return EquipRefusal.NeedsStrength;
        if (_owner.Skills.Modified(record.Class) == 0)
            return EquipRefusal.CantUse;
        return _owner.SoleWeapon is { } sole && sole != weapon.Id ? EquipRefusal.CantUse : EquipRefusal.None;
    }

    /// <summary>Whether the character can carry a shield: its modified shield skill above 0 (<c>Entity::NoSheildSkill</c>).</summary>
    public bool CanCarryShield => _owner.Skills.Modified(SkillId.Shield) != 0;

    /// <summary>
    /// Whether the character can wear a piece of gear (<c>Entity::GearMinStatCheck</c>): a free slot,
    /// then base Strength, then base Intelligence.
    /// </summary>
    public EquipRefusal CanWear(Item gear)
    {
        if (GearCount == GearSlots)
            return EquipRefusal.NoRoom;
        if (_owner.Stats.Base(Stat.Strength) < gear.RequiredStrength)
            return EquipRefusal.NeedsStrength;
        return _owner.Stats.Base(Stat.Intelligence) < gear.RequiredIntelligence ? EquipRefusal.NeedsIntelligence : EquipRefusal.None;
    }

    /// <summary>Wields a weapon if <see cref="CanWield"/> allows (<c>Entity::EquipWeapon</c>). The old one must be taken off first.</summary>
    public EquipRefusal EquipWeapon(ItemInstance weapon)
    {
        var refusal = CanWield(weapon.Item);
        if (refusal != EquipRefusal.None)
            return refusal;
        Weapon = weapon;
        Apply(weapon);
        return EquipRefusal.None;
    }

    /// <summary>Wears body armor (<c>Entity::EquipArmor</c>), which anyone can. The old must be taken off first.</summary>
    public void EquipArmor(ItemInstance armor)
    {
        Armor = armor;
        ApplyArmor(armor, 1);
    }

    /// <summary>Carries a shield if <see cref="CanCarryShield"/> (<c>Entity::EquipSheild</c>). The old must be taken off first.</summary>
    public EquipRefusal EquipShield(ItemInstance shield)
    {
        if (!CanCarryShield)
            return EquipRefusal.CantUse;
        Shield = shield;
        ApplyArmor(shield, 1);
        return EquipRefusal.None;
    }

    /// <summary>
    /// Wears a piece of gear in the first free slot if <see cref="CanWear"/> allows, adding its
    /// protection and damage to the sheet's (<c>Entity::EquipGear</c>).
    /// </summary>
    public EquipRefusal EquipGear(ItemInstance gear)
    {
        var refusal = CanWear(gear.Item);
        if (refusal != EquipRefusal.None)
            return refusal;
        _gear[Array.IndexOf(_gear, null)] = gear;
        GearCount++;
        Apply(gear);
        _owner.Protection = (byte)(_owner.Protection + gear.Item.Protection);
        _owner.Damage = (byte)(_owner.Damage + gear.Item.Damage);
        return EquipRefusal.None;
    }

    /// <summary>Takes the weapon off, undoing what it did (<c>Entity::UnequipWeapons</c>), and returns it.</summary>
    public ItemInstance? RemoveWeapon()
    {
        if (Weapon is not { } weapon)
            return null;
        Remove(weapon);
        Weapon = null;
        return weapon;
    }

    /// <summary>Takes the body armor off (<c>Entity::RemoveArmor</c>).</summary>
    public ItemInstance? RemoveArmor()
    {
        if (Armor is not { } armor)
            return null;
        Remove(armor);
        ApplyArmor(armor, -1);
        Armor = null;
        return armor;
    }

    /// <summary>Puts the shield away (<c>Entity::RemoveShield</c>).</summary>
    public ItemInstance? RemoveShield()
    {
        if (Shield is not { } shield)
            return null;
        Remove(shield);
        ApplyArmor(shield, -1);
        Shield = null;
        return shield;
    }

    /// <summary>Takes off the gear in a slot, and its protection and damage (<c>Entity::UnequipGear</c>).</summary>
    public ItemInstance? RemoveGear(int slot)
    {
        if (_gear[slot] is not { } gear)
            return null;
        Remove(gear);
        _owner.Protection = (byte)(_owner.Protection - gear.Item.Protection);
        _owner.Damage = (byte)(_owner.Damage - gear.Item.Damage);
        _gear[slot] = null;
        GearCount--;
        return gear;
    }

    /// <summary>
    /// Puts an item in a slot without applying it, for a sheet whose values already count it, such as a
    /// save's or a checkpoint's. Gear goes in <paramref name="gearSlot"/>.
    /// </summary>
    public void Restore(ItemInstance item, int gearSlot = -1)
    {
        switch (item.Item.Kind)
        {
            case ItemKind.Weapon:
                Weapon = item;
                break;
            case ItemKind.Armor:
                Armor = item;
                break;
            case ItemKind.Shield:
                Shield = item;
                break;
            default:
                if (_gear[gearSlot] is null)
                    GearCount++;
                _gear[gearSlot] = item;
                break;
        }
    }

    /// <summary>
    /// What the worn weapon, armor, shield and gear add to a skill: what a trainer applies when the
    /// skill goes from 0 to 1 (<c>WidgetSkillTrain::Purchase</c>, which looks at all 12 gear slots).
    /// </summary>
    public int SkillBonus(SkillId skill) => All.Select(i => i.Item.Bonuses.Skill).OfType<SkillBonus>()
        .Where(b => SkillId.FromRecord(b.Skill) == skill).Sum(b => b.Amount);

    /// <summary>
    /// Pays a spell's Stamina cost from the batteries first, then from Stamina, if the two together
    /// can pay it all (<c>Entity::TestEquipStamina</c>). The batteries are the weapon's, the armor's,
    /// the shield's and the first <see cref="GearCount"/> gear slots', in that order.
    /// </summary>
    public bool PayStamina(int cost)
    {
        if (Pay(cost, apply: false) != 0)
            return false;
        Pay(cost, apply: true);
        return true;
    }

    // Entity::EquipStamina: the batteries in order, then what's left from Stamina. 0 if it's paid.
    private int Pay(int cost, bool apply)
    {
        var left = (short)cost;
        foreach (var item in new[] { Weapon, Armor, Shield }.Concat(_gear.Take(GearCount)))
            left = DrawBattery(item, left, apply);
        if (left < 1)
            return 0;
        var stamina = _owner.Stats.Modified(Stat.Stamina);
        var shortfall = (short)(left - stamina);
        if (shortfall < 0)
        {
            if (apply)
                _owner.Stats.ChangeCurrent(Stat.Stamina, -(sbyte)left);
            return 0;
        }
        if (apply)
            _owner.Stats.ChangeCurrent(Stat.Stamina, -(sbyte)stamina);
        return shortfall;
    }

    // Entity::AddEquipStamina.
    private static short DrawBattery(ItemInstance? item, short cost, bool apply)
    {
        if (cost == 0)
            return 0;
        if (item?.Battery is not { } battery || battery == 0)
            return cost;
        var left = (short)(cost - battery);
        if (left < 0)
        {
            if (apply)
                item.Battery = (sbyte)-left;
            return 0;
        }
        if (apply)
            item.Battery = 0;
        return left;
    }

    /// <summary>
    /// Refills each worn item's battery by <paramref name="fraction"/> of its capacity, truncated, up to
    /// full (<c>ItemCampStamina</c>), as camping does.
    /// </summary>
    public void RechargeBatteries(float fraction)
    {
        foreach (var item in new[] { Weapon, Armor, Shield }.Concat(_gear.Take(GearCount)))
        {
            if (item is not null)
                Recharge(item, fraction);
        }
    }

    /// <summary>Refills an item's battery by <paramref name="fraction"/> of its capacity, truncated, up to full (<c>ItemCampStamina</c>).</summary>
    public static void Recharge(ItemInstance item, float fraction)
    {
        if (item.Battery is not { } battery || item.Item.BatteryCapacity is not { } capacity)
            return;
        var gain = (sbyte)(int)(capacity * fraction);
        item.Battery = capacity < battery + gain ? (sbyte)capacity : (sbyte)(battery + gain);
    }

    // Entity::ApplyEquipment: the skill bonus if the skill's base level isn't 0, then the stat bonus.
    private void Apply(ItemInstance item)
    {
        var bonuses = item.Item.Bonuses;
        if (bonuses.Skill is { } skill && _owner.Skills.Base(SkillId.FromRecord(skill.Skill)) != 0)
            _owner.Skills.Modify(SkillId.FromRecord(skill.Skill), skill.Amount);
        if (bonuses.Stat is { } stat && stat.Stat != Item.StaminaStat)
        {
            var which = (Stat)int.Parse(stat.Stat.Key);
            var amount = Bounded(which, (sbyte)stat.Amount, adding: true);
            if (which == Stat.Endurance)
                _owner.Stats.ChangeBase(which, amount);
            else
                _owner.Stats.ChangeModified(which, amount);
        }
    }

    // FUN_80078874: the same undone.
    private void Remove(ItemInstance item)
    {
        var bonuses = item.Item.Bonuses;
        if (bonuses.Skill is { } skill && _owner.Skills.Base(SkillId.FromRecord(skill.Skill)) != 0)
            _owner.Skills.Modify(SkillId.FromRecord(skill.Skill), -skill.Amount);
        if (bonuses.Stat is { } stat && stat.Stat != Item.StaminaStat)
        {
            var which = (Stat)int.Parse(stat.Stat.Key);
            var amount = Bounded(which, (sbyte)stat.Amount, adding: false);
            if (which == Stat.Endurance)
                _owner.Stats.ChangeBase(which, -amount);
            else
                _owner.Stats.ChangeModified(which, -amount);
        }
    }

    // Armor's Dexterity and Stealth (Entity::EquipArmorOrShield, RemoveArmorOrShield).
    private void ApplyArmor(ItemInstance armor, int sign)
    {
        if (sign > 0)
            Apply(armor);
        var record = armor.Item.Armor!;
        _owner.Stats.ChangeModified(Stat.Dexterity, sign * record.Dexterity);
        _owner.Skills.Modify(Skill.Stealth, sign * record.Stealth);
    }

    /// <summary>
    /// How much of a bonus to Endurance or the Level applies, so it can't take it to 0
    /// (<c>FUN_8007b6bc</c> adding, <c>FUN_8007b760</c> taking off). The Level is bounded only while
    /// Endurance is at 0. Other stats take the whole bonus.
    /// </summary>
    private sbyte Bounded(Stat stat, sbyte amount, bool adding)
    {
        if (stat == Stat.Level ? _owner.Stats.Modified(Stat.Endurance) != 0 : stat != Stat.Endurance)
            return amount;
        var value = (sbyte)_owner.Stats.Modified(stat);
        return adding
            ? -amount < value ? amount : (sbyte)(1 - value)
            : amount < value ? amount : (sbyte)(value - 1);
    }
}
