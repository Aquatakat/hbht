namespace Aidyn.Core.Ui;

/// <summary>
/// What the pause menu's Party tab is showing, and so what takes its presses: the tab itself (<see cref="PartyMenu"/>),
/// the loot window and shops (<see cref="BarterMenu"/>) and the training window (<see cref="TrainingMenu"/>), each a
/// <c>WidgetDollMenu</c> in one of the original's pause states. The view draws the header and lists of
/// <see cref="Menu"/> for every window and its own parts besides, and the pause menu sends each press to the window
/// that is up. A further window, as combat's post-battle one (pause state 5) will be, implements this and the view draws it.
/// </summary>
/// <remarks>
/// A window handles a press as the original's widget handler gives it to the top widget (<see cref="MenuOverlay"/>): a
/// popup that is open takes it, and what is under waits. A press the window has no use for does nothing, so the pause menu
/// needs no knowledge of which window it is sending to.
/// </remarks>
public interface IPartyWindow
{
    /// <summary>
    /// The header, with the party's member shown and the lists under it: the window itself for the tab, else the
    /// <see cref="PartyMenu"/> it is built on.
    /// </summary>
    PartyMenu Menu { get; }

    /// <summary>The popups over the window: its message, help and question, and C-down's details.</summary>
    MenuOverlay Overlay { get; }

    /// <summary>Whether a popup or the details are taking the presses, fading or not.</summary>
    bool HasPopup { get; }

    /// <summary>C-down's details, which fade as popups do (<see cref="PopupFade"/>).</summary>
    DetailsPane DetailsPane { get; }

    /// <summary>Whether C-left and C-right change the member shown, which they don't in the loot window and shops.</summary>
    bool ChangesMember { get; }

    /// <summary>Whether the window closed itself, as leaving the loot's items does. The pause menu closes when it has.</summary>
    bool Closed => false;

    /// <summary>What C-down shows for the row the window points at, or null if it shows nothing.</summary>
    MenuDetails? Details();

    /// <summary>C-down pressed: shows the details, unless a popup is up or they show already.</summary>
    void PressDetails();

    /// <summary>C-down let go: the details fade out once they're open.</summary>
    void ReleaseDetails();

    /// <summary>Steps the window's pulses, changes and fades by a tick.</summary>
    void Step();

    /// <summary>Moves the cursor of the popup that is up, or of the list the window is working in.</summary>
    void MoveCursor(int by);

    /// <summary>
    /// The stick left (<paramref name="direction"/> −1) or right (1): the list the window works in, or the lists shown.
    /// </summary>
    void Sideways(int direction);

    /// <summary>Shows the next member, or with a negative step the one before (C-left and C-right), if the window changes it.</summary>
    void ChangeMember(int by);

    /// <summary>A: closes a message or the help, chooses in a popup, or uses the row the window points at.</summary>
    void Accept();

    /// <summary>B, or Start: closes a popup. False if nothing holds the window open, so it closes.</summary>
    bool Back();

    /// <summary>C-down or C-up: closes a message. True if it did, so C-down doesn't show the details too.</summary>
    bool CloseMessage();

    /// <summary>C-up (the shop's appraisal): does what it does in this window. False if the window has no use for it.</summary>
    bool Appraise() => false;

    /// <summary>
    /// A press on the window, whichever it is, as the pause menu's Party tab sends them and the editor's panels press them:
    /// C-down closes a message, else holds the details open, and its letting go lets them fade; C-up appraises; A takes the row;
    /// C-left shows the next member and C-right the one before, and the stick's left the next list and its right the one before,
    /// as the original's do; up and down move the cursor. True if the window has a use for the button.
    /// </summary>
    bool Press(MenuAction action)
    {
        switch (action)
        {
            case MenuAction.Details:
                if (!CloseMessage())
                    PressDetails();
                return true;
            case MenuAction.DetailsRelease:
                ReleaseDetails();
                return true;
            case MenuAction.Appraise:
                Appraise();
                return true;
            case MenuAction.Accept:
                Accept();
                return true;
            case MenuAction.MemberLeft:
                ChangeMember(1);
                return true;
            case MenuAction.MemberRight:
                ChangeMember(-1);
                return true;
            case MenuAction.Left or MenuAction.Right:
                Sideways(action.Horizontal());
                return true;
            case MenuAction.Up or MenuAction.Down:
                MoveCursor(action.Vertical());
                return true;
            default:
                return false;
        }
    }
}
