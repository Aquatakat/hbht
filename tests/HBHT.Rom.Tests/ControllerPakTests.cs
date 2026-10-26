using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

public class ControllerPakTests
{
    /// <summary>The paks beside the ROMs: 1.0's with the four story saves, and 1.1's as ares formatted it, empty.</summary>
    public const string Na10Pak = "Aidyn Chronicles - The First Mage (U) (V1.0) [!].pak", Na11Pak = "Aidyn Chronicles - The First Mage (U) (V1.1).pak";

    public static byte[] Pak(string name)
    {
        var path = Path.Combine(TestRoms.Folder, name);
        if (!File.Exists(path))
            Assert.Skip($"No {name} in {TestRoms.Folder}.");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void ReadsTheFourSaves()
    {
        var notes = ControllerPak.Read(Pak(Na10Pak)).Notes;
        Assert.Equal(["CHRONICLES.A", "CHRONICLES.B", "CHRONICLES.C", "CHRONICLES.D"], notes.Select(n => n.Label));
        Assert.All(notes, n => Assert.Equal(("NAYE", "78", 28), (n.Game, n.Company, n.Pages)));
        Assert.Equal([5, 33, 61, 89], notes.Select(n => n.StartPage));
    }

    [Fact]
    public void BlankIsAsAresFormatsOne() => Assert.Equal(Pak(Na11Pak), ControllerPak.Blank().ToArray());

    [Fact]
    public void WritingTheSavesIntoABlankPakLaysThemOutAsTheGameDid()
    {
        var real = Pak(Na10Pak);
        var source = ControllerPak.Read(real);
        var pak = ControllerPak.Blank();
        foreach (var note in source.Notes)
            pak.WriteNote(note.Game, note.Company, note.Name, note.Extension, source.ReadNote(note));

        var written = pak.ToArray();
        // The inode tables, and every note's data, match byte for byte. The ID blocks are the blank pak's, and the
        // directory's reserved bytes held stale values.
        Assert.Equal(real.AsSpan(0x100, 0x200).ToArray(), written.AsSpan(0x100, 0x200).ToArray());
        Assert.Equal(real.AsSpan(5 * 0x100).ToArray(), written.AsSpan(5 * 0x100).ToArray());
        Assert.Equal(source.Notes.Select(n => (n.Label, n.StartPage, n.Pages)), pak.Notes.Select(n => (n.Label, n.StartPage, n.Pages)));
    }

    [Fact]
    public void ReplacingANoteFreesItsPages()
    {
        var pak = ControllerPak.Blank();
        var name = FontText.Encode("CHRONICLES", 16);
        pak.WriteNote("NAYE", "78", name, FontText.Encode("A", 4), new byte[7168]);
        pak.WriteNote("NAYE", "78", name, FontText.Encode("B", 4), new byte[7168]);
        var again = pak.WriteNote("NAYE", "78", name, FontText.Encode("A", 4), new byte[300]);
        Assert.Equal((0, 5, 2), (again.Index, again.StartPage, again.Pages));
        Assert.Equal(["CHRONICLES.A", "CHRONICLES.B"], pak.Notes.Select(n => n.Label));
        Assert.Equal(pak.ToArray(), ControllerPak.Read(pak.ToArray()).ToArray());
    }
}
