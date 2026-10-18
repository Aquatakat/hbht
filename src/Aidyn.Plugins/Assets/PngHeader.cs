using System.Buffers.Binary;

namespace Aidyn.Plugins.Assets;

/// <summary>What a PNG asset's header says, read without decoding it.</summary>
public static class PngHeader
{
    /// <summary>A PNG's width and height in pixels, from its header, or null if it isn't a PNG.</summary>
    public static (int Width, int Height)? Size(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var file = File.OpenRead(path);
        if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || header[12..16].SequenceCompareTo("IHDR"u8) != 0)
            return null;
        return (BinaryPrimitives.ReadInt32BigEndian(header[16..]), BinaryPrimitives.ReadInt32BigEndian(header[20..]));
    }
}
