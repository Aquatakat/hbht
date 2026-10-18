using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>Potions, flasks and the inventory record's messages for them, for the potion and Party tab tests.</summary>
internal static class PotionData
{
    public static ItemRecord Of(Potion potion) =>
        ItemData.NewItem(0, ItemKind.Potion) with { Id = Potions.Id(potion), Name = potion.ToString(), Description = $"{potion} potion." };

    public static readonly InventoryMessages Messages = new("Not now.", "Not in trek.", "Only the lighthouse.");

    public static ItemCatalog Catalog(params object[] more) => ItemData.CatalogWith(TradeData.Rules() with { Messages = Messages },
        [TradeData.Spice, TradeData.Herb, TradeData.Gem, TradeData.Hide, TradeData.Leather, .. Enum.GetValues<Potion>().Select(Of), .. more]);

    /// <summary>A member called <paramref name="name"/>, with the given skills and a distinct entity.</summary>
    public static Character Member(string name, int n = 0, SkillLevels? skills = null, int intelligence = 18, int stamina = 30,
        int strength = 15) => ItemData.Alaron(e => e with
        {
            Id = RecordId.Hex(RecordTypes.Entity, 0x0300 + n),
            Name = name,
            Level = 1,
            Attributes = new Attributes(intelligence, 12, 15, 15, strength, stamina),
            Skills = skills ?? new SkillLevels(),
        });
}

public class PotionTests
{
    private static (CharacterParty Party, Character Member) Setup(params Potion[] carried)
    {
        // A skill that can't be learnt can't be raised, so these start at 0.
        var member = PotionData.Member("Alaron", skills: new SkillLevels(Loremaster: 0, Diplomat: 0, Stealth: 0));
        var party = TradeData.PartyOf(PotionData.Catalog(), null, member);
        foreach (var potion in carried)
            party.Inventory.Add(Potions.Id(potion));
        return (party, member);
    }

    private static Item Item(CharacterParty party, Potion potion) => party.Inventory.Catalog[Potions.Id(potion)];

    [Fact]
    public void PotionsAreItemsTenHexByTheirNumberAndTheFirstFourAreFlasks()
    {
        Assert.Equal(RecordId.Hex(RecordTypes.Item, 0x1004), Potions.Id(Potion.Healing));
        Assert.Equal(Potion.Stealth, Potions.Of(RecordId.Hex(RecordTypes.Item, 0x1010)));
        Assert.Null(Potions.Of(RecordId.Hex(RecordTypes.Item, 0x1011)));
        Assert.True(Potions.IsFlask(Potion.AcidFlask));
        Assert.False(Potions.IsFlask(Potion.Healing));
    }

    /// <summary>A healing potion at full health would do nothing, so it isn't drunk.</summary>
    [Fact]
    public void AHealingPotionAtFullHealthIsNotDrunk()
    {
        var (party, member) = Setup(Potion.Healing);

        Assert.Equal(DrinkResult.NotNow, Potions.Drink(party, member, Item(party, Potion.Healing), new ScriptedRandom()));
        Assert.Equal(1, party.Inventory.Count(Potions.Id(Potion.Healing)));
    }

