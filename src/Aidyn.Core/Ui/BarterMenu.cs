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
/// and inventory list, with no silhouette, beside a pile on the details' parchment. So far it's loot
/// (<c>Shopkeep</c> −1): a container's items, which the party takes a stack at a time, and drops what it
/// carries. A shop is the same window with prices (session 7).
/// </summary>
/// <remarks>
/// <para>
/// The stick's left and right make the pile's list or the party's the one the cursor works in, which
/// pulses; the other is plain. The pile's list starts active, and the party's takes over when the pile is
/// empty. C-left and C-right don't change the member here. C-down shows the active row's details.
/// </para>
/// <para>
/// <b>A on the pile</b> takes the stack, or as many as the party's stack has room for. The first potion
/// the party gains shows help. <b>A on the party's list</b> asks how many to drop, which are lost; an
/// item worth 1 gold or less at half its price can't be dropped, and a worn one must be taken off first.
/// <b>B</b> closes the window, unless items are left: then it asks whether to leave them, or with a
/// must-take item among them, says there's something you really need in here.
/// </para>
/// <para>
/// A popup takes every press while it's open, as the original's widget handler gives them to the top
/// widget. A message closes with A, B, C-down or C-up, and the help with A or B; the drop's question and
/// the leave-items choice only with A, on one of their options.
/// </para>
/// </remarks>
public sealed class BarterMenu
{
    /// <summary>The pile's list's place and bounds in the 320 × 240 frame (<c>WidgetBarter</c>).</summary>
    public static readonly ScreenRect ClassicPile = new(20, 88, 157, 166);

    private readonly FlagNetwork? _flags;
    private readonly MenuPulse _popupPulse = new();

    private BarterMenu(PartyMenu party, LootWindow loot, FlagNetwork? flags)
    {
        Party = party;
        Loot = loot;
        _flags = flags;
        Pile = new MenuList<InventoryRow>(PartyMenuRows.Pile(loot.Loot));
        OnPile = Pile.Rows.Count > 0;
    }

    /// <summary>
    /// The loot window on a container's items (<c>build_loot_menu</c>), whose gold the party already has.
    /// </summary>
    /// <param name="flags">The flags the first potion taken sets.</param>
    /// <param name="random">The stream skill checks draw from; a fresh one if none.</param>
    public static BarterMenu ForLoot(PluginSet plugins, Party party, ItemPile loot, int gold, FlagNetwork? flags, IRandom? random = null) =>
        new(PartyMenu.From(plugins, party, random: random), LootWindow.Showing(loot, gold, party, party.Inventory!.Catalog, flags), flags);

    /// <summary>The header and the party's inventory list.</summary>
    public PartyMenu Party { get; }

    /// <summary>The loot's rules.</summary>
    public LootWindow Loot { get; }

    /// <summary>The pile's rows.</summary>
    public MenuList<InventoryRow> Pile { get; }

    /// <summary>Whether the cursor works in the pile's list, rather than the party's.</summary>
    public bool OnPile { get; private set; }

    /// <summary>The pile's title.</summary>
    public RecordId Title => MenuImages.TreasureTitle;

    /// <summary>The line under the pile: the gold the loot held (<c>UpdateGoldText</c>).</summary>
    public string GoldLine => Party.Text.Format(MenuText.FoundGold, Loot.Gold);

    /// <summary>A message over everything, which A, B, C-down or C-up closes.</summary>
    public MenuMessage? Message { get; private set; }

    /// <summary>The help on the first potion, on the parchment, which A or B closes (<c>WidgetMenuPrompt</c>).</summary>
    public string? Help { get; private set; }

    /// <summary>The question that drops some of what the party carries.</summary>
    public BarterConfirm? Confirm { get; private set; }

    /// <summary>Whether to leave the items that are left.</summary>
    public MenuChoice? Choice { get; private set; }

    public bool HasPopup => Message is not null || Help is not null || Confirm is not null || Choice is not null;

    /// <summary>Whether the window closed itself: the party chose to leave the items.</summary>
    public bool Closed { get; private set; }

    /// <summary>The colour the popups' cursors pulse to now.</summary>
    public Rgba PopupHighlight => _popupPulse.Now(MenuPulse.ScrollFrom, MenuPulse.ScrollTo);

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
        else if (Choice is { } choice)
            choice.Choices.Move(by);
        else if (!HasPopup)
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

