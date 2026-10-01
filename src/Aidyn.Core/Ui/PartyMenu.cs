using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>The Party tab's lists, in the order the stick steps through them (<c>DollMenuLists::InitMenu</c>).</summary>
public enum PartyList
{
    Inventory,
    Spells,
    Skills,
    Stats,
}

/// <summary>
/// The pause menu's Party tab (<c>WidgetDollMenu</c>): the member shown, with their portrait, hit
/// points, level and the party's gold, what they wear on the silhouette, and one of four lists with
/// a cursor, whose row C-down describes. Layouts place it; the model is the same for each, and for
/// the menu's reuses in barter and training.
/// </summary>
/// <remarks>
/// C-left and C-right change the member (<c>CHorizontal</c>), and the stick changes the list
/// (<c>DollMenuLists::LRToggle</c>). Changing either keeps each list's cursor, as the original
/// rebuilds the lists in place. The original animates both, and the details' parchment fading in;
/// those aren't ported yet.
/// </remarks>
public sealed class PartyMenu
{
    /// <summary>The colour of a row, and of the header's numbers (<c>COLOR_RED1</c>).</summary>
    public static readonly Rgba TextColor = MenuDetailsBuilder.TextColor;

    /// <summary>What the cursor's row pulses toward (<c>COLOR_PURPLE1</c>), from the row's colour, over <see cref="PulseFrames"/>.</summary>
    public static readonly Rgba HighlightColor = new(151, 141, 191, 255);

    /// <summary>The colour the cursor's row pulses from (<c>COLOR_BROWN2</c>).</summary>
    public static readonly Rgba HighlightFrom = new(68, 42, 34, 255);

    public const int PulseFrames = 20;

    private readonly MenuText _text;
    private readonly Func<RecordId, SpellRecord?> _spells;
    private readonly Func<RecordId, RecordId?> _portraits;
    private int _ticks, _pulse, _pulseSign = 1;

    /// <param name="portraits">Each entity's portrait.</param>
    public PartyMenu(Party party, MenuText text, Func<RecordId, SpellRecord?> spells, Func<RecordId, RecordId?> portraits, int member = 0)
    {
        Party = party;
        _text = text;
        _spells = spells;
        _portraits = portraits;
        Member = Math.Clamp(member, 0, Party.MaxSize - 1);
        // The original steps back to a slot with a member (DollMenu::InitMenu).
        while (Member > 0 && party.Slots.ElementAtOrDefault(Member) is null)
            Member--;
        Inventory = new MenuList<InventoryRow>([]);
        Stats = new MenuList<StatRow>([]);
        Skills = new MenuList<SkillRow>([]);
        Spells = new MenuList<SpellRow>([]);
        Refresh();
    }

    public static PartyMenu From(PluginSet plugins, Party party, int member = 0) => new(party, MenuText.From(plugins),
        id => plugins.TryGet<SpellRecord>(id), id => plugins.TryGet<EntityRecord>(id)?.Portrait, member);

    public Party Party { get; }

    /// <summary>The party slot of the member shown (<c>gPartyPicker</c>).</summary>
    public int Member { get; private set; }

    public Character? Shown => Party.Slots.ElementAtOrDefault(Member);

    public MenuText Text => _text;

    public PartyList List { get; private set; } = PartyList.Inventory;

    public MenuList<InventoryRow> Inventory { get; }

    public MenuList<StatRow> Stats { get; }

    public MenuList<SkillRow> Skills { get; }

    public MenuList<SpellRow> Spells { get; }

    /// <summary>The lines under the stats list.</summary>
    public ExperienceLines? Experience { get; private set; }

    /// <summary>The party's slots with a member, from the one shown round, as the header's portraits go.</summary>
    public IReadOnlyList<int> PortraitOrder =>
    [
        .. Enumerable.Range(0, Party.MaxSize).Select(i => (Member + i) % Party.MaxSize).Where(i => Party.Slots.ElementAtOrDefault(i) is not null),
    ];

    /// <summary>An entity's portrait.</summary>
    public RecordId? Portrait(RecordId entity) => _portraits(entity);

    /// <summary>The share of the bar the member's hit points fill, of <paramref name="width"/> (<c>WidgetHealthGold</c>).</summary>
    public int HealthBar(int width) => Shown is { MaxHp: > 0 } c ? (int)((float)c.Hp / c.MaxHp * width) : 0;

    /// <summary>What a slot of the silhouette shows: the worn item's icon, or the slot's own image of what it takes.</summary>
    public RecordId SlotImage(EquipmentSlot slot) => Worn(slot)?.Item.Icon ?? MenuImages.EmptySlots[(int)slot];