    /// <summary>Healing heals a share of the maximum from 15% to 25%, truncated, and Curing 40% to 60%.</summary>
    [Fact]
    public void HealingAndCuringHealAShareOfTheMaximumHitPoints()
    {
        var (party, member) = Setup(Potion.Healing, Potion.Curing);
        member.TakeDamage(1000);
        Assert.Equal(0, member.Hp);
        var max = member.MaxHp;

        Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, Potion.Healing), new ScriptedRandom(RandomFor.Range(15, 25, 20))));
        Assert.Equal((int)(0.2f * max), member.Hp);
        Assert.False(party.Inventory.Has(Potions.Id(Potion.Healing)));

        var before = member.Hp;
        Potions.Drink(party, member, Item(party, Potion.Curing), new ScriptedRandom(RandomFor.Range(40, 60, 40)));
        Assert.Equal(before + (int)(0.4f * max), member.Hp);
    }

    /// <summary>The Stamina potion works only below Stamina's base, the machine code's test, and restores it.</summary>
    [Fact]
    public void TheStaminaPotionWorksOnlyBelowStaminasBaseAndRestoresIt()
    {
        var (party, member) = Setup(Potion.Stamina);
        Assert.Equal(DrinkResult.NotNow, Potions.Drink(party, member, Item(party, Potion.Stamina), new ScriptedRandom()));

        member.DrainStamina(5);
        Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, Potion.Stamina), new ScriptedRandom()));
        Assert.Equal(member.Stats.Base(Stat.Stamina), member.Stats.Modified(Stat.Stamina));
    }

    /// <summary>Flasks and the left-out potions can't be drunk outside combat; the Antidote and Restore need effects combat brings.</summary>
    [Theory]
    [InlineData(Potion.FireFlask, DrinkResult.NotInTrek)]
    [InlineData(Potion.AcidFlask, DrinkResult.NotInTrek)]
    [InlineData(Potion.Jumping, DrinkResult.NotInTrek)]
    [InlineData(Potion.Aspect, DrinkResult.NotInTrek)]
    [InlineData(Potion.Antidote, DrinkResult.NotNow)]
    [InlineData(Potion.Restore, DrinkResult.NotNow)]
    public void SomePotionsCantBeDrunkInTrek(Potion potion, DrinkResult expected)
    {
        var (party, member) = Setup(potion);

        Assert.Equal(expected, Potions.Drink(party, member, Item(party, potion), new ScriptedRandom()));
        Assert.True(party.Inventory.Has(Potions.Id(potion)));
    }

    /// <summary>
    /// Strength adds 10 to the base for 3 hours of game time, which trek runs through in 9,000 ticks of play.
    /// One already working can't be drunk again. It ends on the tick after it runs down, taking the 10 off.
    /// </summary>
    [Fact]
    public void AStrengthPotionRaisesTheBaseForThreeHoursOfGameTime()
    {
        var (party, member) = Setup(Potion.Strength, Potion.Strength);
        var strength = member.Stats.Base(Stat.Strength);

        Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, Potion.Strength), new ScriptedRandom()));
        Assert.Equal(strength + 10, member.Stats.Base(Stat.Strength));
        Assert.Equal(strength + 10, member.Stats.Modified(Stat.Strength));
        Assert.Equal(DrinkResult.NotNow, Potions.Drink(party, member, Item(party, Potion.Strength), new ScriptedRandom()));

        var ticks = (int)(Potions.Duration / GameClock.DaySpeed);
        Assert.Equal(9000, ticks);
        member.StepPotionEffects(ticks - 1);
        Assert.Equal(GameClock.DaySpeed, (int)member.PotionEffects.Single().Timer);
        member.StepPotionEffects(1);
        Assert.Equal(0u, member.PotionEffects.Single().Timer);
        Assert.Equal(strength + 10, member.Stats.Base(Stat.Strength));
        member.StepPotionEffects(1);
        Assert.Empty(member.PotionEffects);
        Assert.Equal(strength, member.Stats.Base(Stat.Strength));
    }

    /// <summary>Clarity, Charisma and Stealth add 3 to Loremaster, Diplomat and Stealth while they work.</summary>
    [Fact]
    public void SkillPotionsAddThreeToTheirSkillWhileTheyWork()
    {
        var (party, member) = Setup(Potion.Clarity, Potion.Charisma, Potion.Stealth);
        var before = new[] { Skill.Loremaster, Skill.Diplomat, Skill.Stealth }.Select(s => member.Skills.RawModified(s)).ToArray();

        foreach (var potion in new[] { Potion.Clarity, Potion.Charisma, Potion.Stealth })
            Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, potion), new ScriptedRandom()));
        Assert.Equal(before.Select(b => b + 3), new[] { Skill.Loremaster, Skill.Diplomat, Skill.Stealth }.Select(s => member.Skills.RawModified(s)));

        member.StepPotionEffects((int)(Potions.Duration / GameClock.DaySpeed));
        member.StepPotionEffects(1);
        Assert.Equal(before, new[] { Skill.Loremaster, Skill.Diplomat, Skill.Stealth }.Select(s => member.Skills.RawModified(s)));
    }

    /// <summary>A character holds seven potions' effects at most, and a dead one's don't run down.</summary>
    [Fact]
    public void ACharacterHoldsSevenEffectsAndADeadOnesDontRunDown()
    {
        var member = PotionData.Member("Alaron");
        foreach (var potion in Enumerable.Repeat(Potion.Defence, 9))
            member.ApplyPotionEffect(potion, 0, 100);
        Assert.Equal(Character.PotionSlots, member.PotionEffects.Count);

        member.TakeDamage(1000);
        member.StepPotionEffects(1000);
        Assert.All(member.PotionEffects, p => Assert.Equal(100u, p.Timer));
    }
}

