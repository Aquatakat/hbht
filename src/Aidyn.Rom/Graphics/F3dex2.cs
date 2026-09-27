using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

public readonly record struct N64Triangle(N64Vertex A, N64Vertex B, N64Vertex C);

/// <summary>
/// Just enough of the F3DEX2 microcode to turn a geometry display list into triangles: vertex
/// loads, texture-coordinate edits, triangles, and the end marker. State commands (render
/// modes, textures, matrices) belong to whoever calls the list, so anything else is rejected.
/// </summary>
public static class F3dex2
{
    public const int VertexBufferSize = 32;

    private const byte GVtx = 0x01;
    private const byte GModifyVtx = 0x02;
    private const byte GCullDl = 0x03;
    private const byte GTri1 = 0x05;
    private const byte GTri2 = 0x06;
    private const byte GEndDl = 0xDF;

    /// <summary><c>G_MWO_POINT_ST</c>, the only <c>G_MODIFYVTX</c> target supported.</summary>
    private const byte ModifySt = 0x14;

    /// <summary>
    /// Runs the display list at <paramref name="offset"/> in <paramref name="data"/> and returns
    /// its triangles, in order. <paramref name="segments"/> maps segment numbers to the memory
    /// that <c>G_VTX</c> addresses in that segment point into.
    /// </summary>
    /// <remarks>
    /// <c>G_CULLDL</c> is ignored: it only skips the rest of the list when its vertices are all
    /// off screen, and here everything is drawn.
    /// </remarks>
    public static List<N64Triangle> Run(ReadOnlySpan<byte> data, int offset, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> segments)
    {
        var buffer = new N64Vertex?[VertexBufferSize];
        var triangles = new List<N64Triangle>();
        for (var pc = offset; ; pc += 8)
        {
            if (pc + 8 > data.Length)
                throw new InvalidDataException($"The display list at 0x{offset:X} runs off the end without G_ENDDL.");
            var w0 = BinaryPrimitives.ReadUInt32BigEndian(data[pc..]);
            var w1 = BinaryPrimitives.ReadUInt32BigEndian(data[(pc + 4)..]);
            switch ((byte)(w0 >> 24))
            {
                case GVtx:
                    LoadVertices(buffer, w0, w1, segments);
                    break;
                case GModifyVtx:
                    var where = (byte)(w0 >> 16);
                    if (where != ModifySt)
                        throw new InvalidDataException($"G_MODIFYVTX of 0x{where:X2} at 0x{pc:X} isn't supported.");
                    var index = (int)(w0 & 0xFFFF) >> 1;
                    buffer[index] = Loaded(buffer, index, pc) with { S = (short)(w1 >> 16), T = (short)w1 };
                    break;
                case GCullDl:
                    break;
                case GTri1:
                    triangles.Add(Triangle(buffer, w0, pc));
                    break;
                case GTri2:
                    triangles.Add(Triangle(buffer, w0, pc));
                    triangles.Add(Triangle(buffer, w1, pc));
                    break;
                case GEndDl:
                    return triangles;
                default:
                    throw new InvalidDataException($"Display-list command 0x{w0 >> 24:X2} at 0x{pc:X} isn't supported.");
            }
        }
    }

    private static void LoadVertices(N64Vertex?[] buffer, uint w0, uint w1, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> segments)
    {
        var count = (int)(w0 >> 12) & 0xFF;
        var end = (int)(w0 & 0xFF) >> 1;
        var first = end - count;
        if (first < 0 || end > VertexBufferSize)
            throw new InvalidDataException($"G_VTX loads vertices {first} to {end - 1}.");

        var segment = (int)(w1 >> 24) & 0xF;
        var address = (int)(w1 & 0xFFFFFF);
        if (!segments.TryGetValue(segment, out var memory))
            throw new InvalidDataException($"G_VTX reads from segment 0x{segment:X}, which isn't set.");
        if (address + count * N64Vertex.Size > memory.Length)
            throw new InvalidDataException($"G_VTX reads {count} vertices at 0x{w1:X8}, past the segment's end.");

        var source = memory.Span[address..];
        for (var i = 0; i < count; i++)
            buffer[first + i] = N64Vertex.Read(source[(i * N64Vertex.Size)..]);
    }

    /// <summary>A triangle from the low three bytes of a word, which hold buffer indices times 2.</summary>
    private static N64Triangle Triangle(N64Vertex?[] buffer, uint word, int pc) => new(
        Loaded(buffer, (int)(word >> 16 & 0xFF) >> 1, pc),
        Loaded(buffer, (int)(word >> 8 & 0xFF) >> 1, pc),
        Loaded(buffer, (int)(word & 0xFF) >> 1, pc));

    private static N64Vertex Loaded(N64Vertex?[] buffer, int index, int pc) =>
        (index < VertexBufferSize ? buffer[index] : null)
        ?? throw new InvalidDataException($"The command at 0x{pc:X} uses vertex {index}, which isn't loaded.");
}
