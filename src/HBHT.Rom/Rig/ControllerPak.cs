using System.Buffers.Binary;

namespace HBHT.Rom.Rig;

/// <summary>
/// A Controller Pak's 32 KB, in libultra's file system (<c>osPfs*</c>), as ares keeps one beside the ROM
/// (<c>&lt;rom basename&gt;.pak</c>). The game's saves are notes in it (see docs/systems/saves.md).
/// </summary>
/// <remarks>
/// 128 pages of 256 bytes. Page 0 holds the pak's ID block four times (at 0x20, 0x60, 0x80 and 0xC0), page 1
/// the inode table and page 2 its copy, pages 3 and 4 the 16 notes' directory, and pages 5 to 127 the notes'
/// data. An inode entry is the next page of a note's chain, 1 for its last page, or 3 for a free page; the
/// table's checksum, in entry 0's low byte, is the sum of the bytes of entries 5 to 127.
/// </remarks>
public sealed class ControllerPak
{
    public const int Size = 0x8000, PageSize = 0x100, Pages = 128, FirstDataPage = 5, MaxNotes = 16;
    private const int InodePage = 1, InodeCopyPage = 2, DirectoryOffset = 3 * PageSize, NoteSize = 32;
    private const ushort LastPage = 1, FreePage = 3;
    /// <summary>A note in use (<c>DIR_STATUS_OCCUPIED</c>).</summary>
    private const byte Occupied = 2;

    private readonly byte[] _data;

    private ControllerPak(byte[] data) => _data = data;

    /// <summary>
    /// A pak read from a file: a 32 KB <c>.pak</c>, or a Mupen64Plus <c>.mpk</c>, whose first 32 KB are
    /// controller 1's pak.
    /// </summary>
    public static ControllerPak Read(string path) => Read(File.ReadAllBytes(path));

    public static ControllerPak Read(byte[] data)
    {
        if (data.Length < Size || data.Length % Size != 0)
            throw new InvalidDataException($"A Controller Pak is {Size:N0} bytes (or four of them, as Mupen64Plus keeps), not {data.Length:N0}.");
        var pak = new ControllerPak(data[..Size]);
        if (!pak.HasValidId)
            throw new InvalidDataException("The pak's ID block's checksum is wrong: it isn't formatted.");
        return pak;
    }

    /// <summary>
    /// A formatted, empty pak, as ares formats a new one: its label blank, its ID block that of the pak ares made
    /// beside the user's 1.1 ROM, and every page free.
    /// </summary>
    public static ControllerPak Blank()
    {
        var data = new byte[Size];
        ReadOnlySpan<byte> id =
        [
            0x00, 0x37, 0x00, 0x00, 0x00, 0x06, 0x41, 0x08, 0x05, 0x0D, 0x94, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];
        foreach (var at in IdBlocks)
        {
            id.CopyTo(data.AsSpan(at));
            SealId(data.AsSpan(at, NoteSize));
        }
        var pak = new ControllerPak(data);
        for (var page = FirstDataPage; page < Pages; page++)
            pak.SetInode(page, FreePage);
        pak.SealInodes();
        return pak;
    }

    private static readonly int[] IdBlocks = [0x20, 0x60, 0x80, 0xC0];

    /// <summary>Whether the first ID block's checksums hold, as <c>__osGetId</c> checks them.</summary>
    private bool HasValidId
    {
        get
        {
            var block = _data.AsSpan(IdBlocks[0], NoteSize);
            var (sum, inverse) = IdSums(block);
            return BinaryPrimitives.ReadUInt16BigEndian(block[0x1C..]) == sum && BinaryPrimitives.ReadUInt16BigEndian(block[0x1E..]) == inverse;
        }
    }

    /// <summary>The ID block's checksum, the sum of its first 14 halfwords, and its inverse, 0xFFF2 less it.</summary>
    private static (ushort Sum, ushort Inverse) IdSums(ReadOnlySpan<byte> block)
    {
        var sum = 0;
        for (var i = 0; i < 0x1C; i += 2)
            sum += BinaryPrimitives.ReadUInt16BigEndian(block[i..]);
        return ((ushort)sum, (ushort)(0xFFF2 - sum));
    }

    private static void SealId(Span<byte> block)
    {
        var (sum, inverse) = IdSums(block);
        BinaryPrimitives.WriteUInt16BigEndian(block[0x1C..], sum);
        BinaryPrimitives.WriteUInt16BigEndian(block[0x1E..], inverse);
    }

