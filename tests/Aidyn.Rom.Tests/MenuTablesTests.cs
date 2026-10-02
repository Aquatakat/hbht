using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using R = Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

public class MenuTablesTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    private static RecordId Image(int borg8) => RecordId.Hex(RecordTypes.Image, borg8);

    /// <summary>The tables are the same in every version. Only their offsets differ.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void MenuTablesMatchAcrossVersions(RomVersionId id)
    {
        var a = MenuTables.Read(TestRoms.Get(RomVersionId.Na10));
        var b = MenuTables.Read(TestRoms.Get(id));

        Assert.Equal(a.SpellSymbols.OrderBy(e => e.Key), b.SpellSymbols.OrderBy(e => e.Key));
        Assert.Equal(a.PotionDescriptions.OrderBy(e => e.Key), b.PotionDescriptions.OrderBy(e => e.Key));
        Assert.Equal(a.ItemDescriptions.OrderBy(e => e.Key), b.ItemDescriptions.OrderBy(e => e.Key));
        Assert.Equal(a.SpellDescriptions.OrderBy(e => e.Key), b.SpellDescriptions.OrderBy(e => e.Key));
        Assert.Equal(a.InventoryOrder, b.InventoryOrder);
        Assert.Equal(a.EquipmentSlotImages, b.EquipmentSlotImages);
        Assert.Equal(a.Messages, b.Messages);
    }

    /// <summary>The potion and scroll messages in code are whole sentences, the second naming trek.</summary>
    [Fact]
    public void TheMessagesInCodeAreWholeSentences()
    {
        var messages = MenuTables.Read(TestRoms.Get(RomVersionId.Na10)).Messages;

        Assert.All(new[] { messages.PotionNotNow, messages.PotionNotInTrek, messages.ScrollNoSpell }, m =>
        {
            Assert.EndsWith(".", m);
            Assert.All(m, c => Assert.InRange(c, ' ', '~'));
        });
        Assert.StartsWith("That potion", messages.PotionNotNow);
        Assert.Contains("Trek", messages.PotionNotInTrek);
        Assert.Contains("Lighthouse", messages.ScrollNoSpell);
    }

    /// <summary>
    /// The spell list has a position for every spell in the spell DB. Air Shield and Spirit Shield
    /// share symbol 1, vs. Elemental has 4, Strength 5 and Wind (the list's next spell) none, and every
    /// symbol has an icon.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void SpellSymbolsCoverEverySpellInTheDb(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var symbols = MenuTables.Read(rom).SpellSymbols;
        var spells = GameDb.Read(rom).Spells.Select(s => s.Id).Distinct().ToList();

        Assert.Equal(59, symbols.Count);
        Assert.Equal(spells.Order(), symbols.Keys.Order());
        Assert.All(symbols.Keys, spell => Assert.Equal(DbType.Spell, spell.Type));
        Assert.Equal(1, symbols[new ItemId(0x0300)]);
        Assert.Equal(1, symbols[new ItemId(0x031C)]);
        Assert.Equal(4, symbols[new ItemId(0x031D)]);
        Assert.Equal(5, symbols[new ItemId(0x0308)]);
        Assert.Equal(0, symbols[new ItemId(0x0309)]);

        var icons = ItemTables.Read(rom).SpellSymbolIcons;
        Assert.All(symbols.Values, symbol => Assert.InRange(symbol, 0, icons.Count - 1));
    }

    /// <summary>
    /// The core's copies of the inventory's order of kinds and the slots' empty images, and the
    /// aspects' and schools' icons, are what the ROM holds.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void TheCoresMenuFactsAreTheRomsTables(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var menu = MenuTables.Read(rom);
        var items = ItemTables.Read(rom);

        Assert.Equal(InventoryOrder.Kinds.Select(k => (int)k), menu.InventoryOrder);
        Assert.Equal(15, menu.EquipmentSlotImages.Count);
        Assert.Equal(MenuImages.EmptySlots, menu.EquipmentSlotImages.Select(i => Image(i)));

        foreach (var aspect in Enum.GetValues<R.Aspect>())
            Assert.Equal(items.AspectIcons[(int)aspect] is var a and not 0 ? Image(a) : (RecordId?)null, MenuImages.AspectIcon(aspect));
        foreach (var school in Enum.GetValues<R.MagicSchool>())
            Assert.Equal(items.SchoolIcons[(int)school] is var s and not 0 ? Image(s) : (RecordId?)null, MenuImages.SchoolIcon(school));
    }

    /// <summary>
    /// Every description is of something that exists, and names a line in its string table. Every
    /// potion has one but 0x100B and 0x100C, and four spells have none.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void DescriptionsNameExistingThingsAndLines(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var menu = MenuTables.Read(rom);
        var db = GameDb.Read(rom);

        Assert.Equal(82, menu.ItemDescriptions.Count);
        var gear = db.Gear.Select(g => g.Id).ToHashSet();
        Assert.All(menu.ItemDescriptions.Keys, item => Assert.Contains(item, gear));
        Assert.All(menu.ItemDescriptions.Values, line => Assert.InRange(line, 0, db.Strings[RomStringTable.Items].Count - 1));
        Assert.Equal(menu.ItemDescriptions.Count, menu.ItemDescriptions.Values.Distinct().Count());

        var potions = Enumerable.Range(0, ItemTables.PotionCount).Select(i => ItemId.Of(DbType.Potion, (byte)i)).ToList();
        Assert.All(menu.PotionDescriptions.Keys, potion => Assert.Contains(potion, potions));
        Assert.All(menu.PotionDescriptions.Values, line => Assert.InRange(line, 0, db.Strings[RomStringTable.PotionDetails].Count - 1));
        Assert.Equal([new ItemId(0x100B), new ItemId(0x100C)], potions.Where(p => !menu.PotionDescriptions.ContainsKey(p)));
        Assert.Equal(13, menu.PotionDescriptions[new ItemId(0x100F)]);
        Assert.Equal(14, menu.PotionDescriptions[new ItemId(0x1010)]);

        Assert.Equal(55, menu.SpellDescriptions.Count);
        Assert.All(menu.SpellDescriptions.Keys, spell => Assert.Contains(spell, menu.SpellSymbols.Keys));
        Assert.All(menu.SpellDescriptions.Values, line => Assert.InRange(line, 0, db.Strings[RomStringTable.Spells].Count - 1));
        Assert.Equal([0x030F, 0x0311, 0x0327, 0x0328],
            menu.SpellSymbols.Keys.Where(s => !menu.SpellDescriptions.ContainsKey(s)).Select(s => (int)s.Value).Order());
    }
}

