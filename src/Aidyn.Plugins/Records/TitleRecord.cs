namespace Aidyn.Plugins.Records;

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
/// <param name="Row">The Overworld chunk the scenery is loaded round, as <c>Zones.Row</c>: 1-based, as in <see cref="ChunkEntry.Row"/>.</param>
/// <param name="Column">As <see cref="Row"/>, <c>Zones.Column</c>.</param>
/// <param name="Unread">
/// Two more u16s in the table, which <c>Title.StartScenery</c> stores at <c>0x800D90DC</c> and <c>0x800D90DE</c> and nothing in 1.0's
/// code reads. Each is within a few chunks of the row and column, so they may be a second chunk of the camera's route. Kept as they are.
/// </param>
/// <param name="NeedsExpansionPak">
/// Skipped without the Expansion Pak (<c>Memory.HasExpansionPak</c>): the one at place 4, which goes on to place 5.
/// </param>
public sealed record TitleFlycam(RecordId Scene, RecordId Animation, int Row, int Column, IReadOnlyList<int> Unread, bool NeedsExpansionPak = false);
