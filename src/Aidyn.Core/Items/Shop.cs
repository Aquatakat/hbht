using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>What buying, selling, taking or dropping did.</summary>
public enum TradeResult
{
    Done,
    /// <summary>The party's gold is less than the price.</summary>
    CantAfford,
    /// <summary>The party's stack of it is full.</summary>
    StackFull,
    /// <summary>The inventory has no general slot for a new stack.</summary>
    InventoryFull,
    /// <summary>The shopkeeper won't buy it: it would sell for 1 gold or less.</summary>
    Refused,
    /// <summary>There's none of it to sell, take or drop.</summary>
    NoneLeft,
}

/// <summary>
/// A shop's barter window (<c>InitShop</c>, <c>WidgetBarter</c>, <c>WidgetInvShop</c>). It sells its stock
/// without end, at prices the Merchant skill moves both ways, and buys what the party carries.
/// </summary>
/// <remarks>
/// Opening it sets the barter factor once (<c>Party::Barter</c>): the shopkeeper's Merchant less the party's
/// best, times <see cref="InventoryRecord.MerchantStep"/>. The party's best is the living member with the
/// highest modified Merchant above 0, the later on a tie, and bartering costs them 15 less that in Stamina;
/// if they can't pay it, the party's Merchant counts as 0. Buying costs the stock's price × (1 + factor)
/// and selling pays the item's price × (1 − factor) × <see cref="InventoryRecord.SellShare"/>, each in
/// single precision and truncated to 16 bits (<c>WidgetInvShop::GetGoldPrice</c>).
/// </remarks>
public sealed class Shop
{
    private readonly Party _party;
    private readonly ItemCatalog _items;
    private readonly FlagNetwork? _flags;

    private Shop(ShopRecord record, ItemPile stock, float barter, Party party, ItemCatalog items, FlagNetwork? flags)
    {
        Record = record;
        Stock = stock;
        Barter = barter;
        _party = party;
        _items = items;
        _flags = flags;
    }

    public ShopRecord Record { get; }

    /// <summary>What it sells, each stack's instance at the shop's price.</summary>
    public ItemPile Stock { get; }

    /// <summary>The barter factor (<c>gBarterFloat</c>): above 0 the shopkeeper has the better of it.</summary>
    public float Barter { get; }

    /// <summary>The first shop the shopkeeper runs (<c>InitShop</c>), or null.</summary>
    public static ShopRecord? For(IEnumerable<ShopRecord> shops, RecordId shopkeeper) => shops.FirstOrDefault(s => s.Shopkeeper == shopkeeper);

    /// <summary>
    /// Opens a shop (<c>InitShop</c>): stocks it, and sets the barter factor, which may cost a member
    /// Stamina.
    /// </summary>
    /// <param name="shopkeeper">Whose shop: their record's Merchant level counts, as the ROM has it.</param>
    /// <param name="flags">The flags the first potion bought sets.</param>
    public static Shop Open(ShopRecord record, EntityRecord shopkeeper, Party party, ItemCatalog items, FlagNetwork? flags = null)
    {
        var stock = new ItemPile(items);
        foreach (var slot in record.Stock)
        {
            if (!stock.Add(slot.Item))
                continue;
            // A slot listing an item already stocked multiplies that stack's price again.
            if (slot.PriceMultiplier != 1 && stock.Find(PartyInventory.Replace(items, items[slot.Item]).Id) is { } stack)
            {
                float price = stack.Instance.Price;
                stack.Instance.Price = (ushort)(uint)(price * slot.PriceMultiplier);
            }
        }
        return new Shop(record, stock, BarterFactor(party, shopkeeper.Skills.Merchant ?? 0xFF, items.Rules.MerchantStep), party, items, flags);
    }

    /// <summary>The barter factor, taking its Stamina from the member who barters (<c>Party::Barter</c>).</summary>
    public static float BarterFactor(Party party, int shopkeeperMerchant, float step)
    {
        Character? barterer = null;
        var best = 0;
        foreach (var member in party.Members.Where(m => !m.IsDead))
        {
            var level = member.Skills.Modified(Skill.Merchant);
            if (level != 0 && level >= best)
                (barterer, best) = (member, level);
        }
        if (barterer is not null)
        {
            var cost = (short)(15 - best);
            if (cost <= barterer.Stats.Modified(Stat.Stamina))
            {
                barterer.DrainStamina(cost);
                return ((byte)shopkeeperMerchant - best) * step;
            }
        }
        return (byte)shopkeeperMerchant * step;
    }

    /// <summary>What the party pays for one of a stack of the stock.</summary>
    public int BuyPrice(ItemInstance item)
    {
        float price = item.Price;
        var cost = Barter * price;
        cost += price;
        return (ushort)(uint)cost;
    }

    /// <summary>What the shopkeeper pays for one of the party's.</summary>
    public int SellPrice(ItemInstance item)
    {
        float price = item.Price;
        var share = 1f - Barter;
        var paid = price * share;
        paid *= _items.Rules.SellShare;
        return (ushort)(uint)paid;
    }

    /// <summary>Buys one of an item from the stock (<c>WidgetBarter::AFunc</c>).</summary>
    public TradeResult Buy(RecordId item)
    {
        if (Stock.Find(item) is not { } stack)
            return TradeResult.NoneLeft;
        var price = BuyPrice(stack.Instance);
        if (_party.Gold < price)
            return TradeResult.CantAfford;
        if (_party.Inventory!.Count(item) >= _items.Rules.StackLimit)
            return TradeResult.StackFull;
        if (!_party.Inventory.Add(item))
            return TradeResult.InventoryFull;
        _party.Gold -= (uint)price;
        Trade.NoteFirstPotion(stack.Item, _items, _flags);
        return TradeResult.Done;
    }

