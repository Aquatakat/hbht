using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>What A does on the Party tab, and the popups it opens (<c>DollMenuLists::AFunc</c>).</summary>
/// <remarks>
/// <para>
/// <b>The inventory</b> (<c>WidgetInvShop::AFunc</c>): A on a worn item takes it off, into the inventory;
/// on a carried one, the member shown drinks a potion, reads a scroll, or equips anything else, putting
/// back what it replaces. What can't be done says why in a message.
/// </para>
/// <para>
/// <b>The skills</b> (<c>WidgetSkillInfo::AFunc</c>, in trek only): A on the Alchemist, Mechanic or Healer
/// opens their popup. A on a spell or a stat does nothing.
/// </para>
/// <para>
/// A popup takes every press while it's open, as the original's widget handler gives them to the top
/// widget: the stick moves its cursor, A chooses, B closes it, and the member and list can't change. A
/// message closes with A, B or C-down; one a crafting popup shows closes that popup too, so each opening
/// does one task.
/// </para>
/// <para>
/// A crafting popup and C-down's details fade in as they open and out as they close (<see cref="PopupFade"/>), and
/// while either fades, no press reaches it or the tab under it, as the original's handler gives them to the top widget
/// only once it's open. The details take the presses while they show, and do nothing with them.
/// </para>
/// </remarks>
public sealed partial class PartyMenu
{
    /// <summary>The stream skill checks and potions draw from.</summary>
    public IRandom Random { get; set; } = new Lcg();

    /// <summary>The recipes the Alchemist and Mechanic offer, in order.</summary>
    public IReadOnlyList<RecipeRecord> Recipes { get; init; } = [];

    /// <summary>The message and C-down's details, which take the presses ahead of the tab and the crafting popup.</summary>
    public MenuOverlay Overlay { get; } = new();

    /// <summary>A message over everything, which A, B or C-down closes.</summary>
    public MenuMessage? Message => Overlay.Message;

    /// <summary>A skill's popup, under any message, kept while it fades out.</summary>
    public CraftingMenu? Crafting { get; private set; }

    /// <summary>C-down's details, over the tab or the crafting popup.</summary>
    public DetailsPane DetailsPane => Overlay.Details;

    /// <summary>Whether a popup or the details are taking the presses, fading or not.</summary>
    public bool HasPopup => Overlay.HasPopup || Crafting is not null;

    /// <summary>The crafting popup, if it's open and nothing over it takes the presses.</summary>
    private CraftingMenu? OpenCraftingPopup => Crafting is { Fade.IsOpen: true } crafting && !DetailsPane.Showing ? crafting : null;

    /// <summary>
    /// C-down pressed: shows the details of the cursor's row, or of the crafting popup's recipe, unless a popup is
    /// fading or the details already show. A message closes first (<see cref="CloseMessage"/>).
    /// </summary>
    public void PressDetails() =>
        Overlay.PressDetails(Message is null && (Crafting is null || OpenCraftingPopup is not null) && Details() is not null);

    /// <summary>C-down let go: the details fade out once they're open.</summary>
    public void ReleaseDetails() => Overlay.ReleaseDetails();

    /// <summary>Steps the crafting popup's and the details' fades by a tick, dropping the popup once it's gone.</summary>
    private void StepPopups()
    {
        if (Crafting is { } crafting)
        {
            crafting.Fade.Advance();
            if (crafting.Fade.IsGone)
                Crafting = null;
        }
        Overlay.Step();
    }

    /// <summary>A: closes a message, chooses in a popup, or uses the cursor's row.</summary>
    public void Accept()
    {
        if (Message is not null)
            CloseMessage();
        else if (DetailsPane.Showing)
            return;
        else if (Crafting is not null)
        {
            if (OpenCraftingPopup is { } crafting)
                Choose(crafting);
        }
        else if (List == PartyList.Inventory && Inventory.Current is { } item)
            Use(item);
        else if (List == PartyList.Skills && Skills.Current is { } skill)
            OpenCrafting(skill.Skill);
    }

