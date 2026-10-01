namespace Aidyn.Plugins.Records;

/// <summary>
/// A DB record's name and portrait, keyed by its ItemID (<c>aidyn:entity/0x023F</c>). Dialogue
/// entities have only these; entities, spells, weapons, armor, items and loot tables have records of
/// their own, which read as this too.
/// </summary>
/// <param name="Portrait">
/// The image beside what an entity or a dialogue entity says (<c>EntityDB::GetPortrait</c>). None
/// for other records.
/// </param>
public sealed record DbRecord(RecordId Id, string Name, RecordId? Portrait = null);