public class PartyMenuActionTests
{
    private static readonly WeaponRecord Sword = ItemData.NewWeapon(1, WeaponClass.Sword);
    private static readonly WeaponRecord Heavy = ItemData.NewWeapon(2, WeaponClass.Sword, strength: 40);
    private static readonly WeaponRecord Staff = ItemData.NewWeapon(3, WeaponClass.Sword, isFixed: true);
    private static readonly SpellRecord Fireball = Characters.Fireball;
    private static readonly ItemRecord Scroll = ItemData.NewItem(10, ItemKind.Scroll, bonuses: ItemData.CastsSpell(Fireball.Id));
    private static readonly ItemRecord Rock = ItemData.NewItem(11);

    /// <summary>Line n of the common strings is "n" and its conversions, so a message shows which line it is and what filled it.</summary>
    private static readonly MenuText Text = new([.. Enumerable.Range(0, 600).Select(i => $"{i}|%s|%s|%d")]);

    private static PartyMenu Menu(CharacterParty party, params RecipeRecord[] recipes) =>
        new(party, Text, id => id == Fireball.Id ? Fireball : null, _ => null) { Recipes = recipes, Random = new ScriptedRandom() };

    private static (CharacterParty Party, Character Member) Setup(SkillLevels? skills = null, params Character[] others)
    {
        var member = PotionData.Member("Alaron", 0, skills);
        var party = TradeData.PartyOf(PotionData.Catalog(Sword, Heavy, Staff, Scroll, Rock), null, [member, .. others]);
        return (party, member);
    }

    private static void PointAt(PartyMenu menu, Func<InventoryRow, bool> row) =>
        menu.Point(menu.Inventory.Rows.Select((r, i) => (r, i)).First(e => row(e.r)).i);

    private static int Line(PartyMenu menu) => int.Parse(menu.Message!.Text.Split('|')[0], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ACarriedWeaponIsEquippedAndAWornOneTakenOff()
    {
        var (party, member) = Setup();
        party.Inventory.Add(Sword.Id);
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Sword.Id);
        menu.Accept();
        Assert.Equal(Sword.Id, member.Equipment.Weapon?.Id);
        Assert.Null(menu.Message);
        Assert.Contains(menu.Inventory.Rows, r => r.Item.Id == Sword.Id && r.Wearer == 0);

        PointAt(menu, r => r.Item.Id == Sword.Id);
        menu.Accept();
        Assert.Null(member.Equipment.Weapon);
        Assert.Contains(menu.Inventory.Rows, r => r.Item.Id == Sword.Id && r.Wearer is null);
    }

    /// <summary>Equipping from a stack leaves the cursor on what's left of it.</summary>
    [Fact]
    public void EquippingFromAStackLeavesTheCursorOnTheStack()
    {
        var (party, _) = Setup();
        party.Inventory.Add(Sword.Id, 2);
        party.Inventory.Add(Rock.Id);
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Sword.Id);
        menu.Accept();

