using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// When a journal entry's notice comes, which is when 1.0 brings the flag network up to date after its flag is set: at once
/// after a map object's write, as trek comes back where loading the chunks reads a derived flag, and otherwise not until
/// something reads one (docs/systems/flags.md#when-the-network-comes-up-to-date).
/// </summary>
public sealed partial class GameSessionTests
{
    /// <summary>A new game's session, with the player set down at a point of a chunk of any map set.</summary>
    private (GameSession Session, Host Host) StartIn(int mapSet, string chunkName, float x, float z, float y = 10)
    {
        var game = GameState.NewGame(Data.Set);
        var host = new Host();
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, mapSet));
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), set, new VisibleWorld());
        var start = GameStart.OfNewGame(Data.Set) with { Set = set, Chunk = chunkName, Local = new Vector3(x, y, z) };
        var session = GameSession.Start(game, host, new ZoneEngine(game, streamer), start, menuHost: host);
        return (session, host);
    }

    /// <summary>The ticks until the new journal entry's notice is told, or −1 if it isn't within <paramref name="most"/>, its audio rendered.</summary>
    private static int TicksToJournalNotice(GameSession session, int most = 300)
    {
        for (var i = 1; i <= most; i++)
        {
            Hear(session);
            if (session.Trek.Use.Popup?.Body?.Text == TrekLoop.NewJournalEntry)
                return i;
        }
        return -1;
    }

    private bool NewEntryWaiting(GameSession session) => session.Game.Flags.Peek(session.Game.Flags.Handle(session.Game.StateFlagsRecord.NewJournalEntry));

    private IReadOnlyList<JournalItem> OpenJournal(GameState game)
    {
        var menu = new WorldMenu(Data.Set, game, new TextLayout(Data.Set.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0))), null, outdoors: false);
        menu.MoveCursor(1);
        menu.Accept();
        return menu.Journal!.Items;
    }

    [Fact]
    public void TheWizardsSchoolsEntryIsToldTheTickAfterTheFirstStepInsideItsDoor()
    {
        // JundarInteriors A01: coming in from Jundar (entryL1) puts him at point 68, 6.13 from the SetFlag trigger (object 5,
        // radius 5) that sets 216, "We are granted admittance to the Wizard's School."
        var school = Data.Set.Get<MapRecord>(RecordId.Hex(RecordTypes.Map, 0x1890));
        var arrival = school.Objects[68].Position;
        var trigger = (TriggerObject)school.Objects[5];
        var (session, _) = StartIn(11, "A01", arrival.X, arrival.Z);
        var flags = session.Game.Flags;
        var admitted = RecordId.Decimal(RecordTypes.Flag, 216);
        // A new game's start sets him down from on high, on the top floor: put him on the ground floor at the arrival point.
        var player = session.Trek.Player;
        var origin = session.Trek.Chunks.MapAt(player.Sphere.Position)!.Origin;
        player.Sphere.Position = origin + arrival + new Vector3(0, 0.5f, 0);
        player.Sphere.Velocity = Vector3.Zero;
        Assert.Equal(-1, TicksToJournalNotice(session, 60));
        Assert.False(flags.IsSet(admitted));
        Assert.InRange(Vector3.Distance(player.Sphere.Position - origin, trigger.Position), trigger.Radius + 0.5f, trigger.Radius + 2);

        // One step in, 1.5 toward the trigger: its write is followed by the read of 5626, so the entry waits at once and is told
        // on the next tick, as in 1.0.
        var toward = (trigger.Position - arrival) with { Y = 0 };
        player.Sphere.Position += 1.5f * Vector3.Normalize(toward);
        Tick(session);
        Assert.True(flags.IsSet(admitted));
        Assert.True(NewEntryWaiting(session));
        Assert.Equal(1, TicksToJournalNotice(session));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLettersEntryWaitsInTheCaveUnderPortSaiidUntilItsScrollIsTakenOrItsLeft(bool takeTheScroll)
    {
        // PortSaiid E01, the cave: the letter (object 1, its flag B 294) is a backdrop, script 0x041F, whose node 1 sets 295.
        var cave = Data.Set.Get<MapRecord>(RecordId.Hex(RecordTypes.Map, 0x12D8));
        var letter = (DialogueObject)cave.Objects[1];
        var (session, _) = StartIn(2, "E01", letter.Position.X - 0.5f, letter.Position.Z);
        var trek = session.Trek;
        var flags = session.Game.Flags;
        Tick(session);
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        trek.Use.Use(map, letter);
        Assert.Equal(GameMode.Backdrop, session.Mode);
        for (var i = 0; i < 1000 && trek.Conversation is { } reading; i++)
        {
            // Its pages are a menu of seasons, each read and back to the menu; the last choice, Leave, ends it.
            if (session.Backdrop is { Alpha: 255 } && reading.Runner.Page is { } page)
            {
                DialogueBox.Advance(reading.Runner, Math.Max(0, page.Choices.Count - 1));
                trek.Refresh();
            }
            Tick(session);
        }
        Assert.Null(trek.Conversation);
        for (var i = 0; i < 60 && session.Mode != GameMode.Trek; i++)
            Tick(session);
        Assert.Equal(GameMode.Trek, session.Mode);

        // Trek is built again in the cave, whose objects' gates are all plain flags, and nothing in it reads a derived one: the
        // entry waits, unnoticed, as 1.0's does.
        Assert.True(flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 295)));
        Assert.False(NewEntryWaiting(session));
        Assert.Equal(-1, TicksToJournalNotice(session));

        if (takeTheScroll)
        {
            // The scroll in the same cave (object 7, a custom container) writes its Used and opened flags and reads 5626
            // (MapObjectUse.Loot, 0x800105ec): the entry waits at once, as on the cartridge.
            trek.Use.Use(map, (ContainerObject)cave.Objects[7]);
            Assert.True(NewEntryWaiting(session));
            return;
        }

        // Leaving through its door (object 0) loads the Overworld round Saiid, whose gates read derived flags: told as the
        // teleport's fade ends.
        trek.Use.Use(map, (TeleporterObject)cave.Objects[0]);
        Assert.InRange(TicksToJournalNotice(session), 2, 120);
        Assert.NotEqual(RecordId.Decimal(RecordTypes.MapSet, 2), trek.Chunks.MapSet.Id);
    }

    [Fact]
    public void KitaraksEntryWaitsAfterHisBattleUntilTheJournalOrSomethingElseReadsADerivedFlag()
    {
        // GoblinCamp C01: Kitarak's party (object 3), whose boss flag, 154, winning sets (Combat.SetKillFlags, 0x80067860), after
        // the reads the battle's end makes.
        var room = Data.Set.Get<MapRecord>(RecordId.Hex(RecordTypes.Map, 0x35A5));
        var kitarak = (MonsterPartyObject)room.Objects[3];
        var (session, _) = StartIn(1, "C01", kitarak.Position.X + 2, kitarak.Position.Z + 2);
        var trek = session.Trek;
        var flags = session.Game.Flags;
        Tick(session);
        var (place, terrain) = trek.Here();
        session.CueBattle(BattleSetup.WithParty(BattleSource.MonsterParty, MapRecord.VoxelId(room.Id, kitarak.Index), kitarak, place, terrain,
            ambush: false));
        Assert.Equal(SessionStep.BattleCued, TickUntil(session, SessionStep.BattleCued));
        session.BeginBattle();
        var battle = session.Battle!;
        for (var i = 0; i < 5000 && !battle.ForceWin(); i++)
            Tick(session);
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        session.EndBattle();
        session.MenuClosed(PauseExit.Close);
        Assert.Equal(GameMode.Trek, session.Mode);

        // Trek comes back in his room, where loading reads no derived flag and no tangible object is near: the entry waits,
        // unnoticed, as 1.0's does, until the chest's flags are written or he leaves.
        var beat = RecordId.Decimal(RecordTypes.Flag, 154);
        Assert.True(flags.IsSet(beat));
        Assert.False(NewEntryWaiting(session));
        Assert.Equal(-1, TicksToJournalNotice(session));

        // The journal lists it, as 1.0's reads each entry's own flag (Journal::Load, 0x8005263c), but not in the newest's colour.
        // Reading entry 32's, derived flag 5017, brings the network up to date, which adds it: the notice is told once the menu
        // has closed, and it's the newest when the journal next opens.
        var listed = Assert.Single(OpenJournal(session.Game), e => e.Text.StartsWith("We killed the goblin king Kitarak"));
        Assert.False(listed.Newest);
        Assert.True(NewEntryWaiting(session));
        Assert.Equal(RecordId.Decimal(RecordTypes.Journal, 14), flags.NewestJournalEntry);
        Assert.Equal(1, TicksToJournalNotice(session));
        Assert.True(Assert.Single(OpenJournal(session.Game), e => e.Text.StartsWith("We killed the goblin king Kitarak")).Newest);
    }

    [Fact]
    public void TheJournalListsAnEntryTheFlagsHaventAddedYetAndAddsItAsItReadsTheLighthousesEntry()
    {
        // The lighthouse lit (286, which derived 5017, entry 32's own flag, reads) and its entry added; then Kitarak beaten, 154
        // set plainly, as a battle's end sets it, with nothing read since.
        var game = GameState.NewGame(Data.Set);
        var flags = game.Flags;
        flags.Set(RecordId.Decimal(RecordTypes.Flag, 286), true);
        Assert.True(flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 5017)));
        Assert.Equal([RecordId.Decimal(RecordTypes.Journal, 32)], flags.JournalOrder);
        flags.Set(flags.Handle(game.StateFlagsRecord.NewJournalEntry), false);
        flags.NewestJournalEntry = null;
        flags.Set(RecordId.Decimal(RecordTypes.Flag, 154), true);

        // Reading entry 32's flag brings the network up to date as the journal is listed, which adds Kitarak's entry: listed
        // after the lighthouse's, as it came after, but not in the newest's colour, which the next opening shows.
        // A blank line comes first and last.
        var items = OpenJournal(game).Skip(1).SkipLast(1).ToList();
        Assert.Equal(2, items.Count);
        Assert.StartsWith("Lighthouse light is back on!", items[0].Text);
        Assert.StartsWith("We killed the goblin king Kitarak", items[1].Text);
        Assert.False(items[1].Newest);
        Assert.Equal([RecordId.Decimal(RecordTypes.Journal, 32), RecordId.Decimal(RecordTypes.Journal, 14)], flags.JournalOrder);
        Assert.True(flags.Peek(flags.Handle(game.StateFlagsRecord.NewJournalEntry)));
        Assert.True(OpenJournal(game).Single(i => i.Text.StartsWith("We killed")).Newest);
    }

    private static readonly short[] Mixed = new short[2 * 735];

    /// <summary>A tick with its audio rendered, so the map's sounds end as they do in the runtime.</summary>
    private static void Hear(GameSession session)
    {
        session.Zone.Audio.Step();
        Tick(session);
        session.Zone.Audio.Render(Mixed);
    }

    [Fact]
    public void YerazasFirstConversationsEntryIsToldAsTheBirdsongNearHerEndsAfterIt()
    {
        // Overworld C08, Erromon's street: Yeraza (object 31, there by day) and her first conversation, script 0x03C5, whose
        // node 27 sets 135, "I met a Mirari woman on the street today". Nothing in it reads a derived flag after; but she stands
        // within 30 of sound object 142, a repeating birdsong gated by the day (derived 5284), which 1.0 reads every frame it
        // isn't playing (Audio.UpdateSound, 0x8004ea10).
        var street = Data.Set.Get<MapRecord>(RecordId.Hex(RecordTypes.Map, 0x0EFA));
        var yeraza = (MonsterPartyObject)street.Objects[31];
        var birdsong = (SoundObject)street.Objects[142];
        var (session, _) = StartIn(0, "C08", yeraza.Position.X + 1, yeraza.Position.Z);
        var trek = session.Trek;
        var flags = session.Game.Flags;
        for (var i = 0; i < 40; i++)
            Hear(session);
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        Assert.True(Vector3.Distance(trek.Player.Sphere.Position - map.Origin, birdsong.Position) < birdsong.Radius);
        trek.Use.Use(map, yeraza);
        // Through it to node 27, by the castle's question (the third answer) and the king's (the first).
        for (var i = 0; i < 5000 && trek.Conversation is { } talk; i++)
        {
            if (talk.Runner is { Status: ScriptStatus.Showing, Page: not null } && trek.Press(MenuAction.Accept) == PressOutcome.Taken
                && trek.Box.Layout is { Choices.Count: > 0 } && talk.Runner.Page is { Choices.Count: > 0 } menu)
            {
                var answer = menu.Choices.Select((c, k) => (c.Node, k)).FirstOrDefault(c => c.Node is 17 or 31).k;
                for (var k = 0; k < answer; k++)
                    trek.Press(MenuAction.Down);
                trek.Press(MenuAction.Accept);
            }
            Hear(session);
        }
        Assert.Null(trek.Conversation);
        Assert.True(flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 135)));

        // Told within a second or two of its end, once the birdsong stops; reading the gate only when it needs starting, as the
        // port did, waited for the next song, about 12 seconds on.
        var ticks = 0;
        for (; ticks < 900 && trek.Use.Popup?.Body?.Text != TrekLoop.NewJournalEntry; ticks++)
            Hear(session);
        Assert.InRange(ticks, 1, 120);
    }

    [Fact]
    public void YerazasDinnerEntryIsToldAsTrekFadesInAfterIt()
    {
        // Erromon A11: the dinner, script 0x03E1 from the campfire (object 3), sets 192 on its last node. Trek is built again in
        // A11, whose scene objects 15–17 and 25 are gated by derived flag 5515, so loading it brings the network up to date, as
        // 1.0's does, and the notice comes once the fade-in is over.
        var (session, _) = StartIn(7, "A11", 8, 8);
        Tick(session);
        PlayCutScene(session, 0x03E1);
        var scene = session.Scene!;
        for (var i = 0; i < 100_000 && !scene.Over; i++)
        {
            if (scene.Accepts && scene.Page is { } page)
            {
                if (page.Choices.Count > 0 && scene.Runner.Wait != ScriptWait.Player)
                    scene.Press(() => scene.Runner.Choose(0));
                else
                    scene.Press(scene.Runner.Continue);
            }
            Tick(session);
        }
        Assert.True(scene.Over);
        Assert.True(session.Game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 192)));
        Assert.False(NewEntryWaiting(session));
        session.EndCutScene();
        Assert.True(NewEntryWaiting(session));
        // The fade-in takes 30 ticks, at 1/30 a tick; the notice waits for it.
        Assert.Equal(31, TicksToJournalNotice(session));
    }
}