    /// <summary>
    /// B: closes the message, or fades the popup out (<c>MenuPopup.OnB</c>, <c>0x80055e10</c>). False if there's none and
    /// the details don't show, so B closes the menu.
    /// </summary>
    public bool Back()
    {
        if (Message is not null)
            CloseMessage();
        else if (OpenCraftingPopup is { } crafting)
            crafting.Fade.Close();
        else if (Crafting is null && !DetailsPane.Showing)
            return false;
        return true;
    }

    /// <summary>Closes the message, and fades out the crafting popup under it if it showed its result (A, B or C-down). True if there was one.</summary>
    public bool CloseMessage()
    {
        if (Message is { FromCrafting: true })
            Crafting?.Fade.Close();
        return Overlay.CloseMessage();
    }

    // A plugin older than the messages in code has none, and says nothing.
    private void Say(string? text, bool fromCrafting = false)
    {
        if (text is not null)
            Overlay.Say(new MenuMessage(text, fromCrafting));
    }

    private string Format(int line, params object[] args) => _text.Format(line, args);

    private InventoryMessages? Messages => Party.Inventory.Catalog.Rules.Messages;

    /// <summary>
    /// In a battle's pause, its rules for the Party tab (<c>PartyMenu.InventoryA</c>, <c>0x80035FA0</c>): only the member whose turn it
    /// is may change anything, a weapon changed or a flask taken in hand ends the turn, potions are drunk from C-up, and body armor and
    /// shields stay as they are. Null in trek.
    /// </summary>
    public CombatPause? Combat { get; set; }

    // WidgetInvShop::AFunc. Whatever it does that isn't refused with a message, PartyMenu.InventoryA (0x80035fa0) ends
    // at 0x80036694 by crossfading the silhouette's changed slots and the header's numbers (PartyMenu.EquipmentChange,
    // HealthGoldChange): a potion drunk, a scroll read whatever it says, an item put on or taken off, a flask taken in hand.
    private void Use(InventoryRow row)
    {
        if (Shown is not { } member)
            return;
        var numbers = Numbers;
        var slots = WornIcons;
        if ((Combat is { } combat ? UseInCombat(combat, member, row) : null) ?? UseInTrek(member, row))
            ChangeHeader(numbers, slots);
    }

    // A on the row out of a battle's turn rules: whether it did something.
    private bool UseInTrek(Character member, InventoryRow row)
    {
        if (row.Wearer is { } slot)
            return TakeOff(row, slot);
        switch (row.Item.Kind)
        {
            case ItemKind.Potion:
                var drink = Potions.Drink(Party, member, row.Item, Random);
                Say(drink switch
                {
                    DrinkResult.NotNow => Messages?.PotionNotNow,
                    DrinkResult.NotInTrek => Messages?.PotionNotInTrek,
                    _ => null,
                });
                Refresh();
                return drink == DrinkResult.Drunk;
            case ItemKind.Scroll:
                Read(member, row.Item);
                Refresh();
                return true;
            default:
                return Equip(member, row);
        }
    }

    // The combat pause's A (PartyMenu.InventoryA): whether it did something, or null if it leaves the row to trek's rules.
    private bool? UseInCombat(CombatPause combat, Character member, InventoryRow row)
    {
        if (member != combat.Member)
        {
            Say(_text[row.Wearer is null ? MenuText.NotTheirTurnToEquip : MenuText.NotTheirTurnToRemove]);
            return false;
        }
        if (row.Wearer is not null)
        {
            // Body armor and a shield can't come off in combat (Party.TakeOffArmor, 0x80076140); a weapon goes with no turn spent and
            // leaves the member unarmed (Party.StowHeldItem).
            if (row.Item.Kind is ItemKind.Armor or ItemKind.Shield)
            {
                Say(_text[MenuText.NotInCombat]);
                return false;
            }
            if (row.Item.Kind == ItemKind.Weapon && row.Item.Weapon?.Fixed != true)
            {
                var off = TakeOff(row, row.Wearer.Value);
                combat.WeaponChanged(false);
                return off;
            }
            return null;
        }
        switch (row.Item.Kind)
        {
            case ItemKind.Potion when Potions.IsFlask(row.Item):
                // Party.EquipFlaskInCombat (0x800753F0): the flask in hand, the turn over as the menu closes.
                combat.HoldFlask(row.Item);
                Refresh();
                return true;
            case ItemKind.Potion:
                Say(_text[MenuText.PotionFromCUp]);
                return false;
            case ItemKind.Armor or ItemKind.Shield:
                Say(_text[MenuText.CantEquip]);
                return false;
            case ItemKind.Weapon:
                // Party.EquipWeapon (0x800758CC), then Combatant.AfterEquipChange: the turn ends as the menu closes.
                var before = member.Equipment.Weapon;
                var equipped = Equip(member, row);
                if (member.Equipment.Weapon != before)
                    combat.WeaponChanged(true);
                return equipped;
            case ItemKind.Scroll:
                // Party.EquipScroll (0x80075A58): worn in a gear slot, its spell on C-up, no turn spent.
                return Equip(member, row);
            default:
                return null;
        }
    }

