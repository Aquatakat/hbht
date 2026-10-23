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
public sealed partial class GameSessionTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private sealed class Host : ITrekHost, IPauseMenuHost
    {
        public List<Transition> Transitions { get; } = [];
        public List<string> Warnings { get; } = [];
        /// <summary>The pause menu's cues, its sounds and the scroll's steps, in order.</summary>
        public List<PauseMenuCue> Cues { get; } = [];
        /// <summary>The paths the menu's Save asked to write.</summary>
        public List<string> Saved { get; } = [];

        void IPauseMenuHost.Cue(PauseMenuCue cue) => Cues.Add(cue);

        string? IPauseMenuHost.Save(string path)
        {
            Saved.Add(path);
            return null;
        }

        /// <summary>The menu sounds cued so far, by sequence, in order.</summary>
        public List<RecordId> Sounds => Cues.OfType<MenuSoundCue>().Select(c => c.Sound.Sequence).ToList();

        void ITransitionHost.Transition(Transition next) => Transitions.Add(next);

        void ITrekHost.Arrived()
        {
        }

        public int ConversationChanges { get; private set; }

        void ITrekHost.ConversationChanged() => ConversationChanges++;

        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
        {
        }

        void ITrekHost.Changed(LoadedMap map, SceneObject scene)
        {
        }


        void ITrekHost.Log(string text)
        {
        }

        void ITrekHost.Warn(string text) => Warnings.Add(text);

        void IScriptHost.Cue(ScriptCue cue)
        {
        }
    }

    /// <summary>A new game's session, started as the runtime starts one (<see cref="GameSession.Start"/>), with the player set down at a chunk's point of the Overworld.</summary>
    private (GameSession Session, Host Host) Start(string chunkName = "I08", float x = 16, float z = 62)
    {
        var game = GameState.NewGame(Data.Set);
        var host = new Host();
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), set, new VisibleWorld());
        var start = GameStart.OfNewGame(Data.Set) with { Set = set, Chunk = chunkName, Local = new Vector3(x, 10, z) };
        var session = GameSession.Start(game, host, new ZoneEngine(game, streamer), start, menuHost: host);
        return (session, host);
    }

    /// <summary>A tick with no input, playing by the original's settings unless others are given.</summary>
    private static SessionStep Tick(GameSession session, SessionSettings? settings = null) =>
        session.Tick(settings ?? SessionSettings.Original, () => new PlayerInput(Vector2.Zero), false, _ => null);

    [Fact]
    public void ASettingChangedBetweenTwoTicksTakesEffectOnTheNextTickAndNotBefore()
    {
        var game = GameState.NewGame(Data.Set);
        var host = new Host();
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), set, new VisibleWorld());
        var particles = new Core.Particles.ParticleHead(capacity: Core.Particles.WeatherParticles.PoolCapacity);
        var zone = new ZoneEngine(game, streamer, particles: particles);
        var start = GameStart.OfNewGame(Data.Set) with { Set = set, Chunk = "G14", Local = new Vector3(40, 10, 40) };
        var settings = new Settings();
        // What it starts with it plays by at once, before its first tick, as the runtime's sky and audio are built.
        var session = GameSession.Start(game, host, zone, start, settings, host, playing: SessionSettings.Original with { MusicVolume = 40 });
        (float Pace, Core.Sky.SkyPlacement Sky, int Music, float Rain) Playing() =>
            (session.Trek.Player.Pace, session.Sky.Placement, zone.Audio.Settings.MusicVolume, zone.Precipitation!.Density);
        Assert.Equal((1f, Core.Sky.SkyPlacement.Original, 40, 1f), Playing());
        Tick(session);
        Assert.Equal((1f, Core.Sky.SkyPlacement.Original, 65, 1f), Playing());

        // The options menu changes the player's settings, which the core doesn't read: nothing changes until a tick is handed them.
        settings.BriskPace = true;
        settings.SkyPlacement = Core.Sky.SkyPlacement.Projected;
        settings.MusicVolume = 30;
        settings.PrecipitationDensity = 4;
        settings.Notify();
        var next = SessionSettings.From(settings);
        Assert.Equal((1f, Core.Sky.SkyPlacement.Original, 65, 1f), Playing());
        Assert.Equal(SessionSettings.Original, session.Settings);
        Tick(session, next);
        Assert.Equal((SessionSettings.BriskPace, Core.Sky.SkyPlacement.Projected, 30, 4f), Playing());
        Assert.Equal(next, session.Settings);
        Assert.Equal(4 * Core.Particles.WeatherParticles.OriginalPool, particles.MaxParticles);

        // And back again on the tick after the original's.
        Tick(session);
        Assert.Equal((1f, Core.Sky.SkyPlacement.Original, 65, 1f), Playing());
    }

    [Fact]
    public void TheModeIsTheHighestUpAndTheTickCountsInEveryMode()
    {
        // Away from the new game's arrival, where the opening's dialogue object cues its cut-scene on the first tick.
        var (session, host) = Start("G14", 40, 40);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.Equal(SessionStep.Trek, Tick(session));

        // A popup over trek, which runs under it; Start can't open the menu over it.
        session.Trek.Use.Tell("Something.");
        Assert.Equal(GameMode.Popup, session.Mode);
        Assert.True(session.TrekRuns);
        Assert.Equal(SessionStep.Trek, Tick(session));
        session.OpenMenu();
        Assert.Equal((GameMode.Popup, false), (session.Mode, session.MenuOpen));
        Assert.Empty(host.Transitions);
        session.Trek.Use.Back();
        Assert.Equal(GameMode.Trek, session.Mode);

        // Start opens the menu, which holds the world: the tick counts, the clock doesn't.
        session.OpenMenu();
        Assert.Equal(new Transition.OpenMenu(), Assert.Single(host.Transitions));
        Assert.Equal((GameMode.Menu, true, false), (session.Mode, session.Held, session.TrekRuns));
        var played = session.Game.PlayTime;
        var time = session.Game.Clock.Time;
        Assert.Equal(SessionStep.Held, Tick(session));
        Assert.Equal(played + 1, session.Game.PlayTime);
        Assert.Equal(time, session.Game.Clock.Time);

        // Its Help: the help menu's page, which the session makes and steps, holds the world until it has faded out.
        session.MenuClosed(PauseExit.Help);
        Assert.False(session.MenuOpen);
        Assert.Equal((GameMode.Backdrop, true, false), (session.Mode, session.Held, session.TrekRuns));
        var page = session.Backdrop!;
        var conversation = session.Trek.Conversation!;
        Assert.Equal(SystemMenu.HelpScript, conversation.Runner.Script.Id);
        Assert.NotNull(page.Layout);
        // It fades in over a few ticks, held.
        for (var i = 0; i < 10; i++)
            Assert.Equal(SessionStep.Held, Tick(session));
        Assert.Equal(255, page.Alpha);
        // B takes the last choice, Exit Help, until the script ends: the page fades out over a few ticks, held still, then trek runs.
        for (var i = 0; i < 10 && session.Trek.Conversation is not null; i++)
        {
            DialogueBox.Advance(conversation.Runner, conversation.Runner.Page!.Choices.Count - 1);
            session.Trek.Refresh();
        }
        Assert.Null(session.Trek.Conversation);
        Assert.True(page.Closing);
        Assert.Equal((GameMode.Backdrop, true), (session.Mode, session.Held));
        var ticks = 0;
        for (; ticks < 30 && session.Mode == GameMode.Backdrop; ticks++)
            Assert.Equal(SessionStep.Held, Tick(session));
        Assert.InRange(ticks, 2, 10);
        Assert.Equal((GameMode.Trek, null), (session.Mode, session.Backdrop));

        // A cinematic is over everything, the menu too.
        host.Transitions.Clear();
        session.OpenMenu();
        var cinematic = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 0));
        session.PlayCinematic(cinematic, fromTheatre: false);
        played = session.Game.PlayTime;
        Assert.Equal((GameMode.Cinematic, SessionStep.Cinematic), (session.Mode, Tick(session)));
        Assert.Equal(played + 1, session.Game.PlayTime);
    }

    [Fact]
    public void TheMenuOpensOnTheSessionsOwnTransitionsAndClosesOnItsWord()
    {
        var (session, host) = Start();
        var game = session.Game;
        var shop = Data.Set.All<ShopRecord>(RecordTypes.Shop).First();

        // A script's shop, carried out by trek, opens the menu in its barter state; the session knows without being told.
        session.Trek.CarryOut(Transition.AfterScript(game, new ScriptEnding(ScriptEndKind.Shop, shop.Shopkeeper, null, 0, false), shop.Shopkeeper));
        Assert.IsType<Transition.OpenShop>(Assert.Single(host.Transitions));
        Assert.Equal((GameMode.Menu, true), (session.Mode, session.MenuOpen));
        session.MenuClosed(PauseExit.Close);
        Assert.Equal((GameMode.Trek, false), (session.Mode, session.MenuOpen));

        // So does a container's loot, as using one would open it.
        host.Transitions.Clear();
        session.OpenLoot(new ItemPile(game.Party.Inventory.Catalog), 3);
        var loot = Assert.IsType<Transition.OpenLoot>(Assert.Single(host.Transitions));
        Assert.Equal(3, loot.Gold);
        Assert.True(session.MenuOpen);
        session.MenuClosed(PauseExit.Close);
        Assert.False(session.MenuOpen);

        // Start opens it on a tab; through a cut-scene's fade the press is dead.
        host.Transitions.Clear();
        session.OpenMenu(PauseTab.World);
        Assert.Equal(new Transition.OpenMenu(PauseTab.World), Assert.Single(host.Transitions));
        session.MenuClosed(PauseExit.Close);
        host.Transitions.Clear();
        var ambush = Data.Set.Get<DialogueRecord>(RecordId.Parse("aidyn:dialogue/0x0365"));
        var (_, map, dialogue) = Data.Of<DialogueObject>().First(d => d.Object.Script == ambush.Id && d.Object.Stage is not null);
        session.Cue(ambush, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);
        session.OpenMenu();
        Assert.Empty(host.Transitions);
        Assert.False(session.MenuOpen);
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
            with { Enemies = fight.Enemies, Size = (party.MinSize, party.MaxSize), BonusLoot = party.Loot, PartyGate = party.Visible }, fight);
        Assert.Equal(party.Enemies, fight.Enemies);

        // A wanderer catching the party is the same, as an ambush.
        var ambush = BattleSetup.WithParty(BattleSource.Wanderer, id, party, here, place.Chunk.Terrain, ambush: true);
        Assert.Equal(fight with { Source = BattleSource.Wanderer, Ambush = true }, ambush);

        // A camp ambush where the player stands, in G14 (terrain 0): the terrain's two kinds at the party's level's tier, its least
        // and most theirs summed, and no fleeing. Two of the Gwernia recruits make ambushes possible; each camp that isn't one heals and cues the scene.
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
        var tier = BattleSetup.AmbushTier(Data.Set, camping.Game.Party.AverageLevel);
        var expected = Data.Set.Get<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, 0)).Ambushes![tier].Enemies;
        Assert.Equal(expected, camp.Enemies);
        Assert.Equal((expected.Sum(e => e.Min), expected.Sum(e => e.Max)), camp.Size);
        Assert.Null(camp.BonusLoot);
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
        // He stands 30 away for a while, facing back the way the battle will be, the camera behind him, so a breadcrumb is
        // kept there; then he comes back, facing and seen the other way.
        var away = Body.SetDown(trek.Chunks.World, start + new Vector3(0, 0, -30), 0.5f, null).Position;
        var toward = Vector2.UnitY;
        trek.Player.Sphere.Position = away;
        trek.Player.Heading = toward;
        trek.Camera.Place(away, toward);
        for (var i = 0; i < Breadcrumbs.Ticks + 1; i++)
            Tick(session);
        trek.Player.Sphere.Position = start;
        trek.Player.Heading = -toward;
        trek.Camera.Place(start, -toward);
        var awayOrigin = trek.Chunks.Origin(trek.Chunks.ChunkAt(away)!);
        var kept = Assert.Single(trek.Breadcrumbs.All.Distinct(), c => Vector3.Distance(c.Local, away - awayOrigin) < 1);
        Assert.Equal(toward, kept.Heading);

        // Two rats in arena 1, as a monster party used.
        var rat = RecordId.Hex(RecordTypes.Entity, 0x026E);
        var (id, _, rats) = Data.Of<MonsterPartyObject>().First(p => p.Object is { Dialogue: null, MinSize: 2, MaxSize: 2 }
            && p.Object.Enemies.All(e => e.Entity == rat));
        var (place, terrain) = trek.Here();
        session.CueBattle(BattleSetup.WithParty(BattleSource.MonsterParty, id, rats, place, terrain, ambush: false) with { Arena = 1 });
        // Trek plays by the options' Each character's spell auras up to the battle, which it has no use for.
        var each = SessionSettings.Original with { SpellAuras = AuraTurns.EachCharacter };
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.BattleCued; i++)
            step = Tick(session, each);
        Assert.Equal(SessionStep.BattleCued, step);
        session.BeginBattle();
        var began = Assert.IsType<Transition.StartedBattle>(Assert.Single(host.Transitions));
        var battle = session.Battle!;
        Assert.Same(battle, began.Battle);
        // It takes the spell auras the session plays by as it begins, and the next tick's from that tick on: the session is their one owner.
        Assert.Equal(AuraTurns.EachCharacter, battle.Fx.Turns);
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

        // It fades in over a second, then the turn is Alaron's, or the rats' come first: each walks at him, and bites or rests.
        for (var i = 0; i < 60; i++)
            Assert.Equal((SessionStep.Combat, CombatState.Intro), (Tick(session), battle.State));
        Assert.Equal(AuraTurns.Battle, battle.Fx.Turns);
        for (var i = 0; i < 3000 && battle.State != CombatState.Turn; i++)
            Tick(session);
        Assert.Equal((CombatState.Turn, alaron), (battle.State, battle.Current));

        // Start: the menu, from a turn, with the Party tab and Flee, two frames on.
        host.Transitions.Clear();
        Assert.True(battle.Pause());
        for (var i = 0; i < 5; i++)
            Tick(session);
        Assert.Equal(new Transition.PauseBattle(PartyTab: true, CanFlee: true), Assert.Single(host.Transitions));
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
        // Facing as he did there, and seen from where the camera was then, behind him, not from where the battle began
        // (InitGameTrek, 0x80020F40, from the breadcrumb InitZoneEngine copied over the kept place).
        Assert.Equal(toward, trek.Player.Heading);
        Assert.Equal(awayOrigin + kept.Eye, trek.Camera.Position);
        var behind = new Vector2(trek.Camera.Position.X - away.X, trek.Camera.Position.Z - away.Z);
        Assert.True(Vector2.Dot(Vector2.Normalize(behind), toward) < -0.9f);
        Assert.Equal(away, trek.Camera.Aim);
        Assert.All(trek.Breadcrumbs.All, c => Assert.Equal(trek.Breadcrumbs.All[0], c));
        Assert.NotEmpty(trek.Wanderers.All);
    }

    /// <summary>A battle with two rats in arena 1, as a monster party used, begun and faded in to Alaron's turn.</summary>
    private (Battle Battle, RecordId Party, MonsterPartyObject Rats) RatBattle(GameSession session, Host host, RecordId? flag = null)
    {
        var rat = RecordId.Hex(RecordTypes.Entity, 0x026E);
        var (id, _, rats) = Data.Of<MonsterPartyObject>().First(p => p.Object is { Dialogue: null, MinSize: 2, MaxSize: 2 }
            && p.Object.Enemies.All(e => e.Entity == rat));
        var (place, terrain) = session.Trek.Here();
        var setup = BattleSetup.WithParty(BattleSource.MonsterParty, id, rats, place, terrain, ambush: false) with { Arena = 1 };
        if (flag is { } f)
            setup = setup with { EncounterFlag = f };
        session.CueBattle(setup);
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.BattleCued; i++)
            step = Tick(session);
        session.BeginBattle();
        var battle = session.Battle!;
        // The rats may act first: their AI walks and attacks before Alaron's turn.
        for (var i = 0; i < 3000 && battle.State != CombatState.Turn; i++)
            Tick(session);
        Assert.Equal((CombatState.Turn, GameState.Alaron), (battle.State, battle.Current!.Entity));
        host.Transitions.Clear();
        return (battle, id, rats);
    }

    private static void Kill(Battle battle, Combatant c)
    {
        c.Sheet.TakeDamage(1000, c);
        battle.Death(c);
        battle.FreeBody(c);
    }

    /// <summary>
    /// Plays the cinematic or the credits running out, skipping what can be skipped, until the session has ended it and
    /// carried out what follows; the ending's credits, which can't be, roll to their end.
    /// </summary>
    private static void PlayOut(GameSession session)
    {
        var mode = session.Mode;
        Assert.True(mode is GameMode.Cinematic or GameMode.Credits);
        for (var i = 0; i < 60000 && session.Mode == mode; i++)
        {
            session.Cinematic?.Player.Skip();
            session.Credits?.Roll.Skip();
            Tick(session);
        }
        Assert.NotEqual(mode, session.Mode);
    }

    private static SessionStep TickUntil(GameSession session, SessionStep until, int most = 2000)
    {
        var step = SessionStep.Trek;
        for (var i = 0; i < most && step != until; i++)
            step = Tick(session);
        return step;
    }

    [Fact]
    public void ABattleWonOpensThePostBattleWindowsAndTrekComesBackWhereItBegan()
    {
        var (session, host) = Start();
        var trek = session.Trek;
        var (battle, _, rats) = RatBattle(session, host);
        var start = trek.Player.Sphere.Position;
        var alaron = session.Game.Party.Slots[0]!;
        var exp = alaron.TotalExp;
        var gold = session.Game.Party.Gold;

        // The rats fall, and the turn's end wins it: the rewards at once, the victory line and the fade.
        foreach (var rat in battle.Combatants.Where(c => c.Hostile).ToList())
            Kill(battle, rat);
        Assert.True(battle.Skip());
        Assert.Equal(CombatState.Victory, battle.State);
        Assert.Equal(battle.CombatText(Battle.Victorious), battle.Lines.Shown);
        Assert.Equal(200, battle.ExperiencePool);
        Assert.Equal(exp + 300, alaron.TotalExp);

        // Faded out, the party has the gold and the encounter flag is set; the battle leaves, won.
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.Equal(BattleOutcome.Won, battle.Outcome);
        var spoils = battle.Spoils!;
        Assert.Equal(gold + (uint)spoils.Gold, session.Game.Party.Gold);
        if (rats.Used?.Flag is { } flag)
            Assert.True(session.Game.Flags.IsSet(flag));

        // Its arena goes, cutting the battle's music (Combat.Exit's Bgm.Stop), and the post-battle windows open over the held world, silent
        // but for their own sounds; trek comes back where it began once they close, asking for its music again.
        var audio = session.Arena!.Audio;
        Assert.NotNull(audio.Music);
        session.EndBattle();
        Assert.Null(audio.Music);
        Assert.Equal([typeof(Transition.EndBattle), typeof(Transition.PostBattle)], host.Transitions.Select(t => t.GetType()));
        Assert.Same(spoils, session.PostBattle);
        Assert.Equal((GameMode.Menu, false), (session.Mode, session.TrekRuns));
        Assert.Equal(SessionStep.Held, Tick(session));
        Assert.True(session.MenuOpen);
        host.Transitions.Clear();
        session.MenuClosed(PauseExit.Close);
        Assert.Equal([typeof(Transition.ResumeTrek)], host.Transitions.Select(t => t.GetType()));
        Assert.Null(session.PostBattle);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.Equal(start, trek.Player.Sphere.Position);
    }

    [Fact]
    public void ThePostBattleWindowsAreSilentButForTheLevelUpOnceTheyAreBuilt()
    {
        // In K08's forest, within one of its sounds, played and mixed as the runtime plays them: trek's chunks follow the player
        // while the session streams them (StreamsTrek), and the menu's sounds are played as they're cued.
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var k08 = set.Chunks.Single(c => c.Name == "K08");
        var sound = Data.Set.Get<MapRecord>(k08.Map).Objects.OfType<SoundObject>().First(o => (o.Flags & SoundFlags.Loop) != 0);
        var (session, host) = Start("K08", sound.Position.X, sound.Position.Z);
        var audio = session.Zone.Audio;
        var mixed = new short[2 * 735];
        var played = 0;
        void Run(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                audio.Step();
                if (session.StreamsTrek)
                    session.Trek.Chunks.Update(session.Trek.Player.Sphere.Position);
                Tick(session);
                foreach (var cue in host.Cues.Skip(played).OfType<MenuSoundCue>())
                    audio.Play(cue.Sound);
                played = host.Cues.Count;
                audio.Render(mixed);
            }
        }
        Run(60);
        Assert.True(session.Zone.Sounds.Playing > 0);

        // Alaron one short of his next level, the rats fall and the battle is won.
        var (battle, _, _) = RatBattle(session, host);
        played = host.Cues.Count;
        var alaron = session.Game.Party.Slots[0]!;
        alaron.TotalExp = alaron.NextLevelExp!.Value - 1;
        foreach (var rat in battle.Combatants.Where(c => c.Hostile).ToList())
            Kill(battle, rat);
        battle.Skip();
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        session.EndBattle();
        Assert.Equal([alaron], session.PostBattle!.Levelled);
        Assert.False(session.StreamsTrek);

        // The level-up waits for the window to be built, once the world has darkened (PartyMenu.ListsCreate), then a second more.
        var levelUp = session.Game.Sounds.Menu.LevelUp;
        Assert.DoesNotContain(levelUp, host.Sounds);
        var ticks = 0;
        while (!session.Menu.Scroll.ShowsMenu && ticks++ < 60)
            Run(1);
        // 13 frames darkening, built on the 14th.
        Assert.Equal(14, ticks);
        Assert.Contains(host.Cues, c => c is MenuSoundCue { Sound: { Delay: Core.Audio.OneShot.MenuTicks } s } && s.Sequence == levelUp);
        // Nothing of the map's plays under the windows: only the level-up, from a second on.
        for (var i = 0; i < 300; i++)
        {
            Run(1);
            Assert.Equal(0, session.Zone.Sounds.Playing);
        }
        Assert.Equal(1, audio.SoundsPlaying);

        // The windows close, trek is built again, and its sounds come back.
        session.MenuClosed(PauseExit.Close);
        Assert.True(session.StreamsTrek);
        Run(60);
        Assert.True(session.Zone.Sounds.Playing > 0);
    }

    [Fact]
    public void ABattleLostHealsAlaronAndPlaysTheGameOverWhoseEndIsTheGames()
    {
        var (session, host) = Start();
        var (battle, _, _) = RatBattle(session, host);
        var alaron = battle.Current!;

        // Alaron's death loses it at the turn's change, and the screen fades out.
        Kill(battle, alaron);
        battle.EndTurn(alaron);
        Assert.Equal((CombatState.Defeat, true), (battle.State, battle.Fade.Fading));
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.Equal((BattleOutcome.Lost, CombatState.LeaveToScript), (battle.Outcome, battle.State));
        var leader = session.Game.Party.Slots[0]!;
        Assert.Equal(leader.MaxHp, leader.Hp);

        // The game over's death scene plays from its dialogue object in the Dreamscape, and its end is the game's.
        session.EndBattle();
        Assert.Contains(host.Transitions, t => t is Transition.StartCutScene);
        Assert.Equal(RecordId.Hex(RecordTypes.Dialogue, 0x0512), session.Scene!.Runner.Script.Id);
        Assert.Contains(session.Game.Party.Members, m => m.Entity == GameState.Alaron);
        Assert.Equal(SessionStep.CutScene, Tick(session));
        host.Transitions.Clear();
        session.EndCutScene();
        Assert.Equal([typeof(Transition.EndCutScene), typeof(Transition.GameOver)], host.Transitions.Select(t => t.GetType()));
    }

    [Fact]
    public void LosingTheGoblinAmbushWakesThePartyInOrianasHutTheNextMorning()
    {
        var (session, host) = Start();
        var ambush = RecordId.Decimal(RecordTypes.Flag, 978);
        var (battle, id, _) = RatBattle(session, host, ambush);
        Assert.True(battle.NonLethal);
        var game = session.Game;
        var day = game.Clock.Time / Core.Calendar.GameClock.Day;

        // Alaron's fall holds 120 frames before the fade.
        Kill(battle, battle.Current!);
        battle.EndTurn(battle.Current!);
        Assert.Equal(CombatState.DefeatWait, battle.State);
        for (var i = 0; i < Battle.SparedFrames * Cadence.FrameTicks - 1; i++)
            Tick(session);
        Assert.Equal(CombatState.DefeatWait, battle.State);
        Tick(session);
        Assert.Equal((CombatState.Defeat, true), (battle.State, battle.Fade.Fading));
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));

        // Spared: the flag, the party fought remembered, and 6:00 the next day.
        Assert.Equal(BattleOutcome.Spared, battle.Outcome);
        Assert.True(game.Flags.IsSet(ambush));
        Assert.NotNull(game.Memory.Find(new Memory(id, MemoryKind.Beaten)));
        Assert.Equal((day + 1) * Core.Calendar.GameClock.Day + 6 * Core.Calendar.GameClock.Hour, game.Clock.Time);

        // Its end: flag 47, Alaron healed, and trek in Oriana's hut, MiscInteriors E03, facing her, −x: Combat.Exit's (1, 0) is the
        // original's reversed facing (0x80023060, copied into the body's +0x20 by InitGameTrek).
        session.EndBattle();
        Assert.True(game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 47)));
        var alaron = game.Party.Slots[0]!;
        Assert.Equal(alaron.MaxHp, alaron.Hp);
        var trek = session.Trek;
        Assert.Equal(RecordId.Decimal(RecordTypes.MapSet, 6), trek.Chunks.MapSet.Id);
        var hut = trek.Chunks.MapSet.Chunks.Single(c => c.Row == 5 && c.Column == 3);
        Assert.Equal(trek.Chunks.Origin(hut) + new Vector3(5.9f, 0.5f, 3f), trek.Player.Sphere.Position);
        Assert.Equal(new Vector2(-1, 0), trek.Player.Heading);
        Assert.Equal(GameMode.Trek, session.Mode);
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
        session.OpenMenu();
        Assert.Equal((GameMode.Menu, true), (session.Mode, session.MenuOpen));
        host.Transitions.Clear();
        session.MenuClosed(PauseExit.Credits);
        Assert.IsType<Transition.PlayCredits>(Assert.Single(host.Transitions));
        Assert.Equal((GameMode.Credits, SessionStep.Credits), (session.Mode, Tick(session)));
        PlayOut(session);
        Assert.Equal(new Transition.ReopenMenu(Theatre: false), TickUntilReopened(session, host));

        // The Theatre's cinematic: its done flag is set, and the menu reopens on the Theatre's page.
        var cinematic = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 2));
        session.MenuClosed(PauseExit.Theatre(cinematic));
        Assert.Equal(new Transition.PlayCinematic(cinematic), host.Transitions[^1]);
        PlayOut(session);
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
    public void TheSessionStepsACinematicAndTheCreditsToTheirEnds()
    {
        var (session, host) = Start("G14", 40, 40);
        var opening = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 0));

        // The Theatre's opening, played out tick by tick with no skip: each tick is the cinematic's, its player moving through
        // its shots; its end goes to the host, trek comes back, not built again, and the menu reopens on the Theatre's page once
        // trek has faded in.
        session.OpenMenu();
        session.MenuClosed(PauseExit.Theatre(opening));
        var showing = session.Cinematic!;
        Assert.Equal((opening, true), (showing.Record, showing.FromTheatre));
        host.Transitions.Clear();
        var ticks = 0;
        var shots = new HashSet<int>();
        for (; ticks < 100000 && session.Mode == GameMode.Cinematic; ticks++)
        {
            Assert.Equal(SessionStep.Cinematic, Tick(session));
            if (showing.Player.Shot is not null)
                shots.Add(showing.Player.ShotIndex);
        }
        Assert.True(showing.Player.Ended && !showing.Player.Skipped);
        Assert.Equal(opening.Shots.Count, shots.Count);
        var length = opening.Shots.Sum(s => s.Length);
        Assert.InRange(ticks, length - 2 * opening.Shots.Count, length + 2 * opening.Shots.Count + 4);
        Assert.Equal([typeof(Transition.EndCinematic)], host.Transitions.Select(t => t.GetType()));
        Assert.True(session.Game.Flags.IsSet(opening.DoneFlag));
        Assert.Equal((GameMode.Trek, null), (session.Mode, session.Cinematic));
        Assert.Equal(new Transition.ReopenMenu(Theatre: true), TickUntilReopened(session, host));

        // Held for a screenshot, it stands still; let go, it goes on, and skipped, it ends in a few ticks.
        session.MenuClosed(PauseExit.Theatre(opening));
        var player = session.Cinematic!.Player;
        for (var i = 0; i < 10; i++)
            Tick(session);
        var (shot, time) = (player.ShotIndex, player.Time);
        session.CinematicHeld = true;
        for (var i = 0; i < 10; i++)
            Assert.Equal(SessionStep.Cinematic, Tick(session));
        Assert.Equal((shot, time), (player.ShotIndex, player.Time));
        session.CinematicHeld = false;
        Tick(session);
        Assert.NotEqual((shot, time), (player.ShotIndex, player.Time));
        player.Skip();
        for (ticks = 0; ticks < 10 && session.Mode == GameMode.Cinematic; ticks++)
            Tick(session);
        Assert.Equal((GameMode.Trek, true), (session.Mode, player.Skipped));
        Assert.Equal(new Transition.ReopenMenu(Theatre: true), TickUntilReopened(session, host));

        // The menu's credits roll on a screen as tall as the session is told, and A ends them at once: their end goes to the
        // host as trek fades in from black, and the menu reopens once it has.
        Tick(session, SessionSettings.Original with { UiLines = 480 });
        host.Transitions.Clear();
        session.MenuClosed(PauseExit.Credits);
        var credits = session.Credits!;
        Assert.Equal((false, true), (credits.Ending, credits.FromMenu));
        Assert.Equal(new Transition.PlayCredits(), Assert.Single(host.Transitions));
        for (var i = 0; i < 100; i++)
            Assert.Equal(SessionStep.Credits, Tick(session));
        Assert.NotEmpty(credits.Roll.Lines);
        credits.Roll.Skip();
        Assert.Equal(SessionStep.Credits, Tick(session));
        Assert.Equal((GameMode.Trek, null), (session.Mode, session.Credits));
        Assert.Equal(new Transition.EndCredits(Ending: false), host.Transitions[^1]);
        Assert.True(session.Trek.CutSceneFade.Fading);
        Assert.Equal(new Transition.ReopenMenu(Theatre: false), TickUntilReopened(session, host));
    }

    [Fact]
    public void TheMenusExitsCampOrStartTheHelpMenu()
    {
        var (session, host) = Start("G14", 40, 40);
        session.MenuClosed(PauseExit.Camp);
        Assert.IsType<Transition.Camp>(host.Transitions[^1]);

        session.MenuClosed(PauseExit.Help);
        Assert.Equal((GameMode.Backdrop, true), (session.Mode, session.Trek.Conversation!.ShowsBackdrop));
        Assert.Equal(SystemMenu.HelpScript, session.Trek.Conversation.Runner.Script.Id);
        Assert.NotNull(session.Backdrop);
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
