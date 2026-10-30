namespace HBHT.Plugins.Records;

/// <summary>
/// A DB record's name and portrait, keyed by its ItemID (<c>aidyn:entity/0x023F</c>). Dialogue
/// entities have only these; entities, spells, weapons, armor, items and loot tables have records of
/// their own, which read as this too.
/// </summary>
/// <param name="Portrait">
/// The image beside what an entity or a dialogue entity says (<c>EntityDB::GetPortrait</c>). None
/// for other records.
/// </param>
/// <param name="Body">How an entity or a dialogue entity stands in the world. None for other records.</param>
public sealed record DbRecord(RecordId Id, string Name, RecordId? Portrait = null, EntityBody? Body = null);

/// <summary>
/// How an entity or a dialogue entity stands in the world, as a wanderer or a cut-scene's actor
/// (<c>gEntityExtras</c>, <c>dailougEnt_info_array</c>; <c>EntityDB::GetBorg7</c>, <c>GetCollideRadius</c>,
/// <c>GetScale</c> and <c>GetHeight</c>).
/// </summary>
/// <param name="Actor">Its model. A record the tables leave out is a bat, as the code makes it.</param>
/// <param name="Radius">Its collision sphere's radius, before <paramref name="Scale"/>. Every dialogue entity's is 0.55, which the code gives them.</param>
/// <param name="Scale">How much bigger than its actor's model it's drawn, and how much bigger its sphere is when bodies push each other.</param>
/// <param name="Height">About how tall its model is at a scale of 1, in scene units.</param>
/// <param name="Strike">
/// The share of its attack's animation at which the blow lands and the target starts to react (<c>EntityBody.StrikePoint</c>,
/// <c>0x8006C7FC</c>, which <c>ProcessPlayers</c> reads in combat); 0.4 for a dialogue entity or one the tables leave out.
/// </param>
/// <param name="ShotStrike">
/// The share of its bow's shot at which the arrow leaves. The original has no such field: every entity's shot and throw leave at 0.4
/// (<c>EntityBody.ShotStrike</c>, <c>0x8006C884</c>), which the port makes the entity's, each apart, for mods (the user's choice).
/// </param>
/// <param name="ThrowStrike">The share of its throw at which what it throws leaves its hand, as <paramref name="ShotStrike"/>.</param>
/// <param name="StopReach">
/// Under Modern movement, the port's, as it leads the party in trek: the furthest the rest of its step carries it once the stick is
/// let go, before its scale. The original has no such field, and plays the whole step (docs/systems/movement.md#modern-movement).
/// </param>
/// <param name="StopPace">Under Modern movement, how many times as fast the rest of its step plays once the stick is let go.</param>
/// <param name="Stride">Under Modern movement, the ticks its speed is the mean root motion of, or 1 for none.</param>
/// <param name="SetOffTurn">
/// Under Modern movement, how many degrees a tick it turns toward the stick as it sets off from standing, until its animation's
/// root first moves it. The original's is 10, as on the move.
/// </param>
public sealed record EntityBody(RecordId Actor, float Radius, float Scale, float Height, float Strike = EntityBody.DefaultStrike,
    float ShotStrike = EntityBody.DefaultShotStrike, float ThrowStrike = EntityBody.DefaultShotStrike,
    float StopReach = EntityBody.DefaultStopReach, float StopPace = EntityBody.DefaultStopPace, int Stride = EntityBody.DefaultStride,
    float SetOffTurn = EntityBody.DefaultSetOffTurn)
{
    /// <summary>The strike's share for a record the tables leave out (<c>0x800D2CF8</c>).</summary>
    public const float DefaultStrike = 0.4f;

    /// <summary>Every entity's share for a shot or a throw in the original (<c>0x800D2CFC</c>).</summary>
    public const float DefaultShotStrike = 0.4f;

    /// <summary>
    /// What the importer gives every entity for Modern movement, the user's tuning to Alaron's run (Oct 30, 2026): 0.3 is about a
    /// quarter of its step of 1.2, and 10 ticks about half its loop of 19.
    /// </summary>
    public const float DefaultStopReach = 0.3f, DefaultStopPace = 3, DefaultSetOffTurn = 20;

    /// <inheritdoc cref="DefaultStopReach"/>
    public const int DefaultStride = 10;
}