[Collection(ImportedPluginCollection.Name)]
public sealed class MenuImportTests(MapObjectFixture fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private R.SpellRecord Spell(int id) => Set.Get<R.SpellRecord>(RecordId.Hex(RecordTypes.Spell, id));

    private R.ItemRecord Item(int id) => Set.Get<R.ItemRecord>(RecordId.Hex(RecordTypes.Item, id));

    private IReadOnlyList<string> Lines(RomStringTable table) =>
        Set.Get<R.StringsRecord>(RecordId.Decimal(RecordTypes.Strings, (int)table)).Lines;

    /// <summary>A spell's icon is the image its symbol names, and its description is a line of the Spells table.</summary>
    [Fact]
    public void SpellsHaveTheirSymbolsIconAndDescription()
    {
        var air = Spell(0x0300);

        Assert.Equal(RecordId.Hex(RecordTypes.Image, 0x01AB), air.Icon);
        Assert.StartsWith("All friendly entities", air.Description);
        Assert.Equal(Lines(RomStringTable.Spells)[0], air.Description);

        var spells = Set.All<R.SpellRecord>(RecordTypes.Spell).ToList();
        Assert.Equal(59, spells.Count);
        Assert.All(spells, s => Assert.NotNull(s.Icon));
        Assert.Equal([0x030F, 0x0311, 0x0327, 0x0328],
            spells.Where(s => s.Description is null).Select(s => Convert.ToInt32(s.Id.Key, 16)).Order());
    }

    /// <summary>Items and potions have their description lines; weapons and armor have no item record.</summary>
    [Fact]
    public void ItemsAndPotionsHaveTheirDescriptions()
    {
        Assert.StartsWith("A letter to King Txomin", Item(0x0100).Description);
        Assert.Equal(Lines(RomStringTable.Items)[0], Item(0x0100).Description);
        Assert.StartsWith("A sticky, incendiary liquid", Item(0x1000).Description);
        Assert.Equal(Lines(RomStringTable.PotionDetails)[0], Item(0x1000).Description);

        var items = Set.All<R.ItemRecord>(RecordTypes.Item).ToList();
        Assert.Equal(82, items.Count(i => i.Kind != R.ItemKind.Potion && i.Description is not null));
        Assert.Equal(15, items.Count(i => i.Kind == R.ItemKind.Potion && i.Description is not null));
        Assert.Null(Item(0x100B).Description);
        Assert.Null(Item(0x100C).Description);
    }
}
