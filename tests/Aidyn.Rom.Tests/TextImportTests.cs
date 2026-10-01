using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Import;

namespace Aidyn.Rom.Tests;

public class TextImportTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    /// <summary>The line counts of the tables that differ between versions: Common, Controller and Credits.</summary>
    public static TheoryData<RomVersionId, int, int, int> LineCounts() => new()
    {
        { RomVersionId.Na10, 555, 21, 133 },
        // 1.1 appended Controller Pak messages.
        { RomVersionId.Na11, 560, 25, 133 },
        { RomVersionId.Debug, 555, 21, 130 },
    };

    [Theory]
    [MemberData(nameof(LineCounts))]
    public void StringTablesAreNumberedInRomOrder(RomVersionId id, int common, int controller, int credits)
    {
        var records = BaseImporter.BuildStrings(GameDb.Read(TestRoms.Get(id)));

        Assert.Equal(12, records.Count);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => RecordId.Decimal(RecordTypes.Strings, i)), records.Select(r => r.Id));
        Assert.Equal(["Items", "Stats", "Spells", "Skills", "PotionDetails", "Common", "Copyright", "Controller", "Combat", "Cheats", "Credits", "PotionNames"],
            records.Select(r => r.Name));

        var counts = records.ToDictionary(r => r.Name, r => r.Lines.Count);
        Assert.Equal(common, counts["Common"]);
        Assert.Equal(controller, counts["Controller"]);
        Assert.Equal(credits, counts["Credits"]);
        Assert.Equal([82, 6, 55, 18, 15, 12, 64, 17, 17],
            new[] { "Items", "Stats", "Spells", "Skills", "PotionDetails", "Copyright", "Combat", "Cheats", "PotionNames" }.Select(n => counts[n]));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void StringEscapesAreDecoded(RomVersionId id)
    {
        var db = GameDb.Read(TestRoms.Get(id));
        var records = BaseImporter.BuildStrings(db);

        foreach (var record in records)
        {
            var raw = db.Strings[Enum.Parse<RomStringTable>(record.Name)];
            Assert.Equal(raw.Count, record.Lines.Count);
            Assert.All(record.Lines, line => Assert.DoesNotContain('\\', line));
            for (var i = 0; i < raw.Count; i++)
            {
                var expected = raw[i].Replace("\\n", "\n").Replace("\\x83", "\u0083").Replace("\\x84", "\u0084");
                Assert.Equal(expected, record.Lines[i]);
            }
        }

        // Common's button glyphs are C-down (line 488 and 521) and C-up (497), where the source had the escapes.
        var rawCommon = db.Strings[RomStringTable.Common];
        var importedCommon = records.Single(r => r.Name == "Common").Lines;
        Assert.Equal([488, 521], Indexes(rawCommon, l => l.Contains("\\x83")));
        Assert.Equal([497], Indexes(rawCommon, l => l.Contains("\\x84")));
        Assert.Equal([488, 521], Indexes(importedCommon, l => l.Contains('\u0083')));
        Assert.Equal([497], Indexes(importedCommon, l => l.Contains('\u0084')));
        Assert.Equal(rawCommon.Count(l => l.Contains("\\n")), importedCommon.Count(l => l.Contains('\n')));
        Assert.Contains(importedCommon, l => l.Contains('\n'));
    }

    private static List<int> Indexes(IReadOnlyList<string> lines, Func<string, bool> test) =>
        [.. lines.Select((l, i) => (l, i)).Where(p => test(p.l)).Select(p => p.i)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void FontsHaveTheirSheetsMeasuresAndMaps(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var records = BaseImporter.BuildFonts(Fonts.Read(rom), BorgArchive.Open(rom));

        Assert.Equal([RecordId.Decimal(RecordTypes.Font, 0), RecordId.Decimal(RecordTypes.Font, 1)], records.Select(r => r.Id));
        Assert.Equal([RecordId.Hex(RecordTypes.Image, 0x1C), RecordId.Hex(RecordTypes.Image, 0x1D)], records.Select(r => r.Image));
        Assert.All(records, r =>
        {
            Assert.Equal((46, 3, 12, 9), (r.Rows, r.Columns, r.CellWidth, r.CellHeight));
            Assert.Equal(138, r.Widths.Count);

            Assert.DoesNotContain("#", r.Glyphs.Keys);
            Assert.DoesNotContain("[", r.Glyphs.Keys);
            Assert.DoesNotContain("]", r.Glyphs.Keys);
            var drawn = Enumerable.Range(0x20, 0x8B - 0x20 + 1).Select(c => ((char)c).ToString()).Where(c => c is not ("#" or "[" or "]"));
            Assert.Equal(drawn.Order(StringComparer.Ordinal), r.Glyphs.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(0x3F, r.Glyphs["A"]);
            Assert.Equal(16, r.Glyphs[" "]);

            Assert.NotNull(r.ButtonColors);
            Assert.Equal(Enumerable.Range(0x7F, 11).Select(c => ((char)c).ToString()), r.ButtonColors.Keys.Order(StringComparer.Ordinal));
            Assert.All(r.ButtonColors.Values, c => Assert.Equal(255, c.A));
            Assert.Equal(new Rgba(0, 0, 164, 255), r.ButtonColors["\u007F"]);
            Assert.Equal(new Rgba(128, 0, 0, 255), r.ButtonColors["\u0085"]);
        });
        Assert.Equal(11, records[0].Widths[records[0].Glyphs["M"]]);
    }

    [Fact]
    public void EntitiesAndDialogueEntitiesHavePortraits()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var db = GameDb.Read(rom);
        var models = EntityModels.Read(rom);
        var names = BaseImporter.BuildNames(db, models).ToDictionary(f => f.File, f => f.Records);
        var entities = CharacterImporter.BuildEntities(db, models);

        Assert.Equal(221, entities.Count);
        Assert.All(entities, r => Assert.NotNull(r.Portrait));
        Assert.Equal(RecordId.Hex(RecordTypes.Image, 0x0108), entities.Single(r => r.Id == RecordId.Hex(RecordTypes.Entity, 0x023F)).Portrait);

        Assert.Equal((37, 32), (names["dialogentities.json"].Count, names["dialogentities.json"].Count(r => r.Portrait is not null)));
        Assert.All(names["dialogentities.json"].Where(r => r.Portrait is not null), r => Assert.Equal(RecordTypes.Image, r.Portrait!.Value.Type));

        Assert.DoesNotContain("entities.json", names.Keys);
        Assert.DoesNotContain("spells.json", names.Keys);
        // Weapons, armor, items and loot tables have records of their own (ItemImporter).
        foreach (var file in new[] { "weapons.json", "armor.json", "items.json", "loot.json" })
            Assert.DoesNotContain(file, names.Keys);
    }
}