    // Taking off: the Archmage Staff stays with its wielder, and an item the inventory has no room for
    // stays on without a word, as the original's does. False only for the staff, as the original's no room
    // still ends with the header's crossfade.
    private bool TakeOff(InventoryRow row, int slot)
    {
        if (Party.Slots.ElementAtOrDefault(slot) is not { } wearer)
            return false;
        if (row.Item.Weapon?.Fixed == true)
        {
            Say(Format(MenuText.PowerfulMagic, row.Item.Name, wearer.Name));
            return false;
        }
        Party.Unequip(wearer, row.Instance);
        Refresh();
        return true;
    }

    // Party::GetEquipError's messages (equip_error_labels). Too many rings, no gear slot free, or no room for
    // what it replaces give the general one, as GetEquipError3 and the item kinds' functions return 1.
    // Whether it was put on.
    private bool Equip(Character member, InventoryRow row)
    {
        var refusal = Party.Equip(member, row.Item.Id);
        switch (refusal)
        {
            case EquipRefusal.None:
                Refresh();
                // The cursor stays where it was, or on what's left of the stack it equipped from.
                if (Inventory.Rows.Select((r, i) => (r, i)).FirstOrDefault(e => e.r.Wearer is null && e.r.Item == row.Item) is { r: not null } left)
                    Inventory.Point(left.i);
                return true;
            case EquipRefusal.WrongAspect:
                Say(_text[MenuText.WrongAspect]);
                break;
            case EquipRefusal.NeedsStrength:
                Say(_text[MenuText.NeedsStrength]);
                break;
            case EquipRefusal.NeedsIntelligence:
                Say(_text[MenuText.NeedsIntelligence]);
                break;
            case EquipRefusal.Fixed when member.Equipment.Weapon is { } staff:
                // The port's refusal: the original lets the staff be replaced, and it vanishes.
                Say(Format(MenuText.PowerfulMagic, staff.Item.Name, member.Name));
                break;
            default:
                Say(_text[MenuText.CantEquip]);
                break;
        }
        return false;
    }

    // Party::UseScroll, which says how it went whatever happens.
    private void Read(Character member, Item scroll)
    {
        var result = Party.ReadScroll(member, scroll.Id, id => _spells(id) ?? throw new KeyNotFoundException($"No spell {id}."));
        Say(result switch
        {
            ScrollResult.Learned => Format(MenuText.Learned, member.Name, scroll.Bonuses.Spell is { } s ? _spells(s.Spell)?.Name ?? "" : ""),
            ScrollResult.NotAWizard => _text[MenuText.OnlyWizards],
            ScrollResult.NoSpell => Messages?.ScrollNoSpell,
            ScrollResult.AlreadyKnown => Format(MenuText.AlreadyKnows, member.Name),
            ScrollResult.NeedsWizard => _text[MenuText.NeedsWizardRank],
            _ => _text[MenuText.WrongSchool],
        });
    }

