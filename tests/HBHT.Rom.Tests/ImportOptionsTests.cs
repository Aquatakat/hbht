using HBHT.Plugins;
using HBHT.Rom.Import;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Tests;

/// <summary>
/// The player's choices at import (<see cref="ImportOptions"/>) and the ROM the editor's import page takes (<see cref="ImportableRom"/>).
/// The mushroom's move itself is <c>GameSessionTests.Mushroom.cs</c>'s, over the shared default import.
/// </summary>
public sealed class ImportOptionsTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public ImportOptionsTests() => Directory.CreateDirectory(_temp.Root);

    public void Dispose() => _temp.Dispose();

    private static (IReadOnlyList<MapChunk> Chunks, Dictionary<int, Borg9Map> Maps, int Borg9) RoogD01()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var chunks = MapChunks.Read(rom);
        var borg9 = chunks.Single(c => c.Set == MapSet.RoogCave && c.Name == "D01").Collision;
        return (chunks, new Dictionary<int, Borg9Map> { [borg9] = Borg9Map.Parse(Borg.BorgArchive.Open(rom).Read(borg9)) }, borg9);
    }

    [Fact]
    public void KeepingTheMushroomLeavesTheRomsMapAsItIs()
    {
        var (chunks, maps, borg9) = RoogD01();
        var rom = maps[borg9];
        MapCorrections.Apply(chunks, maps, new ImportOptions(FixMushroom: false));
        // The same map, so its object's position and its collision's vertices are the ROM's.
        Assert.Same(rom, maps[borg9]);
        Assert.Empty(MapCorrections.Moved(new ImportOptions(FixMushroom: false)));
    }

    [Fact]
    public void FixingTheMushroomIsTheDefault()
    {
        Assert.True(ImportOptions.Default.FixMushroom);
        var (chunks, maps, borg9) = RoogD01();
        var rom = maps[borg9];
        MapCorrections.Apply(chunks, maps, ImportOptions.Default);
        var moved = MapCorrections.RoogCaveMushroom;
        Assert.Equal(rom.Objects[moved.Object].Position + moved.By, maps[borg9].Objects[moved.Object].Position);
        Assert.Equal(rom.Vertices[rom.Triangles[moved.FirstTriangle].A] + moved.By, maps[borg9].Vertices[rom.Triangles[moved.FirstTriangle].A]);
    }

    private string Write(string name, ReadOnlySpan<byte> bytes)
    {
        var path = Path.Combine(_temp.Root, name);
        File.WriteAllBytes(path, bytes.ToArray());
        return path;
    }

    [Fact]
    public void TheImportPageTakesNa10InAnyByteOrder()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var swapped = rom.Data.ToArray();
        RomByteOrders.Swap(swapped, RomByteOrder.ByteSwapped);
        var opened = ImportableRom.Open(Write("rom.v64", swapped));
        Assert.Equal((RomVersionId.Na10, RomByteOrder.ByteSwapped), (opened.Version.Id, opened.OriginalByteOrder));
    }

    [Fact]
    public void TheImportPageRefusesOtherVersionsWithTheirNames()
    {
        var rom = TestRoms.Get(RomVersionId.Na11);
        var e = Assert.Throws<InvalidDataException>(() => ImportableRom.Open(Write("rom.z64", rom.Data)));
        Assert.Equal("This is the NA 1.1 ROM. A North American 1.0 ROM is required.", e.Message);
    }

    [Fact]
    public void TheImportPageSaysWhatsWrongWithAFileThatIsntTheGame()
    {
        Assert.StartsWith("This file isn't an N64 ROM.", Assert.Throws<InvalidDataException>(() => ImportableRom.Open(Write("notes.z64", "hello"u8))).Message,
            StringComparison.Ordinal);
        Assert.Equal("Choose a ROM to import.", Assert.Throws<InvalidDataException>(() => ImportableRom.Open(" ")).Message);
        Assert.Throws<FileNotFoundException>(() => ImportableRom.Open(Path.Combine(_temp.Root, "missing.z64")));

        // An N64 ROM that isn't one HBHT knows: NA 1.0 with a byte changed.
        var other = TestRoms.Get(RomVersionId.Na10).Data.ToArray();
        other[^1] ^= 0xFF;
        Assert.Contains("isn't a ROM of Aidyn Chronicles: The First Mage that HBHT knows",
            Assert.Throws<InvalidDataException>(() => ImportableRom.Open(Write("other.z64", other))).Message, StringComparison.Ordinal);
    }
}
