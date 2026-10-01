using Aidyn.Core.Items;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The order the inventory list shows items in (<c>WidgetInvShop::InitMenu</c>'s sorts): what members
/// wear first, the member shown first and then the rest in party order, then what's carried. Either
/// part is sorted by kind, in <see cref="Kinds"/>' order, and then by name.
/// </summary>
public static class InventoryOrder
{
    /// <summary>
    /// The kinds in the order the list shows them (<c>D_800ed560</c>). The decomp leaves scrolls out;
    /// the machine code has them after wands.
    /// </summary>
    public static IReadOnlyList<ItemKind> Kinds => KindOrder;

    private static readonly ItemKind[] KindOrder =
    [
        ItemKind.Potion, ItemKind.Weapon, ItemKind.Wand, ItemKind.Scroll, ItemKind.Amulet, ItemKind.Ring, ItemKind.Helmet,
        ItemKind.Cloak, ItemKind.Gloves, ItemKind.Boots, ItemKind.Belt, ItemKind.Armor, ItemKind.Shield, ItemKind.Key, ItemKind.Misc,
    ];

    /// <summary>
    /// Compares two items as the list sorts them: by kind, then by name in byte order (<c>strcmp</c>).
    /// A kind not in <see cref="Kinds"/> goes last.
    /// </summary>
    public static int Compare(Item a, Item b)
    {
        if (a.Kind != b.Kind)
            return Rank(a.Kind).CompareTo(Rank(b.Kind));
        return string.CompareOrdinal(a.Name, b.Name);
    }

    private static int Rank(ItemKind kind) => Array.IndexOf(KindOrder, kind) is var i and >= 0 ? i : int.MaxValue;
}
