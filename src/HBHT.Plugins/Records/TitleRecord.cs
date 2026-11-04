namespace HBHT.Plugins.Records;

/// <summary>
/// The title screen (<c>aidyn:title/0</c>, the only one): the fly-throughs of the Overworld behind its menu (<c>Title.StartScenery</c>,
/// <c>0x80054D70</c>, from the table <c>gFlycamSequences</c> at <c>0x800E09DC</c>).
/// </summary>
/// <param name="Flycams">
/// In the order they play: the first when the title is first built, the next each time a fly-through ends and the title builds its
/// scenery again, after the last the first. The original passes over the fifth without the Expansion Pak (<c>Memory.HasExpansionPak</c>);
/// the port always plays as with one.
/// </param>
public sealed record TitleRecord(RecordId Id, IReadOnlyList<TitleFlycam> Flycams);

/// <summary>
/// One fly-through: a camera animation over a map set, drawn with the 3×3 chunks round <paramref name="Chunk"/>.
/// </summary>
/// <param name="Scene">The scene the animation drives, whose camera the shot is seen through (its Borg6's scene).</param>
/// <param name="Animation">The Borg6 that moves the camera.</param>
/// <param name="Length">
/// The animation's length in ticks. The fly-through fades out over its last 100 and the next is built
/// (<c>Title.SceneryFrame</c>, <c>0x80054F70</c>).
/// </param>
/// <param name="Chunk">
/// The chunk the scenery is loaded round: an Overworld chunk in the original, whose row and column it sets as <c>Zones.Row</c> and
/// <c>Zones.Column</c>.
/// </param>
/// <param name="Origin">
/// The chunk whose corner the animation's coordinates are from, in the same map set: <c>Title.StartScenery</c> stores its row at
/// <c>0x800D90DC</c> and its column at <c>0x800D90DE</c>. Every fly-through's camera runs a few units over the ground from that corner,
/// and from no other.
/// </param>
public sealed record TitleFlycam(RecordId Scene, RecordId Animation, int Length, RecordId Chunk, RecordId Origin);
