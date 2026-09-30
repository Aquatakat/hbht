namespace Aidyn.Plugins.Records;

/// <summary>
/// A DB record's name: an entity, a dialogue entity, a weapon, armor, a spell or an item, keyed by
/// its ItemID (<c>aidyn:entity/0x023F</c>). Scripts and tools name them. Their other fields come
/// with the systems that use them: characters (2.2) and items (2.3).
/// </summary>
public sealed record DbRecord(RecordId Id, string Name);
