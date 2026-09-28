namespace Aidyn.Rom.Engine;

/// <summary>
/// The sky each terrain type has (<c>BorgMaps::GetMapTerrain</c>), which the chunk the player is in
/// chooses. A sky is a Borg8 gradient, 12 × 37 RGBA32: rows 0–31 are the sky from top to bottom and
/// rows 32–36 the scenery's tint and the sky, fog, cloud and sun colours (<c>Sky::SetColors</c>),
/// with a column every two hours from midnight.
/// </summary>
public static class Skies
{
    /// <summary>The gradients' Borg8 archive indices by sky number, read from the code segment. Number 0 is unused.</summary>
    public static IReadOnlyList<int> ReadGradients(RomImage rom)
    {
        var data = CodeSegment.Table(rom, CodeTable.SkyGradients);
        var gradients = new int[data.Length / 4];
        for (var i = 0; i < gradients.Length; i++)
            gradients[i] = (int)CodeSegment.U32(data, i * 4);
        return gradients;
    }

    /// <summary>
    /// A terrain type's sky number, and whether the gradient is drawn behind the scenery. Where it
    /// isn't (the original's sky type 4), the background is black and weather is off, but the
    /// colours still come from the gradient. The code holds this as a switch, which this carries as
    /// a fact, checked against the debug ROM's jump table.
    /// </summary>
    public static (int Sky, bool Drawn) ForTerrain(int terrain) => terrain switch
    {
        3 or 26 => (3, true),
        4 => (5, true),
        6 or 11 or 13 or 14 or 15 or 16 => (6, false),
        8 => (7, false),
        9 => (8, false),
        10 => (4, true),
        18 => (2, true),
        _ => (1, true),
    };

    /// <summary>The terrain types, which <c>World::SetTerrain</c> checks.</summary>
    public const int TerrainCount = 28;
}
