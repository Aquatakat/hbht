using System.Buffers.Binary;

namespace HBHT.Rom.Graphics;

/// <summary>
/// A Borg3: a perspective projection. Every Borg5 scene names one. There are only five, and they
/// differ only in their clipping planes. Distances are in scene units (see <see cref="Borg5Scene"/>).
/// </summary>
/// <remarks>
/// The data is six floats (0, the vertical field of view in degrees, near, far, 1, aspect), an
/// offset to the viewport (always 0x20), a word that's always 0, and an N64 <c>Vp</c>.
/// </remarks>
public sealed record Borg3Camera(float FieldOfViewY, float Near, float Far, float Aspect)
{
    public const int Size = 0x30;

    public static Borg3Camera Parse(byte[] data)
    {
        if (data.Length != Size)
            throw new InvalidDataException($"A Borg3 is 0x{Size:X} bytes, not 0x{data.Length:X}.");

        float F(int offset) => BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset));
        return new Borg3Camera(F(4), F(8), F(0xC), F(0x14));
    }
}