    // get_crafting_menu: the Alchemist, Healer and Mechanic have popups; other skills do nothing.
    private void OpenCrafting(SkillId skill)
    {
        if (Shown is not { } member || !skill.IsSkill)
            return;
        switch ((Skill)skill.Slot)
        {
            case Skill.Alchemist:
                Crafting = new CraftingMenu(CraftingTask.Alchemy, _text[MenuText.Alchemy], RecipeRows(member, Items.Crafting.Alchemist), Member);
                break;
            case Skill.Mechanic:
                var rows = RecipeRows(member, Items.Crafting.Mechanic);
                if (rows.Count == 0)
                    Say(_text[MenuText.NoArmorComponents]);
                else
                    Crafting = new CraftingMenu(CraftingTask.Armor, _text[MenuText.CreateArmor], rows, Member);
                break;
            case Skill.Healer:
                Crafting = new CraftingMenu(CraftingTask.HealerTasks, _text[MenuText.HealerTasks],
                [
                    new CraftingRow(_text[MenuText.LayOnHands], Task: CraftingTask.LayOnHands),
                    new CraftingRow(_text[MenuText.HealWithHerbs], Task: CraftingTask.HealWithHerbs),
                    new CraftingRow(_text[MenuText.Restore], Task: CraftingTask.Restore),
                ], Member);
                break;
        }
    }

    // WidgetPotionCraft and WidgetArmorCraft list each recipe offered by its product's name.
    private List<CraftingRow> RecipeRows(Character member, RecordId skill) =>
    [
        .. Items.Crafting.Offered(Party, member, Recipes.Where(r => r.Skill == skill))
            .Select(r => new CraftingRow(Party.Inventory.Catalog.TryGet(r.Product)?.Name ?? r.Product.ToString(), Recipe: r)),
    ];

    private void Choose(CraftingMenu crafting)
    {
        if (crafting.Rows.Current is not { } row || Party.Slots.ElementAtOrDefault(crafting.User) is not { } user)
            return;
        var before = Numbers;
        switch (crafting.Task)
        {
            case CraftingTask.Alchemy:
                Brew(user, row.Recipe!, before);
                break;
            case CraftingTask.Armor:
                var made = Items.Crafting.MakeArmor(Party, user, row.Recipe!, Random) == CraftResult.Made;
                Result(_text[made ? MenuText.ArmorMade : MenuText.ArmorFailed], before);
                break;
            case CraftingTask.HealerTasks:
                Crafting = Targets(crafting.User, row.Task!.Value, crafting.Fade);
                break;
            case CraftingTask.LayOnHands:
                Healed(user, row.Member!.Value, Items.Crafting.LayOnHands(user, Party.Slots[row.Member.Value]!), before);
                break;
            case CraftingTask.HealWithHerbs:
                Healed(user, row.Member!.Value, Items.Crafting.HerbHeal(Party, user, Party.Slots[row.Member.Value]!, Random), before);
                break;
            case CraftingTask.Restore:
                Crafting = new CraftingMenu(CraftingTask.RestoreStat, _text[MenuText.Restore],
                [
                    .. RestoreOrder.Select(s => new CraftingRow(user.Rules[s].Name, Stat: s)),
                ], crafting.User, row.Member, crafting.Fade);
                break;
            case CraftingTask.RestoreStat when Party.Slots.ElementAtOrDefault(crafting.Target ?? -1) is { } target:
                var stat = row.Stat!.Value;
                var restored = Items.Crafting.Restore(Party, user, target, stat, Random);
                Result(restored.Outcome switch
                {
                    HealOutcome.Healed => Format(MenuText.RestoredBy, target.Name, row.Text, restored.Amount),
                    HealOutcome.AlreadyWell => Format(MenuText.StatAtMax, target.Name, row.Text),
                    _ => Format(MenuText.HealFailed, user.Name),
                }, before);
                break;
        }
    }

    /// <summary>The attributes the Healer can restore, in the popup's order (<c>WidgetMenuHealer::PrintRestore</c>).</summary>
    public static readonly IReadOnlyList<Stat> RestoreOrder =
        [Stat.Intelligence, Stat.Willpower, Stat.Dexterity, Stat.Endurance, Stat.Strength, Stat.Stamina];

