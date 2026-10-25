namespace HBHT.Rom.Engine;

/// <summary>
/// The textures particle emitters draw with (<c>partTextInds</c>, <c>ZoneEngine.cpp</c>): Borg1
/// archive indices by emitter texture slot, read from the code segment. Empty slots are null.
/// </summary>
public static class ParticleTextures
{
    public static IReadOnlyList<int?> Read(RomImage rom)
    {
        return [.. Be.S32s(rom.Table(CodeTable.ParticleTextures)).Select(index => index >= 0 ? index : (int?)null)];
    }
}
