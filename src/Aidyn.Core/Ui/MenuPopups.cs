using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// A message over the menu, which A, B or C-down closes: why something can't be used, or how a task went
/// (<c>TextBox_Centered</c>, <c>WidgetCrafting::TextPopup</c>).
/// </summary>
/// <param name="FromCrafting">
/// A crafting popup's result, whose box has 16 more pixels across and 5 more down, and whose closing
/// closes the crafting popup too (<c>WidgetCrafting_ClosePopup</c>).
/// </param>
/// <param name="Box">
/// A box of a size of its own, rather than fitted to the text, with the text in white (<c>TextPopup_New</c>).
/// </param>
/// <param name="Wrap">The widest the text wraps at, which the box is fitted to.</param>
public sealed record MenuMessage(string Text, bool FromCrafting = false, (int Width, int Height)? Box = null, int Wrap = MenuMessage.MaxWidth)
{
    /// <summary>The widest a message wraps at.</summary>
    public const int MaxWidth = 150;

    /// <summary>The message's off-white (<c>COLOR_OFFWHITE</c>).</summary>
    public static readonly Rgba Color = new(225, 225, 225, 255);

    /// <summary>A sized box's text (<c>COLOR_WHITE</c>).</summary>
    public static readonly Rgba White = new(255, 255, 255, 255);
}

/// <summary>
/// A question over choices, centred on the screen on a feathered panel (<c>WidgetChoiceDia</c>): the stick
/// moves the cursor, which pulses between a scroll menu's own colours, and A chooses.
/// </summary>
/// <param name="Width">The widest the question wraps at, and the panel's width.</param>
/// <param name="Gap">The space between the question and its choices.</param>
public sealed record MenuChoice(string Text, int Width, int Gap, Rgba Color, Rgba ChoiceColor, MenuList<string> Choices)
{
    /// <summary>The leave-items choice's question, in white, and its choices, in tan (<c>WidgetBarter::BFunc</c>).</summary>
    public static readonly Rgba Tan = new(200, 180, 100, 255);

    /// <summary>
    /// The shop's and the training window's question, 150 wide with no gap, in white over its answers in tan
    /// (<c>WidgetBarter::BFunc</c>): the leave-items question, or whether to train.
    /// </summary>
    public static MenuChoice Question(string text, IReadOnlyList<string> answers) =>
        new(text, MenuMessage.MaxWidth, 0, MenuMessage.White, Tan, new MenuList<string>(answers));
}

/// <summary>What a crafting popup is offering.</summary>
public enum CraftingTask
{
    /// <summary>The Alchemist's recipes (<c>WidgetPotionCraft</c>).</summary>
    Alchemy,
    /// <summary>The Mechanic's recipes (<c>WidgetArmorCraft</c>).</summary>
    Armor,
    /// <summary>The Healer's three tasks (<c>WidgetMenuHealer</c>).</summary>
    HealerTasks,
    /// <summary>Whom to lay hands on: the living members but the Healer.</summary>
    LayOnHands,
    /// <summary>Whom to heal with herbs: the living members.</summary>
    HealWithHerbs,
    /// <summary>Whom to restore: the living members.</summary>
    Restore,
    /// <summary>Which of the target's attributes to restore.</summary>
    RestoreStat,
}

/// <summary>A row of a crafting popup: a recipe, a Healer's task, a member or an attribute.</summary>
/// <param name="Member">A member's party slot, whose portrait, name and hit points the row shows.</param>
/// <param name="Health">The member's hit points, as "Health (n / max)".</param>
public sealed record CraftingRow(string Text, RecipeRecord? Recipe = null, CraftingTask? Task = null, int? Member = null, string? Health = null,
    Stat? Stat = null);

/// <summary>
/// A skill's popup on the parchment the details use (<c>WidgetCrafting</c>): a title over a list with a
/// cursor. A does the row's task, C-down shows a recipe's product, and B fades it out.
/// </summary>
/// <remarks>
/// The title is centred on x 93, 70 down, and the list is from x 23 to 163 under it, 122 tall for the Alchemist's recipes and
/// 117 for the rest (<see cref="Layout"/>). Its rows are a line of the slim font apart, 11, or a member's portrait and two
/// lines, 28.
/// </remarks>
/// <param name="user">The party slot of the member using the skill.</param>
/// <param name="target">For <see cref="CraftingTask.RestoreStat"/>, the member being restored.</param>
/// <param name="fade">
/// The popup's fade, kept when the Healer's lists change in it, as the original rebuilds its title and list in the same
/// popup (<c>Healer.ClearList</c>, <c>0x80004304</c>); else a new one, fading in from nothing.
/// </param>
public sealed class CraftingMenu(CraftingTask task, string title, IReadOnlyList<CraftingRow> rows, int user, int? target = null,
    PopupFade? fade = null)
{
    /// <summary>Where the title is centred across, and its top.</summary>
    public const int TitleCentre = 93, TitleTop = 70;

    /// <summary>The list's sides.</summary>
    public const int ListLeft = 23, ListRight = 163;

    /// <summary>A text row's pitch, a line of the slim font (a 9-texel cell and 2), and a member's.</summary>
    public const int TextRow = 11, MemberRow = 28;

    public CraftingTask Task { get; } = task;

    public string Title { get; } = title;

    /// <summary>The list's place and the rows' pitch, under the title.</summary>
    public ListLayout Layout { get; } = LayoutOf(task);

    public MenuList<CraftingRow> Rows { get; } = LayoutOf(task).List(rows);

    /// <summary>The party slot of the member using the skill.</summary>
    public int User { get; } = user;

    public int? Target { get; } = target;

    /// <summary>How far it's faded in or out: it opens from nothing, and takes presses only once it's open.</summary>
    public PopupFade Fade { get; } = fade ?? new();

    /// <summary>Whether the rows are members, with their portraits.</summary>
    public bool ListsMembers => ListsMembersOf(Task);

    private static bool ListsMembersOf(CraftingTask task) => task is CraftingTask.LayOnHands or CraftingTask.HealWithHerbs or CraftingTask.Restore;

    private static ListLayout LayoutOf(CraftingTask task)
    {
        var top = TitleTop + TextRow;
        return new ListLayout(new ScreenRect(ListLeft, top, ListRight, top + (task == CraftingTask.Alchemy ? 0x7A : 0x75)),
            ListsMembersOf(task) ? MemberRow : TextRow);
    }
}
