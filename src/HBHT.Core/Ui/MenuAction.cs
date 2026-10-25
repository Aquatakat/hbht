namespace HBHT.Core.Ui;

/// <summary>
/// What a press means to the game, whatever pressed it: the original's buttons as its menus, popups, conversations and
/// combat read them (<c>Controller::ReadInput</c>, <c>BaseWidget::Control</c>). The runtime translates each input event to
/// one (its <c>MenuStick.Translate</c>) and the editor's panels name them, and the core dispatches it on the mode that runs
/// (<see cref="GameSession.Press"/>), so the two can't drift (ADR 0003).
/// </summary>
public enum MenuAction
{
    /// <summary>The event is none of the buttons, or a stick that hasn't crossed its threshold anew.</summary>
    None,
    /// <summary>The stick, a key or the D-pad's up, which repeats as it's held (<see cref="StickRepeat"/>).</summary>
    Up,
    /// <summary>Down, as up.</summary>
    Down,
    /// <summary>Left, as up.</summary>
    Left,
    /// <summary>Right, as up.</summary>
    Right,
    /// <summary>A. A key held doesn't repeat it.</summary>
    Accept,
    /// <summary>B.</summary>
    Back,
    /// <summary>Start (<c>pause</c>).</summary>
    Start,
    /// <summary>C-down going down. Its letting go is <see cref="DetailsRelease"/>.</summary>
    Details,
    /// <summary>C-down going up.</summary>
    DetailsRelease,
    /// <summary>C-up going down. Its letting go is <see cref="AppraiseRelease"/>.</summary>
    Appraise,
    /// <summary>C-up going up, which chooses from combat's held C-up menu.</summary>
    AppraiseRelease,
    /// <summary>C-left, which shows the next member.</summary>
    MemberLeft,
    /// <summary>C-right, which shows the member before.</summary>
    MemberRight,
    /// <summary>The left trigger (or Z): the tab before. Repeats as a key does.</summary>
    TabPrevious,
    /// <summary>The right trigger (or X): the next tab.</summary>
    TabNext,
}

/// <summary>What the actions mean to a cursor.</summary>
public static class MenuActions
{
    /// <summary>The four directions, in the order they're repeated when held.</summary>
    public static readonly MenuAction[] Directions = [MenuAction.Up, MenuAction.Down, MenuAction.Left, MenuAction.Right];

    /// <summary>Whether an action is one of the stick's four directions.</summary>
    public static bool IsDirection(this MenuAction action) => action is MenuAction.Up or MenuAction.Down or MenuAction.Left or MenuAction.Right;

    /// <summary>Which way an action moves a cursor along a list: -1 for up, 1 for down, else 0.</summary>
    public static int Vertical(this MenuAction action) => action switch { MenuAction.Up => -1, MenuAction.Down => 1, _ => 0 };

    /// <summary>Which way an action moves sideways: -1 for left, 1 for right, else 0.</summary>
    public static int Horizontal(this MenuAction action) => action switch { MenuAction.Left => -1, MenuAction.Right => 1, _ => 0 };
}
