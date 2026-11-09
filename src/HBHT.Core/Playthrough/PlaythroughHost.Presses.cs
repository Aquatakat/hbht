using System.Numerics;
using HBHT.Core.MapObjects;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Core.Ui;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The presses a player makes, asked of the policies by the mode that runs and pressed before the tick, as the runtime's input events come
/// before its physics tick: one button a tick, as a player presses them.
/// </summary>
public sealed partial class PlaythroughHost
{
    /// <summary>The page being read, how many ticks it has shown, and the choice the policy chose for it.</summary>
    private (object Key, int Age, int? Choice)? _page;

    /// <summary>The nodes of the choices taken at each page's node, for each script run.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScriptRunner, Dictionary<int, HashSet<int>>> _taken = [];

    /// <summary>Ticks the pause menu has been open, and since the last press in it or on a popup.</summary>
    private int _menuOpen, _sincePress;

    /// <summary>
    /// The presses this tick, to the mode that runs, and the stick it ticks with: a cinematic skipped and the credits ended or hurried,
    /// the menu's and the popups' presses, a page read, a battle's tick, or trek's input.
    /// </summary>
    private PlayerInput Deliver(GameSession session)
    {
        var still = new PlayerInput(Vector2.Zero);
        if (session.Mode != GameMode.Menu)
            _menuOpen = 0;
        _sincePress++;
        switch (session.Mode)
        {
            case GameMode.Cinematic when Options.SkipCinematics && session.Cinematic is { Player.Skipped: false }:
                // B skips it, once it's playing; till then the press is dropped, and tried again the next tick.
                Press(MenuAction.Back);
                return still;
            case GameMode.Credits when session.Credits is { } credits:
                if (!credits.Ending && Options.SkipCredits)
                    Press(MenuAction.Accept);
                else if (credits.Ending)
                    Hurry(credits.Roll);
                return still;
            case GameMode.Menu:
                PressMenu(session.Menu);
                return still;
            case GameMode.Backdrop when session.Backdrop is { Closing: false } backdrop && session.Trek.Conversation?.Runner is { Status: ScriptStatus.Showing, Page: not null } runner:
                Read(PageSource.Backdrop, runner, null, backdrop);
                return still;
            case GameMode.Popup when session.Trek.Use.Popup is { IsOpen: true } popup:
                PressPopup(popup);
                return still;
            case GameMode.Conversation when session.Trek.Conversation?.Runner is { Status: ScriptStatus.Showing, Page: not null } runner:
                Read(PageSource.Conversation, runner, session.Trek.Box, null);
                return still;
            case GameMode.CutScene when session.Scene is { Accepts: true, Page: not null } scene:
                Read(PageSource.CutScene, scene.Runner, scene.Box, null);
                return still;
            case GameMode.Combat when session.Battle is { } battle:
                return Battles.Tick(this, battle);
            case GameMode.Trek:
                var input = Input.Next(this);
                foreach (var press in input.Presses)
                    Press(press);
                return input.Stick;
            default:
                _page = null;
                return still;
        }
    }

    /// <summary>The ending's credits, which can't be skipped, roll on <see cref="PlaythroughOptions.CreditsPace"/> ticks a tick; the session's own tick is the last.</summary>
    private void Hurry(CreditsRoll roll)
    {
        for (var i = 1; i < Options.CreditsPace && !roll.Ended; i++)
            roll.Step();
    }

    /// <summary>
    /// The menu policy's press, once the menu takes presses (its scroll open, not sliding or swapping its windows) and
    /// <see cref="MenuPressGap"/> ticks after the last. A menu open past <see cref="MenuPatience"/> is closed at once, with a warning.
    /// </summary>
    private void PressMenu(PauseMenu menu)
    {
        if (++_menuOpen > MenuPatience && menu.IsOpen)
        {
            Warn($"The pause menu was open {MenuPatience} ticks on {menu.Window.GetType().Name}: the host closed it.");
            menu.CloseNow();
            _menuOpen = 0;
            return;
        }
        if (!menu.IsOpen || !menu.Taking || menu.Sliding || menu.Swapping || _sincePress < MenuPressGap)
            return;
        if (Menus.Press(this, menu) is var press and not MenuAction.None)
        {
            Press(press);
            _sincePress = 0;
        }
    }

