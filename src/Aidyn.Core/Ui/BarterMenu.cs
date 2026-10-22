using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>A choice of how many to drop (<c>DollBarterConfirm</c>): none, which cancels, then the quantities.</summary>
public sealed record BarterOption(string Text, int Quantity);

/// <summary>
/// The question that drops some of what the party carries, or sells it (<c>DollBarterConfirm</c>): the
/// question in white over its options in tan, on a feathered panel 160 wide at the screen's centre. A
/// chooses; B does nothing, as the original's has no B.
/// </summary>
/// <param name="Price">What one sells for, or 0 to drop.</param>
public sealed record BarterConfirm(string Text, MenuList<BarterOption> Options, RecordId Item, int Price)
{
    /// <summary>The panel's width, and its left in the 320-wide frame: the question wraps from x 80 to 240.</summary>
    public const int Width = 160;

    /// <summary>The space between the question and the options.</summary>
    public const int Gap = 8;

    public static readonly Rgba TextColor = MenuMessage.White, OptionColor = MenuChoice.Tan;
}

/// <summary>
/// The pause menu's barter state (<c>WidgetDollMenu</c> with <c>WidgetBarter</c>): the Party tab's header
/// and inventory list, with no silhouette, beside a pile on the details' parchment. The pile is loot
/// (<c>Shopkeep</c> −1), a container's items, which the party takes a stack at a time; or a shop's stock,
/// which it buys one at a time, selling what it carries.
/// </summary>
/// <remarks>
/// <para>
/// The stick's left and right make the pile's list or the party's the one the cursor works in, which
/// pulses; the other is plain. The pile's list starts active, and the party's takes over when the pile is
/// empty. C-left and C-right don't change the member here. C-down shows the active row's details.
/// </para>
/// <para>
/// <b>Loot.</b> A on the pile takes the stack, or as many as the party's stack has room for. The first
/// potion the party gains shows help. A on the party's list asks how many to drop, which are lost; an
/// item worth 1 gold or less at half its price can't be dropped, and a worn one must be taken off first.
/// B closes the window, unless items are left: then it asks whether to leave them, or with a must-take
/// item among them, says there's something you really need in here.
/// </para>
/// <para>
/// <b>A shop</b> (checked in 1.0's code, <c>WidgetBarter</c> at <c>0x8003cb68</c>): its stock has no
/// counts, as it never runs out, and under it is the active row's price, what buying or selling one
/// would come to. A on the stock buys one at once, with no question, and the first potion bought shows
/// the help as loot's does. A on the party's list asks how many to sell, at the price; the shopkeeper
/// won't buy what would sell for 1 gold or less, and a worn item must be taken off first. C-up on the
/// stock appraises the price. B closes the window.
/// </para>
/// <para>
/// A popup takes every press while it's open, as the original's widget handler gives them to the top
/// widget. A message closes with A, B, C-down or C-up, and the help with A or B; the question and the
/// leave-items choice only with A, on one of their options.
/// </para>
/// </remarks>
public sealed class BarterMenu : IPartyWindow
{
    /// <summary>The pile's list's place and bounds in the 320 × 240 frame (<c>WidgetBarter</c>).</summary>
    public static readonly ScreenRect ClassicPile = new(20, 88, 157, 166);

    /// <summary>The pile's list: <see cref="ClassicPile"/> with its rows 13 apart, so six show.</summary>
    public static readonly ListLayout PileLayout = new(ClassicPile, 13);

    private readonly FlagNetwork? _flags;

    private BarterMenu(PartyMenu party, LootWindow? loot, Shop? shop, FlagNetwork? flags)
    {
        Party = party;
        Loot = loot;
        Shop = shop;
        _flags = flags;
        Pile = PileLayout.List(PileRows());
        OnPile = Pile.Rows.Count > 0;
    }

    /// <summary>
    /// The loot window on a container's items (<c>build_loot_menu</c>), whose gold the party already has.
    /// </summary>
    /// <param name="flags">The flags the first potion taken sets.</param>
    /// <param name="random">The stream skill checks draw from; a fresh one if none.</param>
    /// <param name="member">The member the header shows, the one the menu last showed (<see cref="PauseMenu.Member"/>).</param>
    public static BarterMenu ForLoot(PluginSet plugins, Party party, ItemPile loot, int gold, FlagNetwork? flags, IRandom? random = null, int member = 0) =>
        new(PartyMenu.From(plugins, party, member, random), LootWindow.Showing(loot, gold, party, party.Inventory.Catalog, flags), null, flags);

