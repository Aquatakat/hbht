using HBHT.Core.Flags;
using HBHT.Core.Items;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Characters;

/// <summary>What reading a scroll in trek did (<c>Party::UseScroll</c>).</summary>
public enum ScrollResult
{
    Learned,
    /// <summary>Only a character who can learn Wizard can learn from scrolls.</summary>
    NotAWizard,
    /// <summary>The scroll has no spell: the Lighthouse scroll, whose magic only relights the lighthouse.</summary>
    NoSpell,
    AlreadyKnown,
    /// <summary>The character's base Wizard is below the spell's.</summary>
    NeedsWizard,
    /// <summary>The spell is of another school, and the character isn't Chaos.</summary>
    WrongSchool,
}

public sealed partial class Party
{
    /// <summary>A party with an empty inventory, whose key items set <paramref name="flags"/>.</summary>
    public Party(ItemCatalog items, FlagNetwork? flags = null) => Inventory = new PartyInventory(items, flags);

    public PartyInventory Inventory { get; }

    /// <summary>
    /// Whether the party has an item, carried or equipped (<c>Party::hasItem</c>). Gear is looked for
    /// only in each member's first <see cref="Equipment.GearCount"/> slots, as the original looks.
    /// </summary>
    public bool HasItem(RecordId item) => Inventory.Has(item) || Members.Any(m => m.Equipment.Find(item) is not null);

    /// <summary>
    /// Takes one of an item for a script (<c>Party::TakeItem</c>): from the inventory, or else from the
    /// first member who has it equipped, which destroys it. False if the party has none.
    /// </summary>
    public bool TakeItem(RecordId item)
    {
        if (Inventory.Take(item))
            return true;
        foreach (var member in Members)
        {
            if (member.Equipment.Find(item) is { } worn)
                return member.Equipment.Remove(worn) is not null;
        }
        return false;
    }

    /// <summary>
    /// Equips a member with an item from the inventory (<c>Party::GetEquipError</c>), putting back what it
    /// replaces. Body armor, a shield, the weapon and each kind of gear replace the one worn; rings go
    /// two to a member, and scrolls with a spell three, outside trek. The item takes the stack's battery
    /// and charges.
    /// </summary>
    /// <remarks>
    /// The original lets anyone replace the Archmage Staff, which then vanishes with its bonuses still
    /// applied; here it's refused (<see cref="EquipRefusal.Fixed"/>). Gear is checked for a free slot
    /// before what it replaces comes off, so with all 12 worn, nothing can be swapped.
    /// </remarks>
    public EquipRefusal Equip(Character member, RecordId item)
    {
        if (Inventory.Find(item) is not { } stack)
            return EquipRefusal.CantUse;
        var thing = stack.Item;
        if (thing.Bonuses.Aspect != Aspect.None && thing.Bonuses.Aspect != member.Aspect)
            return EquipRefusal.WrongAspect;
        var equipment = member.Equipment;
        switch (thing.Kind)
        {
            case ItemKind.Armor:
                return Swap(stack, equipment.Armor, equipment.RemoveArmor, i => { equipment.EquipArmor(i); return EquipRefusal.None; });
            case ItemKind.Shield:
                return equipment.CanCarryShield
                    ? Swap(stack, equipment.Shield, equipment.RemoveShield, equipment.EquipShield)
                    : EquipRefusal.CantUse;
            case ItemKind.Weapon:
                var wield = equipment.CanWield(thing);
                if (wield != EquipRefusal.None)
                    return wield;
                if (equipment.Weapon?.Item.Weapon?.Fixed == true)
                    return EquipRefusal.Fixed;
                return Swap(stack, equipment.Weapon, equipment.RemoveWeapon, equipment.EquipWeapon);
            case ItemKind.Ring:
                return WearAnother(stack, equipment, Equipment.Rings);
            case ItemKind.Scroll:
                return thing.Bonuses.Spell is null ? EquipRefusal.CantUse : WearAnother(stack, equipment, Equipment.Scrolls);
            case var _ when thing.IsGear:
                var wear = equipment.CanWear(thing);
                if (wear != EquipRefusal.None)
                    return wear;
                var slot = Enumerable.Range(0, equipment.GearCount).FirstOrDefault(s => equipment.Gear[s]?.Item.Kind == thing.Kind, -1);
                return Swap(stack, slot < 0 ? null : equipment.Gear[slot], () => equipment.RemoveGear(slot), equipment.EquipGear);
            default:
                return EquipRefusal.CantUse;
        }
    }

