using Aidyn.Core;
using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The controls, headless, on NA 1.0's data: each press goes to the mode that runs (<see cref="GameSession.Press"/>), as the
/// runtime sends them, and what it comes to is returned.
/// </summary>
public sealed partial class GameSessionTests
{
    [Fact]
    public void InTrekStartOpensTheMenuAUsesWhatIsReachedAndBRaisesThePortraits()
    {
        // Away from I08, where a new game's first steps cue the opening's cut-scene.
        var (session, _) = Start("G14", 40, 40);
        var hud = session.Trek.Hud;
        Tick(session);
        var shown = hud.Portraits.Shown;
        // Nothing is within reach where the player stands.
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Accept));
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.Equal(!shown, hud.Portraits.Shown);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.Equal(shown, hud.Portraits.Shown);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Details));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.None));
        Assert.Equal(PressOutcome.MenuOpened, session.Press(MenuAction.Start));

        // With the free camera flying, the world is frozen: A and B do nothing, and Start still opens the menu.
        session.Tick(() => new Core.Trek.PlayerInput(System.Numerics.Vector2.Zero), frozen: true, true, _ => null);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Back));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Accept));
        Assert.Equal(PressOutcome.MenuOpened, session.Press(MenuAction.Start));
        Tick(session);

        // Trek's input is dead while it fades into a cut-scene.
        var ambush = Data.Set.Get<DialogueRecord>(RecordId.Parse("aidyn:dialogue/0x0365"));
        var (_, map, dialogue) = Data.Of<DialogueObject>().First(d => d.Object.Script == ambush.Id && d.Object.Stage is not null);
        session.Cue(ambush, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);
        Tick(session);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Start));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Accept));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Back));
    }

    [Fact]
    public void APopupTakesAAndBAndTheStickAndNothingElse()
    {
        var (session, _) = Start();
        session.Trek.Use.Tell("Something.");
        Assert.Equal(GameMode.Popup, session.Mode);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Start));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Details));
        Assert.Equal(GameMode.Popup, session.Mode);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.Null(session.Trek.Use.Popup);
        Assert.Equal(GameMode.Trek, session.Mode);

        session.Trek.Use.Tell("Again.");
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.Equal(GameMode.Trek, session.Mode);
    }

    [Fact]
    public void TheHelpMenusPageTakesAAndBAndFadesOutOnceItsScriptEnds()
    {
        var (session, host) = Start("G14", 40, 40);
        session.MenuClosed(PauseExit.Help);
        var trek = session.Trek;
        Assert.Equal(GameMode.Backdrop, session.Mode);
        Assert.True(session.Held);
        var page = trek.Backdrop!;
        // The page fades in over the first ticks, the world held under it.
        Assert.Equal(0, page.Alpha);
        for (var i = 0; i < 60 && page.Alpha < 255; i++)
            Assert.Equal(SessionStep.Held, Tick(session));
        Assert.Equal(255, page.Alpha);
        Assert.Equal(SystemMenu.HelpScript, trek.Conversation!.Runner.Script.Id);
        var top = page.Layout!;
        Assert.True(top.Choices.Count > 1, "the help menu's first page lists its topics");

        // The stick moves the cursor; a backdrop's page wraps.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        Assert.Equal(1, page.Highlight);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Up));
        Assert.Equal(0, page.Highlight);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Up));
        Assert.Equal(top.Choices.Count - 1, page.Highlight);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Start));

        // A takes the cursor's choice: the first topic opens, on a new page.
        session.Press(MenuAction.Down);
        var changes = host.ConversationChanges;
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.True(host.ConversationChanges > changes);
        Assert.NotSame(top, page.Layout);
        Assert.Equal(0, page.Highlight);

        // B takes the last choice, back and then Exit Help, until the script ends; the page fades out, and the game goes on.
        for (var i = 0; i < 5 && trek.Conversation is not null; i++)
            Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.Null(trek.Conversation);
        Assert.True(page.Closing);
        Assert.Equal(GameMode.Backdrop, session.Mode);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Accept));
        var ticks = 0;
        while (session.Mode == GameMode.Backdrop && ticks++ < 120)
            Assert.Equal(SessionStep.Held, Tick(session));
        Assert.Null(trek.Backdrop);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.InRange(ticks, 2, 120);
    }

    [Fact]
    public void ASignsPagesTurnWithAInTheBoxAndStartDoesNothing()
    {
        var (session, host) = Start();
        var trek = session.Trek;
        var sign = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue).First(d => d.Mode == DialogueMode.Sign
            && Data.Of<DialogueObject>().Any(o => o.Object.Script == d.Id));
        trek.ShowBackdrop(sign);
        Assert.Equal(GameMode.Conversation, session.Mode);
        Assert.NotNull(trek.Box.Layout);
        Assert.Null(trek.Backdrop);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Start));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Details));

        var pages = 0;
        for (var i = 0; i < 20 && trek.Conversation is not null; i++)
        {
            var changes = host.ConversationChanges;
            Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
            Assert.True(host.ConversationChanges > changes);
            pages++;
        }
        Assert.Null(trek.Conversation);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.InRange(pages, 1, 20);
    }

    [Fact]
    public void ACutScenesPressesReachItsBoxOnlyOnceItAccepts()
    {
        var (session, _) = Start();
        var ambush = Data.Set.Get<DialogueRecord>(RecordId.Parse("aidyn:dialogue/0x0365"));
        var (_, map, dialogue) = Data.Of<DialogueObject>().First(d => d.Object.Script == ambush.Id && d.Object.Stage is not null);
        session.Cue(ambush, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.CutSceneCued; i++)
            step = Tick(session);
        session.BeginCutScene();
        var scene = session.Scene!;
        Assert.Equal(GameMode.CutScene, session.Mode);
        // The screen fades in first: nothing reaches the box.
        Assert.False(scene.Accepts);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Accept));

        var ticks = 0;
        while (!(scene.Accepts && scene.Page is not null) && ticks++ < 1200)
            Tick(session);
        Assert.True(scene.Accepts && scene.Page is not null, "the cut-scene shows a page that takes presses");
        Assert.NotNull(scene.Box.Layout);
        var before = scene.Runner.Page;
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Start));
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.True(scene.Runner.Page != before || scene.Runner.Status != ScriptStatus.Showing, "A moved the script on");
    }

    [Fact]
    public void ACinematicSkipsOnBOrStartAndTheCreditsOnAToo()
    {
        var (session, _) = Start();
        var cinematic = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 0));
        session.PlayCinematic(cinematic, fromTheatre: false);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.Equal(PressOutcome.Skip, session.Press(MenuAction.Back));
        Assert.Equal(PressOutcome.Skip, session.Press(MenuAction.Start));
        session.CinematicEnded();

        session.MenuClosed(PauseExit.Credits);
        Assert.Equal(GameMode.Credits, session.Mode);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        Assert.Equal(PressOutcome.Skip, session.Press(MenuAction.Accept));
    }
}