    /// <summary>
    /// A trek popup, open: a message is closed with A; a question's cursor moves to the policy's choice and A takes it, or B backs out.
    /// </summary>
    private void PressPopup(TrekPopup popup)
    {
        if (_sincePress < MenuPressGap)
            return;
        _sincePress = 0;
        if (popup.Kind != TrekPopupKind.Choice)
        {
            Press(MenuAction.Accept);
            return;
        }
        if (Menus.Popup(this, popup) is not { } want)
        {
            Press(MenuAction.Back);
            return;
        }
        var choice = Math.Clamp(want, 0, Math.Max(0, popup.Choices.Rows.Count - 1));
        var at = popup.Choices.Cursor;
        Press(at < choice ? MenuAction.Down : at > choice ? MenuAction.Up : MenuAction.Accept);
    }

    /// <summary>
    /// A page that takes presses, read for the policy's <see cref="IConversationPolicy.ReadTicks"/>, then pressed on as the player does: A
    /// brings up the choices behind a line shown alone; on choices to take, the cursor moves a step a tick to the policy's choice and A takes
    /// it; else A moves the script on. Each page pressed on is recorded, with the choice taken.
    /// </summary>
    /// <param name="box">The box it shows in, whose cursor the presses move: a conversation's or a cut-scene's; none for a backdrop's page.</param>
    /// <param name="backdrop">The backdrop's or the help menu's page, whose own cursor the presses move.</param>
    private void Read(PageSource source, ScriptRunner runner, DialogueBox? box, BackdropPage? backdrop)
    {
        var page = runner.Page!;
        var alone = box?.ShowsLineBeforeChoices ?? false;
        var key = (runner, page.Line.Node, page.Line.Text, page.Choices.Count, alone);
        if (_page is not { } reading || !reading.Key.Equals(key))
            reading = (key, 0, null);
        reading.Age++;
        _page = reading;
        if (reading.Age <= Conversations.ReadTicks)
            return;
        if (alone)
        {
            if (Press(MenuAction.Accept) != PressOutcome.Ignored)
                _page = null;
            return;
        }
        if (!DialogueBox.PicksChoice(runner))
        {
            if (Press(MenuAction.Accept) == PressOutcome.Ignored)
                return;
            _page = null;
            _pages.Add(new PageTurned(_now, source, runner.Script.Id, page.Line.Node));
            return;
        }
        var taken = Taken(runner, page.Line.Node);
        var choice = reading.Choice ??= Math.Clamp(Conversations.Choose(new ChoiceContext(this, source, runner.Script, page, taken)), 0,
            page.Choices.Count - 1);
        _page = reading;
        var at = box?.Highlight ?? backdrop!.Highlight;
        if (at != choice)
        {
            Press(at < choice ? MenuAction.Down : MenuAction.Up);
            return;
        }
        if (Press(MenuAction.Accept) == PressOutcome.Ignored)
            return;
        _page = null;
        var chosen = page.Choices[choice];
        taken.Add(chosen.Node);
        _pages.Add(new PageTurned(_now, source, runner.Script.Id, page.Line.Node, choice, page.Choices.Count, chosen.Node, chosen.Text));
    }

    /// <summary>The nodes of the choices taken at a page's node in this run of a script.</summary>
    private HashSet<int> Taken(ScriptRunner runner, int node)
    {
        var nodes = _taken.GetValue(runner, _ => []);
        if (!nodes.TryGetValue(node, out var taken))
            nodes[node] = taken = [];
        return taken;
    }
}
