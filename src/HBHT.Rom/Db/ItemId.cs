using System.Globalization;

namespace HBHT.Rom.Db;

/// <summary>The high byte of an <see cref="ItemId"/>: which kind of record it names.</summary>
public enum DbType : byte
{
    None = 0,
    /// <summary>Miscellaneous items, including crafting reagents.</summary>
    Misc = 1,
    Entity = 2,
    Spell = 3,
    /// <summary>Rituals, from early builds. Unused in the final game.</summary>
    Ritual = 4,
    Armor = 5,
    Shield = 6,
    Weapon = 7,
    /// <summary>Loot tables, for enemies and chests.</summary>
    Loot = 8,
    Helmet = 9,
    Cloak = 10,
    Glove = 11,
    Ring = 12,
    /// <summary>Wands, plus the rope and banner, which use the same slot.</summary>
    Wand = 13,
    Belt = 14,
    Boots = 15,
    Potion = 16,
    Scroll = 17,
    KeyItem = 18,
    Amulet = 19,
    DialogEntity = 20,
}

/// <summary>
/// The game's u16 record ID: a <see cref="DbType"/> in the high byte and an index in the low
/// byte. The ROM tables store it little-endian.
/// </summary>
public readonly record struct ItemId(ushort Value) : IComparable<ItemId>
{
    public static ItemId None => default;

    public DbType Type => (DbType)(Value >> 8);
    public byte Index => (byte)Value;
    public bool IsNone => Value == 0;

    public static ItemId Of(DbType type, byte index) => new((ushort)((int)type << 8 | index));

    public int CompareTo(ItemId other) => Value.CompareTo(other.Value);

    /// <summary>The ADR 0004 key spelling: <c>0x</c> and four uppercase hex digits.</summary>
    public override string ToString() => "0x" + Value.ToString("X4", CultureInfo.InvariantCulture);
}