    /// <summary>The notes in use, by their place in the directory.</summary>
    public IReadOnlyList<PakNote> Notes
    {
        get
        {
            var notes = new List<PakNote>();
            for (var i = 0; i < MaxNotes; i++)
            {
                var entry = _data.AsSpan(DirectoryOffset + i * NoteSize, NoteSize);
                if (BinaryPrimitives.ReadUInt32BigEndian(entry) == 0 || BinaryPrimitives.ReadUInt16BigEndian(entry[4..]) == 0)
                    continue;
                notes.Add(new PakNote(i,
                    System.Text.Encoding.ASCII.GetString(entry[..4]),
                    System.Text.Encoding.ASCII.GetString(entry[4..6]),
                    entry[0x10..0x20].ToArray(), entry[0xC..0x10].ToArray(),
                    BinaryPrimitives.ReadUInt16BigEndian(entry[6..]) & 0xFF,
                    Chain(BinaryPrimitives.ReadUInt16BigEndian(entry[6..]) & 0xFF).Count));
            }
            return notes;
        }
    }

    /// <summary>The note with this game code, company, name and extension, if the pak has one.</summary>
    public PakNote? Find(string game, string company, ReadOnlySpan<byte> name, ReadOnlySpan<byte> extension)
    {
        foreach (var note in Notes)
        {
            if (note.Game == game && note.Company == company && note.Name.AsSpan().SequenceEqual(name) && note.Extension.AsSpan().SequenceEqual(extension))
                return note;
        }
        return null;
    }

    /// <summary>A note's data, its pages' whole bytes in chain order.</summary>
    public byte[] ReadNote(PakNote note)
    {
        var pages = Chain(note.StartPage);
        var data = new byte[pages.Count * PageSize];
        for (var i = 0; i < pages.Count; i++)
            _data.AsSpan(pages[i] * PageSize, PageSize).CopyTo(data.AsSpan(i * PageSize));
        return data;
    }

    /// <summary>
    /// Writes a note, replacing one of the same game, company, name and extension: its pages are freed, and the
    /// lowest free pages taken in order, as many as <paramref name="data"/> fills, padded with zeroes to a page.
    /// </summary>
    /// <param name="name">The note's name, 16 bytes in the N64 font (<see cref="FontText"/>).</param>
    /// <param name="extension">Its extension, 4 bytes in the N64 font.</param>
    public PakNote WriteNote(string game, string company, ReadOnlySpan<byte> name, ReadOnlySpan<byte> extension, ReadOnlySpan<byte> data)
    {
        if (game.Length != 4 || company.Length != 2)
            throw new ArgumentException("A note's game code is 4 characters and its company 2.");
        if (name.Length != 16 || extension.Length != 4)
            throw new ArgumentException("A note's name is 16 bytes and its extension 4.");
        if (Find(game, company, name, extension) is { } old)
            Delete(old);

        var needed = Math.Max(1, (data.Length + PageSize - 1) / PageSize);
        var free = Enumerable.Range(FirstDataPage, Pages - FirstDataPage).Where(p => Inode(p) == FreePage).Take(needed).ToList();
        if (free.Count < needed)
            throw new InvalidOperationException($"The pak has {free.Count} free pages, and the note needs {needed}.");
        var slot = Enumerable.Range(0, MaxNotes).FirstOrDefault(i => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(DirectoryOffset + i * NoteSize)) == 0, -1);
        if (slot < 0)
            throw new InvalidOperationException($"The pak already holds {MaxNotes} notes.");

        for (var i = 0; i < needed; i++)
        {
            SetInode(free[i], i + 1 < needed ? (ushort)free[i + 1] : LastPage);
            var page = _data.AsSpan(free[i] * PageSize, PageSize);
            page.Clear();
            var start = i * PageSize;
            if (start < data.Length)
                data[start..Math.Min(data.Length, start + PageSize)].CopyTo(page);
        }
        SealInodes();

