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

    /// <summary>
    /// Whether a <c>G_VTX</c> loaded it second of a pair: the 2nd, 4th, … vertex of its load (<see cref="F3dex2"/>), not of the
    /// buffer. F3DEX2 transforms and lights two vertices at once, the first by each light's <c>colc</c> and the second by its
    /// <c>col</c>, and the game's engine lights zero <c>col</c> (<c>SceneLights.Add</c>, <c>0x8009e838</c>), so on a lit list
    /// they reach only the first of each pair. Not part of the 16 bytes; false as read.
    /// </summary>
    public bool SecondOfPair { get; init; }

    /// <summary>The normal of a lit vertex, as signed bytes of length about 127.</summary>
    public (sbyte X, sbyte Y, sbyte Z) Normal => ((sbyte)R, (sbyte)G, (sbyte)B);
}
