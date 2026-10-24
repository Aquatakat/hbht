using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
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
        Assert.Equal(GameMode.Menu, session.Mode);
        session.Menu.CloseNow();
        Assert.Equal(SessionStep.MenuClosed, Tick(session));

        // With the free camera flying, the world is frozen: A and B do nothing, and Start still opens the menu.
        session.Tick(SessionSettings.Original, () => new Core.Trek.PlayerInput(System.Numerics.Vector2.Zero), frozen: true, _ => null);
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Back));
        Assert.Equal(PressOutcome.Ignored, session.Press(MenuAction.Accept));
        Assert.Equal(PressOutcome.MenuOpened, session.Press(MenuAction.Start));
        session.Menu.CloseNow();
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
    public void ThePortraitsComeUpAndTheMinimapGrowsAsTrekIsBuiltAgainAfterABackdropOrACutSceneButNotASign()
    {
        var (session, _) = Start("G14", 40, 40);
        var trek = session.Trek;
        var hud = trek.Hud;
        Tick(session);
        Assert.True(hud.Portraits.Shown && hud.Minimap.Large);

        // The stick lowers them; standing still after doesn't raise them.
        void Lower()
        {
            session.Tick(SessionSettings.Original, () => new Core.Trek.PlayerInput(new System.Numerics.Vector2(0, 0.3f)), false, _ => null);
            for (var i = 0; i < 10; i++)
                Tick(session);
            Assert.False(hud.Portraits.Shown || hud.Minimap.Large);
        }
        Lower();

        // A sign shows in the box over trek, which stays: they stay down.
        var sign = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue).First(d => d.Mode == DialogueMode.Sign
            && Data.Of<DialogueObject>().Any(o => o.Object.Script == d.Id));
        trek.ShowBackdrop(sign);
        for (var i = 0; i < 20 && trek.Conversation is not null; i++)
            session.Press(MenuAction.Accept);
        Assert.Null(trek.Conversation);
        Assert.False(hud.Portraits.Shown || hud.Minimap.Large);

        // A backdrop, here the help menu, leaves trek and comes back through InitGameTrek: they come up.
        session.MenuClosed(PauseExit.Help);
        for (var i = 0; i < 10 && trek.Conversation is not null; i++)
            session.Press(MenuAction.Back);
        Assert.Null(trek.Conversation);
        Assert.True(hud.Portraits.Shown && hud.Minimap.Large);
        for (var i = 0; i < 120 && session.Mode != GameMode.Trek; i++)
            Tick(session);
        Lower();

        // So does a cut-scene's end.
        var ambush = Data.Set.Get<DialogueRecord>(RecordId.Parse("aidyn:dialogue/0x0365"));
        var (_, map, dialogue) = Data.Of<DialogueObject>().First(d => d.Object.Script == ambush.Id && d.Object.Stage is not null);
        session.Cue(ambush, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);
        for (var i = 0; i < 200 && Tick(session) != SessionStep.CutSceneCued; i++)
        {
        }
        session.BeginCutScene();
        Tick(session);
        session.EndCutScene();
        Assert.True(hud.Portraits.Shown && hud.Minimap.Large);
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
        var (session, _) = Start("G14", 40, 40);
        var cinematic = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 0));
        session.PlayCinematic(cinematic, fromTheatre: true);
        var player = session.Cinematic!.Player;
        for (var i = 0; i < 10; i++)
            Tick(session);
        // A cinematic takes every press; only B and Start skip it, and it ends within a few ticks.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.False(player.Skipped);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.True(player.Skipped);
        for (var i = 0; i < 10 && session.Mode == GameMode.Cinematic; i++)
            Tick(session);
        Assert.Equal(GameMode.Trek, session.Mode);

        // The credits take every press too, and A, B or Start ends them.
        session.MenuClosed(PauseExit.Credits);
        Assert.Equal(GameMode.Credits, session.Mode);
        for (var i = 0; i < 10; i++)
            Tick(session);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        Assert.Equal(GameMode.Credits, session.Mode);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.Equal(SessionStep.Credits, Tick(session));
        Assert.Equal(GameMode.Trek, session.Mode);
    }

    /// <summary>Opens the pause menu with Start and ticks until the scroll has unrolled and it takes presses.</summary>
    private static int OpenMenu(GameSession session)
    {
        Assert.Equal(PressOutcome.MenuOpened, session.Press(MenuAction.Start));
        var ticks = 0;
        while (!session.Menu.Taking && ticks++ < 60)
            Assert.Equal(SessionStep.Held, Tick(session));
        Assert.True(session.Menu.Taking);
        return ticks;
    }

    /// <summary>Ticks until the menu has rolled up and the session says so.</summary>
    private static void TickUntilMenuClosed(GameSession session)
    {
        var step = SessionStep.Held;
        for (var i = 0; i < 300 && step == SessionStep.Held; i++)
            step = Tick(session);
        Assert.Equal(SessionStep.MenuClosed, step);
        Assert.False(session.Menu.IsOpen);
    }

    [Fact]
    public void StartOpensTheMenuWhichUnrollsChangesTabAndRollsUpOnB()
    {
        var (session, host) = Start("G14", 40, 40);
        Tick(session);
        var menu = session.Menu;
        var sounds = session.Game.Sounds!.Menu;
        Assert.Equal(PressOutcome.MenuOpened, session.Press(MenuAction.Start));
        Assert.Equal((GameMode.Menu, true, PauseTab.System, PauseScrollPhase.Darkening), (session.Mode, menu.IsOpen, menu.Tab, menu.Scroll.Phase));
        // Nothing reaches the menu while it opens: B is swallowed, and the menu stays.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.True(menu.IsOpen);
        // The world darkens over 13 frames, then the scroll unrolls, in no time without its actor, and the menu takes presses.
        var ticks = 0;
        while (!menu.Taking && ticks++ < 60)
            Assert.Equal(SessionStep.Held, Tick(session));
        Assert.InRange(ticks, 13, 16);
        // The scroll's unrolling plays a tab's sound, back on System.
        Assert.Equal([sounds.TabBack], host.Sounds);
        Assert.Contains(host.Cues, c => c is ScrollPlayCue { Cue.Request: not 0 });

        // R slides to the Party tab over 32 frames, with its sound, and presses wait while it slides.
        host.Cues.Clear();
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.TabNext));
        Assert.Equal((PauseTab.Party, true), (menu.Tab, menu.Sliding));
        var cursor = menu.Window.Menu.Inventory.Cursor;
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        Assert.Equal(cursor, menu.Window.Menu.Inventory.Cursor);
        ticks = 0;
        while (menu.Sliding && ticks++ < 100)
            Tick(session);
        Assert.Equal(32, ticks);
        Assert.Equal([sounds.TabForward], host.Sounds);
        // Once there, down moves the Party tab's cursor, with the cursor's sound twice.
        host.Cues.Clear();
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        Assert.Equal([sounds.Cursor, sounds.Cursor], host.Sounds);
        // R again to the World tab, and once more does nothing: there's no tab past it, and no wrap.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.TabNext));
        while (menu.Sliding)
            Tick(session);
        Assert.Equal(PauseTab.World, menu.Tab);
        session.Press(MenuAction.TabNext);
        Assert.Equal((PauseTab.World, false), (menu.Tab, menu.Sliding));

        // B rolls the scroll up, with the closing slide's sound, back from World, then B's own; the session says when it has closed.
        host.Cues.Clear();
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        Assert.Equal(PauseScrollPhase.Rolling, menu.Scroll.Phase);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Start));
        TickUntilMenuClosed(session);
        Assert.Equal([sounds.TabBack, sounds.Back], host.Sounds);
        Assert.Equal(GameMode.Trek, session.Mode);
        // The menu remembers its tab, and opens there again.
        Assert.Equal(PauseTab.World, menu.Remembered);
        OpenMenu(session);
        Assert.Equal(PauseTab.World, menu.Tab);
    }

    [Fact]
    public void TheSystemTabsHelpRollsTheMenuUpIntoTheHelpMenu()
    {
        var (session, _) = Start("G14", 40, 40);
        Tick(session);
        OpenMenu(session);
        var menu = session.Menu;
        var system = menu.System;
        var help = system.Items.Rows.ToList().IndexOf(SystemItem.Help);
        Assert.True(help > 0);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        Assert.Equal(1, system.Items.Cursor);
        while (system.Items.Cursor < help)
            session.Press(MenuAction.Down);
        // Help rolls the menu up as closing does, and the help menu's page opens once it has.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Accept));
        Assert.Equal(PauseScrollPhase.Rolling, menu.Scroll.Phase);
        TickUntilMenuClosed(session);
        Assert.Equal(GameMode.Backdrop, session.Mode);
        Assert.NotNull(session.Trek.Backdrop);
        Assert.Equal(SystemMenu.HelpScript, session.Trek.Conversation!.Runner.Script.Id);
        // The help menu's B leaves it, and its page fades out into trek.
        for (var i = 0; i < 5 && session.Trek.Conversation is not null; i++)
            session.Press(MenuAction.Back);
        for (var i = 0; i < 120 && session.Mode == GameMode.Backdrop; i++)
            Tick(session);
        Assert.Equal(GameMode.Trek, session.Mode);
    }

    [Fact]
    public void AHeldDirectionRepeatsTheMenusCursorAfterThirtyTicksThenEveryFive()
    {
        var (session, host) = Start("G14", 40, 40);
        Tick(session);
        OpenMenu(session);
        var items = session.Menu.System.Items;
        var sounds = session.Game.Sounds!.Menu;
        // The press itself moves the cursor; the hold then repeats it on the original's ticks, with the cursor's sound each time.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Down));
        session.Stick.Press(MenuAction.Down);
        host.Cues.Clear();
        var moves = new List<int>();
        for (var tick = 1; tick <= 40; tick++)
        {
            var before = items.Cursor;
            Tick(session);
            if (items.Cursor != before)
                moves.Add(tick);
        }
        Assert.Equal([30, 35, 40], moves);
        Assert.Equal([sounds.Cursor, sounds.Cursor, sounds.Cursor], host.Sounds);
        // Let go, and nothing more repeats.
        session.Stick.Release(MenuAction.Down);
        var cursor = items.Cursor;
        for (var tick = 0; tick < 20; tick++)
            Tick(session);
        Assert.Equal(cursor, items.Cursor);
    }

    [Fact]
    public void TheMenuRemembersTheMemberLastShownInEveryPartyWindowButStartsTheListAfresh()
    {
        var (session, _) = Start("G14", 40, 40);
        var game = session.Game;
        Assert.True(game.AddMember(RecordId.Hex(RecordTypes.Entity, 0x0240)));
        Tick(session);
        // Her joining's journal entry is told on the next trek tick (the user's choice), which A closes.
        Assert.Equal(TrekLoop.NewJournalEntry, session.Trek.Use.Popup?.Body?.Text);
        session.Trek.Use.Accept();
        var menu = session.Menu;
        Assert.Equal(0, menu.Member);

        // C-left on the Party tab shows Brenna; the menu opens on her again, on the inventory's top row.
        session.OpenMenu(PauseTab.Party);
        for (var i = 0; i < 60 && !menu.Taking; i++)
            Tick(session);
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.MemberLeft));
        Assert.Equal(1, menu.Member);
        for (var i = 0; i < 30; i++)
            Tick(session);
        session.Press(MenuAction.Down);
        menu.CloseNow();
        Assert.Equal(SessionStep.MenuClosed, Tick(session));
        Assert.Equal(1, menu.Member);
        session.OpenMenu();
        Assert.Equal((PauseTab.Party, 1, PartyList.Inventory, 0),
            (menu.Tab, menu.Party.Member, menu.Party.List, menu.Party.Inventory.Cursor));
        menu.CloseNow();
        Tick(session);

        // The loot window opens on her too, and keeps her.
        session.OpenLoot(new ItemPile(game.Party.Inventory.Catalog), gold: 0);
        Assert.Equal(1, menu.Window.Menu.Member);
        menu.CloseNow();
        Tick(session);
        Assert.Equal(1, menu.Member);

        // Should she leave, it steps back to Alaron's slot, and keeps that.
        game.Party.Remove(RecordId.Hex(RecordTypes.Entity, 0x0240));
        session.OpenMenu(PauseTab.Party);
        Assert.Equal((0, 0), (menu.Party.Member, menu.Member));
        menu.CloseNow();
        Tick(session);
    }

    [Fact]
    public void TheLootWindowOpensTheMenuLockedOnThePartyTabAndBClosesItToTrek()
    {
        var (session, host) = Start("G14", 40, 40);
        Tick(session);
        var menu = session.Menu;
        session.OpenLoot(new ItemPile(session.Game.Party.Inventory.Catalog), gold: 5);
        Assert.Equal((GameMode.Menu, PauseTab.Party, true), (session.Mode, menu.Tab, menu.TabsLocked));
        Assert.IsType<BarterMenu>(menu.Window);
        // The coins sound a second after the window is built, once the world has darkened (BarterMenu.BarterMenu).
        bool Coins() => host.Cues.Any(c => c is MenuSoundCue { Sound: { Delay: Core.Audio.OneShot.MenuTicks } s } && s.Sequence == session.Game.Sounds!.GoldGained);
        Assert.False(Coins());
        var ticks = 0;
        while (!menu.Scroll.ShowsMenu && ticks++ < 60)
            Tick(session);
        Assert.True(Coins());
        while (!menu.Taking && ticks++ < 120)
            Tick(session);
        Assert.True(menu.Taking);
        // The tabs can't be changed, and B leaves an empty pile at once.
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.TabPrevious));
        Assert.Equal((PauseTab.Party, false), (menu.Tab, menu.Sliding));
        Assert.Equal(PressOutcome.Taken, session.Press(MenuAction.Back));
        TickUntilMenuClosed(session);
        Assert.Equal(GameMode.Trek, session.Mode);
        // Locked tabs aren't remembered.
        Assert.Equal(PauseTab.System, menu.Remembered);
    }
}
