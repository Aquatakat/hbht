using Aidyn.Core;
using Aidyn.Core.Ui;

namespace Aidyn.Rom.Tests;

/// <summary>The pause menu's Quit Game, the port's, headless, on NA 1.0's data: what it warns of, and where it goes.</summary>
public sealed partial class GameSessionTests
{
    [Fact]
    public void QuitGameWarnsUnlessTheGameWasSavedSinceTheMenuOpenedAndIsAsItWasSaved()
    {
        var (session, host) = Start("G14", 40, 40);
        Tick(session);
        OpenMenu(session);
        var menu = session.Menu;
        Assert.True(menu.Unsaved);

        // Save Game, on Create New Save, and Continue back to the list.
        menu.System.Items.Point((int)SystemItem.SaveGame);
        session.Press(MenuAction.Accept);
        session.Press(MenuAction.Accept);
        Tick(session);
        Assert.Single(host.Saved);
        Assert.False(menu.Unsaved);
        session.Press(MenuAction.Accept);
        Assert.True(menu.System.OnList);

        // The time played and the sky's twinkling pass while the menu is open, which loses nothing.
        for (var i = 0; i < 120; i++)
            Tick(session);
        Assert.False(menu.Unsaved);
        menu.System.Items.Point(menu.System.Items.Rows.Count - 1);
        session.Press(MenuAction.Accept);
        Assert.Equal("", menu.System.Quitting!.Text);
        session.Press(MenuAction.Back);

        // Anything a save keeps does, until it's as it was saved again.
        session.Game.Party.Gold++;
        Assert.True(menu.Unsaved);
        session.Game.Party.Gold--;
        Assert.False(menu.Unsaved);

        // Unpausing does, whatever trek did while it ran.
        session.Press(MenuAction.Back);
        TickUntilMenuClosed(session);
        OpenMenu(session);
        Assert.True(menu.Unsaved);
        menu.System.Items.Point(menu.System.Items.Rows.Count - 1);
        session.Press(MenuAction.Accept);
        Assert.Equal(SystemMenu.Unsaved, menu.System.Quitting!.Text);
    }

    [Theory]
    [InlineData(0, typeof(Transition.Quit))]
    [InlineData(1, typeof(Transition.ToTitle))]
    public void QuitGamesChoicesCloseTheMenuAtOnceAndEndTheGame(int choice, Type transition)
    {
        var (session, host) = Start("G14", 40, 40);
        Tick(session);
        OpenMenu(session);
        var menu = session.Menu;
        menu.System.Items.Point(menu.System.Items.Rows.Count - 1);
        session.Press(MenuAction.Accept);
        menu.System.PointQuit(choice);
        session.Press(MenuAction.Accept);

        // No scroll rolls up first.
        Assert.False(menu.IsOpen);
        Assert.Equal(SessionStep.MenuClosed, Tick(session));
        Assert.IsType(transition, host.Transitions[^1]);
    }

    [Fact]
    public void TheTitlesQuitGameEndsTheProgramWithNoFade()
    {
        var (session, host) = Start("G14", 40, 40);
        session.ShowTitle(cards: false, skippable: false);
        var title = session.Title!;
        // PRESS START fades in, then Start brings the menu up, and it settles over the logo's fade.
        for (var i = 0; i < 20; i++)
            Tick(session);
        title.Press(MenuAction.Start);
        for (var i = 0; i < 20 && (title.Menu!.Fading || title.LogoAlpha > 0); i++)
            Tick(session);
        title.Menu!.Items.Point(3);
        title.Press(MenuAction.Accept);
        Tick(session);
        Assert.IsType<Transition.Quit>(host.Transitions[^1]);
    }
}
