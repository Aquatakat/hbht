using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Audio;

/// <summary>
/// Which background music plays as the player comes into a chunk, by walking or a teleport (<c>Zones.SetMapSet</c>, 1.0
/// <c>0x80026264</c>).
/// </summary>
public static class MapMusic
{
    /// <summary>The chunk's own music, else one of the set's at random (<c>Rng.Below</c>), else none, which stops it.</summary>
    public static RecordId? For(MapSetRecord set, ChunkEntry? chunk, IRandom random) =>
        chunk?.Music ?? (set.Music is { Count: > 0 } music ? music[(int)random.Below((uint)music.Count)] : null);
}
