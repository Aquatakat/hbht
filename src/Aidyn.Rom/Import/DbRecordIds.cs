using Aidyn.Plugins;
using Aidyn.Rom.Db;

namespace Aidyn.Rom.Import;

/// <summary>Names the records that the game's u16 DB IDs and flag numbers stand for (ADR 0004).</summary>
internal static class DbRecordIds
{
    /// <summary>
    /// The record an ID names, keyed by the whole u16, such as <c>aidyn:entity/0x023F</c>. Null for
    /// IDs that have no record: none, rituals, and bytes the game never defined,
    /// such as the tools' fill value 0xCC.
    /// </summary>
    public static RecordId? For(ItemId id)
    {
        var type = id.Type switch
        {
            DbType.Entity => RecordTypes.Entity,
            DbType.DialogEntity => RecordTypes.DialogEntity,
            DbType.Spell => RecordTypes.Spell,
            DbType.Loot => RecordTypes.Loot,
            DbType.Weapon => RecordTypes.Weapon,
            DbType.Armor or DbType.Shield => RecordTypes.Armor,
            DbType.Misc or DbType.Helmet or DbType.Cloak or DbType.Glove or DbType.Ring or DbType.Wand
                or DbType.Belt or DbType.Boots or DbType.Potion or DbType.Scroll or DbType.KeyItem or DbType.Amulet => RecordTypes.Item,
            _ => null,
        };
        return type is null ? null : RecordId.Hex(type, id.Value);
    }

    /// <summary>The record <paramref name="id"/> names, or an error that says where it was found if there is none.</summary>
    /// <param name="where">What names the ID, such as "Borg9 123", which the error starts with.</param>
    public static RecordId Required(ItemId id, string? where = null) =>
        For(id) ?? throw new InvalidDataException($"{(where is null ? "" : where + ": ")}DB ID {id} has no record.");

    /// <summary>
    /// The record of an item in a console's inventory or hands, by its ID: weapons are 0x07…, armor and shields 0x05… and
    /// 0x06…, and the rest items.
    /// </summary>
    public static RecordId Item(ushort id) => For(new ItemId(id)) ?? RecordId.Hex(RecordTypes.Item, id);

    /// <summary>The record of event flag <paramref name="flag"/>, such as <c>aidyn:flag/3612</c>.</summary>
    public static RecordId Flag(int flag) => RecordId.Decimal(RecordTypes.Flag, flag);
}
