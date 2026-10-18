using System.Buffers.Binary;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class FontTests
{
    private static int GlyphOf(Fonts fonts, char c) => fonts.Glyphs[c - Fonts.FirstCharacter];

    [Fact]
    public void TheFacesMapAndButtonColoursAreRead()
    {
        var fonts = Fonts.Read(TestRoms.Get(RomVersionId.Na10));

        Assert.Equal([new FontFace(0x1C, 46, 3), new FontFace(0x1D, 46, 3)], fonts.Faces);

        Assert.Equal(108, fonts.Glyphs.Count);
        Assert.Equal(16, GlyphOf(fonts, ' '));
        Assert.Equal(0, GlyphOf(fonts, '0'));
        Assert.Equal(0x3F, GlyphOf(fonts, 'A'));
        Assert.Equal(0x11, GlyphOf(fonts, 'a'));
        Assert.Equal(0x6D, GlyphOf(fonts, '~'));
        Assert.Equal(0x5C, GlyphOf(fonts, '\u007F'));
        Assert.Equal([Fonts.NotDrawn, Fonts.NotDrawn, Fonts.NotDrawn], new[] { '#', '[', ']' }.Select(c => GlyphOf(fonts, c)));
        Assert.Equal(0x2B, Fonts.NotDrawn);

        Assert.Equal(11, fonts.ButtonColors.Count);
        Assert.Equal(((byte)0, (byte)0, (byte)164), fonts.ButtonColors[0]);
        Assert.Equal(((byte)0, (byte)164, (byte)0), fonts.ButtonColors[1]);
        Assert.All(fonts.ButtonColors.Skip(2).Take(4), c => Assert.Equal(((byte)200, (byte)164, (byte)0), c));
        Assert.Equal(((byte)128, (byte)0, (byte)0), fonts.ButtonColors[0x85 - Fonts.FirstButton]);
        Assert.All(fonts.ButtonColors.Skip(7), c => Assert.Equal(((byte)90, (byte)90, (byte)90), c));
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheTablesMatchAcrossVersions(RomVersionId id)
    {
        var a = Fonts.Read(TestRoms.Get(RomVersionId.Na10));
        var b = Fonts.Read(TestRoms.Get(id));

        Assert.Equal(a.Faces, b.Faces);
        Assert.Equal(a.Glyphs, b.Glyphs);
        Assert.Equal(a.ButtonColors, b.ButtonColors);
    }

    [Fact]
    public void GlyphWidthsAreMeasuredFromTheMainFontsSheet()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var fonts = Fonts.Read(rom);
        var sheet = Borg8Image.Parse(BorgArchive.Open(rom).Read(fonts.Faces[0].Image));
        Assert.Equal((Borg8Format.Ia8, 36, 414), (sheet.Format, sheet.Width, sheet.Height));

        var widths = Fonts.MeasureGlyphs(sheet, 46, 3);

        Assert.Equal(138, widths.Count);
        Assert.Equal(3, widths[GlyphOf(fonts, ' ')]);
        Assert.Equal(8, widths[GlyphOf(fonts, '0')]);
        Assert.Equal(11, widths[GlyphOf(fonts, 'M')]);
        Assert.Equal(4, widths[GlyphOf(fonts, 'i')]);
        // The L and R buttons' glyphs are blank in the top row, so the original never shows them.
        Assert.Equal([0, 0], new[] { 0x65, 0x66 }.Select(g => widths[g]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryPrintableCharacterHasAGlyphWithAWidth(int face)
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var fonts = Fonts.Read(rom);
        var f = fonts.Faces[face];
        var widths = Fonts.MeasureGlyphs(Borg8Image.Parse(BorgArchive.Open(rom).Read(f.Image)), f.Rows, f.Columns);

        Assert.Equal(138, widths.Count);
        var glyphs = Enumerable.Range('!', '~' - '!' + 1).Select(c => GlyphOf(fonts, (char)c)).Where(g => g != Fonts.NotDrawn).ToList();
        Assert.Equal(91, glyphs.Count);
        Assert.All(glyphs, g => Assert.InRange(widths[g], 1, 12));
    }

    /// <summary>A Borg8 of the given format whose bitmap is <paramref name="bitmap"/>, with no palette.</summary>
    private static Borg8Image Sheet(Borg8Format format, int width, int height, byte[] bitmap)
    {
        var data = new byte[Borg8Image.HeaderSize + bitmap.Length];
        BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)format);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)height);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0xC), Borg8Image.HeaderSize);
        bitmap.CopyTo(data, Borg8Image.HeaderSize);
        return Borg8Image.Parse(data);
    }

    private static byte[] Texels(int bytesPerTexel, params int[] values)
    {
        var bytes = new byte[values.Length * bytesPerTexel];
        for (var i = 0; i < values.Length; i++)
        {
            var at = bytes.AsSpan(i * bytesPerTexel);
            if (bytesPerTexel == 1)
                at[0] = (byte)values[i];
            else if (bytesPerTexel == 2)
                BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)values[i]);
            else
                BinaryPrimitives.WriteUInt32BigEndian(at, (uint)values[i]);
        }
        return bytes;
    }

    [Theory]
    [InlineData(Borg8Format.Ia8)]
    [InlineData(Borg8Format.I8)]
    public void An8BitCellEndsAfterTheLastTexelThatDiffersFromItsOwnRightmost(Borg8Format format)
    {
        // Four 4 × 2 cells in a row. Only each cell's top row counts.
        var sheet = Sheet(format, 16, 2, Texels(1,
            1, 1, 2, 2, /**/ 3, 4, 4, 5, /**/ 8, 7, 7, 7, /**/ 6, 6, 6, 6,
            9, 9, 9, 9, /**/ 9, 9, 9, 9, /**/ 9, 9, 9, 9, /**/ 9, 9, 9, 9));

        Assert.Equal([2, 3, 1, 0], Fonts.MeasureGlyphs(sheet, 1, 4));
    }

    [Theory]
    [InlineData(Borg8Format.Ia16, 2)]
    [InlineData(Borg8Format.Rgba16, 2)]
    [InlineData(Borg8Format.Rgba32, 4)]
    public void AWiderCellsReferenceIsTheNextTexelInMemory(Borg8Format format, int bytesPerTexel)
    {
        // Cell 0's rightmost (3) differs from the next texel (6), so it's full width. Cell 1's (5) equals
        // cell 2's first, so it ends after the 8. Cell 2 is 1 wide. Cell 3 is the last in its row, so
        // its reference is the first texel of the row below: 9.
        var sheet = Sheet(format, 16, 2, Texels(bytesPerTexel,
            1, 2, 3, 3, /**/ 6, 4, 8, 5, /**/ 5, 7, 7, 7, /**/ 7, 7, 9, 9,
            9, 0, 0, 0, /**/ 0, 0, 0, 0, /**/ 0, 0, 0, 0, /**/ 0, 0, 0, 0));

        Assert.Equal([4, 3, 1, 2], Fonts.MeasureGlyphs(sheet, 1, 4));
    }

    [Theory]
    [InlineData(Borg8Format.Ia4)]
    [InlineData(Borg8Format.I4)]
    public void A4BitSheetIsNotSupported(Borg8Format format)
    {
        var sheet = Sheet(format, 16, 2, new byte[16]);

        Assert.Throws<NotSupportedException>(() => Fonts.MeasureGlyphs(sheet, 1, 4));
    }
}