    /// <summary>
    /// A shop's window (<c>InitShop</c>, <c>build_loot_menu</c> with a shopkeeper), on the shop as
    /// <see cref="Items.Shop.Open"/> opened it, which set the barter factor.
    /// </summary>
    /// <param name="random">The stream skill checks draw from; a fresh one if none.</param>
    /// <param name="member">The member the header shows, the one the menu last showed (<see cref="PauseMenu.Member"/>).</param>
    public static BarterMenu ForShop(PluginSet plugins, Shop shop, IRandom? random = null, int member = 0) =>
        new(PartyMenu.From(plugins, shop.Party, member, random), null, shop, shop.Flags);

    /// <summary>The header and the party's inventory list.</summary>
    public PartyMenu Party { get; }

    PartyMenu IPartyWindow.Menu => Party;

    /// <summary>C-left and C-right don't change the member here.</summary>
    bool IPartyWindow.ChangesMember => false;

    void IPartyWindow.ChangeMember(int by)
    {
    }

    /// <summary>The stick's left reaches the pile, and its right the party's list, as they're laid out.</summary>
    void IPartyWindow.Sideways(int direction)
    {
        if (direction < 0)
            ShowPile();
        else
            ShowParty();
    }

    /// <summary>C-up always takes the press here, if it does nothing on the party's list.</summary>
    bool IPartyWindow.Appraise()
    {
        Appraise();
        return true;
    }

    /// <summary>The loot's rules, or null in a shop.</summary>
    public LootWindow? Loot { get; }

    /// <summary>The shop's rules, or null for loot.</summary>
    public Shop? Shop { get; }

    /// <summary>The pile's rows.</summary>
    public MenuList<InventoryRow> Pile { get; }

    /// <summary>Whether the cursor works in the pile's list, rather than the party's.</summary>
    public bool OnPile { get; private set; }

    /// <summary>The pile's title: Treasure, or a shop's Buy.</summary>
    public RecordId Title => Shop is null ? MenuImages.TreasureTitle : MenuImages.BuyTitle;

    /// <summary>The party's list's title: Inventory, or beside a shop, Sell (<c>WidgetInvShop</c>).</summary>
    public RecordId PartyTitle => Shop is null ? MenuImages.InventoryTitle : MenuImages.SellTitle;

    /// <summary>
    /// The line under the pile (<c>UpdateGoldText</c>, <c>0x8003cfd8</c>): the gold the loot held, or in a
    /// shop the active row's price, what buying one from the stock costs or what selling one of the
    /// party's brings, worn or not. With no row it's blank.
    /// </summary>
    public string PileLine
    {
        get
        {
            if (Shop is not { } shop)
                return Party.Text.Format(MenuText.FoundGold, Loot!.Gold);
            if ((OnPile ? Pile : Party.Inventory).Current is not { } row)
                return " ";
            return Party.Text.Format(MenuText.Value, OnPile ? shop.BuyPrice(row.Instance) : shop.SellPrice(row.Instance));
        }
    }

    /// <summary>The popups over the window: the message, the help and the leave-items choice, and C-down's details.</summary>
    public MenuOverlay Overlay { get; } = new();

    /// <summary>A message over everything, which A, B, C-down or C-up closes.</summary>
    public MenuMessage? Message => Overlay.Message;

    /// <summary>The help on the first potion, on the parchment, which A or B closes (<c>WidgetMenuPrompt</c>).</summary>
    public string? Help => Overlay.Help;

    /// <summary>The question that drops or sells some of what the party carries.</summary>
    public BarterConfirm? Confirm { get; private set; }

    /// <summary>Whether to leave the items that are left.</summary>
    public MenuChoice? Choice => Overlay.Choice;

    /// <summary>Whether a popup or the details are taking the presses.</summary>
    public bool HasPopup => Overlay.HasPopup || Confirm is not null;

    /// <summary>C-down's details of the active row, which fade as the Party tab's do (<see cref="PopupFade"/>).</summary>
    public DetailsPane DetailsPane => Overlay.Details;

