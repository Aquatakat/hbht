using System.Security.Cryptography;

namespace Aidyn.Rom;

/// <summary>Thrown for a ROM that is a valid N64 image but not a version in <see cref="RomVersions"/>.</summary>
public sealed class UnsupportedRomException(string sha1, RomHeader header)
    : Exception($"Unsupported ROM (SHA-1 {sha1}, name \"{header.Name}\", code \"{header.GameCode}\", revision {header.Revision}). " +
                $"Supported: {string.Join(", ", RomVersions.All.Select(v => v.Name))}.")
{
    public string Sha1 { get; } = sha1;
    public RomHeader Header { get; } = header;
}

/// <summary>An identified ROM, normalised to big-endian (roadmap 0.2, ADR 0007).</summary>
public sealed class RomImage
{
    private readonly byte[] _data;

    private RomImage(byte[] data, RomByteOrder originalOrder, string sha1, RomHeader header, RomVersion version)
    {
        _data = data;
        OriginalByteOrder = originalOrder;
        Sha1 = sha1;
        Header = header;
        Version = version;
    }

    /// <summary>The whole image, big-endian.</summary>
    public ReadOnlySpan<byte> Data => _data;

    public RomByteOrder OriginalByteOrder { get; }
    public string Sha1 { get; }
    public RomHeader Header { get; }
    public RomVersion Version { get; }
    public RomLayout Layout => Version.Layout;

    public static RomImage Load(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>
    /// Identifies a ROM dump in any byte order. <paramref name="data"/> is normalised to
    /// big-endian in place and owned by the returned image.
    /// </summary>
    /// <exception cref="InvalidDataException">Not an N64 ROM.</exception>
    /// <exception cref="UnsupportedRomException">An N64 ROM, but not a known version.</exception>
    public static RomImage FromBytes(byte[] data)
    {
        var order = RomByteOrders.Detect(data);
        RomByteOrders.Swap(data, order);

        var header = RomHeader.Parse(data);
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(data));
        var version = RomVersions.FindBySha1(sha1) ?? throw new UnsupportedRomException(sha1, header);
        return new RomImage(data, order, sha1, header, version);
    }

    /// <summary>A bounds-checked view of <paramref name="range"/>.</summary>
    public ReadOnlySpan<byte> Slice(RomRange range) => Slice(range.Offset, range.Length);

    public ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _data.Length - length)
            throw new InvalidDataException($"Range 0x{offset:X}+0x{length:X} is outside the {_data.Length:X}-byte ROM.");
        return _data.AsSpan(offset, length);
    }
}
