using Aidyn.Core;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>The title screen over a new game's session, headless, on NA 1.0's data (<see cref="GameSession.ShowTitle"/>).</summary>
public sealed partial class GameSessionTests
{
    [Fact]
    public void TheTitleFreesTreksWorldPlaysItsMusicAndAfterTheCardsFliesOverTheOverworld()
    {
        var (session, host) = Start();
        session.ShowTitle(cards: true, skippable: false);
        var title = session.Title!;

        Assert.Equal(GameMode.Title, session.Mode);
        Assert.False(session.Zone.WorldLoaded);
        Assert.False(session.StreamsTrek);
        Assert.Equal(session.Game.Sounds.Title, session.Zone.Audio.Music);
        Assert.IsType<Transition.ShowTitle>(host.Transitions[^1]);
        // Nothing skips the original's cards.
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Start));

        // 775 ticks of cards, the last of which starts the title.
        for (var i = 0; i < 774; i++)
            Assert.Equal(SessionStep.Title, Tick(session));
        Assert.Equal(TitlePhase.Cards, title.Phase);
        Tick(session);
        Assert.Equal(TitlePhase.Title, title.Phase);
        // The first fly-through's scenery: the Overworld's chunks about (11, 9), its coordinates from (11, 6)'s corner.
        var scenery = host.Transitions.OfType<Transition.StartTitleScenery>().Single();
        Assert.Equal(0, title.Place);
        var overworld = Data.Set.Get<MapSetRecord>(TitleScreen.Overworld);
        Assert.Equal(overworld.Origin(11, 6), scenery.Corner);
        Assert.Same(session.Scenery, scenery.Scenery);
        Assert.True(scenery.Scenery.WorldLoaded);
        // No time is played on the title.
        Assert.Equal(0u, session.Game.PlayTime);
    }

    [Fact]
    public void AFlyThroughFadesOutOverItsLast100TicksAndTheNextBuildsAndFadesIn()
    {
        var (session, host) = Start();
        session.ShowTitle(cards: false, skippable: false);
        var title = session.Title!;
        Tick(session);
        var first = title.Flycam!;
        Assert.Equal(1, title.Time);

        // The first fades in over 15 ticks.
        for (var i = 0; i < 15; i++)
            Tick(session);
        Assert.Equal(1, title.SceneryBrightness, 3);
        while (title.Place == 0)
            Tick(session);
        // Its animation's length less 100 to start the fade out, then 100 ticks of it.
        Assert.Equal(1, title.Time);
        Assert.Single(host.Transitions.OfType<Transition.EndTitleScenery>());
        Assert.Equal(2, host.Transitions.OfType<Transition.StartTitleScenery>().Count());
        Assert.NotEqual(first, title.Flycam);
        Assert.Equal(0, title.SceneryBrightness);
        // The rest fade in over 100.
        for (var i = 0; i < 99; i++)
            Tick(session);
        Assert.True(title.SceneryBrightness < 1);
        Tick(session);
        Assert.Equal(1, title.SceneryBrightness, 3);
    }

    [Fact]
    public void NewGameNamesAlaronFadesOutAndBeginsTheGameWithTheOpening()
    {
        var (session, host) = Start();
        session.ShowTitle(cards: false, skippable: false);
        var title = session.Title!;
        // The logo fades in over 13 ticks, and takes no press till it has.
        for (var i = 0; i < 12; i++)
            Tick(session);
        session.Press(MenuAction.Start);
        Assert.Null(title.Menu);
        Tick(session);
        Assert.Equal(255, title.LogoAlpha);

        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Start));
        var menu = title.Menu!;
        Assert.Equal([SystemItem.NewGame, SystemItem.LoadGame, SystemItem.Options, SystemItem.Quit], menu.Items.Rows);
        // Nothing takes a press while the logo fades out and the list in.
        session.Press(MenuAction.Down);
        Assert.Equal(0, menu.Items.Cursor);
        for (var i = 0; i < 13; i++)
            Tick(session);
        session.Press(MenuAction.Accept);
        for (var i = 0; i < 26; i++)
            Tick(session);
        Assert.Equal(SystemPage.Rename, menu.Page);
        Assert.True(menu.TypeName('n'));
        session.Press(MenuAction.Start);
        session.Press(MenuAction.Start);
        Tick(session);
        Assert.Equal(TitlePhase.Leaving, title.Phase);

        // The screen fades out over 20 ticks, and then the game begins.
        for (var i = 0; i < 19; i++)
            Tick(session);
        Assert.Equal(0.05f, title.Brightness, 3);
        Assert.Same(title, session.Title);
        Tick(session);
        Assert.Null(session.Title);
        Assert.Null(session.Scenery);
        Assert.Equal(GameMode.Cinematic, session.Mode);
        Assert.Equal(RecordId.Decimal(RecordTypes.Cinematic, 0), session.Cinematic!.Record.Id);
        Assert.True(session.Zone.WorldLoaded);
        Assert.Equal("Alaronn", session.Game.Party.Members.First().Name);
        Assert.Contains(host.Transitions, t => t is Transition.EndTitle);
        Assert.NotEqual(session.Game.Sounds.Title, session.Zone.Audio.Music);
    }
}