    /// <summary>C-down pressed: shows the details, unless a popup is up or they show already.</summary>
    public void PressDetails() => Overlay.PressDetails(!HasPopup && Details() is not null);

    /// <summary>C-down let go: the details fade out once they're open.</summary>
    public void ReleaseDetails() => Overlay.ReleaseDetails();

    /// <summary>Whether the window closed itself: the party chose to leave the loot's items.</summary>
    public bool Closed { get; private set; }

    /// <summary>The colour the popups' cursors pulse to now.</summary>
    public Rgba PopupHighlight => Overlay.Highlight;

    /// <summary>Makes the pile's list the active one (<c>WidgetBarter::LeftFunc</c>), unless it's empty.</summary>
    public void ShowPile()
    {
        if (!HasPopup && Pile.Rows.Count > 0)
            OnPile = true;
    }

    /// <summary>Makes the party's list the active one (<c>RightFunc</c>; the decomp has it the other way, the machine code this way).</summary>
    public void ShowParty()
    {
        if (!HasPopup)
            OnPile = false;
    }

    /// <summary>Moves the cursor of the popup's options or the active list.</summary>
    public void MoveCursor(int by)
    {
        if (Confirm is { } confirm)
            confirm.Options.Move(by);
        else if (!Overlay.MoveCursor(by) && !HasPopup)
            (OnPile ? Pile : Party.Inventory).Move(by);
    }

    /// <summary>Makes a list active and puts its cursor on a row, as hovering does.</summary>
    public void Point(bool pile, int row)
    {
        if (HasPopup || pile && Pile.Rows.Count == 0)
            return;
        OnPile = pile;
        (pile ? Pile : Party.Inventory).Point(row);
    }

    /// <summary>What C-down shows for the active list's row.</summary>
    public MenuDetails? Details() => (OnPile ? Pile : Party.Inventory).Current is { } row ? Party.ItemDetails(row.Instance) : null;

    /// <summary>Steps the cursors' pulses and the details' fade by a tick.</summary>
    public void Step()
    {
        Party.Step();
        Overlay.Step();
    }

    /// <summary>A: closes a message or the help, chooses in a popup, or takes, buys, drops or sells the active row.</summary>
    public void Accept()
    {
        if (Overlay.Dismiss())
            return;
        if (Confirm is { } confirm)
            Trade(confirm);
        else if (Choice is { } choice)
            Leave(choice);
        else if (DetailsPane.Showing)
            return;
        else if (OnPile)
            Take();
        else
            Ask();
    }

    /// <summary>
    /// B, or Start: closes a message or the help, or asks before leaving loot behind. False if nothing
    /// holds the window open, so it closes (<c>WidgetBarter::BFunc</c>): a shop closes at once, as the
    /// machine code has it, where the decomp's would do nothing.
    /// </summary>
    public bool Back()
    {
        if (Overlay.Dismiss())
            return true;
        if (Confirm is not null || Choice is not null || DetailsPane.Showing)
            return true;
        else if (Loot is null || Loot.Loot.Stacks.Count == 0)
            return false;
        else if (Loot.CanLeave)
            Overlay.Ask(MenuChoice.Question(Party.Text[MenuText.LeaveItems], [Party.Text[MenuText.LeaveItemsYes], Party.Text[MenuText.LeaveItemsNo]]));
        else
            Overlay.Say(new MenuMessage(Party.Text[MenuText.MustTake], Box: (155, 40)));
        return true;
    }

    /// <summary>C-down or C-up: closes a message. True if it did, so C-down doesn't show the details too.</summary>
    public bool CloseMessage() => Overlay.CloseMessage();

    /// <summary>
    /// C-up (<c>0x8003de10</c>): closes a message, or in a shop, on the stock's row, says how the party
    /// judges its price (<see cref="Items.Shop.Appraise"/>), from "This is highway robbery!" up. It does
    /// nothing on the party's list, or in loot.
    /// </summary>
    public void Appraise()
    {
        if (CloseMessage() || HasPopup || Shop is not { } shop || !OnPile || Pile.Current is not { } row)
            return;
        Overlay.Say(Party.Text[MenuText.Appraisal + shop.Appraise(row.Instance)]);
    }