    /// <summary>Steps the cursors' pulses by a tick.</summary>
    public void Step()
    {
        Party.Step();
        _popupPulse.Step();
    }

    /// <summary>A: closes a message or the help, chooses in a popup, or takes or drops the active row.</summary>
    public void Accept()
    {
        if (Message is not null)
            Message = null;
        else if (Help is not null)
            Help = null;
        else if (Confirm is { } confirm)
            Drop(confirm);
        else if (Choice is { } choice)
            Leave(choice);
        else if (OnPile)
            Take();
        else
            AskDrop();
    }

    /// <summary>
    /// B, or Start: closes a message or the help, or asks before leaving items behind. False if nothing
    /// holds the window open, so it closes (<c>WidgetBarter::BFunc</c>).
    /// </summary>
    public bool Back()
    {
        if (Message is not null)
            Message = null;
        else if (Help is not null)
            Help = null;
        else if (Confirm is not null || Choice is not null)
            return true;
        else if (Loot.Loot.Stacks.Count == 0)
            return false;
        else if (Loot.CanLeave)
            Choose(new MenuChoice(Party.Text[MenuText.LeaveItems], MenuMessage.MaxWidth, 0, MenuMessage.White, MenuChoice.Tan,
                new MenuList<string>([Party.Text[MenuText.LeaveItemsYes], Party.Text[MenuText.LeaveItemsNo]])));
        else
            Message = new MenuMessage(Party.Text[MenuText.MustTake], Box: (155, 40));
        return true;
    }

    /// <summary>C-down or C-up: closes a message. True if it did, so C-down doesn't show the details too.</summary>
    public bool CloseMessage()
    {
        if (Message is null)
            return false;
        Message = null;
        return true;
    }

    private void Say(int line) => Message = new MenuMessage(Party.Text[line]);

    private void Choose(MenuChoice choice)
    {
        Choice = choice;
        _popupPulse.Reset();
    }

    // WidgetBarter::AFunc on the pile.
    private void Take()
    {
        if (Pile.Current is not { } row)
        {
            Say(MenuText.NothingThere);
            return;
        }
        var item = row.Item;
        var firstPotion = item.Kind == ItemKind.Potion && _flags is not null && !_flags.Get(Party.Party.Inventory!.Catalog.Rules.FirstPotionFlag);
        switch (Loot.Take(item.Id))
        {
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
        Pile.Replace(PartyMenuRows.Pile(Loot.Loot), resetPastEnd: false);
        if (firstPotion)
            Help = Party.Text[MenuText.FirstPotion];
        if (Pile.Rows.Count == 0)
            OnPile = false;
    }

    // WidgetBarter::AFunc on the party's list: the price first, then whether it's worn.
    private void AskDrop()
    {
        if (Party.Inventory.Current is not { } row)
        {
            Say(MenuText.NothingThere);
            return;
        }
        if (!Loot.Droppable(row.Instance))
        {
            Say(MenuText.CantDrop);
            return;
        }
        if (row.Wearer is not null)
        {
            Say(MenuText.UnequipToDrop);
            return;
        }
        var held = Party.Party.Inventory!.Count(row.Item.Id);
        var text = Party.Text;
        List<BarterOption> options = [new(text[MenuText.DropNo], 0)];
        if (held == 1)
            options.Add(new BarterOption(text[MenuText.DropYes], 1));
        else
            options.AddRange(Shop.Quantities(held).Select(q => new BarterOption(q == held ? text[MenuText.DropAll] : text.Format(MenuText.DropSome, q), q)));
        Confirm = new BarterConfirm(text.Format(MenuText.DropConfirm, row.Item.Name), new MenuList<BarterOption>(options), row.Item.Id, 0);
        _popupPulse.Reset();
    }

    // DollBarterConfirm::AFunc: the cursor stays on the row, which what's left or the next moves up into.
    private void Drop(BarterConfirm confirm)
    {
        Confirm = null;
        if (confirm.Options.Current is not { Quantity: > 0 } option)
            return;
        Loot.Drop(confirm.Item, option.Quantity);
        Party.Refresh();
    }

    // The leave-items choice: "Yes, leave them." closes the window (setGlobalsPointer); "No" only the choice.
    private void Leave(MenuChoice choice)
    {
        Choice = null;
        if (choice.Choices.Cursor == 0)
            Closed = true;
    }
}
