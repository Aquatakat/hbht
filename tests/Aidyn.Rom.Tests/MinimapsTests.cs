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

    /// <summary>Trigger objects set every flag the minimaps read.</summary>
    [Fact]
    public void MinimapFlagsHaveSetters()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var minimaps = Minimaps.Read(rom);
        var triggers = QuestAtlas.Build(rom).References.Where(r => r.Role == FlagRole.TriggerSets).Select(r => r.Flag).ToHashSet();
        var flags = new[] { minimaps.Ehud, minimaps.RoogEntrance, minimaps.Barrows }.SelectMany(l => l).Select(m => m.Flag).ToList();

        Assert.Equal([.. Enumerable.Range(4222, 16), .. Enumerable.Range(4286, 9)], flags.Select(f => (int)f).Order());
        Assert.All(flags, f => Assert.Contains(f, triggers));
    }
}
