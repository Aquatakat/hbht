using Aidyn.Plugins;
using Aidyn.Rom.Db;

namespace Aidyn.Rom.Import;

/// <summary>Names the records that the game's u16 DB IDs stand for (ADR 0004).</summary>
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
}
