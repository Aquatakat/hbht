using System.Buffers.Binary;

namespace Aidyn.Rom.Rig;

/// <summary>
/// The RAM of an ares save state (<c>.bs1</c>–<c>.bs9</c>), for checking the port against the
/// checkpoints the user has captured (see docs/reference-rig.md).
/// </summary>
/// <remarks>
/// ares stores RDRAM whole, as 32-bit little-endian words, but doesn't say where in the file. Rather
/// than fingerprint the code segment against a ROM (<c>tools/bst.py</c>), this finds the game's flag
/// table (<c>GameStateFunnel</c>), whose header is unmistakable, and takes RDRAM's start from where the
/// header sits and the address its own pointers give it. So it needs no ROM and reads 1.0's states and
/// the debug ROM's alike.
/// </remarks>
public sealed class AresCheckpoint
{
    private const int RdramSize = 0x800000;
    private const int FlagCount = 5627, BitFlagCount = 5001;
    /// <summary>The funnel's header, then its flag entries (each 8 bytes, the value at +6).</summary>
    private const int HeaderSize = 0x20, EntrySize = 8, ValueOffset = 6;

    private readonly byte[] _state;
    private readonly int _base;
    private readonly uint _funnel;
    private bool[]? _flags;

    private AresCheckpoint(byte[] state, int rdramStart, uint funnel)
    {
        _state = state;
        _base = rdramStart;
        _funnel = funnel;
    }

    /// <summary>Where the flag table is (<c>GameStateFunnel</c>), a KSEG0 address.</summary>
    public uint FlagTableAddress => _funnel;

    /// <summary>Every flag's value, by number.</summary>
    public IReadOnlyList<bool> Flags => _flags ??= [.. Enumerable.Range(0, FlagCount)
        .Select(i => ReadByte(_funnel + HeaderSize + (uint)(i * EntrySize + ValueOffset)) != 0)];

    public static AresCheckpoint Read(string path) => Read(File.ReadAllBytes(path));

    public static AresCheckpoint Read(byte[] state)
    {
        if (state.Length < 4 || state[0] != 'B' || state[1] != 'S' || state[2] != 'T' || state[3] != '1')
            throw new InvalidDataException("Not an ares state (no BST1 magic).");

        (int Offset, uint Address)? found = null;
        // The words are little-endian in the file: the flag count's bytes are 2 and 3 of the header's
        // first word, the bit flag count's are 6 and 7.
        for (var i = 4; i + HeaderSize <= state.Length; i++)
        {
            if (state[i + 2] != FlagCount % 256 || state[i + 3] != FlagCount / 256
                || state[i + 6] != BitFlagCount % 256 || state[i + 7] != BitFlagCount / 256)
                continue;

            var entries = Word(state, i + 8);
            var inputs = Word(state, i + 12);
            if ((entries & 3) != 0 || entries < 0x80000000 + HeaderSize || entries >= 0x80000000 + RdramSize
                || inputs != entries + FlagCount * EntrySize)
                continue;
            var address = entries - HeaderSize;
            var start = i - (int)(address & 0x7FFFFF);
            if (start < 4 || start + RdramSize > state.Length)
                continue;
            if (found is not null)
                throw new InvalidDataException("The flag table is in this state more than once.");
            found = (start, address);
        }
        if (found is not { } f)
            throw new InvalidDataException("Can't find the flag table in this state, so can't tell where RAM is.");
        return new AresCheckpoint(state, f.Offset, f.Address);
    }

    /// <summary>A byte of RAM, by KSEG0 address (0x80000000–0x807FFFFF).</summary>
    public byte ReadByte(uint address) => _state[_base + (Physical(address) ^ 3)];

    /// <summary>A big-endian word of RAM, by 4-byte-aligned KSEG0 address.</summary>
    public uint ReadWord(uint address)
    {
        if ((address & 3) != 0)
            throw new ArgumentException($"{address:x8} isn't word-aligned.", nameof(address));
        return Word(_state, _base + Physical(address));
    }

    /// <summary>A big-endian word at a word-aligned file offset, whose bytes the file holds reversed.</summary>
    private static uint Word(byte[] state, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(offset, 4));

    private static int Physical(uint address)
    {
        if (address is < 0x80000000 or >= 0x80000000 + RdramSize)
            throw new ArgumentOutOfRangeException(nameof(address), $"{address:x8} isn't in KSEG0's RDRAM.");
        return (int)(address & 0x7FFFFF);
    }
}
