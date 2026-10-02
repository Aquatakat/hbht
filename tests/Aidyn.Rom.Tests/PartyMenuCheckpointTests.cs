using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;
using CharacterParty = Aidyn.Core.Characters.Party;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Checks the pause menu's Party tab model against the user's captures of the original's, taken from
/// the "Before final encounter" checkpoint (NA 1.0), with the records imported from 1.0.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class PartyMenuCheckpointTests(MapObjectFixture fixture)
{
    private const string Checkpoint = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Before final encounter.bs1";

    private PluginSet Set => fixture.Data.Set;

    private static AresCheckpoint Load()
    {
        var path = Path.Combine(TestRoms.Folder, Checkpoint);
        if (!File.Exists(path))
            Assert.Skip($"No {Checkpoint} in {TestRoms.Folder}.");
        return AresCheckpoint.Read(path);
    }

    private static RecordId Entity(int id) => RecordId.Hex(RecordTypes.Entity, id);

    private static RecordId Image(int id) => RecordId.Hex(RecordTypes.Image, id);

    private static RecordId ItemId(ushort id) => (id >> 8) switch
    {
        7 => RecordId.Hex(RecordTypes.Weapon, id),
        5 or 6 => RecordId.Hex(RecordTypes.Armor, id),
        _ => RecordId.Hex(RecordTypes.Item, id),
    };

    private static ItemInstance Instance(ItemCatalog items, CheckpointItem held) => new(items[ItemId(held.Id)])
    {
        Price = held.Price,
        Battery = held.StatMod is { Stat: 5, Amount: var left } ? left : null,
        Charges = held.Charges,
    };

    /// <summary>The checkpoint's party as the editor restores it: members as the sheets hold them, wearing what they wear, with the gold and inventory.</summary>
    private CharacterParty Party(AresCheckpoint checkpoint)
    {
        var rules = CharacterRules.From(Set);
        var items = ItemCatalog.From(Set);
        var party = new CharacterParty(items);
        foreach (var sheet in checkpoint.Party)
        {
            var entity = Set.Get<EntityRecord>(Entity(sheet.Id));
            var spells = sheet.Spells.Select(s => new LearnedSpell(Set.Get<SpellRecord>(RecordId.Hex(RecordTypes.Spell, s.Spell)), s.Level)).ToList();
            var character = Character.Restore(entity, rules, new CharacterStats(rules, sheet.Trained, sheet.Current, sheet.Base, sheet.Modified),
                new CharacterSkills(rules, sheet.SkillBase, sheet.SkillModified), spells);
            character.TrueName = (sheet.Flags & 1) != 0;
            character.TotalExp = sheet.TotalExp;
            character.SpendableExp = sheet.SpendableExp;
            character.Protection = sheet.Protection;
            character.Damage = sheet.Damage;
            foreach (var worn in new[] { sheet.Weapon, sheet.Armor, sheet.Shield }.OfType<CheckpointItem>())
                character.Equipment.Restore(Instance(items, worn));
            for (var slot = 0; slot < sheet.Gear.Count; slot++)
            {
                if (sheet.Gear[slot] is { } gear)
                    character.Equipment.Restore(Instance(items, gear), slot);
            }
            Assert.True(party.Add(character));
        }
        party.Gold = checkpoint.Gold;
        foreach (var stack in checkpoint.Inventory)
        {
            var held = Instance(items, stack.Item);
            party.Inventory!.Add(held.Item, stack.Quantity, held);
        }
        return party;
    }

    private PartyMenu Menu(int member = 0) => PartyMenu.From(Set, Party(Load()), member);

    private static string Text(DetailLine line) => string.Concat(line.Left.OfType<DetailText>().Select(t => t.Text));

    private static string Right(DetailLine line) => string.Concat(line.Right.OfType<DetailText>().Select(t => t.Text));

    /// <summary>
    /// The lines A's messages and popups read are the common strings the code reads: the popups' titles and
    /// tasks, and the messages with their conversions in the order the port fills them.
    /// </summary>
    [Fact]
    public void TheActionsLinesAreTheOnesTheCodeReads()
    {
        var text = MenuText.From(Set);
        string Conversions(int line) => string.Concat(System.Text.RegularExpressions.Regex.Matches(text[line], "%l?[sdu]").Select(m => m.Value[^1]));

        Assert.Equal(["Perform Alchemy", "Create Armor", "Healer Tasks", "Lay On Hands", "Heal With Herbs", "Restore", "Ok"],
            new[] { MenuText.Alchemy, MenuText.CreateArmor, MenuText.HealerTasks, MenuText.LayOnHands, MenuText.HealWithHerbs, MenuText.Restore, 95 }
                .Select(l => text[l]));
        Assert.StartsWith("Health (", text[MenuText.Health]);
        Assert.Equal("dd", Conversions(MenuText.Health));
        Assert.Equal("ssd", Conversions(MenuText.HealsBy));
        Assert.Equal("ssd", Conversions(MenuText.RestoredBy));
        Assert.Equal("ss", Conversions(MenuText.StatAtMax));
        Assert.Equal("ss", Conversions(MenuText.Learned));
        Assert.Equal("ss", Conversions(MenuText.PowerfulMagic));
        Assert.Equal("s", Conversions(MenuText.AlreadyKnows));
        Assert.Equal("s", Conversions(MenuText.AlreadyAtMaxHealth));
        Assert.Equal("s", Conversions(MenuText.HealFailed));
        Assert.All(new[] { MenuText.CantEquip, MenuText.WrongAspect, MenuText.NeedsStrength, MenuText.NeedsIntelligence },
            l => Assert.Contains("equip", text[l]));
        Assert.Contains("Potion", text[MenuText.PotionMade]);
        Assert.Contains("Flask", text[MenuText.FlaskMade]);
        Assert.Contains("potion", text[MenuText.PotionComponents]);
        Assert.Contains("flask", text[MenuText.FlaskComponents]);
        Assert.Contains("Armor", text[MenuText.ArmorMade]);
    }

    /// <summary>
    /// A on the inventory's first row, Alaron's sword, takes it off into the inventory, and A on it there puts it
    /// back on, with the cursor left on what's left of its stack.
    /// </summary>
    [Fact]
    public void AOnAWornWeaponTakesItOffAndAgainPutsItOn()
    {
        var menu = Menu();
        var alaron = menu.Party.Slots[0]!;
        var sword = alaron.Equipment.Weapon!;
        Assert.Equal((sword, (int?)0), (menu.Inventory.Current!.Instance, menu.Inventory.Current.Wearer));
        Assert.Equal("Lodin's Sword", sword.Item.Name);

        menu.Accept();
        Assert.Null(menu.Message);
        Assert.Null(alaron.Equipment.Weapon);
        Assert.True(menu.Party.Inventory!.Has(sword.Id));
        Assert.Equal(0, menu.Inventory.Cursor);

        menu.Point(menu.Inventory.Rows.Select((r, i) => (r, i)).First(e => e.r.Wearer is null && e.r.Item.Id == sword.Id).i);
        menu.Accept();
        Assert.Null(menu.Message);
        Assert.Equal(sword.Id, alaron.Equipment.Weapon?.Id);
        Assert.False(menu.Party.Inventory.Has(sword.Id));
    }

    /// <summary>The menu offers the imported recipes in order, the Alchemist's first.</summary>
    [Fact]
    public void TheMenuOffersTheRecipesInOrder()
    {
        var recipes = PartyMenu.From(Set, new CharacterParty(ItemCatalog.From(Set))).Recipes;

        Assert.Equal(20, recipes.Count);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => RecordId.Decimal(RecordTypes.Recipe, i)), recipes.Select(r => r.Id));
        Assert.All(recipes.Take(15), r => Assert.Equal(Crafting.Alchemist, r.Skill));
        Assert.All(recipes.Skip(15), r => Assert.Equal(Crafting.Mechanic, r.Skill));
    }

    /// <summary>The party is Alaron, Rheda, Brenna and Sholeh in slots 0 to 3, with the gold the capture shows.</summary>
    [Fact]
    public void ThePartyIsTheFourMembersInTheirSlotsWithTheirGold()
    {
        var party = Menu().Party;

        Assert.Equal([Entity(0x023F), Entity(0x024C), Entity(0x0243), Entity(0x024D)], party.Slots.Select(m => m!.Entity));
        Assert.Equal(321306u, party.Gold);
    }

    /// <summary>The header's portraits go from the member shown round the party.</summary>
    [Fact]
    public void PortraitsGoFromTheMemberShownRoundTheParty()
    {
        var party = Party(Load());

        Assert.Equal([0, 1, 2, 3], PartyMenu.From(Set, party, 0).PortraitOrder);
        Assert.Equal([2, 3, 0, 1], PartyMenu.From(Set, party, 2).PortraitOrder);
        Assert.Equal([3, 0, 1, 2], PartyMenu.From(Set, party, 3).PortraitOrder);
    }

    /// <summary>Hit points, level and the hit bar's cut read as the capture's header does.</summary>
    [Fact]
    public void TheHeaderShowsHitPointsLevelAndTheBarsShare()
    {
        var party = Party(Load());
        var alaron = PartyMenu.From(Set, party, 0);
        var brenna = PartyMenu.From(Set, party, 2);
        var sholeh = PartyMenu.From(Set, party, 3);

        Assert.Equal((116, 117, 20), (alaron.Shown!.Hp, alaron.Shown.MaxHp, alaron.Shown.Stats.Base(Stat.Level)));
        Assert.Equal(79, alaron.HealthBar(80));
        Assert.Equal((100, 100, 19), (brenna.Shown!.Hp, brenna.Shown.MaxHp, brenna.Shown.Stats.Base(Stat.Level)));
        Assert.Equal(80, brenna.HealthBar(80));
        Assert.Equal((112, 112, 22), (sholeh.Shown!.Hp, sholeh.Shown.MaxHp, sholeh.Shown.Stats.Base(Stat.Level)));
    }

    /// <summary>Alaron's attributes with what his gear changes, and the experience lines under them.</summary>
    [Fact]
    public void AlaronsStatsAndExperienceAreWhatTheCaptureShows()
    {
        var menu = Menu();

        Assert.Equal(
            [("Intelligence", 21, 0), ("Willpower", 15, 0), ("Dexterity", 30, -3), ("Endurance", 18, 0), ("Strength", 21, -12), ("Stamina", 79, -1)],
            menu.Stats.Rows.Select(r => (r.Name, r.Value, r.Change)));
        Assert.Equal(new ExperienceLines("XP Remaining:", "659576", "Total XP:", "1602076", "Next Level:", "53424"), menu.Experience);
    }

    /// <summary>Alaron's skills: the five with levels, with icons, then weapon skills and the shield, without.</summary>
    [Fact]
    public void AlaronsSkillsAreTheSkillsThenTheWeaponSkillsThenTheShield()
    {
        var rows = Menu().Skills.Rows;

        Assert.Equal(
            [("Loremaster", 2, 0), ("Stealth", 10, 5), ("Troubadour", 5, 0), ("Warrior", 3, 3), ("Wizard", 10, 0), ("Hafted", 1, 0),
                ("Missile", 3, 0), ("Sword", 4, 0), ("Shield", 3, 0)],
            rows.Select(r => (r.Name, r.Value, r.Change)));
        Assert.All(rows.Take(5), r => Assert.NotNull(r.Icon));
        Assert.All(rows.Skip(5), r => Assert.Null(r.Icon));
    }

    /// <summary>Brenna's skills, with her Stealth lowered by what she wears.</summary>
    [Fact]
    public void BrennasSkillsAreWhatTheCaptureShows()
    {
        var rows = Menu(2).Skills.Rows;

        Assert.Equal([("Mechanic", 1, 0), ("Stealth", 3, -3), ("Thief", 10, 2), ("Sword", 5, 0), ("Thrown", 2, 0)],
            rows.Select(r => (r.Name, r.Value, r.Change)));
    }

    /// <summary>
    /// Alaron's spells in the order learnt, with the icons the capture draws: the vs. spells a bowtie
    /// symbol alone (0x01AA, not 0x01AE, which is a different, round symbol), and the others their
    /// school's icon and symbol.
    /// </summary>
    [Fact]
    public void AlaronsSpellsHaveTheirSchoolsAndSymbolsIcons()
    {
        var rows = Menu().Spells.Rows;

        Assert.Equal(
            [("vs. Elemental", 10, null, Image(0x01AA)), ("vs. Naming", 10, null, Image(0x01AA)), ("vs. Necromancy", 10, null, Image(0x01AA)),
                ("vs. Star", 10, null, Image(0x01AA)), ("Air Shield", 3, Image(0x01A5), Image(0x01AB)), ("Wind", 10, Image(0x01A5), Image(0x01B1)),
                ("Spirit Shield", 10, Image(0x01A7), Image(0x01AB))],
            rows.Select(r => (r.Name, r.Rank, r.School, r.Symbol)));
        Assert.All(rows, r => Assert.Null(r.Aspect));
    }

    /// <summary>The first eight inventory rows with Alaron shown: his gear, then Rheda's, then Brenna's, each of one, by kind and name.</summary>
    [Fact]
    public void TheInventoryWithAlaronShownStartsWithWhatTheMembersWearInPartyOrder()
    {
        var rows = Menu(0).Inventory.Rows;

        Assert.Equal(
            [("Lodin's Sword", 0), ("Enchanted Hide", 0), ("Small Shield", 0), ("Staff", 1), ("Leather Armor", 1), ("Dagger", 2), ("Rope", 2),
                ("Scale Armor", 2)],
            rows.Take(8).Select(r => (r.Item.Name, r.Wearer!.Value)));
        Assert.All(rows.Take(8), r => Assert.Equal(1, r.Quantity));
    }

    /// <summary>With Sholeh shown the list starts with her gear and then wraps round to Alaron's.</summary>
    [Fact]
    public void TheInventoryWithSholehShownStartsWithHerGearAndWrapsRound()
    {
        var rows = Menu(3).Inventory.Rows;

        Assert.Equal(
            [("Sabre", 3), ("Jundar Leather", 3), ("Lodin's Sword", 0), ("Enchanted Hide", 0), ("Small Shield", 0), ("Staff", 1),
                ("Leather Armor", 1), ("Dagger", 2)],
            rows.Take(8).Select(r => (r.Item.Name, r.Wearer!.Value)));
        Assert.All(rows.Take(8), r => Assert.Equal(1, r.Quantity));
    }

    /// <summary>Everything worn is listed before everything carried, and a carried row has its stack's quantity.</summary>
    [Fact]
    public void WornItemsComeBeforeCarriedOnesWhichKeepTheirStacksQuantities()
    {
        var menu = Menu();
        var rows = menu.Inventory.Rows;
        var worn = menu.Party.Members.Sum(m => m.Equipment.All.Count());
        var stacks = menu.Party.Inventory!.Stacks;

        Assert.Equal(worn + stacks.Count, rows.Count);
        Assert.All(rows.Take(worn), r => Assert.NotNull(r.Wearer));
        Assert.All(rows.Skip(worn), r => Assert.Null(r.Wearer));
        var carried = rows.Skip(worn).ToList();
        Assert.All(stacks, stack => Assert.Contains(carried, r => r.Instance == stack.Instance && r.Quantity == stack.Quantity));
        Assert.Contains(carried, r => r.Item.Name == "Gemstone" && r.Quantity == 99);
        // Carried rows are in the list's order: by kind, then by name.
        Assert.Equal(carried.Select(r => r.Item).Order(Comparer<Item>.Create(InventoryOrder.Compare)), carried.Select(r => r.Item));
    }

    /// <summary>The silhouette shows Alaron's sword, armor and shield, and the empty slots' own images.</summary>
    [Fact]
    public void TheSilhouetteShowsWhatAlaronWearsAndTheEmptySlotsOwnImages()
    {
        var menu = Menu();
        var items = ItemCatalog.From(Set);

        Assert.Equal(items.All.First(i => i.Name == "Lodin's Sword").Icon, menu.SlotImage(EquipmentSlot.Weapon));
        Assert.Equal(items.All.First(i => i.Name == "Enchanted Hide").Icon, menu.SlotImage(EquipmentSlot.Armor));
        Assert.Equal(items.All.First(i => i.Name == "Small Shield").Icon, menu.SlotImage(EquipmentSlot.Shield));
        var empty = Enum.GetValues<EquipmentSlot>().Except([EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Shield]).ToList();
        Assert.All(empty, slot =>
        {
            Assert.Null(menu.Worn(slot));
            Assert.Equal(MenuImages.EmptySlots[(int)slot], menu.SlotImage(slot));
        });
        Assert.Equal(Image(0x35F9), menu.SlotImage(EquipmentSlot.Helmet));
    }

    /// <summary>C-down on Lodin's Sword lists its skill, aspect, requirements, hit and damage, then what it adds, its spell and its resistance.</summary>
    [Fact]
    public void LodinsSwordsDetailsAreTheCapturesLines()
    {
        var menu = Menu();

        var details = menu.Details()!;

        Assert.Equal("Lodin's Sword", details.Title);
        Assert.Equal(menu.SlotImage(EquipmentSlot.Weapon), details.Icon);
        Assert.Empty(details.Footer);
        Assert.Null(details.FooterTop);
        Assert.Null(details.FooterBottom);
        Assert.Equal(
            [("Skill", "Sword", false), ("Aspect", null, false), ("Min. Strength", "15", false), ("Base Hit", "75", false), ("Damage", "5", false),
                ("Strength", "(+5)", false), ("Warrior", "(+3)", false), ("Spell:", "", false), ("Whitefire", "", true), ("Resists:", "", false),
                ("Necromancy", "50", true)],
            details.Lines.Select(l => (Text(l), l.Right.Count == 0 ? "" : l.Right.OfType<DetailText>().Select(t => t.Text).FirstOrDefault(), l.Indent)));
        Assert.Equal(new DetailImage(Image(0x01A4)), Assert.Single(details.Lines[1].Right));
    }

    /// <summary>C-down on a stat shows its description as one wrapped line.</summary>
    [Fact]
    public void AStatsDetailsAreItsDescriptionInOneWrappedLine()
    {
        var menu = Menu();
        menu.ShowList(PartyList.Stats);

        var details = menu.Details()!;

        Assert.Null(details.Title);
        var line = Assert.Single(details.Lines);
        Assert.True(line.Wrap);
        Assert.StartsWith("Intelligence affects chances of successfully casting spells", Text(line));
    }

    /// <summary>The body armor Alaron wears has totals that equal what he has now, in the plain colour.</summary>
    [Fact]
    public void TheArmorAlaronWearsHasTotalsThatEqualHisOwn()
    {
        var menu = Menu();
        menu.Point(1);

        var details = menu.Details()!;

        Assert.Equal("Enchanted Hide", details.Title);
        Assert.Equal(213, details.FooterBottom);
        Assert.Equal(["Total Protection", "Total Defense"], details.Footer.Select(Text));
        var protection = details.Footer[0].Right.OfType<DetailText>().ToList();
        Assert.Equal(["6", "(6)"], protection.Select(t => t.Text));
        Assert.Equal(MenuDetailsBuilder.TextColor, protection[1].Color);
        Assert.Equal(["3", "(3)"], details.Footer[1].Right.OfType<DetailText>().Select(t => t.Text));
    }

    /// <summary>
    /// Armor others wear shows what Alaron's totals would become: Leather Armor (2, against his 4) costs
    /// him 2 Protection, in red, and Scale Armor (4) changes nothing.
    /// </summary>
    [Fact]
    public void ArmorOthersWearShowsWhatAlaronsTotalsWouldBecome()
    {
        var menu = Menu();
        var alaron = menu.Shown!.Equipment;
        Assert.Equal((6, 4), (alaron.ArmorProtection, alaron.Armor!.Item.Armor!.Protection));

        menu.Point(4);
        var leather = menu.Details()!;
        Assert.Equal("Leather Armor", leather.Title);
        var totals = leather.Footer[0].Right.OfType<DetailText>().ToList();
        Assert.Equal([("6", MenuDetailsBuilder.TextColor), ("(4)", MenuDetailsBuilder.Loss)], totals.Select(t => (t.Text, t.Color)));
        Assert.Equal(new Rgba(128, 0, 0, 255), MenuDetailsBuilder.Loss);

        menu.Point(7);
        var scale = menu.Details()!;
        Assert.Equal("Scale Armor", scale.Title);
        var same = scale.Footer[0].Right.OfType<DetailText>().ToList();
        Assert.Equal([("6", MenuDetailsBuilder.TextColor), ("(6)", MenuDetailsBuilder.TextColor)], same.Select(t => (t.Text, t.Color)));
    }

    /// <summary>A carried armor better than Alaron's shows his gain in green, and a worse one his loss in red, by the records' protection.</summary>
    [Fact]
    public void CarriedArmorShowsGainsInGreenAndLossesInRed()
    {
        var menu = Menu();
        var current = menu.Shown!.Equipment.ArmorProtection;
        var worn = menu.Shown.Equipment.Armor!.Item.Armor!.Protection;
        var checkedGain = false;
        var checkedLoss = false;

        for (var row = 0; row < menu.Inventory.Rows.Count; row++)
        {
            if (menu.Inventory.Rows[row] is not { Wearer: null, Item.Armor: { Slot: ArmorSlot.Body } armor })
                continue;
            menu.Point(row);
            var total = menu.Details()!.Footer[0].Right.OfType<DetailText>().ToList();
            var withIt = current - worn + armor.Protection;
            Assert.Equal([current.ToString(), $"({withIt})"], total.Select(t => t.Text));
            var colour = withIt > current ? MenuDetailsBuilder.Gain : withIt < current ? MenuDetailsBuilder.Loss : MenuDetailsBuilder.TextColor;
            Assert.Equal(colour, total[1].Color);
            checkedGain |= withIt > current;
            checkedLoss |= withIt < current;
        }

        Assert.True(checkedGain && checkedLoss);
        Assert.Equal(new Rgba(0, 128, 0, 255), MenuDetailsBuilder.Gain);
    }

    /// <summary>Changing member shows the next slot, or the one before, wrapping, and keeps the inventory cursor's row.</summary>
    [Fact]
    public void ChangingMemberStepsRoundTheSlotsAndKeepsTheInventoryCursor()
    {
        var menu = Menu();
        menu.Point(5);

        menu.ChangeMember(1);
        Assert.Equal((1, 5), (menu.Member, menu.Inventory.Cursor));
        menu.ChangeMember(-1);
        menu.ChangeMember(-1);
        Assert.Equal((3, 5), (menu.Member, menu.Inventory.Cursor));
        Assert.Equal([3, 0, 1, 2], menu.PortraitOrder);
    }
}
