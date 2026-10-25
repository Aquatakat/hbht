namespace HBHT.Plugins.Records;

/// <summary>
/// The title screen (<c>aidyn:title/0</c>, the only one): the fly-throughs of the Overworld behind its menu (<c>Title.StartScenery</c>,
/// <c>0x80054D70</c>, from the table <c>gFlycamSequences</c> at <c>0x800E09DC</c>).
/// </summary>
/// <param name="Flycams">
/// In the order they play: the first when the title is first built, the next each time a fly-through ends and the title builds its
/// scenery again, after the last the first. Without the Expansion Pak the one that needs it (<see cref="TitleFlycam.NeedsExpansionPak"/>) is passed over.
/// </param>
public sealed record TitleRecord(RecordId Id, IReadOnlyList<TitleFlycam> Flycams);

/// <summary>
/// One fly-through: a camera animation over the Overworld, drawn with the 3×3 chunks round (<paramref name="Row"/>, <paramref name="Column"/>).
/// </summary>
/// <param name="Scene">The scene the animation drives, whose camera the shot is seen through (its Borg6's scene).</param>
/// <param name="Animation">The Borg6 that moves the camera.</param>
/// <param name="Length">
/// The animation's length in ticks. The fly-through fades out over its last 100 and the next is built
/// (<c>Title.SceneryFrame</c>, <c>0x80054F70</c>).
/// </param>
/// <param name="Row">The Overworld chunk the scenery is loaded round, as <c>Zones.Row</c>: 1-based, as in <see cref="ChunkEntry.Row"/>.</param>
/// <param name="Column">As <see cref="Row"/>, <c>Zones.Column</c>.</param>
/// <param name="OriginRow">
/// The chunk whose corner the animation's coordinates are from: its row, which <c>Title.StartScenery</c> stores at <c>0x800D90DC</c>
/// (its column at <c>0x800D90DE</c>). Every fly-through's camera runs a few units over the ground from that corner, and from no other.
/// </param>
/// <param name="OriginColumn">As <see cref="OriginRow"/>.</param>
/// <param name="NeedsExpansionPak">
/// Skipped without the Expansion Pak (<c>Memory.HasExpansionPak</c>): the one at place 4, which goes on to place 5.
/// </param>
public sealed record TitleFlycam(RecordId Scene, RecordId Animation, int Length, int Row, int Column, int OriginRow, int OriginColumn,
    bool NeedsExpansionPak = false);
