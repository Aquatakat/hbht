using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// UI chrome the menus draw with: images the engine's code names, which need no records and which
/// mods replace as assets under the same IDs (see <c>docs/hardcoded-content.md</c>).
/// </summary>
public static class MenuImages
{
    /// <summary>The Party tab's parchment, with the silhouette, frames and labels painted in.</summary>
    public static readonly RecordId PartyTab = Image(0x00D9);

    /// <summary>The parchment C-down's details, barter and training draw on (<c>BORG8_BarterBG</c>), and its shadow.</summary>
    public static readonly RecordId Parchment = Image(0x00E2), ParchmentShadow = Image(0x00E4);

    /// <summary>The hit points bar, cut to the share of hit points left (<c>BORG8_HealthBar</c>).</summary>
    public static readonly RecordId HealthBar = Image(0x00E0);

    /// <summary>The lists' titles (<c>BORG8_TitleInventory</c> and so on).</summary>
    public static readonly RecordId InventoryTitle = Image(0x00E9), SkillsTitle = Image(0x00EC), SpellsTitle = Image(0x00ED),
        StatsTitle = Image(0x00EE);

    /// <summary>The barter window's titles: the loot's, a shop's stock and the party's list beside a shop (<c>BORG8_TitleTreasure</c>, <c>Buy</c>, <c>Sell</c>).</summary>
    public static readonly RecordId TreasureTitle = Image(0x00F0), BuyTitle = Image(0x00E7), SellTitle = Image(0x00EB);

    /// <summary>The arrows beside a list's title that show it has rows above or below (<c>BORG8_ScrollBarArrowUp</c>, <c>Down</c>).</summary>
    public static readonly RecordId ScrollUp = Image(0x38D0), ScrollDown = Image(0x38CF);

    /// <summary>The arrow under the knotwork: both ways in trek, left only in combat (<c>BORG8_SliderArrowLR</c>, <c>L</c>).</summary>
    public static readonly RecordId SliderBoth = Image(0x3756), SliderLeft = Image(0x3755);

    /// <summary>
    /// Each equipment slot's image of what it takes, by <see cref="EquipmentSlot"/>, which shows while
    /// the slot is empty (<c>gItemSlotBorg8Indecies</c>).
    /// </summary>
    public static readonly IReadOnlyList<RecordId> EmptySlots =
        [.. new[] { 0x3601, 0x35F8, 0x35FA, 0x35F9, 0x35F3, 0x35F7, 0x35F4, 0x35F5, 0x35F6, 0x35FF, 0x3600, 0x35FB, 0x35FC, 0x35FD, 0x35FE }
            .Select(Image)];

    /// <summary>Lunar's and Solar's icons (<c>getAspectIcon</c>). An item or spell of no aspect has none.</summary>
    public static RecordId? AspectIcon(Aspect aspect) => aspect switch
    {
        Aspect.Lunar => Image(0x01A3),
        Aspect.Solar => Image(0x01A4),
        _ => null,
    };

    /// <summary>Each school's icon (<c>getSchoolIcon</c>). Unschooled has none.</summary>
    public static RecordId? SchoolIcon(MagicSchool school) => school switch
    {
        MagicSchool.Chaos => Image(0x01A9),
        MagicSchool.Elemental => Image(0x01A5),
        MagicSchool.Naming => Image(0x01A6),
        MagicSchool.Necromancy => Image(0x01A7),
        MagicSchool.Star => Image(0x01A8),
        _ => null,
    };

    private static RecordId Image(int borg8) => RecordId.Hex(RecordTypes.Image, borg8);
}

/// <summary>
/// The silhouette's 15 slots, in the order the original numbers them (<c>DollEquipmentMenu</c>). The
/// decomp reads cloaks for the helmet's slot; the machine code reads helmets.
/// </summary>
public enum EquipmentSlot
{
    Weapon,
    Gloves,
    Ring1,
    Helmet,
    Amulet,
    Cloak,
    Armor,
    Belt,
    Boots,
    Shield,
    Wand,
    Ring2,
    Scroll1,
    Scroll2,
    Scroll3,
}