    private void Say(int line) => Overlay.Say(Party.Text[line]);

    // WidgetBarter::AFunc on the pile: loot gives the stack, a shop sells one.
    private void Take()
    {
        if (Pile.Current is not { } row)
        {
            Say(MenuText.NothingThere);
            return;
        }
        var item = row.Item;
        var firstPotion = item.Kind == ItemKind.Potion && _flags is not null && !_flags.IsSet(Party.Party.Inventory.Catalog.Rules.FirstPotionFlag);
        switch (Shop is { } shop ? shop.Buy(item.Id) : Loot!.Take(item.Id))
        {
            case TradeResult.CantAfford:
                Say(MenuText.CantAfford);
                return;
            case TradeResult.StackFull:
                Say(MenuText.StackFull);
                return;
            case TradeResult.InventoryFull:
                Say(MenuText.InventoryFull);
                return;
        }
        // The party's cursor goes to what it took; the pile's stays on its row, which the next moves up into.
        Party.Refresh();
        if (Party.Inventory.Rows.Select((r, i) => (r, i)).FirstOrDefault(e => e.r.Wearer is null && e.r.Item.Id == item.Id) is { r: not null } taken)
            Party.Inventory.Point(taken.i);
        Pile.Replace(PileRows(), resetPastEnd: false);
        if (firstPotion)
            Overlay.ShowHelp(Party.Text[MenuText.FirstPotion]);
        if (Pile.Rows.Count == 0)
            OnPile = false;
    }

    // WidgetBarter::AFunc on the party's list: the price first, then whether it's worn.
    private void Ask()
    {
        if (Party.Inventory.Current is not { } row)
        {
            Say(MenuText.NothingThere);
            return;
        }
        var shop = Shop;
        var selling = shop is not null;
        var price = shop?.SellPrice(row.Instance) ?? 0;
        if (selling ? price <= 1 : !Loot!.Droppable(row.Instance))
        {
            Say(selling ? MenuText.MerchantRefuses : MenuText.CantDrop);
            return;
        }
        if (row.Wearer is not null)
        {
            Say(selling ? MenuText.UnequipToSell : MenuText.UnequipToDrop);
            return;
        }
        var held = Party.Party.Inventory.Count(row.Item.Id);
        var text = Party.Text;
        List<BarterOption> options = [new(text[selling ? MenuText.SellNo : MenuText.DropNo], 0)];
        if (held == 1)
        {
            options.Add(new BarterOption(text[selling ? MenuText.SellYes : MenuText.DropYes], 1));
        }
        else
        {
            options.AddRange(Items.Shop.Quantities(held).Select(q => new BarterOption(q == held
                ? text[selling ? MenuText.SellAll : MenuText.DropAll]
                : text.Format(selling ? MenuText.SellSome : MenuText.DropSome, q), q)));
        }
        var question = selling ? text.Format(MenuText.SellConfirm, price, row.Item.Name) : text.Format(MenuText.DropConfirm, row.Item.Name);
        Confirm = new BarterConfirm(question, new MenuList<BarterOption>(options), row.Item.Id, price);
        Overlay.ResetPulse();
    }

    // DollBarterConfirm::AFunc: the cursor stays on the row, which what's left or the next moves up into.
    private void Trade(BarterConfirm confirm)
    {
        Confirm = null;
        if (confirm.Options.Current is not { Quantity: > 0 } option)
            return;
        if (Shop is { } shop)
            shop.Sell(confirm.Item, option.Quantity);
        else
            Loot!.Drop(confirm.Item, option.Quantity);
        Party.Refresh();
    }

    // A shop's stock has no counts (WidgetInvShop::InitMenu builds its rows with none), as it never runs out.
    private IReadOnlyList<InventoryRow> PileRows() => Shop is { } shop
        ? [.. PartyMenuRows.Pile(shop.Stock).Select(r => r with { Quantity = 0 })]
        : PartyMenuRows.Pile(Loot!.Loot);

    // The leave-items choice: "Yes, leave them." closes the window (setGlobalsPointer); "No" only the choice.
    private void Leave(MenuChoice choice)
    {
        Overlay.CloseChoice();
        if (choice.Choices.Cursor == 0)
            Closed = true;
    }
}
