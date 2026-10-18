using System.Numerics;

namespace Aidyn.Plugins.Records;

/// <summary>
/// How a new game starts (<c>aidyn:newgame/0</c>, the only one): where the player arrives, and who the party is.
/// </summary>
/// <param name="Chunk">
/// The chunk the player arrives in: the Overworld's portal 0, I08, which a new game hands <c>Zones.SetMapSet</c>
/// (<c>0x80026264</c>, row 9 and column 8 at <c>0x80026374</c>) from the title screen (<c>Title.Leave</c>, <c>0x80055718</c>).
/// </param>
/// <param name="Position">Where in the chunk, x and z: (16, 62), the code's floats at <c>0x800ce7d8</c> and <c>0x800ce7dc</c>.</param>
/// <param name="Height">
/// The height the player's body is made at, 10, before it's set down by a sweep from on high (<c>InitGameTrek</c>'s
/// <c>AllocPlayer</c>, the literal at <c>0x80021018</c>).
/// </param>
/// <param name="Leader">
/// Who the party starts with and is led by (<c>init_DBs</c>): Alaron. The player's body is the leader's actor
/// (<see cref="EntityBody.Actor"/>), which for Alaron is the literal the title screen gives it (<c>0x25F9</c> at
/// <c>0x800557c0</c>).
/// </param>
/// <param name="Gold">The party's gold (<c>init_DBs</c>).</param>
public sealed record NewGameRecord(RecordId Id, ChunkRef Chunk, Vector2 Position, float Height, RecordId Leader, uint Gold);
