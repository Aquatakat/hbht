namespace Aidyn.Rom.Db;

/// <summary>One of a shop's 20 priced stock slots.</summary>
/// <param name="Unk0">Not read by the game. Mostly 50, and always 5–100.</param>
/// <param name="PriceMultiplier">An index into the game's multipliers: ×0.5, ×0.75, ×1, ×1.25, ×1.5, ×2, ×3, ×5.</param>
/// <param name="Unk2">The game stocks the slot only when this is nonzero, and then stocks one of the item whatever the value. Mostly 1.</param>
public readonly record struct RomShopStock(ItemId Item, byte Unk0, byte PriceMultiplier, byte Unk2);

/// <summary>A <c>ShopDB</c> record (0x6C bytes).</summary>
/// <param name="Shopkeeper">The entity who runs the shop.</param>
/// <param name="ExtraStock">Three more slots, always stocked at the base price when not empty.</param>
public sealed record RomShopRecord(ItemId Shopkeeper, IReadOnlyList<RomShopStock> Stock, IReadOnlyList<ItemId> ExtraStock)
{
    public const int Size = 0x6C;

    internal static RomShopRecord Read(ref RecordReader r)
    {
        var shopkeeper = r.Id();
        var stock = new RomShopStock[20];
        for (var i = 0; i < stock.Length; i++)
            stock[i] = new(r.Id(), r.U8(), r.U8(), r.U8());
        return new RomShopRecord(shopkeeper, stock, r.Ids(3));
    }
}
