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
public sealed record MenuMessage(string Text, bool FromCrafting = false, (int Width, int Height)? Box = null)
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
/// cursor. A does the row's task, C-down shows a recipe's product, and B closes it.
/// </summary>
/// <param name="user">The party slot of the member using the skill.</param>
/// <param name="target">For <see cref="CraftingTask.RestoreStat"/>, the member being restored.</param>
public sealed class CraftingMenu(CraftingTask task, string title, IReadOnlyList<CraftingRow> rows, int user, int? target = null)
{
    public CraftingTask Task { get; } = task;

    public string Title { get; } = title;

    public MenuList<CraftingRow> Rows { get; } = new(rows);

    /// <summary>The party slot of the member using the skill.</summary>
    public int User { get; } = user;

    public int? Target { get; } = target;

    /// <summary>Whether the rows are members, with their portraits.</summary>
    public bool ListsMembers => Task is CraftingTask.LayOnHands or CraftingTask.HealWithHerbs or CraftingTask.Restore;
}
