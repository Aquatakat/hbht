using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

/// <summary>
/// The RSP's 16-byte <c>Vtx</c>: position, a flag the hardware ignores, texture coordinates in
/// s10.5 fixed point, and four bytes that are a colour or, for lit geometry, a signed normal
/// plus alpha.
/// </summary>
public readonly record struct N64Vertex(short X, short Y, short Z, ushort Flag, short S, short T, byte R, byte G, byte B, byte A)
{
    public const int Size = 16;

    public static N64Vertex Read(ReadOnlySpan<byte> b) => new(
        BinaryPrimitives.ReadInt16BigEndian(b),
        BinaryPrimitives.ReadInt16BigEndian(b[2..]),
        BinaryPrimitives.ReadInt16BigEndian(b[4..]),
        BinaryPrimitives.ReadUInt16BigEndian(b[6..]),
        BinaryPrimitives.ReadInt16BigEndian(b[8..]),
        BinaryPrimitives.ReadInt16BigEndian(b[10..]),
        b[12], b[13], b[14], b[15]);

    /// <summary>The normal of a lit vertex, as signed bytes of length about 127.</summary>
    public (sbyte X, sbyte Y, sbyte Z) Normal => ((sbyte)R, (sbyte)G, (sbyte)B);
}
