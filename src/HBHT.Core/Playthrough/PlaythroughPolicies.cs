using System.Numerics;
using HBHT.Core.Combat;
using HBHT.Core.MapObjects;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>What the host hands trek for a tick: the stick, and the presses delivered before it, in order.</summary>
public readonly record struct TickInput(PlayerInput Stick, IReadOnlyList<MenuAction> Presses)
{
    /// <summary>Nothing held, nothing pressed.</summary>
    public static readonly TickInput None = new(new PlayerInput(Vector2.Zero), []);
}

/// <summary>
/// What the player does in trek when nothing else has the controls (<see cref="GameMode.Trek"/>): the stick, and presses such as A on
/// the map object he reaches. The pilot (layer 4) is one; the default holds nothing (<see cref="NoInput"/>).
/// </summary>
public interface IInputPolicy
{
    /// <summary>This tick's input, asked once a tick in trek before the session ticks.</summary>
    TickInput Next(PlaythroughHost host);
}

/// <summary>Nobody at the controls.</summary>
public sealed class NoInput : IInputPolicy
{
    public static readonly NoInput Instance = new();

    public TickInput Next(PlaythroughHost host) => TickInput.None;
}

/// <summary>Where a page the host turns is shown.</summary>
public enum PageSource
{
    /// <summary>A cut-scene's box (<see cref="CutScene.Box"/>).</summary>
    CutScene,

    /// <summary>A conversation's or a sign's box in trek (<see cref="TrekLoop.Box"/>).</summary>
    Conversation,

    /// <summary>A backdrop's or the help menu's full-screen page (<see cref="TrekLoop.Backdrop"/>).</summary>
    Backdrop,
}

/// <summary>
/// A page with choices to take, as the host asks a policy which.
/// </summary>
/// <param name="Script">The script showing it.</param>
/// <param name="Page">Its line and its choices.</param>
/// <param name="Taken">
/// The nodes of the choices already taken at this page's node since the script started (<see cref="ScriptLine.Node"/>): a loop back to the
/// page brings up again those a backdrop or the help menu shows, where a conversation hides them.
/// </param>
public sealed record ChoiceContext(PlaythroughHost Host, PageSource Source, ScriptRecord Script, ScriptPage Page, IReadOnlySet<int> Taken);

/// <summary>
/// How conversations, signs, backdrops and cut-scenes' pages are read: how long each shows before it's pressed on, and which choice a
/// page with choices takes. The host presses as the player does, one button a tick, moving the cursor to the choice and pressing A.
/// </summary>
public interface IConversationPolicy
{
    /// <summary>How many ticks a page shows, once it takes presses, before the host presses on it.</summary>
    int ReadTicks { get; }

    /// <summary>Which of <see cref="ChoiceContext.Page"/>'s choices to take, from 0, as they're shown.</summary>
    int Choose(ChoiceContext page);
}

/// <summary>
/// The default reading: every page pressed on at once, and on a page with choices the first not yet taken at that node since the script
/// started, else the last. A conversation hides the choices taken, so it always takes the first shown; a backdrop's or the help menu's
/// page shows them again, so a book's chapters are read through to the last, usually the way out, rather than the first for ever.
/// </summary>
public sealed class FirstChoice : IConversationPolicy
{
    public static readonly FirstChoice Instance = new();

    public int ReadTicks => 0;

    public int Choose(ChoiceContext page)
    {
        for (var i = 0; i < page.Page.Choices.Count; i++)
        {
            if (!page.Taken.Contains(page.Page.Choices[i].Node))
                return i;
        }
        return page.Page.Choices.Count - 1;
    }
}

/// <summary>
/// Who plays a battle (layer 5's seam): asked as it begins, every tick it runs before the session ticks it, and as it ends. It plays by
/// the stick it returns, which the battle reads in a turn the player plays, and by pressing (<see cref="PlaythroughHost.Press"/>) or acting
/// on the battle as the combat AI does. The default wins outright (<see cref="WinOutright"/>).
/// </summary>
public interface IBattlePolicy
{
    /// <summary>The battle has begun on its arena (<see cref="Transition.StartedBattle"/>).</summary>
    void Begin(PlaythroughHost host, Battle battle)
    {
    }