    /// <summary>
    /// Takes an item off a member and puts it in the inventory, with its battery and charges
    /// (<c>Party::RemoveArmorFrom</c> and the rest). False if the inventory can't take it, or it's the
    /// Archmage Staff, which stays.
    /// </summary>
    public bool Unequip(Character member, ItemInstance item)
    {
        if (item.Item.Weapon?.Fixed == true || !Stow(item))
            return false;
        member.Equipment.Remove(item);
        return true;
    }

    /// <summary>
    /// Takes everything off a member who's leaving and puts it in the inventory (<c>Party::StripMember</c>):
    /// the gear, then the weapon, the body armor and the shield. The Archmage Staff stays, and so does
    /// whatever the inventory has no room for.
    /// </summary>
    public void Strip(Character member)
    {
        var equipment = member.Equipment;
        foreach (var gear in equipment.Gear.OfType<ItemInstance>().ToList())
            Unequip(member, gear);
        foreach (var item in new[] { equipment.Weapon, equipment.Armor, equipment.Shield })
        {
            if (item is not null)
                Unequip(member, item);
        }
    }

    /// <summary>
    /// A member reads a scroll in trek and learns its spell at level 1, which uses it up
    /// (<c>Party::UseScroll</c>). The checks are in the original's order.
    /// </summary>
    /// <param name="spells">The spell records scrolls name.</param>
    public ScrollResult ReadScroll(Character member, RecordId scroll, Func<RecordId, SpellRecord> spells)
    {
        var wizard = member.Skills.Base(Skill.Wizard);
        if (wizard < 0)
            return ScrollResult.NotAWizard;
        if (Inventory.Find(scroll)?.Item.Bonuses.Spell is not { } carried)
            return ScrollResult.NoSpell;
        if (member.FindSpell(carried.Spell) is not null)
            return ScrollResult.AlreadyKnown;
        var spell = spells(carried.Spell);
        switch (member.CanLearn(spell))
        {
            case SpellLearnability.WizardTooLow:
                return ScrollResult.NeedsWizard;
            case SpellLearnability.WrongSchool:
                return ScrollResult.WrongSchool;
        }
        member.LearnSpell(spell);
        Inventory.Take(scroll);
        return ScrollResult.Learned;
    }

    // Takes one from the stack, puts the old one in the inventory, and equips the new one; if the old
    // can't go back, the new one does instead.
    private EquipRefusal Swap(ItemStack stack, ItemInstance? old, Func<ItemInstance?> remove, Func<ItemInstance, EquipRefusal> equip)
    {
        var item = stack.Instance.Copy();
        Inventory.Take(item.Id);
        if (old is not null)
        {
            if (!Stow(old))
            {
                Stow(item);
                return EquipRefusal.NoRoom;
            }
            remove();
        }
        return equip(item);
    }

    // Party::GetEquipError3: rings and scrolls are added, up to a number of each, never swapped.
    private EquipRefusal WearAnother(ItemStack stack, Equipment equipment, int most)
    {
        var refusal = equipment.CanWear(stack.Item);
        if (refusal != EquipRefusal.None)
            return refusal;
        if (equipment.Gear.Take(equipment.GearCount).Count(g => g?.Item.Kind == stack.Item.Kind) >= most)
            return EquipRefusal.NoRoom;
        var item = stack.Instance.Copy();
        Inventory.Take(item.Id);
        return equipment.EquipGear(item);
    }

    // Puts an item in the inventory, its stack taking the item's battery and charges (UpdateItemStatCharges).
    private bool Stow(ItemInstance item)
    {
        if (!Inventory.Add(item.Item, 1, item))
            return false;
        Inventory.Find(item.Id)?.Instance.TakeStateOf(item);
        return true;
    }
}
