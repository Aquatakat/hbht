using System.Numerics;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Tests;

/// <summary>
/// The last of the code's content tables (roadmap 1H): the position lists, the actors combat always animates, the title
/// screen's fly-throughs and the map sets' spawn points.
/// </summary>
public class ContentTablesTests
{
    /// <summary>1.0's code segment: a RAM address as an offset into the ROM.</summary>
    private static int Rom(uint address) => (int)(address - 0x80000400) + 0x1000;

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void ThePositionListsAreTheDbsOwnOrder(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var db = GameDb.Read(rom);

        PositionLists.Verify(rom, db);

        Assert.Equal((221, 37, 59, 68, 105, 197),
            (db.Entities.Count, db.DialogEntities.Count, db.Spells.Count, db.Armor.Count, db.Weapons.Count, db.Gear.Count));
    }

    [Fact]
    public void ThePositionListsHoldTheDbsIdsAsTheCodeWritesThem()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);

        // Bytes seen in the dump: ArmorList starts 3D 3F 41, weaponList 5B 4C 4B, and itemID_array 01 19, 01 20, 01 1F.
        Assert.Equal([0x3D, 0x3F, 0x41], rom.Table(CodeTable.ArmorList)[..3].ToArray());
        Assert.Equal([0x5B, 0x4C, 0x4B], rom.Table(CodeTable.WeaponList)[..3].ToArray());
        Assert.Equal([0x0119, 0x0120, 0x011F], Be.U16s(rom.Table(CodeTable.ItemList)[..6]));
    }

    [Fact]
    public void ThePositionListsCatchADbInAnotherOrder()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var db = GameDb.Read(rom);
        var swapped = db with { Weapons = [db.Weapons[1], db.Weapons[0], .. db.Weapons.Skip(2)] };

        var error = Assert.Throws<InvalidDataException>(() => PositionLists.Verify(rom, swapped));

        Assert.Contains("WeaponList", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheAlwaysAnimatedActorsMatchAcrossVersions(RomVersionId id) =>
        Assert.Equal(ActorTables.ReadAlwaysAnimated(TestRoms.Get(RomVersionId.Na10)), ActorTables.ReadAlwaysAnimated(TestRoms.Get(id)));

    /// <summary>The nine are the bat, wyvern, harpy, dryads, Pandara, gryphon, manticore and scorpion's actors.</summary>
    [Fact]
    public void TheAlwaysAnimatedActorsAreTheFlyingCreatures()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var db = GameDb.Read(rom);
        var actors = ActorTables.ReadAlwaysAnimated(rom);

        Assert.Equal([0x2D4A, 0x3463, 0x30C5, 0x28DC, 0x28E9, 0x2BE3, 0x3094, 0x31FE, 0x3347], actors);
        Assert.All(actors, a => Assert.Equal(BorgType.Actor, archive.Listings[a].Type));
        var names = EntityModels.Read(rom).Entities.Where(m => actors.Contains(m.Actor))
            .Select(m => db.Entities.Single(e => e.Id == m.Entity).Name).Order().ToList();
        Assert.Equal(["Darkenbat", "Dracovern", "Female Dryad", "Giant Bat", "Giant Scorpion", "Gryphon", "Harpy", "Large Scorpion",
            "Male Dryad", "Manticore", "Pandara", "Wyvern"], names);
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheFlycamsMatchAcrossVersions(RomVersionId id) =>
        Assert.Equal(TitleTables.ReadFlycams(TestRoms.Get(RomVersionId.Na10)), TitleTables.ReadFlycams(TestRoms.Get(id)));

    /// <summary>Six camera animations, each over an Overworld chunk that exists.</summary>
    [Fact]
    public void TheFlycamsAreSixCameraAnimationsOverTheOverworld()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var flycams = TitleTables.ReadFlycams(rom);
        var overworld = MapChunks.Read(rom).Where(c => c.Set == MapSet.Overworld).ToList();

        Assert.Equal(6, flycams.Count);
        Assert.Equal(new Flycam(0x21DC, 11, 9, 11, 6), flycams[0]);
        Assert.Equal(new Flycam(0x21DE, 1, 4, 1, 4), flycams[1]);
        Assert.Equal(new Flycam(0x21E2, 11, 8, 9, 8), flycams[2]);
        Assert.Equal(new Flycam(0x21E4, 14, 21, 13, 20), flycams[3]);
        Assert.Equal(new Flycam(0x21E6, 15, 25, 14, 25), flycams[4]);
        Assert.Equal(new Flycam(0x21E8, 13, 7, 13, 6), flycams[5]);
        Assert.All(flycams, f =>
        {
            Assert.Equal(BorgType.Animation, archive.Listings[f.Animation].Type);
            Assert.Contains(overworld, c => c.Row == f.Row && c.Column == f.Column);
        });
    }

    /// <summary>
    /// The facts <see cref="MapSets.Spawns"/> carries, against 1.0's code: the row and column immediates (<c>addiu s7</c> and
    /// <c>s6</c>) and the floats <c>Zones.SetMapSet</c> loads, and the arenas' table, which is the set's own chunk list.
    /// </summary>
    [Fact]
    public void TheSpawnsAreWhatZonesSetMapSetLoads()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var data = rom.Data.ToArray();
        var chunks = MapChunks.Read(rom);

        uint Word(uint address) => Be.U32(data, Rom(address));
        float Float(uint address) => Be.F32(data, Rom(address));
        // (the "addiu s7, zero, row" and "addiu s6, zero, column" instructions' addresses, and the addresses of x's and z's floats)
        (MapSet Set, int Portal, uint RowAt, int Row, uint ColumnAt, int Column, uint X, uint Z)[] code =
        [
            (MapSet.Overworld, 0, 0x80026374, 9, 0x80026378, 8, 0x800CE7D8, 0x800CE7DC),
            (MapSet.MiscInteriors, 0, 0x80026648, 5, 0x80026660, 3, 0x800CE7E4, 0x800CE7E8),
            (MapSet.MiscInteriors, 1, 0x8002663C, 1, 0x80026670, 1, 0x800CE7EC, 0x800CE7EC),
            (MapSet.Erromon, 0, 0x800266D0, 1, 0x800266E8, 15, 0x800CE7F4, 0x800CE7F8),
            (MapSet.Barrows, 0, 0x80026768, 4, 0x80026778, 1, 0x800CE804, 0x800CE804),
            (MapSet.JundarInteriors, 0, 0x800267F4, 1, 0x800267F8, 1, 0x800CE808, 0x800CE80C),
            (MapSet.EhudUnder, 0, 0x80026904, 17, 0x80026914, 1, 0x800CE814, 0x800CE814),
            (MapSet.Ugarit, 0, 0x80026948, 13, 0x80026970, 5, 0x800CE818, 0x800CE81C),
        ];
        foreach (var (set, portal, rowAt, row, columnAt, column, x, z) in code)
        {
            Assert.Equal(0x24170000u | (uint)row, Word(rowAt));
            Assert.Equal(0x24160000u | (uint)column, Word(columnAt));
            var spawn = Assert.Single(MapSets.Spawns(set, chunks)!, s => s.Portal == portal);
            Assert.Equal(new SpawnPoint(portal, row, column, new Vector2(Float(x), Float(z))), spawn);
        }

        // The arenas: portal n is the n-th entry of the set's chunk list, at 20.0 in x and z.
        var arenas = MapSets.Spawns(MapSet.Battlefields, chunks)!;
        var list = chunks.Where(c => c.Set == MapSet.Battlefields).ToList();
        Assert.Equal(28, arenas.Count);
        Assert.Equal(20f, Float(0x800CE824));
        for (var n = 0; n < arenas.Count; n++)
        {
            var entry = Rom(0x800DCA74) + n * 12;
            Assert.Equal(new SpawnPoint(n, Be.U16(data, entry), Be.U16(data, entry + 2), new Vector2(20, 20)), arenas[n]);
            Assert.Equal((list[n].Row, list[n].Column), (arenas[n].Row, arenas[n].Column));
        }

        // The sets the code gives no portal.
        Assert.All(Enum.GetValues<MapSet>().Where(s => s is not (MapSet.Overworld or MapSet.MiscInteriors or MapSet.Erromon or MapSet.Barrows
            or MapSet.JundarInteriors or MapSet.EhudUnder or MapSet.Ugarit or MapSet.Battlefields)), s => Assert.Null(MapSets.Spawns(s, chunks)));
    }

    /// <summary>
    /// The float literals the spawns come from are 1.1's too, wherever its code puts them. The debug build's differ (its
    /// <c>Zones.SetMapSet</c> has other places and its own literals), and it can't be imported.
    /// </summary>
    [Fact]
    public void TheSpawnFloatsAreIn11AndNotInTheDebugBuild()
    {
        var na10 = TestRoms.Get(RomVersionId.Na10).Data.Slice(Rom(0x800CE7D4), 0x54).ToArray();
        var other = TestRoms.Get(RomVersionId.Na11).Data.ToArray();
        Assert.True(TestRoms.Get(RomVersionId.Debug).Data.ToArray().AsSpan().IndexOf(na10) < 0);

        var first = other.AsSpan().IndexOf(na10);

        Assert.True(first >= 0, "The literals aren't in this ROM.");
        Assert.Equal(-1, other.AsSpan(first + 1).IndexOf(na10));
    }

    [Collection(ImportedPluginCollection.Name)]
    public sealed class Imported(Na10Import fixture)
    {
        [Fact]
        public void TheActorsCombatAlwaysAnimatesAreMarked()
        {
            var actors = fixture.Data.Set.All<ActorRecord>(RecordTypes.Actor);

            Assert.Equal(["aidyn:actor/0x28DC", "aidyn:actor/0x28E9", "aidyn:actor/0x2BE3", "aidyn:actor/0x2D4A", "aidyn:actor/0x3094",
                "aidyn:actor/0x30C5", "aidyn:actor/0x31FE", "aidyn:actor/0x3347", "aidyn:actor/0x3463"],
                actors.Where(a => a.AlwaysAnimated).Select(a => a.Id.ToString()).Order(StringComparer.Ordinal));
            Assert.False(fixture.Data.Set.Get<ActorRecord>(RecordId.Hex(RecordTypes.Actor, 0x25F9)).AlwaysAnimated);
        }

        [Fact]
        public void TheTitleHasSixFlycamsAndTheFifthNeedsThePak()
        {
            var title = fixture.Data.Set.Singleton<TitleRecord>();

            Assert.Equal(6, title.Flycams.Count);
            var first = title.Flycams[0];
            Assert.Equal(RecordId.Hex(RecordTypes.Animation, 0x21DC), first.Animation);
            Assert.Equal((11, 9), (first.Row, first.Column));
            Assert.Equal((11, 6), (first.OriginRow, first.OriginColumn));
            Assert.Equal([4], Enumerable.Range(0, 6).Where(i => title.Flycams[i].NeedsExpansionPak));
            Assert.All(title.Flycams, f => Assert.Equal(RecordTypes.Scene, f.Scene.Type));
            // The Borg6s' lengths, which each fly-through fades out by.
            Assert.Equal([1561, 2631, 2181, 1601, 1853, 2401], title.Flycams.Select(f => f.Length));
        }

        [Fact]
        public void TheMapSetsCarryTheirSpawnsAndANewGameStartsAtTheOverworlds()
        {
            var sets = fixture.Data.MapSets;

            var overworld = Assert.Single(sets, s => s.Label == "Overworld");
            Assert.Equal([new SpawnPoint(0, 9, 8, new Vector2(16, 62))], overworld.Spawns);
            var misc = Assert.Single(sets, s => s.Label == "MiscInteriors");
            Assert.Equal([new SpawnPoint(0, 5, 3, new Vector2(5.9f, 3)), new SpawnPoint(1, 1, 1, new Vector2(5, 5))], misc.Spawns);
            Assert.Equal(28, Assert.Single(sets, s => s.Label == "Battlefields").Spawns!.Count);
            Assert.Equal(["Barrows", "Battlefields", "EhudUnder", "Erromon", "JundarInteriors", "MiscInteriors", "Overworld", "Ugarit"],
                sets.Where(s => s.Spawns is not null).Select(s => s.Label).Order(StringComparer.Ordinal));
            // Every spawn is a chunk of its set.
            Assert.All(sets, s => Assert.All(s.Spawns ?? [], p => Assert.Contains(s.Chunks, c => c.Row == p.Row && c.Column == p.Column)));

            var start = fixture.Data.Set.Singleton<NewGameRecord>();
            Assert.Equal((overworld.Id, 9, 8), (start.Chunk.MapSet, start.Chunk.Row, start.Chunk.Column));
            Assert.Equal(overworld.Spawns![0].Position, start.Position);
        }
    }
}