        var entry = _data.AsSpan(DirectoryOffset + slot * NoteSize, NoteSize);
        entry.Clear();
        System.Text.Encoding.ASCII.GetBytes(game, entry);
        System.Text.Encoding.ASCII.GetBytes(company, entry[4..]);
        BinaryPrimitives.WriteUInt16BigEndian(entry[6..], (ushort)free[0]);
        entry[8] = Occupied;
        extension.CopyTo(entry[0xC..]);
        name.CopyTo(entry[0x10..]);
        return Notes.First(n => n.Index == slot);
    }

    /// <summary>Deletes a note: frees its pages and clears its directory entry, as <c>osPfsDeleteFile</c> does.</summary>
    public void Delete(PakNote note)
    {
        foreach (var page in Chain(note.StartPage))
            SetInode(page, FreePage);
        SealInodes();
        _data.AsSpan(DirectoryOffset + note.Index * NoteSize, NoteSize).Clear();
    }

    public byte[] ToArray() => (byte[])_data.Clone();

    public void Write(string path) => File.WriteAllBytes(path, _data);

    /// <summary>The pages of a note's chain, from its start page.</summary>
    private List<int> Chain(int start)
    {
        var pages = new List<int>();
        for (var page = start; page is >= FirstDataPage and < Pages; page = Inode(page))
        {
            if (pages.Count >= Pages - FirstDataPage || pages.Contains(page))
                throw new InvalidDataException($"The note starting at page {start} has a broken chain.");
            pages.Add(page);
            if (Inode(page) == LastPage)
                return pages;
        }
        throw new InvalidDataException($"The note starting at page {start} has a broken chain.");
    }

    private ushort Inode(int page) => (ushort)(BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(InodePage * PageSize + page * 2)) & 0xFF);

    private void SetInode(int page, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(_data.AsSpan(InodePage * PageSize + page * 2), value);

    /// <summary>Writes the inode table's checksum and copies the table to its backup page, as <c>__osPfsRWInode</c> does.</summary>
    private void SealInodes()
    {
        var table = _data.AsSpan(InodePage * PageSize, PageSize);
        var sum = 0;
        foreach (var b in table[(FirstDataPage * 2)..])
            sum += b;
        table[1] = (byte)sum;
        table.CopyTo(_data.AsSpan(InodeCopyPage * PageSize));
    }
}

/// <summary>A note in a pak's directory.</summary>
/// <param name="Index">Its place in the directory, 0 to 15.</param>
/// <param name="Game">The game's code, such as <c>NAYE</c>.</param>
/// <param name="Company">The company's code, such as <c>78</c>.</param>
/// <param name="Name">Its name, 16 bytes in the N64 font.</param>
/// <param name="Extension">Its extension, 4 bytes in the N64 font.</param>
/// <param name="StartPage">Its first page.</param>
/// <param name="Pages">How many pages it fills.</param>
public sealed record PakNote(int Index, string Game, string Company, byte[] Name, byte[] Extension, int StartPage, int Pages)
{
    public string Label => FontText.Decode(Name) + (Extension[0] == 0 ? "" : "." + FontText.Decode(Extension));
}

/// <summary>
/// The N64's font codes, which notes' names are written in: 0 ends the name, 0x0F is a space, 0x10–0x19 the
/// digits and 0x1A–0x33 the capitals.
/// </summary>
public static class FontText
{
    public static string Decode(ReadOnlySpan<byte> codes)
    {
        var text = new System.Text.StringBuilder();
        foreach (var c in codes)
        {
            if (c == 0)
                break;
            text.Append(c switch
            {
                0x0F => ' ',
                >= 0x10 and <= 0x19 => (char)('0' + c - 0x10),
                >= 0x1A and <= 0x33 => (char)('A' + c - 0x1A),
                _ => '?',
            });
        }
        return text.ToString();
    }

    /// <summary><paramref name="text"/> in font codes, padded with zeroes to <paramref name="length"/>.</summary>
    public static byte[] Encode(string text, int length)
    {
        if (text.Length > length)
            throw new ArgumentException($"\"{text}\" is longer than {length}.", nameof(text));
        var codes = new byte[length];
        for (var i = 0; i < text.Length; i++)
        {
            codes[i] = char.ToUpperInvariant(text[i]) switch
            {
                ' ' => 0x0F,
                >= '0' and <= '9' and var d => (byte)(0x10 + d - '0'),
                >= 'A' and <= 'Z' and var l => (byte)(0x1A + l - 'A'),
                _ => throw new ArgumentException($"The N64 font has no '{text[i]}'.", nameof(text)),
            };
        }
        return codes;
    }
}
