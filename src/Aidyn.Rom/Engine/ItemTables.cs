using Aidyn.Rom.Db;

namespace Aidyn.Rom.Engine;

/// <summary>
/// The tables in code that items use (roadmap 2.3): potions' prices, the Alchemist's and Mechanic's
/// recipes, the menus' icons, and which items have inventory slots of their own.
/// </summary>
/// <param name="PotionPrices">By potion, the low byte of its ID (<c>potion_prices</c>).</param>
/// <param name="ItemIcons">The icon of each item that has one of its own, by ID. Potions have none: they share <see cref="PotionIcon"/>.</param>
/// <param name="SkillIcons">By skill, Alchemist to Wizard.</param>
/// <param name="AspectIcons">By aspect; 0 for none.</param>
/// <param name="SchoolIcons">By school, Chaos to Star; 0 for none.</param>
/// <param name="SpellSymbolIcons">By the symbol a spell's icon table gives it (<c>SpellDB::GetIcon</c>).</param>
/// <param name="ReservedItems">Items with a slot of their own after the general ones, in slot order.</param>
/// <param name="KeyEquipment">Unique gear with a slot of its own, after the key items', in slot order.</param>
/// <param name="MustTakeItems">Items loot can't be left behind with.</param>
/// <param name="WeaponModels">The Borg5 of each weapon that has one, by its position in the weapon DB (<c>Weapon_borg5_lookup</c>).</param>
/// <param name="ItemModels">The Borg5 of each of the six items that have one, by position in the item DB (<c>ItemBorg5Search</c>).</param>
public sealed record ItemTables(
    IReadOnlyList<int> PotionPrices,
    IReadOnlyList<PotionRecipe> PotionRecipes,
    IReadOnlyList<ArmorRecipe> ArmorRecipes,
    IReadOnlyDictionary<ItemId, ushort> ItemIcons,
    IReadOnlyList<ushort> SkillIcons,
    IReadOnlyList<ushort> AspectIcons,
    IReadOnlyList<ushort> SchoolIcons,
    IReadOnlyList<ushort> SpellSymbolIcons,
    IReadOnlyList<ItemId> ReservedItems,
    IReadOnlyList<ItemId> KeyEquipment,
    IReadOnlyList<ItemId> MustTakeItems,
    IReadOnlyDictionary<int, ushort> WeaponModels,
    IReadOnlyDictionary<int, ushort> ItemModels)
{
    /// <summary>
    /// The Borg5 of each shield that has one, by its position in the armor DB. The original has these in
    /// code, a switch on the position (<c>ArmorDB::GetBorg5</c>, 0x8006b3b8 in 1.0, with the same cases in
    /// 1.1 and the debug build), which returns <c>ShieldNONE</c> (0x38F9, meaning none) for every other
    /// position, body armor included. Positions 51 to 67, Sheridan's Shield to the Chaos Shield.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, ushort> ShieldModels = new Dictionary<int, ushort>
    {
        [51] = 0x351B, [52] = 0x351D, [53] = 0x353C, [54] = 0x3540, [55] = 0x3542, [56] = 0x3526, [57] = 0x3521, [58] = 0x3534,
        [59] = 0x3532, [60] = 0x352F, [61] = 0x353A, [62] = 0x3528, [63] = 0x351F, [64] = 0x353E, [65] = 0x3537, [66] = 0x352A,
        [67] = 0x3523,
    };

    /// <summary>The icon of an item that isn't in <see cref="ItemIcons"/>: a question mark (<c>GetItemImage</c>).</summary>
    public const ushort DefaultIcon = 0x35DC;

    /// <summary>Every potion's icon (<c>GetItemImage</c>).</summary>
    public const ushort PotionIcon = 0x35E5;

    public const int PotionCount = 17;

    public static ItemTables Read(RomImage rom)
    {
        var prices = CodeSegment.Table(rom, CodeTable.PotionPrices).ToArray();
        var potionPrices = Enumerable.Range(0, PotionCount).Select(i => (int)CodeSegment.U16(prices, i * 2)).ToList();

        var potions = CodeSegment.Table(rom, CodeTable.PotionRecipes);
        var potionRecipes = new List<PotionRecipe>();
        for (var at = 0; at + PotionRecipe.Size <= potions.Length; at += PotionRecipe.Size)
            potionRecipes.Add(new PotionRecipe(potions[at], potions[at + 1], potions[at + 2], potions[at + 3], potions[at + 4],
                potions[at + 5], potions[at + 6]));

        var armor = CodeSegment.Table(rom, CodeTable.ArmorRecipes);
        var armorRecipes = new List<ArmorRecipe>();
        for (var at = 0; at + ArmorRecipe.Size <= armor.Length; at += ArmorRecipe.Size)
            armorRecipes.Add(new ArmorRecipe(CodeSegment.U16(armor, at), armor[at + 2], armor[at + 3], armor[at + 4]));

        // menuimages.cpp: five lists of (key, Borg8) u16 pairs, each followed by its count as a u32.
        var icons = CodeSegment.Table(rom, CodeTable.MenuIcons).ToArray();
        var at2 = 0;
        List<(ushort Key, ushort Image)> List()
        {
            var entries = new List<(ushort, ushort)>();
            // The count comes after the list, so find it by trying each length. None is empty, and the
            // aspects' starts with a (0, 0) entry, which would read as an empty list's count.
            for (var n = 1; at2 + n * 4 + 4 <= icons.Length; n++)
            {
                if (CodeSegment.U32(icons, at2 + n * 4) != n)
                    continue;
                for (var i = 0; i < n; i++)
                    entries.Add((CodeSegment.U16(icons, at2 + i * 4), CodeSegment.U16(icons, at2 + i * 4 + 2)));
                at2 += n * 4 + 4;
                return entries;
            }
            throw new InvalidDataException("A menu icon list has no count after it.");
        }
        var items = List();
        var skills = List();
        var aspects = List();
        var schools = List();
        var symbols = List();

        return new ItemTables(potionPrices, potionRecipes, armorRecipes,
            items.ToDictionary(e => new ItemId(e.Key), e => e.Image),
            Ordered(skills), Ordered(aspects), Ordered(schools), Ordered(symbols),
            Ids(rom, CodeTable.ReservedItems), Ids(rom, CodeTable.KeyEquipment), Ids(rom, CodeTable.MustTakeItems),
            Models(rom, CodeTable.WeaponsWithModels, CodeTable.WeaponModels, 1, 0xFF),
            Models(rom, CodeTable.ItemsWithModels, CodeTable.ItemModels, 2, 0xFFFF));

        // Each list is keyed 0 to n − 1, in order.
        static List<ushort> Ordered(List<(ushort Key, ushort Image)> list) =>
            list.Select((e, i) => e.Key == i ? e.Image : throw new InvalidDataException($"Icon list key {e.Key} isn't at {i}.")).ToList();
    }

    /// <summary>
    /// A list of positions ending with <paramref name="end"/>, each of <paramref name="width"/> bytes, and the
    /// u16 Borg5 at the same index in the table of models. The tables' ranges hold the terminator and padding.
    /// </summary>
    private static Dictionary<int, ushort> Models(RomImage rom, CodeTable positionsTable, CodeTable modelsTable, int width, int end)
    {
        var positions = CodeSegment.Table(rom, positionsTable).ToArray();
        var models = CodeSegment.Table(rom, modelsTable).ToArray();
        var result = new Dictionary<int, ushort>();
        for (var i = 0; (i + 1) * width <= positions.Length; i++)
        {
            var position = width == 1 ? positions[i] : CodeSegment.U16(positions, i * 2);
            if (position == end)
                return result;
            result.Add(position, CodeSegment.U16(models, i * 2));
        }
        throw new InvalidDataException($"The {positionsTable} list has no terminator.");
    }

    private static List<ItemId> Ids(RomImage rom, CodeTable table)
    {
        var data = CodeSegment.Table(rom, table).ToArray();
        return Enumerable.Range(0, data.Length / 2).Select(i => new ItemId(CodeSegment.U16(data, i * 2))).ToList();
    }
}

/// <summary>A potion the Alchemist brews (<c>potionRecipie</c>).</summary>
/// <param name="Potion">The potion's index, the low byte of its ID.</param>
/// <param name="Alchemist">The modified Alchemist level it needs.</param>
/// <param name="Unk5">Unread: 1 to 10.</param>
/// <param name="Step">The least step of the check that brews it: 0 for all.</param>
public sealed record PotionRecipe(int Potion, int Spice, int Herb, int Gemstone, int Alchemist, int Unk5, int Step)
{
    public const int Size = 7;
}

/// <summary>Armor the Mechanic makes from a hide (<c>ArmorCraftRecipie</c>).</summary>
/// <param name="Hide">The hide's position in the item DB (<see cref="GameDb.Gear"/>).</param>
/// <param name="Armor">The armor's position in the armor DB (<see cref="GameDb.Armor"/>).</param>
/// <param name="Step">The least step of the check that makes it. The decomp calls it the stamina used.</param>
/// <param name="Mechanic">The base Mechanic level it needs.</param>
public sealed record ArmorRecipe(int Hide, int Armor, int Step, int Mechanic)
{
    public const int Size = 6;
}