    /// <summary>A tick of the battle, before the session steps it: the stick for a turn the player plays.</summary>
    PlayerInput Tick(PlaythroughHost host, Battle battle);

    /// <summary>The battle is over and its arena gone (<see cref="Transition.EndBattle"/>); its <see cref="Battle.Outcome"/> is set.</summary>
    void End(PlaythroughHost host, Battle battle)
    {
    }
}

/// <summary>
/// Wins every battle outright on the party's first turn the player plays: the enemies on the field fall and no reserve comes
/// (<see cref="Battle.ForceWin"/>, the runtime's B held), so the turn's end wins it through the game's own victory (state 12), which sets
/// the encounter flag, gives the gold and the experience, and opens the post-battle windows. Turns before it play as they come, the
/// enemies' by their AI. The Shadow's battle can't be won so, as it ends in the merge or its vanishing; it plays on unwon.
/// </summary>
public sealed class WinOutright : IBattlePolicy
{
    public static readonly WinOutright Instance = new();

    public PlayerInput Tick(PlaythroughHost host, Battle battle)
    {
        host.WinBattleOutright();
        return new PlayerInput(Vector2.Zero);
    }
}

/// <summary>
/// What's done in the pause menu, opened as a container's loot, a shop, training, the post-battle windows, Start or a battle's pause, and
/// in trek's popups. The host asks for a press each time the menu takes one (<see cref="PlaythroughHost.MenuPressGap"/> ticks apart), and
/// presses it as the player would; <see cref="MenuAction.None"/> waits.
/// </summary>
public interface IMenuPolicy
{
    /// <summary>The next press for the open menu.</summary>
    MenuAction Press(PlaythroughHost host, PauseMenu menu);

    /// <summary>Which of a trek popup's choices to take, from 0, or null to back out with B. A message is closed with A whatever this says.</summary>
    int? Popup(PlaythroughHost host, TrekPopup popup);
}

/// <summary>
/// The default: the post-battle window is closed into its loot, loot is taken stack by stack until the pile is empty or a stack won't go
/// (the inventory or the stack is full), then left; shops, training and the pause menu are left at once; any popup in them is closed or
/// answered with its first option; and a trek popup's question takes its first option.
/// </summary>
public sealed class TakeLootLeaveTheRest : IMenuPolicy
{
    /// <summary>The loot window being taken from, and the pile's size when a stack was last taken from it.</summary>
    private (BarterMenu Window, int Before)? _taking;

    /// <summary>A loot window whose next stack wouldn't go, which is left with what's on it.</summary>
    private BarterMenu? _gaveUp;

    public MenuAction Press(PlaythroughHost host, PauseMenu menu)
    {
        if (menu.Tab != PauseTab.Party)
            return MenuAction.Back;
        var window = menu.Window;
        if (window is BarterMenu { Loot: not null } loot)
            return Loot(loot);
        // A message, the help, a question or the details: A closes or answers it (a question's cursor starts on its first option).
        if (window.HasPopup)
            return MenuAction.Accept;
        return MenuAction.Back;
    }

    private MenuAction Loot(BarterMenu loot)
    {
        var count = loot.Pile.Rows.Count;
        if (_taking is var (window, before) && window == loot && before == count && count > 0)
            _gaveUp = loot;
        _taking = null;
        // A message (what wouldn't go, or the first potion's help), the leave question's "Yes, leave them.", or a drop's question's
        // "No": A.
        if (loot.HasPopup)
            return MenuAction.Accept;
        if (count == 0 || _gaveUp == loot)
            return MenuAction.Back;
        if (!loot.OnPile)
            return MenuAction.Left;
        _taking = (loot, count);
        return MenuAction.Accept;
    }

    public int? Popup(PlaythroughHost host, TrekPopup popup) => 0;
}
