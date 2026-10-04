using System.Text.Json;
using Aidyn.Rom.Db;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Real-data checks for the DB readers. The tables have no checksums, so these test what a
/// wrong layout would break: counts, ID types, references between tables, and readable text.
/// </summary>
public class GameDbTests
{
    private static readonly Dictionary<RomVersionId, GameDb> Cache = [];

    private static GameDb Db(RomVersionId id)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(id, out var db))
                Cache[id] = db = GameDb.Read(TestRoms.Get(id));
            return db;
        }
    }

    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void TablesHaveExpectedCounts(RomVersionId id)
    {
        var db = Db(id);

        Assert.Equal(221, db.Entities.Count);
        Assert.Equal(105, db.Weapons.Count);
        Assert.Equal(68, db.Armor.Count);
        Assert.Equal(59, db.Spells.Count);
        Assert.Equal(197, db.Gear.Count);
        Assert.Equal(63, db.Loot.Count);
        Assert.Equal(75, db.Shops.Count);
        Assert.Equal(37, db.DialogEntities.Count);
        Assert.Equal(5627, db.GameStateFunnel.Flags.Count);
        Assert.Equal(5001, db.GameStateFunnel.BitFlagCount);
        Assert.Equal(1372, db.GameStateFunnel.Inputs.Count);
        Assert.Equal(71, db.Journal.Count);
        Assert.Equal(4, db.CinematicTitles.Count);
        Assert.Equal(15, db.Captions.Count);
        Assert.Equal(27 * CampEncounter.SlotsPerTerrain, db.CampEncounters.Count);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void IdsAreUniqueAndMatchTheirTable(RomVersionId id)
    {
        var db = Db(id);

        Assert.All(db.Entities, e => Assert.Equal(DbType.Entity, e.Id.Type));
        Assert.All(db.Weapons, w => Assert.Equal(DbType.Weapon, w.Id.Type));
        Assert.All(db.Armor, a => Assert.Equal(a.Slot == 0 ? DbType.Armor : DbType.Shield, a.Id.Type));
        Assert.All(db.Spells, s => Assert.Equal(DbType.Spell, s.Id.Type));
        Assert.All(db.Loot, l => Assert.Equal(DbType.Loot, l.Id.Type));
        Assert.All(db.DialogEntities, d => Assert.Equal(DbType.DialogEntity, d.Id.Type));
        Assert.All(db.Gear, g => Assert.Contains(g.Id.Type, GearTypes));

        var all = AllIds(db).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    /// <summary>
    /// The readers flatten each grouped table. That loses nothing only if every group holds one
    /// value of a field, in ascending order, so the field can rebuild the groups.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void GroupsFollowTheirField(RomVersionId id)
    {
        var db = Db(id);

        AssertAscending(db.Entities.Select(e => (int)e.Category));
        AssertAscending(db.Weapons.Select(w => (int)w.Class));
        AssertAscending(db.Armor.Select(a => (int)a.Slot));
        AssertAscending(db.Spells.Select(s => (int)s.School));
        AssertAscending(db.Gear.Select(g => Array.IndexOf(GearTypes, g.Id.Type)));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void ReferencesResolve(RomVersionId id)
    {
        var db = Db(id);
        var ids = AllIds(db).ToHashSet();
        var potionCount = db.Strings[RomStringTable.PotionNames].Count;

        // Potions have no table of their own; their index picks a potion name.
        void AssertItem(ItemId item)
        {
            if (item.IsNone)
                return;
            Assert.True(ids.Contains(item) || (item.Type == DbType.Potion && item.Index < potionCount), $"{item} doesn't resolve");
        }

        foreach (var e in db.Entities)
        {
            Assert.All(e.Weapons, w => Assert.True(w.IsNone || w.Type == DbType.Weapon && ids.Contains(w)));
            Assert.All(e.Spells, s => Assert.True(s.IsNone || s.Type == DbType.Spell && ids.Contains(s)));
            Assert.True(e.Armor.IsNone || e.Armor.Type == DbType.Armor && ids.Contains(e.Armor));
            Assert.True(e.Shield.IsNone || e.Shield.Type == DbType.Shield && ids.Contains(e.Shield));
            Assert.All(e.RitualIds, r => Assert.True(r.IsNone));
            Assert.Contains(ItemId.Of(DbType.Loot, e.LootCategory), ids);
        }

        var bonuses = db.Weapons.Select(w => w.Bonuses).Concat(db.Armor.Select(a => a.Bonuses)).Concat(db.Gear.Select(g => g.Bonuses));
        foreach (var b in bonuses)
        {
            Assert.True(b.Spell.IsNone || b.Spell.Type == DbType.Spell && ids.Contains(b.Spell));
            Assert.True(b.Magic.IsNone || b.Magic.Type == DbType.Spell && ids.Contains(b.Magic));
        }

        foreach (var l in db.Loot)
            Assert.All(l.Drops, d => AssertItem(d.Item));
        foreach (var s in db.Shops)
        {
            Assert.Equal(DbType.Entity, s.Shopkeeper.Type);
            Assert.Contains(s.Shopkeeper, ids);
            Assert.All(s.Stock, x => AssertItem(x.Item));
            Assert.All(s.ExtraStock, AssertItem);
        }
        foreach (var c in db.CampEncounters)
            Assert.All(c.Groups, g => Assert.True(g.Enemy.Type == DbType.Entity && ids.Contains(g.Enemy)));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EnumFieldsAreDefined(RomVersionId id)
    {
        var db = Db(id);

        Assert.All(db.Entities, e =>
        {
            Assert.True(Enum.IsDefined(e.Aspect));
            Assert.True(Enum.IsDefined(e.School));
            Assert.All(e.Resistances, r => Assert.True(Enum.IsDefined(r.Element)));
        });
        Assert.All(db.Weapons, w => Assert.True(Enum.IsDefined(w.Class) && Enum.IsDefined(w.DamageType)));
        Assert.All(db.Spells, s => Assert.True(
            Enum.IsDefined(s.School) && Enum.IsDefined(s.TargetCount) && Enum.IsDefined(s.TargetType) && Enum.IsDefined(s.Reagent)));

        var bonuses = db.Weapons.Select(w => w.Bonuses).Concat(db.Armor.Select(a => a.Bonuses)).Concat(db.Gear.Select(g => g.Bonuses));
        Assert.All(bonuses, b => Assert.True(Enum.IsDefined(b.Aspect) && Enum.IsDefined(b.Resist)));
        Assert.All(db.GameStateFunnel.Flags, f => Assert.True(Enum.IsDefined(f.Kind) && Enum.IsDefined(f.Op)));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void FlagReferencesAreInRange(RomVersionId id)
    {
        var funnel = Db(id).GameStateFunnel;
        var flagCount = funnel.Flags.Count;

        Assert.All(funnel.Flags, f => Assert.True(f.InputStart + f.InputCount <= funnel.Inputs.Count));
        Assert.All(funnel.Inputs, i => Assert.True(i < flagCount || i == 0xFFFF));
        Assert.All(funnel.Flags.Skip(funnel.BitFlagCount), f => Assert.NotEqual(FlagKind.Bit, f.Kind));
        Assert.All(Db(id).Journal.Concat(Db(id).CinematicTitles), t => Assert.True(t.Flag < flagCount));
    }

    /// <summary>The cipher's main check: one wrong key byte garbles every string after it.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void AllTextIsPrintable(RomVersionId id)
    {
        var db = Db(id);
        var text = db.Strings.Values.SelectMany(s => s)
            .Concat(db.Journal.Select(j => j.Text))
            .Concat(db.CinematicTitles.Select(t => t.Text))
            .Concat(db.Captions.SelectMany(s => s).Select(c => c.Text))
            .Concat(db.Entities.SelectMany(e => new[] { e.Name, e.InternalName }))
            .Concat(db.Weapons.Select(w => w.Name))
            .Concat(db.Armor.Select(a => a.Name))
            .Concat(db.Spells.Select(s => s.Name))
            .Concat(db.Gear.Select(g => g.Name))
            .Concat(db.Loot.Select(l => l.Name))
            .Concat(db.DialogEntities.Select(d => d.Name))
            .ToList();

        Assert.True(text.Count > 1500);
        Assert.All(text, s => Assert.True(s.All(c => c is >= ' ' and <= '~'), $"Unprintable text: \"{s}\""));
    }

    [Fact]
    public void CaptionTimesIncrease() =>
        Assert.All(Db(RomVersionId.Na10).Captions.SelectMany(s => s), c => Assert.True(c.Start < c.End));

    /// <summary>
    /// 1.1 changed no records. Its only DB block changes are new Controller Pak messages,
    /// appended to two string tables.
    /// </summary>
    [Fact]
    public void Na11DiffersOnlyByNewControllerPakMessages()
    {
        var na10 = Db(RomVersionId.Na10);
        var na11 = Db(RomVersionId.Na11);

        Assert.Equal(Json(na10 with { Strings = null! }), Json(na11 with { Strings = null! }));
        foreach (var (table, strings) in na10.Strings)
        {
            var added = table switch { RomStringTable.Common => 5, RomStringTable.Controller => 4, _ => 0 };
            Assert.Equal(strings, na11.Strings[table].Take(strings.Count));
            Assert.Equal(strings.Count + added, na11.Strings[table].Count);
        }
    }

    [Fact]
    public void DebugDiffersOnlyInCredits()
    {
        var na10 = Db(RomVersionId.Na10);
        var debug = Db(RomVersionId.Debug);

        Assert.Equal(Json(na10 with { Strings = null! }), Json(debug with { Strings = null! }));
        Assert.All(na10.Strings.Where(s => s.Key != RomStringTable.Credits), s => Assert.Equal(s.Value, debug.Strings[s.Key]));
    }

    private static readonly DbType[] GearTypes =
    [
        DbType.Misc, DbType.Helmet, DbType.Cloak, DbType.Glove, DbType.Ring, DbType.Wand,
        DbType.Belt, DbType.Boots, DbType.Scroll, DbType.KeyItem, DbType.Amulet,
    ];

    private static IEnumerable<ItemId> AllIds(GameDb db) =>
        db.Entities.Select(e => e.Id)
            .Concat(db.Weapons.Select(w => w.Id))
            .Concat(db.Armor.Select(a => a.Id))
            .Concat(db.Spells.Select(s => s.Id))
            .Concat(db.Gear.Select(g => g.Id))
            .Concat(db.Loot.Select(l => l.Id))
            .Concat(db.DialogEntities.Select(d => d.Id));

    private static void AssertAscending(IEnumerable<int> keys)
    {
        var list = keys.ToList();
        Assert.DoesNotContain(-1, list);
        Assert.Equal(list.Order(), list);
    }

    private static string Json(GameDb db) => JsonSerializer.Serialize(db);
}
