using Aidyn.Core.Ui;

namespace Aidyn.Core.Combat;

public sealed partial class Battle
{
    /// <summary>
    /// A press in combat (<c>Combat.Controls</c>), before the tick reads the stick: any press closes Sense Aura's popup first
    /// (<see cref="DismissAura"/>); a held menu takes only up and down, and its button let go chooses (<see cref="CloseMenu"/>);
    /// otherwise C-up and C-down open the item and spell menus, Start asks for the pause menu (combat's state 27, then 14), Z
    /// (the left trigger) skips a turn the player plays or puts an aimed attack away (<see cref="Skip"/>), A attacks in a turn,
    /// confirms an aimed attack's target and skips the AI's walk (state 5, <c>Combat.InputMoving</c>), B puts an aim away, left and
    /// right step an attack's or a spell's cursor (state 9), and C-left and C-right a bow's or thrown weapon's (state 18). Whether
    /// the press did anything.
    /// </summary>
    public bool Press(MenuAction action)
    {
        if (action == MenuAction.None)
            return false;
        // Combat.DismissPopup: any press closes Sense Aura's popup, and ends the turn.
        if (action is not (MenuAction.DetailsRelease or MenuAction.AppraiseRelease) && DismissAura())
            return true;
        // A held menu (states 22 and 23): up and down move its cursor, and letting its button go chooses; nothing else does anything.
        if (Menu is { } menu)
        {
            if (action is MenuAction.Up or MenuAction.Down)
            {
                MoveMenu(action.Vertical());
                return true;
            }
            return action == (menu.Items ? MenuAction.AppraiseRelease : MenuAction.DetailsRelease) && CloseMenu(menu.Items);
        }
        switch (action)
        {
            case MenuAction.Appraise:
                return OpenMenu(items: true);
            case MenuAction.Details:
                return OpenMenu(items: false);
            case MenuAction.Start:
                return Pause();
            case MenuAction.TabPrevious:
                return Skip();
            case MenuAction.Accept:
                // A attacks in a turn, confirms an aim, and skips the AI's walk (Combat.InputMoving).
                return Attack() || Confirm() || SkipWalk();
            case MenuAction.Back:
                return Cancel();
            case MenuAction.Left or MenuAction.Right when State is CombatState.TargetAttack or CombatState.TargetSpell:
                return StepTarget(action == MenuAction.Left ? 1 : -1);
            case MenuAction.MemberLeft or MenuAction.MemberRight when State == CombatState.TargetThrown:
                return StepTarget(action == MenuAction.MemberLeft ? 1 : -1);
            default:
                return false;
        }
    }
}
