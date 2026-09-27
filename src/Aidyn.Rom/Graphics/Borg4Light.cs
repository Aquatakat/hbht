using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

/// <summary>A Borg4: a light's colour. The scene places it (see <see cref="Borg5Scene.Lights"/>).</summary>
/// <remarks>The data is 1.0, the colour as three floats from 0 to 1, and two words that are always 0.</remarks>
public sealed record Borg4Light(float Red, float Green, float Blue)
{
    public const int Size = 0x18;

    public static Borg4Light Parse(byte[] data)
    {
        if (data.Length != Size)
            throw new InvalidDataException($"A Borg4 is 0x{Size:X} bytes, not 0x{data.Length:X}.");

        float F(int offset) => BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset));
        return new Borg4Light(F(4), F(8), F(0xC));
    }
}
