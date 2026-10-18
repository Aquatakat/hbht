using Aidyn.Rom.Db;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Tests;

public class EngineTablesTests
{
    private static IReadOnlyDictionary<ItemId, string> Names(RomImage rom)
    {
        var db = GameDb.Read(rom);
        return db.Entities.Select(e => (e.Id, e.Name)).Concat(db.Gear.Select(g => (g.Id, g.Name)))
            .ToDictionary(e => e.Id, e => e.Name);
    }

    /// <summary>The tables are the same in every version. Only their offsets differ.</summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TablesMatchAcrossVersions(RomVersionId id)
    {
        var a = EngineTables.Read(TestRoms.Get(RomVersionId.Na10));
        var b = EngineTables.Read(TestRoms.Get(id));

        Assert.Equal(a.EntityList, b.EntityList);
        Assert.Equal(a.Party, b.Party);
        Assert.Equal(a.KeyItems, b.KeyItems);
        Assert.Equal(a.Regions, b.Regions);
        Assert.Equal(a.CampDialogueFlags, b.CampDialogueFlags);
        Assert.Equal(a.ScriptBattleFlags, b.ScriptBattleFlags);
        Assert.Equal(a.CampfireScenes, b.CampfireScenes);
        Assert.Equal(a.ScriptEndings, b.ScriptEndings);
        Assert.Equal(a.GameOverScript, b.GameOverScript);
        Assert.Equal(a.WorldMap, b.WorldMap);
    }

    [Fact]
    public void EntityListHoldsEveryEntityOnce()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var entities = GameDb.Read(rom).Entities;
        var list = EngineTables.Read(rom).EntityList;

        Assert.Equal(entities.Count, list.Count);
        Assert.Equal(entities.Select(e => e.Id).Order(), list.Order());
    }

    /// <summary>
    /// Joined, left and died flags, by name. The decomp gives Sholeh's left flag as 700, but the ROM
    /// has 694. Brenna has no left flag.
    /// </summary>
    [Fact]
    public void PartyFlagsNameTheRecruits()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var names = Names(rom);
        var party = EngineTables.Read(rom).Party.ToDictionary(p => names[p.Member], p => (p.Joined, p.Left, p.Died));

        Assert.Equal(new Dictionary<string, (ushort, ushort, ushort)>
        {
            ["Farris"] = (455, 456, 450), ["Abrecan"] = (89, 685, 615), ["Arturo"] = (247, 689, 614),
            ["Baird"] = (380, 690, 623), ["Becan"] = (375, 697, 618), ["Brenna"] = (92, 0, 613),
            ["Donovan"] = (382, 691, 620), ["Dougal"] = (574, 695, 582), ["Godric"] = (90, 686, 616),
            ["Keelin"] = (265, 688, 619), ["Niesen"] = (412, 692, 448), ["Rheda"] = (91, 687, 617),
            ["Sholeh"] = (523, 694, 622),
        }, party);
    }

    /// <summary>
    /// Every key item has a "has" flag, and the maps reveal world map regions. Map 13's region is on
    /// Cradawgh's Body instead, which the decomp notes as a bug.
    /// </summary>
    [Fact]
    public void KeyItemsHaveFlags()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var names = Names(rom);
        var keys = EngineTables.Read(rom).KeyItems;

        Assert.Equal(31, keys.Count);
        Assert.Equal([.. Enumerable.Range(3403, 30), 405], keys.Select(k => (int)k.Held));
        Assert.Equal(("Amaranth", 3404), (names[keys[1].Item], (int)keys[1].Held));
        Assert.Equal(("Dragon Key", 3432), (names[keys[29].Item], (int)keys[29].Held));

        var regions = keys.Where(k => k.Region != 0).ToDictionary(k => names[k.Item], k => (int)k.Region);
        Assert.Equal(Enumerable.Range(788, 18), regions.Values);
        Assert.Equal(799, regions["Cradawgh's Body"]);
        Assert.DoesNotContain("Map 13", regions.Keys);
    }

    /// <summary>Two entries name chunks the overworld doesn't have, G18 and R29, so they never apply.</summary>
    [Fact]
    public void RegionsCoverOverworldChunks()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var regions = EngineTables.Read(rom).Regions;
        var overworld = MapChunks.Read(rom).Where(c => c.Set == MapSet.Overworld).Select(c => (c.Row, c.Column)).ToHashSet();

        Assert.Equal(162, regions.Count);
        Assert.Equal(Enumerable.Range(788, 18), regions.Select(r => (int)r.Flag).Distinct().Order());
        Assert.Equal([(7, 18), (18, 29)], regions.Select(r => (r.Row, r.Column)).Where(c => !overworld.Contains(c)));
        Assert.Equal(regions.Count, regions.Select(r => (r.Row, r.Column)).Distinct().Count());
    }

    [Fact]
    public void SmallTablesArePinned()
    {
        var tables = EngineTables.Read(TestRoms.Get(RomVersionId.Na10));

        Assert.Equal([5056, 5150, 5151, 5154, 5155, 5163, 5191, 5011], tables.CampDialogueFlags.Select(f => (int)f));
        Assert.Equal(new Dictionary<int, ushort> { [0x365] = 978, [0x4D1] = 5250 }, tables.ScriptBattleFlags);
    }

    /// <summary>
    /// The camp scenes include the Campsite Borg9's own script (6235), which plays when no camp
    /// scene is nearby. Of the three death scenes, the game only uses the first.
    /// </summary>
    [Fact]
    public void ScriptTablesArePinned()
    {
        var tables = EngineTables.Read(TestRoms.Get(RomVersionId.Na10));

        Assert.Equal([972, 1180, 1181, 1184, 1185, 1194, 1259, 978, 6235], tables.CampfireScenes);
        Assert.Equal(new Dictionary<int, ScriptEnding>
        {
            [1298] = ScriptEnding.GameOver, [1299] = ScriptEnding.GameOver, [1300] = ScriptEnding.GameOver,
            [1268] = ScriptEnding.RoogCinematic, [1278] = ScriptEnding.EndingCinematic,
        }, tables.ScriptEndings);
        Assert.Equal(1298, tables.GameOverScript);
    }

    /// <summary>The world map has a piece for each region flag, in order, each with its own image.</summary>
    [Fact]
    public void WorldMapPiecesFollowTheRegions()
    {
        var tables = EngineTables.Read(TestRoms.Get(RomVersionId.Na10));

        Assert.Equal(tables.Regions.Select(r => r.Flag).Distinct().Order(), tables.WorldMap.Select(p => p.Flag));
        Assert.Equal(Enumerable.Range(457, 18), tables.WorldMap.Select(p => p.Image));
        Assert.Equal((58, 29), (tables.WorldMap[0].X, tables.WorldMap[0].Y));
    }

    [Fact]
    public void BossFlagsNameTheBosses()
    {
        var names = Names(TestRoms.Get(RomVersionId.Na10));

        Assert.Equal(new Dictionary<string, ushort>
        {
            ["Kitarak"] = 154, ["Pochanargat"] = 3204, ["Shamsuk"] = 458, ["Ksathra"] = 3738,
            ["Shatrevar"] = 3737, ["Mehrdad"] = 3736, ["Golnar"] = 3735, ["Nasim"] = 3734,
            ["Behrooz"] = 3733, ["Assim"] = 3739, ["Lugash"] = 533,
        }, EngineFlags.BossFlags.ToDictionary(b => names[b.Key], b => b.Value));
    }
}
