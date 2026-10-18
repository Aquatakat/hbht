using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Actors;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The game session, headless, on NA 1.0's data and a new game: which mode runs, and where each ending leads, the
/// transitions recorded as the runtime would carry them out.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class GameSessionTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private sealed class Host : ITrekHost
    {
        public List<Transition> Transitions { get; } = [];
        public List<string> Said { get; } = [];
        public List<string> Warnings { get; } = [];

        void ITransitionHost.Transition(Transition next) => Transitions.Add(next);

        void ITrekHost.Entered(ChunkEntry? chunk)
        {
        }

        void ITrekHost.Arrived()
        {
        }

        void ITrekHost.ConversationChanged()
        {
        }

        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

        void ITrekHost.OpenLoot(ItemPile loot, int gold)
        {
        }

        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
        {
        }

        void ITrekHost.Changed(LoadedMap map, SceneObject scene)
        {
        }

        void ITrekHost.Say(string text) => Said.Add(text);

        void ITrekHost.Log(string text)
        {
        }

        void ITrekHost.Warn(string text) => Warnings.Add(text);

        void IScriptHost.Cue(ScriptCue cue)
        {
        }
    }

    /// <summary>A new game's session, with the player on the ground at a chunk's point of the Overworld.</summary>
    private (GameSession Session, Host Host) Start(string chunkName = "I08", float x = 16, float z = 62)
    {
        var game = GameState.NewGame(Data.Set);
        var host = new Host();
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0)),
            new VisibleWorld());
        var session = new GameSession(game, host, new ZoneEngine(game, streamer));
        var chunk = streamer.Find(chunkName)!;
        var point = streamer.Origin(chunk) + new Vector3(x, 10, z);
        session.Trek.Enter(chunk);
        streamer.Update(point);
        var ground = Body.SetDown(streamer.World, point, 0.5f, null, near: point.Y - 0.5f).Position;
        var look = new Vector2(0, -1);
        session.Trek.Begin(new PlayerMotion(ground, look), new FollowCamera(ground, look));
        return (session, host);
    }

    private static SessionStep Tick(GameSession session) => session.Tick(() => new PlayerInput(Vector2.Zero), false, true, _ => null);

    [Fact]
    public void TheModeIsTheHighestUpAndTheTickCountsInEveryMode()
    {
        var (session, _) = Start();
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.Equal(SessionStep.Trek, Tick(session));

        // A popup over trek, which runs under it.
        session.Trek.Use.Tell("Something.");
        Assert.Equal(GameMode.Popup, session.Mode);
        Assert.True(session.TrekRuns);
        Assert.Equal(SessionStep.Trek, Tick(session));

        // The menu over it holds the world, and so does a backdrop's page under the menu, until both have gone.
        session.MenuOpened();
        session.BackdropOpened();
        Assert.Equal((GameMode.Menu, true, false), (session.Mode, session.Held, session.TrekRuns));
        var played = session.Game.PlayTime;
        var time = session.Game.Clock.Time;
        Assert.Equal(SessionStep.Held, Tick(session));
        Assert.Equal(played + 1, session.Game.PlayTime);
        Assert.Equal(time, session.Game.Clock.Time);
        session.MenuClosed(PauseExit.Close);
        Assert.Equal(GameMode.Backdrop, session.Mode);
        session.BackdropClosed();
        Assert.Equal(GameMode.Popup, session.Mode);

        // A cinematic is over everything, the menu too.
        var cinematic = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 0));
        session.MenuOpened();
        session.PlayCinematic(cinematic, fromTheatre: false);
        Assert.Equal((GameMode.Cinematic, SessionStep.Cinematic), (session.Mode, Tick(session)));
        Assert.Equal(played + 2, session.Game.PlayTime);
    }

    [Fact]
    public void AScriptEndingInAShopOpensTheKeepersShopAndOneWithoutSaysSo()
    {
        var (session, host) = Start();
        var game = session.Game;
        var shop = Data.Set.All<ShopRecord>(RecordTypes.Shop).First();
        var ending = new ScriptEnding(ScriptEndKind.Shop, shop.Shopkeeper, null, 0, false);

        Transition.CarryOut(game, host, Transition.AfterScript(game, ending, shop.Shopkeeper));
        var opened = Assert.IsType<Transition.OpenShop>(Assert.Single(host.Transitions));
        Assert.Same(shop, opened.Shop.Record);
        Assert.Same(game.Party, opened.Shop.Party);

        // Alaron keeps no shop: the original would crash, and the port says so.
        var none = Assert.IsType<Transition.Say>(Assert.Single(Transition.AfterScript(game, ending, GameState.Alaron)));
        Assert.EndsWith("keeps no shop.", none.Text);
        // Training at someone, and with no keeper.
        Assert.IsType<Transition.OpenTraining>(Assert.Single(Transition.AfterScript(game, ending with { Kind = ScriptEndKind.Train }, GameState.Alaron)));
        Assert.Equal("No one teaches nothing.",
            Assert.IsType<Transition.Say>(Assert.Single(Transition.AfterScript(game, ending with { Kind = ScriptEndKind.Train }, null))).Text);
    }

    [Fact]
    public void RestLapsesTheHoursBeforeWhatFollows()
    {
        var (session, host) = Start();
        var game = session.Game;
        var time = game.Clock.Time;
        var played = game.PlayTime;
        // A camp heal in a camp scene: sixteen hours.
        Transition.CarryOut(game, host, Transition.AfterScript(game, new ScriptEnding(ScriptEndKind.Camp, null, null, 16, false), null));

        Assert.Equal(new Transition.Rest(16), Assert.Single(host.Transitions));
        Assert.Equal((time + 16 * Core.Calendar.GameClock.Hour) % Core.Calendar.GameClock.Year, game.Clock.Time);
        Assert.Equal(played, game.PlayTime);
        // Deciding alone passes nothing.
        Transition.AfterScript(game, new ScriptEnding(ScriptEndKind.Camp, null, null, 8, false), null);
        Assert.Equal((time + 16 * Core.Calendar.GameClock.Hour) % Core.Calendar.GameClock.Year, game.Clock.Time);
    }

    [Fact]
    public void EveryBattleSiteBuildsOneBattleSetup()
    {
        // A monster party with no dialogue, used: it fights.
        var placements = MapPlacements.For(Data.Set);
        var overworld = RecordId.Decimal(RecordTypes.MapSet, 0);
        var (id, map, party) = Data.Of<MonsterPartyObject>().First(p => p.Object is { Dialogue: null, Home: not null }
            && placements.First(p.Map.Id) is { Set.Id: var set } && set == overworld);
        var place = placements.First(map.Id)!;
        var (session, host) = Start(place.Chunk.Name, party.Position.X, party.Position.Z);
        var loaded = session.Trek.Chunks.All.Single(c => c.Map.Record.Id == map.Id).Map;
        Assert.Contains(session.Trek.Wanderers.All, w => w.Party == party);
        ((IMapObjectHost)session.Trek).Talk(loaded, party);
        // Trek cues it, fading out, rather than handing it on.
        Assert.Empty(host.Transitions);
        var fight = session.Trek.CuedBattle!;
        Assert.True(session.Trek.CutSceneFade.Fading);
        var here = new ChunkRef(place.Set.Id, place.Chunk.Row, place.Chunk.Column);
        Assert.Equal(new BattleSetup(BattleSource.MonsterParty, here, place.Chunk.Terrain, id, party.Enemies, null, false, true, party.Used?.Flag)
            with { Enemies = fight.Enemies, Size = (party.MinSize, party.MaxSize) }, fight);
        Assert.Equal(party.Enemies, fight.Enemies);

        // A wanderer catching the party is the same, as an ambush.
        var ambush = BattleSetup.WithParty(BattleSource.Wanderer, id, party, here, place.Chunk.Terrain, ambush: true);
        Assert.Equal(fight with { Source = BattleSource.Wanderer, Ambush = true }, ambush);

        // A camp ambush where the player stands, in G14 (terrain 0): no enemies until the camp encounters are imported, and
        // no fleeing. Two of the Gwernia recruits make ambushes possible; each camp that isn't one heals and cues the scene.
        var (camping, campHost) = Start("G14", 40, 40);
        foreach (var joined in new[] { 89, 90 })
            camping.Game.Flags.TrySet(RecordId.Decimal(RecordTypes.Flag, joined), true);
        // A seed whose d100 is 1 ambushes whatever the chance.
        var seed = Enumerable.Range(0, 10000).Select(s => (uint)s * 2654435761u).First(s => new Lcg(s).RollD(1, 100) == 1);
        camping.Game.Random.World.Seed = seed;
        Assert.Equal(CampOutcome.Ambushed, camping.Trek.Camp());
        var camp = camping.Trek.CuedBattle!;
        var g14 = new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 7, 14);
        Assert.Equal((BattleSource.Camp, g14, 0, null, true, false), (camp.Source, camp.Place, camp.Terrain, camp.Party, camp.Ambush, camp.CanFlee));
        Assert.Empty(camp.Enemies);
        var game = session.Game;

        // A script's, where it ends, and in a conversation with a party, against it under its flag C.
        var scripted = BattleSetup.Scripted([party.Entity], 3, 1, 2, canFlee: true, RecordId.Decimal(RecordTypes.Flag, 978));
        var ending = new ScriptEnding(ScriptEndKind.Battle, null, scripted, 0, false);
        var battle = Assert.IsType<Transition.StartBattle>(Assert.Single(Transition.AfterScript(game, ending, null, (here, 4)))).Setup;
        Assert.Equal(scripted with { Place = here, Terrain = 4 }, battle);
        var talking = Assert.IsType<Transition.StartBattle>(Assert.Single(Transition.AfterScript(game, ending, null, (here, 4), (id, party)))).Setup;
        Assert.Equal((BattleSource.Script, id, party.Enemies, party.BattleFlag, 3), (talking.Source, talking.Party, talking.Enemies, talking.EncounterFlag, talking.Arena));
    }

    [Fact]
    public void ABattleBeginsInItsArenaPausesAndFleesToTheFarthestBreadcrumb()
    {
        var (session, host) = Start();
        var trek = session.Trek;
        var start = trek.Player.Sphere.Position;
        // He stands 30 away for a while, so a breadcrumb is kept there, then comes back.
        var away = Body.SetDown(trek.Chunks.World, start + new Vector3(0, 0, -30), 0.5f, null).Position;
        trek.Player.Sphere.Position = away;
        for (var i = 0; i < Breadcrumbs.Ticks + 1; i++)
            Tick(session);
        trek.Player.Sphere.Position = start;
        Assert.Contains(trek.Breadcrumbs.All, c => Vector3.Distance(c.Local, away - trek.Chunks.Origin(trek.Chunks.ChunkAt(away)!)) < 1);

        // Two rats in arena 1, as a monster party used.
        var rat = RecordId.Hex(RecordTypes.Entity, 0x026E);
        var (id, _, rats) = Data.Of<MonsterPartyObject>().First(p => p.Object is { Dialogue: null, MinSize: 2, MaxSize: 2 }
            && p.Object.Enemies.All(e => e.Entity == rat));
        var (place, terrain) = trek.Here();
        session.CueBattle(BattleSetup.WithParty(BattleSource.MonsterParty, id, rats, place, terrain, ambush: false) with { Arena = 1 });
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.BattleCued; i++)
            step = Tick(session);
        Assert.Equal(SessionStep.BattleCued, step);
        session.BeginBattle();
        var began = Assert.IsType<Transition.StartedBattle>(Assert.Single(host.Transitions));
        var battle = session.Battle!;
        Assert.Same(battle, began.Battle);
        Assert.Equal(GameMode.Combat, session.Mode);
        Assert.False(session.TrekRuns);
        Assert.Equal((1, "A03"), (battle.Arena.Number, battle.Arena.Chunk.Name));
        // Alaron on the party's first spawn of set 4, the rats on the enemies', the grid taken under each.
        Assert.Equal([rat, rat], battle.Enemies);
        Assert.Equal((1, 2, true), (battle.PartySize, battle.EnemiesOnField, battle.CanFlee));
        var alaron = battle.Slots[0]!;
        Assert.Equal(GameState.Alaron, alaron.Entity);
        var spawn = battle.Arena.Point("good4a")!.Position;
        Assert.Equal(new Vector2((int)spawn.X, (int)spawn.Z), alaron.Cell);
        Assert.InRange(alaron.Direction, 1, 3);
        Assert.All(battle.Combatants.Where(c => c.Hostile), c => Assert.InRange(c.Direction, 5, 7));
        Assert.Equal(battle.Arena.Point("bad4b")!.Position.X, battle.Slots[2]!.Cell.X, 0.99f);
        Assert.Equal(battle.Leader, battle.Slots[1]);
        Assert.All(battle.Combatants, c => Assert.Equal(ArenaGrid.Taken, battle.Grid[(int)c.Cell.X, (int)c.Cell.Y] & ArenaGrid.Taken));
        Assert.Equal((76, 77), (battle.Grid.Width, battle.Grid.Depth));
        Assert.Equal(battle.Camera, session.Arena!.Camera);

        // It fades in over a second, then the turn is Alaron's, or the rats' pass on to him.
        for (var i = 0; i < 60; i++)
            Assert.Equal((SessionStep.Combat, CombatState.Intro), (Tick(session), battle.State));
        for (var i = 0; i < 10 && battle.State != CombatState.Turn; i++)
            Tick(session);
        Assert.Equal((CombatState.Turn, alaron), (battle.State, battle.Current));

        // Start: the menu, from a turn, with the Party tab and Flee, two frames on.
        host.Transitions.Clear();
        Assert.True(battle.Pause());
        for (var i = 0; i < 5; i++)
            Tick(session);
        Assert.Equal(new Transition.PauseBattle(PartyTab: true, CanFlee: true), Assert.Single(host.Transitions));
        session.MenuOpened();
        Assert.Equal(GameMode.Menu, session.Mode);
        session.MenuClosed(PauseExit.Flee);
        Assert.Equal((CombatState.LeaveFled, true), (battle.State, battle.Fled));
        Assert.Equal(SessionStep.CombatOver, Tick(session));

        // The arena goes, and trek comes back at the breadcrumb farthest from where the battle began.
        host.Transitions.Clear();
        session.EndBattle();
        Assert.Null(session.Battle);
        Assert.Null(session.Arena);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.Equal([typeof(Transition.EndBattle), typeof(Transition.ResumeTrek)], host.Transitions.Select(t => t.GetType()));
        Assert.Equal(away, trek.Player.Sphere.Position);
        Assert.All(trek.Breadcrumbs.All, c => Assert.Equal(trek.Breadcrumbs.All[0], c));
        Assert.NotEmpty(trek.Wanderers.All);
    }

    [Fact]
    public void ACutSceneBeginsOnItsStageAndTrekComesBackAfter()
    {
        var (session, host) = Start();
        var ambush = Data.Set.Get<DialogueRecord>(RecordId.Parse("aidyn:dialogue/0x0365"));
        var (_, map, dialogue) = Data.Of<DialogueObject>().First(d => d.Object.Script == ambush.Id && d.Object.Stage is not null);
        session.Cue(ambush, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);

        // Trek fades out, then says the cut-scene takes over; it begins once drawn.
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.CutSceneCued; i++)
            step = Tick(session);
        Assert.Equal(SessionStep.CutSceneCued, step);
        session.BeginCutScene();
        var began = Assert.IsType<Transition.StartCutScene>(Assert.Single(host.Transitions));
        Assert.Same(session.Scene, began.Scene);
        Assert.Same(session.Stage, began.Stage);
        Assert.Equal(GameMode.CutScene, session.Mode);
        Assert.Equal(SessionStep.CutScene, Tick(session));

        host.Transitions.Clear();
        session.EndCutScene();
        Assert.Null(session.Scene);
        Assert.Null(session.Stage);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.IsType<Transition.EndCutScene>(host.Transitions[0]);
        Assert.IsType<Transition.ResumeTrek>(host.Transitions[1]);
        Assert.True(session.Trek.CutSceneFade.Fading);
    }

    [Fact]
    public void TheMenuReopensOnceTrekHasFadedInAfterTheCreditsAndTheTheatre()
    {
        var (session, host) = Start();
        session.MenuOpened();
        session.MenuClosed(PauseExit.Credits);
        Assert.IsType<Transition.PlayCredits>(Assert.Single(host.Transitions));
        Assert.Equal((GameMode.Credits, SessionStep.Credits), (session.Mode, Tick(session)));
        session.CreditsEnded();
        Assert.Equal(new Transition.ReopenMenu(Theatre: false), TickUntilReopened(session, host));

        // The Theatre's cinematic: its done flag is set, and the menu reopens on the Theatre's page.
        var cinematic = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 2));
        session.MenuClosed(PauseExit.Theatre(cinematic));
        Assert.Equal(new Transition.PlayCinematic(cinematic), host.Transitions[^1]);
        session.CinematicEnded();
        Assert.True(session.Game.Flags.IsSet(cinematic.DoneFlag));
        Assert.Equal(new Transition.ReopenMenu(Theatre: true), TickUntilReopened(session, host));
    }

    private static Transition TickUntilReopened(GameSession session, Host host)
    {
        host.Transitions.Clear();
        for (var i = 0; i < 100 && host.Transitions.Count == 0; i++)
            Tick(session);
        Assert.False(session.Trek.CutSceneFade.Fading);
        return Assert.Single(host.Transitions);
    }

    [Fact]
    public void TheMenusExitsCampOrStartTheHelpMenu()
    {
        var (session, host) = Start("G14", 40, 40);
        session.MenuClosed(PauseExit.Camp);
        Assert.IsType<Transition.Camp>(host.Transitions[^1]);

        session.MenuClosed(PauseExit.Help);
        Assert.Equal((GameMode.Conversation, true), (session.Mode, session.Trek.Conversation!.ShowsBackdrop));
        Assert.Equal(SystemMenu.HelpScript, session.Trek.Conversation.Runner.Script.Id);
    }

    [Fact]
    public void ANewGameStartsAsItsRecordSays()
    {
        // The importer's facts from 1.0's code: the Overworld's portal 0, the body made 10 up, Alaron alone with 1 gold.
        var start = GameSession.NewGame(Data.Set);
        Assert.Equal((new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 9, 8), new Vector2(16, 62), 10f, GameState.Alaron, 1u),
            (start.Chunk, start.Position, start.Height, start.Leader, start.Gold));
        var (set, chunk, at) = GameSession.Arrival(Data.Set, start);
        Assert.Equal(("Overworld", "I08", new Vector3(16, 10, 62)), (set.Label, chunk, at));

        // The party it builds, and the player's body, the leader's actor: BORG7_Alaron, as the title screen sets it.
        var (session, _) = Start();
        Assert.Equal(GameState.Alaron, Assert.Single(session.Game.Party.Members).Entity);
        Assert.Equal(1u, session.Game.Party.Gold);
        Assert.Equal(RecordId.Hex(RecordTypes.Actor, 0x25F9), session.PlayerActor);
    }

    [Fact]
    public void WhereThePlayerStandsIsNotedInHisChunk()
    {
        var (session, _) = Start();
        var (place, outdoors) = session.Where();
        Assert.Equal((new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 9, 8), true), (place, outdoors));
        Assert.True(session.NotePlace());
        var saved = session.Game.Place!;
        Assert.Equal("I08", saved.Chunk);
        Assert.Equal(16, saved.Position.X, 3);
        Assert.Equal(62, saved.Position.Z, 3);
        Assert.Equal(session.Trek.Player.Heading, saved.Heading);
    }
}
