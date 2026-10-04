using Aidyn.Rom.Borg;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Tests;

public class MinimapsTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void MinimapsMatchAcrossVersions(RomVersionId id)
    {
        var a = Minimaps.Read(TestRoms.Get(RomVersionId.Na10));
        var b = Minimaps.Read(TestRoms.Get(id));

        Assert.Equal(a.Sections.Count, b.Sections.Count);
        for (var i = 0; i < a.Sections.Count; i++)
            Assert.Equal(a.Sections[i], b.Sections[i]);
        Assert.Equal(a.Ehud, b.Ehud);
        Assert.Equal(a.RoogEntrance, b.RoogEntrance);
        Assert.Equal(a.Barrows, b.Barrows);
    }

    [Fact]
    public void MinimapsNameTheirImages()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var minimaps = Minimaps.Read(rom);

        Assert.Equal([50, 7, 12, 76, 10, 5, 24, 4], minimaps.Sections.Select(s => s.Count));
        var images = minimaps.Sections.SelectMany(s => s).Select(s => s.Image)
            .Concat(new[] { minimaps.Ehud, minimaps.RoogEntrance, minimaps.Barrows }.SelectMany(l => l).Select(m => m.Image));
        Assert.All(images, i => Assert.Equal(BorgType.UiImage, archive.Listings[i].Type));

        // The flag lists' images are pieces of the Ehud, Roog entrance and Barrows lists.
        Assert.Equal(Enumerable.Range(0x68, 11), minimaps.Ehud.Select(m => m.Image));
        Assert.Equal(Enumerable.Range(0x1E, 5), minimaps.RoogEntrance.Select(m => m.Image).Order());
        Assert.Equal(minimaps.Sections[5].Select(s => s.Image).Order(), minimaps.RoogEntrance.Select(m => m.Image).Order());
    }

    [Fact]
    public void SomePiecesAreSeenFromTheStart()
    {
        var minimaps = Minimaps.Read(TestRoms.Get(RomVersionId.Na10));

        // Gwernia castle's courtyard, all five pieces, and its tower's upper floor; the mage school's stairs' upper floors.
        var seen = minimaps.Sections.SelectMany((list, i) => list.Where(s => s.Seen).Select(s => (i, s.Image)));
        Assert.Equal([(0, 0x37), (0, 0x38), (0, 0x29), (0, 0x2A), (0, 0x2B), (0, 0x9D), (4, 0xA7), (4, 0xAA)], seen);
    }

    [Fact]
    public void TheBarrowsRulesUseTheBarrowsFlags()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var minimaps = Minimaps.Read(rom);
        var barrows = Import.MinimapImporter.Build(minimaps).Single(m => m.MapSet.Key == "9");

        // The code's literals are the flags of the Barrows list, though some show another piece than the list's.
        Assert.Equal(minimaps.Barrows.Select(b => (int)b.Flag).Order(), barrows.Rules.Where(r => r.Flag is not null)
            .Select(r => int.Parse(r.Flag!.Value.Key)).Distinct().Order());
        Assert.Equal(24, barrows.Sections.Count);
    }
}
