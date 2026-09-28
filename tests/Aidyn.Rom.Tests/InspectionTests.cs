using System.Numerics;
using System.Text;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Inspection;

namespace Aidyn.Rom.Tests;

public class InspectionTests
{
    private enum Colour : byte { Red = 1, Green = 12 }

    [Flags]
    private enum Bits { None = 0, A = 1, B = 0x10 }

    private sealed record Inner(int Value, string Name);

    private sealed record Outer(Vector3 Position, Colour Colour, Bits Bits, Inner Child, IReadOnlyList<int> Items, (int, float) Pair)
    {
        public int Doubled => Child.Value * 2;
    }

    [Fact]
    public void RecordsExpandIntoTheirPropertiesInOrder()
    {
        var node = FieldTree.Of("x", new Outer(new Vector3(1, 2.5f, -3), Colour.Green, Bits.A | Bits.B, new Inner(21, "hi"), [1, 2], (3, 0.5f)));

        Assert.Equal("Outer", node.Value);
        Assert.Equal(
            ["Position = (1, 2.5, -3)", "Colour = Green (0xC)", "Bits = A, B (0x11)", "Child = Inner", "Items = Int32[2]",
             "Pair = (3, 0.5)", "Doubled = 42 (0x2A)"],
            node.Children.Select(c => c.ToString()));
        Assert.Equal(["Value = 21 (0x15)", "Name = \"hi\""], node.Children[3].Children.Select(c => c.ToString()));
        Assert.Equal(["[0] = 1", "[1] = 2"], node.Children[4].Children.Select(c => c.ToString()));
    }

    [Fact]
    public void LongListsArePaged()
    {
        var node = FieldTree.Of("list", Enumerable.Range(0, 70_000).ToArray());

        Assert.Equal("Int32[70000]", node.Value);
        // 70,000 items need pages of 65,536, each split into pages of 256.
        Assert.Equal(["[0…65535]", "[65536…69999]"], node.Children.Select(c => c.Name));
        Assert.Equal(256, node.Children[0].Children.Count);
        Assert.Equal("[256…511]", node.Children[0].Children[1].Name);
        Assert.Equal("[300] = 300 (0x12C)", node.Children[0].Children[1].Children[44].ToString());
        Assert.Equal(18, node.Children[1].Children.Count);
    }

    [Fact]
    public void HexDumpShowsOffsetsBytesAndText()
    {
        var hex = BorgInspector.Hex(Encoding.ASCII.GetBytes("Borg archive, 1.0\n"));

        Assert.Equal(
            "000000  42 6F 72 67 20 61 72 63  68 69 76 65 2C 20 31 2E  Borg archive, 1.\n" +
            "000010  30 0A                                             0.\n",
            hex);
    }

    /// <summary>
    /// Every Borg with a decoder decodes and summarises, and its field tree can be walked two
    /// levels down (the first page of each list) without a property throwing.
    /// </summary>
    [Fact]
    public void EveryDecodableBorgHasFieldsAndASummary()
    {
        var inspector = new BorgInspector(TestRoms.Get(RomVersionId.Na10));
        var decoded = 0;
        foreach (var listing in inspector.Archive.Listings)
        {
            if (!BorgInspector.CanDecode(listing.Type))
            {
                Assert.Null(inspector.Decode(listing.Index));
                continue;
            }

            var value = inspector.Decode(listing.Index);
            Assert.NotNull(value);
            Assert.NotEmpty(BorgInspector.Summary(value));
            var fields = FieldTree.Of("borg", value);
            foreach (var child in fields.Children)
            {
                Assert.DoesNotContain("‹", child.Value, StringComparison.Ordinal);
                foreach (var grandchild in child.Children)
                    Assert.DoesNotContain("‹", grandchild.Value, StringComparison.Ordinal);
            }
            decoded++;
        }
        Assert.Equal(14585 - 149 - 476, decoded);
    }

    [Fact]
    public void PreviewsResolveTheBorgsTheyNeed()
    {
        var inspector = new BorgInspector(TestRoms.Get(RomVersionId.Na10));

        // The Gwernia house, Brenna's actor and the first shot of the opening, as in the reference rig.
        Assert.Null(inspector.Model(2558)!.Animation);
        Assert.Equal("borg6 8729", inspector.Model(8729)!.Animation);
        Assert.StartsWith("In scene 8728.", inspector.Model(8729)!.Note, StringComparison.Ordinal);
        var brenna = inspector.Model(10723)!;
        Assert.StartsWith("In scene 10696,", brenna.Note, StringComparison.Ordinal);
        Assert.NotNull(brenna.Animation);

        foreach (var type in new[] { BorgType.Geometry, BorgType.MapChunk })
        {
            var first = inspector.Archive.Listings.First(l => l.Type == type).Index;
            Assert.Equal("glTF"u8.ToArray(), inspector.Model(first)!.Glb[..4]);
        }

        var texture = inspector.Archive.Listings.First(l => l.Type == BorgType.Texture).Index;
        Assert.Equal(Borg1Texture.Parse(inspector.Archive.Read(texture)).LevelCount, inspector.Images(texture)!.Count);
        var dialogue = inspector.Archive.Listings.First(l => l.Type == BorgType.Dialogue).Index;
        Assert.NotEmpty(inspector.Text(dialogue)!);
        Assert.Null(inspector.Model(texture));
    }
}
