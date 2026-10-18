using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Engine;

/// <summary>
/// Which of the Battle arenas a battle takes by the terrain of the chunk it starts in (<c>BattleSetup.ChooseArena</c>,
/// <c>0x80008698</c> in 1.0): a switch through the jump table at <c>0x800CD400</c>, which this carries as a fact, checked
/// against 1.0's code. Each case is a roll over a range of arenas, or for terrains 20–22 one of two ranges, each half the
/// time.
/// </summary>
public static class Arenas
{
    /// <summary>The ranges a terrain's battles choose from, the first being the one-in-two's.</summary>
    public static IReadOnlyList<ArenaRange> ForTerrain(int terrain) => terrain switch
    {
        0 or 7 => [new(6, 7)],
        1 or 24 => [new(2, 3)],
        2 => [new(0, 1)],
        3 or 27 => [new(14, 15)],
        4 => [new(19, 19)],
        5 or 17 => [new(4, 5)],
        6 => [new(18, 18)],
        8 => [new(12, 13)],
        9 => [new(17, 17)],
        10 => [new(16, 16)],
        11 => [new(23, 23)],
        12 => [new(22, 22)],
        13 or 25 => [new(27, 27)],
        14 => [new(24, 24)],
        15 => [new(25, 25)],
        16 => [new(26, 26)],
        18 => [new(20, 20)],
        19 or 23 or 26 => [new(8, 11)],
        20 => [new(6, 7), new(8, 11)],
        21 => [new(2, 3), new(8, 11)],
        22 => [new(0, 1), new(8, 11)],
        _ => [],
    };
}
