namespace Aidyn.Rom.Engine;

/// <summary>
/// The textures particle emitters draw with (<c>partTextInds</c>, <c>ZoneEngine.cpp</c>): Borg1
/// archive indices by emitter texture slot, read from the code segment. Empty slots are null.
/// </summary>
public static class ParticleTextures
{
    public static IReadOnlyList<int?> Read(RomImage rom)
    {
        var data = CodeSegment.Table(rom, CodeTable.ParticleTextures);
        var slots = new int?[data.Length / 4];
        for (var i = 0; i < slots.Length; i++)
            slots[i] = (int)CodeSegment.U32(data, i * 4) is var index and >= 0 ? index : null;
        return slots;
    }
}