    /// <summary>
    /// What the member shown wears in a slot (<c>DollEquipmentMenu::GetEntityItemId</c>): a gear slot
    /// takes the first, second or third piece of its kind in the gear's order.
    /// </summary>
    public ItemInstance? Worn(EquipmentSlot slot)
    {
        if (Shown?.Equipment is not { } equipment)
            return null;
        return slot switch
        {
            EquipmentSlot.Weapon => equipment.Weapon,
            EquipmentSlot.Armor => equipment.Armor,
            EquipmentSlot.Shield => equipment.Shield,
            EquipmentSlot.Gloves => Nth(ItemKind.Gloves, 0),
            EquipmentSlot.Ring1 => Nth(ItemKind.Ring, 0),
            EquipmentSlot.Ring2 => Nth(ItemKind.Ring, 1),
            EquipmentSlot.Helmet => Nth(ItemKind.Helmet, 0),
            EquipmentSlot.Amulet => Nth(ItemKind.Amulet, 0),
            EquipmentSlot.Cloak => Nth(ItemKind.Cloak, 0),
            EquipmentSlot.Belt => Nth(ItemKind.Belt, 0),
            EquipmentSlot.Boots => Nth(ItemKind.Boots, 0),
            EquipmentSlot.Wand => Nth(ItemKind.Wand, 0),
            EquipmentSlot.Scroll1 => Nth(ItemKind.Scroll, 0),
            EquipmentSlot.Scroll2 => Nth(ItemKind.Scroll, 1),
            EquipmentSlot.Scroll3 => Nth(ItemKind.Scroll, 2),
            _ => null,
        };

        ItemInstance? Nth(ItemKind kind, int n) => equipment.Gear.OfType<ItemInstance>().Where(i => i.Item.Kind == kind).ElementAtOrDefault(n);
    }

    /// <summary>The list showing's cursor row, if it has any rows.</summary>
    public int Cursor => List switch
    {
        PartyList.Inventory => Inventory.Cursor,
        PartyList.Stats => Stats.Cursor,
        PartyList.Skills => Skills.Cursor,
        _ => Spells.Cursor,
    };

    /// <summary>Shows the next member, or with a negative step the one before (C-right and C-left).</summary>
    public void ChangeMember(int by)
    {
        var order = Enumerable.Range(0, Party.MaxSize).Where(i => Party.Slots.ElementAtOrDefault(i) is not null).ToList();
        if (order.Count < 2)
            return;
        var at = order.IndexOf(Member);
        Member = order[((at + by) % order.Count + order.Count) % order.Count];
        Refresh();
    }

    /// <summary>Shows the next list, or with a negative step the one before (the stick).</summary>
    public void ChangeList(int by)
    {
        var count = Enum.GetValues<PartyList>().Length;
        List = (PartyList)((((int)List + by) % count + count) % count);
    }

    /// <summary>Shows a list.</summary>
    public void ShowList(PartyList list) => List = list;

    /// <summary>Moves the cursor of the list showing.</summary>
    public void MoveCursor(int by)
    {
        switch (List)
        {
            case PartyList.Inventory: Inventory.Move(by); break;
            case PartyList.Stats: Stats.Move(by); break;
            case PartyList.Skills: Skills.Move(by); break;
            default: Spells.Move(by); break;
        }
    }

    /// <summary>Puts the cursor of the list showing on a row.</summary>
    public void Point(int row)
    {
        switch (List)
        {
            case PartyList.Inventory: Inventory.Point(row); break;
            case PartyList.Stats: Stats.Point(row); break;
            case PartyList.Skills: Skills.Point(row); break;
            default: Spells.Point(row); break;
        }
    }

    /// <summary>What C-down shows for the cursor's row, or null if it shows nothing.</summary>
    public MenuDetails? Details()
    {
        if (Shown is not { } shown)
            return null;
        return List switch
        {
            PartyList.Inventory when Inventory.Current is { } row =>
                MenuDetailsBuilder.Item(row.Instance, shown, _text, _spells, Party.Inventory?.Catalog),
            PartyList.Stats when Stats.Current is { } row => MenuDetailsBuilder.Stat(shown.Rules[row.Stat]),
            PartyList.Skills when Skills.Current is { } row => MenuDetailsBuilder.Skill(shown.Rules[row.Skill]),
            PartyList.Spells when Spells.Current is { } row => MenuDetailsBuilder.Spell(row.Spell.Spell, _text, Party.Inventory?.Catalog),
            _ => null,
        };
    }

    /// <summary>Rebuilds the rows from the party, as a member change or anything that changes the party does.</summary>
    public void Refresh()
    {
        Inventory.Replace(PartyMenuRows.Inventory(Party, Member), resetPastEnd: false);
        if (Shown is { } shown)
        {
            Stats.Replace(PartyMenuRows.Stats(shown), resetPastEnd: false);
            Skills.Replace(PartyMenuRows.Skills(shown), resetPastEnd: true);
            Spells.Replace(PartyMenuRows.Spells(shown), resetPastEnd: true);
            Experience = PartyMenuRows.Experience(shown, _text);
        }
    }

    /// <summary>Steps the cursor's pulse by a tick.</summary>
    public void Step()
    {
        if (++_ticks < Cadence.FrameTicks)
            return;
        _ticks = 0;
        _pulse += _pulseSign;
        if (_pulse == 0 || _pulse >= PulseFrames - 1)
            _pulseSign = -_pulseSign;
    }

    /// <summary>
    /// The cursor's row's colour now: from <see cref="HighlightFrom"/> toward <see cref="HighlightColor"/>
    /// by whole steps, a 20th of the way rounded toward zero per frame (<c>WidgetFastScrollMenu::Tick</c>).
    /// </summary>
    public Rgba HighlightNow => new(Blend(HighlightFrom.R, HighlightColor.R), Blend(HighlightFrom.G, HighlightColor.G),
        Blend(HighlightFrom.B, HighlightColor.B), Blend(HighlightFrom.A, HighlightColor.A));

    private byte Blend(byte from, byte to) => (byte)(from + (to - from) / PulseFrames * _pulse);
}