    /// <summary>
    /// Sells some of what the party carries (<c>DollBarterConfirm</c>), at most what it holds (the menu
    /// offers <see cref="Quantities"/>): equipped items must be taken off first. The stock doesn't take them.
    /// </summary>
    public TradeResult Sell(RecordId item, int quantity)
    {
        if (_party.Inventory!.Find(item) is not { } stack)
            return TradeResult.NoneLeft;
        var price = SellPrice(stack.Instance);
        if (price <= 1)
            return TradeResult.Refused;
        quantity = Math.Min(quantity, stack.Quantity);
        _party.Inventory.Take(item, quantity);
        _party.Gold += (uint)(price * quantity);
        return TradeResult.Done;
    }

    /// <summary>
    /// What the party can see of a price (<c>Party::ApraisePrice</c>), 0 to 6, from "This is highway
    /// robbery!" to the best of the Common table's lines from 384 (counting from 0). The member best at
    /// it counts, dead or not: Intelligence + Merchant × 6, which a curve turns into how well they judge
    /// the item's worth.
    /// </summary>
    public int Appraise(ItemInstance item)
    {
        var best = (ushort)_party.Members.Select(m => (ushort)(m.Stats.Modified(Stat.Intelligence) + m.Skills.Modified(Skill.Merchant) * 6))
            .DefaultIfEmpty().Max();
        float judge = best * best * -0.0186603f;
        judge += best * 2.366025f;
        judge += 50f;
        judge /= 100f;
        judge = Math.Clamp(judge, 0f, 125f);
        float worth = item.Item.Price;
        worth *= judge;
        worth *= 3f;
        worth /= BuyPrice(item);
        var index = worth > 0 ? (byte)(int)(worth + 0.5) : (byte)-(int)(0.5 - worth);
        return Math.Min((int)index, 6);
    }

    /// <summary>The quantities the sell or drop menu offers for a stack: 1, 5, 10 and 20 below what's held, then all (<c>DollBarterConfirm</c>).</summary>
    public static IReadOnlyList<int> Quantities(int held) =>
        held == 1 ? [1] : [.. new[] { 1, 5, 10, 20 }.Where(q => q < held), held];
}

/// <summary>
/// The loot window (<c>WidgetBarter</c> for loot): a container's or beaten party's items, which the party
/// takes a stack at a time. Its gold is the party's as it opens (<c>build_loot_menu</c>).
/// </summary>
public sealed class LootWindow
{
    private readonly Party _party;
    private readonly ItemCatalog _items;
    private readonly FlagNetwork? _flags;

    private LootWindow(ItemPile loot, int gold, Party party, ItemCatalog items, FlagNetwork? flags)
    {
        Loot = loot;
        Gold = gold;
        _party = party;
        _items = items;
        _flags = flags;
    }

    public ItemPile Loot { get; }

    /// <summary>The gold it had, already given.</summary>
    public int Gold { get; }

    /// <summary>Opens the window, giving the party the gold.</summary>
    public static LootWindow Open(ItemPile loot, int gold, Party party, ItemCatalog items, FlagNetwork? flags = null)
    {
        party.Gold += (uint)gold;
        return Showing(loot, gold, party, items, flags);
    }

    /// <summary>Opens the window on gold the party already has, as a container gives it before the window opens.</summary>
    public static LootWindow Showing(ItemPile loot, int gold, Party party, ItemCatalog items, FlagNetwork? flags = null) =>
        new(loot, gold, party, items, flags);

    /// <summary>Takes a whole stack, or as many as the party's stack has room for (<c>WidgetBarter::AFunc</c>).</summary>
    public TradeResult Take(RecordId item)
    {
        if (Loot.Find(item) is not { } stack)
            return TradeResult.NoneLeft;
        var held = _party.Inventory!.Count(item);
        if (held >= _items.Rules.StackLimit)
            return TradeResult.StackFull;
        var quantity = Math.Min(stack.Quantity, _items.Rules.StackLimit - held);
        if (!_party.Inventory.Add(item, quantity, stack.Instance))
            return TradeResult.InventoryFull;
        Loot.Take(item, quantity);
        Trade.NoteFirstPotion(stack.Item, _items, _flags);
        return TradeResult.Done;
    }

    /// <summary>
    /// Drops some of what the party carries, which is lost: the loot doesn't take it. An item worth 1 gold
    /// or less at half its price can't be dropped.
    /// </summary>
    public TradeResult Drop(RecordId item, int quantity)
    {
        if (_party.Inventory!.Find(item) is not { } stack)
            return TradeResult.NoneLeft;
        if (!Droppable(stack.Instance))
            return TradeResult.Refused;
        _party.Inventory.Take(item, quantity);
        return TradeResult.Done;
    }

    /// <summary>
    /// Whether an item can be dropped: it must be worth more than 1 gold at half its price, worked as the
    /// shop's selling price with no barter (<c>WidgetInvShop::GetGoldPrice</c>).
    /// </summary>
    public bool Droppable(ItemInstance item) => (ushort)(uint)(item.Price * _items.Rules.SellShare) > 1;

    /// <summary>Whether the window can close with items left: not while it holds one that must be taken.</summary>
    public bool CanLeave => Loot.CanLeave;
}

/// <summary>Rules the shop and loot windows share.</summary>
internal static class Trade
{
    /// <summary>
    /// Sets the first-potion flag when the party first gains a potion in a shop or loot. Its help shows on
    /// the flag (a trigger, 2.10).
    /// </summary>
    public static void NoteFirstPotion(Item item, ItemCatalog items, FlagNetwork? flags)
    {
        if (item.Kind == ItemKind.Potion && flags is not null && !flags.Get(items.Rules.FirstPotionFlag))
            flags.Set(items.Rules.FirstPotionFlag, true);
    }
}