    // WidgetPotionCraft::AFunc: not enough reagents, or a full stack, say so; flasks have their own lines.
    private void Brew(Character user, RecipeRecord recipe, HeaderNumbers before)
    {
        var flask = Party.Inventory.Catalog.TryGet(recipe.Product) is { } product && Potions.IsFlask(product);
        var result = Items.Crafting.Brew(Party, user, recipe, Random);
        Result(_text[result switch
        {
            CraftResult.MissingIngredients => flask ? MenuText.FlaskComponents : MenuText.PotionComponents,
            CraftResult.Made => flask ? MenuText.FlaskMade : MenuText.PotionMade,
            _ => flask ? MenuText.FlaskFailed : MenuText.PotionFailed,
        }], before);
    }

    // WidgetMenuHealer::LayOnHands and HealWithHerbs: the living members, but not the Healer for laying on hands, in
    // the same popup as the tasks, its title and list rebuilt in it (Healer.ShowLayOnHandsList, 0x800038e4, and
    // ShowHerbList and ShowRestoreMembers), so it neither fades nor stops taking presses.
    private CraftingMenu Targets(int user, CraftingTask task, PopupFade fade)
    {
        var rows = new List<CraftingRow>();
        foreach (var (member, slot) in Party.Slots.Select((m, i) => (m, i)))
        {
            if (member is null || member.IsDead || (task == CraftingTask.LayOnHands && slot == user))
                continue;
            rows.Add(new CraftingRow(member.Name, Member: slot, Health: Format(MenuText.Health, member.Hp, member.MaxHp)));
        }
        var title = task switch
        {
            CraftingTask.LayOnHands => MenuText.LayOnHands,
            CraftingTask.HealWithHerbs => MenuText.HealWithHerbs,
            _ => MenuText.Restore,
        };
        return new CraftingMenu(task, _text[title], rows, user, fade: fade);
    }

    // Party::PrintHeal and HerbHeal's messages.
    private void Healed(Character healer, int target, HealResult result, HeaderNumbers before)
    {
        var who = Party.Slots[target]!;
        Result(result.Outcome switch
        {
            HealOutcome.Healed => Format(MenuText.HealsBy, healer.Name, who.Name, result.Amount),
            HealOutcome.AlreadyWell => Format(MenuText.AlreadyAtMaxHealth, who.Name),
            _ => Format(MenuText.HealFailed, healer.Name),
        }, before);
    }

    // WidgetCrafting::TextPopup: the result, and the menu brought up to date under it. Showing it (MenuPopup.ShowResult,
    // 0x80055ed4) crossfades the header's numbers from how they stood before the task (PartyMenu.HealthGoldChange), whoever
    // it healed and whatever it did, for the member whose skill it is, the member shown.
    private void Result(string text, HeaderNumbers before)
    {
        Say(text, fromCrafting: true);
        Refresh();
        ChangeNumbers(before);
    }
}

/// <summary>
/// What the Party tab may do in a battle's pause (<c>PauseMenu.Member</c>, <c>0x800DE9F0</c>): whose turn it is, and what the
/// battle does as a flask is taken in hand or the weapon changes.
/// </summary>
/// <param name="Member">The member whose turn it is, the only one who may change anything; null on an enemy's turn.</param>
/// <param name="HoldFlask">A flask taken in hand (<c>Combatant.SetSpellAction</c>), which ends the turn as the menu closes.</param>
/// <param name="WeaponChanged">
/// The weapon changed (<c>Combatant.AfterEquipChange</c>, <c>0x800610C0</c>), equipped (which ends the turn as the menu closes) or
/// taken off (which doesn't).
/// </param>
public sealed record CombatPause(Character? Member, Action<Item> HoldFlask, Action<bool> WeaponChanged)
{
    /// <summary>The rules of <paramref name="battle"/>'s pause, for its current combatant.</summary>
    public static CombatPause For(Combat.Battle battle) =>
        new(battle.Current is { Controlled: false } c ? c.Sheet : null, battle.HoldFlaskFromMenu, battle.WeaponChangedFromMenu);
}
