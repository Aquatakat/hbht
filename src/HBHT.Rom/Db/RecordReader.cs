using System.Buffers.Binary;
using HBHT.Rom.Text;

namespace HBHT.Rom.Db;

/// <summary>A bounds-checked cursor over a table's bytes, for reading packed records in order.</summary>
internal ref struct RecordReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; private set; }
    public readonly int Remaining => _data.Length - Position;

    public ReadOnlySpan<byte> Bytes(int count)
    {
        if (count < 0 || count > Remaining)
            throw new InvalidDataException($"Read of {count} bytes at 0x{Position:X} runs past the end of the {_data.Length}-byte table.");
        var span = _data.Slice(Position, count);
        Position += count;
        return span;
    }

    public byte U8() => Bytes(1)[0];
    public sbyte S8() => (sbyte)U8();
    public ushort U16Le() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
    public ushort U16Be() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2));
    public short S16Be() => BinaryPrimitives.ReadInt16BigEndian(Bytes(2));
    public uint U32Be() => BinaryPrimitives.ReadUInt32BigEndian(Bytes(4));

    /// <summary>An <see cref="ItemId"/> as the DB tables store it, little-endian.</summary>
    public ItemId Id() => new(U16Le());

    public ItemId[] Ids(int count)
    {
        var ids = new ItemId[count];
        for (var i = 0; i < count; i++)
            ids[i] = Id();
        return ids;
    }

    public byte[] Array(int count) => Bytes(count).ToArray();

    public string Name(int length) => GameText.Field(Bytes(length));

    /// <summary>
    /// A DB array header: a count byte and three junk bytes. The game reads a whole table's
    /// header, then one per group within it.
    /// </summary>
    public byte Count()
    {
        var count = U8();
        Bytes(3);
        return count;
    }

    public readonly bool StartsWith(ReadOnlySpan<byte> marker) => _data[Position..].StartsWith(marker);
}