        Assert.Equal((Sword.Id, (int?)null, 1), (menu.Inventory.Current!.Item.Id, menu.Inventory.Current.Wearer, menu.Inventory.Current.Quantity));
    }

    [Fact]
    public void AnItemTheMemberCantEquipSaysWhyAndStaysCarried()
    {
        var (party, member) = Setup();
        party.Inventory.Add(Heavy.Id);
        party.Inventory.Add(Rock.Id);
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Heavy.Id);
        menu.Accept();
        Assert.Equal(MenuText.NeedsStrength, Line(menu));
        Assert.Null(member.Equipment.Weapon);

        menu.Accept();
        PointAt(menu, r => r.Item.Id == Rock.Id);
        menu.Accept();
        Assert.Equal(MenuText.CantEquip, Line(menu));
    }

    /// <summary>The Archmage Staff can't be taken off: its message names it and its wielder.</summary>
    [Fact]
    public void TheStaffCantBeTakenOffAndSaysWhoCanControlIt()
    {
        var (party, member) = Setup();
        party.Inventory.Add(Staff.Id);
        Assert.Equal(EquipRefusal.None, party.Equip(member, Staff.Id));
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Staff.Id);
        menu.Accept();

        Assert.Equal($"{MenuText.PowerfulMagic}|{Staff.Name}|Alaron|", menu.Message!.Text);
        Assert.Equal(Staff.Id, member.Equipment.Weapon?.Id);
    }

    /// <summary>
    /// A potion that would do nothing says so with the inventory record's message. While it shows, the
    /// cursor, the member and the list don't move, and B closes it rather than the menu.
    /// </summary>
    [Fact]
    public void AMessageTakesThePressesUntilItsClosed()
    {
        var other = PotionData.Member("Brenna", 1);
        var (party, _) = Setup(null, other);
        party.Inventory.Add(Potions.Id(Potion.Healing));
        party.Inventory.Add(Rock.Id);
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Potions.Id(Potion.Healing));
        menu.Accept();
        Assert.Equal(PotionData.Messages.PotionNotNow, menu.Message!.Text);

        var cursor = menu.Inventory.Cursor;
        menu.MoveCursor(1);
        menu.ChangeMember(1);
        menu.ChangeList(1);
        Assert.Equal((cursor, 0, PartyList.Inventory), (menu.Inventory.Cursor, menu.Member, menu.List));

        Assert.True(menu.Back());
        Assert.Null(menu.Message);
        Assert.False(menu.Back());
    }

    [Fact]
    public void ADrunkPotionIsUsedUpAndAFlaskSaysItsForCombat()
    {
        var (party, member) = Setup();
        party.Inventory.Add(Potions.Id(Potion.Strength));
        party.Inventory.Add(Potions.Id(Potion.FireFlask));
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Potions.Id(Potion.Strength));
        menu.Accept();
        Assert.Null(menu.Message);
        Assert.True(member.HasPotionEffect(Potion.Strength));
        Assert.DoesNotContain(menu.Inventory.Rows, r => r.Item.Id == Potions.Id(Potion.Strength));

        PointAt(menu, r => r.Item.Id == Potions.Id(Potion.FireFlask));
        menu.Accept();
        Assert.Equal(PotionData.Messages.PotionNotInTrek, menu.Message!.Text);
    }

    /// <summary>Reading a scroll teaches its spell and says so, naming the reader and the spell; reading it again says they know it.</summary>
    [Fact]
    public void ReadingAScrollTeachesItsSpellAndSaysSo()
    {
        var (party, member) = Setup(new SkillLevels(Wizard: 3));
        party.Inventory.Add(Scroll.Id, 2);
        var menu = Menu(party);

        PointAt(menu, r => r.Item.Id == Scroll.Id);
        menu.Accept();
        Assert.Equal($"{MenuText.Learned}|Alaron|{Fireball.Name}|", menu.Message!.Text);
        Assert.NotNull(member.FindSpell(Fireball.Id));

        menu.Accept();
        PointAt(menu, r => r.Item.Id == Scroll.Id);
        menu.Accept();
        Assert.Equal(MenuText.AlreadyKnows, Line(menu));
    }

    private static readonly RecipeRecord Strength = TradeData.Recipe(0, Crafting.Alchemist, Potions.Id(Potion.Strength), 1, 0,
        new Ingredient(TradeData.Herb.Id, 1));

    private static readonly RecipeRecord Fire = TradeData.Recipe(1, Crafting.Alchemist, Potions.Id(Potion.FireFlask), 3, 0,
        new Ingredient(TradeData.Spice.Id, 1));

    private static readonly RecipeRecord Armor = TradeData.Recipe(2, Crafting.Mechanic, TradeData.Leather.Id, 1, 0,
        new Ingredient(TradeData.Hide.Id, 1));

    private static void OpenSkill(PartyMenu menu, Skill skill)
    {
        menu.ShowList(PartyList.Skills);
        menu.Point(menu.Skills.Rows.Select((r, i) => (r, i)).First(e => e.r.Skill == skill).i);
        menu.Accept();
        Fade(menu);
    }

    /// <summary>Steps the menu the five ticks a popup takes to fade fully in, or out from open (<see cref="PopupFade"/>).</summary>
    private static void Fade(PartyMenu menu)
    {
        for (var i = 0; i < 5; i++)
            menu.Step();
    }

    /// <summary>
    /// A on the Alchemist lists the recipes their level reaches by their potion's name; C-down shows the potion.
    /// Brewing says how it went, and closing that closes the popup too.
    /// </summary>
    [Fact]
    public void TheAlchemistBrewsFromAPopupOfRecipes()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2));
        party.Inventory.Add(TradeData.Herb.Id);
        var menu = Menu(party, Strength, Fire);
        menu.Random = new ScriptedRandom(RandomFor.D(100, 1));

        OpenSkill(menu, Skill.Alchemist);
        var crafting = Assert.IsType<CraftingMenu>(menu.Crafting);
        Assert.Equal((CraftingTask.Alchemy, $"{MenuText.Alchemy}|%s|%s|%d"), (crafting.Task, crafting.Title));
        Assert.Equal(["Strength"], crafting.Rows.Rows.Select(r => r.Text));
        Assert.Equal("Strength", menu.Details()!.Title);

        menu.Accept();
        Assert.Equal(MenuText.PotionMade, Line(menu));
        Assert.Equal(1, party.Inventory.Count(Potions.Id(Potion.Strength)));
        Assert.Contains(menu.Inventory.Rows, r => r.Item.Id == Potions.Id(Potion.Strength));

        menu.Accept();
        Assert.True(menu.HasPopup);
        Fade(menu);
        Assert.False(menu.HasPopup);
    }

    /// <summary>A flask's recipe uses the flask lines: here, that the party lacks its reagents.</summary>
    [Fact]
    public void AFlasksRecipeSaysFlask()
    {
        var (party, _) = Setup(new SkillLevels(Alchemist: 3));
        var menu = Menu(party, Fire);

        OpenSkill(menu, Skill.Alchemist);
        menu.Accept();

        Assert.Equal(MenuText.FlaskComponents, Line(menu));
    }

    /// <summary>Without a hide for any recipe the Mechanic's level reaches, A says so and opens nothing.</summary>
    [Fact]
    public void TheMechanicWithoutAHideSaysSo()
    {
        var (party, _) = Setup(new SkillLevels(Mechanic: 2));
        var menu = Menu(party, Armor);

        OpenSkill(menu, Skill.Mechanic);

        Assert.Equal(MenuText.NoArmorComponents, Line(menu));
        Assert.Null(menu.Crafting);
        menu.Accept();
        Assert.False(menu.HasPopup);
    }

    [Fact]
    public void TheMechanicMakesArmorFromAHide()
    {
        var (party, _) = Setup(new SkillLevels(Mechanic: 2));
        party.Inventory.Add(TradeData.Hide.Id);
        var menu = Menu(party, Armor);
        menu.Random = new ScriptedRandom(RandomFor.D(100, 1));

        OpenSkill(menu, Skill.Mechanic);
        Assert.Equal([TradeData.Leather.Name], menu.Crafting!.Rows.Rows.Select(r => r.Text));
        menu.Accept();

        Assert.Equal(MenuText.ArmorMade, Line(menu));
        Assert.True(party.Inventory.Has(TradeData.Leather.Id));
    }

    /// <summary>
    /// The Healer's popup offers three tasks. Laying on hands lists the living members but the Healer, with
    /// their health, and says how much it healed.
    /// </summary>
    [Fact]
    public void TheHealerLaysHandsOnAnotherLivingMember()
    {
        var brenna = PotionData.Member("Brenna", 1);
        var dead = TradeData.Dead(PotionData.Member("Rheda", 2));
        var (party, healer) = Setup(new SkillLevels(Healer: 2), brenna, dead);
        brenna.TakeDamage(10);
        var menu = Menu(party);

        OpenSkill(menu, Skill.Healer);
        Assert.Equal([MenuText.LayOnHands, MenuText.HealWithHerbs, MenuText.Restore],
            menu.Crafting!.Rows.Rows.Select(r => int.Parse(r.Text.Split('|')[0], System.Globalization.CultureInfo.InvariantCulture)));

        menu.Accept();
        Fade(menu);
        var targets = menu.Crafting!;
        Assert.Equal(CraftingTask.LayOnHands, targets.Task);
        Assert.Equal(["Brenna"], targets.Rows.Rows.Select(r => r.Text));
        Assert.Equal($"{MenuText.Health}|{brenna.Hp}|{brenna.MaxHp}|", targets.Rows.Rows[0].Health);

        var hp = brenna.Hp;
        menu.Accept();
        Assert.Equal($"{MenuText.HealsBy}|Alaron|Brenna|{brenna.Hp - hp}", menu.Message!.Text);
        Assert.Equal(hp + 2 * 2 + 5, brenna.Hp);
        Assert.Equal(healer.Stats.Base(Stat.Stamina) - 3, healer.Stats.Modified(Stat.Stamina));

        menu.Accept();
        Fade(menu);
        Assert.False(menu.HasPopup);
    }

    /// <summary>
    /// Restoring lists the six attributes for the member chosen; one that isn't hurt is at its maximum. Each list is
    /// built in the same popup, which stays open and whole, so the next takes presses at once (<c>Healer.ClearList</c>).
    /// </summary>
    [Fact]
    public void RestoringAnAttributeThatIsntHurtSaysItsAtMax()
    {
        var (party, _) = Setup(new SkillLevels(Healer: 2));
        party.Inventory.Add(TradeData.Herb.Id);
        var menu = Menu(party);

        OpenSkill(menu, Skill.Healer);
        var fade = menu.Crafting!.Fade;
        menu.Point(2);
        menu.Accept();
        Assert.Equal(CraftingTask.Restore, menu.Crafting!.Task);
        Assert.Equal(["Alaron"], menu.Crafting.Rows.Rows.Select(r => r.Text));
        Assert.Same(fade, menu.Crafting.Fade);
        Assert.Equal((PopupFadeState.Open, 255), (fade.State, fade.Alpha));

        menu.Accept();
        Assert.Equal(CraftingTask.RestoreStat, menu.Crafting!.Task);
        Assert.Same(fade, menu.Crafting.Fade);
        Assert.Equal(PartyMenu.RestoreOrder.Select(s => s.ToString()), menu.Crafting.Rows.Rows.Select(r => r.Text));

        menu.Accept();
        Assert.Equal($"{MenuText.StatAtMax}|Alaron|Intelligence|", menu.Message!.Text);
        Assert.True(party.Inventory.Has(TradeData.Herb.Id));
    }

    /// <summary>B fades a popup out at any depth, and the stick moves its cursor rather than the list's.</summary>
    [Fact]
    public void BClosesAPopupAndTheStickMovesItsCursor()
    {
        var (party, _) = Setup(new SkillLevels(Healer: 2));
        var menu = Menu(party);
        OpenSkill(menu, Skill.Healer);
        var listCursor = menu.Skills.Cursor;

        menu.MoveCursor(1);
        Assert.Equal((1, listCursor), (menu.Crafting!.Rows.Cursor, menu.Skills.Cursor));
        menu.ChangeList(1);
        Assert.Equal(PartyList.Skills, menu.List);

        Assert.True(menu.Back());
        Assert.NotNull(menu.Crafting);
        Fade(menu);
        Assert.Null(menu.Crafting);
    }

    /// <summary>A on a spell or a stat does nothing.</summary>
    [Fact]
    public void AOnAStatDoesNothing()
    {
        var (party, member) = Setup();
        var menu = Menu(party);
        menu.ShowList(PartyList.Stats);
        var hp = member.Hp;

        menu.Accept();

        Assert.False(menu.HasPopup);
        Assert.Equal(hp, member.Hp);
    }

    /// <summary>
    /// While a popup fades in, no press reaches it or the tab: the stick, A and B do nothing, and B doesn't close the
    /// menu. Once it's open it takes them; B fades it out, and while it does, nothing reaches it or the tab either.
    /// </summary>
    [Fact]
    public void APopupTakesNoPressesWhileItFades()
    {
        var (party, _) = Setup(new SkillLevels(Healer: 2));
        var menu = Menu(party);
        menu.ShowList(PartyList.Skills);
        menu.Point(menu.Skills.Rows.Select((r, i) => (r, i)).First(e => e.r.Skill == Skill.Healer).i);
        menu.Accept();
        var popup = menu.Crafting!;
        var listCursor = menu.Skills.Cursor;

        for (var i = 0; i < 4; i++)
        {
            menu.Step();
            menu.MoveCursor(1);
            menu.Accept();
            Assert.True(menu.Back());
            menu.ChangeList(1);
            Assert.Equal((0, listCursor, PartyList.Skills, PopupFadeState.Opening), (popup.Rows.Cursor, menu.Skills.Cursor, menu.List, popup.Fade.State));
            Assert.Same(popup, menu.Crafting);
        }
        menu.Step();
        Assert.True(popup.Fade.IsOpen);
        menu.MoveCursor(1);
        Assert.Equal(1, popup.Rows.Cursor);

        Assert.True(menu.Back());
        menu.Step();
        menu.MoveCursor(1);
        menu.Accept();
        Assert.True(menu.Back());
        Assert.Equal((1, PopupFadeState.Closing, 195), (popup.Rows.Cursor, popup.Fade.State, popup.Fade.Alpha));
        for (var i = 0; i < 4; i++)
            menu.Step();
        Assert.Null(menu.Crafting);
        Assert.False(menu.HasPopup);
    }

    /// <summary>
    /// C-down tapped and let go at once still fades the details fully in, and then straight out; while they show, the
    /// tab takes no presses, and B doesn't close the menu.
    /// </summary>
    [Fact]
    public void AQuickTapOfCDownFadesTheDetailsInThenOut()
    {
        var (party, _) = Setup();
        var menu = Menu(party);
        menu.ShowList(PartyList.Stats);
        var cursor = menu.Stats.Cursor;

        menu.PressDetails();
        menu.ReleaseDetails();
        var alphas = new List<int>();
        while (menu.DetailsPane.Showing)
        {
            menu.Step();
            alphas.Add(menu.DetailsPane.Alpha);
            if (!menu.DetailsPane.Showing)
                break;
            menu.MoveCursor(1);
            Assert.True(menu.Back());
        }

        Assert.Equal([60, 120, 180, 240, 255, 195, 135, 75, 15, 0], alphas);
        Assert.Equal(cursor, menu.Stats.Cursor);
        Assert.False(menu.Back());
    }

    /// <summary>Held, the details stay open; let go, they fade out. Pressing again while they fade doesn't reopen them.</summary>
    [Fact]
    public void TheDetailsStayWhileCDownIsHeld()
    {
        var (party, _) = Setup();
        var menu = Menu(party);
        menu.ShowList(PartyList.Stats);

        menu.PressDetails();
        for (var i = 0; i < 20; i++)
            menu.Step();
        Assert.Equal((PopupFadeState.Open, 255), (menu.DetailsPane.Fade!.State, menu.DetailsPane.Alpha));

        // Let go, they start out on the next update, and fade from the one after.
        menu.ReleaseDetails();
        menu.Step();
        Assert.Equal((PopupFadeState.Closing, 255), (menu.DetailsPane.Fade!.State, menu.DetailsPane.Alpha));
        menu.Step();
        Assert.Equal(195, menu.DetailsPane.Alpha);
        menu.PressDetails();
        for (var i = 0; i < 4; i++)
            menu.Step();
        Assert.False(menu.DetailsPane.Showing);
    }

    /// <summary>C-down shows nothing over a popup that's still fading in.</summary>
    [Fact]
    public void TheDetailsWaitForThePopupToOpen()
    {
        var (party, _) = Setup(new SkillLevels(Alchemist: 2));
        party.Inventory.Add(TradeData.Herb.Id);
        var menu = Menu(party, Strength);
        menu.ShowList(PartyList.Skills);
        menu.Point(menu.Skills.Rows.Select((r, i) => (r, i)).First(e => e.r.Skill == Skill.Alchemist).i);
        menu.Accept();

        menu.PressDetails();
        Assert.False(menu.DetailsPane.Showing);
        menu.ReleaseDetails();
        Fade(menu);
        menu.PressDetails();
        Assert.True(menu.DetailsPane.Showing);
    }
}
