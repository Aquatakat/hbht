namespace HBHT.Rom.Engine;

/// <summary>
/// A chunk scene the zone engine swaps for another while a flag holds: the dark lighthouse's for the lit one's.
/// </summary>
/// <param name="Scene">The Borg5 a chunk's first scene is tested for.</param>
/// <param name="Flag">The flag read as the chunk loads.</param>
/// <param name="Lit">The Borg5 that takes its place while the flag holds.</param>
public readonly record struct ToggledScene(int Scene, int Flag, int Lit);

/// <summary>
/// The lighthouse's lit scenes (<c>CheckToggleZoneScene</c>, <c>0x8000aa14</c> in 1.0, <c>./src/zoneengine.cpp</c>). As each cell of
/// the 3×3 is filled from the map set's chunk list (<c>MakeGameZoneNames</c>, <c>0x8000aa94</c>), the function tests the cell's first
/// scene, alone of its fields, for two Borg5s, Overworld A15's and Lighthouse A05's, and while flag 5017 holds puts the lit one in its
/// place. The second scene, the map and everything else are left as they are, and the lit scenes are in no chunk list.
/// </summary>
/// <remarks>
/// The instructions from the first test on are the same in all three ROMs but for the calls' targets: <c>addiu v0, zero, scene</c> and a
/// <c>beq</c> for each test, then for each a call of <c>FlagNetwork.Get</c> with the flag in its delay slot, <c>addiu a0, zero, flag</c>,
/// and the lit scene's <c>addiu v0, zero, lit</c> two instructions on. The reader takes the immediates and checks the opcodes.
/// </remarks>
public static class ToggledScenes
{
    public static IReadOnlyList<ToggledScene> Read(RomImage rom)
    {
        var code = rom.Table(CodeTable.ToggledZoneScenes);
        return
        [
            new(Immediate(code, 0x00, AddiuV0, "first scene"), Immediate(code, 0x20, AddiuA0, "first flag"), Immediate(code, 0x28, AddiuV0, "first lit scene")),
            new(Immediate(code, 0x08, AddiuV0, "second scene"), Immediate(code, 0x38, AddiuA0, "second flag"), Immediate(code, 0x40, AddiuV0, "second lit scene")),
        ];
    }

    private const uint AddiuV0 = 0x2402, AddiuA0 = 0x2404; // addiu (9) rs 0, rt 2 or 4

    private static int Immediate(ReadOnlySpan<byte> code, int at, uint high, string what)
    {
        var word = Be.U32(code, at);
        if (word >> 16 != high)
            throw new InvalidDataException($"CheckToggleZoneScene's {what} isn't an addiu from zero where it's expected (0x{word:X8}).");
        return (int)(word & 0xFFFF);
    }
}
