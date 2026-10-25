using HBHT.Plugins;

namespace HBHT.Core.Items;

/// <summary>
/// An item as one is held (<c>ItemInstance</c>): its price, and what's left in its battery and its
/// spell's charges. An inventory's stack is one instance, which every item in it shares.
/// </summary>
public sealed class ItemInstance(Item item)
{
    public Item Item { get; } = item;

    /// <summary>Its price here: a shop multiplies its stock's (<c>InitShop</c>), as a u16.</summary>
    public int Price { get; set; } = item.Price;

    /// <summary>
    /// The Stamina left in its battery, as a signed byte, or null for an item without one (its
    /// <c>StatMod</c>'s amount). Casting draws it down (<see cref="Equipment.PayStamina"/>) and camping
    /// refills it.
    /// </summary>
    public int? Battery { get; set; } = item.BatteryCapacity;

    /// <summary>Its spell's charges left, as a byte, or null for an item without a spell.</summary>
    public int? Charges { get; set; } = item.Bonuses.Spell?.Charges;

    public RecordId Id => Item.Id;

    /// <summary>
    /// A copy of its state, as the original copies an item's <c>StatMod</c> and charges when it moves
    /// between the inventory and a character (<c>CreateStatMod</c>, <c>UpdateItemStatCharges</c>).
    /// </summary>
    public ItemInstance Copy() => new(Item) { Price = Price, Battery = Battery, Charges = Charges };

    /// <summary>Takes <paramref name="other"/>'s battery and charges (<c>UpdateItemStatCharges</c>).</summary>
    internal void TakeStateOf(ItemInstance other)
    {
        Battery = other.Battery;
        if (other.Charges is { } charges && Charges is not null)
            Charges = charges;
    }

    public override string ToString() => Item.Name;
}
